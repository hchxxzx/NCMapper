using System;
using System.Diagnostics.CodeAnalysis;

namespace NCMapper.Shared;

/// <summary>
/// 类型兼容判定门面（Type 反射版）— ③ 收敛后的薄包装。
/// <para>判据链（步骤 0/1-10）与 ImplicitConversions 转换表已收敛至
/// <see cref="TypeCompatibilityCore"/> 单点（Shared 零依赖 + csproj Link 进 SourceGen）；
/// 本类仅保留 CanConvert 门面签名（MapValidator:418 消费方零改动），内部经
/// <see cref="TypeCompatAdapter"/>（Type → TypeShape）转调核心。</para>
/// <para>步骤 0b 可空引用剥离为运行时恒等操作（Nullable 约束 T:struct），由 adapter 语义占位；
/// Error 态运行时不可达（无编译错误类型）。判定结论与生成侧 TypeHelpers.HasMappingPath /
/// TypeRefHelpers.HasMappingPath 三向一致（TypeCompatViewConsistencyTests 三向锁定）。</para>
/// <para>对齐裁决（向符号版权威收敛）：
/// 委托对不再归入嵌套类（Kind=Delegate）；字典门控补齐 IReadOnlyDictionary&lt;,&gt; 实现方。</para>
/// <para>本文件位于 Shared 目录但仅被核心库（NCMapper）编译：Type 模型 adapter
/// （TypeCompatAdapter.cs）依赖 System.Type 反射，不适配 SourceGen 双端编译；
/// 符号/值模型侧由 SymbolCompatAdapter / TypeRefCompatAdapter 实现同一核心。</para>
/// </summary>
internal static class TypeCompatView
{
    /// <summary>
    /// 类型兼容判定：源类型 → 目标类型是否存在映射/转换路径。
    /// <para>步骤 0/1-10 判据链由 TypeCompatibilityCore 单点承载（原门面内私有判据方法已删除，
    /// 镜像逻辑迁入 TypeCompatAdapter 与核心）。</para>
    /// </summary>
    /// <param name="sourceType">源类型</param>
    /// <param name="targetType">目标类型</param>
    /// <returns>存在映射/转换路径时为 true</returns>
    public static bool CanConvert(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type sourceType,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type targetType)
    {
        // ③：判据链收敛至 TypeCompatibilityCore 单点（步骤 0/1-10 +
        // ImplicitConversions 单点表）；步骤 0b 恒等剥离与 Error 不可达语义见 adapter 注释。
        return TypeCompatibilityCore.CanConvert(
            TypeCompatAdapter.Resolve(sourceType),
            TypeCompatAdapter.Resolve(targetType));
    }
}
