using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using NCMapper.SourceGen;
using Xunit;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// 字段守恒测试框架（B4-13 +）——"逐字段枚举"点的
/// Equals/GetHashCode 字段集一致性机械防线（注册制反射全量，统筹 D2 定案）。
/// <para><b>语义边界</b>（首评 P1-2）：既有 <see cref="SnapshotConservationTests"/>（W1 快照重建
/// 透传守恒）与 SnapshotFieldRegistry（双通道等价比较面登记）承载"比较面/透传面"，
/// 本框架承载"Equals 参与 / GetHashCode 参与"面——孪生变异确定性断言，不复用其 Role。</para>
/// <para><b>断言三段</b>：</para>
/// <list type="bullet">
/// <item>孪生变异——每个注册变异器构造"基准 except 单字段"孪生对：Equals 恒 false（守恒面）；</item>
/// <item>哈希差异确定性——可哈希标量字段（豁免表外）变异后 GetHashCode 必须改变
/// （B4-13 漏项字段修复前哈希不变红、修复后改变绿——先红后绿 D3）；</item>
/// <item>契约断言（评审 002-P1-1 自动拦截器）——Equals 相等的孪生对（同字段同值、集合独立
/// 构造）GetHashCode 必须相等：朴素引用哈希（hash*31 + List.GetHashCode()）在此直接红。</item>
/// </list>
/// <para><b>豁免口径</b>（002-P1-1 定案）：豁免仅限守恒断言形态（集合字段不断言"单元素变异 →
/// 哈希差异"，聚合实现自由度），集合字段必须参与哈希计算（由验收 #4 的 47 项清单
/// 人工对照兜底）。新类型接入 = 追加注册项 + 逐字段变异清单（泛化承接）。</para>
/// </summary>
public class EqualityConservationTests
{
    // ------------------------------------------------------------------
    // 注册制载体（最小形态——泛化能力以"新类型注册变异器 + 豁免表即可接入"为限）
    // ------------------------------------------------------------------

    /// <summary>单字段变异：Field 名 + 孪生值工厂 + 哈希差异断言豁免标志（集合族豁免）。</summary>
    private sealed record Mutation(string Field, Func<object?> TwinValue, bool HashExempt = false);

    /// <summary>注册项：类型名 + 基准实例工厂（每次调用独立构造——契约断言的"集合独立构造"前提）+ 变异清单。</summary>
    private sealed record ConservationCase(string TypeName, Func<object> BaseFactory, IReadOnlyList<Mutation> Mutations);

    /// <summary>反射写入属性（init-only 为 C# 编译期约束，运行时反射赋值有效——modreq 不拦反射）。</summary>
    private static void SetProperty(object instance, string property, object? value)
    {
        var prop = instance.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"{instance.GetType().Name}.{property} not found");
        prop.SetValue(instance, value);
    }

    // ------------------------------------------------------------------
    // Roslyn fixture：IPropertySymbol/INamedTypeSymbol 对（PropertyMapping/Compose 族 ISymbol 字段）
    // ------------------------------------------------------------------

    private static (IPropertySymbol PropA, IPropertySymbol PropB, INamedTypeSymbol TypeA, INamedTypeSymbol TypeB) SymbolPair()
    {
        var comp = CSharpCompilation.Create("EqConstSymbols",
            new[] { CSharpSyntaxTree.ParseText("""
                public class EqConstHolderA { public int Alpha { get; set; } public int Beta { get; set; } }
                public class EqConstHolderB { public int Gamma { get; set; } }
                """) },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });
        var model = comp.GetSemanticModel(comp.SyntaxTrees[0]);
        var props = comp.SyntaxTrees[0].GetRoot().DescendantNodes()
            .OfType<PropertyDeclarationSyntax>().ToList();
        Assert.True(props.Count >= 3, "symbol fixture 形态漂移：属性声明数不足");
        var propA = (IPropertySymbol)model.GetDeclaredSymbol(props[0])!;
        var propB = (IPropertySymbol)model.GetDeclaredSymbol(props[1])!;
        var types = comp.SyntaxTrees[0].GetRoot().DescendantNodes()
            .OfType<TypeDeclarationSyntax>().ToList();
        var typeA = (INamedTypeSymbol)model.GetDeclaredSymbol(types[0])!;
        var typeB = (INamedTypeSymbol)model.GetDeclaredSymbol(types[^1])!;
        Assert.NotEqual(SymbolEqualityComparer.Default.GetHashCode(propA), SymbolEqualityComparer.Default.GetHashCode(propB));
        Assert.NotEqual(SymbolEqualityComparer.Default.GetHashCode(typeA), SymbolEqualityComparer.Default.GetHashCode(typeB));
        return (propA, propB, typeA, typeB);
    }

    private static TypeRef MakeTypeRef(string equalityKey) => new() { FullName = equalityKey, EqualityKey = equalityKey };

    private static PropertyRef MakePropertyRef(string name) => new() { Name = name, Type = MakeTypeRef("T:" + name) };

    private static LocationSnapshot MakeLocation(string file) => new(
        file, new TextSpan(0, 5), new LinePositionSpan(new LinePosition(0, 0), new LinePosition(0, 5)));

    private static DiagnosticInfo MakeDiagnostic(DiagnosticDescriptor rule)
        => new() { Descriptor = rule, Args = new object[] { "d" } };

    // ------------------------------------------------------------------
    // 注册表：B4-13 五类型/五处 + ConstructorParameterRef（回归面，字段集对齐口径同族互证）
    // ------------------------------------------------------------------

    private static IEnumerable<ConservationCase> Registry()
    {
        var (propA, propB, typeA, typeB) = SymbolPair();

        // DiagnosticInfo 注册 fixture（#8）：两个不同 Descriptor 单例——Equals 为引用比较（Descriptors
        // static 单例语义），哈希为 DiagnosticDescriptor 值哈希
        var ruleA = new DiagnosticDescriptor("NCMUT001", "T", "M", "C", DiagnosticSeverity.Warning, true);
        var ruleB = new DiagnosticDescriptor("NCMUT002", "T", "M", "C", DiagnosticSeverity.Warning, true);

        // ---- 1. ConstructorParameterRef（B4-04 回归面；5 字段全参与）----
        yield return new ConservationCase(
            nameof(ConstructorParameterRef),
            () => new ConstructorParameterRef
            {
                Name = "x",
                Type = MakeTypeRef("T:int"),
                HasExplicitDefaultValue = true,
                ExplicitDefaultValue = 1,
                SourcePropertyName = "s",
            },
            new[]
            {
                new Mutation("Name", () => "y"),
                new Mutation("Type", () => MakeTypeRef("T:long")),
                new Mutation("HasExplicitDefaultValue", () => false),
                new Mutation("ExplicitDefaultValue", () => 2),
                new Mutation("SourcePropertyName", () => "t"),
            });

        // ---- 2. PropertyMapping（符号态；Equals 27 项）----
        yield return new ConservationCase(
            nameof(PropertyMapping),
            () => new PropertyMapping
            {
                SourceProperty = propA,
                TargetProperty = propA,
                SourcePath = new List<IPropertySymbol> { propA },
                SourceIndexes = new List<int?> { 0 },
                ConverterDeclaringType = "C",
                ConverterMethodName = "M",
                ConverterShape = ConverterShape.T1SingleValue,
                BackConverterDeclaringType = "BC",
                BackConverterMethodName = "BM",
                BackConverterShape = ConverterShape.T1SingleValue,
                MaskType = typeA,
                FormatString = null,
                IsFromJson = false,
                FluentOverrideIgnore = false,
                FluentIgnoreDirection = null,
                HasFluentExplicit = false,
                ConditionMethodRef = null,
                ConditionMethodBody = null,
                ConditionMethodParamName = null,
                ConditionShape = ConditionShape.None,
                ConverterMethodBody = null,
                ConverterMethodParamTypes = null,
                ConverterMethodParamNames = null,
                BackConverterMethodBody = null,
                BackConverterMethodParamTypes = null,
                BackConverterMethodParamNames = null,
            },
            new[]
            {
                new Mutation("IsFlattened", () => true),
                new Mutation("PathOrigin", () => PathOrigin.ExplicitDotPath),
                new Mutation("SourceProperty", () => propB),
                new Mutation("TargetProperty", () => propB),
                new Mutation("SourcePath", () => new List<IPropertySymbol> { propB }, HashExempt: true),
                new Mutation("SourceIndexes", () => new List<int?> { 1 }, HashExempt: true),
                new Mutation("ConverterDeclaringType", () => "C2"),
                new Mutation("ConverterMethodName", () => "M2"),
                new Mutation("ConverterShape", () => ConverterShape.T2MultiField),
                new Mutation("BackConverterDeclaringType", () => "BC2"),
                new Mutation("BackConverterMethodName", () => "BM2"),
                new Mutation("BackConverterShape", () => ConverterShape.T1Guarded),
                new Mutation("MaskType", () => typeB),
                // FormatString 变异行（守恒面新增字段——改格式串触发失效）
                new Mutation("FormatString", () => "yyyy-MM-dd"),
                new Mutation("IsFromJson", () => true),
                new Mutation("FluentOverrideIgnore", () => true),
                new Mutation("FluentIgnoreDirection", () => 1),
                // FluentSourceIgnoreDirection 变异行随字段退役移除
                new Mutation("HasFluentExplicit", () => true),
                new Mutation("ConditionMethodRef", () => "a::b"),
                new Mutation("ConditionMethodBody", () => "body"),
                new Mutation("ConditionMethodParamName", () => "p"),
                new Mutation("ConditionShape", () => ConditionShape.FieldValue),
                new Mutation("ConverterMethodBody", () => "cb"),
                new Mutation("ConverterMethodParamTypes", () => new[] { "int" }, HashExempt: true),
                new Mutation("ConverterMethodParamNames", () => new[] { "a" }, HashExempt: true),
                new Mutation("BackConverterMethodBody", () => "bcb"),
                new Mutation("BackConverterMethodParamTypes", () => new[] { "int" }, HashExempt: true),
                new Mutation("BackConverterMethodParamNames", () => new[] { "a" }, HashExempt: true),
            });

        // ---- 3. ComposePropertyMapping（符号态；Equals 17 项）----
        yield return new ConservationCase(
            nameof(ComposePropertyMapping),
            () => new ComposePropertyMapping
            {
                SubSourceType = typeA,
                SourceProperty = propA,
                SourcePath = new List<IPropertySymbol> { propA },
                SourceIndexes = new List<int?> { 0 },
                TargetProperty = propA,
                ConverterDeclaringType = "C",
                ConverterMethodName = "M",
                ConverterShape = ConverterShape.T1SingleValue,
                MaskType = typeA,
                FormatString = null,
                IsFromJson = false,
                ConditionMethodRef = null,
                ConditionMethodBody = null,
                ConditionMethodParamName = null,
                ConditionShape = ConditionShape.None,
                ConverterMethodBody = null,
                ConverterMethodParamTypes = null,
                ConverterMethodParamNames = null,
            },
            new[]
            {
                new Mutation("SubSourceType", () => typeB),
                new Mutation("SourceProperty", () => propB),
                new Mutation("SourcePath", () => new List<IPropertySymbol> { propB }, HashExempt: true),
                new Mutation("SourceIndexes", () => new List<int?> { 1 }, HashExempt: true),
                new Mutation("TargetProperty", () => propB),
                new Mutation("ConverterDeclaringType", () => "C2"),
                new Mutation("ConverterMethodName", () => "M2"),
                new Mutation("ConverterShape", () => ConverterShape.T2MultiField),
                new Mutation("MaskType", () => typeB),
                // FormatString 变异行（Compose 符号态守恒面）
                new Mutation("FormatString", () => "N2"),
                new Mutation("IsFromJson", () => true),
                new Mutation("ConditionMethodRef", () => "a::b"),
                new Mutation("ConditionMethodBody", () => "body"),
                new Mutation("ConditionMethodParamName", () => "p"),
                new Mutation("ConditionShape", () => ConditionShape.FieldValue),
                new Mutation("ConverterMethodBody", () => "cb"),
                new Mutation("ConverterMethodParamTypes", () => new[] { "int" }, HashExempt: true),
                new Mutation("ConverterMethodParamNames", () => new[] { "a" }, HashExempt: true),
            });

        // ---- 4. PropertyMappingSnapshot（值态；Equals 19 项）----
        yield return new ConservationCase(
            nameof(PropertyMappingSnapshot),
            () => new PropertyMappingSnapshot
            {
                Target = MakePropertyRef("t1"),
                Source = MakePropertyRef("s1"),
                SourcePath = new List<PropertyRef> { MakePropertyRef("s1") },
                SourceIndexes = new List<int?> { 0 },
                ReverseWritePath = null,
                IsFlattened = false,
                PathOrigin = PathOrigin.Direct,
                ConverterDeclaringType = "C",
                ConverterMethodName = "M",
                ConverterShape = ConverterShape.T1SingleValue,
                MaskType = null,
                FormatString = null,
                IsFromJson = false,
                ConditionMethodRef = null,
                ConditionMethodBody = null,
                ConditionMethodParamName = null,
                ConditionShape = ConditionShape.None,
                ConverterMethodBody = null,
                ConverterMethodParamTypes = null,
                ConverterMethodParamNames = null,
            },
            new[]
            {
                new Mutation("Target", () => MakePropertyRef("t2")),
                new Mutation("Source", () => MakePropertyRef("s2")),
                new Mutation("SourcePath", () => new List<PropertyRef> { MakePropertyRef("s2") }, HashExempt: true),
                new Mutation("SourceIndexes", () => new List<int?> { 1 }, HashExempt: true),
                new Mutation("ReverseWritePath", () => new List<PropertyRef> { MakePropertyRef("r") }, HashExempt: true),
                new Mutation("IsFlattened", () => true),
                new Mutation("PathOrigin", () => PathOrigin.ExplicitDotPath),
                new Mutation("ConverterDeclaringType", () => "C2"),
                new Mutation("ConverterMethodName", () => "M2"),
                new Mutation("ConverterShape", () => ConverterShape.T2MultiField),
                new Mutation("MaskType", () => MakeTypeRef("T:mask")),
                // FormatString 变异行（pm 值态守恒面）
                new Mutation("FormatString", () => "yyyy-MM-dd"),
                new Mutation("IsFromJson", () => true),
                new Mutation("ConditionMethodRef", () => "a::b"),
                new Mutation("ConditionMethodBody", () => "body"),
                new Mutation("ConditionMethodParamName", () => "p"),
                new Mutation("ConditionShape", () => ConditionShape.FieldValue),
                new Mutation("ConverterMethodBody", () => "cb"),
                new Mutation("ConverterMethodParamTypes", () => new[] { "int" }, HashExempt: true),
                new Mutation("ConverterMethodParamNames", () => new[] { "a" }, HashExempt: true),
            });

        // ---- 5. ComposeMappingSnapshot（值态；Equals 18 项）----
        yield return new ConservationCase(
            nameof(ComposeMappingSnapshot),
            () => new ComposeMappingSnapshot
            {
                SubSource = MakeTypeRef("T:sub"),
                Source = MakePropertyRef("s1"),
                SourcePath = new List<PropertyRef> { MakePropertyRef("s1") },
                SourceIndexes = new List<int?> { 0 },
                Target = MakePropertyRef("t1"),
                ConverterDeclaringType = "C",
                ConverterMethodName = "M",
                ConverterShape = ConverterShape.T1SingleValue,
                MaskType = null,
                FormatString = null,
                IsFromJson = false,
                ConditionMethodRef = null,
                ConditionMethodBody = null,
                ConditionMethodParamName = null,
                ConditionShape = ConditionShape.None,
                ConverterMethodBody = null,
                ConverterMethodParamTypes = null,
                ConverterMethodParamNames = null,
            },
            new[]
            {
                new Mutation("SubSource", () => MakeTypeRef("T:sub2")),
                new Mutation("Source", () => MakePropertyRef("s2")),
                new Mutation("SourcePath", () => new List<PropertyRef> { MakePropertyRef("s2") }, HashExempt: true),
                new Mutation("SourceIndexes", () => new List<int?> { 1 }, HashExempt: true),
                new Mutation("Target", () => MakePropertyRef("t2")),
                new Mutation("ConverterDeclaringType", () => "C2"),
                new Mutation("ConverterMethodName", () => "M2"),
                new Mutation("ConverterShape", () => ConverterShape.T2MultiField),
                new Mutation("MaskType", () => MakeTypeRef("T:mask")),
                // FormatString 变异行（Compose 值态守恒面）
                new Mutation("FormatString", () => "D"),
                new Mutation("IsFromJson", () => true),
                new Mutation("ConditionMethodRef", () => "a::b"),
                new Mutation("ConditionMethodBody", () => "body"),
                new Mutation("ConditionMethodParamName", () => "p"),
                new Mutation("ConditionShape", () => ConditionShape.FieldValue),
                new Mutation("ConverterMethodBody", () => "cb"),
                new Mutation("ConverterMethodParamTypes", () => new[] { "int" }, HashExempt: true),
                new Mutation("ConverterMethodParamNames", () => new[] { "a" }, HashExempt: true),
            });

        // ---- 6. DeepCopyNodeShape（值态；无 ISymbol 字段；Equals 22 项）----
        yield return new ConservationCase(
            nameof(DeepCopyNodeShape),
            () => new DeepCopyNodeShape
            {
                Type = MakeTypeRef("T:node"),
                Location = null,
                HasPublicParameterlessCtor = false,
                PublicConstructors = Array.Empty<ConstructorRef>(),
                Properties = Array.Empty<DeepCopyPropertyShape>(),
                IsLeaf = false,
                IsCollectionLike = false,
                IsDictionary = false,
                KeyType = null,
                ValueType = null,
                ElementType = null,
                IsNonGenericEnumerable = false,
                IsArray = false,
                ArrayElementType = null,
                IsNullableValueType = false,
                UnderlyingType = null,
                IsInterface = false,
                IsTypeParameter = false,
                IsPointer = false,
                IsValueType = false,
                IsString = false,
                IsDelegate = false,
            },
            new[]
            {
                new Mutation("Type", () => MakeTypeRef("T:node2")),
                new Mutation("Location", () => MakeLocation("f2.cs")),
                new Mutation("HasPublicParameterlessCtor", () => true),
                new Mutation("PublicConstructors", () => new[]
                {
                    new ConstructorRef { IsPublic = true, Parameters = new[] { new ConstructorParameterRef { Name = "x", Type = MakeTypeRef("T:int") } } },
                }, HashExempt: true),
                new Mutation("Properties", () => new[]
                {
                    new DeepCopyPropertyShape { Name = "n1", Type = MakeTypeRef("T:int") },
                }, HashExempt: true),
                new Mutation("IsLeaf", () => true),
                new Mutation("IsCollectionLike", () => true),
                new Mutation("IsDictionary", () => true),
                new Mutation("KeyType", () => MakeTypeRef("T:k")),
                new Mutation("ValueType", () => MakeTypeRef("T:v")),
                new Mutation("ElementType", () => MakeTypeRef("T:e")),
                new Mutation("IsNonGenericEnumerable", () => true),
                new Mutation("IsArray", () => true),
                new Mutation("ArrayElementType", () => MakeTypeRef("T:ae")),
                new Mutation("IsNullableValueType", () => true),
                new Mutation("UnderlyingType", () => MakeTypeRef("T:u")),
                new Mutation("IsInterface", () => true),
                new Mutation("IsTypeParameter", () => true),
                new Mutation("IsPointer", () => true),
                new Mutation("IsValueType", () => true),
                new Mutation("IsString", () => true),
                new Mutation("IsDelegate", () => true),
            });

        // ---- 7. MappingInfo（符号态；B5-4 R-10：四字段补齐面——NoAutoInclude/
        // AfterMappingMethodRef/BeforeMappingMethodRef/IsFluentForward 修复前不参与
        // Equals/GetHashCode，孪生断言一/二红）----
        yield return new ConservationCase(
            nameof(MappingInfo),
            () => new MappingInfo
            {
                SourceType = typeA,
                TargetType = typeB,
                SourceClassName = "Src",
                TargetClassName = "Dto",
            },
            new[]
            {
                new Mutation("SourceClassName", () => "Src2"),
                new Mutation("TargetClassName", () => "Dto2"),
                new Mutation("SourceType", () => typeB),
                new Mutation("TargetType", () => typeA),
                new Mutation("SkipCodeGeneration", () => true),
                new Mutation("Diagnostics", () => new List<DiagnosticInfo> { MakeDiagnostic(ruleA) }, HashExempt: true),
                new Mutation("AllTargetProperties", () => new List<IPropertySymbol> { propA }, HashExempt: true),
                new Mutation("DiagnosticOptions", () => new DiagnosticOptions
                {
                    ReportUnmappedTargetMembers = true,
                    ReportUnmappedSourceMembers = false,
                }),
                new Mutation("PropertyMappings", () => new List<PropertyMapping> { new() { SourceProperty = propA, TargetProperty = propA } }, HashExempt: true),
                new Mutation("ComposeMappings", () => new List<ComposePropertyMapping> { new() { SourceProperty = propA, TargetProperty = propA } }, HashExempt: true),
                new Mutation("ReverseOnlyConvertBacks", () => new List<PropertyMapping> { new() { SourceProperty = propA, TargetProperty = propA } }, HashExempt: true),
                // R-10 四字段（B5-4）：修复前不参与 Equals/GetHashCode——孪生断言一/二红
                new Mutation("NoAutoInclude", () => true),
                new Mutation("AfterMappingMethodRef", () => "Ns.Mapper.HookA"),
                new Mutation("BeforeMappingMethodRef", () => "Ns.Mapper.HookB"),
                new Mutation("IsFluentForward", () => true),
            });

        // ---- 8. DiagnosticInfo（符号态；B5-4 B4-07——IsExplicit 修复前不参与
        // Equals/GetHashCode，孪生断言一/二红；Args null 防护见 CacheKeyEquivalenceTests）----
        yield return new ConservationCase(
            nameof(DiagnosticInfo),
            () => new DiagnosticInfo
            {
                Descriptor = ruleA,
                Args = new object[] { "a" },
                Location = null,
                IsExplicit = false,
            },
            new[]
            {
                new Mutation("Descriptor", () => ruleB),
                new Mutation("Args", () => new object[] { "b" }, HashExempt: true),
                new Mutation("Location", () => Location.None),
                // B4-07：修复前不参与 Equals/GetHashCode——孪生断言一/二红
                new Mutation("IsExplicit", () => true),
            });
    }

    // ------------------------------------------------------------------
    // 断言一：孪生变异守恒——Equals 恒 false（B4-13 漏项字段修复前亦参与 Equals，不红；
    // 守恒目标家族 = "Equals 补了字段但 GetHashCode 漏"的 002 缺陷族 + 新字段漏参与两断言面）
    // ------------------------------------------------------------------

    [Fact]
    public void TwinMutation_Equals_False_ForEveryRegisteredField()
    {
        foreach (var c in Registry())
        foreach (var m in c.Mutations)
        {
            var baseInstance = c.BaseFactory();
            var twin = c.BaseFactory();
            SetProperty(twin, m.Field, m.TwinValue());
            Assert.False(baseInstance.Equals(twin),
                $"{c.TypeName}.{m.Field}: 孪生对 Equals 必须 false——字段参与相等性守恒面");
        }
    }

    // ------------------------------------------------------------------
    // 断言二：哈希差异确定性——可哈希标量字段变异后 GetHashCode 必须改变
    // （B4-13 已知漏项修复前此断言红——V4 先红后绿；集合族豁免，002-P1-1 定案）
    // ------------------------------------------------------------------

    [Fact]
    public void TwinMutation_GetHashCode_Changes_ForHashableScalars()
    {
        foreach (var c in Registry())
        foreach (var m in c.Mutations)
        {
            if (m.HashExempt) continue;
            var baseInstance = c.BaseFactory();
            var twin = c.BaseFactory();
            SetProperty(twin, m.Field, m.TwinValue());
            // xunit v3 无 (T,T,string) 重载——以 Assert.True 承载上下文消息
            Assert.True(baseInstance.GetHashCode() != twin.GetHashCode(),
                $"{c.TypeName}.{m.Field}: 变异后 GetHashCode 必须改变——字段参与哈希（B4-13 守恒）");
        }
    }

    // ------------------------------------------------------------------
    // 断言三（002-P1-1 契约拦截器）：Equals 相等孪生对（同字段同值、集合独立构造）
    // GetHashCode 必须相等——朴素引用哈希实现在此直接红
    // ------------------------------------------------------------------

    [Fact]
    public void EqualTwins_IndependentlyConstructed_GetHashCode_Equal()
    {
        foreach (var c in Registry())
        {
            var a = c.BaseFactory();
            var b = c.BaseFactory();
            Assert.True(a.Equals(b), $"{c.TypeName}: 基准工厂两次独立构造必须判等（fixture 自证）");
            Assert.True(a.GetHashCode() == b.GetHashCode(),
                $"{c.TypeName}: Equals 相等 ⇒ GetHashCode 相等（哈希契约；集合独立构造下引用哈希在此红）");
        }
    }

    // ------------------------------------------------------------------
    // 断言四（注册完整性自证）：变异字段数 ≥ Equals 参与字段数的注册底线——
    // 反射枚举全部 init/set 属性与注册变异清单对账（derived get-only / 非语义属性除外），
    // 防注册表漂移（漏注册字段 = 守恒盲区）
    // ------------------------------------------------------------------

    [Fact]
    public void Registry_Covers_All_Settable_Semantic_Properties()
    {
        // 各类型 get-only 派生属性（表达式体，不参与 Equals/GetHashCode——豁免对账）
        var derivedExempt = new Dictionary<string, string[]>
        {
            [nameof(PropertyMappingSnapshot)] = new[] { "HasIndex", "IsReverseWriteBack" },
        };
        // Equals/GetHashCode 均不参与的 settable 属性（符号态输入，物化期消费——值态快照才参与对账）
        // B5-4，PropertyMapping.FluentSourceIgnoreDirection 曾注册为变异——字段随退役移除
        // ——Equals/GetHashCode 补齐随修复 commit 落地（X2：SnapshotConservationTests 排除集同 commit
        // 移出）；MappingInfo 七项为转嫁排除面（值层剥离/跨 Compilation 不可比/物化后子模型值相等性
        // 覆盖——与 SnapshotConservationTests.EqualsExcluded 同口径）
        var equalityExempt = new Dictionary<string, string[]>
        {
            [nameof(MappingInfo)] = new[]
            {
                "MethodSuffix", "TargetSyntax", "ForLocation", "TreeConfig", "PropertyNoAutoIncludeTargets",
                "HasComposeMappings", "SubclassPairs", "CtorParamBindings",
            },
        };

        foreach (var c in Registry())
        {
            var sample = c.BaseFactory();
            var settableProps = sample.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 0)
                .Where(p => p.CanWrite || p.SetMethod != null)
                .Select(p => p.Name)
                .ToHashSet(StringComparer.Ordinal);
            var registered = c.Mutations.Select(m => m.Field).ToHashSet(StringComparer.Ordinal);

            var unregistered = settableProps
                .Where(n => !registered.Contains(n))
                .Where(n => !equalityExempt.TryGetValue(c.TypeName, out var exempt) || !exempt.Contains(n))
                .ToList();
            Assert.True(unregistered.Count == 0,
                $"{c.TypeName}: 未注册变异的 settable 属性（新增字段须入注册表——守恒盲区防线）: {string.Join(", ", unregistered)}");
            var phantom = registered.Where(n => !settableProps.Contains(n)).ToList();
            Assert.True(phantom.Count == 0,
                $"{c.TypeName}: 注册了不存在的 settable 属性（形态漂移须同步注册表）: {string.Join(", ", phantom)}");

            if (derivedExempt.TryGetValue(c.TypeName, out var exempt))
            {
                Assert.True(exempt.All(n => !settableProps.Contains(n)),
                    $"{c.TypeName}: 派生豁免清单含已消失属性——须同步本测试");
            }

            if (equalityExempt.TryGetValue(c.TypeName, out var eqExempt))
            {
                Assert.True(eqExempt.All(n => settableProps.Contains(n)),
                    $"{c.TypeName}: 相等性豁免清单含已消失属性——须同步本测试");
            }
        }
    }
}
