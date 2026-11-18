using Xunit;

namespace NCMapper.IntegrationTests;

/// <summary>
/// 验收测试（DeepCopyTypeClassifier 接入 ClearCache 链）
/// （投影缓存清理委托多槽位）（源生成 Validate 注册字典分槽）
/// <para>测试隔离说明：T3 的 Validate 委托注册残留于进程级静态字典，本类实现 IDisposable 在
/// Dispose 时以 <c>RegisterGeneratedValidate(null)</c> 复位（null = 清空全部槽位语义）；
/// T2 的投影清理计数委托幂等无害（仅自增计数，不改变任何被测状态），无注销 API，
/// 残留面登记。注册一律使用独立静态类的方法组（禁 lambda —— 编译器闭包类
/// 可能共享 DeclaringType 致键冲突合并，回流 P3-1 约束）。</para>
/// </summary>
[Collection("PipelineGlobalState")]
public class Wave2SlotAndClearChainFixTests : IDisposable
{
    // ================================================================
    // 前置：可被分类器分类的普通引用类型
    // ================================================================

    private class V2Entity
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    // ================================================================
    // 双槽位：两个独立静态类的方法组注册
    // ================================================================

    private static class ProjectionClearCounterA
    {
        public static int Count;

        public static void Clear() => Interlocked.Increment(ref Count);
    }

    private static class ProjectionClearCounterB
    {
        public static int Count;

        public static void Clear() => Interlocked.Increment(ref Count);
    }

    // ================================================================
    // 幂等覆盖：同一声明类型第二个方法组
    // ================================================================

    private static class ProjectionClearCounterC
    {
        public static int Count;
        public static int Count2;

        public static void Clear() => Interlocked.Increment(ref Count);

        public static void Clear2() => Interlocked.Increment(ref Count2);
    }

    // ================================================================
    // 两个独立静态类的方法组验证委托
    // ================================================================

    private class ValidateEntityA { }

    private class ValidateDtoA { }

    private class ValidateEntityB { }

    private class ValidateDtoB { }

    private static class ValidateSlotA
    {
        public static List<MapValidationResult> Validate() => new()
        {
            new MapValidationResult
            {
                SourceType = typeof(ValidateEntityA),
                TargetType = typeof(ValidateDtoA),
                Level = MapValidationLevel.OK,
                MappedCount = 1,
                TotalCount = 1,
            },
        };
    }

    private static class ValidateSlotB
    {
        public static List<MapValidationResult> Validate() => new()
        {
            new MapValidationResult
            {
                SourceType = typeof(ValidateEntityB),
                TargetType = typeof(ValidateDtoB),
                Level = MapValidationLevel.OK,
                MappedCount = 1,
                TotalCount = 1,
            },
        };
    }

    /// <summary>
    /// T3 隔离复位 — null = 清空全部已注册 Validate 槽位（R-c 隔离策略）
    /// </summary>
    public void Dispose()
    {
        Mapper.RegisterGeneratedValidate(null);
    }

    // ================================================================
    // V2：ClearCache 链清空 DeepCopyTypeClassifier 冷路径缓存
    // ================================================================

    [Fact]
    public void T1_ClearCache_Clears_DeepCopyTypeClassifier_Cache()
    {
        // 清空冷路径缓存，保证确定性起点
        DeepCopyTypeClassifier.ClearCache();
        Assert.Equal(0, DeepCopyTypeClassifier.CacheEntryCount);

        // 触发一次分类 → 冷路径缓存应非空
        _ = DeepCopyTypeClassifier.Classify(typeof(V2Entity));
        Assert.True(DeepCopyTypeClassifier.CacheEntryCount > 0,
            "Classify 后冷路径缓存应有条目（V2 前置条件）");

        // 接入 ClearCache 链 → 缓存被清空
        Mapper.ClearCache();
        Assert.Equal(0, DeepCopyTypeClassifier.CacheEntryCount);
    }

    // ================================================================
    // V4：双注册均生效（修复前单槽位下先注册者计数必为 0）
    // ================================================================

    [Fact]
    public void T2_MultipleProjectionCacheClearSlots_AllInvoked()
    {
        ProjectionClearCounterA.Count = 0;
        ProjectionClearCounterB.Count = 0;

        // 方法组注册（禁 lambda，回流 P3-1）— 两个独立静态类，键互不相同
        Mapper.RegisterProjectionCacheClear(ProjectionClearCounterA.Clear);
        Mapper.RegisterProjectionCacheClear(ProjectionClearCounterB.Clear);

        Mapper.ClearCache();

        Assert.Equal(1, ProjectionClearCounterA.Count);
        Assert.Equal(1, ProjectionClearCounterB.Count);
    }

    // ================================================================
    // V5：同一声明类型重复注册 → 幂等覆盖，仅新委托执行一次
    // ================================================================

    [Fact]
    public void T2_SameDeclaringType_Registration_ReplacesIdempotently()
    {
        ProjectionClearCounterC.Count = 0;
        ProjectionClearCounterC.Count2 = 0;

        Mapper.RegisterProjectionCacheClear(ProjectionClearCounterC.Clear);
        Mapper.RegisterProjectionCacheClear(ProjectionClearCounterC.Clear2);

        Mapper.ClearCache();

        // 同键（声明类型 = ProjectionClearCounterC）重复注册 → 仅新委托（Clear2）执行一次
        Assert.Equal(0, ProjectionClearCounterC.Count);
        Assert.Equal(1, ProjectionClearCounterC.Count2);
    }

    // ================================================================
    // V8：双注册结果聚合（ValidationEnabled 默认 true；B13 只认内容不认顺序）
    // ================================================================

    [Fact]
    public void T3_Validate_Aggregates_MultipleSlots()
    {
        // 清理残留槽位（含 SourceGen GeneratedMapper 若同进程注入，P3-4 防护）
        Mapper.RegisterGeneratedValidate(null);
        Mapper.RegisterGeneratedValidate(ValidateSlotA.Validate);
        Mapper.RegisterGeneratedValidate(ValidateSlotB.Validate);

        var results = Mapper.Validate();

        // 聚合语义：双方结果均出现；"包含"语义防他处注入槽位干扰（B13）
        Assert.Contains(results, r => r.SourceType == typeof(ValidateEntityA));
        Assert.Contains(results, r => r.SourceType == typeof(ValidateEntityB));
    }

    // ================================================================
    // V9：单注册行为不变 — 结果与该委托返回逐条一致
    // ================================================================

    [Fact]
    public void T3_SingleRegistration_BehaviorUnchanged()
    {
        Mapper.RegisterGeneratedValidate(null);
        Mapper.RegisterGeneratedValidate(ValidateSlotA.Validate);

        var results = Mapper.Validate();

        Assert.Single(results);
        Assert.Equal(typeof(ValidateEntityA), results[0].SourceType);
        Assert.Equal(typeof(ValidateDtoA), results[0].TargetType);
        Assert.Equal(MapValidationLevel.OK, results[0].Level);
    }

    // ================================================================
    // V10：null 重置语义 — 字典空后 Validate() 走层2 反射路径
    // ================================================================

    [Fact]
    public void T3_NullRegistration_Resets_ToLayer2()
    {
        Mapper.RegisterGeneratedValidate(ValidateSlotA.Validate);
        Mapper.RegisterGeneratedValidate(ValidateSlotB.Validate);
        Mapper.RegisterGeneratedValidate(null); // null = 清空全部槽位

        var ex = Record.Exception(() =>
        {
            var results = Mapper.Validate();

            // 层2 反射路径：扫描 AppDomain 中 [MapFrom] 类型；
            // A/B 槽位标记结果不应出现（证明未走聚合路径）
            Assert.DoesNotContain(results, r => r.SourceType == typeof(ValidateEntityA));
            Assert.DoesNotContain(results, r => r.SourceType == typeof(ValidateEntityB));
        });
        Assert.Null(ex);
    }
}
