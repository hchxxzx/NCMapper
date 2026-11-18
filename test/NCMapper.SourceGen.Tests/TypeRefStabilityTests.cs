using Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NCMapper.SourceGen;
using System;
using System.Linq;

namespace NCMapper.SourceGen.Tests
{
    /// <summary>
    /// 测试 #8：TypeRef 键跨 Compilation 稳定性。
    /// <para>值模型相等性键（EqualityKey）必须跨 Compilation 稳定——同一逻辑类型在两个
    /// 不同 Compilation 实例中物化的 TypeRef 必须 Equals 相等、GetHashCode 一致，
    /// 否则增量管线缓存命中判定（Unchanged）依赖的键值在跨编译比较时失效（H-06 复发面）。</para>
    /// <para>边缘形态覆盖：全局命名空间/带命名空间、嵌套类型、泛型、数组、可空引用、
    /// Nullable&lt;T&gt; 值类型、枚举、字典。</para>
    /// </summary>
    public class TypeRefStabilityTests
    {
        [Fact]
        public void TypeRef_Key_Stable_Across_Compilations()
        {
            const string source = """
                using System;
                using System.Collections.Generic;
                using NCMapper.Attributes;

                namespace ProbeNs
                {
                    public enum Status { None = 0, Active = 1 }
                    public class Outer
                    {
                        public class Inner { public int Id { get; set; } }
                    }
                }

                public class GlobalType { public int Id { get; set; } }

                [MapFrom(typeof(ProbeNs.Outer))]
                public class ProbeDto
                {
                    public int Id { get; set; }
                    public string? Name { get; set; }
                    public List<string> Items { get; set; } = new();
                    public int[] Numbers { get; set; } = Array.Empty<int>();
                    public Dictionary<string, int> Map { get; set; } = new();
                    public int? Maybe { get; set; }
                    public ProbeNs.Status Status { get; set; }
                    public ProbeNs.Outer.Inner Nested { get; set; } = new();
                }
                """;

            var compilation1 = CSharpCompilation.Create(
                "StabilityA", new[] { CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken) },
                DiagnosticAndCacheTests.BuildMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable));
            var compilation2 = CSharpCompilation.Create(
                "StabilityB", new[] { CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken) },
                DiagnosticAndCacheTests.BuildMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable));

            // 两个独立 Compilation 的同名类型符号
            var types1 = compilation1.GetTypeByMetadataName("ProbeDto")!;
            var types2 = compilation2.GetTypeByMetadataName("ProbeDto")!;

            // ① 顶层类型：跨编译 Equals + 哈希一致
            var refA = MappingExtractor.TypeRefFromSymbol(types1);
            var refB = MappingExtractor.TypeRefFromSymbol(types2);
            Assert.Equal(refA, refB);
            Assert.Equal(refA.GetHashCode(), refB.GetHashCode());

            // ② 属性类型矩阵（覆盖边缘形态）
            foreach (var member in types1.GetMembers().OfType<IPropertySymbol>())
            {
                var symbol2 = types2.GetMembers(member.Name).OfType<IPropertySymbol>().First();
                var ta = MappingExtractor.TypeRefFromSymbol(member.Type);
                var tb = MappingExtractor.TypeRefFromSymbol(symbol2.Type);
                Assert.True(ta.Equals(tb), $"TypeRef 跨编译不等: {member.Name} ({ta.FullName} vs {tb.FullName})");
                Assert.Equal(ta.GetHashCode(), tb.GetHashCode());
            }

            // ③ 跨编译不等性（不同类型必须不等——键的区分度）
            var intRef = MappingExtractor.TypeRefFromSymbol(types1.GetMembers("Id").OfType<IPropertySymbol>().First().Type);
            var strRef = MappingExtractor.TypeRefFromSymbol(
                types1.GetMembers("Name").OfType<IPropertySymbol>().First().Type);
            Assert.NotEqual(intRef, strRef);

            // ④ 可空引用 vs 非可空引用：相等（SymbolEqualityComparer.Default 忽略 Nullability 语义）
            var nameRef = MappingExtractor.TypeRefFromSymbol(
                types1.GetMembers("Name").OfType<IPropertySymbol>().First().Type);      // string?
            var nonNullRef = MappingExtractor.TypeRefFromSymbol(
                types2.GetMembers("Name").OfType<IPropertySymbol>().First().Type
                    .WithNullableAnnotation(NullableAnnotation.NotAnnotated));          // string
            Assert.True(nameRef.Equals(nonNullRef), "string? vs string 应相等（忽略可空注解）");
        }
    }
}
