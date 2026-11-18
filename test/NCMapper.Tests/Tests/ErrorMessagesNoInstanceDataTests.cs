using System.Reflection;
using NCMapper;
using Xunit;

namespace NCMapper.Tests.Tests;

/// <summary>
/// ErrorMessages 消息零实例数据守护测试（C16 补测）
/// <para>报告 §五 C16 断言形态：消息仅含类型名/属性名，无实例数据（14 方法枚举）。
/// 反射枚举 ErrorMessages 全部消息工厂方法，验证：①方法数 ≥14（报告口径）；
/// ②签名参数均为类型名/属性名/配置语义（string/int），无实例值/对象参数；
/// ③抽样调用后消息确实包含传入的类型名（参数被模板使用而非拼接实例值）。</para>
/// </summary>
public class ErrorMessagesNoInstanceDataTests
{
    /// <summary>
    /// 反射枚举 ErrorMessages 消息工厂 — 数量与签名面守护
    /// <para>实例数据语义参数名（Value/Data/Content/Secret/Instance）出现即视为
    /// 消息模板可能拼接实例值的信号（CWE-209 信息泄露防护）。</para>
    /// </summary>
    [Fact]
    public void ErrorMessages_ReflectiveEnumeration_CountAndSignatureGuard()
    {
        var methods = typeof(ErrorMessages)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.ReturnType == typeof(string) && m.DeclaringType == typeof(ErrorMessages))
            .ToList();

        // 报告口径：14 方法枚举（新增方法需同步刷新本测试与报告）
        Assert.True(methods.Count >= 14, $"期望 ≥14 个消息工厂，实际 {methods.Count}");

        // 签名面：参数仅允许 类型名/属性名/配置 语义（string / int / 失败分类枚举），禁实例数据语义
        // 同步刷新（本文件 docstring 预告的新方法同步义务）：
        // DotPathFailureKind 为失败原因分类（配置语义枚举，7 分支，评审 002 N1），非实例数据。
        var instanceDataParamNames = new[] { "value", "data", "content", "secret", "instance", "rawValue", "json" };
        // 设计逐字签名的特例白名单（方法名, 参数名）：配置语义，非实例数据
        var configParamWhitelist = new[]
        {
            // NamingStrategy 实际取值（int，设计参数名 value）
            ("NamingStrategyOutOfRange", "value"),
        };
        foreach (var method in methods)
        {
            foreach (var p in method.GetParameters())
            {
                // 参数类型：string（名称类）或 int（深度/长度配置）或失败分类枚举或 bool（失败因开关，配置语义）
                // 同步刷新：TreeChildrenPropertyInvalid 的 isReadOnly（名命中只读 vs 非自引用两分支）
                Assert.True(
                    p.ParameterType == typeof(string)
                        || p.ParameterType == typeof(int)
                        || p.ParameterType == typeof(DotPathFailureKind)
                        || p.ParameterType == typeof(bool),
                    $"{method.Name} 参数 {p.Name} 类型 {p.ParameterType.Name} 非名称/配置语义");

                // 参数名：不含实例数据语义（白名单特例除外）
                var whitelisted = configParamWhitelist.Any(t => t.Item1 == method.Name && t.Item2 == p.Name);
                Assert.False(
                    !whitelisted && instanceDataParamNames.Contains(p.Name, StringComparer.OrdinalIgnoreCase),
                    $"{method.Name} 参数 {p.Name} 疑似实例数据语义");
            }
        }
    }

    /// <summary>
    /// 模板面：传入哨兵类型名后消息确实包含之（参数被模板使用，非硬编码拼接）
    /// <para>抽样覆盖 14 方法全部调用：以唯一哨兵值作为类型名/属性名参数，
    /// 断言返回消息包含该哨兵——证明消息由参数构成（仅类型名/属性名），
    /// 同时不含任何硬编码实例值。</para>
    /// </summary>
    [Fact]
    public void ErrorMessages_AllMethods_MessageContainsTypeNameArgument()
    {
        const string sentinel = "SENTINEL_TYPE_9X7";
        var methods = typeof(ErrorMessages)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.ReturnType == typeof(string) && m.DeclaringType == typeof(ErrorMessages))
            .ToList();

        foreach (var method in methods)
        {
            var args = method.GetParameters().Select(p =>
            {
                if (p.ParameterType == typeof(string)) return (object)sentinel;
                if (p.ParameterType == typeof(int)) return (object)1;
                // 失败分类枚举取该方法 switch 覆盖的合法分支
                // （正向方法取正向成员，反向方法取反向成员——避免 default 分支 ArgumentOutOfRangeException）
                if (p.ParameterType == typeof(DotPathFailureKind))
                    return (object)(method.Name == "ReverseWriteBackPathFailed"
                        ? DotPathFailureKind.ReverseWriteBackSegmentNotNavigable
                        : DotPathFailureKind.InvalidIndexSyntax);
                // （任务书 20260901-001）同步刷新：方法族错误消息的方向上下文开关（isReverse，
                // 配置语义——首个 bool 无默认值参数形态，与既有 TreeChildrenPropertyInvalid.isReadOnly 同族）
                if (p.ParameterType == typeof(bool)) return (object)false;
                if (p.HasDefaultValue) return p.DefaultValue ?? "";
                return (object)"";
            }).ToArray();

            var message = (string)method.Invoke(null, args)!;

            // 消息由参数构成：包含哨兵类型名（无参校验消息如 PageNumberMustBePositive 无类型名参数，豁免哨兵断言）
            if (method.GetParameters().Length > 0)
                Assert.Contains(sentinel, message, StringComparison.OrdinalIgnoreCase);
            // 无实例数据硬编码（哨兵之外的固定敏感样本不出现）
            Assert.DoesNotContain("password", message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("token", message, StringComparison.OrdinalIgnoreCase);
        }
    }
}
