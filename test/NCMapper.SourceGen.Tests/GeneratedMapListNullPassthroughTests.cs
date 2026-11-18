using NCMapper.SourceGen.Tests.Models;
using NCMapper.Generated;
using Xunit;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// 生成 MapList 方法 A' 语义守护测试
/// <para>方案 A' 裁决（D3）：生成方法签名由 List&lt;T&gt;? 改为非空 List&lt;T&gt;，
/// null 入参 return null! —— 签名层消除 CS8621/CS8634，运行时保持 null 透传，行为零变化。</para>
/// <para>本测试三组用例守护：</para>
/// <list type="number">
/// <item>直接调用生成 MapList：null 源返回 null、空源返回空集合、正常源逐项一致（null 透传守护）</item>
/// <item>嵌套集合属性：源集合属性 null → 目标属性保持默认（不赋空集合）——覆盖 :633 调用点真实影响面</item>
/// <item>简单赋值路径 null 透传对照（语义一致性守护）</item>
/// </list>
/// <para>反向验证：若将生成代码 return null! 误写为 return new List&lt;T&gt;()，
/// 用例 MapList_NullSource_Returns_Null 与 NestedCollection_NullSourceProperty_KeepsDefault 应失败。</para>
/// </summary>
public class GeneratedMapListNullPassthroughTests
{
    // ================================================================
    // 用例①：直接调用生成 MapList — null 透传守护（A' 签名契约）
    // ================================================================

    /// <summary>
    /// 生成 MapList 方法 null 源返回 null — A' null 透传守护
    /// <para>签名声明非空仅为满足 RegisterGeneratedList 委托契约；
    /// 运行时 null 入参必须返回 null（而非空集合），否则触发 W1 行为变更。</para>
    /// </summary>
    [Fact]
    public void MapList_NullSource_Returns_Null()
    {
        var result = GeneratedMapper.MapList_SgCollectionItemEntity_To_SgCollectionItemDto(null!);

        Assert.Null(result);
    }

    /// <summary>
    /// 生成 MapList 方法空源返回空集合 — 非 null 语义正常路径
    /// </summary>
    [Fact]
    public void MapList_EmptySource_Returns_Empty()
    {
        var result = GeneratedMapper.MapList_SgCollectionItemEntity_To_SgCollectionItemDto(
            new List<SgCollectionItemEntity>());

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    /// <summary>
    /// 生成 MapList 方法正常源逐项一致 — 映射正确性
    /// </summary>
    [Fact]
    public void MapList_NormalSource_Items_Match()
    {
        var sources = new List<SgCollectionItemEntity>
        {
            new() { Id = 10, Name = "Item10" },
            new() { Id = 20, Name = "Item20" }
        };

        var result = GeneratedMapper.MapList_SgCollectionItemEntity_To_SgCollectionItemDto(sources);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Count);
        Assert.Equal(10, result[0].Id);
        Assert.Equal("Item10", result[0].Name);
        Assert.Equal(20, result[1].Id);
        Assert.Equal("Item20", result[1].Name);
    }

    // ================================================================
    // 用例②：嵌套集合属性 — 源集合属性 null → 目标保持默认（633 调用点真实影响面）
    // ================================================================

    /// <summary>
    /// 嵌套集合属性 null → 目标属性保持默认（不赋空集合）— W1 防线核心用例
    /// <para>实体映射 Map_SgCollectionEntity_To_SgCollectionDto 内部 :633 调用点
    /// 发射 var _list_Items = GeneratedMapper.MapList_...(src.Items)，
    /// 其后五处判空（638/:640/:645/:651/:658）依赖 null 透传：
    /// src.Items 为 null → MapList 返回 null → 判空不赋值 → dto.Items 保持 null。</para>
    /// <para>反向验证：若 MapList 误返回空集合，此断言失败（行为变更信号）。</para>
    /// </summary>
    [Fact]
    public void NestedCollection_NullSourceProperty_KeepsDefault()
    {
        var entity = new SgCollectionEntity { Id = 2, Name = "NoItems", Items = null };

        var dto = GeneratedMapper.Map_SgCollectionEntity_To_SgCollectionDto(entity);

        Assert.Equal(2, dto!.Id);
        Assert.Equal("NoItems", dto.Name);
        Assert.Null(dto.Items);
    }

    /// <summary>
    /// 嵌套集合属性非 null → 目标属性映射正确（对照组）
    /// </summary>
    [Fact]
    public void NestedCollection_NonNullSourceProperty_Maps()
    {
        var entity = new SgCollectionEntity
        {
            Id = 1,
            Name = "WithItems",
            Items = new List<SgCollectionItemEntity>
            {
                new() { Id = 10, Name = "Item10" },
                new() { Id = 20, Name = "Item20" }
            }
        };

        var dto = GeneratedMapper.Map_SgCollectionEntity_To_SgCollectionDto(entity);

        Assert.NotNull(dto!.Items);
        Assert.Equal(2, dto.Items!.Count);
        Assert.Equal(10, dto.Items[0].Id);
        Assert.Equal("Item20", dto.Items[1].Name);
    }

    // ================================================================
    // 用例③：简单赋值路径 null 透传对照（语义一致性守护）
    // ================================================================

    /// <summary>
    /// 简单赋值路径 null 透传对照 — 与生成 MapList 的 null 透传语义一致
    /// <para>同类型属性（string → string）走简单赋值路径 dst.Name = src.Name（null 直传），
    /// 生成 MapList 的 null 透传（return null!）与该路径语义必须一致，
    /// 否则两条路径语义分裂（方案 W1 关注点）。</para>
    /// </summary>
    [Fact]
    public void SimpleAssignment_NullPassthrough()
    {
        var entity = new SgFlatEntity { Id = 7, Name = null! };

        var dto = GeneratedMapper.Map_SgFlatEntity_To_SgFlatDto(entity);

        Assert.Equal(7, dto!.Id);
        Assert.Null(dto.Name);
    }

    // ================================================================
    // 用例④：生成 DeepCopy 方法 null 透传守护（同构修复：DeepCopy 签名 A'）
    // ================================================================

    /// <summary>
    /// 生成 DeepCopy 方法 null 源返回 null — DeepCopy 签名 A' 同构守护
    /// <para> 连带发现：DeepCopy 生成签名 {typeName}? 与
    /// RegisterGeneratedDeepCopy 非空委托契约（Func&lt;T, DeepCopyContext, T&gt）不匹配，
    /// 产生 CS8621。同 MapList 采用 A' 修复：签名非空 + null 透传。</para>
    /// <para>运行时入口 Mapper.DeepCopy 在层 1 委托调用前拦截 null（source == null 直接返回），
    /// 注册通道永不传 null；此处直接调用验证 null 透传语义保持。</para>
    /// </summary>
    [Fact]
    public void DeepCopy_NullSource_Returns_Null()
    {
        var result = GeneratedMapper.DeepCopy_SgFlatEntity(null!, new NCMapper.DeepCopyContext());

        Assert.Null(result);
    }

    /// <summary>
    /// 生成 DeepCopy 方法正常源深拷贝正确 — 非 null 语义正常路径
    /// </summary>
    [Fact]
    public void DeepCopy_NormalSource_Copies()
    {
        var entity = new SgFlatEntity { Id = 9, Name = "Copy" };

        var result = GeneratedMapper.DeepCopy_SgFlatEntity(entity, new NCMapper.DeepCopyContext());

        Assert.NotNull(result);
        Assert.NotSame(entity, result);
        Assert.Equal(9, result!.Id);
        Assert.Equal("Copy", result.Name);
    }

    // ================================================================
    // C23 补测：Update 路径目标集合 null — 不 NRE 保持语义
    // ================================================================

    /// <summary>
    /// Update 路径目标集合 null — 不 NRE 保持语义（C23 补测）
    /// <para>报告 §五 C23 断言形态：Update 目标集合 null → 不 NRE 保持语义。
    /// 专项1 F-07：Update 路径集合 null 透传守护。生成 Update 方法带 null 守卫
    /// （src/dst null 直接 return），目标 DTO 集合属性为 null 时逐属性赋值不应 NRE，
    /// 源集合映射结果正常写入目标。</para>
    /// </summary>
    [Fact]
    public void Update_NullTargetCollection_NoNRE_SemanticsKept()
    {
        var src = new SgCollectionEntity
        {
            Id = 5,
            Name = "Updated",
            Items = new List<SgCollectionItemEntity> { new() { Id = 1, Name = "A" } }
        };

        // 目标集合属性初始为 null（Update 语义：更新到既有目标对象）
        var dst = new SgCollectionDto { Id = 0, Name = "", Items = null };

        GeneratedMapper.Map_SgCollectionEntity_To_SgCollectionDto_Update(src, dst, null);

        // 不 NRE；常规属性已更新
        Assert.Equal(5, dst.Id);
        Assert.Equal("Updated", dst.Name);
        // 集合语义保持：源集合映射结果写入目标（生成代码集合赋值路径）
        Assert.NotNull(dst.Items);
        Assert.Single(dst.Items!);
        Assert.Equal("A", dst.Items![0].Name);
    }
}
