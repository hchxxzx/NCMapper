// 内联 lambda 转录（11 例/27 断言）
// 锚点：src/NCMapper.SourceGen/InlineTranscriber.cs
//   ?. 段：接受 :263-274（mae2 嵌套绑定支）；拒绝 :275-279（default 支——WhenNotNull 被
//     Invocation/ElementBinding 包裹，v1.3 订正口径）
//   拒绝段：捕获外层参数 :328-330；lambda/局部函数 :334-337；private 方法组 :339-342；
//     方法组成功限定名 :344-345；捕获局部变量 :323-326（语义回归——先例 F26_LambdaCapture_Rejected_F41 已覆盖，
//     本类保留为拒绝语义组合回归）
//   可见性契约：IsAccessibleFromGeneratedCode :585-600 拒绝集仅 Private/Protected/ProtectedAndInternal
//     ＋lambda/局部函数——internal 可见（动作 5 零诊断）
// 断言口径：拒绝类 3 断言＝F41/F42 恰 1＋诊断 span 指向该符号＋生成代码不含转录方法
//   （Map_Cv_*，命名规则 MappingSnapshotBuilder.cs:747-756；生成器不因 F41/F42 停射——v1.2 订正）；
//   接受类 2 断言＝生成代码可编译（CompileWithGeneratorsAndLoad，Emit 失败即抛）＋运行期结果值正确
//   （反射调 Map_* 静态方法——生成腿真实执行转录产物）。
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests.Fluent
{
    public class InlineLambdaTranscriptionTests
    {
        // ================================================================
        // 接受类（5 例 × 2 断言）
        // ================================================================

        [Fact]
        public void Accept_MathStaticMember_Transcribed_ValueCorrect()
        {
            // 例 1：静态成员访问（Math.Round）——生成代码 global::System.Math 限定，零 using 依赖
            var source = """
                using System;
                using NCMapper;
                public class Fcl1Entity { public int Price { get; set; } }
                public class Fcl1Dto { public string Price { get; set; } = ""; }

                [MapperConfig]
                public class Fcl1Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<Fcl1Entity, Fcl1Dto>().Member(d => d.Price)
                           .Convert((int v) => Math.Round((double)v / 100).ToString());
                    }
                }
                """;
            var result = TestHelper.GenerateMapper(source);
            Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F41" || d.Id == "NCMAP_F42");
            Assert.Contains("global::System.Math.Round", result.GeneratedCode);

            // 断言①：生成代码可编译＋断言②：运行期值正确（123 → Math.Round(1.23) → "1"）
            var asm = TestHelper.CompileWithGeneratorsAndLoad(source, emitRuntimeSnapshot: false);
            var mapperType = asm.GetTypes().Single(t => t.Name == "GeneratedMapper");
            var mapMethod = mapperType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .Single(m => m.Name == "Map_Fcl1Entity_To_Fcl1Dto");
            var srcType = asm.GetType("Fcl1Entity", throwOnError: true)!;
            var src = Activator.CreateInstance(srcType)!;
            srcType.GetProperty("Price")!.SetValue(src, 123);
            var dto = mapMethod.Invoke(null, new[] { src })!;
            Assert.Equal("1", dto.GetType().GetProperty("Price")!.GetValue(dto));
        }

        [Fact]
        public void Accept_NestedTypeStaticField_Transcribed_ValueCorrect()
        {
            // 例 2：嵌套类型静态字段（Outer.Inner.Tag）——嵌套类型限定名展开
            var source = """
                using NCMapper;
                public class Fcl2Entity { public string Band { get; set; } = ""; }
                public class Fcl2Dto { public string Band { get; set; } = ""; }
                public static class Fcl2Outer
                {
                    public static class Inner { public static string Tag = "IN:"; }
                }

                [MapperConfig]
                public class Fcl2Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<Fcl2Entity, Fcl2Dto>().Member(d => d.Band)
                           .Convert((string v) => Fcl2Outer.Inner.Tag + v);
                    }
                }
                """;
            var result = TestHelper.GenerateMapper(source);
            Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F41" || d.Id == "NCMAP_F42");
            Assert.Contains("Fcl2Outer.Inner.Tag", result.GeneratedCode);

            var asm = TestHelper.CompileWithGeneratorsAndLoad(source, emitRuntimeSnapshot: false);
            var mapperType = asm.GetTypes().Single(t => t.Name == "GeneratedMapper");
            var mapMethod = mapperType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .Single(m => m.Name == "Map_Fcl2Entity_To_Fcl2Dto");
            var srcType = asm.GetType("Fcl2Entity", throwOnError: true)!;
            var src = Activator.CreateInstance(srcType)!;
            srcType.GetProperty("Band")!.SetValue(src, "abc");
            var dto = mapMethod.Invoke(null, new[] { src })!;
            Assert.Equal("IN:abc", dto.GetType().GetProperty("Band")!.GetValue(dto));
        }

        [Fact]
        public void Accept_MethodGroupInvocation_Transcribed_ValueCorrect()
        {
            // 例 3：方法组引用（静态方法调用 FQN 重写——344-345 成功限定名支）
            var source = """
                using NCMapper;
                using static Fcl3Helper;
                public class Fcl3Entity { public string Band { get; set; } = ""; }
                public class Fcl3Dto { public string Band { get; set; } = ""; }
                public static class Fcl3Helper
                {
                    public static string Decorate(string v) => "[" + v + "]";
                    public static string Run(System.Func<string, string> f, string v) => f(v);
                }

                [MapperConfig]
                public class Fcl3Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<Fcl3Entity, Fcl3Dto>().Member(d => d.Band)
                           // 裸方法组标识符成功限定名（:344-345）——using static 作用域内的裸方法组实参
                           .Convert((string v) => Run(Decorate, v));
                    }
                }
                """;
            var result = TestHelper.GenerateMapper(source);
            Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F41" || d.Id == "NCMAP_F42");
            Assert.Contains("Fcl3Helper.Decorate", result.GeneratedCode);

            var asm = TestHelper.CompileWithGeneratorsAndLoad(source, emitRuntimeSnapshot: false);
            var mapperType = asm.GetTypes().Single(t => t.Name == "GeneratedMapper");
            var mapMethod = mapperType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .Single(m => m.Name == "Map_Fcl3Entity_To_Fcl3Dto");
            var srcType = asm.GetType("Fcl3Entity", throwOnError: true)!;
            var src = Activator.CreateInstance(srcType)!;
            srcType.GetProperty("Band")!.SetValue(src, "x");
            var dto = mapMethod.Invoke(null, new[] { src })!;
            Assert.Equal("[x]", dto.GetType().GetProperty("Band")!.GetValue(dto));
        }

        [Fact]
        public void Accept_ParamRootedChain_Transcribed_ValueCorrect()
        {
            // 例 4：按参数转录（参数根链原样——295-299/:412-417，v.Trim().ToUpper()）
            var source = """
                using NCMapper;
                public class Fcl4Entity { public string Band { get; set; } = ""; }
                public class Fcl4Dto { public string Band { get; set; } = ""; }

                [MapperConfig]
                public class Fcl4Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<Fcl4Entity, Fcl4Dto>().Member(d => d.Band)
                           .Convert((string v) => v.Trim().ToUpper());
                    }
                }
                """;
            var result = TestHelper.GenerateMapper(source);
            Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F41" || d.Id == "NCMAP_F42");

            var asm = TestHelper.CompileWithGeneratorsAndLoad(source, emitRuntimeSnapshot: false);
            var mapperType = asm.GetTypes().Single(t => t.Name == "GeneratedMapper");
            var mapMethod = mapperType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .Single(m => m.Name == "Map_Fcl4Entity_To_Fcl4Dto");
            var srcType = asm.GetType("Fcl4Entity", throwOnError: true)!;
            var src = Activator.CreateInstance(srcType)!;
            srcType.GetProperty("Band")!.SetValue(src, " abc ");
            var dto = mapMethod.Invoke(null, new[] { src })!;
            Assert.Equal("ABC", dto.GetType().GetProperty("Band")!.GetValue(dto));
        }

        [Fact]
        public void Accept_InternalMember_Transcribed_ZeroDiagnostics_ValueCorrect()
        {
            // 例 5：internal 成员访问——可见性契约拒绝集仅 Private/Protected/ProtectedAndInternal
            //（585-600），internal 对同程序集 GeneratedMapper 可见 → 零诊断＋转录放行
            var source = """
                using NCMapper;
                public class Fcl5Entity { public string Band { get; set; } = ""; }
                public class Fcl5Dto { public string Band { get; set; } = ""; }
                internal static class Fcl5Helper { internal static string Mark(string v) => "#" + v; }

                [MapperConfig]
                public class Fcl5Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<Fcl5Entity, Fcl5Dto>().Member(d => d.Band)
                           .Convert((string v) => Fcl5Helper.Mark(v));
                    }
                }
                """;
            var result = TestHelper.GenerateMapper(source);
            Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F41" || d.Id == "NCMAP_F42");
            Assert.Contains("Fcl5Helper.Mark", result.GeneratedCode);

            var asm = TestHelper.CompileWithGeneratorsAndLoad(source, emitRuntimeSnapshot: false);
            var mapperType = asm.GetTypes().Single(t => t.Name == "GeneratedMapper");
            var mapMethod = mapperType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .Single(m => m.Name == "Map_Fcl5Entity_To_Fcl5Dto");
            var srcType = asm.GetType("Fcl5Entity", throwOnError: true)!;
            var src = Activator.CreateInstance(srcType)!;
            srcType.GetProperty("Band")!.SetValue(src, "y");
            var dto = mapMethod.Invoke(null, new[] { src })!;
            Assert.Equal("#y", dto.GetType().GetProperty("Band")!.GetValue(dto));
        }

        // ================================================================
        // 拒绝类（4 例 × 3 断言：F41 恰 1 ＋ 诊断 span 指向该符号 ＋ 不含转录方法）
        // ================================================================

        [Fact]
        public void Reject_PrivateMember_F41_SpanAndNoProduct()
        {
            // 例 6：private 方法组（339-342 IMethodSymbol 支）
            var source = """
                using NCMapper;
                public class Fcl6Entity { public string Band { get; set; } = ""; }
                public class Fcl6Dto { public string Band { get; set; } = ""; }

                [MapperConfig]
                public class Fcl6Mapper
                {
                    // 同类 private 方法（裸标识符唯一合法引用位——using static 只导入可访问成员，
                    // 跨类 private 裸名源码级 CS0103 不可达）
                    private static string Secret(string v) => v;
                    public static string Run(System.Func<string, string> f, string v) => f(v);

                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<Fcl6Entity, Fcl6Dto>().Member(d => d.Band)
                           // 裸方法组标识符（同类 Invocation 实参）→ :339-342 IMethodSymbol 不可访问
                           .Convert((string v) => Run(Secret, v));
                    }
                }
                """;
            var result = TestHelper.GenerateMapper(source);
            var f41 = result.Diagnostics.Where(d => d.Id == "NCMAP_F41").ToList();
            Assert.Single(f41);
            // 诊断 span 指向该符号（Location 还原源文本含方法名）
            AssertSpanPointsToSymbol(f41[0], source, "Secret");
            // 生成器不因 F41 停射（v1.2 订正），但不含该条转录产物
            Assert.DoesNotContain("Map_Cv_Fcl6Dto_Band_", result.GeneratedCode);
        }

        [Fact]
        public void Reject_LocalFunction_F41_SpanAndNoProduct()
        {
            // 例 7：局部函数引用（334 判定＋:336-337 LocalFunction 支）
            var source = """
                using NCMapper;
                public class Fcl7Entity { public string Band { get; set; } = ""; }
                public class Fcl7Dto { public string Band { get; set; } = ""; }
                public static class Fcl7Helper
                {
                    public static string Run(System.Func<string, string> f, string v) => f(v);
                }

                [MapperConfig]
                public class Fcl7Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        string LocalFn(string x) => x.Trim();
                        cfg.For<Fcl7Entity, Fcl7Dto>().Member(d => d.Band)
                           // 裸局部函数标识符（Invocation 实参，无调用括号）→ :334-337 MethodKind=LocalFunction
                           .Convert((string v) => Fcl7Helper.Run(LocalFn, v));
                    }
                }
                """;
            var result = TestHelper.GenerateMapper(source);
            var f41 = result.Diagnostics.Where(d => d.Id == "NCMAP_F41").ToList();
            Assert.Single(f41);
            AssertSpanPointsToSymbol(f41[0], source, "LocalFn");
            Assert.DoesNotContain("Map_Cv_Fcl7Dto_Band_", result.GeneratedCode);
        }

        [Fact]
        public void Reject_CapturedLocal_F41_SpanAndNoProduct()
        {
            // 例 8：捕获局部变量（323-326——先例 F26_LambdaCapture_Rejected_F41 已覆盖，
            // 本例保留为拒绝语义组合回归，不贡献承诺行）
            var source = """
                using NCMapper;
                public class Fcl8Entity { public string Band { get; set; } = ""; }
                public class Fcl8Dto { public string Band { get; set; } = ""; }

                [MapperConfig]
                public class Fcl8Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        var prefix = "v:";
                        cfg.For<Fcl8Entity, Fcl8Dto>().Member(d => d.Band)
                           .Convert((string v) => prefix + v);
                    }
                }
                """;
            var result = TestHelper.GenerateMapper(source);
            var f41 = result.Diagnostics.Where(d => d.Id == "NCMAP_F41").ToList();
            Assert.Single(f41);
            AssertSpanPointsToSymbol(f41[0], source, "prefix");
            Assert.DoesNotContain("Map_Cv_Fcl8Dto_Band_", result.GeneratedCode);
        }

        [Fact]
        public void Reject_CapturedOuterParameter_F41_SpanAndNoProduct()
        {
            // 例 9：捕获外层参数（328-330 IParameterSymbol 支——lambda 形参 v 之外的 Configure 形参 cfg）
            var source = """
                using NCMapper;
                public class Fcl9Entity { public string Band { get; set; } = ""; }
                public class Fcl9Dto { public string Band { get; set; } = ""; }

                [MapperConfig]
                public class Fcl9Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<Fcl9Entity, Fcl9Dto>().Member(d => d.Band)
                           .Convert((string v) => (cfg == null ? "" : "") + v);
                    }
                }
                """;
            var result = TestHelper.GenerateMapper(source);
            var f41 = result.Diagnostics.Where(d => d.Id == "NCMAP_F41").ToList();
            Assert.Single(f41);
            AssertSpanPointsToSymbol(f41[0], source, "cfg");
            Assert.DoesNotContain("Map_Cv_Fcl9Dto_Band_", result.GeneratedCode);
        }

        // ================================================================
        // 空条件访问（?. 段 2 例：接受 ×2 断言 / 拒绝 ×3 断言）
        // ================================================================

        [Fact]
        public void Accept_NullConditionalNestedBinding_Transcribed_ValueCorrect()
        {
            // 例 10：x.Sub?.A.B 形态（v?.A.B，v 为 lambda 参数根）——WhenNotNull 为
            // MemberAccess(MemberBinding(.A), .B)（末段无调用），落 mae2 嵌套绑定支 :263-274。
            // 形态注：末段带调用（.B()）会使 WhenNotNull 顶层变 Invocation → 落 default F42（实测）。
            var source = """
                using NCMapper;
                public class Fcl10Node { public string B { get; set; } = ""; }
                public class Fcl10Val { public Fcl10Node? A { get; set; } }
                public class Fcl10Entity { public Fcl10Val Band { get; set; } = new(); }
                public class Fcl10Dto { public string Band { get; set; } = ""; }

                [MapperConfig]
                public class Fcl10Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<Fcl10Entity, Fcl10Dto>().Member(d => d.Band)
                           .Convert((Fcl10Val v) => v?.A.B);
                    }
                }
                """;
            var result = TestHelper.GenerateMapper(source);
            Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F41" || d.Id == "NCMAP_F42");
            Assert.Contains("?.A.B", result.GeneratedCode);

            var asm = TestHelper.CompileWithGeneratorsAndLoad(source, emitRuntimeSnapshot: false);
            var mapperType = asm.GetTypes().Single(t => t.Name == "GeneratedMapper");
            var mapMethod = mapperType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .Single(m => m.Name == "Map_Fcl10Entity_To_Fcl10Dto");
            var srcType = asm.GetType("Fcl10Entity", throwOnError: true)!;
            var src = Activator.CreateInstance(srcType)!;
            var valType = asm.GetType("Fcl10Val", throwOnError: true)!;
            var nodeType = asm.GetType("Fcl10Node", throwOnError: true)!;
            var node = Activator.CreateInstance(nodeType)!;
            nodeType.GetProperty("B")!.SetValue(node, "k");
            var val = Activator.CreateInstance(valType)!;
            valType.GetProperty("A")!.SetValue(val, node);
            srcType.GetProperty("Band")!.SetValue(src, val);
            var dto = mapMethod.Invoke(null, new[] { src })!;
            Assert.Equal("k", dto.GetType().GetProperty("Band")!.GetValue(dto));
        }

        [Fact]
        public void Reject_NullConditionalElementBinding_F42_SpanAndNoProduct()
        {
            // 例 11：v?[0].ToString()——WhenNotNull = MemberAccess(ElementBinding([0]), .ToString())
            //（mae2.Expression 非 MemberBinding）→ default 支 :275-279 F42
            var source = """
                using NCMapper;
                public class Fcl11Entity { public string Band { get; set; } = ""; }
                public class Fcl11Dto { public string Band { get; set; } = ""; }

                [MapperConfig]
                public class Fcl11Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<Fcl11Entity, Fcl11Dto>().Member(d => d.Band)
                           .Convert((string v) => v?[0].ToString());
                    }
                }
                """;
            var result = TestHelper.GenerateMapper(source);
            var f42 = result.Diagnostics.Where(d => d.Id == "NCMAP_F42").ToList();
            Assert.Single(f42);
            AssertSpanPointsToSymbol(f42[0], source, "[0]");
            Assert.DoesNotContain("Map_Cv_Fcl11Dto_Band_", result.GeneratedCode);
        }

        // ================================================================
        // 辅助
        // ================================================================

        /// <summary>
        /// 断言"诊断位置指向该符号"（不依赖行号）：①诊断载荷（Args）含该符号节点文本；
        /// ②Location 解包（F41/F42 经生成器管线传播后为 ExternalFile 包装，GetLineSpan 仍还原
        /// 真实源码 span——dump 实测 Kind=ExternalFile、ToString 带 (行,列)）后定位行上的源码含该符号。
        /// </summary>
        private static void AssertSpanPointsToSymbol(Microsoft.CodeAnalysis.Diagnostic d, string source, string symbolName)
        {
            // ①载荷含符号（EmitF41/F42 的 Args[.] = node.ToString()）
            Assert.Contains(symbolName, d.ToString());
            // ②位置行上的源码含该符号（定位到符号声明/引用行）
            var span = d.Location.GetLineSpan();
            Assert.True(span.Path.Length >= 0, "Location 应可解包");
            var lines = source.Replace("\r\n", "\n").Split('\n');
            var line = span.StartLinePosition.Line;
            Assert.True(line >= 0 && line < lines.Length, $"定位行 {line} 越界（源码 {lines.Length} 行）");
            Assert.Contains(symbolName, lines[line]);
        }
    }
}
