using System;
using System.Linq.Expressions;

namespace NCMapper;

/// <summary>
/// Fluent 入口配置根 — 用户在 [MapperConfig] 类的 <c>static void M(FluentMappingConfig cfg)</c>
/// 方法中调用
/// <para>与 [MapFrom] 特性通道并存（模式并存）：用户可对不同 (S,T) 类型对混合使用两通道；
/// 同一 (S,T) 对被两通道同时声明时触发 NCMAP_F11 互斥禁止。</para>
/// <para>本类仅作为编译期源生成器扫描的语法锚点——所有终端方法不做任何运行时工作，
/// 运行时映射由源生成器生成的 GeneratedMapper 承担（Configure 方法本身不会被调用）。</para>
/// </summary>
public sealed class FluentMappingConfig
{
    /// <summary>
    /// 声明 (Source, Target) 类型对，返回类型对级链式配置器继续配置
    /// </summary>
    /// <typeparam name="TSource">源类型</typeparam>
    /// <typeparam name="TTarget">目标类型</typeparam>
    /// <returns>类型对级链式配置器</returns>
    public FluentTypeConfig<TSource, TTarget> For<TSource, TTarget>() => new(this);
}

/// <summary>
/// 类型对级链式配置器 — 对级终端方法返回 this 实现链式调用
/// <para><b>演进红线（按 Fluent 字段链式换代口径修订，
/// 20261008-）</b>：字段级方法族随 20261008- 换代重定义（<see cref="Member{TProp}"/>
/// 三段式）；对级 NoAutoInclude 改名 <see cref="NoAutoMatch()"/>；"新增形态、不动既有签名"的
/// 演进纪律适用于换代后的新面；链式方法必须返回可继续链式的配置类型（覆盖
/// MemberHead / MemberConfig / AfterIgnore 的分段承载形态）。</para>
/// <para><b>继承契约（sealed 移除，20261008-）</b>：本类不再 sealed
/// <see cref="MemberConfig{TSource,TTarget,TProp}"/> / <see cref="AfterIgnore{TSource,TTarget}"/>
/// 继承本类实现"字段段配完后回到外层"（C# 继承硬性约束）；跨程序集继承仍被 internal 构造
/// 阻断，封闭性可控。</para>
/// </summary>
/// <typeparam name="TSource">源类型</typeparam>
/// <typeparam name="TTarget">目标类型</typeparam>
public class FluentTypeConfig<TSource, TTarget>
{
    /// <summary>
    /// 内部构造（仅经 <see cref="FluentMappingConfig.For{TSource,TTarget}"/> 创建）
    /// </summary>
    /// <param name="owner">入口配置根（链式锚点无运行时状态，仅为构造签名对称保留）</param>
    internal FluentTypeConfig(FluentMappingConfig owner)
    {
        _ = owner;
    }

    /// <summary>
    /// 字段链式入口（三段式第一段）— 锚定目标字段，返回字段"头"配置器继续该字段的声明/配置
    /// （字段名只写一次）
    /// <para><b>三段式形态</b>：本入口锚定字段（TProp 随后从字段段方法中消失，泛型参数少写）→
    /// <see cref="MemberHead{TSource,TTarget,TProp}"/> 头声明（From / FromSource / Ignore）
    /// 或直接进入配置段 → <see cref="MemberConfig{TSource,TTarget,TProp}"/> 配置段叠加变换/附加层
    /// （同字段多操作不再重复写字段名）→ MemberConfig 继承本类"回到外层"续下一个字段或对级方法。</para>
    /// <para><b>并存期注记</b>：既有字段级 25 签名（Map/Mask/Ignore 等 dest 首参形态）在换代
    /// 过渡期保留可用，与本入口解析产物同构（产物一致纪律）；新面提取器消费
    /// 适配属后续批次，落地前 <c>.Member</c> 链按未知终端处理（NCMAP_F60）。</para>
    /// <para>本方法与既有 Fluent 终端一致，仅作为编译期源生成器扫描的语法锚点——不做任何运行时工作。</para>
    /// </summary>
    /// <typeparam name="TProp">目标属性类型（dest lambda 推断；字段段内后续方法共享该锚定）</typeparam>
    /// <param name="dest">目标属性 lambda（单段纯属性路径——与既有字段级 dest 同限制）</param>
    /// <returns>字段"头"配置器（承载头声明与配置段入口；不继承对级——裸 Member 不可续链）</returns>
    public MemberHead<TSource, TTarget, TProp> Member<TProp>(
        Expression<Func<TTarget, TProp>> dest) => new();

    /// <summary>
    /// 显式构造器形参绑定（形态 1：源 lambda）
    /// 把源属性值喂给目标类型公共构造器的指定形参，解决"构造器形参不对应任何目标属性"
    ///（不可变带参构造类的纯 ctor 形参，无 <c>.Map</c> 可锚定、自动同名也绑不上）的绑定缺口。
    /// <para><b>锚定语义</b>：绑定经两点注入生成路径——①选器加权：绑定形参名命中的候选公共
    /// 构造器升权优先（多命中维持"参数最多优先"机械裁决）；②参数匹配优先：构造器实参
    /// 先查显式绑定表再走同名匹配。形参名须存在于至少一个候选公共构造器参数集
    ///（否则生成期报 NCMAP_F52）；与 <c>.Map</c> 显式条目指向同一 ctor 形参报
    /// NCMAP_F53 双声明矛盾（fail-fast）。</para>
    /// <para><b>泛型推断来源</b>：<typeparamref name="TSrcProp"/> 由 <paramref name="sourceProp"/>
    /// 源 lambda 返回类型推断（形参侧为字符串无推断位——泛型须显式声明在方法上）。
    /// 源 lambda 仅支持单段纯属性路径（<c>s =&gt; s.PropName</c>）；多段链/方法调用等非纯路径
    /// 沿用既有 lambda 诊断机制拒绝（不占新诊断码位）。</para>
    /// </summary>
    /// <typeparam name="TSrcProp">源属性类型（由源 lambda 返回类型推断）</typeparam>
    /// <param name="paramName">目标构造器形参名（与形参忽略大小写匹配；ByRef/指针形参不可绑定）</param>
    /// <param name="sourceProp">源属性 lambda（TSource 锚定单段纯属性路径）</param>
    /// <returns>this（链式）</returns>
    public FluentTypeConfig<TSource, TTarget> MapCtorParam<TSrcProp>(
        string paramName,
        Expression<Func<TSource, TSrcProp>> sourceProp) => this;

    /// <summary>
    /// 显式构造器形参绑定（形态 2：源属性名字符串）
    /// ——辅助形态，语义与 <see cref="MapCtorParam{TSrcProp}(string, System.Linq.Expressions.Expression{System.Func{TSource,TSrcProp}})"/>
    /// 一致，源属性以名字指定（适用于无类型上下文/动态场景）。
    /// <para>源属性名拼错或无公共 getter 时生成期报 NCMAP_F51（绑定源解析失败）。
    /// 不同构理由：形态 1 为编译期符号锚点主形态（lambda 符号直解析），
    /// 形态 2 仅字符串锚定辅助——两形态统一登记同一绑定面，物化后承载完全一致。</para>
    /// </summary>
    /// <param name="paramName">目标构造器形参名（与形参忽略大小写匹配；ByRef/指针形参不可绑定）</param>
    /// <param name="sourcePropertyName">源属性名（TSource 侧公共可读属性）</param>
    /// <returns>this（链式）</returns>
    public FluentTypeConfig<TSource, TTarget> MapCtorParam(
        string paramName,
        string sourcePropertyName) => this;

    /// <summary>
    /// 树构建配置 — 语义镜像特性侧 <c>[MapTree]</c>：
    /// 声明树挂载位三元组（主键/父键/子节点集合属性名），供 ToTreeList 树构建与
    /// 投影树挂载位判定（IsTreeChildrenSlot）消费。<b>与字段级 <c>MapTree(dest)</c> 语义辨析</b>：
    /// <c>MapTree(dest)</c> 声明"该属性递归映射"（属性级映射声明）；本终端配置"树按哪三个属性构建"（对级构建配置）——
    /// 名近义远，互补使用（递归映射属性通常即 Children 挂载位）。
    /// <para><b>opt-in 语义</b>：未调用本终端时注册表 TreeConfig=null，
    /// 树挂载位判定恒 false——不因"恰有同名 Children 自引用集合属性"而启用树语义（防双挂载缺口）。</para>
    /// <para><b>无参默认</b>：<c>Tree()</c> 等价 <c>Tree("Id", "ParentId", "Children")</c>，
    /// 与 <see cref="Attributes.MapTreeAttribute"/> 无参默认、<see cref="Runtime.RuntimeTreeConfig"/> init 默认三处一致。</para>
    /// </summary>
    /// <returns>this（链式）</returns>
    public FluentTypeConfig<TSource, TTarget> Tree() => this;

    /// <summary>
    /// 树构建配置（自定义三元组重载）— 语义同 <see cref="Tree()"/>，三个属性名均精确名查找
    ///（与 [MapTree] 特性侧同契约：Children 缺失/类型不符/只读时树构建抛 MapConfigurationException）
    /// </summary>
    /// <param name="idProperty">主键属性名</param>
    /// <param name="parentIdProperty">父键属性名</param>
    /// <param name="childrenProperty">子节点集合属性名</param>
    /// <returns>this（链式）</returns>
    public FluentTypeConfig<TSource, TTarget> Tree(string idProperty, string parentIdProperty, string childrenProperty) => this;

    /// <summary>
    /// 禁用该 (S,T) 对的自动匹配/自动包含 — 对级语义
    /// <para>对级语义：该 (S,T) 对不做自动匹配（含自动扁平化），仅映射显式声明/同名默认的属性；
    /// 与 [MapNoAutoInclude] 特性（属性级）语义粒度不同，双承载并行而非等价。</para>
    /// <para><b>特性通道等价物</b>：<see cref="NCMapper.Attributes.MapNoAutoMatchAttribute"/>
    /// ——类级标注，与本终端填同一承载位（<c>NoAutoInclude</c>），双通道语义逐项对等；
    /// 运行时消费入口 <c>RuntimeSnapshotStore.IsNoAutoMatch(Type, Type)</c>（方向无关）。</para>
    /// <para><b>反向派生语义差异</b>：反向映射基于正向显式声明逐条翻转——本开关场景下
    /// 反向映射仅覆盖显式声明的属性（[MapFrom] 通道自动匹配属性也参与反向，属可感知差异；
    /// 标注 [MapNoAutoMatch] 后特性通道同样收窄为仅翻转显式声明）。</para>
    /// </summary>
    /// <returns>this（链式）</returns>
    public FluentTypeConfig<TSource, TTarget> NoAutoMatch() => this;

    /// <summary>
    /// 后置回调 — 映射完成后执行（发射于共享核心方法末尾，6 正向形态继承；反向 4 形态不发射）
    /// <para><b>可见性契约</b>：handler 必须是可静态解析的方法组引用——目标方法须为
    /// <b>public 或 internal static</b>，且与生成代码（internal static class GeneratedMapper，同程序集）可见；
    /// private 误用触发 NCMAP_F12；lambda/闭包/捕获变量触发 NCMAP_F14。</para>
    /// </summary>
    /// <param name="handler">静态方法组引用（须 public/internal static + 同程序集可见）</param>
    /// <returns>this（链式）</returns>
    public FluentTypeConfig<TSource, TTarget> AfterMapping(Action<TSource, TTarget> handler) => this;

    /// <summary>
    /// 前置回调 — 映射执行前调用（发射于 Core/Update/MapListInto 站点首条属性赋值前；_Update 形态下
    /// dst 携带旧值可见）——语义镜像特性侧 <c>[BeforeMapping]</c>（双通道填充同一快照承载字段）
    /// <para><b>可见性契约</b>：handler 必须是可静态解析的方法组引用——目标方法须为
    /// <b>public 或 internal static</b>，且与生成代码（internal static class GeneratedMapper，同程序集）可见；
    /// private 误用触发 NCMAP_F35；lambda/闭包/捕获变量触发 NCMAP_F36。</para>
    /// <para><b>构造器一体化目标</b>（record init-only 全参构造等"构造与赋值不可分"形态）无插入点，
    /// 触发 NCMAP_F37；FromSource 块内声明触发 NCMAP_F38。</para>
    /// <para><b>投影行为</b>：声明本钩子的映射对在 ORM 投影下强制降级（与 [Convert] 等
    /// IProjectionPolicy 特性同口径——嵌套投影不执行生成代码，钩子丢失即语义错误）。</para>
    /// </summary>
    /// <param name="handler">静态方法组引用（须 public/internal static + 同程序集可见）</param>
    /// <returns>this（链式）</returns>
    public FluentTypeConfig<TSource, TTarget> BeforeMapping(Action<TSource, TTarget> handler) => this;

    /// <summary>
    /// 多态子类型对登记 — 为当前 (TSource, TTarget) 基类对显式登记一对子类型映射
    /// （对级终端——作用于整对，非属性级）。
    /// <para><b>分派语义</b>：编译期生成 isinst 分派链——基类映射运行时按<b>可赋值匹配</b>路由
    /// （<c>src is TSrcDerived</c>，含未登记的更深派生类型命中最近登记祖先层）；多对登记同时命中
    /// 按（源侧继承距离，目标侧继承距离）字典序<b>最派生优先</b>仲裁；未命中任何登记子类走基类兜底体
    /// （子类专有字段保持默认）。分派覆盖 Map/_Update/MapList/MapListInto 四族（Update/MapListInto
    /// 为双实例匹配——<c>src isinst &amp;&amp; dst isinst</c> 同时成立才路由，dst 固定型约束）。</para>
    /// <para><b>继承约束</b>：泛型约束调用点编译期拦截（CS0311/CS0314）；<c>TTargetDerived == TTarget</c>
    /// （源侧多态、目标收敛）为合法用例；<c>TSrcDerived == TSource</c>（登记基类自身）触发 NCMAP_F47 同族边界诊断。</para>
    /// <para><b>子类对生成</b>：登记即声明该子类型对参与基类分派——未独立声明的子类对按 (TSrcDerived, TTargetDerived)
    /// 独立生成（含子类专有属性 + 全族方法与注册）；已独立声明（同 FQN 二元组）则引用不重发。完全同对重复登记触发
    /// NCMAP_F47；部分重叠（同源异层/同源异目标）合法，按最派生优先仲裁不报。</para>
    /// <para><b>反向映射</b>：子类对经既有反向注册臂自动产出反向映射与反向直调重载（登记非单向）；
    /// 但<b>反向基类对不消费分派</b>（反向 dst 为调用方已存在实体，dst 固定型），
    /// 读/写不对称由 NCMAP_F50 Info 提供可见性。</para>
    /// <para><b>投影行为</b>：登记本对的基类映射在 ORM 投影下恒降级（isinst 分派链不可投影，
    /// 分类 SubclassDispatch）。</para>
    /// </summary>
    /// <typeparam name="TSrcDerived">子类源类型（须派生自 TSource）</typeparam>
    /// <typeparam name="TTargetDerived">子类目标类型（须派生自 TTarget；可等于 TTarget——目标收敛用例）</typeparam>
    /// <returns>this（链式）</returns>
    public FluentTypeConfig<TSource, TTarget> MapSubclass<TSrcDerived, TTargetDerived>()
        where TSrcDerived : TSource
        where TTargetDerived : TTarget => this;

    /// <summary>
    /// FromSource 补充源绑定 — 分组块形态：以 <typeparamref name="TSubSource"/> 声明一个子源分组，
    /// 块内按 <c>For&lt;TSubSource, TTarget&gt;</c> 同型链式配置该子源到主目标 <typeparamref name="TTarget"/> 的属性映射。
    /// <para>主源语义：链首 <c>For&lt;TSource, TTarget&gt;</c> 声明的主源承载主链映射；子源仅经本块（或速记重载）
    /// 声明跨源绑定，运行时 <c>Mapper.Compose&lt;TTarget&gt;(主源, 子源...)</c> 多源合成，同名字段后来者覆盖。</para>
    /// <para><b>块内 = For</b>：块接收者复用 <see cref="FluentTypeConfig{TSubSource, TTarget}"/>，
    /// 块内可用终端与 For 链一致（Map/Ignore/Mask/Convert/FromJson/MapTree 等，源锚点为子源）。
    /// <b>块内禁止</b>：ConvertBack（NCMAP_F30）、NoAutoInclude（NCMAP_F31）、AfterMapping（NCMAP_F32）——
    /// 反向派生/全局开关/映射后钩子在 FromSource 子源面语义无落点，提取期诊断 fail-fast。</para>
    /// <para>索引器/索引访问不受支持（子源绑定仅纯属性路径）。</para>
    /// <para>镜像特性侧 <c>[MapProperty(FromSource = typeof(子源))]</c>；运行时合成走
    /// <c>Mapper.Compose&lt;TTarget&gt;</c>（Compose pipeline）。</para>
    /// </summary>
    /// <typeparam name="TSubSource">子源类型</typeparam>
    /// <param name="configure">分组块配置器（块接收者 <see cref="FluentTypeConfig{TSubSource, TTarget}"/>，块内 = For）</param>
    /// <returns>this（链式）</returns>
    public FluentTypeConfig<TSource, TTarget> FromSource<TSubSource>(
        Action<FluentTypeConfig<TSubSource, TTarget>> configure) => this;
}

/// <summary>
/// 字段"头"配置器 — 字段链式三段式第二段承载：
/// <see cref="FluentTypeConfig{TSource,TTarget}.Member{TProp}"/> 锚定字段后返回本类型，
/// 承载头声明（From / FromSource / Ignore 三组）与配置段入口（Mask / Format / FromJson /
/// MapTree / Convert / ConvertBack / MapWhen / NoAutoInclude）。
/// <para><b>分层规则</b>：本类型<b>不继承</b>对级 <see cref="FluentTypeConfig{TSource,TTarget}"/>——
/// 裸 <c>.Member(x)</c> 未完成声明/配置时不可续链、不可接对级方法（编译期强制，Spike 反例
/// N6/N7）；完成任一声明或配置后返回 <see cref="MemberConfig{TSource,TTarget,TProp}"/>
/// （继承对级——回到外层续下一个字段或对级方法）。</para>
/// <para><b>来源可省语义</b>：配置段方法（如 Mask）省略 From 时按目标字段名解析同名源属性；
/// 无同名源属性时提取期防呆报错（新形态消费适配落地后生效）。</para>
/// <para>本类型与既有 Fluent 终端一致，仅作为编译期源生成器扫描的语法锚点——所有方法
/// 不做任何运行时工作（链式锚点无运行时状态）。</para>
/// </summary>
/// <typeparam name="TSource">源类型</typeparam>
/// <typeparam name="TTarget">目标类型</typeparam>
/// <typeparam name="TProp">目标属性类型（由 Member 的 dest lambda 推断锚定）</typeparam>
public class MemberHead<TSource, TTarget, TProp>
{
    /// <summary>内部构造（仅经 <see cref="FluentTypeConfig{TSource,TTarget}.Member{TProp}"/> 创建）</summary>
    internal MemberHead()
    {
    }

    /// <summary>
    /// 显式来源声明 — 为 Member 锚定的目标字段指定源属性路径（迁移自旧
    /// <c>Map&lt;TSrcProp,TTargetProp&gt;(dest, src)</c> 双签名族，豁免门收拢为可选参数；
    /// #2）
    /// <para><b>独立来源类型参数</b>：<typeparamref name="TSrcProp"/> 位于 src lambda 返回位——
    /// 同型可省、跨型（如 DateTime→string）自动推断，能力与旧 Map 1:1（跨型由生成代码内建
    /// 类型转换承接；无可行转换的类型对在生成期报 NCMAP027）。</para>
    /// <para>src 仅支持纯属性路径（含嵌套路径与 null 抑制 <c>!</c>）；非纯路径表达式按形态发
    /// 精确诊断（NCMAP_F06 索引器 / NCMAP_F55 多段 / NCMAP_F57 块体与三元），该声明跳过
    /// （与旧 Map src 同口径）。</para>
    /// </summary>
    /// <typeparam name="TSrcProp">源属性类型（src lambda 返回位推断；可与 TProp 不同）</typeparam>
    /// <param name="src">源属性 lambda（支持嵌套路径；不支持 <c>?.</c>/索引器/方法调用）</param>
    /// <param name="overrideIgnore">true = 全局忽略清单命中该字段时豁免（映射不被抑制）；默认 false</param>
    /// <returns>字段"配置"对象（继承对级——回到外层）</returns>
    public MemberConfig<TSource, TTarget, TProp> From<TSrcProp>(
        Expression<Func<TSource, TSrcProp>> src,
        bool overrideIgnore = false) => new();

    /// <summary>
    /// 子源速记来源声明 — 一条声明绑定一个子源属性到 Member 锚定的目标字段
    /// （迁移自旧 <c>FromSource&lt;TSubSource&gt;(dest, src)</c> 速记重载，最高频一行了事；
    /// #3）
    /// <para><b>object 占位</b>：src lambda 返回类型 <c>object</c> 占位（属性真实类型经 Roslyn
    /// 符号解析恢复，不受 TProp 约束——跨型不受限）；<typeparamref name="TSubSource"/> 为唯一
    /// 类型参数（须显式）。运行时 <c>Mapper.Compose&lt;TTarget&gt;</c> 多源合成，同名字段后来者覆盖。</para>
    /// <para>src 仅支持纯属性路径（含嵌套点路径），不支持 <c>?.</c>/索引器/方法调用
    /// （与旧速记同限制）；同 dest 重复绑定触发 NCMAP_F29。</para>
    /// </summary>
    /// <typeparam name="TSubSource">子源类型（唯一类型参数，须显式）</typeparam>
    /// <param name="src">子源属性 lambda（object 占位，支持嵌套路径）</param>
    /// <returns>字段"配置"对象（继承对级——回到外层）</returns>
    public MemberConfig<TSource, TTarget, TProp> FromSource<TSubSource>(
        Expression<Func<TSubSource, object>> src) => new();

    /// <summary>
    /// 忽略目标字段（不参与映射）— 语义等同旧 <c>Ignore&lt;TProp&gt;(dest)</c>（属性级 [MapIgnore]
    /// 镜像；#4）
    /// <para><b>独唱终态</b>：忽略即该字段段的结束声明——其后仅可链唯一例外
    /// <see cref="AfterIgnore{TSource,TTarget}.NoAutoInclude()"/>，不得跟任何映射/变换操作
    /// （编译期强制，Spike 反例 N3）。</para>
    /// </summary>
    /// <returns>"忽略之后"对象（继承对级；唯一例外 NoAutoInclude 可链）</returns>
    public AfterIgnore<TSource, TTarget> Ignore() => new();

    /// <summary>
    /// 方向性忽略目标字段 — 语义等同旧 <c>Ignore&lt;TProp&gt;(dest, Direction)</c>
    /// （[MapIgnore(Direction = ToDto/ToEntity)] 镜像）：仅在指定方向忽略（另一方向照常映射）。
    /// <para><b>反向派生语义</b>：ToEntity 方向忽略的字段不参与反向写回；ToDto 方向忽略的字段
    /// 正向跳过、反向照常派生（正向不建即无反向派生源——实际语义以物化端方向过滤为准）。</para>
    /// <para><b>独唱终态</b>：同 <see cref="Ignore()"/>——其后仅可链 NoAutoInclude 唯一例外。</para>
    /// </summary>
    /// <param name="direction">忽略方向（ToDto = 仅正向忽略 / ToEntity = 仅反向忽略 / Both = 双向）</param>
    /// <returns>"忽略之后"对象（继承对级；唯一例外 NoAutoInclude 可链）</returns>
    public AfterIgnore<TSource, TTarget> Ignore(NCMapper.Attributes.Direction direction) => new();

    /// <summary>
    /// 脱敏目标字段（走 MaskProvider，与 [Mask] 特性下游共享）— 迁移自旧
    /// <c>Mask&lt;TMask,TProp&gt;(dest)</c> 双签名族，豁免门收拢为可选参数
    /// （#5；TProp 由 Member 锚定后从签名消失）
    /// <para><b>输入域契约</b>：泛型约束 <c>where TMask : IMask</c>——脱敏实现类型须实现
    /// <see cref="NCMapper.IMask"/>（如 MaskPhone/MaskIdCard/MaskBankCard/MaskEmail/MaskChineseName）。</para>
    /// <para><b>来源可省</b>：省略 From 时按目标字段名解析同名源属性（无同名时提取期防呆报错）。</para>
    /// <para><b>组合能力（换代新面）</b>：可与 From 显式来源叠用（"自定义来源 + 脱敏"——旧字段级
    /// 写法写不出的组合）。</para>
    /// </summary>
    /// <typeparam name="TMask">脱敏实现类型，须实现 <see cref="NCMapper.IMask"/></typeparam>
    /// <param name="overrideIgnore">true = 全局忽略命中该字段时豁免（脱敏映射不被抑制）；默认 false</param>
    /// <returns>字段"配置"对象（继承对级——回到外层）</returns>
    public MemberConfig<TSource, TTarget, TProp> Mask<TMask>(bool overrideIgnore = false)
        where TMask : IMask => new();

    /// <summary>
    /// 格式化目标字段 — 迁移自旧 <c>Format&lt;TProp&gt;(dest, format)</c>（豁免门并入可选参数；
    /// #6）
    /// <para>源须实现 <see cref="IFormattable"/>，目标须为 string（TProp 锚定后类型契约由提取期
    /// 校验）——语义镜像 <c>[MapFormat]</c>。文化口径：恒传 null provider（=
    /// IFormattable.ToString(format, null) 的"当前文化"语义），与未标注属性恒 InvariantCulture
    /// 的转换路径输出分叉为有意设计；需要跨文化一致的固定输出请改用
    /// <see cref="Convert(System.Func{TProp},bool)"/> 自控文化。</para>
    /// <para>三类窄格式域提示：Guid 仅认 N/D/B/P/X；TimeSpan 自定义格式串的冒号句点须转义
    /// （如 hh\:mm）；枚举格式串为 G/D/X/F 等枚举专用形式。</para>
    /// <para><b>诊断指引</b>（沿用旧面同锚点码）：目标非 string → NCMAP_F64；源属性不可达/非
    /// IFormattable → NCMAP_F65；与 .Convert 同锚点矛盾 → NCMAP_F66；反向派生命中且未标
    /// ConvertBack → NCMAP_F67（Error）。格式串须为编译期常量（非常量静默跳过声明）。</para>
    /// </summary>
    /// <param name="format">格式串（编译期常量，非空）</param>
    /// <param name="overrideIgnore">true = 全局忽略命中该字段时豁免；默认 false</param>
    /// <returns>字段"配置"对象（继承对级——回到外层）</returns>
    public MemberConfig<TSource, TTarget, TProp> Format(string format, bool overrideIgnore = false) => new();

    /// <summary>
    /// JSON 字符串展开 — 迁移自旧 <c>FromJson&lt;TProp&gt;(dest)</c>（#7）
    /// <para>语义镜像特性侧 <c>[MapProperty(IsFromJson = true)]</c>：源侧 string 属性经
    /// <see cref="NCMapper.JsonConverter"/> 反序列化为目标复杂类型字段（正向 string→对象；
    /// 反向序列化写回）。源/目标均须为 string 或引用类型（值类型参与时声明被静默丢弃，
    /// 与特性侧同口径）。</para>
    /// <para><b>源侧契约</b>：省略 From 时按 dest 名解析源（或 FromSource 子源）同名属性为
    /// JSON 字符串源——同名属性不存在或类型门不过时声明丢弃（NCMAP001 由自动匹配口径承载）。</para>
    /// </summary>
    /// <returns>字段"配置"对象（继承对级——回到外层）</returns>
    public MemberConfig<TSource, TTarget, TProp> FromJson() => new();

    /// <summary>
    /// 树形映射 — 迁移自旧 <c>MapTree&lt;TProp&gt;(dest)</c>（#8）：
    /// 该目标字段递归走相同 (S,T) 配置（源侧省略 From 时按同名属性解析）。
    /// <para><b>辨析</b>：树构建三元组配置（主键/父键/子节点集合）为对级 <c>Tree()</c> 终端
    /// （<see cref="FluentTypeConfig{TSource,TTarget}.Tree()"/>），与本属性级递归映射声明互补使用。</para>
    /// </summary>
    /// <returns>字段"配置"对象（继承对级——回到外层）</returns>
    public MemberConfig<TSource, TTarget, TProp> MapTree() => new();

    /// <summary>
    /// 正向自定义转换（Entity→DTO）— 形态 T0（无参常量）：converter 为 <c>static TProp M()</c>，
    /// 目标字段写入常量。迁移自旧 <c>Convert&lt;TTargetProp&gt;(dest, converter)</c>
    /// （TTargetProp 由 Member 锚定后从签名消失；豁免门并入可选参数；#9）
    /// <para>语义镜像特性通道 <c>[Convert]</c>（静态方法组形态，4 转换形态 1:1 对齐）。</para>
    /// <para><b>可见性契约</b>：converter 须为 public/internal static 且与生成代码（同程序集）可见，
    /// private/protected 误用触发 NCMAP_F25；无捕获表达式 lambda 经转录引擎放行（符号级限定名
    /// 重写物化为 GeneratedMapper 私有静态方法）——捕获/私有访问触发 NCMAP_F41、语句体等
    /// 不可转录形态触发 NCMAP_F42，委托变量/实例绑定仍触发 NCMAP_F26；get-only/非 public
    /// setter 锚点触发 NCMAP_F28（Warning）；源侧全不可达触发 NCMAP_F24；同字段 Ignore+Convert
    /// 矛盾触发 NCMAP_F23。</para>
    /// <para><b>组合能力（换代新面）</b>：可与 From 显式来源叠用（变换作用于显式来源取值）。</para>
    /// </summary>
    /// <param name="converter">静态方法组（须 public/internal static + 同程序集可见）或无捕获表达式 lambda</param>
    /// <param name="overrideIgnore">true = 全局忽略命中该字段时豁免；默认 false</param>
    /// <returns>字段"配置"对象（继承对级——回到外层）</returns>
    public MemberConfig<TSource, TTarget, TProp> Convert(
        Func<TProp> converter,
        bool overrideIgnore = false) => new();

    /// <summary>
    /// 正向自定义转换 — 形态 T1（字段值）/ T2（整源）单参通用：converter 为
    /// <c>static TProp M(TSrcProp)</c>。迁移自旧 <c>Convert&lt;TSrcProp,TTargetProp&gt;(dest, converter)</c>
    /// （TTargetProp 由 Member 锚定后从签名消失）。单参 == 源字段类型判 T1（字段值入参）；单参 ==
    /// 源实体类型（TSource）判 T2（整源多字段组合）——判定在提取期按与特性侧一致的优先序完成
    /// （单参同时 == 源实体与源字段类型时判整源 T2）。
    /// <para>语义/可见性契约同 T0 形态重载
    /// <see cref="Convert(System.Func{TProp},bool)"/>。</para>
    /// <para><b>方法组推断指引</b>（沿用旧面实测口径）：<typeparamref name="TSrcProp"/> 位于委托
    /// 入参位（无推断位），方法组直用须<b>显式类型参</b> <c>.Convert&lt;TSrcProp&gt;(M)</c>；
    /// 或 converter 实参显式委托转换 / 先赋值局部委托变量后传入。委托变量/lambda 实参不受限。</para>
    /// </summary>
    /// <typeparam name="TSrcProp">converter 入参类型（源字段类型或源实体类型）</typeparam>
    /// <param name="converter">静态方法组（须 public/internal static + 同程序集可见）或无捕获表达式 lambda</param>
    /// <param name="overrideIgnore">true = 全局忽略命中该字段时豁免；默认 false</param>
    /// <returns>字段"配置"对象（继承对级——回到外层）</returns>
    public MemberConfig<TSource, TTarget, TProp> Convert<TSrcProp>(
        Func<TSrcProp, TProp> converter,
        bool overrideIgnore = false) => new();

    /// <summary>
    /// 正向自定义转换 — 形态 T3（组合+字段值）：converter 为 <c>static TProp M(TSource, TSrcProp)</c>，
    /// 整源 + 字段值双入参（整源取上下文 + 字段值转换）。迁移自旧
    /// <c>Convert&lt;TSrcProp,TTargetProp&gt;(dest, converter)</c> 三参重载。
    /// <para>语义/可见性契约/方法组推断指引同 T1 形态重载
    /// <see cref="Convert{TSrcProp}(System.Func{TSrcProp,TProp},bool)"/>。</para>
    /// </summary>
    /// <typeparam name="TSrcProp">converter 第二入参类型（源字段类型）</typeparam>
    /// <param name="converter">静态方法组（须 public/internal static + 同程序集可见）或无捕获表达式 lambda</param>
    /// <param name="overrideIgnore">true = 全局忽略命中该字段时豁免；默认 false</param>
    /// <returns>字段"配置"对象（继承对级——回到外层）</returns>
    public MemberConfig<TSource, TTarget, TProp> Convert<TSrcProp>(
        Func<TSource, TSrcProp, TProp> converter,
        bool overrideIgnore = false) => new();

    /// <summary>
    /// 正向自定义转换 — 形态 T2（整源）推断安全重载：converter 为 <c>static TProp M(TSource)</c>，
    /// 整个源对象入参（多字段组合）。委托入参 TSource 为类级固定类型参（有推断位）——
    /// <b>方法组可直用无需显式类型参</b>（<c>.Convert(M)</c>）。迁移自旧
    /// <c>Convert&lt;TTargetProp&gt;(dest, Func&lt;TSource,TTargetProp&gt;)</c> 推断安全重载。
    /// <para>语义/形态判定与本族其他重载一致（提取期形态匹配单点判定——整源方法组恒判
    /// T2MultiField）；可见性契约同 T0 形态重载 <see cref="Convert(System.Func{TProp},bool)"/>。</para>
    /// </summary>
    /// <param name="converter">静态方法组（须 public/internal static + 同程序集可见）或无捕获表达式 lambda</param>
    /// <param name="overrideIgnore">true = 全局忽略命中该字段时豁免；默认 false</param>
    /// <returns>字段"配置"对象（继承对级——回到外层）</returns>
    public MemberConfig<TSource, TTarget, TProp> Convert(
        Func<TSource, TProp> converter,
        bool overrideIgnore = false) => new();

    /// <summary>
    /// 反向自定义转换（DTO→Entity）— 形态 T0（无参常量）：converter 为 <c>static TEntityProp M()</c>，
    /// 反向写入常量。迁移自旧 <c>ConvertBack&lt;TDtoProp,TEntityProp&gt;(dest, converter)</c>
    /// 八重载族（豁免门收拢为可选参数；#10）——TDtoProp 由 Member
    /// 锚定后从签名消失（锚点即 Member 的 dest，TProp 承载），实体侧按名解析反向写回点
    /// （OrdinalIgnoreCase）。
    /// <para>语义镜像特性通道 <c>[ConvertBack]</c>（4 转换形态 1:1 对齐）；正向映射不消费本声明
    /// （按类型转换路径处理，"正向不消费"契约一致）；与同字段正向转换声明配对、各自独立可选。</para>
    /// <para><b>可见性契约</b>：converter 须为 public/internal static 且与生成代码（同程序集）可见，
    /// private/protected 误用触发 NCMAP_F19；lambda/闭包/局部函数触发 NCMAP_F20；签名不匹配
    /// 4 形态模板触发 NCMAP_F21；get-only/非 public setter 锚点触发 NCMAP_F22（Warning）；
    /// 同字段 Ignore+ConvertBack 矛盾触发 NCMAP_F17。</para>
    /// </summary>
    /// <typeparam name="TEntityProp">实体侧属性类型（converter 返回类型推断）</typeparam>
    /// <param name="converter">静态方法组（须 public/internal static + 同程序集可见）或无捕获表达式 lambda</param>
    /// <param name="overrideIgnore">true = 全局忽略命中该字段时豁免（反向写回不被抑制）；默认 false</param>
    /// <returns>字段"配置"对象（继承对级——回到外层）</returns>
    public MemberConfig<TSource, TTarget, TProp> ConvertBack<TEntityProp>(
        Func<TEntityProp> converter,
        bool overrideIgnore = false) => new();

    /// <summary>
    /// 反向自定义转换 — 形态 T1（单值）：converter 为 <c>static TEntityProp M(TProp)</c>，
    /// 字段值入参（最常见：string→DateTime 解析）。迁移自旧
    /// <c>ConvertBack&lt;TDtoProp,TEntityProp&gt;(dest, converter)</c>；锚点属性为
    /// <c>Nullable&lt;T&gt;</c> 而入参为 T 时自动生成 null 守卫三元（守卫变体）。
    /// <para>语义/锚点解析/可见性契约同 T0 形态重载
    /// <see cref="ConvertBack{TEntityProp}(System.Func{TEntityProp},bool)"/>；
    /// 正向映射不消费本声明。</para>
    /// <para><b>写回目标解析（两侧属性名不同时必读）</b>：实体侧写回目标按
    /// <b>同名 → 自动扁平化叶 → lambda 体</b> 的顺序解析。前两步按锚点名（= 目标字段名）
    /// 在实体上查找；两侧属性名不同（典型：实体 <c>Amount</c> → DTO <c>AmountText</c>，
    /// 正向用 <c>.From(s =&gt; s.Amount)</c> 显式来源后格式化）时前两步必然落空，此时允许用
    /// lambda 体自行指明写回目标：</para>
    /// <list type="bullet">
    /// <item>T2 整源形态 <c>(dto) =&gt; dto.XxxYyy</c>——表达式体为形参上的直接成员访问，
    /// 该成员即写回目标（纯改名映射可直接这么写）</item>
    /// <item>T3 组合形态 <c>(dto, 字段值) =&gt; M(字段值)</c>——反向转换器需要字段实参时，
    /// 写回目标取<b>正向来源字段</b>（即 <c>.From(s =&gt; s.Amount)</c> 的 <c>s.Amount</c>），
    /// 因为此时 lambda 体里只有字段值、没有实体侧属性名</item>
    /// </list>
    /// <para>解析规则刻意收窄（只认形参上的单层成员访问 / 直接使用字段形参；public setter
    /// 必需），对象初始化器、复合表达式、静态成员接收者等形态一律不猜测——仍报 NCMAP_F18，
    /// 该诊断的文案会给出上述可执行出口。</para>
    /// </summary>
    /// <typeparam name="TEntityProp">实体侧属性类型（converter 返回类型推断）</typeparam>
    /// <param name="converter">静态方法组（须 public/internal static + 同程序集可见）或无捕获表达式 lambda</param>
    /// <param name="overrideIgnore">true = 全局忽略命中该字段时豁免；默认 false</param>
    /// <returns>字段"配置"对象（继承对级——回到外层）</returns>
    public MemberConfig<TSource, TTarget, TProp> ConvertBack<TEntityProp>(
        Func<TProp, TEntityProp> converter,
        bool overrideIgnore = false) => new();

    /// <summary>
    /// 反向自定义转换 — 形态 T2（整源）：converter 为 <c>static TEntityProp M(TTarget)</c>，
    /// 整个 DTO 对象入参（多字段组合）。迁移自旧
    /// <c>ConvertBack&lt;TDtoProp,TEntityProp&gt;(dest, converter)</c> 整源重载。
    /// <para>语义/锚点解析/可见性契约同 T0 形态重载；正向映射不消费本声明。</para>
    /// </summary>
    /// <typeparam name="TEntityProp">实体侧属性类型（converter 返回类型推断）</typeparam>
    /// <param name="converter">静态方法组（须 public/internal static + 同程序集可见）或无捕获表达式 lambda</param>
    /// <param name="overrideIgnore">true = 全局忽略命中该字段时豁免；默认 false</param>
    /// <returns>字段"配置"对象（继承对级——回到外层）</returns>
    public MemberConfig<TSource, TTarget, TProp> ConvertBack<TEntityProp>(
        Func<TTarget, TEntityProp> converter,
        bool overrideIgnore = false) => new();

    /// <summary>
    /// 反向自定义转换 — 形态 T3（组合+字段值）：converter 为
    /// <c>static TEntityProp M(TTarget, TProp)</c>，整 DTO + 字段值双入参。迁移自旧
    /// <c>ConvertBack&lt;TDtoProp,TEntityProp&gt;(dest, converter)</c> 三参重载；Nullable 守卫
    /// 变体识别同 T1。
    /// <para>语义/锚点解析/可见性契约同 T0 形态重载；正向映射不消费本声明。</para>
    /// </summary>
    /// <typeparam name="TEntityProp">实体侧属性类型（converter 返回类型推断）</typeparam>
    /// <param name="converter">静态方法组（须 public/internal static + 同程序集可见）或无捕获表达式 lambda</param>
    /// <param name="overrideIgnore">true = 全局忽略命中该字段时豁免；默认 false</param>
    /// <returns>字段"配置"对象（继承对级——回到外层）</returns>
    public MemberConfig<TSource, TTarget, TProp> ConvertBack<TEntityProp>(
        Func<TTarget, TProp, TEntityProp> converter,
        bool overrideIgnore = false) => new();

    /// <summary>
    /// 条件守卫（T1 字段值形态）— 仅当 <paramref name="predicate"/> 对<b>源字段值</b>求值返回
    /// true 时才执行该字段的映射赋值（真才映射——极性入名）。迁移自旧
    /// <c>MapWhen&lt;TSrcProp&gt;(dest, condition)</c>（#11 混合载：
    /// T1 非泛型——TProp 已由 Member 锚定，内联 lambda 直接可用）。
    /// <para><b>附着语义</b>：锚定 Member 字段的映射（同名默认解析，或 From 显式声明的映射）
    /// ——Convert/Mask 伴随字段同构；链路次序 = 条件 → 转换 → 赋值（条件看到转换前的源字段值）；
    /// 条件假 → 跳过赋值（新建保持初始化器值 / <c>_Update</c> 保留 dst 既有值）。</para>
    /// <para><b>谓词形态</b>：A 命名静态方法组（<c>public/internal static bool M(TProp)</c>，须与
    /// 生成代码同程序集可见——private/protected 误用触发 NCMAP_F40）；或 B 无捕获内联 lambda
    /// （表达式体，经符号级限定名重写转录进生成代码——捕获变量/私有访问触发 NCMAP_F41、
    /// 语句体/不可转录节点触发 NCMAP_F42）。</para>
    /// <para><b>PATCH 惯用法注记</b>：<c>.MapWhen(v =&gt; v != null)</c> 实现 null = 未提供 →
    /// 跳过赋值的 PATCH 语义；注意与显式赋 null 的交换——守卫仅跳过映射，不区分"未提供"与
    /// "显式清空"，需精确语义时调用方自行前置判断。</para>
    /// <para><b>边界</b>：同字段重复声明触发 NCMAP_F44（单条件，谓词取反表达负向）；
    /// 锚定字段无映射或被 <see cref="Ignore()"/> 静态排除触发 NCMAP_F43；锚定仅 FromSource
    /// 跨源覆盖的字段触发 NCMAP_F45（块内声明除外）；投影路径恒降级（分类 MapWhen）；
    /// 反向映射不消费本声明。</para>
    /// </summary>
    /// <param name="predicate">谓词（A 命名静态方法组 / B 无捕获表达式 lambda；入参 = 源字段值 TProp）</param>
    /// <returns>字段"配置"对象（继承对级——回到外层）</returns>
    public MemberConfig<TSource, TTarget, TProp> MapWhen(Func<TProp, bool> predicate) => new();

    /// <summary>
    /// 条件守卫（T2 整源形态）— 仅当 <paramref name="condition"/> 对<b>整个源对象</b>求值返回
    /// true 时才执行该字段的映射赋值（多字段判断场景）。混合载泛型位：须<b>显式类型参</b>
    /// （<c>.MapWhen&lt;TSource&gt;(x =&gt; ...)</c>——内联 lambda 无推断位）；提取期校验
    /// <typeparamref name="TInput"/> ∈ {TProp, TSource}（不符报防呆诊断，新形态消费适配落地后生效）。
    /// <para>语义/附着语义/谓词形态/PATCH 注记/边界同 T1 形态
    /// <see cref="MapWhen(System.Func{TProp,bool})"/>。</para>
    /// </summary>
    /// <typeparam name="TInput">条件入参类型（TProp 字段值 或 TSource 整源；须显指定）</typeparam>
    /// <param name="condition">谓词（A 命名静态方法组 / B 无捕获表达式 lambda；入参 = TInput）</param>
    /// <returns>字段"配置"对象（继承对级——回到外层）</returns>
    public MemberConfig<TSource, TTarget, TProp> MapWhen<TInput>(Func<TInput, bool> condition) => new();

    /// <summary>
    /// 属性级禁用自动包含 — 迁移自旧 <c>NoAutoInclude(dest)</c> 字段版（#12）：该字段不参与 ORM 降级路径的导航树自动收集（NavigationTreeCollector 跳过
    /// 该属性的导航 Include）。语义镜像特性侧 <c>[MapNoAutoInclude]</c>（属性级）。
    /// <para><b>与对级 <see cref="FluentTypeConfig{TSource,TTarget}.NoAutoMatch()"/> 语义辨析</b>：
    /// 对级 = 该 (S,T) 对禁用自动匹配（仅显式声明映射）；属性级 = 仅该字段跳过导航收集（其余
    /// 字段照常自动匹配）——语义粒度不同，双承载并行而非等价。两形态叠加时物化取并集。</para>
    /// <para><b>并存期压制注记（CS0108）</b>：本方法与继承链上对级旧名 NoAutoInclude() 同名共存
    /// ——本类型不继承对级，无压制问题；返回 <see cref="MemberConfig{TSource,TTarget,TProp}"/>
    /// 后（继承对级）字段段内再调用 NoAutoInclude 由 MemberConfig 的字段版（new 压制）承载。
    /// 字段版/对级版同名卸除与 new 压制移除归测试册断代清扫段（
    /// 册间接口设计；届时无同名继承成员，保留 new 会产生 CS0109）。</para>
    /// </summary>
    /// <returns>字段"配置"对象（继承对级——回到外层）</returns>
    public MemberConfig<TSource, TTarget, TProp> NoAutoInclude() => new();
}

/// <summary>
/// 字段"配置"对象 — 字段链式三段式配置段承载：
/// MemberHead 完成头声明（或直接进入配置段）后返回本类型，承载配置段 8 组
/// （Mask / Format / FromJson / MapTree / Convert / ConvertBack / MapWhen 两载 / NoAutoInclude）。
/// <para><b>回到外层</b>：本类型继承对级 <see cref="FluentTypeConfig{TSource,TTarget}"/>——
/// 字段段配完后可直接续下一个 <see cref="FluentTypeConfig{TSource,TTarget}.Member{TProp}"/>
/// 或对级方法（AfterMapping / Tree / FromSource 块等）；对级新名
/// <see cref="FluentTypeConfig{TSource,TTarget}.NoAutoMatch()"/> 经继承面任意位置可调。</para>
/// <para><b>配置段边界（编译期强制）</b>：本类型无 From/FromSource/Ignore 头声明——配置之后
/// 不可再补来源声明（Spike 反例 N1/N8）、不可再忽略（Spike 反例 N2）；同字段来源唯一性由
/// 头段结构保证。</para>
/// <para><b>并存期压制注记</b>：本类型 <c>NoAutoInclude()</c> 与继承的对级旧名同名（CS0108）
/// ——加 <c>new</c> 修饰符压制（字段语义遮蔽对级语义），移除归测试册断代清扫段
/// （册间接口设计）。</para>
/// <para>本类型与既有 Fluent 终端一致，仅作为编译期源生成器扫描的语法锚点——所有方法
/// 不做任何运行时工作。</para>
/// </summary>
/// <typeparam name="TSource">源类型</typeparam>
/// <typeparam name="TTarget">目标类型</typeparam>
/// <typeparam name="TProp">目标属性类型（由 Member 的 dest lambda 推断锚定）</typeparam>
public class MemberConfig<TSource, TTarget, TProp> : FluentTypeConfig<TSource, TTarget>
{
    /// <summary>内部构造（仅经 <see cref="MemberHead{TSource,TTarget,TProp}"/> 各声明/配置方法创建）</summary>
    internal MemberConfig() : base(null!)
    {
    }

    /// <summary>
    /// 脱敏目标字段 — 语义/可见性契约/来源可省同
    /// <see cref="MemberHead{TSource,TTarget,TProp}.Mask{TMask}(bool)"/>
    /// （配置段位置：头声明 From 之后叠加变换）。
    /// </summary>
    /// <typeparam name="TMask">脱敏实现类型，须实现 <see cref="NCMapper.IMask"/></typeparam>
    /// <param name="overrideIgnore">true = 全局忽略命中该字段时豁免；默认 false</param>
    /// <returns>this（字段"配置"段——可继续叠加或回到外层）</returns>
    public MemberConfig<TSource, TTarget, TProp> Mask<TMask>(bool overrideIgnore = false)
        where TMask : IMask => new();

    /// <summary>
    /// 格式化目标字段 — 语义/文化口径/诊断指引同
    /// <see cref="MemberHead{TSource,TTarget,TProp}.Format(string,bool)"/>
    /// （配置段位置：头声明 From 之后叠加变换）。
    /// </summary>
    /// <param name="format">格式串（编译期常量，非空）</param>
    /// <param name="overrideIgnore">true = 全局忽略命中该字段时豁免；默认 false</param>
    /// <returns>this（字段"配置"段——可继续叠加或回到外层）</returns>
    public MemberConfig<TSource, TTarget, TProp> Format(string format, bool overrideIgnore = false) => new();

    /// <summary>
    /// JSON 字符串展开 — 语义/源侧契约同
    /// <see cref="MemberHead{TSource,TTarget,TProp}.FromJson()"/>
    /// （配置段位置：变换作用于显式/同名来源取值）。
    /// </summary>
    /// <returns>this（字段"配置"段——可继续叠加或回到外层）</returns>
    public MemberConfig<TSource, TTarget, TProp> FromJson() => new();

    /// <summary>
    /// 树形映射 — 语义同 <see cref="MemberHead{TSource,TTarget,TProp}.MapTree()"/>
    /// （配置段位置：该字段递归走相同 (S,T) 配置）。
    /// </summary>
    /// <returns>this（字段"配置"段——可继续叠加或回到外层）</returns>
    public MemberConfig<TSource, TTarget, TProp> MapTree() => new();

    /// <summary>
    /// 正向自定义转换 — 形态 T0（无参常量）：converter 为 <c>static TProp M()</c>。
    /// 语义/可见性契约同 <see cref="MemberHead{TSource,TTarget,TProp}.Convert(System.Func{TProp},bool)"/>
    /// （配置段位置：附加层叠加，如 Convert 与 ConvertBack 配对）。
    /// </summary>
    /// <param name="converter">静态方法组（须 public/internal static + 同程序集可见）或无捕获表达式 lambda</param>
    /// <param name="overrideIgnore">true = 全局忽略命中该字段时豁免；默认 false</param>
    /// <returns>this（字段"配置"段——可继续叠加或回到外层）</returns>
    public MemberConfig<TSource, TTarget, TProp> Convert(
        Func<TProp> converter,
        bool overrideIgnore = false) => new();

    /// <summary>
    /// 正向自定义转换 — 形态 T1（字段值）/ T2（整源）单参通用：converter 为
    /// <c>static TProp M(TSrcProp)</c>。语义/形态判定/方法组推断指引同
    /// <see cref="MemberHead{TSource,TTarget,TProp}.Convert{TSrcProp}(System.Func{TSrcProp,TProp},bool)"/>。
    /// </summary>
    /// <typeparam name="TSrcProp">converter 入参类型（源字段类型或源实体类型）</typeparam>
    /// <param name="converter">静态方法组（须 public/internal static + 同程序集可见）或无捕获表达式 lambda</param>
    /// <param name="overrideIgnore">true = 全局忽略命中该字段时豁免；默认 false</param>
    /// <returns>this（字段"配置"段——可继续叠加或回到外层）</returns>
    public MemberConfig<TSource, TTarget, TProp> Convert<TSrcProp>(
        Func<TSrcProp, TProp> converter,
        bool overrideIgnore = false) => new();

    /// <summary>
    /// 正向自定义转换 — 形态 T3（组合+字段值）：converter 为
    /// <c>static TProp M(TSource, TSrcProp)</c>。语义同
    /// <see cref="MemberHead{TSource,TTarget,TProp}.Convert{TSrcProp}(System.Func{TSource,TSrcProp,TProp},bool)"/>。
    /// </summary>
    /// <typeparam name="TSrcProp">converter 第二入参类型（源字段类型）</typeparam>
    /// <param name="converter">静态方法组（须 public/internal static + 同程序集可见）或无捕获表达式 lambda</param>
    /// <param name="overrideIgnore">true = 全局忽略命中该字段时豁免；默认 false</param>
    /// <returns>this（字段"配置"段——可继续叠加或回到外层）</returns>
    public MemberConfig<TSource, TTarget, TProp> Convert<TSrcProp>(
        Func<TSource, TSrcProp, TProp> converter,
        bool overrideIgnore = false) => new();

    /// <summary>
    /// 正向自定义转换 — 形态 T2（整源）推断安全重载：converter 为 <c>static TProp M(TSource)</c>。
    /// 语义同 <see cref="MemberHead{TSource,TTarget,TProp}.Convert(System.Func{TSource,TProp},bool)"/>。
    /// </summary>
    /// <param name="converter">静态方法组（须 public/internal static + 同程序集可见）或无捕获表达式 lambda</param>
    /// <param name="overrideIgnore">true = 全局忽略命中该字段时豁免；默认 false</param>
    /// <returns>this（字段"配置"段——可继续叠加或回到外层）</returns>
    public MemberConfig<TSource, TTarget, TProp> Convert(
        Func<TSource, TProp> converter,
        bool overrideIgnore = false) => new();

    /// <summary>
    /// 反向自定义转换 — 形态 T0（无参常量）：converter 为 <c>static TEntityProp M()</c>。
    /// 语义/锚点解析/可见性契约同
    /// <see cref="MemberHead{TSource,TTarget,TProp}.ConvertBack{TEntityProp}(System.Func{TEntityProp},bool)"/>。
    /// </summary>
    /// <typeparam name="TEntityProp">实体侧属性类型（converter 返回类型推断）</typeparam>
    /// <param name="converter">静态方法组（须 public/internal static + 同程序集可见）或无捕获表达式 lambda</param>
    /// <param name="overrideIgnore">true = 全局忽略命中该字段时豁免；默认 false</param>
    /// <returns>this（字段"配置"段——可继续叠加或回到外层）</returns>
    public MemberConfig<TSource, TTarget, TProp> ConvertBack<TEntityProp>(
        Func<TEntityProp> converter,
        bool overrideIgnore = false) => new();

    /// <summary>
    /// 反向自定义转换 — 形态 T1（单值）：converter 为 <c>static TEntityProp M(TProp)</c>。
    /// 语义/Nullable 守卫变体同
    /// <see cref="MemberHead{TSource,TTarget,TProp}.ConvertBack{TEntityProp}(System.Func{TProp,TEntityProp},bool)"/>。
    /// </summary>
    /// <typeparam name="TEntityProp">实体侧属性类型（converter 返回类型推断）</typeparam>
    /// <param name="converter">静态方法组（须 public/internal static + 同程序集可见）或无捕获表达式 lambda</param>
    /// <param name="overrideIgnore">true = 全局忽略命中该字段时豁免；默认 false</param>
    /// <returns>this（字段"配置"段——可继续叠加或回到外层）</returns>
    public MemberConfig<TSource, TTarget, TProp> ConvertBack<TEntityProp>(
        Func<TProp, TEntityProp> converter,
        bool overrideIgnore = false) => new();

    /// <summary>
    /// 反向自定义转换 — 形态 T2（整源）：converter 为 <c>static TEntityProp M(TTarget)</c>。
    /// 语义同
    /// <see cref="MemberHead{TSource,TTarget,TProp}.ConvertBack{TEntityProp}(System.Func{TTarget,TEntityProp},bool)"/>。
    /// </summary>
    /// <typeparam name="TEntityProp">实体侧属性类型（converter 返回类型推断）</typeparam>
    /// <param name="converter">静态方法组（须 public/internal static + 同程序集可见）或无捕获表达式 lambda</param>
    /// <param name="overrideIgnore">true = 全局忽略命中该字段时豁免；默认 false</param>
    /// <returns>this（字段"配置"段——可继续叠加或回到外层）</returns>
    public MemberConfig<TSource, TTarget, TProp> ConvertBack<TEntityProp>(
        Func<TTarget, TEntityProp> converter,
        bool overrideIgnore = false) => new();

    /// <summary>
    /// 反向自定义转换 — 形态 T3（组合+字段值）：converter 为
    /// <c>static TEntityProp M(TTarget, TProp)</c>。语义同
    /// <see cref="MemberHead{TSource,TTarget,TProp}.ConvertBack{TEntityProp}(System.Func{TTarget,TProp,TEntityProp},bool)"/>。
    /// </summary>
    /// <typeparam name="TEntityProp">实体侧属性类型（converter 返回类型推断）</typeparam>
    /// <param name="converter">静态方法组（须 public/internal static + 同程序集可见）或无捕获表达式 lambda</param>
    /// <param name="overrideIgnore">true = 全局忽略命中该字段时豁免；默认 false</param>
    /// <returns>this（字段"配置"段——可继续叠加或回到外层）</returns>
    public MemberConfig<TSource, TTarget, TProp> ConvertBack<TEntityProp>(
        Func<TTarget, TProp, TEntityProp> converter,
        bool overrideIgnore = false) => new();

    /// <summary>
    /// 条件守卫（T1 字段值形态）— 语义/附着语义/谓词形态/边界同
    /// <see cref="MemberHead{TSource,TTarget,TProp}.MapWhen(System.Func{TProp,bool})"/>
    /// （配置段位置：头声明 From 之后叠加守卫）。
    /// </summary>
    /// <param name="predicate">谓词（A 命名静态方法组 / B 无捕获表达式 lambda；入参 = 源字段值 TProp）</param>
    /// <returns>this（字段"配置"段——可继续叠加或回到外层）</returns>
    public MemberConfig<TSource, TTarget, TProp> MapWhen(Func<TProp, bool> predicate) => new();

    /// <summary>
    /// 条件守卫（T2 整源形态）— 语义/显式类型参要求/类型参校验同
    /// <see cref="MemberHead{TSource,TTarget,TProp}.MapWhen{TInput}(System.Func{TInput,bool})"/>。
    /// </summary>
    /// <typeparam name="TInput">条件入参类型（TProp 字段值 或 TSource 整源；须显指定）</typeparam>
    /// <param name="condition">谓词（A 命名静态方法组 / B 无捕获表达式 lambda；入参 = TInput）</param>
    /// <returns>this（字段"配置"段——可继续叠加或回到外层）</returns>
    public MemberConfig<TSource, TTarget, TProp> MapWhen<TInput>(Func<TInput, bool> condition) => new();

    /// <summary>
    /// 属性级禁用自动包含 — 语义/与对级 NoAutoMatch 辨析同
    /// <see cref="MemberHead{TSource,TTarget,TProp}.NoAutoInclude()"/>（配置段位置，返回自身可续配置）。
    /// 同字段重复声明幂等（HashSet 承载，现状行为保持）。
    /// </summary>
    /// <returns>this（字段"配置"段——可继续叠加或回到外层）</returns>
    public MemberConfig<TSource, TTarget, TProp> NoAutoInclude() => this;
}

/// <summary>
/// "忽略之后"对象 — 字段链式三段式忽略独唱终态承载：
/// <see cref="MemberHead{TSource,TTarget,TProp}.Ignore()"/> / Ignore(Direction) 返回本类型。
/// <para><b>独唱终态</b>：忽略之后不得跟任何映射/变换操作（编译期强制，Spike 反例 N3）；
/// 唯一例外为本类型上的 <see cref="NoAutoInclude()"/>（"忽略 + 查询不加载"组合，v1.4 唯一
/// 例外）——写完即回对级（<see cref="FluentTypeConfig{TSource,TTarget}"/>），不可再写第二个
/// 。</para>
/// <para>本类型与既有 Fluent 终端一致，仅作为编译期源生成器扫描的语法锚点——所有方法
/// 不做任何运行时工作。</para>
/// </summary>
/// <typeparam name="TSource">源类型</typeparam>
/// <typeparam name="TTarget">目标类型</typeparam>
public class AfterIgnore<TSource, TTarget> : FluentTypeConfig<TSource, TTarget>
{
    /// <summary>内部构造（仅经 <see cref="MemberHead{TSource,TTarget,TProp}.Ignore()"/> / Ignore(Direction) 创建）</summary>
    internal AfterIgnore() : base(null!)
    {
    }

    /// <summary>
    /// 属性级禁用自动包含 — 忽略字段的唯一例外（"忽略 + 查询不加载"组合）：
    /// 该字段不参与 ORM 降级路径的导航树自动收集。语义/与对级
    /// <see cref="FluentTypeConfig{TSource,TTarget}.NoAutoMatch()"/> 辨析同
    /// <see cref="MemberHead{TSource,TTarget,TProp}.NoAutoInclude()"/>。
    /// <para><b>写完即回对级</b>：本方法返回 <see cref="FluentTypeConfig{TSource,TTarget}"/>——
    /// 忽略对象的唯一挂点，不可再写第二个。</para>
    /// </summary>
    /// <returns>对级配置器（写完即回外层）</returns>
    public FluentTypeConfig<TSource, TTarget> NoAutoInclude() => this;
}
