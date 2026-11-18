using System.Reflection;
using PublicApiGenerator;
using Xunit;

namespace NCMapper.Tests;

/// <summary>
/// 公共 API 快照测试 — 对所有可打包程序集的公开 API 进行快照守护。
/// 基线文件 PublicApi.{AssemblyName}.approved.txt 与测试代码同目录，作为参考快照参与比较。
/// 如果 API 发生预期变更（新增/删除公开成员），需同步更新对应基线文件。
/// </summary>
/// <remarks>
/// <para><b>纪律（W3D-3，2026-08-08 实证）</b>新增/修改公共 API 必须同步更新
/// 对应 approved.txt 基线并经裁决人确认——门禁反向验证已实证：任意公开成员签名变更
/// （含"同行参数类型变化"）均导致本测试失败，Except() 集合差可捕获；internal 成员不在快照范围，
/// 变更不触发。基线更新须作为 API 变更 commit 的组成部分一并提交。</para>
/// </remarks>
public class PublicApiTests
{
    /// <summary>
    /// 生成指定程序集的公开 API 文本，并过滤掉因 TFM/构建环境不同而变化的程序集级属性。
    /// </summary>
    /// <param name="assembly">目标程序集</param>
    private static string GeneratePublicApi(Assembly assembly)
    {
        var rawApi = assembly.GeneratePublicApi();

        // 过滤因 TFM 不同而变化的程序集级属性（TargetFramework、AssemblyMetadata 等）
        var lines = rawApi.Split('\n');
        var filtered = lines.Where(line =>
            !line.StartsWith("[assembly: System.Runtime.Versioning.TargetFramework") &&
            !line.StartsWith("[assembly: System.Reflection.AssemblyMetadata"));
        return string.Join("\n", filtered).TrimEnd();
    }

    /// <summary>
    /// 生成主库（NCMapper）的公开 API 文本，保留现有断言测试的便捷调用。
    /// </summary>
    private static string GeneratePublicApi() => GeneratePublicApi(typeof(Mapper).Assembly);

    /// <summary>
    /// 验证指定程序集的公开 API 与已批准的基线文件一致。
    /// </summary>
    /// <param name="assembly">目标程序集</param>
    /// <param name="approvedFileName">基线文件名（如 "PublicApi.NCMapper.approved.txt"）</param>
    private static void AssertPublicApiMatches(Assembly assembly, string approvedFileName)
    {
        var baselinePath = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", approvedFileName);

        Assert.True(File.Exists(baselinePath),
            $"Public API 基线文件不存在: {baselinePath}。请从已批准的 API 快照生成基线。");

        var approved = File.ReadAllText(baselinePath)
            .Replace("\r\n", "\n").TrimEnd();

        var current = GeneratePublicApi(assembly)
            .Replace("\r\n", "\n").TrimEnd();

        if (approved != current)
        {
            var approvedLines = approved.Split('\n');
            var currentLines = current.Split('\n');

            var added = currentLines.Except(approvedLines).Take(10);
            var removed = approvedLines.Except(currentLines).Take(10);

            var diff = new System.Text.StringBuilder();
            diff.AppendLine($"公共 API 发生了变更（{approvedFileName}）！如果变更是预期的，请更新基线文件。");
            if (added.Any())
            {
                diff.AppendLine("\n+ 新增的行:");
                foreach (var line in added) diff.AppendLine($"  + {line}");
            }
            if (removed.Any())
            {
                diff.AppendLine("\n- 移除的行:");
                foreach (var line in removed) diff.AppendLine($"  - {line}");
            }
            Assert.Fail(diff.ToString());
        }
    }

    // ─── 主库 NCMapper 快照测试 ──────────────────────────────────────────
    [Fact]
    public void PublicApi_Should_Not_Change_Unintentionally()
    {
        AssertPublicApiMatches(typeof(Mapper).Assembly, "PublicApi.NCMapper.approved.txt");
    }

    /// <summary>
    /// 撞名守卫：本库导出面不存在名为 Convert/ConvertBack 的非方法公共类型
    /// <para> 命名规范③——限定导出面（5 基线程序集）而非全局可见名
    /// （System.Convert 等 BCL 类型按全局扫描断言恒假——复审意见 001 P3-B）。
    /// FluentTypeConfig 同名方法族（.Convert/.ConvertBack）为既有事实且属同一收敛目标，
    /// 特性名走全名串匹配与方法名无冲突，故仅约束"公共类型"面。</para>
    /// </summary>
    [Fact]
    public void PublicApi_Should_Not_Export_Types_Named_Convert_Or_ConvertBack()
    {
        var assemblies = new[]
        {
            typeof(Mapper).Assembly,
            typeof(NCMapper.SourceGen.NCMapperSourceGenerator).Assembly,
            GetAssemblyByName("NCMapper.Dapper"),
            GetAssemblyByName("NCMapper.EFCore"),
            GetAssemblyByName("NCMapper.FreeSql"),
        };

        foreach (var asm in assemblies)
        {
            if (asm == null) continue;
            var exported = asm.GetExportedTypes();
            Assert.DoesNotContain(exported, t => t.Name is "Convert" or "ConvertBack");
        }
    }

    private static Assembly? GetAssemblyByName(string name)
        => AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name)
           ?? Assembly.Load(new AssemblyName(name));

    [Fact]
    public void PublicApi_Should_Contain_Core_Types()
    {
        var api = GeneratePublicApi();

        // 核心接口和类
        Assert.Contains("public class Mapper", api);
        Assert.Contains("public interface IMapper", api);
        // IValueConverter 接口形态已退役（删除），不得回归公开 API
        Assert.DoesNotContain("public interface IValueConverter", api);

        // Attributes
        Assert.Contains("public class MapFromAttribute", api);
        Assert.Contains("public class MapPropertyAttribute", api);
        Assert.Contains("public class MapIgnoreAttribute", api);
        Assert.Contains("public class ConvertAttribute", api);

        // 枚举
        Assert.Contains("public enum Direction", api);
        Assert.Contains("public enum MapValidationLevel", api);

        // 异常
        Assert.Contains("public class MapException", api);
        Assert.Contains("public class MapConfigurationException", api);

        // 深拷贝
        Assert.Contains("public abstract class DeepCopyProviderBase", api);
        Assert.Contains("public sealed class FastDeepCopyProvider", api);
        Assert.Contains("public sealed class ExpressionTreeDeepCopyProvider", api);

        // 管道/诊断
        Assert.Contains("public sealed class MapPipeline", api);
        Assert.Contains("public sealed class MapValidationResult", api);
    }

    [Fact]
    public void PublicApi_Should_Not_Expose_Internal_Types()
    {
        var api = GeneratePublicApi();

        // 内部类型不应出现在公开 API 中
        Assert.DoesNotContain("class MapperBase", api);
        Assert.DoesNotContain("class ExpressionBuilder", api);
        Assert.DoesNotContain("class PropertyMatcher", api);
        Assert.DoesNotContain("class CollectionCopier", api);
    }

    [Fact]
    public void PublicApi_Should_Contain_GA_Entry_Points()
    {
        var api = GeneratePublicApi();

        // Mapper 核心方法必须存在
        Assert.Contains("Map<TSource, TTarget>", api);
        Assert.Contains("MapList<TSource, TTarget>", api);
        Assert.Contains("DeepCopy<T>", api);
        Assert.Contains("ClearCache()", api);

        // DI 扩展
        Assert.Contains("AddNCMapper", api);

        // 验证
        Assert.Contains("ValidateOrThrow", api);

        // 源生成器注册点
        Assert.Contains("RegisterGenerated", api);
    }

    // ─── NCMapper.FreeSql 快照测试 ──────────────────────────────────────

    [Fact]
    public void PublicApi_FreeSql_Should_Not_Change_Unintentionally()
    {
        AssertPublicApiMatches(
            typeof(NCMapper.FreeSql.NCMapperFreeSqlExtensions).Assembly,
            "PublicApi.NCMapper.FreeSql.approved.txt");
    }

    // ─── NCMapper.EFCore 快照测试 ────────────────────────────────────────

    [Fact]
    public void PublicApi_EFCore_Should_Not_Change_Unintentionally()
    {
        AssertPublicApiMatches(
            typeof(NCMapper.EFCore.NCMapperEFCoreExtensions).Assembly,
            "PublicApi.NCMapper.EFCore.approved.txt");
    }

    // ─── NCMapper.Dapper 快照测试 ────────────────────────────────────────

    [Fact]
    public void PublicApi_Dapper_Should_Not_Change_Unintentionally()
    {
        AssertPublicApiMatches(
            typeof(NCMapper.Dapper.NCMapperDapperExtensions).Assembly,
            "PublicApi.NCMapper.Dapper.approved.txt");
    }

    // ─── NCMapper.SourceGen 快照测试 ──────────────────────────────────────

    [Fact]
    public void PublicApi_SourceGen_Should_Not_Change_Unintentionally()
    {
        AssertPublicApiMatches(
            typeof(NCMapper.SourceGen.NCMapperSourceGenerator).Assembly,
            "PublicApi.NCMapper.SourceGen.approved.txt");
    }
}
