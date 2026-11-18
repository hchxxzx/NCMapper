using NCMapper;
using NCMapper.Attributes;
using NCMapper.Generated;
using Xunit;

namespace NCMapper.Tests.Tests;

// ================================================================
// 模型（public 顶层 → 源生成器生成映射；Rt 系列无 [MapFrom] → 运行时路径）
// ================================================================

/// <summary>
/// 矩阵/封闭泛型转换器 — M(TField) 开放泛型声明，经 typeof(Conv&lt;int&gt;) 封闭锚定（T1 模板）
/// </summary>
public static class CsmMGenConv<T>
{
    /// <summary>
    /// 将字段值加前缀（前缀标识封闭泛型实例被消费）
    /// </summary>
    /// <param name="fieldValue">字段值</param>
    /// <returns>带 "GEN:" 前缀的字段值</returns>
    public static string Convert(T fieldValue) => "GEN:" + fieldValue;
}

/// <summary>
/// 矩阵/封闭泛型源实体
/// </summary>
public class CsmMGenSource
{
    /// <summary>编码</summary>
    public int Code { get; set; } = 7;
}

/// <summary>
/// 矩阵/封闭泛型生成侧 DTO — typeof(CsmMGenConv&lt;int&gt;) + nameof 封闭泛型成员
/// </summary>
[MapFrom(typeof(CsmMGenSource))]
public class CsmMGenDto
{
    /// <summary>编码（经封闭泛型 T1 转换）</summary>
    [Convert(typeof(CsmMGenConv<int>), nameof(CsmMGenConv<int>.Convert))]
    public string Code { get; set; } = "";
}

/// <summary>
/// 矩阵/partial 源实体
/// </summary>
public class CsmMParSource
{
    /// <summary>备注</summary>
    public string Note { get; set; } = "n1";
}

/// <summary>
/// 矩阵/partial 生成侧 DTO（第一部分：特性标注；静态方法在另一 partial 部分）
/// </summary>
[MapFrom(typeof(CsmMParSource))]
public partial class CsmMParGenDto
{
    /// <summary>备注（单参形态锚定 DTO 自身，方法声明于第二 partial 部分）</summary>
    [Convert(nameof(Mark))]
    public string Note { get; set; } = "";
}

/// <summary>
/// 矩阵/partial 生成侧 DTO（第二部分：静态转换方法）
/// </summary>
public partial class CsmMParGenDto
{
    /// <summary>
    /// 加前缀标识 partial 合并符号被解析
    /// </summary>
    /// <param name="fieldValue">字段值</param>
    /// <returns>带 "P:" 前缀的字段值</returns>
    public static string Mark(string fieldValue) => "P:" + fieldValue;
}

/// <summary>
/// 矩阵/字段 null 传递转换器 — 引用类型 null 照常调用
/// </summary>
public static class CsmMNullConv
{
    /// <summary>
    /// null 入参返回哨兵值（标识 M(null) 被真实调用而非短路）
    /// </summary>
    /// <param name="fieldValue">字段值（可为 null）</param>
    /// <returns>null → "SEEN_NULL"；非空 → "v:" 前缀</returns>
    public static string Convert(string fieldValue) => fieldValue is null ? "SEEN_NULL" : "v:" + fieldValue;
}

/// <summary>
/// 矩阵/字段 null 传递源实体
/// </summary>
public class CsmMNullSource
{
    /// <summary>备注（可为 null 的引用类型字段）</summary>
    public string? Note { get; set; }
}

/// <summary>
/// 矩阵/字段 null 传递生成侧 DTO
/// </summary>
[MapFrom(typeof(CsmMNullSource))]
public class CsmMNullGenDto
{
    /// <summary>备注（引用类型 null → M(null) 照常调用）</summary>
    [Convert(typeof(CsmMNullConv), nameof(CsmMNullConv.Convert))]
    public string Note { get; set; } = "";
}

/// <summary>
/// 矩阵/Q3 单方法边界源实体 — Child 字段类型 == 源实体类型（自引用）
/// </summary>
public class CsmMQ3Node
{
    /// <summary>源实体标签（T2 判定下转换器读取此处）</summary>
    public string Label { get; set; } = "SRC";

    /// <summary>子节点（字段类型 == 源实体类型，Q3 边界成立条件）</summary>
    public CsmMQ3Node? Child { get; set; }
}

/// <summary>
/// 矩阵/Q3 单方法边界转换器 — 单参 == 源实体类型 == 字段类型 → Q3 优先序判 T2（收到源实体）
/// </summary>
public static class CsmMQ3Conv
{
    /// <summary>
    /// 读取源实体标签（T2 多字段形态）
    /// </summary>
    /// <param name="source">源实体对象</param>
    /// <returns>带 "T2:" 前缀的源实体标签</returns>
    public static string Convert(CsmMQ3Node source) => "T2:" + source.Label;
}

/// <summary>
/// 矩阵/Q3 单方法边界生成侧 DTO
/// </summary>
[MapFrom(typeof(CsmMQ3Node))]
public class CsmMQ3GenDto
{
    /// <summary>子节点摘要（同名匹配源属性 Child；单方法双命中 → T2 多字段）</summary>
    [Convert(typeof(CsmMQ3Conv), nameof(CsmMQ3Conv.Convert))]
    public string Child { get; set; } = "";
}

/// <summary>
/// 矩阵/反向 T0 源实体
/// </summary>
public class CsmMT0Entity
{
    /// <summary>标签（反向写回消费无参常量模板）</summary>
    public string Tag { get; set; } = "";
}

/// <summary>
/// 矩阵/反向 T0 转换器 — 无参常量模板 M()
/// </summary>
public static class CsmMT0Conv
{
    /// <summary>
    /// 返回常量（标识无参模板被消费）
    /// </summary>
    /// <returns>常量 "CONST"</returns>
    public static string Convert() => "CONST";
}

/// <summary>
/// 矩阵/反向 T0 DTO — [ConvertBack] 无参常量模板
/// </summary>
[MapFrom(typeof(CsmMT0Entity))]
public class CsmMT0Dto
{
    /// <summary>标签（反向经无参常量转换器写回实体）</summary>
    [ConvertBack(typeof(CsmMT0Conv), nameof(CsmMT0Conv.Convert))]
    public string Tag { get; set; } = "";
}

/// <summary>
/// 集成/全矩阵双引擎等价性测试（任务书 20260901-001，补齐项）
/// <para>规则级单测已在落地（ConverterMethodResolutionTests / ConverterStaticMethodRuntimeTests），
/// 本类补齐集成矩阵剩余五项：封闭泛型 / partial / 引用类型字段 null 传递 / Q3 单方法边界 / 反向 T0。</para>
/// <para>双引擎模式：生成侧 = public 顶层 DTO 标 [MapFrom]（源生成器生成并自动注册）；
/// 运行时侧 = private 嵌套 DTO 标 [MapFrom]（源生成器跳过 private → NCMAP011，Mapper.Map 走
/// 运行时 ExpressionBuilder 路径；标 [MapFrom] 使方向守卫 targetIsDefine 通过，转换器方可消费）。</para>
/// <para>反向 T0 用例经构造/Dispose 取消并恢复自动注册（模式）使 Mapper.Map 走运行时路径，
/// 生成侧直接调用 GeneratedMapper 反向方法做差分。</para>
/// </summary>
public class ConverterStaticMethodMatrixTests : IDisposable
{
    // ================================================================
    // 运行时模型（private 嵌套 → 源生成器跳过 → 运行时路径；标 [MapFrom] 使方向守卫通过）
    // ================================================================

    /// <summary>运行时/封闭泛型 DTO</summary>
    [MapFrom(typeof(CsmMGenSource))]
    private class CsmMGenRtDto
    {
        /// <summary>编码（经封闭泛型 T1 转换）</summary>
        [Convert(typeof(CsmMGenConv<int>), nameof(CsmMGenConv<int>.Convert))]
        public string Code { get; set; } = "";
    }

    /// <summary>运行时/partial DTO（第一部分：特性标注）</summary>
    [MapFrom(typeof(CsmMParSource))]
    private partial class CsmMParRtDto
    {
        /// <summary>备注（单参形态锚定 DTO 自身，方法声明于第二 partial 部分）</summary>
        [Convert(nameof(Mark))]
        public string Note { get; set; } = "";
    }

    /// <summary>运行时/partial DTO（第二部分：静态转换方法）</summary>
    private partial class CsmMParRtDto
    {
        /// <summary>
        /// 加前缀标识 partial 合并符号被解析
        /// </summary>
        /// <param name="fieldValue">字段值</param>
        /// <returns>带 "P:" 前缀的字段值</returns>
        public static string Mark(string fieldValue) => "P:" + fieldValue;
    }

    /// <summary>运行时/字段 null 传递 DTO</summary>
    [MapFrom(typeof(CsmMNullSource))]
    private class CsmMNullRtDto
    {
        /// <summary>备注（引用类型 null → M(null) 照常调用）</summary>
        [Convert(typeof(CsmMNullConv), nameof(CsmMNullConv.Convert))]
        public string Note { get; set; } = "";
    }

    /// <summary>运行时/Q3 单方法边界 DTO</summary>
    [MapFrom(typeof(CsmMQ3Node))]
    private class CsmMQ3RtDto
    {
        /// <summary>子节点摘要（同名匹配源属性 Child；单方法双命中 → T2 多字段）</summary>
        [Convert(typeof(CsmMQ3Conv), nameof(CsmMQ3Conv.Convert))]
        public string Child { get; set; } = "";
    }

    /// <summary>
    /// 取消 CsmMT0 类型对的自动注册，使 Mapper.Map 反向走运行时路径
    /// </summary>
    public ConverterStaticMethodMatrixTests()
    {
        Mapper.RegisterGenerated<CsmMT0Dto, CsmMT0Entity>(null!);
        Mapper.RegisterGeneratedCore<CsmMT0Dto, CsmMT0Entity>(null!);
    }

    /// <summary>
    /// 恢复 CsmMT0 类型对的自动注册（防止污染其他测试）
    /// </summary>
    public void Dispose()
    {
        Mapper.RegisterGeneratedCore<CsmMT0Dto, CsmMT0Entity>(GeneratedMapper.Map_CsmMT0Dto_To_CsmMT0Entity);
        Mapper.RegisterGenerated<CsmMT0Dto, CsmMT0Entity>(GeneratedMapper.Map_CsmMT0Dto_To_CsmMT0Entity_WithAction);
    }

    /// <summary>
    /// 矩阵/封闭泛型：typeof(Conv&lt;int&gt;) + nameof(Conv&lt;int&gt;.M) 双参形态 → 双引擎 T1 值一致
    /// <para>生成侧发射 global::…Conv&lt;int&gt;.M(字段值)（FQN 含泛型实参，编译期由测试程序集构建守护）。</para>
    /// </summary>
    [Fact]
    public void ClosedGeneric_T1_DualEngine_ValueConsistent()
    {
        var src = new CsmMGenSource { Code = 7 };

        var gen = Mapper.Map<CsmMGenSource, CsmMGenDto>(src)!;
        Assert.Equal("GEN:7", gen.Code);

        var rt = Mapper.Map<CsmMGenSource, CsmMGenRtDto>(src)!;
        Assert.Equal("GEN:7", rt.Code);

        Assert.Equal(gen.Code, rt.Code);
    }

    /// <summary>
    /// 矩阵/partial：方法与特性标注分属两个 partial 部分（单参形态）→ 双引擎合并符号解析值一致
    /// </summary>
    [Fact]
    public void PartialDeclaration_SingleForm_DualEngine_ValueConsistent()
    {
        var src = new CsmMParSource { Note = "n1" };

        var gen = Mapper.Map<CsmMParSource, CsmMParGenDto>(src)!;
        Assert.Equal("P:n1", gen.Note);

        var rt = Mapper.Map<CsmMParSource, CsmMParRtDto>(src)!;
        Assert.Equal("P:n1", rt.Note);

        Assert.Equal(gen.Note, rt.Note);
    }

    /// <summary>
    /// 矩阵/字段 null 传递：引用类型字段 null → M(null) 照常调用（Nullable 值类型 null 短路已由
    /// R6_NullableUnwrap_NullField_ShortCircuitsToDefault 覆盖）→ 双引擎值一致
    /// </summary>
    [Fact]
    public void ReferenceTypeNullField_ConverterCalledWithNull_DualEngine_Consistent()
    {
        var src = new CsmMNullSource { Note = null };

        var gen = Mapper.Map<CsmMNullSource, CsmMNullGenDto>(src)!;
        Assert.Equal("SEEN_NULL", gen.Note);

        var rt = Mapper.Map<CsmMNullSource, CsmMNullRtDto>(src)!;
        Assert.Equal("SEEN_NULL", rt.Note);

        Assert.Equal(gen.Note, rt.Note);
    }

    /// <summary>
    /// 矩阵/Q3 单方法边界：单参同时==源实体类型==字段类型（自引用 Child）→ 双引擎均判 T2 多字段
    /// <para>可观测差分：T2 收源实体（Label="SRC"）；若误判 T1 则收字段值（Child.Label="CHILD"）。</para>
    /// </summary>
    [Fact]
    public void Q3_SingleMethod_ParamEqualsEntityAndFieldType_JudgedT2_DualEngine_Consistent()
    {
        var src = new CsmMQ3Node { Label = "SRC", Child = new CsmMQ3Node { Label = "CHILD" } };

        var gen = Mapper.Map<CsmMQ3Node, CsmMQ3GenDto>(src)!;
        Assert.Equal("T2:SRC", gen.Child);

        var rt = Mapper.Map<CsmMQ3Node, CsmMQ3RtDto>(src)!;
        Assert.Equal("T2:SRC", rt.Child);

        Assert.Equal(gen.Child, rt.Child);
    }

    /// <summary>
    /// 矩阵/反向 T0：[ConvertBack] 无参常量模板 M() → 反向写回常量，双引擎值一致
    /// </summary>
    [Fact]
    public void ConvertBackUsing_T0_NoArgConstant_Reverse_DualEngine_ValueConsistent()
    {
        var dto = new CsmMT0Dto { Tag = "ignored" };

        var runtime = Mapper.Map<CsmMT0Dto, CsmMT0Entity>(dto)!;
        Assert.Equal("CONST", runtime.Tag);

        var generated = GeneratedMapper.Map_CsmMT0Dto_To_CsmMT0Entity(dto)!;
        Assert.Equal("CONST", generated.Tag);

        Assert.Equal(runtime.Tag, generated.Tag);
    }
}
