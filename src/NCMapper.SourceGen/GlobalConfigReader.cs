using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NCMapper.SourceGen;

/// <summary>
/// SourceGen 侧全局配置快照 — 11 项配置解析结果载体（值对象，支持增量管线缓存比较）。
/// <para>约定：int 项用 -1 表示"未设置"，string[] 项用 null 表示"未设置"（继承下层）；
/// <see cref="Diagnostics"/> 为解析副产品（NCMAP020 非法值诊断），不参与相等比较
/// （每次解析重新产出，避免污染增量缓存键）。</para>
/// </summary>
internal sealed class GlobalConfigSnapshot : IEquatable<GlobalConfigSnapshot>
{
    /// <summary>
    /// 命名策略（-1 = 未设置；0-3 对应 NamingStrategy 枚举值）
    /// </summary>
    // <GENERATED:rdr-props>
    public int NamingStrategy { get; set; } = -1;

    /// <summary>
    /// 源属性名前缀剥离清单（null = 未设置）
    /// </summary>
    public string[]? Prefixes { get; set; }

    /// <summary>
    /// 源属性名后缀剥离清单（null = 未设置）
    /// </summary>
    public string[]? Postfixes { get; set; }

    /// <summary>
    /// 双向忽略类型全名清单（null = 未设置）
    /// </summary>
    public string[]? IgnoreTypeFullNames { get; set; }

    /// <summary>
    /// 双向忽略属性名清单（null = 未设置）
    /// </summary>
    public string[]? IgnoreNames { get; set; }

    /// <summary>
    /// 双向忽略属性名前缀清单（null = 未设置）
    /// </summary>
    public string[]? IgnoreNamePrefixes { get; set; }

    /// <summary>
    /// 双向忽略属性名后缀清单（null = 未设置）
    /// </summary>
    public string[]? IgnoreNameSuffixes { get; set; }

    /// <summary>
    /// ToEntity 方向额外忽略类型全名清单（null = 未设置）
    /// </summary>
    public string[]? IgnoreTypeFullNamesToEntity { get; set; }

    /// <summary>
    /// ToEntity 方向额外忽略属性名清单（null = 未设置）
    /// </summary>
    public string[]? IgnoreNamesToEntity { get; set; }

    /// <summary>
    /// ToEntity 方向额外忽略属性名前缀清单（null = 未设置）
    /// </summary>
    public string[]? IgnoreNamePrefixesToEntity { get; set; }

    /// <summary>
    /// ToEntity 方向额外忽略属性名后缀清单（null = 未设置）
    /// </summary>
    public string[]? IgnoreNameSuffixesToEntity { get; set; }
    // </GENERATED:rdr-props>

    /// <summary>
    /// 程序集级 [NCMapperOptions] 的 ReportUnmappedTargetMembers（null = 未设置；B-6 迁移自 transform，管线注入）
    /// </summary>
    public bool? ReportUnmappedTargetMembers { get; set; }

    /// <summary>
    /// 程序集级 [NCMapperOptions] 的 ReportUnmappedSourceMembers（null = 未设置；B-6 迁移自 transform，管线注入）
    /// </summary>
    public bool? ReportUnmappedSourceMembers { get; set; }

    /// <summary>
    /// 解析诊断列表（NCMAP020 非法值；不参与相等比较）
    /// </summary>
    public List<DiagnosticInfo> Diagnostics { get; } = new();

    /// <summary>
    /// 是否全部项未设置（未设置时生成器跳过快照输出，保持零变更默认生成代码干净）
    /// </summary>
    public bool IsEmpty
        // <GENERATED:rdr-isempty>
        => NamingStrategy < 0
            && Prefixes == null && Postfixes == null
            && IgnoreTypeFullNames == null && IgnoreNames == null
            && IgnoreNamePrefixes == null && IgnoreNameSuffixes == null
            && IgnoreTypeFullNamesToEntity == null && IgnoreNamesToEntity == null
            && IgnoreNamePrefixesToEntity == null && IgnoreNameSuffixesToEntity == null
            && ReportUnmappedTargetMembers == null && ReportUnmappedSourceMembers == null;
            // </GENERATED:rdr-isempty>

    /// <summary>
    /// 值相等比较（仅 11 项配置值；Diagnostics 不参与）
    /// </summary>
    /// <param name="other">另一个快照</param>
    /// <returns>配置值全等返回 true</returns>
    public bool Equals(GlobalConfigSnapshot? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        // <GENERATED:rdr-equals>
        return NamingStrategy == other.NamingStrategy
            && SequenceEqual(Prefixes, other.Prefixes)
            && SequenceEqual(Postfixes, other.Postfixes)
            && SequenceEqual(IgnoreTypeFullNames, other.IgnoreTypeFullNames)
            && SequenceEqual(IgnoreNames, other.IgnoreNames)
            && SequenceEqual(IgnoreNamePrefixes, other.IgnoreNamePrefixes)
            && SequenceEqual(IgnoreNameSuffixes, other.IgnoreNameSuffixes)
            && SequenceEqual(IgnoreTypeFullNamesToEntity, other.IgnoreTypeFullNamesToEntity)
            && SequenceEqual(IgnoreNamesToEntity, other.IgnoreNamesToEntity)
            && SequenceEqual(IgnoreNamePrefixesToEntity, other.IgnoreNamePrefixesToEntity)
            && SequenceEqual(IgnoreNameSuffixesToEntity, other.IgnoreNameSuffixesToEntity)
            && ReportUnmappedTargetMembers == other.ReportUnmappedTargetMembers
            && ReportUnmappedSourceMembers == other.ReportUnmappedSourceMembers;
            // </GENERATED:rdr-equals>
    }

    /// <summary>
    /// 覆盖 Equals 重载
    /// </summary>
    /// <param name="obj">任意对象</param>
    /// <returns>值相等返回 true</returns>
    public override bool Equals(object? obj) => Equals(obj as GlobalConfigSnapshot);

    /// <summary>
    /// 哈希（11 项配置值混合）
    /// </summary>
    /// <returns>哈希码</returns>
    public override int GetHashCode()
    {
        unchecked
        {
            // <GENERATED:rdr-gethash>
            var hash = NamingStrategy;
            hash = (hash * 397) ^ HashList(Prefixes);
            hash = (hash * 397) ^ HashList(Postfixes);
            hash = (hash * 397) ^ HashList(IgnoreTypeFullNames);
            hash = (hash * 397) ^ HashList(IgnoreNames);
            hash = (hash * 397) ^ HashList(IgnoreNamePrefixes);
            hash = (hash * 397) ^ HashList(IgnoreNameSuffixes);
            hash = (hash * 397) ^ HashList(IgnoreTypeFullNamesToEntity);
            hash = (hash * 397) ^ HashList(IgnoreNamesToEntity);
            hash = (hash * 397) ^ HashList(IgnoreNamePrefixesToEntity);
            hash = (hash * 397) ^ HashList(IgnoreNameSuffixesToEntity);
            // </GENERATED:rdr-gethash>
            return hash;
        }
    }

    /// <summary>
    /// 序列相等（B3-07 重建等价契约：null（未设置，继承下层）与 []（已设置空清单）不再判等；
    /// Ordinal 比较）
    /// </summary>
    /// <param name="a">序列 A</param>
    /// <param name="b">序列 B</param>
    /// <returns>相等返回 true</returns>
    private static bool SequenceEqual(string[]? a, string[]? b)
    {
        if (ReferenceEquals(a, b)) return true;
        // 原「null/空 视为相等」使空清单声明在缓存键上
        // 与未设置不可分——重建静默继承下层值（缓存击穿形态）；null 与 [] 必须区分
        if (a == null || b == null) return a == null && b == null;
        return a.SequenceEqual(b, StringComparer.Ordinal);
    }

    /// <summary>
    /// 序列哈希（B3-07 新哈希契约：null（未设置）→ 0、[]（已设置空清单）→ 1（非 0 区分值）、
    /// 非空 → 内容哈希——与 SequenceEqual 的 null/[] 区分口径对齐）
    /// </summary>
    /// <param name="items">序列</param>
    /// <returns>哈希码</returns>
    private static int HashList(string[]? items)
    {
        if (items == null) return 0;
        if (items.Length == 0) return 1;
        unchecked
        {
            var hash = 17;
            foreach (var item in items)
                hash = (hash * 31) + (item?.GetHashCode() ?? 0);
            return hash;
        }
    }
}

/// <summary>
/// 全局映射配置读取器（SourceGen 侧）— 读取 MSBuild 属性与 [assembly: MapDefaults] 特性，
/// 按三层优先链合并（特性 &gt; MSBuild &gt; 内建默认，逐项非空覆盖）。
/// <para>解析非法值（枚举越界、清单超 8 项、复杂全名超出 MVP 范围）→ 该项降级"未设置" +
/// 收集 NCMAP020 诊断（输出阶段经 EmitDiagnostics 报告）。</para>
/// </summary>
internal static class GlobalConfigReader
{
    /// <summary>
    /// 前后缀/忽略清单单项上限（对标 Mapperly 2^8 切分上限）
    /// </summary>
    private const int MaxListItems = 8;

    /// <summary>
    /// 读取 MSBuild 属性（11 项 build_property.NCMapperXxx）
    /// </summary>
    /// <param name="options">AnalyzerConfigOptions（GlobalOptions）</param>
    /// <returns>MSBuild 层配置快照</returns>
    public static GlobalConfigSnapshot ReadFromMSBuild(AnalyzerConfigOptions options)
    {
        var snapshot = new GlobalConfigSnapshot();
        // <GENERATED:rdr-readmsbuild>
        snapshot.NamingStrategy = ParseNamingStrategy(
            GetProperty(options, "NCMapperNamingStrategy"), "NCMapperNamingStrategy", snapshot);
        snapshot.Prefixes = ParseStringList(
            GetProperty(options, "NCMapperPrefixes"), "NCMapperPrefixes", snapshot);
        snapshot.Postfixes = ParseStringList(
            GetProperty(options, "NCMapperPostfixes"), "NCMapperPostfixes", snapshot);
        snapshot.IgnoreTypeFullNames = ParseTypeFullNameList(
            GetProperty(options, "NCMapperIgnoreTypes"), "NCMapperIgnoreTypes", snapshot);
        snapshot.IgnoreNames = ParseStringList(
            GetProperty(options, "NCMapperIgnoreNames"), "NCMapperIgnoreNames", snapshot);
        snapshot.IgnoreNamePrefixes = ParseStringList(
            GetProperty(options, "NCMapperIgnoreNamePrefixes"), "NCMapperIgnoreNamePrefixes", snapshot);
        snapshot.IgnoreNameSuffixes = ParseStringList(
            GetProperty(options, "NCMapperIgnoreNameSuffixes"), "NCMapperIgnoreNameSuffixes", snapshot);
        snapshot.IgnoreTypeFullNamesToEntity = ParseTypeFullNameList(
            GetProperty(options, "NCMapperIgnoreTypesToEntity"), "NCMapperIgnoreTypesToEntity", snapshot);
        snapshot.IgnoreNamesToEntity = ParseStringList(
            GetProperty(options, "NCMapperIgnoreNamesToEntity"), "NCMapperIgnoreNamesToEntity", snapshot);
        snapshot.IgnoreNamePrefixesToEntity = ParseStringList(
            GetProperty(options, "NCMapperIgnoreNamePrefixesToEntity"), "NCMapperIgnoreNamePrefixesToEntity", snapshot);
        snapshot.IgnoreNameSuffixesToEntity = ParseStringList(
            GetProperty(options, "NCMapperIgnoreNameSuffixesToEntity"), "NCMapperIgnoreNameSuffixesToEntity", snapshot);
            // </GENERATED:rdr-readmsbuild>
        return snapshot;
    }

    /// <summary>
    /// 读取 [assembly: MapDefaults] 程序集特性（编译单元内符号解析，零反射）
    /// </summary>
    /// <param name="compilation">编译单元</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>特性层配置快照；未标注特性返回 null</returns>
    public static GlobalConfigSnapshot? ReadFromAssemblyAttribute(Compilation compilation, CancellationToken ct)
    {
        // [assembly: NCMapperOptions] 与 [assembly: MapDefaults] 独立读取
        // 任一存在即返回快照（不再因 MapDefaults 缺失提前 return null 而丢失选项）
        var snapshot = new GlobalConfigSnapshot();
        var found = false;

        var optionsType = compilation.GetTypeByMetadataName(AttributeNames.NCMapperOptions);
        if (optionsType != null)
        {
            var optionsAttr = compilation.Assembly.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass != null
                    && SymbolEqualityComparer.Default.Equals(a.AttributeClass, optionsType));
            if (optionsAttr != null)
            {
                found = true;
                foreach (var kvp in optionsAttr.NamedArguments)
                {
                    ct.ThrowIfCancellationRequested();
                    switch (kvp.Key)
                    {
                        case "ReportUnmappedTargetMembers" when kvp.Value.Value is bool target:
                            snapshot.ReportUnmappedTargetMembers = target;
                            break;
                        case "ReportUnmappedSourceMembers" when kvp.Value.Value is bool source:
                            snapshot.ReportUnmappedSourceMembers = source;
                            break;
                    }
                }
            }
        }

        var attrType = compilation.GetTypeByMetadataName(AttributeNames.MapDefaults);
        if (attrType == null) return found ? snapshot : null;

        var attr = compilation.Assembly.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass != null
                && SymbolEqualityComparer.Default.Equals(a.AttributeClass, attrType));
        if (attr == null) return found ? snapshot : null;

        found = true;
        foreach (var kvp in attr.NamedArguments)
        {
            ct.ThrowIfCancellationRequested();
            var name = $"MapDefaults.{kvp.Key}";
            switch (kvp.Key)
            {
                // <GENERATED:rdr-switch>
                case "NamingStrategy":
                    if (TryGetEnumValue(kvp.Value.Value, out var enumValue))
                        snapshot.NamingStrategy = enumValue;
                    else
                        AddInvalid(snapshot, name, kvp.Value.Value?.ToString() ?? "<null>", "枚举值无法解析");
                    break;
                case "Prefixes":
                    snapshot.Prefixes = FromConstantArray(kvp.Value, name, snapshot);
                    break;
                case "Postfixes":
                    snapshot.Postfixes = FromConstantArray(kvp.Value, name, snapshot);
                    break;
                case "IgnoreTypes":
                    snapshot.IgnoreTypeFullNames = FromTypeConstantArray(kvp.Value, name, snapshot);
                    break;
                case "IgnoreNames":
                    snapshot.IgnoreNames = FromConstantArray(kvp.Value, name, snapshot);
                    break;
                case "IgnoreNamePrefixes":
                    snapshot.IgnoreNamePrefixes = FromConstantArray(kvp.Value, name, snapshot);
                    break;
                case "IgnoreNameSuffixes":
                    snapshot.IgnoreNameSuffixes = FromConstantArray(kvp.Value, name, snapshot);
                    break;
                case "IgnoreTypesToEntity":
                    snapshot.IgnoreTypeFullNamesToEntity = FromTypeConstantArray(kvp.Value, name, snapshot);
                    break;
                case "IgnoreNamesToEntity":
                    snapshot.IgnoreNamesToEntity = FromConstantArray(kvp.Value, name, snapshot);
                    break;
                case "IgnoreNamePrefixesToEntity":
                    snapshot.IgnoreNamePrefixesToEntity = FromConstantArray(kvp.Value, name, snapshot);
                    break;
                case "IgnoreNameSuffixesToEntity":
                    snapshot.IgnoreNameSuffixesToEntity = FromConstantArray(kvp.Value, name, snapshot);
                    break;
                    // </GENERATED:rdr-switch>
            }
        }
        return snapshot;
    }

    /// <summary>
    /// 三层合并 — 特性层优先（非空覆盖），MSBuild 层次之；逐项 null = 未设置继承下层
    /// </summary>
    /// <param name="attribute">特性层快照（可为 null = 未标注特性）</param>
    /// <param name="msbuild">MSBuild 层快照</param>
    /// <returns>合并后的快照</returns>
    public static GlobalConfigSnapshot Merge(GlobalConfigSnapshot? attribute, GlobalConfigSnapshot? msbuild)
    {
        var result = new GlobalConfigSnapshot();
        // <GENERATED:rdr-merge>
        result.NamingStrategy = attribute?.NamingStrategy >= 0
            ? attribute!.NamingStrategy
            : (msbuild?.NamingStrategy ?? -1);
        result.Prefixes = Pick(attribute?.Prefixes, msbuild?.Prefixes);
        result.Postfixes = Pick(attribute?.Postfixes, msbuild?.Postfixes);
        result.IgnoreTypeFullNames = Pick(attribute?.IgnoreTypeFullNames, msbuild?.IgnoreTypeFullNames);
        result.IgnoreNames = Pick(attribute?.IgnoreNames, msbuild?.IgnoreNames);
        result.IgnoreNamePrefixes = Pick(attribute?.IgnoreNamePrefixes, msbuild?.IgnoreNamePrefixes);
        result.IgnoreNameSuffixes = Pick(attribute?.IgnoreNameSuffixes, msbuild?.IgnoreNameSuffixes);
        result.IgnoreTypeFullNamesToEntity = Pick(attribute?.IgnoreTypeFullNamesToEntity, msbuild?.IgnoreTypeFullNamesToEntity);
        result.IgnoreNamesToEntity = Pick(attribute?.IgnoreNamesToEntity, msbuild?.IgnoreNamesToEntity);
        result.IgnoreNamePrefixesToEntity = Pick(attribute?.IgnoreNamePrefixesToEntity, msbuild?.IgnoreNamePrefixesToEntity);
        result.IgnoreNameSuffixesToEntity = Pick(attribute?.IgnoreNameSuffixesToEntity, msbuild?.IgnoreNameSuffixesToEntity);
        result.ReportUnmappedTargetMembers = attribute?.ReportUnmappedTargetMembers ?? msbuild?.ReportUnmappedTargetMembers;
        result.ReportUnmappedSourceMembers = attribute?.ReportUnmappedSourceMembers ?? msbuild?.ReportUnmappedSourceMembers;
        // </GENERATED:rdr-merge>
        if (attribute?.Diagnostics.Count > 0) result.Diagnostics.AddRange(attribute.Diagnostics);
        if (msbuild?.Diagnostics.Count > 0) result.Diagnostics.AddRange(msbuild.Diagnostics);
        return result;
    }

    /// <summary>
    /// 非空覆盖选择 — 高层非 null 用高层，否则用低层
    /// </summary>
    /// <param name="high">高层值（可为 null）</param>
    /// <param name="low">低层值（可为 null）</param>
    /// <returns>选中的值；均未设置返回 null</returns>
    private static string[]? Pick(string[]? high, string[]? low) => high ?? low;

    /// <summary>
    /// 读取 MSBuild 属性原始值
    /// </summary>
    /// <param name="options">配置选项</param>
    /// <param name="name">属性名（不含 build_property. 前缀）</param>
    /// <returns>属性值；未设置返回 null</returns>
    private static string? GetProperty(AnalyzerConfigOptions options, string name)
        => options.TryGetValue($"build_property.{name}", out var value) ? value : null;

    /// <summary>
    /// 解析命名策略枚举名（OrdinalIgnoreCase）；非法值 → 未设置 + NCMAP020
    /// </summary>
    /// <param name="value">原始字符串</param>
    /// <param name="itemName">配置项名（诊断消息用）</param>
    /// <param name="snapshot">快照（收集诊断）</param>
    /// <returns>枚举值 0-3；非法/未设置返回 -1</returns>
    private static int ParseNamingStrategy(string? value, string itemName, GlobalConfigSnapshot snapshot)
    {
        // 未设置或空字符串（Roslyn 对已声明但未赋值的 CompilerVisibleProperty 返回 ""）→ 未设置，不产生诊断
        if (string.IsNullOrEmpty(value)) return -1;
        if (string.Equals(value, "Default", StringComparison.OrdinalIgnoreCase)) return 0;
        if (string.Equals(value, "CaseSensitive", StringComparison.OrdinalIgnoreCase)) return 1;
        if (string.Equals(value, "SnakeCase", StringComparison.OrdinalIgnoreCase)) return 2;
        if (string.Equals(value, "UpperSnakeCase", StringComparison.OrdinalIgnoreCase)) return 3;
        AddInvalid(snapshot, itemName, value!, "非法枚举名（合法值：Default/CaseSensitive/SnakeCase/UpperSnakeCase）");
        return -1;
    }

    /// <summary>
    /// 解析 MSBuild 通道清单（<c>,</c> 分隔，≤8 项）；非法值 → 未设置 + NCMAP020
    /// <para><b>Q3 PoC 实测结论</b>：MSBuild 属性值经 GeneratedMSBuildEditorConfig
    /// 传递给生成器，而 editorconfig 规范中 <c>;</c> 为行内注释符—— 原定的 <c>;</c> 分隔
    /// 会导致值在传递时被截断（实测 <c>RowVersion;CreatedAt</c> 只收到 <c>RowVersion</c>）。
    /// 故 MSBuild 通道分隔符定为 <c>,</c>（逗号在 editorconfig 值中安全）；特性通道不受影响
    /// （C# 数组语法，无分隔符概念）。文档/ 同步修订。</para>
    /// </summary>
    /// <param name="value">原始字符串</param>
    /// <param name="itemName">配置项名</param>
    /// <param name="snapshot">快照（收集诊断）</param>
    /// <returns>清单；null = 未设置/非法</returns>
    private static string[]? ParseStringList(string? value, string itemName, GlobalConfigSnapshot snapshot)
    {
        // 未设置或空字符串（Roslyn 对已声明但未赋值的 CompilerVisibleProperty 返回 ""）→ 未设置，不产生诊断
        if (string.IsNullOrEmpty(value)) return null;
        var items = value!.Split(',')
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .ToArray();
        if (items.Length > MaxListItems)
        {
            AddInvalid(snapshot, itemName, value, $"清单超过 {MaxListItems} 项上限");
            return null;
        }
        return items;
    }

    /// <summary>
    /// 解析类型全名清单（<c>,</c> 分隔，≤8 项；Q3 PoC：MVP 仅承诺封闭非泛型全名，
    /// 含泛型 arity 反引号 / 程序集限定逗号等复杂语法 → 该项降级"未设置" + NCMAP020）
    /// </summary>
    /// <param name="value">原始字符串（逗号分隔）</param>
    /// <param name="itemName">配置项名</param>
    /// <param name="snapshot">快照（收集诊断）</param>
    /// <returns>类型全名清单；null = 未设置/非法</returns>
    private static string[]? ParseTypeFullNameList(string? value, string itemName, GlobalConfigSnapshot snapshot)
    {
        var items = ParseStringList(value, itemName, snapshot);
        if (items == null) return null;
        foreach (var item in items)
        {
            // Q3 边界（实测结论）：类型全名含反引号（泛型 arity，如 System.Collections.Generic.List`1）
            // 或逗号（程序集限定名，与分隔符冲突）时，列表分隔解析无法可靠表达，MVP 降级未设置
            if (item.Contains('`') || item.Contains(','))
            {
                AddInvalid(snapshot, itemName, value ?? "<null>",
                    $"复杂类型全名超出 MVP 范围（含泛型 arity 反引号或与分隔符冲突的逗号）：{item}");
                return null;
            }
        }
        return items;
    }

    /// <summary>
    /// 从特性 TypedConstant 数组提取字符串清单（null = 特性未设置该属性）
    /// </summary>
    /// <param name="constant">TypedConstant（数组）</param>
    /// <param name="itemName">配置项名</param>
    /// <param name="snapshot">快照（收集诊断）</param>
    /// <returns>清单；null = 未设置</returns>
    private static string[]? FromConstantArray(TypedConstant constant, string itemName, GlobalConfigSnapshot snapshot)
    {
        if (constant.IsNull || constant.Kind != TypedConstantKind.Array) return null;
        var values = constant.Values;
        if (values.Length > MaxListItems)
        {
            AddInvalid(snapshot, itemName, $"<{values.Length} 项>", $"清单超过 {MaxListItems} 项上限");
            return null;
        }
        var items = new List<string>(values.Length);
        foreach (var item in values)
        {
            if (item.Value is string s && s.Length > 0)
                items.Add(s);
        }
        return items.Count == 0 ? Array.Empty<string>() : items.ToArray();
    }

    /// <summary>
    /// 从特性 TypedConstant 数组提取类型全名（Type → 与运行时 Type.FullName 一致的全名：
    /// 命名空间.名称 + 泛型 arity 反引号 + 嵌套 + 分隔）
    /// </summary>
    /// <param name="constant">TypedConstant（Type 数组）</param>
    /// <param name="itemName">配置项名</param>
    /// <param name="snapshot">快照（收集诊断）</param>
    /// <returns>类型全名清单；null = 未设置</returns>
    private static string[]? FromTypeConstantArray(TypedConstant constant, string itemName, GlobalConfigSnapshot snapshot)
    {
        if (constant.IsNull || constant.Kind != TypedConstantKind.Array) return null;
        var values = constant.Values;
        if (values.Length > MaxListItems)
        {
            AddInvalid(snapshot, itemName, $"<{values.Length} 项>", $"清单超过 {MaxListItems} 项上限");
            return null;
        }
        var items = new List<string>(values.Length);
        foreach (var item in values)
        {
            if (item.Value is ITypeSymbol type)
            {
                var fullName = GetFullName(type);
                if (fullName != null)
                    items.Add(fullName);
            }
        }
        return items.Count == 0 ? Array.Empty<string>() : items.ToArray();
    }

    /// <summary>
    /// 计算与运行时 Type.FullName 一致的类型全名（命名空间.名称 + `arity + 嵌套 +）
    /// </summary>
    /// <param name="type">类型符号</param>
    /// <returns>全名；无法解析返回 null</returns>
    private static string? GetFullName(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
        {
            var element = GetFullName(array.ElementType);
            return element == null ? null : element + "[]";
        }
        if (type is INamedTypeSymbol named)
        {
            var simple = named.MetadataName;
            var containing = named.ContainingType;
            if (containing != null)
            {
                var outer = GetFullName(containing);
                return outer == null ? null : outer + "+" + simple;
            }
            var ns = named.ContainingNamespace?.ToDisplayString();
            return string.IsNullOrEmpty(ns) ? simple : ns + "." + simple;
        }
        return null;
    }

    /// <summary>
    /// 提取枚举底层值（TypedConstant 对枚举常量的 Value 为 boxed 枚举实例而非 int）
    /// </summary>
    /// <param name="value">TypedConstant.Value</param>
    /// <param name="result">底层 int 值（输出）</param>
    /// <returns>提取成功返回 true</returns>
    private static bool TryGetEnumValue(object? value, out int result)
    {
        if (value is int intValue)
        {
            result = intValue;
            return true;
        }
        if (value is System.Enum enumValue)
        {
            result = System.Convert.ToInt32(enumValue);
            return true;
        }
        result = -1;
        return false;
    }

    /// <summary>
    /// 记录非法配置值诊断（NCMAP020）
    /// </summary>
    /// <param name="snapshot">快照（追加诊断）</param>
    /// <param name="itemName">配置项名</param>
    /// <param name="value">非法值</param>
    /// <param name="reason">原因</param>
    private static void AddInvalid(GlobalConfigSnapshot snapshot, string itemName, string value, string reason)
    {
        snapshot.Diagnostics.Add(new DiagnosticInfo
        {
            Descriptor = DiagnosticDescriptors.InvalidGlobalConfigValueRule,
            Args = new object[] { itemName, value, reason },
            Location = null,
        });
    }
}
