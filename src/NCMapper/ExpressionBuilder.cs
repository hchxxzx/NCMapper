using NCMapper.Runtime;
using NCMapper.Attributes;
using NCMapper.Internal;
using NCMapper.Shared;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using static System.Linq.Expressions.Expression;

namespace NCMapper;

/// <summary>
/// 静态转换方法调用模板 — 运行时侧形态标记，与源生成器 ConverterShape 同语义
/// </summary>
internal enum ConverterTemplate
{
    /// <summary>未命中</summary>
    None = 0,
    /// <summary>T0 无参常量：static TTarget M()</summary>
    T0Constant = 1,
    /// <summary>T1 单值：static TTarget M(TField)</summary>
    T1SingleValue = 2,
    /// <summary>T2 多字段组合：static TTarget M(TSource)</summary>
    T2MultiField = 3,
    /// <summary>T3 组合+字段值：static TTarget M(TSource, TField)</summary>
    T3Combined = 4,
}

/// <summary>
/// 非泛型表达式树构建器 — 从 MapperBase&lt;TSource,TTarget&gt; 提取的表达式构建逻辑
/// <para>消除 TryGetInlineMapBody 中对 MapperBase&lt;,&gt;.GetMapBody 的反射调用，
/// 改为直接调用 BuildMapBody，提升编译期表达式树构建性能</para>
/// <para><b>P1-SHOULD-1 架构说明 — 循环依赖状态（W2-1 ④ 策略化深化后）</b></para>
/// <para>3 处反射回指已由委托工厂消除：record 构造器参数匹配
/// （TryBuildNewExpr）、嵌套对象 fallback（GetClassExpression）、集合元素递归映射
/// （GetListExpression）三条路径改为按实际类型对经 <see cref="NCMapper.Internal.MapperDelegateRegistry"/>
/// 解析 <see cref="NCMapper.Internal.IMapperBaseDelegate"/> 委托——MapperBase&lt;TSource,TTarget&gt;
/// 显式静态构造函数注册 + RunClassConstructor 兜底，无 <c>typeof(MapperBase&lt;,&gt;)</c> 反射。</para>
/// <para>编译期引用环因 C# 泛型 + static class 语言限制有意保留（ADR 登记项）：GetMapBody 转发
/// （MapperBase.ExpressionBuilder.cs:53）与 Trace 辅助调用链（TryWriteDirectionAmbiguityTrace 等）。</para>
/// <para><b>历史记录（反射回指时期， 前）</b>：本类型为 static class 无法持有泛型类型参数，
/// MapperBase 部分逻辑（BuildConstructorArgs / Map / MapList）依赖具体泛型实例的静态字段，
/// 三条路径曾经 <c>typeof(MapperBase&lt;,&gt;).MakeGenericType(...)</c> 反射调用，反射查找经
/// <see cref="ReflectionHelper"/> 的 ConcurrentDictionary 缓存（同一类型对仅反射一次）。</para>
/// </summary>
internal static partial class ExpressionBuilder
{
    // CacheRegistry 登记制自注册：仅登记清理委托，不触发缓存构建（防自触发）
    static ExpressionBuilder()
    {
        NCMapper.Internal.CacheRegistry.Register("expression-builder", CacheLayer.Full, ClearCache);
    }

    /// <summary>
    /// 特性类型 — 供表达式构建内部直接使用
    /// </summary>
    private static readonly Type s_mapFromAttribute = typeof(MapFromAttribute);

    /// <summary>
    /// 表达式树构建时的当前内联深度计数器 — 每进入一层嵌套对象生成 +1
    /// [ThreadStatic] 防止不同泛型实例化的静态构造函数并发执行时数据竞争
    /// </summary>
    [ThreadStatic]
    private static int s_inlineDepth;

    /// <summary>
    /// F-09 方向判定受限提示集合 — 记录已输出过 Trace 提示的类型对
    /// <para>T6-类型对双侧均未标 [MapFrom] 时方向不可推断，
    /// 同类型对仅输出一次引导提示，避免每次映射构建刷屏</para>
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(Type Source, Type Target), byte> s_directionAmbiguousNotified = new();

    /// <summary>
    /// 扁平化路径解析缓存 — (源类型, 目标属性) → 匹配路径；null 值 = 失败缓存
    /// <para>继承 PropertyMatcher 动态类型不缓存判据：Reflection.Emit/可收集程序集走直算；</para>
    /// <para>键改为 (Type, PropertyInfo)：PropertyInfo 自带声明类型身份，消除同名属性跨类型污染；
    /// 失败缓存语义限定：null 仅保留给推导扁平化——显式点路径失败抛 MapConfigurationException，不进失败缓存（G4 可观测性）。</para>
    /// <para>键无需含全局配置：扁平化段匹配固定 OrdinalIgnoreCase，配置无关（兼容性审计 豁免形态）。</para>
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(Type Source, PropertyInfo TargetItem), PropertyInfo[]?> s_flattenPathCache = new();

    /// <summary>
    /// 反向写路径解析缓存 — (目标类型, 源属性) → 写路径；null 值 = 跳过缓存
    /// <para>键设计对齐 s_flattenPathCache：(Type, PropertyInfo) 自带声明类型身份 + 动态类型不缓存判据；
    /// 仅缓存路径解析成功结果与“中间节点不可实例化跳过”（软失败）；
    /// 抛 MapConfigurationException 的解析失败（段缺失/类型非法）不进缓存（与正向显式路径一致，G4 可观测性）。</para>
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(Type Target, PropertyInfo SourceProp), PropertyInfo[]?> s_reverseWritePathCache = new();

    /// <summary>
    /// 路径语义归一解析器— 段循环统一收敛为 TypePathResolver，
    /// 正向/反向判据 + 失败分类单点承载（①②④⑤ 共用实例，无状态安全）
    /// </summary>
    private static readonly TypePathResolver s_typeResolver = new();

    /// <summary>
    /// 构建属性赋值表达式列表 — 核心映射逻辑，GetMapAction、CreateMapListFunc、GetClassExpression 共用
    /// 将所有 target.Prop = source.Prop（含类型转换/嵌套/集合/JSON/可空等）的表达式收集到列表中
    /// </summary>
    /// <param name="sourceType">源对象类型</param>
    /// <param name="targetType">目标对象类型</param>
    /// <param name="sourceParameter">源对象参数表达式</param>
    /// <param name="targetParameter">目标对象参数表达式</param>
    /// <returns>赋值表达式列表</returns>
    [RequiresDynamicCode("BuildMapBody uses Expression.Compile()/MakeGenericType/MakeGenericMethod and reflection in the runtime fallback path. Not supported in AOT. Use the source generator to ensure AOT compatibility.")]
    [RequiresUnreferencedCode("BuildMapBody scans type members and attributes via reflection. Some members may be trimmed. Use the source generator to ensure AOT compatibility.")]
    internal static List<Expression> BuildMapBody(Type sourceType, Type targetType,
        ParameterExpression sourceParameter, ParameterExpression targetParameter)
    {
        //创建一个表达式集合
        var expressions = new List<Expression>();

        //获取所有可用的转换字段列表
        var key = PropertyMatcher.GetPropertyList(sourceType, targetType);
        var sourceTypes = key.Key;
        var targetTypes = key.Value;

        //获取源、目标对象是否有类特性标注
        var sourceIsDefine = sourceType.IsDefined(s_mapFromAttribute);
        var targetIsDefine = targetType.IsDefined(s_mapFromAttribute);

        // 四必参 + 逐层推导要素打包为不可变构建会话（BuildSession）
        // 策略类统一入参载体（无 MapperDelegate/InlineDepth：委托按实际类型对在 3 处反射回指点
        // 独立解析；InlineDepth 维持 [ThreadStatic] 实时读）
        var session = new BuildSession
        {
            SourceType = sourceType,
            TargetType = targetType,
            SourceParameter = sourceParameter,
            TargetParameter = targetParameter,
            SourceProperties = sourceTypes,
            TargetProperties = targetTypes,
            SourceIsDefine = sourceIsDefine,
            TargetIsDefine = targetIsDefine,
            PathResolver = s_typeResolver,
        };

        // 注册表腿上下文：按对解析一次（本方法域内已持对级类型；构建期 Trace 唯一发射位），
        // 决策 Plan 与扁平化下传共用同一份实例
        var registryCtx = RegistryMaskContext.Resolve(sourceType, targetType);

        // 反向无源 [ConvertBack] 暂挂链出清——无锚场景的
        // [ConvertBack] 消费属"无锚不消费"违规现状（出清段），原
        // 暂挂列表/防御 Trace/发射段/EmitPendingBackConverters/BuildBackConverterAssignmentForNullSource
        // 全链删除（载体随退）。

        // 循环不变量上移——sourceTypes 为循环外
        // 常量（BuildSession.SourceProperties， P6），SplitAttrsByAnchor 按需循环外计算一次，
        // 循环内复用（消除 O(m)×O(n) 逐目标属性重算 + 逐属性锚门放大）；分支不可达时 null =
        // GetSourceItemByTargetItem 内部现状自算，行为与改造前一致。
        (List<PropertyInfo> WithAttr, List<PropertyInfo> WithoutAttr)? split = (sourceIsDefine && !targetIsDefine) ? PropertyMatcher.SplitAttrsByAnchor(sourceTypes) : null;

        // 循环不变量上移——白名单判定对
        // (S,T) 恒常，紧邻既有 SplitAttrsByAnchor 上移位（:148，M-15 先例）算一次、循环内复用
        //（BuildMapBody 为反射映射热路径，触及全部反射映射调用方；未标注对两腿均 false → 零行为变化，
        // 量化对照见 SelfBench）。
        var noAutoMatch = RuntimeSnapshotStore.IsNoAutoMatch(sourceType, targetType);

        foreach (var targetItem in targetTypes)
        {
            // （②/ E1–E4）：白名单消费门——置于取源
            //（GetSourceItemByTargetItem）**之前**：先判门后取源，按原文语义"先取到自动匹配结果再判门"
            // 门形同虚设（风险③）。门覆盖 E1 同名 / E2 配置感知重匹配 / E3 命名扁平化 / E4 反向同名
            //（PropertyMatcher 两级查找）与对侧标注两出口（:359-376 /，双保险）；
            // 显式声明（同身 [MapProperty]，含跨表 FromSource≠S；反向图另含承载侧 DTO 属性经第二级
            // 翻转消费的 [MapProperty]）照常走显式出口 + 声明叠加变换（[Convert]/[Mask]/[MapWhen]）。
            if (noAutoMatch && !HasExplicitDeclarationForPair(targetItem, sourceTypes, sourceIsDefine, targetIsDefine, split))
            {
                // D1 可观测性：生成器缺席时 NCMAP047 不发射，
                // 白名单下"带变换特性无 [MapProperty]"会静默退化为跳过——反射腿输出 MapTracer 提示
                //（沿 :248/:260 既有 Trace 惯例，不抛异常，使契约在无生成器时仍可观测）
                if (HasWhitelistTransformAttribute(targetItem) && MapTracer.GloballyEnabled)
                {
                    MapTracer.Write($"[NCMapper] ExpressionBuilder: 跳过属性 {targetItem.Name}（原因：[MapNoAutoMatch] 白名单模式下带映射变换特性但未标注 [MapProperty]，不参与映射——补 [MapProperty] 声明或移除变换特性）");
                }
                continue;
            }

            //获取对应的源字段
            var sourceItem = PropertyMatcher.GetSourceItemByTargetItem(sourceType, targetItem, sourceTypes, sourceIsDefine, targetIsDefine, out MapPropertyAttribute? attr, split);

            //判断实体的读写权限
            if (sourceItem == null || !sourceItem.CanRead || sourceItem.PropertyType.IsNotPublic || targetItem.CanWrite == false)
            {
                // 同源显式点路径（H6 提前）：[MapProperty(typeof(Src), "A.B")] 且 FromSource == sourceType
                // 优先于 [Convert] 无源属性分支——路径场景的 [Convert] 由 BuildFlattenedAssignmentFromPath
                // 消费（转换器输入 = 路径值）；无源属性分支仅处理真·无源属性（转换器输入 default(TField)）。
                // 插推导扁平化之前：显式 = 契约，段解析失败抛 MapConfigurationException；
                // 失败不进失败缓存（G4，可观测性不随缓存命中消失）；IsFromJson 优先（C9，JSON 分支先走，此处排除）
                // （F3 收口）：仅规范正向消费 targetItem 侧标注
                // targetIsDefine（目标标 [MapFrom]）锁定“DTO 属性上的读路径标注”；反向（ToEntity）时
                // 实体侧路径标注不消费（反向写回由 BuildReversePathWriteBacks 消费 DTO 侧标注）
                var dotPathAttr = PropertyIgnoreChecker.GetMapPropertyAttribute(targetItem); // 缓存族收敛
                // 触发条件含 '['（单段集合索引 "B[0]" 无 '.'）
                if (targetIsDefine && dotPathAttr != null && dotPathAttr.FromSource == sourceType
                    && !string.IsNullOrEmpty(dotPathAttr.FromField) && (dotPathAttr.FromField.Contains('.') || dotPathAttr.FromField.Contains('['))
                    && !dotPathAttr.IsFromJson)
                {
                    var dotPathExpr = BuildExplicitDotPathAssignment(sourceParameter, targetParameter, sourceType, targetItem, dotPathAttr, registryCtx);
                    if (dotPathExpr != null)
                    {
                        expressions.Add(dotPathExpr);
                        continue;
                    }
                    // 全局忽略/类型不兼容 → 保持跳过（Trace 已由 BuildFlattenedAssignmentFromPath 输出，C4 叶节点语义）
                    continue;
                }

                // 命名约定扁平化（H6 提前）：同名/配置感知匹配失败且无显式 [MapProperty] 时，
                // 按 PascalCase 拆解目标属性名匹配源嵌套路径（AddressCity → Address.City）
                // 方向守卫：仅正向（目标标 [MapFrom]，isToDto 显式推导）；对齐源生成器 TryFindFlattenedPath
                // 尽力而为语义：匹配失败 → 回退下方 [Convert] 无源属性分支（转换器 default）或跨表/跳过
                if (targetIsDefine && attr == null)
                {
                    var flatExpr = BuildFlattenedAssignment(sourceParameter, targetParameter, sourceType, targetItem, registryCtx);
                    if (flatExpr != null)
                    {
                        expressions.Add(flatExpr);
                        continue;
                    }
                }

                // [Convert] 无匹配源属性（真·无源属性或扁平化匹配失败）— 转换器仍被调用，fieldValue 传 default(TField)
                // 仅正向（isToDto）消费；反向默认不消费 [Convert]
                // （转换器泛型绑定正向方向），下方落入跳过分支（与生成端 NCMAP033 同语义）
                // 排除跨表属性（attr.FromSource != sourceType）——跨表 [Convert]
                // 的 TSource 基准=子源（非主源），此处按主源 TSource 校验会误报泛型不匹配；
                // 跨表属性交下方跨表分支（BuildCrossTableAssignment）消费，source=子源导航。
                if (sourceItem == null && targetItem.CanWrite && targetIsDefine
                    && !(attr != null && attr.FromSource != sourceType))
                {
                    var converterAttrForNull = targetItem.GetCustomAttribute<ConvertAttribute>();
                    if (converterAttrForNull != null)
                    {
                        var targetPropForNull = Property(targetParameter, targetItem);
                        var converterExpr = BuildConverterAssignmentForNullSource(targetItem, converterAttrForNull, targetPropForNull, sourceParameter);
                        expressions.Add(converterExpr ?? throw new MapException(ErrorMessages.ConverterExpressionNull(targetItem.Name, ErrorMessages.NoSourceScenario)));
                        continue;
                    }
                }

                // 反向无源消费分支——[ConvertBack] 标注在实体属性上、
                // DTO 无对应源属性（一对多拆分 / 无源计算属性等形态）时转换器仍被调用（T2/T3 模板的
                // TSource 实参 = 整个 DTO 对象）。与上方正向 [Convert] 无源分支同构，方向门互补：
                // !targetIsDefine（= !isToDto，含双侧无 [MapFrom] 的方向缺省场景）。
                // 生成端镜像：ReversePlanner/CreateReverseMapping（ReverseOnlyConvertBacks 追加）。
                // 门不带正向 的跨表排除（attr.FromSource != sourceType）——执行期实读实证：
                // 反向构建走 GetSourceItemByTargetItem 的 sourceIsDefine 分支，attr 仅经 withAttr 循环
                //（:312 逐次赋值不回置）残留为「DTO 侧无关属性的 [MapProperty]」，实体属性自身的跨表
                // 标注（FromSource≠DTO）在 同源守卫处即已旁路、从不流入 attr——逐字镜像该门
                // 会因脏 attr 在「DTO 带任意 [MapProperty] 标注」形态下静默禁用本分支，且与生成端
                // 排除集（仅覆盖集 + setter 门，无跨表门）分叉，违反硬性规则 #7（双引擎成对改造）。
                // 偏差处置登记于 （执行记录）。
                // 反向无源 [ConvertBack] 兜底腿出清（v1.3 改判——无锚
                // 场景的 [ConvertBack] 暂挂消费属"无锚不消费"违规现状， 出清段；
                // sourceItem == null 且 !targetIsDefine = 目标无锚 → 原块整体删除）

                    // 跨表导航解析：[MapProperty(typeof(X), "Field")] 且 X != 当前源类型
                    // 方向守卫：仅当 attr 声明在 targetItem 上时才走跨表（正向）
                    if (attr != null && attr.FromSource != sourceType
                        && targetItem.IsDefined(typeof(MapPropertyAttribute)))
                    {
                        // BuildCrossTableAssignment 已迁入 CrossTableStrategy（方法体逐行不动），此处限定调用
                        var crossTableExpr = CrossTableStrategy.BuildCrossTableAssignment(sourceType, targetItem, attr, sourceParameter, targetParameter);
                        if (crossTableExpr != null)
                        {
                            expressions.Add(crossTableExpr);
                            continue;
                        }
                        // 导航/字段未找到 → Trace + 跳过
                        if (MapTracer.GloballyEnabled) MapTracer.Write($"[NCMapper] ExpressionBuilder: 跳过属性 {targetItem.Name}（原因：跨表导航解析失败，FromSource={attr.FromSource.Name}，FromField={attr.FromField}）");
                        continue;
                    }

                if (MapTracer.GloballyEnabled) MapTracer.Write($"[NCMapper] ExpressionBuilder: 跳过属性 {targetItem.Name}（原因：源属性 {(sourceItem == null ? "未找到" : $"{sourceItem.Name} 不可读/类型非 public 或目标属性不可写")}）");
                continue;
            }

            // sourceItem 非空不变式——外层 if（:95）块内全路径以 continue 终止
            //（点路径 :115/:118、扁平化、无源属性 ConvertUsing、跨表 :163/:167、兜底），
            // 此处必非空。显式断言使后续可空状态收敛（最小复现证实：仅当守卫后仍出现
            // sourceItem != null 布尔表达式时，Roslyn 空状态合并会重新引入假想 null 路径并降级为
            // MaybeNull，导致 CS8604；守卫本身足以收敛）
            //（防御性分支：运行时不可达，零行为变更）
            if (sourceItem == null)
            {
                if (MapTracer.GloballyEnabled) MapTracer.Write($"[NCMapper] ExpressionBuilder: 跳过属性 {targetItem.Name}（原因：源属性未找到——防御性分支，理论上不可达）");
                continue;
            }

            // ② 枢纽深化：前置判定（Ignore/ConvertUsing/Mask/ConvertBackUsing）收进 Plan。
            // 现状 手写 if-else 链移除，替换为 Plan(PropertyInfo) 调用 + Trace 输出 + RenderPlan 渲染。
            // 方向推导保留（isToDto = targetIsDefine，与 Plan(PropertyInfo) 内部步骤 2/3 方向分支一致）
            var isToDto = targetIsDefine;

            //F-09 方向判定受限 Trace 引导：类型对双侧均未标 [MapFrom] 时，运行时无法从
            //类型信息推断映射方向（象限④客观限制，见 备案），带方向性（非 Both）的
            //[MapIgnore] 生效方向不可预期 —— 输出一次性提示，建议为目标类型标注 [MapFrom]
            TryWriteDirectionAmbiguityTrace(sourceType, targetType, sourceItem, targetItem);

            // X3-A 方向判反 Trace：[MapFrom] 标在源侧（非规范位置）时
            // 方向判定与直觉相反，带方向性 [MapIgnore] 生效方向反转——输出 Trace 使已知限制可观测
            TryWriteDirectionReversalTrace(sourceType, targetType, sourceItem, targetItem);

            // 跨表导航源类型计算（原 RenderPlan 片段移至此处，Plan(PropertyInfo) 需要）
            Type? crossTableFromSource = null;
            if (attr != null && attr.FromSource != sourceType
                && targetItem.IsDefined(typeof(MapPropertyAttribute)))
                crossTableFromSource = attr.FromSource;

            // 赋值决策收进 Plan(PropertyInfo)（前置判定 + 类型形态决策单点化）
            var plan = AssignmentPlanner.Plan(sourceItem, targetItem, isToDto, attr, crossTableFromSource, registryCtx);

            // G9 Trace 可观测性保留（四处 MapTracer.Write 内容与触发时机不变）
            // Trace 在 BuildMapBody 输出（PlanCore 保持纯函数不写 Trace——不抛异常，决策始终产出 Kind）
            if (plan.Kind == AssignmentKind.Ignore)
            {
                // :212 Ignore 跳过 Trace
                if (MapTracer.GloballyEnabled) MapTracer.Write($"[NCMapper] ExpressionBuilder: 跳过属性 {targetItem.Name}（原因：源或目标属性标注了 [MapIgnore] 或命中全局忽略清单）");
                continue;
            }

            // :265 反向 ConvertUsing 降级 Trace
            if (plan.Kind == AssignmentKind.Convert && plan.IsConvertedFromConvertUsing)
            {
                if (MapTracer.GloballyEnabled) MapTracer.Write($"[NCMapper] ExpressionBuilder: 反向映射 {sourceType.Name} → {targetType.Name} 属性 {targetItem.Name} 的 [Convert] 按类型转换路径写入（生成端 NCMAP033 同语义）");
            }

            // :300 Mask 非 string Trace
            // Plan 产 Mask/ConvertUsingThenMask 时 targetItem.PropertyType == string（PlanCore 判断）；
            // 非 string 且有 Mask 特性时，PlanCore 不产 Mask（落类型形态决策）
            // → BuildMapBody 需查特性（缓存）判断 Trace 条件
            // ：Format/FormatThenMask 加入
            // 负判定白名单——避免多余的特性/类型查询（Format 族目标恒 string，下方 Mask 非 string
            // Trace 条件恒假）+ 与 Kind 集合一致性
            if (plan.Kind != AssignmentKind.Mask
                && plan.Kind != AssignmentKind.ConvertUsingThenMask
                && plan.Kind != AssignmentKind.Format
                && plan.Kind != AssignmentKind.FormatThenMask
                && plan.Kind != AssignmentKind.Ignore)
            {
                var maskAttrForTrace = PropertyIgnoreChecker.GetMaskAttribute(targetItem);
                var hasRegistryMaskForTrace = registryCtx != null && registryCtx.HasRegistryMask(targetItem.Name);
                // 注册表腿非 string 守卫（与特性腿同口径）：两腿互斥（特性命中即不查注册表），
                // 分支必须显式成对——若内层守卫不加大括号，else 会悬挂到内层 if 上，
                // 注册表腿（特性恒未命中形态）的 Trace 将不可达（F4 静默面）
                if (maskAttrForTrace != null && targetItem.PropertyType != typeof(string))
                {
                    if (MapTracer.GloballyEnabled) // Trace 关闭态免插值分配
                        MapTracer.Write(
                        $"Mask 特性不支持非 string 类型，'{targetType.Name}.{targetItem.Name}' 类型为 {targetItem.PropertyType.Name}，将按常规映射赋值");
                }
                else if (maskAttrForTrace == null && hasRegistryMaskForTrace && targetItem.PropertyType != typeof(string))
                {
                    if (MapTracer.GloballyEnabled)
                        MapTracer.Write(
                        $"Mask 注册表明细不支持非 string 类型，'{targetType.Name}.{targetItem.Name}' 类型为 {targetItem.PropertyType.Name}，将按常规映射赋值");
                }
            }

            // Format + Convert 同标 Trace（"运行时同标防御"）：
            // 编译期 NCMAP045 已拦（Error），运行时属理论路径（纯反射场景无编译期拦截）；为免静默，
            // Plan 产 Format 族且目标属性同标 [Convert] 时输出 Trace 提示"格式化优先、转换器被忽略"
            if ((plan.Kind == AssignmentKind.Format || plan.Kind == AssignmentKind.FormatThenMask)
                && PropertyIgnoreChecker.GetConvertAttribute(targetItem) != null)
            {
                if (MapTracer.GloballyEnabled)
                    MapTracer.Write(
                        $"[MapFormat] 与 [Convert] 同时标注于 '{targetType.Name}.{targetItem.Name}'，格式化优先、自定义转换器被忽略（编译期 NCMAP045 已拦截此组合，运行时理论路径）");
            }

            // 渲染（只渲染计划不决策）— RenderPlan switch 由策略查表分发替代
            // ；Ignore Kind 已于上方
            // Trace 守卫区提前 continue，Skip Kind 由 SkipStrategy 防御壳返回 null 跳过
            var strategy = AssignmentStrategyResolver.Resolve(plan);
            var assignment = strategy?.BuildExpression(in session, sourceItem, targetItem, attr, plan);
            if (assignment != null)
            {
                // 运行时回退守卫包裹（S8 双路径一致）——生成端
                // EmitGuardedSinglePropertyAssignment 镜像（#31.6）：IfThen(谓词, 赋值)——
                // 条件 → 转换 → 赋值（谓词在 strategy 转换前求值——IfThen test 位先于 body）；
                // 条件假跳过赋值（更新保留 dst 既有值 / 新建保持默认初始化）。
                // Batch-2 CI 补修：sourceItem 可空（无源映射场景）——T1 字段值
                // 实参无从构建时 BuildWhenGuardCondition 返回 null（不包裹，与生成端 F43
                // "声明被拒、映射照常"语义镜像）；T2 整源实参恒 src 不受影响
                var whenDecl = PropertyMatcher.TryGetWhenDeclaration(sourceType, targetType, targetItem);
                if (whenDecl != null)
                {
                    var guard = BuildWhenGuardCondition(sourceParameter,
                        sourceItem != null ? Property(sourceParameter, sourceItem) : null,
                        sourceType, targetItem, whenDecl);
                    if (guard != null)
                        assignment = IfThen(guard, assignment);
                }
                expressions.Add(assignment);
            }
        }

        // 反向路径写回（DTO 扁平属性 → 实体嵌套路径，折叠）
        // 源驱动遍历 DTO 属性带路径标注者；与主循环（目标属性遍历）互补，共同构成完整反向语义
        // coveredSegments 恒 null（原暂挂链出清——无覆盖排除消费方；
        // BuildReversePathWriteBacks 的 coveredSegments 参数保留签名兼容，null = 无收集）
        BuildReversePathWriteBacks(sourceParameter, targetParameter, sourceType, targetType, sourceTypes, expressions, coveredSegments: null, noAutoMatch: noAutoMatch);

        return expressions;
    }

    /// <summary>
    /// 白名单"显式声明"判定（①②——调用侧门的输入）：
    /// <para>①目标属性同身 <c>[MapProperty]</c>（正向单边分支显式出口 <c>PropertyMatcher:348-351</c>
    /// 与跨表分支 <c>:233</c> 准入，含 FromSource≠S 的跨表声明——白名单内照常）；</para>
    /// <para>②反向图（<c>sourceIsDefine &amp;&amp; !targetIsDefine</c>）承载侧（DTO 属性）的
    /// <c>[MapProperty]</c>：经 <c>PropertyMatcher:331-340</c> 两级查找第二级
    ///（<c>pa.FromField == 目标属性名</c>）翻转消费——正向显式声明在反向的镜像即"反向仅翻转显式"。</para>
    /// <para>门必须在调用侧：单边分支的显式读入与同名匹配融合在一次 <c>ResolveByName</c>（:351），
    /// 改其内部即破坏同名匹配路径。
    /// 仅在白名单对（<c>noAutoMatch == true</c>）上被调用，未标注对零开销。</para>
    /// </summary>
    internal static bool HasExplicitDeclarationForPair(PropertyInfo targetItem, List<PropertyInfo> sourceTypes,
        bool sourceIsDefine, bool targetIsDefine,
        (List<PropertyInfo> WithAttr, List<PropertyInfo> WithoutAttr)? split)
    {
        // 缓存族：与 PropertyMatcher 显式出口同一读取点（同身 [MapProperty]）
        if (PropertyIgnoreChecker.GetMapPropertyAttribute(targetItem) != null) return true;

        if (sourceIsDefine && !targetIsDefine)
        {
            var withAttr = split?.WithAttr ?? PropertyMatcher.SplitAttrsByAnchor(sourceTypes).WithAttr;
            for (var i = 0; i < withAttr.Count; i++)
            {
                var pa = PropertyIgnoreChecker.GetMapPropertyAttribute(withAttr[i]);
                if (pa != null && string.Equals(pa.FromField, targetItem.Name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// D1 变换特性族判定（纯反射腿可观测性）——目标属性带
    /// [Convert]/[Mask]/[MapFormat]/[MapWhen] 之一（与生成端 NCMAP047 判定族同集合）。
    /// 仅在白名单跳过分支上调用，未标注对零开销。
    /// </summary>
    private static bool HasWhitelistTransformAttribute(PropertyInfo targetItem)
        => targetItem.IsDefined(typeof(ConvertAttribute), inherit: true)
           || targetItem.IsDefined(typeof(MaskAttribute), inherit: true)
           || targetItem.IsDefined(typeof(MapFormatAttribute), inherit: true)
           || targetItem.IsDefined(typeof(MapWhenAttribute), inherit: true);

    /// <summary>
    /// 判断属性是否标注了带方向性（非 Both）的 [MapIgnore] — T6-S2 辅助
    /// <para>方向性 [MapIgnore]（ToDto/ToEntity）的生效方向依赖 isToDto 判定；
    /// 双侧均未标 [MapFrom] 时方向不可推断，需 Trace 引导。Both 恒忽略无方向依赖，不参与提示。</para>
    /// </summary>
    /// <param name="prop">属性信息</param>
    /// <returns>标注了非 Both 方向的 [MapIgnore] 返回 true</returns>
    private static bool HasDirectionalMapIgnore(PropertyInfo prop)
    {
        // T5 本方法为 F09 Trace 引导面（建议标注 [MapFrom]）
        // 恰以无锚场景为服务对象，属诊断引导面（B10 不门化），保留无门读取
        var attr = prop.GetCustomAttribute<MapIgnoreAttribute>();
        return attr != null && attr.Direction != Direction.Both;
    }

    /// <summary>
    /// F-09 方向判定受限 Trace 引导（T6-S2 共享入口）— 供 BuildMapBody（复杂路径）与
    /// MapperBase.TryBuildMemberInit（简单路径）共同调用，保证象限④提示不因构建路径分叉而遗漏。
    /// <para>条件：类型对双侧均未标 [MapFrom] 且存在带方向性（非 Both）[MapIgnore] 属性；
    /// 同类型对仅输出一次（s_directionAmbiguousNotified 去重）。</para>
    /// </summary>
    /// <param name="sourceType">源类型</param>
    /// <param name="targetType">目标类型</param>
    /// <param name="sourceItem">源属性（可为 null——若属性枚举阶段已剔除则不提示）</param>
    /// <param name="targetItem">目标属性</param>
    internal static void TryWriteDirectionAmbiguityTrace(Type sourceType, Type targetType, PropertyInfo? sourceItem, PropertyInfo targetItem)
    {
        var sourceIsDefine = sourceType.IsDefined(s_mapFromAttribute, false);
        var targetIsDefine = targetType.IsDefined(s_mapFromAttribute, false);
        if (!sourceIsDefine && !targetIsDefine
            && ((sourceItem != null && HasDirectionalMapIgnore(sourceItem)) || HasDirectionalMapIgnore(targetItem))
            && s_directionAmbiguousNotified.TryAdd((sourceType, targetType), 0))
        {
            if (MapTracer.GloballyEnabled) MapTracer.Write($"[NCMapper] ExpressionBuilder: 类型对 {sourceType.Name} → {targetType.Name} 双侧均未标注 [MapFrom]，映射方向不可推断，带方向性的 [MapIgnore] 可能无法按预期生效。建议为目标类型 {targetType.Name} 标注 [MapFrom(typeof({sourceType.Name}))] 以消除方向歧义（详见 CHANGELOG 迁移指引）");
        }
    }

    /// <summary>
    /// X3-A 方向判反 Trace 引导— 非规范场景已知限制可观测化
    /// <para>触发条件：源类型标 [MapFrom] 指向目标类型（[MapFrom] 标在源侧 = 非规范位置），
    /// 此时 isToDto 判定（目标带 [MapFrom] 才为 ToDto）与直觉相反——带方向性（非 Both）[MapIgnore]
    /// 的生效方向反转。属"有方向性标注"场景，不适用 hch 原则 1/2（显式声明方向的标注行为理应依赖方向判定）。
    /// 组件如实执行判反行为（用户标注矛盾的后果），但输出 Trace 使已知限制可观测（P2-2 可观测性）。</para>
    /// <para>与 F-09（TryWriteDirectionAmbiguityTrace）的区别：F-09 覆盖双侧均未标 [MapFrom]（象限④）；
    /// 本方法覆盖源标 [MapFrom] 目标未标（非规范位置）的方向判反场景。同一类型对仅输出一次（复用
    /// s_directionAmbiguousNotified 去重集）。</para>
    /// </summary>
    /// <param name="sourceType">源类型</param>
    /// <param name="targetType">目标类型</param>
    /// <param name="sourceItem">源属性（可为 null——若属性枚举阶段已剔除则不提示）</param>
    /// <param name="targetItem">目标属性</param>
    internal static void TryWriteDirectionReversalTrace(Type sourceType, Type targetType, PropertyInfo? sourceItem, PropertyInfo targetItem)
    {
        var sourceIsDefine = sourceType.IsDefined(s_mapFromAttribute, false);
        var targetIsDefine = targetType.IsDefined(s_mapFromAttribute, false);
        if (sourceIsDefine && !targetIsDefine
            && sourceType.GetCustomAttribute<MapFromAttribute>()?.FromSource == targetType
            && ((sourceItem != null && HasDirectionalMapIgnore(sourceItem)) || HasDirectionalMapIgnore(targetItem))
            && s_directionAmbiguousNotified.TryAdd((sourceType, targetType), 0))
        {
            if (MapTracer.GloballyEnabled) MapTracer.Write($"[NCMapper] ExpressionBuilder: 类型对 {sourceType.Name} → {targetType.Name} 的 [MapFrom] 标在源侧（非规范位置），映射方向判定与直觉相反，带方向性的 [MapIgnore] 生效方向将反转。建议将 [MapFrom] 移至目标类型 {targetType.Name}（规范位置）以消除方向反转（详见 CHANGELOG 迁移指引）");
        }
    }

    /// <summary>
    /// 清空方向歧义通知去重缓存（s_directionAmbiguousNotified）
    /// 接入 Mapper.ClearCache 链（先例模式）。
    /// <para>清空后同一类型对若再次触发方向歧义场景将重新输出一次 Trace 引导提示
    /// （行为可重复，无状态副作用）；去重集为 (Type,Type) 键空间，动态类型/泛型爆炸场景
    /// 存在无界增长风险，接入 Clear 链后由 Mapper.ClearCache() 统一收敛。</para>
    /// </summary>
    internal static void ClearCache()
    {
        s_directionAmbiguousNotified.Clear();
        s_flattenPathCache.Clear();
        s_reverseWritePathCache.Clear();
    }

    /// <summary>
    /// 扁平化路径缓存条目数（internal 测试钩子）— 用于断言
    /// "动态类型不缓存"与 ClearCache 清理链
    /// </summary>
    internal static int FlattenPathCacheEntryCount => s_flattenPathCache.Count;

    /// <summary>
    /// 方向歧义通知去重缓存条目数（internal 测试钩子 — s_directionAmbiguousNotified 为
    /// private 字段，InternalsVisibleTo 仅放行 internal 成员，验收测试经此钩子断言
    /// ClearCache 后缓存被清空，遵循 DeepCopyTypeClassifier.CacheEntryCount 先例）
    /// </summary>
    internal static int CacheEntryCount => s_directionAmbiguousNotified.Count;

    // RenderPlan（原 按 Kind switch 的渲染方法）已整体删除
    // 主循环改策略查表分发（AssignmentStrategyResolver.Resolve(plan)
    //   → strategy.BuildExpression(in session, ...)），不留 stub、不设反射回退；
    //   12 分支渲染逻辑已由 12 策略类承载（试点 3 + 批量 9）

    // BuildNullableConditionalAssignment / BuildTypeConversionAssignment /
    // BuildEnumToStringSwitch 三方法已整体迁入 ConvertStrategy（方法体逐行不动，
    // private → internal 供 RenderPlan 过渡期限定调用；RenderPlan 调用点已同步限定）

    // BuildNestedAssignment 已整体迁入 NestedStrategy（方法体逐行不动；
    // RenderPlan 调用点已同步限定）

    /// <summary>
    /// 构建同源显式点路径赋值 — [MapProperty(typeof(Src), "A.B")] 段解析 + 复用扁平化赋值构造；
    /// 支持集合索引段 "B[0]"，索引元数据与路径并行传递）
    /// <para>段解析失败（段缺失/中间节点值类型或 string/非集合段索引/非法索引语法）抛 <see cref="MapConfigurationException"/>（C6 显式=契约；
    /// 与投影嵌套 Mask/Convert 抛异常先例一致）；成功复用 <see cref="BuildFlattenedAssignmentFromPath"/>
    /// （null 安全链/类型转换/全局忽略叶节点判定/Mask 与 Convert 消费）。显式路径解析不进 s_flattenPathCache（失败缓存仅限推导）。</para>
    /// </summary>
    /// <returns>赋值表达式；叶节点全局忽略或类型不兼容时返回 null（调用方跳过，Trace 已输出）</returns>
    [RequiresUnreferencedCode("BuildExplicitDotPathAssignment scans type members and attributes via reflection. Some members may be trimmed.")]
    [RequiresDynamicCode("BuildExplicitDotPathAssignment builds expressions via MakeGenericMethod/MakeGenericType. Not supported in AOT.")]
    private static Expression? BuildExplicitDotPathAssignment(
        ParameterExpression sourceParameter, ParameterExpression targetParameter,
        Type sourceType, PropertyInfo targetItem, MapPropertyAttribute attr,
        RegistryMaskContext? registryCtx)
    {
        var segments = attr.FromField.Split('.');

        // 段循环收敛为 TypePathResolver（正向契约式）
        // PathSegmentFailure → DotPathFailureKind 映射后抛 MapConfigurationException（C6 显式=契约；
        // 失败分类逐类映射见 前 5 行）
        var result = s_typeResolver.Resolve(sourceType, segments, PathResolutionOptions.ForwardContract, context: null);
        if (!result.Success)
        {
            var kind = MapFailureKind(result.Failure);
            var failedSegment = result.FailedSegment ?? segments[result.FailedSegmentIndex];
            // 失败段所在类型名：前缀路径末段属性类型（FailedSegmentIndex==0 时为根类型）
            var failedTypeName = result.FailedSegmentIndex == 0
                ? sourceType.Name
                : result.Path[result.FailedSegmentIndex - 1].PropertyType.Name;
            throw new MapConfigurationException(
                ErrorMessages.DotPathSegmentFailed(attr.FromField, failedSegment, kind, targetItem.Name, failedTypeName));
        }

        return BuildFlattenedAssignmentFromPath(result.Path.ToArray(), result.SourceIndexes.ToArray(), sourceParameter, targetParameter, targetItem, registryCtx);
    }

    // IsInitOnlySetter 私有副本已删除——W2-2 重构后本类零调用点
    // （src/tests 全量 grep 实证），判据单一事实源收敛于 TypePathResolver.IsInitOnlySetter

    /// <summary>
    /// ElementAtOrDefault 开放泛型方法定义缓存
    /// <para>静态缓存一次 GetMethods 枚举查找，构建时仅 MakeGenericMethod（原每次索引段构建
    /// 都 GetMethods 枚举 + LINQ First 闭包匹配）。反射查找发生在静态初始化（AppendSegment
    /// 消费方已带 Requires* 标注）。</para>
    /// </summary>
    private static readonly System.Reflection.MethodInfo s_elementAtOrDefaultDef =
        typeof(Enumerable).GetMethods()
            .First(m => m.Name == nameof(Enumerable.ElementAtOrDefault) && m.GetParameters().Length == 2);

    // BuildCrossTableAssignment 已整体迁入 CrossTableStrategy（方法体逐行不动；
    // RenderPlan 与主循环点路径跨表分支 两调用点已同步限定）

    /// <summary>
    /// 构建 MapWhen 守卫谓词条件表达式
    /// 谓词方法解析（PropertyMatcher.ResolveWhenPredicate，A/B 双规则）+ Expression.Call 静态调用
    /// （编译进映射委托，与 [Convert] 转换器调用同形态——非逐次 MethodInfo.Invoke）。
    /// <para>生成端镜像（#31.6）：CodeGenerator.WhenGuard.cs RenderWhenCall（global:: 限定静态调用）。</para>
    /// <para>实参选择：声明侧显式形态（注册表 ConditionShape）优先；特性路径运行时判定
    /// （单参 == sourceType 判 T2 整源，Nullable 解包——提取期 TypeEqualsUnwrapped 同口径）。</para>
    /// <para>Batch-2 CI 补修：valueExpr 可空化——null（无源映射场景，仅 [MapWhen]
    /// 特性反射回退路径可达）且判 T1 字段值时返回 null（调用方不包裹，与生成端 F43
    /// "声明被拒、映射照常"语义镜像）；T2 整源实参恒 src 不受影响。</para>
    /// </summary>
    /// <param name="sourceParameter">源参数表达式（T2 整源实参）</param>
    /// <param name="valueExpr">T1 字段值实参表达式（源属性值 / null 安全链路径值—— 转换前的源值；null = 无源映射场景）</param>
    /// <param name="sourceType">源实体类型（特性路径形态判定基准）</param>
    /// <param name="targetItem">目标属性（声明类型 = 谓词解析上下文）</param>
    /// <param name="decl">守卫声明（PropertyMatcher.TryGetWhenDeclaration 产物）</param>
    [RequiresUnreferencedCode("BuildWhenGuardCondition resolves the predicate static method via reflection (PropertyMatcher.ResolveWhenPredicate). Some methods may be trimmed.")]
    private static Expression? BuildWhenGuardCondition(ParameterExpression sourceParameter, Expression? valueExpr,
        Type sourceType, PropertyInfo targetItem, PropertyMatcher.WhenDeclaration decl)
    {
        var method = PropertyMatcher.ResolveWhenPredicate(targetItem.DeclaringType!, decl.MethodRef, decl.MethodName);
        var paramType = method.GetParameters()[0].ParameterType;
        var isWholeSource = decl.IsWholeSource
            ?? (Nullable.GetUnderlyingType(paramType) ?? paramType) == sourceType;
        // T1 字段值 + 无源（实参无从构建）→ 守卫不可求值，返回 null（调用方跳过包裹）
        if (!isWholeSource && valueExpr == null) return null;
        var arg = isWholeSource ? sourceParameter : valueExpr!;
        // 类型不匹配兜底（运行时反射路径无编译期签名门——Nullable 装箱/隐式转换）
        if (arg.Type != paramType)
            arg = Convert(arg, paramType);
        return Call(method, arg);
    }

    // BuildCrossTypeAssignment / BuildSameTypeCollectionAssignment 已整体迁入
    // CollectionStrategy、BuildJsonAssignment 已整体迁入 JsonStrategy（方法体逐行不动；
    // RenderPlan :414/:415/:425 调用点已同步限定）

    // BuildTypeConversionAssignment / BuildEnumToStringSwitch 已整体迁入
    // ConvertStrategy（方法体逐行不动；RenderPlan 调用点已同步限定）

    /// <summary>
    /// 为指定源/目标类型构建内联映射表达式（GetClassExpression 调用）
    /// 直接调用 BuildMapBody 获取赋值表达式列表（不再通过反射调用 MapperBase&lt;,&gt;.GetMapBody），
    /// 用 ExpressionVisitor 将其参数替换为外层表达式树中的实际参数，实现零委托内联
    /// </summary>
    /// <param name="srcType">嵌套源类型</param>
    /// <param name="tgtType">嵌套目标类型</param>
    /// <param name="sourceExpr">外层表达式树中的源属性表达式</param>
    /// <param name="innerTgtVar">输出的目标变量表达式（供调用方构建 new + Block）</param>
    /// <returns>内联后的赋值表达式列表；null 表示无法内联（走 fallback）</returns>
    [RequiresDynamicCode("TryGetInlineMapBody builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    [RequiresUnreferencedCode("TryGetInlineMapBody scans type members and attributes via reflection. Some members may be trimmed.")]
    internal static List<Expression>? TryGetInlineMapBody(Type srcType, Type tgtType,
        Expression sourceExpr, out ParameterExpression innerTgtVar)
    {
        innerTgtVar = Variable(tgtType, "innerTgt");
        try
        {
            // 创建临时参数表达式供 BuildMapBody 使用
            var tempSrcParam = Parameter(srcType, "inlineSrc");
            var tempTgtParam = Parameter(tgtType, "inlineTgt");

            // 直接调用 BuildMapBody — 消除原反射调用 mapperType.GetMethod("GetMapBody").Invoke()
            var bodyList = BuildMapBody(srcType, tgtType, tempSrcParam, tempTgtParam);

            // 用 ExpressionVisitor 将 tempSrcParam 替换为 sourceExpr，tempTgtParam 替换为 innerTgtVar
            var rewriter = new ParameterRewriter(tempSrcParam, sourceExpr, tempTgtParam, innerTgtVar);
            var result = new List<Expression>(bodyList.Count);
            foreach (var expr in bodyList)
            {
                result.Add(rewriter.Visit(expr));
            }
            return result;
        }
        catch (Exception ex) when (ex is not MapException)
        {
            // 构建失败（TypeLoadException、MissingMethodException 等）→ 返回 null，调用方走 fallback
            // MapException（配置错误，如 Convert 转换器不合法）不捕获，向上传播给用户
            if (MapTracer.GloballyEnabled) MapTracer.Write($"[NCMapper] TryGetInlineMapBody 构建失败: {srcType.Name}→{tgtType.Name}: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 为嵌套目标类型构建 new 表达式（无参构造或 record 带参构造）
    /// </summary>
    /// <param name="tgtType">目标类型</param>
    /// <param name="srcType">源类型（record 路径需匹配构造器参数）</param>
    /// <param name="sourceExpr">源属性表达式（record 路径用于构造器参数匹配）</param>
    /// <returns>new 表达式；null 表示无法构造</returns>
    [RequiresDynamicCode("TryBuildNewExpr uses MakeGenericType and reflection in the runtime fallback path. Not supported in AOT.")]
    [RequiresUnreferencedCode("TryBuildNewExpr scans type members and attributes via reflection. Some members may be trimmed.")]
    internal static Expression? TryBuildNewExpr(Type tgtType, Type srcType, Expression sourceExpr)
    {
        var defaultCtor = tgtType.GetConstructor(Type.EmptyTypes);
        if (defaultCtor != null)
            return New(defaultCtor);

        // W2-1 ④（P1-SHOULD-1 消除）：record 路径改经委托工厂按实际类型对（record 属性类型对）
        // Resolve——MapperBase<srcType, tgtType> 显式 .cctor 注册 + RunClassConstructor 兜底
        // （BuildConstructorArgs 元组解构 + New(ctor, args) 组装逻辑保持现状）
        var mapperDelegate = MapperDelegateRegistry.Resolve(srcType, tgtType);
        var (ctor, args) = mapperDelegate.BuildConstructorArgs(tgtType, srcType, sourceExpr);
        return New(ctor, args);
    }

    #region 对象转换

    /// <summary>
    /// 集合拷贝共享 ctx 获取方法句柄（T-P2 重构后原集合拷贝方法已改为反射调用，旧方法注释残留清理）
    /// </summary>
#pragma warning disable IL2026 // RequiresUnreferencedCode
#pragma warning disable IL2060 // MakeGenericMethod 无法静态分析
    // 映射路径集合拷贝共享当前线程复用 ctx（替代每次执行 New(typeof(DeepCopyContext))）
    private static readonly System.Reflection.MethodInfo s_getOrCreateDeepCopyCtxMethod =
        typeof(Mapper).GetMethod(nameof(Mapper.GetOrCreateDeepCopyCtx),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
#pragma warning restore IL2060
#pragma warning restore IL2026

    #endregion 对象转换

    /// <summary>
    /// 参数替换器 — 将 BuildMapBody 返回的表达式中的临时参数替换为外层表达式树中的实际表达式
    /// </summary>
    internal sealed class ParameterRewriter : ExpressionVisitor
    {
        /// <summary>
        /// 临时源参数（BuildMapBody 中使用的临时参数）
        /// </summary>
        private readonly ParameterExpression _oldSrc;

        /// <summary>
        /// 实际源表达式（外层表达式树中的对应表达式）
        /// </summary>
        private readonly Expression _newSrc;

        /// <summary>
        /// 临时目标参数
        /// </summary>
        private readonly ParameterExpression _oldTgt;

        /// <summary>
        /// 实际目标表达式
        /// </summary>
        private readonly Expression _newTgt;

        /// <summary>
        /// 创建参数替换器实例
        /// </summary>
        /// <param name="oldSrc">BuildMapBody 中使用的临时源参数</param>
        /// <param name="newSrc">外层表达式树中对应的实际源表达式</param>
        /// <param name="oldTgt">BuildMapBody 中使用的临时目标参数</param>
        /// <param name="newTgt">外层表达式树中对应的实际目标表达式</param>
        public ParameterRewriter(ParameterExpression oldSrc, Expression newSrc,
            ParameterExpression oldTgt, Expression newTgt)
        {
            _oldSrc = oldSrc;
            _newSrc = newSrc;
            _oldTgt = oldTgt;
            _newTgt = newTgt;
        }

        /// <summary>
        /// 访问参数表达式节点 — 将临时参数替换为实际表达式
        /// </summary>
        /// <param name="node">当前访问的参数表达式节点</param>
        /// <returns>替换后的表达式节点</returns>
        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (node == _oldSrc) return _newSrc;
            if (node == _oldTgt) return _newTgt;
            return node;
        }
    }
}
