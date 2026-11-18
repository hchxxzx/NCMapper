// : ORM 降级检测内核统一 —— DegradationDetectorBase
// 从 EfCoreDegradationDetector / FreeSqlDegradationDetector 提取公共骨架：
// 版本化缓存 + IProjectionPolicy 扫描 + 白名单 + 嵌套递归 + 顶层无参构造检查 + PostMap 检测。
// 差异通过两个注入点收敛：RunExtraChecks（顶层额外检查）、RunNestedCollectionChecks（嵌套集合处理）。
#pragma warning disable NCMapper001 // 内部实现使用 [Experimental] 的 Pipeline API
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using NCMapper.Attributes;
using NCMapper.Runtime;

namespace NCMapper.Internal
{
    /// <summary>
    /// 降级检测基类 —— 模板方法：公共判定骨架 + 子类差异注入
    /// <para>EFCore 子类：RunExtraChecks = HasNonProjectableCollection；嵌套集合无额外检查</para>
    /// <para>FreeSql 子类：RunExtraChecks = 全局忽略/集合导航/嵌套深度/嵌套跨表 4 项；嵌套集合元素即降级</para>
    /// <para>ORM 无关判定层（EvaluateChannelAgnostic）承载于 partial 文件
    /// DegradationDetectorBase.Agnostic.cs</para>
    /// </summary>
    internal abstract partial class DegradationDetectorBase
    {
        /// <summary>
        /// 嵌套投影深度上限（EFCore=8 / FreeSql=3）—— 子类显式声明，编译期防漏
        /// </summary>
        protected abstract int MaxNestingDepth { get; }

        /// <summary>
        /// ORM 通道判定缓存条目（版本由键成分改条目内字段——结构先例同文件 Agnostic 通道）
        /// </summary>
        private sealed class DegradationCacheEntry
        {
            public int Version;
            // 注册表内容版本（定案——ORM 通道与 agnostic
            // 通道同族双条件，先例同文件 <see cref="AgnosticCacheEntry"/>.ContentVersion 先行）
            public long ContentVersion;
            public bool Needs;
            public string Reason = "";
        }

        /// <summary>
        /// ORM 通道判定缓存：改<b>实例级</b>双层 CWT 弱键
        /// （外层 Source 弱键 / 内层 Target 弱键；结构对齐同文件 <see cref="AgnosticCacheEntry"/> 通道）。
        /// <para><b>隔离维度规则（硬约束）</b>：缓存结论依赖 ORM 身份——注入点
        /// RunExtraChecks/RunNestedCollectionChecks 实现不同、EFCore（深度 8）与 FreeSql（深度 3）
        /// 上限不同，同一 (Source, Target) 在两 ORM 下结论可不同 ⇒ 缓存必须为<b>实例字段</b>
        /// （两子类各持 <c>Instance</c> 单例），<b>不得静态化</b>（静态化会致跨 ORM 结论串扰）。</para>
        /// <para>弱键化消除 ConcurrentDictionary 强键对 collectible ALC 动态类型的钉驻；
        /// 版本失效改条目内 <see cref="DegradationCacheEntry.Version"/> 校验（每 (S,T) 恒一条目，
        /// 消除版本维度无界沉淀）；清理改 CWT Clear()（net8+，字段 readonly 无实例换新）——
        /// 既有两条 ORM 登记（ef-core-degradation-detector / free-sql-degradation-detector，
        /// 委托 Instance.ClearCaches()）原样保留覆盖本缓存。</para>
        /// </summary>
        private readonly ConditionalWeakTable<Type, ConditionalWeakTable<Type, DegradationCacheEntry>> s_cache = new();

        /// <summary>
        /// 与投影无关的 NCMapper 特性集合（不触发降级）
        /// <para> ：实例字段 static 化——不可变白名单，
        /// 实例份→类型份行为等价；提取 ORM 无关静态判定层（EvaluateTopLayers/NeedsNestedDegradation 静态化）的前提。</para>
        /// </summary>
        private static readonly HashSet<Type> s_projectionIrrelevantAttrs = new()
        {
            typeof(MapFromAttribute),      // 类型级映射声明，已在外层处理
            // T5 契约定性：仅影响降级路径 Include 过滤（有锚 DTO 上合法），
            // 本身不触发降级
            typeof(MapNoAutoIncludeAttribute),
            typeof(MapTreeAttribute),      // 树契约声明（A4 T-3 执行期发现）：树挂载位跳过属投影构建细节，
                                           // 勿因"未声明特性"强制降级——否则所有 [MapTree] DTO 永走 degrade（差异登记）
            // （#17 检查清单登记）：MapWhenAttribute 不得加入本白名单
            // 条件守卫谓词为 CLR 委托路径（SQL 不可翻译），必须触发投影降级
            //（IProjectionPolicy.RequiresDowngrade=true 自声明 + ClassifyUntranslatableDeclaration
            // "MapWhen" 分类双保险；特性扫描 :223/:229 属性级 IProjectionPolicy 命中即降级）
        };

        /// <summary>
        /// **属性级映射配置标注族**：
        /// 所在类型无**特性锚**（Fluent 对 / 无锚）时不因这些标注触发投影降级——
        /// 消除"运行时引擎不消费、降级判定却触发"的口径分叉。
        /// <para>口径边界：**MapWhen 不在本集**（条件守卫必须降级——:66-69 注不变）；
        /// MapFrom/MapNoAutoInclude/MapTree 属 <see cref="s_projectionIrrelevantAttrs"/> 白名单，
        /// 口径不动；本集只影响"未声明特性 → 强制降级"与 "IProjectionPolicy 自声明降级"两条腿
        /// （族内标注在无特性锚类型上两腿均跳过）。备选口径（明细驱动
        /// <c>ClassifyUntranslatableDeclaration</c> 扩展）见。</para>
        /// </summary>
        private static readonly HashSet<Type> s_attributeFamilyGatedAttrs = new()
        {
            typeof(MapIgnoreAttribute),
            typeof(MapPropertyAttribute),
            typeof(ConvertAttribute),
            typeof(ConvertBackAttribute),
            typeof(MaskAttribute),
            typeof(MapFormatAttribute),
        };

        /// <summary>
        /// G6 判定单点：标注属属性级映射配置族且所在类型无特性锚 → 不触发降级（跳过本标注）。
        /// </summary>
        /// <param name="attr">扫描到的标注实例（<c>GetCustomAttributes</c> 返回 <c>object[]</c>——按 object 收）</param>
        /// <param name="ownerType">标注所在类型（属性取 DeclaringType、类级取声明类型）</param>
        /// <returns>true = 本标注不参与降级判定</returns>
        private static bool IsAttributeFamilyWithoutAnchor(object attr, Type ownerType)
            => s_attributeFamilyGatedAttrs.Contains(attr.GetType())
               && !RuntimeAnchorGate.HasAttributeAnchor(ownerType);

        /// <summary>
        /// Fluent/特性映射声明不可翻译分类（G1-② 翻译路径矩阵；
        /// 对齐表落地——扫描面扩至特性对）
        /// 扫描快照明细条目的映射对级承载字段、pm 级能力位与 Compose 跨表能力位，
        /// 返回首个不可翻译声明类别
        /// </summary>
        /// <param name="meta">快照明细条目（特性对与 Fluent 对统一——TryGetMappingDetail 命中形态）</param>
        /// <returns>不可翻译时返回类别名（Mask/Convert/FromJson/AfterMapping/BeforeMapping/SubclassDispatch/
        /// NoAutoMatch/Compose/Format/MapWhen——语义对齐特性侧 IProjectionPolicy.RequiresDowngrade 自声明）；
        /// 纯可翻译（Map 异名/嵌套路径/Ignore）时 null</returns>
        /// <remarks>
        /// <para>机制对应：四承载字段与特性侧自声明同构——HasMask↔[Mask].RequiresDowngrade=true、
        /// HasConverter↔[Convert].RequiresDowngrade=true、IsFromJson↔[MapProperty].IsFromJson、
        /// AfterMappingMethodRef/BeforeMappingMethodRef↔[AfterMapping]/[BeforeMapping].RequiresDowngrade=true
        ///（迁出条件兑现——特性侧钩子已落地，双字段同构自声明；
        /// 投影嵌套构建不执行生成代码，钩子语义同样丢失）。
        /// 未来新增"影响投影能力"的终端/特性时须同步本判定——同步义务由投影降级承载登记门禁
        ///（起 前移为 出口判据：承载路径存在 ∨ Classify 实际消费双核查）逼红。</para>
        /// </remarks>
        internal static string? ClassifyUntranslatableDeclaration(RuntimeMapperMetadata meta)
        {
            // Compose 判定（决策点 2/4—— /， PoC 定稿：
            // 形态空间 14 形态 + 不可译类别集 {Mask,Convert,Format,FromJson,MapWhen}∪{AfterMapping,
            // BeforeMapping,SubclassDispatch,Compose}，PoC 15 用例实证 @`13853ad`）：
            // 原"HasComposeMappings → 恒 Compose"短路改为 **H3 保守臂**——"声明有、产出无"形态
            //（composeFlag=true 而 ComposeMappings 空——F29 fail-fast 族）按"无可译依据"保守维持降级；
            // ComposeMappings 非空时落下方通用判定序（对级三类 → Properties 五类 → ComposeMappings
            // 五类循环）——Fluent Compose 对由恒降级改为明细可译性判定（行为变更：可译场景放行投影，
            // 不可译场景 reason 细化为具体类别）。特性对（[MapFrom] 通道）composeFlag 恒 false
            //（RuntimeMapperMetadata 载荷语义），跨表能力位仍由文末 ComposeMappings 循环承接
            //（对齐表——跨表 Mask 漏判 = 明文外泄级），行为零变化（R8 防线）。
            if (meta.HasComposeMappings && meta.ComposeMappings.Length == 0)
                return "Compose";
            // 多态分派分类（——isinst 分派链不可投影，
            // 恒降级；Fluent .MapSubclass 与特性侧 [MapSubclass]同承载同分类）
            if (meta.HasSubclassDispatch)
                return "SubclassDispatch";
            if (!string.IsNullOrEmpty(meta.AfterMappingMethodRef))
                return "AfterMapping";
            // BeforeMapping 分类——前置钩子同样不可投影翻译
            if (!string.IsNullOrEmpty(meta.BeforeMappingMethodRef))
                return "BeforeMapping";
            // 
            // 通道限定 Channel==MapFrom——特性侧白名单协议位强制降级（20261010-005 "先降级保
            // 正确、后优化"；列裁剪优化方向登记 /#18）；Fluent 侧白名单语义由提取期收窄
            // 承载（MappingSnapshotBuilder：NoAutoInclude 时自动匹配不入候选——明细即白名单面），维持
            // 不降级（现状）。双通道不对称属机制差异（有意），统一化归 后续方向
            if (meta.Channel == RuntimeMappingChannel.MapFrom && meta.NoAutoInclude)
                return "NoAutoMatch";
            foreach (var p in meta.Properties)
            {
                if (p.HasMask) return "Mask";
                if (p.HasConverter) return "Convert";
                if (p.IsFromJson) return "FromJson";
                // ：Format 分类
                // ToString(格式串) 为 CLR 路径（SQL 不可翻译），恒降级；与特性侧 [MapFormat]
                // IProjectionPolicy.RequiresDowngrade=true 自声明同构（承载 + 分类双保险）
                if (!string.IsNullOrEmpty(p.FormatString)) return "Format";
                // MapWhen 分类（——条件守卫谓词 CLR 委托路径，
                // SQL 不可翻译，恒降级；与特性侧 [MapWhen] IProjectionPolicy.RequiresDowngrade=true 同构）
                if (!string.IsNullOrEmpty(p.ConditionMethodRef)) return "MapWhen";
            }
            // 
            // 跨表声明的能力位（Mask/Convert/FromJson/Format/When 承载于 RuntimeComposeMapping，
            // 发射）现状由属性级扫描腿直接命中；切换后由本明细扫描承接
            // 特性对（HasComposeMappings=false）经此扫描等价于属性级扫描腿（对齐表驱动）；
            // 空组条目跳过
            foreach (var cm in meta.ComposeMappings)
            {
                if (cm.SourcePathNames.Length == 0) continue;
                if (cm.HasMask) return "Mask";
                if (cm.HasConverter) return "Convert";
                if (cm.IsFromJson) return "FromJson";
                if (!string.IsNullOrEmpty(cm.FormatString)) return "Format";
                if (!string.IsNullOrEmpty(cm.ConditionMethodRef)) return "MapWhen";
            }
            return null;
        }

        /// <summary>
        /// 额外检查注入点（顶层）—— EFCore: HasNonProjectableCollection；FreeSql: 4 项检查
        /// </summary>
        /// <param name="sourceType">源实体类型</param>
        /// <param name="targetType">目标 DTO 类型</param>
        /// <param name="reason">降级原因（返回 true 时有效）</param>
        /// <returns>需要降级时为 true</returns>
        [RequiresUnreferencedCode("RunExtraChecks scans type members and attributes via reflection. Some members may be trimmed.")]
        protected abstract bool RunExtraChecks(Type sourceType, Type targetType, out string reason);

        /// <summary>
        /// 嵌套集合检查注入点 —— EFCore: 恒 false；FreeSql: 集合元素即降级 + HasCollectionNavigation
        /// </summary>
        /// <param name="nestedType">嵌套属性类型（元素或属性声明类型）</param>
        /// <param name="nestedSourceType">嵌套属性的源类型</param>
        /// <param name="isCollectionElement">当前嵌套属性是否为集合元素（List&lt;T&gt; 的 T）</param>
        /// <param name="reason">降级原因（返回 true 时有效）</param>
        /// <returns>需要降级时为 true</returns>
        [RequiresUnreferencedCode("RunNestedCollectionChecks scans type members and attributes via reflection. Some members may be trimmed.")]
        protected abstract bool RunNestedCollectionChecks(
            Type nestedType, Type nestedSourceType, bool isCollectionElement, out string reason);

        /// <summary>
        /// 判定是否需要降级（带版本化缓存）—— 派生类静态门面委托本方法
        /// </summary>
        /// <param name="sourceType">源实体类型</param>
        /// <param name="targetType">目标 DTO 类型</param>
        /// <param name="reason">降级原因（返回 true 时有效）</param>
        /// <returns>需要降级时为 true</returns>
        [RequiresUnreferencedCode("NeedsDegradation scans type members and attributes via reflection. Some members may be trimmed.")]
        internal bool Evaluate(Type sourceType, Type targetType, out string reason)
        {
            // CWT 弱键读路径——版本条目内校验（命中条件 Version == Pipeline.Version，
            // 不等重算覆盖；语义等价原版本化键，先例 Agnostic 通道）
            var version = Mapper.Pipeline.Version;
            // 双条件——Pipeline.Version（PostMap 版）
            // 与 RuntimeSnapshotStore.ContentVersion（注册表内容版）任一失配即重算——
            // 注册表手工 Register/Clear 后旧条目不得继续命中（先例同文件 Agnostic 通道 先行）
            var contentVersion = RuntimeSnapshotStore.ContentVersion;
            var inner = s_cache.GetValue(sourceType, static _ => new());
            if (inner.TryGetValue(targetType, out var cached)
                && cached.Version == version && cached.ContentVersion == contentVersion)
            {
                reason = cached.Reason;
                return cached.Needs;
            }

            var needs = NeedsDegradationCore(sourceType, targetType, out reason);
            inner.AddOrUpdate(targetType, new DegradationCacheEntry { Version = version, ContentVersion = contentVersion, Needs = needs, Reason = reason });
            return needs;
        }

        /// <summary>
        /// 清除缓存 —— 供测试隔离调用（派生类静态门面委托本方法）。
        /// CWT Clear()（net8+；实例字段 readonly，无实例换新）
        /// </summary>
        internal void ClearCaches() => s_cache.Clear();

        [RequiresUnreferencedCode("NeedsDegradationCore scans type members and attributes via reflection. Some members may be trimmed.")]
        private bool NeedsDegradationCore(Type sourceType, Type targetType, out string reason)
        {
            // 顶层四层判定（Fluent 声明分流/顶层无参构造/类级特性扫描/属性级特性扫描）——
            // 原内联段（G1-①/G1-② 注释与实现）原样搬移至 EvaluateTopLayers（
            // 提取自，判定顺序与 reason 文本零变更），供 ORM 检测器与 agnostic 通道共用。
            if (EvaluateTopLayers(sourceType, targetType, out reason))
                return true;

            // 子类额外检查（EFCore: 不可投影集合；FreeSql: 全局忽略/集合导航/嵌套深度/嵌套跨表）
            if (RunExtraChecks(sourceType, targetType, out reason))
                return true;

            // 嵌套递归检测（owner=this：启用 ORM 特定嵌套集合检查——原 includeOrmSpecific 参数并入 owner）
            if (NeedsNestedDegradation(this, targetType, sourceType, out reason, depth: 0, visited: null))
                return true;

            // PostMap 检测
            if (Mapper.Pipeline.IsEnabled && Mapper.Pipeline.HasPostMapFor(sourceType, targetType))
            {
                reason = $"已为 ({sourceType.Name} → {targetType.Name}) 注册了 Pipeline.PostMap";
                return true;
            }

            reason = "";
            return false;
        }

        /// <summary>
        /// 顶层四层判定（ORM 无关）—— 快照三态分流 → 顶层无参构造 → 类级特性扫描（兜底）→ 属性级特性扫描（兜底）。
        /// <para>static 提取前提 = <see cref="s_projectionIrrelevantAttrs"/> static 化。
        /// 分流三态化：既有 IsFluentRegisteredDto/TryGetFluentDetailMeta 双门升级为统一门面
        /// <see cref="RuntimeSnapshotStore.TryGetMappingDetail"/>（特性对/Fluent 对按 (源,目标) 精确对统一命中）：
        /// ① 明细命中（条目存在且 Properties 非空——判据逐字对齐 TryGetFluentDetailMeta 既有 on 态门）
        /// → <see cref="ClassifyUntranslatableDeclaration"/> 判定；纯可翻译 → 快照为权威，特性扫描腿不跑，
        /// 无参构造仍生效；
        /// ② 条目命中而明细不可达（off 态身份键；Properties 空的其他形态保守同判）→ Fluent 通道维持
        /// 既有强制降级（reason 文本逐字保持）/MapFrom 通道（罕见空明细形态）落既有判定序（等价现状）；
        /// ③ miss（纯运行时模式/未注册对）→ 落既有判定序（特性扫描腿兜底——现状逻辑原样）。</para>
        /// <para>判定顺序：Fluent 分流 → 无参构造 → 类级扫描 → 属性级扫描；
        /// off 态 reason 文本为稳定契约（诊断文案锁）。</para>
        /// </summary>
        [RequiresUnreferencedCode("EvaluateTopLayers scans type members and attributes via reflection. Some members may be trimmed.")]
        private static bool EvaluateTopLayers(Type sourceType, Type targetType, out string reason)
        {
            // G1-① 三态分流
            // 现状 IsFluentRegisteredDto + TryGetFluentDetailMeta 双门（target 任意源命中）升级为
            // (sourceType, targetType) 精确对门面：明细命中 → Classify（对齐表 12 项）；明细不可达 →
            // Fluent 通道维持 off 态强制降级（B9 逐字）/MapFrom 通道落既有判定序；miss → 特性扫描腿。
            // （语义修正登记：多源注册同一 DTO 时，现状按 target 任意源条目分流——源错位；切换后按
            // 本判定调用的真实源对精确命中。差异面随 对照记录登记归因。）
            // 判定入口覆盖：off 态身份登记键（起身份段独立输出节点无条件发射，与
            // EmitRuntimeSnapshot 解耦——复审 R-A 承诺域）/ on 态明细条目 / 特性对条目（产物恒明细形态）均命中。
            var snapshotAuthoritative = false; // 三态①纯可翻译明细 → 特性扫描腿不跑（快照为权威）
            if (RuntimeSnapshotStore.TryGetMappingDetail(sourceType, targetType, out var detailMeta))
            {
                // 决策点 7：三态① 判据由 Properties.Length>0
                // 扩为 Properties.Length>0 ∨ ComposeMappings 有效非空——Compose-only 声明对
                //（多源合成 DTO 全字段来自跨表）视为明细可达（与投影主链/内核入口两处同批一致扩容；
                // 编制期"两处"口径 → 实现三处的第三处，理由：三态① 判据族一致性——检测器不扩容则
                // F13 形态误走三态② off 态降级，验收 8 可译不成立）。空组条目形态由 Classify 循环
                // continue 兜底（发射产物无空组行——防御面）。
                if (detailMeta.Properties.Length > 0 || detailMeta.ComposeMappings.Length > 0)
                {
                    // 三态①明细命中：Classify 判定（对齐表 12 项承载——特性对/Fluent 对统一）
                    var untranslatable = ClassifyUntranslatableDeclaration(detailMeta);
                    if (untranslatable != null)
                    {
                        // Fluent 通道文本逐字保持现状（含"Fluent 声明类型对"字样）；
                        // MapFrom 通道明细命中为新路径（特性对判定源切换），新文本同构
                        reason = detailMeta.Channel == RuntimeMappingChannel.Fluent
                            ? $"Fluent 声明类型对（{targetType.Name}）含不可翻译声明（{untranslatable}），投影无法表达，强制降级走内存映射"
                            : $"映射声明类型对（{targetType.Name}）含不可翻译声明（{untranslatable}），投影无法表达，强制降级走内存映射";
                        return true;
                    }
                    // 纯可翻译明细 → 快照为权威（G1-② 声明级分流语义——特性扫描腿不跑，
                    // 未知 NCMapper 特性守卫随之仅存于兜底腿—— 边界段 定案）
                    snapshotAuthoritative = true;
                }
                else if (detailMeta.Channel == RuntimeMappingChannel.Fluent)
                {
                    // 三态②条目命中而明细不可达（off 态身份键；Properties 与 ComposeMappings 均空——
                    // 扩容后此臂收窄）：Fluent 通道维持既有强制降级（reason 文本逐字保持——B9/F3 红线，不放宽）
                    reason = $"Fluent 声明类型对（{targetType.Name}，off 态身份键/明细不可达），走降级兜底内存映射";
                    return true;
                }
                // 三态② MapFrom 通道（罕见空明细形态）→ 落既有判定序（等价现状）；三态③ miss 同
            }

            // 顶层无参构造器检查（已加：record 等有参构造类型 → 降级；三态①②③共享
            // 目标类型形态检查与声明数据源无关）
            if (targetType.GetConstructor(Type.EmptyTypes) == null)
            {
                reason = $"顶层 DTO 类型 {targetType.Name} 无无参构造器（如 record 类型），投影无法构建 MemberInit 表达式，需降级到内存映射";
                return true;
            }

            // 类级特性扫描（三态③ miss 兜底腿 + 三态② MapFrom 空明细落序；三态①纯可翻译明细不跑——
            // 快照为权威）：IProjectionPolicy 需降级 / 未知 NCMapper 特性
            if (!snapshotAuthoritative)
            {
            var classAttrs = targetType.GetCustomAttributes(inherit: true);
            foreach (var attr in classAttrs)
            {
                // 属性级映射配置族标注在无特性锚类型上不参与降级判定
                if (IsAttributeFamilyWithoutAnchor(attr, targetType)) continue;
                if (attr is IProjectionPolicy policy && policy.RequiresDowngrade)
                {
                    reason = $"类型 {targetType.Name} 标注了 {attr.GetType().Name}（需要降级）";
                    return true;
                }
                if (attr is not IProjectionPolicy
                    && attr.GetType().Namespace?.StartsWith("NCMapper.") == true
                    && !s_projectionIrrelevantAttrs.Contains(attr.GetType()))
                {
                    reason = $"类型 {targetType.Name} 标注了未声明的 NCMapper 特性 {attr.GetType().Name}";
                    return true;
                }
            }

            // 属性级特性扫描（兜底腿——同上执行条件）
            var targetProps = TypeHelper.GetPublicProperties(targetType);
            foreach (var prop in targetProps)
            {
                var attrs = prop.GetCustomAttributes(inherit: true);
                foreach (var attr in attrs)
                {
                    // 属性级映射配置族标注在无特性锚类型上不参与降级判定
                    if (IsAttributeFamilyWithoutAnchor(attr, prop.DeclaringType!)) continue;
                    if (attr is IProjectionPolicy p && p.RequiresDowngrade)
                    {
                        reason = $"属性 {prop.Name} 标注了 {attr.GetType().Name}（需要降级）";
                        return true;
                    }
                    if (attr is not IProjectionPolicy
                        && attr.GetType().Namespace?.StartsWith("NCMapper.") == true
                        && !s_projectionIrrelevantAttrs.Contains(attr.GetType()))
                    {
                        reason = $"属性 {prop.Name} 标注了未声明的 NCMapper 特性 {attr.GetType().Name}";
                        return true;
                    }
                }
            }
            }

            reason = "";
            return false;
        }

        /// <summary>
        /// 嵌套递归降级检测（static， ②/ ②）
        /// 原 includeOrmSpecific 布尔参数并入 <paramref name="owner"/>：owner!=null（ORM 检测器通道）
        /// 启用嵌套集合检查注入点；owner==null（agnostic 通道）跳过 ORM 特定检查、保留嵌套无关子集
        /// （嵌套 Fluent 明细判定/嵌套无参构造/嵌套特性扫描/嵌套 PostMap）。
        /// <para>判定顺序与 reason 文本零变更。</para>
        /// </summary>
        /// <param name="owner">调用方检测器实例（ORM 特定嵌套集合检查注入点；agnostic 通道传 null）</param>
        /// <param name="targetType">当前目标 DTO 类型</param>
        /// <param name="sourceType">当前源类型</param>
        /// <param name="reason">降级原因（返回 true 时有效）</param>
        /// <param name="depth">嵌套深度</param>
        /// <param name="visited">已访问类型集合（防环）</param>
        /// <returns>需要降级时为 true</returns>
        [RequiresUnreferencedCode("NeedsNestedDegradation scans type members and attributes via reflection. Some members may be trimmed.")]
        private static bool NeedsNestedDegradation(DegradationDetectorBase? owner, Type targetType, Type sourceType, out string reason,
            int depth, HashSet<Type>? visited)
        {
            reason = "";

            // 硬编码 8 → 对齐派生类抽象上限（EFCore=8/FreeSql=3，
            // 抽象属性）——原 FreeSql 深层（4-8 层）触发源仍被扫描，与自身 MaxNestingDepth
            // 降级口径不一致；agnostic 通道（owner==null）无 ORM 上限语义，维持既有 8 截止。
            if (depth > (owner?.MaxNestingDepth ?? 8)) return false;

            visited ??= new HashSet<Type>();
            if (!visited.Add(targetType)) return false;

            var targetProps = TypeHelper.GetPublicProperties(targetType);
            foreach (var prop in targetProps)
            {
                var propType = prop.PropertyType;

                if (propType.IsValueType || propType == typeof(string)) continue;

                Type? elementType = null;
                if (propType.IsGenericType)
                {
                    var genArgs = propType.GetGenericArguments();
                    if (genArgs.Length == 1 && !genArgs[0].IsValueType && genArgs[0] != typeof(string))
                        elementType = genArgs[0];
                }
                else if (propType.IsArray)
                {
                    var et = propType.GetElementType();
                    if (et != null && !et.IsValueType && et != typeof(string))
                        elementType = et;
                }

                var nestedType = elementType ?? propType;
                if (nestedType.IsValueType || nestedType == typeof(string)) continue;

                // 注册表优先 + 反射回退双读（主源查询）
                if (!RuntimeSnapshotStore.TryGetMappingMeta(nestedType, out var view191)) continue;

                var nestedSourceType = view191.PrimarySource!;

                // G1-② 嵌套快照明细判定（建；
                // 同构三态化，嵌套扫描腿同降为兜底）：嵌套子 DTO 的不可翻译声明
                //（Mask/Convert/FromJson/AfterMapping/Compose/NoAutoMatch/Format/MapWhen——分类集
                // 扩展后自动生效，嵌套路径对 ComposeMappings/NoAutoInclude 经
                // RuntimeMapperMetadata 全字段承载可见）承载于注册表 pm 级/对级（无特性），
                // 下方嵌套特性扫描读不到——漏判即 F5（投影嵌套构建静默丢 Mask 明文）。
                // 三态（与顶层同构）：明细命中（Properties 非空——判据逐字同顶层）→ 不分通道走
                // Classify（对齐表承接嵌套特性对的扫描判据——特性对跨表能力位由 ComposeMappings
                // 扫描覆盖）；条目命中明细不可达（off 态身份键）→ 落后续嵌套检查（嵌套既有保守
                // 语义——:380 注释口径维持，不引入顶层三态②的强制降级增强）；纯可翻译明细 →
                // 嵌套扫描腿不跑（快照为权威——nestedDetailAuthoritative）。
                var nestedDetailAuthoritative = false;
                if (view191.IsRegistered && view191.Metadata != null)
                {
                    if (view191.Metadata.Properties.Length > 0)
                    {
                        var nestedUntranslatable = ClassifyUntranslatableDeclaration(view191.Metadata);
                        if (nestedUntranslatable != null)
                        {
                            // Fluent 通道文本逐字保持现状；MapFrom 通道明细命中为新路径（嵌套
                            // 特性对判定源切换），新文本同构
                            reason = view191.Metadata.Channel == RuntimeMappingChannel.Fluent
                                ? $"嵌套 Fluent DTO {nestedType.Name}（属性 {prop.Name}）含不可翻译声明（{nestedUntranslatable}），投影嵌套构建无法表达，强制降级走内存映射"
                                : $"嵌套映射 DTO {nestedType.Name}（属性 {prop.Name}）含不可翻译声明（{nestedUntranslatable}），投影嵌套构建无法表达，强制降级走内存映射";
                            return true;
                        }
                        nestedDetailAuthoritative = true;
                    }
                }

                // 子类嵌套集合检查（FreeSql: 集合元素即降级 + 嵌套 HasCollectionNavigation；EFCore: 无）
                // owner==null（agnostic 通道）跳过 ORM 特定检查（②）
                if (owner != null && owner.RunNestedCollectionChecks(nestedType, nestedSourceType, elementType != null, out reason))
                    return true;

                // 嵌套 DTO 无参构造器检查
                if (nestedType.GetConstructor(Type.EmptyTypes) == null)
                {
                    reason = $"嵌套属性 {prop.Name} 的元素类型 {nestedType.Name} 无无参构造器（如 record 类型），投影无法构建 MemberInit 表达式，需降级到内存映射";
                    return true;
                }

                // 嵌套属性特性扫描（兜底腿——嵌套纯可翻译明细命中时不跑：快照为权威）
                if (!nestedDetailAuthoritative)
                {
                var nestedProps = TypeHelper.GetPublicProperties(nestedType);
                foreach (var nestedProp in nestedProps)
                {
                    var attrs = nestedProp.GetCustomAttributes(inherit: true);
                    foreach (var attr in attrs)
                    {
                        // 嵌套 DTO 属性级映射配置族标注同口径
                        if (IsAttributeFamilyWithoutAnchor(attr, nestedProp.DeclaringType!)) continue;
                        if (attr is IProjectionPolicy p && p.RequiresDowngrade)
                        {
                            reason = $"嵌套 DTO {nestedType.Name} 的属性 {nestedProp.Name} 标注了 {attr.GetType().Name}（需要降级）";
                            return true;
                        }

                        if (attr is not IProjectionPolicy
                            && attr.GetType().Namespace?.StartsWith("NCMapper.") == true
                            && !s_projectionIrrelevantAttrs.Contains(attr.GetType()))
                        {
                            reason = $"嵌套类型 {nestedType.Name} 属性 {nestedProp.Name} 标注了未声明的 NCMapper 特性 {attr.GetType().Name}";
                            return true;
                        }
                    }
                }
                }

                // 嵌套 PostMap 检测
                if (Mapper.Pipeline.IsEnabled && Mapper.Pipeline.HasPostMapFor(nestedSourceType, nestedType))
                {
                    reason = $"已为嵌套类型对 ({nestedSourceType.Name} → {nestedType.Name}) 注册了 Pipeline.PostMap";
                    return true;
                }

                // 递归
                if (NeedsNestedDegradation(owner, nestedType, nestedSourceType, out reason, depth + 1, visited))
                    return true;
            }

            return false;
        }
    }
}
