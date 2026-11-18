using System.Reflection;
using System.Runtime.Serialization;
using NCMapper;
using Xunit;

namespace NCMapper.Tests.Tests;

/// <summary>
/// MapException ISerializable 弃用守护测试（C15 适配：）
/// <para>C15 原断言形态（GetObjectData 往返保真）随 移除 ISerializable 而失效；
/// 按范围守卫 2 + B12 适配为"弃用落地守护"（登记）：不再实现 ISerializable、
/// 序列化构造器不存在、公共构造函数签名全部保留、catch InvalidOperationException 兼容保持。
/// 断言意图（A-003-F2 弃用处置回归守护）不变，仅被守护的行为从"存在并保真"变为"已移除"。</para>
/// </summary>
public class MapExceptionSerializationTests
{
    /// <summary>
    /// 弃用落地 — 无自定义序列化实现，序列化构造器已移除
    /// <para>处置验证：SYSLIB0051 弃用模式（GetObjectData 覆写 + 序列化构造器）已移除。
    /// 注：MapException 继承链（InvalidOperationException ← Exception）仍实现 ISerializable（.NET 8 基类行为），
    /// 故验证点是"无自定义覆写/构造器"而非"不实现接口"。</para>
    /// </summary>
    [Fact]
    public void MapException_IsSerializable_Removed_DeprecationApplied()
    {
        // GetObjectData 覆写已移除（DeclaringType 应为基类 Exception，而非 MapException）
        var getObjectData = typeof(MapException).GetMethod(
            "GetObjectData",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            new[] { typeof(SerializationInfo), typeof(StreamingContext) },
            modifiers: null);
        Assert.NotNull(getObjectData);
        Assert.Equal(typeof(Exception), getObjectData!.DeclaringType);

        // 序列化构造器（protected，SerializationInfo/StreamingContext）已移除
        var ctor = typeof(MapException).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            new[] { typeof(SerializationInfo), typeof(StreamingContext) },
            modifiers: null);
        Assert.Null(ctor);
    }

    /// <summary>
    /// 公共构造函数签名保留 + catch InvalidOperationException 兼容保持
    /// <para>移除 ISerializable 不得破坏公共 API 面：完整 5 参构造与 3 参构造可用，
    /// 字段初始化正常，既有 catch (InvalidOperationException) 捕获兼容保持。</para>
    /// </summary>
    [Fact]
    public void MapException_PublicCtors_Retained_AndCatchCompatibility()
    {
        // 完整 5 参构造：字段初始化正常
        var ex = new MapException("msg", typeof(string), typeof(int), "Name", new Exception("inner"));
        Assert.Equal(typeof(string), ex.SourceType);
        Assert.Equal(typeof(int), ex.TargetType);
        Assert.Equal("Name", ex.PropertyName);
        Assert.Equal("msg", ex.Message);

        // catch InvalidOperationException 兼容保持（继承链未破坏）
        try
        {
            throw ex;
        }
        catch (InvalidOperationException caught)
        {
            Assert.Same(ex, caught);
        }

        // MapConfigurationException 公共构造保留（3 参）
        var cfg = new MapConfigurationException("cfg", typeof(string), typeof(int));
        Assert.Equal(typeof(string), cfg.SourceType);
        Assert.Equal(typeof(int), cfg.TargetType);
    }
}
