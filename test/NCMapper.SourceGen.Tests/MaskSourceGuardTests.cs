using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests
{
    /// <summary>
    /// 生成侧发射期守卫测试（W3 + 路径矩阵；
    /// 修复裁决 = 20260909-02 评审意见_001 §四 发射期降级）。
    /// <para>缺陷：单标 [Mask] + 源非 string 时生成代码裸发射 <c>__mask_X.Mask(src.IntProp)</c>
    /// → CS1503 编译失败（运行时侧已守卫：PlanCore :124-139 降级常规转换）。
    /// 修复：标准路径（CodeGenerator.Mapping.cs 纯 Mask 分支）与 Compose 纯 Mask 分支前置
    /// 有效源类型守卫——非 string fall through 类型转换链（int→string 经 IConvertible 白名单
    /// TypeCompatibilityCore 步骤 9 判可转 → ConvertValue&lt;int,string&gt; fallback，与运行时
    /// ConvertValue Invariant 语义等价）。</para>
    /// <para>先红后绿（执行期登记）：守卫落地前用例①②的最终编译含 CS1503（红锚），
    /// 落地后转绿。无源 + [Mask] 双通道语义锚 = 两侧均跳过（生成侧 :1208 早退先于 Mask 分支；
    /// 运行时 ExpressionBuilder :252-253）——评审意见_002 §二修正口径。</para>
    /// </summary>
    public class MaskSourceGuardTests
    {
        /// <summary>生成 + 最终编译诊断（生成树并入编译后取全部 Error 级诊断——
        /// 含生成代码自身类型错误，红锚 CS1503 的承载面）。</summary>
        private static (string gen, Diagnostic[] errorDiags) GenerateAndCompile(string source)
        {
            var compilation = CSharpCompilation.Create(
                "MaskGuard",
                new[] { CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken) },
                DiagnosticAndCacheTests.BuildMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new[] { new NCMapperSourceGenerator().AsSourceGenerator() });
            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation, out var updated, out _, TestContext.Current.CancellationToken);
            var gen = updated.SyntaxTrees
                .Skip(compilation.SyntaxTrees.Count())
                .FirstOrDefault(t => t.FilePath.Contains("NCMapper_Generated_GeneratedMapper.g.cs"))
                ?.ToString() ?? "";
            var errorDiags = updated.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .ToArray();
            return (gen, errorDiags);
        }

        /// <summary>用例①：int 源 + string 目标 + 单标 [Mask]——守卫落地后生成代码编译通过，
        /// 赋值降级走类型转换链（ConvertValue&lt;int,string&gt; fallback 形态）。先验锚：声明被提取
        /// （Map 方法存在，经验 #67）。先红锚（守卫前）：最终编译含 CS1503（Mask(int) 无隐式转换）。</summary>
        [Fact]
        public void IntSourceStringTargetMask_CompilesAndFallsThroughConversion()
        {
            const string source = """
                using NCMapper;
                using NCMapper.Attributes;
                public class MsgI1Entity { public int Code { get; set; } }
                [MapFrom(typeof(MsgI1Entity))]
                public class MsgI1Dto
                {
                    [Mask(typeof(MaskPhone))]
                    public string Code { get; set; } = "";
                }
                """;
            var (gen, errorDiags) = GenerateAndCompile(source);
            Assert.Contains("Map_MsgI1Entity_To_MsgI1Dto", gen);                        // 先验锚
            Assert.DoesNotContain(errorDiags, d => d.Id == "CS1503");                   // 守卫后编译通过
            Assert.DoesNotContain(errorDiags, d => d.Severity == DiagnosticSeverity.Error);
            Assert.Contains("ConvertValue<int, string>(src.Code)", gen);                // 降级转换链形态
        }

        /// <summary>用例②：Compose 子源 int 变体（[MapProperty(typeof(Sub),...)] 跨表 + [Mask]）——
        /// Compose 纯 Mask 分支（CodeGenerator.Compose.cs:302-308）同款守卫，编译通过 + 降级转换。
        /// 先红锚（守卫前）：CS1503 同构。</summary>
        [Fact]
        public void ComposeSubSourceIntMask_CompilesAndFallsThroughConversion()
        {
            const string source = """
                using NCMapper;
                using NCMapper.Attributes;
                public class MsgI2Primary { public int Id { get; set; } }
                public class MsgI2Sub { public int Code { get; set; } }
                [MapFrom(typeof(MsgI2Primary))]
                public class MsgI2Dto
                {
                    public int Id { get; set; }
                    [MapProperty(typeof(MsgI2Sub), nameof(MsgI2Sub.Code))]
                    [Mask(typeof(MaskPhone))]
                    public string Code { get; set; } = "";
                }
                """;
            var (gen, errorDiags) = GenerateAndCompile(source);
            Assert.Contains("MsgI2Sub", gen);                                           // 先验锚
            Assert.DoesNotContain(errorDiags, d => d.Id == "CS1503");                   // 守卫后编译通过
            Assert.DoesNotContain(errorDiags, d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>无源 + [Mask] 双通道语义锚（评审意见_002 §二修正口径——两侧均跳过）：
        /// 目标属性无同名源 → 正向 Map 方法体零发射（pm.Source==null 早退先于 Mask 分支）。
        /// 断言限定正向方法体——DeepCopy 克隆体为 DTO→DTO 同型复制（dst.Orphan = src.Orphan
        /// 合法，探针实证），与 Map 方向无关。运行时对应 ExpressionBuilder :252-253 前置块跳过。</summary>
        [Fact]
        public void NoSourceMask_BothEnginesSkip()
        {
            const string source = """
                using NCMapper;
                using NCMapper.Attributes;
                public class MsgI3Entity { public string Name { get; set; } = ""; }
                [MapFrom(typeof(MsgI3Entity))]
                public class MsgI3Dto
                {
                    public string Name { get; set; } = "";
                    [Mask(typeof(MaskPhone))]
                    public string Orphan { get; set; } = "";
                }
                """;
            var (gen, errorDiags) = GenerateAndCompile(source);
            Assert.Contains("Map_MsgI3Entity_To_MsgI3Dto", gen);                        // 先验锚
            var mapBody = ExtractMethodBody(gen, "Map_MsgI3Entity_To_MsgI3Dto");
            Assert.Contains("Name = src.Name", mapBody);                                // 正常映射在位（初始化器形态）
            Assert.DoesNotContain("Orphan", mapBody);                                   // 语义锚：无源属性正向零发射
            Assert.DoesNotContain(errorDiags, d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>提取指定方法体（方法签名起，花括号配对至平衡闭括号——机械精确，
        /// 防相邻方法段吞入：DeepCopy 克隆体与 Map 方法相邻时文本包含同型赋值）。</summary>
        private static string ExtractMethodBody(string gen, string methodPrefix)
        {
            var idx = gen.IndexOf(methodPrefix + "(", StringComparison.Ordinal);
            if (idx < 0) return "";
            var open = gen.IndexOf('{', idx);
            if (open < 0) return "";
            var depth = 0;
            for (var i = open; i < gen.Length; i++)
            {
                if (gen[i] == '{') depth++;
                else if (gen[i] == '}')
                {
                    depth--;
                    if (depth == 0) return gen[idx..(i + 1)];
                }
            }
            return gen[idx..];
        }

        /// <summary>用例④：源 string + [Mask] 回归——守卫不误伤（源 string 仍走 Mask 发射），
        /// 产物含 mask 字段调用与脱敏赋值。防守卫回归红线（否定条件 F2：源 string+Mask 产物变化）。</summary>
        [Fact]
        public void StringSourceMask_MaskEmissionUnchanged()
        {
            const string source = """
                using NCMapper;
                using NCMapper.Attributes;
                public class MsgI4Entity { public string Phone { get; set; } = ""; }
                [MapFrom(typeof(MsgI4Entity))]
                public class MsgI4Dto
                {
                    [Mask(typeof(MaskPhone))]
                    public string Phone { get; set; } = "";
                }
                """;
            var (gen, errorDiags) = GenerateAndCompile(source);
            Assert.Contains("__mask", gen);                                             // mask 字段在位
            Assert.Contains("dst.Phone =", gen);                                        // 脱敏赋值在位
            Assert.DoesNotContain(errorDiags, d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>用例⑤：守卫后行为等价（真跑）——生成引擎 int 12345 → "12345"（ConvertValue
        /// Invariant 语义），与运行时引擎 Mapper.Map（PlanCore 守卫同语义）结果一致（双引擎等价锚）。</summary>
        [Fact]
        public void GuardedConversion_BehaviorMatchesRuntimeEngine()
        {
            const string source = """
                using NCMapper;
                using NCMapper.Attributes;
                public class MsgI1Entity { public int Code { get; set; } }
                [MapFrom(typeof(MsgI1Entity))]
                public class MsgI1Dto
                {
                    [Mask(typeof(MaskPhone))]
                    public string Code { get; set; } = "";
                }
                """;
            var asm = TestHelper.CompileWithGeneratorsAndLoad(source, emitRuntimeSnapshot: false);
            var entityType = asm.GetType("MsgI1Entity")!;
            var entity = Activator.CreateInstance(entityType)!;
            entityType.GetProperty("Code")!.SetValue(entity, 12345);

            var mapperType = asm.GetType("NCMapper.Generated.GeneratedMapper")!;
            var dto = mapperType.GetMethod("Map_MsgI1Entity_To_MsgI1Dto")!
                .Invoke(null, new[] { entity })!;
            var genEngineCode = (string)dto.GetType().GetProperty("Code")!.GetValue(dto)!;

            var runtimeDto = NCMapper.Mapper.Map<MsgI5RuntimeEntity, MsgI5RuntimeDto>(new MsgI5RuntimeEntity { Code = 12345 })!;
            Assert.Equal(genEngineCode, runtimeDto!.Code);  // 双引擎等价（守卫后同语义降级）
            Assert.Equal("12345", genEngineCode);           // ConvertValue Invariant 语义
        }
    }

    // 用例⑤运行时引擎对照镜像（生成侧 MsgI1 fixture 的运行时版——运行时引擎按 [MapFrom]
    // 特性自走 PlanCore 守卫 :124-139，同语义降级 ConvertValue）
    internal sealed class MsgI5RuntimeEntity { public int Code { get; set; } }

    [NCMapper.Attributes.MapFrom(typeof(MsgI5RuntimeEntity))]
    internal sealed class MsgI5RuntimeDto
    {
        [NCMapper.Attributes.Mask(typeof(NCMapper.MaskPhone))]
        public string Code { get; set; } = "";
    }
}
