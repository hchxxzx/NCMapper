// （G7，B-2 勘误落地）：FreeSql 降级判定改读注册表测试——
//  嵌套 Fluent DTO 索引段经注册表 HasIndex 命中（顶层 Fluent 判定只覆盖顶层目标类型，
//   嵌套面由 HasCollectionIndexPath 递归——原实现仅读特性，嵌套 Fluent 永不命中）。
//   隔离：GlobalConfigState 集合 + 自足注册 + 检测器 ClearCache + try/finally 清理。
using NCMapper.EFCore;
using NCMapper.FreeSql;
using NCMapper.Runtime;
using Xunit;

namespace NCMapper.Tests.Runtime;

[Collection("GlobalConfigState")]
public class IndexPathDegradationTests
{
    // 嵌套 Fluent 对（索引段承载）
    public class IdxSrcEntity { public int Id { get; set; } }
    public class IdxTarget { public int Id { get; set; } public string FirstTitle { get; set; } = ""; }

    // 父对（[MapFrom] 通道——顶层判定不触发，走 RunExtraChecks 递归）
    public class ParentEntity
    {
        public int Id { get; set; }
        public IdxTarget Child { get; set; } = new();
    }

    [NCMapper.Attributes.MapFrom(typeof(ParentEntity))]
    public class ParentTarget
    {
        public int Id { get; set; }
        public IdxTarget Child { get; set; } = new();
    }

    private static void RegisterPairs()
    {
        // 嵌套 Fluent 对：HasIndex 承载（明细段/ Fluent 索引段）
        RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
        {
            SourceType = typeof(IdxSrcEntity),
            TargetType = typeof(IdxTarget),
            SourceTypeFqn = typeof(IdxSrcEntity).FullName!,
            TargetTypeFqn = typeof(IdxTarget).FullName!,
            Channel = RuntimeMappingChannel.Fluent,
            IsFluentForward = true,
            Properties = new[]
            {
                new RuntimePropertyMapping
                {
                    Target = new RuntimeTargetPropertySlot { Name = "FirstTitle", TypeName = "String" },
                    SourceName = "Title",
                    SourcePathNames = new[] { "Items", "Title" },
                    HasIndex = true,
                    SourceIndexes = new[] { 0 },
                },
            },
        });
        // 父对（[MapFrom] 通道——顶层 Fluent 判定不触发；无索引段）
        RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
        {
            SourceType = typeof(ParentEntity),
            TargetType = typeof(ParentTarget),
            SourceTypeFqn = typeof(ParentEntity).FullName!,
            TargetTypeFqn = typeof(ParentTarget).FullName!,
            Channel = RuntimeMappingChannel.MapFrom,
            Properties = new[]
            {
                new RuntimePropertyMapping
                {
                    Target = new RuntimeTargetPropertySlot { Name = "Child", TypeName = "IdxTarget" },
                    SourceName = "Child",
                },
            },
        });
    }

    /// <summary>标准 3：嵌套 Fluent DTO 索引段经注册表 HasIndex 命中 → FreeSql 强制降级
    /// （B-2 勘误——原实现仅读 [MapProperty] 特性，嵌套 Fluent 永不命中）。</summary>
    [Fact]
    public void NestedFluentIndexPath_RegistryRead_ForcesDegrade()
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            RegisterPairs();
            FreeSqlDegradationDetector.ClearCache();
            EfCoreDegradationDetector.ClearCache();

            var needsFs = FreeSqlDegradationDetector.NeedsDegradation(typeof(ParentEntity), typeof(ParentTarget), out var reasonFs);
            Assert.True(needsFs, "嵌套 Fluent 索引段 DTO 须强制降级（FreeSql 无法翻译集合导航索引访问）");
            Assert.Contains("索引", reasonFs);

            // EFCore 侧无集合索引判定（X7 边界）——嵌套面命中不来自 EFCore 判定
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            FreeSqlDegradationDetector.ClearCache();
            EfCoreDegradationDetector.ClearCache();
        }
    }

    /// <summary>反射回退回归（标准 6/否定条件 F6）：注册表未命中时特性通道 FromField '[n]' 判定零变化。</summary>
    [Fact]
    public void ReflectionFallback_FromFieldIndexPath_StillDetected()
    {
        try
        {
            RuntimeSnapshotStore.Clear(); // 空注册表——反射回退腿
            FreeSqlDegradationDetector.ClearCache();

            var needs = FreeSqlDegradationDetector.NeedsDegradation(typeof(VBlogEntity), typeof(VBlogIdxDto), out var reason);
            Assert.True(needs);
            Assert.Contains("索引", reason);
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            FreeSqlDegradationDetector.ClearCache();
        }
    }

    // 特性通道 fixture（[MapProperty(FromField = "Posts[0].Title")]——与既有
    // collectionindexvalidatortests 同形态；本类内声明保证 [MapFrom] 反射回退可读）
    public class VBlogEntity
    {
        public int Id { get; set; }
        public System.Collections.Generic.List<VBlogPost> Posts { get; set; } = new();
    }

    public class VBlogPost
    {
        public string Title { get; set; } = "";
    }

    [NCMapper.Attributes.MapFrom(typeof(VBlogEntity))]
    public class VBlogIdxDto
    {
        public int Id { get; set; }

        [NCMapper.Attributes.MapProperty(typeof(VBlogEntity), "Posts[0].Title")]
        public string FirstTitle { get; set; } = "";
    }
}
