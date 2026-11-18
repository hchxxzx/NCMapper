using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace NCMapper.SourceGen.Tests
{
    /// <summary>
    /// MapCtorParam 绑定编辑增量检出回归防线（W2' v1.2 翻转）。
    /// <para><b>定性背景（评审 A1）</b>：上游审计 P1-1"绑定变更不触发增量再生成"为误报——绑定变更
    /// 检出已由 DeepCopy 通道覆盖（Materialize.cs:29/:50/:919-924 双点注入 → DeepCopyNodeShape.Equals:1266
    /// PublicConstructors.SequenceEqual → ConstructorRef.Equals:301-308 → ConstructorParameterRef.Equals:271-278
    /// → MappingSnapshot.Equals:1496 DeepCopyNodes 腿）。本任务将增量两跑测试翻转为<b>现状回归防线</b>：
    /// 锁定"四类绑定编辑 → 同 driver 第二跑产物/诊断面反映新输入"的现行为，防未来 DeepCopy 图重构
    /// （如"叶子类型不入节点"）静默破坏该<b>隐式</b>检出通道（X7 备案重评触发器）。</para>
    /// <para>防线有效性由变异演练证明（执行期）：临时注释 Materialize.cs:919-924 SourcePropertyName
    /// 填充（或 DeepCopyNodeShape.Equals:1266 PublicConstructors 腿）→ 本类用例必红 → 还原复绿。
    /// 四类编辑的观测面：改绑定/ctor 形态变化/删绑定 → 产物实参形态；形参改名 → 绑定未同步时
    /// 校验重跑出 F52（Name 参与 Equals 的行为锚——产物实参为位置形态不可观测改名本身）。</para>
    /// </summary>
    public class IncrementalCtorParamTests
    {
        /// <summary>同 CSharpGeneratorDriver 实例两跑（增量管线 transform 缓存比较发生处）——
        /// 返回两跑主生成器产物与两跑诊断。</summary>
        private static (string gen1, string gen2, Diagnostic[] diags1, Diagnostic[] diags2) RunTwoPass(string source1, string source2)
        {
            var compilation1 = CSharpCompilation.Create(
                "IncrCtorParam",
                new[] { CSharpSyntaxTree.ParseText(source1, cancellationToken: TestContext.Current.CancellationToken) },
                DiagnosticAndCacheTests.BuildMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new[] { new NCMapperSourceGenerator().AsSourceGenerator() });
            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation1, out var updated1, out var diags1, TestContext.Current.CancellationToken);
            var gen1 = GetGenerated(updated1, compilation1);

            var compilation2 = CSharpCompilation.Create(
                "IncrCtorParam",
                new[] { CSharpSyntaxTree.ParseText(source2, cancellationToken: TestContext.Current.CancellationToken) },
                DiagnosticAndCacheTests.BuildMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation2, out var updated2, out var diags2, TestContext.Current.CancellationToken);
            var gen2 = GetGenerated(updated2, compilation2);
            return (gen1, gen2, diags1.ToArray(), diags2.ToArray());
        }

        private static string GetGenerated(Compilation updated, Compilation original) =>
            updated.SyntaxTrees
                .Skip(original.SyntaxTrees.Count())
                .FirstOrDefault(t => t.FilePath.Contains("NCMapper_Generated_GeneratedMapper.g.cs"))
                ?.ToString() ?? "";

        /// <summary>用例①（改绑定，评审 A1 探针同款）：Run1 绑定 NickName → Run2 改绑 AltName——
        /// 先验锚 Run1 产物含 src.NickName（防 vacuous pass，经验 #67），断言 Run2 产物含 src.AltName。
        /// DeepCopy 通道失效（变异）时 Run2 缓存命中旧快照 → 产物仍 src.NickName → 红。</summary>
        [Fact]
        public void Rebind_Run2ProductReflectsNewBinding()
        {
            const string source1 = """
                using NCMapper;
                public class FcpI1Entity { public string NickName { get; set; } = ""; public string AltName { get; set; } = ""; }
                public class FcpI1Dto
                {
                    public string Nick { get; }
                    public FcpI1Dto(string nick) { Nick = nick; }
                }
                [MapperConfig]
                public class FcpI1Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<FcpI1Entity, FcpI1Dto>()
                           .MapCtorParam("nick", s => s.NickName);
                    }
                }
                """;
            const string source2 = """
                using NCMapper;
                public class FcpI1Entity { public string NickName { get; set; } = ""; public string AltName { get; set; } = ""; }
                public class FcpI1Dto
                {
                    public string Nick { get; }
                    public FcpI1Dto(string nick) { Nick = nick; }
                }
                [MapperConfig]
                public class FcpI1Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<FcpI1Entity, FcpI1Dto>()
                           .MapCtorParam("nick", s => s.AltName);
                    }
                }
                """;
            var (gen1, gen2, _, _) = RunTwoPass(source1, source2);
            Assert.Contains("new FcpI1Dto(src.NickName)", gen1); // 先验锚
            Assert.Contains("new FcpI1Dto(src.AltName)", gen2);  // 回归防线：Run2 反映新绑定
        }

        /// <summary>用例②（形参改名，绑定未同步）：Run2 形参 nick→nickRenamed 而绑定声明仍写
        /// "nick" → 校验重跑出 F52（提示可用参数集）。Name 参与 ConstructorParameterRef.Equals
        /// (:274)——若 Name 环失效（变异/未来重构），Run2 缓存命中 → 不重跑校验 → 无 F52 → 红。
        /// （同步改名场景产物为位置实参形态、逐字节不变，无行为观测面——由本用例的 Name 行为锚承载。）</summary>
        [Fact]
        public void ParamRenameWithStaleBinding_Run2RerunsValidation_F52()
        {
            const string source1 = """
                using NCMapper;
                public class FcpI2Entity { public string NickName { get; set; } = ""; }
                public class FcpI2Dto
                {
                    public string Nick { get; }
                    public FcpI2Dto(string nick) { Nick = nick; }
                }
                [MapperConfig]
                public class FcpI2Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<FcpI2Entity, FcpI2Dto>()
                           .MapCtorParam("nick", s => s.NickName);
                    }
                }
                """;
            const string source2 = """
                using NCMapper;
                public class FcpI2Entity { public string NickName { get; set; } = ""; }
                public class FcpI2Dto
                {
                    public string Nick { get; }
                    public FcpI2Dto(string nickRenamed) { Nick = nickRenamed; }
                }
                [MapperConfig]
                public class FcpI2Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<FcpI2Entity, FcpI2Dto>()
                           .MapCtorParam("nick", s => s.NickName);
                    }
                }
                """;
            var (gen1, _, diags1, diags2) = RunTwoPass(source1, source2);
            Assert.DoesNotContain(diags1, d => d.Id == "NCMAP_F52"); // 先验锚：Run1 绑定合法零诊断
            Assert.Contains("new FcpI2Dto(src.NickName)", gen1);     // Run1 绑定生效
            Assert.Contains(diags2, d => d.Id == "NCMAP_F52");       // 回归防线：Run2 重跑检出改名失配
        }

        /// <summary>用例③（ctor 形态变化——增删重载/形参序列，定向子用例：.MapCtorParam 声明不动）：
        /// Run2 ctor 增第二形参 → 未绑定形参发射 default(类型)（BuildConstructorArgs :641/:1967 先例）。
        /// ConstructorRef.Parameters 序列参与 DeepCopyNodeShape.Equals（1266/:305-307）——若 ctor
        /// 形态环失效（变异），Run2 缓存命中 → 产物无 default(int) → 红。</summary>
        [Fact]
        public void CtorShapeChange_Run2ProductReflectsNewCtor()
        {
            const string source1 = """
                using NCMapper;
                public class FcpI3Entity { public string NickName { get; set; } = ""; }
                public class FcpI3Dto
                {
                    public string Nick { get; }
                    public FcpI3Dto(string nick) { Nick = nick; }
                }
                [MapperConfig]
                public class FcpI3Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<FcpI3Entity, FcpI3Dto>()
                           .MapCtorParam("nick", s => s.NickName);
                    }
                }
                """;
            const string source2 = """
                using NCMapper;
                public class FcpI3Entity { public string NickName { get; set; } = ""; }
                public class FcpI3Dto
                {
                    public string Nick { get; }
                    public FcpI3Dto(string nick, int extra) { Nick = nick; }
                }
                [MapperConfig]
                public class FcpI3Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<FcpI3Entity, FcpI3Dto>()
                           .MapCtorParam("nick", s => s.NickName);
                    }
                }
                """;
            var (gen1, gen2, _, _) = RunTwoPass(source1, source2);
            Assert.Contains("new FcpI3Dto(src.NickName)", gen1);          // 先验锚
            Assert.Contains("new FcpI3Dto(src.NickName, default(int))", gen2); // 回归防线：Run2 反映新 ctor 形态
        }

        /// <summary>用例④（删绑定）：Run2 移除 .MapCtorParam 声明 → 无绑定分支（行 15 先例——
        /// F5x 诊断零发射）+ nick 无同名 pm → 实参落 default(string)（641/:1967）。
        /// SourcePropertyName → null 环参与 ConstructorParameterRef.Equals（277）——若环失效（变异），
        /// Run2 缓存命中 → 产物仍 src.NickName → 红。</summary>
        [Fact]
        public void BindingRemoved_Run2FallsBackToUnboundShape()
        {
            const string source1 = """
                using NCMapper;
                public class FcpI4Entity { public string NickName { get; set; } = ""; }
                public class FcpI4Dto
                {
                    public string Nick { get; }
                    public FcpI4Dto(string nick) { Nick = nick; }
                }
                [MapperConfig]
                public class FcpI4Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<FcpI4Entity, FcpI4Dto>()
                           .MapCtorParam("nick", s => s.NickName);
                    }
                }
                """;
            const string source2 = """
                using NCMapper;
                public class FcpI4Entity { public string NickName { get; set; } = ""; }
                public class FcpI4Dto
                {
                    public string Nick { get; }
                    public FcpI4Dto(string nick) { Nick = nick; }
                }
                [MapperConfig]
                public class FcpI4Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<FcpI4Entity, FcpI4Dto>();
                    }
                }
                """;
            var (gen1, gen2, _, diags2) = RunTwoPass(source1, source2);
            Assert.Contains("new FcpI4Dto(src.NickName)", gen1);   // 先验锚
            Assert.DoesNotContain(diags2, d => d.Id.StartsWith("NCMAP_F5")); // 行 15 语义：无绑定 F5x 零发射
            Assert.Contains("new FcpI4Dto(default(string))", gen2);          // 回归防线：Run2 反映删绑定
            Assert.DoesNotContain("new FcpI4Dto(src.NickName)", gen2);
        }

        // ====================================================================
        // B4-04 白盒回归：ConstructorParameterRef 相等性补值比较
        // ====================================================================

        /// <summary>B4-04：仅 ExplicitDefaultValue 异值（同标志 true，值 1 vs 2）的双实例必须判不等
        /// ——缺陷态（标志位已比、缺值比较）全链相等 → Roslyn 输出级缓存陈旧命中 → 生成代码沿用旧
        /// 默认值字面量。GetHashCode 五项（Name/Type/SourcePropertyName/标志位/值——首评 P1-3 补两项）
        /// 对异值/异标志变异均改变。负向不误伤：同值同标志 Equals==true。链路层：含该参数的
        /// DeepCopyNodeShape 实例对 Equals==false（快照链最深叶子 → MappingSnapshot.Equals :1496
        /// DeepCopyNodes 腿逐级传导）。</summary>
        [Fact]
        public void ConstructorParameterRef_ExplicitDefaultValue_ParticipatesInEqualityAndHash()
        {
            static NCMapper.SourceGen.TypeRef MakeTypeRef(string equalityKey) => new()
            {
                FullName = equalityKey,
                EqualityKey = equalityKey,
            };
            static NCMapper.SourceGen.ConstructorParameterRef MakeParam(object? defaultValue, bool hasDefault) => new()
            {
                Name = "x",
                Type = MakeTypeRef("global::System.Int32"),
                HasExplicitDefaultValue = hasDefault,
                ExplicitDefaultValue = defaultValue,
            };

            var withDefault1 = MakeParam(1, hasDefault: true);
            var withDefault2 = MakeParam(2, hasDefault: true);
            var noDefault = MakeParam(1, hasDefault: false);
            var sameAs1 = MakeParam(1, hasDefault: true);

            // 修复核心断言（缺陷态：Equals 返回 true → 红）
            Assert.False(withDefault1.Equals(withDefault2));
            // 哈希两项参与（缺陷态：异值/异标志哈希相等 → 红）
            Assert.NotEqual(withDefault1.GetHashCode(), withDefault2.GetHashCode());
            Assert.NotEqual(withDefault1.GetHashCode(), noDefault.GetHashCode());
            // 负向：同值同标志不误伤
            Assert.True(withDefault1.Equals(sameAs1));
            Assert.Equal(withDefault1.GetHashCode(), sameAs1.GetHashCode());

            // 链路层：DeepCopyNodeShape（PublicConstructors 含该参数）级传导
            static NCMapper.SourceGen.DeepCopyNodeShape MakeShape(NCMapper.SourceGen.ConstructorParameterRef param) => new()
            {
                Type = MakeTypeRef("global::FixB404.Entity"),
                PublicConstructors = new[]
                {
                    new NCMapper.SourceGen.ConstructorRef
                    {
                        IsPublic = true,
                        Parameters = new[] { param },
                    },
                },
            };
            var shape1 = MakeShape(withDefault1);
            var shape2 = MakeShape(withDefault2);
            Assert.False(shape1.Equals(shape2)); // 缺陷态 true → 红（叶子值比较缺失使整链误判等）
            Assert.NotEqual(shape1.GetHashCode(), shape2.GetHashCode());
        }
    }
}
