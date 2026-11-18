// 补充（验收 #1/#2 缺口闭合）：
// §A 混合场景合并臂（Register 同对双条目 → MergeMetadata HasSubclassDispatch || 合并——
//    F5 模式防线；变异 M2 检出方：删合并臂 → MergeMetadata_PreservesSubclassDispatch_AcrossIdentityAndDetailEntries 红）
// §B 投影降级分类（ClassifyUntranslatableDeclaration → "SubclassDispatch"——
//    变异 M3 检出方：删分类 → ClassifyUntranslatableDeclaration_SubclassDispatch_WhenRegistered 红）
// 隔离（#76 追加型豁免）：MsX fixture 类型唯一不与他类碰撞，Register 自足对全库状态零破坏；
// [Collection("GlobalConfigState")] 防并行交错。
using System;
using NCMapper.Runtime;
using Xunit;

namespace NCMapper.Tests.Tests;

// MsX fixture（合并臂测试注册键——仅元数据面使用，无映射行为）
public class MsXEntity { }
public class MsXDto { }

[Collection("GlobalConfigState")]
public class MapSubclassRuntimeMetadataTests
{
    /// <summary>混合场景合并臂（验收 #1）：身份条目（[MapFrom] 形态，HasSubclassDispatch=false）
    /// + 明细条目（Fluent 形态，HasSubclassDispatch=true）同对注册 → 合并产物保留 true
    ///（F5 模式防线：漏加合并臂 = 混合场景声明静默丢失——本测试红）。</summary>
    [Fact]
    public void MergeMetadata_PreservesSubclassDispatch_AcrossIdentityAndDetailEntries()
    {
        // 身份条目（先注册——ModuleInitializer 先身份后明细形态）
        RuntimeSnapshotStore.Register(MakeMeta(hasSubclassDispatch: false, withProperties: false));
        // 明细条目（HasSubclassDispatch=true——Fluent .MapSubclass 生成形态）
        RuntimeSnapshotStore.Register(MakeMeta(hasSubclassDispatch: true, withProperties: true));

        var merged = RuntimeSnapshotStore.FindByPair(typeof(MsXEntity), typeof(MsXDto));
        Assert.NotNull(merged);
        Assert.True(merged!.HasSubclassDispatch,
            "合并产物丢失 HasSubclassDispatch——MergeMetadata 合并臂缺失（F5 模式：混合场景声明静默丢失）");
    }

    /// <summary>合并臂反向序（先明细后身份——R-B 顺序无关）：身份条目后到不覆盖明细 true。</summary>
    [Fact]
    public void MergeMetadata_ReverseOrder_StillPreserves()
    {
        RuntimeSnapshotStore.Register(MakeMeta(hasSubclassDispatch: true, withProperties: true));
        RuntimeSnapshotStore.Register(MakeMeta(hasSubclassDispatch: false, withProperties: false));

        var merged = RuntimeSnapshotStore.FindByPair(typeof(MsXEntity), typeof(MsXDto));
        Assert.NotNull(merged);
        Assert.True(merged!.HasSubclassDispatch); // 明细优先——身份不覆盖
    }

    /// <summary>降级分类（验收 #2）：HasSubclassDispatch=true → "SubclassDispatch"
    ///（isinst 分派链不可投影，恒降级——分类名 + 消费语义断言）。</summary>
    [Fact]
    public void ClassifyUntranslatableDeclaration_SubclassDispatch_WhenRegistered()
    {
        var meta = MakeMeta(hasSubclassDispatch: true, withProperties: true);
        var classification = NCMapper.Internal.DegradationDetectorBase.ClassifyUntranslatableDeclaration(meta);
        Assert.Equal("SubclassDispatch", classification);
    }

    /// <summary>降级分类对照（G3 语义）：无声明 → null（可翻译——不误降级）。</summary>
    [Fact]
    public void ClassifyUntranslatableDeclaration_Null_WhenNotRegistered()
    {
        var meta = MakeMeta(hasSubclassDispatch: false, withProperties: true);
        var classification = NCMapper.Internal.DegradationDetectorBase.ClassifyUntranslatableDeclaration(meta);
        Assert.Null(classification);
    }

    private static RuntimeMapperMetadata MakeMeta(bool hasSubclassDispatch, bool withProperties) => new()
    {
        SourceType = typeof(MsXEntity),
        TargetType = typeof(MsXDto),
        SourceTypeFqn = typeof(MsXEntity).FullName!,
        TargetTypeFqn = typeof(MsXDto).FullName!,
        Channel = RuntimeMappingChannel.Fluent,
        HasSubclassDispatch = hasSubclassDispatch,
        Properties = withProperties
            ? new[] { new RuntimePropertyMapping { Target = new RuntimeTargetPropertySlot { Name = "Any" }, SourceName = "Any" } }
            : Array.Empty<RuntimePropertyMapping>(),
    };
}
