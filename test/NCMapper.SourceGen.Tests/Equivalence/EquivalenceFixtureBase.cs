using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NCMapper.SourceGen;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests.Equivalence;

/// <summary>
/// Marks a test method as an equivalence pair with the given ID.
/// Used by registry integrity tests (check 2) to verify all pairs are registered.
/// Pair with [Trait("EqPair", id)] for --filter Trait~"A-03" single-pair runs.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class EqPairAttribute : Attribute
{
    public string Id { get; }
    public EqPairAttribute(string id) => Id = id;
}

/// <summary>
/// Equivalence fixture base — shared infrastructure for all A-class equivalence pairs.
/// Dual-source build → dual-channel direct extraction → L1 field-level + L2 specified-method-body
/// text-level assertion（L2 指定方法体文本级——单方法，非全产物；scheme fixture convention）
/// </summary>
public abstract class EquivalenceFixtureBase
{
    // ----------------------------------------------------------------
    // Core assertion: dual-source → L1 + L2
    // ----------------------------------------------------------------

    /// <summary>
    /// Assert that two sources (attribute-channel + Fluent-channel) produce equivalent MappingSnapshots
    /// (L1: field-level, differences list empty) AND equivalent generated Map method bodies
    /// (L2: specified-method-body text-level — L2 指定方法体文本级，单方法，非全产物).
    /// </summary>
    /// <param name="attrSource">[MapFrom] channel source code (S/T classes with attributes)</param>
    /// <param name="fluentSource">Fluent channel source code ([MapperConfig] class + Configure method)</param>
    /// <param name="pairId">Equivalence pair ID (e.g. "A-01")</param>
    /// <param name="mapMethodName">Expected generated method name (e.g. "Map_Source_To_TargetDto")</param>
    protected static void AssertEquivalent(string attrSource, string fluentSource, string pairId, string mapMethodName)
    {
        // --- L1: Field-level equivalence ---
        var attrSnapshot = ExtractAttrChannelSnapshot(attrSource);
        var fluentSnapshot = ExtractFluentChannelSnapshot(fluentSource);

        Assert.NotNull(attrSnapshot);
        Assert.NotNull(fluentSnapshot);

        var result = SnapshotEquivalenceComparer.Compare(attrSnapshot, fluentSnapshot);
        Assert.True(result.AreEquivalent,
            $"[{pairId}] L1 field-level equivalence FAILED — {result.Differences.Count} difference(s):\n" +
            string.Join("\n", result.Differences));

        // --- L2: 指定方法体文本级 equivalence (specified method body only — 单方法，非全产物) ---
        var attrGenResult = TestHelper.GenerateMapper(attrSource);
        var fluentGenResult = TestHelper.GenerateMapper(fluentSource);

        var attrBody = GetMethodBody(attrGenResult, mapMethodName);
        var fluentBody = GetMethodBody(fluentGenResult, mapMethodName);

        Assert.True(attrBody != null, $"[{pairId}] L2: method '{mapMethodName}' not found in attribute-channel generated code");
        Assert.True(fluentBody != null, $"[{pairId}] L2: method '{mapMethodName}' not found in Fluent-channel generated code");

        Assert.True(string.Equals(attrBody, fluentBody, StringComparison.Ordinal),
            $"[{pairId}] L2 text-level equivalence FAILED — method body differs:\n" +
            $"--- Attribute channel ---\n{attrBody}\n" +
            $"--- Fluent channel ---\n{fluentBody}");
    }

    /// <summary>
    /// Assert L1-only (skip L2) — used when text-level comparison is not meaningful
    /// (e.g. Q2 not yet converged, or mixed boundary scenarios).
    /// </summary>
    protected static void AssertL1Only(string attrSource, string fluentSource, string pairId)
    {
        var attrSnapshot = ExtractAttrChannelSnapshot(attrSource);
        var fluentSnapshot = ExtractFluentChannelSnapshot(fluentSource);

        Assert.NotNull(attrSnapshot);
        Assert.NotNull(fluentSnapshot);

        var result = SnapshotEquivalenceComparer.Compare(attrSnapshot, fluentSnapshot);
        Assert.True(result.AreEquivalent,
            $"[{pairId}] L1 field-level equivalence FAILED — {result.Differences.Count} difference(s):\n" +
            string.Join("\n", result.Differences));
    }

    /// <summary>
    /// L1 负例断言：双通道快照须检出差异，且差异含指定顶层字段路径。
    /// <para>与 <see cref="AssertL1Only"/> 对偶——锁定"承载字段确实参与 L1 比较"：该字段若被剥离
    /// （<c>SnapshotFieldRegistry</c> 角色为 Stripped），两侧都看不出差异 → 本断言红
    ///（方案书证伪 F5 假绿形态）。返回类型不暴露 internal 快照类型（CS0050 可访问性约束）。</para>
    /// </summary>
    protected static void AssertL1Difference(string attrSource, string fluentSource, string pairId, string expectedFieldPath)
    {
        var attrSnapshot = ExtractAttrChannelSnapshot(attrSource);
        var fluentSnapshot = ExtractFluentChannelSnapshot(fluentSource);

        Assert.NotNull(attrSnapshot);
        Assert.NotNull(fluentSnapshot);

        var result = SnapshotEquivalenceComparer.Compare(attrSnapshot!, fluentSnapshot!);
        Assert.False(result.AreEquivalent,
            $"[{pairId}] L1 负例失效：两通道快照未检出任何差异（承载字段被剥离 = 字段层等价缺证，F5 假绿）");
        Assert.Contains(result.Differences, d => d.FieldPath == expectedFieldPath);
    }

    /// <summary>
    /// 候选面读取（决策点 3——评审 002-P2-7 断言具体化）：按通道提取快照
    /// <c>AllTargetProperties</c> 的属性名（Ordinal 排序），供"特性侧 ↔ Fluent 侧集合相等"断言。
    /// </summary>
    protected static string[] AllTargetPropertyNames(string source, bool fluentChannel)
    {
        var snapshot = fluentChannel
            ? ExtractFluentChannelSnapshot(source)
            : ExtractAttrChannelSnapshot(source);

        Assert.NotNull(snapshot);
        return snapshot!.AllTargetProperties.Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
    }

    // ----------------------------------------------------------------
    // Attribute channel direct extraction (ExtractMappingCore)
    // ----------------------------------------------------------------

    /// <summary>
    /// Build compilation from source → locate [MapFrom] class → unwrap symbols → direct-call ExtractMappingCore.
    /// Bypasses ForAttributeWithMetadataName pipeline (scheme).
    /// </summary>
    private static MappingSnapshot? ExtractAttrChannelSnapshot(string source)
    {
        var compilation = TestHelper.BuildCompilation(source);
        var semanticModel = compilation.GetSemanticModel(compilation.SyntaxTrees.First());

        // Locate the [MapFrom] class (TypeDeclarationSyntax with [MapFrom] attribute)
        var typeDecl = compilation.SyntaxTrees.First().GetRoot()
            .DescendantNodes().OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(IsMapFromAnnotated);

        if (typeDecl == null)
        {
            var allTypes = compilation.SyntaxTrees.First().GetRoot()
                .DescendantNodes().OfType<TypeDeclarationSyntax>()
                .Select(t => $"{t.Identifier.ValueText} (attrs: {string.Join(",", t.AttributeLists.SelectMany(al => al.Attributes).Select(a => a.Name.ToString()))})");
            Assert.Fail($"Attribute channel: no [MapFrom] class found. Types found: [{string.Join("; ", allTypes)}]");
        }

        var targetSymbol = semanticModel.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;
        if (targetSymbol == null) Assert.Fail($"Attribute channel: GetDeclaredSymbol returned null for {typeDecl.Identifier.ValueText}");

        // Unwrap [MapFrom(typeof(Source))] attribute → source type symbol
        var mapFromAttr = targetSymbol.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.ToDisplayString() == "NCMapper.MapFromAttribute" ||
            a.AttributeClass?.ToDisplayString() == "NCMapper.Attributes.MapFromAttribute");

        if (mapFromAttr == null || mapFromAttr.ConstructorArguments.Length == 0)
            Assert.Fail($"Attribute channel: [MapFrom] attribute not found or has no constructor args on {targetSymbol.Name}");

        var sourceType = mapFromAttr.ConstructorArguments[0].Value as INamedTypeSymbol;
        if (sourceType == null) Assert.Fail($"Attribute channel: [MapFrom] source type arg is null on {targetSymbol.Name}");

        // Direct-call ExtractMappingCore (K2 split entry point)
        return MappingExtractor.ExtractMappingCore(targetSymbol, sourceType, typeDecl, CancellationToken.None);
    }

    // ----------------------------------------------------------------
    // Fluent channel direct extraction (ExtractCore)
    // ----------------------------------------------------------------

    /// <summary>
    /// Build compilation from source → locate [MapperConfig] class → direct-call ExtractCore.
    /// Bypasses ForAttributeWithMetadataName pipeline (scheme).
    /// </summary>
    private static MappingSnapshot? ExtractFluentChannelSnapshot(string source)
    {
        var compilation = TestHelper.BuildCompilation(source);

        // Locate the [MapperConfig] class
        var typeDecl = compilation.SyntaxTrees.First().GetRoot()
            .DescendantNodes().OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(IsMapperConfigAnnotated);

        if (typeDecl == null)
        {
            var allTypes = compilation.SyntaxTrees.First().GetRoot()
                .DescendantNodes().OfType<TypeDeclarationSyntax>()
                .Select(t => $"{t.Identifier.ValueText} (attrs: {string.Join(",", t.AttributeLists.SelectMany(al => al.Attributes).Select(a => a.Name.ToString()))})");
            Assert.Fail($"Fluent channel: no [MapperConfig] class found. Types found: [{string.Join("; ", allTypes)}]");
        }

        var semanticModel = compilation.GetSemanticModel(typeDecl.SyntaxTree);
        var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;
        if (typeSymbol == null) Assert.Fail($"Fluent channel: GetDeclaredSymbol returned null for {typeDecl.Identifier.ValueText}");

        // Direct-call ExtractCore (K2 split entry point)
        var extractionResult = FluentExtractor.ExtractCore(compilation, typeSymbol, typeDecl, CancellationToken.None);
        if (extractionResult == null)
            Assert.Fail($"Fluent channel ExtractCore returned null — typeSymbol={typeSymbol?.Name}");
        if (extractionResult.Declarations.Count == 0)
        {
            var diags = string.Join("; ", extractionResult.Diagnostics.Select(d => d.DescriptorId));
            Assert.Fail($"Fluent channel ExtractCore returned 0 declarations — typeSymbol={typeSymbol?.Name}, diagnostics=[{diags}]");
        }

        // Return first declaration's snapshot
        return extractionResult.Declarations[0].Snapshot;
    }

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    private static bool IsMapFromAnnotated(TypeDeclarationSyntax typeDecl)
    {
        return typeDecl.AttributeLists.SelectMany(al => al.Attributes)
            .Any(a => a.Name.ToString().Contains("MapFrom"));
    }

    private static bool IsMapperConfigAnnotated(TypeDeclarationSyntax typeDecl)
    {
        return typeDecl.AttributeLists.SelectMany(al => al.Attributes)
            .Any(a => a.Name.ToString().Contains("MapperConfig"));
    }

    private static string? GetMethodBody(MapperGenerationResult result, string methodName)
    {
        if (result.Methods.TryGetValue(methodName, out var methods) && methods.Count > 0)
            return methods[0].Body;
        return null;
    }
}
