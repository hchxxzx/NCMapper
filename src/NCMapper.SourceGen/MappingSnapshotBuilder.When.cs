using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NCMapper.SourceGen;

internal static partial class MappingSnapshotBuilder
{
    /// <summary>
    /// 守卫声明注入（pm 通道）——A 形态直取方法引用；
    /// B 形态计算生成方法限定名（含 (S,T) 对稳定哈希防同名类型冲突——提取期无
    /// suffixMap 后缀信息，哈希为唯一确定性消歧源）。
    /// </summary>
    private static void ApplyWhenDeclaration(
        PropertyMapping pm,
        FluentWhenDeclaration whenDecl,
        INamedTypeSymbol sourceType,
        INamedTypeSymbol targetType)
    {
        pm.ConditionShape = whenDecl.Shape;
        pm.ConditionMethodBody = whenDecl.TranscribedBody;
        pm.ConditionMethodParamName = whenDecl.TranscribedParamName;
        pm.ConditionMethodRef = whenDecl.MethodRef
            ?? BuildGeneratedWhenMethodName(sourceType, targetType, whenDecl.AnchorPropertyName);
    }

    /// <summary>
    /// 守卫声明注入（ComposePropertyMapping 通道）
    /// 基准对换为 (子源, 目标)。
    /// </summary>
    private static void ApplyComposeWhenDeclaration(
        ComposePropertyMapping cm,
        FluentWhenDeclaration whenDecl,
        INamedTypeSymbol subSourceType,
        INamedTypeSymbol targetType)
    {
        cm.ConditionShape = whenDecl.Shape;
        cm.ConditionMethodBody = whenDecl.TranscribedBody;
        cm.ConditionMethodParamName = whenDecl.TranscribedParamName;
        cm.ConditionMethodRef = whenDecl.MethodRef
            ?? BuildGeneratedWhenMethodName(subSourceType, targetType, whenDecl.AnchorPropertyName);
    }

    /// <summary>
    /// B 形态生成方法限定名
    /// "NCMapper.Generated.GeneratedMapper::Map_When_{目标类名}_{属性名}_{稳定哈希8}"。
    /// 哈希 = FNV-1a(sourceFqn|targetFqn|propName) 低 32 位十六进制——同名类型对冲突消歧
    /// （与 suffixMap 解耦：生成方法名在物化期定形，两消费点（发射+注册表）同源一致）。
    /// </summary>
    private static string BuildGeneratedWhenMethodName(INamedTypeSymbol sourceType, INamedTypeSymbol targetType, string anchorName)
    {
        var hashInput = sourceType.ToDisplayString() + "|" + targetType.ToDisplayString() + "|" + anchorName;
        var hash = 2166136261u;
        foreach (var ch in hashInput)
        {
            hash ^= ch;
            hash *= 16777619u;
        }
        return "NCMapper.Generated.GeneratedMapper::Map_When_"
               + targetType.Name + "_" + SanitizeWhenIdentifier(anchorName) + "_" + hash.ToString("x8");
    }

    /// <summary>MapWhen 生成方法名段净化（非字母数字/下划线 → '_'；与生成方法名语法兼容）</summary>
    private static string SanitizeWhenIdentifier(string name)
    {
        var chars = name.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_')
                chars[i] = '_';
        }
        return new string(chars);
    }

    /// <summary>
    /// 跨对传播——目标 DTO 类型级钩子特性回读注入
    /// <para>[BeforeMapping]/[AfterMapping] 随 DTO target-level 生效：物化期按 (fs.SourceType, fs.TargetType)
    /// 签名适配回读注入（共享 <see cref="MappingExtractor.ResolveLifecycleHook"/>——与特性通道本对解析
    /// 同一规则零分叉）；签名不适用对不注入且不发诊断（D-d 正常过滤路径）。</para>
    /// <para>Fluent 显式声明值优先（承载字段非空不覆盖）；诊断口径恒 null——声明所在对的
    /// NCMAP038~040 由特性通道发现管线产出，纯 Fluent 场景为 D-d 静默路径。</para>
    /// </summary>
    /// <param name="fs">解析期单链快照（目标 DTO 符号 + 本对源类型）</param>
    /// <param name="info">映射信息（两承载字段注入目标）</param>
    private static void PropagateTargetLevelHooks(FluentSnapshot fs, MappingInfo info)
    {
        foreach (var attr in fs.TargetType.GetAttributes())
        {
            // 赋值形态同族站点缓存化（编制期 == 口径外，等价替换登记）
            var attrName = AttrSymbolCache.DisplayName(attr.AttributeClass);
            if (attrName == AttributeNames.BeforeMapping && info.BeforeMappingMethodRef == null)
            {
                info.BeforeMappingMethodRef = MappingExtractor.ResolveLifecycleHook(
                    attr, fs.TargetType, fs.SourceType, fs.TargetType,
                    diagnostics: null, diagnoseSignatureMismatch: false);
            }
            else if (attrName == AttributeNames.AfterMapping && info.AfterMappingMethodRef == null)
            {
                info.AfterMappingMethodRef = MappingExtractor.ResolveLifecycleHook(
                    attr, fs.TargetType, fs.SourceType, fs.TargetType,
                    diagnostics: null, diagnoseSignatureMismatch: false);
            }
        }
    }
}
