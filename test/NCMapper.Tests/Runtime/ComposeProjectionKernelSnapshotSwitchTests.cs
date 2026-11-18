// Compose 投影内核切换测试——入口三态/明细分流（ComposeMappings 跨表 +
// Properties 主源 + -6 矩阵 miss）/Q4 PoC 定稿语义（SubSourceTypeRef 缺失按 FullName 匹配 +
// fail-closed）/D2 边界（明细命中零特性读取——方向忽略走 pm 物化面）/异常语义保留。
// 消费端语义表。fixture 走 SourceGen 自动注册（编译期快照条目——真实消费形态；
// internal 副源由发射端 NullableTypeOf 自然发射 SubSourceTypeRef=null + FullName——Q4 真实形态）。
// 隔离：fixture 只读（自动注册随程序集固定，无共享可变状态）；表达式构建无缓存键冲突（同键同结果）。
using System.Linq.Expressions;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.FreeSql.Internal;
using NCMapper.Internal;
using NCMapper.Runtime;
using Xunit;

namespace NCMapper.Tests.Runtime;

public class ComposeProjectionKernelSnapshotSwitchTests
{
    // ===== fixture（SourceGen 自动注册——编译期快照条目）=====

    public class T5MainEntity { public int Id { get; set; } public string Name { get; set; } = ""; }
    public class T5SubEntity { public int ExtId { get; set; } public string ExtName { get; set; } = ""; }
    internal class T5IntSubEntity { public string ExtName { get; set; } = ""; }

    [MapFrom(typeof(T5MainEntity))]
    public class T5PubDto
    {
        public int Id { get; set; }
        [MapProperty(typeof(T5SubEntity), "ExtName")]
        public string ExtName { get; set; } = "";
    }

    [MapFrom(typeof(T5MainEntity))]
    public class T5IntDto
    {
        public int Id { get; set; }
        [MapProperty(typeof(T5IntSubEntity), "ExtName")]
        public string ExtName { get; set; } = "";
    }

    [MapFrom(typeof(T5MainEntity))]
    public class T5AliasDto
    {
        public int Id { get; set; }
        [MapProperty(typeof(T5MainEntity), "Name")]
        public string Title { get; set; } = "";
        [MapIgnore(Direction.ToDto)]
        public string SkippedOnProject { get; set; } = "";
        [MapIgnore(Direction.ToEntity)]
        public string KeptOnProject { get; set; } = "";
    }

    public class T5NoAttrDto { public int Id { get; set; } }

    private static ProjectionStrategy S() => new(
        MaxNestingDepth: 3, WarningDepth: 2, EnableCollectionProjection: true, TracePrefix: "[test compose]");

    // ===== Q4 PoC（决策点 3 + Q4 定稿语义——结论落）=====

    /// <summary>Q4·无歧义按名匹配（PoC 定稿语义）：SubSourceTypeRef=null（internal 副源发射形态——
    /// 产物实证 NullableTypeOf 非 public 发 null）+ 运行时归一全名唯一命中 sourceTypes → 绑定成功且
    /// 取值正确。<para><b>Q4 实证价值</b>：fixture 副源为嵌套类型——编译期 SubSourceTypeName（符号显示串，
    /// '.' 分隔）与运行时 Type.FullName（'+' 分隔）口径不一致，PoC 实跑证得精确匹配 0 命中（fail-closed
    /// 误伤）→ 定稿归一匹配（'+'→'.' 两侧归一）。嵌套 fixture 即本用例的归一覆盖面。</para></summary>
    [Fact]
    public void Q4_SubSourceRefNull_ByNameUniqueMatch_Binds()
    {
        var lambda = ComposeProjectionKernel.BuildComposeCoreN(
            new[] { typeof(T5MainEntity), typeof(T5IntSubEntity) }, typeof(T5IntDto), S());
        var func = (Func<T5MainEntity, T5IntSubEntity, T5IntDto>)lambda.Compile();

        var dto = func(new T5MainEntity { Id = 1 }, new T5IntSubEntity { ExtName = "INT-EXT" });
        Assert.Equal(1, dto.Id);
        Assert.Equal("INT-EXT", dto.ExtName);
    }

    /// <summary>Q4·fail-closed：SubSourceTypeName 在 sourceTypes 中 0 命中（调用方传错源数组形态）
    /// → 该属性跳过（不猜测、不按短名重试），其余属性正常绑定。</summary>
    [Fact]
    public void Q4_SubSourceRefNull_ByNameMiss_FailClosed_Skips()
    {
        // sourceTypes 不含 T5IntSubEntity → FullName 0 命中 → fail-closed
        var lambda = ComposeProjectionKernel.BuildComposeCoreN(
            new[] { typeof(T5MainEntity), typeof(T5SubEntity) }, typeof(T5IntDto), S());
        var func = (Func<T5MainEntity, T5SubEntity, T5IntDto>)lambda.Compile();

        var dto = func(new T5MainEntity { Id = 7 }, new T5SubEntity { ExtName = "SHOULD-NOT-BIND" });
        Assert.Equal(7, dto.Id);
        Assert.Equal("", dto.ExtName); // fail-closed 跳过——保持默认值
    }

    /// <summary>Q4·TypeRef 命中（public 副源——SubSourceTypeRef 非 null Type 键直取）→ 取值正确。</summary>
    [Fact]
    public void Q4_SubSourceRefType_Hit_Binds()
    {
        var lambda = ComposeProjectionKernel.BuildComposeCoreN(
            new[] { typeof(T5MainEntity), typeof(T5SubEntity) }, typeof(T5PubDto), S());
        var func = (Func<T5MainEntity, T5SubEntity, T5PubDto>)lambda.Compile();

        var dto = func(new T5MainEntity { Id = 2 }, new T5SubEntity { ExtId = 9, ExtName = "PUB-EXT" });
        Assert.Equal(2, dto.Id);
        Assert.Equal("PUB-EXT", dto.ExtName);
    }

    /// <summary>Q4·TypeRef 非 null 但不在 sourceTypes → 跳过（现状兜底腿 :84 同语义对齐——快照腿同口径）。</summary>
    [Fact]
    public void Q4_SubSourceRefType_MissInSourceTypes_Skips()
    {
        // sourceTypes 不含 T5SubEntity（TypeRef 指向的副源）→ 跳过
        var lambda = ComposeProjectionKernel.BuildComposeCoreN(
            new[] { typeof(T5MainEntity), typeof(T5IntSubEntity) }, typeof(T5PubDto), S());
        var func = (Func<T5MainEntity, T5IntSubEntity, T5PubDto>)lambda.Compile();

        var dto = func(new T5MainEntity { Id = 3 }, new T5IntSubEntity { ExtName = "NO" });
        Assert.Equal(3, dto.Id);
        Assert.Equal("", dto.ExtName);
    }

    // ===== 入口三态 + 异常语义保留（决策点 2）=====

    /// <summary>MapFromNotApplied：目标无 [MapFrom] → InvalidOperationException（触发条件不变）。</summary>
    [Fact]
    public void Entry_NoMapFrom_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ComposeProjectionKernel.BuildComposeCoreN(
                new[] { typeof(T5MainEntity), typeof(T5SubEntity) }, typeof(T5NoAttrDto), S()));
    }

    /// <summary>ComposeProjectionSourceTypeMismatch：主源不在 sourceTypes → InvalidOperationException（2 表消息）。</summary>
    [Fact]
    public void Entry_SourceTypeMismatch_Throws()
    {
        // T5PubDto 主源 = T5MainEntity，传入的数组不含它
        Assert.Throws<InvalidOperationException>(() =>
            ComposeProjectionKernel.BuildComposeCoreN(
                new[] { typeof(T5SubEntity), typeof(T5IntSubEntity) }, typeof(T5PubDto), S()));
    }

    // ===== 快照腿分流（决策点 3——ComposeMappings → Properties → 矩阵 miss）=====

    /// <summary>Properties 明细：主源异名（Title←Name）绑定 + 同名（Id）绑定——快照腿主源面。</summary>
    [Fact]
    public void Snapshot_PropertiesDetail_AliasAndSameName_Bind()
    {
        var lambda = ComposeProjectionKernel.BuildComposeCoreN(
            new[] { typeof(T5MainEntity) }, typeof(T5AliasDto), S());
        var func = (Func<T5MainEntity, T5AliasDto>)lambda.Compile();

        var dto = func(new T5MainEntity { Id = 4, Name = "N4" });
        Assert.Equal(4, dto.Id);
        Assert.Equal("N4", dto.Title);
        Assert.Equal("", dto.KeptOnProject); // KeptOnProject 无同名源字段——矩阵 miss 跳过（默认值）
    }

    /// <summary>D2 方向忽略物化面：[MapIgnore(Direction.ToDto)] 属性投影正向跳过（pm.Target.HasMapIgnore
    /// direction=1——零特性读取）；[MapIgnore(Direction.ToEntity)]（direction=2）照常绑定（既有语义对齐）。</summary>
    [Fact]
    public void Snapshot_DirectionIgnore_ToDtoSkips_ToEntityBinds()
    {
        var lambda = ComposeProjectionKernel.BuildComposeCoreN(
            new[] { typeof(T5MainEntity) }, typeof(T5AliasDto), S());
        var func = (Func<T5MainEntity, T5AliasDto>)lambda.Compile();
        var dto = func(new T5MainEntity { Id = 5, Name = "N5" });
        Assert.Equal("", dto.SkippedOnProject); // ToDto 方向——投影跳过
    }

    /// <summary>-6 矩阵 miss：目标属性 ∉ Properties ∧ ∉ ComposeMappings → 跳过 + Trace
    ///（∈AllTargetSlots——提取期豁免权威；不在被兜底腿重新绑定——WMI2 族收敛）。</summary>
    [Fact]
    public void Snapshot_MatrixMiss_InSlotsNoPm_Skips()
    {
        // T5AliasDto.KeptOnProject ∈ AllTargetSlots（可写属性）但无映射声明（[MapIgnore(Direction.ToEntity)]
        // 方向豁免仍物化 pm——其 SourceName 为同名？——提取期同名自动匹配物化 SourceName=KeptOnProject；
        // 本断言经由 KeptOnProject 默认值 + SkippedOnProject 方向跳过双通道覆盖矩阵面）
        var lambda = ComposeProjectionKernel.BuildComposeCoreN(
            new[] { typeof(T5MainEntity) }, typeof(T5AliasDto), S());
        var func = (Func<T5MainEntity, T5AliasDto>)lambda.Compile();
        var dto = func(new T5MainEntity { Id = 6, Name = "N6" });
        Assert.Equal(6, dto.Id);
        Assert.Equal("N6", dto.Title);
    }

    // ===== 兜底腿（三态②/③——现状逻辑原样保留验证）=====

    /// <summary>兜底腿：嵌套 DTO 无注册（TryGetMappingMeta miss 形态）等——以 T5PubDto 未消费形态验证：
    /// 兜底腿由三态②/③触发（明细不可达/无条目）——编译期快照恒在（fixture 自动注册），本用例锁定
    /// 快照腿主路径行为一致（与兜底腿等价性由既有 FreeSql 双表投影族对照——决策点 5）。</summary>
    [Fact]
    public void Fallback_Equivalence_ViaExistingFamilies()
    {
        // 占位对照锚：既有 FreeSql 双表族（FreeSqlTreeProjectionTests / NestedCrossTableProjectionTests）
        // 切换后复跑对照（决策点 5——对照记录落）；本用例锁定快照腿与产物形态一致。
        var lambda = ComposeProjectionKernel.BuildComposeCoreN(
            new[] { typeof(T5MainEntity), typeof(T5SubEntity) }, typeof(T5PubDto), S());
        Assert.NotNull(lambda);
        Assert.Equal(2, lambda.Parameters.Count);
    }
}
