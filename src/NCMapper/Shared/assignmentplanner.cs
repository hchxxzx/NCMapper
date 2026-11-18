using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using NCMapper.Attributes;
using NCMapper.Runtime;
using NCMapper.Internal;

namespace NCMapper.Shared;

/// <summary>
/// 赋值计划器 — 赋值决策单点化。
/// <para>规则表与运行时决策链逐条对照（附录 C A 链 8 步 + BuildMapBody ConvertUsing/Mask 前置）：
/// 现状 ConvertUsing/Mask 决策在 BuildMapBody（ExpressionBuilder.cs:138-182，:138 注释"优先于所有其他转换路径"），
/// 本计划器以 hasConvertUsing/hasMask 输入承载（M7 修正：规则 2 前置）；忽略判定由调用方完成
/// （BuildMapBody / TryBuildSingleBinding），本计划器以 globallyIgnored 输入承载（规则 1）。</para>
/// <para>执行定稿（相对规则表的细化，登记 D4）：
/// ① 规则 9 为"类型不同 → Convert"（无条件，镜像 ExpressionBuilder.cs:351-354 现状——运行时对
/// 类型不同的属性总是构建转换尝试，无 CanConvert 预检；CanConvert 的预检语义在生成侧由 HasMappingPath
/// 承载、在校验侧由 TypeCompatView 承载）；② Direct（规则 10）必须位于 Convert 之后且以等类型为条件，
/// 避免 CanConvert 对等类型对误判 Convert。</para>
/// <para>② 枢纽深化：新增 Plan(PropertyInfo) 重载——前置判定（ConvertUsing/Mask/
/// ConvertBackUsing/Ignore）收进 Plan，三渲染器按 Kind 分发渲染只渲染计划不决策。
/// 旧 Plan(Type,...) 重载保留供类型形态子集复用，规则 2 标记废弃（两运行时调用方恒传 false）。</para>
/// <para>本文件位于 Shared 目录，assignmentplan.cs 已加入 SourceGen.csproj Link 列表（② T2 新增 Link）；
/// 本文件（assignmentplanner.cs）依赖 TypeConverter/CollectionTypeDetector/PropertyIgnoreChecker/
/// PropertyMatcher/特性类（NCMapper.Attributes），不加入 SourceGen.csproj Link——生成侧由
/// PlanSymbolAdapter（T2 新建）实现符号版形态判定。</para>
/// </summary>
internal static class AssignmentPlanner
{
    // CacheRegistry 登记制自注册（补漏）：仅登记清理委托，不触发缓存构建（防自触发）
    static AssignmentPlanner()
    {
        CacheRegistry.Register("assignment-planner", CacheLayer.Full, ClearCache);
    }

    /// <summary>
    /// Plan(PropertyInfo) 结果缓存 — 避免同一属性对重复执行前置判定 + 特性查询
    /// <para>缓存键：(sourceItem, targetItem, isToDto, attr, crossTableFromSource) 五元组
    /// + 对级类型分量 (registrySourceType, registryTargetType)——注册表腿是该缓存的第一个
    /// 对级状态输入（键含对级类型；null 上下文两分量同为 null，自成一组键与带上下文键不互串）；
    /// 值类型为 readonly record struct CachedPlan——版本条目内比对 + 索引器写回替换
    /// （版本不作键成分，在册纪律），键空间恒定、版本维度零沉淀。
    /// ConcurrentDictionary 线程安全（TOCTOU 无风险——重复计算结果等价）。</para>
    /// <para>A2-02 缓存治理：动态代理/collectible 场景键恒新零命中，
    /// Plan 入口直算不入缓存（同口径，判据 PropertyMatcher.IsDynamicType 复用）；
    /// collectible ALC 长驻场景须显式 <see cref="ClearCache"/>（经 CacheRegistry "assignment-planner"
    /// 登记项接入 Mapper.ClearCache 全链）——弱键化（CWT）改造出批。</para>
    /// </summary>
    private static readonly ConcurrentDictionary<PlanCacheKey, CachedPlan> s_planCache = new();

    /// <summary>Plan 缓存条目 — 版本在条目内比对（非键成分，在册纪律），失配重算后索引器写回替换同键旧值。</summary>
    private readonly record struct CachedPlan(long ContentVersion, AssignmentPlan Plan);

    /// <summary>
    /// 赋值决策（运行时重载） — 接收 PropertyInfo 完成全部前置判定（② 枢纽深化）。
    /// <para>前置判定（ConvertUsing/Mask/ConvertBackUsing/Ignore）原散落在三渲染器，收进 Plan。
    /// Plan 只产出 Kind + 标记位，不携带特性实例——渲染器按 Kind 自行查询特性实例。</para>
    /// <para>isFromJson 从 attr?.IsFromJson == true 派生。</para>
    /// </summary>
    /// <param name="sourceItem">源属性（可为 null——无源属性场景由调用方在调 Plan 前处理特殊路径）</param>
    /// <param name="targetItem">目标属性</param>
    /// <param name="isToDto">方向（目标标 [MapFrom] → ToDto）</param>
    /// <param name="attr">[MapProperty] 特性（非 null 表示有显式标注）</param>
    /// <param name="crossTableFromSource">跨表导航源类型（null 表示非跨表）</param>
    /// <param name="registryCtx">注册表 Mask 明细上下文（null = 无注册声明恒等态；对级分量入缓存键）</param>
    /// <returns>赋值计划（含前置判定结果编码为 Kind + 标记位）</returns>
    [RequiresUnreferencedCode("Plan(PropertyInfo) calls PropertyMatcher.ShouldIgnore which triggers config freeze, reflecting over [assembly: MapDefaults] when no generated snapshot exists.")]
    public static AssignmentPlan Plan(
        PropertyInfo? sourceItem, PropertyInfo targetItem,
        bool isToDto,
        MapPropertyAttribute? attr, Type? crossTableFromSource,
        RegistryMaskContext? registryCtx = null)
    {
        // 动态类型直算不入缓存（同口径，先例
        // ExpressionBuilder.Paths.cs useCache 形态）——动态代理场景 PropertyInfo/特性实例每次
        // 新建 → 强键恒新，入缓存即无界增长且零命中；PlanCore 纯函数重复计算等价（:42 注释）。
        // sourceItem 可空：null 时也无稳定键，直算（守卫式空安全形态，首评 P1-4）。
        if (sourceItem is null
            || PropertyMatcher.IsDynamicType(sourceItem.DeclaringType!)
            || PropertyMatcher.IsDynamicType(targetItem.DeclaringType!))
        {
            return PlanCore(sourceItem, targetItem, isToDto, attr, crossTableFromSource, registryCtx);
        }
        var key = new PlanCacheKey(sourceItem, targetItem, isToDto, attr, crossTableFromSource,
            registryCtx?.SourceType, registryCtx?.TargetType);
        if (s_planCache.TryGetValue(key, out var cached))
        {
            // 版本条目内比对（非键成分）：失配重算并索引器写回替换同键旧值
            if (cached.ContentVersion == RuntimeSnapshotStore.ContentVersion)
                return cached.Plan;
            var refreshed = PlanCore(sourceItem, targetItem, isToDto, attr, crossTableFromSource, registryCtx);
            s_planCache[key] = new CachedPlan(RuntimeSnapshotStore.ContentVersion, refreshed);
            return refreshed;
        }
        var plan = PlanCore(sourceItem, targetItem, isToDto, attr, crossTableFromSource, registryCtx);
        s_planCache[key] = new CachedPlan(RuntimeSnapshotStore.ContentVersion, plan);
        return plan;
    }

    /// <summary>
    /// Plan(PropertyInfo) 核心实现（无缓存，由 Plan(PropertyInfo) 缓存层调用）。
    /// </summary>
    [RequiresUnreferencedCode("PlanCore calls PropertyMatcher.ShouldIgnore which triggers config freeze, reflecting over [assembly: MapDefaults] when no generated snapshot exists.")]
    private static AssignmentPlan PlanCore(
        PropertyInfo? sourceItem, PropertyInfo targetItem,
        bool isToDto,
        MapPropertyAttribute? attr, Type? crossTableFromSource,
        RegistryMaskContext? registryCtx)
    {
        // 1. Ignore 判定（含 globalIgnoreExempt 双侧——以 Block 语义为基准）
        // PropertyIgnoreChecker.ShouldIgnore(:32) 内部已含全局忽略清单 + OverrideIgnore 豁免门
        // 不再设独立 globallyIgnored 门（避免死参数或绕过豁免门）
        if (sourceItem != null)
        {
            var globalIgnoreExempt = PropertyIgnoreChecker.HasOverrideIgnore(targetItem)
                                   || PropertyIgnoreChecker.HasOverrideIgnore(sourceItem);
            if (PropertyMatcher.ShouldIgnore(sourceItem, isToDto, globalIgnoreExempt)
                || PropertyMatcher.ShouldIgnore(targetItem, isToDto, globalIgnoreExempt))
                return new AssignmentPlan(AssignmentKind.Ignore);
        }

        // 2. ConvertBackUsing 判定（反向专用，!isToDto 时）
        // 单查承载侧（反向 sourceItem）——对侧兜底支与 sourceItem==null
        // else 支删除（；与 注释"标注于 DTO 属性（sourceItem 优先）"一致）。
        // sourceItem == null → 无承载侧锚点，[ConvertBack] 不消费（无锚不消费口径）。
        // （发现 #1）：承载侧锚门——所在类型无锚时不消费
        // 收紧：锚门由双锚（HasAnchor）改**特性锚**（HasAttributeAnchor）——Fluent 对不消费
        var backAttr = sourceItem != null && RuntimeAnchorGate.HasAttributeAnchor(sourceItem.DeclaringType!)
            ? PropertyIgnoreChecker.GetConvertBackAttribute(sourceItem)
            : null;
        if (backAttr != null && !isToDto)
        {
            return new AssignmentPlan(AssignmentKind.ConvertBackUsing);
        }
        // 命中且 isToDto → 不消费（正向不消费 ConvertBackUsing，落入类型形态决策）

        // 3. ConvertUsing 判定
        // 现状 ExpressionBuilder.cs:251-287：正向消费/反向降级/双标组合
        // [Convert] 单查承载侧（恒 DTO 属性——正向=targetItem、
        // 反向=sourceItem；消歧），对侧兜底删；反向降级标记（NCMAP033
        // 运行时镜像）语义保持——承载侧命中后 !isToDto 分支降级
        // （发现 #1）：承载侧锚门（恒 DTO 属性——正向 target/
        // 反向 source；所在类型无锚时不消费）
        // 收紧：两腿锚门均改**特性锚**——Fluent 对不消费 [Convert]
        var converterAttr = isToDto
            ? (RuntimeAnchorGate.HasAttributeAnchor(targetItem.DeclaringType!)
                ? PropertyIgnoreChecker.GetConvertAttribute(targetItem) : null)
            : (sourceItem != null && RuntimeAnchorGate.HasAttributeAnchor(sourceItem.DeclaringType!)
                ? PropertyIgnoreChecker.GetConvertAttribute(sourceItem) : null);
        bool hasConvertUsing = converterAttr != null;
        bool isConvertedFromConvertUsing = false;
        if (hasConvertUsing && !isToDto)
        {
            // 反向降级：不消费转换器，与生成端 NCMAP033 同语义
            // 产出 Convert Kind + IsConvertedFromConvertUsing=true
            hasConvertUsing = false;
            isConvertedFromConvertUsing = true;
        }

        // 3.5 Format 判定（插在第 3 步后、
        // 第 4 步前；Format 与 ConvertUsing 互斥由编译期 NCMAP045 拦，运行时同标属理论路径，
        // 决策链取 Format——本步先于第 5 步 ConvertUsing 正向命中）
        bool hasFormat = false;
        if (!isToDto)
        {
            // 方向门：反向不产 Format Kind。执行后设计修订（用户拍板，附录 B）：
            // 反向跳过——Format 是单向展示声明（格式化输出有损且随文化，反解析不闭合），裸声明合法；
            // 反向命中（未标 [ConvertBack]）→ Ignore 计划（属性不赋值，不编数据、不抛异常），
            // 生成端 NCMAP046/F67 已给警告提示；已标 [ConvertBack] 为合法组合（方向正交），反向照常。
            // 锚门同口径：所在类型无锚时不消费（"无锚不消费"家族协议）。
            // 收紧：改**特性锚**——Fluent 对反向不消费 [MapFormat] 触发 Ignore
            if (sourceItem != null && backAttr == null
                && RuntimeAnchorGate.HasAttributeAnchor(sourceItem.DeclaringType!)
                && PropertyIgnoreChecker.GetMapFormatAttribute(sourceItem) != null)
            {
                if (MapTracer.GloballyEnabled)
                    MapTracer.Write(ErrorMessages.MapFormatReverseSkipped(sourceItem.Name));
                return new AssignmentPlan(AssignmentKind.Ignore);
            }
        }
        else
        {
            // 收紧：正向 Format 准入改**特性锚**——Fluent 对不消费 [MapFormat]
            var formatAttr = RuntimeAnchorGate.HasAttributeAnchor(targetItem.DeclaringType!)
                ? PropertyIgnoreChecker.GetMapFormatAttribute(targetItem)
                : null;
            // 源/目标守卫（镜像 Mask 口径）：目标 string + 源 IFormattable（含 Nullable<T> 解包；
            // 枚举经接口判定自然通过——Enum 实现 IFormattable，net10 实测）；不满足 → 不产 Format
            //（落类型形态决策——生成侧场景编译期已由 NCMAP043/044 报出）；无源属性不产 Format
            if (formatAttr != null
                && targetItem.PropertyType == typeof(string)
                && IsFormattableSource(sourceItem))
            {
                hasFormat = true;
            }
        }

        // 4. Mask/Format 判定（ConvertUsing 未命中或反向降级后）
        // 现状 ExpressionBuilder.cs:289-302：string 守卫 + Trace 警告
        // （发现 #1）：锚门——所在类型无锚时不消费
        // 收紧：改**特性锚**——Fluent 对不消费 [Mask]
        var maskAttr = RuntimeAnchorGate.HasAttributeAnchor(targetItem.DeclaringType!)
            ? PropertyIgnoreChecker.GetMaskAttribute(targetItem)
            : null;
        // 注册表腿（并行决策腿）：特性腿先行（分派优先级），仅特性腿未命中时查注册表——
        // 注册表腿不受特性锚门约束（锚门是"Fluent 对不消费 [Mask] 特性"的收紧位，
        // 注册表腿消费的是注册声明本身；不加特性锚门，防抵消修复）。
        // plan 携带 RegistryMaskTypeName（string 决策结果，不触特性实例不变式）
        var registryMaskName = maskAttr == null && registryCtx != null && registryCtx.HasRegistryMask(targetItem.Name)
            ? registryCtx.MaskTypeNameOf(targetItem.Name)
            : null;
        // 入口扩为 (maskAttr != null || registryMaskName != null || hasFormat) && 目标 string——
        // 否则纯 [MapFormat]（无 [Mask]）或纯注册表 Mask 时整块跳过
        if ((maskAttr != null || registryMaskName != null || hasFormat) && targetItem.PropertyType == typeof(string))
        {
            // 源类型守卫——单标 Mask 源非 string（值类型等）时
            // 渲染层 BuildMaskValue 需 Convert(源, string)（无 coercion operator，
            // 构建期裸抛 InvalidOperationException），落类型形态决策（int→string 等
            // 走常规转换路径）；源 string 或无源属性时维持原 Mask 语义不变。
            // 双标组合（ConvertUsing 正向命中）不受源类型约束——转换器输出（TField，
            // BuildMaskValue 双标调用传转换结果）才是 Mask 输入，与源属性类型无关。
            if (hasConvertUsing && isToDto)
                // 双标有序执行——位段必须随 Kind 同位携带：注册表形态携带解析名（渲染腿分派依据），
                // 特性形态两来源互斥恒 null（漏带则渲染腿特性查询 miss 抛"内部不变式违约"）
                return new AssignmentPlan(AssignmentKind.ConvertUsingThenMask, RegistryMaskTypeName: registryMaskName);
            // FormatThenMask 双标：须在下方"源非 string 落常规路径"守卫之前
            // 双标下源本非 string（如 DateTime 源），该守卫不适用（镜像 位置语义）
            if (maskAttr != null && hasFormat)
                return new AssignmentPlan(AssignmentKind.FormatThenMask);
            if (registryMaskName != null && hasFormat)
                return new AssignmentPlan(AssignmentKind.FormatThenMask, RegistryMaskTypeName: registryMaskName); // 注册表 Mask + 特性 Format 双标
            if (maskAttr != null && (sourceItem == null || sourceItem.PropertyType == typeof(string)))
                return new AssignmentPlan(AssignmentKind.Mask);
            if (registryMaskName != null && (sourceItem == null || sourceItem.PropertyType == typeof(string)))
                return new AssignmentPlan(AssignmentKind.Mask, RegistryMaskTypeName: registryMaskName);
            if (hasFormat && maskAttr == null && registryMaskName == null)
                return new AssignmentPlan(AssignmentKind.Format);
            // 源非 string + 单标 [Mask] → 落类型形态决策（Trace 由渲染侧/主循环承担，
            // Plan 纯决策不写 Trace）
        }
        // maskAttr != null && non-string → Trace 警告 + 按常规映射（落入类型形态决策）
        // （Trace 由 RenderPlan 负责，Plan 不写 Trace——Plan 是纯决策不抛异常）

        // 5. ConvertUsing 正向命中（Mask 未命中或非 string）
        if (hasConvertUsing && isToDto)
            return new AssignmentPlan(AssignmentKind.ConvertUsing);

        // 6. 无源属性且无 ConvertUsing → Skip
        if (sourceItem == null)
            return new AssignmentPlan(AssignmentKind.Skip);

        // 7. 类型形态决策（复用现有 Plan(Type,...) 规则 3-10）
        // 顺序须与现有一致：Nested→Collection→Json→CrossTable
        return PlanTypeShape(
            sourceItem.PropertyType, targetItem.PropertyType,
            isFromJson: attr?.IsFromJson == true,
            crossTableFromSource: crossTableFromSource,
            isConvertedFromConvertUsing: isConvertedFromConvertUsing);
    }

    /// <summary>
    /// 类型形态决策（规则 3-10）— 从 Plan(Type,...) 提取，供 Plan(PropertyInfo) 复用。
    /// <para>规则序：Nested→Collection→Json→CrossTable→可空→类型不同→等类型。
    /// 不含规则 1（Ignore）和规则 2（ConvertUsing/Mask），由 Plan(PropertyInfo) 前置处理。</para>
    /// </summary>
    /// <param name="sourcePropType">源属性类型</param>
    /// <param name="targetPropType">目标属性类型</param>
    /// <param name="isFromJson">[MapProperty(IsFromJson=true)]</param>
    /// <param name="crossTableFromSource">跨表导航源类型</param>
    /// <param name="isConvertedFromConvertUsing">反向 ConvertUsing 降级标记</param>
    /// <returns>类型形态决策计划</returns>
    private static AssignmentPlan PlanTypeShape(
        Type sourcePropType, Type targetPropType,
        bool isFromJson, Type? crossTableFromSource,
        bool isConvertedFromConvertUsing)
    {
        // 3. 嵌套类（异型 class，非 string、非泛型、非数组）→ Nested
        if (IsNestedClassShape(sourcePropType, targetPropType))
            return new AssignmentPlan(AssignmentKind.Nested, NestedTargetType: targetPropType);

        // 4. 集合?集合（双方 IEnumerable，非 string；同型/异型合并）→ Collection
        if (IsCollectionShape(sourcePropType, targetPropType))
            return new AssignmentPlan(AssignmentKind.Collection);

        // 5. [MapProperty(IsFromJson)] → Json
        if (isFromJson)
            return new AssignmentPlan(AssignmentKind.Json, JsonTargetType: targetPropType);

        // 6. 跨表导航（FromSource≠源类型）→ CrossTable
        if (crossTableFromSource != null)
            return new AssignmentPlan(AssignmentKind.CrossTable, CrossTableSourceType: crossTableFromSource);

        // 7. 可空→非可空值类型 → 条件转换
        if (TypeConverter.IsNullableType(sourcePropType)
            && !TypeConverter.IsNullableType(targetPropType)
            && targetPropType.IsValueType)
            return new AssignmentPlan(AssignmentKind.Convert, ConvertTargetType: targetPropType, IsNullableConditional: true);

        // 8. 非可空值类型→可空 → 转换
        if (!TypeConverter.IsNullableType(sourcePropType)
            && sourcePropType.IsValueType       // 引用类型（如 string）不可直接转 Nullable<T>
            && targetPropType.IsValueType
            && TypeConverter.IsNullableType(targetPropType))
            return new AssignmentPlan(AssignmentKind.Convert, ConvertTargetType: targetPropType);

        // 9. 类型不同 → Convert（:351-354 镜像：运行时总是构建转换尝试，D4 执行定稿）
        if (sourcePropType != targetPropType)
            return new AssignmentPlan(AssignmentKind.Convert, ConvertTargetType: targetPropType, IsConvertedFromConvertUsing: isConvertedFromConvertUsing);

        // 10. 等类型 → Direct
        // 等类型时 isConvertedFromConvertUsing 不可能为 true（反向 ConvertUsing 降级要求类型不同）
        return new AssignmentPlan(AssignmentKind.Direct);
    }

    // A2-10②处置登记（出口型）：编制期"全仓零调用、可删除"实证被
    // 门禁证伪——生产调用面确为零（MapperBase.cs:598 / ExpressionBuilder.cs:288 走 5 参
    // Plan(PropertyInfo,...)；PlanSymbolAdapter 自有 Plan 非本方法），但测试面有真实消费：
    // test/NCMapper.Tests/Tests/AssignmentPlanHubTestsExtended.cs:40/51/67 与
    // test/NCMapper.SourceGen.Tests/plansymbolconsistencytests.cs:56 经本 8 参形态直测规则 3-10。
    // 按 B2（不得改测试迁就实现）与"现状即契约"出口（B4-12 先例）保留本重载，原
    // "保留供 PlanSymbolAdapter 复用 / T6 后可 [Obsolete] 或删除"承诺改判为事实陈述：
    // 本重载 = 规则 3-10 的无特性直测入口（生产零调用，测试专用消费面）；
    // 若未来删除，须同步迁移上述测试调用点，属后续批域。
    /// <summary>
    /// 赋值决策（类型形态子集） — 保留现有签名供 adapter 复用。
    /// <para>② 枢纽深化：规则 2（hasConvertUsing || hasMask → Convert）标记废弃——
    /// 两运行时调用方（BuildAssignmentForProperty:434/TryBuildSingleBinding:597）恒传 false，
    /// 改造后调用方改用 Plan(PropertyInfo) 重载。此重载保留供 PlanSymbolAdapter 复用规则 3-10 形态判定。</para>
    /// </summary>
    /// <param name="sourcePropType">源属性类型</param>
    /// <param name="targetPropType">目标属性类型</param>
    /// <param name="hasConvertUsing">目标/源属性带 [Convert]（调用方已判定，规则 2）</param>
    /// <param name="hasMask">目标属性带 [Mask]（调用方已判定，规则 2）</param>
    /// <param name="isToDto">方向（保留参数：忽略判定由调用方完成，本计划器不使用）</param>
    /// <param name="globallyIgnored">全局忽略命中（调用方已判定，规则 1）</param>
    /// <param name="isFromJson">[MapProperty(IsFromJson=true)]（规则 5）</param>
    /// <param name="crossTableFromSource">跨表导航源类型（[MapProperty] FromSource≠当前源类型时非 null，规则 6）</param>
    /// <returns>赋值计划（含赋值种类与所需辅助信息）</returns>
    public static AssignmentPlan Plan(
        Type sourcePropType, Type targetPropType,
        bool hasConvertUsing, bool hasMask,
        bool isToDto, bool globallyIgnored,
        bool isFromJson, Type? crossTableFromSource)
    {
        // 1. MapIgnore/全局忽略 → Ignore（调用方已判定）
        if (globallyIgnored)
            return new AssignmentPlan(AssignmentKind.Ignore);

        // 2. ConvertUsing/Mask → Convert（优先于所有其他路径，ExpressionBuilder.cs:138）
        // ② 废弃：改造后前置判定由 Plan(PropertyInfo) 重载完成，产出独立 Kind（ConvertUsing/Mask/ConvertUsingThenMask）
        if (hasConvertUsing || hasMask)
            return new AssignmentPlan(AssignmentKind.Convert, ConvertTargetType: targetPropType);

        // 3-10. 类型形态决策（复用 PlanTypeShape）
        return PlanTypeShape(sourcePropType, targetPropType, isFromJson, crossTableFromSource, isConvertedFromConvertUsing: false);
    }

    /// <summary>
    /// 源属性类型是否满足 IFormattable 约束
    /// <para>Nullable&lt;T&gt; 解包后判定（net10 实测 typeof(DateTime?).GetInterfaces() 为空——
    /// 可空包装不透传接口）；枚举经接口判定自然通过（Enum 实现 IFormattable）。
    /// 无源属性返回 false（无源不产 Format）。</para>
    /// </summary>
    private static bool IsFormattableSource(PropertyInfo? sourceItem)
    {
        if (sourceItem == null) return false;
        var sourceType = Nullable.GetUnderlyingType(sourceItem.PropertyType) ?? sourceItem.PropertyType;
        return typeof(IFormattable).IsAssignableFrom(sourceType);
    }

    /// <summary>
    /// 嵌套 class 形态判定（与 BuildCrossTypeAssignment class 分支 + 前置一致）。
    /// </summary>
    private static bool IsNestedClassShape(Type sourceType, Type targetType)
    {
        if (!sourceType.IsClass || !targetType.IsClass) return false;
        if (sourceType == typeof(string) || targetType == typeof(string)) return false;
        if (sourceType.IsArray || targetType.IsArray) return false;
        if (sourceType.IsGenericType || targetType.IsGenericType) return false;
        return sourceType != targetType;
    }

    /// <summary>
    /// 集合形态判定（双方 IEnumerable 且非 string；同型/异型均属 Collection）。
    /// </summary>
    private static bool IsCollectionShape(Type sourceType, Type targetType)
    {
        if (sourceType == typeof(string) || targetType == typeof(string)) return false;
        return typeof(IEnumerable).IsAssignableFrom(sourceType)
               && typeof(IEnumerable).IsAssignableFrom(targetType);
    }

    /// <summary>
    /// 清除 Plan 缓存 — 经 CacheRegistry 登记（补漏）执行
    /// </summary>
    internal static void ClearCache() => s_planCache.Clear();

    /// <summary>
    /// Plan 缓存键 — (sourceItem, targetItem, isToDto, attr, crossTableFromSource) 五元组
    /// + 注册表腿对级类型分量 (RegistrySourceType, RegistryTargetType)——键含对级类型
    /// （注册表腿是本缓存的第一个对级状态输入；继承属性共享同一 PropertyInfo 实例时
    /// 五元组无法区分兄弟对）；null 上下文两分量同为 null，自成一组键与带上下文键不互串。
    /// <para>record 结构自动生成值相等（PropertyInfo/MapPropertyAttribute 引用相等；
    /// Type 引用相等；bool 值相等）。</para>
    /// </summary>
    private sealed record PlanCacheKey(
        PropertyInfo? SourceItem,
        PropertyInfo TargetItem,
        bool IsToDto,
        MapPropertyAttribute? Attr,
        Type? CrossTableFromSource,
        Type? RegistrySourceType,
        Type? RegistryTargetType);
}
