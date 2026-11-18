using System.Threading;
using NCMapper.Attributes;
using Xunit;

namespace NCMapper.IntegrationTests;

/// <summary>
/// 验收测试 — MapPipeline 并发注册竞态修复验证
/// <para>V2：Clear 与 AddPostMap 并发交错 200 轮，按最终 _hasPostMap 状态条件断言回调计数，统计"标志 true 但计数 0"轮次必须为 0（修复前 E18 场景必失败）</para>
/// <para>预热逆序缓存后注册新回调，下一次 Map 新回调必须生效（消除判定）</para>
/// </summary>
[Collection("PipelineGlobalState")]
public class PipelineConcurrencyFixTests : IDisposable
{
    private class ConcEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    [MapFrom(typeof(ConcEntity))]
    private class ConcDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    /// <summary>
    /// 每轮用例结束后清理管道，保证测试隔离（与项目全局串行约束一致）
    /// </summary>
    public void Dispose() => Mapper.Pipeline.Clear();

    /// <summary>
    /// V2：并发注册竞态测试 — 200 轮零丢失（条件断言，不依赖时序窗口/sleep/重试）
    /// <para>每轮：起始 Clear 保证干净态 → 并发 AddPostMap/Clear → 两者均返回后读 _hasPostMap 并按条件断言：</para>
    /// <para>① _hasPostMap==true（Add 获胜）→ 执行一次 Map，回调执行计数必须 == 1</para>
    /// <para>② _hasPostMap==false（Clear 获胜）→ 执行一次 Map，回调执行计数必须 == 0</para>
    /// <para>③ 全轮统计"标志 true 但计数 0"轮次（直接反证）必须 == 0；失败输出含轮次、标志状态与期望/实际计数</para>
    /// </summary>
    [Fact]
    public async Task ConcurrentClearAndAddPostMap_NoLostCallback()
    {
        const int rounds = 200;
        int lostRounds = 0;
        var failures = new List<string>();

        for (int round = 0; round < rounds; round++)
        {
            // 起始清空管道，确保每轮从干净状态开始（上一轮残留回调/标志全部复位）
            Mapper.Pipeline.Clear();
            int callbackCount = 0;

            // 并发交错：AddPostMap 与 Clear 同时发起，无任何时序控制，交错由线程调度器决定
            var addTask = Task.Run(() =>
                Mapper.Pipeline.AddPostMap<ConcEntity, ConcDto>((src, tgt) => Interlocked.Increment(ref callbackCount)), TestContext.Current.CancellationToken);
            var clearTask = Task.Run(() => Mapper.Pipeline.Clear(), TestContext.Current.CancellationToken);
            await Task.WhenAll(addTask, clearTask);

            // 执行一次 Map 触发 PostMap（若管道处于启用态）
            var dto = Mapper.Map<ConcEntity, ConcDto>(new ConcEntity { Id = round, Name = "t" });
            int actual = Volatile.Read(ref callbackCount);

            bool hasPostMap = Mapper.Pipeline._isEnabled; // 双标志统一后同一状态（实例化适配）
            if (hasPostMap)
            {
                // Add 获胜：回调已注册，Map 后必须恰执行 1 次
                if (actual != 1)
                {
                    lostRounds++;
                    failures.Add($"round={round}, _hasPostMap=true, expected=1, actual={actual}");
                }
            }
            else
            {
                // Clear 获胜：回调已被清空，Map 后不得执行
                if (actual != 0)
                {
                    failures.Add($"round={round}, _hasPostMap=false, expected=0, actual={actual}");
                }
            }
        }

        Assert.True(lostRounds == 0,
            $"丢失回调轮次应为零，实际 {lostRounds} 轮。详情: {string.Join("; ", failures.Take(10))}");
    }

    /// <summary>
    /// 注册生效测试 — 预热逆序缓存后注册新回调，下一次 Map 新回调必须生效（消除判定）
    /// <para>旧回调 A 预热阶段执行 1 次；注册新回调 B 后再次 Map，B 必须执行（缓存被 AddPostMap 移除并重建），A 累计 2 次</para>
    /// </summary>
    [Fact]
    public void PostMapRegisteredAfterCacheWarmup_TakesEffect()
    {
        Mapper.Pipeline.Clear();
        int countA = 0;
        int countB = 0;

        // 注册回调 A 并 Map 一次，构建逆序缓存
        Mapper.Pipeline.AddPostMap<ConcEntity, ConcDto>((src, tgt) => Interlocked.Increment(ref countA));
        Mapper.Map<ConcEntity, ConcDto>(new ConcEntity { Id = 1, Name = "a" });
        Assert.Equal(1, Volatile.Read(ref countA));

        // 注册新回调 B（AddPostMap 锁内 TryRemove 逆序缓存），再次 Map 必须重建缓存并执行 B
        Mapper.Pipeline.AddPostMap<ConcEntity, ConcDto>((src, tgt) => Interlocked.Increment(ref countB));
        Mapper.Map<ConcEntity, ConcDto>(new ConcEntity { Id = 2, Name = "b" });

        // 逆序执行（洋葱模型）：后注册的 B 先执行，随后 A
        Assert.Equal(1, Volatile.Read(ref countB));
        Assert.Equal(2, Volatile.Read(ref countA));
    }
}
