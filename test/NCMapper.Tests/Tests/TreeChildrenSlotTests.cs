using System.Linq.Expressions;
using NCMapper.Attributes;
using NCMapper.Internal;
using Xunit;

namespace NCMapper.Tests.Tests;

/// <summary>
/// 树挂载位契约单元测试（A4）
/// <para>覆盖：[MapTree] 命中 ChildrenProperty 的自引用集合不绑定投影（挂载位跳过，TreeBuilder 填充）；
/// 自定义 ChildrenProperty 名命中；普通 DTO（无 [MapTree]）投影行为与基线一致（F3 回归锁定——
/// 失活 IsTreeChildrenSlot 判定时用例 1/2 必红，普通 DTO 用例 3/4 恒绿证伪误伤）</para>
/// </summary>
public class TreeChildrenSlotTests
{
    /// <summary>
    /// 测试策略（对齐 EFCore 投影策略形态：集合投影开启——树挂载位跳过分支可达）
    /// </summary>
    private static readonly ProjectionStrategy EfCoreLikeStrategy = new(
        MaxNestingDepth: 8, WarningDepth: 5, EnableCollectionProjection: true, TracePrefix: "[test slot]");

    // ===== 测试模型（源实体带自引用集合——嵌套投影分支可达，保证跳过可证伪）=====

    private sealed class FlatTreeEntity
    {
        public int Id { get; set; }
        public int? ParentId { get; set; }
        public List<FlatTreeEntity> Children { get; set; } = new();
        /// <summary>自定义挂载位名同源属性（保证 CustomSlot 用例可证伪——无挂载位跳过时会绑定）</summary>
        public List<FlatTreeEntity> Nodes { get; set; } = new();
    }

    [MapFrom(typeof(FlatTreeEntity))]
    [MapTree("Id", "ParentId", "Children")]
    private sealed class TreeSlotDto
    {
        public int Id { get; set; }
        public int? ParentId { get; set; }
        public List<TreeSlotDto> Children { get; set; } = new();
    }

    [MapFrom(typeof(FlatTreeEntity))]
    [MapTree("Id", "ParentId", "Nodes")]
    private sealed class CustomSlotDto
    {
        public int Id { get; set; }
        public int? ParentId { get; set; }
        public List<CustomSlotDto> Nodes { get; set; } = new();
    }

    /// <summary>普通 DTO（无 [MapTree]）——同名自引用集合照常嵌套投影（基线行为）</summary>
    [MapFrom(typeof(FlatTreeEntity))]
    private sealed class PlainDto
    {
        public int Id { get; set; }
        public int? ParentId { get; set; }
        public List<PlainDto> Children { get; set; } = new();
    }

    /// <summary>非自引用集合 DTO（[MapTree] 标注但元素类型 != 自身）——不构成挂载位，照常投影</summary>
    [MapFrom(typeof(FlatTreeEntity))]
    [MapTree("Id", "ParentId", "Children")]
    private sealed class NonSelfRefDto
    {
        public int Id { get; set; }
        public int? ParentId { get; set; }
        public List<PlainDto> Children { get; set; } = new();
    }

    // ===== 断言辅助 =====

    /// <summary>
    /// 构建投影表达式并提取 MemberInit 绑定成员名列表
    /// </summary>
    private static List<string> BoundMemberNames<TSource, TTarget>()
        where TSource : class
        where TTarget : class
    {
        var expr = ProjectionExpressionFactory.Build<TSource, TTarget>(EfCoreLikeStrategy);
        var init = (MemberInitExpression)expr.Body;
        return init.Bindings.Select(b => b.Member.Name).ToList();
    }

    // ===== 用例 =====

    /// <summary>[MapTree] 默认名：Children 不在投影绑定中（树挂载位跳过），其余属性照常</summary>
    [Fact]
    public void MapTreeDto_DefaultName_ChildrenExcludedFromBindings()
    {
        var bound = BoundMemberNames<FlatTreeEntity, TreeSlotDto>();

        Assert.DoesNotContain("Children", bound);
        Assert.Contains("Id", bound);
        Assert.Contains("ParentId", bound);
    }

    /// <summary>[MapTree] 自定义名：Nodes（ChildrenProperty 命中）不在投影绑定中</summary>
    [Fact]
    public void MapTreeDto_CustomChildrenProperty_SlotExcluded()
    {
        var bound = BoundMemberNames<FlatTreeEntity, CustomSlotDto>();

        Assert.DoesNotContain("Nodes", bound);
        Assert.Contains("Id", bound);
        Assert.Contains("ParentId", bound);
    }

    /// <summary>普通 DTO（无 [MapTree]）：同名自引用集合照常绑定（嵌套投影，F3 回归锁定）</summary>
    [Fact]
    public void PlainDto_WithoutMapTree_ChildrenStillBound()
    {
        var bound = BoundMemberNames<FlatTreeEntity, PlainDto>();

        Assert.Contains("Children", bound);
    }

    /// <summary>普通 DTO 绑定全集与基线一致（Id/ParentId/Children——零多余/零缺失）</summary>
    [Fact]
    public void PlainDto_BindingSet_BaselineUnchanged()
    {
        var bound = BoundMemberNames<FlatTreeEntity, PlainDto>();

        Assert.Equal(new[] { "Children", "Id", "ParentId" }, bound.OrderBy(n => n).ToArray());
    }

    /// <summary>非自引用集合（[MapTree] 标注但元素 != 自身）：不构成挂载位，Children 照常投影</summary>
    [Fact]
    public void MapTreeDto_NonSelfRefCollection_ChildrenStillBound()
    {
        var bound = BoundMemberNames<FlatTreeEntity, NonSelfRefDto>();

        Assert.Contains("Children", bound);
    }
}
