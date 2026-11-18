// ============================================================================
// P1Fix_T004 — 批次 2，Q1 string 元素 cross-type /
// 构参指针·ByRef 形态崩溃修复验收用例。
//
// 归因（D1 复现实证）：MapperBase.BuildConstructorArgs（752-784）与
// DeepCopyCompiler.TryCompileRecordCreator（281-284）对"未按名匹配参数"统一
// Default(paramType) —— paramType 为 ByRef（ref/out）或指针形态时
// Expression.Default/New 裸抛 ArgumentException（构建期裸抛，Q1 崩溃形态）；
// string 元素 cross-type 语义已由 B1 兜底（同型恒等直传 / 跨类型抛
// MapConfigurationException）。
//
// 修复后期望（D2）：ByRef/指针参数构造器标记不可用（多构造器可回退其他），
// 全部不可用时抛 MapConfigurationException（含类型上下文，异常契约统一）。
// ============================================================================

using Xunit;

namespace NCMapper.Tests.Tests;

public class P1Fix_T004_Q1_CtorParamForm
{
    /// <summary>
    /// Q1 A1/A2：构造器含 ByRef 参数（ref int state）且无匹配源属性——
    /// 修复前 Expression.Default(int&amp;) 构建期裸抛 ArgumentException；
    /// 修复后抛 MapConfigurationException（含类型上下文）。
    /// </summary>
    [Fact]
    public void Q1_ByRef构参_无匹配属性_抛MapConfigurationException非裸抛()
    {
        var ex = Assert.Throws<MapConfigurationException>(() =>
            Mapper.Map<Q1Src, Q1RefCtorDto>(new Q1Src { Name = "a" }));

        Assert.NotNull(ex.SourceType);
    }

    /// <summary>
    /// Q1 A2（深拷贝侧）：同形态构造器深拷贝不再裸抛 ArgumentException。
    /// </summary>
    [Fact]
    public void Q1_ByRef构参_深拷贝侧_不裸抛ArgumentException()
    {
        var st = 0;
        var ex = Assert.ThrowsAny<Exception>(() => Mapper.DeepCopy(new Q1RefCtorDeepItem(ref st)));
        Assert.IsNotType<ArgumentException>(ex);
    }

    /// <summary>
    /// Q1 A3：string 元素 cross-type 语义明确——string→string 恒等直传
    ///（构造参数与源属性同为 string 集合形态，同型等值拷贝）。
    /// </summary>
    [Fact]
    public void Q1_String元素同型_恒等直传()
    {
        var src = new Q1StrSrc { Tags = new List<string> { "x", "y" } };
        var dto = Mapper.Map<Q1StrSrc, Q1StrDto>(src);

        Assert.Equal(new[] { "x", "y" }, dto!.Tags);
    }

    /// <summary>
    /// Q1 A3：string 元素 cross-type（源 int 集合 → 构造参数 string 集合）
    /// 显式异常（B1 契约：类型不兼容抛 MapConfigurationException，不静默不裸崩）。
    /// </summary>
    [Fact]
    public void Q1_String元素跨型_构造参数不兼容_抛MapConfigurationException()
    {
        Assert.Throws<MapConfigurationException>(() =>
            Mapper.Map<Q1IntSrc, Q1StrCtorDto>(new Q1IntSrc { Tags = new List<int> { 1, 2 } }));
    }

    /// <summary>
    /// Q1 边界：多构造器场景——ByRef 构造器不可用时回退可用构造器（无参构造优先路径不受影响）。
    /// </summary>
    [Fact]
    public void Q1_多构造器_ByRef不可用_回退其他构造器()
    {
        var dto = Mapper.Map<Q1Src, Q1MultiCtorDto>(new Q1Src { Name = "multi" });

        Assert.Equal("multi", dto!.Name);
    }

    // ============================== 模型 ==============================

    public sealed class Q1Src
    {
        public string Name { get; set; } = "";
    }

    /// <summary>仅含 ByRef 构造参数的 DTO（无无参构造 → 走 BuildConstructorArgs）。</summary>
    public sealed class Q1RefCtorDto
    {
        public string Name { get; set; } = "";

        public Q1RefCtorDto(ref int state)
        {
            Name = state.ToString();
        }
    }

    public sealed class Q1RefCtorDeepItem
    {
        public int Value { get; set; }

        public Q1RefCtorDeepItem(ref int state)
        {
            Value = state;
        }
    }

    public sealed class Q1StrSrc
    {
        public List<string> Tags { get; set; } = new();
    }

    public sealed class Q1StrDto
    {
        public List<string> Tags { get; set; } = new();
    }

    public sealed class Q1IntSrc
    {
        public List<int> Tags { get; set; } = new();
    }

    /// <summary>构造参数 string 集合与源 int 集合 cross-type（B1 不兼容契约承载）。</summary>
    public sealed class Q1StrCtorDto
    {
        public Q1StrCtorDto(IEnumerable<string> tags)
        {
        }
    }

    public sealed class Q1MultiCtorDto
    {
        public string Name { get; set; } = "";

        public Q1MultiCtorDto(ref int state)
        {
            Name = state.ToString();
        }

        public Q1MultiCtorDto(string name)
        {
            Name = name;
        }
    }
}