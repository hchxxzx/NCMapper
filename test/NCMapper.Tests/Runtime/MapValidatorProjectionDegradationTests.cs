// MapValidator 投影降级可见性测试（D-12 用例矩阵 ①~⑬）
//   报告面 = Validate() 双通道降级维度：特性通道对（[MapFrom] 循环内 EvaluateChannelAgnostic 填充，D-10）
//   + Fluent 通道补扫（RuntimeSnapshotStore.All 过滤，直接构造行 Level 恒 OK——Z-3 裁决，D-9）。
//  变异演练 V1/V2 与反向验证为执行期流程（三列登记），非本文件用例。
// 隔离范式（经验库 #71/#60/#65）：[Collection("GlobalConfigState")]（DisableParallelization）+
//   每用例自足（Clear + 显式清各下游缓存 + 手工 Register）+ try/finally 清理。
// 负向先验锚（#67）：每个不降级断言携带"注册表条目存在性 / 正向对翻转"先验；
//   Fluent 正向降级例 fixture 纯度约束（D-14）：不携带属性级 NCMapper 特性——否则特性扫描腿兜底拦截，
//   特性通道诊断由 Fluent 通道承载，V1 变异将不红。
using System.Reflection;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.EFCore;
using NCMapper.FreeSql;
using NCMapper.Internal;
using NCMapper.Runtime;
using Xunit;

namespace NCMapper.Tests.Runtime;

[Collection("GlobalConfigState")]
public class MapValidatorProjectionDegradationTests
{
    // ================================================================
    // 夹具集（每用例独立类型对——避免类型对缓存串扰；均不携带属性级 NCMapper 特性，D-14 纯度）
    // ================================================================

    // ① 纯可翻译 Fluent 对（Map 异名）
    public class AgnEntity
    {
        public int Id { get; set; }
        public string RealName { get; set; } = "";
    }
    public class AgnView
    {
        public int Id { get; set; }
        public string AliasName { get; set; } = "";
    }

    // ② Mask Fluent 对（pm 级 HasMask）
    public class AgnMaskEntity
    {
        public int Id { get; set; }
        public string Phone { get; set; } = "";
    }
    public class AgnMaskView
    {
        public int Id { get; set; }
        public string Phone { get; set; } = "";
    }

    // ③ Convert Fluent 对（pm 级 HasConverter）
    public class AgnCnvEntity
    {
        public int Id { get; set; }
        public string Money { get; set; } = "";
    }
    public class AgnCnvView
    {
        public int Id { get; set; }
        public string Money { get; set; } = "";
    }

    // ④ record 目标 Fluent 对（纯可翻译明细 → 顶层无参构造腿）
    public class AgnRecEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }
    public record AgnRecView(int Id, string AliasName);

    // ⑤ 特性侧无降级声明对（[MapFrom] 同名直映射）
    public class AttrCleanEntity
    {
        public int Id { get; set; }
    }
    [MapFrom(typeof(AttrCleanEntity))]
    public class AttrCleanView
    {
        public int Id { get; set; }
    }

    // ⑥ 特性侧属性级 [Convert] 对（属性级特性扫描腿；ConvertUsing 为 AttributeTargets.Property，
    //    类级标注编译不可行——评审 002 J-4 定案）
    public class AttrConvEntity
    {
        public string Code { get; set; } = "";
    }
    public static class AgnConvHelper
    {
        public static string Conv(string v) => v + "-conv";
    }
    [MapFrom(typeof(AttrConvEntity))]
    public class AttrConvView
    {
        [Convert(typeof(AgnConvHelper), nameof(AgnConvHelper.Conv))]
        public string Code { get; set; } = "";
    }

    // ⑨ 双通道并存（同目标异源）：DualView 标注 [MapFrom(B)] + 手工 Register(A, DualView, Fluent)
    public class DualSourceA
    {
        public int Id { get; set; }
    }
    public class DualSourceB
    {
        public int Id { get; set; }
    }
    [MapFrom(typeof(DualSourceB))]
    public class DualView
    {
        public int Id { get; set; }
    }

    // ⑩ Fluent 嵌套子 DTO 不可翻译（嵌套无关腿——嵌套 Fluent 明细判定）
    public class NestEntity
    {
        public int Id { get; set; }
        public string Phone { get; set; } = "";
    }
    public class NestChildView
    {
        public string Phone { get; set; } = "";
    }
    public class NestTopView
    {
        public int Id { get; set; }
        public NestChildView Child { get; set; } = new();
    }

    // ⑬ Fluent 嵌套 record 子 DTO（嵌套无参构造腿——V2 检出例，评审 002 Z-2）
    public class RecNestEntity
    {
        public int Id { get; set; }
    }
    public record RecNestChild(int Id);
    public class RecNestTopView
    {
        public int Id { get; set; }
        public RecNestChild Child { get; set; } = new(0);
    }

    // ================================================================
    // 隔离与注册辅助（#71 三件套 + agnostic 缓存；#60 幂等注册）
    // ================================================================

    private static void ClearAllState()
    {
        RuntimeSnapshotStore.Clear();
        EfCoreDegradationDetector.ClearCache();
        FreeSqlDegradationDetector.ClearCache();
        NavigationTreeCollector.ClearCache();
        ProjectionExpressionFactory.ClearCache();
        DegradationDetectorBase.ClearAgnosticCache();
    }

    private static void ClearDownstreamCaches()
    {
        EfCoreDegradationDetector.ClearCache();
        FreeSqlDegradationDetector.ClearCache();
        NavigationTreeCollector.ClearCache();
        ProjectionExpressionFactory.ClearCache();
        DegradationDetectorBase.ClearAgnosticCache();
    }

    /// <summary>每用例注册表快照（Clear 前捕获，finally 重放——隔离纪律见 CaptureRegistry 注释）</summary>
    private static List<RuntimeMapperMetadata> _registrySnapshot = new();

    /// <summary>
    /// 注册表快照-恢复（本任务新增纪律）—— RuntimeSnapshotStore.Clear 销毁 ModuleInitializer
    /// 注册的生成器条目且不可逆，会破坏跨类消费方（ValidateSourceGenTests 一致性比对依赖注册表
    /// 视图）；每用例 Clear 前快照、finally 重放（Register 幂等合并，终态等价）+ 清下游缓存
    /// （缓存键无注册表版本，重算前注册表须已在位）。
    /// </summary>
    private static List<RuntimeMapperMetadata> CaptureRegistry() => new(RuntimeSnapshotStore.All);

    private static void RestoreRegistry(List<RuntimeMapperMetadata> snapshot)
    {
        foreach (var m in snapshot) RuntimeSnapshotStore.Register(m);
        ClearDownstreamCaches();
    }

    private static void RegisterFluent(Type source, Type target, RuntimePropertyMapping[] properties)
    {
        RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
        {
            SourceType = source,
            TargetType = target,
            SourceTypeFqn = source.FullName!,
            TargetTypeFqn = target.FullName!,
            Channel = RuntimeMappingChannel.Fluent,
            IsFluentForward = true,
            Properties = properties,
        });
    }

    private static RuntimePropertyMapping Pm(string target, string sourceName,
        bool hasMask = false, bool hasConverter = false, string typeName = "String")
        => new()
        {
            Target = new RuntimeTargetPropertySlot { Name = target, TypeName = typeName },
            SourceName = sourceName,
            HasMask = hasMask,
            HasConverter = hasConverter,
            ConverterDeclaringType = hasConverter ? typeof(AgnConvHelper).FullName : null,
            ConverterMethodName = hasConverter ? nameof(AgnConvHelper.Conv) : null,
        };

    /// <summary>取指定 (源,目标) 对的验证结果行（找不到即 Fail——先验锚）</summary>
    private static MapValidationResult FindRow(List<MapValidationResult> results, Type source, Type target)
    {
        var row = results.FirstOrDefault(r => r.SourceType == source && r.TargetType == target);
        Assert.NotNull(row);
        return row!;
    }

    private static Assembly TestAsm() => typeof(MapValidatorProjectionDegradationTests).Assembly;

    // ================================================================
    // ① Fluent 纯可翻译对（Map 异名）→ 不降级；Level=OK 且 Message 不含"不存在"（Z-3 防假 Warn 回归锚）
    // ================================================================

    [Fact]
    public void FluentTranslatablePair_NotDowngraded_And_NoFalseWarn()
    {
        try
        {
            _registrySnapshot = CaptureRegistry();
            ClearAllState();
            // 注册（先验锚：注册表条目存在）
            RegisterFluent(typeof(AgnEntity), typeof(AgnView), new[] { Pm("AliasName", "RealName") });
            Assert.True(RuntimeSnapshotStore.IsFluentRegisteredDto(typeof(AgnView)));

            var results = MapValidator.Validate(TestAsm());
            var row = FindRow(results, typeof(AgnEntity), typeof(AgnView));

            // 不降级（纯可翻译：Map 异名经注册表明细可翻译）
            Assert.False(row.RequiresProjectionDowngrade);
            Assert.Equal("", row.ProjectionDowngradeReason);
            // Z-3 防假 Warn 回归锚：补扫行 Level 恒 OK、Message 不含"源字段不存在"
            //（若误走 ValidateMapping，异名 AliasName 将报"源字段 AliasName 不存在"假 Warn）
            Assert.Equal(MapValidationLevel.OK, row.Level);
            Assert.DoesNotContain("不存在", row.Message);
        }
        finally { RestoreRegistry(_registrySnapshot); }
    }

    // ================================================================
    // ② Fluent 含 Mask → 降级（reason 含声明类别 Mask）
    // ================================================================

    [Fact]
    public void FluentMaskPair_Downgraded_WithCategoryReason()
    {
        try
        {
            _registrySnapshot = CaptureRegistry();
            ClearAllState();
            RegisterFluent(typeof(AgnMaskEntity), typeof(AgnMaskView), new[]
            {
                Pm("Id", "Id", typeName: "Int32"),
                Pm("Phone", "Phone", hasMask: true),
            });

            var results = MapValidator.Validate(TestAsm());
            var row = FindRow(results, typeof(AgnMaskEntity), typeof(AgnMaskView));

            Assert.True(row.RequiresProjectionDowngrade);
            Assert.Contains("Mask", row.ProjectionDowngradeReason);
            Assert.Contains("不可翻译声明", row.ProjectionDowngradeReason);
        }
        finally { RestoreRegistry(_registrySnapshot); }
    }

    // ================================================================
    // ③ Fluent 含 Convert → 降级（reason 含声明类别 Convert）
    // ================================================================

    [Fact]
    public void FluentConvertPair_Downgraded_WithCategoryReason()
    {
        try
        {
            _registrySnapshot = CaptureRegistry();
            ClearAllState();
            RegisterFluent(typeof(AgnCnvEntity), typeof(AgnCnvView), new[]
            {
                Pm("Id", "Id", typeName: "Int32"),
                Pm("Money", "Money", hasConverter: true),
            });

            var results = MapValidator.Validate(TestAsm());
            var row = FindRow(results, typeof(AgnCnvEntity), typeof(AgnCnvView));

            Assert.True(row.RequiresProjectionDowngrade);
            Assert.Contains("Convert", row.ProjectionDowngradeReason);
        }
        finally { RestoreRegistry(_registrySnapshot); }
    }

    // ================================================================
    // ④ Fluent record 目标 → 降级（顶层无参构造腿 :149-153——V2 变异不影响本例，Z-2 映射依据）
    // ================================================================

    [Fact]
    public void FluentRecordTarget_Downgraded_TopLevelCtorReason()
    {
        try
        {
            _registrySnapshot = CaptureRegistry();
            ClearAllState();
            RegisterFluent(typeof(AgnRecEntity), typeof(AgnRecView), new[]
            {
                Pm("Id", "Id", typeName: "Int32"),
                Pm("AliasName", "Name"),
            });

            var results = MapValidator.Validate(TestAsm());
            var row = FindRow(results, typeof(AgnRecEntity), typeof(AgnRecView));

            Assert.True(row.RequiresProjectionDowngrade);
            Assert.Contains("无无参构造器", row.ProjectionDowngradeReason);
            Assert.Contains("顶层 DTO 类型", row.ProjectionDowngradeReason);
        }
        finally { RestoreRegistry(_registrySnapshot); }
    }

    // ================================================================
    // ⑤ 特性侧无降级声明对 → 不降级（先验锚：行存在且映射计数正常）
    // ================================================================

    [Fact]
    public void AttributeSideCleanPair_NotDowngraded()
    {
        try
        {
            _registrySnapshot = CaptureRegistry();
            ClearAllState();

            var results = MapValidator.Validate(TestAsm());
            var row = FindRow(results, typeof(AttrCleanEntity), typeof(AttrCleanView));

            // 先验锚：属性完整性正常（同名 Id 直映射）
            Assert.Equal(MapValidationLevel.OK, row.Level);
            Assert.True(row.MappedCount > 0);
            Assert.False(row.RequiresProjectionDowngrade);
            Assert.Equal("", row.ProjectionDowngradeReason);
        }
        finally { RestoreRegistry(_registrySnapshot); }
    }

    // ================================================================
    // ⑥ 特性侧属性级 [Convert] 对 → 降级（属性级特性扫描腿；IProjectionPolicy.RequiresDowngrade）
    // ================================================================

    [Fact]
    public void AttributeSideConvertUsing_Downgraded_PropertyScanLeg()
    {
        try
        {
            _registrySnapshot = CaptureRegistry();
            ClearAllState();

            var results = MapValidator.Validate(TestAsm());
            var row = FindRow(results, typeof(AttrConvEntity), typeof(AttrConvView));

            Assert.True(row.RequiresProjectionDowngrade);
            // 属性级扫描腿 reason 文本（逐字保留）：属性 {name} 标注了 {attr}（需要降级）
            Assert.Contains("属性 Code", row.ProjectionDowngradeReason);
            Assert.Contains("ConvertAttribute", row.ProjectionDowngradeReason);
        }
        finally { RestoreRegistry(_registrySnapshot); }
    }

    // ================================================================
    // ⑦ PrintReport：汇总行"降级 N 对"恒输出（showOk=false 亦输出）；明细行跟随 showOk
    // ================================================================

    [Fact]
    public void PrintReport_DegradeSummaryAlways_And_DetailFollowsShowOk()
    {
        // 验证消息双语化后本断言锁定 zh 形态，固定 zh-CN 文化
        //（B2 特例登记：断言文本零改动，仅消除新引入的文化依赖；先例 ErrorMessagesLocalizationTests 模式）
        var originalCurrent = System.Globalization.CultureInfo.CurrentCulture;
        var originalUi = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("zh-CN");
            System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("zh-CN");

            _registrySnapshot = CaptureRegistry();
            ClearAllState();
            RegisterFluent(typeof(AgnMaskEntity), typeof(AgnMaskView), new[]
            {
                Pm("Id", "Id", typeName: "Int32"),
                Pm("Phone", "Phone", hasMask: true),
            });
            var results = MapValidator.Validate(TestAsm());
            var row = FindRow(results, typeof(AgnMaskEntity), typeof(AgnMaskView));
            Assert.True(row.RequiresProjectionDowngrade); // 先验锚

            // 单元素列表喂 PrintReport（测试程序集含其他 [MapFrom] fixture，全量列表的降级计数不可预知——
            // 精确控制输入使汇总计数断言确定化）
            var single = new List<MapValidationResult> { row };

            // showOk=false：汇总行恒输出降级计数；OK 级降级行明细不出现（跟随所属行可见性）
            var sw1 = new StringWriter();
            MapValidator.PrintReport(single, sw1, showOk: false);
            var report1 = sw1.ToString();
            Assert.Contains("⤵ 降级 1 对", report1);
            Assert.DoesNotContain("⤵ 降级原因:", report1);

            // showOk=true：明细行出现，含 reason 全文
            var sw2 = new StringWriter();
            MapValidator.PrintReport(single, sw2, showOk: true);
            var report2 = sw2.ToString();
            Assert.Contains("⤵ 降级 1 对", report2);
            Assert.Contains("⤵ 降级原因:", report2);
            Assert.Contains("不可翻译声明（Mask）", report2);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = originalCurrent;
            System.Globalization.CultureInfo.CurrentUICulture = originalUi;
            RestoreRegistry(_registrySnapshot);
        }
    }

    // ================================================================
    // ⑧ ToString 降级标记：降级行尾追加 " ⤵ ORM投影降级"；非降级行不含
    // ================================================================

    [Fact]
    public void ToString_DegradeMarkAppended()
    {
        // 验证消息双语化后本断言锁定 zh 形态，固定 zh-CN 文化
        //（B2 特例登记：断言文本零改动，仅消除新引入的文化依赖；先例 ErrorMessagesLocalizationTests 模式）
        var originalCurrent = System.Globalization.CultureInfo.CurrentCulture;
        var originalUi = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("zh-CN");
            System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("zh-CN");

            _registrySnapshot = CaptureRegistry();
            ClearAllState();
            RegisterFluent(typeof(AgnMaskEntity), typeof(AgnMaskView), new[]
            {
                Pm("Id", "Id", typeName: "Int32"),
                Pm("Phone", "Phone", hasMask: true),
            });
            var results = MapValidator.Validate(TestAsm());

            var degraded = FindRow(results, typeof(AgnMaskEntity), typeof(AgnMaskView));
            Assert.Contains("⤵ ORM投影降级", degraded.ToString());

            // 非降级行（同报告中的特性行 AttrCleanView）不含标记
            var clean = FindRow(results, typeof(AttrCleanEntity), typeof(AttrCleanView));
            Assert.False(clean.RequiresProjectionDowngrade);
            Assert.DoesNotContain("⤵ ORM投影降级", clean.ToString());
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = originalCurrent;
            System.Globalization.CultureInfo.CurrentUICulture = originalUi;
            RestoreRegistry(_registrySnapshot);
        }
    }

    // ================================================================
    // ⑨ 双通道并存（同目标异源）→ 特性行与 Fluent 补扫行各一行，互不吞并
    // ================================================================

    [Fact]
    public void DualChannelPairs_BothRowsPresent()
    {
        // 验证消息双语化后本断言锁定 zh 形态，固定 zh-CN 文化
        //（B2 特例登记追加：评审期 P4 清单外发现的第 6 个中文锁定点——R3 预演词表未覆盖"Fluent 声明对"；
        // 断言文本零改动，仅消除新引入的文化依赖；先例 ErrorMessagesLocalizationTests 模式）
        var originalCurrent = System.Globalization.CultureInfo.CurrentCulture;
        var originalUi = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("zh-CN");
            System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("zh-CN");

            _registrySnapshot = CaptureRegistry();
            ClearAllState();
            // DualView 已标注 [MapFrom(DualSourceB)]（特性通道）；Fluent 补充 (DualSourceA, DualView)
            RegisterFluent(typeof(DualSourceA), typeof(DualView), new[] { Pm("Id", "Id", typeName: "Int32") });

            var results = MapValidator.Validate(TestAsm());
            var attrRow = FindRow(results, typeof(DualSourceB), typeof(DualView));
            var fluentRow = FindRow(results, typeof(DualSourceA), typeof(DualView));

            Assert.NotSame(attrRow, fluentRow);
            // 特性行：属性完整性正常 + 无降级声明
            Assert.False(attrRow.RequiresProjectionDowngrade);
            // Fluent 补扫行：Level 恒 OK（Z-3）、不降级（纯可翻译）、Message 注记
            Assert.Equal(MapValidationLevel.OK, fluentRow.Level);
            Assert.False(fluentRow.RequiresProjectionDowngrade);
            Assert.Contains("Fluent 声明对", fluentRow.Message);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = originalCurrent;
            System.Globalization.CultureInfo.CurrentUICulture = originalUi;
            RestoreRegistry(_registrySnapshot);
        }
    }

    // ================================================================
    // ⑩ Fluent 嵌套子 DTO 不可翻译 → 顶层对降级（嵌套无关腿：嵌套 Fluent 明细判定）
    // ================================================================

    [Fact]
    public void FluentNestedUntranslatableChild_TopLevelDowngraded()
    {
        try
        {
            _registrySnapshot = CaptureRegistry();
            ClearAllState();
            // 顶层：纯可翻译（Id 同名）；子 DTO：Fluent on 态含 Mask 明细
            RegisterFluent(typeof(NestEntity), typeof(NestTopView), new[] { Pm("Id", "Id", typeName: "Int32") });
            RegisterFluent(typeof(NestEntity), typeof(NestChildView), new[]
            {
                Pm("Phone", "Phone", hasMask: true),
            });

            var results = MapValidator.Validate(TestAsm());
            var row = FindRow(results, typeof(NestEntity), typeof(NestTopView));

            Assert.True(row.RequiresProjectionDowngrade);
            Assert.Contains("嵌套 Fluent DTO", row.ProjectionDowngradeReason);
            Assert.Contains("Mask", row.ProjectionDowngradeReason);
        }
        finally { RestoreRegistry(_registrySnapshot); }
    }

    // ================================================================
    // ⑪ 空注册表冒烟：Clear 后 Validate 输出无 Fluent 补扫行（先验锚：特性行非空——本程序集含 [MapFrom] fixture）
    // ================================================================

    [Fact]
    public void EmptyRegistry_NoFluentRows_Smoke()
    {
        try
        {
            _registrySnapshot = CaptureRegistry();
            ClearAllState();
            Assert.Equal(0, RuntimeSnapshotStore.All.Count); // 先验锚：注册表确已清空

            var results = MapValidator.Validate(TestAsm());

            // 先验锚：本程序集含 [MapFrom] fixture（⑤⑥⑨），特性行必然非空
            Assert.True(results.Count > 0);
            // 断言：结果中不存在任何 Fluent 补扫行（Message 注记特征）
            Assert.All(results, r => Assert.DoesNotContain("Fluent 声明对", r.Message));
        }
        finally { RestoreRegistry(_registrySnapshot); }
    }

    // ================================================================
    // ⑬ Fluent 嵌套 record 子 DTO → 顶层对降级（嵌套无参构造腿——V2 检出例，评审 002 Z-2）
    // ================================================================

    [Fact]
    public void FluentNestedRecordChild_TopLevelDowngraded_NestedCtorLeg()
    {
        try
        {
            _registrySnapshot = CaptureRegistry();
            ClearAllState();
            // 顶层与子 DTO 均纯可翻译；子 DTO 为 record（无无参构造）→ 嵌套无参构造腿命中
            RegisterFluent(typeof(RecNestEntity), typeof(RecNestTopView), new[] { Pm("Id", "Id", typeName: "Int32") });
            RegisterFluent(typeof(RecNestEntity), typeof(RecNestChild), new[] { Pm("Id", "Id", typeName: "Int32") });

            var results = MapValidator.Validate(TestAsm());
            var row = FindRow(results, typeof(RecNestEntity), typeof(RecNestTopView));

            Assert.True(row.RequiresProjectionDowngrade);
            // 嵌套腿 reason 文本（逐字保留）：嵌套属性 {prop} 的元素类型 {type} 无无参构造器...
            Assert.Contains("嵌套属性 Child", row.ProjectionDowngradeReason);
            Assert.Contains("无无参构造器", row.ProjectionDowngradeReason);
        }
        finally { RestoreRegistry(_registrySnapshot); }
    }
}
