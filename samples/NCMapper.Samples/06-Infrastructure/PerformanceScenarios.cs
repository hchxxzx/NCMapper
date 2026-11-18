using System.Diagnostics;
using NCMapper.Generated;
using NCMapper.Samples.Declarative;
using NCMapper.Samples.Orm;
using NCMapper.Samples.DeepCopy;
using NCMapper.Samples.Models.Dtos;
using NCMapper.Samples.Models.Entities;
using NCMapper.Samples.Helpers;
using NCMapper.Samples.Models.Enums;

namespace NCMapper.Samples.Infrastructure;

/// <summary>
/// 性能场景集 — Map vs SourceGen、冷启动、Compose、DeepCopy 策略、Pipeline 开销、指标统计。
/// </summary>
public static class PerformanceScenarios
{
    /// <summary>
    /// 测量 Mapper.Map 性能（Stopwatch）。
    /// </summary>
    /// <param name="src">源实体</param>
    /// <param name="iterations">迭代次数</param>
    /// <returns>每次操作平均耗时（微秒）</returns>
    #region Performance_MeasureMapPerformance
    public static double MeasureMapPerformance(UserEntity src, int iterations = 1000)
    {
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
            _ = Mapper.Map<UserEntity, UserDto>(src);
        sw.Stop();
        return sw.Elapsed.TotalMicroseconds / iterations;
    }
    #endregion

    /// <summary>
    /// 测量源生成器扩展方法性能（零委托直调）。
    /// </summary>
    /// <param name="src">源实体</param>
    /// <param name="iterations">迭代次数</param>
    /// <returns>每次操作平均耗时（微秒）</returns>
    public static double MeasureSourceGenPerformance(UserEntity src, int iterations = 1000)
    {
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
            _ = src.MapToUserDto();
        sw.Stop();
        return sw.Elapsed.TotalMicroseconds / iterations;
    }

    /// <summary>
    /// 冷启动性能：ClearCache 后首次调用的耗时（含表达式树编译）。
    /// </summary>
    /// <param name="src">源实体</param>
    /// <returns>首次调用耗时（微秒）</returns>
    #region Core_Perf_ColdStartAfterClear
    public static double MeasureColdStart(UserEntity src)
    {
        Mapper.ClearCache();
        var sw = Stopwatch.StartNew();
        _ = Mapper.Map<UserEntity, UserDto>(src);
        sw.Stop();
        return sw.Elapsed.TotalMicroseconds;
    }
    #endregion

    /// <summary>
    /// Compose 性能：双源合成 OrderDetailDto。
    /// </summary>
    /// <param name="order">订单</param>
    /// <param name="customer">客户</param>
    /// <param name="iterations">迭代次数</param>
    /// <returns>每次操作平均耗时（微秒）</returns>
    public static double MeasureComposePerformance(OrderEntity order, CustomerEntity customer, int iterations = 1000)
    {
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
            _ = Mapper.Compose<OrderDetailDto>(order, customer);
        sw.Stop();
        return sw.Elapsed.TotalMicroseconds / iterations;
    }

    /// <summary>
    /// DeepCopy 策略对比：Fast vs ExpressionTree vs JSON。
    /// </summary>
    /// <param name="src">源对象</param>
    /// <param name="iterations">迭代次数</param>
    /// <returns>三个策略的耗时（微秒/op）</returns>
    public static (double Fast, double ExprTree, double Json) MeasureDeepCopyStrategies(UserEntity src, int iterations = 100)
    {
        // Fast
        var saved = DeepCopyProviderBase.Current;
        double fast, exprTree, json;

        DeepCopyProviderBase.Current = new FastDeepCopyProvider();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++) _ = Mapper.DeepCopy(src);
        sw.Stop();
        fast = sw.Elapsed.TotalMicroseconds / iterations;

        // ExpressionTree
        DeepCopyProviderBase.Current = new ExpressionTreeDeepCopyProvider();
        sw.Restart();
        for (int i = 0; i < iterations; i++) _ = Mapper.DeepCopy(src);
        sw.Stop();
        exprTree = sw.Elapsed.TotalMicroseconds / iterations;

        // JSON
        DeepCopyProviderBase.Current = DeepCopyProviderBase.JsonProvider;
        sw.Restart();
        for (int i = 0; i < iterations; i++) _ = Mapper.DeepCopy(src);
        sw.Stop();
        json = sw.Elapsed.TotalMicroseconds / iterations;

        DeepCopyProviderBase.Current = saved;
        return (fast, exprTree, json);
    }

    /// <summary>
    /// Pipeline 开销对比：有 PostMap vs 无 PostMap。
    /// </summary>
    /// <param name="src">源实体</param>
    /// <param name="iterations">迭代次数</param>
    /// <returns>(无管线耗时, 有管线耗时) 微秒/op</returns>
    public static (double NoPipeline, double WithPipeline) MeasurePipelineOverhead(UserEntity src, int iterations = 1000)
    {
        // 无管线
        Mapper.Pipeline.Clear();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++) _ = Mapper.Map<UserEntity, UserDto>(src);
        sw.Stop();
        var noPipeline = sw.Elapsed.TotalMicroseconds / iterations;

        // 有管线
        Mapper.Pipeline.AddPostMap<UserEntity, UserDto>((s, t) => { t.Name = $"[P] {t.Name}"; });
        sw.Restart();
        for (int i = 0; i < iterations; i++) _ = Mapper.Map<UserEntity, UserDto>(src);
        sw.Stop();
        var withPipeline = sw.Elapsed.TotalMicroseconds / iterations;

        Mapper.Pipeline.Clear();
        return (noPipeline, withPipeline);
    }

    /// <summary>
    /// 更新已有对象 vs 新建：Map(src, target) vs Map&lt;T,S&gt;(src)。
    /// </summary>
    /// <param name="src">源实体</param>
    /// <param name="target">目标对象</param>
    /// <param name="iterations">迭代次数</param>
    /// <returns>(新建耗时, 更新耗时) 微秒/op</returns>
    public static (double CreateNew, double UpdateExisting) MeasureUpdateVsCreate(UserEntity src, UserDto target, int iterations = 1000)
    {
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++) _ = Mapper.Map<UserEntity, UserDto>(src);
        sw.Stop();
        var createNew = sw.Elapsed.TotalMicroseconds / iterations;

        sw.Restart();
        for (int i = 0; i < iterations; i++) Mapper.Map(src, target);
        sw.Stop();
        var updateExisting = sw.Elapsed.TotalMicroseconds / iterations;

        return (createNew, updateExisting);
    }

    /// <summary>
    /// 映射指标统计：TotalMapCalls / CacheHits / CompilationCount。
    /// </summary>
    /// <param name="src">源实体</param>
    /// <returns>指标统计结果</returns>
    #region Infra_Perf_MetricsObserve
    public static (long TotalCalls, long CacheHits, long Compilations) MeasureMetrics(UserEntity src)
    {
        Mapper.ResetMetrics();
        Mapper.MetricsEnabled = true;

        for (int i = 0; i < 100; i++)
            _ = Mapper.Map<UserEntity, UserDto>(src);

        return (Mapper.TotalMapCalls, Mapper.CacheHits, Mapper.CompilationCount);
    }
    #endregion

    /// <summary>
    /// MapList 列表映射吞吐量基准。
    /// </summary>
    /// <param name="srcs">待映射的源实体列表</param>
    /// <param name="iterations">基准迭代次数（默认 100）</param>
    /// <returns>单次 MapList 平均耗时（微秒）</returns>
    public static double MeasureMapListPerformance(List<UserEntity> srcs, int iterations = 100)
    {
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
            _ = Mapper.MapList<UserEntity, UserDto>(srcs);
        sw.Stop();
        return sw.Elapsed.TotalMicroseconds / iterations;
    }

    /// <summary>
    /// MapListInto 批量就地映射吞吐量基准（复用预分配数组，顶层零分配）。
    /// <para>与 MeasureMapListPerformance 同列表同迭代数对比，可观察"全量分配 vs 零分配"差距
    /// （见 how-to/mapping/map-list-into.md 性能节）。</para>
    /// </summary>
    /// <param name="srcs">待映射的源实体列表</param>
    /// <param name="iterations">基准迭代次数（默认 100）</param>
    /// <returns>单次 MapListInto 平均耗时（微秒）</returns>
    public static double MeasureMapListIntoPerformance(List<UserEntity> srcs, int iterations = 100)
    {
        var dtos = new UserDto[srcs.Count];
        for (var i = 0; i < dtos.Length; i++) dtos[i] = new UserDto();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
            Mapper.MapListInto<UserEntity, UserDto>(srcs, dtos);
        sw.Stop();
        return sw.Elapsed.TotalMicroseconds / iterations;
    }

    /// <summary>映射三路径热对比巡览：生成扩展直调（最优）/ 标注类型对 Mapper.Map / 未标注对运行时表达式树。</summary>
    #region Core_Perf_GeneratedHotPath
    public static void GeneratedHotPath(UserEntity entity, OrderConfigSource src)
    {
        // Path 1: source-generated extension method (hottest, zero delegate)
        _ = entity.MapToUserDto();
        // Path 2: Mapper.Map on a [MapFrom]-annotated pair (generated cache hit)
        _ = Mapper.Map<UserEntity, UserDto>(entity);
        // Path 3: runtime expression tree for an unannotated pair (cost paid once on first call)
        _ = Mapper.Map<OrderConfigSource, OrderConfigReverseDto>(src);
    }
    #endregion

    /// <summary>生成层探测与缓存清理三级：HasGenerated 探测 / ClearGenerated 仅清生成层保留手写声明缓存 / ClearCache 全量级联。</summary>
    #region Core_Api_GeneratedProbe
    public static bool GeneratedProbeAndClear(UserEntity src)
    {
        // Probe the generated layer; clear it alone when present (hand-written declaration cache kept)
        if (Mapper.HasGenerated<UserEntity, UserDto>())
            Mapper.ClearGenerated();
        // Full cascade clear (FreeSql/EFCore projection layers included)
        Mapper.ClearCache();
        _ = Mapper.Map<UserEntity, UserDto>(src);
        return Mapper.HasGenerated<UserEntity, UserDto>();
    }
    #endregion

    /// <summary>
    /// 复杂 DTO（ConvertUsing/扁平化）吞吐量基准。
    /// </summary>
    /// <param name="src">待映射的源实体</param>
    /// <param name="iterations">基准迭代次数（默认 500）</param>
    /// <returns>单次复杂映射平均耗时（微秒）</returns>
    public static double MeasureComplexDtoPerformance(UserEntity src, int iterations = 500)
    {
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
            _ = Mapper.Map<UserEntity, UserDetailDto>(src);
        sw.Stop();
        return sw.Elapsed.TotalMicroseconds / iterations;
    }

    /// <summary>
    /// 运行时表达式树回退冷启动（无 [MapFrom] 的 BenchRuntimeDto）。
    /// </summary>
    /// <param name="src">待映射的源实体</param>
    /// <returns>单次冷启动映射耗时（微秒）</returns>
    public static double MeasureRuntimeColdStart(UserEntity src)
    {
        Mapper.ClearCache();
        var sw = Stopwatch.StartNew();
        _ = Mapper.Map<UserEntity, BenchRuntimeDto>(src);
        sw.Stop();
        return sw.Elapsed.TotalMicroseconds;
    }

    /// <summary>
    /// 源生成器扩展方法更新已有对象吞吐量。
    /// </summary>
    /// <param name="src">待映射的源实体</param>
    /// <param name="target">已存在的目标对象（原地更新）</param>
    /// <param name="iterations">基准迭代次数（默认 1000）</param>
    /// <returns>单次生成扩展更新平均耗时（微秒）</returns>
    public static double MeasureSourceGenUpdate(UserEntity src, UserDto target, int iterations = 1000)
    {
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
            src.MapToUserDto(target);
        sw.Stop();
        return sw.Elapsed.TotalMicroseconds / iterations;
    }

    /// <summary>
    /// 三源 Compose 吞吐量基准。
    /// </summary>
    /// <param name="order">订单源实体</param>
    /// <param name="customer">客户源实体</param>
    /// <param name="product">产品源实体</param>
    /// <param name="iterations">基准迭代次数（默认 500）</param>
    /// <returns>单次三源 Compose 平均耗时（微秒）</returns>
    public static double MeasureCompose3Source(OrderEntity order, CustomerEntity customer, ProductEntity product, int iterations = 500)
    {
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
            _ = Mapper.Compose<ReportDto>(order, customer, product);
        sw.Stop();
        return sw.Elapsed.TotalMicroseconds / iterations;
    }

    // ===== 场景注册入口=====
    public static void Run()
    {
    var src = new UserEntity { Id = 1, Name = "Perf", Age = 30, Email = "perf@test.com", Address = new Address { City = "Beijing", Street = "Test" } };

    // Map vs SourceGen
    var mapTime = PerformanceScenarios.MeasureMapPerformance(src, 1000);
    var sgTime = PerformanceScenarios.MeasureSourceGenPerformance(src, 1000);
    OutputHelper.Dump("604-engines", $"Mapper.Map: {mapTime:F2}μs, SourceGen: {sgTime:F2}μs", "");
    // 结构性断言：双引擎产物等值——604-engines 打印点对应的功能行为；耗时只作日志
    var engineViaMap = Mapper.Map<UserEntity, UserDto>(src)!;
    var engineViaSg = src.MapToUserDto();
    if (engineViaMap.Name != engineViaSg.Name || engineViaMap.Email != engineViaSg.Email || engineViaMap.Age != engineViaSg.Age)
        OutputHelper.Error($"604-engines: Mapper.Map and SourceGen extension should produce identical output (Map: {engineViaMap.Name}/{engineViaSg.Name})");

    // 冷启动
    var cold = PerformanceScenarios.MeasureColdStart(src);
    OutputHelper.Dump("604-cold-start", $"First call after ClearCache", $"{cold:F2}μs");
    // 结构性断言：ClearCache 后映射仍成功——缓存重建
    var afterCold = Mapper.Map<UserEntity, UserDto>(src)!;
    if (afterCold.Name != "Perf" || afterCold.Age != 30)
        OutputHelper.Error($"604-cold-start: mapping after ClearCache should still produce correct output (actual: {afterCold.Name}/{afterCold.Age})");

    // Compose 性能
    var order = new OrderEntity { OrderId = 1, OrderNo = "ORD-P", Amount = 100m };
    var customer = new CustomerEntity { Name = "PerfCust", Level = CustomerLevel.Normal, Phone = "111" };
    var composeTime = PerformanceScenarios.MeasureComposePerformance(order, customer, 500);
    OutputHelper.Dump("604-compose", $"Compose x500", $"{composeTime:F2}μs/op");
    // 结构性断言：Compose 产物字段正确——OrderDetailDto.OrderId 来自订单源
    var composeProbe = Mapper.Compose<OrderDetailDto>(order, customer)!;
    if (composeProbe.OrderId != 1)
        OutputHelper.Error($"604-compose: Compose<OrderDetailDto> should carry OrderId=1 from order source (actual: {composeProbe.OrderId})");

    // DeepCopy 策略对比
    var (fast, exprTree, json) = PerformanceScenarios.MeasureDeepCopyStrategies(src, 100);
    OutputHelper.Dump("604-deepcopy-strategies", $"Fast/ExprTree/Json", $"Fast={fast:F2}μs, ExprTree={exprTree:F2}μs, Json={json:F2}μs");
    // 结构性断言：三策略各自产出有效副本——Name 保持
    var fastProbe = DeepCopyScenarios.DeepCopyFast(src);
    var exprProbe = DeepCopyScenarios.DeepCopyExpressionTree(src);
    var jsonProbe = DeepCopyScenarios.DeepCopyJson(src);
    if (fastProbe.Name != "Perf" || exprProbe.Name != "Perf" || jsonProbe.Name != "Perf")
        OutputHelper.Error($"604-deepcopy-strategies: all three providers should copy Name (Fast={fastProbe.Name}, Expr={exprProbe.Name}, Json={jsonProbe.Name})");

    // Pipeline 开销
    var (noPipe, withPipe) = PerformanceScenarios.MeasurePipelineOverhead(src, 500);
    OutputHelper.Dump("604-pipeline", $"NoPipe/WithPipe", $"No={noPipe:F2}μs, With={withPipe:F2}μs");
    // 结构性断言：管线版 PostMap 真实生效——产物带 [P] 前缀
    Mapper.Pipeline.Clear();
    Mapper.Pipeline.AddPostMap<UserEntity, UserDto>((s, t) => { t.Name = $"[P] {t.Name}"; });
    var pipeProbe = Mapper.Map<UserEntity, UserDto>(src)!;
    Mapper.Pipeline.Clear();
    if (pipeProbe.Name != $"[P] {src.Name}")
        OutputHelper.Error($"604-pipeline: pipeline probe should carry [P] prefix (actual: {pipeProbe.Name})");

    // 更新 vs 新建
    var target = new UserDto();
    var (newOp, updateOp) = PerformanceScenarios.MeasureUpdateVsCreate(src, target, 500);
    OutputHelper.Dump("604-update-vs-create", $"CreateNew/UpdateExisting", $"New={newOp:F2}μs, Update={updateOp:F2}μs");
    // 结构性断言：更新重载把字段写入既有对象——「更新已有对象」语义
    var updateProbe = new UserDto();
    Mapper.Map(src, updateProbe);
    if (updateProbe.Name != "Perf" || updateProbe.Age != 30)
        OutputHelper.Error($"604-update-vs-create: Map(src, target) should fill existing target (actual: {updateProbe.Name}/{updateProbe.Age})");

    // 指标统计
    var (calls, hits, comps) = PerformanceScenarios.MeasureMetrics(src);
    OutputHelper.Dump("604-metrics", $"Calls/Hits/Comps", $"Total={calls}, Hits={hits}, Compilations={comps}");
    // 结构性断言：计数器契约——MeasureMetrics 内 ResetMetrics 后映射 100 次，TotalMapCalls 应=100；
    // comps=0 合法：ResetMetrics 只重置计数不清缓存，同对映射全部命中既有缓存
    if (calls != 100)
        OutputHelper.Error($"604-metrics: 100 mappings with MetricsEnabled should report TotalMapCalls=100 (actual: calls={calls}, hits={hits}, comps={comps})");

    // 补充场景
    var perfList = new List<UserEntity>
    {
        new() { Id = 1, Name = "A", Age = 25, Email = "a@test.com" },
        new() { Id = 2, Name = "B", Age = 30, Email = "b@test.com" },
        new() { Id = 3, Name = "C", Age = 35, Email = "c@test.com" },
    };
    var listPerf = PerformanceScenarios.MeasureMapListPerformance(perfList, 100);
    OutputHelper.Dump("604-maplist", $"MapList x100, {perfList.Count} items", $"{listPerf:F2}μs/op");
    // 结构性断言：MapList 产物条数与字段
    var listProbe = Mapper.MapList<UserEntity, UserDto>(perfList);
    if (listProbe.Count != 3 || listProbe[1].Name != "B")
        OutputHelper.Error($"604-maplist: MapList should map 3 items in order (actual: {listProbe.Count}, [1]={listProbe.ElementAtOrDefault(1)?.Name})");

    // MapListInto 对比（同列表同迭代数，观察"全量分配 vs 零分配"差距）
    var listIntoPerf = PerformanceScenarios.MeasureMapListIntoPerformance(perfList, 100);
    OutputHelper.Dump("604-maplist-into", $"MapListInto x100, {perfList.Count} items (reuse)", $"{listIntoPerf:F2}μs/op");
    // 结构性断言：MapListInto 原地填充——预分配数组元素被写入
    var intoTargets = new UserDto[3];
    for (var i = 0; i < intoTargets.Length; i++) intoTargets[i] = new UserDto();
    Mapper.MapListInto<UserEntity, UserDto>(perfList, intoTargets);
    if (intoTargets.Any(t => t.Name == null || t.Name == ""))
        OutputHelper.Error("604-maplist-into: MapListInto should fill every pre-allocated slot in place");

    var detailSrcPerf = new UserEntity
    {
        Id = 100, Name = "PerfDetail", Age = 28, Email = "detail@perf.com",
        Status = UserStatus.Active, CreatedAt = DateTime.Now,
        Address = new Address { City = "Shanghai", Street = "Test" },
    };
    var complexTime = PerformanceScenarios.MeasureComplexDtoPerformance(detailSrcPerf, 500);
    OutputHelper.Dump("604-complex-dto", "UserDetailDto x500", $"{complexTime:F2}μs/op");
    // 结构性断言：复杂 DTO（ConvertUsing/扁平化）产物字段——UserDetailDto.Id
    var complexProbe = Mapper.Map<UserEntity, UserDetailDto>(detailSrcPerf)!;
    if (complexProbe.Id != 100 || complexProbe.DisplayName == "")
        OutputHelper.Error($"604-complex-dto: complex mapping should carry Id=100 and DisplayName (actual: {complexProbe.Id}/{complexProbe.DisplayName})");

    var coldRuntime = PerformanceScenarios.MeasureRuntimeColdStart(src);
    OutputHelper.Dump("604-cold-runtime", "BenchRuntimeDto cold start", $"{coldRuntime:F2}μs");
    // 结构性断言：运行时表达式树回退产物字段——BenchRuntimeDto.Id
    var runtimeProbe = Mapper.Map<UserEntity, BenchRuntimeDto>(src)!;
    if (runtimeProbe.Id != 1)
        OutputHelper.Error($"604-cold-runtime: runtime-compiled mapping should carry Id (actual: {runtimeProbe.Id})");

    var targetPerf = new UserDto();
    var sgUpdateTime = PerformanceScenarios.MeasureSourceGenUpdate(src, targetPerf, 500);
    OutputHelper.Dump("604-sg-update", "SourceGen update x500", $"{sgUpdateTime:F2}μs/op");
    // 结构性断言：源生成扩展更新重载写入既有对象
    var sgUpdateProbe = new UserDto();
    src.MapToUserDto(sgUpdateProbe);
    if (sgUpdateProbe.Name != "Perf")
        OutputHelper.Error($"604-sg-update: SourceGen update should fill existing target (actual: {sgUpdateProbe.Name})");

    var productPerf = new ProductEntity { ProductId = 5001, Name = "PerfProduct", Price = 100m };
    var comp3Time = PerformanceScenarios.MeasureCompose3Source(order, customer, productPerf, 500);
    OutputHelper.Dump("604-compose-3src", "Compose 3-source x500", $"{comp3Time:F2}μs/op");
    // 结构性断言：三源合成产物——ReportDto.OrderNumber 来自订单源
    var comp3Probe = Mapper.Compose<ReportDto>(order, customer, productPerf)!;
    if (comp3Probe.OrderNumber == null || comp3Probe.OrderNumber == "")
        OutputHelper.Error($"604-compose-3src: 3-source Compose should carry order number (actual: {comp3Probe.OrderNumber ?? "null"})");
    }

}
