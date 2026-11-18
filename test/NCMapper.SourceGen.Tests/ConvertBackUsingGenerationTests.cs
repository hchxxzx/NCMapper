using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// 定向测试（生成列）— [ConvertBack] 反向消费生成形态
/// <para>覆盖验收 1（直接属性反向消费）、2（写回路径 + N-1）、4（NCMAP008 错配）、
/// 5（双特性并列方向隔离）、6（NCMAP033 互斥）、7（NCMAP019 扫描登记点识别）。
/// 运行时值行为见 NCMapper.Tests.ConvertBackUsingRuntimeTests（本类锁定生成形态，
/// 运行时列以真实执行实证同一语义，验收 1/2/5 运行时部分 + 验收 3 B-1 快捷路径回退）。</para>
/// </summary>
public class ConvertBackUsingGenerationTests
{
    /// <summary>
    /// 断言反向扩展 6 重载齐备（MapToXxx 4 个 + MapToXxxList 2 个）
    /// </summary>
    /// <param name="result">生成结果</param>
    /// <param name="methodName">扩展方法名（MapToXxxEntity）</param>
    private static void AssertSixReverseOverloads(MapperGenerationResult result, string methodName)
    {
        Assert.True(result.Methods.TryGetValue(methodName, out var overloads),
            $"反向扩展 '{methodName}' 未生成。已生成方法: [{string.Join(", ", result.Methods.Keys)}]");
        Assert.Equal(4, overloads.Count);
        Assert.True(result.Methods.TryGetValue(methodName + "List", out var listOverloads),
            $"反向列表扩展 '{methodName}List' 未生成");
        Assert.Equal(2, listOverloads.Count);
    }

    /// <summary>
    /// 验收 1（生成列）：直接属性反向消费 [ConvertBack]（source=DTO），正向不消费
    /// <para>DTO.Tag 标 [ConvertBack(typeof(CbuBackConverter), nameof(CbuBackConverter.Convert))]，实体 Tag 同类型 string。
    /// 反向方法 Map_CbuDto_To_CbuEntity 应生成 `Tag = global::CbuBackConverter.Convert(src.Tag)`（T1 模板，src=DTO 对象）；
    /// 正向方法 Map_CbuEntity_To_CbuDto 同类型直赋、无转换器调用。</para>
    /// </summary>
    [Fact]
    public void DirectProperty_ReverseConsumesConverter_ForwardDoesNot()
    {
        var source = """
            using NCMapper.Attributes;

            public class CbuEntity { public string Tag { get; set; } = ""; }

            public static class CbuBackConverter
            {
                public static string Convert(string fieldValue) => "back:" + fieldValue;
            }

            [MapFrom(typeof(CbuEntity))]
            public class CbuDto
            {
                [ConvertBack(typeof(CbuBackConverter), nameof(CbuBackConverter.Convert))]
                public string Tag { get; set; } = "";
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 反向 6 重载照常生成（特性不阻断反向扩展）
        AssertSixReverseOverloads(result, "MapToCbuEntity");

        // 反向核心方法消费转换器（source=DTO：src 参数即 DTO 对象；T1 内联静态调用单实参）
        result.Should().HaveMethodBodyContaining("Map_CbuDto_To_CbuEntity", "Tag = global::CbuBackConverter.Convert(src.Tag)");

        // 正向核心方法不消费（同类型直赋，无转换器调用）
        var fwdBody = result.Methods["Map_CbuEntity_To_CbuDto"][0].Body;
        Assert.DoesNotContain(".Convert(", fwdBody);

        // 验收 6（互斥）：仅标 [ConvertBack] 不报 NCMAP033（NCMAP033 仅对"标 ConvertUsing 未标 ConvertBackUsing"发射）
        Assert.False(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP033"),
            "仅标 [ConvertBack] 不应报 NCMAP033");
    }

    /// <summary>
    /// 验收 2（生成列）：写回路径反向消费 + N-1 源 null 守卫
    /// <para>DTO.City 标 [MapProperty(typeof(CbuWEntity), "Address.City")]（扁平化写回链）+ [ConvertBack]。
    /// 反向写回方法应生成 `dst.Address.City = global::CbuWBackConverter.Convert(src.City)`（T1 模板），
    /// 且转换器调用位于 `if (src.City != null)` 守卫写分支内（源 null 走清空分支不调用，N-1 语义）。</para>
    /// </summary>
    [Fact]
    public void WriteBackPath_ReverseConsumesConverter_WithNullGuard()
    {
        var source = """
            using NCMapper.Attributes;

            public class CbuWAddress { public string City { get; set; } = ""; }
            public class CbuWEntity { public CbuWAddress Address { get; set; } = new(); }

            public static class CbuWBackConverter
            {
                public static string Convert(string fieldValue) => "city:" + fieldValue;
            }

            [MapFrom(typeof(CbuWEntity))]
            public class CbuWDto
            {
                [MapProperty(typeof(CbuWEntity), "Address.City")]
                [ConvertBack(typeof(CbuWBackConverter), nameof(CbuWBackConverter.Convert))]
                public string City { get; set; } = "";
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        var reverseBody = result.Methods["Map_CbuWDto_To_CbuWEntity"][0].Body;
        // 写回链叶赋值消费转换器（T1 内联静态调用；赋值形态 dst.Address.City = ...）
        Assert.Contains("dst.Address.City = global::CbuWBackConverter.Convert(src.City)", reverseBody);
        // N-1 源 null 守卫：转换器调用仅存在于源非 null 写分支
        Assert.Contains("if (src.City != null)", reverseBody);
    }

    /// <summary>
    /// 验收 4（反例）：[ConvertBack] 方法 source 参数类型≠DTO 报 NCMAP037
    /// <para>CbuWrongConverter.Convert(CbuOtherEntity)（T2 形态 source 参数），
    /// 反向校验基准 TSource=DTO CbuDto ≠ CbuOtherEntity → 方法签名不匹配诊断。
    /// （起旧接口形态 NCMAP008 随接口退役；方法引用形态的等价失配落点为 NCMAP037。）</para>
    /// </summary>
    [Fact]
    public void TSourceMismatch_EmitsNCMAP037()
    {
        var source = """
            using NCMapper.Attributes;

            public class CbuEntity { public string Tag { get; set; } = ""; }
            public class CbuOtherEntity { public string Tag { get; set; } = ""; }

            public static class CbuWrongConverter
            {
                public static string Convert(CbuOtherEntity source) => source.Tag;
            }

            [MapFrom(typeof(CbuEntity))]
            public class CbuDto
            {
                [ConvertBack(typeof(CbuWrongConverter), nameof(CbuWrongConverter.Convert))]
                public string Tag { get; set; } = "";
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        Assert.True(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP037"),
            "TSource≠DTO 的 [ConvertBack] 应报 NCMAP037（方法签名不匹配）");
    }

    /// <summary>
    /// 验收 5（生成列）：同属性双特性（[Convert] + [ConvertBack]）各自生效、方向隔离
    /// <para>正向消费 CbuFwdConverter.Convert（T1，TField=DTO Tag），反向消费 CbuBackConverter.Convert（T1，TField=DTO Tag）。
    /// 方向隔离：正向方法体调用 global::CbuFwdConverter.Convert(src.Tag)，反向方法体调用 global::CbuBackConverter.Convert(src.Tag)——
    /// 同属性双特性互不串扰；转换器与调用方向一一对应。</para>
    /// </summary>
    [Fact]
    public void DualAttribute_ForwardAndReverse_UseRespectiveConverters()
    {
        var source = """
            using NCMapper.Attributes;

            public class CbuEntity { public string Tag { get; set; } = ""; }

            public static class CbuFwdConverter
            {
                public static string Convert(string fieldValue) => "fwd:" + fieldValue;
            }

            public static class CbuBackConverter
            {
                public static string Convert(string fieldValue) => "back:" + fieldValue;
            }

            [MapFrom(typeof(CbuEntity))]
            public class CbuDto
            {
                [Convert(typeof(CbuFwdConverter), nameof(CbuFwdConverter.Convert))]
                [ConvertBack(typeof(CbuBackConverter), nameof(CbuBackConverter.Convert))]
                public string Tag { get; set; } = "";
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 不再生成 __conv 单例字段；方向隔离由各自方法体的内联静态调用承载
        Assert.DoesNotContain("__conv_", result.GeneratedCode);

        // 正向方法消费正向转换器（src=实体）
        result.Should().HaveMethodBodyContaining("Map_CbuEntity_To_CbuDto", "Tag = global::CbuFwdConverter.Convert(src.Tag)");
        // 反向方法消费反向转换器（src=DTO）
        result.Should().HaveMethodBodyContaining("Map_CbuDto_To_CbuEntity", "Tag = global::CbuBackConverter.Convert(src.Tag)");
        // 验收 6：双特性并列不报 NCMAP033（互斥登记生效）
        Assert.False(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP033"),
            "同属性双特性不应报 NCMAP033");
    }

    /// <summary>
    /// 验收 7（NCMAP019 扫描登记点）：未标 [MapFrom] 的类型属性标 [ConvertBack] → NCMAP019
    /// <para>NCMAP019 谓词 name.Contains("ConvertUsing") 天然覆盖 ConvertBackUsing；
    /// 源码含锚点 [MapFrom] 类型属语料对照需要——**mappings 非空不是 019 输出前提**
    ///（口径订正，零锚回归锁 = NCMAP019ZeroAnchorUnitRegressionTests）。</para>
    /// </summary>
    [Fact]
    public void NoMapFrom_WithConvertBackUsing_EmitsNCMAP019()
    {
        var source = """
            using NCMapper.Attributes;

            public class CbuN19Entity { public string Tag { get; set; } = ""; }

            [MapFrom(typeof(CbuN19Entity))]
            public class CbuN19Anchor { public string Tag { get; set; } = ""; }

            public static class CbuN19BackConverter
            {
                public static string Convert(string fieldValue) => fieldValue;
            }

            public class CbuN19Dto
            {
                [ConvertBack(typeof(CbuN19BackConverter), nameof(CbuN19BackConverter.Convert))]
                public string Tag { get; set; } = "";
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        Assert.True(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP019"),
            "未标 [MapFrom] 但属性标 [ConvertBack] 应报 NCMAP019");

        // （第三处补断言）：锁定 T1 拆码后 NCMAP019 = Error 终态。
        // 原断言仅 ContainsKey 存在性（不断言 Severity）——本断言为新增锁定，不弱化既有语义。
        // 红测先证红（#96）：T1 拆码前（001 提交 a82e0cf 之前）NCMAP019 descriptor 为 Info 级，
        // 本断言在该历史形态下必红（git show 历史态取证成对留档见）。
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error,
            result.DiagnosticsByDescriptorId["NCMAP019"][0].Severity);
    }

    // ================================================================
    // 红测先行 — 反向无源消费（实体侧标注 + DTO 无同名源属性）
    // ================================================================

    /// <summary>
    /// 形态①（生成列）：一对多拆分 — 实体 FirstName/LastName 标 [ConvertBack]（T2 模板收整个 DTO），
    /// DTO 仅 FullName。反向方法应生成 `FirstName = global::CbuNsSplitter.ToFirstName(src)`（src=DTO 对象整传）；
    /// 正向方法不消费（无源 FullName 照旧跳过）。（现状红：实体独有属性不进 forward.PropertyMappings，
    /// 反向管线不可见，无转换器调用。）
    /// </summary>
    [Fact]
    public void NullSource_Splitter_EmitsDtoWideCalls()
    {
        var source = """
            using NCMapper.Attributes;

            public class CbuNsEntity
            {
                public int Id { get; set; }
                [ConvertBack(typeof(CbuNsSplitter), nameof(CbuNsSplitter.ToFirstName))]
                public string FirstName { get; set; } = "";
                [ConvertBack(typeof(CbuNsSplitter), nameof(CbuNsSplitter.ToLastName))]
                public string LastName { get; set; } = "";
            }

            public static class CbuNsSplitter
            {
                public static string ToFirstName(CbuNsDto dto) => "First:" + dto.FullName;
                public static string ToLastName(CbuNsDto dto) => "Last:" + dto.FullName;
            }

            [MapFrom(typeof(CbuNsEntity))]
            public class CbuNsDto
            {
                public int Id { get; set; }
                public string FullName { get; set; } = "";
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 反向方法消费实体侧转换器（T2：src = 整个 DTO 对象）
        result.Should().HaveMethodBodyContaining("Map_CbuNsDto_To_CbuNsEntity", "FirstName = global::CbuNsSplitter.ToFirstName(src)");
        result.Should().HaveMethodBodyContaining("Map_CbuNsDto_To_CbuNsEntity", "LastName = global::CbuNsSplitter.ToLastName(src)");

        // 正向方法不消费（实体侧标注仅反向语义）
        var fwdBody = result.Methods["Map_CbuNsEntity_To_CbuNsDto"][0].Body;
        Assert.DoesNotContain("ToFirstName", fwdBody);
        Assert.DoesNotContain("ToLastName", fwdBody);
    }

    /// <summary>
    /// 形态②（生成列）：四模板无源形态发射 — T0 `M()` / T1 `M(default)` / T2 `M(src)` / T3 `M(src, default)`
    /// （现状红：P0~P3 均无转换器调用）。
    /// </summary>
    [Fact]
    public void NullSource_FourTemplates_EmitRespectiveShapes()
    {
        var source = """
            using NCMapper.Attributes;

            public class CbuNs2Entity
            {
                public int Id { get; set; }
                [ConvertBack(typeof(CbuNs2Conv), nameof(CbuNs2Conv.M0))]
                public string P0 { get; set; } = "";
                [ConvertBack(typeof(CbuNs2Conv), nameof(CbuNs2Conv.M1))]
                public string P1 { get; set; } = "";
                [ConvertBack(typeof(CbuNs2Conv), nameof(CbuNs2Conv.M2))]
                public string P2 { get; set; } = "";
                [ConvertBack(typeof(CbuNs2Conv), nameof(CbuNs2Conv.M3))]
                public string P3 { get; set; } = "";
            }

            public static class CbuNs2Conv
            {
                public static string M0() => "T0";
                public static string M1(int v) => "T1:" + v;
                public static string M2(CbuNs2Dto dto) => "T2:" + dto.FullName;
                public static string M3(CbuNs2Dto dto, int v) => "T3:" + dto.FullName + ":" + v;
            }

            [MapFrom(typeof(CbuNs2Entity))]
            public class CbuNs2Dto
            {
                public int Id { get; set; }
                public string FullName { get; set; } = "";
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        result.Should().HaveMethodBodyContaining("Map_CbuNs2Dto_To_CbuNs2Entity", "P0 = global::CbuNs2Conv.M0()");
        result.Should().HaveMethodBodyContaining("Map_CbuNs2Dto_To_CbuNs2Entity", "P1 = global::CbuNs2Conv.M1(default)");
        result.Should().HaveMethodBodyContaining("Map_CbuNs2Dto_To_CbuNs2Entity", "P2 = global::CbuNs2Conv.M2(src)");
        result.Should().HaveMethodBodyContaining("Map_CbuNs2Dto_To_CbuNs2Entity", "P3 = global::CbuNs2Conv.M3(src, default)");
    }

    /// <summary>
    /// 形态②锚定（生成列）：单参构造 — 锚定=实体类型自身（方法声明于实体类），
    /// 发射 `Note = global::CbuNs2AEntity.SelfBack(src)`（现状红：无转换器调用）。
    /// </summary>
    [Fact]
    public void NullSource_SingleCtorAnchor_EmitsEntitySelfMethodCall()
    {
        var source = """
            using NCMapper.Attributes;

            public class CbuNs2AEntity
            {
                public int Id { get; set; }
                [ConvertBack(nameof(SelfBack))]
                public string Note { get; set; } = "";

                public static string SelfBack(CbuNs2ADto dto) => "self:" + dto.FullName;
            }

            [MapFrom(typeof(CbuNs2AEntity))]
            public class CbuNs2ADto
            {
                public int Id { get; set; }
                public string FullName { get; set; } = "";
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        result.Should().HaveMethodBodyContaining("Map_CbuNs2ADto_To_CbuNs2AEntity", "Note = global::CbuNs2AEntity.SelfBack(src)");
    }

    /// <summary>
    /// 验收 #2（生成列，v1.1/ 最严形态）：仅剩无源条目 — DTO 与实体无同名属性
    /// （正向 pm 集合为空）+ 实体侧一条无源标注：主 foreach 零产出，反向快照非 null 仅靠
    /// 追加点位于计数门之前。反向 6 重载照常生成且核心方法消费转换器（T2：src = 整个 DTO 对象）。
    /// </summary>
    [Fact]
    public void OnlyReverseOnlyEntries_EmptyForwardPms_ReverseMethodStillGenerated()
    {
        var source = """
            using NCMapper.Attributes;

            public class CbuNs4Entity
            {
                [ConvertBack(typeof(CbuNs4Conv), nameof(CbuNs4Conv.Convert))]
                public string Note { get; set; } = "";
            }

            public static class CbuNs4Conv
            {
                public static string Convert(CbuNs4Dto dto) => "only:" + dto.FullName;
            }

            [MapFrom(typeof(CbuNs4Entity))]
            public class CbuNs4Dto
            {
                public string FullName { get; set; } = "";
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 反向 6 重载齐备（仅剩无源条目时反向快照非 null、反向扩展照常注册）
        AssertSixReverseOverloads(result, "MapToCbuNs4Entity");

        // 反向核心方法消费转换器（T2 模板：src = 整个 DTO 对象）
        result.Should().HaveMethodBodyContaining("Map_CbuNs4Dto_To_CbuNs4Entity", "Note = global::CbuNs4Conv.Convert(src)");
    }

    /// <summary>
    /// M-22（20260926-001 五轴审查）裁定后口径：匹配对实体侧标注 — 实体 Tag 标 [ConvertBack]（T1 模板）、
    /// DTO Tag 同名无标注。原实体侧兜底（备案）已按 ADR-002 §决策 3
    /// 删除段退役 → 生成端不消费，反向走类型转换路径直赋 `Tag = src.Tag`（与运行时 M-26 后同口径；
    /// 原"生成端消费/运行时不过"的 -2 分叉就此闭合）。
    /// </summary>
    [Fact]
    public void MatchedPair_EntitySideAnnotation_NotConsumed()
    {
        var source = """
            using NCMapper.Attributes;

            public class CbuNs3Entity
            {
                public int Id { get; set; }
                [ConvertBack(typeof(CbuNs3Conv), nameof(CbuNs3Conv.Convert))]
                public string Tag { get; set; } = "";
            }

            public static class CbuNs3Conv
            {
                public static string Convert(string fieldValue) => "back:" + fieldValue;
            }

            [MapFrom(typeof(CbuNs3Entity))]
            public class CbuNs3Dto
            {
                public int Id { get; set; }
                public string Tag { get; set; } = "";
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // M-22：实体侧 [ConvertBack] 不再被反向消费 → 反向走类型转换路径直赋（若兜底回流会生成
        // `Tag = global::CbuNs3Conv.Convert(src.Tag)` 而本条断言不成立，故正向断言即可锁定新口径）
        result.Should().HaveMethodBodyContaining("Map_CbuNs3Dto_To_CbuNs3Entity", "Tag = src.Tag");
    }

    /// <summary>
    /// 验收 #2（生成列）：双侧同标 DTO 侧优先 — 实体与 DTO 同名属性各标不同 [ConvertBack]
    /// 转换器。反向方法应消费 DTO 侧转换器（与运行时 sourceItem ?? targetItem 查询序一致），
    /// 生成 `Tag = global::CbuNs3BDtoConv.Convert(src.Tag)` 而非实体侧转换器（防实体侧
    /// 兜底翻转优先序的锁定测试）。
    /// </summary>
    [Fact]
    public void MatchedPair_BothSidesAnnotated_DtoSideConverterWins()
    {
        var source = """
            using NCMapper.Attributes;

            public class CbuNs3BEntity
            {
                public int Id { get; set; }
                [ConvertBack(typeof(CbuNs3BEntityConv), nameof(CbuNs3BEntityConv.Convert))]
                public string Tag { get; set; } = "";
            }

            public static class CbuNs3BEntityConv
            {
                public static string Convert(string fieldValue) => "entity:" + fieldValue;
            }

            public static class CbuNs3BDtoConv
            {
                public static string Convert(string fieldValue) => "dto:" + fieldValue;
            }

            [MapFrom(typeof(CbuNs3BEntity))]
            public class CbuNs3BDto
            {
                public int Id { get; set; }
                [ConvertBack(typeof(CbuNs3BDtoConv), nameof(CbuNs3BDtoConv.Convert))]
                public string Tag { get; set; } = "";
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        result.Should().HaveMethodBodyContaining("Map_CbuNs3BDto_To_CbuNs3BEntity", "Tag = global::CbuNs3BDtoConv.Convert(src.Tag)");
    }

    /// <summary>
    /// 矩阵（生成列，setter 门）：get-only — 实体属性无 setter 时扫描门跳过并登记
    /// NCMAP013（ReverseMappingSkipped，Info 级），反向方法体无转换器调用（不生成非法赋值）。
    /// </summary>
    [Fact]
    public void SetterGate_GetOnly_SkipsWithNCMAP013()
    {
        var source = """
            using NCMapper.Attributes;

            public class CbuSg1Entity
            {
                public int Id { get; set; }
                [ConvertBack(typeof(CbuSg1Conv), nameof(CbuSg1Conv.Make))]
                public string Tag { get; } = "";
            }

            public static class CbuSg1Conv
            {
                public static string Make(string fieldValue) => "sg:" + fieldValue;
            }

            [MapFrom(typeof(CbuSg1Entity))]
            public class CbuSg1Dto
            {
                public int Id { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        Assert.True(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP013"),
            "get-only setter 门应登记 NCMAP013 ReverseMappingSkipped");
        Assert.True(result.Methods.ContainsKey("Map_CbuSg1Dto_To_CbuSg1Entity"), "反向方法应生成（Id 匹配对保留）");
        Assert.DoesNotContain("CbuSg1Conv.Make", result.GetMethod("Map_CbuSg1Dto_To_CbuSg1Entity")!.Body);
    }

    /// <summary>
    /// 矩阵（生成列，setter 门）：非 public 且非 init-only（internal set）— 扫描门跳过并登记
    /// NCMAP013，反向方法体无转换器调用（与 ReversePlanner.cs 匹配对路径 setter 门同语义）。
    /// </summary>
    [Fact]
    public void SetterGate_InternalSetter_SkipsWithNCMAP013()
    {
        var source = """
            using NCMapper.Attributes;

            public class CbuSg2Entity
            {
                public int Id { get; set; }
                [ConvertBack(typeof(CbuSg2Conv), nameof(CbuSg2Conv.Make))]
                public string Tag { get; internal set; } = "";
            }

            public static class CbuSg2Conv
            {
                public static string Make(string fieldValue) => "sg:" + fieldValue;
            }

            [MapFrom(typeof(CbuSg2Entity))]
            public class CbuSg2Dto
            {
                public int Id { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        Assert.True(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP013"),
            "internal setter 门应登记 NCMAP013 ReverseMappingSkipped");
        Assert.True(result.Methods.ContainsKey("Map_CbuSg2Dto_To_CbuSg2Entity"), "反向方法应生成（Id 匹配对保留）");
        Assert.DoesNotContain("CbuSg2Conv.Make", result.GetMethod("Map_CbuSg2Dto_To_CbuSg2Entity")!.Body);
    }

    /// <summary>
    /// 矩阵（生成列，setter 门，v1.1/评审 B-2）：init-only 正常接受消费 — 实体属性
    /// init-only + 参数化构造器时，无源反向转换器经构造器参数匹配路径（H-04）作为构造实参消费。
    /// </summary>
    [Fact]
    public void SetterGate_InitOnly_ConsumedViaConstructorArg()
    {
        var source = """
            using NCMapper.Attributes;

            public class CbuSg3Entity
            {
                public int Id { get; set; }
                [ConvertBack(typeof(CbuSg3Conv), nameof(CbuSg3Conv.Make))]
                public string Tag { get; init; } = "";
                public CbuSg3Entity(int id, string tag) { Id = id; Tag = tag; }
            }

            public static class CbuSg3Conv
            {
                public static string Make(string fieldValue) => "init:" + fieldValue;
            }

            [MapFrom(typeof(CbuSg3Entity))]
            public class CbuSg3Dto
            {
                public int Id { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        result.Should().HaveMethodBodyContaining("Map_CbuSg3Dto_To_CbuSg3Entity", "new CbuSg3Entity(");
        result.Should().HaveMethodBodyContaining("Map_CbuSg3Dto_To_CbuSg3Entity", "global::CbuSg3Conv.Make(");
    }

    /// <summary>
    /// 矩阵（生成列，全局忽略抑制）：MapDefaults(IgnoreNamesToEntity) 命中实体属性名 —
    /// 无源反向转换被抑制（反向方法体无转换器调用）+ NCMAP013 登记 + NCMAP021 显式特性命中指引。
    /// </summary>
    [Fact]
    public void GlobalIgnore_ToEntityHit_SuppressesReverseOnlyConverter()
    {
        var source = """
            using NCMapper.Attributes;

            [assembly: MapDefaults(IgnoreNamesToEntity = new[] { "Stamp" })]

            public class CbuGi1Entity
            {
                public int Id { get; set; }
                [ConvertBack(typeof(CbuGi1Conv), nameof(CbuGi1Conv.Make))]
                public string Stamp { get; set; } = "";
            }

            public static class CbuGi1Conv
            {
                public static string Make(string fieldValue) => "gi:" + fieldValue;
            }

            [MapFrom(typeof(CbuGi1Entity))]
            public class CbuGi1Dto
            {
                public int Id { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        Assert.True(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP013"),
            "全局 ToEntity 忽略命中应登记 NCMAP013（反向跳过）");
        Assert.True(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP021"),
            "显式特性（[ConvertBack]）命中忽略清单应登记 NCMAP021 指引");
        Assert.True(result.Methods.ContainsKey("Map_CbuGi1Dto_To_CbuGi1Entity"), "反向方法应生成（Id 匹配对保留）");
        Assert.DoesNotContain("CbuGi1Conv.Make", result.GetMethod("Map_CbuGi1Dto_To_CbuGi1Entity")!.Body);
    }

    /// <summary>
    /// 矩阵（生成列，全局忽略抑制豁免）：OverrideIgnore = true — 实体侧 [ConvertBack]
    /// 带豁免开关时全局 ToEntity 忽略清单不抑制无源反向消费（转换器照常发射、无 NCMAP013）。
    /// </summary>
    [Fact]
    public void GlobalIgnore_OverrideIgnore_Exempts_StillConsumed()
    {
        var source = """
            using NCMapper.Attributes;

            [assembly: MapDefaults(IgnoreNamesToEntity = new[] { "Stamp" })]

            public class CbuGi2Entity
            {
                public int Id { get; set; }
                [ConvertBack(typeof(CbuGi2Conv), nameof(CbuGi2Conv.Make), OverrideIgnore = true)]
                public string Stamp { get; set; } = "";
            }

            public static class CbuGi2Conv
            {
                public static string Make(string fieldValue) => "gi:" + fieldValue;
            }

            [MapFrom(typeof(CbuGi2Entity))]
            public class CbuGi2Dto
            {
                public int Id { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        result.Should().HaveMethodBodyContaining("Map_CbuGi2Dto_To_CbuGi2Entity", "Stamp = global::CbuGi2Conv.Make(");
        Assert.False(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP013"),
            "OverrideIgnore 豁免时不应登记 NCMAP013（无源反向消费不被抑制）");
    }

    /// <summary>
    /// 矩阵（生成列，写回链覆盖排除，验收 #4 生成端）：实体中间段标 [ConvertBack] —
    /// 中间段已被正向扁平化 pm 的 SourcePath 收入覆盖排除集（SourceProperty ∪ SourcePath 全段），
    /// 不入无源集合：反向方法体仅含写回链产物（City = src.AddressCity），无转换器整对象覆盖调用
    /// （无双赋值）。运行时列见 ConvertBackWriteBackCoverageTests（集成值断言双引擎）。
    /// </summary>
    [Fact]
    public void CoveredIntermediateSegment_ExcludedFromReverseOnly_NoConverterCall()
    {
        var source = """
            using NCMapper.Attributes;

            public class CbuNs4BEntity
            {
                public int Id { get; set; }
                [ConvertBack(typeof(CbuNs4BConv), nameof(CbuNs4BConv.MakeAddress))]
                public CbuNs4BAddress Address { get; set; } = new();
            }

            public class CbuNs4BAddress
            {
                public string City { get; set; } = "";
            }

            public static class CbuNs4BConv
            {
                public static CbuNs4BAddress MakeAddress(CbuNs4BDto dto) => new CbuNs4BAddress { City = "POISON" };
            }

            [MapFrom(typeof(CbuNs4BEntity))]
            public class CbuNs4BDto
            {
                public int Id { get; set; }
                public string AddressCity { get; set; } = "";
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        Assert.True(result.Methods.ContainsKey("Map_CbuNs4BDto_To_CbuNs4BEntity"), "反向方法应生成（Id 匹配对保留）");
        var body = result.GetMethod("Map_CbuNs4BDto_To_CbuNs4BEntity")!.Body;
        Assert.DoesNotContain("CbuNs4BConv.MakeAddress", body);
        Assert.Contains("City = src.AddressCity", body);
    }

    // ================================================================
    // 红测先行 — 写回链实体叶标注（X2 分叉：生成端不消费，运行时已消费）
    // M-22（20260926-001 五轴审查）：叶兜底已按 ADR-002 §决策 3 删除段退役 → 生成端不消费，
    // 与运行时（删叶兜底）口径合一，原"生成端消费/运行时不过"分叉反向闭合
    // ================================================================

    /// <summary>
    /// M-22 裁定后口径：写回链实体叶属性（非规范位置）标 [ConvertBack] — 生成端不消费。
    /// <para>实体嵌套叶 CbuWLAddress.City 标 [ConvertBack]、DTO 扁平属性 CbuWLDto.City 仅标
    /// [MapProperty] 写回链、不标 [ConvertBack]。原叶兜底退役后，
    /// 反向写回走类型转换路径直写 `dst.Address.City = src.City`（不再生成叶侧转换器调用；
    /// 原断言 `= global::CbuWLConv.Convert(src.City)` 随之反转）。</para>
    /// </summary>
    [Fact]
    public void WriteBackLeaf_EntitySideAnnotation_NotConsumed()
    {
        var source = """
            using NCMapper.Attributes;

            public class CbuWLAddress
            {
                [ConvertBack(typeof(CbuWLConv), nameof(CbuWLConv.Convert))]
                public string City { get; set; } = "";
            }

            public class CbuWLEntity
            {
                public CbuWLAddress Address { get; set; } = new();
            }

            public static class CbuWLConv
            {
                public static string Convert(string fieldValue) => "wleaf:" + fieldValue;
            }

            [MapFrom(typeof(CbuWLEntity))]
            public class CbuWLDto
            {
                [MapProperty(typeof(CbuWLEntity), "Address.City")]
                public string City { get; set; } = "";
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // M-22：叶侧 [ConvertBack] 不再被反向写回消费 → 类型转换路径直写
        // （若叶兜底回流会生成 `dst.Address.City = global::CbuWLConv.Convert(src.City)`，本条断言不成立）
        result.Should().HaveMethodBodyContaining("Map_CbuWLDto_To_CbuWLEntity", "dst.Address.City = src.City");
    }

    /// <summary>
    /// 验收 #2（生成列）：写回链双侧同标 DTO 侧优先 — 实体叶与 DTO 扁平属性各标不同
    /// [ConvertBack] 转换器。反向写回应消费 DTO 侧转换器（镜像运行时 sourceProp ?? leaf
    /// 查询序——兜底分支挂在 DTO 侧分支之后），生成 `dst.Address.City = global::CbuWLBDtoConv.Convert(src.City)`
    /// 而非叶侧转换器（防兜底分支翻转查询序的锁定测试，写回链版）。
    /// </summary>
    [Fact]
    public void WriteBackLeaf_BothSidesAnnotated_DtoSideConverterWins()
    {
        var source = """
            using NCMapper.Attributes;

            public class CbuWLBAddress
            {
                [ConvertBack(typeof(CbuWLBLeafConv), nameof(CbuWLBLeafConv.Convert))]
                public string City { get; set; } = "";
            }

            public class CbuWLBEntity
            {
                public CbuWLBAddress Address { get; set; } = new();
            }

            public static class CbuWLBLeafConv
            {
                public static string Convert(string fieldValue) => "leaf:" + fieldValue;
            }

            public static class CbuWLBDtoConv
            {
                public static string Convert(string fieldValue) => "dto:" + fieldValue;
            }

            [MapFrom(typeof(CbuWLBEntity))]
            public class CbuWLBDto
            {
                [MapProperty(typeof(CbuWLBEntity), "Address.City")]
                [ConvertBack(typeof(CbuWLBDtoConv), nameof(CbuWLBDtoConv.Convert))]
                public string City { get; set; } = "";
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        result.Should().HaveMethodBodyContaining("Map_CbuWLBDto_To_CbuWLBEntity", "dst.Address.City = global::CbuWLBDtoConv.Convert(src.City)");
    }

    /// <summary>
    /// M-22 裁定后口径（原验收 #3）：叶标注 × DTO 侧 [Convert]，原"叶兜底消费 → 与 NCMAP033 互斥"。
    /// <para>叶兜底退役后，实体叶 [ConvertBack] 不再被反向写回消费 → 该属性反向确为"降级不消费"场景，
    /// NCMAP033 应如实发射（原断言"不登记"随之反转）；反向写回体走类型转换直写、无叶侧转换器调用。
    /// 与直接分支（M-22 :348-367 同批）的互斥豁免废止同口径。</para>
    /// </summary>
    [Fact]
    public void WriteBackLeaf_WithForwardConvertUsing_ReportsNCMAP033()
    {
        var source = """
            using NCMapper.Attributes;

            public class CbuWLFAddress
            {
                [ConvertBack(typeof(CbuWLFBackConv), nameof(CbuWLFBackConv.Convert))]
                public string City { get; set; } = "";
            }

            public class CbuWLFEntity
            {
                public CbuWLFAddress Address { get; set; } = new();
            }

            public static class CbuWLFFwdConv
            {
                public static string Convert(string fieldValue) => "fwd:" + fieldValue;
            }

            public static class CbuWLFBackConv
            {
                public static string Convert(string fieldValue) => "wleaf:" + fieldValue;
            }

            [MapFrom(typeof(CbuWLFEntity))]
            public class CbuWLFDto
            {
                [MapProperty(typeof(CbuWLFEntity), "Address.City")]
                [Convert(typeof(CbuWLFFwdConv), nameof(CbuWLFFwdConv.Convert))]
                public string City { get; set; } = "";
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // M-22：叶侧 [ConvertBack] 不再被消费 → 反向写回走类型转换直写
        result.Should().HaveMethodBodyContaining("Map_CbuWLFDto_To_CbuWLFEntity", "dst.Address.City = src.City");
        // 叶标注不再作互斥豁免 → 正向 [Convert] 反向降级不消费，如实登记 NCMAP033
        Assert.True(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP033"),
            "叶标注退役后不再是互斥豁免场景，应报 NCMAP033（正向 [Convert] 反向降级不消费）");
    }

    // ================================================================
    // 路径场景 [ConvertBack] 校验诊断补齐（F-D2）
    // ================================================================

    /// <summary>
    /// 验收 #1（生成列）：路径场景 DTO 侧标注 [ConvertBack] + 方法签名不匹配 → NCMAP037。
    /// <para>写回链 DTO 扁平属性标 [ConvertBack]（T2 形态 source 参数类型≠DTO），
    /// 校验基准 TSource=DTO 类型 ≠ CbuWPOtherEntity → 签名不匹配诊断
    ///（路径场景此前零校验、签名不匹配静默降级——F-D2 补齐；对照直接属性形态 NCMAP037 既有用例）。</para>
    /// </summary>
    [Fact]
    public void Path_DtoSideConvertBack_SignatureMismatch_EmitsNCMAP037()
    {
        var source = """
            using NCMapper.Attributes;

            public class CbuWPAddress { public string City { get; set; } = ""; }
            public class CbuWPEntity { public CbuWPAddress Address { get; set; } = new(); }

            public class CbuWPOtherEntity { public string City { get; set; } = ""; }

            public static class CbuWPWrongConv
            {
                public static string Convert(CbuWPOtherEntity source) => source.City;
            }

            [MapFrom(typeof(CbuWPEntity))]
            public class CbuWPDto
            {
                [MapProperty(typeof(CbuWPEntity), "Address.City")]
                [ConvertBack(typeof(CbuWPWrongConv), nameof(CbuWPWrongConv.Convert))]
                public string City { get; set; } = "";
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        Assert.True(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP037"),
            "路径场景 DTO 侧 [ConvertBack] 签名不匹配应报 NCMAP037");
        // 单点产出：恰 1 条（提取块产出、物化期丢弃——防重复发射）
        Assert.Single(result.DiagnosticsByDescriptorId["NCMAP037"]);
    }

    /// <summary>
    /// M-22（20260926-001 五轴审查）：路径场景实体叶标注 + 签名不匹配 → 零 NCMAP037。
    /// <para>原验收 #2 锁定"叶属性兜底校验命中、NCMAP037 载体=叶属性"。叶侧兜底消费能力
    /// 随退役（ADR-002 §决策 3 删除段）后，叶侧标注 100% 不可消费 →
    /// 校验不可消费的标注属噪音，叶兜底校验支删除（静默口径与同任务 D3 索引路径先例同族）。
    /// 本用例反转为"不校验、零 NCMAP037"。</para>
    /// </summary>
    [Fact]
    public void Path_LeafSideConvertBack_NotValidated_NoNCMAP037()
    {
        var source = """
            using NCMapper.Attributes;

            public class CbuWPLAddress
            {
                [ConvertBack(typeof(CbuWPLWrongConv), nameof(CbuWPLWrongConv.Convert))]
                public string City { get; set; } = "";
            }
            public class CbuWPLEntity { public CbuWPLAddress Address { get; set; } = new(); }

            public class CbuWPLOtherEntity { public string City { get; set; } = ""; }

            public static class CbuWPLWrongConv
            {
                public static string Convert(CbuWPLOtherEntity source) => source.City;
            }

            [MapFrom(typeof(CbuWPLEntity))]
            public class CbuWPLDto
            {
                [MapProperty(typeof(CbuWPLEntity), "Address.City")]
                public string City { get; set; } = "";
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        Assert.False(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP037"),
            "叶侧兜底校验退役后：实体叶 [ConvertBack] 不可消费，不校验不产 NCMAP037（M-22 / D3 同族静默）");
        // 叶侧标注零消费：写回链直写、无转换器调用
        var body = result.GetMethod("Map_CbuWPLDto_To_CbuWPLEntity")!.Body;
        Assert.Contains("dst.Address.City = src.City", body);
        Assert.DoesNotContain("CbuWPLWrongConv.Convert", body);
    }

    /// <summary>
    /// 验收 #4（生成列）：双侧同标（DTO 侧有效 + 叶侧签名不匹配）— DTO 侧优先消费、
    /// 叶侧不校验不产诊断（D5 门：锚点优先查询序，叶侧仅在前侧无标注时兜底）。
    /// </summary>
    [Fact]
    public void Path_BothSidesAnnotated_DtoValidLeafInvalid_ZeroNCMAP037()
    {
        var source = """
            using NCMapper.Attributes;

            public class CbuWPBAddress
            {
                [ConvertBack(typeof(CbuWPBWrongConv), nameof(CbuWPBWrongConv.Convert))]
                public string City { get; set; } = "";
            }
            public class CbuWPBEntity { public CbuWPBAddress Address { get; set; } = new(); }

            public class CbuWPBOtherEntity { public string City { get; set; } = ""; }

            public static class CbuWPBWrongConv
            {
                public static string Convert(CbuWPBOtherEntity source) => source.City;
            }

            public static class CbuWPBGoodConv
            {
                public static string Convert(string fieldValue) => "dto:" + fieldValue;
            }

            [MapFrom(typeof(CbuWPBEntity))]
            public class CbuWPBDto
            {
                [MapProperty(typeof(CbuWPBEntity), "Address.City")]
                [ConvertBack(typeof(CbuWPBGoodConv), nameof(CbuWPBGoodConv.Convert))]
                public string City { get; set; } = "";
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // DTO 侧有效签名消费（生成转换器调用）
        result.Should().HaveMethodBodyContaining("Map_CbuWPBDto_To_CbuWPBEntity", "dst.Address.City = global::CbuWPBGoodConv.Convert(src.City)");
        // 叶侧（签名不匹配）不被校验——零 NCMAP037
        Assert.False(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP037"),
            "双侧同标 DTO 侧优先：叶侧不校验不产诊断（D5 门）");
    }

    /// <summary>
    /// D3 锁定（生成列）：索引路径 [ConvertBack] 跳过校验 — 集合索引路径
    ///（"Items[0].Name" 形态，反向写回整体跳过 S12h）不产 NCMAP037（校验不可消费的标注属噪音）。
    /// </summary>
    [Fact]
    public void Path_IndexedPathConvertBack_SkipsValidation_NoNCMAP037()
    {
        var source = """
            using NCMapper.Attributes;
            using System.Collections.Generic;

            public class CbuWPIItem { public string Name { get; set; } = ""; }
            public class CbuWPIEntity { public List<CbuWPIItem> Items { get; set; } = new(); }

            public class CbuWPIOtherEntity { public string Name { get; set; } = ""; }

            public static class CbuWPIWrongConv
            {
                public static string Convert(CbuWPIOtherEntity source) => source.Name;
            }

            [MapFrom(typeof(CbuWPIEntity))]
            public class CbuWPIDto
            {
                [MapProperty(typeof(CbuWPIEntity), "Items[0].Name")]
                [ConvertBack(typeof(CbuWPIWrongConv), nameof(CbuWPIWrongConv.Convert))]
                public string FirstName { get; set; } = "";
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 索引路径反向写回整体跳过（S12h）→ [ConvertBack] 不可消费 → 校验跳过、零诊断
        Assert.False(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP037"),
            "索引路径 [ConvertBack] 应跳过校验（D3——不可消费标注不校验）");
    }

    // ================================================================
    // 忽略域行为锁定（F-D1 强形式证伪落地）
    // 叶侧 [MapIgnore] 非生成端无视：发射期 ShouldSkipForDirectionPair 双侧判定
    //（先于写回专用分支分发）拦截叶侧 ToEntity/Both——与运行时写回双侧检查最终行为一致。
    // ================================================================

    /// <summary>
    /// 用例①（生成列）：叶侧 [MapIgnore(Direction.ToEntity)] — 双引擎均跳过写回。
    /// <para>实体叶标 [ConvertBack] + [MapIgnore(ToEntity)]：规划期单查 DTO 侧（不拦），
    /// 发射期双侧判定拦截（静默 continue）→ 反向方法体无 Address.City 写回行
    ///（运行时镜像：ExpressionBuilder :990-991 双侧检查跳过——运行时列见
    /// ConvertBackWriteLeafIgnoreRuntimeTests）。</para>
    /// </summary>
    [Fact]
    public void WriteBackLeaf_MapIgnoreToEntityOnLeaf_SkipsWriteBack()
    {
        var source = """
            using NCMapper.Attributes;

            public class CbuWMI1Address
            {
                [MapIgnore(Direction.ToEntity)]
                [ConvertBack(typeof(CbuWMConv), nameof(CbuWMConv.Convert))]
                public string City { get; set; } = "";
            }

            public class CbuWMI1Entity { public CbuWMI1Address Address { get; set; } = new(); }

            public static class CbuWMConv
            {
                public static string Convert(string fieldValue) => "wmi:" + fieldValue;
            }

            [MapFrom(typeof(CbuWMI1Entity))]
            public class CbuWMI1Dto
            {
                [MapProperty(typeof(CbuWMI1Entity), "Address.City")]
                public string City { get; set; } = "";
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        var body = result.GetMethod("Map_CbuWMI1Dto_To_CbuWMI1Entity")!.Body;
        Assert.DoesNotContain("dst.Address.City", body);
        Assert.DoesNotContain("CbuWMConv.Convert", body);
    }

    /// <summary>
    /// 用例②（生成列）：DTO 侧 [MapIgnore(Direction.ToEntity)] — 双引擎均跳过写回
    ///（规划期 DTO 侧单查即拦，分层路径不同、最终行为一致；Id 锚点保证反向方法生成——
    /// 全属性被忽略时反向方法整体不生成属另一独立边界，本用例不触发）。
    /// </summary>
    [Fact]
    public void WriteBackLeaf_MapIgnoreToEntityOnDto_SkipsWriteBack()
    {
        var source = """
            using NCMapper.Attributes;

            public class CbuWMI2Address
            {
                [ConvertBack(typeof(CbuWMConv), nameof(CbuWMConv.Convert))]
                public string City { get; set; } = "";
            }

            public class CbuWMI2Entity
            {
                public int Id { get; set; }
                public CbuWMI2Address Address { get; set; } = new();
            }

            public static class CbuWMConv
            {
                public static string Convert(string fieldValue) => "wmi:" + fieldValue;
            }

            [MapFrom(typeof(CbuWMI2Entity))]
            public class CbuWMI2Dto
            {
                public int Id { get; set; }

                [MapProperty(typeof(CbuWMI2Entity), "Address.City")]
                [MapIgnore(Direction.ToEntity)]
                public string City { get; set; } = "";
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        var body = result.GetMethod("Map_CbuWMI2Dto_To_CbuWMI2Entity")!.Body;
        Assert.DoesNotContain("dst.Address.City", body);
        Assert.DoesNotContain("CbuWMConv.Convert", body);
    }

    /// <summary>
    /// 用例③（生成列）：叶侧 [MapIgnore(Direction.ToDto)] — 方向不命中（反向是 ToEntity 方向），
    /// 写回不跳过（方向精确性锁定）。
    /// <para>M-22：叶侧 [ConvertBack] 已随叶兜底退役不再被消费，故"不跳过"的断言证据由
    /// "叶侧转换器调用"改为"类型转换直写赋值存在"——锁定意图不变（方向不命中不得致写回被跳过）。</para>
    /// </summary>
    [Fact]
    public void WriteBackLeaf_MapIgnoreToDtoOnLeaf_WriteBackNotSkipped()
    {
        var source = """
            using NCMapper.Attributes;

            public class CbuWMI3Address
            {
                [MapIgnore(Direction.ToDto)]
                [ConvertBack(typeof(CbuWMConv), nameof(CbuWMConv.Convert))]
                public string City { get; set; } = "";
            }

            public class CbuWMI3Entity { public CbuWMI3Address Address { get; set; } = new(); }

            public static class CbuWMConv
            {
                public static string Convert(string fieldValue) => "wmi:" + fieldValue;
            }

            [MapFrom(typeof(CbuWMI3Entity))]
            public class CbuWMI3Dto
            {
                [MapProperty(typeof(CbuWMI3Entity), "Address.City")]
                public string City { get; set; } = "";
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 方向不命中 → 写回照常（M-22 后为类型转换直写，叶侧转换器不再消费）
        result.Should().HaveMethodBodyContaining("Map_CbuWMI3Dto_To_CbuWMI3Entity", "dst.Address.City = src.City");
    }

    // ================================================================
    // 测试矩阵补全（四模板/单参锚定/警告态行为锁定）
    // ================================================================

    /// <summary>
    /// 矩阵（生成列，M-22 后改挂承载侧）：四模板 — T0 常量 / T2 整 DTO / T3 DTO+字段
    ///（T1 M(TField) 由 WriteBackLeaf_BothSidesAnnotated_DtoSideConverterWins 覆盖）。
    /// <para>M-22：原用例将 [ConvertBack] 标在**实体叶**（非规范位置）以测叶兜底消费，叶兜底退役后
    /// 改挂**承载侧**（DTO 扁平属性）——模板形态覆盖不变，三形态仍应分别发射
    /// <c>M0()</c> / <c>M2(src)</c> / <c>M3(src, src.P3)</c>。</para>
    /// </summary>
    [Fact]
    public void WriteBack_DtoSideFourTemplates_T0T2T3_EmitRespectiveShapes()
    {
        var source = """
            using NCMapper.Attributes;

            public class CbuWLMAddress
            {
                public string P0 { get; set; } = "";
                public string P2 { get; set; } = "";
                public string P3 { get; set; } = "";
            }

            public class CbuWLMEntity { public CbuWLMAddress Address { get; set; } = new(); }

            public static class CbuWLMConv
            {
                public static string M0() => "T0";
                public static string M2(CbuWLMDto dto) => "T2:" + dto.P2;
                public static string M3(CbuWLMDto dto, string v) => "T3:" + v;
            }

            [MapFrom(typeof(CbuWLMEntity))]
            public class CbuWLMDto
            {
                [MapProperty(typeof(CbuWLMEntity), "Address.P0")]
                [ConvertBack(typeof(CbuWLMConv), nameof(CbuWLMConv.M0))]
                public string P0 { get; set; } = "";
                [MapProperty(typeof(CbuWLMEntity), "Address.P2")]
                [ConvertBack(typeof(CbuWLMConv), nameof(CbuWLMConv.M2))]
                public string P2 { get; set; } = "";
                [MapProperty(typeof(CbuWLMEntity), "Address.P3")]
                [ConvertBack(typeof(CbuWLMConv), nameof(CbuWLMConv.M3))]
                public string P3 { get; set; } = "";
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        var body = result.GetMethod("Map_CbuWLMDto_To_CbuWLMEntity")!.Body;
        Assert.Contains("dst.Address.P0 = global::CbuWLMConv.M0()", body);
        Assert.Contains("dst.Address.P2 = global::CbuWLMConv.M2(src)", body);
        Assert.Contains("dst.Address.P3 = global::CbuWLMConv.M3(src, src.P3)", body);
    }

    /// <summary>
    /// 矩阵（生成列，M-22 后改挂承载侧）：单参锚定 — 锚定类型 = 承载侧属性所属类型
    ///（DTO 自身，方法声明于 DTO 上）。
    /// <para>M-22：原用例锚定类型为**实体叶所属类型**（嵌套类型 Address 自身，
    /// 镜像运行时 targetItem.DeclaringType 兜底）；叶兜底退役后改挂承载侧，
    /// 锚定基准随承载侧同源，形态不变。<c>[ConvertBack(nameof(...))]</c> 单参形态（无 typeof）保留。</para>
    /// </summary>
    [Fact]
    public void WriteBack_DtoSideSingleCtorAnchor_DtoSelfMethod()
    {
        var source = """
            using NCMapper.Attributes;

            public class CbuWLSAddress
            {
                public string City { get; set; } = "";
            }

            public class CbuWLSEntity { public CbuWLSAddress Address { get; set; } = new(); }

            [MapFrom(typeof(CbuWLSEntity))]
            public class CbuWLSDto
            {
                [MapProperty(typeof(CbuWLSEntity), "Address.City")]
                [ConvertBack(nameof(MakeCity))]
                public string City { get; set; } = "";

                public static string MakeCity(string fieldValue) => "self:" + fieldValue;
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        result.Should().HaveMethodBodyContaining("Map_CbuWLSDto_To_CbuWLSEntity", "dst.Address.City = global::CbuWLSDto.MakeCity(src.City)");
    }

    /// <summary>
    /// M-22（20260926-001 五轴审查）：实体叶标注（信号不匹配）+ 写回链 → 零 NCMAP037 + 直写。
    /// <para>原专项行⑥锁定"叶标注 + 方法签名不匹配 → NCMAP037 发射 + 三元组 null 走直写"。
    /// 叶侧兜底消费与校验均随退役（ADR-002 §决策 3 删除段）后，
    /// 叶侧标注 100% 不可消费 → 不校验不产诊断；生成端直接类型转换直写
    ///（<c>dst.Address.City = src.City</c>，无转换器调用）。</para>
    /// </summary>
    [Fact]
    public void WriteBackLeaf_EntitySideAnnotation_NotValidated_GeneratesDirectWrite()
    {
        var source = """
            using NCMapper.Attributes;

            public class CbuWLWAddress
            {
                [ConvertBack(typeof(CbuWLWWrongConv), nameof(CbuWLWWrongConv.Convert))]
                public string City { get; set; } = "";
            }

            public class CbuWLWEntity { public CbuWLWAddress Address { get; set; } = new(); }

            public class CbuWLWOtherEntity { public string City { get; set; } = ""; }

            public static class CbuWLWWrongConv
            {
                public static string Convert(CbuWLWOtherEntity source) => source.City;
            }

            [MapFrom(typeof(CbuWLWEntity))]
            public class CbuWLWDto
            {
                [MapProperty(typeof(CbuWLWEntity), "Address.City")]
                public string City { get; set; } = "";
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 叶侧兜底校验退役（M-22）：不可消费标注不校验、零 NCMAP037
        Assert.False(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP037"),
            "叶侧兜底校验退役后不应报 NCMAP037");
        var body = result.GetMethod("Map_CbuWLWDto_To_CbuWLWEntity")!.Body;
        Assert.Contains("dst.Address.City = src.City", body);
        Assert.DoesNotContain("CbuWLWWrongConv.Convert", body);
    }
}
