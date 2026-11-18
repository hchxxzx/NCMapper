// 快照运行时元数据承载类型
// 发现机制候选 b：Type 键控注册表（查询面，ORM 消费主路径）+ 候选 c 载体（All 全量清单）。
// 统一查询门面 TryGetMappingMeta：注册表优先 + 反射回退双读收敛为单入口。
using NCMapper.Attributes;
using NCMapper.Internal;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

namespace NCMapper.Runtime;

/// <summary>
/// Type 键控运行时元数据注册表 —— ORM 投影/降级检测的消费主路径
/// </summary>
/// <remarks>
/// <para>注册由生成代码 [ModuleInitializer] 执行（与 <see cref="RuntimeSnapshotPair{TSource,TTarget}"/>
/// 双写、同一元数据实例）；冲突对（跨通道互斥/Fluent 同对重复）在发射端恒抑制，注册表无重复键。</para>
/// <para>主源选择规则：多源 DTO（同目标类型多源注册）按 Channel=MapFrom 优先；
/// 同通道多条按 <see cref="RuntimeMapperMetadata.SourceTypeFqn"/> 序取首——
/// 与 [MapFrom] 单源语义兼容（单源场景规则退化为唯一解）。</para>
/// <para><b>消费面口径</b>：注册来源双通道——生成代码 [ModuleInitializer] 发射与运行时
/// 手工 <see cref="Register"/>（幂等合并，同键终态一条）。属性级映射配置特性
/// （[Mask]/[Convert] 等）的运行时消费准入 = 特性锚（[MapFrom] 反射腿）；注册表明细
/// 承载的声明（如脱敏类型名）不经特性锚门——映射决策链特性腿优先、注册表腿在特性腿
/// 未命中时补位，两腿互斥不叠加。</para>
/// <para><b>消费面口径</b>：注册来源双通道——生成代码 [ModuleInitializer] 发射与运行时
/// 手工 <see cref="Register"/>（幂等合并，同键终态一条）。属性级映射配置特性
/// （[Mask]/[Convert] 等）的运行时消费准入 = 特性锚（[MapFrom] 反射腿）；注册表明细
/// 承载的声明（如脱敏类型名）不经特性锚门——映射决策链特性腿优先、注册表腿在特性腿
/// 未命中时补位，两腿互斥不叠加。</para>
/// <para><b>性能契约</b>：</para>
/// <para>C-1 读侧无锁——读侧<b>不出现<c>lock (s_allLock)</c></b>（封闭口径，覆盖全部 6 个读出口：
/// <see cref="All"/>/<see cref="FindByTarget"/>/<see cref="IsPropertyNoAutoInclude"/>/
/// <see cref="IsFluentRegisteredDto"/>/<see cref="TryGetFluentDetailMeta"/>/<see cref="GetPrimaryMeta"/>
/// 及其转发出口）；并发 <see cref="Register"/> 期间读到的是某一<b>完整历史快照</b>（可能是注册前或
/// 注册后的状态），不出现撕裂或半写状态。三容器以"不可变数组 + 整替换"承载：读侧任一出口只读
/// 单一容器（读模型 P3），不感知发布交错。</para>
/// <para>C-2 三容器写原子——写侧（<see cref="Register"/>/<see cref="Clear"/>）全程持
/// <c>s_allLock</c>，写操作经锁串行、终态一致；三容器在同一临界区内<b>顺序发布</b>（不承诺
/// "同一时刻原子可见"）。</para>
/// <para>C-3 锚门负缓存零分配——<see cref="RuntimeAnchorGate.HasAnchor"/>命中负缓存
/// （未注册且无 [MapFrom]）时零堆分配、零锁；命中注册表腿或正缓存腿时零锁（各构造一次
/// <see cref="MappingMetaView"/>，消除手段登记）。</para>
/// </remarks>
public static class RuntimeSnapshotStore
{
    // （-A）：内层 List → 不可变数组（写侧锁内整替换，读侧无锁枚举）；
    // s_all 改数组整替换（Volatile 发布/读取——C-1 完整历史快照语义）。
    private static readonly ConcurrentDictionary<Type, RuntimeMapperMetadata[]> s_byTarget = new();
    private static readonly ConcurrentDictionary<(Type Source, Type Target), RuntimeMapperMetadata> s_byPair = new();
    private static readonly object s_allLock = new();
    private static RuntimeMapperMetadata[] s_all = Array.Empty<RuntimeMapperMetadata>();

    /// <summary>
    /// 反射腿 [MapFrom] 探针条目（正/负结果缓存——CWT TValue 须引用类型）。
    /// <para><see cref="Attr"/> = null 表示负结果（无 [MapFrom]）；<see cref="IsDto"/> 等价
    /// <c>Attr != null</c>（[MapFrom] 存在性）。</para>
    /// </summary>
    private sealed class MapFromProbeEntry
    {
        public MapFromAttribute? Attr;
        public bool IsDto;
    }

    // M-14-B：反射腿正/负结果缓存（CWT 弱键——键不阻止 collectible ALC 类型回收；
    // 同一 Type 至多一次 GetCustomAttribute 反射）。清理由静态构造登记委托承接。
    private static readonly ConditionalWeakTable<Type, MapFromProbeEntry> s_mapFromProbe = new();

    /// <summary>
    /// 反射腿 [MapNoAutoMatch] 探针条目
    /// CWT TValue 须引用类型，故装箱为条目类；<see cref="Has"/> = 该类型（含基类链，inherit:true）
    /// 是否标注 [MapNoAutoMatch]。**独立缓存，禁止复用 <see cref="s_mapFromProbe"/>**（后者为
    /// [MapFrom] 专用条目，语义不同）。
    /// </summary>
    private sealed class NoAutoMatchProbeEntry
    {
        public bool Has;
    }

    // 白名单探针缓存（CWT 弱键，登记名 runtime-noautomatch-probe，
    // CacheLayer.Full，随 Mapper.ClearCache 全链清理——沿 s_mapFromProbe 模式；
    // 禁裸 IsDefined 无缓存调用，C-3 口径 RuntimeAnchorGate.cs:23-31）。
    private static readonly ConditionalWeakTable<Type, NoAutoMatchProbeEntry> s_noAutoMatchProbe = new();

    /// <summary>
    /// CacheRegistry 登记制自注册：仅登记清理委托，
    /// 不触发缓存构建（防自触发，同 PropertyMatcher/NavigationTreeCollector 口径）。
    /// </summary>
    static RuntimeSnapshotStore()
    {
        CacheRegistry.Register("runtime-snapshot-probe", CacheLayer.Full, static () => s_mapFromProbe.Clear());
        // 白名单探针独立登记（清理链完整性——#145 同族跨用例串扰防线）
        CacheRegistry.Register("runtime-noautomatch-probe", CacheLayer.Full, static () => s_noAutoMatchProbe.Clear());
    }

    /// <summary>
    /// 注册单条元数据（生成代码 [ModuleInitializer] 调用；线程安全）
    /// </summary>
    /// <param name="metadata">运行时元数据（SourceType/TargetType 不可为 null）</param>
    /// <exception cref="ArgumentNullException">metadata 或其 SourceType/TargetType 为 null</exception>
    /// <remarks>
    /// <para>幂等合并契约（双写合并——复审 R-B）：
    /// 同 (Source, Target) 键重复登记时按「明细优先」合并——明细条目（身份段之后发射的
    /// 明细段条目）补全/覆盖身份条目，身份条目不覆盖已登记明细；与 ModuleInitializer
    /// 跨文件执行顺序无关（先身份后明细 / 先明细后身份 / 同内容重复登记终态一致）。</para>
    /// <para>手工注册的属性明细（如脱敏类型名声明）由内存映射决策链消费：特性腿优先，
    /// 注册表腿在特性腿未命中时补位，同一属性不双重施加。</para>
    /// </remarks>
    public static void Register(RuntimeMapperMetadata metadata)
    {
        if (metadata == null) throw new ArgumentNullException(nameof(metadata));
        if (metadata.SourceType == null) throw new ArgumentNullException(nameof(metadata), "Metadata.SourceType is null.");
        if (metadata.TargetType == null) throw new ArgumentNullException(nameof(metadata), "Metadata.TargetType is null.");

        // 审计 GetOrAdd 与 Add 原非原子（并发 Register 同 Target 理论丢条目）。
        // 全部写入纳入 s_allLock 串行化——s_all/s_byTarget/s_byPair 三写原子，强化 public API 并发契约。
        // 三容器改"不可变数组 + 整替换"——锁内读-改-写（无并发竞争），
        // s_all 经 Volatile.Write 发布（读侧 Volatile.Read 取完整历史快照，C-1）。
        lock (s_allLock)
        {
            // D1 运行时同对双声明门（——与 NCMAP_F11 严格同构；
            // 用户 裁定收口）。锁内检测：臂二需读 s_byPair 与并发 Register 串行化；
            // 标准模式生成路径经 F11 前置拦截恒零命中（硬断言测试保护），实际触发面=纯运行时/手工注册非法形态
            ThrowIfDualDeclarationConflict(metadata);

            var key = (metadata.SourceType, metadata.TargetType);
            if (s_byPair.TryGetValue(key, out var existing))
            {
                // 幂等合并（R-B）：终态只保留一条合并后的条目（明细优先）
                var merged = MergeMetadata(existing, metadata);
                if (ReferenceEquals(merged, existing)) return;

                ReplaceEntry(existing, merged);
                s_byPair[key] = merged;
                // ContentVersion 递增（合并替换路径， 递增面）
                Interlocked.Increment(ref s_contentVersion);
                return;
            }

            // s_all 整替换：复制旧数组并追加（原 s_all.Add 原地改写已废除——读侧去锁前提）
            var newAll = new RuntimeMapperMetadata[s_all.Length + 1];
            Array.Copy(s_all, newAll, s_all.Length);
            newAll[s_all.Length] = metadata;
            Volatile.Write(ref s_all, newAll);

            // s_byTarget 内层数组整替换（AddOrUpdate 工厂仅作形态适配——锁内无竞争）
            s_byTarget.AddOrUpdate(
                metadata.TargetType,
                _ => new[] { metadata },
                (_, old) =>
                {
                    var arr = new RuntimeMapperMetadata[old.Length + 1];
                    Array.Copy(old, arr, old.Length);
                    arr[old.Length] = metadata;
                    return arr;
                });
            s_byPair[key] = metadata;
            // ContentVersion 递增（新条目注册路径—— 递增面）
            Interlocked.Increment(ref s_contentVersion);
        }
    }

    /// <summary>
    /// 全量清单（候选 c 消费面：枚举/扫描；测试与内部诊断用）
    /// </summary>
    /// <remarks>
    /// 读侧无锁——<see cref="Volatile.Read"/> 取当前不可变数组引用直接返回
    /// ；并发写期间读到某一完整历史快照。
    /// </remarks>
    internal static IReadOnlyList<RuntimeMapperMetadata> All
    {
        get { return Volatile.Read(ref s_all); }
    }

    /// <summary>
    /// 按目标类型查询全部声明（多源场景返回多条）
    /// </summary>
    /// <param name="targetType">目标（DTO）类型</param>
    /// <returns>该目标类型的全部注册条目（无注册时为空集）。读侧无锁
    /// 返回内层不可变数组引用本身（定案无防御性拷贝；调用方不得下行转换改写）</returns>
    public static IReadOnlyList<RuntimeMapperMetadata> FindByTarget(Type targetType)
    {
        return s_byTarget.TryGetValue(targetType, out var arr)
            ? arr
            : Array.Empty<RuntimeMapperMetadata>();
    }

    /// <summary>
    /// 按 (源, 目标) 对查询
    /// </summary>
    /// <param name="sourceType">源类型</param>
    /// <param name="targetType">目标类型</param>
    /// <returns>命中的元数据；未注册时为 null</returns>
    public static RuntimeMapperMetadata? FindByPair(Type sourceType, Type targetType)
        => s_byPair.TryGetValue((sourceType, targetType), out var meta) ? meta : null;

    /// <summary>
    /// 按 (源, 目标) 精确对查询映射条目 —— 投影/降级检测统一取数入口（
    /// 三态契约）。
    /// 特性对与 Fluent 对统一命中（不区分 Channel）；miss = 纯运行时模式 / 未注册对。
    /// <para><b>三态语义</b>：调用方须先判"明细可达"再分流
    /// ① 明细命中：条目存在且 <c>meta.Properties.Length &gt; 0</c>（判据对齐
    /// <see cref="TryGetFluentDetailMeta"/> 既有 on 态门，逐字同口径）；
    /// ② 条目命中而明细不可达：显式 off 态 Fluent 身份键（零明细字段；Properties 空的其他形态
    /// 保守同判）；③ miss：无条目。
    /// [MapFrom] 通道无身份条目（EmitIdentity 仅过滤 IsFluentForward），产物中特性对恒为明细形态。</para>
    /// <para>internal：消费方全为库内 + InternalsVisibleTo 友元，不进公共 API 面。
    /// 消费端分流表（投影主链 / 降级检测器 × 三态）见 ——off 态 Fluent 对投影
    /// 维持强制降级（reason 文本逐字保持，B9），不放宽。</para>
    /// </summary>
    /// <param name="sourceType">源（Entity）类型</param>
    /// <param name="targetType">目标（DTO）类型</param>
    /// <param name="meta">命中条目（返回 false 时为 null）</param>
    /// <returns>条目存在时为 true（明细可达性由调用方按 <c>meta.Properties.Length</c> 判定）</returns>
    internal static bool TryGetMappingDetail(Type sourceType, Type targetType,
        [MaybeNullWhen(false)] out RuntimeMapperMetadata meta)
        => s_byPair.TryGetValue((sourceType, targetType), out meta);

    /// <summary>
    /// 嵌套/集合元素 DTO 判定 —— 等价重建 <c>type.IsDefined(typeof(MapFromAttribute))</c>
    /// （Fluent DTO 同命中，消除降级漏判缺口）
    /// </summary>
    /// <param name="type">待判定类型</param>
    /// <returns>已注册为映射 DTO 时为 true</returns>
    public static bool IsRegisteredDto(Type type)
        => s_byTarget.ContainsKey(type);

    /// <summary>
    /// 主源类型查询 —— 等价重建 <c>GetCustomAttribute&lt;MapFromAttribute&gt;().FromSource</c>
    /// </summary>
    /// <param name="dtoType">目标（DTO）类型</param>
    /// <returns>主源类型；未注册时为 null</returns>
    public static Type? GetPrimarySource(Type dtoType)
        => GetPrimaryMeta(dtoType)?.SourceType;

    /// <summary>
    /// 树构建配置查询（门面）
    /// TreeBuilder/IsTreeChildrenSlot/EFCore 树挂载位改读入口
    /// </summary>
    /// <param name="dtoType">目标（DTO）类型</param>
    /// <returns>树构建配置；未注册或未启用树语义（TreeConfig=null，opt-in）时为 null
    /// 消费端对 null 不得取默认三元组</returns>
    public static RuntimeTreeConfig? GetTreeConfig(Type dtoType)
        => GetPrimaryMeta(dtoType)?.TreeConfig;

    /// <summary>
    /// 属性级 NoAutoInclude 查询（门面）
    /// NavigationTreeCollector 导航收集跳过判定改读入口
    /// </summary>
    /// <param name="dtoType">目标（DTO）类型</param>
    /// <param name="propertyName">目标属性名</param>
    /// <returns>注册表面（Fluent .NoAutoInclude(dest) 对级数组承载 +
    /// [MapNoAutoInclude]/Fluent pm 级发射承载，OR 语义）命中该属性时为 true；
    /// 未注册或未命中时为 false——消费端对 false 回退反射特性判定</returns>
    public static bool IsPropertyNoAutoInclude(Type dtoType, string propertyName)
    {
        // 读侧无锁——内层已改不可变数组（写侧整替换），锁外 foreach 无撕裂面
        if (!s_byTarget.TryGetValue(dtoType, out var arr)) return false;
        foreach (var meta in arr)
        {
            // 对级数组承载（Fluent 属性级声明——收集器消费面为全部可写属性，pm 无关）
            var targets = meta.PropertyNoAutoIncludeTargets;
            if (targets is { Length: > 0 })
            {
                foreach (var t in targets)
                {
                    if (t == propertyName) return true;
                }
            }
            // pm 级承载（特性侧 [MapNoAutoInclude] / Fluent pm 命中场景—— 既有发射面）
            foreach (var p in meta.Properties)
            {
                if (p.IsPropertyNoAutoInclude && p.Target?.Name == propertyName) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 对级白名单开关查询（门面）—— **方向无关**判定：
    /// 该 (S,T) 对是否禁用自动匹配（未声明同名配对 / 配置感知补匹配 / 自动扁平化三样）。
    /// <para><b>两腿口径</b>：①注册表腿——<b>无序对探测 + 按对校验</b>：<c>s_byPair[(S,T)]</c> 与
    /// <c>s_byPair[(T,S)]</c> 两次查找（键容器先例 :45/:161），命中条目 <c>NoAutoInclude=true</c> 即为真
    ///（<see cref="MergeMetadata"/> OR 臂已保证同对多条注册合并后对级位 = 任一为真）；
    /// <b>不得经 <c>GetPrimaryMeta</c> 盲取主条目</b>（:364-375 仅按 Channel/Fqn 序取首，与本对无关联），
    /// 亦不得由 <c>sourceIsDefine/targetIsDefine</c> 推导方向（Fluent 声明对两侧均无 [MapFrom]，判据不可用）。
    /// 开工探针定案：生成期<b>不发射</b> (Dto,Entity) 反向注册条目
    ///（<c>RuntimeSnapshotGenerator</c> 仅 Emit(merged.Mappings)），故本腿以无序对探测为唯一实现口径，
    /// 反向调用经正向条目 <c>(Entity,Dto)</c> 命中。</para>
    /// <para>②纯反射腿——承载侧（任一侧）<c>IsDefined(typeof(MapNoAutoMatchAttribute), inherit: true)</c>
    ///（特性锚对必有一侧带 [MapFrom]，无方向歧义；D5③ 各腿一律 inherit:true），经独立探针缓存
    /// <c>s_noAutoMatchProbe</c>（登记名 <c>runtime-noautomatch-probe</c>，CacheLayer.Full，随
    /// <c>Mapper.ClearCache()</c>全链清理；不复用<c>s_mapFromProbe</c>——C-3 口径禁裸反射无缓存）。</para>
    /// </summary>
    /// <param name="sourceType">源类型（正向 = 实体，反向调用 = DTO）</param>
    /// <param name="targetType">目标类型（正向 = DTO，反向调用 = 实体）</param>
    /// <returns>该对开启白名单时为 true；未注册且两侧均无标注时为 false（现状行为，零行为变化）</returns>
    public static bool IsNoAutoMatch(Type sourceType, Type targetType)
    {
        if (sourceType == null || targetType == null) return false;

        // ① 注册表腿：无序对两次查找 + 按对校验（键 = (Source, Target)，读侧无锁——C-1 前提：
        // s_byPair 为 ConcurrentDictionary，条目不可变（合并经整替换））
        if (ProbePairNoAutoInclude(sourceType, targetType) || ProbePairNoAutoInclude(targetType, sourceType))
            return true;

        // ② 纯反射腿：任一侧具 [MapNoAutoMatch]（inherit:true，沿基类链——与符号提取腿同口径）
        return ProbeNoAutoMatchAttribute(sourceType) || ProbeNoAutoMatchAttribute(targetType);
    }

    /// <summary>注册表腿单对探测（<see cref="IsNoAutoMatch"/> ①腿）——按 (Source,Target) 精确取条目读对级位。</summary>
    private static bool ProbePairNoAutoInclude(Type sourceType, Type targetType)
        => s_byPair.TryGetValue((sourceType, targetType), out var meta) && meta.NoAutoInclude;

    /// <summary>
    /// 纯反射腿 [MapNoAutoMatch] 探针单点（<see cref="IsNoAutoMatch"/> ②腿）——CWT 弱键正/负缓存，
    /// 同一 Type 至多一次 <c>IsDefined</c> 反射（并发首查下工厂可重入、赢者入表，终态一致）。
    /// </summary>
    private static bool ProbeNoAutoMatchAttribute(Type type)
        => s_noAutoMatchProbe.GetValue(type, static t =>
            new NoAutoMatchProbeEntry { Has = t.IsDefined(typeof(MapNoAutoMatchAttribute), inherit: true) }).Has;

    /// <summary>
    /// Fluent 声明 DTO 判定（门面）
    /// G1-① 降级兜底强制降级判定入口
    /// </summary>
    /// <param name="dtoType">目标（DTO）类型</param>
    /// <returns>该类型存在 Channel=Fluent 的注册条目时为 true
    /// （显式 off 态身份登记键 / 默认 on 态明细条目均命中）</returns>
    public static bool IsFluentRegisteredDto(Type dtoType)
    {
        // 读侧无锁（内层不可变数组，写侧整替换）
        if (!s_byTarget.TryGetValue(dtoType, out var arr)) return false;
        foreach (var meta in arr)
        {
            if (meta.Channel == RuntimeMappingChannel.Fluent) return true;
        }
        return false;
    }

    /// <summary>
    /// Fluent on 态明细条目查询（门面， G1-②）
    /// 降级检测声明级分流（不可翻译声明判定）与投影绑定明细消费的取数入口
    /// </summary>
    /// <param name="dtoType">目标（DTO）类型</param>
    /// <param name="meta">Fluent 通道 on 态明细条目（Properties 非空）</param>
    /// <returns>命中 on 态明细条目（Channel=Fluent 且承载属性明细）时为 true；
    /// 未注册 / off 态身份键（Properties 空，明细不可达）时为 false——消费端对 false
    /// 维持保守降级语义（同族，不放宽）</returns>
    public static bool TryGetFluentDetailMeta(Type dtoType, [MaybeNullWhen(false)] out RuntimeMapperMetadata meta)
    {
        // 读侧无锁（内层不可变数组，写侧整替换）
        meta = null;
        if (!s_byTarget.TryGetValue(dtoType, out var arr)) return false;
        foreach (var m in arr)
        {
            if (m.Channel == RuntimeMappingChannel.Fluent && m.Properties.Length > 0)
            {
                meta = m;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 统一查询门面—— 消费端统一调用此方法，内部完成
    /// 「注册表优先 + 反射回退 + 特性实例语义缺口处理」，
    /// 改造点从各自 if/else 双读收敛为一次门面调用 + 读 <see cref="MappingMetaView"/> 字段
    /// </summary>
    /// <param name="type">待查询类型（通常为目标 DTO 类型）</param>
    /// <param name="view">统一查询视图（注册表命中或反射 [MapFrom] 命中时非 null）</param>
    /// <returns>是映射 DTO（注册表命中或反射 [MapFrom] 命中）时为 true</returns>
    public static bool TryGetMappingMeta(Type type, [MaybeNullWhen(false)] out MappingMetaView view)
    {
        // 注册表优先（on 热路径）
        var meta = GetPrimaryMeta(type);
        if (meta != null)
        {
            view = new MappingMetaView
            {
                IsDto = true,
                PrimarySource = meta.SourceType,
                MapFromAttribute = GetCachedMapFromAttribute(meta),
                IsRegistered = true,
                Metadata = meta,
            };
            return true;
        }

        // 反射回退（off 兜底——与改造前现状路径一致）。
        // 正/负结果 CWT 弱键缓存——同一 Type 至多一次 GetCustomAttribute 反射
        //（单线程语义；并发首查下 CWT GetValue 工厂可重入多次、赢者入表，终态一致无危害）；
        // 负结果（无 [MapFrom]）缓存命中时 view=null 早返回（负缓存腿零分配零锁）。
        // 动态类型（collectible ALC）经弱键承载，不阻止其回收（三分法第一行）。
        var probe = ProbeMapFrom(type);
        if (probe.IsDto)
        {
            view = new MappingMetaView
            {
                IsDto = true,
                PrimarySource = probe.Attr!.FromSource,
                MapFromAttribute = probe.Attr,
                IsRegistered = false,
                Metadata = null,
            };
            return true;
        }

        view = null;
        return false;
    }

    /// <summary>
    /// 反射腿 [MapFrom] 探针单点（M-14-B CWT 正/负缓存家族——<see cref="TryGetMappingMeta"/>
    /// 反射回退腿与 <see cref="IsMapFromDeclared"/> 共享同一缓存与同一反射调用点，零新增反射路径）。
    /// </summary>
    private static MapFromProbeEntry ProbeMapFrom(Type type)
        => s_mapFromProbe.GetValue(type, static t =>
        {
            var a = t.GetCustomAttribute<MapFromAttribute>();
            return new MapFromProbeEntry { Attr = a, IsDto = a != null };
        });

    /// <summary>
    /// **特性锚**判定——"读特性"准入专用：
    /// 类型是否具 <c>[MapFrom]</c> 声明（**仅反射腿**，不含注册表腿）。
    /// <para>与 <see cref="TryGetMappingMeta"/>（双锚：注册表命中 ∨ 反射腿）职责分离：
    /// 映射资格判定继续走 <c>RuntimeAnchorGate.HasAnchor</c> 双锚语义；属性级映射配置标注的
    /// 运行时消费准入自本册起收紧为特性锚（Fluent 对按快照明细执行，不再消费属性级标注——
    /// 007 错误报告四组实证的双引擎分叉收口）。</para>
    /// <para>缓存口径：复用 <see cref="s_mapFromProbe"/> CWT 正/负缓存（同一 Type 至多一次
    /// <c>GetCustomAttribute&lt;MapFromAttribute&gt;</c>负缓存零分配零锁——C-3）；失效随
    /// <c>Mapper.ClearCache()</c> 全链清理（登记名 <c>runtime-snapshot-probe</c> 机制不变）。
    /// internal 成员，不进公共 API 面（PublicApi 五基线零 diff 判据）。</para>
    /// </summary>
    /// <param name="type">待判定类型（属性 DeclaringType）</param>
    /// <returns>true = 具 [MapFrom] 特性锚（属性级标注可消费）；false = 无特性锚（不消费）</returns>
    internal static bool IsMapFromDeclared(Type type)
        => type != null && ProbeMapFrom(type).IsDto;

    /// <summary>
    /// 测试隔离：清空注册表
    /// </summary>
    public static void Clear()
    {
        // 三清整体入锁——原 s_all 在锁内、byTarget/byPair
        // 在锁外的分清形态与并发 Register 交错会产生孤儿条目（s_all 有而 byPair/byTarget 无，
        // 三容器不一致）；整体入锁后与 Register 串行化，终态一致。
        // s_all 清空改数组整替换（写侧锁内，C-2 写原子保持）。
        lock (s_allLock)
        {
            Volatile.Write(ref s_all, Array.Empty<RuntimeMapperMetadata>());
            s_byTarget.Clear();
            s_byPair.Clear();
            // ContentVersion 递增（Clear 入口， 递增面：
            // 漏 Clear 臂 = Clear 后 s_cache 旧条目版本仍"匹配"→ 陈旧表达式继续命中）
            Interlocked.Increment(ref s_contentVersion);
        }
    }

    /// <summary>
    /// 注册表内容版本——Register（新条目/合并替换路径）
    /// 与 Clear 每次内容变更 Interlocked.Increment；读侧 Volatile.Read（32 位运行时防 long 撕裂）。
    /// <para>internal：消费方全为库内 + InternalsVisibleTo 友元，不进公共 API 面。
    /// 消费方：① 投影缓存条目校验（条目化落地）；② 降级/agnostic 缓存条目校验
    /// （与 Mapper.Pipeline.Version 双条件—— 定案：ContentVersion 与 Pipeline.Version 分立，
    /// 投影缓存仅校验 ContentVersion；agnostic 通道已双条件）。
    /// 先例 DegradationCacheEntry.Version（条目内字段比对，非键成分——避免版本维度无界沉淀）。</para>
    /// </summary>
    internal static long ContentVersion => Volatile.Read(ref s_contentVersion);

    /// <summary>ContentVersion 存储位（Interlocked 写侧 / Volatile 读侧）</summary>
    private static long s_contentVersion;

    /// <summary>
    /// D1 运行时同对双声明门（与 NCMAP_F11 严格同构，
    /// [ModuleInitializer] 生成路径经 F11 前置拦截恒零命中，触发面=纯运行时/手工注册）。
    /// <para>两臂合拢覆盖任意登记顺序：臂一闭"锚在、Fluent 明细后注册"态；臂二闭"异通道条目先在"态。</para>
    /// <para>边界（零误伤—— D1 边界 + D3 用例）：跨对并存放行（收窄为同向对）；
    /// 同对非明细形态放行（分叉面只在明细形态）；单通道/同通道合并不受影响；
    /// 手工模拟 MapFrom 通道不触发臂一（要求 Channel==Fluent）。</para>
    /// </summary>
    private static void ThrowIfDualDeclarationConflict(RuntimeMapperMetadata metadata)
    {
        // 臂一（锚探测——收窄判据）：Fluent 明细形态 + 目标类型具 [MapFrom(S)] 锚且锚对同向
        // （归档复查 修：判据随 明细可达扩容同步——Properties 空 + ComposeMappings 非空的
        // 同向对同样触发 D1（原仅 Properties.Length>0 时该形态绕门——正是本门要封死的分叉形态））
        if (metadata.Channel == RuntimeMappingChannel.Fluent
            && (metadata.Properties.Length > 0 || metadata.ComposeMappings.Length > 0))
        {
            var probe = ProbeMapFrom(metadata.TargetType);
            if (probe.IsDto && Equals(probe.Attr!.FromSource, metadata.SourceType))
            {
                throw new MapConfigurationException(
                    $"同对 ({metadata.SourceType.FullName}, {metadata.TargetType.FullName}) 双声明冲突：" +
                    "同时存在 [MapFrom] 声明与 Fluent 明细注册（同向对/明细形态判定，运行时镜像 NCMAP_F11——" +
                    "检索标识 NCMAP_D1），违反通道互斥禁止规则；请在两处之一移除声明");
            }
        }

        // 臂二（异通道条目探测——双向）：该 (S,T) 对已存在异通道注册
        if (s_byPair.TryGetValue((metadata.SourceType, metadata.TargetType), out var existing)
            && existing.Channel != metadata.Channel)
        {
            throw new MapConfigurationException(
                $"同对 ({metadata.SourceType.FullName}, {metadata.TargetType.FullName}) 双声明冲突：" +
                "同时存在 [MapFrom] 声明与 Fluent 注册（异通道条目已在注册表——运行时镜像 NCMAP_F11——" +
                "检索标识 NCMAP_D1），违反通道互斥禁止规则；请在两处之一移除声明");
        }
    }

    /// <summary>
    /// 主源条目选取（规则）—— Channel=MapFrom 优先；同通道按 SourceTypeFqn 序取首
    /// </summary>
    /// <param name="targetType">目标（DTO）类型</param>
    /// <returns>主源条目；未注册时为 null</returns>
    private static RuntimeMapperMetadata? GetPrimaryMeta(Type targetType)
    {
        // 读侧无锁——内层不可变数组上做同样的 ComparePrimary 遍历
        //（原 A3-06 锁内索引遍历逻辑不变，仅去锁 + List → 数组）
        if (!s_byTarget.TryGetValue(targetType, out var arr) || arr.Length == 0) return null;
        var best = arr[0];
        for (var i = 1; i < arr.Length; i++)
        {
            if (ComparePrimary(arr[i], best) < 0) best = arr[i];
        }
        return best;
    }

    /// <summary>主源条目排序比较（Channel=MapFrom 优先，同通道按 SourceTypeFqn 序）</summary>
    private static int ComparePrimary(RuntimeMapperMetadata x, RuntimeMapperMetadata y)
    {
        var channelCompare = ((int)x.Channel).CompareTo((int)y.Channel);
        if (channelCompare != 0) return channelCompare;
        return string.CompareOrdinal(x.SourceTypeFqn, y.SourceTypeFqn);
    }

    /// <summary>
    /// 注册表条目的 [MapFrom] 特性实例缓存读取
    /// 发射时物化回填至信号槽（Fluent DTO 全槽 null），此处仅透传不反射
    /// </summary>
    private static MapFromAttribute? GetCachedMapFromAttribute(RuntimeMapperMetadata meta)
    {
        foreach (var slot in meta.AllTargetSlots)
        {
            if (slot.HasMapFromAttribute) return slot.MapFromAttribute;
        }
        return null;
    }

    // ---- 幂等合并内核（R-B；调用方持有 s_allLock）----

    /// <summary>既有条目在三容器中的整替换（s_all/s_byTarget/s_byPair 终态各恰一条）。
    /// 原"原地下标改写"改为"复制新数组后整替换"——读侧无锁的前提：
    /// 不可变数组一经发布永不原位变更，读侧枚举无撕裂面。</summary>
    private static void ReplaceEntry(RuntimeMapperMetadata existing, RuntimeMapperMetadata merged)
    {
        // s_all 整替换（写侧锁内，Volatile.Write 发布）
        var oldAll = s_all;
        var newAll = new RuntimeMapperMetadata[oldAll.Length];
        for (var i = 0; i < oldAll.Length; i++)
        {
            newAll[i] = ReferenceEquals(oldAll[i], existing) ? merged : oldAll[i];
        }
        Volatile.Write(ref s_all, newAll);

        // s_byTarget 内层数组整替换（锁内读-改-写，无并发竞争）
        if (s_byTarget.TryGetValue(existing.TargetType, out var arr))
        {
            var newArr = new RuntimeMapperMetadata[arr.Length];
            for (var i = 0; i < arr.Length; i++)
            {
                newArr[i] = ReferenceEquals(arr[i], existing) ? merged : arr[i];
            }
            s_byTarget[existing.TargetType] = newArr;
        }
    }

    /// <summary>
    /// 「明细优先」合并（R-B 顺序无关）：身份条目（Properties 空 + 明细字段默认）不覆盖已登记明细；
    /// 明细条目补全身份条目。两侧均明细（发射端 F16 抑制保证不出现的退化形态）按字段并集确定性合并。
    /// </summary>
    private static RuntimeMapperMetadata MergeMetadata(RuntimeMapperMetadata a, RuntimeMapperMetadata b)
    {
        // 通道冲突（F11 仲裁排除的形态）确定性取舍：MapFrom 优先（与主源选取 规则同向）
        var channel = a.Channel == b.Channel
            ? a.Channel
            : (a.Channel == RuntimeMappingChannel.MapFrom ? a.Channel : b.Channel);

        var merged = new RuntimeMapperMetadata
        {
            SourceType = a.SourceType,
            TargetType = a.TargetType,
            SourceTypeFqn = a.SourceTypeFqn.Length > 0 ? a.SourceTypeFqn : b.SourceTypeFqn,
            TargetTypeFqn = a.TargetTypeFqn.Length > 0 ? a.TargetTypeFqn : b.TargetTypeFqn,
            Channel = channel,
            NoAutoInclude = a.NoAutoInclude || b.NoAutoInclude,
            IsFluentForward = a.IsFluentForward || b.IsFluentForward,
            AfterMappingMethodRef = a.AfterMappingMethodRef ?? b.AfterMappingMethodRef,
            // 前置钩子合并臂（同 a ?? b 形态——运行时元数据合并，
            // 不承担生成期跨对传播， 限定）
            BeforeMappingMethodRef = a.BeforeMappingMethodRef ?? b.BeforeMappingMethodRef,
            TreeConfig = a.TreeConfig ?? b.TreeConfig,
            PropertyNoAutoIncludeTargets = a.PropertyNoAutoIncludeTargets is { Length: > 0 }
                ? a.PropertyNoAutoIncludeTargets
                : b.PropertyNoAutoIncludeTargets,
            // 多态分派合并臂（|| 形态——混合场景声明静默丢失防线，
            // F5 模式；漏加 = [MapFrom] 身份条目 + Fluent 明细条目合并时 HasSubclassDispatch 丢失）
            HasSubclassDispatch = a.HasSubclassDispatch || b.HasSubclassDispatch,
            // Compose 多源声明合并臂（|| 形态——同 F5 模式；
            // 漏加 = Fluent Compose 多源 DTO 双笔登记合并时 HasComposeMappings 丢失，
            // DegradationDetectorBase "Compose" 降级分类恒失效）
            HasComposeMappings = a.HasComposeMappings || b.HasComposeMappings,
            // Compose 明细合并臂——漏加 = 双笔登记
            //（身份条目 + 明细条目）合并时 ComposeMappings 丢失（A3-01/F5 模式同族）
            ComposeMappings = MergeComposeMappings(a.ComposeMappings, b.ComposeMappings),
            Properties = MergeProperties(a.Properties, b.Properties),
            AllTargetSlots = a.Properties.Length == 0 ? b.AllTargetSlots
                : b.Properties.Length == 0 ? a.AllTargetSlots
                : a.AllTargetSlots,
        };

        // 逐字段等价短路：
        // 合并结果与 a 语义等价时返回 a 本身，使调用方 ReferenceEquals(merged, existing) 短路成立，
        // 从而「同内容重复注册」不递增 ContentVersion、不触发无谓的 ReplaceEntry 与 s_all 重建。
        // 若无此短路，合并恒产新实例，上述 ReferenceEquals 判断将恒为 false 而失效。
        return IsMergeEquivalentToFirst(a, merged) ? a : merged;
    }

    /// <summary>
    /// 判定合并结果 <paramref name="merged"/> 是否与首个入参 <paramref name="a"/> 语义等价。
    /// <para>逐字段覆盖 <see cref="MergeMetadata"/> 产出的全部字段；比较口径＝该字段合并语义的"是否被 b 改变"，
    /// 即 merged 与 a 在该字段上是否引用/值相同。</para>
    /// <para>相等则合并无信息增益（b 未带来任何新内容），可安全返回 a 复用实例。</para>
    /// </summary>
    private static bool IsMergeEquivalentToFirst(RuntimeMapperMetadata a, RuntimeMapperMetadata merged)
    {
        return ReferenceEquals(a, merged)
            || (a.SourceType == merged.SourceType
                && a.TargetType == merged.TargetType
                && string.Equals(a.SourceTypeFqn, merged.SourceTypeFqn, StringComparison.Ordinal)
                && string.Equals(a.TargetTypeFqn, merged.TargetTypeFqn, StringComparison.Ordinal)
                && a.Channel == merged.Channel
                && a.NoAutoInclude == merged.NoAutoInclude
                && a.IsFluentForward == merged.IsFluentForward
                && ReferenceEquals(a.AfterMappingMethodRef, merged.AfterMappingMethodRef)
                && ReferenceEquals(a.BeforeMappingMethodRef, merged.BeforeMappingMethodRef)
                && ReferenceEquals(a.TreeConfig, merged.TreeConfig)
                && ReferenceEquals(a.PropertyNoAutoIncludeTargets, merged.PropertyNoAutoIncludeTargets)
                && a.HasSubclassDispatch == merged.HasSubclassDispatch
                && a.HasComposeMappings == merged.HasComposeMappings
                && ReferenceEquals(a.ComposeMappings, merged.ComposeMappings)
                && ReferenceEquals(a.Properties, merged.Properties)
                && ReferenceEquals(a.AllTargetSlots, merged.AllTargetSlots));
    }

    /// <summary>
    /// Compose 明细合并（辅助方法）
    /// 按 (TargetPropertyName, SubSourceTypeName) 键去重、同键取 a（沿"非空胜/a 优先"既有口径，
    /// 与 HasMask 臂冲突处理一致——运行时臂只保"不丢"，编译期一致性由比较器/门禁负责）。
    /// </summary>
    private static RuntimeComposeMapping[] MergeComposeMappings(RuntimeComposeMapping[] a, RuntimeComposeMapping[] b)
    {
        if (a.Length == 0) return b;
        if (b.Length == 0) return a;

        // (string, string) 元组默认 EqualityComparer = 各分量 string 序数等价（StringComparer.Ordinal 同义）
        var seen = new HashSet<(string, string)>();
        var result = new List<RuntimeComposeMapping>(a.Length + b.Length);
        foreach (var cm in a)
        {
            if (seen.Add((cm.TargetPropertyName, cm.SubSourceTypeName))) result.Add(cm);
        }
        foreach (var cm in b)
        {
            if (seen.Add((cm.TargetPropertyName, cm.SubSourceTypeName))) result.Add(cm);
        }
        return result.ToArray();
    }

    /// <summary>属性映射对合并（按目标属性名；一侧为空集时整体取另一侧——身份/明细形态主路径）。</summary>
    private static RuntimePropertyMapping[] MergeProperties(RuntimePropertyMapping[] a, RuntimePropertyMapping[] b)
    {
        if (a.Length == 0) return b;
        if (b.Length == 0) return a;

        var byName = new Dictionary<string, RuntimePropertyMapping>(StringComparer.Ordinal);
        foreach (var p in a) byName[p.Target.Name] = p;
        foreach (var p in b)
        {
            byName[p.Target.Name] = byName.TryGetValue(p.Target.Name, out var existing)
                ? MergeProperty(existing, p)
                : p;
        }

        // 保持 a 的顺序在前、b 新增条目按序追加（发射端排序确定性前提下的稳定输出）
        var result = new List<RuntimePropertyMapping>(a.Length + b.Length);
        foreach (var p in a) result.Add(byName[p.Target.Name]);
        var seen = new HashSet<string>(a.Select(p => p.Target.Name), StringComparer.Ordinal);
        foreach (var p in b)
        {
            if (seen.Add(p.Target.Name)) result.Add(byName[p.Target.Name]);
        }
        return result.ToArray();
    }

    /// <summary>单属性映射对字段并集合并（明细优先：非空/true 胜；退化形态两侧均明细时字段级确定性并集）。</summary>
    private static RuntimePropertyMapping MergeProperty(RuntimePropertyMapping a, RuntimePropertyMapping b)
    {
        return new RuntimePropertyMapping
        {
            Target = a.Target,
            SourceName = a.SourceName ?? b.SourceName,
            SourcePathNames = a.SourcePathNames.Length > 0 ? a.SourcePathNames : b.SourcePathNames,
            SourcePropertyTypeName = a.SourcePropertyTypeName ?? b.SourcePropertyTypeName,
            SourcePropertyTypeRef = a.SourcePropertyTypeRef ?? b.SourcePropertyTypeRef,
            HasIndex = a.HasIndex || b.HasIndex,
            IsFlattened = a.IsFlattened || b.IsFlattened,
            IsReverseWriteBack = a.IsReverseWriteBack || b.IsReverseWriteBack,
            HasConverter = a.HasConverter || b.HasConverter,
            ConverterDeclaringType = a.ConverterDeclaringType ?? b.ConverterDeclaringType,
            ConverterMethodName = a.ConverterMethodName ?? b.ConverterMethodName,
            HasMask = a.HasMask || b.HasMask,
            MaskTypeName = a.MaskTypeName ?? b.MaskTypeName,
            // 格式化承载合并臂（a ?? b 形态，MaskTypeName 同款）
            // 漏臂则快照合并丢声明（降级判定读不到 Format 分类）
            FormatString = a.FormatString ?? b.FormatString,
            IsFromJson = a.IsFromJson || b.IsFromJson,
            IsPropertyNoAutoInclude = a.IsPropertyNoAutoInclude || b.IsPropertyNoAutoInclude,
            SourceIndexes = a.SourceIndexes ?? b.SourceIndexes,
            // 条件守卫合并臂（a ?? b 形态——SourceIgnoreDirection 同款；
            // ConditionShape 取 a 侧非零值（身份条目恒 0），与 ref 的伴随关系保持）
            ConditionMethodRef = a.ConditionMethodRef ?? b.ConditionMethodRef,
            ConditionShape = a.ConditionShape != 0 ? a.ConditionShape : b.ConditionShape,
        };
    }
}
