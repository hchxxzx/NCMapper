using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace NCMapper.SourceGen.Tests.Equivalence;

/// <summary>
/// 生成产物注释面零过程信息守门：逐字节基线产物（<c>generated-baseline/**</c> 下全部 <c>.g.cs</c>）
/// 的注释不得含任务编号、需求编号、内部文档章节引用或日期。
/// <para>生成码是调用者可见面——它落在用户工程的 <c>obj/Generated</c> 里、随用户代码一起编译，
/// 其头部与成员注释由源生成器发射模板逐字决定；因此该面需要常驻锁定，防止发射模板回灌过程噪声。</para>
/// <para>判据为固定模式集（任务编号 / 需求编号 / 章节引用 / ISO 日期 / 批次与子任务标记），
/// 命中即失败并列出文件、行号与原文。模式集与 <c>scripts/check-comment-process-noise.ps1</c>
/// 硬模式同族，但只覆盖生成产物面——该面不在脚本扫描面（src / samples）之内。</para>
/// </summary>
public class GeneratedArtifactCommentNoiseTests
{
    private static readonly Regex[] NoisePatterns =
    {
        new("TASK-\\d{8}-\\d{3}", RegexOptions.Compiled),
        new(@"\bT-\d{3}\b", RegexOptions.Compiled),
        new(@"\bS-\d{2}\b", RegexOptions.Compiled),
        new(@"\bB-\d{2}\b", RegexOptions.Compiled),
        new(@"§\d", RegexOptions.Compiled),
        new(@"20\d{2}-\d{2}-\d{2}", RegexOptions.Compiled),
    };

    [Fact]
    public void GeneratedBaseline_Has_No_Process_Noise()
    {
        var repoRoot = CapabilitySurfaceIntegrityTests.FindRepoRoot();
        var baselineRoot = Path.Combine(repoRoot, "generated-baseline");
        Assert.True(Directory.Exists(baselineRoot), $"基线目录缺失：{baselineRoot}");

        var files = Directory.GetFiles(baselineRoot, "*.cs", SearchOption.AllDirectories);
        Assert.NotEmpty(files);

        var failures = new List<string>();
        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (var pattern in NoisePatterns)
                {
                    if (pattern.IsMatch(lines[i]))
                    {
                        failures.Add($"{Path.GetFileName(file)}:{i + 1} [{pattern}] {lines[i].Trim()}");
                        break;
                    }
                }
            }
        }

        Assert.True(
            failures.Count == 0,
            $"生成产物注释面含过程信息 {failures.Count} 处：{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
    }
}
