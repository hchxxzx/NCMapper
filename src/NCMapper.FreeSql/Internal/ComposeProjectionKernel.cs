// NCMapper FreeSql 扩展包 —— Compose 投影元数无关内核（W0-1 ⑦）
// BuildComposeCore/3Core 95% 同构收敛
// Internal/ 子目录为显式约定：后续新增 internal 内核类沿用 NCMapper.FreeSql.Internal 命名空间
// 内核无状态、不缓存——Type[] 默认引用相等，若以数组为缓存键将永不命中（每次 new[] 均新键），
// 缓存保留在强类型门面层
using System.Linq.Expressions;
using System.Reflection;
using NCMapper.Attributes;
using System.Diagnostics.CodeAnalysis;
using NCMapper.Internal;
using NCMapper.Runtime;

namespace NCMapper.FreeSql.Internal;

/// <summary>
/// Compose 投影元数无关内核 —— 2 表/3 表共用（W0-1 ⑦ 第二层收敛）
/// <para>承接以 Type[] 参数化统一 BuildComposeCore/BuildCompose3Core 的
/// 表达式树构建逻辑（主源校验 / 参数与字典 / bindings 循环 / Lambda 构造）；
/// 反射、解析、点路径、扁平化、类型转换继续委托共享内核 ProjectionExpressionFactory。</para>
/// </summary>
internal static class ComposeProjectionKernel
{
    /// <summary>
    /// 元数无关 Compose 投影构建 —— 2 表/3 表共用
    /// </summary>
    /// <param name="sourceTypes">源类型数组（2 表 [T1,T2] / 3 表 [T1,T2,T3]）</param>
    /// <param name="targetType">目标类型</param>
    /// <param name="strategy">投影策略（FreeSql 专属策略，由门面传入保持单一事实来源）</param>
    /// <returns>Compose 投影 Lambda（运行时元组参数化，门面按元数强转为强类型表达式）</returns>
    [RequiresUnreferencedCode("ComposeProjectionKernel scans type members and attributes via reflection. Some members may be trimmed.")]
    [RequiresDynamicCode("ComposeProjectionKernel builds expressions via MakeGenericMethod/MakeGenericType or Expression.GetDelegateType. Not supported in AOT.")]
    internal static LambdaExpression BuildComposeCoreN(Type[] sourceTypes, Type targetType, ProjectionStrategy strategy)
    {
        // 入口守卫：internal 契约显式化，防御空引用/空数组
        ArgumentNullException.ThrowIfNull(sourceTypes);
        ArgumentNullException.ThrowIfNull(targetType);
        ArgumentNullException.ThrowIfNull(strategy);
        if (sourceTypes.Length == 0)
            throw new ArgumentException("sourceTypes 不能为空数组。", nameof(sourceTypes));

        // 主源校验（特性对）：mapFromAttr.FromSource 必须命中任一泛型参数（按元数分派既有消息方法，文本逐字保留）
        // （缺口回补——消费面配合）：Fluent Compose 对（无 [MapFrom] 锚
        // MapFromAttribute 全槽 null）在判定面放行（可译性判定）后到达本内核，
        // 原 直接抛 MapFromNotApplied 属消费面缺口——改为 **Fluent 探测臂**：逐 sourceTypes
        // 探测 Fluent 明细条目（扩容判据同族），命中即以条目 SourceType 为主源继续（∈ sourceTypes
        // 由探测来源保证）；未命中 → 现状异常语义逐字保持（MapFromNotApplied）。
        var mapFromAttr = ProjectionExpressionFactory.GetCachedMapFromAttribute(targetType);
        Type primarySourceType;
        RuntimeMapperMetadata? entryMeta;
        if (mapFromAttr == null)
        {
            RuntimeMapperMetadata? fluentEntry = null;
            foreach (var st in sourceTypes)
            {
                if (RuntimeSnapshotStore.TryGetMappingDetail(st, targetType, out var probedFluent)
                    && probedFluent.Channel == RuntimeMappingChannel.Fluent
                    && (probedFluent.Properties.Length > 0 || probedFluent.ComposeMappings.Length > 0))
                {
                    fluentEntry = probedFluent;
                    break;
                }
            }
            if (fluentEntry == null)
                // Fluent 探测臂专属消息——原 MapFromNotApplied 文本的
                // "请先标注 [MapFrom]" 指引对 Fluent 对不适用（照做会制造 D1 双声明冲突）；
                // 指引改为"确认 Fluent 声明与查询源类型一致"
                throw new InvalidOperationException(
                    $"Fluent Compose 对（目标 {targetType.Name}）未在注册表命中：查询源类型参数中无任何类型为该对已注册的声明主源。" +
                    "请确认 Fluent 声明（cfg.For<S,T>()）已注册，且查询源类型与声明主源一致");
            primarySourceType = fluentEntry.SourceType;
            entryMeta = fluentEntry;
        }
        else
        {
            primarySourceType = mapFromAttr.FromSource;

            // 决策点 2：入口三态——明细命中
            //（TryGetMappingDetail 条目存在且 Properties 非空，判据逐字对齐 降级检测器同款判据）
            // 取 meta.SourceType 为主源；条目命中明细不可达 / miss → 回退现状反射值（mapFromAttr.FromSource）。
            // 特性对 meta.SourceType 与特性 FromSource 同源（编译期提取）→ 等价。
            // 决策点 7：判据扩为
            // Properties.Length>0 ∨ ComposeMappings 有效非空——Compose-only 声明对（Fluent Compose
            // 解锁后的多源合成 DTO）明细可达（与检测器/主链同批一致扩容；"跨批第三次触碰——提交须
            // 仅判据谓词扩展，其余 逻辑零触碰）。
            // 异常语义逐字保留（MapFromNotApplied / ComposeProjectionSourceTypeMismatch）。
            entryMeta = null;
            if (RuntimeSnapshotStore.TryGetMappingDetail(primarySourceType, targetType, out var probedMeta)
                && (probedMeta.Properties.Length > 0 || probedMeta.ComposeMappings.Length > 0))
            {
                entryMeta = probedMeta;
                primarySourceType = probedMeta.SourceType;
            }
        }

        if (!sourceTypes.Contains(primarySourceType))
        {
            var names = sourceTypes.Select(t => t.Name).ToArray();
            if (sourceTypes.Length == 2)
                throw new InvalidOperationException(
                    ErrorMessages.ComposeProjectionSourceTypeMismatch(primarySourceType.Name, names[0], names[1]));
            // 3 表（含未来 >3 表延展：ErrorMessages 仅提供 3 表方法，取前 3 个泛型参数名；X4 范围外不新增 public 成员）
            throw new InvalidOperationException(
                ErrorMessages.ComposeProjectionSourceTypeMismatch3(primarySourceType.Name, names[0], names[1], names[2]));
        }

        // 参数表达式（t1/t2/t3...）与 sourcePropsByType 字典（循环构建，消除 2 表 vs 3 表差异）
        var parameters = new ParameterExpression[sourceTypes.Length];
        var sourcePropsByType = new Dictionary<Type, (ParameterExpression Param, List<PropertyInfo> Props)>(sourceTypes.Length);
        for (var i = 0; i < sourceTypes.Length; i++)
        {
            parameters[i] = Expression.Parameter(sourceTypes[i], "t" + (i + 1));
            sourcePropsByType[sourceTypes[i]] = (parameters[i], ProjectionExpressionFactory.GetPublicInstanceProperties(sourceTypes[i]));
        }

        // Trace 前缀按元数参数化（"双表"/"三表" 与 "T1/T2 中"/"T1/T2/T3 中" 文本逐字保留）
        var traceTable = sourceTypes.Length == 2 ? "双表" : "三表";
        var traceScope = sourceTypes.Length == 2 ? "T1/T2 中" : "T1/T2/T3 中";

        var targetProps = ProjectionExpressionFactory.GetPublicInstanceProperties(targetType).Where(p => p.CanWrite).ToList();
        var bindings = new List<MemberBinding>();

        // 决策点 3/6：bindings 双分支
        // 快照腿（明细命中：ComposeMappings 跨表明细 → Properties 主源明细 → 矩阵 miss，
        // 命中分支**零特性读取**——IsMapIgnoreDefined/ShouldIgnore 特性腿不调，全局忽略腿直调保留）
        // 与兜底腿（条目命中明细不可达 / miss：现状逻辑原样——D2"miss/不可达路径保留原调用"）。
        // 与降级检测器 同构（明细权威 + 不可达/miss 兜底）。
        if (entryMeta != null)
        {
            // 明细索引（按 TargetPropertyName；**取首**——归档复查 P2-3 修：与主链 GroupBy First
            //（ProjectionExpressionFactory）/ 合并臂去重取先（RuntimeSnapshotStore MergeComposeMappings）
            // 三处统一为同胜出规则——消除"同一快照在单表 AsDto 与双表门面绑不同副源"的分叉）
            var composeByTarget = new Dictionary<string, RuntimeComposeMapping>(StringComparer.Ordinal);
            foreach (var cm in entryMeta.ComposeMappings)
            {
                if (!composeByTarget.ContainsKey(cm.TargetPropertyName))
                    composeByTarget[cm.TargetPropertyName] = cm;
            }
            var pmByTarget = new Dictionary<string, RuntimePropertyMapping>(StringComparer.Ordinal);
            foreach (var pm in entryMeta.Properties)
            {
                if (pm.Target?.Name != null) pmByTarget[pm.Target.Name] = pm;
            }
            var allSlotNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var slot in entryMeta.AllTargetSlots)
            {
                if (slot?.Name != null) allSlotNames.Add(slot.Name);
            }

            foreach (var targetProp in targetProps)
            {
                // ── ① ComposeMappings 明细（跨表声明——快照为权威，零特性读取）──
                if (composeByTarget.TryGetValue(targetProp.Name, out var cm))
                {
                    // 空组 = 忽略信号
                    if (cm.SourcePathNames.Length == 0)
                    {
                        if (MapTracer.GloballyEnabled) MapTracer.Write($"[NCMapper.FreeSql] ComposeProjection({traceTable}): 跳过属性 {targetProp.Name}（原因：Compose 明细空组=忽略信号）");
                        continue;
                    }

                    // 副源参数解析（决策点 3 + Q4 PoC 定稿）：SubSourceTypeRef Type 键优先；
                    // null（编译期非 public 不可达——NullableTypeOf 发 null）时按 SubSourceTypeName
                    //（编译期发射 FullName）与 sourceTypes FullName 精确匹配；0 命中/多命中 → fail-closed
                    //（Trace + 跳过绑定——不猜测、不按短名重试； 结论登记）
                    ParameterExpression? subParam;
                    if (cm.SubSourceTypeRef != null)
                    {
                        if (!sourcePropsByType.TryGetValue(cm.SubSourceTypeRef, out var subInfo))
                        {
                            if (MapTracer.GloballyEnabled) MapTracer.Write($"[NCMapper.FreeSql] ComposeProjection({traceTable}): 跳过属性 {targetProp.Name}（原因：副源类型 {cm.SubSourceTypeRef.Name} 不在 {traceScope}）");
                            continue;
                        }
                        subParam = subInfo.Param;
                    }
                    else
                    {
                        // Q4 PoC 实证：编译期 SubSourceTypeName = 符号显示串
                        //（TypeRef.FullName——嵌套类型用 '.' 分隔、可含可空注解），与运行时 Type.FullName
                        //（嵌套用 '+'）在嵌套类型上不一致——PoC 构造嵌套 internal 副源实跑证得 0 命中。
                        // 按"运行时归一全名"匹配（双方 '+'→'.' 归一后精确比较），0 命中/多命中
                        // → fail-closed（Trace + 跳过绑定——不猜测、不按短名重试）
                        var byName = sourceTypes
                            .Where(t => NamesMatchRuntimeNormalized(t.FullName, cm.SubSourceTypeName))
                            .ToArray();
                        if (byName.Length != 1 || !sourcePropsByType.TryGetValue(byName[0], out var subInfo2))
                        {
                            if (MapTracer.GloballyEnabled) MapTracer.Write($"[NCMapper.FreeSql] ComposeProjection({traceTable}): 跳过属性 {targetProp.Name}（原因：副源类型引用缺失，按名解析 {cm.SubSourceTypeName} 命中 {byName.Length} 条（{traceScope}），fail-closed 跳过）");
                            continue;
                        }
                        subParam = subInfo2.Param;
                    }

                    // 路径/索引重建（BuildSegmentsWithIndexes 同口径——与 pm 级同一重建单点）
                    var segs = ProjectionExpressionFactory.BuildSegmentsWithIndexes(cm.SourcePathNames, cm.SourceIndexes);
                    var subProps = sourcePropsByType[subParam.Type].Props;
                    Expression? valueExpr;
                    if (segs.Length == 1 && !segs[0].Contains('['))
                    {
                        var sp = ProjectionNameResolver.Resolve(subProps, segs[0]);
                        if (sp == null)
                        {
                            if (MapTracer.GloballyEnabled) MapTracer.Write($"[NCMapper.FreeSql] ComposeProjection({traceTable}): 跳过属性 {targetProp.Name}（原因：副源 {subParam.Type.Name} 中未找到声明字段 {segs[0]}）");
                            continue;
                        }
                        // 全局忽略腿直调（D2 两段式——源字段面；OverrideIgnore 豁免保留；特性面不读）
                        if (!PropertyIgnoreChecker.HasOverrideIgnore(targetProp)
                            && GlobalIgnoreChecker.ShouldIgnore(sp, isToDto: true))
                        {
                            if (MapTracer.GloballyEnabled) MapTracer.Write($"[NCMapper.FreeSql] ComposeProjection({traceTable}): 跳过属性 {targetProp.Name}（原因：源字段 {sp.Name} 命中全局忽略清单）");
                            continue;
                        }
                        valueExpr = Expression.Property(subParam, sp);
                    }
                    else
                    {
                        // 多段/带索引单段 → 点路径精确构建（TryBuild* 内部已做叶节点忽略判定——现状语义对齐）
                        var dotPath = string.Join(".", segs);
                        valueExpr = ProjectionExpressionFactory.TryBuildDotPathExpression(subParam, subParam.Type, dotPath, strategy, targetProp);
                        if (valueExpr == null)
                        {
                            if (MapTracer.GloballyEnabled) MapTracer.Write($"[NCMapper.FreeSql] ComposeProjection({traceTable}): 跳过属性 {targetProp.Name}（原因：Compose 明细路径 {dotPath} 无法构建点路径表达式）");
                            continue;
                        }
                    }

                    var converted = ProjectionExpressionFactory.ConvertProjectionType(valueExpr, targetProp.PropertyType, depth: 0, strategy);
                    if (converted == null)
                    {
                        if (MapTracer.GloballyEnabled) MapTracer.Write($"[NCMapper.FreeSql] ComposeProjection({traceTable}): 跳过属性 {targetProp.Name}（原因：类型 {valueExpr.Type.Name}→{targetProp.PropertyType.Name} 不可转换）");
                        continue;
                    }
                    bindings.Add(Expression.Bind(targetProp, converted));
                    continue;
                }

                // ── ② Properties 明细（主源字段——快照为权威，零特性读取）──
                if (pmByTarget.TryGetValue(targetProp.Name, out var pm))
                {
                    // pm 物化面方向判定（MapIgnoreDirection：0=Both/1=ToDto/2=ToEntity——投影正向 ToDto，
                    // ToEntity 方向照常绑定；对齐 FluentProjectionSplitTests 投影面既有语义）
                    if (pm.Target != null && pm.Target.HasMapIgnore && pm.Target.MapIgnoreDirection != 2)
                    {
                        if (MapTracer.GloballyEnabled) MapTracer.Write($"[NCMapper.FreeSql] ComposeProjection({traceTable}): 跳过属性 {targetProp.Name}（原因：明细方向忽略声明（{pm.Target.MapIgnoreDirection}）投影正向剔除）");
                        continue;
                    }

                    var pathNames = pm.SourcePathNames;
                    // ReverseOnly（无正向源）→ 跳过 + Trace（矩阵——提取期判定不映射；
                    // 特性对产物无此形态（提取期只物化有正向源 pm），防御分支保留）
                    if (pm.SourceName == null && pathNames is not { Length: > 0 })
                    {
                        if (MapTracer.GloballyEnabled) MapTracer.Write($"[NCMapper.FreeSql] ComposeProjection({traceTable}): 跳过属性 {targetProp.Name}（原因：明细无正向源（ReverseOnly））");
                        continue;
                    }

                    var mainInfo = sourcePropsByType[primarySourceType];
                    Expression? valueExpr;
                    if (pathNames is { Length: > 1 })
                    {
                        var dotPath = string.Join(".", ProjectionExpressionFactory.BuildSegmentsWithIndexes(pathNames, pm.SourceIndexes));
                        valueExpr = ProjectionExpressionFactory.TryBuildDotPathExpression(mainInfo.Param, primarySourceType, dotPath, strategy, targetProp);
                        if (valueExpr == null)
                        {
                            if (MapTracer.GloballyEnabled) MapTracer.Write($"[NCMapper.FreeSql] ComposeProjection({traceTable}): 跳过属性 {targetProp.Name}（原因：明细嵌套路径 {dotPath} 无法构建点路径表达式）");
                            continue;
                        }
                    }
                    else
                    {
                        // 单段（异名/同名显式/带索引重建同构——对齐主链 fluent pm 消费口径）
                        var single = pathNames is { Length: 1 }
                            ? ProjectionExpressionFactory.BuildSegmentsWithIndexes(pathNames, pm.SourceIndexes)[0]
                            : pm.SourceName!;
                        var sp = ProjectionNameResolver.Resolve(mainInfo.Props, single);
                        if (sp == null)
                        {
                            if (MapTracer.GloballyEnabled) MapTracer.Write($"[NCMapper.FreeSql] ComposeProjection({traceTable}): 跳过属性 {targetProp.Name}（原因：主源 {primarySourceType.Name} 中未找到声明字段 {single}）");
                            continue;
                        }
                        // 全局忽略腿直调（对应现状 判定位——D2 两段式）
                        if (!PropertyIgnoreChecker.HasOverrideIgnore(targetProp)
                            && GlobalIgnoreChecker.ShouldIgnore(sp, isToDto: true))
                        {
                            if (MapTracer.GloballyEnabled) MapTracer.Write($"[NCMapper.FreeSql] ComposeProjection({traceTable}): 跳过属性 {targetProp.Name}（原因：源字段 {sp.Name} 命中全局忽略清单）");
                            continue;
                        }
                        valueExpr = Expression.Property(mainInfo.Param, sp);
                    }

                    var converted = ProjectionExpressionFactory.ConvertProjectionType(valueExpr, targetProp.PropertyType, depth: 0, strategy);
                    if (converted == null)
                    {
                        if (MapTracer.GloballyEnabled) MapTracer.Write($"[NCMapper.FreeSql] ComposeProjection({traceTable}): 跳过属性 {targetProp.Name}（原因：类型 {valueExpr.Type.Name}→{targetProp.PropertyType.Name} 不可转换）");
                        continue;
                    }
                    bindings.Add(Expression.Bind(targetProp, converted));
                    continue;
                }

                // ── ③ 属性级 miss（矩阵——跳过 + Trace，提取期权威，不调特性读取、不按名重试）──
                if (MapTracer.GloballyEnabled)
                {
                    var reason = allSlotNames.Contains(targetProp.Name)
                        ? "明细无该目标属性映射声明（提取期权威——提取期豁免/诊断跳过/配置感知跳过收敛）"
                        : "目标属性不在快照 AllTargetSlots（Ignore(Both) 零痕迹）";
                    MapTracer.Write($"[NCMapper.FreeSql] ComposeProjection({traceTable}): 跳过属性 {targetProp.Name}（原因：{reason}）");
                }
                continue;
            }
        }
        else
        {
            // ── 兜底腿（条目命中明细不可达 / miss——现状逻辑原样；D2 决策点 6"miss/不可达路径保留原调用"）──
            foreach (var targetProp in targetProps)
            {
            if (ProjectionExpressionFactory.IsMapIgnoreDefined(targetProp)) continue;
            if (PropertyMatcher.ShouldIgnore(targetProp, isToDto: true, PropertyIgnoreChecker.HasOverrideIgnore(targetProp))) continue;

            PropertyMatcher.TryGetMapPropertyInfo(targetProp, primarySourceType,
                out string sourceFieldName, out Type fieldSourceType, out _);

            if (!sourcePropsByType.TryGetValue(fieldSourceType, out var sourceInfo))
            {
                if (MapTracer.GloballyEnabled) MapTracer.Write($"[NCMapper.FreeSql] ComposeProjection({traceTable}): 跳过属性 {targetProp.Name}（原因：字段来源类型 {fieldSourceType.Name} 不在 {traceScope}）");
                continue;
            }

            var sourceProp = ProjectionNameResolver.Resolve(sourceInfo.Props, sourceFieldName);
            if (sourceProp == null)
            {
                // Compose 点路径 + 推导扁平化——与单表顶层同语义
                // （复用 TryResolveSegmentChain 共用循环；TryBuild* 内部已做叶节点忽略判定，命中返回 null）
                Expression? pathExpr = null;
                if (sourceFieldName.Contains('.'))
                    pathExpr = ProjectionExpressionFactory.TryBuildDotPathExpression(
                        sourceInfo.Param, fieldSourceType, sourceFieldName, strategy, targetProp);
                pathExpr ??= ProjectionExpressionFactory.TryBuildFlattenedExpression(
                    sourceInfo.Param, fieldSourceType, sourceFieldName, targetProp, strategy);
                if (pathExpr != null)
                {
                    var pathConverted = ProjectionExpressionFactory.ConvertProjectionType(
                        pathExpr, targetProp.PropertyType, depth: 0, strategy);
                    if (pathConverted != null)
                    {
                        bindings.Add(Expression.Bind(targetProp, pathConverted));
                        continue;
                    }
                }

                if (MapTracer.GloballyEnabled) MapTracer.Write($"[NCMapper.FreeSql] ComposeProjection({traceTable}): 跳过属性 {targetProp.Name}（原因：来源类型 {fieldSourceType.Name} 中未找到字段 {sourceFieldName}，且点路径/扁平化均无匹配路径）");
                continue;
            }

            if (ProjectionExpressionFactory.ShouldIgnoreSource(sourceProp, targetProp))
            {
                if (MapTracer.GloballyEnabled) MapTracer.Write($"[NCMapper.FreeSql] ComposeProjection({traceTable}): 跳过属性 {targetProp.Name}（原因：源字段 {sourceProp.Name} 命中全局忽略清单）");
                continue;
            }

            var valueExpr = Expression.Property(sourceInfo.Param, sourceProp);
            var convertedExpr = ProjectionExpressionFactory.ConvertProjectionType(valueExpr, targetProp.PropertyType, depth: 0, strategy);
            if (convertedExpr == null)
            {
                if (MapTracer.GloballyEnabled) MapTracer.Write($"[NCMapper.FreeSql] ComposeProjection({traceTable}): 跳过属性 {targetProp.Name}（原因：类型 {valueExpr.Type.Name}→{targetProp.PropertyType.Name} 不可转换）");
                continue;
            }

            bindings.Add(Expression.Bind(targetProp, convertedExpr));
            }
        }

        if (bindings.Count == 0)
            throw new InvalidOperationException(
                ErrorMessages.NoMappablePropertiesForProjection(targetType.Name));

        var defaultCtor = ProjectionExpressionFactory.GetParameterlessCtor(targetType);
        if (defaultCtor == null)
            throw new InvalidOperationException(
                ErrorMessages.ProjectionRequiresParameterlessConstructor(targetType.Name));

        // Lambda 构造：显式委托类型（GetDelegateType 末位为返回类型），门面强转 Expression<Func<...>> 恒成立
        var body = Expression.MemberInit(Expression.New(defaultCtor), bindings);
        var delegateType = Expression.GetDelegateType(sourceTypes.Append(targetType).ToArray());
        return Expression.Lambda(delegateType, body, parameters);
    }

    /// <summary>
    /// 运行时归一全名匹配
    /// 编译期 <c>SubSourceTypeName</c>（TypeRef.FullName 符号显示串：嵌套 '.' 分隔）与运行时
    /// <c>Type.FullName</c>（嵌套 '+' 分隔）在嵌套类型上口径不一致；两侧 '+'→'.' 归一后
    /// 序数精确比较。仅用于 SubSourceTypeRef 缺失时的按名解析（TypeRef 在场走 Type 直比不经此）。
    /// </summary>
    private static bool NamesMatchRuntimeNormalized(string? runtimeFullName, string? compileTimeDisplayName)
    {
        if (runtimeFullName is null || compileTimeDisplayName is null) return false;
        if (string.Equals(runtimeFullName, compileTimeDisplayName, StringComparison.Ordinal)) return true;
        return string.Equals(
            runtimeFullName.Replace('+', '.'),
            compileTimeDisplayName.Replace('+', '.'),
            StringComparison.Ordinal);
    }
}
