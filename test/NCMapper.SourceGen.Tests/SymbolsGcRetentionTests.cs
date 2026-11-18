using Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Linq;

namespace NCMapper.SourceGen.Tests
{
    /// <summary>
    /// Q4 真机 GC 验证（审计建议④补办，2026-08-14）：
    /// 动态实证——管线 run 结束后，编译符号（用户类型）无强引用残留（可被 GC 回收）。
    /// <para>静态断言（CacheValueNoSymbolTests）锁定"缓存值字段零符号"；本测试补充运行时证据：
    /// compilation/driver/生成结果全部释放后，符号弱引用必须死亡——若管线缓存驻留符号（旧实现行为），
    /// 符号会被强引用保留而无法回收。</para>
    /// <para>已知不稳定性（Q4 备案）：JIT/GC 时序可能延迟回收——独立方法边界 + 双轮 GC.Collect 缓解；
    /// 若 CI 偶发失败按 Q4 降级口径处置（以静态断言为准）。</para>
    /// </summary>
    public class SymbolsGcRetentionTests
    {
        [Fact]
        public void UserTypeSymbol_IsCollected_AfterPipelineReleased()
        {
            // 独立方法取弱引用（方法返回后局部强引用全部出作用域）
            var weak = CaptureUserTypeSymbolWeakRef();

            // 双轮强制回收（含终结器等待）
            for (int i = 0; i < 2; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }

            Assert.False(weak.IsAlive,
                "符号未被回收——存在驻留强引用（管线缓存或 compilation 未释放；若为 JIT 时序偶发，重跑确认）");
        }

        /// <summary>独立方法：编译 + 生成 + 捕获用户类型符号弱引用 + 释放全部强引用</summary>
        private static WeakReference CaptureUserTypeSymbolWeakRef()
        {
            var tree = CSharpSyntaxTree.ParseText("""
                using NCMapper.Attributes;
                namespace GcProbe
                {
                    public class GcSrc { public int Id { get; set; } public string Name { get; set; } = ""; }
                    [MapFrom(typeof(GcSrc))]
                    public class GcDst { public int Id { get; set; } public string Name { get; set; } = ""; }
                }
                """);
            var compilation = CSharpCompilation.Create(
                "GcProbe",
                new[] { tree },
                DiagnosticAndCacheTests.BuildMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new[] { new NCMapperSourceGenerator().AsSourceGenerator() });
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);

            // 捕获用户类型符号（编译程序集内，非 BCL——排除 Roslyn 全局缓存干扰）
            var symbol = compilation.GetTypeByMetadataName("GcProbe.GcDst");
            Assert.NotNull(symbol);
            var weak = new WeakReference(symbol);

            // 释放全部强引用（管线 run 结束后缓存值不应持有符号）
            symbol = null;
            compilation = null;
            updated = null;
            driver = null!;
            tree = null;
            return weak;
        }
    }
}
