using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace NCMapper.SourceGen;

internal static partial class MappingExtractor
{
        /// <summary>
        /// 处理单个目标属性的映射关系 — 从 ExtractMappingInfo 拆分
        /// <para>解析 [MapProperty] 特性，分发到跨表（Compose）或同源（PropertyMappings）路径</para>
        /// </summary>
        /// <param name="info">映射信息（结果追加到此对象）</param>
        /// <param name="prop">目标属性符号</param>
        /// <param name="sourceType">主源类型</param>
        private static void ProcessTargetProperty(MappingInfo info, IPropertySymbol prop, INamedTypeSymbol sourceType)
        {
            // NCMAP017: [MapNoAutoInclude] 标注在引用属性（非集合）上通常无意义
            var noAutoIncludeAttr = prop.GetAttributes().FirstOrDefault(a =>
                AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeNames.MapNoAutoInclude);
            if (noAutoIncludeAttr != null
                && prop.Type.TypeKind == TypeKind.Class
                && prop.Type.SpecialType != SpecialType.System_String
                && !TypeHelpers.ImplementsIEnumerable(prop.Type))
            {
                info.Diagnostics.Add(new DiagnosticInfo
                {
                    Descriptor = DiagnosticDescriptors.NoAutoIncludeOnReferenceRule,
                    Args = new object[] { prop.Name },
                    Location = prop.Locations.FirstOrDefault()
                });
            }

            // [MapProperty] 处理
            var fieldAttr = prop.GetAttributes().FirstOrDefault(a =>
                AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeNames.MapProperty);

            string sourceFieldName;
            INamedTypeSymbol? fieldSourceType;

            if (fieldAttr != null && fieldAttr.ConstructorArguments.Length >= 2)
            {
                fieldSourceType = fieldAttr.ConstructorArguments[0].Value as INamedTypeSymbol;
                sourceFieldName = fieldAttr.ConstructorArguments[1].Value as string ?? prop.Name;
            }
            else
            {
                fieldSourceType = sourceType;
                sourceFieldName = prop.Name;
            }

            if (fieldSourceType == null)
                return;

            // 跨表字段（FromSource ≠ 主源类型）→ 收集到 ComposeMappings
            if (!SymbolEqualityComparer.Default.Equals(fieldSourceType, sourceType))
            {
                ProcessComposeMapping(info, prop, fieldSourceType, sourceFieldName, fieldAttr);
                return;
            }

            // 同源字段匹配
            ProcessSameSourceMapping(info, prop, fieldAttr, sourceType, sourceFieldName);
        }

        /// <summary>
        /// 处理跨表字段（ComposeMappings）映射 — 从 ExtractMappingInfo 拆分
        /// </summary>
        /// <param name="info">映射信息（结果追加到此对象）</param>
        /// <param name="prop">目标属性符号</param>
        /// <param name="fieldSourceType">子源类型</param>
        /// <param name="sourceFieldName">源字段名</param>
        /// <param name="fieldAttr">目标属性 [MapProperty] 特性（IsFromJson 读取——调用方已查询，避免重复符号查询）</param>
        private static void ProcessComposeMapping(MappingInfo info, IPropertySymbol prop, INamedTypeSymbol fieldSourceType, string sourceFieldName, AttributeData? fieldAttr = null)
        {
            // 查找子源类型的属性（FromField 含 '.' 时拆段解析；：含 '[' 索引段同理），
            // 同 ProcessSameSourceMapping 规则
            var subSourceProp = FindProperty(fieldSourceType, sourceFieldName);
            List<IPropertySymbol>? dotPath = null;
            List<int?>? dotIndexes = null;
            if ((subSourceProp == null || subSourceProp.GetMethod == null)
                && (sourceFieldName.Contains('.') || sourceFieldName.Contains('[')))
            {
                dotPath = TryResolveDotPath(fieldSourceType, sourceFieldName, out var failedSegment, out var failedType, out dotIndexes);
                if (dotPath != null)
                {
                    subSourceProp = dotPath[dotPath.Count - 1];
                }
                else
                {
                    // 拆段失败 → NCMAP005 带失败段名 + 所在类型
                    info.Diagnostics.Add(new DiagnosticInfo
                    {
                        Descriptor = DiagnosticDescriptors.ComposeFieldNotFoundRule,
                        Args = new object[] { prop.Name, failedType?.Name ?? fieldSourceType.Name, failedSegment ?? sourceFieldName },
                        Location = prop.Locations.FirstOrDefault()
                    });
                    return;
                }
            }
            if (subSourceProp == null || subSourceProp.GetMethod == null)
            {
                // Compose 跨表字段未找到 → 使用 NCMAP005 诊断
                info.Diagnostics.Add(new DiagnosticInfo
                {
                    Descriptor = DiagnosticDescriptors.ComposeFieldNotFoundRule,
                    Args = new object[] { prop.Name, fieldSourceType.Name, sourceFieldName },
                    Location = prop.Locations.FirstOrDefault()
                });
                return;
            }

            var composeMapping = new ComposePropertyMapping
            {
                SubSourceType = fieldSourceType,
                SourceProperty = subSourceProp,
                TargetProperty = prop,
                // 写入端归一：SourcePath 恒非空（拆段成功用路径；单段用单元素列表）
                SourcePath = dotPath ?? new List<IPropertySymbol> { subSourceProp },
                // 索引元数据与路径等长（拆段成功用索引列表；否则空列表）
                SourceIndexes = dotIndexes ?? new List<int?>()
            };

            // [MapProperty(IsFromJson = true)] — 镜像主源值类型守卫
            //（主源先例 = 本文件 ProcessComposeMapping 的同名匹配分支：源=末段子源属性 subSourceProp，
            // 目标=DTO 属性 prop）：源/目标含非 string 值类型 → 整条丢弃（return 不入 ComposeMappings，与主源同语义）
            if (fieldAttr != null
                && fieldAttr.NamedArguments.Any(kv => kv.Key == "IsFromJson" && kv.Value.Value is true))
            {
                var cSrcIsString = subSourceProp.Type.SpecialType == SpecialType.System_String;
                var cTgtIsString = prop.Type.SpecialType == SpecialType.System_String;
                var cSrcIsValueType = subSourceProp.Type.IsValueType;
                var cTgtIsValueType = prop.Type.IsValueType;

                if ((cSrcIsValueType && !cSrcIsString) || (cTgtIsValueType && !cTgtIsString))
                    return;

                composeMapping.IsFromJson = true;
            }

            // 跨表字段消费 [Convert]（source=子源对象）
            // TSource 校验基准=子源类型（fieldSourceType），TField=末段属性类型（subSourceProp.Type），
            // 多段/索引段时 subSourceProp 已为末段（拆段后赋值），TField 天然取末段类型
            var convAttr = prop.GetAttributes().FirstOrDefault(a =>
                AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeNames.Convert);
            if (convAttr != null && convAttr.ConstructorArguments.Length >= 1)
            {
                // 方法引用形态：生成端解析器（-）；跨表基准 TSource=子源类型、TField=末段属性类型。
                // 采集侧：解析命中物化模型三字段
                if (TryGetMethodReference(convAttr, prop, out var mrAnchorType, out var mrMethodName))
                {
                    ResolveConverterMethod(mrAnchorType, mrMethodName, subSourceProp.Type, fieldSourceType, prop.Type,
                        prop.Name, prop.Locations.FirstOrDefault(), info.Diagnostics, out var mrMethod, out var mrShape);
                    ApplyConverterMethodReference(composeMapping, mrMethod, mrShape);
                }
            }

            // 特性侧 [MapWhen] 跨表消费（镜像 Fluent 块内）
            // 跨表属性发射落点 = Update_{Sub}_To_{Target} 块内赋值层（EmitComposeWhenGuardOpen），
            // 有真实发射落点故消费而非 F45；T2 基准 = 子源类型（fieldSourceType）、T1 基准 = 末段
            // 属性类型（subSourceProp.Type——与 Compose 通道 composeSrcType 判定同口径）；
            // 无此前消费点时静默丢失过滤语义 = 正确性危害
            var composeWhenAttr = prop.GetAttributes().FirstOrDefault(a =>
                AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeNames.MapWhen);
            if (composeWhenAttr != null
                && composeWhenAttr.ConstructorArguments.Length >= 1
                && composeWhenAttr.ConstructorArguments[0].Value is string composeWhenMethodName
                && TryResolveWhenMethod(info, prop, composeWhenMethodName, fieldSourceType, subSourceProp.Type,
                    out var cmWhenRef, out var cmWhenShape))
            {
                composeMapping.ConditionMethodRef = cmWhenRef;
                composeMapping.ConditionShape = cmWhenShape;
            }

            // [Mask] 脱敏读注+校验 — 镜像主源三段式
            //（MappingExtractor.cs:671-709 主源先例：采集→NCMAP015/014 校验→校验通过才置位）。
            // 行为面注：校验镜像后，此前静默的无效副源 [Mask]（非 IMask/
            // 非 string 目标）将开始报既有诊断——属既有规则作用面扩展，不新增规则（X1 本意）。
            // 判定内核单点（B3-10 组2）：目标 string 判定 + IMask 实现判定
            var composeMaskAttr = prop.GetAttributes().FirstOrDefault(a =>
                AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeNames.Mask);
            if (composeMaskAttr != null && composeMaskAttr.ConstructorArguments.Length >= 1)
            {
                var composeMaskType = composeMaskAttr.ConstructorArguments[0].Value as INamedTypeSymbol;
                if (composeMaskType != null && ValidateMaskApplication(info, prop, composeMaskType))
                {
                    composeMapping.MaskType = composeMaskType;
                }
            }

            // [MapFormat] 格式化 — Compose 通道三段式（/，
            // 路径 8——校验落点同 ValidateMaskApplication Compose 通道先例；共享 ValidateFormatApplication
            // 判定内核：045 同标 → 043 目标 string → 044 源 IFormattable）
            var composeFormatAttr = prop.GetAttributes().FirstOrDefault(a =>
                AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeNames.MapFormat);
            if (composeFormatAttr != null && composeFormatAttr.ConstructorArguments.Length >= 1
                && composeFormatAttr.ConstructorArguments[0].Value is string composeFormatString)
            {
                if (ValidateFormatApplication(info, prop, subSourceProp, composeFormatString))
                {
                    composeMapping.FormatString = composeFormatString;
                }
            }

            info.ComposeMappings.Add(composeMapping);

            // 诊断：类型不兼容
            if (!IsTypeCompatibleForDiagnostic(subSourceProp.Type, prop.Type))
            {
                info.Diagnostics.Add(new DiagnosticInfo
                {
                    Descriptor = DiagnosticDescriptors.TypeIncompatibleRule,
                    Args = new object[] { prop.Name, prop.Type.Name, subSourceProp.Name, subSourceProp.Type.Name },
                    Location = prop.Locations.FirstOrDefault()
                });
            }
        }

        /// <summary>
        /// 处理同源字段映射 — 从 ExtractMappingInfo 拆分
        /// <para>包含同名匹配 + 自动扁平化匹配 + 类型兼容性诊断</para>
        /// </summary>
        /// <param name="info">映射信息（结果追加到此对象）</param>
        /// <param name="prop">目标属性符号</param>
        /// <param name="fieldAttr">[MapProperty] 特性（null 表示无标注）</param>
        /// <param name="sourceType">主源类型</param>
        /// <param name="sourceFieldName">源字段名</param>
        private static void ProcessSameSourceMapping(MappingInfo info, IPropertySymbol prop, Microsoft.CodeAnalysis.AttributeData? fieldAttr, INamedTypeSymbol sourceType, string sourceFieldName)
        {
            var sourceProp = FindProperty(sourceType, sourceFieldName);

            // ===== 反向匹配 =====
            //：属性级 [MapProperty] 标注无论类级 [MapFrom] 标注位置均应消费。
            // 锚点属性无 [MapProperty]（fieldAttr == null）且按名匹配失败时，
            // 从源类型属性上的 [MapProperty(FromSource=标注类, FromField=锚点属性名)] 反向查找。
            // reverseAttr 输出供改动点 3（IsFromJson 对侧 fallback）消费，避免二次查询。
            // ===== X1-1a（W4S-2，语义点①a）：锚点带 [Convert] 时不触发反向匹配 =====
            // ①a 优先级：锚点侧显式标注（[Convert]）优先于对侧反向标注——锚点带转换器时
            // 走转换器路径，不进入反向匹配（防止反向匹配抢占转换器路径，B1 ）。
            AttributeData? reverseAttr = null;
            if ((sourceProp == null || sourceProp.GetMethod == null) && fieldAttr == null
                && !prop.GetAttributes().Any(a => AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeNames.Convert))
            {
                var reverseProp = TryFindSourceByReverseAttribute(sourceType, prop, out reverseAttr);
                if (reverseProp?.GetMethod != null)
                    sourceProp = reverseProp;
            }
            // ===== B1 改动点 2 / X1-1a 结束 =====

            // B3-10 组3：三态判定内核单点（FindProperty/反向匹配前置语义留薄壳——判定 + NCMAP001 构造单点；
            // 发射时机留薄壳：[MapIgnore] 豁免与点路径分支之后；特性侧显式 [MapProperty] 不参与扁平化兜底）
            var autoMatch = JudgeAutoMatch(prop, sourceType, sourceFieldName, sourceProp,
                isExplicitMiss: fieldAttr != null, allowFlattenFallback: fieldAttr == null);

            if (autoMatch.SourceProp == null)
            {
                // [Convert] 无匹配源属性 — 转换器仍被调用，fieldValue 传 default(TField)
                var converterAttrForNull = prop.GetAttributes().FirstOrDefault(a =>
                    AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeNames.Convert);
                if (converterAttrForNull != null && converterAttrForNull.ConstructorArguments.Length >= 1)
                {
                    // （H6 消费）：路径场景不在本分支消费
                    // ①同源显式点路径（FromField 含 '.'）：由下方点路径拆段分支填充转换器三字段消费路径值
                    //   （转换器输入 = 路径值，非 default）；NCMAP030 不再输出（消费已实现）。
                    // ②推导扁平化可匹配（无 [MapProperty]）：由下方扁平化分支消费（转换器输入 = 路径值）。
                    // 本分支仅处理真·无源属性（无路径可消费）→ 转换器 default 调用（与运行时同构）。
                    // 触发条件含 '['（单段集合索引 "Posts[0]" 无 '.'）
                    var isDotPathForConvert = fieldAttr != null
                        && TryGetMapPropertyFromField(fieldAttr, out var cuFromField) && (cuFromField.Contains('.') || cuFromField.Contains('['));
                    var flatPathForConvert = !isDotPathForConvert && fieldAttr == null
                        ? TryFindFlattenedPath(sourceType, prop.Name)
                        : null;

                    if (!isDotPathForConvert && (flatPathForConvert == null || flatPathForConvert.Count <= 1))
                    {
                        // 方法引用形态： -b 无匹配源属性路径（sourceFieldType=null，仅形态匹配
                        // 不做字段类型交叉校验，R6-b 四模板调用形态 T0 M()/T1 M(default)/T2 M(src)/T3 M(src, default)）。
                        // 发射端：解析成功创建 pm+三字段；
                        // 字段实参发射为 default 字面量（实参类型推断，无需方法参数类型物化）
                        if (TryGetMethodReference(converterAttrForNull, prop, out var mrAnchorType, out var mrMethodName))
                        {
                            if (ResolveConverterMethod(mrAnchorType, mrMethodName, null, sourceType, prop.Type,
                                prop.Name, prop.Locations.FirstOrDefault(), info.Diagnostics, out var mrMethod, out var mrShape))
                            {
                                var pmForNull = new PropertyMapping
                                {
                                    TargetProperty = prop,
                                    SourceProperty = null!,
                                    SourcePath = new List<IPropertySymbol>(),
                                    IsFlattened = false,
                                    PathOrigin = PathOrigin.Direct,
                                };
                                ApplyConverterMethodReference(pmForNull, mrMethod, mrShape);
                                ApplyMaskForNullSource(info, prop, pmForNull);
                                // [Convert] 无源分支补 [MapWhen] 消费
                                // 本分支提前 return 绕过主链尾部消费区，[MapWhen] 条件守卫静默丢失
                                //（四处既有消费点同款：:174 跨表 / 扁平化 / 显式点 / 主链；
                                //  T1 触发基准 = 末段属性类型，无源场景即目标属性类型，与扁平化分支同口径）。
                                var nullWhenAttr = prop.GetAttributes().FirstOrDefault(a =>
                                    AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeNames.MapWhen);
                                if (nullWhenAttr != null
                                    && nullWhenAttr.ConstructorArguments.Length >= 1
                                    && nullWhenAttr.ConstructorArguments[0].Value is string nullWhenMethodName
                                    && TryResolveWhenMethod(info, prop, nullWhenMethodName, sourceType, prop.Type,
                                        out var nullWhenRef, out var nullWhenShape))
                                {
                                    pmForNull.ConditionMethodRef = nullWhenRef;
                                    pmForNull.ConditionShape = nullWhenShape;
                                }
                                info.PropertyMappings.Add(pmForNull);
                                return;
                            }
                        }
                    }
                }

                // 同名匹配失败 → 尝试自动扁平化匹配（仅对无 [MapProperty] 的属性；判定内核单点——B3-10 组3）
                if (fieldAttr == null)
                {
                    var flatPath = autoMatch.FlatPath;
                    if (flatPath != null) // 内核判定已保证 Count > 1
                    {
                        var finalProp = flatPath[flatPath.Count - 1];

                        // T1（H6 消费）：推导扁平化 × [Mask]/[Convert] 消费
                        // 提取并验证特性后填充模型字段（生成端入口分支按三字段/MaskType 消费，与直接属性分支同形态）
                        ExtractMaskAndConverterForPath(info, prop, finalProp, sourceType,
                            pathHasIndex: false, // 推导扁平化无索引段
                            out var flatMaskType,
                            out var flatConvMethod, out var flatConvShape);

                        var flatPm = BuildFlattenMapping(prop, flatPath); // pm 建造内核单点——B3-10 组3
                        flatPm.MaskType = flatMaskType;
                        ApplyConverterMethodReference(flatPm, flatConvMethod, flatConvShape);

                        // 推导扁平化 × [MapWhen] 消费（本分支提前 return
                        // 绕过主链尾部消费区——此处补齐；T1 基准=末段属性类型，与主链同口径）
                        var flatWhenAttr = prop.GetAttributes().FirstOrDefault(a =>
                            AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeNames.MapWhen);
                        if (flatWhenAttr != null
                            && flatWhenAttr.ConstructorArguments.Length >= 1
                            && flatWhenAttr.ConstructorArguments[0].Value is string flatWhenMethodName
                            && TryResolveWhenMethod(info, prop, flatWhenMethodName, sourceType, finalProp.Type,
                                out var flatWhenRef, out var flatWhenShape))
                        {
                            flatPm.ConditionMethodRef = flatWhenRef;
                            flatPm.ConditionShape = flatWhenShape;
                        }

                        info.PropertyMappings.Add(flatPm);
                        return;
                    }
                }

                // 含 [MapIgnore]（任意方向）的属性豁免 NCMAP001
                // 属性已声明忽略语义（方向性 [MapIgnore] 由生成阶段按方向跳过，双向 [MapIgnore] 已在 L127 跳过），
                // 源字段缺失正是该语义的预期（AutoMapper 默认忽略：源中无字段即不映射），报 NCMAP001 属误报。
                // 保真：仅 [MapProperty] 显式 FromField 错误（无 [MapIgnore] 标注）仍报 NCMAP001。
                if (prop.GetAttributes().Any(a =>
                    AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeNames.MapIgnore))
                {
                    return;
                }

                // 同源显式点路径拆段（FromField 含 '.'）
                // 挂 FindProperty 失败之后、NCMAP001 之前，不受 fieldAttr == null 门限（:388）限制；
                // 触发条件扩展到含 '['（单段集合索引 "B[0]" 无 '.'）；
                // 成功 → PropertyMapping { SourceProperty=末段, SourcePath=路径, IsFlattened=true（过渡期统一多段语义）, PathOrigin=ExplicitDotPath }
                if (fieldAttr != null
                    && !fieldAttr.NamedArguments.Any(kv => kv.Key == "IsFromJson" && kv.Value.Value is true) // IsFromJson 优先于点路径解析
                    && TryGetMapPropertyFromField(fieldAttr, out var dotFromField)
                    && (dotFromField.Contains('.') || dotFromField.Contains('[')))
                {
                    var dotPath = TryResolveDotPath(sourceType, dotFromField, out var failedSegment, out var failedType, out var dotIndexes);
                    if (dotPath != null)
                    {
                        var dotLeaf = dotPath[dotPath.Count - 1];

                        // T1（H6 消费）：显式点路径 × [Mask]/[Convert] 消费
                        // 提取并验证特性后填充模型字段；消费生效后不再输出 NCMAP030 诊断
                        // （NCMAP030 保留场景：上方 [Convert] 无匹配源属性分支，转换器输入 default 无法消费路径值）
                        ExtractMaskAndConverterForPath(info, prop, dotLeaf, sourceType,
                            pathHasIndex: dotFromField.Contains('['), // 索引路径 TField 校验用元素类型
                            out var dotMaskType,
                            out var dotConvMethod, out var dotConvShape);

                        var dotPm = new PropertyMapping
                        {
                            TargetProperty = prop,
                            SourceProperty = dotLeaf,
                            SourcePath = dotPath,
                            // 索引元数据与路径等长（拆段成功恒非 null）
                            SourceIndexes = dotIndexes ?? new List<int?>(),
                            IsFlattened = true, // 过渡期：显式点路径统一多段语义（PathOrigin 独立区分来源）
                            PathOrigin = PathOrigin.ExplicitDotPath,
                            MaskType = dotMaskType
                        };
                        ApplyConverterMethodReference(dotPm, dotConvMethod, dotConvShape);

                        // 显式点路径 × [MapWhen] 消费（本分支提前 return
                        // 绕过主链尾部消费区——此处补齐；T1 基准=末段属性类型 dotLeaf.Type——
                        // 索引路径时与 GetEffectiveSourceType 元素类型口径一致由发射层解析）
                        var dotWhenAttr = prop.GetAttributes().FirstOrDefault(a =>
                            AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeNames.MapWhen);
                        if (dotWhenAttr != null
                            && dotWhenAttr.ConstructorArguments.Length >= 1
                            && dotWhenAttr.ConstructorArguments[0].Value is string dotWhenMethodName
                            && TryResolveWhenMethod(info, prop, dotWhenMethodName, sourceType, dotLeaf.Type,
                                out var dotWhenRef, out var dotWhenShape))
                        {
                            dotPm.ConditionMethodRef = dotWhenRef;
                            dotPm.ConditionShape = dotWhenShape;
                        }

                        info.PropertyMappings.Add(dotPm);
                        return;
                    }

                    // 拆段失败 → NCMAP001 带失败段名 + 所在类型
                    info.Diagnostics.Add(new DiagnosticInfo
                    {
                        Descriptor = DiagnosticDescriptors.SourceFieldNotFoundRule,
                        Args = new object[] { prop.Name, failedSegment ?? dotFromField, failedType?.Name ?? sourceType.Name },
                        Location = prop.Locations.FirstOrDefault(),
                        IsExplicit = true
                    });
                    return;
                }

                // 无条件生成 NCMAP001——报告与否由输出期按注入后选项过滤
                // （类级 > 程序集级 > 默认，具体见 EmitDiagnostics）；判定内核单点（B3-10 组3：
                // 诊断构造在内核，发射时机保持在 [MapIgnore] 豁免与点路径分支之后）
                info.Diagnostics.Add(autoMatch.MissDiagnostic!);
                // 移除重复的 NCMAP006 诊断，保留主诊断 NCMAP001（SourceFieldNotFoundRule）
                return;
            }

            // B3-10 组3：命中收窄（autoMatch.SourceProp == sourceProp 同引用；miss 分支已 return，
            // 此行恢复编译器对 sourceProp 的非 null 流状态）
            sourceProp = autoMatch.SourceProp!;

            // 同名匹配成功（pm 建造内核单点——B3-10 组3；特性消费族在 pm 上继续附着）
            var pm = BuildDirectMapping(prop, sourceProp);

            // [MapProperty(IsFromJson = true)] — JSON 序列化/反序列化转换
            if (fieldAttr != null
                && fieldAttr.NamedArguments.Any(kv => kv.Key == "IsFromJson" && kv.Value.Value is true))
            {
                var srcIsString = sourceProp.Type.SpecialType == SpecialType.System_String;
                var tgtIsString = prop.Type.SpecialType == SpecialType.System_String;
                var srcIsValueType = sourceProp.Type.IsValueType;
                var tgtIsValueType = prop.Type.IsValueType;

                // 值类型参与时不支持 → 跳过（与运行时 BuildJsonAssignment 行为一致）
                if ((srcIsValueType && !srcIsString) || (tgtIsValueType && !tgtIsString))
                    return;

                pm.IsFromJson = true;
            }

            // ===== 对侧 IsFromJson fallback=====
            // 覆盖两个子场景：①反向匹配成功（改动点 2）→ reverseAttr 已有；
            // ②正向匹配成功但 IsFromJson 在对侧 → sourceProp.GetAttributes() 补读。
            if (!pm.IsFromJson)
            {
                // 先判属性数量再 ToDisplayString 比较（减少符号查询开销）
                var srcAttrs = sourceProp.GetAttributes();
                AttributeData? srcJsonAttr = reverseAttr;
                if (srcJsonAttr == null && srcAttrs.Length > 0)
                {
                    srcJsonAttr = srcAttrs.FirstOrDefault(a =>
                        AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeNames.MapProperty);
                }
                if (srcJsonAttr != null
                    && srcJsonAttr.NamedArguments.Any(kv => kv.Key == "IsFromJson" && kv.Value.Value is true))
                {
                    var srcIsString = sourceProp.Type.SpecialType == SpecialType.System_String;
                    var tgtIsString = prop.Type.SpecialType == SpecialType.System_String;
                    var srcIsValueType = sourceProp.Type.IsValueType;
                    var tgtIsValueType = prop.Type.IsValueType;

                    // 值类型参与时 return（放弃映射），与正向 L408-409 逐字节一致。
                    // 不保留普通赋值——否则标注位置不同行为分叉（锚点侧 return，对侧赋值）。
                    if ((srcIsValueType && !srcIsString) || (tgtIsValueType && !tgtIsString))
                        return;

                    pm.IsFromJson = true;
                }
            }
            // ===== B1 改动点 3 结束 =====

            // X1-1b 消费端：对侧（源属性）[Convert] 消费
            // 覆盖两子场景：①反向匹配命中（reverseAttr 为 ConvertUsing）；②按名匹配成功（reverseAttr == null）——
            // sourceProp.GetAttributes() 补读（与 B1 改动点 3 IsFromJson 补读模式同构）。
            // 语义点①a：锚点无 ConvertUsing + 源侧带 ConvertUsing → fallback 消费转换器（标注位置不影响结果，原则 1）。
            if (pm.ConverterDeclaringType == null)
            {
                AttributeData? srcConvAttr = reverseAttr;
                if (srcConvAttr == null || AttrSymbolCache.DisplayName(srcConvAttr.AttributeClass) != AttributeNames.Convert)
                {
                    var srcAttrsForConv = sourceProp.GetAttributes();
                    if (srcAttrsForConv.Length > 0)
                    {
                        srcConvAttr = srcAttrsForConv.FirstOrDefault(a =>
                            AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeNames.Convert);
                    }
                }
                if (srcConvAttr != null
                    && AttrSymbolCache.DisplayName(srcConvAttr.AttributeClass) == AttributeNames.Convert
                    && srcConvAttr.ConstructorArguments.Length >= 1)
                {
                    // 方法引用形态：单参锚定类型 = 携带特性的源属性所属类型；
                    // 采集侧：解析命中物化 pm 三字段
                    if (TryGetMethodReference(srcConvAttr, sourceProp, out var mrAnchorType, out var mrMethodName))
                    {
                        ResolveConverterMethod(mrAnchorType, mrMethodName, sourceProp.Type, info.SourceType, prop.Type,
                            prop.Name, prop.Locations.FirstOrDefault(), info.Diagnostics, out var mrMethod, out var mrShape);
                        ApplyConverterMethodReference(pm, mrMethod, mrShape);
                    }
                }
            }

            // 检查 [Convert] 自定义转换器
            var converterAttr = prop.GetAttributes().FirstOrDefault(a =>
                AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeNames.Convert);
            if (converterAttr != null && converterAttr.ConstructorArguments.Length >= 1)
            {
                // 方法引用形态：单参锚定类型 = 标注属性所属类型；
                // 采集侧：解析命中物化 pm 三字段
                if (TryGetMethodReference(converterAttr, prop, out var mrAnchorType, out var mrMethodName))
                {
                    ResolveConverterMethod(mrAnchorType, mrMethodName, sourceProp.Type, info.SourceType, prop.Type,
                        prop.Name, prop.Locations.FirstOrDefault(), info.Diagnostics, out var mrMethod, out var mrShape);
                    ApplyConverterMethodReference(pm, mrMethod, mrShape);
                }
            }

            // 检查 [ConvertBack] 反向自定义转换器
            // TSource 校验基准 = DTO 类型（info.TargetType，正向映射中 DTO 为 target，
            // 反向 DTO→Entity 中 DTO 为 source）；TField = DTO 属性类型（prop.Type）；
            // TTarget = 实体属性类型（sourceProp.Type）
            var backConvAttr = prop.GetAttributes().FirstOrDefault(a =>
                AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeNames.ConvertBack);
            if (backConvAttr != null && backConvAttr.ConstructorArguments.Length >= 1)
            {
                // 方法引用形态：反向基准TSource=DTO 类型、TField=DTO 属性类型、TTarget=实体属性类型；
                // 仅产出诊断（值版物化经 PropertyRefFromSymbol 独立采集）
                if (TryGetMethodReference(backConvAttr, prop, out var mrAnchorType, out var mrMethodName))
                {
                    ResolveConverterMethod(mrAnchorType, mrMethodName, prop.Type, info.TargetType, sourceProp.Type,
                        prop.Name, prop.Locations.FirstOrDefault(), info.Diagnostics, out _, out _);
                }
            }

            // 原 「实体侧匹配对 [ConvertBack] 校验
            // （仅 DTO 侧无标注时——实体侧三元组被反向消费的前提）」随实体侧兜底消费能力退役删除
            //（删除段）——实体侧标注现 100% 不可消费，校验不可消费的标注属噪音
            //（静默口径与同任务索引路径 D3 先例同族）。
            // 原注释所称"与运行时 sourceItem ?? targetItem 查询序（assignmentplanner.cs:91-94）严格镜像"
            // 已失实：该运行时对侧兜底已由 T5 退役（assignmentplanner.cs 单查 sourceItem）。

            // 检查 [Mask] 脱敏（判定内核单点——B3-10 组2：目标 string 判定 + IMask 实现判定）
            var maskAttr = prop.GetAttributes().FirstOrDefault(a =>
                AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeNames.Mask);
            if (maskAttr != null && maskAttr.ConstructorArguments.Length >= 1)
            {
                var maskType = maskAttr.ConstructorArguments[0].Value as INamedTypeSymbol;
                if (maskType != null && ValidateMaskApplication(info, prop, maskType))
                {
                    pm.MaskType = maskType;
                }
            }

            // 检查 [MapFormat] 格式化（pm.MaskType 同区三段式：采集→校验（NCMAP043/044/045）→置位）
            var formatAttr = prop.GetAttributes().FirstOrDefault(a =>
                AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeNames.MapFormat);
            if (formatAttr != null && formatAttr.ConstructorArguments.Length >= 1
                && formatAttr.ConstructorArguments[0].Value is string formatString)
            {
                if (ValidateFormatApplication(info, prop, sourceProp, formatString))
                {
                    pm.FormatString = formatString;
                }
            }

            // 检查 [MapWhen] 条件守卫
            // 方法在被标注属性声明类型上按名解析（单类型自足）；T1 字段值 / T2 整源按
            // "单参 == 源实体类型判整源，否则判字段值"判定（Convert 族同规则）；
            // 解析失败/签名不符/非静态/可见性不足 → NCMAP041（形态镜像 Convert 族）
            var whenAttr = prop.GetAttributes().FirstOrDefault(a =>
                AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeNames.MapWhen);
            if (whenAttr != null
                && whenAttr.ConstructorArguments.Length >= 1
                && whenAttr.ConstructorArguments[0].Value is string whenMethodName)
            {
                ApplyMapWhenAttribute(info, prop, pm, sourceProp, whenMethodName);
            }

            info.PropertyMappings.Add(pm);

            // 诊断：类型不兼容（NCMAP002 发射内核单点——B3-10 组3）
            // 移除重复的 NCMAP004（ProjectionSkippedRule）诊断，保留 NCMAP002（TypeIncompatibleRule）
            EmitTypeIncompatibleIfNotCompatible(info, prop, sourceProp);
        }

        // ================================================================
        // Fluent 通道三组复刻收敛——共享判定内核。
        // 三组镜像/复刻对（薄壳差异仅通道语义：Location 口径 / 特性消费 / 豁免条件）：
        //   组1 NCMAP011 镜像：MappingSnapshotBuilder.Build 入口 ↔ ExtractMappingCore non-public 段；
        //   组2 Mask：BuildMaskMapping ↔ 特性主链 [Mask] / Compose [Mask]；
        //   组3 自动匹配：BuildAutoMatch ↔ ProcessSameSourceMapping。
        // 等价守护：Equivalence 族（AutoMatchFlatten/Mask/Ignore 约 60 测试）+ X4 字节门。
        // ================================================================

        /// <summary>
        /// 组1 内核：NCMAP011 non-public 跳过判定 + 诊断构造——特性侧（ExtractMappingCore）与
        /// Fluent 侧（MappingSnapshotBuilder.Build）共享；薄壳差异仅 Location 口径（特性侧恒
        /// DTO 标识位置，Fluent 侧各类型自身位置）。
        /// </summary>
        /// <param name="type">待判定类型（源或目标）</param>
        /// <param name="location">诊断位置（薄壳口径传入）</param>
        /// <returns>non-public 跳过诊断；public 类型返回 null（不跳过）</returns>
        internal static DiagnosticInfo? TryBuildNonPublicSkipDiagnostic(INamedTypeSymbol type, Location? location)
            => type.DeclaredAccessibility != Accessibility.Public
                ? new DiagnosticInfo
                {
                    Descriptor = DiagnosticDescriptors.NonPublicTypeSkippedRule,
                    Args = new object[] { type.Name, type.DeclaredAccessibility.ToString() },
                    Location = location,
                }
                : null;

        /// <summary>
        /// 符号版 IFormattable 判定——Nullable&lt;T&gt; 解包后接口判定
        /// （枚举经接口判定自然通过——Enum 实现 IFormattable）。供 Fluent 块内 Format 装配消费；
        /// 特性侧 <see cref="ValidateFormatApplication"/> 与 Fluent 主链 BuildFormatMapping 各自
        /// 内联同口径判定（三站点独立防御，与家族 B3-10 判定内核单点口径的差异：块内 cm 源为
        /// 符号直取，无需跨快照共享——登记 观察）。
        /// </summary>
        internal static bool IsFormattableSourceSymbol(ITypeSymbol type)
        {
            if (type is not INamedTypeSymbol named) return false;
            var underlying = named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                ? (INamedTypeSymbol?)named.TypeArguments[0]
                : named;
            return underlying != null
                && underlying.AllInterfaces.Any(i => i.ToDisplayString() == "System.IFormattable");
        }

        /// <summary>
        /// 组2 内核：Mask 应用校验（目标 string 判定 + IMask 实现判定）——特性主链 [Mask] /
        /// Compose [Mask] / Fluent Mask&lt;TMask&gt;() 三薄壳共享。Fluent 侧 TMask 泛型约束
        ///（where TMask : IMask）编译期保证第二判定恒过——保留判定作声明面放宽时的自动防线。
        /// </summary>
        /// <returns>true = 可应用（薄壳设 MaskType）；false = 已发诊断（MaskTargetNotStringRule / MaskTypeMismatchRule）</returns>
        internal static bool ValidateMaskApplication(MappingInfo info, IPropertySymbol prop, INamedTypeSymbol maskType)
        {
            // 验证目标属性是否为 string 类型（IMask.Mask(string?) → string）
            if (prop.Type.SpecialType != SpecialType.System_String)
            {
                info.Diagnostics.Add(new DiagnosticInfo
                {
                    Descriptor = DiagnosticDescriptors.MaskTargetNotStringRule,
                    Args = new object[] { prop.Name, prop.Type.ToDisplayString() },
                    Location = prop.Locations.FirstOrDefault(),
                });
                return false;
            }

            // 验证脱敏类型是否实现 NCMapper.IMask 接口
            if (!maskType.AllInterfaces.Any(i => i.ToDisplayString() == "NCMapper.IMask"))
            {
                info.Diagnostics.Add(new DiagnosticInfo
                {
                    Descriptor = DiagnosticDescriptors.MaskTypeMismatchRule,
                    Args = new object[] { prop.Name, maskType.Name },
                    Location = prop.Locations.FirstOrDefault(),
                });
                return false;
            }
            return true;
        }

        /// <summary>
        /// [MapFormat] 应用校验
        /// 与 <see cref="ValidateMaskApplication"/> 同构三段式：045 同标矛盾 → 043 目标 string →
        /// 044 源 IFormattable（Nullable&lt;T&gt; 解包判定；枚举经接口判定自然通过——Enum 实现
        /// IFormattable，net10 实测）。
        /// </summary>
        /// <param name="info">映射信息（诊断收集）</param>
        /// <param name="prop">目标属性</param>
        /// <param name="sourceProp">源属性（IFormattable 判定基准）</param>
        /// <param name="formatString">提取的格式串（本方法不消费，入参为诊断上下文预留）</param>
        /// <returns>true = 可应用（置位 pm.FormatString）；false = 已发诊断</returns>
        private static bool ValidateFormatApplication(MappingInfo info, IPropertySymbol prop,
            IPropertySymbol sourceProp, string formatString)
        {
            // NCMAP045：与 [Convert] 同标矛盾（含三标 Format+Convert+Mask 优先拦截——
            // 本判定先于其他分支执行， 组合矩阵）
            if (prop.GetAttributes().Any(a =>
                AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeNames.Convert))
            {
                info.Diagnostics.Add(new DiagnosticInfo
                {
                    Descriptor = DiagnosticDescriptors.MapFormatWithConvertRule,
                    Args = new object[] { prop.Name },
                    Location = prop.Locations.FirstOrDefault(),
                });
                return false;
            }

            // NCMAP043：目标属性须为 string（形态参照 NCMAP015 MaskTargetNotStringRule）
            if (prop.Type.SpecialType != SpecialType.System_String)
            {
                info.Diagnostics.Add(new DiagnosticInfo
                {
                    Descriptor = DiagnosticDescriptors.MapFormatTargetNotStringRule,
                    Args = new object[] { prop.Name, prop.Type.ToDisplayString() },
                    Location = prop.Locations.FirstOrDefault(),
                });
                return false;
            }

            // NCMAP044：源属性须实现 IFormattable（Nullable<T> 解包后判定——可空包装不透传接口；
            // 枚举经接口判定自然通过）
            var sourceTypeRaw = sourceProp.Type as INamedTypeSymbol;
            var underlyingType = sourceTypeRaw != null
                && sourceTypeRaw.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                    ? (INamedTypeSymbol?)sourceTypeRaw.TypeArguments[0]
                    : sourceTypeRaw;
            if (underlyingType == null
                || !underlyingType.AllInterfaces.Any(i => i.ToDisplayString() == "System.IFormattable"))
            {
                info.Diagnostics.Add(new DiagnosticInfo
                {
                    Descriptor = DiagnosticDescriptors.MapFormatSourceNotFormattableRule,
                    Args = new object[] { prop.Name, sourceProp.Type.ToDisplayString() },
                    Location = prop.Locations.FirstOrDefault(),
                });
                return false;
            }
            return true;
        }

        /// <summary>
        /// 组3 内核：自动匹配三态判定结果——SourceProp 非 null = Direct 命中；
        /// FlatPath（Count&gt;1）= 扁平化兜底命中；两者皆空 = 未命中（MissDiagnostic 为
        /// 构造好的 NCMAP001，发射时机由薄壳决定——特性侧 [MapIgnore] 豁免与点路径分支之后）。
        /// </summary>
        internal sealed record AutoMatchJudgment(
            IPropertySymbol? SourceProp,
            List<IPropertySymbol>? FlatPath,
            DiagnosticInfo? MissDiagnostic);

        /// <summary>
        /// 组3 内核：自动匹配三态判定（纯判定零副作用——不发诊断不建 pm）。
        /// FindProperty 与反向匹配等前置语义留薄壳（特性侧反向匹配可覆盖 sourceProp）；
        /// 未命中分支构造 NCMAP001（Args 第二位 = sourceFieldName——特性侧可来自
        /// [MapProperty].FromField；IsExplicit = isExplicitMiss——特性侧 fieldAttr != null）。
        /// </summary>
        /// <param name="prop">目标属性</param>
        /// <param name="sourceType">主源类型</param>
        /// <param name="sourceFieldName">源字段名</param>
        /// <param name="sourceProp">薄壳前置解析的源属性（FindProperty / 反向匹配结果）</param>
        /// <param name="isExplicitMiss">NCMAP001 显式标记（特性侧 fieldAttr != null；Fluent 侧恒 false）</param>
        /// <param name="allowFlattenFallback">扁平化兜底开关——特性侧显式 [MapProperty]（fieldAttr != null）
        /// 不参与扁平化兜底（miss 分支原门限语义），保证该场景 MissDiagnostic 恒非 null；Fluent 侧恒 true</param>
        internal static AutoMatchJudgment JudgeAutoMatch(
            IPropertySymbol prop, INamedTypeSymbol sourceType, string sourceFieldName,
            IPropertySymbol? sourceProp, bool isExplicitMiss, bool allowFlattenFallback = true)
        {
            if (sourceProp == null || sourceProp.GetMethod == null)
            {
                if (allowFlattenFallback)
                {
                    var flatPath = TryFindFlattenedPath(sourceType, prop.Name);
                    if (flatPath != null && flatPath.Count > 1)
                    {
                        return new AutoMatchJudgment(null, flatPath, null);
                    }
                }
                return new AutoMatchJudgment(null, null, new DiagnosticInfo
                {
                    Descriptor = DiagnosticDescriptors.SourceFieldNotFoundRule,
                    Args = new object[] { prop.Name, sourceFieldName, sourceType.Name },
                    Location = prop.Locations.FirstOrDefault(),
                    IsExplicit = isExplicitMiss,
                });
            }
            return new AutoMatchJudgment(sourceProp, null, null);
        }

        /// <summary>组3 内核：Direct pm 建造（同名命中）——薄壳在返回后附着通道独有消费</summary>
        internal static PropertyMapping BuildDirectMapping(IPropertySymbol prop, IPropertySymbol sourceProp)
            => new()
            {
                TargetProperty = prop,
                SourceProperty = sourceProp,
                SourcePath = new List<IPropertySymbol> { sourceProp },
                IsFlattened = false,
                PathOrigin = PathOrigin.Direct,
            };

        /// <summary>组3 内核：扁平化兜底 pm 建造——薄壳在返回后附着通道独有消费（特性侧 Mask/converter/MapWhen）</summary>
        internal static PropertyMapping BuildFlattenMapping(IPropertySymbol prop, List<IPropertySymbol> flatPath)
            => new()
            {
                TargetProperty = prop,
                SourceProperty = flatPath[flatPath.Count - 1],
                SourcePath = flatPath,
                IsFlattened = true,
                PathOrigin = PathOrigin.DerivedFlatten,
            };

        /// <summary>
        /// 组3 内核：NCMAP002（自动路径类型不兼容）条件发射——特性主链尾部与 Fluent
        /// BuildAutoMatch 尾部共享；Fluent 侧属性级 NoAutoInclude 豁免由薄壳前置（豁免时不调）。
        /// </summary>
        internal static void EmitTypeIncompatibleIfNotCompatible(MappingInfo info, IPropertySymbol prop, IPropertySymbol sourceProp)
        {
            if (!IsTypeCompatibleForDiagnostic(sourceProp.Type, prop.Type))
            {
                info.Diagnostics.Add(new DiagnosticInfo
                {
                    Descriptor = DiagnosticDescriptors.TypeIncompatibleRule,
                    Args = new object[] { prop.Name, prop.Type.Name, sourceProp.Name, sourceProp.Type.Name },
                    Location = prop.Locations.FirstOrDefault(),
                });
            }
        }
}
