// 快照运行时元数据承载类型
// 声明通道枚举——D3 双通道等价消费用。
using System;

namespace NCMapper.Runtime;

/// <summary>
/// 映射声明通道 —— 标记一条运行时元数据来自哪个声明通道（双驱动仲裁产物归属）
/// </summary>
public enum RuntimeMappingChannel
{
    /// <summary>
    /// [MapFrom(typeof(S))] 特性通道 —— DTO 类标注 <see cref="Attributes.MapFromAttribute"/> 声明
    /// </summary>
    MapFrom = 0,

    /// <summary>
    /// [MapperConfig] + Fluent 终端方法通道 —— Mapper 配置类内 Fluent 声明物化
    /// </summary>
    Fluent = 1,
}
