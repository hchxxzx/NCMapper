using System.Collections.Concurrent;
using NCMapper;
using Xunit;

namespace NCMapper.Tests.Tests;

/// <summary>
/// 并发 DeepCopy 测试（C4 补测）— Fast/生成双路径结果等价 + 无槽位串扰
/// <para>报告 §五 C4 断言形态：DeepCopy 结果等价 + 无槽位串扰。
/// 层 1 生成路径走 <see cref="Mapper.RegisterGeneratedDeepCopy{T}(Func{T, DeepCopyContext, T}, bool)"/>
/// 注册的委托槽位（volatile），层 2/3 运行时路径走
/// <see cref="DeepCopyProviderBase.Current"/>（默认 Fast）。</para>
/// </summary>
[Collection("DeepCopyGlobalState")]
public class DeepCopyConcurrencyTests
{
    /// <summary>
    /// 被测类型 A — 用于 DeepCopy 双路径并发验证
    /// </summary>
    private class DcEntity
    {
        /// <summary>
        /// 标识字段
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 名称字段
        /// </summary>
        public string Name { get; set; } = "";
    }

    /// <summary>
    /// 被测类型 B — 用于槽位串扰验证（与 DcEntity 独立缓存槽）
    /// </summary>
    private class DcOther
    {
        /// <summary>
        /// 标识字段
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 标签字段
        /// </summary>
        public string Label { get; set; } = "";
    }

    /// <summary>
    /// Fast 路径并发 DeepCopy — 结果等价（字段守恒）+ 独立实例（深拷贝语义）
    /// <para>显式锁定 <see cref="DeepCopyProviderBase.Current"/> 为 FastDeepCopyProvider，
    /// 避免并行测试串改默认 provider 导致路径漂移。</para>
    /// </summary>
    [Fact]
    public void ConcurrentDeepCopy_FastPath_ResultsEquivalent()
    {
        var original = DeepCopyProviderBase.Current;
        DeepCopyProviderBase.Current = new FastDeepCopyProvider();
        try
        {
            var source = new DcEntity { Id = 42, Name = "ncmapper" };
            var results = new ConcurrentBag<DcEntity?>();

            Parallel.For(0, 200, _ => results.Add(Mapper.DeepCopy(source)));

            // 结果等价：固定字段与源一致
            Assert.All(results, r =>
            {
                Assert.NotNull(r);
                Assert.Equal(42, r!.Id);
                Assert.Equal("ncmapper", r.Name);
            });

            // 深拷贝语义：每个结果均为独立实例，非源引用
            Assert.All(results, r => Assert.NotSame(source, r));
        }
        finally
        {
            DeepCopyProviderBase.Current = original;
        }
    }

    /// <summary>
    /// 生成路径并发 DeepCopy — 结果等价
    /// <para>通过 RegisterGeneratedDeepCopy 注册层 1 委托（volatile 槽位），
    /// 验证并发调用下结果守恒；测试结束注销（null）避免泛型静态槽位污染其他测试。</para>
    /// </summary>
    [Fact]
    public void ConcurrentDeepCopy_GeneratedPath_ResultsEquivalent()
    {
        Mapper.RegisterGeneratedDeepCopy<DcEntity>(
            (src, ctx) => new DcEntity { Id = src.Id, Name = src.Name }, acyclic: true);
        try
        {
            var source = new DcEntity { Id = 7, Name = "generated" };
            var results = new ConcurrentBag<DcEntity?>();

            Parallel.For(0, 200, _ => results.Add(Mapper.DeepCopy(source)));

            // 结果等价：固定字段与源一致
            Assert.All(results, r =>
            {
                Assert.NotNull(r);
                Assert.Equal(7, r!.Id);
                Assert.Equal("generated", r.Name);
            });
            Assert.All(results, r => Assert.NotSame(source, r));
        }
        finally
        {
            Mapper.RegisterGeneratedDeepCopy<DcEntity>((Func<DcEntity, DeepCopyContext, DcEntity>?)null);
        }
    }

    /// <summary>
    /// 双类型并发 DeepCopy — 无槽位串扰
    /// <para>两个独立泛型静态缓存槽（DcEntity/DcOther）并发写入读取，
    /// 断言各槽位结果不受另一类型并发影响（槽位串扰 = 缓存键错乱产出的交叉数据）。</para>
    /// </summary>
    [Fact]
    public void ConcurrentDeepCopy_TwoTypes_NoSlotCrossTalk()
    {
        Mapper.RegisterGeneratedDeepCopy<DcEntity>(
            (src, ctx) => new DcEntity { Id = src.Id, Name = src.Name }, acyclic: true);
        Mapper.RegisterGeneratedDeepCopy<DcOther>(
            (src, ctx) => new DcOther { Id = src.Id, Label = src.Label }, acyclic: true);
        try
        {
            var srcA = new DcEntity { Id = 1, Name = "A" };
            var srcB = new DcOther { Id = 2, Label = "B" };

            Parallel.For(0, 100, _ =>
            {
                var a = Mapper.DeepCopy(srcA);
                var b = Mapper.DeepCopy(srcB);

                Assert.NotNull(a);
                Assert.NotNull(b);

                // A 槽位结果不受 B 并发影响
                Assert.Equal(1, a!.Id);
                Assert.Equal("A", a.Name);
                Assert.IsType<DcEntity>(a);

                // B 槽位结果不受 A 并发影响
                Assert.Equal(2, b!.Id);
                Assert.Equal("B", b.Label);
                Assert.IsType<DcOther>(b);
            });
        }
        finally
        {
            Mapper.RegisterGeneratedDeepCopy<DcEntity>((Func<DcEntity, DeepCopyContext, DcEntity>?)null);
            Mapper.RegisterGeneratedDeepCopy<DcOther>((Func<DcOther, DeepCopyContext, DcOther>?)null);
        }
    }
}
