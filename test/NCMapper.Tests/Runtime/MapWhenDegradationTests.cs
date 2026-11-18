// 验收 #4：[MapWhen] 特性面投影降级断言——
//   条件守卫谓词为 CLR 委托路径（SQL 不可翻译），IProjectionPolicy.RequiresDowngrade=true 自声明 +
//  EvaluateTopLayers 属性级 IProjectionPolicy 扫描命中 → 投影恒降级（分类 MapWhen 与分类断言联动，
//  本测试先锁特性面；Fluent 侧 ConditionMethodRef 明细分类随交付）。
// 隔离：[Collection("GlobalConfigState")]（DisableParallelization）+ 每用例自足注册 + 检测器 ClearCache + finally 清理。
using NCMapper;
using NCMapper.EFCore;
using NCMapper.FreeSql;
using NCMapper.Runtime;
using Xunit;

namespace NCMapper.Tests.Runtime;

[Collection("GlobalConfigState")]
public class MapWhenDegradationTests
{
    public class MwDEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    [NCMapper.Attributes.MapFrom(typeof(MwDEntity))]
    public class MwDDto
    {
        public int Id { get; set; }

        [NCMapper.Attributes.MapWhen(nameof(HasContent))]
        public string Name { get; set; } = "";

        public static bool HasContent(string value) => !string.IsNullOrEmpty(value);
    }

    [Fact]
    public void MapWhenAttribute_ForceDowngrade_BothOrmDetectors()
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            EfCoreDegradationDetector.ClearCache();
            FreeSqlDegradationDetector.ClearCache();

            // 特性面：[MapWhen] 实现 IProjectionPolicy（RequiresDowngrade=true）——
            // EvaluateTopLayers 属性级扫描命中（"属性 Name 标注了 MapWhenAttribute（需要降级）"）
            Assert.True(EfCoreDegradationDetector.NeedsDegradation(typeof(MwDEntity), typeof(MwDDto), out var efReason));
            Assert.Contains("MapWhenAttribute", efReason);

            Assert.True(FreeSqlDegradationDetector.NeedsDegradation(typeof(MwDEntity), typeof(MwDDto), out var fsReason));
            Assert.Contains("MapWhenAttribute", fsReason);
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            EfCoreDegradationDetector.ClearCache();
            FreeSqlDegradationDetector.ClearCache();
        }
    }

    // ================================================================
    // 投影维度（行——2026-09-07）：Fluent 侧分类 "MapWhen" + MapValidator 报告行
    // ================================================================

    // Fluent 分类断言夹具类型对（独立键——注册表手工登记，不依赖 ModuleInitializer 时序）
    public class MwFlEntity { public int Id { get; set; } public string Name { get; set; } = ""; }
    public class MwFlDto { public int Id { get; set; } public string Name { get; set; } = ""; }

    /// <summary>
    /// Fluent MapWhen 声明 → 分类 "MapWhen"（DegradationDetectorBase.ClassifyUntranslatableDeclaration
    /// ConditionMethodRef 分支——注册表明细 ConditionMethodRef 承载消费；B 形态生成方法引用
    /// 与 A 形态用户方法引用同分支——谓词为 CLR 委托路径，SQL 不可翻译恒降级）。
    /// </summary>
    [Fact]
    public void FluentMapWhen_ConditionRef_ClassifiedAsMapWhen_NeedsDegradation()
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            EfCoreDegradationDetector.ClearCache();
            FreeSqlDegradationDetector.ClearCache();

            RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
            {
                SourceType = typeof(MwFlEntity),
                TargetType = typeof(MwFlDto),
                SourceTypeFqn = typeof(MwFlEntity).FullName!,
                TargetTypeFqn = typeof(MwFlDto).FullName!,
                Channel = RuntimeMappingChannel.Fluent,
                IsFluentForward = true,
                Properties = new[]
                {
                    new RuntimePropertyMapping
                    {
                        Target = new RuntimeTargetPropertySlot { Name = "Name", TypeName = "String" },
                        SourceName = "Name",
                        ConditionMethodRef = "NCMapper.Generated.GeneratedMapper::Map_When_MwFlDto_Name_abcd1234",
                        ConditionShape = 1,
                    },
                },
            });

            // 先验锚：注册表明细命中（否则分类断言为"未注册"假绿）
            Assert.True(RuntimeSnapshotStore.IsFluentRegisteredDto(typeof(MwFlDto)));

            Assert.True(EfCoreDegradationDetector.NeedsDegradation(typeof(MwFlEntity), typeof(MwFlDto), out var efReason));
            Assert.Contains("MapWhen", efReason); // 分类 "MapWhen"（78 ConditionMethodRef 分支）

            Assert.True(FreeSqlDegradationDetector.NeedsDegradation(typeof(MwFlEntity), typeof(MwFlDto), out var fsReason));
            Assert.Contains("MapWhen", fsReason);
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            EfCoreDegradationDetector.ClearCache();
            FreeSqlDegradationDetector.ClearCache();
        }
    }

    /// <summary>
    /// MapValidator 报告行（P0-1 锚点④）：Fluent MapWhen 对 → Validate 报告含降级行
    /// （RequiresProjectionDowngrade + 分类原因 "MapWhen"——双通道报告面可见性）。
    /// </summary>
    [Fact]
    public void MapValidator_Report_FluentMapWhen_DowngradeRow()
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
            {
                SourceType = typeof(MwFlEntity),
                TargetType = typeof(MwFlDto),
                SourceTypeFqn = typeof(MwFlEntity).FullName!,
                TargetTypeFqn = typeof(MwFlDto).FullName!,
                Channel = RuntimeMappingChannel.Fluent,
                IsFluentForward = true,
                Properties = new[]
                {
                    new RuntimePropertyMapping
                    {
                        Target = new RuntimeTargetPropertySlot { Name = "Name", TypeName = "String" },
                        SourceName = "Name",
                        ConditionMethodRef = "NCMapper.Generated.GeneratedMapper::Map_When_MwFlDto_Name_abcd1234",
                        ConditionShape = 1,
                    },
                },
            });

            var results = MapValidator.Validate(typeof(MwFlEntity).Assembly);
            var row = results.FirstOrDefault(r => r.SourceType == typeof(MwFlEntity) && r.TargetType == typeof(MwFlDto));
            Assert.NotNull(row); // 先验锚：报告行存在
            Assert.True(row!.RequiresProjectionDowngrade);
            Assert.Contains("MapWhen", row.ProjectionDowngradeReason);
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
        }
    }
}
