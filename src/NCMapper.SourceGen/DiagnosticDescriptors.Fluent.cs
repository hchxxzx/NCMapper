using Microsoft.CodeAnalysis;
using NCMapper.SourceGen.Resources;

namespace NCMapper.SourceGen;

/// <summary>
/// Fluent 通道编译期诊断描述符（错误码表；
/// 增补 NCMAP_F16）。
/// <para>编号体系：NCMAP_F02/F04~F08/F11~F60/F62~F67（F03/F09/F10/F13/F61 保留位不启用——
/// F03（成员非属性/字段）按 / 拍板静默跳过；F09/F10/F13 为历史保留位；
/// F61 退役空号不复用（Unshipped Removed Rules 登记）—— 头部注释同步至实际最大号 F67）。</para>
/// <para>双语：neutral resx（EN 默认 fallback）+ zh-CN resx 卫星，与 NCMAP0xx 同机制（v1.3 结论）。
/// 触发位置：FluentExtractor（F02/F04~F08/F12~F15）+ DualDriverArbiter（F11/F16）。</para>
/// <para>I'-1 登记处置（v1.0.3）： PoC 即落地全套描述符（诊断发射依赖 FromId 反查），
/// 收编为正式验收（触发正确性 + 双语齐备 + 模板对照）。</para>
/// </summary>
internal static class DiagnosticDescriptorsFluent
{
    /// <summary>
    /// Fluent lambda 引用的属性在类型上不存在
    /// </summary>
    public static readonly DiagnosticDescriptor FluentPropertyNotFoundRule = new(
        id: "NCMAP_F02",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F02_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F02_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F02_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// Fluent lambda 根表达式不是 lambda 参数（局部变量/静态类引用，F04）
    /// </summary>
    public static readonly DiagnosticDescriptor FluentLambdaRootNotParameterRule = new(
        id: "NCMAP_F04",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F04_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F04_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F04_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// Fluent lambda 包含 null 条件访问 ?.
    /// </summary>
    public static readonly DiagnosticDescriptor FluentNullConditionalRule = new(
        id: "NCMAP_F05",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F05_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F05_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F05_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// Fluent lambda 包含索引器访问 []
    /// </summary>
    public static readonly DiagnosticDescriptor FluentIndexerRule = new(
        id: "NCMAP_F06",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F06_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F06_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F06_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// Fluent lambda 包含方法调用 ()
    /// </summary>
    public static readonly DiagnosticDescriptor FluentMethodCallRule = new(
        id: "NCMAP_F07",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F07_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F07_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F07_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// Fluent Configure 方法体包含动态配置语句（F08 黑名单：if/for/foreach/while/switch/try/三元/赋值/局部变量）
    /// </summary>
    public static readonly DiagnosticDescriptor FluentDynamicConfigRule = new(
        id: "NCMAP_F08",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F08_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F08_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F08_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// 双驱动同对双声明（F11，候选 A 从严互斥，v1.5/v1.6 决策）——同一 (S,T) 对被 [MapFrom] + Fluent 两通道同时声明；
    /// 双位置各发一条 + 消息互指；仅约束同对双声明，模式并存（不同对各走各通道）不触发
    /// </summary>
    public static readonly DiagnosticDescriptor DualDriverConflictRule = new(
        id: "NCMAP_F11",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F11_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F11_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F11_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// AfterMapping 目标方法为 private（F12，可见性契约）
    /// </summary>
    public static readonly DiagnosticDescriptor AfterMappingInaccessibleRule = new(
        id: "NCMAP_F12",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F12_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F12_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F12_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// AfterMapping 目标不是可静态解析的方法组引用（F14，lambda/闭包/捕获变量/局部函数）
    /// </summary>
    public static readonly DiagnosticDescriptor AfterMappingNotMethodGroupRule = new(
        id: "NCMAP_F14",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F14_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F14_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F14_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// [MapperConfig] 类内没有匹配签名的方法（F15，启发式诊断——用户已显式声明入口意图但漏写方法）
    /// </summary>
    public static readonly DiagnosticDescriptor MapperConfigNoMatchingMethodRule = new(
        id: "NCMAP_F15",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F15_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F15_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F15_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// Fluent 通道内同一 (S,T) 对重复声明（F16， 拍板"报错"候选）
    /// 同一对被声明 ≥2 处（跨 [MapperConfig] 类或同一 Configure 内多链）时每处各发一条；
    /// 全部冲突声明均抑制派生（不生成方法/不注册/不生成反向扩展——消费端 CS0111 消失）
    /// </summary>
    public static readonly DiagnosticDescriptor FluentDuplicateTypePairRule = new(
        id: "NCMAP_F16",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F16_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F16_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F16_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    // ====：ConvertBack 终端方法诊断族（F17~F22， 定稿表）====

    /// <summary>
    /// 同属性 Ignore + ConvertBack 矛盾（F17，Error——fail-fast）——
    /// Fluent 同锚点 Ignore+ConvertBack 是同一显式声明面内的无歧义用户矛盾：
    /// Ignore 使属性不入候选（无 pm 即无反向派生），ConvertBack 反向写永远不会执行。
    /// <para>非 NCMAP022 镜像：NCMAP022 为 [MapIgnore]+OverrideIgnore=true 矛盾（Warning 级）；
    /// 特性侧普通 [MapIgnore]+[ConvertBack] 静默不消费无专门诊断——F17 为 Fluent 新增 Error 级。</para>
    /// </summary>
    public static readonly DiagnosticDescriptor FluentIgnoreConvertBackConflictRule = new(
        id: "NCMAP_F17",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F17_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F17_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F17_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// ConvertBack 实体侧反向写回点不存在（F18，Error——提取期单点发射）——
    /// dest 锚点属性名在 TSource 上无同名属性（OrdinalIgnoreCase），反向写回无目标。
    /// </summary>
    public static readonly DiagnosticDescriptor ConvertBackReverseTargetNotFoundRule = new(
        id: "NCMAP_F18",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F18_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F18_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F18_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// ConvertBack 转换器可见性不足（F19，Error——镜像 F12）——
    /// converter 为 private/protected static：生成代码（internal static class GeneratedMapper，同程序集）不可访问。
    /// </summary>
    public static readonly DiagnosticDescriptor ConvertBackInaccessibleRule = new(
        id: "NCMAP_F19",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F19_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F19_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F19_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// ConvertBack 转换器非静态可寻址方法组（F20，Error——镜像 F14； 契约收窄）
    /// 仅非 lambda 的不可静态寻址形态（委托变量/实例方法绑定）触发；无捕获表达式 lambda 经转录引擎
    /// 放行（InlineTranscriber → GeneratedMapper 私有静态方法），捕获/私有访问/语句体等保留禁令
    /// 由 F41/F42 承载（镜像 MapWhen B 形态语义）。
    /// </summary>
    public static readonly DiagnosticDescriptor ConvertBackNotMethodGroupRule = new(
        id: "NCMAP_F20",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F20_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F20_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F20_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// ConvertBack 转换器签名不匹配 4+2 模板（F21，Error——与特性侧 NCMAP037 同口径）——
    /// 方法组签名与 T0/T1/T2/T3（含 Nullable 守卫变体）无一命中（含泛型方法/可选参/params/ref-out-in 排除）。
    /// </summary>
    public static readonly DiagnosticDescriptor ConvertBackShapeMismatchRule = new(
        id: "NCMAP_F21",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F21_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F21_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F21_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// ConvertBack 锚点属性不可外部赋值（F22，Warning——方向 A 特有边界，意见_001）
    /// get-only/非 public setter 的 DTO 锚点声明将被物化遍历 H-07 写门
    /// （MappingSnapshotBuilder.cs:84-86，在声明处理之前）静默吞掉；提前告知而非改门
    /// （改门波及既有 8 方法遍历语义，制造新通道分叉）。逃生口：public/init setter，
    /// 或特性通道实体侧 [ConvertBack]（ReverseOnly 不经 DTO 遍历门）。
    /// </summary>
    public static readonly DiagnosticDescriptor ConvertBackAnchorNotWritableRule = new(
        id: "NCMAP_F22",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F22_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F22_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F22_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    // ====：Convert 终端方法诊断族（F23~F28， 定稿表
    // 与 F17~F22 ConvertBack 反向族镜像对称：矛盾/源侧未命中/可见性/非方法组/形态/写门六类一一对应；
    // 随终端更名 ConvertUsing→Convert，码号不变）====

    /// <summary>
    /// 同属性 Ignore + Convert 矛盾（F23，Error——镜像 F17）——
    /// Fluent 同锚点 Ignore+Convert 是同一显式声明面内的无歧义用户矛盾：
    /// Ignore 使属性不入候选（无 pm 即无转换器附着），Convert 正向写永远不会执行。
    /// </summary>
    public static readonly DiagnosticDescriptor FluentIgnoreConvertConflictRule = new(
        id: "NCMAP_F23",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F23_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F23_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F23_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// Convert 源侧匹配流程不可达（F24，Error——正向版 F18）——
    /// 同名 FindProperty → 自动扁平化 TryFindFlattenedPath → [MapProperty] 反向查找全未命中，
    /// 声明无源侧输入（附着模式无 pm 锚点），fail-fast（与特性侧无源 default 调用形态为通道边界差异，
    /// 见差异登记）。
    /// </summary>
    public static readonly DiagnosticDescriptor ConvertSourceNotFoundRule = new(
        id: "NCMAP_F24",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F24_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F24_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F24_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// Convert 转换器可见性不足（F25，Error——镜像 F19）——
    /// converter 为 private/protected static：生成代码（internal static class GeneratedMapper，同程序集）不可访问。
    /// </summary>
    public static readonly DiagnosticDescriptor ConvertInaccessibleRule = new(
        id: "NCMAP_F25",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F25_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F25_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F25_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// Convert 转换器非静态可寻址方法组（F26，Error——镜像 F20； 契约收窄）
    /// 仅非 lambda 的不可静态寻址形态（委托变量/实例方法绑定）触发；无捕获表达式 lambda 经转录引擎
    /// 放行（InlineTranscriber → GeneratedMapper 私有静态方法），捕获/私有访问/语句体等保留禁令
    /// 由 F41/F42 承载（镜像 MapWhen B 形态语义）。
    /// </summary>
    public static readonly DiagnosticDescriptor ConvertNotMethodGroupRule = new(
        id: "NCMAP_F26",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F26_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F26_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F26_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// Convert 转换器签名不匹配 4+2 模板（F27，Error——镜像 F21，与特性侧 NCMAP037 同口径）——
    /// 方法组签名与 T0/T1/T2/T3（含 Nullable 守卫变体）无一命中（含泛型方法/可选参/params/ref-out-in 排除）。
    /// </summary>
    public static readonly DiagnosticDescriptor ConvertShapeMismatchRule = new(
        id: "NCMAP_F27",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F27_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F27_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F27_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// Convert 锚点属性不可外部赋值（F28，Warning——镜像 F22）——
    /// get-only/非 public setter 的 DTO 锚点声明将被物化遍历 H-07 写门
    /// （MappingSnapshotBuilder.cs，在声明处理之前）静默吞掉；提前告知而非改门。
    /// 逃生口：public/init setter，或特性通道 [Convert] 属性级标注。
    /// </summary>
    public static readonly DiagnosticDescriptor ConvertAnchorNotWritableRule = new(
        id: "NCMAP_F28",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F28_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F28_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F28_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    // ====：FromSource 补充源绑定诊断族（F29-F32，/
    // F29 主链×FromSource 同 dest 矛盾（物化发射）；F30-F32 块内禁止（提取期发射，本任务）====

    /// <summary>
    /// 主链×FromSource 同 dest 矛盾（F29，Error——沿 F23/F17 同款门）——
    /// FromSource 命中属性同时出现在主链三表（Declarations ∥ ForwardDeclarations ∥ BackDeclarations，
    /// v1.7 扩面）或 FromSource×FromSource 同子源重复绑定：一个属性只能由一个源绑定供给，fail-fast。
    /// </summary>
    public static readonly DiagnosticDescriptor ComposeConflictRule = new(
        id: "NCMAP_F29",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F29_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F29_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F29_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// FromSource 块内禁止 ConvertBack（F30，Error——块内"可达但无落点"方法禁止， 禁止表）
    /// FromSource 绑定仅正向映射，子源块内反向写回无语义落点；提取期检测块上下文内方法名即 fail-fast。
    /// </summary>
    public static readonly DiagnosticDescriptor ComposeBlockConvertBackForbiddenRule = new(
        id: "NCMAP_F30",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F30_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F30_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F30_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// FromSource 块内禁止 NoAutoInclude（F31，Error——块内"可达但无落点"方法禁止）——
    /// 子源字段全显式声明，块上下文无"自动包含"语义可禁；提取期检测块上下文内方法名即 fail-fast。
    /// </summary>
    public static readonly DiagnosticDescriptor ComposeBlockNoAutoIncludeForbiddenRule = new(
        id: "NCMAP_F31",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F31_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F31_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F31_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// FromSource 块内禁止 AfterMapping（F32，Error——块内"可达但无落点"方法禁止）——
    /// 运行时 Compose 管线（生成委托/ComposeRuntime 兜底）无钩子注入口，声明永不执行；
    /// Error 防用户误以为生效（fail-fast）。
    /// </summary>
    public static readonly DiagnosticDescriptor ComposeBlockAfterMappingForbiddenRule = new(
        id: "NCMAP_F32",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F32_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F32_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F32_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// BeforeMapping 目标方法为 private/protected（F35，可见性契约——镜像 F12）
    /// </summary>
    public static readonly DiagnosticDescriptor BeforeMappingInaccessibleRule = new(
        id: "NCMAP_F35",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F35_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F35_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F35_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// BeforeMapping 目标不是可静态解析的方法组引用（F36，lambda/闭包/捕获变量/局部函数——镜像 F14）
    /// </summary>
    public static readonly DiagnosticDescriptor BeforeMappingNotMethodGroupRule = new(
        id: "NCMAP_F36",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F36_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F36_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F36_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// BeforeMapping 目标为构造器一体化形态（F37，record init-only 全参构造等"构造与赋值不可分"——
    /// 发射端无插入点；双侧通道同报，特性通道 [BeforeMapping] 同形态共用本码）
    /// </summary>
    public static readonly DiagnosticDescriptor BeforeMappingUninsertableTargetRule = new(
        id: "NCMAP_F37",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F37_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F37_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F37_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// FromSource 块内禁止 BeforeMapping（F38，Error——块内"可达但无落点"方法禁止，镜像 F32）——
    /// 运行时 Compose 管线（生成委托/ComposeRuntime 兜底）无钩子注入口，声明永不执行；
    /// Error 防用户误以为生效（fail-fast）。
    /// </summary>
    public static readonly DiagnosticDescriptor ComposeBlockBeforeMappingForbiddenRule = new(
        id: "NCMAP_F38",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F38_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F38_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F38_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// 双通道同目标钩子重复声明（F39，D-h——同对 Before×Before / After×After
    /// 同类并存；交叉组合（特性 After + Fluent Before）不报；判定前经 D-d 签名适用过滤——
    /// ref 非空即签名适用已过，冲突静默合并即语义错误）。双位置发射（Fluent 侧 For 位置 +
    /// 特性侧 DTO 标识位置），镜像 F11 口径。
    /// </summary>
    public static readonly DiagnosticDescriptor HookDualChannelConflictRule = new(
        id: "NCMAP_F39",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F39_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F39_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F39_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    // ==== /：MapWhen 守卫与内联转录诊断族（F40~F45）====
    // F40 方法组契约 / F41 捕获与私有访问（InlineTranscriber） / F42 语句体与不可转录节点（InlineTranscriber）/
    // F43 锚点落空 / F44 重复声明 / F45 主链 Compose × MapWhen 无发射落点

    /// <summary>
    /// MapWhen 方法组契约失败（F40，Error——镜像 F26 家族）——
    /// 谓词方法组不可解析 / 非静态 / 签名不符（单参须 == 源字段类型或 == 源实体类型且返回 bool）/
    /// 可见性不足（private/protected 跨类不可访问）。
    /// </summary>
    public static readonly DiagnosticDescriptor MapWhenMethodContractRule = new(
        id: "NCMAP_F40",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F40_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F40_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F40_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// 内联表达式捕获变量/私有成员访问（F41，Error——InlineTranscriber 禁令）——
    /// 捕获外层局部变量/参数/本地函数/this、私有或受保护成员跨类访问：生成私有静态方法无捕获语义承载，拒绝。
    /// 定位到精确子表达式 span（非整体拒绝）。
    /// </summary>
    public static readonly DiagnosticDescriptor InlineCaptureOrInaccessibleRule = new(
        id: "NCMAP_F41",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F41_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F41_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F41_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// 内联表达式语句体/不可转录节点（F42，Error——InlineTranscriber 禁令）——
    /// 语句体 lambda、dynamic、unsafe/await/查询语法等白名单外节点：转录引擎无法产出
    /// 语义等价的规范化文本，拒绝。定位到精确子表达式 span。
    /// </summary>
    public static readonly DiagnosticDescriptor InlineNotTranscribableRule = new(
        id: "NCMAP_F42",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F42_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F42_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F42_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// MapWhen 锚点落空（F43，Error）——dest 属性无对应映射（无自动匹配源且无显式 .Map），
    /// 或被静态 Ignore 声明排除：守卫无附着映射，声明永不生效，fail-fast。
    /// </summary>
    public static readonly DiagnosticDescriptor MapWhenAnchorNotFoundRule = new(
        id: "NCMAP_F43",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F43_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F43_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F43_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// MapWhen 重复声明（F44，Error—— 单条件）
    /// 同属性多次 MapWhen 声明矛盾：单一条件语义（谓词取反表达负向，叠加属逻辑错误），fail-fast。
    /// </summary>
    public static readonly DiagnosticDescriptor MapWhenDuplicateRule = new(
        id: "NCMAP_F44",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F44_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F44_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F44_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// MapWhen 声明于主链 Compose 映射（F45，Error——镜像 F30~F32"可达但无落点"家族）——
    /// 锚定属性仅由 FromSource 跨源映射覆盖（主链生成器按设计不展开属性赋值链），守卫语义无处安放；
    /// 与 块内允许的不对称：块内有真实发射落点（Update_{Sub}_To_{Target} 逐属性赋值层）。
    /// </summary>
    public static readonly DiagnosticDescriptor MapWhenComposeNoEmissionSiteRule = new(
        id: "NCMAP_F45",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F45_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F45_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F45_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    // =====================：MapSubclass 诊断族（F46~F50）=====================
    // 码位取号自 F46 起（全枚举实读去重）。
    // F46/F49 挂特性侧（Fluent 通道泛型约束 CS0311/CS0314 调用点拦截，生成器不可达）
    // 特性侧落地时接线；本批先落描述符与资源键。

    /// <summary>
    /// [MapSubclass] 特性实参继承关系不满足（F46，Error——特性侧专码， 定案）
    /// typeof 实参无编译期约束（不同于 Fluent 泛型），Source 须派生自基类对源、Target 须派生自基类对目标。
    /// </summary>
    public static readonly DiagnosticDescriptor MapSubclassAttributeInheritanceRule = new(
        id: "NCMAP_F46",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F46_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F46_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F46_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// MapSubclass 登记无效（F47，Error—— + 边界）
    /// ①完全同对 (TSrcDerived, TTargetDerived) 重复登记（部分重叠走最派生优先仲裁不报）；
    /// ②TSrcDerived == TSource 登记基类自身（分派自引用死循环，泛型约束不拦截——约束允许自身）。
    /// </summary>
    public static readonly DiagnosticDescriptor SubclassDuplicateOrSelfRule = new(
        id: "NCMAP_F47",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F47_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F47_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F47_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// MapSubclass 登记子类对落空（F48，Error——仿落空即诊断语义）——
    /// 登记的子类目标类型无可映射公共面/构造约束（合成快照 SkipCodeGeneration 或零可映射属性）。
    /// </summary>
    public static readonly DiagnosticDescriptor SubclassTargetEmptyRule = new(
        id: "NCMAP_F48",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F48_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F48_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F48_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// [MapSubclass] 锚定缺失（F49，Error——特性侧专码，O-1 方案 1 连锁）——
    /// 标注类无 [MapFrom] 锚定（基类映射对不存在），登记未被任何分派面引用（F50① 语义并入：锚定缺失即未引用）。
    /// </summary>
    public static readonly DiagnosticDescriptor MapSubclassAnchorMissingRule = new(
        id: "NCMAP_F49",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F49_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F49_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F49_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// 多态分派静态 advisory（F50，Info 族—— 补码， / S-a 定性编译期静态 advisory）
    /// ②反向不对称可见性：正向基类对分派而反向基类对不分派（反向 dst 固定型）；
    /// ③双实例匹配残留面：Update/MapListInto 的 dst 运行时非子类目标类型时回落基类体（子类专有字段不写入）。
    /// 触发条件 = 声明态静态可判定谓词（SubclassPairs 非空），每对一次；运行时探针不实现（备选弃用）。
    /// </summary>
    public static readonly DiagnosticDescriptor SubclassDispatchAdvisoryRule = new(
        id: "NCMAP_F50",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F50_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F50_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F50_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    // =====================：MapCtorParam 诊断族（F51~F54）=====================
    // （编制期建议码位 F46 起已被
    // MapSubclass 族（F46~F50）消耗，顺延 F51 起—— 差异登记）。

    /// <summary>
    /// MapCtorParam 绑定源解析失败（F51，Error——原 F46 对应位）
    /// 形态 2 源属性名拼错 / 在源类型上不存在 / 无公共 getter（形态 1 非纯路径沿用
    /// LambdaParser 既有诊断机制，不占本码位——B12）。
    /// </summary>
    public static readonly DiagnosticDescriptor CtorParamBindingSourceNotFoundRule = new(
        id: "NCMAP_F51",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F51_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F51_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F51_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// MapCtorParam 绑定形参名不存在（F52，Error—— 定稿）——绑定形参名不存在于
    /// 目标类型任何候选公共构造器参数集（ByRef/指针形参跳过比对）；消息提示可用参数集。
    /// </summary>
    public static readonly DiagnosticDescriptor CtorParamParamNotFoundRule = new(
        id: "NCMAP_F52",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F52_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F52_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F52_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// MapCtorParam 双声明矛盾（F53，Error—— 定稿/ 子规则）
    /// ①同一 ctor 形参重复绑定（duplicate）；②<c>.Map</c> 显式条目与 MapCtorParam 指向
    /// 同一 ctor 形参（map-conflict，record 同形参场景——fail-fast 非静默覆盖，
    /// 对齐 F39 双声明/NCMAP_F23 同锚矛盾纪律）。
    /// </summary>
    public static readonly DiagnosticDescriptor CtorParamDuplicateOrConflictRule = new(
        id: "NCMAP_F53",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F53_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F53_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F53_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// 多等权构造器歧义 advisory（F54，Info—— 机械口径）——目标类型公共构造器
    /// 参数数量并列第一的候选 &gt;1 且 MapCtorParam 绑定集合为空（零预演——与生成端
    /// 选器逻辑同构）；绑定集合非空时不报（显式绑定已表达消解意图）。
    /// </summary>
    public static readonly DiagnosticDescriptor CtorParamAmbiguousConstructorRule = new(
        id: "NCMAP_F54",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F54_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F54_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F54_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    // ============：静默语义治理诊断族（F55~F58）============
    // 级别随族先例：F55/F57 = Warning（F06/F08 解析形态族，声明跳过）；F56/F58 = Error（F26/F40 实参形态族）。
    // F 系登记面 = 描述符 + Register（类尾 Id 反查表）+ resx×2 + Designer.cs + AnalyzerReleases.Unshipped.md + docs/zh-CN/diagnostics/rules.md 六处
    //（② 收口；"四方"为 #82 期口径，已过期）；F 系不参与 check-diagnostics-sync.sh 机械对账
    //（该脚本解析 NCMAP[0-9]+ 域，F 码天然不匹配——保持绿为 NCMAPnnn 域回归口径）。

    /// <summary>
    /// 多段 dest 路径（F55，Warning——F06 同族：解析形态类，声明跳过）——
    /// <c>Map(d =&gt; d.Address.City, ...)</c> 等多段目标路径当前无实现且无诊断，静默丢弃。
    /// 发射条件显式 = <c>dest != null &amp;&amp; dest.IsValidPath &amp;&amp; dest.Segments.Count &gt; 1</c>
    ///（与 F56 严格互斥：dest 为 null 属 F56 场景）。
    /// </summary>
    public static readonly DiagnosticDescriptor MultiSegmentDestNotSupportedRule = new(
        id: "NCMAP_F55",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F55_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F55_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F55_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// 非 lambda 实参（F56，Error——F26/F40 同族：实参形态拒绝）——
    /// dest/src 位传入方法组、委托变量等可编译但非 lambda 形态的实参，当前整条声明静默消失。
    /// 判定落公共层 <c>FluentExtractor.Parsing.cs</c> ParseLambdaArg（覆盖全部终端调用位）。
    /// </summary>
    public static readonly DiagnosticDescriptor NonLambdaArgNotSupportedRule = new(
        id: "NCMAP_F56",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F56_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F56_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F56_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// 块体/三元 lambda（F57，Warning——F08 同族最近先例：动态形态黑名单）——
    /// 语句块 lambda 体或解析消费位含三元表达式，无法归约为属性路径，当前静默丢弃。
    /// 触发面 = 解析消费位（dest 链/源选择器）；MapWhen/Convert 内联谓词三元经 InlineTranscriber 转录，
    /// 不经 LambdaParser——零误报。
    /// </summary>
    public static readonly DiagnosticDescriptor BlockOrTernaryLambdaNotSupportedRule = new(
        id: "NCMAP_F57",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F57_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F57_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F57_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// 实参不可静态求值（F58，Error——F26/F40 同族：实参形态不可静态求值）——
    /// overrideIgnore/Direction 传入变量等不可求值形态，配置意图静默丢失。
    /// 判定面 = 语义常量（字面量/枚举成员/const 标识符，GetConstantValue 有值）放行、求值无值 → 诊断；
    /// 处置 = 声明照常生成 + 无效实参按默认值生效（不跳过声明）——映射行为与修复前逐字节一致。
    /// </summary>
    public static readonly DiagnosticDescriptor NonStaticEvaluableArgIgnoredRule = new(
        id: "NCMAP_F58",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F58_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F58_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F58_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// 同成员真冲突声明（F59，Error——原"跨种类覆盖检测"（Warning，
    /// last-wins 静默覆盖）按 20261008- 修订为"仅真冲突报错"且级别升 Error）
    /// 真冲突清单：①Ignore × 映射分支一切操作（结构规则，Ignore 独唱）②脱敏/格式化 ×
    /// 树递归 ③脱敏 × JSON 展开 ④格式化 × JSON 展开 ⑤同维度重复（含跨段同槽/同 Kind 重复——
    /// 必报错）⑥既有门协同（F23/F17/F66 保持）。无冲突场景（单来源单变换）零诊断。
    /// 写入位 = AddPathDeclaration / ProcessChainMask 等三检测点（真冲突判定）+ 组装侧协同。
    /// </summary>
    public static readonly DiagnosticDescriptor SameAnchorCrossKindOverrideRule = new(
        id: "NCMAP_F59",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F59_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F59_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F59_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// 未知链终端方法（F60，Warning）
    /// 链解析按语法方法名分派；object 继承成员（如 ToString）等可编译但非 Fluent 终端的节点
    /// 原静默跳过（主链丢整链快照、块内仅跳该语句）→ 显式化。丢弃语义本身不变
    ///（行为扩展评估出口登记 R-5）。
    /// </summary>
    public static readonly DiagnosticDescriptor UnknownChainTerminalRule = new(
        id: "NCMAP_F60",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F60_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F60_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F60_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// Fluent lambda 源链深度超上限（F62，Warning）
    /// 成员访问链段数 &gt; 上限 16 → 深链声明丢弃 + 显式诊断。缺陷态：解析器对源链深度
    /// 无界（递归逐段下行），超深链静默落快照且深链形态可撑爆解析深度；16 段以内
    /// 合法面零诊断（上限校准证据：F62_SixteenLevelChain_LegalBoundary）。
    /// </summary>
    public static readonly DiagnosticDescriptor FluentLambdaDepthExceededRule = new(
        id: "NCMAP_F62",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F62_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F62_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F62_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// 环检测访问预算耗尽（F63，Warning）
    /// 密集注册图单起点 DFS 访问数 &gt; NodeBudget 10_000 → 该起点环检测"未决"：
    /// 不发射 NCMAP029、不标记降级，仅本 advisory（预算先例 = TypeRefDeepCopyClassifier.NodeBudget）。
    /// 缺陷态：DfsDetect 递归无记忆化无预算——指数爆炸 + 10_001 深链直接 StackOverflow
    /// 进程崩溃（实测 exit code 0xC0000005）；修复采迭代 DFS + 跨起点记忆化 + 每起点预算。
    /// </summary>
    public static readonly DiagnosticDescriptor CycleDetectionBudgetExceededRule = new(
        id: "NCMAP_F63",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F63_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F63_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F63_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    // Format 终端诊断族（F64~F67——特性侧 ~ 的 Fluent 镜像；
    // 触发落点 = FluentExtractor .Format 提取链，本批先落登记面）
    /// <summary>
    /// Fluent .Format 目标类型非 string（F64，Error——特性侧 NCMAP043 的 Fluent 镜像）
    /// </summary>
    public static readonly DiagnosticDescriptor FormatTargetNotStringRule = new(
        id: "NCMAP_F64",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F64_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F64_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F64_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// Fluent .Format 源属性不可达/非 IFormattable（F65，Error——特性侧 NCMAP044 的 Fluent 镜像）
    /// </summary>
    public static readonly DiagnosticDescriptor FormatSourceNotFormattableRule = new(
        id: "NCMAP_F65",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F65_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F65_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F65_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// Fluent .Format 与 .Convert 同锚点矛盾（F66，Error——镜像 F23 FluentIgnoreConvertConflictRule 形态）
    /// </summary>
    public static readonly DiagnosticDescriptor FormatConvertConflictRule = new(
        id: "NCMAP_F66",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F66_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F66_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F66_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// Fluent .Format 反向派生命中（F67，Warning——特性侧 NCMAP046 的 Fluent 镜像）
    /// </summary>
    public static readonly DiagnosticDescriptor FormatReverseNotConsumedRule = new(
        id: "NCMAP_F67",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F67_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F67_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F67_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    // ============：字段链式换代提取期防呆诊断族（F68~F71）============
    // （四类消息模板）+ 子步骤①：取号自 F68 起（全枚举实读
    // 最大号 F67 顺延——经验 #131）。级别：F68 提醒（Warning）；F69/F70/F71 拦截（Error）。
    // F 系登记面 = 描述符 + Register + resx×2 + Designer + Unshipped + rules.md（六处；
    // F 系不入 check-diagnostics-sync.sh 的 NCMAPnnn 对账域）。

    /// <summary>
    /// 裸 Member 提醒（F68，Warning—— ①/ 防呆①）
    /// <c>.Member(d =&gt; d.X)</c> 之后段内无任何跟随（无 From/FromSource/Ignore、无任何配置）时
    /// 提醒：该字段段为空声明，不产生任何映射/忽略效果。合法面（段内有至少一个声明或配置）零诊断。
    /// 段结束边界 = 下一个 Member / 对级方法 / 链尾（提取器字段上下文状态机）。
    /// </summary>
    public static readonly DiagnosticDescriptor BareMemberNoFollowUpRule = new(
        id: "NCMAP_F68",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F68_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F68_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F68_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// 省略来源但无同名字段（F69，Error—— ①/ 防呆②）
    /// 配置段（Mask/Format/FromJson/MapTree/MapWhen/NoAutoInclude 等）省略 From 时按目标字段名
    /// 解析同名源属性；源类型（或 FromSource 子源）不存在同名成员时报错——旧字段级写法该场景
    /// 由 NCMAP001（自动匹配口径）静默承载，新形态消费适配后显式化（fail-fast，防静默丢声明）。
    /// </summary>
    public static readonly DiagnosticDescriptor OmittedSourceNoSameNameRule = new(
        id: "NCMAP_F69",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F69_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F69_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F69_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// 同维度重复声明（F70，Error—— ①/ 防呆③ + F59 真冲突⑤）
    /// 同字段的来源槽/变换槽重复声明（提取期可达形态 = 跨段：编译期结构已保证单段内来源唯一；
    /// MemberHead 与 MemberConfig 同名方法两段可达）。承载语义：跨段同槽/同 Kind 重复**必报错**
    ///（003-P1-3 旧面边界：今日同 Kind last-wins 静默处显式化）；NoAutoInclude 清单重复幂等
    /// 不在此列（HashSet 承载，现状行为保持）。
    /// </summary>
    public static readonly DiagnosticDescriptor SameDimensionDuplicateRule = new(
        id: "NCMAP_F70",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F70_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F70_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F70_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// MapWhen 整源形态类型参不符（F71，Error—— ①/ 防呆③）
    /// 新面 MapWhen 混合载：T1 字段值形态（非泛型，内联 lambda 直接可用）/ T2 整源形态（泛型，
    /// 显式类型参 <c>.MapWhen&lt;TSource&gt;(...)</c>）。显式类型参须 ∈ {TProp（目标字段类型）,
    /// TSource（源类型）}——不符报错（防"写错类型参被静默丢弃条件"）。合法面零诊断。
    /// </summary>
    public static readonly DiagnosticDescriptor MapWhenTypeArgMismatchRule = new(
        id: "NCMAP_F71",
        title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F71_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F71_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
        category: "NCMapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP_F71_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

    /// <summary>
    /// 注册 Fluent 描述符到 Id 反查表（DiagnosticDescriptors.ByIdLazy 由 DiagnosticDescriptors.BuildById 调用——
    /// 本方法在 BuildById 内逐条登记，保持单一注册点）
    /// </summary>
    /// <param name="map">Id → Descriptor 反查表（BuildById 构造中）</param>
    internal static void Register(System.Collections.Generic.Dictionary<string, DiagnosticDescriptor> map)
    {
        map[FluentPropertyNotFoundRule.Id] = FluentPropertyNotFoundRule;
        map[FluentLambdaRootNotParameterRule.Id] = FluentLambdaRootNotParameterRule;
        map[FluentNullConditionalRule.Id] = FluentNullConditionalRule;
        map[FluentIndexerRule.Id] = FluentIndexerRule;
        map[FluentMethodCallRule.Id] = FluentMethodCallRule;
        map[FluentDynamicConfigRule.Id] = FluentDynamicConfigRule;
        map[DualDriverConflictRule.Id] = DualDriverConflictRule;
        map[AfterMappingInaccessibleRule.Id] = AfterMappingInaccessibleRule;
        map[AfterMappingNotMethodGroupRule.Id] = AfterMappingNotMethodGroupRule;
        map[MapperConfigNoMatchingMethodRule.Id] = MapperConfigNoMatchingMethodRule;
        map[FluentDuplicateTypePairRule.Id] = FluentDuplicateTypePairRule;
        // ConvertBack 诊断族（F17~F22）
        map[FluentIgnoreConvertBackConflictRule.Id] = FluentIgnoreConvertBackConflictRule;
        map[ConvertBackReverseTargetNotFoundRule.Id] = ConvertBackReverseTargetNotFoundRule;
        map[ConvertBackInaccessibleRule.Id] = ConvertBackInaccessibleRule;
        map[ConvertBackNotMethodGroupRule.Id] = ConvertBackNotMethodGroupRule;
        map[ConvertBackShapeMismatchRule.Id] = ConvertBackShapeMismatchRule;
        map[ConvertBackAnchorNotWritableRule.Id] = ConvertBackAnchorNotWritableRule;
        // Convert 诊断族（F23~F28—— 更名）
        map[FluentIgnoreConvertConflictRule.Id] = FluentIgnoreConvertConflictRule;
        map[ConvertSourceNotFoundRule.Id] = ConvertSourceNotFoundRule;
        map[ConvertInaccessibleRule.Id] = ConvertInaccessibleRule;
        map[ConvertNotMethodGroupRule.Id] = ConvertNotMethodGroupRule;
        map[ConvertShapeMismatchRule.Id] = ConvertShapeMismatchRule;
        map[ConvertAnchorNotWritableRule.Id] = ConvertAnchorNotWritableRule;
        // FromSource 补充源绑定诊断族（F29-F32）
        map[ComposeConflictRule.Id] = ComposeConflictRule;
        map[ComposeBlockConvertBackForbiddenRule.Id] = ComposeBlockConvertBackForbiddenRule;
        map[ComposeBlockNoAutoIncludeForbiddenRule.Id] = ComposeBlockNoAutoIncludeForbiddenRule;
        map[ComposeBlockAfterMappingForbiddenRule.Id] = ComposeBlockAfterMappingForbiddenRule;
        // BeforeMapping 诊断族（F35~F38）
        map[BeforeMappingInaccessibleRule.Id] = BeforeMappingInaccessibleRule;
        map[BeforeMappingNotMethodGroupRule.Id] = BeforeMappingNotMethodGroupRule;
        map[BeforeMappingUninsertableTargetRule.Id] = BeforeMappingUninsertableTargetRule;
        map[ComposeBlockBeforeMappingForbiddenRule.Id] = ComposeBlockBeforeMappingForbiddenRule;
        // 双通道钩子冲突
        map[HookDualChannelConflictRule.Id] = HookDualChannelConflictRule;
        // MapSubclass 诊断族（F46~F50）
        map[MapSubclassAttributeInheritanceRule.Id] = MapSubclassAttributeInheritanceRule;
        map[SubclassDuplicateOrSelfRule.Id] = SubclassDuplicateOrSelfRule;
        map[SubclassTargetEmptyRule.Id] = SubclassTargetEmptyRule;
        map[MapSubclassAnchorMissingRule.Id] = MapSubclassAnchorMissingRule;
        map[SubclassDispatchAdvisoryRule.Id] = SubclassDispatchAdvisoryRule;
        // /：MapWhen 守卫与内联转录诊断族（F40~F45）
        map[MapWhenMethodContractRule.Id] = MapWhenMethodContractRule;
        map[InlineCaptureOrInaccessibleRule.Id] = InlineCaptureOrInaccessibleRule;
        map[InlineNotTranscribableRule.Id] = InlineNotTranscribableRule;
        map[MapWhenAnchorNotFoundRule.Id] = MapWhenAnchorNotFoundRule;
        map[MapWhenDuplicateRule.Id] = MapWhenDuplicateRule;
        map[MapWhenComposeNoEmissionSiteRule.Id] = MapWhenComposeNoEmissionSiteRule;
        // MapCtorParam 诊断族（F51~F54）
        map[CtorParamBindingSourceNotFoundRule.Id] = CtorParamBindingSourceNotFoundRule;
        map[CtorParamParamNotFoundRule.Id] = CtorParamParamNotFoundRule;
        map[CtorParamDuplicateOrConflictRule.Id] = CtorParamDuplicateOrConflictRule;
        map[CtorParamAmbiguousConstructorRule.Id] = CtorParamAmbiguousConstructorRule;
        // 静默语义治理诊断族（F55~F58）
        map[MultiSegmentDestNotSupportedRule.Id] = MultiSegmentDestNotSupportedRule;
        map[NonLambdaArgNotSupportedRule.Id] = NonLambdaArgNotSupportedRule;
        map[BlockOrTernaryLambdaNotSupportedRule.Id] = BlockOrTernaryLambdaNotSupportedRule;
        map[NonStaticEvaluableArgIgnoredRule.Id] = NonStaticEvaluableArgIgnoredRule;
        // 静默站点补诊断族（F59~F61）
        map[SameAnchorCrossKindOverrideRule.Id] = SameAnchorCrossKindOverrideRule;
        map[UnknownChainTerminalRule.Id] = UnknownChainTerminalRule;
        // 符号解析防御族（F62~F63）
        map[FluentLambdaDepthExceededRule.Id] = FluentLambdaDepthExceededRule;
        map[CycleDetectionBudgetExceededRule.Id] = CycleDetectionBudgetExceededRule;
        // Format 终端诊断族（F64~F67）
        map[FormatTargetNotStringRule.Id] = FormatTargetNotStringRule;
        map[FormatSourceNotFormattableRule.Id] = FormatSourceNotFormattableRule;
        map[FormatConvertConflictRule.Id] = FormatConvertConflictRule;
        map[FormatReverseNotConsumedRule.Id] = FormatReverseNotConsumedRule;
        // ② 提前落位（验收依赖——反查表缺项时报告层兜底
        // NCMAP099 且实参错位，诊断组探针实证）：防呆族 F68~F71
        map[BareMemberNoFollowUpRule.Id] = BareMemberNoFollowUpRule;
        map[OmittedSourceNoSameNameRule.Id] = OmittedSourceNoSameNameRule;
        map[SameDimensionDuplicateRule.Id] = SameDimensionDuplicateRule;
        map[MapWhenTypeArgMismatchRule.Id] = MapWhenTypeArgMismatchRule;
    }
}
