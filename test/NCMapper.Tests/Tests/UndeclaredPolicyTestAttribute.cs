// 安全网测试用：NCMapper 命名空间内、未实现 IProjectionPolicy 的特性——
// 降级检测器对此类未声明特性应默认触发降级（扫描臂现居 DegradationDetectorBase，
// 原 ProjectionStrategy 属性级扫描分支已退役迁移）。
// 独立成文件是因为消费测试文件使用 file-scoped namespace，
// 无法在同一文件内混用块级 namespace（CS8956）；原消费用例已随旧套件退役，
// 本特性作为安全网资产保留（全仓当前无消费点）。

namespace NCMapper
{
    [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Property)]
    public sealed class UndeclaredPolicyTestAttribute : System.Attribute { }
}
