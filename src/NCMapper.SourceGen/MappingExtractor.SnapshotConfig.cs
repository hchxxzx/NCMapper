using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace NCMapper.SourceGen
{
    /// <summary>
    /// 全局配置感知修正（值版）— 对 MappingSnapshot 执行
    /// ① 目标/源属性全局忽略（fail-closed + NCMAP021）② 配置感知补匹配（值版 FindProperty
    /// 在 SourceProperties 上查表）④ NCMAP007 抑制。
    /// <para>与符号版语义逐分支同构；快照不可变，修正产生新快照实例（上游缓存天然不受污染）。</para>
    /// </summary>
    internal static partial class MappingExtractor
    {
        /// <summary>
        /// 配置感知修正入口（值版）— 配置空（默认）时原样返回（增量缓存不变）；
        /// 非默认时对快照副本修正。
        /// </summary>
        /// <param name="mappings">待修正的映射快照数组</param>
        /// <param name="config">全局配置快照（可为 null）</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>修正后的映射快照数组</returns>
        public static ImmutableArray<MappingSnapshot?> ApplyGlobalConfig(ImmutableArray<MappingSnapshot?> mappings, GlobalConfigSnapshot? config, CancellationToken ct)
        {
            if (config == null || config.IsEmpty) return mappings;

            var result = new MappingSnapshot?[mappings.Length];
            for (var i = 0; i < mappings.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                var m = mappings[i];
                if (m == null) continue;
                result[i] = ApplyToSnapshot(m, config);
            }
            return result.ToImmutableArray();
        }

        /// <summary>
        /// 对单个快照修正（不可变重建）
        /// </summary>
        private static MappingSnapshot ApplyToSnapshot(MappingSnapshot info, GlobalConfigSnapshot config)
        {
            var isToDto = true; // [MapFrom] 目标即 DTO → ToDto 方向（反向映射不在本期匹配注入范围）

            // 可变工作副本（仅本方法内使用；最终重建不可变快照）
            var propertyMappings = info.PropertyMappings.ToList();
            var composeMappings = info.ComposeMappings.ToList();
            var diagnostics = info.Diagnostics.ToList();

            // ① 目标属性全局忽略（fail-closed）
            ApplyTargetGlobalIgnoreValue(propertyMappings, composeMappings, diagnostics, config, isToDto);
            // ①b 源属性面全局忽略
            ApplySourceGlobalIgnoreValue(propertyMappings, composeMappings, diagnostics, config, isToDto);
            // ② 配置感知补匹配：NoAutoInclude 对级语义门（Fluent 通道
            // 显式声明意图不被配置感知补匹配重新引入自动映射；[MapFrom] 通道默认 false、行为不变，
            // 标注 [MapNoAutoMatch] 时为 true → 同样不补匹配（注释口径订正②，
            // 语义方向：位为 true 时跳过补匹配）。
            // 判定按 v1.2 自查③硬约束：EqualityComparer<T>.Default.Equals(field, default)）
            if (System.Collections.Generic.EqualityComparer<bool>.Default.Equals(info.NoAutoInclude, default))
            {
                ApplyConfigAwareMatchingValue(info, propertyMappings, composeMappings, diagnostics, config, isToDto);
            }
            // ④ NCMAP007 抑制
            SuppressNCMAP007ForGlobalIgnoredValue(diagnostics, config, isToDto);
            // ④b NCMAP007 白名单抑制臂：白名单对不产出
            // NCMAP007（与 Fluent 通道零产出对齐；白名单意图 = "未声明是有意为之"）——**语义级抑制**，
            // 即便类级 [NCMapperOptions(ReportUnmappedSourceMembers=true)] 亦不产出（如需未映射审计
            // 请勿开白名单）；沿 全局忽略抑制臂先例。
            SuppressNCMAP007ForNoAutoMatchValue(diagnostics, info.NoAutoInclude);

            return new MappingSnapshot
            {
                SourceType = info.SourceType,
                TargetType = info.TargetType,
                SourceClassName = info.SourceClassName,
                TargetClassName = info.TargetClassName,
                SkipCodeGeneration = info.SkipCodeGeneration,
                // 程序集级 [NCMapperOptions] 管线注入——仅无类级标注时覆盖（类级 > 程序集级优先级保持）
                ReportUnmappedTargetMembers = !info.HasClassLevelDiagnosticOptions && config.ReportUnmappedTargetMembers.HasValue
                    ? config.ReportUnmappedTargetMembers.Value
                    : info.ReportUnmappedTargetMembers,
                ReportUnmappedSourceMembers = !info.HasClassLevelDiagnosticOptions && config.ReportUnmappedSourceMembers.HasValue
                    ? config.ReportUnmappedSourceMembers.Value
                    : info.ReportUnmappedSourceMembers,
                PropertyMappings = propertyMappings,
                AllTargetProperties = info.AllTargetProperties,
                SourceProperties = info.SourceProperties,
                // 字段守恒（B3-09 单点指引）：新增 MappingSnapshot 字段须在本初始化器与
                // DualDriverArbiter.Suppress / ReversePlanner.ApplyReverseGlobalIgnoreFilter（mayOmit
                // 白名单口径）/ MappingExtractor.Materialize 同步决策——透传或登记缺省；
                // SnapshotConservationTests 反射集 vs 赋值集机械防线（缺陷家族 007②→008→010→
                // 5464683→B-1 四次复现后防线化，W1）
                DeepCopyNodes = info.DeepCopyNodes,
                ComposeMappings = composeMappings,
                ReverseOnlyConvertBacks = info.ReverseOnlyConvertBacks,
                Diagnostics = diagnostics,
                TargetIdentifierLocation = info.TargetIdentifierLocation,
                // ForInvocationLocation 全量透传（ApplyToSnapshot 不可变重建
                // 字段守恒 B3-09 单点指引；025 Fluent 侧定位经配置修正管线不丢）
                ForInvocationLocation = info.ForInvocationLocation,
                NoAutoInclude = info.NoAutoInclude,
                AfterMappingMethodRef = info.AfterMappingMethodRef,
                BeforeMappingMethodRef = info.BeforeMappingMethodRef,
                IsFluentForward = info.IsFluentForward,
                TreeConfig = info.TreeConfig,
                PropertyNoAutoIncludeTargets = info.PropertyNoAutoIncludeTargets,
                HasComposeMappings = info.HasComposeMappings,
                SubclassPairs = info.SubclassPairs,
                HasClassLevelDiagnosticOptions = info.HasClassLevelDiagnosticOptions,
            };
        }

        /// <summary>
        /// 目标属性全局忽略（值版）— 命中清单且无 OverrideIgnore → 移除映射（留空）；带显式特性加 NCMAP021
        /// </summary>
        private static void ApplyTargetGlobalIgnoreValue(
            List<PropertyMappingSnapshot> propertyMappings,
            List<ComposeMappingSnapshot> composeMappings,
            List<DiagnosticSnapshot> diagnostics,
            GlobalConfigSnapshot config, bool isToDto)
        {
            for (var i = propertyMappings.Count - 1; i >= 0; i--)
            {
                var pm = propertyMappings[i];
                var targetProp = pm.Target;
                if (targetProp.HasOverrideIgnore) continue;
                if (!ShouldIgnoreGlobalValue(targetProp, config, isToDto)) continue;

                if (HasExplicitAttributeValue(targetProp))
                {
                    diagnostics.Add(new DiagnosticSnapshot
                    {
                        DescriptorId = DiagnosticDescriptors.ExplicitMappingHitGlobalIgnoreRule.Id,
                        Args = new[] { targetProp.Name, GetIgnoreMatchDescriptionValue(targetProp, config, isToDto) },
                    });
                }
                propertyMappings.RemoveAt(i);
            }

            for (var i = composeMappings.Count - 1; i >= 0; i--)
            {
                var cm = composeMappings[i];
                var targetProp = cm.Target;
                if (targetProp.HasOverrideIgnore) continue;
                if (!ShouldIgnoreGlobalValue(targetProp, config, isToDto)) continue;

                if (HasExplicitAttributeValue(targetProp))
                {
                    diagnostics.Add(new DiagnosticSnapshot
                    {
                        DescriptorId = DiagnosticDescriptors.ExplicitMappingHitGlobalIgnoreRule.Id,
                        Args = new[] { targetProp.Name, GetIgnoreMatchDescriptionValue(targetProp, config, isToDto) },
                    });
                }
                composeMappings.RemoveAt(i);
            }
        }

        /// <summary>
        /// 源属性面全局忽略（值版）
        /// </summary>
        private static void ApplySourceGlobalIgnoreValue(
            List<PropertyMappingSnapshot> propertyMappings,
            List<ComposeMappingSnapshot> composeMappings,
            List<DiagnosticSnapshot> diagnostics,
            GlobalConfigSnapshot config, bool isToDto)
        {
            for (var i = propertyMappings.Count - 1; i >= 0; i--)
            {
                var pm = propertyMappings[i];
                var targetProp = pm.Target;
                if (targetProp.HasOverrideIgnore) continue;
                if (pm.Source == null) continue;
                if (!ShouldIgnoreGlobalValue(pm.Source, config, isToDto)) continue;

                if (HasExplicitAttributeValue(targetProp))
                {
                    diagnostics.Add(new DiagnosticSnapshot
                    {
                        DescriptorId = DiagnosticDescriptors.ExplicitMappingHitGlobalIgnoreRule.Id,
                        Args = new[] { targetProp.Name, GetIgnoreMatchDescriptionValue(pm.Source, config, isToDto) },
                    });
                }
                propertyMappings.RemoveAt(i);
            }

            for (var i = composeMappings.Count - 1; i >= 0; i--)
            {
                var cm = composeMappings[i];
                var targetProp = cm.Target;
                if (targetProp.HasOverrideIgnore) continue;
                if (cm.Source == null) continue;
                if (!ShouldIgnoreGlobalValue(cm.Source, config, isToDto)) continue;

                if (HasExplicitAttributeValue(targetProp))
                {
                    diagnostics.Add(new DiagnosticSnapshot
                    {
                        DescriptorId = DiagnosticDescriptors.ExplicitMappingHitGlobalIgnoreRule.Id,
                        Args = new[] { targetProp.Name, GetIgnoreMatchDescriptionValue(cm.Source, config, isToDto) },
                    });
                }
                composeMappings.RemoveAt(i);
            }
        }

        /// <summary>
        /// 配置感知补匹配（值版）— 未匹配属性用配置重试；已匹配无显式 FromField 的做替换性重匹配
        /// </summary>
        private static void ApplyConfigAwareMatchingValue(
            MappingSnapshot info,
            List<PropertyMappingSnapshot> propertyMappings,
            List<ComposeMappingSnapshot> composeMappings,
            List<DiagnosticSnapshot> diagnostics,
            GlobalConfigSnapshot config, bool isToDto)
        {
            foreach (var targetProp in info.AllTargetProperties)
            {
                // 身份判定（SameProperty）：值相等性含反向三元组等上下文字段，pm.Target（携带反向物化）
                // 与 AllTargetProperties 条目（独立物化）对 [ConvertBack] 方法引用形态的属性值不等，
                // 用作匹配键会把已映射属性误判为未匹配 → 补匹配追加重复 pm
                var alreadyMatched = propertyMappings.Any(pm => pm.Target.SameProperty(targetProp))
                    || composeMappings.Any(cm => cm.Target.SameProperty(targetProp));
                if (alreadyMatched)
                {
                    ReplaceWithConfigAwareMatchValue(info, propertyMappings, targetProp, config, isToDto, diagnostics);
                    continue;
                }

                // 补匹配：配置感知匹配可能找回 OrdinalIgnoreCase 失败者（如 user_name ↔ UserName）
                // ⚠️ 全局忽略的属性不补回（① 已移除，补匹配须跳过）
                if (ShouldIgnoreGlobalValue(targetProp, config, isToDto)) continue;

                var beforeCount = propertyMappings.Count + composeMappings.Count;
                ProcessTargetPropertyValue(info, propertyMappings, composeMappings, targetProp, config, isToDto, diagnostics);
                var afterCount = propertyMappings.Count + composeMappings.Count;
                if (afterCount > beforeCount)
                {
                    // 补匹配成功 → 移除该属性的 NCMAP001（源字段不存在）诊断
                    RemoveSourceFieldNotFoundDiagnosticValue(diagnostics, targetProp.Name);
                }
            }
        }

        /// <summary>
        /// 替换性重匹配（值版）— 无显式 [MapProperty] 的普通映射用配置感知 FindProperty 重匹配
        /// </summary>
        private static void ReplaceWithConfigAwareMatchValue(
            MappingSnapshot info,
            List<PropertyMappingSnapshot> propertyMappings,
            PropertyRef targetProp,
            GlobalConfigSnapshot config, bool isToDto,
            List<DiagnosticSnapshot> diagnostics)
        {
            // 显式 [MapProperty].FromField 恒字面匹配（G3 不随策略）→ 不重匹配
            if (targetProp.HasMapProperty) return;

            for (var i = 0; i < propertyMappings.Count; i++)
            {
                var pm = propertyMappings[i];
                // 身份判定（SameProperty）：同 alreadyMatched 口径，防 [ConvertBack] 方法引用
                // 形态属性因值不等被跳过重匹配
                if (!pm.Target.SameProperty(targetProp)) continue;
                // ：Fluent 显式声明豁免替换性重匹配
                // 显式 Map/Convert/Mask/MapTree/FromJson 为符号级路径声明，不参与 NamingStrategy
                // 名称规范化重匹配（此前异名单段 Map 在 SnakeCase 下被 :257/:272 静默删除）
                if (pm.Target.HasFluentExplicit) return;
                if (pm.SourcePath.Count > 1) continue; // 多段路径不随策略（现状兜底；显式点路径同判据）
                if (pm.Source == null) continue; // 无匹配源属性的 [Convert] pm（T2/T0 无源模板）——
                // 按名重匹配语义不适用（转换器不依赖名字匹配；无源下 FindPropertyValue 恒 null 会误删，T-004b）

                var newSource = FindPropertyValue(info.SourceProperties, targetProp.Name, config);
                if (newSource == null)
                {
                    // 配置下匹配失败（如 CaseSensitive 更严格）→ 移除映射 + NCMAP001
                    propertyMappings.RemoveAt(i);
                    if (info.ReportUnmappedTargetMembers)
                    {
                        diagnostics.Add(new DiagnosticSnapshot
                        {
                            DescriptorId = DiagnosticDescriptors.SourceFieldNotFoundRule.Id,
                            Args = new[] { targetProp.Name, targetProp.Name, info.SourceType.FullName },
                        });
                    }
                    return;
                }
                // 源侧全局忽略：源属性命中清单 → 不参与候选（fail-closed）
                // ：豁免门判定与 ApplyTargetGlobalIgnoreValue /
                // ApplySourceGlobalIgnoreValue 同口径——Target.HasOverrideIgnore（镜像 [MapProperty(OverrideIgnore=true)]，
                // Fluent 四终端 overrideIgnore 物化叠加位）命中时豁免，映射不被源侧复检删除
                if (!pm.Target.HasOverrideIgnore && ShouldIgnoreGlobalValue(newSource, config, isToDto))
                {
                    propertyMappings.RemoveAt(i);
                    return;
                }
                // （IgnoreSource 畸变根因补修）：同属性判定改用 SameProperty
                // 物化后的 pm.Source 携带 FluentSourceIgnoreDirection→HasMapIgnore/MapIgnoreDirection
                // 等声明标记，而 SourceProperties 派生 ref 不携带 → 值不等触发整源重建 → 源侧方向
                // 标记丢失（samples 02 组 IgnoreSource 正反向全失效）。同名同型（SameProperty）时
                // 保留原 Source（物化标记不丢）；仅不同属性（如 user_name↔UserName 归一化换绑）才重建
                if (!pm.Source!.SameProperty(newSource))
                {
                    propertyMappings[i] = new PropertyMappingSnapshot
                    {
                        Target = pm.Target,
                        Source = newSource,
                        SourcePath = new[] { newSource },
                        IsFlattened = pm.IsFlattened,
                        PathOrigin = pm.PathOrigin,
                        // 源段索引随重建透传（单段带索引
                        // s.tags[2] 形态经配置感知重匹配换源时静默丢失——HasIndex 塌陷 +
                        // 元数据发射丢索引；索引绑定声明路径，不随换源变）
                        SourceIndexes = pm.SourceIndexes,
                        // 方法引用三字段随重匹配源透传（转换器绑定目标属性，不随源变）
                        ConverterDeclaringType = pm.ConverterDeclaringType,
                        ConverterMethodName = pm.ConverterMethodName,
                        ConverterShape = pm.ConverterShape,
                        MaskType = pm.MaskType,
                        IsFromJson = pm.IsFromJson,
                        // （义务①）：条件守卫四字段随重匹配源透传
                        // （守卫绑定目标属性（锚点），不随源变——重建丢字段即 007 同型陷阱；
                        // 转换器三字段同款注记）
                        ConditionMethodRef = pm.ConditionMethodRef,
                        ConditionMethodBody = pm.ConditionMethodBody,
                        ConditionMethodParamName = pm.ConditionMethodParamName,
                        ConditionShape = pm.ConditionShape,
                        // （义务①延伸）：B 形态转换器方法体三字段随重匹配源透传
                        // （转换器绑定目标属性，不随源变——与转换器三字段同款注记）
                        ConverterMethodBody = pm.ConverterMethodBody,
                        ConverterMethodParamTypes = pm.ConverterMethodParamTypes,
                        ConverterMethodParamNames = pm.ConverterMethodParamNames,
                    };
                }
                return;
            }
        }

        /// <summary>
        /// 补匹配值版 ProcessTargetProperty — 仅处理"同源配置感知匹配"路径：
        /// 显式 [MapProperty].FromField 字面匹配（G3 不随策略，主匹配已处理，此处重试无增益）；跨表（FromSource≠主源）
        /// 需子源属性列表（未物化），保持主匹配结果（备案：边缘场景行为差异，见执行记录）。
        /// </summary>
        private static void ProcessTargetPropertyValue(
            MappingSnapshot info,
            List<PropertyMappingSnapshot> propertyMappings,
            List<ComposeMappingSnapshot> composeMappings,
            PropertyRef targetProp,
            GlobalConfigSnapshot config, bool isToDto,
            List<DiagnosticSnapshot> diagnostics)
        {
            // 显式跨表（FromSource ≠ 主源）：子源属性列表未物化，跳过补匹配（主匹配结果保留）
            if (targetProp.MapPropertyFromSource != null
                && !TypeRefHelpers.IsSameType(targetProp.MapPropertyFromSource, info.SourceType))
            {
                return;
            }

            var sourceFieldName = targetProp.MapPropertyFromField ?? targetProp.Name;
            var sourceProp = FindPropertyValue(info.SourceProperties, sourceFieldName, config);
            if (sourceProp == null) return;

            // 源侧全局忽略（fail-closed）
            if (ShouldIgnoreGlobalValue(sourceProp, config, isToDto)) return;

            // [MapProperty(IsFromJson=true)] 值类型跳过（镜像符号版 ProcessTargetProperty 规则：
            // 值类型参与时不支持 JSON 反序列化 → 跳过，与运行时 BuildJsonAssignment 行为一致）
            if (targetProp.IsFromJson)
            {
                var srcIsString = sourceProp.Type.SpecialType == SpecialType.System_String;
                var tgtIsString = targetProp.Type.SpecialType == SpecialType.System_String;
                var srcIsValueType = sourceProp.Type.IsValueType;
                var tgtIsValueType = targetProp.Type.IsValueType;
                if ((srcIsValueType && !srcIsString) || (tgtIsValueType && !tgtIsString))
                    return;
            }

            propertyMappings.Add(new PropertyMappingSnapshot
            {
                Target = targetProp,
                Source = sourceProp,
                SourcePath = new[] { sourceProp },
                IsFlattened = false,
                PathOrigin = PathOrigin.Direct,
                // 方法引用三字段从目标属性带入
                ConverterDeclaringType = targetProp.ConverterDeclaringType,
                ConverterMethodName = targetProp.ConverterMethodName,
                ConverterShape = targetProp.ConverterShape,
                MaskType = targetProp.MaskType,
                IsFromJson = targetProp.IsFromJson,
                // （义务①边界注记）：ConditionMethodRef 为 pm 级字段
                //（PropertyRef 不承载）——补匹配属性此前无 pm，无从携带条件，恒默认 null；
                // 已映射属性的条件守卫经 ReplaceWithConfigAwareMatchValue 重建透传（:303-323）。
            });
        }

        /// <summary>
        /// 值版配置感知按名匹配 — 在 SourceProperties 快照上查表（与符号版 FindProperty(type,name,config) 同源）
        /// </summary>
        private static PropertyRef? FindPropertyValue(IReadOnlyList<PropertyRef> sourceProperties, string name, GlobalConfigSnapshot config)
        {
            if (sourceProperties.Count == 0) return null;

            var names = sourceProperties.Select(p => p.Name).ToList();
            // Q6-M3与 MappingExtractor.cs 符号版同源——NamingStrategy == -1（未设置）
            // 应回退 IgnoreCase（NCMapperOptions 特性触发配置感知路径后 -1 落入 Ordinal 精确匹配的同一 bug）；
            var comparer = config.NamingStrategy <= 0
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
            var dict = NCMapper.Shared.NameCandidateResolver.BuildNormalizedSourceKeys(
                names,
                config.Prefixes ?? Array.Empty<string>(),
                config.Postfixes ?? Array.Empty<string>(),
                config.NamingStrategy,
                comparer);
            if (NCMapper.Shared.NameCandidateResolver.TryResolveCandidate(
                    dict, name, config.NamingStrategy, out var index))
            {
                return sourceProperties[index];
            }
            return null;
        }

        /// <summary>
        /// NCMAP007 抑制（值版）— 源属性命中全局忽略清单时其"未映射源属性"诊断不报告
        /// </summary>
        private static void SuppressNCMAP007ForGlobalIgnoredValue(
            List<DiagnosticSnapshot> diagnostics,
            GlobalConfigSnapshot config, bool isToDto)
        {
            for (var i = diagnostics.Count - 1; i >= 0; i--)
            {
                var diag = diagnostics[i];
                if (diag.DescriptorId != DiagnosticDescriptors.UnmappedSourceMemberRule.Id) continue;
                if (diag.Args.Count < 1 || diag.Args[0] is not string propName) continue;
                if (ShouldIgnoreGlobalValue(propName, null, isToDto, config))
                    diagnostics.RemoveAt(i);
            }
        }

        /// <summary>
        /// NCMAP007 白名单抑制（值版）— 对级白名单位（Fluent <c>NoAutoMatch()</c> / 特性
        /// <c>[MapNoAutoMatch]</c>）为 true 时移除全部
        /// NCMAP007"未映射源成员"诊断。
        /// <para><b>语义级抑制</b>：不看 <c>ReportUnmappedSourceMembers</c> 开关——白名单意图即
        /// "未声明是有意为之"，类级显式开审计亦不产出（与 Fluent 通道零产出对齐，A-25 双通道等价
        /// 在诊断面成立）。与 <see cref="SuppressNCMAP007ForGlobalIgnoredValue"/>（按属性名命中
        /// 全局忽略逐条抑制）为两条独立臂，互不合并。</para>
        /// </summary>
        private static void SuppressNCMAP007ForNoAutoMatchValue(List<DiagnosticSnapshot> diagnostics, bool noAutoInclude)
        {
            if (!noAutoInclude) return;
            for (var i = diagnostics.Count - 1; i >= 0; i--)
            {
                if (diagnostics[i].DescriptorId == DiagnosticDescriptors.UnmappedSourceMemberRule.Id)
                    diagnostics.RemoveAt(i);
            }
        }

        /// <summary>
        /// 移除指定目标属性的 NCMAP001 诊断（值版）— 补匹配成功后的清理
        /// </summary>
        private static void RemoveSourceFieldNotFoundDiagnosticValue(List<DiagnosticSnapshot> diagnostics, string targetPropName)
        {
            for (var i = diagnostics.Count - 1; i >= 0; i--)
            {
                var diag = diagnostics[i];
                if (diag.DescriptorId != DiagnosticDescriptors.SourceFieldNotFoundRule.Id) continue;
                if (diag.Args.Count >= 1 && diag.Args[0] == targetPropName)
                    diagnostics.RemoveAt(i);
            }
        }

        // ================================================================
        // 反向快照全局 ToEntity 忽略过滤
        // ApplyReverseGlobalIgnoreFilter 已逐行迁至
        // ReversePlanner（C1 前移）；下方四个辅助判定仍留本类（正向路径广泛复用，
        // 单一事实源），internal 化供 ReversePlanner 调用。
        // ================================================================

        /// <summary>
        /// 全局忽略判定（PropertyRef 值版）— 调共享 NameCandidateResolver.ShouldIgnoreGlobal
        /// <para>internal 化供 ReversePlanner 调用</para>
        /// <para>类型全名改元数据形态归一（缺陷态 = 传显示
        /// 形态 FullName "…List&lt;System.Int32&gt;" 与特性通道清单元数据形态 "…List`1" 不命中 →
        /// 全局忽略漏防）</para>
        /// </summary>
        internal static bool ShouldIgnoreGlobalValue(PropertyRef prop, GlobalConfigSnapshot config, bool isToDto)
            => ShouldIgnoreGlobalValue(prop.Name, GetNormalizedTypeFullName(prop.Type), isToDto, config);

        /// <summary>
        /// 类型全名归一（元数据形态）——ns + MetadataName（List&lt;int&gt; →
        /// "System.Collections.Generic.List`1"，与特性通道 GlobalConfigReader/符号版
        /// - GetFullName 同源口径）；非泛型类型两形态同串行为不变；数组 Namespace
        /// 取元素 ns（MetadataName 已含 "[]" 形态）。
        /// </summary>
        internal static string GetNormalizedTypeFullName(TypeRef type)
            => string.IsNullOrEmpty(type.Namespace)
                ? type.MetadataName
                : type.Namespace + "." + type.MetadataName;

        /// <summary>
        /// 全局忽略判定（字符串版）— 与符号版 ShouldIgnoreGlobal(string,...) 同源
        /// </summary>
        private static bool ShouldIgnoreGlobalValue(string propName, string? typeFullName, bool isToDto, GlobalConfigSnapshot config)
            => NCMapper.Shared.NameCandidateResolver.ShouldIgnoreGlobal(
                propName, typeFullName, isToDto,
                config.IgnoreTypeFullNames ?? Array.Empty<string>(),
                config.IgnoreNames ?? Array.Empty<string>(),
                config.IgnoreNamePrefixes ?? Array.Empty<string>(),
                config.IgnoreNameSuffixes ?? Array.Empty<string>(),
                config.IgnoreTypeFullNamesToEntity ?? Array.Empty<string>(),
                config.IgnoreNamesToEntity ?? Array.Empty<string>(),
                config.IgnoreNamePrefixesToEntity ?? Array.Empty<string>(),
                config.IgnoreNameSuffixesToEntity ?? Array.Empty<string>());

        /// <summary>
        /// 目标属性是否带显式映射特性（值版，NCMAP021 触发条件）
        /// <para>internal 化供 ReversePlanner 调用</para>
        /// <para> （接入面 d）：HasMapFormat 纳入——漏改 = [MapFormat]
        /// 命中全局忽略清单被静默跳过且不报 NCMAP021</para>
        /// </summary>
        internal static bool HasExplicitAttributeValue(PropertyRef prop)
            => prop.HasMapProperty || prop.HasConvertUsing || prop.HasMask || prop.HasConvertBackUsing || prop.HasMapFormat;

        /// <summary>
        /// 生成 NCMAP021 消息的命中描述（值版）
        /// <para>internal 化供 ReversePlanner 调用</para>
        /// </summary>
        internal static string GetIgnoreMatchDescriptionValue(PropertyRef prop, GlobalConfigSnapshot config, bool isToDto)
        {
            var name = prop.Name;
            var typeFullName = prop.Type.FullName;
            var hits = new List<string>();
            if (config.IgnoreNames?.Contains(name, StringComparer.OrdinalIgnoreCase) == true) hits.Add("IgnoreNames");
            if (config.IgnoreTypeFullNames?.Contains(typeFullName, StringComparer.Ordinal) == true) hits.Add("IgnoreTypes");
            if (config.IgnoreNamePrefixes?.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase)) == true) hits.Add("IgnoreNamePrefixes");
            if (config.IgnoreNameSuffixes?.Any(s => name.EndsWith(s, StringComparison.OrdinalIgnoreCase)) == true) hits.Add("IgnoreNameSuffixes");
            if (!isToDto)
            {
                if (config.IgnoreNamesToEntity?.Contains(name, StringComparer.OrdinalIgnoreCase) == true) hits.Add("IgnoreNamesToEntity");
                if (config.IgnoreTypeFullNamesToEntity?.Contains(typeFullName, StringComparer.Ordinal) == true) hits.Add("IgnoreTypesToEntity");
                if (config.IgnoreNamePrefixesToEntity?.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase)) == true) hits.Add("IgnoreNamePrefixesToEntity");
                if (config.IgnoreNameSuffixesToEntity?.Any(s => name.EndsWith(s, StringComparison.OrdinalIgnoreCase)) == true) hits.Add("IgnoreNameSuffixesToEntity");
            }
            return hits.Count > 0 ? string.Join(",", hits) : "全局忽略清单";
        }
    }
}
