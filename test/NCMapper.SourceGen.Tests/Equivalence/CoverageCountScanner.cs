using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NCMapper.SourceGen.Tests.Equivalence;

/// <summary>
/// Check6 计数下限门禁的计数源。
/// <para><b>口径</b> = 「行使该签名的测试方法数」：遍历仓库 <c>test/</c> 下全部 <c>*.cs</c>（排除 <c>bin/obj</c>），
/// 以 Roslyn 语法树界定测试方法（<c>[Fact]</c>/<c>[Theory]</c>/<c>[Property]</c> 标注的方法声明），
/// 对每个方法体文本匹配台账行的 <c>CountPattern</c>，命中即计 1（按 <c>file::method</c> 去重）。</para>
/// <para><b>为何用语法树而非纯文本状态机</b>：测试源码普遍含原样字符串字面量（其内容含 <c>{ } ( )</c>）与注释——
/// 纯括号配平的轻量状态机在这些形态上失衡，方法边界判定不稳（探索项 E1 实跑证伪）；
/// Roslyn 按语法 token 界定方法边界，确定性强，满足「两次运行计数一致」的收敛标准。</para>
/// <para><b>近似性边界</b>：文本命中为近似口径（非编译级语义；不计 theory 展开倍率；不辨接收者类型/重载归属）。
/// 同名签名行（如 <c>MemberHead.Convert</c> 四重载）共享同一 <c>CountPattern</c>，故其计数相同——
/// 门禁只对「相对自身登记下限的下降」负责，不主张绝对精确。</para>
/// </summary>
internal static class CoverageCountScanner
{
    private static readonly HashSet<string> TestAttributeNames =
        new(StringComparer.Ordinal) { "Fact", "Theory", "Property" };

    /// <summary>
    /// 计数源自引用面的<b>防护面</b>门禁类自身文件不参与计数（登记簿 #43 /）。
    /// <para>计数源遍历全 <c>test/</c> 树，而门禁类既是扫描器的调用者、又是自证宿主——其测试方法体
    /// 若含 <c>.MapTree(</c> 一类台账模式文本，即会<b>静默抬高</b>对应行的实测计数（门禁自灌下限），
    /// 使「该签名调用点减少」这一维的检出力被门禁自身的改动抵消。
    /// 本注<b>不写死当前命中数</b>——现无灌入属事实而非保证，防护由文件名排除承载，
    /// 其承重由 <c>SignatureCoverageIntegrityTests.SelfProof19_SelfFileExclusion_IsLoadBearing</c>
    /// 每次运行实测（门禁类文本内含台账模式 / 语料内不含其任何方法）。</para>
    /// <para>按<b>文件名</b>而非路径排除：门禁类与其扫描器同族搬迁时（改目录、改名不改后缀）防护不失效。</para>
    /// </summary>
    internal static readonly IReadOnlySet<string> SelfFileNames =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SignatureCoverageIntegrityTests.cs",
            "CoverageCountScanner.cs",
        };

    /// <summary>
    /// 按台账行规格计算「行使测试方法数」，键 = <see cref="RowKey"/>。
    /// <para><paramref name="surfaceFileFilters"/>（可空）= 面级文件过滤器：键 = 面名（台账 <c>Surface</c> 列），
    /// 值 = 作用于归一化相对路径（<c>test/...</c>，正斜杠）的正则。命中的面只统计<b>匹配该正则的文件</b>内的测试方法，
    /// 未列出的面（含全部既有 Fluent/属性面行）保持全 <c>test/</c> 树口径不变。
    /// 该参数是 ORM 终端面的同名辨异防线之一：三扩展终端同名（<c>Single</c>/<c>First</c>/<c>ToPagedList</c> 各一份）
    /// 且与 LINQ 同名，缺省 Name 派生模式的下限会被灌至 LINQ 同名量级而门禁实际失效
    /// （量级由 <c>SignatureCoverageIntegrityTests.SelfProof15</c> 每次运行实测，本注不写死数字）。</para>
    /// </summary>
    internal static Dictionary<string, int> CountForRows(
        string repoRoot,
        IEnumerable<(string Surface, string Name, string ShapeKey, string CountPattern)> specs,
        IReadOnlyDictionary<string, string>? surfaceFileFilters = null)
    {
        var methods = ExtractTestMethodBodies(repoRoot);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var spec in specs)
        {
            var key = RowKey(spec.Surface, spec.Name, spec.ShapeKey);
            if (counts.ContainsKey(key)) continue;
            var rx = new Regex(spec.CountPattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
            Regex? pathRx = null;
            if (surfaceFileFilters != null
                && surfaceFileFilters.TryGetValue(spec.Surface, out var pattern)
                && !string.IsNullOrWhiteSpace(pattern))
            {
                pathRx = new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            }
            var repoNorm = repoRoot.Replace('\\', '/').TrimEnd('/') + "/";
            counts[key] = methods
                .Where(m => (pathRx == null || pathRx.IsMatch(RelToRepo(m.File, repoNorm))) && rx.IsMatch(m.Body))
                .Select(m => m.File + "::" + m.Method)
                .Distinct(StringComparer.Ordinal)
                .Count();
        }

        return counts;
    }

    /// <summary>归一化绝对路径 → 仓库相对路径（<c>test/...</c>，正斜杠）；不在仓库内则原样返回。</summary>
    private static string RelToRepo(string normalizedAbsolutePath, string normalizedRepoRootWithSlash)
        => normalizedAbsolutePath.StartsWith(normalizedRepoRootWithSlash, StringComparison.OrdinalIgnoreCase)
            ? normalizedAbsolutePath[normalizedRepoRootWithSlash.Length..]
            : normalizedAbsolutePath;

    /// <summary>行键（与台账行一一对应；同名同形态键行唯一）。</summary>
    internal static string RowKey(string surface, string name, string shapeKey)
        => surface + "." + name + "@" + shapeKey;

    /// <summary>提取 <c>test/</c> 下全部测试方法（文件、方法名、方法体文本）。</summary>
    /// <param name="repoRoot">仓库根。</param>
    /// <param name="includeSelfFiles">
    /// <c>false</c>（缺省 = 生产口径）= 按 <see cref="SelfFileNames"/> 排除门禁类自身文件；
    /// <c>true</c> 仅供自证实测防护面承重（门禁类向语料贡献多少方法、是否正在灌入台账模式）。
    /// </param>
    internal static List<(string File, string Method, string Body)> ExtractTestMethodBodies(
        string repoRoot, bool includeSelfFiles = false)
    {
        var result = new List<(string, string, string)>();
        var testRoot = Path.Combine(repoRoot, "test");
        if (!Directory.Exists(testRoot)) return result;

        foreach (var file in Directory.EnumerateFiles(testRoot, "*.cs", SearchOption.AllDirectories))
        {
            var norm = file.Replace('\\', '/');
            if (norm.Contains("/bin/", StringComparison.Ordinal)
                || norm.Contains("/obj/", StringComparison.Ordinal))
                continue;
            if (!includeSelfFiles && SelfFileNames.Contains(Path.GetFileName(norm)))
                continue;   // 门禁自身文件不参与计数（见 <see cref="SelfFileNames"/> 注）

            string text;
            try { text = File.ReadAllText(file); }
            catch (IOException) { continue; }

            var tree = CSharpSyntaxTree.ParseText(text);
            foreach (var method in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (!IsTestMethod(method)) continue;
                var body = method.Body?.ToFullString()
                           ?? method.ExpressionBody?.ToFullString()
                           ?? string.Empty;
                result.Add((norm, method.Identifier.ValueText, body));
            }
        }

        return result;
    }

    private static bool IsTestMethod(MethodDeclarationSyntax method)
        => method.AttributeLists
            .SelectMany(a => a.Attributes)
            .Any(a =>
            {
                var name = a.Name.ToString();
                var last = name.Contains('.') ? name[(name.LastIndexOf('.') + 1)..] : name;
                if (last.EndsWith("Attribute", StringComparison.Ordinal))
                    last = last[..^"Attribute".Length];
                return TestAttributeNames.Contains(last);
            });
}
