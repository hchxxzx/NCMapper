using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace NCMapper.SourceGen.Tests.Equivalence;

/// <summary>
/// B-3 比较器体 ↔ 注册表一致性护栏（契约 + 八步骨架）——
/// 守恒测试"登记期望集 ↔ 执行体"判据在 L1 比较轴的同构移植，实现换轴 Roslyn（用户 2026-10-11 裁定，001）。
/// <para><b>契约（双向臂，共用同一"比较性提及"判据谓词）</b>：
/// 正向臂——注册表 Compared 字段必须在其映射到的 <c>Compare*</c> 方法体中有"比较性提及"；
/// 反向臂——注册表 Stripped 字段不得在该方法体中出现于比较位（防"登记剥离但实际在比"行为漂移；
/// 失败处置单出口：落注册表行角色，不设测试内白名单——H4 口径）。</para>
/// <para><b>"比较性提及"判据（步骤 4 文法，逐字采用）</b>
/// <c>MemberAccessExpressionSyntax</c>（含 <c>ConditionalAccessSyntax</c> 的 <c>.F</c> 部分），
/// <c>Expression ∈ {expected, actual}</c> ∧ <c>Name ∈ 目标字段名</c> ∧ 祖先形态 ∈
/// ① <c>==</c>/<c>!=</c> 二元比较的操作数（含 <c>!(…)</c> 包裹）；
/// ② <c>Equals</c> / <c>string.Equals</c> / <c>SequenceEqual</c> / <c>SequenceEqualOrdinal</c> 的实参；
/// ③ <c>CompareList</c> / 嵌套 <c>Compare*</c> 的实参（投影分派即比较）。
/// 不计入：注释、字符串/插值字面量、<c>differences.Add</c> 的消息与格式化实参、
/// if 守卫条件中的非比较性提及（<c>HasIndex</c> 形态）。</para>
/// <para>类型 → 方法映射读 <see cref="SnapshotFieldRegistry.ComparerMethodByType"/>（不在本类内硬编码）；
/// 映射值为 null 的类型（顶层 <c>MappingSnapshot</c>——反射枚举 + denylist 派生，无手写投影体）两臂跳过，
/// 扫描面矩阵见。</para>
/// <para><b>计数守门</b>：checkedCompared == 83 ∧ checkedStripped == 33 ∧ 映射类型数 == 7（B-4 后态；
/// 基准 = 口径表定案值，非本类自证派生）——护栏自身不得成为下一个 A-2（001）。
/// 键集与 <see cref="SnapshotFieldRegistry.All"/> 类型集集合相等亦在此护栏内。</para>
/// <para><b>交付形态（H5）</b>：先红（落地即检出 FormatString 两处，红态单独 commit 记 failure 清单原文）
/// → A-1 补齐后转绿（绿态单独 commit）——红→绿两 SHA 记入三态登记。</para>
/// </summary>
public class ComparerFieldCoverageTests
{
    /// <summary>扫描目标（仓库根相对路径）——七手写投影体所在文件。</summary>
    internal const string ComparerSourcePath =
        "test/NCMapper.SourceGen.Tests/Equivalence/SnapshotEquivalenceComparer.cs";

    private const string ComparerClassName = "SnapshotEquivalenceComparer";

    // 计数守门基准（口径表 B-4 后态：Compared 83 / Stripped 33 / 手写 Compare* 方法 7 个）
    private const int ExpectedCheckedCompared = 83;
    private const int ExpectedCheckedStripped = 33;
    private const int ExpectedMappedTypeCount = 7;

    // 判据形态 ② 的比较方法名（步骤 4 枚举，不自创措辞）
    private static readonly HashSet<string> ComparisonMethodNames = new(StringComparer.Ordinal)
    {
        "Equals", "SequenceEqual", "SequenceEqualOrdinal",
    };

    // ----------------------------------------------------------------
    // 真实核查（八步骨架 1-6：读源 → 语法树 → 期望集 → 判据 → 双臂 → 计数守门）
    // ----------------------------------------------------------------

    [Fact]
    public void ComparerBodies_MatchRegistryFieldCoverage_BothArms()
    {
        var t = BuildRealScanTargets();

        var failures = RunComparerCoverageCheck(
            t.ComparedByType, t.StrippedByType, t.MethodNameByType, t.MethodDeclByType,
            ExpectedCheckedCompared, ExpectedCheckedStripped, ExpectedMappedTypeCount);

        Assert.True(failures.Count == 0,
            "B-3 FAILED（比较器体 ↔ 注册表一致性——双向臂 + 计数守门）:\n" + string.Join("\n", failures));
    }

    /// <summary>映射列键集与注册表 All 类型集集合相等（映射自身不得漂移——护栏）。</summary>
    [Fact]
    public void ComparerMethodByType_KeySet_EqualsRegistryAllTypeNames()
    {
        var failures = RunKeySetCheck(
            SnapshotFieldRegistry.All.Select(t => t.TypeName),
            SnapshotFieldRegistry.ComparerMethodByType.Keys);

        Assert.True(failures.Count == 0,
            "B-3 键集护栏 FAILED:\n" + string.Join("\n", failures));
    }

    // ----------------------------------------------------------------
    // 自证四例（八步骨架 8——合成片段经同一 ParseText + 同一纯函数，#38 同一代码路径自证）
    // ----------------------------------------------------------------

    /// <summary>自证 ①：字段名仅出现在注释与 differences.Add 消息实参中 → 正向臂红（证明"提及 ≠ 比较"）。</summary>
    [Fact]
    public void SelfProof1_FieldMentionedOnlyInCommentAndMessageArgs_ForwardArmRed()
    {
        var decl = SyntheticMethod("""
            internal static class SyntheticComparer
            {
                private static void CompareProbe(Probe expected, Probe actual, List<EquivalenceDifference> differences, string path)
                {
                    // GhostField — 注释里提到一次
                    differences.Add(new EquivalenceDifference(path + ".GhostField",
                        "\"" + expected.GhostField + "\"", "\"" + actual.GhostField + "\"", "GhostField in message text"));
                }
            }
            """, "CompareProbe");

        var failures = RunComparerCoverageCheck(
            FieldMap(("Probe", new[] { "GhostField" })),
            FieldMap(("Probe", Array.Empty<string>())),
            NameMap(("Probe", (string?)"CompareProbe")),
            DeclMap(("Probe", (MethodDeclarationSyntax?)decl)),
            expectedCheckedCompared: 1, expectedCheckedStripped: 0, expectedMappedTypeCount: 1);

        Assert.Contains(failures, f => f.Contains("Probe.GhostField") && f.Contains("无比较性提及"));
    }

    /// <summary>自证 ②：Stripped 字段仅作 if 守卫（HasIndex 形态）→ 反向臂不红（证明守卫形态不误报）。</summary>
    [Fact]
    public void SelfProof2_StrippedFieldOnlyAsIfGuard_ReverseArmStaysGreen()
    {
        var decl = SyntheticMethod("""
            internal static class SyntheticComparer
            {
                private static void CompareProbe(Probe expected, Probe actual, List<EquivalenceDifference> differences, string path)
                {
                    if (expected.HasIndex || actual.HasIndex)
                    {
                        if (!expected.Items.SequenceEqual(actual.Items))
                            differences.Add(new EquivalenceDifference(path + ".Items", "SequenceEqual"));
                    }
                }
            }
            """, "CompareProbe");

        var failures = RunComparerCoverageCheck(
            FieldMap(("Probe", new[] { "Items" })),
            FieldMap(("Probe", new[] { "HasIndex" })),
            NameMap(("Probe", (string?)"CompareProbe")),
            DeclMap(("Probe", (MethodDeclarationSyntax?)decl)),
            expectedCheckedCompared: 1, expectedCheckedStripped: 1, expectedMappedTypeCount: 1);

        Assert.True(failures.Count == 0,
            "自证 ② 失效（守卫形态被误判为比较位）:\n" + string.Join("\n", failures));
    }

    /// <summary>自证 ③：Stripped 字段出现在 ==/!= 比较位 → 反向臂红。</summary>
    [Fact]
    public void SelfProof3_StrippedFieldInComparisonPosition_ReverseArmRed()
    {
        var decl = SyntheticMethod("""
            internal static class SyntheticComparer
            {
                private static void CompareProbe(Probe expected, Probe actual, List<EquivalenceDifference> differences, string path)
                {
                    if (expected.StrippedButCompared != actual.StrippedButCompared)
                        differences.Add(new EquivalenceDifference(path + ".StrippedButCompared", "drift"));
                }
            }
            """, "CompareProbe");

        var failures = RunComparerCoverageCheck(
            FieldMap(("Probe", Array.Empty<string>())),
            FieldMap(("Probe", new[] { "StrippedButCompared" })),
            NameMap(("Probe", (string?)"CompareProbe")),
            DeclMap(("Probe", (MethodDeclarationSyntax?)decl)),
            expectedCheckedCompared: 0, expectedCheckedStripped: 1, expectedMappedTypeCount: 1);

        Assert.Contains(failures, f => f.Contains("Probe.StrippedButCompared") && f.Contains("比较位"));
    }

    /// <summary>自证 ④：类型→方法映射副本缺一项（该类型映射置 null）→ 计数守门红。</summary>
    [Fact]
    public void SelfProof4_MethodMapCopyMissingEntry_CountGateRed()
    {
        var t = BuildRealScanTargets();

        // 内存变异：DiagnosticSnapshot 的映射置 null（= 映射副本缺一项，扫描面少一个手写体）
        t.MethodNameByType["DiagnosticSnapshot"] = null;
        t.MethodDeclByType["DiagnosticSnapshot"] = null;

        var failures = RunComparerCoverageCheck(
            t.ComparedByType, t.StrippedByType, t.MethodNameByType, t.MethodDeclByType,
            ExpectedCheckedCompared, ExpectedCheckedStripped, ExpectedMappedTypeCount);

        Assert.Contains(failures, f => f.Contains("映射类型数") && f.Contains("A-2"));
    }

    // ----------------------------------------------------------------
    // 数据源装配（真实核查入口——注册表 + 语法树，单点读取机制）
    // ----------------------------------------------------------------

    /// <summary>扫描面装配结果（真实核查与自证共用；字典可变以便自证做内存变异副本）。</summary>
    internal sealed record ScanTargets(
        Dictionary<string, IReadOnlyList<string>> ComparedByType,
        Dictionary<string, IReadOnlyList<string>> StrippedByType,
        Dictionary<string, string?> MethodNameByType,
        Dictionary<string, MethodDeclarationSyntax?> MethodDeclByType);

    private static ScanTargets BuildRealScanTargets()
    {
        var root = ParseSourceWithNoErrors(ReadRepoRelativeSource(ComparerSourcePath));
        var cls = LocateClass(root, ComparerClassName);

        var compared = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var stripped = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var names = new Dictionary<string, string?>(StringComparer.Ordinal);
        var decls = new Dictionary<string, MethodDeclarationSyntax?>(StringComparer.Ordinal);

        foreach (var (typeName, fields) in SnapshotFieldRegistry.All)
        {
            compared[typeName] = fields.Where(r => r.Role == FieldRole.Compared).Select(r => r.Field).ToList();
            stripped[typeName] = fields.Where(r => r.Role == FieldRole.Stripped).Select(r => r.Field).ToList();

            var methodName = SnapshotFieldRegistry.ComparerMethodByType.TryGetValue(typeName, out var m) ? m : null;
            names[typeName] = methodName;
            decls[typeName] = methodName == null ? null : LocateMethod(cls, methodName);
        }

        return new ScanTargets(compared, stripped, names, decls);
    }

    // ----------------------------------------------------------------
    // 纯核查逻辑（真实核查与自证共用同一函数——RunA4/RunA5 范式）
    // ----------------------------------------------------------------

    /// <summary>
    /// B-3 纯核查：双向臂 + 计数守门 + 键集一致性。输入为"类型 → 字段名集"与"类型 → 方法声明"
    /// 两张表（真实核查传注册表 + 真实语法树，自证传合成片段），输出 failure 清单（空 = 通过）。
    /// 计数基准由调用方传入（真实核查传方案书定案值 83/33/7，不得由输入派生——防恒真）。
    /// </summary>
    internal static List<string> RunComparerCoverageCheck(
        IReadOnlyDictionary<string, IReadOnlyList<string>> comparedByType,
        IReadOnlyDictionary<string, IReadOnlyList<string>> strippedByType,
        IReadOnlyDictionary<string, string?> methodNameByType,
        IReadOnlyDictionary<string, MethodDeclarationSyntax?> methodDeclByType,
        int expectedCheckedCompared,
        int expectedCheckedStripped,
        int expectedMappedTypeCount)
    {
        var failures = new List<string>();

        // 键集一致性（三张对照表必须与主表同键——登记面漂移即红）
        foreach (var (label, keys) in new (string, IEnumerable<string>)[]
                 {
                     ("StrippedByType", strippedByType.Keys),
                     ("ComparerMethodByType", methodNameByType.Keys),
                     ("MethodDeclByType", methodDeclByType.Keys),
                 })
        {
            failures.AddRange(RunKeySetCheck(comparedByType.Keys, keys, label));
        }

        int checkedCompared = 0;
        int checkedStripped = 0;
        int mappedTypes = 0;

        foreach (var typeName in comparedByType.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var methodName = methodNameByType.TryGetValue(typeName, out var n) ? n : null;

            // 映射值为 null 的类型（顶层 MappingSnapshot）两臂跳过——无手写投影体
            if (methodName == null) continue;
            mappedTypes++;

            var decl = methodDeclByType.TryGetValue(typeName, out var d) ? d : null;
            if (decl == null)
            {
                failures.Add($"{typeName}: ComparerMethodByType 指向方法 '{methodName}' 在 {ComparerClassName} 类中不存在——映射列漂移（B-3 扫描面定位失败）");
                continue;
            }

            var comparedFields = comparedByType.TryGetValue(typeName, out var c) ? c : Array.Empty<string>();
            var strippedFields = strippedByType.TryGetValue(typeName, out var s) ? s : Array.Empty<string>();
            checkedCompared += comparedFields.Count;
            checkedStripped += strippedFields.Count;

            var comparative = CollectComparativeMentions(decl);

            foreach (var field in comparedFields.OrderBy(f => f, StringComparer.Ordinal))
            {
                if (!comparative.Contains(field))
                    failures.Add($"{typeName}.{field} 注册表 Compared 但 {methodName} 体内无比较性提及——补比较（实现注册表声明）或将注册表行改 Stripped 登记理由");
            }

            foreach (var field in strippedFields.OrderBy(f => f, StringComparer.Ordinal))
            {
                if (comparative.Contains(field))
                    failures.Add($"{typeName}.{field} 登记 Stripped 但出现在 {methodName} 比较位——行为漂移，须改登记或撤比较");
            }
        }

        if (checkedCompared != expectedCheckedCompared)
            failures.Add($"计数守门: checkedCompared = {checkedCompared} ≠ 基准 {expectedCheckedCompared}（口径表 B-4 后态）——登记面与扫描面须同批对账，护栏自身不得成为下一个 A-2");
        if (checkedStripped != expectedCheckedStripped)
            failures.Add($"计数守门: checkedStripped = {checkedStripped} ≠ 基准 {expectedCheckedStripped}（口径表 B-4 后态）——登记面与扫描面须同批对账，护栏自身不得成为下一个 A-2");
        if (mappedTypes != expectedMappedTypeCount)
            failures.Add($"计数守门: 映射类型数 = {mappedTypes} ≠ 基准 {expectedMappedTypeCount}（手写 Compare* 投影体个数）——映射副本缺项，护栏自身不得成为下一个 A-2");

        return failures;
    }

    /// <summary>键集集合相等纯核查（真实核查与自证共用）。</summary>
    internal static List<string> RunKeySetCheck(
        IEnumerable<string> expectedKeys,
        IEnumerable<string> actualKeys,
        string label = "ComparerMethodByType")
    {
        var expected = new HashSet<string>(expectedKeys, StringComparer.Ordinal);
        var actual = new HashSet<string>(actualKeys, StringComparer.Ordinal);

        var onlyExpected = expected.Except(actual).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var onlyActual = actual.Except(expected).OrderBy(k => k, StringComparer.Ordinal).ToList();

        if (onlyExpected.Count == 0 && onlyActual.Count == 0) return new List<string>();

        return new List<string>
        {
            $"{label} 键集与 SnapshotFieldRegistry.All 类型集不相等——仅注册表有 [{string.Join(", ", onlyExpected)}] / 仅映射表有 [{string.Join(", ", onlyActual)}]（映射列漂移）"
        };
    }

    // ----------------------------------------------------------------
    // 判据："比较性提及"（步骤 4 文法）
    // ----------------------------------------------------------------

    /// <summary>
    /// 收集方法体内处于比较位的字段名：MemberAccess（含 ConditionalAccess 的 .F 部分）且
    /// Expression ∈ {expected, actual} 且祖先形态 ∈ ① ==/!= 操作数（含 !(…) 包裹）
    /// ② Equals/string.Equals/SequenceEqual/SequenceEqualOrdinal 实参
    /// ③ CompareList/嵌套 Compare* 实参。注释与字面量天然不在语法节点命中面内。
    /// </summary>
    internal static HashSet<string> CollectComparativeMentions(MethodDeclarationSyntax method)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in method.DescendantNodes())
        {
            string? fieldName;
            SyntaxNode anchored;

            if (node is MemberAccessExpressionSyntax ma
                && ma.Expression is IdentifierNameSyntax recv
                && IsOperandReceiver(recv.Identifier.Text))
            {
                fieldName = ma.Name.Identifier.Text;
                anchored = ma;
            }
            else if (node.Parent is ConditionalAccessExpressionSyntax ca
                     && ca.WhenNotNull == node
                     && ca.Expression is IdentifierNameSyntax caRecv
                     && IsOperandReceiver(caRecv.Identifier.Text))
            {
                // ConditionalAccessSyntax 的 .F 部分（expected?.F 形态）
                fieldName = node.ChildNodes().OfType<SimpleNameSyntax>().FirstOrDefault()?.Identifier.Text;
                anchored = ca;
            }
            else
            {
                continue;
            }

            if (string.IsNullOrEmpty(fieldName)) continue;
            if (IsComparativePosition(anchored)) result.Add(fieldName);
        }

        return result;
    }

    private static bool IsOperandReceiver(string identifierText)
        => identifierText == "expected" || identifierText == "actual";

    /// <summary>祖先形态判定：沿祖先链上溯（仅穿 !(…) 与括号），命中三类形态即真，其余即假。</summary>
    private static bool IsComparativePosition(SyntaxNode node)
    {
        var cur = node;
        for (int hop = 0; hop < 32 && cur != null; hop++)
        {
            var parent = cur.Parent;
            if (parent == null) return false;

            // ① ==/!= 二元比较的操作数
            if (parent is BinaryExpressionSyntax bin
                && (bin.IsKind(SyntaxKind.EqualsExpression) || bin.IsKind(SyntaxKind.NotEqualsExpression))
                && (bin.Left == cur || bin.Right == cur))
                return true;

            // !(…) 包裹 / 括号——继续上溯
            if (parent is PrefixUnaryExpressionSyntax pre
                && pre.IsKind(SyntaxKind.LogicalNotExpression) && pre.Operand == cur)
            {
                cur = pre;
                continue;
            }
            if (parent is ParenthesizedExpressionSyntax par && par.Expression == cur)
            {
                cur = par;
                continue;
            }

            // ② Equals 族 / ③ CompareList·嵌套 Compare* 的实参
            // （实参节点的直接父节点是 ArgumentSyntax/ArgumentListSyntax，须再上溯两级到调用点）
            if (parent is ArgumentSyntax arg
                && arg.Parent is ArgumentListSyntax argList
                && argList.Parent is InvocationExpressionSyntax inv
                && IsComparisonInvocation(inv))
                return true;

            return false;
        }

        return false;
    }

    private static bool IsComparisonInvocation(InvocationExpressionSyntax inv)
    {
        string name;
        if (inv.Expression is MemberAccessExpressionSyntax ma) name = ma.Name.Identifier.Text;
        else if (inv.Expression is IdentifierNameSyntax id) name = id.Identifier.Text;
        else return false;

        if (ComparisonMethodNames.Contains(name)) return true;
        return name == "CompareList" || name.StartsWith("Compare", StringComparison.Ordinal);
    }

    // ----------------------------------------------------------------
    // 读源 / 解析 / 定位基建（internal static——顶层 case 穷举断言单点复用，003 P2-4）
    // ----------------------------------------------------------------

    /// <summary>
    /// 仓库根相对路径读源——以 NCMapper.slnx 定位仓库根（SnapshotConservationTests 同模式）。
    /// <para>不复用 <c>SnapshotConservationTests.ReadSourceGenSource</c>：该 helper 硬编码
    /// src/NCMapper.SourceGen 根，对 test 树目标首跑即"源文件缺失"断言失败（002 P1-2）。</para>
    /// </summary>
    internal static string ReadRepoRelativeSource(string repoRelativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "NCMapper.slnx")))
            {
                var path = Path.Combine(dir.FullName, repoRelativePath.Replace('/', Path.DirectorySeparatorChar));
                Assert.True(File.Exists(path), $"源文件缺失: {path}");
                return File.ReadAllText(path);
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException("无法定位仓库根（未找到 NCMapper.slnx）——B-3 护栏须在仓库内运行");
    }

    /// <summary>语法树解析（无正则、无花括号配对）；语法诊断非零即失败（F5 回退通道触发点）。</summary>
    internal static SyntaxNode ParseSourceWithNoErrors(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var errors = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0,
            "ParseText 语法诊断非零（F5——须按回退口径重估并登记为方案级变更）:\n" +
            string.Join("\n", errors.Select(e => e.ToString())));
        return tree.GetRoot();
    }

    internal static ClassDeclarationSyntax LocateClass(SyntaxNode root, string className)
        => root.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault(c => c.Identifier.Text == className)
           ?? throw new InvalidOperationException($"类定位失败: {className}");

    internal static MethodDeclarationSyntax? LocateMethod(ClassDeclarationSyntax classDecl, string methodName)
        => classDecl.DescendantNodes().OfType<MethodDeclarationSyntax>()
             .FirstOrDefault(m => m.Identifier.Text == methodName);

    // ----------------------------------------------------------------
    // 自证辅助
    // ----------------------------------------------------------------

    private static MethodDeclarationSyntax SyntheticMethod(string source, string methodName)
    {
        var root = ParseSourceWithNoErrors(source);
        var cls = root.DescendantNodes().OfType<ClassDeclarationSyntax>().First();
        return LocateMethod(cls, methodName)
               ?? throw new InvalidOperationException($"合成片段方法定位失败: {methodName}");
    }

    private static Dictionary<string, IReadOnlyList<string>> FieldMap(params (string Type, string[] Fields)[] entries)
        => entries.ToDictionary(e => e.Type, e => (IReadOnlyList<string>)e.Fields, StringComparer.Ordinal);

    private static Dictionary<string, string?> NameMap(params (string Type, string? Name)[] entries)
        => entries.ToDictionary(e => e.Type, e => e.Name, StringComparer.Ordinal);

    private static Dictionary<string, MethodDeclarationSyntax?> DeclMap(params (string Type, MethodDeclarationSyntax? Decl)[] entries)
        => entries.ToDictionary(e => e.Type, e => e.Decl, StringComparer.Ordinal);
}
