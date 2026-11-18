using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NCMapper.SourceGen;

/// <summary>
/// [MapSubclass] 合成管道 transform（ForAttributeWithMetadataName
/// 专管道——扫描全部 [MapSubclass] 标注类，O-1 Spike Q2 单一入口回溯模型；本方法在
/// FluentExtractor——产物 FluentExtractionResult/FluentSnapshot/ToDiagnostics 均本文件成员，
/// 解析内核共享 MappingExtractor.ResolveSubclassRegistrations）。
/// <para>有 [MapFrom] 锚定 → 登记已由特性主通道消费（ExtractMappingCore → SubclassPairs），
/// 本管道构造未独立声明子类对的合成快照（SyntheticDeclarations 平行流——Arbiter 三查去重后
/// 入 mappings）；无锚定（孤儿）→ F49（锚定缺失：未被任何分派面引用），零合成。</para>
/// <para>产物融入 fluentResults 流（伪 FluentExtractionResult——Declarations 恒空，
/// 不进 F11/F16 冲突面；诊断经通道诊断流发射）。</para>
/// </summary>
internal static class SubclassSyntheticPipeline
{
    /// <summary>合成管道 transform（NCMapperSourceGenerator 专用管道入口）</summary>
    public static FluentExtractionResult? Extract(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        var targetSymbol = ctx.TargetSymbol as INamedTypeSymbol;
        if (targetSymbol == null) return null;

        // 回溯 [MapFrom] 锚定（类级——基类映射对存在性判定）
        INamedTypeSymbol? baseSource = null;
        Microsoft.CodeAnalysis.Location? mapFromLocation = null;
        foreach (var a in targetSymbol.GetAttributes())
        {
            if (AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeNames.MapFrom
                && a.ConstructorArguments.Length > 0
                && a.ConstructorArguments[0].Value is INamedTypeSymbol bs)
            {
                baseSource = bs;
                mapFromLocation = a.ApplicationSyntaxReference?.GetSyntax().GetLocation();
                break;
            }
        }

        var diagnostics = new List<DiagnosticInfo>();
        var synthetics = new List<FluentDeclaration>();

        if (baseSource == null)
        {
            // F49 孤儿：无 [MapFrom] 锚定——每条 [MapSubclass] 报锚定缺失（零合成）
            foreach (var attr in targetSymbol.GetAttributes())
            {
                if (AttrSymbolCache.DisplayName(attr.AttributeClass) != AttributeNames.MapSubclass) continue;
                var s0 = attr.ConstructorArguments.Length > 0 ? attr.ConstructorArguments[0].Value : null;
                var t0 = attr.ConstructorArguments.Length > 1 ? attr.ConstructorArguments[1].Value : null;
                diagnostics.Add(new DiagnosticInfo
                {
                    Descriptor = DiagnosticDescriptorsFluent.MapSubclassAnchorMissingRule,
                    Args = new object[] { s0 ?? "?", t0 ?? "?", targetSymbol.ToDisplayString() },
                    Location = attr.ApplicationSyntaxReference?.GetSyntax().GetLocation()
                        ?? targetSymbol.Locations.FirstOrDefault(),
                });
            }
            return new FluentExtractionResult(targetSymbol.Name,
                new List<FluentDeclaration>(), FluentExtractor.ToDiagnostics(diagnostics));
        }

        // 有锚定：特性主通道已消费 SubclassPairs（含 F46/F47 单点诊断）——本管道静默解析 + 合成
        var pairs = MappingExtractor.ResolveSubclassRegistrations(targetSymbol, baseSource, diagnostics: null);
        var anchorLocation = mapFromLocation ?? targetSymbol.Locations.FirstOrDefault()
            ?? Microsoft.CodeAnalysis.Location.None;
        var anchorSnapshot = MappingExtractor.SnapshotLocationInternal(anchorLocation);
        foreach (var sp in pairs)
        {
            var subFs = new FluentSnapshot(sp.SourceType, sp.TargetType, sp.Location ?? anchorLocation);
            var subSnapshot = MappingSnapshotBuilder.Build(subFs, null, ct);
            // F48 落空（与 Fluent 侧 ExtractCore 同口径——防静默丢子类字段；发射端 generatableKeys 防线兜底）
            if (subSnapshot == null || subSnapshot.SkipCodeGeneration
                || subSnapshot.PropertyMappings.Count == 0)
            {
                diagnostics.Add(new DiagnosticInfo
                {
                    Descriptor = DiagnosticDescriptorsFluent.SubclassTargetEmptyRule,
                    Args = new object[]
                    {
                        sp.SourceType.ToDisplayString(), sp.TargetType.ToDisplayString(),
                        baseSource.ToDisplayString(), targetSymbol.ToDisplayString(),
                    },
                    Location = sp.Location ?? anchorLocation,
                });
                continue;
            }
            synthetics.Add(new FluentDeclaration(subSnapshot, anchorSnapshot));
        }
        return new FluentExtractionResult(targetSymbol.Name,
            new List<FluentDeclaration>(), FluentExtractor.ToDiagnostics(diagnostics), synthetics);
    }
}
