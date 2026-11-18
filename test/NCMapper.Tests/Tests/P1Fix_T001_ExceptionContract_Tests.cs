// ============================================================================
// P1Fix_T001 — 批次 2，异常契约
// 统一验收用例。
//
// 缺陷承载（006 审计 20260827-005）：
// 可空值类型对构建期裸抛（ConvertStrategy.BuildNullableConditionalAssignment
//        :61 + MapperBase.TryBuildSingleBinding :606-607 孪生点）—— DateTime?→long 无
//        coercion operator，Expression.Convert 构建期裸抛 InvalidOperationException；
// Mask 源值类型构建期裸抛（assignmentplanner :121-127 不看源类型 +
//        ExpressionBuilder.BuildMaskValue :1096-1098 非 string 源无条件 Convert）——
//        int 源 + [Mask] string 目标 → Convert(int, string) 裸抛；
// 索引段值类型 null 链裸抛（ExpressionBuilder :548/:586 Constant(null, 值类型段)
//        抛 ArgumentException）；
// 同型复合键字典误入列表管线裸抛（CollectionStrategy :99-109 isSimpleElement
//        判定 → GetListExpression GetGenericArguments()[0]=TKey → Expression.Call
//        IEnumerable<TSource> 参数不匹配 → ArgumentException）。
//
// 修复后期望（异常契约统一表）：
//   DateTime?→long              → MapConfigurationException（含类型上下文）
//   [Mask] int 源               → 不再构建期裸抛（落常规转换路径 / 渲染侧 ToString 兜底）
//  索引段 struct 元素 null → 正常构建（ChainAccess 已内联集合 null 守卫，评审 002 定案）
//   Dictionary<Item,Item> 同型  → 走字典路径正常映射（或 MapConfigurationException，非裸抛）
// ============================================================================

using NCMapper.Attributes;
using Xunit;

namespace NCMapper.Tests.Tests;

public class P1Fix_T001_ExceptionContract
{

    /// <summary>
    /// MemberInit 路径（TryBuildSingleBinding :606-607 孪生点）：
    /// DateTime?→long 不可转换对，修复前 Convert(DateTime, long) 构建期裸抛
    /// InvalidOperationException；修复后抛 MapConfigurationException 且异常携带
    /// 源/目标类型上下文。
    /// </summary>
    [Fact]
    public void S16_DateTimeNullable转long_MemberInit路径_抛MapConfigurationException含类型上下文()
    {
        var ex = Assert.Throws<MapConfigurationException>(() =>
            Mapper.Map<S16MiSrc, S16MiDto>(new S16MiSrc { Value = DateTime.UnixEpoch }));

        Assert.Equal(typeof(DateTime?), ex.SourceType);
        Assert.Equal(typeof(long), ex.TargetType);
    }

    /// <summary>
    /// Block 路径（ConvertStrategy.BuildNullableConditionalAssignment :61）：
    /// 追加集合属性令整条映射 shouldFallback → Block 引擎渲染，同一不可转换对
    /// 抛 MapConfigurationException（含类型上下文）——与 MemberInit 路径形态一致。
    /// </summary>
    [Fact]
    public void S16_DateTimeNullable转long_Block路径_抛MapConfigurationException含类型上下文()
    {
        var ex = Assert.Throws<MapConfigurationException>(() =>
            Mapper.Map<S16BlkSrc, S16BlkDto>(new S16BlkSrc { Value = DateTime.UnixEpoch }));

        Assert.Equal(typeof(DateTime?), ex.SourceType);
        Assert.Equal(typeof(long), ex.TargetType);
    }

    /// <summary>
    /// 反幻觉锚点：int?→long 可转换对现有行为不变（MemberInit 路径）——
    /// HasValue 时拓宽转换，null 时回落目标默认值 0。
    /// </summary>
    [Fact]
    public void S16_IntNullable转long_MemberInit路径_现有行为不变()
    {
        var dto1 = Mapper.Map<S16IntMiSrc, S16IntMiDto>(new S16IntMiSrc { Value = 5 });
        Assert.Equal(5L, dto1!.Value);

        var dto2 = Mapper.Map<S16IntMiSrc, S16IntMiDto>(new S16IntMiSrc { Value = null });
        Assert.Equal(0L, dto2!.Value);
    }

    /// <summary>
    /// 反幻觉锚点：int?→long 可转换对现有行为不变（Block 路径）。
    /// </summary>
    [Fact]
    public void S16_IntNullable转long_Block路径_现有行为不变()
    {
        var dto1 = Mapper.Map<S16IntBlkSrc, S16IntBlkDto>(new S16IntBlkSrc { Value = 5 });
        Assert.Equal(5L, dto1!.Value);

        var dto2 = Mapper.Map<S16IntBlkSrc, S16IntBlkDto>(new S16IntBlkSrc { Value = null });
        Assert.Equal(0L, dto2!.Value);
    }


    /// <summary>
    /// int 源 + [Mask] string 目标——修复前渲染层 Convert(int, string) 构建期
    /// 裸抛 InvalidOperationException；修复后不再裸抛，落常规类型转换路径
    ///（int→string），映射成功。
    /// </summary>
    [Fact]
    public void S17_Int源Mask目标String_不再构建期裸抛_按常规转换映射()
    {
        var dto = Mapper.Map<S17Src, S17Dto>(new S17Src { Phone = 138000012 })!;

        Assert.Equal("138000012", dto.Phone);
    }

    /// <summary>
    /// 反幻觉锚点：string 源 + [Mask] 行为不变——脱敏正常生效。
    /// </summary>
    [Fact]
    public void S17_String源Mask目标String_行为不变脱敏生效()
    {
        var dto = Mapper.Map<S17StrSrc, S17StrDto>(new S17StrSrc { Phone = "13812345678" })!;

        Assert.NotEqual("13812345678", dto.Phone);
        Assert.Contains("***", dto.Phone);
    }


    /// <summary>
    /// struct 元素索引路径（Rows[0].Id）——修复前 null 安全链
    /// Constant(null, S18Row) 构建期裸抛 ArgumentException；修复后正常构建
    /// （评审 002 定案：ChainAccess 已内联集合 null 守卫，索引段值类型元素
    /// 以正常构建为预期结果）。
    /// </summary>
    [Fact]
    public void S18_Struct元素索引路径_正常构建不裸抛()
    {
        var src = new S18Src
        {
            Rows = new List<S18Row> { new() { Id = 42 } },
        };

        var dto = Mapper.Map<S18Src, S18Dto>(src)!;

        Assert.Equal(42, dto.FirstId);
    }

    /// <summary>
    /// 边界：索引段集合为 null 时回落目标默认值（ChainAccess 内联守卫语义）。
    /// </summary>
    [Fact]
    public void S18_Struct元素索引路径_集合null_回落默认值()
    {
        var dto = Mapper.Map<S18Src, S18Dto>(new S18Src { Rows = null! })!;

        Assert.Equal(0, dto.FirstId);
    }


    /// <summary>
    /// Dictionary&lt;Item,Item&gt; 同型复合键字典——修复前误入
    /// BuildSameTypeCollectionAssignment 列表管线（GetGenericArguments()[0]=TKey 当
    /// 元素类型），Expression.Call(IEnumerable&lt;TSource&gt;) 参数不匹配构建期裸抛
    /// ArgumentException；修复后分流字典路径（GetDictionaryExpression），正常映射。
    /// </summary>
    [Fact]
    public void S19_同型复合键字典_ItemItem_正常映射不裸抛()
    {
        var key = new S19Item { Name = "key-a" };
        var val = new S19Item { Name = "val-a" };
        var src = new S19Src { Map = new Dictionary<S19Item, S19Item> { [key] = val } };

        var dto = Mapper.Map<S19Src, S19Dto>(src)!;

        Assert.NotNull(dto.Map);
        Assert.Single(dto.Map);
        var entry = Assert.Single(dto.Map);
        Assert.Equal("key-a", entry.Key.Name);
        Assert.Equal("val-a", entry.Value.Name);
    }

    // ============================== 模型 ==============================

    // ----- 模型 -----

    private sealed class S16MiSrc
    {
        public DateTime? Value { get; set; }
    }

    /// <summary>MemberInit 路径 DTO：仅简单属性（DateTime?→long）。</summary>
    private sealed class S16MiDto
    {
        public long Value { get; set; }
    }

    /// <summary>反幻觉锚点模型：int?→long（MemberInit 路径）。</summary>
    private sealed class S16IntMiSrc
    {
        public int? Value { get; set; }
    }

    private sealed class S16IntMiDto
    {
        public long Value { get; set; }
    }

    /// <summary>Block 路径源：集合属性先声明（先于 Value 触发 shouldFallback → 整条回退 Block 渲染）。</summary>
    private sealed class S16BlkSrc
    {
        public List<int> Tags { get; set; } = new();
        public DateTime? Value { get; set; }
    }

    private sealed class S16BlkDto
    {
        public List<int> Tags { get; set; } = new();
        public long Value { get; set; }
    }

    /// <summary>反幻觉锚点模型：int?→long（Block 路径，集合属性触发 shouldFallback）。</summary>
    private sealed class S16IntBlkSrc
    {
        public int? Value { get; set; }
        public List<int> Tags { get; set; } = new();
    }

    private sealed class S16IntBlkDto
    {
        public long Value { get; set; }
        public List<int> Tags { get; set; } = new();
    }

    // ----- 模型 -----

    private sealed class S17Src
    {
        public int Phone { get; set; }
    }

    [MapFrom(typeof(S17Src))] // T5 复核：Plan 直调输入的承载侧合法形态

    private sealed class S17Dto
    {
        [Mask(typeof(MaskPhone))]
        public string Phone { get; set; } = "";
    }

    private sealed class S17StrSrc
    {
        public string Phone { get; set; } = "";
    }

    [MapFrom(typeof(S17StrSrc))] // T5 复核：Plan 直调输入的承载侧合法形态

    private sealed class S17StrDto
    {
        [Mask(typeof(MaskPhone))]
        public string Phone { get; set; } = "";
    }

    // ----- 模型 -----

    private struct S18Row
    {
        public int Id { get; set; }
    }

    private sealed class S18Src
    {
        public List<S18Row> Rows { get; set; } = new();
    }

    [MapFrom(typeof(S18Src))]
    private sealed class S18Dto
    {
        [MapProperty(typeof(S18Src), "Rows[0].Id")]
        public int FirstId { get; set; }
    }

    // ----- 模型 -----

    private sealed class S19Item
    {
        public string Name { get; set; } = "";
    }

    private sealed class S19Src
    {
        public Dictionary<S19Item, S19Item> Map { get; set; } = new();
    }

    private sealed class S19Dto
    {
        public Dictionary<S19Item, S19Item> Map { get; set; } = new();
    }
}