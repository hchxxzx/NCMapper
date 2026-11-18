using System.Diagnostics.CodeAnalysis;
using System.Reflection;
namespace NCMapper;
/// <summary>
/// 共享类型工具方法 — 消除 MapTracer/MapValidator/TypeConverter 中的重复定义
/// </summary>
internal static class TypeHelper
{
    /// <summary>
    /// 获取类型的公共实例属性（排除索引器）
    /// <para>接口类型额外合并继承接口的属性（Type.GetProperties 对接口不返回继承链）</para>
    /// </summary>
    /// <param name="type">要获取属性的类型</param>
    /// <returns>类型的公共实例属性列表</returns>
    [RequiresUnreferencedCode("GetPublicProperties enumerates type properties via reflection. Some properties may be trimmed. Use the source-generated path to make this AOT-safe.")]
    public static List<PropertyInfo> GetPublicProperties(Type type)
    {
        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0)
            .ToList();

        // 接口类型：GetProperties 不返回继承接口的属性，需手动补充
        if (type.IsInterface)
        {
            foreach (var iface in type.GetInterfaces())
            {
                props.AddRange(iface.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.GetIndexParameters().Length == 0));
            }
        }

        return props;
    }
}
