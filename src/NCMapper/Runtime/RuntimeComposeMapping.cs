// Compose 跨表映射运行时承载类型
// ComposeMappingSnapshot（Models.Snapshots.cs）的运行时消费子集——
// 双通道统一发射（特性侧 [MapProperty(FromSource)] + Fluent FromSource 同载），
// 承载 可译性判定与 分类判定的全部输入（路径/索引/Converter 三元组/Mask/
// IsFromJson/FormatString/ConditionMethodRef—— 一次性补齐，避免二次动发射面/基线二次再生）。
using System;

namespace NCMapper.Runtime;

/// <summary>
/// Compose 跨表映射的运行时形态 —— ComposeMappingSnapshot 的运行时消费子集（双通道统一发射）。
/// </summary>
public sealed class RuntimeComposeMapping
{
    /// <summary>目标属性名（消费端按此索引；SourcePathNames 空 = 空组=忽略信号，见文末说明）</summary>
    public string TargetPropertyName { get; init; } = "";

    /// <summary>副源类型引用（public 可达时非 null；否则以 SubSourceTypeName 按名解析）</summary>
    public Type? SubSourceTypeRef { get; init; }

    /// <summary>副源类型显示名（兜底比对依据）</summary>
    public string SubSourceTypeName { get; init; } = "";

    /// <summary>副源字段访问路径段（多段点路径；单段为单元素数组）——消费端经索引重建后交点路径解析</summary>
    public string[] SourcePathNames { get; init; } = Array.Empty<string>();

    /// <summary>路径段索引元数据（与 SourcePathNames 等长、-1 占位；无索引段时 null）</summary>
    public int[]? SourceIndexes { get; init; }

    /// <summary>路径是否含索引段</summary>
    public bool HasIndex { get; init; }

    /// <summary>转换器承载（[Convert] / Fluent 块内 Convert）——降级判定与 Trace 消费</summary>
    public bool HasConverter { get; init; }

    /// <summary>转换器锚定类型全名（null=未声明）</summary>
    public string? ConverterDeclaringType { get; init; }

    /// <summary>转换器静态方法名（null=未声明）</summary>
    public string? ConverterMethodName { get; init; }

    /// <summary>脱敏承载（对齐 ComposeMappingSnapshot.MaskType， 镜像）</summary>
    public bool HasMask { get; init; }

    /// <summary>脱敏类型显示名（null=未声明）</summary>
    public string? MaskTypeName { get; init; }

    /// <summary>JSON 反序列化形态（镜像）</summary>
    public bool IsFromJson { get; init; }

    /// <summary>格式化承载（Compose 侧 FormatString 同载—— 教训）</summary>
    public string? FormatString { get; init; }

    /// <summary>条件守卫方法引用（块内 [MapWhen]；null=无条件）</summary>
    public string? ConditionMethodRef { get; init; }

    // 空组语义（处置 + 实读定稿， 审计）：
    // Block-Ignore 声明在物化期"全 Ignore 组不产 cm → 不入候选集"（MappingSnapshotBuilder :339-358/:462-467），
    // 产物中不存在 SourcePathNames 为空的行；消费端遇空组（SourcePathNames.Length == 0）
    // 跳过该目标属性绑定（防御分支保留，与 slot 差集同机制）。
}
