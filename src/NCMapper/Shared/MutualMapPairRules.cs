// -1: 互指对判定规则 —— 单一事实源
// 运行时 PropertyMatcher.IsMutualMapFromPair（Type 反射版）与生成侧
// DetectMutualPairs / MappingExtractor P2-7 守卫（INamedTypeSymbol 版）
// 共用本文件（NCMapper.SourceGen.csproj 经 <Compile Include Link> 双端 include，遵循 DeepCopyGraphRules 先例）。
// 类型系统适配由两端各自完成（Type/INamedTypeSymbol → bool 输入）；判定组合全部收敛于此，单点维护。
// 语义基准：互指对 = 双侧均有 [MapFrom] 且互相指向。
namespace NCMapper.Shared
{
    /// <summary>
    /// 互指对判定规则 —— 纯布尔组合，无类型系统依赖
    /// <para>两端一致性由物理同源（双端 include）保证，一致性测试锁定适配层行为</para>
    /// </summary>
    internal static class MutualMapPairRules
    {
        /// <summary>
        /// 互指对完整判定（运行时 [MapFrom] 双属性版）：
        /// A 标 [MapFrom(B)] 且 B 标 [MapFrom(A)] → 互指对
        /// <para>对应 PropertyMatcher.IsMutualMapFromPair 语义：
        /// sourceAttr.FromSource == targetType &amp;&amp; targetAttr.FromSource == sourceType（均非 null）</para>
        /// </summary>
        /// <param name="hasSourceAttr">源类型声明了 [MapFrom]</param>
        /// <param name="hasTargetAttr">目标类型声明了 [MapFrom]</param>
        /// <param name="sourcePointsToTarget">源类型的 [MapFrom] 指向目标类型</param>
        /// <param name="targetPointsToSource">目标类型的 [MapFrom] 指向源类型</param>
        /// <returns>构成互指对时为 true</returns>
        public static bool IsMutualPair(
            bool hasSourceAttr, bool hasTargetAttr,
            bool sourcePointsToTarget, bool targetPointsToSource)
            => hasSourceAttr && hasTargetAttr && sourcePointsToTarget && targetPointsToSource;

        /// <summary>
        /// 互指映射对判定（生成侧 MappingInfo 交叉版）：
        /// (A.SourceType == B.TargetType &amp;&amp; A.TargetType == B.SourceType) → A、B 构成互指有向对
        /// <para>对应 CodeGenerator.DetectMutualPairs 语义；与 <see cref="IsMutualPair"/> 等价
        /// （[MapFrom(T)] 生成 SourceType→TargetType 映射，两表达互为同义）</para>
        /// </summary>
        /// <param name="aSourceEqualsBTarget">A 的源类型等于 B 的目标类型</param>
        /// <param name="aTargetEqualsBSource">A 的目标类型等于 B 的源类型</param>
        /// <returns>构成互指映射对时为 true</returns>
        public static bool IsMutualMappingPair(
            bool aSourceEqualsBTarget, bool aTargetEqualsBSource)
            => aSourceEqualsBTarget && aTargetEqualsBSource;

        /// <summary>
        /// 单向指向判定（P2-7 守卫用）：源类型声明 [MapFrom(target)] 即构成互指的必要不充分条件
        /// <para>对应 MappingExtractor.DetectMaskPositionMismatch 内 P2-7 守卫语义
        /// （NCMAP026 抑制条件：仅查 source 侧指向，由 NCMAP025 主判定兜底）</para>
        /// </summary>
        /// <param name="hasAttrWithArgs">源类型声明了带构造参数的 [MapFrom]</param>
        /// <param name="fromSourceEqualsTarget">构造参数目标类型等于目标类型</param>
        /// <returns>源类型单向指向目标时为 true</returns>
        public static bool PointsToTarget(bool hasAttrWithArgs, bool fromSourceEqualsTarget)
            => hasAttrWithArgs && fromSourceEqualsTarget;
    }
}
