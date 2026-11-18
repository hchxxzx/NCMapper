using NCMapper.Attributes;
using Xunit;

namespace NCMapper.Tests.Tests;

/// <summary>
/// （任务书 20260901-001）运行时解析器规则级单测 — ExpressionBuilder.ResolveStaticMethod
/// <para>覆盖 R2-R7 的运行时镜像：锚定类型 / DeclaredOnly + 可访问性白名单 / 名称 Ordinal /
/// 形态排除 / 四模板 + Q3 优先序 + 相等匹配（不做 assignable 放宽）/ Nullable 解包-提升-降级 /
/// R7 nameof 截断 / R6-b 无匹配源属性 / 反向 [ConvertBack] 基准 / 错误消息族（NCMAP035/036/037 同源文案）。</para>
/// <para>路径说明：与生成端 ConverterMethodResolutionTests 逐条镜像。模型全部为 private 嵌套类，
/// 源生成器跳过（private → NCMAP011 SkipCodeGeneration），Mapper.Map 走运行时 ExpressionBuilder 路径；
/// 正向模型 DTO 标 [MapFrom] 使 targetIsDefine=true（方向守卫通过），反向模型映射 Dto→Entity（目标无 [MapFrom]）。</para>
/// <para>桥接期断言口径：行为级（Map() 产物值 / MapException + 消息片段）——与生成端诊断级断言互补，
/// 全矩阵双引擎等价性随集成矩阵（任务书 5.3 测试归属注）。</para>
/// </summary>
[Collection("GlobalConfigState")]
public class ConverterStaticMethodRuntimeTests : IDisposable
{
    public ConverterStaticMethodRuntimeTests() { Mapper.ClearCache(); GlobalMapConfig.ResetForTests(); }
    public void Dispose() { Mapper.ClearCache(); GlobalMapConfig.ResetForTests(); }

    /// <summary>双语消息片段断言（zh/en 任一命中，避免对运行环境语言的隐式依赖）</summary>
    private static void AssertMessage(MapException ex, string zh, string en)
        => Assert.True(ex.Message.Contains(zh) || ex.Message.Contains(en),
            $"Expected message fragment '{zh}'/'{en}', but got: {ex.Message}");

    // ================================================================
    // 模型（全部 private 嵌套 → 源生成器跳过 → 运行时路径）
    // ================================================================

    private class RsmOrderEntity
    {
        public string Name { get; set; } = "";
        public int Amount { get; set; }
    }

    private static class RsmStringConv
    {
        public static string Upper(string s) => s.ToUpperInvariant();
    }

    // ---------- R2 正例 ----------

    [MapFrom(typeof(RsmOrderEntity))]
    private class RsmDualFormDto
    {
        [Convert(typeof(RsmStringConv), nameof(RsmStringConv.Upper))]
        public string Name { get; set; } = "";
    }

    [MapFrom(typeof(RsmOrderEntity))]
    private class RsmSingleFormDto
    {
        [Convert(nameof(EchoName))] // 单参形态：锚定类型 = DTO 自身
        public string Name { get; set; } = "";

        public static string EchoName(string s) => "[" + s + "]";
    }

    /// <summary>对侧消费：[Convert] 标注于源（实体）属性，单参锚定 = 实体类型</summary>
    private class RsmAnchorOnSourceEntity
    {
        public string Name { get; set; } = "";

        public static string FromEntity(string s) => "ent:" + s;
    }

    [MapFrom(typeof(RsmAnchorOnSourceEntity))]
    private class RsmAnchorOnSourceDto
    {
        public string Name { get; set; } = "";
    }

    // ---------- R2 反例 ----------

    private class RsmConvBase { public static string M(string s) => s; }
    private class RsmConvDerived : RsmConvBase { }

    [MapFrom(typeof(RsmOrderEntity))]
    private class RsmBaseTypeMethodDto
    {
        [Convert(typeof(RsmConvDerived), nameof(RsmConvBase.M))]
        public string Name { get; set; } = "";
    }

    private class RsmGenericConv<T>
    {
        public static string M(T t) => t?.ToString() ?? "";
    }

    [MapFrom(typeof(RsmOrderEntity))]
    private class RsmOpenGenericDto
    {
        [Convert(typeof(RsmGenericConv<>), nameof(RsmGenericConv<string>.M))]
        public string Name { get; set; } = "";
    }

    // ---------- R3 可访问性白名单 ----------

    private class RsmAccConv
    {
        public static string Pub(string s) => "pub:" + s;
        internal static string Int(string s) => "int:" + s;
        protected internal static string ProtInt(string s) => "protint:" + s;
        protected static string Prot(string s) => "prot:" + s;
        private protected static string PrivProt(string s) => "privprot:" + s;
        private static string Priv(string s) => "priv:" + s;
    }

    [MapFrom(typeof(RsmOrderEntity))]
    private class RsmAccPubDto
    {
        [Convert(typeof(RsmAccConv), "Pub")]
        public string Name { get; set; } = "";
    }

    [MapFrom(typeof(RsmOrderEntity))]
    private class RsmAccIntDto
    {
        [Convert(typeof(RsmAccConv), "Int")]
        public string Name { get; set; } = "";
    }

    [MapFrom(typeof(RsmOrderEntity))]
    private class RsmAccProtIntDto
    {
        [Convert(typeof(RsmAccConv), "ProtInt")]
        public string Name { get; set; } = "";
    }

    [MapFrom(typeof(RsmOrderEntity))]
    private class RsmAccProtDto
    {
        [Convert(typeof(RsmAccConv), "Prot")]
        public string Name { get; set; } = "";
    }

    [MapFrom(typeof(RsmOrderEntity))]
    private class RsmAccPrivProtDto
    {
        [Convert(typeof(RsmAccConv), "PrivProt")]
        public string Name { get; set; } = "";
    }

    [MapFrom(typeof(RsmOrderEntity))]
    private class RsmAccPrivDto
    {
        [Convert(typeof(RsmAccConv), "Priv")]
        public string Name { get; set; } = "";
    }

    // ---------- R4 名称 Ordinal ----------

    [MapFrom(typeof(RsmOrderEntity))]
    private class RsmCaseMismatchDto
    {
        [Convert(typeof(RsmStringConv), "upper")] // Ordinal：不匹配 Upper
        public string Name { get; set; } = "";
    }

    // ---------- R5 形态排除 ----------

    private static class RsmGenConv { public static string M<T>(string s) => s; }

    [MapFrom(typeof(RsmOrderEntity))]
    private class RsmGenericMethodDto
    {
        [Convert(typeof(RsmGenConv), nameof(RsmGenConv.M))]
        public string Name { get; set; } = "";
    }

    private static class RsmOptConv { public static string M(string s = "") => s; }

    [MapFrom(typeof(RsmOrderEntity))]
    private class RsmOptionalParamDto
    {
        [Convert(typeof(RsmOptConv), nameof(RsmOptConv.M))]
        public string Name { get; set; } = "";
    }

    private static class RsmParamsConv { public static string M(params string[] xs) => ""; }

    [MapFrom(typeof(RsmOrderEntity))]
    private class RsmParamsDto
    {
        [Convert(typeof(RsmParamsConv), nameof(RsmParamsConv.M))]
        public string Name { get; set; } = "";
    }

    private static class RsmRefConv { public static string M(ref string s) => s; }

    [MapFrom(typeof(RsmOrderEntity))]
    private class RsmRefParamDto
    {
        [Convert(typeof(RsmRefConv), nameof(RsmRefConv.M))]
        public string Name { get; set; } = "";
    }

    private class RsmInstConv { public string M(string s) => s; }

    [MapFrom(typeof(RsmOrderEntity))]
    private class RsmInstanceMethodDto
    {
        [Convert(typeof(RsmInstConv), nameof(RsmInstConv.M))]
        public string Name { get; set; } = "";
    }

    // ---------- R6 四模板（有源属性常规路径） ----------

    private class RsmTagEntity
    {
        public string Tag { get; set; } = "";
    }

    private static class RsmT0Conv { public static string Stamp() => "CONST"; }

    [MapFrom(typeof(RsmTagEntity))]
    private class RsmT0Dto
    {
        [Convert(typeof(RsmT0Conv), nameof(RsmT0Conv.Stamp))]
        public string Tag { get; set; } = "";
    }

    private static class RsmT2Conv
    {
        public static string Describe(RsmOrderEntity s) => s.Name + ":" + s.Amount;
    }

    [MapFrom(typeof(RsmOrderEntity))]
    private class RsmT2Dto
    {
        [Convert(typeof(RsmT2Conv), nameof(RsmT2Conv.Describe))]
        public string Name { get; set; } = "";
    }

    private static class RsmT3Conv
    {
        public static string Describe(RsmOrderEntity s, string name) => name + "|" + s.Name;
    }

    [MapFrom(typeof(RsmOrderEntity))]
    private class RsmT3Dto
    {
        [Convert(typeof(RsmT3Conv), nameof(RsmT3Conv.Describe))]
        public string Name { get; set; } = "";
    }

    // ---------- R6 反例（相等匹配 / 参数数 / 歧义） ----------

    private static class RsmWideConv { public static string M(object o) => o.ToString() ?? ""; }

    [MapFrom(typeof(RsmOrderEntity))]
    private class RsmAssignableWideningDto
    {
        [Convert(typeof(RsmWideConv), nameof(RsmWideConv.M))]
        public string Name { get; set; } = "";
    }

    private static class RsmTriConv
    {
        public static string M(RsmOrderEntity s, string name, int i) => name;
    }

    [MapFrom(typeof(RsmOrderEntity))]
    private class RsmThreeParamsDto
    {
        [Convert(typeof(RsmTriConv), nameof(RsmTriConv.M))]
        public string Name { get; set; } = "";
    }

    private static class RsmAmbConv
    {
        public static string M(string s) => s;
        public static string M(RsmOrderEntity s) => s.Name;
    }

    [MapFrom(typeof(RsmOrderEntity))]
    private class RsmAmbiguousDto
    {
        [Convert(typeof(RsmAmbConv), nameof(RsmAmbConv.M))]
        public string Name { get; set; } = "";
    }

    // ---------- R6 Nullable 口径 ----------

    private class RsmNullableEntity { public int? Count { get; set; } }

    private static class RsmNulConv { public static int? Inc(int v) => v + 1; }

    [MapFrom(typeof(RsmNullableEntity))]
    private class RsmNullableDto
    {
        [Convert(typeof(RsmNulConv), nameof(RsmNulConv.Inc))]
        public int? Count { get; set; }
    }

    private class RsmLiftEntity { public int Amount { get; set; } }

    private static class RsmLiftConv { public static string Desc(int? v) => "amt:" + (v ?? -1); }

    [MapFrom(typeof(RsmLiftEntity))]
    private class RsmLiftDto
    {
        [Convert(typeof(RsmLiftConv), nameof(RsmLiftConv.Desc))]
        public string Amount { get; set; } = "";
    }

    private class RsmDownEntity { public int Count { get; set; } }

    private static class RsmDownConv { public static int? Inc(int v) => v + 1; }

    [MapFrom(typeof(RsmDownEntity))]
    private class RsmDownDto
    {
        [Convert(typeof(RsmDownConv), nameof(RsmDownConv.Inc))]
        public int Count { get; set; }
    }

    // ---------- R7 nameof 截断 ----------

    private static class RsmOtherConv { public static string M(string s) => s; }

    [MapFrom(typeof(RsmOrderEntity))]
    private class RsmNameOfTruncDto
    {
        [Convert(nameof(RsmOtherConv.M))] // 编译产物 "M"，限定名被丢弃 → 锚定 = DTO 自身
        public string Name { get; set; } = "";
    }

    // ---------- R6-b 无匹配源属性 ----------

    private static class RsmNullSrcConv
    {
        public static string M0() => "T0";
        public static string M1(int v) => "T1:" + v;
        public static string M2(RsmOrderEntity s) => "T2:" + s.Name;
        public static string M3(RsmOrderEntity s, int v) => "T3:" + s.Name + ":" + v;
        public static string M9(string a, string b) => a + b;
    }

    [MapFrom(typeof(RsmOrderEntity))]
    private class RsmNullSourceT0Dto
    {
        public string Name { get; set; } = "";
        [Convert(typeof(RsmNullSrcConv), nameof(RsmNullSrcConv.M0))]
        public string Extra { get; set; } = "";
    }

    [MapFrom(typeof(RsmOrderEntity))]
    private class RsmNullSourceT1Dto
    {
        public string Name { get; set; } = "";
        [Convert(typeof(RsmNullSrcConv), nameof(RsmNullSrcConv.M1))]
        public string Extra { get; set; } = "";
    }

    [MapFrom(typeof(RsmOrderEntity))]
    private class RsmNullSourceT2Dto
    {
        public string Name { get; set; } = "";
        [Convert(typeof(RsmNullSrcConv), nameof(RsmNullSrcConv.M2))]
        public string Extra { get; set; } = "";
    }

    [MapFrom(typeof(RsmOrderEntity))]
    private class RsmNullSourceT3Dto
    {
        public string Name { get; set; } = "";
        [Convert(typeof(RsmNullSrcConv), nameof(RsmNullSrcConv.M3))]
        public string Extra { get; set; } = "";
    }

    [MapFrom(typeof(RsmOrderEntity))]
    private class RsmNullSourceBadDto
    {
        public string Name { get; set; } = "";
        [Convert(typeof(RsmNullSrcConv), nameof(RsmNullSrcConv.M9))]
        public string Extra { get; set; } = "";
    }

    // ---------- 反向 [ConvertBack]（基准 004 定稿：TSource=DTO、TField=DTO 属性、TTarget=实体属性） ----------

    private class RsmBackEntity
    {
        public string Name { get; set; } = "";
    }

    private static class RsmBackConv
    {
        public static string Norm(string s) => s.Trim();
    }

    [MapFrom(typeof(RsmBackEntity))]
    private class RsmBackT1Dto
    {
        [ConvertBack(typeof(RsmBackConv), nameof(RsmBackConv.Norm))]
        public string Name { get; set; } = "";
    }

    [MapFrom(typeof(RsmBackEntity))]
    private class RsmBackT2Dto
    {
        [ConvertBack(typeof(RsmBackT2Conv), nameof(RsmBackT2Conv.Join))]
        public string Name { get; set; } = "";
        public int Score { get; set; }
    }

    private static class RsmBackT2Conv
    {
        public static string Join(RsmBackT2Dto dto) => dto.Name + "/" + dto.Score;
    }

    [MapFrom(typeof(RsmBackEntity))]
    private class RsmBackT3Dto
    {
        [ConvertBack(typeof(RsmBackT3Conv), nameof(RsmBackT3Conv.Join))]
        public string Name { get; set; } = "";
    }

    private static class RsmBackT3Conv
    {
        public static string Join(RsmBackT3Dto dto, string name) => name + "|" + dto.Name;
    }

    private static class RsmBackBadConv { public static int Len(string s) => s.Length; }

    [MapFrom(typeof(RsmBackEntity))]
    private class RsmBackReturnMismatchDto
    {
        [ConvertBack(typeof(RsmBackBadConv), nameof(RsmBackBadConv.Len))]
        public string Name { get; set; } = "";
    }

    [MapFrom(typeof(RsmBackEntity))]
    private class RsmBackSingleFormDto
    {
        [ConvertBack("NotOnDto")]
        public string Name { get; set; } = "";
    }

    // ================================================================
    // R2 锚定类型
    // ================================================================

    [Fact]
    public void R2_DualForm_T1_AppliesConversion()
    {
        var dto = Mapper.Map<RsmOrderEntity, RsmDualFormDto>(new RsmOrderEntity { Name = "abc" })!;

        Assert.Equal("ABC", dto.Name);
    }

    [Fact]
    public void R2_SingleForm_MethodOnDtoContainingType_AppliesConversion()
    {
        var dto = Mapper.Map<RsmOrderEntity, RsmSingleFormDto>(new RsmOrderEntity { Name = "abc" })!;

        Assert.Equal("[abc]", dto.Name);
    }

    // R2 源侧 [Convert] 消费锁定用例随源侧腿退役删除（④）

    [Fact]
    public void R2_DualForm_BaseTypeMethod_DeclaredOnly_ThrowsInaccessible()
    {
        var ex = Assert.Throws<MapException>(
            () => Mapper.Map<RsmOrderEntity, RsmBaseTypeMethodDto>(new RsmOrderEntity { Name = "abc" }));

        AssertMessage(ex, "不存在可访问的静态方法", "no accessible static method");
        AssertMessage(ex, "基类继承", "inherited static methods");
    }

    [Fact]
    public void R2_DualForm_OpenGenericAnchor_ThrowsSignatureMismatch()
    {
        var ex = Assert.Throws<MapException>(
            () => Mapper.Map<RsmOrderEntity, RsmOpenGenericDto>(new RsmOrderEntity { Name = "abc" }));

        AssertMessage(ex, "无任何重载匹配签名模板", "matches the signature templates");
        Assert.Contains("open generic type", ex.Message);
    }

    // ================================================================
    // R3 可访问性白名单（6 种 C# 可访问性）
    // ================================================================

    [Fact]
    public void R3_PublicMethod_Allowed()
    {
        var dto = Mapper.Map<RsmOrderEntity, RsmAccPubDto>(new RsmOrderEntity { Name = "abc" })!;

        Assert.Equal("pub:abc", dto.Name);
    }

    [Fact]
    public void R3_InternalMethod_SameAssembly_Allowed()
    {
        var dto = Mapper.Map<RsmOrderEntity, RsmAccIntDto>(new RsmOrderEntity { Name = "abc" })!;

        Assert.Equal("int:abc", dto.Name);
    }

    [Fact]
    public void R3_ProtectedInternalMethod_SameAssembly_Allowed()
    {
        var dto = Mapper.Map<RsmOrderEntity, RsmAccProtIntDto>(new RsmOrderEntity { Name = "abc" })!;

        Assert.Equal("protint:abc", dto.Name);
    }

    [Fact]
    public void R3_ProtectedMethod_Rejected()
    {
        var ex = Assert.Throws<MapException>(
            () => Mapper.Map<RsmOrderEntity, RsmAccProtDto>(new RsmOrderEntity { Name = "abc" }));

        AssertMessage(ex, "不存在可访问的静态方法 'Prot'", "no accessible static method 'Prot'");
    }

    [Fact]
    public void R3_PrivateProtectedMethod_Rejected()
    {
        var ex = Assert.Throws<MapException>(
            () => Mapper.Map<RsmOrderEntity, RsmAccPrivProtDto>(new RsmOrderEntity { Name = "abc" }));

        AssertMessage(ex, "不存在可访问的静态方法 'PrivProt'", "no accessible static method 'PrivProt'");
    }

    [Fact]
    public void R3_PrivateMethod_Rejected()
    {
        var ex = Assert.Throws<MapException>(
            () => Mapper.Map<RsmOrderEntity, RsmAccPrivDto>(new RsmOrderEntity { Name = "abc" }));

        AssertMessage(ex, "不存在可访问的静态方法 'Priv'", "no accessible static method 'Priv'");
    }

    // ================================================================
    // R4 名称匹配（Ordinal）
    // ================================================================

    [Fact]
    public void R4_NameCaseMismatch_Ordinal_ThrowsInaccessible()
    {
        var ex = Assert.Throws<MapException>(
            () => Mapper.Map<RsmOrderEntity, RsmCaseMismatchDto>(new RsmOrderEntity { Name = "abc" }));

        AssertMessage(ex, "不存在可访问的静态方法 'upper'", "no accessible static method 'upper'");
    }

    // ================================================================
    // R5 形态排除（同名静态可达方法被形态排除 → 签名不匹配，细节登记）
    // ================================================================

    [Fact]
    public void R5_GenericMethodOnly_ThrowsSignatureMismatch()
    {
        var ex = Assert.Throws<MapException>(
            () => Mapper.Map<RsmOrderEntity, RsmGenericMethodDto>(new RsmOrderEntity { Name = "abc" }));

        Assert.Contains("generic method", ex.Message);
    }

    [Fact]
    public void R5_OptionalParameterOnly_ThrowsSignatureMismatch()
    {
        var ex = Assert.Throws<MapException>(
            () => Mapper.Map<RsmOrderEntity, RsmOptionalParamDto>(new RsmOrderEntity { Name = "abc" }));

        Assert.Contains("optional parameter", ex.Message);
    }

    [Fact]
    public void R5_ParamsArrayOnly_ThrowsSignatureMismatch()
    {
        var ex = Assert.Throws<MapException>(
            () => Mapper.Map<RsmOrderEntity, RsmParamsDto>(new RsmOrderEntity { Name = "abc" }));

        Assert.Contains("params array", ex.Message);
    }

    [Fact]
    public void R5_RefParameterOnly_ThrowsSignatureMismatch()
    {
        var ex = Assert.Throws<MapException>(
            () => Mapper.Map<RsmOrderEntity, RsmRefParamDto>(new RsmOrderEntity { Name = "abc" }));

        Assert.Contains("ref/out/in parameter", ex.Message);
    }

    [Fact]
    public void R5_InstanceMethodOnly_ThrowsInaccessible()
    {
        var ex = Assert.Throws<MapException>(
            () => Mapper.Map<RsmOrderEntity, RsmInstanceMethodDto>(new RsmOrderEntity { Name = "abc" }));

        AssertMessage(ex, "不存在可访问的静态方法 'M'", "no accessible static method 'M'");
    }

    // ================================================================
    // R6 四模板（有源属性常规路径）
    // ================================================================

    [Fact]
    public void R6_T0_NoArgConstant_IgnoresFieldValue()
    {
        var dto = Mapper.Map<RsmTagEntity, RsmT0Dto>(new RsmTagEntity { Tag = "src" })!;

        Assert.Equal("CONST", dto.Tag);
    }

    [Fact]
    public void R6_T1_SingleValue_AppliesConversion()
    {
        var dto = Mapper.Map<RsmOrderEntity, RsmDualFormDto>(new RsmOrderEntity { Name = "abc" })!;

        Assert.Equal("ABC", dto.Name);
    }

    [Fact]
    public void R6_T2_MultiField_ReceivesSourceEntity()
    {
        var dto = Mapper.Map<RsmOrderEntity, RsmT2Dto>(new RsmOrderEntity { Name = "abc", Amount = 5 })!;

        Assert.Equal("abc:5", dto.Name);
    }

    [Fact]
    public void R6_T3_Combined_ReceivesEntityAndField()
    {
        var dto = Mapper.Map<RsmOrderEntity, RsmT3Dto>(new RsmOrderEntity { Name = "abc" })!;

        Assert.Equal("abc|abc", dto.Name);
    }

    [Fact]
    public void R6_AssignableWidening_Rejected_EqualityMatchOnly()
    {
        var ex = Assert.Throws<MapException>(
            () => Mapper.Map<RsmOrderEntity, RsmAssignableWideningDto>(new RsmOrderEntity { Name = "abc" }));

        AssertMessage(ex, "无任何重载匹配签名模板", "matches the signature templates");
    }

    [Fact]
    public void R6_ThreeParams_Rejected()
    {
        var ex = Assert.Throws<MapException>(
            () => Mapper.Map<RsmOrderEntity, RsmThreeParamsDto>(new RsmOrderEntity { Name = "abc" }));

        AssertMessage(ex, "无任何重载匹配签名模板", "matches the signature templates");
    }

    [Fact]
    public void R6_OverloadAmbiguity_ThrowsAmbiguous()
    {
        var ex = Assert.Throws<MapException>(
            () => Mapper.Map<RsmOrderEntity, RsmAmbiguousDto>(new RsmOrderEntity { Name = "abc" }));

        AssertMessage(ex, "多个重载同时命中签名模板", "multiple overloads");
    }

    // ================================================================
    // R6 Nullable 口径（解包守卫 / 提升 / 降级拒绝）
    // ================================================================

    [Fact]
    public void R6_NullableUnwrap_NullField_ShortCircuitsToDefault()
    {
        var dto = Mapper.Map<RsmNullableEntity, RsmNullableDto>(new RsmNullableEntity { Count = null })!;

        Assert.Null(dto.Count);
    }

    [Fact]
    public void R6_NullableUnwrap_ValueField_CallsWithValue()
    {
        var dto = Mapper.Map<RsmNullableEntity, RsmNullableDto>(new RsmNullableEntity { Count = 5 })!;

        Assert.Equal(6, dto.Count);
    }

    [Fact]
    public void R6_NullableLift_NonNullFieldToNullableParam_CallsWithLifted()
    {
        var dto = Mapper.Map<RsmLiftEntity, RsmLiftDto>(new RsmLiftEntity { Amount = 7 })!;

        Assert.Equal("amt:7", dto.Amount);
    }

    [Fact]
    public void R6_NullableDowngrade_Rejected()
    {
        var ex = Assert.Throws<MapException>(
            () => Mapper.Map<RsmDownEntity, RsmDownDto>(new RsmDownEntity { Count = 5 }));

        AssertMessage(ex, "无任何重载匹配签名模板", "matches the signature templates");
    }

    // ================================================================
    // R7 nameof 限定名截断
    // ================================================================

    [Fact]
    public void R7_SingleFormNameOfOtherType_Truncated_ThrowsInaccessible()
    {
        var ex = Assert.Throws<MapException>(
            () => Mapper.Map<RsmOrderEntity, RsmNameOfTruncDto>(new RsmOrderEntity { Name = "abc" }));

        // 锚定类型 = DTO 自身（限定名 OtherConv 被编译器丢弃），其上无 M → 显性报错（不静默错配）
        AssertMessage(ex, "不存在可访问的静态方法 'M'", "no accessible static method 'M'");
        AssertMessage(ex, "正向", "forward"); // 消息族方向上下文（正向）
    }

    // ================================================================
    // R6-b 无匹配源属性（fieldType=null：仅形态匹配，字段实参 default）
    // ================================================================

    [Fact]
    public void R6b_T0_NoArg_CalledAsConstant()
    {
        var dto = Mapper.Map<RsmOrderEntity, RsmNullSourceT0Dto>(new RsmOrderEntity { Name = "abc" })!;

        Assert.Equal("T0", dto.Extra);
    }

    [Fact]
    public void R6b_T1_AnySingleParam_CalledWithDefault()
    {
        var dto = Mapper.Map<RsmOrderEntity, RsmNullSourceT1Dto>(new RsmOrderEntity { Name = "abc" })!;

        Assert.Equal("T1:0", dto.Extra); // M1(default(int)) = "T1:0"
    }

    [Fact]
    public void R6b_T2_EntityParam_ReceivesSourceEntity()
    {
        var dto = Mapper.Map<RsmOrderEntity, RsmNullSourceT2Dto>(new RsmOrderEntity { Name = "abc" })!;

        Assert.Equal("T2:abc", dto.Extra);
    }

    [Fact]
    public void R6b_T3_Combined_ReceivesEntityAndDefault()
    {
        var dto = Mapper.Map<RsmOrderEntity, RsmNullSourceT3Dto>(new RsmOrderEntity { Name = "abc" })!;

        Assert.Equal("T3:abc:0", dto.Extra); // M3(src, default(int))
    }

    [Fact]
    public void R6b_TwoParamsNotEntity_Rejected()
    {
        var ex = Assert.Throws<MapException>(
            () => Mapper.Map<RsmOrderEntity, RsmNullSourceBadDto>(new RsmOrderEntity { Name = "abc" }));

        AssertMessage(ex, "无任何重载匹配签名模板", "matches the signature templates");
    }

    // ================================================================
    // 反向 [ConvertBack]（TSource=DTO、TField=DTO 属性、TTarget=实体属性）
    // ================================================================

    [Fact]
    public void ConvertBackUsing_T1_AppliesConversion()
    {
        var entity = Mapper.Map<RsmBackT1Dto, RsmBackEntity>(new RsmBackT1Dto { Name = "  x  " })!;

        Assert.Equal("x", entity.Name);
    }

    [Fact]
    public void ConvertBackUsing_T2_ReceivesDtoEntity()
    {
        var entity = Mapper.Map<RsmBackT2Dto, RsmBackEntity>(new RsmBackT2Dto { Name = "abc", Score = 9 })!;

        Assert.Equal("abc/9", entity.Name);
    }

    [Fact]
    public void ConvertBackUsing_T3_ReceivesDtoAndField()
    {
        var entity = Mapper.Map<RsmBackT3Dto, RsmBackEntity>(new RsmBackT3Dto { Name = "abc" })!;

        Assert.Equal("abc|abc", entity.Name);
    }

    [Fact]
    public void ConvertBackUsing_ReturnMismatch_ThrowsSignatureMismatch()
    {
        var ex = Assert.Throws<MapException>(
            () => Mapper.Map<RsmBackReturnMismatchDto, RsmBackEntity>(new RsmBackReturnMismatchDto { Name = "abc" }));

        AssertMessage(ex, "无任何重载匹配签名模板", "matches the signature templates");
    }

    [Fact]
    public void ConvertBackUsing_SingleForm_MethodNotOnDto_ThrowsInaccessible()
    {
        var ex = Assert.Throws<MapException>(
            () => Mapper.Map<RsmBackSingleFormDto, RsmBackEntity>(new RsmBackSingleFormDto { Name = "abc" }));

        AssertMessage(ex, "不存在可访问的静态方法 'NotOnDto'", "no accessible static method 'NotOnDto'");
        AssertMessage(ex, "反向", "reverse"); // 消息族方向上下文（反向）
    }
}
