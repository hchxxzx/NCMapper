using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
namespace NCMapper.SourceGen;

/// <summary>
/// 代码生成器（验证生成）— 编译时验证结果方法，零反射，AOT 友好
/// </summary>
internal static partial class CodeGenerator
{
    #region 验证生成

    /// <summary>
    /// 生成 ValidateGenerated 方法 — 编译时验证结果，零反射，AOT 友好
    /// <para>遍历所有 [MapFrom] 映射对，生成验证结果列表；互指对不生成验证条目</para>
    /// </summary>
    /// <param name="sb">字符串构建器</param>
    /// <param name="mappings">映射信息集合</param>
    /// <param name="mutualPairs">互指有向对字典</param>
    /// <param name="ctx">生成上下文</param>
    public static void GenerateValidateMethod(StringBuilder sb, System.Collections.Immutable.ImmutableArray<MappingSnapshot?> mappings, Dictionary<TypeRef, TypeRef> mutualPairs, GenerationContext? ctx = null)
    {
        sb.AppendLine($"        /// <summary>");
        sb.AppendLine($"        /// 编译时验证结果 — 由源生成器在编译时生成，零反射，AOT 友好");
        sb.AppendLine($"        /// </summary>");

        sb.AppendLine($"        public static System.Collections.Generic.List<NCMapper.MapValidationResult> ValidateGenerated()");
        sb.AppendLine($"        {{");
        sb.AppendLine($"            var results = new System.Collections.Generic.List<NCMapper.MapValidationResult>();");
        sb.AppendLine();

        foreach (var mapping in mappings)
        {
            if (mapping == null) continue;
            // NCMAP011：non-public 类型跳过验证方法生成（引用 non-public 类型会 CS0122）
            if (mapping.SkipCodeGeneration) continue;
            // 互指对不生成验证条目（该对不生成，无验证结果）
            if (IsMutualPair(mapping, mutualPairs)) continue;

            var sourceName = mapping.SourceType.FullName;
            var targetName = mapping.TargetType.FullName;

            // 统计主源映射的属性
            var mappedCount = mapping.PropertyMappings.Count;
            var unmappedProps = new List<string>();
            var typeMismatchProps = new List<string>();
            var errorProps = new List<string>();
            var enumMismatchProps = new List<string>();

            // 从诊断中提取未映射和类型不兼容的信息（读 transform 初始 + per-run 追加的合并列表，
            // 与现状可见集一致——追加诊断 NCMAP027/028/023/024 不在下列归类分支，合并读输出等价）
            var diagnostics = ctx != null
                ? mapping.Diagnostics.Concat(ctx.Diagnostics.GetAppended(mapping))
                : mapping.Diagnostics;
            foreach (var diag in diagnostics)
            {
                if (diag.DescriptorId == "NCMAP001")
                {
                    // 与输出期过滤同口径（选项 false 时仅显式错误参与验证结果）
                    if (mapping.ReportUnmappedTargetMembers || diag.IsExplicit)
                        unmappedProps.Add(diag.Args[0]);
                }
                else if (diag.DescriptorId == "NCMAP005")
                {
                    // Compose 跨表字段未找到也算未映射属性
                    unmappedProps.Add(diag.Args[0]);
                }
                else if (diag.DescriptorId == "NCMAP002")
                {
                    typeMismatchProps.Add($"{diag.Args[0]}: {diag.Args[1]} -> {diag.Args[3]}");
                }
                else if (diag.DescriptorId == "NCMAP006")
                {
                    // NCMAP006(Warning)：目标属性未映射（setter 非 public）
                    unmappedProps.Add(diag.Args[0]);
                }
                else if (diag.DescriptorId == "NCMAP010")
                {
                    // NCMAP010(Warning)：枚举成员无重叠（Args[0]=源枚举类型名，Args[1]=目标枚举类型名）
                    enumMismatchProps.Add($"{diag.Args[0]} → {diag.Args[1]}");
                }
                else if (diag.DescriptorId == "NCMAP014")
                {
                    // NCMAP014(Error)：Mask 类型未实现 IMask
                    errorProps.Add($"NCMAP014: {diag.Args[0]}");
                }
                else if (diag.DescriptorId == "NCMAP015")
                {
                    // NCMAP015(Error)：Mask 目标属性非 string
                    errorProps.Add($"NCMAP015: {diag.Args[0]}");
                }
            }

            // 计算总属性数 = 已映射 + 未映射（枚举不匹配不计入属性数，它是类型级问题）
            var totalCount = mappedCount + unmappedProps.Count;

            // 判定级别
            string level;
            string message;
            if (errorProps.Count > 0)
            {
                level = "NCMapper.MapValidationLevel.Error";
                // 生成侧验证消息固定英文（编译期固化无法随文化，D1=乙案）；
                // 与运行时 en 分支同文（ErrorMessages.ValidatorTypeMismatchSummary en 形态为唯一事实源，
                // 生成器不引用运行时程序集，两侧一致性由 ValidatorMessagesLocalizationTests 锁定）
                message = $", {errorProps.Count} configuration error(s): {string.Join(", ", errorProps)}";
            }
            else if (typeMismatchProps.Count > 0)
            {
                level = "NCMapper.MapValidationLevel.Error";
                message = $", {typeMismatchProps.Count} incompatible type(s): {string.Join(", ", typeMismatchProps)}";
            }
            else if (unmappedProps.Count > 0 || enumMismatchProps.Count > 0)
            {
                level = "NCMapper.MapValidationLevel.Warn";
                var parts = new List<string>();
                if (unmappedProps.Count > 0)
                    parts.Add($"{unmappedProps.Count} unmapped: {string.Join(", ", unmappedProps)}");
                if (enumMismatchProps.Count > 0)
                    parts.Add($"{enumMismatchProps.Count} enum overlap mismatch(es): {string.Join(", ", enumMismatchProps)}");
                message = $", {string.Join("; ", parts)}";
            }
            else
            {
                level = "NCMapper.MapValidationLevel.OK";
                message = "";
            }

            // 生成 unmapped 列表
            var unmappedStr = unmappedProps.Count > 0
                ? $"new System.Collections.Generic.List<string> {{ {string.Join(", ", unmappedProps.Select(p => $"\"{p}\""))} }}"
                : "new System.Collections.Generic.List<string>()";

            var typeMismatchStr = typeMismatchProps.Count > 0
                ? $"new System.Collections.Generic.List<string> {{ {string.Join(", ", typeMismatchProps.Select(p => $"\"{p}\""))} }}"
                : "new System.Collections.Generic.List<string>()";

            sb.AppendLine($"            results.Add(new NCMapper.MapValidationResult");
            sb.AppendLine($"            {{");
            sb.AppendLine($"                SourceType = typeof({sourceName}),");
            sb.AppendLine($"                TargetType = typeof({targetName}),");
            sb.AppendLine($"                Level = {level},");
            sb.AppendLine($"                MappedCount = {mappedCount},");
            sb.AppendLine($"                TotalCount = {totalCount},");
            sb.AppendLine($"                UnmappedProperties = {unmappedStr},");
            sb.AppendLine($"                TypeMismatchProperties = {typeMismatchStr},");
            // 原仅转义 " 升级为五规则单点（SourceGenEscape.Literal）
            // X5 零文本判定：message 上游（上方 level/message 构造段）全为标识符/数字/中文标点，五规则转义零影响
            sb.AppendLine("                Message = " + SourceGenEscape.Literal(message));
            sb.AppendLine($"            }});");
        }

        sb.AppendLine($"            return results;");
        sb.AppendLine($"        }}");
        sb.AppendLine();
    }

    #endregion 验证生成
}
