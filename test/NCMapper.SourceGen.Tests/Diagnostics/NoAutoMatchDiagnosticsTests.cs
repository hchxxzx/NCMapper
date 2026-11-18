// [MapNoAutoMatch] 诊断面测试（测试矩阵维度①诊断族枚举 /
//   ②019 家族承接 / ⑥NCMAP007 语义级抑制 / ⑧既有码零回归抽查；F11 零交互）。
// 断言基准 = 既有诊断测试形态（TestHelper.GenerateMapper → result.Diagnostics / DiagnosticsByDescriptorId，
//   参 DiagnosticTests / NCMAP019FluentExemptionTests / ConvertBackUsingGenerationTests）。
// 业务逻辑说明（业务逻辑验证项）：
//   · NCMAP047 组——白名单下"带变换特性但未同身标注 [MapProperty]"是用户最易踩的空转形态
//     （特性写了却不生效），编译期 Error 拦截并给出两条出路；
//   · 019 组——[MapNoAutoMatch] 已入十一项属性级家族，无类锚时配置静默失效须由 019 承接；
//   · 007 组——白名单对"未映射源成员"是声明意图，抑制须覆盖默认形态与类级显式开报告两形态；
//   · F11 组——白名单特性不得改变双驱动冲突仲裁的既有行为。
using System.Linq;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests.Diagnostics;

/// <summary>
/// [MapNoAutoMatch] 白名单门诊断族用例（NCMAP047 / 019 承接 / 007 抑制 / F11 零交互 / 既有码零回归）
/// </summary>
public class NoAutoMatchDiagnosticsTests
{
    // ----------------------------------------------------------------
    // 夹具模板（__WHITELIST__ / __TRANSFORM__ 两处插槽，避免多份近似常量漂移）
    // ----------------------------------------------------------------

    private const string PairTemplate = """
        using NCMapper;
        using NCMapper.Attributes;
        namespace Ncm.Nam
        {
            public static class NamConverter { public static string Conv(string v) => v; }

            public class NamEntity
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";
                public string Extra { get; set; } = "";
            }

            [MapFrom(typeof(NamEntity))]
            __WHITELIST__
            public class NamDto
            {
                [MapProperty(typeof(NamEntity), nameof(NamEntity.Id))]
                public int Id { get; set; }

                __TRANSFORM__
                public string Name { get; set; } = "";

                public static bool NamGuard(string v) => true;
            }
        }
        """;

    private static string Pair(string whitelistMarker, string transformAttribute)
        => PairTemplate.Replace("__WHITELIST__", whitelistMarker).Replace("__TRANSFORM__", transformAttribute);

    private static string On(string transformAttribute) => Pair("[MapNoAutoMatch]", transformAttribute);
    private static string Off(string transformAttribute) => Pair("", transformAttribute);

    // ================================================================
    // ① NCMAP047 —— 变换特性族逐一枚举（Convert / Mask / MapFormat / MapWhen）
    // ================================================================

    [Theory]
    [InlineData("[Convert(typeof(NamConverter), nameof(NamConverter.Conv))]")]  // 变换：类型+方法双参形态
    [InlineData("[Mask(typeof(NCMapper.MaskPhone))]")]                          // 脱敏
    [InlineData("[MapFormat(\"G\")]")]                                          // 格式化
    [InlineData("[MapWhen(nameof(NamDto.NamGuard))]")]                          // 条件守卫
    public void NCMAP047_TransformAttributeUnderWhitelist_ProducedAndPropertyNotMapped(string transform)
    {
        var result = TestHelper.GenerateMapper(On(transform));

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "NCMAP047");
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, diag.Severity);
        Assert.Contains("Name", diag.GetMessage(), System.StringComparison.Ordinal);

        // 语义面：该属性既不映射也不入候选（门在 AllTargetProperties.Add 之前）
        var body = result.Methods["Map_NamEntity_To_NamDto"][0].Body;
        Assert.Contains("Id = src.Id", body, System.StringComparison.Ordinal);   // 显式声明照常
        Assert.DoesNotContain("Name = src.Name", body);                          // 变换特性不复活自动匹配
    }

    /// <summary>
    /// 047 消息文案含两条出路（评审 002-9.5 用户落差消解：补声明 / 移除变换特性）。
    /// </summary>
    [Fact]
    public void NCMAP047_MessageCarriesTwoWaysOut()
    {
        var result = TestHelper.GenerateMapper(On("[Mask(typeof(NCMapper.MaskPhone))]"));
        var message = result.Diagnostics.Single(d => d.Id == "NCMAP047").GetMessage();

        Assert.Contains("MapProperty", message, System.StringComparison.Ordinal);
        Assert.Contains("MapNoAutoMatch", message, System.StringComparison.Ordinal);
    }

    // ================================================================
    // ② NCMAP047 负例族 —— 不越界误报（显式声明豁免 / 非判定族 / 白名单关）
    // ================================================================

    [Fact]
    public void NCMAP047_NotProduced_WhenSamePropertyHasMapProperty()
    {
        var source = On("[MapProperty(typeof(NamEntity), nameof(NamEntity.Name))]\n        [Mask(typeof(NCMapper.MaskPhone))]");
        var result = TestHelper.GenerateMapper(source);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP047");
        var body = result.Methods["Map_NamEntity_To_NamDto"][0].Body;
        Assert.Contains("Name", body, System.StringComparison.Ordinal);   // 显式声明 + 变换照常生效
    }

    [Theory]
    [InlineData("[MapIgnore]")]                                            // 忽略声明（非变换族）
    [InlineData("[ConvertBack(typeof(NamConverter), nameof(NamConverter.Conv))]")] // 反向声明（D1 不管辖）
    [InlineData("[BeforeMapping]")]                                        // 生命周期钩子（类级语义，属性位标注由 019/自身规则管辖）
    public void NCMAP047_NotProduced_ForNonTransformAttributeFamily(string attribute)
    {
        var result = TestHelper.GenerateMapper(On(attribute));
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP047");
    }

    [Fact]
    public void NCMAP047_NotProduced_WhenWhitelistOff()
    {
        var result = TestHelper.GenerateMapper(Off("[Mask(typeof(NCMapper.MaskPhone))]"));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP047");
        // 白名单关：变换特性 + 同名自动匹配照常（既有行为零回归）
        var body = result.Methods["Map_NamEntity_To_NamDto"][0].Body;
        Assert.Contains("Name", body, System.StringComparison.Ordinal);
    }

    // ================================================================
    // ③ NCMAP019 家族承接 —— 无类锚的 [MapNoAutoMatch] 由 019 Error 拦截
    // ================================================================

    /// <summary>
    /// 无 [MapFrom] 无 Fluent 声明的类标注 [MapNoAutoMatch] → NCMAP019 Error（十一项家族扩列直证）；
    /// 同型 Dto 命名启发式 NCMAP042 不双报（命中家族者归 019）。
    /// </summary>
    [Fact]
    public void NCMAP019_MapNoAutoMatchWithoutAnchor_Produced_NotSplitTo042()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            namespace Ncm.Nam019
            {
                public class AnchorEntity { public int Id { get; set; } }

                [MapNoAutoMatch]
                public class OrphanDto
                {
                    public int Id { get; set; }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "NCMAP019");
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, diag.Severity);
        Assert.Contains("OrphanDto", diag.GetMessage(), System.StringComparison.Ordinal);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP042");
    }

    /// <summary>
    /// Fluent 声明豁免（既有 019 豁免臂对新增家族成员同样生效）：无 [MapFrom] 但有 For&lt;S,T&gt;()
    /// 且 DTO 标注 [MapNoAutoMatch] → 不报 019。
    /// </summary>
    [Fact]
    public void NCMAP019_FluentDeclaredPair_WithMapNoAutoMatch_Exempt()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            namespace Ncm.Nam019f
            {
                public class FluentEntity { public int Id { get; set; } }
                [MapNoAutoMatch]
                public class FluentDto { public int Id { get; set; } }

                [MapperConfig]
                public class FluentMapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<FluentEntity, FluentDto>().Member(d => d.Id).From(s => s.Id);
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP019");
    }

    // ================================================================
    // ④ NCMAP007 语义级抑制 —— 默认形态 / 类级显式开报告 / 对照组
    // ================================================================

    [Fact]
    public void NCMAP007_Suppressed_UnderWhitelist_DefaultOptions()
    {
        var result = TestHelper.GenerateMapper(On(""));
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP007");
    }

    [Fact]
    public void NCMAP007_Suppressed_UnderWhitelist_ClassLevelExplicitReport()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            namespace Ncm.Nam007
            {
                public class OptEntity { public int Id { get; set; } public string Extra { get; set; } = ""; }

                [MapFrom(typeof(OptEntity))]
                [MapNoAutoMatch]
                [NCMapperOptions(ReportUnmappedSourceMembers = true)]
                public class OptDto
                {
                    [MapProperty(typeof(OptEntity), nameof(OptEntity.Id))]
                    public int Id { get; set; }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP007");
    }

    /// <summary>
    /// 对照组（排除恒绿）：白名单关 + 类级开报告 → NCMAP007 必产出（证明上述零产出由白名单抑制臂所致）。
    /// </summary>
    [Fact]
    public void NCMAP007_Produced_WhenWhitelistOff_ClassLevelExplicitReport()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            namespace Ncm.Nam007c
            {
                public class Opt2Entity { public int Id { get; set; } public string Extra { get; set; } = ""; }

                [MapFrom(typeof(Opt2Entity))]
                [NCMapperOptions(ReportUnmappedSourceMembers = true)]
                public class Opt2Dto
                {
                    [MapProperty(typeof(Opt2Entity), nameof(Opt2Entity.Id))]
                    public int intId_Holder { get; set; }
                    public int Id { get; set; }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Contains(result.Diagnostics, d => d.Id == "NCMAP007");
    }

    // ================================================================
    // ⑤ F11 零交互 —— 白名单特性不改变双驱动冲突仲裁
    // ================================================================

    /// <summary>
    /// 同一对既走 [MapFrom]+[MapNoAutoMatch] 又走 Fluent For&lt;S,T&gt;() → NCMAP_F11 产出与不带
    /// 白名单标注时一致（白名单不得改变双驱动仲裁行为——F11 零交互回归）。
    /// </summary>
    [Fact]
    public void F11_DualDriverConflict_UnchangedByWhitelistAttribute()
    {
        const string WithWhitelist = """
            using NCMapper;
            using NCMapper.Attributes;
            namespace Ncm.F11a
            {
                public class F11Entity { public int Id { get; set; } }
                [MapFrom(typeof(F11Entity))]
                [MapNoAutoMatch]
                public class F11Dto { public int Id { get; set; } }

                [MapperConfig]
                public class F11Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<F11Entity, F11Dto>().Member(d => d.Id).From(s => s.Id);
                }
            }
            """;
        const string WithoutWhitelist = """
            using NCMapper;
            using NCMapper.Attributes;
            namespace Ncm.F11b
            {
                public class F11Entity { public int Id { get; set; } }
                [MapFrom(typeof(F11Entity))]
                public class F11Dto { public int Id { get; set; } }

                [MapperConfig]
                public class F11Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<F11Entity, F11Dto>().Member(d => d.Id).From(s => s.Id);
                }
            }
            """;

        var with = TestHelper.GenerateMapper(WithWhitelist).Diagnostics.Count(d => d.Id == "NCMAP_F11");
        var without = TestHelper.GenerateMapper(WithoutWhitelist).Diagnostics.Count(d => d.Id == "NCMAP_F11");

        Assert.Equal(without, with);
        Assert.True(with > 0, "F11 双驱动冲突对照组须产出（否则本用例恒真——白名单侧未真正构成双驱动）");
    }

    // ================================================================
    // ⑥ 既有码零回归抽查 —— 白名单不吞既有诊断、空白名单形态可感知
    // ================================================================

    /// <summary>
    /// 空白名单对（无任何显式声明）→ 既有 NCMAP003（NoPropertiesMapped）照常产出
    ///（批内 Code Review #5 定桩：该形态非缺陷，留档防后续误判为回归）。
    /// </summary>
    [Fact]
    public void EmptyWhitelist_Pair_StillProducesNoPropertiesMapped()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            namespace Ncm.Empty03
            {
                public class E03Entity { public int Id { get; set; } }
                [MapFrom(typeof(E03Entity))]
                [MapNoAutoMatch]
                public class E03Dto { public int Id { get; set; } }
            }
            """;
        var result = TestHelper.GenerateMapper(source);

        Assert.Contains(result.Diagnostics, d => d.Id == "NCMAP003");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP047");
    }

    /// <summary>
    /// 白名单对不吞既有诊断：同身 [MapProperty] + [MapIgnore] 矛盾（NCMAP022）照常产出
    ///（既有治理先行，白名单门不遮蔽矛盾信号）。
    /// </summary>
    [Fact]
    public void ExistingDiagnostics_NotShadowed_UnderWhitelist()
    {
        var source = On("[MapProperty(typeof(NamEntity), nameof(NamEntity.Name), OverrideIgnore = true)]\n        [MapIgnore]");
        var result = TestHelper.GenerateMapper(source);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP047");
        Assert.Contains(result.Diagnostics, d => d.Id == "NCMAP022");
    }

    // ================================================================
    // ⑦ 生成侧边界：实体侧标注不激活 + 继承腿（D5① 符号腿）
    // ================================================================

    /// <summary>
    /// 生成侧对侧标注负例：<c>[MapNoAutoMatch]</c> 标在**实体（源）侧**时不激活白名单——
    /// 提取链只读目标类型继承链（<c>HasMapNoAutoMatchInHierarchy(targetSymbol)</c>），
    /// 同名自动匹配照常、NCMAP047 不产出。
    /// <para>与反射腿"两侧对称探测"的口径差异属"生成期声明语义 vs 运行时探测语义"分界
    ///（反向映射时 DTO 侧即源侧，运行时须任一侧生效）——登记。</para>
    /// </summary>
    [Fact]
    public void EntitySideAnnotation_DoesNotActivateWhitelist()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            namespace Ncm.Side
            {
                [MapNoAutoMatch]
                public class SideEntity { public int Id { get; set; } public string Name { get; set; } = ""; }

                [MapFrom(typeof(SideEntity))]
                public class SideDto
                {
                    public int Id { get; set; }
                    [Mask(typeof(NCMapper.MaskPhone))]
                    public string Name { get; set; } = "";
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP047");
        var body = result.Methods["Map_SideEntity_To_SideDto"][0].Body;
        Assert.Contains("Name", body, System.StringComparison.Ordinal);   // 自动匹配照常
    }

    /// <summary>
    /// D5① 符号腿继承：DTO 派生自带 <c>[MapNoAutoMatch]</c> 的基类（自身不重复标注）——
    /// 提取链经 <c>GetTypeAndBaseTypes</c> 继承链遍历命中，白名单在生成侧生效
    ///（未声明属性不入候选、变换特性触发 NCMAP047）。
    /// </summary>
    [Fact]
    public void Inherited_Leg1_SymbolSide_DerivedDtoInheritsWhitelist()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            namespace Ncm.Inh
            {
                public class InhEntity { public int Id { get; set; } public string Name { get; set; } = ""; }

                [MapNoAutoMatch]
                public class InhBaseDto
                {
                    [MapProperty(typeof(InhEntity), nameof(InhEntity.Id))]
                    public int Id { get; set; }
                }

                [MapFrom(typeof(InhEntity))]
                public class InhDerivedDto : InhBaseDto
                {
                    [Mask(typeof(NCMapper.MaskPhone))]
                    public string Name { get; set; } = "";
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "NCMAP047");
        Assert.Contains("Name", diag.GetMessage(), System.StringComparison.Ordinal);

        var body = result.Methods["Map_InhEntity_To_InhDerivedDto"][0].Body;
        Assert.Contains("Id = src.Id", body, System.StringComparison.Ordinal);   // 基类显式声明照常
        Assert.DoesNotContain("Name = src.Name", body);                          // 继承白名单：未声明不接
    }

    /// <summary>
    /// Q5 探针（任务书决策点 5）：[MapSubclass] 子对 × 基类白名单继承。
    /// <para><b>三列结论（评审 002-9.8 格式，搬运登记簿 #18）</b>
    /// 结论 = 子对（<c>Q5Dog → Q5DogDto</c>）**不继承**基类 DTO 上的 <c>[MapNoAutoMatch]</c>——
    /// 子对由 <c>[MapSubclass]</c> 注册独立提取，不经 <c>ExtractMappingCore</c> 的白名单读取点
    ///（<c>HasMapNoAutoMatchInHierarchy</c> 未被调用），故 <c>Breed</c>/<c>Name</c> 在子对仍自动匹配；
    /// 缺口可感知 = 是（用户若期望基类白名单覆盖多态子对，实际不覆盖——定桩当前实态，非修复断言）。</para>
    /// <para>处置 = 登记落账 #18；本册不扩改动面
    ///（X3/X4 同款：改动子对提取链会波及 MapSubclass 全族）。</para>
    /// </summary>
    [Fact]
    public void Q5_SubclassPair_DoesNotInheritBaseWhitelist_Measured()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            namespace Ncm.Q5
            {
                public class Q5Animal { public string Name { get; set; } = ""; }
                public class Q5Dog : Q5Animal { public string Breed { get; set; } = ""; }

                [MapFrom(typeof(Q5Animal))]
                [MapNoAutoMatch]
                [MapSubclass(typeof(Q5Dog), typeof(Q5DogDto))]
                public class Q5AnimalDto
                {
                    [MapProperty(typeof(Q5Animal), nameof(Q5Animal.Name))]
                    public string Name { get; set; } = "";
                }
                public class Q5DogDto : Q5AnimalDto { public string Breed { get; set; } = ""; }
            }
            """;
        var result = TestHelper.GenerateMapper(source);

        // 基对：白名单生效（仅显式声明参与）
        var baseBody = result.Methods["Map_Q5Animal_To_Q5AnimalDto"][0].Body;
        Assert.Contains("Name = src.Name", baseBody, System.StringComparison.Ordinal);   // 显式声明
        Assert.DoesNotContain("Breed", baseBody);

        // 子对：白名单不继承——同名自动匹配照常（定桩当前实态）
        var subBody = result.Methods["Map_Q5Dog_To_Q5DogDto"][0].Body;
        Assert.Contains("Breed = src.Breed", subBody, System.StringComparison.Ordinal);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP047");
    }
}
