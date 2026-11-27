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

        // 路线四（TB-20261127-01 卡 C1）：把用户 join 的源参数集下传给共享内核（唯一改动行，captain 授权）。
        using var scope = ComposeNestedSourceScope.Push(sourceTypes, parameters);

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
                // ③-b 嵌套/集合 DTO 例外（TB-20261127-01 卡 C2 —— 递归入口保留）：
                // 对齐单表链 ProjectionExpressionFactory.cs:380-396 / 1278-1293 的「⑤ 属性级 miss」同口径——
                // 目标属性 ∈ AllTargetSlots 但 ∉ Properties 时，若其类型是**已注册的嵌套/集合 DTO**，
                // 其映射语义由子对明细承载，必须递归进入嵌套投影构建，而不是在此直接跳过（跳过即嵌套投影断链，
                // 缺陷④：同一份元数据在单表能投影嵌套、Compose 却整段跳过）。
                // 注意：不在 AllTargetSlots 的目标属性**不走**本例外（Ignore(Both) 零痕迹，语义上确为剔除）；
                // 单表链同款——其 slot 差集判定（:242-246）先于 ⑤ 分支执行。
                // 递归进入后：引用型嵌套的源经 ComposeNestedSourceScope.RequireJoinedSource 改指**用户 join 的
                // 该 DTO 主源参数**（路线四 C1 已交付机制，本卡不改其代码），嵌套绑定再经
                // BuildNestedProjection → RedirectNestedSource 保持同一 join 参数，不再按 [Navigate] 自建 JOIN。
                if (allSlotNames.Contains(targetProp.Name)
                    && (RuntimeSnapshotStore.IsRegisteredDto(targetProp.PropertyType)
                        || ContainsRegisteredDtoElement(targetProp.PropertyType)))
                {
                    var nestedRecursiveExpr = BuildNestedRecursiveValueForMiss(
                        targetProp, primarySourceType, sourcePropsByType[primarySourceType], strategy);
                    if (nestedRecursiveExpr == null)
                    {
                        if (MapTracer.GloballyEnabled) MapTracer.Write($"[NCMapper.FreeSql] ComposeProjection({traceTable}): 跳过属性 {targetProp.Name}（原因：嵌套/集合投影构建失败）");
                        continue;
                    }
                    bindings.Add(Expression.Bind(targetProp, nestedRecursiveExpr));
                    continue;
                }

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
    /// 属性级 miss（<c>∈ AllTargetSlots ∧ ∉ Properties</c>）的**嵌套/集合 DTO 递归入口**取值
    /// —— 对齐单表链 <c>ProjectionExpressionFactory.BuildProjectionValueExpression</c> 的同一取值链
    /// （TB-20261127-01 卡 C2）。
    /// <para><b>为什么必须递归</b>：该目标属性的映射语义由**子对明细**（如 <c>Dept → DeptDto</c>）承载，
    /// 顶层明细里没有它自己的 <c>Properties</c> 条目是正常形态（源侧异名 <c>Department→Dept</c>、
    /// 自动匹配收敛等）。在此直接跳过即「嵌套投影断链」——同一份元数据单表能投影、Compose 整段跳过（缺陷④）。</para>
    /// <para><b>源改指（路线四 / C1 已交付机制，本卡不改其代码）</b>：Compose 作用域内（本内核
    /// <c>:122-123</c> 已 Push）先按嵌套 DTO 的**已注册主源类型**经
    /// <see cref="ComposeNestedSourceScope.RequireJoinedSource"/> 取**用户 join 的该源参数**（Strict：
    /// 声明了却没 join 属配置错，fail-closed 抛 <see cref="MapConfigurationException"/>，不静默空值）；
    /// 取到后交 <see cref="ProjectionExpressionFactory.ConvertProjectionType"/> 递归进入嵌套投影构建，
    /// 其内部 <c>BuildNestedProjection</c> 再经 <see cref="ComposeNestedSourceScope.RedirectNestedSource"/>
    /// 保持同一 join 参数——ORM 不再按 <c>[Navigate]</c> 自建第二条 JOIN（缺陷③）。</para>
    /// <para><b>非引用型（集合元素 DTO）</b>：按声明面/命名约定解析源表达式后同样交
    /// <see cref="ProjectionExpressionFactory.ConvertProjectionType"/> 分类（集合投影关闭时由策略自行跳过，
    /// 与单表链同口径）。</para>
    /// </summary>
    /// <returns>转换后的绑定表达式；无法构建时为 null（调用方 Trace + 跳过）</returns>
    [RequiresUnreferencedCode("BuildNestedRecursiveValueForMiss resolves nested sources via reflection (attributes, navigation properties, member scanning). Some members may be trimmed.")]
    [RequiresDynamicCode("BuildNestedRecursiveValueForMiss builds expressions via MakeGenericMethod/MakeGenericType. Not supported in AOT.")]
    private static Expression? BuildNestedRecursiveValueForMiss(
        PropertyInfo targetProp, Type primarySourceType,
        (ParameterExpression Param, List<PropertyInfo> Props) primaryInfo,
        ProjectionStrategy strategy)
    {
        // ── ① 引用型嵌套 DTO：源改指用户 join 的该 DTO 主源参数（与单表链 BuildProjectionValueExpression
        //    首段同口径——C1 路线四唯一改动面之外的调用点，本卡只「进入」不改其逻辑）──
        if (RuntimeSnapshotStore.IsRegisteredDto(targetProp.PropertyType))
        {
            var nestedPrimarySource = RuntimeSnapshotStore.GetPrimarySource(targetProp.PropertyType);
            if (nestedPrimarySource != null)
            {
                var joinedNestedSource = ComposeNestedSourceScope.RequireJoinedSource(
                    nestedPrimarySource, targetProp.Name, strategy.TracePrefix);
                if (joinedNestedSource != null)
                    return ProjectionExpressionFactory.ConvertProjectionType(
                        joinedNestedSource, targetProp.PropertyType, depth: 0, strategy);
            }
        }

        // ── ② 声明面/命名约定解析源表达式（对齐 BuildProjectionValueExpression 主流程：
        //    [MapProperty]/Fluent 明细退化形态 → 按名 → 点路径 → 扁平化；跨表则取 join 参数或导航）──
        PropertyMatcher.TryGetMapPropertyInfo(targetProp, primarySourceType,
            out var sourceFieldName, out var fieldSourceType, out _);

        Expression? sourceExpr;
        if (fieldSourceType == primarySourceType)
        {
            var srcProp = ProjectionNameResolver.Resolve(primaryInfo.Props, sourceFieldName);
            if (srcProp != null)
            {
                if (ProjectionExpressionFactory.ShouldIgnoreSource(srcProp, targetProp)) return null;
                sourceExpr = Expression.Property(primaryInfo.Param, srcProp);
            }
            else
            {
                sourceExpr = ProjectionExpressionFactory.TryBuildDotPathExpression(
                    primaryInfo.Param, primarySourceType, sourceFieldName, strategy, targetProp);
                sourceExpr ??= ProjectionExpressionFactory.TryBuildFlattenedExpression(
                    primaryInfo.Param, primarySourceType, sourceFieldName, targetProp, strategy);
            }
        }
        else
        {
            // 跨表：Compose 作用域内取用户 join 参数；作用域外（恒返回 null）回退导航推断——
            // 与共享内核同款两级 API 语义（Soft 回退 / Strict 已在 ① 面消费）
            var joinedFieldSource = ComposeNestedSourceScope.RequireJoinedSource(
                fieldSourceType, targetProp.Name, strategy.TracePrefix);
            Expression fieldSourceExpr;
            if (joinedFieldSource != null)
            {
                fieldSourceExpr = joinedFieldSource;
            }
            else
            {
                var navProp = NavigationResolver.ResolveNavigationProperty(
                    primaryInfo.Props, fieldSourceType, primarySourceType.Name);
                if (navProp == null) return null;
                fieldSourceExpr = Expression.Property(primaryInfo.Param, navProp);
            }

            var navProps = ProjectionExpressionFactory.GetPublicInstanceProperties(fieldSourceType);
            var navField = ProjectionNameResolver.Resolve(navProps, sourceFieldName);
            if (navField == null) return null;
            if (ProjectionExpressionFactory.ShouldIgnoreSource(navField, targetProp)) return null;
            sourceExpr = Expression.Property(fieldSourceExpr, navField);
        }

        if (sourceExpr == null) return null;

        // ── ③ 交共享内核分类：同类型/隐式转换/嵌套 DTO 递归/集合元素（策略关闭时自行跳过）──
        return ProjectionExpressionFactory.ConvertProjectionType(
            sourceExpr, targetProp.PropertyType, depth: 0, strategy);
    }

    /// <summary>
    /// 判定类型是否为「元素为已注册 DTO 的集合/可枚举形态」（属性级 miss 的集合豁免判定：
    /// <c>List&lt;NestedDto&gt;</c> / <c>NestedDto[]</c> 等形态 → 递归入口保留）。
    /// <para>与共享内核 <c>ProjectionExpressionFactory.ContainsRegisteredDtoElement</c> 同口径
    /// （该处为 private，本包不可直调故镜像；判据：单泛型参数/数组元素为非值类型且非 string
    /// 且已注册为映射 DTO）。非泛型、非数组、值类型元素、string 元素 → false。</para>
    /// </summary>
    private static bool ContainsRegisteredDtoElement(Type type)
    {
        Type? elementType = null;
        if (type.IsGenericType)
        {
            var genArgs = type.GetGenericArguments();
            if (genArgs.Length == 1 && !genArgs[0].IsValueType && genArgs[0] != typeof(string))
                elementType = genArgs[0];
        }
        else if (type.IsArray)
        {
            var et = type.GetElementType();
            if (et != null && !et.IsValueType && et != typeof(string))
                elementType = et;
        }
        return elementType != null && RuntimeSnapshotStore.IsRegisteredDto(elementType);
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
