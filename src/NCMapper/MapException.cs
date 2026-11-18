using System;

namespace NCMapper;

/// <summary>
/// 映射操作异常 — 对象映射过程中发生的错误
/// <para>继承自 <see cref="InvalidOperationException"/> 以保持向后兼容
/// （用户已有的 <c>catch (InvalidOperationException)</c> 仍能捕获此异常）</para>
/// <para>W3已移除 ISerializable 实现（GetObjectData/序列化构造器，
/// .NET 8+ 弃用模式 SYSLIB0051），公共构造函数签名全部保留；异常序列化在 .NET 8+ 不再支持自定义字段往返</para>
/// </summary>
public class MapException : InvalidOperationException
{
    /// <summary>
    /// 源类型（映射的源对象类型）
    /// </summary>
    public Type? SourceType { get; }

    /// <summary>
    /// 目标类型（映射的目标对象类型）
    /// </summary>
    public Type? TargetType { get; }

    /// <summary>
    /// 属性名（触发异常的属性名称，可选）
    /// <para>用于精确定位映射失败的位置，便于调试</para>
    /// </summary>
    public string? PropertyName { get; }

    /// <summary>
    /// 创建映射异常实例
    /// </summary>
    public MapException() : base() { }

    /// <summary>
    /// 创建带错误消息的映射异常实例
    /// </summary>
    /// <param name="message">错误消息</param>
    public MapException(string message) : base(message) { }

    /// <summary>
    /// 创建带错误消息和内部异常的映射异常实例
    /// </summary>
    /// <param name="message">错误消息</param>
    /// <param name="innerException">内部异常</param>
    public MapException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>
    /// 创建带源/目标类型和错误消息的映射异常实例
    /// </summary>
    /// <param name="message">错误消息</param>
    /// <param name="sourceType">源类型</param>
    /// <param name="targetType">目标类型</param>
    public MapException(string message, Type? sourceType, Type? targetType) : base(message)
    {
        SourceType = sourceType;
        TargetType = targetType;
    }

    /// <summary>
    /// 创建带源/目标类型、错误消息和内部异常的映射异常实例
    /// </summary>
    /// <param name="message">错误消息</param>
    /// <param name="sourceType">源类型</param>
    /// <param name="targetType">目标类型</param>
    /// <param name="innerException">内部异常</param>
    public MapException(string message, Type? sourceType, Type? targetType, Exception innerException)
        : base(message, innerException)
    {
        SourceType = sourceType;
        TargetType = targetType;
    }

    /// <summary>
    /// 创建带源/目标类型、属性名和错误消息的映射异常实例
    /// </summary>
    /// <param name="message">错误消息</param>
    /// <param name="sourceType">源类型</param>
    /// <param name="targetType">目标类型</param>
    /// <param name="propertyName">触发异常的属性名</param>
    public MapException(string message, Type? sourceType, Type? targetType, string? propertyName) : base(message)
    {
        SourceType = sourceType;
        TargetType = targetType;
        PropertyName = propertyName;
    }

    /// <summary>
    /// 创建带源/目标类型、属性名、错误消息和内部异常的映射异常实例（五参数完整构造器）
    /// </summary>
    /// <param name="message">错误消息</param>
    /// <param name="sourceType">源类型</param>
    /// <param name="targetType">目标类型</param>
    /// <param name="propertyName">触发异常的属性名</param>
    /// <param name="innerException">内部异常</param>
    public MapException(string message, Type? sourceType, Type? targetType, string? propertyName, Exception innerException)
        : base(message, innerException)
    {
        SourceType = sourceType;
        TargetType = targetType;
        PropertyName = propertyName;
    }
}

/// <summary>
/// 映射配置异常 — [MapFrom]/[MapProperty] 特性配置错误
/// <para>如：DTO 未标注 [MapFrom]、Compose 找不到主源类型等</para>
/// <para>W3已移除 [Serializable] 与序列化构造器（同 MapException）</para>
/// </summary>
public class MapConfigurationException : MapException
{
    /// <summary>
    /// 创建映射配置异常实例
    /// </summary>
    public MapConfigurationException() : base() { }

    /// <summary>
    /// 创建带错误消息的映射配置异常实例
    /// </summary>
    /// <param name="message">错误消息</param>
    public MapConfigurationException(string message) : base(message) { }

    /// <summary>
    /// 创建带错误消息和内部异常的映射配置异常实例
    /// </summary>
    /// <param name="message">错误消息</param>
    /// <param name="innerException">内部异常</param>
    public MapConfigurationException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>
    /// 创建带源/目标类型和错误消息的映射配置异常实例
    /// </summary>
    /// <param name="message">错误消息</param>
    /// <param name="sourceType">源类型</param>
    /// <param name="targetType">目标类型</param>
    public MapConfigurationException(string message, Type? sourceType, Type? targetType) : base(message, sourceType, targetType) { }
}
