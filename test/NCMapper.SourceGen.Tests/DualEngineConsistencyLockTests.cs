// 行 8——双引擎一致性成对断言
//   成对规则 = 同一 fixture（同一段源码字符串）双断言：① 生成侧 NCMAP019 Error 拦截；
//   ② 运行时反射路径不消费无锚标注（Emit 内存程序集真跑 Mapper.Map）。
//  双引擎口径对齐：生成侧拦 Error 前置排除 + 运行时锚门化"无锚不消费"，
//   两侧必须对同一形态同时成立——单侧成立即分叉（B2 防假测试：先验锚 = 同语料有锚形态
//   消费生效，排除"特性本来就不工作"的恒绿）。
using Xunit;
using System;
using System.Linq;
using System.Reflection;

namespace NCMapper.SourceGen.Tests
{
    /// <summary>
    /// 双引擎一致性锁定：无锚 [Mask] fixture 生成侧 Error ∧ 运行时不消费，同一源码双断言
    /// <para> 行 8（承接）。</para>
    /// </summary>
    public class DualEngineConsistencyLockTests
    {
        // 同一 fixture 源码：Anchor 有锚对（先验锚载体/对照；**非 019 输出前提**
        // 口径订正）+ 无锚 [Mask] 对（被测形态）
        // Mask 用合法 Type 构造形态（MaskAttribute 仅 (Type) 单参构造——编译须干净供 Emit 腿使用）
        private const string SharedFixture = """
            using NCMapper;
            using NCMapper.Attributes;

            public class AnchorEntity
            {
                public int Id { get; set; }
                public string Phone { get; set; } = "";
            }

            [MapFrom(typeof(AnchorEntity))]
            public class AnchorMaskedDto
            {
                public int Id { get; set; }

                [Mask(typeof(MaskPhone))]
                public string Phone { get; set; } = "";
            }

            public class BareMaskedView
            {
                public int Id { get; set; }

                [Mask(typeof(MaskPhone))]
                public string Phone { get; set; } = "";
            }

            public class BareMaskedSource
            {
                public int Id { get; set; }
                public string Phone { get; set; } = "13800001111";
            }
            """;

        /// <summary>① 生成侧腿：无锚 [Mask] 类型 → NCMAP019 Error（编译期前置拦截）。</summary>
        [Fact]
        public void GenerationSide_BareMask_NoAnchor_Reports019_Error()
        {
            var result = Helpers.TestHelper.GenerateMapper(SharedFixture);
            var d019 = Assert.Single(result.Diagnostics, d => d.Id == "NCMAP019");
            Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, d019.Severity);
            // 先验锚：Anchor 有锚对正常生成（生成面可达——排除"整个生成失败"的假绿）
            Assert.Contains("Map_AnchorEntity_To_AnchorMaskedDto", result.GeneratedCode);
        }

        /// <summary>
        /// ② 运行时腿：同一 fixture Emit 真跑——无锚 [BareMaskedView] 不消费 [Mask]（值原样）；
        /// 有锚 AnchorMaskedDto 消费 [Mask]（值脱敏）——反向先验锚排除假绿
        /// </summary>
        [Fact]
        public void RuntimeSide_BareMask_NoAnchor_NotConsumed_WithAnchor_Consumed()
        {
            var asm = Helpers.TestHelper.CompileWithGeneratorsAndLoad(SharedFixture, emitRuntimeSnapshot: false);
            var srcType = asm.GetType("BareMaskedSource", throwOnError: true)!;
            var bareDstType = asm.GetType("BareMaskedView", throwOnError: true)!;
            var anchorSrcType = asm.GetType("AnchorEntity", throwOnError: true)!;
            var anchorDstType = asm.GetType("AnchorMaskedDto", throwOnError: true)!;

            // 无锚腿：Map&lt;BareMaskedSource, BareMaskedView&gt;—— 运行时回退路径，[Mask] 不消费
            var src = Activator.CreateInstance(srcType)!;
            srcType.GetProperty("Phone")!.SetValue(src, "13800001111");
            var bareDst = InvokeMap(srcType, bareDstType, src);
            var barePhone = (string)bareDstType.GetProperty("Phone")!.GetValue(bareDst)!;
            Assert.Equal("13800001111", barePhone); // 原样——无锚不脱敏

            // 有锚腿（先验锚）：Map&lt;AnchorEntity, AnchorMaskedDto&gt;—— [Mask] 消费，值脱敏
            var anchorSrc = Activator.CreateInstance(anchorSrcType)!;
            anchorSrcType.GetProperty("Phone")!.SetValue(anchorSrc, "13800001111");
            var anchorDst = InvokeMap(anchorSrcType, anchorDstType, anchorSrc);
            var anchorPhone = (string)anchorDstType.GetProperty("Phone")!.GetValue(anchorDst)!;
            Assert.NotEqual("13800001111", anchorPhone); // 脱敏生效（掩码替换，形态不锁定）
            Assert.Contains("*", anchorPhone);
        }

        /// <summary>反射调用 Mapper.Map&lt;TSource, TTarget&gt;(source)（新建形态——重载 (TSource, Action&lt;TSource,TTarget&gt;?)）。</summary>
        private static object InvokeMap(Type srcType, Type dstType, object source)
        {
            var openMap = typeof(NCMapper.Mapper).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(m => m.Name == "Map" && m.IsGenericMethodDefinition
                    && m.GetGenericArguments().Length == 2
                    && m.GetParameters().Length == 2
                    && m.GetParameters()[0].ParameterType == m.GetGenericArguments()[0]
                    && m.GetParameters()[1].ParameterType.IsGenericType
                    && m.GetParameters()[1].ParameterType.GetGenericTypeDefinition() == typeof(Action<,>));
            var closed = openMap.MakeGenericMethod(srcType, dstType);
            return closed.Invoke(null, new[] { source, null })!;
        }

        // ================================================================
        // 互指对（双 true）运行时收敛——从 D3RuntimeClosureLockTests 迁入（025 Error 编译
        // 诊断会阻断测试工程构建，fixture 须经合成编译承载——同一 fixture 双引擎断言形态：
        // 生成侧 025 Error 拦截 + 运行时 Emit 腿按名兜底复制）
        // ================================================================

        private const string MutualFixture = """
            using NCMapper.Attributes;

            [MapFrom(typeof(D3MutualB))]
            public class D3MutualA
            {
                public int Id { get; set; }
                public string Tag { get; set; } = "";
            }

            [MapFrom(typeof(D3MutualA))]
            public class D3MutualB
            {
                public int Id { get; set; }
                public string Tag { get; set; } = "";
            }
            """;

        /// <summary>① 生成侧腿：互指对 → NCMAP025 Error ×2（双向各一条），该对不生成。</summary>
        [Fact]
        public void MutualPair_GenerationSide_Reports025_Error_PairNotGenerated()
        {
            var result = Helpers.TestHelper.GenerateMapper(MutualFixture);
            var d025s = result.Diagnostics.Where(d => d.Id == "NCMAP025").ToList();
            Assert.Equal(2, d025s.Count);
            Assert.All(d025s, d => Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, d.Severity));
            // 先验锚：互指对不生成
            Assert.DoesNotContain("Map_D3MutualA_To_D3MutualB", result.GeneratedCode);
        }

        /// <summary>② 运行时腿：同一 fixture Emit 真跑——互指对按名兜底复制（双向）。</summary>
        [Fact]
        public void MutualPair_RuntimeSide_FallsBackToByName()
        {
            var asm = Helpers.TestHelper.CompileWithGeneratorsAndLoad(MutualFixture, emitRuntimeSnapshot: false);
            var aType = asm.GetType("D3MutualA", throwOnError: true)!;
            var bType = asm.GetType("D3MutualB", throwOnError: true)!;

            var a = Activator.CreateInstance(aType)!;
            aType.GetProperty("Id")!.SetValue(a, 3);
            aType.GetProperty("Tag")!.SetValue(a, "mutual");
            var b = InvokeMap(aType, bType, a);
            Assert.Equal(3, (int)bType.GetProperty("Id")!.GetValue(b)!);
            Assert.Equal("mutual", (string)bType.GetProperty("Tag")!.GetValue(b)!);

            var back = InvokeMap(bType, aType, b);
            Assert.Equal(3, (int)aType.GetProperty("Id")!.GetValue(back)!);
            Assert.Equal("mutual", (string)aType.GetProperty("Tag")!.GetValue(back)!);
        }
    }
}
