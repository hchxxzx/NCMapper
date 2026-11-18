using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NCMapper.SourceGen;
using Xunit;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// 快照重建守恒测试（W1）——快照不可变重建
/// "透传完整性"机械防线：反射枚举 <see cref="MappingSnapshot"/> 全部 public 实例属性，
/// 与各重建类初始化器的源码赋值集比对——新增属性未入白名单且未透传即红。缺陷家族
/// （007 缺陷② → 008 → 010 → 5464683 → 方案书评审 002 B-1）四次复现的"人工记得加"模式防线化。
/// <para>扫描面（v1.2 评审 A2/P1-1 订正）：三处重建类初始化器——
/// ① <c>ApplyToSnapshot</c>（MappingExtractor.SnapshotConfig.cs，全透传 + 修正语义白名单 5 项）；
/// ② <c>DualDriverArbiter.Suppress</c>（SkipCodeGeneration 定值 + Diagnostics 追加语义白名单）；
/// ③ <c>ReversePlanner.ApplyReverseGlobalIgnoreFilter</c>（部分透传：PropertyMappings 替换 +
/// 14 项反向恒缺省——430 反向全新构造不赋值者）。</para>
/// <para>Arbiter 合并臂不存在重建初始化器（MergeAndArbitrate :45/:88 直接引用归并，无赋值集需守恒）；
/// Materialize.cs:24 主物化与 ReversePlanner.cs:430 反向全新构造豁免扫描（值来源为上游信息，
/// 非透传语义——豁免理由登记）。</para>
/// </summary>
public class SnapshotConservationTests
{
    /// <summary>
    /// 反射枚举 MappingSnapshot 全部 public 实例属性名（新增字段自动纳入比对集——
    /// 白名单语义见各测试；indexer 排除）。
    /// </summary>
    private static SortedSet<string> ReflectedPropertyNames() =>
        new(typeof(MappingSnapshot).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0)
            .Select(p => p.Name), StringComparer.Ordinal);

    /// <summary>
    /// 源码扫描：从方法锚点起定位第一个 <c>new MappingSnapshot { ... }</c> 初始化器，
    /// 提取顶层赋值属性名（缩进锚定 = 对象创建行缩进 + 4；闭合行缩进 = 对象创建行缩进）。
    /// 区间锚点漂移（方法签名改名/缩进重构）时以断言失败暴露，测试须同步修订。
    /// </summary>
    private static SortedSet<string> ExtractAssignedNames(string source, string methodAnchor, string filePath)
    {
        var methodStart = source.IndexOf(methodAnchor, StringComparison.Ordinal);
        Assert.True(methodStart >= 0, $"{filePath}: 未找到方法锚点 \"{methodAnchor}\"——源码重构须同步测试扫描锚点");
        var ctorIdx = source.IndexOf("new MappingSnapshot", methodStart, StringComparison.Ordinal);
        Assert.True(ctorIdx > 0, $"{filePath}: 方法区间内未找到 new MappingSnapshot 初始化器——重建点形态变化须同步测试");
        var lineStart = source.LastIndexOf('\n', ctorIdx) + 1;
        var createIndent = 0;
        while (lineStart + createIndent < source.Length && source[lineStart + createIndent] == ' ') createIndent++;
        var braceIdx = source.IndexOf('{', ctorIdx);
        Assert.True(braceIdx > 0, $"{filePath}: 初始化器左花括号定位失败");
        var result = new SortedSet<string>(StringComparer.Ordinal);
        var pos = braceIdx + 1;
        while (pos < source.Length)
        {
            var lineEnd = source.IndexOf('\n', pos);
            if (lineEnd < 0) lineEnd = source.Length;
            var line = source[pos..lineEnd].TrimEnd('\r');
            var trimmed = line.TrimStart();
            var indent = line.Length - trimmed.Length;
            if (indent == createIndent && trimmed.StartsWith("}")) break;
            var m = Regex.Match(trimmed, "^([A-Za-z_][A-Za-z0-9_]*)\\s*=");
            if (m.Success && indent == createIndent + 4) result.Add(m.Groups[1].Value);
            pos = lineEnd + 1;
        }
        Assert.True(result.Count > 0, $"{filePath}: 初始化器赋值提取为空——扫描区间/缩进锚点失效");
        return result;
    }

    /// <summary>
    /// 守恒断言四段：① 赋值集 ⊆ 反射集（防正则误匹配——扫描器自证）；
    /// ② 反射集 − 两类白名单 ⊆ 赋值集（守恒主断言——新增字段漏透传即红）；
    /// ③ mustAssign（修正/替换/定值语义——必须显式赋值）⊆ 赋值集，漏写即红；
    /// ④ mayOmit（恒缺省语义——必须不赋值）∩ 赋值集 = ∅，出现即红（构造面演进须同步清单）。
    /// </summary>
    private static void AssertConservation(string site, SortedSet<string> reflected, SortedSet<string> assigned, string[] mustAssign, string[] mayOmit)
    {
        var unknown = assigned.Where(n => !reflected.Contains(n)).ToList();
        Assert.True(unknown.Count == 0,
            $"{site}: 扫描到非 MappingSnapshot 属性的赋值名（初始化器形态漂移，扫描器须修订）: {string.Join(", ", unknown)}");
        var missing = reflected.Where(n => !mustAssign.Contains(n) && !mayOmit.Contains(n) && !assigned.Contains(n)).ToList();
        Assert.True(missing.Count == 0,
            $"{site}: 重建初始化器漏透传字段（新增快照字段须在重建点显式决策透传或白名单登记——缺陷家族 007缺陷②→008→010→5464683→B-1 机械防线）: {string.Join(", ", missing)}");
        var missingMust = mustAssign.Where(n => !assigned.Contains(n)).ToList();
        Assert.True(missingMust.Count == 0,
            $"{site}: 须赋值白名单字段（修正/替换/定值语义）未出现在初始化器赋值集: {string.Join(", ", missingMust)}");
        var unexpected = mayOmit.Where(n => assigned.Contains(n)).ToList();
        Assert.True(unexpected.Count == 0,
            $"{site}: 恒缺省白名单字段出现在赋值集（构造面已演进——须同步本测试白名单清单）: {string.Join(", ", unexpected)}");
    }

    private static string ReadSourceGenSource(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            // 以 NCMapper.slnx 作为仓库根标记（MainFileWeightBudgetTests.FindMainFilePath 同模式）
            if (File.Exists(Path.Combine(dir.FullName, "NCMapper.slnx")))
            {
                var path = Path.Combine(dir.FullName, "src", "NCMapper.SourceGen", relativePath);
                Assert.True(File.Exists(path), $"源文件缺失: {path}");
                return File.ReadAllText(path);
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException("无法定位仓库根（未找到 NCMapper.slnx）——守恒测试须在仓库内运行");
    }

    /// <summary>
    /// ① ApplyToSnapshot（ApplyGlobalConfig 修正管线）——全透传语义；
    /// 修正语义白名单 5 项（PropertyMappings/ComposeMappings/Diagnostics/
    /// ReportUnmappedTargetMembers/ReportUnmappedSourceMembers 重建时合法改值）。
    /// 已知漏项（先红锚点）：HasClassLevelDiagnosticOptions（SnapshotConfig.cs:67-114
    /// 初始化器无此项，重建后恒 false——75/:78 消费符号态 info.* 掩盖了缺陷）。
    /// </summary>
    [Fact]
    public void ApplyToSnapshot_Initializer_PassesThroughAllProperties()
    {
        var assigned = ExtractAssignedNames(
            ReadSourceGenSource("MappingExtractor.SnapshotConfig.cs"),
            "private static MappingSnapshot ApplyToSnapshot(",
            "MappingExtractor.SnapshotConfig.cs");
        AssertConservation("ApplyToSnapshot", ReflectedPropertyNames(), assigned,
        [
            "PropertyMappings", "ComposeMappings", "Diagnostics",
            "ReportUnmappedTargetMembers", "ReportUnmappedSourceMembers",
        ], []);
    }

    /// <summary>
    /// ② DualDriverArbiter.Suppress 抑制臂（SkipCodeGeneration=true 的不可变重建）——
    /// 白名单：SkipCodeGeneration（定值 true，非透传）、Diagnostics（追加语义，可批量并入 F11/F39）。
    /// 已知漏项（先红锚点，方案书评审 002 B-1）：IsFluentForward/TreeConfig/
    /// PropertyNoAutoIncludeTargets/HasComposeMappings/SubclassPairs 五后加字段
    /// （透传止于 BeforeMappingMethodRef :291，008 评审只补一个、后续未跟进）。
    /// </summary>
    [Fact]
    public void SuppressArm_Initializer_PassesThroughAllProperties()
    {
        var assigned = ExtractAssignedNames(
            ReadSourceGenSource("DualDriverArbiter.cs"),
            "private static MappingSnapshot Suppress(",
            "DualDriverArbiter.cs");
        AssertConservation("DualDriverArbiter.Suppress", ReflectedPropertyNames(), assigned,
        [
            "SkipCodeGeneration", "Diagnostics",
        ], []);
    }

    /// <summary>
    /// ③ ReversePlanner.ApplyReverseGlobalIgnoreFilter（502，部分透传重建——注释自述
    /// "仅替换 PropertyMappings；其余字段透传"实传 11 字段）。白名单 = 替换字段
    /// PropertyMappings + 14 项反向恒缺省（430 CreateReverseMapping 反向全新构造
    /// 不赋值者——当前无损：:430 构造集 11 = :502 透传集 11）。新增属性未入白名单
    /// 且未赋值即红——防未来反向侧字段演进重蹈缺陷家族覆辙。
    /// <para>NoAutoInclude 由恒缺省转透传（反向快照带位），同批移出 mayOmit
    /// 清单——断言 :88-90"恒缺省字段出现在赋值集"即触发红（触发-修复同批，B2 适用注登记面同步）。</para>
    /// </summary>
    [Fact]
    public void ApplyReverseGlobalIgnoreFilter_Initializer_CoversRequiredProperties()
    {
        var assigned = ExtractAssignedNames(
            ReadSourceGenSource("ReversePlanner.cs"),
            "MappingSnapshot? ApplyReverseGlobalIgnoreFilter(",
            "ReversePlanner.cs");
        AssertConservation("ReversePlanner.ApplyReverseGlobalIgnoreFilter", ReflectedPropertyNames(), assigned,
        [
            // 替换语义（必须显式赋值）
            "PropertyMappings",
        ],
        [
            // 恒缺省集（430 全新构造不赋值——对账 MappingSnapshot 25 属性 = :430 构造 11 + 缺省 14；
            // 出现在 :502 赋值集即红——构造面演进须同步本清单。实数核写：
            // 原注释"24 属性 = 构造 10 + 缺省 14"与清单实 15 项失实，NoAutoInclude 转透传后清单实 14）
            "HasClassLevelDiagnosticOptions", "AllTargetProperties", "DeepCopyNodes",
            "ComposeMappings", "ReverseOnlyConvertBacks", "TargetIdentifierLocation",
            "ForInvocationLocation", "AfterMappingMethodRef", "BeforeMappingMethodRef",
            "IsFluentForward", "TreeConfig", "PropertyNoAutoIncludeTargets",
            "HasComposeMappings", "SubclassPairs",
        ]);
    }

    // ================================================================
    // B4-10：16 类型 Equals/GetHashCode 字段覆盖守恒——
    // 手写 Equals 体的字段覆盖无法由反射得出（任务书评审 002 P2-5），断言机制（执行首步
    // 裁定登记）= 源码扫描段：净化文本（去注释/字符串字面量）上提取 Equals(类型化)/
    // GetHashCode 方法体，词级匹配属性名，与期望登记表比对。
    // 首步实证产物（2026-09-19）：已一致者入断言面；不一致者按「排除出断言面（登记
    // 理由）/ 修复项回流」二分处置——完整 16 类型逐字段对照表登记。
    // 口径注记：两目标文件（Models.cs / Models.Snapshots.cs）的 Equals/GHC 方法体内
    // 无插值字符串依赖（比较逻辑均为 ==/Equals/GetHashCode 调用），字符串整体跳过
    // 不影响字段覆盖判定；表达式体（=>）与块体（{ }）双形态均支持。
    // ================================================================

    /// <summary>
    /// 字段覆盖期望登记——EqualsCover/EqualsExcluded/GhcCover 为显式集；
    /// GhcExcluded = 数据属性 − GhcCover（断言内派生）；ComputedProps = 表达式体
    /// 计算属性（派生自其他字段，不参与数据字段守恒，反射全集中显式排除）。
    /// </summary>
    // 2026-10-11 元数据一致性调查（跨轴蕴含断言 A4/A5）：private → internal——
    // SnapshotFieldRegistryTests 跨轴锁定消费（L1 Compared ⊆ EqualsCover），
    // 测试程序集内可见性放宽，无行为影响
    internal sealed record FieldCoverageExpectation(
        string SourceFile,
        string TypeName,
        string[] EqualsCover,
        string[] EqualsExcluded,
        string[] GhcCover,
        string[] ComputedProps);

    /// <summary>
    /// 16 类型期望登记（首步实证固化）。EqualsExcluded 登记理由见对照表：
    /// TypeRef 31 项 = 单键相等设计（EqualityKey，:33-34 注释在案）；MappingInfo 7 项 =
    /// MethodSuffix（类型名派生）/TargetSyntax（语法节点跨 Compilation 不可比）安全排除 +
    /// TreeConfig/PropertyNoAutoIncludeTargets/HasComposeMappings/SubclassPairs/CtorParamBindings
    /// 注释在案的转嫁排除；原缓存陈旧面修复项回流登记（R 清单）四字段 +
    /// PropertyMapping.FluentSourceIgnoreDirection（T4 退役移除）/ DiagnosticInfo.IsExplicit 已随
    /// B5-4（R-10/B4-06/B4-07）修复转正入覆盖面（X2 同 commit 移出）。
    /// </summary>
    internal static readonly FieldCoverageExpectation[] FieldCoverageExpectations =
    [
        // ---- 值族 12（Models.Snapshots.cs）----
        new("Models.Snapshots.cs", "TypeRef",
            ["EqualityKey"],
            ["FullName", "NullableAnnotation", "MinimallyQualifiedName", "AssemblyName",
             "NonNullableFullName", "SimpleName", "Namespace", "MetadataName", "Kind",
             "Accessibility", "SpecialType", "IsValueType", "IsReferenceType", "IsGenericType",
             "IsNullableValueType", "UnderlyingType", "IsArray", "ElementType",
             "ImplementsIEnumerable", "IsDictionary", "EnumerableElementType",
             "DictionaryKeyType", "DictionaryValueType", "TypeArguments", "EnumMembers",
             "Constructors", "HasPublicParameterlessCtor", "IsSealed", "IsAbstract",
             "IsDelegate", "MapFromSources",
             // B5-4 B1-06：基类链为 compose 子源拓扑排序的
             // 发射显示派生字段——同类型基类链恒同源，单键相等设计不参与 EqualityKey（同族理由）
             "BaseTypeChainFullNames"],
            ["EqualityKey"], []),
        new("Models.Snapshots.cs", "ConstructorParameterRef",
            ["Name", "Type", "HasExplicitDefaultValue", "ExplicitDefaultValue", "SourcePropertyName"],
            [], ["Name", "Type", "HasExplicitDefaultValue", "ExplicitDefaultValue", "SourcePropertyName"], []),
        new("Models.Snapshots.cs", "ConstructorRef",
            ["Parameters", "IsPublic"], [], ["Parameters", "IsPublic"], []),
        new("Models.Snapshots.cs", "PropertyRef",
            ["Name", "Type", "IsStatic", "IsIndexer", "DeclaredAccessibility",
             "GetMethodAccessibility", "SetMethodAccessibility", "IsInitOnly",
             "IsImplicitlyDeclared", "ContainingType", "Location", "HasMapIgnore",
             "MapIgnoreDirection", "HasMapProperty", "IsFromJson", "HasOverrideIgnore",
             "HasConvertUsing", "ConverterDeclaringType", "ConverterMethodName", "ConverterShape",
             "HasConvertBackUsing", "ConverterBackDeclaringType", "ConverterBackMethodName",
             "ConverterBackShape", "ConverterBackMethodBody", "ConverterBackMethodParamTypes",
             "ConverterBackMethodParamNames", "HasMask", "HasMapFormat", "MaskType",
             "MapPropertyFromField", "MapPropertyFromSource", "HasMapNoAutoInclude", "HasFluentExplicit"],
            [],
            ["Name", "Type", "IsInitOnly", "SetMethodAccessibility", "HasMapIgnore",
             "HasConvertUsing", "HasMask", "HasMapFormat"], []),
        new("Models.Snapshots.cs", "PropertyMappingSnapshot",
            ["Target", "Source", "SourcePath", "SourceIndexes", "ReverseWritePath",
             "IsFlattened", "PathOrigin", "ConverterDeclaringType", "ConverterMethodName",
             "ConverterShape", "MaskType", "FormatString", "IsFromJson", "ConditionMethodRef",
             "ConditionMethodBody", "ConditionMethodParamName", "ConditionShape",
             "ConverterMethodBody", "ConverterMethodParamTypes", "ConverterMethodParamNames"],
            [],
            ["Target", "Source", "SourcePath", "SourceIndexes", "ReverseWritePath",
             "IsFlattened", "PathOrigin", "ConverterDeclaringType", "ConverterMethodName",
             "ConverterShape", "MaskType", "FormatString", "IsFromJson", "ConditionMethodRef",
             "ConditionMethodBody", "ConditionMethodParamName", "ConditionShape",
             "ConverterMethodBody", "ConverterMethodParamTypes", "ConverterMethodParamNames"],
            ["HasIndex", "IsReverseWriteBack"]),
        new("Models.Snapshots.cs", "SubclassPairSnapshot",
            ["SourceType", "TargetType", "SourceClassName", "TargetClassName"], [],
            ["SourceType", "TargetType", "SourceClassName", "TargetClassName"], []),
        new("Models.Snapshots.cs", "ComposeMappingSnapshot",
            ["SubSource", "Source", "SourcePath", "SourceIndexes", "Target",
             "ConverterDeclaringType", "ConverterMethodName", "ConverterShape", "MaskType",
             "FormatString", "IsFromJson", "ConditionMethodRef", "ConditionMethodBody",
             "ConditionMethodParamName", "ConditionShape", "ConverterMethodBody",
             "ConverterMethodParamTypes", "ConverterMethodParamNames"],
            [],
            ["SubSource", "Source", "SourcePath", "SourceIndexes", "Target",
             "ConverterDeclaringType", "ConverterMethodName", "ConverterShape", "MaskType",
             "FormatString", "IsFromJson", "ConditionMethodRef", "ConditionMethodBody",
             "ConditionMethodParamName", "ConditionShape", "ConverterMethodBody",
             "ConverterMethodParamTypes", "ConverterMethodParamNames"],
            ["HasIndex"]),
        new("Models.Snapshots.cs", "LocationSnapshot",
            ["FilePath", "Span", "LineSpan"], [], ["FilePath", "Span"], []),
        new("Models.Snapshots.cs", "DiagnosticSnapshot",
            ["DescriptorId", "Args", "Location", "IsExplicit"], [],
            ["DescriptorId", "Args", "Location"], []),
        new("Models.Snapshots.cs", "DeepCopyPropertyShape",
            ["ContainingTypeIsSealed", "GetterIsPublic", "GetterIsSealed", "GetterIsVirtual",
             "HasMapIgnore", "HasSetter", "Location", "Name", "SetterIsInitOnly",
             "SetterIsPublic", "Type"],
            [],
            ["GetterIsSealed", "GetterIsVirtual", "Name", "SetterIsInitOnly", "Type"], []),
        new("Models.Snapshots.cs", "DeepCopyNodeShape",
            ["ArrayElementType", "ElementType", "HasPublicParameterlessCtor", "IsArray",
             "IsCollectionLike", "IsDelegate", "IsDictionary", "IsInterface", "IsLeaf",
             "IsNonGenericEnumerable", "IsNullableValueType", "IsPointer", "IsString",
             "IsTypeParameter", "IsValueType", "KeyType", "Location", "Properties",
             "PublicConstructors", "Type", "UnderlyingType", "ValueType"],
            [],
            ["ArrayElementType", "ElementType", "HasPublicParameterlessCtor", "IsArray",
             "IsCollectionLike", "IsDelegate", "IsDictionary", "IsInterface", "IsLeaf",
             "IsNonGenericEnumerable", "IsNullableValueType", "IsPointer", "IsString",
             "IsTypeParameter", "IsValueType", "KeyType", "Location", "Properties",
             "PublicConstructors", "Type", "UnderlyingType", "ValueType"], []),
        new("Models.Snapshots.cs", "MappingSnapshot",
            ["SourceType", "TargetType", "SourceClassName", "TargetClassName",
             "SkipCodeGeneration", "ReportUnmappedTargetMembers", "ReportUnmappedSourceMembers",
             "HasClassLevelDiagnosticOptions", "PropertyMappings", "AllTargetProperties",
             "SourceProperties", "DeepCopyNodes", "ComposeMappings", "ReverseOnlyConvertBacks",
             "Diagnostics", "TargetIdentifierLocation", "ForInvocationLocation",
             "NoAutoInclude", "AfterMappingMethodRef",
             "BeforeMappingMethodRef", "IsFluentForward", "TreeConfig",
             "PropertyNoAutoIncludeTargets", "HasComposeMappings", "SubclassPairs"],
            [],
            ["SourceType", "TargetType", "SourceClassName", "TargetClassName",
             "SkipCodeGeneration", "ReportUnmappedTargetMembers", "ReportUnmappedSourceMembers",
             "HasClassLevelDiagnosticOptions", "PropertyMappings", "AllTargetProperties",
             "SourceProperties", "DeepCopyNodes", "ComposeMappings", "ReverseOnlyConvertBacks",
             "Diagnostics", "TargetIdentifierLocation", "ForInvocationLocation",
             "NoAutoInclude", "AfterMappingMethodRef",
             "BeforeMappingMethodRef", "IsFluentForward", "TreeConfig",
             "PropertyNoAutoIncludeTargets", "HasComposeMappings", "SubclassPairs"], []),
        // ---- 符号族 4（Models.cs）----
        new("Models.cs", "DiagnosticInfo",
            ["Descriptor", "Args", "Location", "IsExplicit"], [],
            ["Descriptor", "Args", "Location", "IsExplicit"], []),
        new("Models.cs", "MappingInfo",
            ["SourceClassName", "TargetClassName", "SourceType", "TargetType",
             "SkipCodeGeneration", "Diagnostics", "AllTargetProperties", "DiagnosticOptions",
             "PropertyMappings", "ComposeMappings", "ReverseOnlyConvertBacks",
             // B5-4（R-10）：四字段随修复转正入覆盖面（X2 同 commit 移出）
             "NoAutoInclude", "AfterMappingMethodRef", "BeforeMappingMethodRef", "IsFluentForward"],
            ["MethodSuffix", "TargetSyntax", "ForLocation", "TreeConfig",
             "PropertyNoAutoIncludeTargets", "HasComposeMappings", "SubclassPairs",
             "CtorParamBindings"],
            ["SourceClassName", "TargetClassName", "SourceType", "TargetType",
             "SkipCodeGeneration", "Diagnostics", "AllTargetProperties", "DiagnosticOptions",
             "PropertyMappings", "ComposeMappings", "ReverseOnlyConvertBacks",
             "NoAutoInclude", "AfterMappingMethodRef", "BeforeMappingMethodRef", "IsFluentForward"], []),
        new("Models.cs", "PropertyMapping",
            ["SourceProperty", "TargetProperty", "SourcePath", "SourceIndexes", "IsFlattened",
             "PathOrigin", "ConverterDeclaringType", "ConverterMethodName", "ConverterShape",
             "BackConverterDeclaringType", "BackConverterMethodName", "BackConverterShape",
             "MaskType", "FormatString", "IsFromJson", "FluentOverrideIgnore", "FluentIgnoreDirection",
             "HasFluentExplicit", "ConditionMethodRef", "ConditionMethodBody",
             "ConditionMethodParamName", "ConditionShape", "ConverterMethodBody",
             "ConverterMethodParamTypes", "ConverterMethodParamNames",
             "BackConverterMethodBody", "BackConverterMethodParamTypes",
             "BackConverterMethodParamNames"],
            // FluentSourceIgnoreDirection 随字段退役移除
            [],
            ["SourceProperty", "TargetProperty", "SourcePath", "SourceIndexes", "IsFlattened",
             "PathOrigin", "ConverterDeclaringType", "ConverterMethodName", "ConverterShape",
             "BackConverterDeclaringType", "BackConverterMethodName", "BackConverterShape",
             "MaskType", "FormatString", "IsFromJson", "FluentOverrideIgnore", "FluentIgnoreDirection",
             "HasFluentExplicit", "ConditionMethodRef", "ConditionMethodBody",
             "ConditionMethodParamName", "ConditionShape", "ConverterMethodBody",
             "ConverterMethodParamTypes", "ConverterMethodParamNames",
             "BackConverterMethodBody", "BackConverterMethodParamTypes",
             "BackConverterMethodParamNames"],
            ["HasIndex"]),
        new("Models.cs", "ComposePropertyMapping",
            ["SubSourceType", "SourceProperty", "SourcePath", "SourceIndexes", "TargetProperty",
             "ConverterDeclaringType", "ConverterMethodName", "ConverterShape", "MaskType",
             "FormatString", "IsFromJson", "ConditionMethodRef", "ConditionMethodBody",
             "ConditionMethodParamName", "ConditionShape", "ConverterMethodBody",
             "ConverterMethodParamTypes", "ConverterMethodParamNames"],
            [],
            ["SubSourceType", "SourceProperty", "SourcePath", "SourceIndexes", "TargetProperty",
             "ConverterDeclaringType", "ConverterMethodName", "ConverterShape", "MaskType",
             "FormatString", "IsFromJson", "ConditionMethodRef", "ConditionMethodBody",
             "ConditionMethodParamName", "ConditionShape", "ConverterMethodBody",
             "ConverterMethodParamTypes", "ConverterMethodParamNames"],
            ["HasIndex"]),
    ];

    /// <summary>
    /// 净化源码：注释与字符串/字符字面量内容替换为空格（保留长度与结构字符）——
    /// 净化文本上的花括号/圆括号/分号均为真实代码结构，方法体提取可做纯深度配对。
    /// </summary>
    private static string SanitizeSource(string source)
    {
        var chars = source.ToCharArray();
        int i = 0;
        while (i < chars.Length)
        {
            char c = chars[i];
            if (c == '/' && i + 1 < chars.Length && chars[i + 1] == '/')
            {
                while (i < chars.Length && chars[i] != '\n') chars[i++] = ' ';
            }
            else if (c == '/' && i + 1 < chars.Length && chars[i + 1] == '*')
            {
                chars[i++] = ' '; chars[i++] = ' ';
                while (i < chars.Length && !(chars[i] == '*' && i + 1 < chars.Length && chars[i + 1] == '/'))
                    chars[i++] = ' ';
                if (i < chars.Length) { chars[i++] = ' '; if (i < chars.Length) chars[i++] = ' '; }
            }
            else if (c == '"')
            {
                bool verbatim = i > 0 && chars[i - 1] == '@';
                chars[i++] = ' ';
                while (i < chars.Length)
                {
                    if (verbatim)
                    {
                        if (chars[i] == '"' && (i + 1 >= chars.Length || chars[i + 1] != '"')) { chars[i++] = ' '; break; }
                        chars[i++] = ' ';
                    }
                    else
                    {
                        if (chars[i] == '\\') { chars[i++] = ' '; if (i < chars.Length) chars[i++] = ' '; }
                        else if (chars[i] == '"') { chars[i++] = ' '; break; }
                        else chars[i++] = ' ';
                    }
                }
            }
            else if (c == '\'')
            {
                chars[i++] = ' ';
                while (i < chars.Length && chars[i] != '\'')
                {
                    if (chars[i] == '\\') chars[i++] = ' ';
                    if (i < chars.Length) chars[i++] = ' ';
                }
                if (i < chars.Length) chars[i++] = ' ';
            }
            else i++;
        }
        return new string(chars);
    }

    /// <summary>
    /// 反射枚举指定类型全部 public 实例属性（排除索引器；含 get-only 数据属性与
    /// 表达式体计算属性——后者经 ComputedProps 登记排除）。
    /// </summary>
    private static SortedSet<string> ReflectedPropertyNames(Type type) =>
        new(type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0 && p.GetMethod != null)
            .Select(p => p.Name), StringComparer.Ordinal);

    /// <summary>
    /// 净化文本上提取方法体：在类型体区间内定位签名（词边界防前缀误配），参数圆括号
    /// 配对结束后，块体（{ 深度配对 }）或表达式体（=&gt; 至顶层）双形态提取。
    /// </summary>
    private static string ExtractSanitizedMethodBody(string sanitized, string typeBody, string signatureRegex)
    {
        var match = Regex.Match(typeBody, signatureRegex);
        Assert.True(match.Success, $"方法签名定位失败: {signatureRegex}——源码重构须同步测试扫描锚点");
        // 参数列表圆括号配对（从签名内第一个 '(' 起）
        int i = typeBody.IndexOf('(', match.Index);
        int depth = 0;
        for (; i < typeBody.Length; i++)
        {
            if (typeBody[i] == '(') depth++;
            else if (typeBody[i] == ')') { depth--; if (depth == 0) { i++; break; } }
        }
        // 跳过空白与换行，定位块体或表达式体
        while (i < typeBody.Length && char.IsWhiteSpace(typeBody[i])) i++;
        var body = new System.Text.StringBuilder();
        if (i < typeBody.Length && typeBody[i] == '{')
        {
            depth = 0;
            for (; i < typeBody.Length; i++)
            {
                body.Append(typeBody[i]);
                if (typeBody[i] == '{') depth++;
                else if (typeBody[i] == '}')
                {
                    depth--;
                    if (depth == 0) break;
                }
            }
        }
        else if (i + 1 < typeBody.Length && typeBody[i] == '=' && typeBody[i + 1] == '>')
        {
            for (; i < typeBody.Length; i++)
            {
                if (typeBody[i] == ';') break;
                body.Append(typeBody[i]);
            }
        }
        else
        {
            Assert.Fail($"方法体形态非预期（既非块体也非表达式体）: {signatureRegex}");
        }
        var result = body.ToString();
        Assert.True(result.Length > 0, $"方法体提取为空: {signatureRegex}");
        return result;
    }

    /// <summary>词级出现判定（标识符边界——防前缀/后缀误配）。</summary>
    private static bool WordOccurs(string body, string identifier) =>
        Regex.IsMatch(body, @"(?<![A-Za-z0-9_])" + Regex.Escape(identifier) + @"(?![A-Za-z0-9_])");

    /// <summary>
    /// B4-10 主断言（数据驱动单 Fact，失败消息含类型名与差集/实际集——定位即修订）：
    /// ① 反射全集 == EqualsCover ∪ EqualsExcluded ∪ ComputedProps（新增属性强制显式登记）；
    /// ② EqualsCover ⊆ 实际 Equals 体（删比较字段即红）；
    /// ③ EqualsExcluded ∩ 实际 Equals 体 = ∅（排除登记过时即红——须同步修订登记与理由）；
    /// ④ GhcCover ⊆ 实际 GHC 体；数据属性 − GhcCover（GhcExcluded 派生集）∩ 体 = ∅。
    /// </summary>
    [Fact]
    public void EqualsAndGetHashCode_FieldCoverage_MatchesRegisteredExpectations()
    {
        var cache = new Dictionary<string, string>();
        foreach (var exp in FieldCoverageExpectations)
        {
            var type = typeof(MappingSnapshot).Assembly.GetType("NCMapper.SourceGen." + exp.TypeName)
                ?? throw new InvalidOperationException($"类型解析失败: {exp.TypeName}");
            var reflected = ReflectedPropertyNames(type);

            if (!cache.TryGetValue(exp.SourceFile, out var sanitized))
                sanitized = cache[exp.SourceFile] = SanitizeSource(ReadSourceGenSource(exp.SourceFile));

            // 类型体区间（类声明 → 花括号深度配对）
            var clsMatch = Regex.Match(sanitized, @"(?:class|struct)\s+" + exp.TypeName + @"\b");
            Assert.True(clsMatch.Success, $"{exp.SourceFile}: 类型声明定位失败 {exp.TypeName}");
            int braceStart = sanitized.IndexOf('{', clsMatch.Index);
            int depth = 0; int braceEnd = braceStart;
            for (int i = braceStart; i < sanitized.Length; i++)
            {
                if (sanitized[i] == '{') depth++;
                else if (sanitized[i] == '}') { depth--; if (depth == 0) { braceEnd = i; break; } }
            }
            var typeBody = sanitized.Substring(braceStart, braceEnd - braceStart + 1);

            var eqBody = ExtractSanitizedMethodBody(sanitized, typeBody,
                @"bool\s+Equals\s*\(\s*" + exp.TypeName + @"\b");
            var ghcBody = ExtractSanitizedMethodBody(sanitized, typeBody, @"int\s+GetHashCode\s*\(");

            var dataProps = new SortedSet<string>(reflected.Where(n => !exp.ComputedProps.Contains(n)));
            var actualEq = dataProps.Where(p => WordOccurs(eqBody, p)).ToList();
            var actualGhc = dataProps.Where(p => WordOccurs(ghcBody, p)).ToList();

            var registered = new HashSet<string>(exp.EqualsCover.Concat(exp.EqualsExcluded).Concat(exp.ComputedProps));
            var unregistered = reflected.Where(n => !registered.Contains(n)).ToList();
            var ghost = registered.Where(n => !reflected.Contains(n)).ToList();
            Assert.True(unregistered.Count == 0 && ghost.Count == 0,
                $"{exp.TypeName}: ① 登记不完整——未登记属性 [{string.Join(", ", unregistered)}] / 登记了不存在属性 [{string.Join(", ", ghost)}]（新增字段须显式登记进 Cover/Excluded/Computed——B4-10 守恒主断言）\n  实际 Equals 覆盖: {string.Join(", ", actualEq)}\n  实际 GHC 覆盖: {string.Join(", ", actualGhc)}");

            var eqMissing = exp.EqualsCover.Where(p => !actualEq.Contains(p)).ToList();
            Assert.True(eqMissing.Count == 0,
                $"{exp.TypeName}: ② Equals 期望覆盖字段未出现在 Equals 体（删比较字段即红）: {string.Join(", ", eqMissing)}");

            var eqUnexpected = exp.EqualsExcluded.Where(p => actualEq.Contains(p)).ToList();
            Assert.True(eqUnexpected.Count == 0,
                $"{exp.TypeName}: ③ 排除登记的字段出现在 Equals 体（登记过时——须同步修订登记与理由）: {string.Join(", ", eqUnexpected)}");

            var ghcMissing = exp.GhcCover.Where(p => !actualGhc.Contains(p)).ToList();
            var ghcExcluded = dataProps.Where(n => !exp.GhcCover.Contains(n)).ToList();
            var ghcUnexpected = ghcExcluded.Where(p => actualGhc.Contains(p)).ToList();
            Assert.True(ghcMissing.Count == 0 && ghcUnexpected.Count == 0,
                $"{exp.TypeName}: ④ GHC 覆盖与登记不一致——缺失 [{string.Join(", ", ghcMissing)}] / 排除集出现 [{string.Join(", ", ghcUnexpected)}]\n  实际 GHC 覆盖: {string.Join(", ", actualGhc)}");
        }
    }
}
