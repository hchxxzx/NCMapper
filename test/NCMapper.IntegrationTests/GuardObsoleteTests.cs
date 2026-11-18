using System.Reflection;
using Xunit;

namespace NCMapper.IntegrationTests;

/// <summary>
/// T4-2: Guard 编译失败验证 — 验证 FreeSql GroupBy guard 的 [Obsolete(error:true)] 属性存在
/// <para>完整 Roslyn 编译断言（CS0619）需要 FreeSql 编译环境，此处用反射验证 guard 方法标注正确</para>
/// </summary>
public class GuardObsoleteTests
{
    [Fact]
    public void FreeSqlGroupByGuard_HasObsoleteError()
    {
        // 加载 NCMapper.FreeSql 程序集
        var freeSqlAssembly = typeof(NCMapper.FreeSql.NCMapperFreeSqlExtensions).Assembly;
        var guardType = freeSqlAssembly.GetType("NCMapper.FreeSql.NCMapperFreeSqlGuardExtensions");
        Assert.NotNull(guardType);

        // 查找所有公共方法，验证至少有一个带 [Obsolete(error:true)]
        var methods = guardType!.GetMethods(BindingFlags.Public | BindingFlags.Static);
        var obsoleteMethods = methods
            .Where(m => m.GetCustomAttribute<ObsoleteAttribute>() != null)
            .ToList();

        Assert.True(obsoleteMethods.Count > 0, "Guard 类型应至少有一个 [Obsolete] 方法");

        // 验证所有 [Obsolete] 方法的 error 参数为 true（编译时错误而非警告）
        foreach (var method in obsoleteMethods)
        {
            var attr = method.GetCustomAttribute<ObsoleteAttribute>()!;
#pragma warning disable CS0618 // Obsolete
            Assert.True(attr.IsError, $"Guard 方法 {method.Name} 的 [Obsolete] 应设置 error:true");
#pragma warning restore CS0618
        }
    }

    [Fact]
    public void EFCoreGroupByGuard_HasObsoleteError()
    {
        // 加载 NCMapper.EFCore 程序集
        var efCoreAssembly = typeof(NCMapper.EFCore.NCMapperEFCoreExtensions).Assembly;
        var guardType = efCoreAssembly.GetType("NCMapper.EFCore.NCMapperEFCoreGuardExtensions");
        Assert.NotNull(guardType);

        var methods = guardType!.GetMethods(BindingFlags.Public | BindingFlags.Static);
        var obsoleteMethods = methods
            .Where(m => m.GetCustomAttribute<ObsoleteAttribute>() != null)
            .ToList();

        Assert.True(obsoleteMethods.Count > 0, "EFCore Guard 类型应至少有一个 [Obsolete] 方法");

        foreach (var method in obsoleteMethods)
        {
            var attr = method.GetCustomAttribute<ObsoleteAttribute>()!;
#pragma warning disable CS0618
            Assert.True(attr.IsError, $"Guard 方法 {method.Name} 的 [Obsolete] 应设置 error:true");
#pragma warning restore CS0618
        }
    }
}
