using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace NCMapper.SourceGen
{
    /// <summary>
    /// DeepCopy 类型图值域分类器— 与符号版 ClassifyDeepCopyGraph 逐条镜像：
    /// 三色迭代 DFS（onPath=灰/done=黑）、回边判 Cyclic 并优先返回、节点预算 10000 超限归 Unknown、
    /// F1 virtual 边归 Unknown、容器按节点快照建边、叶子按节点快照判定。
    /// <para>输入为物化的 DeepCopyNodeShape 图（EqualityKey 等性），输出期零反查符号；
    /// 环检测用 TypeRef.EqualityKey（与 SymbolEqualityComparer.Default 忽略可空注解语义一致）。</para>
    /// </summary>
    internal static class TypeRefDeepCopyClassifier
    {
        /// <summary>
        /// 三值分类节点预算（镜像符号版 NodeBudget；与运行时侧 DefaultNodeBudget 一致）
        /// </summary>
        private const int NodeBudget = 10_000;

        /// <summary>
        /// 对根类型执行三值分类（值域版）
        /// </summary>
        /// <param name="root">根类型 TypeRef（deepCopyTypes 键）</param>
        /// <param name="nodes">节点图（EqualityKey → 节点；由 mapping.DeepCopyNodes 合并）</param>
        /// <param name="sharedDone">跨根共享的已完成节点集（
        /// 仅收录以 Acyclic 结局分类的根的 done 并集：其子树保证无 Unknown 边且无环，后根跳过
        /// 零行为差；Cyclic/Unknown 结局根不并入，防半探索状态污染。跳过仅省 visits 与分配，
        /// unknownEdges/Shape 判定逐位不变）</param>
        /// <returns>三值分类结果 + Unknown 时不可解析边清单（NCMAP016 逐边报告）</returns>
        public static (DeepCopyGraphShape Shape, IReadOnlyList<UnknownEdgeSnapshot> UnknownEdges) Classify(
            TypeRef root, IReadOnlyDictionary<string, DeepCopyNodeShape> nodes, HashSet<string>? sharedDone = null)
        {
            var unknownEdges = new List<UnknownEdgeSnapshot>();
            var onPath = new HashSet<string>(StringComparer.Ordinal);
            var done = new HashSet<string>(StringComparer.Ordinal);
            var stack = new Stack<(TypeRef Type, bool Exit)>();
            stack.Push((root, false));
            bool hasUnknown = false;
            int visits = 0;

            while (stack.Count > 0)
            {
                var (type, exit) = stack.Pop();
                var key = type.EqualityKey;
                if (exit)
                {
                    onPath.Remove(key);
                    done.Add(key);
                    continue;
                }
                // done 判定叠加跨根共享集（命中 = 该节点位于某 Acyclic 子树——无 Unknown
                // 边、无环，跳过探索不改变本根 Shape/unknownEdges）
                if (done.Contains(key) || (sharedDone != null && sharedDone.Contains(key))) continue;
                if (onPath.Contains(key)) return (DeepCopyGraphShape.Cyclic, unknownEdges);

                // 节点预算守卫（镜像符号版）——B1-22 共享跳过使 visits 相应减少（per-root 预算
                // 语义不变；NodeBudget=10_000 防御面在共享跳过下更难触达，登记为不可达面差异）
                if (++visits > NodeBudget) return (DeepCopyGraphShape.Unknown, unknownEdges);

                if (!nodes.TryGetValue(key, out var node))
                {
                    // 节点缺失（理论上物化闭包已完整覆盖）——防御性归 Unknown（不应发生；若触发登记）
                    hasUnknown = true;
                    unknownEdges.Add(new UnknownEdgeSnapshot
                    {
                        OwnerType = type.FullName,
                        PropertyName = "(节点缺失)",
                        PropertyTypeDisplay = type.FullName,
                    });
                    continue;
                }

                if (node.IsLeaf) { done.Add(key); continue; }

                onPath.Add(key);
                stack.Push((type, true));

                if (node.IsCollectionLike)
                {
                    // 容器类型不按属性遍历，按元素/K-V 建边（镜像 CollectContainerEdges）
                    bool containerUnresolvable = false;
                    CollectContainerEdges(node, ref containerUnresolvable);
                    if (containerUnresolvable)
                    {
                        hasUnknown = true;
                        unknownEdges.Add(new UnknownEdgeSnapshot
                        {
                            OwnerType = node.Type.FullName,
                            PropertyName = "(容器元素)",
                            PropertyTypeDisplay = "不可解析",
                            Location = node.Location,
                        });
                    }
                    foreach (var t in GetContainerTargets(node))
                        stack.Push((t, false));
                    continue;
                }

                // 公开实例可读非索引器属性边（镜像 virtual 且非 sealed 且容器非 sealed → Unknown）
                foreach (var prop in node.Properties)
                {
                    if (prop.GetterIsVirtual && !prop.GetterIsSealed && !prop.ContainingTypeIsSealed)
                    {
                        hasUnknown = true;
                        unknownEdges.Add(new UnknownEdgeSnapshot
                        {
                            OwnerType = node.Type.FullName,
                            PropertyName = prop.Name,
                            PropertyTypeDisplay = prop.Type.FullName,
                            Location = prop.Location,
                        });
                        continue;
                    }

                    // 边目标解包（镜像 CollectEdgeTargets）
                    bool unresolvable = false;
                    var targets = CollectEdgeTargets(prop.Type, nodes, ref unresolvable);
                    if (unresolvable)
                    {
                        hasUnknown = true;
                        unknownEdges.Add(new UnknownEdgeSnapshot
                        {
                            OwnerType = node.Type.FullName,
                            PropertyName = prop.Name,
                            PropertyTypeDisplay = prop.Type.FullName,
                            Location = prop.Location,
                        });
                        continue;
                    }
                    foreach (var t in targets) stack.Push((t, false));
                }
            }

            // 仅 Acyclic 结局（完整探索 + 全图无 Unknown 边 + 无环）把本根 done 并入共享集——
            // Cyclic（提前 return 的半探索状态）与 Unknown（含 Unknown 边子树）不并入，防污染；
            // 合并成本 O(|done|)，远低于后根对共享子树的重复探索
            var shape = hasUnknown ? DeepCopyGraphShape.Unknown : DeepCopyGraphShape.Acyclic;
            if (shape == DeepCopyGraphShape.Acyclic && sharedDone != null)
            {
                foreach (var k in done) sharedDone.Add(k);
            }
            return (shape, unknownEdges);
        }

        /// <summary>
        /// 容器建边目标（镜像 CollectContainerEdges 的 targets 收集）
        /// </summary>
        private static IReadOnlyList<TypeRef> GetContainerTargets(DeepCopyNodeShape node)
        {
            var targets = new List<TypeRef>(2);
            if (node.IsDictionary)
            {
                if (node.KeyType != null) targets.Add(node.KeyType);
                if (node.ValueType != null) targets.Add(node.ValueType);
            }
            else if (node.ElementType != null)
            {
                targets.Add(node.ElementType);
            }
            return targets;
        }

        /// <summary>
        /// 容器建边（镜像 CollectContainerEdges：字典 → K+V；IEnumerable&lt;T&gt; → 元素；其余不可解析）
        /// </summary>
        private static void CollectContainerEdges(DeepCopyNodeShape node, ref bool unresolvable)
        {
            if (node.IsDictionary && node.KeyType != null && node.ValueType != null) return;
            if (node.ElementType != null) return;
            unresolvable = true;
        }

        /// <summary>
        /// 解包属性类型收集边目标（镜像 CollectEdgeTargets 解包顺序）
        /// </summary>
        private static List<TypeRef> CollectEdgeTargets(TypeRef type, IReadOnlyDictionary<string, DeepCopyNodeShape> nodes, ref bool unresolvable)
        {
            var targets = new List<TypeRef>(4);
            CollectEdgeTargetsCore(type, nodes, targets, ref unresolvable);
            return targets;
        }

        /// <summary>解包核心（递归：Nullable → 底层、数组 → 元素、叶子 → 无边、object → 不可解析、字典 → K+V、集合 → 元素、非泛型枚举/接口/泛型参数/指针 → 不可解析、其余命名类型 → 边）
        /// 数组/可空/叶子形态用 TypeRef 自身字段（物化闭包对它们入口解包不建节点，
        /// 依赖 node 会导致数组被当普通边 → 同数组多次出现误判 Cyclic）</summary>
        private static void CollectEdgeTargetsCore(TypeRef type, IReadOnlyDictionary<string, DeepCopyNodeShape> nodes, List<TypeRef> targets, ref bool unresolvable)
        {
            nodes.TryGetValue(type.EqualityKey, out var node);

            // Nullable<T> → T（TypeRef 字段优先——不依赖节点）
            if (type.IsNullableValueType && type.UnderlyingType != null)
            {
                CollectEdgeTargetsCore(type.UnderlyingType, nodes, targets, ref unresolvable);
                return;
            }

            // 数组 → 元素（TypeRef 字段优先）
            if (type.IsArray && type.ElementType != null)
            {
                CollectEdgeTargetsCore(type.ElementType, nodes, targets, ref unresolvable);
                return;
            }

            // 叶子（值类型/string/enum/Delegate/白名单）— 无边；TypeRef 字段兜底（节点缺失场景）
            if (node != null && node.IsLeaf) return;
            if (type.IsValueType || type.Kind == TypeKind.Enum || type.SpecialType == SpecialType.System_String) return;

            // object → 不可解析
            if (type.SpecialType == SpecialType.System_Object) { unresolvable = true; return; }

            // 字典 → K 和 V 双边
            if (node != null && node.IsDictionary)
            {
                if (node.KeyType != null && node.ValueType != null)
                {
                    CollectEdgeTargetsCore(node.KeyType, nodes, targets, ref unresolvable);
                    CollectEdgeTargetsCore(node.ValueType, nodes, targets, ref unresolvable);
                    return;
                }
                unresolvable = true;   // 仅实现非泛型 IDictionary — K/V 静态不可解析
                return;
            }
            // TypeRef 字段兜底（节点缺失：字典形态）
            if (node == null && type.IsDictionary && type.DictionaryKeyType != null && type.DictionaryValueType != null)
            {
                CollectEdgeTargetsCore(type.DictionaryKeyType, nodes, targets, ref unresolvable);
                CollectEdgeTargetsCore(type.DictionaryValueType, nodes, targets, ref unresolvable);
                return;
            }

            // 泛型集合 → 元素
            if (node != null && node.ElementType != null)
            {
                CollectEdgeTargetsCore(node.ElementType, nodes, targets, ref unresolvable);
                return;
            }
            if (node == null && type.EnumerableElementType != null)
            {
                CollectEdgeTargetsCore(type.EnumerableElementType, nodes, targets, ref unresolvable);
                return;
            }

            // 非泛型 IEnumerable（无 IEnumerable<T>）
            if (node != null && node.IsNonGenericEnumerable) { unresolvable = true; return; }

            // 接口 / 泛型参数 / 指针
            if (node != null && node.IsInterface) { unresolvable = true; return; }
            if (node != null && (node.IsTypeParameter || node.IsPointer)) { unresolvable = true; return; }
            if (node == null && (type.Kind == TypeKind.Interface || type.Kind == TypeKind.TypeParameter || type.Kind == TypeKind.Pointer))
            { unresolvable = true; return; }

            // 其余命名类型 → 递归边
            targets.Add(type);
        }
    }
}
