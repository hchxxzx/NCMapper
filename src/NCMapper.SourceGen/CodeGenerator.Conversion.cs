using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NCMapper.SourceGen;

internal static partial class CodeGenerator
{
    /// <summary>
    /// 有效源类型— 末段带集合索引时取集合元素类型，否则末段属性类型
    /// <para>生成端类型判据统一入口：赋值/转换/兼容性检查须与 BuildSourceAccess 表达式实际类型一致
    /// （索引段表达式 = ElementAtOrDefault 结果 = 元素类型，非集合属性类型）。
    /// internal 化供 GenerationPreparation 表达式化决策调用（逻辑逐行不动）。</para>
    /// </summary>
    internal static TypeRef? GetEffectiveSourceType(PropertyMappingSnapshot pm)
    {
        if (pm.SourcePath.Count > 1 || pm.HasIndex)
        {
            var last = pm.SourcePath[pm.SourcePath.Count - 1];
            if (pm.SourceIndexes.Count > 0 && pm.SourceIndexes[pm.SourceIndexes.Count - 1] is int)
                return last.Type.EnumerableElementType ?? last.Type;
            return last.Type;
        }
        return pm.Source?.Type;
    }

    /// <summary>
    /// 构建源属性访问表达式
    /// <para>前置条件：pm.Source 非 null（[Convert] 无匹配源属性场景由调用方先行分流——
    /// 起方法引用形态渲染 R6-b 静态调用、接口形态 default(参数类型)，均不经本方法）。</para>
    /// </summary>
    /// <param name="pm">属性映射信息（Source 必非 null）</param>
    /// <returns>源属性访问表达式字符串（如"src.Name"或"src.Child?.Name"）</returns>
    public static string BuildSourceAccess(PropertyMappingSnapshot pm)
    {
        if (pm.SourcePath.Count <= 1 && !pm.HasIndex)
            return $"src.{SourceGenEscape.Identifier(pm.Source!.Name)}";

        // 集合索引段生成 ElementAtOrDefault 形态
        // 全限定静态调用 + null 守卫（Enumerable.ElementAtOrDefault(null, n) 抛 ArgumentNullException，
        // 须先判集合 null）；空集合/越界 → default(元素类型)；引用类型元素 default=null 由后续 ?. 链短路。
        // 形态示例：A.B[0].C → (src.A?.B != null ? System.Linq.Enumerable.ElementAtOrDefault(src.A?.B, 0) : default)?.C
        var prefix = "src";
        for (var i = 0; i < pm.SourcePath.Count; i++)
        {
            var idx = pm.SourceIndexes.Count > i ? pm.SourceIndexes[i] : null;
            // 多段链各段成员名 @ 转义（用户属性名可为关键字）
            var segAccess = i == 0
                ? $"{prefix}.{SourceGenEscape.Identifier(pm.SourcePath[i].Name)}"
                : $"{prefix}?.{SourceGenEscape.Identifier(pm.SourcePath[i].Name)}";
            prefix = idx is int n
                ? $"({segAccess} != null ? System.Linq.Enumerable.ElementAtOrDefault({segAccess}, {n}) : default)"
                : segAccess;
        }
        return prefix;
    }

    /// <summary>
    /// 尝试生成枚举转换代码 — 重构为接收独立属性符号，供 PropertyAssignments 和 ComposeUpdateMethods 共用
    /// <para> T2/：string→enum 分支重构为全小写标签（大小写无关）+ 数字串标签 +
    /// default 抛 MapException（统一异常语义）；大小写冲突/数值重复时退化精确匹配 + NCMAP023/024 诊断。</para>
    /// </summary>
    /// <param name="sb">字符串构建器</param>
    /// <param name="sourceProperty">源属性符号</param>
    /// <param name="targetProperty">目标属性符号</param>
    /// <param name="srcAccess">源属性访问表达式字符串</param>
    /// <param name="indent">缩进字符串</param>
    /// <param name="diagnostics">诊断收集列表（Compose 等无 mapping 上下文的路径传 null，退化诊断不输出但行为退化仍生效）</param>
    /// <returns>true 表示已生成枚举转换代码；false 表示非枚举转换</returns>
    private static bool TryGenerateEnumConversion(StringBuilder sb, PropertyRef sourceProperty, PropertyRef targetProperty, string srcAccess, string indent, MappingSnapshot? mapping = null, GenerationContext? ctx = null)
    {
        // B1-13判定内核单点：分支选择/退化检测①②/可空性/冲突集合
        // 构建在 ClassifyEnumConversion（本文件尾部区段）；发射形态（多行语句 AppendLine）留本薄壳
        var facts = ClassifyEnumConversion(sourceProperty.Type, targetProperty.Type);
        if (facts == null)
            return false;

        // enum → string
        if (facts.Kind == EnumConversionKind.EnumToString)
        {
            // 别名枚举（数值重复成员）值冲突检测
            // 冲突时退化为 ToString() 调用：switch 常量模式对同值不同名成员产生不可达 arm（CS8510 编译失败），
            // 且运行时 Expression.Switch 对别名成员静默歧义映射（B→"A"）。退化后与运行时引擎同条件同行为。
            if (facts.HasValueCollision)
            {
                // 可空输入：Nullable<Enum>.ToString() 对 null 返回 ""——与 switch 默认分支语义一致
                sb.AppendLine($"{indent}dst.{SourceGenEscape.Identifier(targetProperty.Name)} = {srcAccess}.ToString();");
                return true;
            }

            sb.AppendLine($"{indent}dst.{SourceGenEscape.Identifier(targetProperty.Name)} = {srcAccess} switch");
            sb.AppendLine($"{indent}{{");
            foreach (var (name, _) in facts.EnumValues)
                sb.AppendLine($"{indent}    {facts.EnumTypeStr}.{name} => \"{name}\",");
            sb.AppendLine(facts.SrcIsNullable
                ? $"{indent}    _ => {srcAccess}?.ToString() ?? \"\","
                : $"{indent}    _ => {srcAccess}.ToString(),");
            sb.AppendLine($"{indent}}};");
            return true;
        }

        // string → enum（T2/：全小写标签大小写无关 + 数字串标签 + 非法值抛 MapException）
        // 语义对齐运行时 TypeConvertHelper.ConvertValue 的 Enum.Parse(ignoreCase:true) + 非法值抛 MapException；
        // null → default 与运行时 ConvertValue(null) 一致；空串 → 抛 MapException（
        // 运行时已取消 IsNullOrEmpty 特判改抛，生成侧同步撤 "" arm 落 _ throw 分支—— 补 arm 的前提已不存在）
        if (facts.Kind == EnumConversionKind.StringToEnum)
        {
            // 冲突退化诊断（Info 级；Compose 等无 mapping 上下文路径 mapping/ctx 为 null 时不输出；
            // 表达式版 TryBuildEnumConversionExpr 预存不发射——移 预登记）
            if (mapping != null && ctx != null)
            {
                var diagLocation = targetProperty.Location;
                if (facts.HasNameCollision)
                {
                    ctx.Diagnostics.Add(mapping, new DiagnosticSnapshot
                    {
                        DescriptorId = DiagnosticDescriptors.EnumNameCollisionDegradedRule.Id,
                        Args = new[] { facts.EnumTypeStr },
                        Location = diagLocation
                    });
                }
                if (facts.HasValueCollision)
                {
                    ctx.Diagnostics.Add(mapping, new DiagnosticSnapshot
                    {
                        DescriptorId = DiagnosticDescriptors.EnumValueCollisionDegradedRule.Id,
                        Args = new[] { facts.EnumTypeStr },
                        Location = diagLocation
                    });
                }
            }

            // 非法值统一抛 MapException（消息口径与 TypeConvertHelper"枚举值不匹配"一致，含源/目标类型）；
            // 生成文件头无 using 区，引用一律全限定；：改调 ErrorMessages 双路径同源；
            // 主体文本内核单点（BuildEnumThrowBranchCore），语句壳包装前导缩进 + 尾逗号
            var throwBranch = $"    {BuildEnumThrowBranchCore(facts.EnumTypeStr)},";

            if (facts.HasNameCollision)
            {
                // 退化路径：精确名称匹配（保留大小写敏感现状）+ null 分支 + default 抛 MapException（非法值维度仍收敛）。
                // null → default 与运行时 ConvertValue(null) 一致；空串不再独立落位（落 _ 分支抛，与运行时同口径）
                sb.AppendLine($"{indent}dst.{SourceGenEscape.Identifier(targetProperty.Name)} = {srcAccess} switch");
                sb.AppendLine($"{indent}{{");
                sb.AppendLine($"{indent}    null => {facts.EmptyDefaultLiteral},");
                foreach (var (name, _) in facts.EnumValues)
                    sb.AppendLine($"{indent}    \"{name}\" => {facts.EnumTypeStr}.{name},");
                sb.AppendLine($"{indent}{throwBranch}");
                sb.AppendLine($"{indent}}};");
                return true;
            }

            // 正常路径：全小写标签 + 数字串标签 + null 分支（空串经 ?.ToLowerInvariant() 仍为 ""，
            // 不再独立落位——落 _ 分支抛 MapException，对齐运行时 A1-08 空串抛口径）
            sb.AppendLine($"{indent}dst.{SourceGenEscape.Identifier(targetProperty.Name)} = {srcAccess}?.ToLowerInvariant() switch");
            sb.AppendLine($"{indent}{{");
            sb.AppendLine($"{indent}    null => {facts.EmptyDefaultLiteral},");
            foreach (var (name, value) in facts.EnumValues)
            {
                sb.AppendLine($"{indent}    \"{name.ToLowerInvariant()}\" => {facts.EnumTypeStr}.{name},");
                // 数字串标签：数值互异时逐成员发射（与运行时 Enum.Parse 数值解析路径对齐）；
                // 数值重复时数字串维度已退化（HasValueCollision），仅保留精确名称标签
                if (!facts.HasValueCollision)
                    sb.AppendLine($"{indent}    \"{value}\" => {facts.EnumTypeStr}.{name},");
            }
            sb.AppendLine($"{indent}{throwBranch}");
            sb.AppendLine($"{indent}}};");
            return true;
        }

        // enum → enum（不同枚举类型，按值强转；主体文本内核单点——B1-13）
        sb.AppendLine($"{indent}dst.{SourceGenEscape.Identifier(targetProperty.Name)} = {BuildEnumCastCore(facts.TgtUnderlying, srcAccess)};");
        return true;
    }

    /// <summary>
    /// 尝试生成基础类型转换赋值代码 — 提取 GeneratePropertyAssignments 与 GenerateComposeUpdateMethods 的重复逻辑
    /// <para>处理：同类型、隐式转换、可空→非可空、非可空→可空 四种常见情况</para>
    /// </summary>
    /// <param name="sb">字符串构建器</param>
    /// <param name="indent">缩进字符串</param>
    /// <param name="targetPropName">目标属性名</param>
    /// <param name="srcType">源属性类型</param>
    /// <param name="tgtType">目标属性类型</param>
    /// <param name="srcAccess">源属性访问表达式字符串</param>
    /// <returns>true 表示已生成赋值代码；false 表示非基础类型转换，需调用方处理其他情况</returns>
    private static bool TryGenerateBasicTypeConversion(StringBuilder sb, string indent, string targetPropName,
        TypeRef srcType, TypeRef tgtType, string srcAccess)
    {
        var srcPropType = srcType.FullName;
        var tgtPropType = tgtType.FullName;

        if (srcPropType == tgtPropType)
        {
            sb.AppendLine($"{indent}dst.{SourceGenEscape.Identifier(targetPropName)} = {srcAccess};");
            return true;
        }
        if (TypeRefHelpers.IsImplicitConvertible(srcType, tgtType))
        {
            sb.AppendLine($"{indent}dst.{SourceGenEscape.Identifier(targetPropName)} = ({tgtPropType}){srcAccess};");
            return true;
        }
        if (TypeRefHelpers.IsNullableToNonNullable(srcType, tgtType))
        {
            // 检查底层类型兼容性
            var underlyingSrc = TypeRefHelpers.GetUnderlyingType(srcType);
            if (TypeRefHelpers.IsSameType(underlyingSrc, tgtType))
                sb.AppendLine($"{indent}dst.{SourceGenEscape.Identifier(targetPropName)} = {srcAccess}.HasValue ? {srcAccess}.Value : default;");
            else if (TypeRefHelpers.IsImplicitConvertible(underlyingSrc, tgtType))
                sb.AppendLine($"{indent}dst.{SourceGenEscape.Identifier(targetPropName)} = {srcAccess}.HasValue ? ({tgtPropType}){srcAccess}.Value : default;");
            else
                sb.AppendLine($"{indent}dst.{SourceGenEscape.Identifier(targetPropName)} = NCMapper.TypeConvertHelper.ConvertValue<{srcPropType}, {tgtPropType}>({srcAccess})!;");
            return true;
        }
        if (TypeRefHelpers.IsNonNullableToNullable(srcType, tgtType))
        {
            // 检查底层类型兼容性
            var underlyingTgt = TypeRefHelpers.GetUnderlyingType(tgtType)!;
            if (TypeRefHelpers.IsSameType(srcType, underlyingTgt)
                || TypeRefHelpers.IsImplicitConvertible(srcType, underlyingTgt))
            {
                sb.AppendLine($"{indent}dst.{SourceGenEscape.Identifier(targetPropName)} = new {tgtPropType}(({underlyingTgt.FullName}){srcAccess});");
            }
            else
            {
                sb.AppendLine($"{indent}dst.{SourceGenEscape.Identifier(targetPropName)} = NCMapper.TypeConvertHelper.ConvertValue<{srcPropType}, {tgtPropType}>({srcAccess})!;");
            }
            return true;
        }
        return false;
    }

    /// <summary>
    /// 生成类型转换兜底代码 — 提取重复逻辑
    /// <para>先尝试枚举转换，失败则回退到 TypeConvertHelper.ConvertValue</para>
    /// </summary>
    /// <param name="sb">字符串构建器</param>
    /// <param name="indent">缩进字符串</param>
    /// <param name="sourceProperty">源属性符号</param>
    /// <param name="targetProperty">目标属性符号</param>
    /// <param name="srcAccess">源属性访问表达式字符串</param>
    /// <param name="diagnostics">诊断收集列表（T2/F-01 枚举退化诊断；Compose 等无 mapping 上下文路径传 null）</param>
    private static void GenerateConversionFallback(StringBuilder sb, string indent,
        PropertyRef sourceProperty, PropertyRef targetProperty, string srcAccess, MappingSnapshot? mapping = null, GenerationContext? ctx = null)
    {
        if (TryGenerateEnumConversion(sb, sourceProperty, targetProperty, srcAccess, indent, mapping, ctx))
            return;

        var srcPropType = sourceProperty.Type.FullName;
        var tgtPropType = targetProperty.Type.FullName;
        sb.AppendLine($"{indent}// fallback to runtime: TypeConvertHelper.ConvertValue<{srcPropType}, {tgtPropType}>({srcAccess})");
        sb.AppendLine($"{indent}dst.{SourceGenEscape.Identifier(targetProperty.Name)} = NCMapper.TypeConvertHelper.ConvertValue<{srcPropType}, {tgtPropType}>({srcAccess})!;");
    }

    // ================================================================
    // 枚举转换两份镜像收敛——共享判定内核。
    // 语句版（TryGenerateEnumConversion 发射壳，本文件）↔ 表达式版
    //（TryBuildEnumConversionExpr 构造壳，CodeGenerator.Mapping.cs）逐分支对照
    //（探索计划判据①）：
    //   分支选择 / 别名冲突检测 / 退化检测①② / 可空性判定 / 冲突集合构建 —— 完全对齐（入内核）；
    //   发射形态（多行语句 AppendLine ↔ 单行表达式 join）—— 形态差异（留薄壳）；
    //   退化诊断 NCMAP023/024 —— 表达式版预存缺失。
    // 等价守护：枚举转换既有用例（S8 矩阵）+ Equivalence 族 + X4 字节门。
    // ================================================================

    /// <summary>枚举转换类别（B1-13 内核：三分支判定单点）</summary>
    internal enum EnumConversionKind
    {
        /// <summary>非枚举转换（三分支均不命中）</summary>
        None,
        /// <summary>enum → string（含别名冲突 ToString 退避）</summary>
        EnumToString,
        /// <summary>string → enum（含退化检测①② 精确匹配/数字串退化路径）</summary>
        StringToEnum,
        /// <summary>enum → enum（不同枚举类型，按值强转）</summary>
        EnumToEnum,
    }

    /// <summary>
    /// 枚举转换判定事实（B1-13 内核）——分支命中后两薄壳共用的判定数据；
    /// 分支路由决策（冲突/可空性/default 字面量）全部在此完成，薄壳仅按事实选择发射形态。
    /// </summary>
    /// <remarks>字段语义：Kind=分支类别（路由用）；SrcUnderlying/TgtUnderlying=Nullable 解包后的
    /// 源/目标 underlying（enum→enum 强转消费 TgtUnderlying）；EnumTypeStr=当前分支的枚举类型全名
    ///（enum→string=源枚举；string→enum=目标枚举）；EnumValues=枚举成员快照（名称+底层值
    /// InvariantCulture 字符串）；SrcIsNullable=源可空（enum→string 兜底 arm 路由）；TgtIsNullable=
    /// 目标可空（string→enum default 字面量路由）；EmptyDefaultLiteral=string→enum null default
    /// 字面量（可空目标 default(T?) / 非可空 default(T)，不发射裸 null；A1-08 后仅 null 分支消费——
    /// 空串已改抛，不再落 default）；HasNameCollision=退化检测①
    ///（成员名全小化冲突 → string→enum 精确匹配）；HasValueCollision=退化检测②（别名枚举 →
    /// enum→string ToString 退避 / string→enum 数字串标签退化）。
    /// 参数级语义说明置于本 remarks（record positional 参数位置不容纳 XML doc——CS1587）。</remarks>
    internal sealed record EnumConversionFacts(
        EnumConversionKind Kind,
        TypeRef SrcUnderlying,
        TypeRef TgtUnderlying,
        string EnumTypeStr,
        IReadOnlyList<EnumMemberRef> EnumValues,
        bool SrcIsNullable,
        bool TgtIsNullable,
        string EmptyDefaultLiteral,
        bool HasNameCollision,
        bool HasValueCollision);

    /// <summary>
    /// B1-13 内核：枚举转换三分支判定 + 事实构建（underlying 解析 / 冲突集合 / 可空性 / default 字面量）。
    /// 两薄壳（语句版发射壳 / 表达式版构造壳）改指本单点；非枚举转换返回 null。
    /// </summary>
    /// <param name="srcType">源属性类型</param>
    /// <param name="tgtType">目标属性类型</param>
    /// <returns>判定事实；非枚举转换返回 null</returns>
    internal static EnumConversionFacts? ClassifyEnumConversion(TypeRef srcType, TypeRef tgtType)
    {
        var srcUnderlying = TypeRefHelpers.GetUnderlyingType(srcType)!;
        var tgtUnderlying = TypeRefHelpers.GetUnderlyingType(tgtType)!;

        // 判断源是否为可空（引用类型或 Nullable<T>）
        var srcIsNullable = srcType.IsReferenceType || TypeRefHelpers.IsNullableValueType(srcType);

        EnumConversionKind kind;
        if (srcUnderlying.Kind == TypeKind.Enum && tgtUnderlying.SpecialType == SpecialType.System_String)
            kind = EnumConversionKind.EnumToString;
        else if (srcUnderlying.SpecialType == SpecialType.System_String && tgtUnderlying.Kind == TypeKind.Enum)
            kind = EnumConversionKind.StringToEnum;
        else if (srcUnderlying.Kind == TypeKind.Enum && tgtUnderlying.Kind == TypeKind.Enum
            && !TypeRefHelpers.IsSameType(srcUnderlying, tgtUnderlying))
            kind = EnumConversionKind.EnumToEnum;
        else
            return null;

        // 枚举面：enum→string 取源侧；string→enum 取目标侧
        var enumUnderlying = kind == EnumConversionKind.StringToEnum ? tgtUnderlying : srcUnderlying;
        var enumTypeStr = enumUnderlying.FullName;
        var enumValues = TypeRefHelpers.GetEnumValues(enumUnderlying);

        // 退化检测①：成员名全小化冲突（如 Red 与 RED，违反 CLS）→ string→enum 退化为精确匹配 switch（大小写敏感）
        var lowerNameSet = new HashSet<string>(StringComparer.Ordinal);
        var hasNameCollision = false;
        foreach (var (name, _) in enumValues)
        {
            if (!lowerNameSet.Add(name.ToLowerInvariant())) { hasNameCollision = true; break; }
        }

        // 退化检测② / 别名枚举（数值重复成员）值冲突——enum→string：switch 常量模式对同值
        // 不同名成员产生不可达 arm（CS8510）+ 运行时 Expression.Switch 静默歧义映射（B→"A"）→
        // ToString() 退避；string→enum：数字串标签重复无法编译 → 数字串维度退化精确名称匹配
        var valueSet = new HashSet<object>();
        var hasValueCollision = false;
        foreach (var (_, value) in enumValues)
        {
            if (!valueSet.Add(value)) { hasValueCollision = true; break; }
        }

        // 目标可空性判定（保留 Nullable 包装）
        // 可空目标 null 发射 default({enumTypeStr}?) = null（对齐运行时 ConvertValue<string, T?>）；
        // 非可空目标维持 default({enumTypeStr})；不发射裸 null。
        // 空串改抛后本字面量仅 null 分支消费（原 null/空串双消费口径已收口）
        var tgtIsNullable = TypeRefHelpers.IsNullableValueType(tgtType);
        var emptyDefaultLiteral = tgtIsNullable ? $"default({enumTypeStr}?)" : $"default({enumTypeStr})";

        return new EnumConversionFacts(kind, srcUnderlying, tgtUnderlying,
            enumTypeStr, enumValues, srcIsNullable, tgtIsNullable, emptyDefaultLiteral,
            hasNameCollision, hasValueCollision);
    }

    /// <summary>
    /// B1-13 内核：string→enum 非法值 throw 分支主体——消息口径与 TypeConvertHelper"枚举值不匹配"
    /// 一致（含源/目标类型）；生成文件头无 using 区，引用一律全限定；
    /// 改调 ErrorMessages 双路径同源。语句薄壳包装前导缩进 + 尾逗号，表达式薄壳直接使用。
    /// </summary>
    /// <param name="enumTypeStr">目标枚举类型全名</param>
    /// <returns>throw 分支主体文本（无缩进无尾逗号）</returns>
    internal static string BuildEnumThrowBranchCore(string enumTypeStr)
        => $"_ => throw new NCMapper.MapException(NCMapper.ErrorMessages.EnumConversionFailedNoMatch(typeof(string).Name, typeof({enumTypeStr}).Name), typeof(string), typeof({enumTypeStr}))";

    /// <summary>
    /// B1-13 内核：enum→enum 强转表达式主体——语句薄壳包装赋值语句（dst.X = …;），表达式薄壳直接内嵌。
    /// </summary>
    /// <param name="tgtUnderlying">目标枚举类型（underlying）</param>
    /// <param name="srcAccess">源属性访问表达式</param>
    /// <returns>强转表达式文本</returns>
    internal static string BuildEnumCastCore(TypeRef tgtUnderlying, string srcAccess)
        => $"({tgtUnderlying.FullName}){srcAccess}";
}
