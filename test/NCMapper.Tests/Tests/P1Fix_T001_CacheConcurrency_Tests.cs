// ============================================================================
// P1Fix_T001 — 批次 3，委托缓存并发统一
// 验收用例（A1/A2：并发首次 miss 编译次数 == 1 / == 类型数）。
//
// 缺陷（006 审计 20260827-005）：Compose/DeepCopy 委托缓存 GetOrAdd 无 Lazy
// 防护——并发首次 miss 时 valueFactory 重复执行（编译开销翻倍、对象膨胀）。
// 修复：Lazy(ExecutionAndPublication) 包装（对照 MapperBase s_mapActionLazy /
// DeepCopyCompiler s_createCache 先例），并发口径统一。
//
// 计数观测：Mapper.ComposeCompileCount / DeepCopyProviderBase.DelegateCompileCount
//（internal 编译计数器，Interlocked 递增）。
// ============================================================================

using NCMapper.Attributes;
using Xunit;

namespace NCMapper.Tests.Tests;

public class P1Fix_T001_CacheConcurrency
{
    /// <summary>
    /// A1：Compose 并发首次 miss——同类型对 8 线程并发编译次数 == 1。
    /// </summary>
    [Fact]
    public async Task S01_Compose并发首次Miss_编译次数为1()
    {
        Mapper.ClearCache();
        var before = Mapper.ComposeCompileCount;
        var src = new B3PrimarySource { Name = "n", Age = 1 };

        var barrier = new Barrier(8);
        var tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            barrier.SignalAndWait(); // 屏障同步最大化并发 miss 重叠窗口
            return Mapper.Compose<B3ComposeDto>(src);
        })).ToArray();
        var results = await Task.WhenAll(tasks);

        var delta = Mapper.ComposeCompileCount - before;
        Assert.Equal(1, delta); // Lazy 后并发首次 miss 仅编译 1 次
        Assert.All(results, r => Assert.NotNull(r));
    }

    /// <summary>
    /// A2：DeepCopy 并发冷启动同类型——8 线程并发委托编译次数 == 1。
    /// 经 ExpressionTreeDeepCopyProvider 直调（s_delegateCache 消费点；Mapper.DeepCopy
    /// 默认 Fast 分发不触达该缓存——分发层 D1 接口转 base 已收口，本用例锁定 ExpressionTree 路径）。
    /// </summary>
    [Fact]
    public async Task S05_DeepCopy并发冷启动_同类型编译次数为1()
    {
        Mapper.ClearCache();
        var provider = new ExpressionTreeDeepCopyProvider();
        var before = DeepCopyProviderBase.DelegateCompileCount;
        var src = new B3DeepItem { Name = "x" };

        var barrier = new Barrier(8);
        var tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            barrier.SignalAndWait();
            return (B3DeepItem)provider.DeepCopyObject(src)!;
        })).ToArray();
        var results = await Task.WhenAll(tasks);

        var delta = DeepCopyProviderBase.DelegateCompileCount - before;
        Assert.Equal(1, delta);
        Assert.All(results, r => Assert.Equal("x", r.Name));
        Assert.All(results, r => Assert.NotSame(src, r));
    }

    /// <summary>
    /// A2 补充：并发多类型——编译次数 == 类型数（每类型恰 1 次）。
    /// </summary>
    [Fact]
    public async Task S05_DeepCopy并发多类型_编译次数等于类型数()
    {
        Mapper.ClearCache();
        var provider = new ExpressionTreeDeepCopyProvider();
        var before = DeepCopyProviderBase.DelegateCompileCount;
        var sources = new object[]
        {
            new B3DeepItemA { V = 1 }, new B3DeepItemB { V = 2 }, new B3DeepItemC { V = 3 },
        };

        var tasks = sources.SelectMany(s => Enumerable.Range(0, 4).Select(_ => Task.Run(() => provider.DeepCopyObject(s)))).ToArray();
        await Task.WhenAll(tasks);

        var delta = DeepCopyProviderBase.DelegateCompileCount - before;
        Assert.Equal(sources.Length, delta); // 3 类型 × Lazy = 恰 3 次编译
    }

    // ============================== 模型 ==============================

    public sealed class B3PrimarySource
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
    }

    public sealed class B3SecondarySource
    {
        public string City { get; set; } = "";
    }

    [MapFrom(typeof(B3PrimarySource))]
    public sealed class B3ComposeDto
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
    }

    public sealed class B3DeepItem
    {
        public string Name { get; set; } = "";
    }

    public sealed class B3DeepItemA { public int V { get; set; } }
    public sealed class B3DeepItemB { public int V { get; set; } }
    public sealed class B3DeepItemC { public int V { get; set; } }
}