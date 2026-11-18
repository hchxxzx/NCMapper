using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using NCMapper.Shared;

// ============================================================
// ITypeSymbol 符号版 PathResolver adapter（①）
// ------------------------------------------------------------
// 供源生成器 MappingExtractor ⑥ ⑦ 使用：
//   ⑥ TryResolveDotPath     → ForwardEager（显式点路径 + 索引段 + 诊断回传）
//   ⑦ TryFindFlattenedPath → ForwardEager（推导扁平化，B-2 修复落点）
// 与 Type 版 TypePathResolver 实现同一 IPathResolver 接口，一致性由
// 三向一致性测试网锁定。
// 判据对齐：中间节点统一排除值类型 + string（含 B-2 修复——⑦ 现状仅排除
// 非 Class/Interface，未排除 string，归一时向 ① 行为收敛）；索引段解析与
// ⑥ 现实现一致；末段索引集合性校验对齐 Type 版 ②（归一新增，向运行时收敛）。
// ============================================================

namespace NCMapper.SourceGen
{
    /// <summary>
    /// ITypeSymbol 符号版路径解析器——以 ITypeSymbol/IPropertySymbol 为模型实现
    /// IPathResolver，承载源生成器侧段循环判据（FindProperty 配置感知 + 索引段 +
    /// 中间节点可导航性统一判据）。FindProperty 配置感知逻辑迁自
    /// MappingExtractor（:906/:1065-1100），消费 PathResolutionContext 基本类型字段。
    /// </summary>
    internal sealed class SymbolPathResolver : IPathResolver<ITypeSymbol, IPropertySymbol>
    {
        /// <summary>
        /// 解析点路径字符串为属性链——Split 后委托段列表重载。
        /// </summary>
        /// <param name="rootType">根类型</param>
        /// <param name="dotPath">点路径（如 "A.B.C"，支持 "B[0]" 索引段）</param>
        /// <param name="options">方向/失败语义/反向判据变体</param>
        /// <param name="context">配置上下文（null = 现状 OrdinalIgnoreCase 匹配）</param>
        /// <returns>解析结果；Eager 模式失败返回 Success=false 结果（非 null）</returns>
        public PathResolutionResult<IPropertySymbol> Resolve(
            ITypeSymbol rootType, string dotPath, PathResolutionOptions options, PathResolutionContext? context)
            => Resolve(rootType, dotPath.Split('.'), options, context);

        /// <summary>
        /// 解析段列表为属性链（候选模式/点路径拆分后的核心实现）。
        /// 段循环判据（对齐 ⑥ + B-2 统一）：段匹配 FindProperty 配置感知 +
        /// GetMethod 可读；中间节点须 class/interface 且非 string（值类型/string → NotNavigable）；
        /// 索引段元素类型须可导航；末段索引校验集合性（对齐 ②，归一新增）。
        /// </summary>
        /// <param name="rootType">根类型</param>
        /// <param name="segments">段列表（如 ["A", "B", "C"]，支持 "B[0]" 索引段）</param>
        /// <param name="options">方向/失败语义/反向判据变体（符号版当前仅正向语义承载）</param>
        /// <param name="context">配置上下文（null = 现状 OrdinalIgnoreCase 匹配）</param>
        /// <returns>解析结果；Eager 模式失败返回 Success=false 结果（非 null，失败信息填充）</returns>
        public PathResolutionResult<IPropertySymbol> Resolve(
            ITypeSymbol rootType, IReadOnlyList<string> segments, PathResolutionOptions options, PathResolutionContext? context)
        {
            INamedTypeSymbol? currentType = rootType as INamedTypeSymbol;
            var path = new List<IPropertySymbol>(segments.Count);
            // 索引列表保持与路径等长（消费方契约：SourceIndexes 与 Path 等长；record struct 已消除结果对象堆分配）
            var indexes = new List<int?>(segments.Count);

            for (var i = 0; i < segments.Count; i++)
            {
                if (currentType == null)
                {
                    return Fail(PathSegmentFailure.SegmentNotFound, i, segments[i], path, indexes);
                }

                // 解析集合索引段 "B[0]" → 属性名 B + 索引 0（对齐 ⑥ 现实现）
                var segName = segments[i];
                int? index = null;
                var bracket = segName.IndexOf('[');
                if (bracket >= 0)
                {
                    if (!segName.EndsWith("]") || bracket == 0)
                    {
                        return Fail(PathSegmentFailure.InvalidIndexSyntax, i, segName, path, indexes);
                    }

                    var idxText = segName.Substring(bracket + 1, segName.Length - bracket - 2);
                    if (!int.TryParse(idxText, out var parsed) || parsed < 0)
                    {
                        return Fail(PathSegmentFailure.IndexNotNonNegative, i, segName, path, indexes);
                    }

                    index = parsed;
                    segName = segName.Substring(0, bracket);
                }

                // 属性查找：FindProperty 配置感知（context 填充自 GlobalConfigSnapshot；null = 现状 OrdinalIgnoreCase）
                var prop = FindProperty(currentType, segName, context);
                if (prop == null || prop.GetMethod == null)
                {
                    return Fail(PathSegmentFailure.SegmentNotFound, i, segName, path, indexes);
                }

                path.Add(prop);
                indexes.Add(index);

                if (i < segments.Count - 1)
                {
                    // C3 契约 + B-2 统一：中间节点须 class/interface，非值类型/string。
                    // 索引段取集合元素类型判可导航性（对齐 ⑥）；元素类型 null（非集合带索引）→ 不可导航。
                    // prop.Type 在属性符号上契约非 null，此处以 ! 抑制可空传递（NRT 契约）
                    ITypeSymbol? navType = prop.Type;
                    if (index != null)
                    {
                        navType = TryGetCollectionElementType(prop.Type!);
                        if (navType == null)
                        {
                            return Fail(PathSegmentFailure.NotNavigable, i, segName, path, indexes);
                        }
                    }

                    if (navType is not INamedTypeSymbol nextType
                        || (nextType.TypeKind != TypeKind.Class && nextType.TypeKind != TypeKind.Interface)
                        || nextType.SpecialType == SpecialType.System_String)
                    {
                        // B-2 修复落点：string 中间节点由"后续段失败"提前为"NotNavigable"（最终结果不变，失败时机提前）
                        return Fail(PathSegmentFailure.NotNavigable, i, segName, path, indexes);
                    }

                    currentType = nextType;
                }
                else if (index != null)
                {
                    // 末段索引校验（对齐 ②，归一新增——⑥ 现状无此检查）：
                    // string 实现 IEnumerable<char> 须排除，非集合不可索引 → NotIndexableType
                    // prop.Type 在属性符号上契约非 null，此处以 ! 抑制可空传递（NRT 契约）
                    if (prop.Type!.SpecialType == SpecialType.System_String
                        || TryGetCollectionElementType(prop.Type!) == null)
                    {
                        return Fail(PathSegmentFailure.NotIndexableType, i, segName, path, indexes);
                    }
                }
            }

            return new PathResolutionResult<IPropertySymbol>(
                Success: true, path, indexes, PathSegmentFailure.None, FailedSegment: null, FailedSegmentIndex: 0);
        }

        /// <summary>
        /// 构造失败结果——统一填充失败分类、失败段名与失败段下标（Eager/Contract 均填充，供诊断回传）。
        /// </summary>
        /// <param name="failure">失败分类</param>
        /// <param name="segmentIndex">失败段下标</param>
        /// <param name="segmentName">失败段名</param>
        /// <param name="path">已解析属性链（部分）</param>
        /// <param name="indexes">各段源索引（部分，与路径等长）</param>
        /// <returns>Success=false 的解析结果</returns>
        private static PathResolutionResult<IPropertySymbol> Fail(
            PathSegmentFailure failure, int segmentIndex, string segmentName,
            List<IPropertySymbol> path, List<int?> indexes)
            => new(Success: false, path, indexes, failure, segmentName, segmentIndex);

        /// <summary>
        /// 配置感知属性查找——context 为空/非配置感知时走 OrdinalIgnoreCase 现状语义；
        /// 配置感知时经 NameCandidateResolver 前缀/后缀剥离 + 命名策略匹配。
        /// 逻辑迁自 MappingExtractor.FindProperty 双重载（:906/:1065-1100）。
        /// </summary>
        /// <param name="type">目标类型（含基类/接口遍历）</param>
        /// <param name="name">段名</param>
        /// <param name="context">配置上下文（null = 现状 OrdinalIgnoreCase 匹配）</param>
        /// <returns>匹配属性；无匹配返回 null</returns>
        private static IPropertySymbol? FindProperty(INamedTypeSymbol type, string name, PathResolutionContext? context)
        {
            if (context == null || !context.HasNonDefaultNaming)
            {
                foreach (var t in GetTypeAndBaseTypes(type))
                {
                    foreach (var prop in t.GetMembers().OfType<IPropertySymbol>())
                    {
                        // 补 IsStatic/IsIndexer 过滤
                        //（裁定 = 路径解析语义同缺口同修——静态/索引器成员混入路径段段语义
                        // 错位；可读性过滤经 t65b6 回归裁定回退——写回目标 = write-only 合法消费面）
                        if (prop.IsStatic || prop.IsIndexer) continue;
                        if (StringComparer.OrdinalIgnoreCase.Equals(prop.Name, name))
                        {
                            return prop;
                        }
                    }
                }

                return null;
            }

            var props = GetTypeAndBaseTypes(type)
                .SelectMany(t => t.GetMembers().OfType<IPropertySymbol>())
                .Where(p => !p.IsStatic && !p.IsIndexer)

                .ToList();
            if (props.Count == 0)
            {
                return null;
            }

            var names = props.Select(p => p.Name).ToList();
            // Q6-M3 修复：NamingStrategy == -1（未设置）应回退 IgnoreCase 现状语义
            // 显式设置命名策略仍精确（对齐 MappingExtractor）
            var comparer = context.NamingStrategy <= 0
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
            var dict = NameCandidateResolver.BuildNormalizedSourceKeys(
                names,
                context.Prefixes ?? Array.Empty<string>(),
                context.Postfixes ?? Array.Empty<string>(),
                context.NamingStrategy,
                comparer);
            if (NameCandidateResolver.TryResolveCandidate(dict, name, context.NamingStrategy, out var index))
            {
                return props[index];
            }

            return null;
        }

        /// <summary>
        /// 遍历类型自身、基类链与所有接口（对齐 MappingExtractor.GetTypeAndBaseTypes）。
        /// </summary>
        /// <param name="type">起始类型</param>
        /// <returns>类型与基类/接口序列</returns>
        private static IEnumerable<INamedTypeSymbol> GetTypeAndBaseTypes(INamedTypeSymbol type)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                yield return t;
            }

            // 统一遍历 AllInterfaces（类实现的接口 + 接口继承的接口）
            foreach (var iface in type.AllInterfaces)
            {
                yield return iface;
            }
        }

        /// <summary>
        /// 取集合元素类型——数组返回元素类型；IEnumerable&lt;T&gt; 返回泛型参数；string 与不可枚举返回 null。
        /// 逻辑迁自 MappingExtractor.TryGetCollectionElementType（:1023-1035）。
        /// </summary>
        /// <param name="type">目标类型</param>
        /// <returns>元素类型；非集合/string 返回 null</returns>
        private static ITypeSymbol? TryGetCollectionElementType(ITypeSymbol type)
        {
            if (type is IArrayTypeSymbol arrayType)
            {
                return arrayType.ElementType;
            }

            if (type.SpecialType == SpecialType.System_String)
            {
                return null;
            }

            foreach (var iface in type.AllInterfaces)
            {
                if (iface is INamedTypeSymbol nt && nt.IsGenericType
                    && nt.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
                {
                    return nt.TypeArguments[0];
                }
            }

            return null;
        }
    }
}
