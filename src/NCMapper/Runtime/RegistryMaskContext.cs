using System;
using System.Collections.Generic;
using NCMapper;

namespace NCMapper.Runtime;

/// <summary>
/// 注册表 Mask 明细的按对只读上下文 —— 决策链注册表腿的取数载体（每类型对解析一次）。
/// <para>构建 = <see cref="RuntimeSnapshotStore.TryGetMappingDetail"/> 按对一次查询；
/// miss 或无可消费声明 → null（调用方以 null 为"无注册声明"恒等态）。
/// 属性匹配 = 明细 Target.Name 精确键（StringComparison.Ordinal，先例 MapTracer 消费同口径）。</para>
/// <para>扫描面 = 属性明细（pm 级）+ Compose 跨表明细（cm 级，仅 Mask 位）：cm 行按
/// TargetPropertyName 索引、pm 优先 cm 补位——Compose-only 对（Properties 空但 ComposeMappings
/// 带声明）不再被跳过，降级后内存映射腿经本上下文消费 cm 级脱敏声明（审计 H-3 修复：
/// 此前 Compose-only 对整体返回 null，PII 明文）。cm 行其余承载位（Converter/Format/Json/
/// When/Index）属跨表取值语义，不在本上下文承载。</para>
/// <para>构建期 Trace（唯一发射位——每对一次；决策/渲染层无从判定声明信息，钉在下游必致
/// 重复发射或漏发）：① 带 HasMask 声明但 MaskTypeName 为 null（两字段独立 init，手工注册可构造，
/// pm/cm 同款）；② 带 Mask 声明但目标面无同名属性（手工注册打错属性名 = 静默丢脱敏形态）。
/// 两类均 Trace 警告不抛异常（不对手写注册引入新异常面，亦不静默跳过）。</para>
/// </summary>
internal sealed class RegistryMaskContext
{
    private readonly Dictionary<string, string> _maskNamesByTarget;

    private RegistryMaskContext(Type sourceType, Type targetType, Dictionary<string, string> maskNamesByTarget)
    {
        SourceType = sourceType;
        TargetType = targetType;
        _maskNamesByTarget = maskNamesByTarget;
    }

    /// <summary>对级源类型（Plan 缓存键对级分量来源）</summary>
    public Type SourceType { get; }

    /// <summary>对级目标类型</summary>
    public Type TargetType { get; }

    /// <summary>目标属性是否声明注册表 Mask（精确键）</summary>
    public bool HasRegistryMask(string targetPropertyName) => _maskNamesByTarget.ContainsKey(targetPropertyName);

    /// <summary>取目标属性的脱敏类型显示名（须先 HasRegistryMask）</summary>
    public string MaskTypeNameOf(string targetPropertyName) => _maskNamesByTarget[targetPropertyName];

    /// <summary>按对解析注册表 Mask 明细；无可消费声明返回 null（恒等态）。每类型对调用一次。</summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming", "IL2070",
        Justification = "GetProperty 为孤儿声明探测的 'this' 实参（目标面同名属性存在性 best-effort）：类型被 trim 时探测不可见（孤儿形态，即 Trace 警告面），声明仍入上下文不阻断；四段解析面 RUC 声明已覆盖调用链。")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming", "IL2075",
        Justification = "GetProperty 为孤儿声明探测（孤儿即 Trace 警告、声明仍入上下文不阻断）；类型被 trim 时探测不可见，四段解析面 RUC 声明已覆盖调用链。")]
    internal static RegistryMaskContext? Resolve(Type sourceType, Type targetType)
    {
        if (!RuntimeSnapshotStore.TryGetMappingDetail(sourceType, targetType, out var meta)
            || ((meta.Properties is not { Length: > 0 }) && (meta.ComposeMappings is not { Length: > 0 })))
            return null;

        var maskNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in meta.Properties)
        {
            if (!p.HasMask)
                continue;
            var targetName = p.Target.Name;
            if (string.IsNullOrEmpty(p.MaskTypeName))
            {
                MapTracer.Write($"[NCMapper] RegistryMaskContext: detail declares HasMask without MaskTypeName for '{targetName}' ({sourceType.Name} -> {targetType.Name}); traced warning, mapped as usual");
                continue;
            }
            if (targetType.GetProperty(targetName) == null)
            {
                MapTracer.Write($"[NCMapper] RegistryMaskContext: detail declares mask for '{targetName}' ({sourceType.Name} -> {targetType.Name}) but the target face has no property with that name; traced warning, the detail will never be consumed");
            }
            maskNames[targetName] = p.MaskTypeName;
        }

        // Compose 跨表明细（cm 级）掩码补位——与属性明细（pm 级）同一消费面（决策链 Plan 与
        // 渲染叶节点均按目标属性名索引本上下文），补齐降级后内存映射腿对 cm 级脱敏声明的
        // 消费缺口（审计 H-3：FromSource+Mask 形态降级后 PII 明文）。承载面仅 HasMask/MaskTypeName
        //（cm 行其余承载位属跨表取值语义，维持现状）；pm 优先 cm 补位（同目标属性并存不覆盖，
        // 特性腿优先的分派前置不受影响——同一属性不双重施加防线保持）。
        if (meta.ComposeMappings is { Length: > 0 })
        {
            foreach (var c in meta.ComposeMappings)
            {
                if (!c.HasMask)
                    continue;
                if (string.IsNullOrEmpty(c.MaskTypeName))
                {
                    MapTracer.Write($"[NCMapper] RegistryMaskContext: compose detail declares HasMask without MaskTypeName for '{c.TargetPropertyName}' ({sourceType.Name} -> {targetType.Name}); traced warning, mapped as usual");
                    continue;
                }
                if (targetType.GetProperty(c.TargetPropertyName) == null)
                {
                    MapTracer.Write($"[NCMapper] RegistryMaskContext: compose detail declares mask for '{c.TargetPropertyName}' ({sourceType.Name} -> {targetType.Name}) but the target face has no property with that name; traced warning, the detail will never be consumed");
                }
                if (!maskNames.ContainsKey(c.TargetPropertyName))
                    maskNames[c.TargetPropertyName] = c.MaskTypeName;
            }
        }

        return maskNames.Count > 0 ? new RegistryMaskContext(sourceType, targetType, maskNames) : null;
    }
}
