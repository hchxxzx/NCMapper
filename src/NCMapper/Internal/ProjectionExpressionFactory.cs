// : ORM 投影内核统一 —— 投影表达式工厂（两库共享内核）
// 从 EfCoreProjectionBuilder 提取（提取前行为锁定：T-A 双端一致性矩阵）。
// 策略差异通过 ProjectionStrategy 参数化；Trace 前缀通过 strategy.TracePrefix。
// 反射缓存移入内核（s_propertiesCache/s_ctorCache/s_mapIgnoreDefinedCache/s_mapFromAttrCache 与策略无关共用；
// s_cache 键含 strategy，两库同 (Source,Target) 互不串用）。
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using NCMapper.Attributes;
using NCMapper.Runtime;
using NCMapper.Shared;
using System.Diagnostics.CodeAnalysis;

namespace NCMapper.Internal
{
    /// <summary>
    /// 投影表达式工厂 —— 构建 ORM 兼容的 Expression&lt;Func&lt;TSource, TTarget&gt;&gt;
    /// <para>EFCore/FreeSql 共享内核：两库构建器薄壳化后仅声明策略常量 + 调用本工厂</para>
    /// <para>internal 类型，经 InternalsVisibleTo 供 NCMapper.EFCore / NCMapper.FreeSql 扩展包使用</para>
    /// </summary>
    internal static class ProjectionExpressionFactory
    {
        /// <summary>
        /// Projection 表达式缓存 —— key: (策略, 源类型, 目标类型), value: 版本化条目
        /// <para>键含策略：EFCore/FreeSql 策略不同（深度 8/3 等），同 (Source,Target) 必须各自缓存</para>
        /// <para> 决策点 6：值改版本化条目
        /// <see cref="ProjectionCacheEntry"/>——命中条件扩为 <c>Version == RuntimeSnapshotStore.ContentVersion</c>
        /// （注册表内容版本失配重算），写路径改索引器（AddOrUpdate 语义——替换旧条目，消除
        /// <c>TryAdd</c> 不覆盖导致的"失配重算后旧条目永驻"性能塌陷）；先例 <c>DegradationCacheEntry</c>
        ///（DegradationDetectorBase.cs 同族， / 双条件先行）。</para>
        /// </summary>
        private sealed class ProjectionCacheEntry
        {
            public long Version;
            public required LambdaExpression Lambda;
        }

        private static readonly ConcurrentDictionary<(ProjectionStrategy Strategy, Type Source, Type Target), ProjectionCacheEntry> s_cache = new();

        /// <summary>
        /// 公共实例属性列表缓存（排除索引器）
        /// </summary>
        private static readonly ConcurrentDictionary<Type, List<PropertyInfo>> s_propertiesCache = new();

        /// <summary>
        /// 路径语义归一解析器— 段循环判据收敛为 TypePathResolver
        /// （③ TryResolveSegmentChain 薄包装消费；无状态安全）
        /// </summary>
        private static readonly TypePathResolver s_typeResolver = new();

        /// <summary>
        /// 无参公共构造器缓存
        /// </summary>
        private static readonly ConcurrentDictionary<Type, ConstructorInfo?> s_ctorCache = new();

        /// <summary>
        /// MapIgnoreAttribute 是否定义缓存
        /// </summary>
        private static readonly ConcurrentDictionary<PropertyInfo, bool> s_mapIgnoreDefinedCache = new();

        /// <summary>
        /// MapFromAttribute 缓存
        /// </summary>
        private static readonly ConcurrentDictionary<Type, MapFromAttribute?> s_mapFromAttrCache = new();

        /// <summary>
        /// Enumerable 开放泛型方法定义缓存。
        /// <para>静态缓存一次 GetMethods 枚举查找（先例 ExpressionBuilder.s_elementAtOrDefaultDef，N-09），
        /// 构建时仅 MakeGenericMethod（原每次构建 3 处 GetMethods 枚举 + LINQ First 闭包匹配）。
        /// 反射查找发生在静态初始化（消费方法已带 Requires* 标注——方法级标注承载，
        /// 初始化器由编译器生成 .cctor 承载，同构先例预期零新增 IL 告警）。</para>
        /// <para><b>双处显式消歧</b><c>Select</c>有 Func&lt;,&gt;/Func&lt;,,&gt;
        /// 两个 2 参重载、<c>ElementAtOrDefault</c> 有 Int32/Index 两个 2 参重载，元数据顺序非契约保证——
        /// 分别消歧为非索引 Func&lt;,&gt; 形态与 Int32 形态（当前实际选中的形态，行为零变化）；
        /// <c>ToList</c> 匹配唯一，条件逐字保持。</para>
        /// </summary>
        private static readonly MethodInfo s_elementAtOrDefaultDef =
            typeof(Enumerable).GetMethods().First(m => m.Name == nameof(Enumerable.ElementAtOrDefault) && m.GetParameters().Length == 2
                && m.GetParameters()[1].ParameterType == typeof(int));

        private static readonly MethodInfo s_selectDef =
            typeof(Enumerable).GetMethods().First(m => m.Name == nameof(Enumerable.Select) && m.GetParameters().Length == 2
                && m.GetParameters()[1].ParameterType.GetGenericTypeDefinition() == typeof(Func<,>));

        private static readonly MethodInfo s_toListDef =
            typeof(Enumerable).GetMethods().First(m => m.Name == nameof(Enumerable.ToList) && m.GetParameters().Length == 1);

        /// <summary>
        /// 构建 Projection 表达式 —— 用于 ORM 查询时直接映射为 DTO，一条 SQL 完成查询
        /// <para><b>Null 传播</b>：导航属性访问自动生成 null 安全表达式，ORM 翻译为 LEFT JOIN + COALESCE</para>
        /// </summary>
        [RequiresUnreferencedCode("Build scans type members and attributes via reflection. Some members may be trimmed.")]
        [RequiresDynamicCode("Build builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
        internal static Expression<Func<TSource, TTarget>> Build<TSource, TTarget>(ProjectionStrategy strategy)
            where TSource : class
            where TTarget : class
        {
            var cacheKey = (strategy, typeof(TSource), typeof(TTarget));
            // 决策点 6：条目内版本校验（ContentVersion 失配重算）+ 索引器写路径（替换旧条目）
            var contentVersion = Runtime.RuntimeSnapshotStore.ContentVersion;
            if (s_cache.TryGetValue(cacheKey, out var cached) && cached.Version == contentVersion)
                return (Expression<Func<TSource, TTarget>>)cached.Lambda;

            // 路线四（TB-20261127-01 卡 C1）作用域边界：单表 Build 是「单源」语义，绝不可继承
            // 外层 Compose 作用域（否则 Compose 明细/自动匹配路径中夹带的一次单表 Build 会把嵌套
            // 错误改指到 join 参数上）。Push 空作用域 = 显式屏蔽，退出即还原。
            Expression<Func<TSource, TTarget>> lambda;
            using (ComposeNestedSourceScope.PushEmpty())
                lambda = BuildCore<TSource, TTarget>(strategy);
            s_cache[cacheKey] = new ProjectionCacheEntry { Version = contentVersion, Lambda = lambda };
            return lambda;
        }

        /// <summary>
        /// 清除表达式缓存 —— 供测试隔离调用（经两库薄壳的 ClearCache 暴露）
        /// </summary>
        internal static void ClearCache()
        {
            s_cache.Clear();
            s_propertiesCache.Clear();
            s_ctorCache.Clear();
            s_mapIgnoreDefinedCache.Clear();
            s_mapFromAttrCache.Clear();
        }

        /// <summary>
        /// Projection 表达式缓存条目数（internal 测试钩子：
        /// FreeSql-only 清理闭环断言的观测点，防漏登记测试经 InternalsVisibleTo 读取）
        /// </summary>
        internal static int CacheEntryCount => s_cache.Count;

        /// <summary>
        /// 核心构建逻辑 —— 遍历目标属性，匹配源属性，组装 MemberInit 表达式
        /// </summary>
        [RequiresUnreferencedCode("BuildCore scans type members and attributes via reflection. Some members may be trimmed.")]
        [RequiresDynamicCode("BuildCore builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
        private static Expression<Func<TSource, TTarget>> BuildCore<TSource, TTarget>(ProjectionStrategy strategy)
            where TSource : class
            where TTarget : class
        {
            var sourceType = typeof(TSource);
            var targetType = typeof(TTarget);

            // 主映射源读取（T1 冒烟前置/T6 改造点）：
            // 注册表优先（FindByPair 命中→编译期已知 (S,T) 对；未命中→GetPrimarySource 多源兜底）
            // + 反射回退（发射关闭/未注册时与改造前现状路径逐字节一致）——
            // Fluent DTO 命中注册表后不再抛 MapFromNotApplied（缺口消除）
            var primarySourceType = Runtime.RuntimeSnapshotStore.FindByPair(sourceType, targetType)?.SourceType
                ?? Runtime.RuntimeSnapshotStore.GetPrimarySource(targetType);
            if (primarySourceType == null)
            {
                var mapFromAttr = GetCachedMapFromAttribute(targetType);
                if (mapFromAttr == null)
                    throw new InvalidOperationException(
                        ErrorMessages.MapFromNotApplied(targetType.Name, "Projection"));
                primarySourceType = mapFromAttr.FromSource;
            }

            if (primarySourceType != sourceType)
                throw new InvalidOperationException(
                    ErrorMessages.ProjectionSourceTypeMismatch(sourceType.Name, primarySourceType.Name));

            var srcParam = Expression.Parameter(sourceType, "src");
            var sourceProps = GetPublicInstanceProperties(sourceType);
            var targetProps = GetPublicInstanceProperties(targetType).Where(p => p.CanWrite).ToList();

            // 构建属性绑定列表
            var bindings = BuildProjectionBindings(sourceType, targetType, srcParam, sourceProps, targetProps, strategy);
            if (bindings.Count == 0)
                throw new InvalidOperationException(
                    ErrorMessages.NoMappablePropertiesForProjection(targetType.Name));

            // 构建 MemberInit（ORM 翻译器识别的格式）
            var defaultCtor = GetParameterlessCtor(targetType);
            if (defaultCtor == null)
                throw new InvalidOperationException(
                    ErrorMessages.ProjectionRequiresParameterlessConstructor(targetType.Name));

            return Expression.Lambda<Func<TSource, TTarget>>(
                Expression.MemberInit(Expression.New(defaultCtor), bindings), srcParam);
        }

        /// <summary>
        /// 构建 Projection 属性绑定列表 —— 遍历目标属性，匹配源属性并生成 MemberBinding
        /// <para> 决策点 1/2/3/4/7：双分支
        /// 快照腿（明细命中：`TryGetMappingDetail(sourceType, targetType)` 按对精确 + 三态判据
        /// <c>Properties.Length &gt; 0 ∨ ComposeMappings 非空</c>〔 扩容〕；ComposeMappings 跨表分流 → Properties 主源明细 →
        /// 矩阵 miss，命中分支<b>不走特性扫描腿</b>——IsMapIgnoreDefined/ShouldIgnore 特性腿不调，
        /// 全局面 IsTreeChildrenSlot/扁平化语义保留；路径解析经 TryBuildDotPathExpression/BuildSingleSourceProjection
        /// 以明细输入承载——函数内部特性面收口登记#27〔归档复查 P2-1——本注释原"零特性读取"表述过度，订正〕）
        /// 与兜底腿（条目命中明细不可达 / miss：现状逻辑
        /// 原样——D2"miss/不可达路径保留原调用"）。与降级检测器 /内核 同构。</para>
        /// </summary>
        [RequiresUnreferencedCode("BuildProjectionBindings scans type members and attributes via reflection. Some members may be trimmed.")]
        [RequiresDynamicCode("BuildProjectionBindings builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
        private static List<MemberBinding> BuildProjectionBindings(
            Type sourceType, Type targetType, ParameterExpression srcParam,
            List<PropertyInfo> sourceProps, List<PropertyInfo> targetProps,
            ProjectionStrategy strategy)
        {
            var bindings = new List<MemberBinding>();

            // 决策点 1：三态 + 键控按对化——既有 TryGetFluentDetailMeta(target)（target 任意源命中）
            // 升级为 TryGetMappingDetail(sourceType, targetType)（按对精确 + 三态判据，逐字对齐门面语义）。
            // 明细命中（三态①）→ detailPmByName/detailSlotNames（键控目标级→对级，双通道语义——
            // 特性对与 Fluent 对统一消费）；三态②③ → null → 兜底腿（特性解析原样）。
            // 差异面 D-b/D-c 预登记。
            Runtime.RuntimeSnapshotStore.TryGetMappingDetail(sourceType, targetType, out var entryMeta);
            // 决策点 7：三态② 收窄
            // Properties 与 ComposeMappings 均空才视为明细不可达（Compose-only 声明对可投影）。
            if (entryMeta is { Properties.Length: 0, ComposeMappings.Length: 0 }) entryMeta = null; // 三态②条目命中明细不可达 → 兜底
            var detailPmByName = entryMeta == null
                ? null
                : entryMeta.Properties
                    .Where(p => p.Target != null)
                    .GroupBy(p => p.Target!.Name)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var detailSlotNames = entryMeta == null
                ? null
                : new HashSet<string>(entryMeta.AllTargetSlots.Select(s => s.Name), StringComparer.Ordinal);
            // 决策点 2：跨表分流索引（ComposeMappings 按 TargetPropertyName； 归一口经 定稿）
            var composeByTarget = entryMeta == null
                ? null
                : entryMeta.ComposeMappings.Length == 0
                    ? null
                    : entryMeta.ComposeMappings
                        .GroupBy(c => c.TargetPropertyName)
                        .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            foreach (var targetProp in targetProps)
            {
                // 树挂载位契约（A4；D10 豁免域——非特性面，两腿共享保留直调）：
                // [MapTree] 命中 ChildrenProperty 的自引用集合不绑定投影（由 TreeBuilder 填充）——
                // 防 EFCore 嵌套投影预填与 TreeBuilder 二次挂载（双挂载）。
                if (IsTreeChildrenSlot(targetProp, targetType)) continue;

                // ── 快照腿（明细命中——零特性读取）──
                if (detailSlotNames != null)
                {
                    // ① slot 差集重建（:205 语义保持）：可写目标属性不在 AllTargetSlots =
                    // Ignore(Both)（发射零痕迹）或矛盾门/Compose 空组剔除 → 绑定跳过
                    if (!detailSlotNames.Contains(targetProp.Name))
                    {
                        MapTracer.Write($"{strategy.TracePrefix} 跳过目标属性 {targetProp.Name}（原因：明细声明面忽略/矛盾门剔除——slot 差集重建）");
                        continue;
                    }
                    // ② 方向声明（pm 物化面——投影恒正向：Both/ToDto 跳过，ToEntity 照常绑定）
                    if (detailPmByName!.TryGetValue(targetProp.Name, out var dirPm)
                        && dirPm.Target != null
                        && dirPm.Target.HasMapIgnore
                        && dirPm.Target.MapIgnoreDirection != 2) // 0=Both/1=ToDto → 正向跳过；2=ToEntity → 照常
                    {
                        MapTracer.Write($"{strategy.TracePrefix} 跳过目标属性 {targetProp.Name}（原因：明细 Ignore 方向声明命中正向）");
                        continue;
                    }
                    // ③ 跨表分流（决策点 2）：ComposeMappings 命中（按 TargetPropertyName）
                    if (composeByTarget != null && composeByTarget.TryGetValue(targetProp.Name, out var cm))
                    {
                        if (cm.SourcePathNames.Length == 0)
                        {
                            MapTracer.Write($"{strategy.TracePrefix} 跳过目标属性 {targetProp.Name}（原因：Compose 明细空组=忽略信号）");
                            continue;
                        }
                        // 副源取值（路线四 TB-20261127-01 卡 C1）：
                        // ① Compose 通道（ComposeNestedSourceScope 作用域内）→ 副源改指**用户 join 的源参数**
                        //    （不再按 [Navigate] 自建 JOIN——缺陷③：软删泄漏／非主键串行）；匹配不到 fail-closed。
                        // ② 非 Compose 通道（单表 AsDto/EFCore/Dapper，作用域外恒返回 null）→ 既有语义逐字不变：
                        //    TypeRef 直比；缺失（internal 副源发射形态）→ 按主源导航属性类型归一匹配（Q4 D-11 同口径）。
                        Type? subType = cm.SubSourceTypeRef;
                        PropertyInfo? navProp = null;
                        Expression? subSrcExpr = null;
                        // 作用域内副源表达式为源参数本身；作用域外为导航链（原 src.Nav）
                        Expression? joinedSubSource = subType != null
                            ? ComposeNestedSourceScope.RequireJoinedSource(subType, targetProp.Name, strategy.TracePrefix)
                            : ComposeNestedSourceScope.RequireJoinedSourceByName(cm.SubSourceTypeName, targetProp.Name, strategy.TracePrefix);
                        if (joinedSubSource != null)
                        {
                            subSrcExpr = joinedSubSource;
                        }
                        else
                        {
                            if (subType != null)
                            {
                                navProp = NavigationResolver.ResolveNavigationProperty(sourceProps, subType, sourceType.Name);
                            }
                            else
                            {
                                navProp = sourceProps.FirstOrDefault(p =>
                                    NamesMatchRuntimeNormalized(p.PropertyType.FullName, cm.SubSourceTypeName));
                            }
                            if (navProp == null)
                            {
                                MapTracer.Write($"{strategy.TracePrefix} 跳过目标属性 {targetProp.Name}（原因：副源 {cm.SubSourceTypeName} 在主源 {sourceType.Name} 上未找到导航属性，fail-closed）");
                                continue;
                            }
                            subSrcExpr = Expression.Property(srcParam, navProp);
                        }

                        // 路径/索引重建（与 pm 级同一重建单点）
                        var segs = BuildSegmentsWithIndexes(cm.SourcePathNames, cm.SourceIndexes);
                        var subSrcParam = subSrcExpr;
                        // 副源类型：作用域外为导航属性类型；作用域内为 join 参数类型（二者一致，统一取值避免双源）
                        var subSrcType = subSrcExpr.Type;
                        Expression? cmExpr;
                        if (segs.Length == 1 && !segs[0].Contains('['))
                        {
                            var subProps = GetPublicInstanceProperties(subSrcType);
                            var sp = ProjectionNameResolver.Resolve(subProps, segs[0]);
                            if (sp == null)
                            {
                                MapTracer.Write($"{strategy.TracePrefix} 跳过目标属性 {targetProp.Name}（原因：副源 {subSrcType.Name} 中未找到声明字段 {segs[0]}）");
                                continue;
                            }
                            cmExpr = Expression.Property(subSrcParam, sp);
                        }
                        else
                        {
                            var dotPath = string.Join(".", segs);
                            cmExpr = TryBuildDotPathExpression(subSrcParam, subSrcType, dotPath, strategy, targetProp);
                            if (cmExpr == null)
                            {
                                MapTracer.Write($"{strategy.TracePrefix} 跳过目标属性 {targetProp.Name}（原因：Compose 明细路径 {dotPath} 无法构建点路径表达式）");
                                continue;
                            }
                        }

                        var cmConverted = ConvertProjectionType(cmExpr, targetProp.PropertyType, depth: 0, strategy);
                        if (cmConverted == null)
                        {
                            MapTracer.Write($"{strategy.TracePrefix} 跳过目标属性 {targetProp.Name}（原因：类型 {cmExpr.Type.Name}→{targetProp.PropertyType.Name} 不可转换）");
                            continue;
                        }
                        bindings.Add(Expression.Bind(targetProp, cmConverted));
                        continue;
                    }
                    // ④ Properties 明细（主源——pm 未命中即矩阵 miss，不回退特性）
                    if (detailPmByName!.TryGetValue(targetProp.Name, out var pm))
                    {
                        var pathNames = pm.SourcePathNames;
                        // ReverseOnly（无正向源）→ 跳过 + Trace（矩阵——提取期判定不映射；
                        // 正向快照 pm 恒有正向源，防御分支保留）
                        if (pm.SourceName == null && pathNames is not { Length: > 0 })
                        {
                            MapTracer.Write($"{strategy.TracePrefix} 跳过目标属性 {targetProp.Name}（原因：明细无正向源（ReverseOnly））");
                            continue;
                        }
                        Expression? pmExpr;
                        if (pathNames is { Length: > 1 })
                        {
                            var dotPath = string.Join(".", BuildSegmentsWithIndexes(pathNames, pm.SourceIndexes));
                            pmExpr = TryBuildDotPathExpression(srcParam, sourceType, dotPath, strategy, targetProp);
                            if (pmExpr == null)
                            {
                                MapTracer.Write($"{strategy.TracePrefix} 跳过目标属性 {targetProp.Name}（原因：明细嵌套路径 {dotPath} 无法构建点路径表达式）");
                                continue;
                            }
                        }
                        else
                        {
                            var single = pathNames is { Length: 1 }
                                ? BuildSegmentsWithIndexes(pathNames, pm.SourceIndexes)[0]
                                : pm.SourceName!;
                            var pmVal = BuildSingleSourceProjection(single, sourceType, srcParam, sourceProps, strategy, targetProp);
                            if (pmVal == null)
                            {
                                MapTracer.Write($"{strategy.TracePrefix} 跳过目标属性 {targetProp.Name}（原因：明细源字段 {single} 无匹配）");
                                continue;
                            }
                            pmExpr = pmVal;
                        }
                        var pmConverted = ConvertProjectionType(pmExpr, targetProp.PropertyType, depth: 0, strategy);
                        if (pmConverted == null)
                        {
                            MapTracer.Write($"{strategy.TracePrefix} 跳过目标属性 {targetProp.Name}（原因：类型 {pmExpr.Type.Name}→{targetProp.PropertyType.Name} 不可转换）");
                            continue;
                        }
                        bindings.Add(Expression.Bind(targetProp, pmConverted));
                        continue;
                    }
                    // ⑤ 属性级 miss（矩阵：∈slots ∧ ∉Properties ∧ ∉Compose → 跳过 + Trace，
                    // 提取期权威——不调特性读取、不按名重试——WMI2 族收敛）。
                    // 嵌套/集合 DTO 属性除外：其映射语义由子对明细承载（递归入口——AddTypeCompatibleBinding
                    // 内 BuildNestedProjection/BuildCollectionProjection），取值经既有解析链（同名导航——
                    // 无锚 TryGetMapPropertyInfo 按名默认，非映射声明面消费——D2 不涉）；跳过即嵌套投影断链。
                    if (Runtime.RuntimeSnapshotStore.IsRegisteredDto(targetProp.PropertyType)
                        || ContainsRegisteredDtoElement(targetProp.PropertyType))
                    {
                        var nestedRecursiveExpr = BuildProjectionValueExpression(targetProp, sourceType, srcParam, sourceProps, strategy, fluentPmByName: null);
                        if (nestedRecursiveExpr == null)
                        {
                            MapTracer.Write($"{strategy.TracePrefix} 跳过目标属性 {targetProp.Name}（原因：嵌套/集合投影构建失败）");
                            continue;
                        }
                        bindings.Add(Expression.Bind(targetProp, nestedRecursiveExpr));
                        continue;
                    }
                    MapTracer.Write($"{strategy.TracePrefix} 跳过目标属性 {targetProp.Name}（原因：明细无该目标属性映射声明（提取期权威））");
                    continue;
                }

                // ── 兜底腿（三态②条目命中明细不可达 / 三态③miss——现状逻辑原样，D2 保留原调用）──
                // 属性级 [MapIgnore] 或全局忽略（投影恒为 Entity→DTO 方向 → isToDto=true）；
                // 映射对级豁免（目标属性带 OverrideIgnore=true 显式特性）时跳过全局判定；
                if (IsMapIgnoreDefined(targetProp)) continue;
                if (PropertyMatcher.ShouldIgnore(targetProp, isToDto: true, PropertyIgnoreChecker.HasOverrideIgnore(targetProp))) continue;

                var valueExpr = BuildProjectionValueExpression(targetProp, sourceType, srcParam, sourceProps, strategy, fluentPmByName: null);
                if (valueExpr == null)
                {
                    MapTracer.Write($"{strategy.TracePrefix} 跳过目标属性 {targetProp.Name}（原因：无法构建源值表达式，无匹配源属性或类型不可转换）");
                    continue;
                }

                bindings.Add(Expression.Bind(targetProp, valueExpr));
            }

            return bindings;
        }

        /// <summary>
        /// 运行时归一全名匹配
        /// 编译期符号显示串（嵌套 '.' 分隔）与运行时 Type.FullName（嵌套 '+' 分隔）口径归一后
        /// 序数精确比较。
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

        /// <summary>
        /// 判定类型是否为"元素为注册 DTO 的集合/可枚举形态"（快照腿矩阵 miss 的嵌套/集合豁免判定
        /// List&lt;NestedDto&gt;/NestedDto[] 等形态 → 递归入口保留）。非泛型/非数组/值类型元素 → false。
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
            return elementType != null && Runtime.RuntimeSnapshotStore.IsRegisteredDto(elementType);
        }

        /// <summary>
        /// 树挂载位判定 —— 目标属性是否为树配置命中 ChildrenProperty 的自引用集合
        /// <para>三判据：目标类型启用树语义（注册表 Fluent .Tree() 或 [MapTree] 标注
        /// 消费点二改读：注册表优先 + 反射回退）+ 属性名 == ChildrenProperty + 集合元素类型 == 目标类型自身</para>
        /// <para>普通 DTO（无树语义）恒 false——投影行为与基线一致（F3 兜底）；FreeSql 投影策略
        /// EnableCollectionProjection=false 不投影集合 → 判断不可达</para>
        /// <para>opt-in 语义：Fluent DTO 未调 .Tree() 且无 [MapTree] 标注 → 恒 false
        /// 注册表 null 不得取默认三元组</para>
        /// </summary>
        /// <param name="prop">目标属性</param>
        /// <param name="targetType">目标 DTO 类型</param>
        /// <returns>是树挂载位时为 true</returns>
        [RequiresUnreferencedCode("IsTreeChildrenSlot reads the [MapTree] attribute via reflection. Some types may be trimmed.")]
        private static bool IsTreeChildrenSlot(PropertyInfo prop, Type targetType)
        {
            // 注册表优先（Fluent .Tree() 承载—— 消费点二）；null 回退反射 [MapTree]
            var registryConfig = RuntimeSnapshotStore.GetTreeConfig(targetType);
            string childrenProperty;
            if (registryConfig is not null)
            {
                childrenProperty = registryConfig.ChildrenProperty;
            }
            else
            {
                // G6 T5 锚门回滚（同 TreeBuilder——[MapTree] 类级树配置，非映射对惰性治理域）
                var treeAttr = targetType.GetCustomAttribute<MapTreeAttribute>();
                if (treeAttr is null) return false;
                childrenProperty = treeAttr.ChildrenProperty;
            }
            if (prop.Name != childrenProperty) return false;
            return NavigationResolver.GetCollectionElementType(prop.PropertyType) == targetType;
        }

        /// <summary>
        /// 源侧全局忽略判定—— 异名 FromField 源字段命中全局忽略
        /// 清单时剔除映射对，对齐运行时双侧判定形态（ExpressionBuilder.cs:108 ShouldIgnore(source) || ShouldIgnore(target)）；
        /// 豁免与目标侧共用同一映射对级豁免（目标属性 OverrideIgnore=true）。
        /// <para>边界：点路径（"Address.City"）与命名约定扁平化（AddressCity）为扩展匹配，已纳入源侧判定
        /// 判定粒度为叶节点（最终选中路径的末段）；中间导航节点仍不纳入
        /// 判定（与源生成器值模型管线一致，CR-2/F1 登记范围更新）。</para>
        /// </summary>
        /// <param name="sourceProp">源侧属性信息</param>
        /// <param name="targetProp">目标侧属性信息</param>
        /// <returns>源侧判定命中忽略清单时为 true</returns>
        [RequiresUnreferencedCode("ShouldIgnoreSource triggers global config freeze via reflection. Some members may be trimmed.")]
        internal static bool ShouldIgnoreSource(PropertyInfo sourceProp, PropertyInfo targetProp)
            => PropertyMatcher.ShouldIgnore(sourceProp, isToDto: true, PropertyIgnoreChecker.HasOverrideIgnore(targetProp));

        /// <summary>
        /// 注册表路径索引段重建——将 <see cref="Runtime.RuntimePropertyMapping.SourceIndexes"/>
        /// 承载的段索引回填段名（"Items" + 0 → "Items[0]"），交 <see cref="TryBuildDotPathExpression"/> 索引段解析
        /// （ElementAtOrDefault 条件表达式）。修复前多段分支仅 join 段名、索引信息被丢弃 →
        /// "Items" 无法解析为索引段 → 绑定构建失败静默跳过（生成端发射契约早已约定 SourceIndexes——发射了没有消费方）。
        /// <para>发射契约（SnapshotMetadataEmitter）：SourceIndexes 与 SourcePathNames 等长、非索引段以 -1 占位
        /// （HasIndex 门内发射）。重建逐段配对：-1（占位）/ 越界 / 段名已含 '['（手写索引字面量形态，
        /// 防 Items[0][0] 叠加）不重建。<see cref="Runtime.RuntimePropertyMapping.HasIndex"/> 为快照布尔与
        /// 发射端条件门，非重建判据——手工注册的不一致形态（数组与段数不等长等）按逐段守卫保守回退原段名。</para>
        /// <para>：private → internal（ComposeProjectionKernel 快照腿同口径消费
        /// RuntimeComposeMapping.SourceIndexes 重建，与 pm 级同一重建单点）。</para>
        /// </summary>
        /// <param name="segments">原始段名数组</param>
        /// <param name="sourceIndexes">索引元数据（可空）</param>
        /// <returns>重建后段名数组（无索引元数据时原样返回）</returns>
        internal static string[] BuildSegmentsWithIndexes(string[] segments, int[]? sourceIndexes)
        {
            if (sourceIndexes is not { Length: > 0 })
                return segments;
            var rebuilt = new string[segments.Length];
            for (int i = 0; i < segments.Length; i++)
            {
                var seg = segments[i];
                rebuilt[i] = i < sourceIndexes.Length && sourceIndexes[i] >= 0 && !seg.Contains('[')
                    ? $"{seg}[{sourceIndexes[i]}]"
                    : seg;
            }
            return rebuilt;
        }

        /// <summary>
        /// 为单个目标属性构建值表达式 —— 包含源属性匹配、导航属性推断和类型转换
        /// </summary>
        /// <returns>值表达式；null 表示跳过该属性</returns>
        /// <remarks>
        /// <para><b>G1-② 注册表明细优先</b>：<paramref name="fluentPmByName"/>
        /// 命中目标属性时读注册表声明（SourceName 单段异名 / SourcePathNames 多段嵌套路径），
        /// 等价重建 <c>[MapProperty(FromField)]</c> 语义——多段走 <c>TryBuildDotPathExpression</c> 精确路径，
        /// 单段落入主流程同名解析。未命中回退既有特性解析（[MapFrom] 通道零变化——F6）。
        /// Mask/Convert/FromJson 声明不进本方法（降级检测声明级分流已在判定层拦截）。</para>
        /// <para><b>索引段重建</b>：多段与单段（显式路径）声明均经
        /// <see cref="BuildSegmentsWithIndexes"/> 消费 <see cref="Runtime.RuntimePropertyMapping.SourceIndexes"/>
        /// ——单段带索引与多段同构（生成端可发射单段带索引形态，B3-15 透传锁实证）。</para>
        /// </remarks>
        [RequiresUnreferencedCode("BuildProjectionValueExpression scans type members and attributes via reflection. Some members may be trimmed.")]
        [RequiresDynamicCode("BuildProjectionValueExpression builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
        private static Expression? BuildProjectionValueExpression(
            PropertyInfo targetProp, Type sourceType, Expression srcParam,
            List<PropertyInfo> sourceProps, ProjectionStrategy strategy,
            IReadOnlyDictionary<string, RuntimePropertyMapping>? fluentPmByName = null)
        {
            // ★ 路线四（TB-20261127-01 卡 C1）——Compose 嵌套 DTO 的源改指（最先判定，优先于一切声明面）：
            // Compose 通道内，目标属性是「已注册嵌套 DTO」时，其数据来源必须是**用户 join 的该 DTO 主源参数**
            // （t2/t3），不能再按导航属性（src.Dept）取值——那会让 ORM 自建第二条 LEFT JOIN，绕过用户 join
            // 上的软删/非主键条件（缺陷③：软删泄漏／T1 无导航丢数据／非主键 join 串行）。
            // 匹配不到该源类型的参数 → fail-closed 抛异常（不静默返回 null/空壳）。
            // 非 Compose 通道（作用域外 IsActive=false）→ 整段跳过，单表/EFCore/Dapper 行为逐字不变。
            if (ComposeNestedSourceScope.IsActive
                && Runtime.RuntimeSnapshotStore.IsRegisteredDto(targetProp.PropertyType))
            {
                var nestedPrimarySource = Runtime.RuntimeSnapshotStore.GetPrimarySource(targetProp.PropertyType);
                if (nestedPrimarySource != null)
                {
                    var joinedNestedSource = ComposeNestedSourceScope.RequireJoinedSource(
                        nestedPrimarySource, targetProp.Name, strategy.TracePrefix);
                    if (joinedNestedSource != null)
                        return ConvertProjectionType(joinedNestedSource, targetProp.PropertyType, depth: 0, strategy);
                }
            }

            // G1-② 注册表明细优先（Fluent 声明面）——命中即等价重建 FromField 语义；未命中走特性解析回退
            if (fluentPmByName != null && fluentPmByName.TryGetValue(targetProp.Name, out var fpm)
                && fpm != null)
            {
                var pathNames = fpm.SourcePathNames;
                if (pathNames is { Length: > 1 })
                {
                    // 多段嵌套路径 → 索引段重建→ 点路径精确构建（既有 dot path 分支复用——src.A.B[0].C）
                    var dotPath = string.Join(".", BuildSegmentsWithIndexes(pathNames, fpm.SourceIndexes));
                    var nestedDotExpr = TryBuildDotPathExpression(srcParam, sourceType, dotPath, strategy, targetProp);
                    if (nestedDotExpr != null)
                        return ConvertProjectionType(nestedDotExpr, targetProp.PropertyType, depth: 0, strategy);
                    // Trace 打印重建后实际尝试的路径（重建前打印原始段名，与解析输入不符）
                    MapTracer.Write($"{strategy.TracePrefix} 跳过目标属性 {targetProp.Name}（原因：注册表嵌套路径 {dotPath} 无法构建点路径表达式）");
                    return null;
                }
                var regSourceName = pathNames is { Length: 1 }
                    ? BuildSegmentsWithIndexes(pathNames, fpm.SourceIndexes)[0] // 单段带索引同构重建（生成端可发射单段带索引）
                    : fpm.SourceName;
                if (regSourceName != null)
                {
                    // 单段（异名或同名显式声明）→ 落入主流程同名解析分支（fieldSourceType=主源）
                    var regExpr = BuildSingleSourceProjection(regSourceName, sourceType, srcParam, sourceProps, strategy, targetProp);
                    if (regExpr != null)
                        return ConvertProjectionType(regExpr, targetProp.PropertyType, depth: 0, strategy);
                    MapTracer.Write($"{strategy.TracePrefix} 跳过目标属性 {targetProp.Name}（原因：注册表源字段 {regSourceName} 无匹配且点路径/扁平化均无命中）");
                    return null;
                }
                // 无源（SourceName=null 且无路径——ReverseOnly 场景）→ 保守走特性解析回退
            }

            PropertyMatcher.TryGetMapPropertyInfo(targetProp, sourceType,
                out string sourceFieldName, out Type fieldSourceType, out _);

            Expression valueExpr;

            // 源类型匹配主映射源 → 直接属性访问
            if (fieldSourceType == sourceType)
            {
                var directExpr = BuildSingleSourceProjection(sourceFieldName, sourceType, srcParam, sourceProps, strategy, targetProp);
                if (directExpr == null) return null;
                valueExpr = directExpr;
            }
            else
            {
                // 源类型不是主映射源 → 跨表取值（路线四 TB-20261127-01 卡 C1）：
                // ① Compose 通道（ComposeNestedSourceScope 作用域内）→ 直接取**用户 join 的该源参数**
                //    （原按 [Navigate] 自建 JOIN 会绕过用户 join 的条件——缺陷③）；匹配不到 fail-closed。
                // ② 非 Compose 通道（作用域外恒返回 null）→ 既有语义逐字不变（按类型匹配推断导航属性）。
                var joinedFieldSource = ComposeNestedSourceScope.RequireJoinedSource(
                    fieldSourceType, targetProp.Name, strategy.TracePrefix);
                Expression fieldSourceExpr;
                PropertyInfo? navProp = null;
                if (joinedFieldSource != null)
                {
                    fieldSourceExpr = joinedFieldSource;
                }
                else
                {
                    navProp = NavigationResolver.ResolveNavigationProperty(
                        sourceProps, fieldSourceType, sourceType.Name);
                    if (navProp == null)
                    {
                        MapTracer.Write($"{strategy.TracePrefix} 跳过目标属性 {targetProp.Name}（原因：源类型 {sourceType.Name} 中未找到类型为 {fieldSourceType.Name} 的导航属性）");
                        return null;
                    }
                    fieldSourceExpr = Expression.Property(srcParam, navProp);
                }

                var navProps = GetPublicInstanceProperties(fieldSourceType);
                var navField = ProjectionNameResolver.Resolve(navProps, sourceFieldName);
                if (navField == null)
                {
                    MapTracer.Write($"{strategy.TracePrefix} 跳过目标属性 {targetProp.Name}（原因：导航类型 {fieldSourceType.Name} 中未找到字段 {sourceFieldName}）");
                    return null;
                }

                // 源侧判定：跨表源字段命中全局忽略 → 剔除（与内存双侧判定一致）
                if (ShouldIgnoreSource(navField, targetProp))
                {
                    MapTracer.Write($"{strategy.TracePrefix} 跳过目标属性 {targetProp.Name}（原因：导航字段 {navField.Name} 命中全局忽略清单）");
                    return null;
                }

                valueExpr = Expression.Property(fieldSourceExpr, navField);
            }

            // 类型兼容性处理
            return ConvertProjectionType(valueExpr, targetProp.PropertyType, depth: 0, strategy);
        }

        /// <summary>
        /// 单段源字段投影表达式构建（从主流程提取——注册表明细单段消费与
        /// 特性解析同名分支共用）——Resolve 精确名 → 点路径拆分 → 扁平化扩展 → 源侧全局忽略判定
        /// </summary>
        /// <returns>原始值表达式（未做类型兼容转换）；无匹配/命中忽略返回 null（MapTracer 已记录）</returns>
        [RequiresUnreferencedCode("BuildSingleSourceProjection scans type members and attributes via reflection. Some members may be trimmed.")]
        [RequiresDynamicCode("BuildSingleSourceProjection builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
        private static Expression? BuildSingleSourceProjection(
            string sourceFieldName, Type sourceType, Expression srcParam,
            List<PropertyInfo> sourceProps, ProjectionStrategy strategy, PropertyInfo targetProp)
        {
            var sourceProp = ProjectionNameResolver.Resolve(sourceProps, sourceFieldName);

            if (sourceProp == null)
            {
                // 点路径拆分（Source="Address.City" → src.Address.City）
                if (sourceFieldName.Contains('.') || sourceFieldName.Contains('['))
                {
                    var dotExpr = TryBuildDotPathExpression(srcParam, sourceType, sourceFieldName, strategy, targetProp);
                    if (dotExpr != null)
                        return dotExpr;
                }
                // 命名约定扁平化（AddressCity → Address.City）
                var flatExpr = TryBuildFlattenedExpression(srcParam, sourceType, sourceFieldName, targetProp, strategy);
                if (flatExpr != null)
                    return flatExpr;
                MapTracer.Write($"{strategy.TracePrefix} 跳过目标属性 {targetProp.Name}（原因：源类型 {sourceType.Name} 中未找到源字段 {sourceFieldName}，且点路径/扁平化均无匹配路径）");
                return null;
            }

            // 源侧判定：异名 FromField 源字段命中全局忽略 → 剔除（与内存 Mapper.Map 双侧判定一致）
            if (ShouldIgnoreSource(sourceProp, targetProp))
            {
                MapTracer.Write($"{strategy.TracePrefix} 跳过目标属性 {targetProp.Name}（原因：源字段 {sourceProp.Name} 命中全局忽略清单）");
                return null;
            }

            return Expression.Property(srcParam, sourceProp);
        }

        /// <summary>
        /// Projection 值表达式的类型兼容性处理 —— 同类型/隐式转换/嵌套对象递归/集合投影
        /// </summary>
        /// <param name="valueExpr">原始值表达式</param>
        /// <param name="tgtPropType">目标属性类型</param>
        /// <param name="depth">当前嵌套深度</param>
        /// <param name="strategy">投影策略（决定嵌套/集合/降级路径选择）</param>
        /// <returns>转换后的表达式；null 表示不可转换需跳过</returns>
        [RequiresDynamicCode("ConvertProjectionType builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
        [RequiresUnreferencedCode("ConvertProjectionType scans type members and attributes via reflection. Some members may be trimmed.")]
        internal static Expression? ConvertProjectionType(Expression valueExpr, Type tgtPropType, int depth, ProjectionStrategy strategy)
        {
            var srcPropType = valueExpr.Type;
            var kind = ClassifyProjectionCompat(srcPropType, tgtPropType, out var srcElementType, out var tgtElementType);
            switch (kind)
            {
                case ProjectionCompatKind.SameType:
                    return valueExpr;
                case ProjectionCompatKind.ImplicitConvertible:
                    return Expression.Convert(valueExpr, tgtPropType);
                case ProjectionCompatKind.NullableLift:
                    return Expression.Convert(valueExpr, tgtPropType); // T → T? 始终合法
                case ProjectionCompatKind.NestedDto: // 嵌套 DTO 投影绑定
                    return BuildNestedProjection(srcPropType, tgtPropType, valueExpr, depth + 1, strategy);
                case ProjectionCompatKind.Collection: // 集合元素投影绑定
                    if (!strategy.EnableCollectionProjection)
                    {
                        MapTracer.Write($"{strategy.TracePrefix} 跳过集合投影 {srcPropType.Name}→{tgtPropType.Name}（原因：集合投影未启用）");
                        return null;
                    }
                    return BuildCollectionProjection(srcPropType, tgtPropType,
                        srcElementType!, tgtElementType!, valueExpr, depth + 1, strategy);
                default:
                    // 不可翻译的类型转换 → 跳过
                    MapTracer.Write($"{strategy.TracePrefix} 跳过类型转换 {srcPropType.Name}→{tgtPropType.Name}（原因：非同名类型、非隐式转换、非可嵌套 [MapFrom] 类型，ORM 无法翻译）");
                    return null;
            }
        }

        /// <summary>
        /// 投影类型兼容判据分类——ConvertProjectionType 与嵌套绑定内联链
        /// （原 TypeCompatible 标签段）的共享判定单点：同类型 / 隐式可转 / T→T? 提升 / 嵌套 DTO /
        /// 集合元素 / 不可转六类。判据表达式与求值顺序与收敛前逐字一致（嵌套命中即短路，
        /// 不再探测集合元素类型；集合判据先 src 后 tgt 元素类型再 meta 查询）。
        /// trace 文案与构建动作由各消费方保留（两链跳过文案本就不同，不合并）。
        /// </summary>
        [RequiresUnreferencedCode("ClassifyProjectionCompat inspects collection element types via reflection. Some members may be trimmed.")]
        private static ProjectionCompatKind ClassifyProjectionCompat(Type srcType, Type tgtType, out Type? srcElementType, out Type? tgtElementType)
        {
            srcElementType = null;
            tgtElementType = null;

            if (srcType == tgtType)
                return ProjectionCompatKind.SameType;

            if (TypeConverter.IsImplicitConvertible(srcType, tgtType))
                return ProjectionCompatKind.ImplicitConvertible;

            if (TypeConverter.IsNullableType(tgtType)
                && Nullable.GetUnderlyingType(tgtType) == srcType)
                return ProjectionCompatKind.NullableLift;

            if (srcType.IsClass && tgtType.IsClass
                && srcType != typeof(string) && tgtType != typeof(string)
                && !srcType.IsGenericType && !tgtType.IsGenericType
                && RuntimeSnapshotStore.TryGetMappingMeta(tgtType, out _)) // 嵌套 DTO 判据
                return ProjectionCompatKind.NestedDto;

            // 集合导航投影判据：List&lt;SrcItem&gt; → List&lt;TgtItem&gt;（Select+ToList）
            srcElementType = NavigationResolver.GetCollectionElementType(srcType);
            tgtElementType = NavigationResolver.GetCollectionElementType(tgtType);
            if (srcElementType != null && tgtElementType != null
                && RuntimeSnapshotStore.TryGetMappingMeta(tgtElementType, out _)) // 集合元素判据
                return ProjectionCompatKind.Collection;

            return ProjectionCompatKind.NotConvertible;
        }

        /// <summary>
        /// 嵌套投影绑定内的类型兼容判据链——原 BuildNestedProjectionBindings
        /// 内联段（TypeCompatible 标签 起）上提：判定复用 ClassifyProjectionCompat 单点，
        /// 跳过路径保留嵌套投影前缀的专有 trace 文案；未入绑定即原 continue 语义（跳过该属性）。
        /// </summary>
        [RequiresDynamicCode("AddTypeCompatibleBinding builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
        [RequiresUnreferencedCode("AddTypeCompatibleBinding scans type members and attributes via reflection. Some members may be trimmed.")]
        private static void AddTypeCompatibleBinding(
            Expression valExpr, PropertyInfo tgtProp, Type srcType, Type tgtType,
            int depth, ProjectionStrategy strategy, List<MemberBinding> nestedBindings)
        {
            var sType = valExpr.Type;
            var tType = tgtProp.PropertyType;
            var kind = ClassifyProjectionCompat(sType, tType, out var srcElementType, out var tgtElementType);

            switch (kind)
            {
                case ProjectionCompatKind.SameType:
                    break;
                case ProjectionCompatKind.ImplicitConvertible:
                case ProjectionCompatKind.NullableLift:
                    valExpr = Expression.Convert(valExpr, tType); // 隐式转换与 T → T? 同为 Convert
                    break;
                case ProjectionCompatKind.NestedDto:
                {
                    var nestedExpr = BuildNestedProjection(sType, tType, valExpr, depth + 1, strategy);
                    if (nestedExpr == null)
                    {
                        MapTracer.Write($"{strategy.TracePrefix} 嵌套投影 {srcType.Name}→{tgtType.Name} 跳过属性 {tgtProp.Name}（原因：子嵌套投影 {sType.Name}→{tType.Name} 构建失败）");
                        return;
                    }
                    valExpr = nestedExpr;
                    break;
                }
                case ProjectionCompatKind.Collection:
                {
                    if (!strategy.EnableCollectionProjection)
                    {
                        MapTracer.Write($"{strategy.TracePrefix} 嵌套投影 {srcType.Name}→{tgtType.Name} 跳过属性 {tgtProp.Name}（原因：集合投影未启用）");
                        return;
                    }
                    var collExpr = BuildCollectionProjection(sType, tType,
                        srcElementType!, tgtElementType!, valExpr, depth + 1, strategy);
                    if (collExpr == null)
                    {
                        MapTracer.Write($"{strategy.TracePrefix} 嵌套投影 {srcType.Name}→{tgtType.Name} 跳过属性 {tgtProp.Name}（原因：集合投影 {sType.Name}→{tType.Name} 构建失败）");
                        return;
                    }
                    valExpr = collExpr;
                    break;
                }
                default:
                    MapTracer.Write($"{strategy.TracePrefix} 嵌套投影 {srcType.Name}→{tgtType.Name} 跳过属性 {tgtProp.Name}（原因：类型 {sType.Name}→{tType.Name} 不可隐式转换）");
                    return;
            }

            nestedBindings.Add(Expression.Bind(tgtProp, valExpr));
        }

        /// <summary>投影类型兼容判据分类（A2-03 共享判定单点，见 ClassifyProjectionCompat）。</summary>
        private enum ProjectionCompatKind
        {
            SameType,
            ImplicitConvertible,
            NullableLift,
            NestedDto,
            Collection,
            NotConvertible
        }

        /// <summary>
        /// 命名约定扁平化 —— 将目标属性名拆解为候选路径，在源类型的嵌套属性中查找匹配
        /// <para>例：AddressCity → 拆为 ["Address", "City"] → 生成 src.Address.City 表达式</para>
        /// <para>源侧忽略判定：匹配成功后对最终路径叶节点执行 ShouldIgnoreSource；
        /// 判定统一在 matched 之后对最终选中的路径执行一次，避免中间候选的叶节点误判干扰最短路径优先；
        /// 中间导航节点不纳入判定（与源生成器值模型管线一致）。命中忽略 → 返回 null 跳过该属性。</para>
        /// <para>srcExpr 类型为 Expression：嵌套 DTO 场景传入导航链表达式而非参数。</para>
        /// </summary>
        /// <param name="srcExpr">源表达式（属性链或导航链表达式）</param>
        /// <param name="sourceType">源类型</param>
        /// <param name="targetPropName">目标属性名（PascalCase）</param>
        /// <param name="targetProp">目标属性信息</param>
        /// <param name="strategy">投影策略</param>
        /// <returns>扁平化值表达式；无可构建路径时返回 null</returns>
        [RequiresUnreferencedCode("TryBuildFlattenedExpression scans type members and attributes via reflection. Some members may be trimmed.")]
        [RequiresDynamicCode("TryBuildFlattenedExpression builds expressions via MakeGenericMethod/MakeGenericType. Not supported in AOT.")]
        internal static Expression? TryBuildFlattenedExpression(
            Expression srcExpr, Type sourceType, string targetPropName,
            PropertyInfo targetProp, ProjectionStrategy strategy)
        {
            var candidates = BuildPathCandidates(targetPropName);

            foreach (var candidate in candidates)
            {
                if (candidate.Count <= 1) continue;

                // T4 收拢：与 TryBuildDotPathExpression 共用段循环（差异核对记录见）；
                // 失败静默（尽力而为语义，与现状一致）→ 下一候选；成功后再做叶节点源侧判定
                if (!TryResolveSegmentChain(srcExpr, sourceType, candidate, out var current, out var leafProp,
                        out _, out _, out _))
                    continue;

                // 叶节点源侧判定：最终选中的路径末段命中全局忽略清单 → 跳过（与运行时/源生成器一致）
                if (leafProp != null && ShouldIgnoreSource(leafProp, targetProp))
                {
                    MapTracer.Write($"{strategy.TracePrefix} 跳过目标属性 {targetProp.Name}（原因：扁平化叶节点 {leafProp.Name} 命中全局忽略清单）");
                    return null;
                }
                return current;
            }

            return null;
        }

        /// <summary>
        /// 点路径表达式构建 —— 将 "Address.City" 解析为 src.Address.City
        /// <para>按 '.' 分段，逐级反射查找属性，中间节点不能为值类型或 string</para>
        /// <para>源侧忽略判定：匹配成功后对叶节点（路径末段）执行 ShouldIgnoreSource；
        /// 中间导航节点不纳入判定（与源生成器值模型管线一致）。命中忽略 → 返回 null 跳过该属性。</para>
        /// <para>srcExpr 类型为 Expression：嵌套 DTO 场景传入导航链表达式而非参数。</para>
        /// </summary>
        /// <param name="srcExpr">源表达式（属性链或导航链表达式）</param>
        /// <param name="sourceType">源类型</param>
        /// <param name="dotPath">点路径（如 "Address.City"）</param>
        /// <param name="strategy">投影策略</param>
        /// <param name="targetProp">目标属性信息</param>
        /// <returns>点路径值表达式；无法构建时返回 null</returns>
        [RequiresUnreferencedCode("TryBuildDotPathExpression scans type members and attributes via reflection. Some members may be trimmed.")]
        [RequiresDynamicCode("TryBuildDotPathExpression builds expressions via MakeGenericMethod/MakeGenericType. Not supported in AOT.")]
        internal static Expression? TryBuildDotPathExpression(
            Expression srcExpr, Type sourceType, string dotPath,
            ProjectionStrategy strategy, PropertyInfo targetProp)
        {
            var segments = dotPath.Split('.');
            // 单段集合索引（无 '.'）也放行（索引段解析在 TryResolveSegmentChain）
            if (segments.Length < 2 && !dotPath.Contains('[')) return null;

            // T4 收拢：与 TryBuildFlattenedExpression 共用段循环（差异核对记录见）；
            // 失败 Trace 措辞按失败原因区分（NotFound=未找到属性；ValueTypeInterrupt=值型/string 不可导航），与现状逐字一致
            if (!TryResolveSegmentChain(srcExpr, sourceType, segments, out var current, out var leafProp,
                    out var failReason, out var failedSegment, out var failedType))
            {
                if (failReason == SegmentChainFailReason.NotFound)
                    MapTracer.Write($"{strategy.TracePrefix} 点路径 '{dotPath}' 在分段 '{failedSegment}' 处失败（类型 {failedType?.Name} 中未找到属性）");
                else
                    MapTracer.Write($"{strategy.TracePrefix} 点路径 '{dotPath}' 在分段 '{failedSegment}' 处中断（类型 {failedType?.Name} 为值型/string，不可继续导航）");
                return null;
            }

            // 叶节点源侧判定：路径末段命中全局忽略清单 → 跳过（与运行时/源生成器一致）
            if (leafProp != null && ShouldIgnoreSource(leafProp, targetProp))
            {
                MapTracer.Write($"{strategy.TracePrefix} 跳过目标属性 {targetProp.Name}（原因：点路径叶节点 {leafProp.Name} 命中全局忽略清单）");
                return null;
            }

            MapTracer.Write($"{strategy.TracePrefix} 点路径 '{dotPath}' 匹配成功");
            return current;
        }

        /// <summary>
        /// 段链解析失败原因（T4 收拢）— 供调用方按自身措辞 Trace
        /// </summary>
        private enum SegmentChainFailReason
        {
            /// <summary>
            /// 段属性未找到或不可读
            /// </summary>
            NotFound,

            /// <summary>
            /// 中间节点为值类型/string，不可继续导航
            /// </summary>
            ValueTypeInterrupt,
        }

        /// <summary>
        /// 段链解析共用循环（T4 收拢）— TryBuildFlattenedExpression/TryBuildDotPathExpression 同构段循环
        /// <para>差异核对：两函数段循环逐段同构（GetPublicInstanceProperties + OrdinalIgnoreCase 匹配 +
        /// null/CanRead 失败 + 中间节点值类型/string 排除 + current/leafProp 构建），仅段来源（候选列表 vs 单一
        /// Split）与失败/成功 Trace 措辞不同——故抽共用循环，调用方保留自身 Trace 语义；纯重构，零行为变化。</para>
        /// <para>中间节点必须是 class 或 interface（非 string、非值类型，C3）；失败输出失败段与类型。</para>
        /// </summary>
        /// <param name="srcExpr">源对象访问表达式（顶层为参数；嵌套 DTO 场景为导航链表达式， T3）</param>
        /// <param name="sourceType">源对象类型</param>
        /// <param name="segments">路径段列表（单段=直接属性；多段=导航链）</param>
        /// <param name="current">成功时末段访问表达式</param>
        /// <param name="leafProp">成功时叶节点属性</param>
        /// <param name="failReason">失败原因</param>
        /// <param name="failedSegment">失败段名（成功为 null）</param>
        /// <param name="failedType">失败相关类型：NotFound=查找所在类型；ValueTypeInterrupt=段属性类型（成功为 null）</param>
        [RequiresUnreferencedCode("TryResolveSegmentChain scans type members via reflection. Some members may be trimmed.")]
        [RequiresDynamicCode("TryResolveSegmentChain builds ElementAtOrDefault via MakeGenericMethod. Not supported in AOT.")]
        private static bool TryResolveSegmentChain(
            Expression srcExpr, Type sourceType, IReadOnlyList<string> segments,
            out Expression current, out PropertyInfo? leafProp,
            out SegmentChainFailReason failReason, out string? failedSegment, out Type? failedType)
        {
            current = srcExpr;
            leafProp = null;
            failReason = SegmentChainFailReason.NotFound;
            failedSegment = null;
            failedType = null;

            // 段循环判据收敛为 TypePathResolver（正向尽力式）
            // 失败分类映射（③ 行）：NotFound ← SegmentNotFound/InvalidIndexSyntax/
            // IndexNotNonNegative/NotIndexableType；ValueTypeInterrupt ← NotNavigable
            var result = s_typeResolver.Resolve(sourceType, segments, PathResolutionOptions.ForwardEager, context: null);
            if (!result.Success)
            {
                failReason = result.Failure == PathSegmentFailure.NotNavigable
                    ? SegmentChainFailReason.ValueTypeInterrupt
                    : SegmentChainFailReason.NotFound;
                failedSegment = result.FailedSegment ?? segments[result.FailedSegmentIndex];
                // 失败相关类型：NotFound=查找所在类型（前缀末段属性类型）；ValueTypeInterrupt=段属性类型（近似）
                failedType = result.FailedSegmentIndex == 0
                    ? sourceType
                    : result.Path[result.FailedSegmentIndex - 1].PropertyType;
                return false;
            }

            // 成功：按已解析路径逐段构造访问表达式（索引段 ElementAtOrDefault + null 守卫，与原实现同形态）
            var path = result.Path;
            for (int i = 0; i < path.Count; i++)
            {
                var prop = path[i];
                current = Expression.Property(current, prop);
                leafProp = prop;

                if (result.SourceIndexes[i] is int n)
                {
                    // 集合索引访问——ElementAtOrDefault + null 守卫
                    // （集合 null → default(元素类型)；空集合/越界 → ElementAtOrDefault 返回 default；
                    //  EF Core 翻译为 LIMIT 1 OFFSET n 子查询，SQL 侧无越界概念——PoC 实证）
                    // ⑦ 连带修复： ① （9a1433a）重构删除 elemType null 守卫
                    // 引入 CS8604（MakeGenericMethod 实参可空）；本分支由 PathResolver 保证 prop 为集合属性，
                    // elemType 实际非空，null-forgiving 行为零变更（规则 4 阻塞性连带修改）
                    var elemType = NavigationResolver.GetCollectionElementType(prop.PropertyType)!;
                    // 静态 MethodInfo 复用（Int32 形态消歧），构建期零 GetMethods 枚举
                    var elementAtOrDefault = s_elementAtOrDefaultDef.MakeGenericMethod(elemType);
                    current = Expression.Condition(
                        Expression.NotEqual(current, Expression.Constant(null, current.Type)),
                        Expression.Call(elementAtOrDefault, current, Expression.Constant(n)),
                        Expression.Default(elemType));
                }
            }

            return true;
        }

        /// <summary>
        /// 将 PascalCase 属性名解析为所有可能的路径候选（按部分数升序排列，优先短路径）
        /// <para>算法收敛为 PathCandidateGenerator.Build 单点
        /// （Link 双工程物理同源，避免镜像漂移）；保留 internal 签名供既有测试与调用方。</para>
        /// </summary>
        /// <param name="name">目标属性名</param>
        /// <returns>路径候选列表（按部分数升序）</returns>
        internal static List<List<string>> BuildPathCandidates(string name)
            => PathCandidateGenerator.Build(name);

        /// <summary>
        /// 嵌套投影 —— 目标属性为 [MapFrom] 类时递归构建
        /// </summary>
        [RequiresUnreferencedCode("BuildNestedProjection scans type members and attributes via reflection. Some members may be trimmed.")]
        [RequiresDynamicCode("BuildNestedProjection builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
        private static Expression? BuildNestedProjection(Type srcType, Type tgtType, Expression srcExpr, int depth, ProjectionStrategy strategy)
        {
            if (depth > strategy.MaxNestingDepth)
            {
                MapTracer.Write($"{strategy.TracePrefix} 跳过嵌套投影 {srcType.Name}→{tgtType.Name}（原因：嵌套深度 {depth} 超过上限 {strategy.MaxNestingDepth}，疑似自引用类型）");
                return null;
            }

            if (depth > strategy.WarningDepth)
                MapTracer.Write($"{strategy.TracePrefix} 嵌套投影 {srcType.Name}→{tgtType.Name} 深度 {depth} 超过建议值 {strategy.WarningDepth}，翻译的 SQL 可能包含过多 LEFT JOIN");

            // 补遗：嵌套主源一致性校验（注册表优先 + 反射回退）
            // — Fluent DTO 注册表命中后 PrimarySource 与 srcType 比对通过，不再被主源校验跳过
            // ★ 顺序约束（复审 finding #2）：本校验必须先于下方 fail-closed 执行。
            // 语义上「主源配错」是声明错误，其正确处置是**跳过该属性、构建继续**（本函数返回 null
            // 由调用方 Trace + 跳过）；若先跑 fail-closed，这里会变成永远走不到的死代码，
            // 使「少一个字段」升级成「整条查询失败」。
            if (!RuntimeSnapshotStore.TryGetMappingMeta(tgtType, out var view550)
                || view550.PrimarySource != srcType)
            {
                MapTracer.Write($"{strategy.TracePrefix} 跳过嵌套投影 {srcType.Name}→{tgtType.Name}（原因：目标类型未标注 [MapFrom] 或主源与源类型不匹配）");
                return null;
            }

            // 路线四（TB-20261127-01 卡 C1）——嵌套源改指用户 join 的源参数：
            // 注册表自动匹配出的嵌套条目走 `t1.Nav` 属性访问（不经 ResolveNavigationProperty），
            // 若不在本入口改指，ORM 仍会自建第二条 LEFT JOIN，绕过用户 join 上的软删/非主键条件（缺陷③）。
            // 非 Compose 通道（作用域外）原样返回，单表/EFCore/Dapper 行为逐字不变。
            // 作用域内匹配不到源参数 → 仅「多跳嵌套」时 fail-closed：
            // 单跳导航 provider 能自建 JOIN（实测可返回业务值），保持旧 Soft 回退不误伤；
            // 多跳（嵌套里再嵌套）才会在 FreeSql 翻译器上无限递归至栈溢出。
            // 阈值依据：顶层属性以 depth:0 进入，单跳为 depth:1；双层最内层为 depth:3。
            srcExpr = ComposeNestedSourceScope.RedirectNestedSource(
                srcType, srcExpr, tgtType.Name, strategy.TracePrefix, nestingDepth: depth);

            var nestedBindings = BuildNestedProjectionBindings(srcType, tgtType, srcExpr, depth, strategy);
            if (nestedBindings == null)
            {
                MapTracer.Write($"{strategy.TracePrefix} 跳过嵌套投影 {srcType.Name}→{tgtType.Name}（原因：无任何可映射属性）");
                return null;
            }

            var nestedCtor = GetParameterlessCtor(tgtType);
            if (nestedCtor == null)
            {
                MapTracer.Write($"{strategy.TracePrefix} 跳过嵌套投影 {srcType.Name}→{tgtType.Name}（原因：目标类型无无参构造器）");
                return null;
            }

            var newExpr = Expression.New(nestedCtor);
            var memberInit = Expression.MemberInit(newExpr, nestedBindings);

            // Null 传播：源为 null 时目标为 null
            var nullCheck = Expression.Equal(srcExpr, Expression.Constant(null, srcType));
            return Expression.Condition(nullCheck, Expression.Constant(null, tgtType), memberInit);
        }

        /// <summary>
        /// 嵌套投影绑定构建 —— 递归处理嵌套 DTO 的属性绑定（含跨表导航与集合投影）
        /// <para> T1：集合元素绑定与嵌套 DTO 统一启用路径分支
        /// （点路径/推导扁平化，B1 的 isCollectionElement 门控已移除——集合元素内路径登记 B2 已完成）。</para>
        /// </summary>
        [RequiresUnreferencedCode("BuildNestedProjectionBindings scans type members and attributes via reflection. Some members may be trimmed.")]
        [RequiresDynamicCode("BuildNestedProjectionBindings builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
        private static List<MemberBinding>? BuildNestedProjectionBindings(Type srcType, Type tgtType, Expression srcExpr, int depth, ProjectionStrategy strategy)
        {
            var srcProps = GetPublicInstanceProperties(srcType);
            var tgtProps = GetPublicInstanceProperties(tgtType)
                .Where(p => p.CanWrite).ToList();

            var nestedBindings = new List<MemberBinding>();

            // 子步骤② 决策点 5（/； PoC 结论
            // 嵌套对按 (srcType, tgtType) 精确可达）：嵌套链快照优先双分支——与顶层/内核同构
            //（明细权威 + 不可达/miss 兜底）。003-P2-2 实证的"嵌套链全程不消费明细"随此收口
            //（嵌套 Fluent 异名等声明面明细首次被嵌套投影消费——能力补齐非等价切换，差异面登记）。
            Runtime.RuntimeSnapshotStore.TryGetMappingDetail(srcType, tgtType, out var entryMeta);
            // 决策点 7（明细可达判据扩容——顶层同款）：Properties 与 ComposeMappings 均空才兜底
            if (entryMeta is { Properties.Length: 0, ComposeMappings.Length: 0 }) entryMeta = null; // 三态② → 兜底
            var detailPmByName = entryMeta == null
                ? null
                : entryMeta.Properties
                    .Where(p => p.Target != null)
                    .GroupBy(p => p.Target!.Name)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var detailSlotNames = entryMeta == null
                ? null
                : new HashSet<string>(entryMeta.AllTargetSlots.Select(s => s.Name), StringComparer.Ordinal);
            var composeByTarget = entryMeta == null || entryMeta.ComposeMappings.Length == 0
                ? null
                : entryMeta.ComposeMappings
                    .GroupBy(c => c.TargetPropertyName)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            foreach (var tgtProp in tgtProps)
            {
                // ── 快照腿（嵌套明细命中——零特性读取）──
                if (detailSlotNames != null)
                {
                    // ① slot 差集（Ignore(Both) 零痕迹跳过）
                    if (!detailSlotNames.Contains(tgtProp.Name))
                    {
                        MapTracer.Write($"{strategy.TracePrefix} 嵌套投影 {srcType.Name}→{tgtType.Name} 跳过属性 {tgtProp.Name}（原因：明细声明面忽略/矛盾门剔除——slot 差集重建）");
                        continue;
                    }
                    // ② 方向声明（pm 物化面）
                    if (detailPmByName!.TryGetValue(tgtProp.Name, out var dirPm)
                        && dirPm.Target != null
                        && dirPm.Target.HasMapIgnore
                        && dirPm.Target.MapIgnoreDirection != 2)
                    {
                        MapTracer.Write($"{strategy.TracePrefix} 嵌套投影 {srcType.Name}→{tgtType.Name} 跳过属性 {tgtProp.Name}（原因：明细 Ignore 方向声明命中正向）");
                        continue;
                    }
                    // ③ 跨表分流（ComposeMappings——嵌套对跨表声明；Q4 D-11 归一口复用）
                    if (composeByTarget != null && composeByTarget.TryGetValue(tgtProp.Name, out var cm))
                    {
                        if (cm.SourcePathNames.Length == 0)
                        {
                            MapTracer.Write($"{strategy.TracePrefix} 嵌套投影 {srcType.Name}→{tgtType.Name} 跳过属性 {tgtProp.Name}（原因：Compose 明细空组=忽略信号）");
                            continue;
                        }
                        // 副源取值（路线四 TB-20261127-01 卡 C1）：Compose 通道内改指**用户 join 的源参数**
                        // （不再按 [Navigate] 自建 JOIN）；匹配不到 fail-closed。非 Compose 通道既有语义不变。
                        Type? subType = cm.SubSourceTypeRef;
                        PropertyInfo? navProp = null;
                        Expression? joinedSubSource = subType != null
                            ? ComposeNestedSourceScope.RequireJoinedSource(subType, tgtProp.Name, strategy.TracePrefix)
                            : ComposeNestedSourceScope.RequireJoinedSourceByName(cm.SubSourceTypeName, tgtProp.Name, strategy.TracePrefix);
                        Expression subSourceExpr;
                        if (joinedSubSource != null)
                        {
                            subSourceExpr = joinedSubSource;
                        }
                        else
                        {
                            if (subType != null)
                            {
                                navProp = NavigationResolver.ResolveNavigationProperty(srcProps, subType, srcType.Name);
                            }
                            else
                            {
                                navProp = srcProps.FirstOrDefault(p =>
                                    NamesMatchRuntimeNormalized(p.PropertyType.FullName, cm.SubSourceTypeName));
                            }
                            if (navProp == null)
                            {
                                MapTracer.Write($"{strategy.TracePrefix} 嵌套投影 {srcType.Name}→{tgtType.Name} 跳过属性 {tgtProp.Name}（原因：副源 {cm.SubSourceTypeName} 未找到导航属性，fail-closed）");
                                continue;
                            }
                            subSourceExpr = Expression.Property(srcExpr, navProp);
                        }

                        var segs = BuildSegmentsWithIndexes(cm.SourcePathNames, cm.SourceIndexes);
                        // 副源类型：作用域外为导航属性类型；作用域内为 join 参数类型（统一取值，避免双源分叉）
                        var subSourceType = subSourceExpr.Type;
                        Expression? cmExpr;
                        if (segs.Length == 1 && !segs[0].Contains('['))
                        {
                            var subProps = GetPublicInstanceProperties(subSourceType);
                            var sp = ProjectionNameResolver.Resolve(subProps, segs[0]);
                            if (sp == null)
                            {
                                MapTracer.Write($"{strategy.TracePrefix} 嵌套投影 {srcType.Name}→{tgtType.Name} 跳过属性 {tgtProp.Name}（原因：副源 {subSourceType.Name} 中未找到声明字段 {segs[0]}）");
                                continue;
                            }
                            cmExpr = Expression.Property(subSourceExpr, sp);
                        }
                        else
                        {
                            var dotPath = string.Join(".", segs);
                            cmExpr = TryBuildDotPathExpression(subSourceExpr, subSourceType, dotPath, strategy, tgtProp);
                            if (cmExpr == null)
                            {
                                MapTracer.Write($"{strategy.TracePrefix} 嵌套投影 {srcType.Name}→{tgtType.Name} 跳过属性 {tgtProp.Name}（原因：Compose 明细路径 {dotPath} 无法构建点路径表达式）");
                                continue;
                            }
                        }
                        AddTypeCompatibleBinding(cmExpr, tgtProp, srcType, tgtType, depth, strategy, nestedBindings);
                        continue;
                    }
                    // ④ Properties 明细（主源——pm 未命中即矩阵 miss，不回退特性）
                    if (detailPmByName!.TryGetValue(tgtProp.Name, out var pm))
                    {
                        // 防御面：不可翻译声明（降级检测器已拦——防御性对齐现状抛错语义）
                        if (pm.IsFromJson)
                        {
                            MapTracer.Write($"{strategy.TracePrefix} 嵌套投影 {srcType.Name}→{tgtType.Name} 跳过属性 {tgtProp.Name}（原因：IsFromJson 字段不可翻译为 SQL 投影）");
                            continue;
                        }
                        if (pm.HasMask || pm.HasConverter)
                        {
                            throw new MapConfigurationException(
                                ErrorMessages.ProjectionAttributeNotSqlTranslatable(tgtType.Name, tgtProp.Name, pm.HasMask ? "Mask" : "Convert"));
                        }

                        var pathNames = pm.SourcePathNames;
                        if (pm.SourceName == null && pathNames is not { Length: > 0 })
                        {
                            MapTracer.Write($"{strategy.TracePrefix} 嵌套投影 {srcType.Name}→{tgtType.Name} 跳过属性 {tgtProp.Name}（原因：明细无正向源（ReverseOnly））");
                            continue;
                        }

                        Expression? pmExpr;
                        if (pathNames is { Length: > 1 })
                        {
                            var dotPath = string.Join(".", BuildSegmentsWithIndexes(pathNames, pm.SourceIndexes));
                            pmExpr = TryBuildDotPathExpression(srcExpr, srcType, dotPath, strategy, tgtProp);
                            if (pmExpr == null)
                            {
                                MapTracer.Write($"{strategy.TracePrefix} 嵌套投影 {srcType.Name}→{tgtType.Name} 跳过属性 {tgtProp.Name}（原因：明细嵌套路径 {dotPath} 无法构建点路径表达式）");
                                continue;
                            }
                        }
                        else
                        {
                            var single = pathNames is { Length: 1 }
                                ? BuildSegmentsWithIndexes(pathNames, pm.SourceIndexes)[0]
                                : pm.SourceName!;
                            var sp = ProjectionNameResolver.Resolve(srcProps, single);
                            if (sp == null)
                            {
                                MapTracer.Write($"{strategy.TracePrefix} 嵌套投影 {srcType.Name}→{tgtType.Name} 跳过属性 {tgtProp.Name}（原因：明细源字段 {single} 无匹配，且点路径/扁平化均不适用）");
                                continue;
                            }
                            // 源侧全局忽略判定（全局腿直调——对应现状 判定位）
                            if (!PropertyIgnoreChecker.HasOverrideIgnore(tgtProp)
                                && GlobalIgnoreChecker.ShouldIgnore(sp, isToDto: true))
                            {
                                MapTracer.Write($"{strategy.TracePrefix} 嵌套投影 {srcType.Name}→{tgtType.Name} 跳过属性 {tgtProp.Name}（原因：源字段 {sp.Name} 命中全局忽略清单）");
                                continue;
                            }
                            pmExpr = Expression.Property(srcExpr, sp);
                        }
                        AddTypeCompatibleBinding(pmExpr, tgtProp, srcType, tgtType, depth, strategy, nestedBindings);
                        continue;
                    }
                    // ⑤ 属性级 miss（矩阵——跳过 + Trace，提取期权威）。
                    // 嵌套/集合 DTO 属性除外（递归入口保留——与顶层快照腿 ⑤ 同口径；跳过即嵌套投影断链）。
                    if (Runtime.RuntimeSnapshotStore.IsRegisteredDto(tgtProp.PropertyType)
                        || ContainsRegisteredDtoElement(tgtProp.PropertyType))
                    {
                        // H-2 修复：取消 (ParameterExpression) 强转直传——嵌套语境 srcExpr 为
                        // 成员表达式（:1194 先例形态），强转必抛 InvalidCastException
                        var nestedValueExpr = BuildProjectionValueExpression(tgtProp, srcType, srcExpr, srcProps, strategy, fluentPmByName: null);
                        if (nestedValueExpr == null)
                        {
                            MapTracer.Write($"{strategy.TracePrefix} 嵌套投影 {srcType.Name}→{tgtType.Name} 跳过属性 {tgtProp.Name}（原因：嵌套/集合投影构建失败）");
                            continue;
                        }
                        nestedBindings.Add(Expression.Bind(tgtProp, nestedValueExpr));
                        continue;
                    }
                    MapTracer.Write($"{strategy.TracePrefix} 嵌套投影 {srcType.Name}→{tgtType.Name} 跳过属性 {tgtProp.Name}（原因：明细无该目标属性映射声明（提取期权威））");
                    continue;
                }

                // ── 兜底腿（嵌套对条目命中明细不可达 / miss——现状逻辑原样，D2 保留原调用）──
                if (IsMapIgnoreDefined(tgtProp)) continue;
                if (PropertyMatcher.ShouldIgnore(tgtProp, isToDto: true, PropertyIgnoreChecker.HasOverrideIgnore(tgtProp))) continue;

                PropertyMatcher.TryGetMapPropertyInfo(tgtProp, srcType,
                    out var srcFieldName, out var fieldSourceType, out var isFromJson);

                if (isFromJson)
                {
                    MapTracer.Write($"{strategy.TracePrefix} 嵌套投影 {srcType.Name}→{tgtType.Name} 跳过属性 {tgtProp.Name}（原因：IsFromJson 字段不可翻译为 SQL 投影）");
                    continue;
                }

                // Mask/Convert 不可翻译为 SQL 投影 → 应走降级路径（与提取前行为一致）
                // 行 9（行 9—— 附 B-① 判类 = 无门直读加门）：
                // 加**特性锚门**——Fluent 对上的残留 [Mask]/[Convert] 不构成"不可翻译"判据（运行时不
                // 消费），原抛错场景放行按明细投影（预期差异面登记： 第三族）
                var nestedHasAttrAnchor = RuntimeAnchorGate.HasAttributeAnchor(tgtProp.DeclaringType!);
                var maskAttr = nestedHasAttrAnchor ? tgtProp.GetCustomAttribute<MaskAttribute>() : null;
                var converterAttr = nestedHasAttrAnchor ? tgtProp.GetCustomAttribute<ConvertAttribute>() : null;
                if (maskAttr != null || converterAttr != null)
                {
                    var attrName = maskAttr != null ? "Mask" : "Convert";
                    throw new MapConfigurationException(
                        ErrorMessages.ProjectionAttributeNotSqlTranslatable(tgtType.Name, tgtProp.Name, attrName));
                }

                Expression valExpr;
                if (fieldSourceType == srcType)
                {
                    var srcProp = ProjectionNameResolver.Resolve(srcProps, srcFieldName);
                    if (srcProp == null)
                    {
                        // 嵌套 DTO 内路径——点路径 + 推导扁平化（与顶层
                        // BuildProjectionValueExpression 同语义；TryBuild* 内部已做叶节点忽略判定，命中返回 null）
                        // 集合元素绑定同样启用路径分支（门控移除）
                        if (srcFieldName.Contains('.') || srcFieldName.Contains('['))
                        {
                            var dotExpr = TryBuildDotPathExpression(srcExpr, srcType, srcFieldName, strategy, tgtProp);
                            if (dotExpr != null)
                            {
                                // 原 goto TypeCompatible——判据链已上提 AddTypeCompatibleBinding，直接消费后结束本轮
                                AddTypeCompatibleBinding(dotExpr, tgtProp, srcType, tgtType, depth, strategy, nestedBindings);
                                continue;
                            }
                        }
                        var flatExpr = TryBuildFlattenedExpression(srcExpr, srcType, srcFieldName, tgtProp, strategy);
                        if (flatExpr != null)
                        {
                            AddTypeCompatibleBinding(flatExpr, tgtProp, srcType, tgtType, depth, strategy, nestedBindings);
                            continue;
                        }

                        MapTracer.Write($"{strategy.TracePrefix} 嵌套投影 {srcType.Name}→{tgtType.Name} 跳过属性 {tgtProp.Name}（原因：源类型中未找到字段 {srcFieldName}，且点路径/扁平化均无匹配路径）");
                        continue;
                    }

                    if (ShouldIgnoreSource(srcProp, tgtProp))
                    {
                        MapTracer.Write($"{strategy.TracePrefix} 嵌套投影 {srcType.Name}→{tgtType.Name} 跳过属性 {tgtProp.Name}（原因：源字段 {srcProp.Name} 命中全局忽略清单）");
                        continue;
                    }

                    valExpr = Expression.Property(srcExpr, srcProp);
                }
                else
                {
                    // 跨表取值（路线四 TB-20261127-01 卡 C1）：Compose 通道内直接取**用户 join 的该源参数**
                    // （原按 [Navigate] 自建 JOIN 会绕过用户 join 条件——缺陷③）；匹配不到 fail-closed。
                    // 非 Compose 通道（作用域外恒返回 null）→ 既有语义逐字不变。
                    var joinedFieldSource = ComposeNestedSourceScope.RequireJoinedSource(
                        fieldSourceType, tgtProp.Name, strategy.TracePrefix);
                    Expression? navAccessExpr;
                    if (joinedFieldSource != null)
                    {
                        navAccessExpr = joinedFieldSource;
                    }
                    else
                    {
                        var navProp = NavigationResolver.ResolveNavigationProperty(
                            srcProps, fieldSourceType, srcType.Name);
                        if (navProp == null)
                        {
                            MapTracer.Write($"{strategy.TracePrefix} 嵌套投影 {srcType.Name}→{tgtType.Name} 跳过属性 {tgtProp.Name}（原因：源类型中未找到类型为 {fieldSourceType.Name} 的导航属性）");
                            continue;
                        }
                        navAccessExpr = Expression.Property(srcExpr, navProp);
                    }

                    var navFieldProps = GetPublicInstanceProperties(fieldSourceType);
                    var navField = ProjectionNameResolver.Resolve(navFieldProps, srcFieldName);
                    if (navField == null)
                    {
                        MapTracer.Write($"{strategy.TracePrefix} 嵌套投影 {srcType.Name}→{tgtType.Name} 跳过属性 {tgtProp.Name}（原因：导航类型 {fieldSourceType.Name} 中未找到字段 {srcFieldName}）");
                        continue;
                    }

                    if (ShouldIgnoreSource(navField, tgtProp))
                    {
                        MapTracer.Write($"{strategy.TracePrefix} 嵌套投影 {srcType.Name}→{tgtType.Name} 跳过属性 {tgtProp.Name}（原因：导航字段 {navField.Name} 命中全局忽略清单）");
                        continue;
                    }

                    var fieldAccessExpr = Expression.Property(navAccessExpr, navField);

                    // 值类型目标属性：导航为 null 时给默认值（避免 NullReference）
                    if (tgtProp.PropertyType.IsValueType
                        && Nullable.GetUnderlyingType(tgtProp.PropertyType) == null
                        && navField.PropertyType != tgtProp.PropertyType)
                    {
                        // 无可转性组合（含 T?→T——IsImplicitConvertible 为
                        // false）现状经 Expression.Convert 在运行期裸抛（下方 Condition 只保护导航 null），
                        // 构建期无信号。前置预检跳过该绑定，对齐 判据与顶层 ConvertProjectionType
                        // "不可转返回 null 跳过"语义（行为变更个案已登记 ⑦）。
                        if (!TypeConverter.IsImplicitConvertible(navField.PropertyType, tgtProp.PropertyType))
                        {
                            MapTracer.Write($"{strategy.TracePrefix} 嵌套投影 {srcType.Name}→{tgtType.Name} 跳过属性 {tgtProp.Name}（原因：字段类型 {navField.PropertyType.Name} 不可隐式转换为 {tgtProp.PropertyType.Name}）");
                            continue;
                        }
                        valExpr = Expression.Condition(
                            Expression.Equal(navAccessExpr, Expression.Constant(null, navAccessExpr.Type)),
                            Expression.Default(tgtProp.PropertyType),
                            Expression.Convert(fieldAccessExpr, tgtProp.PropertyType));
                    }
                    else if (tgtProp.PropertyType.IsValueType
                        && Nullable.GetUnderlyingType(tgtProp.PropertyType) == null)
                    {
                        valExpr = Expression.Condition(
                            Expression.Equal(navAccessExpr, Expression.Constant(null, navAccessExpr.Type)),
                            Expression.Default(tgtProp.PropertyType),
                            fieldAccessExpr);
                    }
                    else
                    {
                        valExpr = fieldAccessExpr;
                    }
                }

                // 原 TypeCompatible 标签 + 内联判据链（:814-861）已上提
                // AddTypeCompatibleBinding——判定走 ClassifyProjectionCompat 单点，跳过路径内聚于方法内
                AddTypeCompatibleBinding(valExpr, tgtProp, srcType, tgtType, depth, strategy, nestedBindings);
            }

            return nestedBindings.Count == 0 ? null : nestedBindings;
        }

        /// <summary>
        /// 集合投影 —— List&lt;SrcItem&gt; → List&lt;TgtItem&gt;（Select + ToList；EFCore 翻译为子查询）
        /// </summary>
        [RequiresDynamicCode("BuildCollectionProjection builds collection projection via MakeGenericMethod/MakeGenericType. Not supported in AOT.")]
        [RequiresUnreferencedCode("BuildCollectionProjection scans type members and attributes via reflection. Some members may be trimmed.")]
        private static Expression? BuildCollectionProjection(
            Type srcCollectionType, Type tgtCollectionType,
            Type srcElementType, Type tgtElementType,
            Expression collectionAccess, int depth, ProjectionStrategy strategy)
        {
            if (depth > strategy.MaxNestingDepth)
            {
                MapTracer.Write($"{strategy.TracePrefix} 跳过集合投影 {srcCollectionType.Name}→{tgtCollectionType.Name}（原因：嵌套深度 {depth} 超过上限 {strategy.MaxNestingDepth}）");
                return null;
            }

            if (depth > strategy.WarningDepth)
                MapTracer.Write($"{strategy.TracePrefix} 集合投影 {srcCollectionType.Name}→{tgtCollectionType.Name} 深度 {depth} 超过建议值 {strategy.WarningDepth}");

            // 补遗：集合元素主源一致性校验（注册表优先 + 反射回退）
            // — Fluent DTO 注册表命中后 PrimarySource 与 srcElementType 比对通过
            if (!RuntimeSnapshotStore.TryGetMappingMeta(tgtElementType, out var view777)
                || view777.PrimarySource != srcElementType)
            {
                MapTracer.Write($"{strategy.TracePrefix} 跳过集合投影 {srcCollectionType.Name}→{tgtCollectionType.Name}（原因：元素类型 {tgtElementType.Name} 未标注 [MapFrom(typeof({srcElementType.Name}))]）");
                return null;
            }

            var itemParam = Expression.Parameter(srcElementType, "item");
            // 集合元素绑定启用路径分支（与嵌套 DTO 一致）
            // 集合元素内点路径/推导扁平化/叶节点忽略现受支持；深度边界由 MaxNestingDepth 控制
            var elementBindings = BuildNestedProjectionBindings(srcElementType, tgtElementType, itemParam, depth, strategy);
            if (elementBindings == null)
            {
                MapTracer.Write($"{strategy.TracePrefix} 跳过集合投影 {srcCollectionType.Name}→{tgtCollectionType.Name}（原因：元素绑定构建失败）");
                return null;
            }

            var elementCtor = GetParameterlessCtor(tgtElementType);
            if (elementCtor == null)
            {
                MapTracer.Write($"{strategy.TracePrefix} 跳过集合投影 {srcCollectionType.Name}→{tgtCollectionType.Name}（原因：元素类型 {tgtElementType.Name} 无无参构造器）");
                return null;
            }

            var elementInit = Expression.MemberInit(Expression.New(elementCtor), elementBindings);
            var selectLambda = Expression.Lambda(elementInit, itemParam);

            // 静态 MethodInfo 复用（Select 非索引 Func<,> 形态消歧 / ToList 条件逐字保持）
            var selectMethod = s_selectDef.MakeGenericMethod(srcElementType, tgtElementType);
            var selectCall = Expression.Call(selectMethod, collectionAccess, selectLambda);

            var toListMethod = s_toListDef.MakeGenericMethod(tgtElementType);
            var toListCall = Expression.Call(toListMethod, selectCall);

            var resultType = typeof(List<>).MakeGenericType(tgtElementType);
            Expression resultExpr;
            if (tgtCollectionType.IsAssignableFrom(resultType))
            {
                resultExpr = toListCall;
            }
            else if (tgtCollectionType.IsAssignableFrom(typeof(IEnumerable<>).MakeGenericType(tgtElementType)))
            {
                resultExpr = selectCall;
            }
            else
            {
                MapTracer.Write($"{strategy.TracePrefix} 跳过集合投影 {srcCollectionType.Name}→{tgtCollectionType.Name}（原因：目标类型 {tgtCollectionType.Name} 不可以 List<{tgtElementType.Name}> 赋值）");
                return null;
            }

            // Null 传播：集合为 null 时目标为 null
            var nullCheck = Expression.Equal(collectionAccess, Expression.Constant(null, srcCollectionType));
            return Expression.Condition(nullCheck,
                Expression.Constant(null, tgtCollectionType),
                resultExpr);
        }

        /// <summary>
        /// 获取类型的公共实例属性列表（带缓存；排除索引器属性）
        /// </summary>
        /// <param name="type">目标类型</param>
        /// <returns>公共实例属性列表</returns>
        [RequiresUnreferencedCode("GetPublicInstanceProperties scans type members and attributes via reflection. Some members may be trimmed.")]
        internal static List<PropertyInfo> GetPublicInstanceProperties(Type type)
            => s_propertiesCache.GetOrAdd(type,
                t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                      .Where(p => p.GetIndexParameters().Length == 0).ToList());

        /// <summary>
        /// 获取类型的无参公共构造器（带缓存）
        /// </summary>
        /// <param name="type">目标类型</param>
        /// <returns>无参构造器；不存在时为 null</returns>
        [RequiresUnreferencedCode("GetParameterlessCtor scans type members and attributes via reflection. Some members may be trimmed.")]
        internal static ConstructorInfo? GetParameterlessCtor(Type type)
            => s_ctorCache.GetOrAdd(type, t => t.GetConstructor(Type.EmptyTypes));

        /// <summary>
        /// 判定属性是否标注 [MapIgnore]（带缓存）
        /// <para>加**特性锚门**——所在类型无
        /// <c>[MapFrom]</c> 特性锚时（Fluent 对 / 无锚）标注不构成投影剔除依据，投影按快照明细执行
        ///（007 实证的"特性压过明细"在投影链形态的收口；与 既有 <c>ShouldIgnore</c> 锚门同口径——
        /// 冗余关系复核记录见）。缓存腿不变：<c>s_mapIgnoreDefinedCache</c> 仅缓存标注
        /// 存在性，锚判定走 <c>RuntimeAnchorGate</c> 的 CWT 反射腿缓存（零新增反射路径）。</para>
        /// </summary>
        /// <param name="prop">属性信息</param>
        /// <returns>标注 [MapIgnore] 且所在类型具特性锚时为 true</returns>
        internal static bool IsMapIgnoreDefined(PropertyInfo prop)
            => RuntimeAnchorGate.HasAttributeAnchor(prop.DeclaringType!)
               && s_mapIgnoreDefinedCache.GetOrAdd(prop,
                   p => p.IsDefined(typeof(MapIgnoreAttribute)));

        /// <summary>
        /// 获取类型上的 [MapFrom] 特性（带缓存）
        /// </summary>
        /// <param name="type">目标类型</param>
        /// <returns>MapFrom 特性；未标注时为 null</returns>
        internal static MapFromAttribute? GetCachedMapFromAttribute(Type type)
            => s_mapFromAttrCache.GetOrAdd(type, t => t.GetCustomAttribute<MapFromAttribute>());
    }
}



