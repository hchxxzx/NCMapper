using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using NCMapper.Internal;

// ============================================================
// Type 反射版 PathResolver adapter（①）
// ------------------------------------------------------------
// 供 ExpressionBuilder ① ② ④ ⑤ 与 ProjectionExpressionFactory ③ 使用：
//   ① BuildFlattenedAssignment        → ForwardEager（候选循环）
//   ② BuildExplicitDotPathAssignment  → ForwardContract（索引段 + 5 类 Kind 映射）
//   ③ TryResolveSegmentChain          → ForwardEager（薄包装）
//   ④ BuildReverseFlattenWriteBack    → ReverseEager（候选循环）
//   ⑤ BuildReversePathWriteBack       → ReverseContract（硬/软失败映射）
// 判据与失败分类逐处对齐 映射表；与符号版 SymbolPathResolver
// 实现同一 IPathResolver 接口，一致性由三向一致性测试网（TG1-TG5）锁定。
// ============================================================

namespace NCMapper.Shared
{
    /// <summary>
    /// Type 反射版路径解析器——以 Type/PropertyInfo 为模型实现 IPathResolver，
    /// 承载正向（CanRead + 排除值类型/string + 索引段）与反向（CanWrite + 非 InitOnly +
    /// 可实例化）判据变体，失败分类统一为 PathSegmentFailure（映射表）。
    /// 属性查找复用 ProjectionExpressionFactory.GetPublicInstanceProperties（带缓存），
    /// 集合元素类型判定复用 NavigationResolver.GetCollectionElementType。
    /// </summary>
    internal sealed class TypePathResolver : IPathResolver<Type, PropertyInfo>
    {
        /// <summary>
        /// 解析点路径字符串为属性链——Split 后委托段列表重载。
        /// </summary>
        /// <param name="rootType">根类型</param>
        /// <param name="dotPath">点路径（如 "A.B.C"，支持 "B[0]" 索引段）</param>
        /// <param name="options">方向/失败语义/反向判据变体</param>
        /// <param name="context">配置上下文（Type 版当前仅支持 null = OrdinalIgnoreCase 匹配）</param>
        /// <returns>解析结果；Eager 模式失败返回 Success=false 结果（非 null）</returns>
        [RequiresUnreferencedCode("TypePathResolver.Resolve scans type members via reflection. Some members may be trimmed.")]
        [RequiresDynamicCode("TypePathResolver.Resolve inspects collection element types via reflection. Not supported in AOT.")]
        public PathResolutionResult<PropertyInfo> Resolve(
            Type rootType, string dotPath, PathResolutionOptions options, PathResolutionContext? context)
            => Resolve(rootType, dotPath.Split('.'), options, context);

        /// <summary>
        /// 解析段列表为属性链（候选模式/点路径拆分后的核心实现）。
        /// 段循环判据：正向 CanRead + 中间节点排除值类型/string + 索引段解析；
        /// 反向 RequireWritable（CanWrite + 非 InitOnly）/RequireInstantiable（可 new）。
        /// </summary>
        /// <param name="rootType">根类型</param>
        /// <param name="segments">段列表（如 ["A", "B", "C"]，支持 "B[0]" 索引段）</param>
        /// <param name="options">方向/失败语义/反向判据变体</param>
        /// <param name="context">配置上下文（Type 版当前仅支持 null = OrdinalIgnoreCase 匹配）</param>
        /// <returns>解析结果；Eager 模式失败返回 Success=false 结果（非 null，失败信息填充）</returns>
        [RequiresUnreferencedCode("TypePathResolver.Resolve scans type members via reflection. Some members may be trimmed.")]
        [RequiresDynamicCode("TypePathResolver.Resolve inspects collection element types via reflection. Not supported in AOT.")]
        public PathResolutionResult<PropertyInfo> Resolve(
            Type rootType, IReadOnlyList<string> segments, PathResolutionOptions options, PathResolutionContext? context)
        {
            var currentType = rootType;
            var path = new List<PropertyInfo>(segments.Count);
            // 索引列表保持与路径等长（消费方契约：SourceIndexes 与 Path 等长；record struct 已消除结果对象堆分配）
            var indexes = new List<int?>(segments.Count);

            for (var i = 0; i < segments.Count; i++)
            {
                var segName = segments[i];
                int? index = null;

                // 解析集合索引段 "B[0]" → 属性名 B + 索引 0（对齐 ② 现实现）
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

                // 属性查找：OrdinalIgnoreCase 匹配（context 为空/默认时的现状语义）
                var props = ProjectionExpressionFactory.GetPublicInstanceProperties(currentType);
                var prop = props.FirstOrDefault(p => string.Equals(p.Name, segName, StringComparison.OrdinalIgnoreCase));
                if (prop == null)
                {
                    return Fail(PathSegmentFailure.SegmentNotFound, i, segName, path, indexes);
                }

                if (options.Direction == PathDirection.Forward && !prop.CanRead)
                {
                    return Fail(PathSegmentFailure.SegmentNotFound, i, segName, path, indexes);
                }

                path.Add(prop);
                indexes.Add(index);

                if (i < segments.Count - 1)
                {
                    // 中间节点：索引段取集合元素类型判可导航性；非集合段带索引 → 元素类型为 null → 失败
                    var propType = index != null
                        ? NavigationResolver.GetCollectionElementType(prop.PropertyType)
                        : prop.PropertyType;

                    if (options.Direction == PathDirection.Reverse)
                    {
                        // 反向中间节点判据（对齐 ④）：值类型/string/不可写 → 不可导航；可实例化要求
                        if (propType == null || propType.IsValueType || propType == typeof(string))
                        {
                            return Fail(PathSegmentFailure.NotNavigable, i, segName, path, indexes);
                        }

                        if (!prop.CanWrite || IsInitOnlySetter(prop))
                        {
                            return Fail(PathSegmentFailure.SegmentNotWritable, i, segName, path, indexes);
                        }

                        if (options.RequireInstantiable
                            && (propType.IsInterface || propType.IsAbstract || propType.GetConstructor(Type.EmptyTypes) == null))
                        {
                            return Fail(PathSegmentFailure.NotInstantiable, i, segName, path, indexes);
                        }
                    }
                    else
                    {
                        // 正向中间节点判据（对齐 ① 与 ②）：排除值类型与 string（统一判据，含 B-2 修复落点）
                        // 
                        // 索引段的值类型元素放行——消费方 ChainAccess 已内联集合 null 守卫
                        //（ElementAtOrDefault 语义），值类型元素导航无 NRE 语义缺口；
                        // 非索引段值类型/string 仍不可导航（保持既有契约不变）。
                        if (propType == null || propType == typeof(string))
                        {
                            return Fail(PathSegmentFailure.NotNavigable, i, segName, path, indexes);
                        }
                        if (propType.IsValueType && index == null)
                        {
                            return Fail(PathSegmentFailure.NotNavigable, i, segName, path, indexes);
                        }
                    }

                    currentType = propType;
                }
                else
                {
                    // 叶段判据
                    if (options.Direction == PathDirection.Reverse)
                    {
                        // 反向叶段：setter 需可用（对齐 ④ 与 ⑤）
                        if (!prop.CanWrite || IsInitOnlySetter(prop))
                        {
                            return Fail(PathSegmentFailure.SegmentNotWritable, i, segName, path, indexes);
                        }
                    }

                    // 末段索引校验（对齐 ②）：string 实现 IEnumerable<char> 须排除，非集合不可索引
                    if (index != null
                        && (prop.PropertyType == typeof(string)
                            || NavigationResolver.GetCollectionElementType(prop.PropertyType) == null))
                    {
                        return Fail(PathSegmentFailure.NotIndexableType, i, segName, path, indexes);
                    }
                }
            }

            return new PathResolutionResult<PropertyInfo>(
                Success: true, path, indexes, PathSegmentFailure.None, FailedSegment: null, FailedSegmentIndex: 0);
        }

        /// <summary>
        /// 构造失败结果——统一填充失败分类、失败段名与失败段下标（Eager/Contract 均填充，供 Trace/诊断）。
        /// </summary>
        /// <param name="failure">失败分类</param>
        /// <param name="segmentIndex">失败段下标</param>
        /// <param name="segmentName">失败段名</param>
        /// <param name="path">已解析属性链（部分）</param>
        /// <param name="indexes">各段源索引（部分，与路径等长）</param>
        /// <returns>Success=false 的解析结果</returns>
        private static PathResolutionResult<PropertyInfo> Fail(
            PathSegmentFailure failure, int segmentIndex, string segmentName,
            List<PropertyInfo> path, List<int?> indexes)
            => new(Success: false, path, indexes, failure, segmentName, segmentIndex);

        /// <summary>
        /// 判断属性的 setter 是否为 init 访问器（init-only 属性不可重复赋值，反向写回不可用）。
        /// 判据单一事实源：原 ExpressionBuilder 私有副本经实读
        /// 确认零调用点（W2-2 重构后调用面已迁至本类），已删除并留一行指向注释。
        /// </summary>
        /// <param name="prop">目标属性</param>
        /// <returns>true = setter 为 init 访问器</returns>
        private static bool IsInitOnlySetter(PropertyInfo prop)
        {
            var setter = prop.SetMethod;
            if (setter == null)
            {
                return false;
            }

            foreach (var mod in setter.ReturnParameter.GetRequiredCustomModifiers())
            {
                if (mod.FullName == "System.Runtime.CompilerServices.IsExternalInit")
                {
                    return true;
                }
            }

            return false;
        }
    }
}
