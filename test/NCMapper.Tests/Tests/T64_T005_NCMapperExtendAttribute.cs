// A2-07测试承载：与 NCMapper 同形前缀的第三方命名空间特性。
// 独立文件声明（同文件混用 file-scoped + block namespace 触发 CS8955）。
namespace NCMapperExtend;

/// <summary>同形前缀命名空间特性——StartsWith("NCMapper") 误命中 / "NCMapper." 不命中</summary>
[System.AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Class)]
public sealed class ExtendStyleAttribute : System.Attribute
{
}
