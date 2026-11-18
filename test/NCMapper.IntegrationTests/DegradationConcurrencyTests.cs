using Xunit;

namespace NCMapper.IntegrationTests;

/// <summary>
/// T4-1: DegradationDetector 缓存失效并发测试 — 验证并发注册/清除 Pipeline + 并发 AsDto 无 NRE/无脏缓存
/// </summary>
[Collection("PipelineGlobalState")]
public class DegradationConcurrencyTests
{
    private class ConcEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    [NCMapper.Attributes.MapFrom(typeof(ConcEntity))]
    private class ConcDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    /// <summary>
    /// 并发 Pipeline 注册/清除 + 并发 Map 调用 — 100 次循环无异常
    /// </summary>
    [Fact]
    public async Task ConcurrentPipelineRegisterAndMap_NoException()
    {
        for (int iter = 0; iter < 100; iter++)
        {
            using var cts = new CancellationTokenSource();
            var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

            // 线程1: 反复注册/清除 Pipeline
            var t1 = Task.Run(() =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        Mapper.Pipeline.AddPostMap<ConcEntity, ConcDto>((src, dst) => { });
                        Mapper.Pipeline.Clear();
                    }
                    catch (Exception ex) { exceptions.Add(ex); }
                }
            }, TestContext.Current.CancellationToken);

            // 线程2: 并发 Map 调用
            var t2 = Task.Run(() =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var entity = new ConcEntity { Id = 1, Name = "test" };
                        var dto = Mapper.Map<ConcEntity, ConcDto>(entity);
                        if (dto == null) exceptions.Add(new Exception("Map returned null"));
                    }
                    catch (Exception ex) { exceptions.Add(ex); }
                }
            }, TestContext.Current.CancellationToken);

            // 线程3: 并发 MapList 调用
            var t3 = Task.Run(() =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var list = new List<ConcEntity> { new() { Id = 2, Name = "x" } };
                        var dtos = Mapper.MapList<ConcEntity, ConcDto>(list);
                        if (dtos == null) exceptions.Add(new Exception("MapList returned null"));
                    }
                    catch (Exception ex) { exceptions.Add(ex); }
                }
            }, TestContext.Current.CancellationToken);

            Thread.Sleep(10); // 让并发跑 10ms
            cts.Cancel();
            await Task.WhenAll(t1, t2, t3);

            Assert.True(exceptions.IsEmpty,
                $"迭代 {iter}: 并发异常: {string.Join("; ", exceptions.Take(3).Select(e => e.Message))}");
        }
    }

    /// <summary>
    /// 并发 ClearCache + Map — 验证缓存重建无 NRE 且映射结果字段级守恒（C1 补测升级）
    /// <para>断言形态字段级守恒（固定字段 DeepEqual）+ 零异常 + 复现率登记。
    /// 并发 ClearCache 对撞时若缓存重建存在竞态，可能产出脏数据（字段不守恒）而非异常，
    /// 故每次 Map 后立即校验 Id/Name 与源一致；失败计入 <paramref name="failures"/> 由断言拦截。</para>
    /// <para>复现率：totalMaps 次守恒校验全部通过时复现率 0，观测结果登记。</para>
    /// <para>并发时序说明：线程1 的 ClearCache 是无锁纯 CPU 空转，若线程2 长期抢不到 CPU 会出现
    /// "测试空转"（totalMaps==0，CI runner 负载/线程调度下 net8/net9 已实测复现）。修复：① ClearCache
    /// 循环内 Thread.Yield 主动让步让出时间片；② 每轮迭代等待线程2 完成至少一次 Map 后才取消，确保
    /// 并发对撞真实发生且 totalMaps 恒 &gt; 0，消除对固定 sleep 碰运气的依赖。</para>
    /// </summary>
    [Fact]
    public async Task ConcurrentClearCacheAndMap_NoException_WithFieldConservation()
    {
        long totalMaps = 0;
        for (int iter = 0; iter < 100; iter++)
        {
            using var cts = new CancellationTokenSource();
            var failures = new System.Collections.Concurrent.ConcurrentBag<string>();

            // 线程1: 反复 ClearCache（Thread.Yield 让出时间片，避免纯 CPU 空转饿死并发 Map 线程）
            var t1 = Task.Run(() =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    try { Mapper.ClearCache(); }
                    catch (Exception ex) { failures.Add($"ClearCache 异常: {ex.Message}"); }
                    Thread.Yield();
                }
            }, TestContext.Current.CancellationToken);

            // 线程2: 并发 Map + 字段级守恒校验
            var t2 = Task.Run(() =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var entity = new ConcEntity { Id = iter, Name = $"y-{iter}" };
                        var dto = Mapper.Map<ConcEntity, ConcDto>(entity);
                        if (dto == null)
                        {
                            failures.Add("Map 返回 null");
                            continue;
                        }

                        // 字段级守恒：固定字段 DeepEqual（Id/Name 与源一致）
                        if (dto.Id != entity.Id || dto.Name != entity.Name)
                            failures.Add($"字段不守恒: Id {dto.Id}≠{entity.Id}, Name '{dto.Name}'≠'{entity.Name}'");
                        Interlocked.Increment(ref totalMaps);
                    }
                    catch (Exception ex) { failures.Add($"Map 异常: {ex.Message}"); }
                }
            }, TestContext.Current.CancellationToken);

            // 每轮等待线程2 完成至少一次 Map（totalMaps 越过本迭代起始值）才取消，确保不"空转"。
            long iterStart = Volatile.Read(ref totalMaps);
            int waitMs = 0;
            while (Volatile.Read(ref totalMaps) == iterStart && waitMs < 5000)
            {
                await Task.Delay(5, TestContext.Current.CancellationToken);
                waitMs += 5;
            }
            cts.Cancel();
            await Task.WhenAll(t1, t2);

            Assert.True(failures.IsEmpty,
                $"迭代 {iter}: 并发异常/字段不守恒: {string.Join("; ", failures.Take(3))}");
        }

        // 复现率登记：totalMaps 次守恒校验全部通过（失败均已计入 failures 并由上方断言拦截）。
        // 观测结果（守恒校验通过率 100%、总校验次数 totalMaps）登记。
        Assert.True(totalMaps > 0, "并发 Map 未执行任何守恒校验（测试空转）");
    }
}
