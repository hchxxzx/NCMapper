// ORM 无关降级判定层（partial 承载）
// 与 DegradationDetectorBase.NeedsDegradationCore 同构分流：顶层四层（EvaluateTopLayers 共用）+
// 嵌套无关子集（NeedsNestedDegradation owner=null）+ 顶层 PostMap；不含 ORM 注入点
// （RunExtraChecks / RunNestedCollectionChecks——D-1 报告口径 = ORM 无关层）。
// 消费方：MapValidator.Validate 双通道降级维度填充——特性通道对与 Fluent 通道对共用本判定。
// 判定顺序与 reason 文本与 NeedsDegradationCore 逐字一致（零变更）。
#pragma warning disable NCMapper001 // 内部实现使用 [Experimental] 的 Pipeline API
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using NCMapper.Runtime;

namespace NCMapper.Internal
{
    /// <summary>
    /// ORM 无关降级判定层 —— <see cref="DegradationDetectorBase"/> 的 partial 承载
    /// </summary>
    /// <remarks>
    /// <para><b>承载形态</b>：partial 新文件保持基类文件聚焦；<see cref="EvaluateChannelAgnostic"/> 为
    /// static 门面（无需 ORM 差异注入点），独立版本化缓存（键含 <see cref="MapPipeline.Version"/>，
    /// 同构基类 <c>s_cache</c> 既有模式）。</para>
    /// <para><b>判定口径</b>报告仅呈现 ORM 无关判定层——顶层四层
    /// （Fluent 声明分流/顶层无参构造/类级特性扫描/属性级特性扫描）+ 嵌套无关子集
    /// （嵌套 Fluent 判定/嵌套无参构造/嵌套特性扫描/嵌套 PostMap）+ 顶层 PostMap。
    /// ORM 特定检查（EFCore 不可投影集合、FreeSql 全局忽略/集合导航/嵌套深度/嵌套跨表）不入本判定，
    /// 报告 reason 文本由各 ORM 检测器运行时另行承载（X1 备案）。</para>
    /// </remarks>
    internal abstract partial class DegradationDetectorBase
    {
    /// <summary>
    /// agnostic 通道缓存条目 —— CWT TValue 须为引用类型（CS0452），值元组包一层
    /// </summary>
    private sealed class AgnosticCacheEntry
    {
        public int Version;
        // 注册表内容版本（定案——降级/agnostic 缓存
        // 条目校验扩为 (Pipeline.Version, ContentVersion) 双条件，消除"注册表手工变更不递增
        // 版本"的同族陈旧坑；先例同族 DegradationCacheEntry.Version 条目内比对）
        public long ContentVersion;
        public bool Needs;
        public string Reason = "";
    }

    /// <summary>
    /// agnostic 通道缓存 —— CWT 弱键双层结构（外层 Source 弱键 / 内层 Target 弱键）。
    /// <para>T11AdmissionProbeTests.S29 准入门（同族）：ConcurrentDictionary 元组键会强引用
    /// collectible ALC 类型致动态程序集无法回收——Validate 特性循环对任意程序集类型调用本判定，
    /// 缓存必须弱键。版本失效采用条目内版本校验（Version != Pipeline._version 时重算覆盖），
    /// 语义同基类 s_cache 的版本化键；ClearAgnosticCache 以换新实例实现清理
    ///（订正：net8+ 的 ConditionalWeakTable 已提供 Clear()，
    /// 原"CWT 无 Clear API"表述系 netstandard2.0 时代前提错误——机制收敛登记，
    /// 本批仅订正注释、机制不动）。</para>
    /// </summary>
    private static ConditionalWeakTable<Type, ConditionalWeakTable<Type, AgnosticCacheEntry>>
        s_agnosticCache = new();

        static DegradationDetectorBase()
        {
            // CacheRegistry 登记制（范式，同 FreeSqlDegradationDetector）：
            // 仅登记清理委托，不触发缓存构建（防自触发）；Mapper.ClearCache() 全链清理覆盖本缓存
            CacheRegistry.Register("degradation-detector-agnostic", CacheLayer.Full, ClearAgnosticCache);
        }

        /// <summary>
        /// ORM 无关降级判定（带版本化缓存）—— 双通道映射对的报告面判定入口
        /// <para>特性通道对（[MapFrom] 扫描结果）与 Fluent 通道对（RuntimeSnapshotStore 补扫）均经本判定
        /// 填充 <c>MapValidationResult</c> 降级维度。</para>
        /// </summary>
        /// <param name="sourceType">源实体类型</param>
        /// <param name="targetType">目标 DTO 类型</param>
        /// <param name="reason">降级原因（返回 true 时有效；文本与 ORM 检测器同层逐字一致）</param>
        /// <returns>ORM 无关判定层认为需要降级时为 true</returns>
        [RequiresUnreferencedCode("EvaluateChannelAgnostic scans type members and attributes via reflection. Some members may be trimmed.")]
        internal static bool EvaluateChannelAgnostic(Type sourceType, Type targetType, out string reason)
        {
            // CWT 弱键读路径：ALC 卸载后条目随 GC 失效（同族）；版本不匹配重算（同构版本化键语义）。
            // 双条件：Pipeline.Version（PostMap 版）
            // 与 RuntimeSnapshotStore.ContentVersion（注册表内容版）任一失配即重算——
            // 注册表手工 Register/Clear 后旧条目不得继续命中
            var version = Mapper.Pipeline.Version;
            var contentVersion = RuntimeSnapshotStore.ContentVersion;
            var inner = s_agnosticCache.GetValue(sourceType, _ => new());
            if (inner.TryGetValue(targetType, out var cached)
                && cached.Version == version && cached.ContentVersion == contentVersion)
            {
                reason = cached.Reason;
                return cached.Needs;
            }

            var needs = EvaluateChannelAgnosticCore(sourceType, targetType, out reason);
            inner.AddOrUpdate(targetType, new AgnosticCacheEntry { Version = version, ContentVersion = contentVersion, Needs = needs, Reason = reason });
            return needs;
        }

        /// <summary>
        /// ORM 无关判定内核 —— 判定序同构<c>NeedsDegradationCore</c>（顺序与 reason 文本零变更），
        /// 仅按 D-1 口径移除 ORM 注入点：顶层 RunExtraChecks 不调用；嵌套检查 owner=null
        /// （NeedsNestedDegradation 内守卫跳过 RunNestedCollectionChecks）
        /// </summary>
        [RequiresUnreferencedCode("EvaluateChannelAgnosticCore scans type members and attributes via reflection. Some members may be trimmed.")]
        private static bool EvaluateChannelAgnosticCore(Type sourceType, Type targetType, out string reason)
        {
            // 顶层四层（与 NeedsDegradationCore 第一判定同源共用）
            if (EvaluateTopLayers(sourceType, targetType, out reason))
                return true;

            // 嵌套递归检测（owner=null：agnostic 通道，跳过 ORM 特定嵌套集合检查）
            if (NeedsNestedDegradation(owner: null, targetType, sourceType, out reason, depth: 0, visited: null))
                return true;

            // PostMap 检测（顶层；Mapper.Pipeline 静态可达，无实例依赖）
            if (Mapper.Pipeline.IsEnabled && Mapper.Pipeline.HasPostMapFor(sourceType, targetType))
            {
                reason = $"已为 ({sourceType.Name} → {targetType.Name}) 注册了 Pipeline.PostMap";
                return true;
            }

            reason = "";
            return false;
        }

        /// <summary>
        /// 清除 agnostic 通道缓存 —— 供 Mapper.ClearCache 全链（CacheRegistry）与测试隔离调用；
        /// 以换新实例实现清理（订正：net8+ 已有 CWT Clear()，
        /// 原"CWT 无 Clear API"表述失实——机制收敛登记，本批机制不动，旧表整体交 GC）
        /// </summary>
        internal static void ClearAgnosticCache() => s_agnosticCache = new();
    }
}
