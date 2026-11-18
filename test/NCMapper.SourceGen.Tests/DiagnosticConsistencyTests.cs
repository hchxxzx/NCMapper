using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NCMapper.SourceGen;
using Xunit;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// 诊断四向一致性构建期测试（B4-01 同族复发防护）——
/// 描述符全集 ↔ 反查表 ↔ 资源键 ↔ 发射实参数 四向机械核对：
/// <list type="bullet">
/// <item>向1 描述符全集——反射两个容器（DiagnosticDescriptors + DiagnosticDescriptorsFluent，
/// 首评 P1-6：只反射单类会假红）全部 static DiagnosticDescriptor 字段；</item>
/// <item>向2 反查表——正向 <see cref="DiagnosticDescriptors.FromId"/> 对全集逐一非 null 且同实例；
/// 反向反射 ByIdLazy（private，首评 P3-3 可执行路径）键集与全集对等；</item>
/// <item>向3 资源键——每描述符 {Id}_Title/_Message/_Description 键在 DiagnosticResources.resx
/// 实存（LocalizableResourceString nameof 强类型引用与 resx data name 同名耦合）；
/// zh-CN.resx 不入本批四向域（002-P3-5——双语覆盖由既有双语测试体系承担）；</item>
/// <item>向4 实参数——扫描 src 全域 DiagnosticSnapshot 初始化器站点（DescriptorId = 表达式 与
/// 同初始化器 Args = new[]{...} 配对，首评 P1-1 修正形态——非 Diagnostic.Create 调用面），
/// 符号名解析（*.XxxRule.Id → 两容器字段映射）得描述符，MessageFormat 占位符数与实参数逐一比对。</item>
/// </list>
/// <para>计数口径（任务书验收 #3）：Id 数 = 双容器反射实测（批次三 F55-F58 落地后 93；89 为编制
/// 时点口径——登记）、DescriptorId 站点数逐测试输出登记。变参场景豁免登记见
/// <see cref="SiteExemptions"/>（空起步，新增豁免须附理由）。</para>
/// </summary>
public class DiagnosticConsistencyTests
{
    // ------------------------------------------------------------------
    // 变参/形态豁免登记（空起步）：key = "文件名:RuleName"，value = 豁免理由
    // ------------------------------------------------------------------
    private static readonly IReadOnlyDictionary<string, string> SiteExemptions = new Dictionary<string, string>
    {
        // （执行期登记——出现"实参数与占位符数恒不等"的合法变参站点时，附理由入册）
    };

    // ------------------------------------------------------------------
    // 仓库根定位（SnapshotConservationTests.ReadSourceGenSource 同模式）
    // ------------------------------------------------------------------
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "NCMapper.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("无法定位仓库根（未找到 NCMapper.slnx）");
    }

    // ------------------------------------------------------------------
    // 向1：双容器反射全集
    // ------------------------------------------------------------------
    private static List<DiagnosticDescriptor> AllDescriptors()
    {
        var result = new List<DiagnosticDescriptor>();
        foreach (var container in new[] { typeof(DiagnosticDescriptors), typeof(DiagnosticDescriptorsFluent) })
        foreach (var field in container.GetFields(BindingFlags.Public | BindingFlags.Static)
                     .Where(f => f.FieldType == typeof(DiagnosticDescriptor)))
        {
            result.Add((DiagnosticDescriptor)field.GetValue(null)!);
        }
        return result;
    }

    /// <summary>字段名 → 描述符映射（向4 符号名解析的查找源）。</summary>
    private static IReadOnlyDictionary<string, DiagnosticDescriptor> DescriptorFieldMap()
    {
        var map = new Dictionary<string, DiagnosticDescriptor>(StringComparer.Ordinal);
        foreach (var container in new[] { typeof(DiagnosticDescriptors), typeof(DiagnosticDescriptorsFluent) })
        foreach (var field in container.GetFields(BindingFlags.Public | BindingFlags.Static)
                     .Where(f => f.FieldType == typeof(DiagnosticDescriptor)))
        {
            map[field.Name] = (DiagnosticDescriptor)field.GetValue(null)!;
        }
        return map;
    }

    /// <summary>向4 占位符计数：{0}..{n} 连续性校验（断开序号 = resx 模板缺陷）。</summary>
    private static int CountPlaceholders(string format, string ruleName)
    {
        var indices = Regex.Matches(format, @"\{(\d+)\}")
            .Select(m => int.Parse(m.Groups[1].Value))
            .ToHashSet();
        if (indices.Count == 0) return 0;
        for (var i = 0; i < indices.Max(); i++)
        {
            Assert.True(indices.Contains(i),
                $"{ruleName}: MessageFormat 占位符序号断开（{{0}}..{{n}} 必须连续，缺 {{{i}}}）——resx 模板缺陷");
        }
        return indices.Count;
    }

    /// <summary>从 DescriptorId 表达式提取规则字段名：*.XxxRule.Id → XxxRule（形态漂移即红）。</summary>
    private static string? ExtractRuleName(ExpressionSyntax descriptorIdExpr)
    {
        if (descriptorIdExpr is not MemberAccessExpressionSyntax idAccess || idAccess.Name.Identifier.Text != "Id")
            return null;
        return idAccess.Expression switch
        {
            MemberAccessExpressionSyntax ruleAccess => ruleAccess.Name.Identifier.Text,
            IdentifierNameSyntax name => name.Identifier.Text,
            _ => null,
        };
    }

    /// <summary>收集 src/NCMapper.SourceGen 全部 .cs 文件（相对仓库根）。</summary>
    private static IEnumerable<(string RelativePath, string Text)> SourceGenSources(string repoRoot)
    {
        var srcDir = Path.Combine(repoRoot, "src", "NCMapper.SourceGen");
        foreach (var file in Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories))
        {
            yield return (Path.GetRelativePath(repoRoot, file), File.ReadAllText(file));
        }
    }

    /// <summary>DiagnosticSnapshot 初始化器站点（DescriptorId + Args 配对）解析。</summary>
    private sealed record EmissionSite(string File, int Line, string RuleName, DiagnosticDescriptor Descriptor, int ArgCount);

    /// <summary>
    /// 透传形态判定：DescriptorId 取自另一 DiagnosticSnapshot/Diagnostic 对象（如
    /// DualDriverArbiter hookConflict.DescriptorId 转抄、Materialize diag.Descriptor?.Id ?? ""）——
    /// 非字面发射点，Args 同为转抄，占位符对账不可行（合法形态，登记计数不红）。
    /// </summary>
    private static bool IsTransitShape(ExpressionSyntax expr)
    {
        var text = expr.ToString();
        return text.EndsWith(".DescriptorId", StringComparison.Ordinal)
               || text.Contains("Descriptor?.Id", StringComparison.Ordinal);
    }

    private static List<EmissionSite> CollectEmissionSites(
        IReadOnlyDictionary<string, DiagnosticDescriptor> fieldMap, out List<string> unresolved)
    {
        var repoRoot = FindRepoRoot();
        var sites = new List<EmissionSite>();
        unresolved = new List<string>();
        foreach (var (relativePath, text) in SourceGenSources(repoRoot))
        {
            var tree = CSharpSyntaxTree.ParseText(text);
            var root = tree.GetRoot();
            foreach (var creation in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
            {
                if (creation.Type.ToString() != "DiagnosticSnapshot") continue;
                var init = creation.Initializer;
                if (init == null) continue;

                ExpressionSyntax? descriptorIdExpr = null;
                ExpressionSyntax? argsExpr = null;
                foreach (var assignment in init.Expressions.OfType<AssignmentExpressionSyntax>())
                {
                    var name = assignment.Left.ToString();
                    if (name == "DescriptorId") descriptorIdExpr = assignment.Right;
                    else if (name == "Args") argsExpr = assignment.Right;
                }
                if (descriptorIdExpr == null) continue; // 非 DescriptorId 形态站点（构造重载等）不入对账面

                var ruleName = ExtractRuleName(descriptorIdExpr);
                if (ruleName == null || !fieldMap.TryGetValue(ruleName, out var descriptor))
                {
                    if (IsTransitShape(descriptorIdExpr))
                    {
                        // 透传形态：转抄站点豁免占位符对账（形态说明见 IsTransitShape 文档）
                        continue;
                    }
                    unresolved.Add($"{relativePath}:{creation.GetLocation().GetLineSpan().StartLinePosition.Line + 1} {descriptorIdExpr}");
                    continue;
                }

                var argCount = argsExpr switch
                {
                    ImplicitArrayCreationExpressionSyntax implicitArr => implicitArr.Initializer.Expressions.Count,
                    ArrayCreationExpressionSyntax { Initializer: not null } array => array.Initializer.Expressions.Count,
                    LiteralExpressionSyntax lit when lit.IsKind(SyntaxKind.NullLiteralExpression) => 0,
                    null => 0,
                    _ => -1, // 未识别实参形态（条件数组/局部变量/集合表达式——登记面）
                };

                sites.Add(new EmissionSite(
                    relativePath,
                    creation.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                    ruleName, descriptor, argCount));
            }
        }
        return sites;
    }

    // ------------------------------------------------------------------
    // 向1 + 向2：全集 ↔ 反查表（双向）
    // ------------------------------------------------------------------

    [Fact]
    public void DescriptorRegistry_FullSet_BidirectionalLookup()
    {
        var all = AllDescriptors();

        // 向1 前提：全集中 Id 唯一（重复 = 登记冲突）
        var duplicate = all.GroupBy(d => d.Id, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        Assert.True(duplicate == null, $"诊断 Id 重复登记: {string.Join(", ", duplicate?.Select(d => d.Id) ?? Array.Empty<string>())}");

        // 全集规模下限护栏（编制时点 89 + 批次三 F55-F58 = 93；低于下限 = 容器反射面坍缩）
        Assert.True(all.Count >= 93, $"描述符全集 {all.Count} 项低于 93 下限——双容器反射面坍缩（F 系漏反射）");
        var fluentCount = typeof(DiagnosticDescriptorsFluent)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Count(f => f.FieldType == typeof(DiagnosticDescriptor));
        Assert.True(fluentCount >= 53, $"Fluent 容器 {fluentCount} 项低于 53 下限（批次三 F55-F58 后口径）");

        // 向2 正向：FromId 对全集逐一非 null 且同实例
        foreach (var descriptor in all)
        {
            var lookup = DiagnosticDescriptors.FromId(descriptor.Id);
            Assert.True(lookup != null, $"{descriptor.Id}: FromId 查空——反查表漏登记（B4-01 缺陷形态，回落 NCMAP099）");
            Assert.Same(descriptor, lookup);
        }

        // 向2 反向：反射 ByIdLazy（private）键集 ⊆ 全集 且计数相等
        var lazyField = typeof(DiagnosticDescriptors).GetField("ByIdLazy", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ByIdLazy field missing——反查表形态漂移须同步测试");
        var lazyValue = lazyField.GetValue(null)!;
        var byId = lazyValue.GetType().GetProperty("Value")!.GetValue(lazyValue)!;
        var keysProp = byId.GetType().GetProperty("Keys")!.GetValue(byId)!;
        var keys = ((IEnumerable<string>)keysProp).ToHashSet(StringComparer.Ordinal);
        var extra = keys.Where(k => all.All(d => d.Id != k)).ToList();
        Assert.True(extra.Count == 0, $"反查表含全集外的键（幽灵登记）: {string.Join(", ", extra)}");
        Assert.Equal(all.Count, keys.Count);
    }

    // ------------------------------------------------------------------
    // 向3：资源键实存（{Id}_Title/_Message/_Description × 全集；zh-CN 不入域——002-P3-5）
    // ------------------------------------------------------------------

    [Fact]
    public void DescriptorResources_ResxKeys_ExistForAllDescriptors()
    {
        var repoRoot = FindRepoRoot();
        var resxPath = Path.Combine(repoRoot, "src", "NCMapper.SourceGen", "Resources", "DiagnosticResources.resx");
        Assert.True(File.Exists(resxPath), $"resx 缺失: {resxPath}");
        var keys = XDocument.Load(resxPath).Descendants("data")
            .Select(e => (string?)e.Attribute("name") ?? "")
            .ToHashSet(StringComparer.Ordinal);

        foreach (var descriptor in AllDescriptors())
        {
            Assert.Contains($"{descriptor.Id}_Title", keys);
            Assert.Contains($"{descriptor.Id}_Message", keys);
            Assert.Contains($"{descriptor.Id}_Description", keys);
        }
    }

    // ------------------------------------------------------------------
    // 向4：发射站点实参数 ↔ MessageFormat 占位符数（DescriptorId 形态站点全扫描）
    // ------------------------------------------------------------------

    [Fact]
    public void EmissionSites_ArgCount_MatchesMessagePlaceholders()
    {
        var fieldMap = DescriptorFieldMap();
        var sites = CollectEmissionSites(fieldMap, out var unresolved);

        // 站点形态漂移登记：DescriptorId 表达式无法解析到两容器字段（须同步测试或修复形态）
        Assert.True(unresolved.Count == 0,
            $"DescriptorId 站点解析失败（符号形态漂移——*.XxxRule.Id 约定外形态须登记）: {string.Join("; ", unresolved)}");

        // 覆盖计数登记（任务书验收 #3 执行口径）：DescriptorId 静态站点 30 + 透传 3 +
        // Diagnostic.Create 调用面（编制期实读 12 处，不在本扫描形态内——首评 P1-1）＝完整发射面；
        // 93 描述符中无独立静态站点者（F 系经公共层聚合发射）由向2/向3 对账兜底。
        // 下限护栏：站点数骤降 = 扫描器失效或发射面重构（须同步本测试）
        Assert.True(sites.Count >= 20,
            $"DescriptorId 站点数 {sites.Count} 低于 20 下限——扫描器失效或发射面重构（计数登记: id={fieldMap.Count}, sites={sites.Count}）");

        foreach (var site in sites)
        {
            var exemptionKey = $"{Path.GetFileName(site.File)}:{site.RuleName}";
            if (SiteExemptions.ContainsKey(exemptionKey)) continue;
            if (site.ArgCount < 0) continue; // 未识别实参形态——登记面，占位符比对豁免（形态新增须评估入册）

            var placeholders = CountPlaceholders(site.Descriptor.MessageFormat.ToString(), site.RuleName);
            Assert.True(placeholders == site.ArgCount,
                $"{site.File}:{site.Line} {site.RuleName}: Args 实参数 {site.ArgCount} != 占位符数 {placeholders}（B4-02/B4-03 缺陷形态——实参同源矫正）");
        }
    }
}
