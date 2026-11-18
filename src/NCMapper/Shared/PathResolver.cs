using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

// ============================================================
// 路径语义解析核心（符号无关，零依赖）
// ------------------------------------------------------------
// 本文件是 W2-2 ① 路径语义归一的收敛单点：
//   - 双引擎（Type 反射 + ITypeSymbol 符号）共 7 处段循环 + 2 处候选生成镜像
//     统一收敛到本文件的接口/枚举/record/候选生成；
//   - Type 版 adapter（PathResolver.Type.cs）与符号版 adapter（PathResolver.Symbol.cs）
//     实现同一 IPathResolver<TType, TProperty> 接口，一致性由三向一致性测试网
//     （PathResolverConsistencyTests，TG1-TG5）锁定；
//   - 经 csproj Link 进 NCMapper.SourceGen.csproj，双工程物理同源一份
//     （先例：NameCandidateResolver/DeepCopyGraphRules/MutualMapPairRules/assignmentplan）。
// 零依赖约束：不引用 GlobalConfigView（核心库）/GlobalConfigSnapshot（源生成器）
// 等任一侧配置类型，配置以 PathResolutionContext 基本类型字段传递。
// ============================================================

namespace NCMapper.Shared
{
    /// <summary>
    /// 路径解析方向——正向（源 → 目标）与反向（目标 → 源写回）的段循环判据不同。
    /// 正向判据为"可读且可导航"，反向判据为"可写且可实例化"（反向边界裁决）。
    /// </summary>
    internal enum PathDirection
    {
        /// <summary>
        /// 正向解析：从根类型出发沿点路径逐段向下查找属性（推导扁平化/显式点路径/投影）。
        /// </summary>
        Forward,

        /// <summary>
        /// 反向解析：从目标类型出发沿点路径逐段向下查找可写属性（反向写回语义）。
        /// </summary>
        Reverse,
    }

    /// <summary>
    /// 失败语义模式——Eager 尽力式与 Contract 契约式的失败呈现不同。
    /// Eager 模式失败返回 Success=false 的结果（调用方 Trace/跳过）；
    /// Contract 模式失败返回失败枚举分类（调用方决定抛异常或软失败）。
    /// </summary>
    internal enum PathFailureMode
    {
        /// <summary>
        /// 尽力式：失败不抛异常，返回 Success=false 的结果，调用方按现状语义 Trace/跳过。
        /// </summary>
        Eager,

        /// <summary>
        /// 契约式：失败仍返回带枚举分类的结果，调用方按分类决定抛异常或软失败。
        /// </summary>
        Contract,
    }

    /// <summary>
    /// 段解析失败统一分类——覆盖 7 处现有失败形态的最小并集（逐处映射表）。
    /// 现有 15 条失败分支全部落入 8 值枚举（None 除外 7 值），无现有分支需要新增枚举值。
    /// </summary>
    internal enum PathSegmentFailure
    {
        /// <summary>
        /// 成功（无失败）。
        /// </summary>
        None,

        /// <summary>
        /// 段缺失/不可读（正向）：属性名在目标类型上不存在。
        /// </summary>
        SegmentNotFound,

        /// <summary>
        /// 段不可写或 init-only（反向）：属性无 setter 或为 init 访问器。
        /// </summary>
        SegmentNotWritable,

        /// <summary>
        /// 中间节点值类型或 string，不可继续导航（含 B-2 修复后的统一判据）。
        /// </summary>
        NotNavigable,

        /// <summary>
        /// 中间节点接口/抽象/无公共无参构造，不可实例化（反向）。
        /// </summary>
        NotInstantiable,

        /// <summary>
        /// 索引段语法非法（"B["、"[0]" 等）。
        /// </summary>
        InvalidIndexSyntax,

        /// <summary>
        /// 索引为负。
        /// </summary>
        IndexNotNonNegative,

        /// <summary>
        /// 索引段目标非集合（含 string 排除）。
        /// </summary>
        NotIndexableType,
    }

    /// <summary>
    /// 零依赖配置上下文——adapter 各自从本侧配置填充（核心库 GlobalConfigView / 源生成器 GlobalConfigSnapshot），
    /// 本类型不引用任一侧配置类型（依赖倒置）。
    /// </summary>
    /// <param name="HasNonDefaultNaming">是否需配置感知匹配（命名策略/前后缀）。</param>
    /// <param name="NamingStrategy">命名策略（-1=未设置→IgnoreCase，>0=Ordinal，同 GlobalConfigReader 语义）。</param>
    /// <param name="Prefixes">前缀剥离清单。</param>
    /// <param name="Postfixes">后缀剥离清单。</param>
    internal sealed record PathResolutionContext(
        bool HasNonDefaultNaming,
        int NamingStrategy,
        string[] Prefixes,
        string[] Postfixes);

    /// <summary>
    /// 路径解析结果——单一失败模型：Success=false 时 Failure 有效；
    /// Eager 模式失败也填充 FailedSegment/FailedSegmentIndex 供 Trace/诊断。
    /// 不携带"失败类型"对象（泛型约束下顶层 record 无法引用 TType），
    /// 失败段所在类型的呈现由调用方按 FailedSegmentIndex 自行取（处 ⑥ 诊断用
    /// ITypeSymbol，处 ②⑤ 异常文案用 Type.Name）。
    /// </summary>
    /// <typeparam name="TProperty">属性符号类型（核心库 PropertyInfo / 源生成器 IPropertySymbol）。</typeparam>
    /// <param name="Success">是否解析成功。</param>
    /// <param name="Path">解析得到的属性链（成功时非空；失败时可为空列表）。</param>
    /// <param name="SourceIndexes">各段的源索引（非索引段为 null；索引段为下标，如 "B[0]" 的 0）。</param>
    /// <param name="Failure">失败分类（Success=true 时为 None）。</param>
    /// <param name="FailedSegment">失败段名（失败时有效，供 Trace/诊断回传）。</param>
    /// <param name="FailedSegmentIndex">失败段下标（Path/SourceIndexes 中的位置；失败时有效）。</param>
    /// <remarks>
    /// 内联优化：record class → readonly record struct
    /// 候选循环（①④ 推导扁平化）每候选一次失败解析，struct 消除结果对象堆分配
    /// （冷缓存首次构建 +90%~+120% 的主因之一，优化后与自实现分配量持平）。
    /// </remarks>
    internal readonly record struct PathResolutionResult<TProperty>(
        bool Success,
        List<TProperty> Path,
        List<int?> SourceIndexes,
        PathSegmentFailure Failure,
        string? FailedSegment,
        int FailedSegmentIndex);

    /// <summary>
    /// 符号无关路径解析器——Type 版（核心库，TypePathResolver）与 ITypeSymbol 版
    /// （源生成器，SymbolPathResolver）实现同一接口，一致性由三向一致性测试网锁定。
    /// </summary>
    /// <typeparam name="TType">根类型模型（核心库 Type / 源生成器 ITypeSymbol）。</typeparam>
    /// <typeparam name="TProperty">属性符号模型（核心库 PropertyInfo / 源生成器 IPropertySymbol）。</typeparam>
    internal interface IPathResolver<TType, TProperty>
    {
        /// <summary>
        /// 解析点路径为属性链（点路径字符串版，内部 Split 后委托段列表版）。
        /// </summary>
        /// <param name="rootType">根类型</param>
        /// <param name="dotPath">点路径（如 "A.B.C"，支持 "B[0]" 索引段）</param>
        /// <param name="options">方向/失败语义/反向判据变体</param>
        /// <param name="context">配置上下文（null = 现状 OrdinalIgnoreCase 匹配）</param>
        /// <returns>解析结果；Eager 模式失败返回 Success=false 结果（非 null）</returns>
        // 条件编译：本文件经 Link 双工程共享——核心库（net8+）Type 引擎实现需反射上下文，
        // 接口须与实现标注一致（IL2046/IL3051）；SourceGen（netstandard2.0）无 RUC/RDC 类型且
        // Symbol 引擎编译期运行无需 AOT 保护，故 netstandard 侧不标注。
#if NET8_0_OR_GREATER
        [RequiresUnreferencedCode("IPathResolver.Resolve may scan type members via reflection (Type engine implementation). Some members may be trimmed.")]
        [RequiresDynamicCode("IPathResolver.Resolve may build expressions via MakeGenericMethod/MakeGenericType (Type engine implementation). Not supported in AOT.")]
#endif
        PathResolutionResult<TProperty> Resolve(
            TType rootType, string dotPath, PathResolutionOptions options, PathResolutionContext? context);

        /// <summary>
        /// 解析段列表为属性链（候选模式——推导扁平化/候选生成场景，段列表非点路径字符串）。
        /// 带索引字符的段名（如 "B[0]"）由本重载直接承载，避免"候选 Join 成串再由核心 Split"往返
        /// （往返存在解析差异风险）。
        /// </summary>
        /// <param name="rootType">根类型</param>
        /// <param name="segments">段列表（如 ["A", "B", "C"]，支持 "B[0]" 索引段）</param>
        /// <param name="options">方向/失败语义/反向判据变体</param>
        /// <param name="context">配置上下文（null = 现状 OrdinalIgnoreCase 匹配）</param>
        /// <returns>解析结果；Eager 模式失败返回 Success=false 结果（非 null）</returns>
#if NET8_0_OR_GREATER
        [RequiresUnreferencedCode("IPathResolver.Resolve may scan type members via reflection (Type engine implementation). Some members may be trimmed.")]
        [RequiresDynamicCode("IPathResolver.Resolve may build expressions via MakeGenericMethod/MakeGenericType (Type engine implementation). Not supported in AOT.")]
#endif
        PathResolutionResult<TProperty> Resolve(
            TType rootType, IReadOnlyList<string> segments, PathResolutionOptions options, PathResolutionContext? context);
    }

    /// <summary>
    /// 路径解析选项——方向/失败语义/反向判据变体（反向边界裁决）。
    /// 提供 4 个静态预置组合，覆盖现有 7 处段循环的全部调用形态。
    /// </summary>
    /// <param name="Direction">解析方向（正向/反向）。</param>
    /// <param name="FailureMode">失败语义模式（尽力式/契约式）。</param>
    /// <param name="RequireWritable">反向判据变体：段须 CanWrite 且非 init-only。</param>
    /// <param name="RequireInstantiable">反向判据变体：中间节点须可 new（公共无参构造/非接口非抽象）。</param>
    internal sealed record PathResolutionOptions(
        PathDirection Direction,
        PathFailureMode FailureMode,
        bool RequireWritable = false,
        bool RequireInstantiable = false)
    {
        /// <summary>
        /// 正向尽力式（推导扁平化/投影）：失败返回 null 结果，调用方 Trace/跳过。
        /// </summary>
        public static PathResolutionOptions ForwardEager { get; } = new(PathDirection.Forward, PathFailureMode.Eager);

        /// <summary>
        /// 正向契约式（显式点路径）：失败返回枚举分类，调用方抛 MapConfigurationException。
        /// </summary>
        public static PathResolutionOptions ForwardContract { get; } = new(PathDirection.Forward, PathFailureMode.Contract);

        /// <summary>
        /// 反向尽力式（推导折叠）：要求段可写且中间节点可实例化，失败返回 null 结果。
        /// </summary>
        public static PathResolutionOptions ReverseEager { get; } =
            new(PathDirection.Reverse, PathFailureMode.Eager, RequireWritable: true, RequireInstantiable: true);

        /// <summary>
        /// 反向契约式（显式反向写回）：要求段可写且中间节点可实例化；
        /// 软失败细分由调用方按 Failure 枚举映射（SegmentNotWritable/NotInstantiable → 跳过+Trace）。
        /// </summary>
        public static PathResolutionOptions ReverseContract { get; } =
            new(PathDirection.Reverse, PathFailureMode.Contract, RequireWritable: true, RequireInstantiable: true);
    }

    /// <summary>
    /// 候选路径生成（M-1/M-2 收敛单点）——PascalCase 拆解，拆点 ≤4、2-5 段、段数升序。
    /// 原 PEF.BuildPathCandidates（Type 引擎）与 MappingExtractor.BuildPathCandidates（符号引擎）
    /// 两份镜像实现统一收敛到本类的 Build 方法，双工程经 Link 物理同源。
    /// </summary>
    internal static class PathCandidateGenerator
    {
        /// <summary>
        /// 将 PascalCase 属性名拆解为候选路径组合（按段数升序，短路径优先）。
        /// 首元素为原名单段（不拆）；大写字母位置为拆点，拆点超过 4 个则仅返回单段候选；
        /// 其余候选按 2^拆点数 组合掩码生成，仅保留 2-5 段的组合。
        /// </summary>
        /// <param name="name">目标属性名</param>
        /// <returns>候选路径列表（首元素为原名单段）</returns>
        public static List<List<string>> Build(string name)
        {
            var result = new List<List<string>> { new() { name } };

            var splitPoints = new List<int>();
            for (int i = 1; i < name.Length; i++)
            {
                if (char.IsUpper(name[i]))
                {
                    splitPoints.Add(i);
                }
            }

            if (splitPoints.Count > 4)
            {
                return result;
            }

            int combinations = 1 << splitPoints.Count;
            for (int mask = 1; mask < combinations; mask++)
            {
                var parts = new List<string>();
                int prev = 0;
                for (int i = 0; i < splitPoints.Count; i++)
                {
                    if ((mask & (1 << i)) != 0)
                    {
                        parts.Add(name.Substring(prev, splitPoints[i] - prev));
                        prev = splitPoints[i];
                    }
                }

                parts.Add(name.Substring(prev));

                if (parts.Count >= 2 && parts.Count <= 5)
                {
                    result.Add(parts);
                }
            }

            result.Sort((a, b) => a.Count.CompareTo(b.Count));
            return result;
        }
    }
}
