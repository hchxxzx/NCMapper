// （B5-4）Commit A 红测——不进 EqualityConservationTests 注册制的
// 三类直测（构造注入无 settable 面 / null-空数组契约 / NRE 防护）：
//   B2-19：ComposeConverterRef.Equals/GetHashCode 补 TranscribedParamTypes/TranscribedParamNames
//          （对齐 FluentForwardDeclaration.Equals 先例——符号态投影漏项，改内联 Convert 形参
//           组的增量失效漏判坐实面）；
//   B3-07：GlobalConfigSnapshot 重建等价契约——null（未设置，继承下层）与 []（已设置空清单）
//          不得判等、不得同哈希（修复前 SequenceEqual 视 null/空相等 → 空清单声明丢失静默
//          继承下层值——缓存键击穿形态）；
//   B4-07（防护腿）：DiagnosticInfo.Args null 双侧/单侧比较不抛 NRE（对齐 GetHashCode 的
//          三重防护形态）。
// 修复前形态：断言用例必红（判等/同哈希/NRE）；修复 commit 后转绿；回退任一字段行 → 复红
// （验收① 机械锚）。
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace NCMapper.SourceGen.Tests;

public class CacheKeyEquivalenceTests
{
    // ------------------------------------------------------------------
    // B2-19：ComposeConverterRef（构造注入型——Decl 为 get-only，不进注册制反射面）
    // ------------------------------------------------------------------

    /// <summary>符号 fixture（同 EqualityConservationTests.SymbolPair 最小形态）。</summary>
    private static (IPropertySymbol propA, IPropertySymbol propB) PropPair()
    {
        var comp = CSharpCompilation.Create("CkeSymbols",
            new[] { CSharpSyntaxTree.ParseText("""
                public class CkeHolder { public int Alpha { get; set; } public int Beta { get; set; } }
                """) },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });
        var model = comp.GetSemanticModel(comp.SyntaxTrees[0]);
        var props = comp.SyntaxTrees[0].GetRoot().DescendantNodes()
            .OfType<PropertyDeclarationSyntax>().ToList();
        Assert.True(props.Count >= 2, "symbol fixture 形态漂移：属性声明数不足");
        return ((IPropertySymbol)model.GetDeclaredSymbol(props[0])!, (IPropertySymbol)model.GetDeclaredSymbol(props[1])!);
    }

    /// <summary>B 形态（内联转录）ComposeConverterRef 构造——TranscribedParamTypes/Names 可注入。</summary>
    private static ComposeConverterRef MakeComposeRef(
        IPropertySymbol prop, string[]? paramTypes = null, string[]? paramNames = null)
    {
        var decl = new FluentForwardDeclaration(
            ConverterShape.T1SingleValue,
            Location.None,
            prop,
            new List<IPropertySymbol> { prop },
            "s + 1",
            paramTypes ?? new[] { "int" },
            paramNames ?? new[] { "x" },
            overrideIgnore: false);
        return new ComposeConverterRef(decl);
    }

    [Fact]
    public void ComposeConverterRef_TranscribedParamTypes_ParticipateInEquality()
    {
        var (propA, _) = PropPair();
        var a = MakeComposeRef(propA, paramTypes: new[] { "int" });
        var b = MakeComposeRef(propA, paramTypes: new[] { "long" });
        // B2-19：修复前 Equals 不含 TranscribedParamTypes → 判等 → 红
        Assert.False(a.Equals(b),
            "ComposeConverterRef.Equals：TranscribedParamTypes 变异必须判不等——字段参与相等性（B2-19）");
    }

    [Fact]
    public void ComposeConverterRef_TranscribedParamNames_ParticipateInEquality()
    {
        var (propA, _) = PropPair();
        var a = MakeComposeRef(propA, paramNames: new[] { "x" });
        var b = MakeComposeRef(propA, paramNames: new[] { "y" });
        // B2-19：修复前 Equals 不含 TranscribedParamNames → 判等 → 红
        Assert.False(a.Equals(b),
            "ComposeConverterRef.Equals：TranscribedParamNames 变异必须判不等——字段参与相等性（B2-19）");
    }

    [Fact]
    public void ComposeConverterRef_TranscribedParams_ParticipateInHashCode()
    {
        var (propA, _) = PropPair();
        var a = MakeComposeRef(propA, paramTypes: new[] { "int" }, paramNames: new[] { "x" });
        var b = MakeComposeRef(propA, paramTypes: new[] { "long" }, paramNames: new[] { "y" });
        // B2-19：修复前 GetHashCode 不含两字段 → 同哈希 → 红
        Assert.True(a.GetHashCode() != b.GetHashCode(),
            "ComposeConverterRef.GetHashCode：TranscribedParamTypes/Names 变异后哈希必须改变（B2-19）");
    }

    [Fact]
    public void ComposeConverterRef_EqualTwins_IndependentlyConstructed_SameHashCode()
    {
        var (propA, _) = PropPair();
        var a = MakeComposeRef(propA);
        var b = MakeComposeRef(propA);
        Assert.True(a.Equals(b), "同字段同值孪生对必须判等（fixture 自证）");
        Assert.True(a.GetHashCode() == b.GetHashCode(),
            "Equals 相等 ⇒ GetHashCode 相等（哈希契约；集合独立构造下朴素引用哈希在此红）");
    }

    // ------------------------------------------------------------------
    // B3-07：GlobalConfigSnapshot null/空数组区分（重建等价契约）
    // ------------------------------------------------------------------

    [Fact]
    public void GlobalConfigSnapshot_NullVsEmptyStringArray_NotEqual()
    {
        var unset = new GlobalConfigSnapshot();
        var emptyList = new GlobalConfigSnapshot { Prefixes = Array.Empty<string>() };
        // B3-07：修复前 SequenceEqual 视 null/空相等 → 判等 → 红
        Assert.False(unset.Equals(emptyList),
            "null（未设置）与 []（已设置空清单）不得判等——重建等价契约（B3-07）");
    }

    [Fact]
    public void GlobalConfigSnapshot_EmptyStringArray_HashDiffersFromUnset()
    {
        var unset = new GlobalConfigSnapshot();
        var emptyList = new GlobalConfigSnapshot { Prefixes = Array.Empty<string>() };
        // B3-07：修复前 HashList(null)=HashList([])=0 → 同哈希 → 红
        Assert.True(unset.GetHashCode() != emptyList.GetHashCode(),
            "null 与 [] 哈希必须区分（空清单取非 0 区分值）——新哈希契约（B3-07）");
    }

    [Fact]
    public void GlobalConfigSnapshot_AllListFields_NullVsEmpty_NotEqualAndHashDistinct()
    {
        // 10 项 string[] 配置逐一对照（反射枚举——新增清单字段自动入面）
        var listProps = typeof(GlobalConfigSnapshot).GetProperties()
            .Where(p => p.PropertyType == typeof(string[]) && p.CanWrite)
            .ToList();
        Assert.True(listProps.Count == 10,
            $"string[] 配置项形态漂移（预期 10 项，实测 {listProps.Count}）——须同步本用例");

        foreach (var prop in listProps)
        {
            var unset = new GlobalConfigSnapshot();
            var emptyList = new GlobalConfigSnapshot();
            prop.SetValue(emptyList, Array.Empty<string>());

            Assert.False(unset.Equals(emptyList),
                $"{prop.Name}: null（未设置）与 []（已设置空清单）不得判等——重建等价契约（B3-07）");
            Assert.True(unset.GetHashCode() != emptyList.GetHashCode(),
                $"{prop.Name}: null 与 [] 哈希必须区分（空清单取非 0 区分值）——新哈希契约（B3-07）");
        }
    }

    [Fact]
    public void GlobalConfigSnapshot_EmptyList_IsNotEmpty_SemanticsUnchanged()
    {
        // 现状语义锁定（B3-07 修复不动 IsEmpty 判据——null = 未设置才计入 IsEmpty）
        var unset = new GlobalConfigSnapshot();
        var emptyList = new GlobalConfigSnapshot { Prefixes = Array.Empty<string>() };
        Assert.True(unset.IsEmpty, "null 全项 = 未设置（IsEmpty 语义保持）");
        Assert.False(emptyList.IsEmpty, "[] = 已设置空清单——非未设置（IsEmpty 语义保持）");
    }

    // ------------------------------------------------------------------
    // B4-07（防护腿）：DiagnosticInfo.Args null 比较不抛 NRE
    // ------------------------------------------------------------------

    [Fact]
    public void DiagnosticInfo_ArgsNull_BothSides_EqualsWithoutNre()
    {
        var rule = new DiagnosticDescriptor("NCMUT001", "T", "M", "C", DiagnosticSeverity.Warning, true);
        var a = new DiagnosticInfo { Descriptor = rule, Args = null! };
        var b = new DiagnosticInfo { Descriptor = rule, Args = null! };
        // B4-07：修复前 Args.SequenceEqual(null) 抛 ArgumentNullException → 红（异常即失败）
        Assert.True(a.Equals(b),
            "Args 双侧 null 必须无 NRE 判等（对齐 GetHashCode 三重防护形态——B4-07）");
    }

    [Fact]
    public void DiagnosticInfo_ArgsNullOneSide_NotEqualWithoutNre()
    {
        var rule = new DiagnosticDescriptor("NCMUT001", "T", "M", "C", DiagnosticSeverity.Warning, true);
        var a = new DiagnosticInfo { Descriptor = rule, Args = null! };
        var b = new DiagnosticInfo { Descriptor = rule, Args = new object[] { "x" } };
        // B4-07：修复前 a.Args=null 进 SequenceEqual 抛 ArgumentNullException → 红
        Assert.False(a.Equals(b),
            "Args 单侧 null 必须无 NRE 判不等（null 视空组——B4-07 防护形态）");
    }
}
