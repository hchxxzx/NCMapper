using System.Text;
using Microsoft.CodeAnalysis;
namespace NCMapper.SourceGen;
    /// <summary>
    /// 类型判断辅助方法 — 隐式转换/可空/嵌套/集合等
    /// </summary>
    internal static class TypeHelpers
    {


        /// <summary>
        /// 判断基元类型是否可隐式转换（int→long 等），使用精确查找表（C# 规范）
        /// <para>与运行时 <c>TypeConverter.IsImplicitConvertible</c> 保持一致</para>
        /// </summary>
        /// <param name="sourceType">源类型符号</param>
        /// <param name="targetType">目标类型符号</param>
        /// <returns>如果源类型可隐式转换为目标类型则返回 true</returns>
        public static bool IsImplicitConvertible(ITypeSymbol sourceType, ITypeSymbol targetType)
        {
            if (!sourceType.IsValueType || !targetType.IsValueType) return false;
            if (sourceType.TypeKind == TypeKind.Enum || targetType.TypeKind == TypeKind.Enum) return false;
            if (SymbolEqualityComparer.Default.Equals(sourceType, targetType)) return true;

            // 转换表单点——TypeCompatibilityCore.ImplicitConversions
            //（原 T-2 表删除，与 T-1/T-3 三表全量比对等价，D1.2）
            return NCMapper.Shared.TypeCompatibilityCore.ImplicitConversions
                       .TryGetValue(SymbolCompatAdapter.GetSpecialTypeId(sourceType), out var targets)
                   && targets.Contains(SymbolCompatAdapter.GetSpecialTypeId(targetType));
        }

        /// <summary>
        /// 判断源为可空类型、目标为非可空类型
        /// </summary>
        /// <param name="sourceType">源类型符号</param>
        /// <param name="targetType">目标类型符号</param>
        /// <returns>源为 Nullable&lt;T&gt; 且目标为非可空值类型时返回 true</returns>
        public static bool IsNullableToNonNullable(ITypeSymbol sourceType, ITypeSymbol targetType)
        {
            if (sourceType is not INamedTypeSymbol srcNamed) return false;
            if (srcNamed.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T) return false;
            if (!targetType.IsValueType) return false;
            if (targetType is INamedTypeSymbol tgtNamed
                && tgtNamed.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
                return false;
            return true;
        }

        /// <summary>
        /// 判断源为非可空类型、目标为可空类型
        /// </summary>
        /// <param name="sourceType">源类型符号</param>
        /// <param name="targetType">目标类型符号</param>
        /// <returns>源为非可空值类型且目标为 Nullable&lt;T&gt; 时返回 true</returns>
        public static bool IsNonNullableToNullable(ITypeSymbol sourceType, ITypeSymbol targetType)
        {
            if (sourceType is not INamedTypeSymbol srcNamed) return false;
            if (srcNamed.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T) return false;
            if (!sourceType.IsValueType) return false; // 引用类型（如 string）不可直接转 Nullable<T>
            if (!targetType.IsValueType) return false;
            if (targetType is not INamedTypeSymbol tgtNamed) return false;
            return tgtNamed.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
        }

        /// <summary>
        /// 判断源为 Nullable&lt;T&gt;、目标为 Nullable&lt;U&gt;（双方均为可空值类型）
        /// </summary>
        /// <param name="sourceType">源类型符号</param>
        /// <param name="targetType">目标类型符号</param>
        /// <returns>双方均为 Nullable&lt;T&gt; 时返回 true</returns>
        public static bool IsNullableToNullable(ITypeSymbol sourceType, ITypeSymbol targetType)
        {
            if (sourceType is not INamedTypeSymbol srcNamed) return false;
            if (srcNamed.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T) return false;
            if (targetType is not INamedTypeSymbol tgtNamed) return false;
            return tgtNamed.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
        }

        /// <summary>
        /// 判断是否为嵌套 class 属性（不同 class 类型，非 string，非泛型）
        /// </summary>
        /// <param name="sourceType">源类型符号</param>
        /// <param name="targetType">目标类型符号</param>
        /// <returns>两端均为非 string、非泛型的 class 且类型不同时返回 true</returns>
        public static bool IsNestedClassProperty(ITypeSymbol sourceType, ITypeSymbol targetType)
        {
            if (sourceType.TypeKind != TypeKind.Class || targetType.TypeKind != TypeKind.Class) return false;
            if (sourceType.SpecialType == SpecialType.System_String || targetType.SpecialType == SpecialType.System_String) return false;
            if (sourceType is INamedTypeSymbol srcN && srcN.IsGenericType) return false;
            if (targetType is INamedTypeSymbol tgtN && tgtN.IsGenericType) return false;
            return !SymbolEqualityComparer.Default.Equals(sourceType, targetType);
        }

        /// <summary>
        /// 尝试获取集合元素类型对
        /// </summary>
        /// <param name="sourceType">源集合类型符号</param>
        /// <param name="targetType">目标集合类型符号</param>
        /// <param name="elemSrc">输出：源集合元素类型</param>
        /// <param name="elemTgt">输出：目标集合元素类型</param>
        /// <returns>成功提取元素类型对返回 true；非集合或为字典/string 时返回 false</returns>
        public static bool TryGetCollectionElementTypes(ITypeSymbol sourceType, ITypeSymbol targetType,
            out ITypeSymbol elemSrc, out ITypeSymbol elemTgt)
        {
            elemSrc = null!;
            elemTgt = null!;

            if (sourceType.SpecialType == SpecialType.System_String) return false;
            if (targetType.SpecialType == SpecialType.System_String) return false;

            if (IsDictionaryType(sourceType) || IsDictionaryType(targetType)) return false;

            if (!ImplementsIEnumerable(sourceType) || !ImplementsIEnumerable(targetType)) return false;

            if (sourceType is IArrayTypeSymbol srcArr && targetType is IArrayTypeSymbol tgtArr)
            {
                elemSrc = srcArr.ElementType;
                elemTgt = tgtArr.ElementType;
                return true;
            }

            var srcElem = GetGenericEnumerableDataType(sourceType);
            var tgtElem = GetGenericEnumerableDataType(targetType);
            if (srcElem != null && tgtElem != null)
            {
                elemSrc = srcElem;
                elemTgt = tgtElem;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 尝试获取集合元素类型（单类型版本，用于 DeepCopy）
        /// </summary>
        /// <param name="type">集合类型符号</param>
        /// <param name="elemType">输出：集合元素类型</param>
        /// <returns>成功提取元素类型返回 true；非集合或为字典/string 时返回 false</returns>
        public static bool TryGetCollectionElementType(ITypeSymbol type, out ITypeSymbol elemType)
        {
            elemType = null!;
            if (type.SpecialType == SpecialType.System_String) return false;
            if (IsDictionaryType(type)) return false;
            if (!ImplementsIEnumerable(type)) return false;

            if (type is IArrayTypeSymbol arr)
            {
                elemType = arr.ElementType;
                return true;
            }

            var resolved = GetGenericEnumerableDataType(type);
            if (resolved == null) return false;
            elemType = resolved;
            return true;
        }

        /// <summary>
        /// 判断类型是否为字典类型
        /// </summary>
        /// <param name="type">要检查的类型符号</param>
        /// <returns>类型本身或其实现的接口包含 IDictionary 或 IReadOnlyDictionary 时返回 true</returns>
        public static bool IsDictionaryType(ITypeSymbol type)
        {
            // 检查类型自身是否为 IDictionary/IReadOnlyDictionary 接口（接口的 AllInterfaces 不含自身）
            // 加命名空间检查，避免误判用户自定义同名类型
            if (NCMapper.Shared.DeepCopyGraphRules.IsDictionaryInterfaceName(type.Name)
                && NCMapper.Shared.DeepCopyGraphRules.IsSystemCollectionsNamespace(type.ContainingNamespace?.ToDisplayString()))
                return true;
            return type.AllInterfaces.Any(i =>
                NCMapper.Shared.DeepCopyGraphRules.IsDictionaryInterfaceName(i.Name)
                && NCMapper.Shared.DeepCopyGraphRules.IsSystemCollectionsNamespace(i.ContainingNamespace?.ToDisplayString()));
        }

        /// <summary>
        /// 尝试获取源/目标字典类型的 Key/Value 类型对
        /// <para>检查两端均为字典类型（实现 IDictionary&lt;K,V&gt; 或 IReadOnlyDictionary&lt;K,V&gt;），
        /// 并提取各自的 K/V 泛型参数</para>
        /// </summary>
        /// <param name="sourceType">源字典类型符号</param>
        /// <param name="targetType">目标字典类型符号</param>
        /// <param name="srcKey">输出：源字典 Key 类型</param>
        /// <param name="srcVal">输出：源字典 Value 类型</param>
        /// <param name="tgtKey">输出：目标字典 Key 类型</param>
        /// <param name="tgtVal">输出：目标字典 Value 类型</param>
        /// <returns>两端均为字典类型且成功提取 K/V 类型对时返回 true</returns>
        public static bool TryGetDictionaryKeyValueTypes(ITypeSymbol sourceType, ITypeSymbol targetType,
            out ITypeSymbol srcKey, out ITypeSymbol srcVal,
            out ITypeSymbol tgtKey, out ITypeSymbol tgtVal)
        {
            srcKey = null!;
            srcVal = null!;
            tgtKey = null!;
            tgtVal = null!;

            if (!IsDictionaryType(sourceType) || !IsDictionaryType(targetType)) return false;

            var srcKV = GetDictionaryKeyValueTypes(sourceType);
            var tgtKV = GetDictionaryKeyValueTypes(targetType);
            if (srcKV == null || tgtKV == null) return false;

            srcKey = srcKV.Value.Key;
            srcVal = srcKV.Value.Value;
            tgtKey = tgtKV.Value.Key;
            tgtVal = tgtKV.Value.Value;
            return true;
        }

        /// <summary>
        /// 尝试获取单个字典类型的 Key/Value 泛型参数对（单类型版本）
        /// <para> T3（盲区①修复）：供深拷贝图三值分类
        /// <c>CodeGenerator.ClassifyDeepCopyGraph</c> 对字典属性建 K+V 双边使用。</para>
        /// </summary>
        /// <param name="type">字典类型符号</param>
        /// <param name="keyType">输出：Key 类型</param>
        /// <param name="valueType">输出：Value 类型</param>
        /// <returns>字典类型且成功提取 K/V 类型对时返回 true；
        /// 非字典或仅实现非泛型 IDictionary（K/V 静态不可解析）返回 false</returns>
        public static bool TryGetDictionaryKeyValueTypes(ITypeSymbol type,
            out ITypeSymbol keyType, out ITypeSymbol valueType)
        {
            keyType = null!;
            valueType = null!;

            if (!IsDictionaryType(type)) return false;

            var kv = GetDictionaryKeyValueTypes(type);
            if (kv == null) return false;

            keyType = kv.Value.Key;
            valueType = kv.Value.Value;
            return true;
        }

        /// <summary>
        /// 从字典类型中提取 Key/Value 泛型参数对
        /// </summary>
        /// <param name="type">字典类型符号</param>
        /// <returns>Key/Value 类型对；非字典类型返回 null</returns>
        private static (ITypeSymbol Key, ITypeSymbol Value)? GetDictionaryKeyValueTypes(ITypeSymbol type)
        {
            // 类型自身即为 IDictionary<,>/IReadOnlyDictionary<,> 接口（AllInterfaces 不含自身）
            if (type is INamedTypeSymbol selfIface && selfIface.IsGenericType
                && (type.Name == "IDictionary" || type.Name == "IReadOnlyDictionary"))
            {
                return (selfIface.TypeArguments[0], selfIface.TypeArguments[1]);
            }

            // 从 AllInterfaces 查找 IDictionary<,>/IReadOnlyDictionary<,>
            foreach (var iface in type.AllInterfaces)
            {
                if (iface is INamedTypeSymbol named && named.IsGenericType
                    && (iface.Name == "IDictionary" || iface.Name == "IReadOnlyDictionary"))
                {
                    return (named.TypeArguments[0], named.TypeArguments[1]);
                }
            }

            // 兜底：类型自身即为泛型字典（Dictionary<,>/SortedDictionary<,>/SortedList<,>）
            if (type is INamedTypeSymbol selfNamed && selfNamed.IsGenericType
                && (type.Name == "Dictionary" || type.Name == "SortedDictionary" || type.Name == "SortedList"))
            {
                return (selfNamed.TypeArguments[0], selfNamed.TypeArguments[1]);
            }

            return null;
        }

        /// <summary>
        /// 判断类型是否实现了 IEnumerable
        /// </summary>
        /// <param name="type">要检查的类型符号</param>
        /// <returns>为数组或实现了 IEnumerable 接口时返回 true</returns>
        public static bool ImplementsIEnumerable(ITypeSymbol type)
        {
            if (type is IArrayTypeSymbol) return true;
            return type.AllInterfaces.Any(i => i.SpecialType == SpecialType.System_Collections_IEnumerable);
        }

        /// <summary>
        /// 从泛型集合类型中提取元素类型
        /// </summary>
        /// <param name="type">集合类型符号</param>
        /// <returns>集合的元素类型；数组返回元素类型，非泛型集合返回 null</returns>
        public static ITypeSymbol? GetGenericEnumerableDataType(ITypeSymbol type)
        {
            if (type is IArrayTypeSymbol arr)
                return arr.ElementType;

            // 检查类型自身是否为 IEnumerable<T>（接口的 AllInterfaces 不含自身，
            // 与 IsDictionaryType 的 自身检查同理）。 T6 一致性语料发现：
            // 属性类型直接声明为 IEnumerable<T> 时，原实现提取不到元素误归 Unknown，
            // 而运行时侧 NavigationResolver.GetCollectionElementType 可解析 → 两侧分类分歧。
            if (type is INamedTypeSymbol self
                && self.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
            {
                return self.TypeArguments[0];
            }

            foreach (var iface in type.AllInterfaces)
            {
                if (iface is INamedTypeSymbol named
                    && named.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
                {
                    return named.TypeArguments[0];
                }
            }
            return null;
        }

        /// <summary>
        /// 判断类型是否为 Nullable&lt;T&gt;
        /// </summary>
        /// <param name="type">要检查的类型符号</param>
        /// <returns>类型为 Nullable&lt;T&gt; 时返回 true</returns>
        public static bool IsNullableValueType(ITypeSymbol type)
        {
            return type is INamedTypeSymbol named
                && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
        }

        /// <summary>
        /// 获取非 Nullable 底层类型
        /// </summary>
        /// <param name="type">要检查的类型符号</param>
        /// <returns>Nullable&lt;T&gt; 的底层类型；非可空类型原样返回</returns>
        public static ITypeSymbol GetUnderlyingType(ITypeSymbol type)
        {
            if (type is INamedTypeSymbol named && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
                return named.TypeArguments[0];
            return type;
        }

        /// <summary>
        /// 获取枚举类型的所有值
        /// </summary>
        /// <param name="enumType">枚举类型符号</param>
        /// <returns>枚举成员名称与常量值的列表</returns>
        public static List<(string Name, object Value)> GetEnumValues(ITypeSymbol enumType)
        {
            var values = new List<(string Name, object Value)>();
            foreach (var member in enumType.GetMembers().OfType<IFieldSymbol>())
            {
                if (member.ConstantValue != null)
                    values.Add((member.Name, member.ConstantValue));
            }
            return values;
        }

        /// <summary>
        /// 判断类型是否为 string
        /// <para>string 实现 IEnumerable&lt;char&gt;，若照抄 ImplementsIEnumerable 条件会把
        /// string→Dictionary 误归为受控异常路径；string 与任何非 string 目标均属"完全不相容"→ 跳过</para>
        /// </summary>
        /// <param name="type">要检查的类型符号</param>
        /// <returns>类型为 string 时返回 true</returns>
        public static bool IsStringType(ITypeSymbol type)
        {
            return type.SpecialType == SpecialType.System_String;
        }

        /// <summary>
        /// 判定源/目标类型是否存在任何生成侧转换路径
        /// <para>与 <c>GenerateSinglePropertyAssignment</c> 分发链 1:1 对齐（步骤 1-9）：
        /// 等类型 / 隐式转换 / 枚举（enum↔enum·enum↔string） / 嵌套类 / 集合 / 字典 /
        /// 真 List·数组→Dictionary（排除 string） / 值类型可空组合。
        /// 完全不相容返回 false —— 调用方（映射分发链）应跳过该成员并输出 NCMAP027 诊断。</para>
        /// </summary>
        /// <param name="source">源属性类型</param>
        /// <param name="target">目标属性类型</param>
        /// <returns>存在生成侧转换路径返回 true；完全不相容返回 false</returns>
        public static bool HasMappingPath(ITypeSymbol source, ITypeSymbol target)
        {
        // ③：判据链（步骤 0/0b/1-10）收敛至 TypeCompatibilityCore 单点
        //（原门面内判据逻辑删除，镜像逻辑迁入 SymbolCompatAdapter 与核心）；本门面保留签名，
        // 消费方（一致性测试/映射路径判定）零改动。步骤 0b 剥离与 Error 兜底由 adapter 承载。
        return NCMapper.Shared.TypeCompatibilityCore.CanConvert(
            SymbolCompatAdapter.Resolve(source),
            SymbolCompatAdapter.Resolve(target));
        }

        // ② 枢纽深化：SymbolAssignmentKind 枚举 + PlanSymbol(ITypeSymbol) 方法已删除。
        // 生成侧统一使用 NCMapper.Shared.AssignmentKind（通过 SourceGen.csproj Link assignmentplan.cs 共享）。
        // PlanSymbolAdapter 直接调用 TypeRefHelpers public 形态判定方法，消除 SymbolAssignmentKind 双宇宙。







        /// <summary>
        /// 生成字典实例构造表达式
        /// <para>三分层：确切 Dictionary&lt;K,V&gt; / 字典接口（IDictionary/IReadOnlyDictionary）
        /// → Dictionary&lt;K,V&gt; 基类构造；字典子类可无参构造 → 子类实例；子类不可无参构造 → null
        /// （调用方差异登记，避免基类局部变量赋子类属性的 CS0266）。与 DeepCopy 字典复制同构。</para>
        /// </summary>
        /// <param name="propType">目标属性类型</param>
        /// <param name="keyTypeName">Key 类型名</param>
        /// <param name="valueTypeName">Value 类型名</param>
        /// <returns>字典实例构造表达式；字典子类不可无参构造返回 null</returns>
        public static string? TryGetDictionaryCreationExpression(ITypeSymbol propType, string keyTypeName, string valueTypeName)
        {
            var isSystemCollections = propType.ContainingNamespace?.ToDisplayString().StartsWith(
                "System.Collections", StringComparison.Ordinal) == true;
            var isInterfaceDict = propType.TypeKind == TypeKind.Interface
                && (propType.Name == "IDictionary" || propType.Name == "IReadOnlyDictionary")
                && isSystemCollections;
            var isExactDictionary = propType is INamedTypeSymbol named
                && named.Name == "Dictionary"
                && isSystemCollections;
            var hasPublicParameterlessCtor = propType is INamedTypeSymbol pn
                && pn.InstanceConstructors.Any(c =>
                    c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public);

            if (isInterfaceDict || isExactDictionary)
                return $"new System.Collections.Generic.Dictionary<{keyTypeName}, {valueTypeName}>()";
            if (hasPublicParameterlessCtor)
                return $"new {propType.ToDisplayString()}()";
            return null;
        }

        /// <summary>
        /// 判定成员是否可在生成代码中直接访问（CS0122 防护）
        /// <para>生成代码跨程序集场景（DTO 程序集 vs NCMapper 运行时）只能访问 public 成员；
        /// internal/protected/private 一律不可直接访问</para>
        /// </summary>
        /// <param name="symbol">属性/类型/构造器等成员符号</param>
        /// <returns>public 可访问返回 true</returns>
        public static bool IsAccessibleFromGeneratedCode(ISymbol symbol)
        {
            return symbol.DeclaredAccessibility == Accessibility.Public;
        }

        /// <summary>
        /// 按目标类型可空性生成 null 表达式（NRT 安全，CS8625 防护）
        /// <para>可空值类型（Nullable&lt;T&gt;）与可空引用类型（NRT Annotated）→ null；
        /// 不可空引用类型 → default!（null-forgiving，明确"运行时可能为 null 但编译期抑制"）；
        /// 非可空值类型 → default（null 字面量对值类型不合法）</para>
        /// </summary>
        /// <param name="targetType">目标类型符号</param>
        /// <returns>null 表达式文本</returns>
        public static string NullLiteralFor(ITypeSymbol targetType)
        {
            // 可空值类型（Nullable<T>）→ null 合法
            if (IsNullableValueType(targetType))
                return "null";

            // 引用类型（class/interface/delegate/array/dynamic/type parameter）：按 NRT 注解
            if (targetType.TypeKind is TypeKind.Class or TypeKind.Interface
                or TypeKind.Delegate or TypeKind.Array or TypeKind.Dynamic or TypeKind.TypeParameter)
            {
                return targetType.NullableAnnotation == NullableAnnotation.Annotated
                    ? "null"
                    : "default!";
            }

            // 非可空值类型 → null 字面量不合法，用 default
            return "default";
        }
    }
