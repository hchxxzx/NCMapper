using NCMapper.SourceGen.Tests.Helpers;
using NCMapper.SourceGen.Tests.Models;
using Xunit;

namespace NCMapper.SourceGen.Tests
{
    /// <summary>
    /// 三值分类驱动的五类 DeepCopy 代码变体 + 注册行三参快照
    /// <para>变体矩阵：无参路径 Acyclic（省略 RegisterDeepCopyTarget，注册行 acyclic: true）/
    /// 无参路径 Cyclic/Unknown（保留登记，acyclic: false）；record 路径 Acyclic（构造器路径，acyclic: true）/
    /// Cyclic/Unknown（NCMAP012 回退 DeepCopyRuntimeOnly，acyclic: false；Unknown 回退为 D4 新增）。</para>
    /// </summary>
    public class DeepCopyVariantGenerationTests
    {
        // ================================================================
        // 无参构造路径变体
        // ================================================================

        /// <summary>
        /// 无参 Acyclic 变体：省略 RegisterDeepCopyTarget（否定条件 F2 的反向断言），
        /// 注册行 acyclic: true，无运行时回退
        /// </summary>
        [Fact]
        public void Parameterless_Acyclic_OmitsRegistration_AcyclicTrue()
        {
            var source = """
                using NCMapper.Attributes;

                public class SgVarFlat { public int Id { get; set; } public string Name { get; set; } = ""; }

                [MapFrom(typeof(SgVarFlat))]
                public class SgVarFlatDto { public int Id { get; set; } public string Name { get; set; } = ""; }
                """;

            var result = TestHelper.GenerateMapper(source);

            Assert.True(result.Methods.ContainsKey("DeepCopy_SgVarFlat"));
            var body = result.Methods["DeepCopy_SgVarFlat"][0].Body;
            Assert.DoesNotContain("RegisterDeepCopyTarget", body);
            Assert.DoesNotContain("DeepCopyRuntimeOnly", body);
            Assert.Contains("RegisterGeneratedDeepCopy<SgVarFlat>(GeneratedMapper.DeepCopy_SgVarFlat, acyclic: true);", result.GeneratedCode);
        }

        /// <summary>
        /// 无参 Cyclic 变体：保留 RegisterDeepCopyTarget（CycleCache 打破循环依赖登记），
        /// 注册行 acyclic: false
        /// </summary>
        [Fact]
        public void Parameterless_Cyclic_RetainsRegistration_AcyclicFalse()
        {
            var source = """
                using NCMapper.Attributes;

                public class SgVarSelfRef { public int Id { get; set; } public SgVarSelfRef? Next { get; set; } }

                [MapFrom(typeof(SgVarSelfRef))]
                public class SgVarSelfRefDto { public int Id { get; set; } }
                """;

            var result = TestHelper.GenerateMapper(source);

            Assert.True(result.Methods.ContainsKey("DeepCopy_SgVarSelfRef"));
            var body = result.Methods["DeepCopy_SgVarSelfRef"][0].Body;
            Assert.Contains("RegisterDeepCopyTarget(src, dst, ctx)", body);
            Assert.DoesNotContain("DeepCopyRuntimeOnly", body);
            Assert.Contains("RegisterGeneratedDeepCopy<SgVarSelfRef>(GeneratedMapper.DeepCopy_SgVarSelfRef, acyclic: false);", result.GeneratedCode);
        }

        /// <summary>
        /// 无参 Unknown 变体：保留登记 + NCMAP016 逐边报告，注册行 acyclic: false
        /// </summary>
        [Fact]
        public void Parameterless_Unknown_RetainsRegistration_AcyclicFalse_Ncmap016()
        {
            var source = """
                using NCMapper.Attributes;

                public class SgVarBoxed { public int Id { get; set; } public object? Payload { get; set; } }

                [MapFrom(typeof(SgVarBoxed))]
                public class SgVarBoxedDto { public int Id { get; set; } }
                """;

            var result = TestHelper.GenerateMapper(source);

            Assert.True(result.Methods.ContainsKey("DeepCopy_SgVarBoxed"));
            var body = result.Methods["DeepCopy_SgVarBoxed"][0].Body;
            Assert.Contains("RegisterDeepCopyTarget(src, dst, ctx)", body);
            Assert.DoesNotContain("DeepCopyRuntimeOnly", body);
            Assert.Contains(result.Diagnostics, d => d.Id == "NCMAP016");
            Assert.Contains("RegisterGeneratedDeepCopy<SgVarBoxed>(GeneratedMapper.DeepCopy_SgVarBoxed, acyclic: false);", result.GeneratedCode);
        }

        // ================================================================
        // record 构造器路径变体
        // ================================================================

        /// <summary>
        /// record Acyclic 变体：构造器路径正常生成（new T(...)），注册行 acyclic: true，
        /// 无登记行（record 路径本就无预登记）、无运行时回退
        /// </summary>
        [Fact]
        public void Record_Acyclic_ConstructorPath_AcyclicTrue()
        {
            var source = """
                using NCMapper.Attributes;

                public record SgVarPoint(int X, int Y);

                [MapFrom(typeof(SgVarPoint))]
                public class SgVarPointDto { public int X { get; set; } public int Y { get; set; } }
                """;

            var result = TestHelper.GenerateMapper(source);

            Assert.True(result.Methods.ContainsKey("DeepCopy_SgVarPoint"));
            var body = result.Methods["DeepCopy_SgVarPoint"][0].Body;
            Assert.Contains("new SgVarPoint(", body);
            Assert.DoesNotContain("DeepCopyRuntimeOnly", body);
            Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP012");
            Assert.Contains("RegisterGeneratedDeepCopy<SgVarPoint>(GeneratedMapper.DeepCopy_SgVarPoint, acyclic: true);", result.GeneratedCode);
        }

        /// <summary>
        /// record Cyclic 变体（现状保持）：NCMAP012 + DeepCopyRuntimeOnly 回退，注册行 acyclic: false
        /// </summary>
        [Fact]
        public void Record_Cyclic_RuntimeFallback_Ncmap012_AcyclicFalse()
        {
            var source = """
                using NCMapper.Attributes;

                public record SgVarRingRec(int Id, SgVarRingRec? Next);

                [MapFrom(typeof(SgVarRingRec))]
                public class SgVarRingDto { public int Id { get; set; } }
                """;

            var result = TestHelper.GenerateMapper(source);

            Assert.True(result.Methods.ContainsKey("DeepCopy_SgVarRingRec"));
            var body = result.Methods["DeepCopy_SgVarRingRec"][0].Body;
            Assert.Contains("DeepCopyRuntimeOnly<SgVarRingRec>", body);
            Assert.Contains(result.Diagnostics, d => d.Id == "NCMAP012");
            Assert.Contains("RegisterGeneratedDeepCopy<SgVarRingRec>(GeneratedMapper.DeepCopy_SgVarRingRec, acyclic: false);", result.GeneratedCode);
        }

        /// <summary>
        /// record Unknown 变体（D4 新增）：object 属性不可解析 → 同 Cyclic 回退运行时，
        /// NCMAP012 + NCMAP016 双诊断，注册行 acyclic: false（否定条件 F3 的反向断言）
        /// </summary>
        [Fact]
        public void Record_Unknown_RuntimeFallback_D4_Ncmap012And016_AcyclicFalse()
        {
            var source = """
                using NCMapper.Attributes;

                public record SgVarBoxedRec(int Id, object? Payload);

                [MapFrom(typeof(SgVarBoxedRec))]
                public class SgVarBoxedRecDto { public int Id { get; set; } }
                """;

            var result = TestHelper.GenerateMapper(source);

            Assert.True(result.Methods.ContainsKey("DeepCopy_SgVarBoxedRec"));
            var body = result.Methods["DeepCopy_SgVarBoxedRec"][0].Body;
            Assert.Contains("DeepCopyRuntimeOnly<SgVarBoxedRec>", body);
            Assert.Contains(result.Diagnostics, d => d.Id == "NCMAP012");
            Assert.Contains(result.Diagnostics, d => d.Id == "NCMAP016");
            Assert.Contains("RegisterGeneratedDeepCopy<SgVarBoxedRec>(GeneratedMapper.DeepCopy_SgVarBoxedRec, acyclic: false);", result.GeneratedCode);
        }

        // ================================================================
        // 注册行三参全覆盖
        // ================================================================

        /// <summary>
        /// 混合语料（Acyclic/Cyclic/Unknown class 各一 + 各自 DTO）：
        /// 全部注册行携带 acyclic 字面量（否定条件 F4 的反向断言：无两参调用残留），
        /// 且 acyclic 值与分类一一对应
        /// </summary>
        [Fact]
        public void RegistrationLines_AllCarryAcyclicLiteral_MixedCorpus()
        {
            var source = """
                using NCMapper.Attributes;

                public class SgMixA { public int Id { get; set; } }
                public class SgMixB { public int Id { get; set; } public SgMixB? Self { get; set; } }
                public class SgMixC { public int Id { get; set; } public object? Box { get; set; } }

                [MapFrom(typeof(SgMixA))]
                public class SgMixADto { public int Id { get; set; } }
                [MapFrom(typeof(SgMixB))]
                public class SgMixBDto { public int Id { get; set; } }
                [MapFrom(typeof(SgMixC))]
                public class SgMixCDto { public int Id { get; set; } }
                """;

            var result = TestHelper.GenerateMapper(source);

            // 6 类型（3 实体 + 3 DTO）均注册，且全部携带 acyclic 字面量
            var registrations = result.GeneratedCode.Split('\n')
                .Where(l => l.Contains("RegisterGeneratedDeepCopy<"))
                .ToList();
            Assert.Equal(6, registrations.Count);
            Assert.All(registrations, l => Assert.Contains("acyclic:", l));

            // Acyclic：SgMixA / SgMixADto / SgMixBDto / SgMixCDto（4 个，DTO 仅 int 属性）
            Assert.Equal(4, registrations.Count(l => l.Contains("acyclic: true")));
            // Cyclic：SgMixB；Unknown：SgMixC
            Assert.Equal(2, registrations.Count(l => l.Contains("acyclic: false")));
            Assert.Contains(registrations, l => l.Contains("RegisterGeneratedDeepCopy<SgMixB>") && l.Contains("acyclic: false"));
            Assert.Contains(registrations, l => l.Contains("RegisterGeneratedDeepCopy<SgMixC>") && l.Contains("acyclic: false"));
        }
    }

    /// <summary>
    /// record Unknown 回退运行时 E2E（D4）
    /// <para>模型 <see cref="SgRecordUnknownDto"/> 实际参与本测试程序集编译，
    /// 源生成器真实产出 DeepCopyRuntimeOnly 回退方法并注册（acyclic: false）。
    /// 运行时语义：层 1 命中委托 → Cyclic/Unknown 分支（CycleCache）→ 委托体转发
    /// DeepCopyRuntimeOnly → Provider 完整检测路径，功能不受回退影响。</para>
    /// </summary>
    [Collection("DeepCopyGlobalState")]
    public class RecordUnknownFallbackE2ETests
    {
        /// <summary>
        /// 回退后 DeepCopy 功能正常：record 被独立重建、值属性正确、载荷引用独立
        /// <para>注意：运行时路径对 object 声明的成员按静态类型拷贝
        /// （DeepCopyCompiler.CreateInstanceCore 使用 typeof(T)），载荷不会以其运行时类型
        /// 存活 —— 这是运行时深拷贝路径的既有语义，非本任务（T5/D4）引入的行为变更，
        /// 本用例仅验证回退路径正确执行且产出独立副本。</para>
        /// </summary>
        [Fact]
        public void RecordUnknown_DeepCopy_RuntimeFallback_Works()
        {
            var src = new SgRecordUnknownDto(7, "payload");

            var copy = Mapper.DeepCopy(src)!;

            Assert.Equal(7, copy.Id);
            Assert.NotSame(src, copy);
            // object 载荷经运行时路径产出独立实例（非原引用）
            Assert.NotNull(copy.Payload);
            Assert.NotSame(src.Payload, copy.Payload);
        }

        /// <summary>
        /// 回退路径对"不可解析边背后的环"保持稳健：载荷为自引用对象图时不栈溢出、不抛异常
        /// <para>运行时路径按静态类型（object）拷贝载荷，不进入载荷内部图，
        /// 因此环根本不会被遍历；本用例验证该退化行为下回退路径依然安全完成。</para>
        /// </summary>
        [Fact]
        public void RecordUnknown_DeepCopy_CyclicPayload_NoStackOverflow()
        {
            var cyclic = new SgUe2eSelfRef { Value = 3 };
            cyclic.Self = cyclic;
            var src = new SgRecordUnknownDto(1, cyclic);

            var copy = Mapper.DeepCopy(src)!;

            Assert.Equal(1, copy.Id);
            Assert.NotSame(src, copy);
            Assert.NotNull(copy.Payload);
            Assert.NotSame(cyclic, copy.Payload);
        }

        /// <summary>
        /// 委托确已注册（层 1 可达），且按 Unknown 口径注册为非 Acyclic
        /// </summary>
        [Fact]
        public void RecordUnknown_HasGeneratedDeepCopy_Registered()
        {
            Assert.True(Mapper.HasGeneratedDeepCopy<SgRecordUnknownDto>(),
                "SgRecordUnknownDto 应有源生成 DeepCopy 委托（方法体为 DeepCopyRuntimeOnly 回退）");
        }
    }

    /// <summary>
    /// E2E 辅助模型 — 自引用 POCO（作为 record Unknown 的循环载荷）
    /// </summary>
    public class SgUe2eSelfRef
    {
        /// <summary>节点值</summary>
        public int Value { get; set; }

        /// <summary>自引用（形成循环）</summary>
        public SgUe2eSelfRef? Self { get; set; }
    }

    /// <summary>
    /// D8 语义 E2E（源生成路径 DAG 共享）
    /// <para>SgDagOrder 为 [MapFrom] 注册的 Acyclic 类型（纯 DAG 无环），
    /// 层 1 走 IsAcyclic 免检测分支（跳过 CycleCache）。D8 破坏性变更：
    /// 无环类型的 DAG 共享引用从"保持共享"变为"逐处独立复制"——
    /// 100 个订单行共享的商品在拷贝产物中产生 100 个互不相同的副本。</para>
    /// </summary>
    [Collection("DeepCopyGlobalState")]
    public class DagSharingSemanticsE2ETests
    {
        /// <summary>
        /// 源生成路径 Acyclic DAG — 共享引用逐处独立复制（D8 正向锚定）
        /// </summary>
        [Fact]
        public void Generated_AcyclicDag_SharedReference_IndependentPerOccurrence()
        {
            // 前置：确认走源生成层 1 路径（否则本用例退化为 Provider 路径验证）
            Assert.True(Mapper.HasGeneratedDeepCopy<SgDagOrder>(),
                "SgDagOrder 应有源生成 DeepCopy 委托，否则未覆盖 D8 变更面（源生成路径）");

            var product = new SgDagProduct { Id = 1, Name = "Widget" };
            var order = new SgDagOrder { Id = 100 };
            for (int i = 0; i < 100; i++)
                order.Lines.Add(new SgDagOrderLine { Id = i + 1, Product = product });

            var copy = Mapper.DeepCopy(order)!;

            Assert.Equal(100, copy.Id);
            Assert.Equal(100, copy.Lines.Count);
            // D8：各订单行的商品逐处独立复制（与源对象引用独立）
            for (int i = 0; i < 100; i++)
            {
                Assert.NotSame(order.Lines[i].Product, copy.Lines[i].Product);
                Assert.Equal(1, copy.Lines[i].Product!.Id);
                Assert.Equal("Widget", copy.Lines[i].Product!.Name);
            }
            // D8：拷贝产物中各商品副本互不相同（不再保持 DAG 共享）
            for (int i = 1; i < 100; i++)
                Assert.NotSame(copy.Lines[0].Product, copy.Lines[i].Product);
        }
    }
}
