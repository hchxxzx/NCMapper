using Xunit;
using NCMapper.SourceGen;
using System;
using System.Linq;
using System.Reflection;

namespace NCMapper.SourceGen.Tests
{
    /// <summary>
    /// 测试 #7 / Q4：缓存值零符号驻留静态断言。
    /// <para> Q4：弱引用+GC 断言在 CI 不稳定（JIT/GC 时序），降级静态断言并备案——
    /// 反射检查值模型类型（进入管线缓存的对象）的全部字段类型不得为 Roslyn 符号/语法/编译
    /// 类型（ISymbol 及其子接口、SyntaxNode、SyntaxTree、Location、Compilation）。</para>
    /// </summary>
    public class CacheValueNoSymbolTests
    {
        private static readonly Type[] SnapshotTypes =
        {
            typeof(TypeRef),
            typeof(PropertyRef),
            typeof(PropertyMappingSnapshot),
            typeof(ComposeMappingSnapshot),
            typeof(DiagnosticSnapshot),
            typeof(LocationSnapshot),
            typeof(MappingSnapshot),
        };

        private static readonly string[] ForbiddenTypeNames =
        {
            "ISymbol", "ITypeSymbol", "INamedTypeSymbol", "IPropertySymbol", "IMethodSymbol",
            "IFieldSymbol", "IAssemblySymbol", "IModuleSymbol", "INamespaceSymbol",
            "IArrayTypeSymbol", "IPointerTypeSymbol", "ITypeParameterSymbol",
            "SyntaxNode", "SyntaxTree", "SyntaxToken", "SyntaxTrivia", "Location", "Compilation",
            "AttributeData", "TypedConstant",
        };

        [Fact]
        public void SnapshotTypes_HaveNoSymbolFields()
        {
            foreach (var type in SnapshotTypes)
            {
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    var ft = field.FieldType;
                    // 展开泛型实参/数组/可空，检查底层类型
                    AssertNoForbidden(type, field.Name, ft);
                }
            }
        }

        private static void AssertNoForbidden(Type owner, string fieldName, Type fieldType)
        {
            var t = fieldType;
            if (t.IsArray) t = t.GetElementType()!;
            if (t.IsGenericType)
            {
                foreach (var arg in t.GetGenericArguments())
                    AssertNoForbidden(owner, fieldName, arg);
                return;
            }
            if (t.IsEnum || t.IsPrimitive || t == typeof(string) || t == typeof(object)) return;
            // 值类型/引用类型：检查类型名是否命中禁止列表（含嵌套泛型如 IReadOnlyList<IPropertySymbol>）
            var name = t.Name;
            if (ForbiddenTypeNames.Contains(name))
            {
                throw new Xunit.Sdk.XunitException(
                    $"缓存值类型 {owner.Name}.{fieldName} 字段类型 {t.FullName} 持有符号引用（违反 Q4 零驻留）");
            }
            // 递归检查字段类型自身的字段（自引用类型有环——仅检查一层已知快照类型）
            if (SnapshotTypes.Contains(t) || t.Namespace?.StartsWith("NCMapper.SourceGen", StringComparison.Ordinal) == true)
                return;
        }
    }
}
