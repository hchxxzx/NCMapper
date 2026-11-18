using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NCMapper.SourceGen.Resources;
namespace NCMapper.SourceGen;

/// <summary>
/// 属性映射路径来源 — 一等字段
/// <para>参与 NCMAP009 判定（推导豁免/显式参与）、反向诊断措辞、相等性/哈希（H19 增量缓存正确性）；
/// 过渡期 IsFlattened=true 统一多段语义（由 SourcePath.Count &gt; 1 隐含），来源由本字段独立区分。</para>
/// </summary>
internal enum PathOrigin
{
    /// <summary>
    /// 直接映射（单段；含同名匹配与显式 [MapProperty] FromField 单段）
    /// </summary>
    Direct = 0,

    /// <summary>
    /// 推导扁平化（命名约定自动拆解目标属性名，尽力而为语义）
    /// </summary>
    DerivedFlatten = 1,

    /// <summary>
    /// 显式点路径（[MapProperty] FromField 含 '.'，契约语义，失败必须显性）
    /// </summary>
    ExplicitDotPath = 2,
}

/// <summary>
/// 转换方法引用形态— [Convert(typeof(X), nameof(X.M))] /
/// [Convert(nameof(M))] 静态方法解析命中的签名模板，T-004a 起随模型三字段物化
/// </summary>
internal enum ConverterShape
{
    /// <summary>未命中任何模板（解析失败或未标注方法引用形态）</summary>
    None = 0,

    /// <summary>T0 无参常量：static TTarget M()</summary>
    T0Constant = 1,

    /// <summary>T1 单值：static TTarget M(TField)（字段值入参，可跨实体复用）</summary>
    T1SingleValue = 2,

    /// <summary>T2 多字段组合：static TTarget M(TSource)（源对象入参；Q3 优先序：单参同时==源实体==字段类型时判本形态）</summary>
    T2MultiField = 3,

    /// <summary>T3 组合+字段值：static TTarget M(TSource, TField)</summary>
    T3Combined = 4,

    /// <summary>T1 守卫变体：字段基准 Nullable&lt;T&gt; + 参数 T（R6 口径 3 守卫行）——
    /// 三字段不含方法签名，Nullable 形态由物化期解析器编码进 Shape，发射端生成
    /// 「字段.HasValue ? M(字段.Value) : default」三元（null 不调用，目标保持默认， null 语义表）</summary>
    T1Guarded = 5,

    /// <summary>T3 守卫变体：字段基准 Nullable&lt;T&gt; + 字段参数 T——
    /// 生成「字段.HasValue ? M(src, 字段.Value) : default」三元（实体实参 + 守卫字段实参）</summary>
    T3Guarded = 6,
}

/// <summary>
/// 条件守卫谓词形态— MapWhen 谓词入参形态判定
/// （判定规则镜像 Convert 族"单参 == 源实体类型判整源，否则判字段值"）。
/// </summary>
internal enum ConditionShape
{
    /// <summary>无条件（未声明 MapWhen；反向派生剥离后的默认态）</summary>
    None = 0,

    /// <summary>T1 字段值：谓词入参 = 源字段值（最常用；条件 → 转换 → 赋值链路， 转换前求值）</summary>
    FieldValue = 1,

    /// <summary>T2 整源：谓词入参 = 整个源对象（多字段判断）</summary>
    WholeSource = 2,
}
    /// <summary>
    /// 诊断描述符定义 — NCMAP001~028（已并入； 由 新增， 由 新增， 由 新增， / 由 新增）, NCMAP099
    /// </summary>
    internal static class DiagnosticDescriptors
    {
        /// <summary>Id → Descriptor 反查表（快照仅持 Id，发射期回查）。
        /// Lazy 初始化规避静态字段文本顺序约束（任意位置声明安全，首次 FromId 访问时才构建）</summary>
        private static readonly System.Lazy<System.Collections.Generic.Dictionary<string, DiagnosticDescriptor>> ByIdLazy =
            new(BuildById);

        /// <summary>
        /// 构建反查表（全部 Descriptor 字段初始化后执行）
        /// </summary>
        private static System.Collections.Generic.Dictionary<string, DiagnosticDescriptor> BuildById()
        {
            var map = new System.Collections.Generic.Dictionary<string, DiagnosticDescriptor>(System.StringComparer.Ordinal)
            {
                [SourceFieldNotFoundRule.Id] = SourceFieldNotFoundRule,
                [TypeIncompatibleRule.Id] = TypeIncompatibleRule,
                [NoPropertiesMappedRule.Id] = NoPropertiesMappedRule,
                [ComposeFieldNotFoundRule.Id] = ComposeFieldNotFoundRule,
                [UnmappedTargetMemberRule.Id] = UnmappedTargetMemberRule,
                [UnmappedSourceMemberRule.Id] = UnmappedSourceMemberRule,
                [DuplicateFieldMappingRule.Id] = DuplicateFieldMappingRule,
                [EnumNoOverlapRule.Id] = EnumNoOverlapRule,
                [NonPublicTypeSkippedRule.Id] = NonPublicTypeSkippedRule,
                [RecordCircularReferenceRule.Id] = RecordCircularReferenceRule,
                [DeepCopyGraphUnknownRule.Id] = DeepCopyGraphUnknownRule,
                [ReverseMappingSkippedRule.Id] = ReverseMappingSkippedRule,
                [ReverseMappingWriteBackNotInstantiableRule.Id] = ReverseMappingWriteBackNotInstantiableRule,
                [GenerationFailedRule.Id] = GenerationFailedRule,
                [MaskTypeMismatchRule.Id] = MaskTypeMismatchRule,
                [MaskTargetNotStringRule.Id] = MaskTargetNotStringRule,
                [NoAutoIncludeOnReferenceRule.Id] = NoAutoIncludeOnReferenceRule,
                [ArrayContainsInExpressionRule.Id] = ArrayContainsInExpressionRule,
                [MissingMapFromWithRelationConfigRule.Id] = MissingMapFromWithRelationConfigRule,
                [DtoNamingWithoutMapFromRule.Id] = DtoNamingWithoutMapFromRule,
                [InvalidGlobalConfigValueRule.Id] = InvalidGlobalConfigValueRule,
                [ExplicitMappingHitGlobalIgnoreRule.Id] = ExplicitMappingHitGlobalIgnoreRule,
                [MapIgnoreWithOverrideRule.Id] = MapIgnoreWithOverrideRule,
                [EnumNameCollisionDegradedRule.Id] = EnumNameCollisionDegradedRule,
                [EnumValueCollisionDegradedRule.Id] = EnumValueCollisionDegradedRule,
                [MutualReferenceRule.Id] = MutualReferenceRule,
                [MaskPositionRule.Id] = MaskPositionRule,
                [SkippedIncompatibleMemberRule.Id] = SkippedIncompatibleMemberRule,
                [InaccessibleMemberRule.Id] = InaccessibleMemberRule,
                [DotPathFeatureNotConsumedRule.Id] = DotPathFeatureNotConsumedRule,
                [InitOnlyUpdateSkipRule.Id] = InitOnlyUpdateSkipRule,
                [ConvertUsingNotConsumedInReverseRule.Id] = ConvertUsingNotConsumedInReverseRule,
                [MaskReverseWriteBackRule.Id] = MaskReverseWriteBackRule,
                [ConverterMethodInaccessibleRule.Id] = ConverterMethodInaccessibleRule,
                [ConverterMethodAmbiguousRule.Id] = ConverterMethodAmbiguousRule,
                [ConverterMethodSignatureMismatchRule.Id] = ConverterMethodSignatureMismatchRule,
                // 特性侧生命周期钩子诊断（[BeforeMapping]/[AfterMapping]
                // 镜像 ConvertUsing 族三码形态 035/036/037 → 038/039/040）
                [LifecycleHookMethodUnresolvedRule.Id] = LifecycleHookMethodUnresolvedRule,
                [LifecycleHookSignatureMismatchRule.Id] = LifecycleHookSignatureMismatchRule,
                [LifecycleHookAmbiguousRule.Id] = LifecycleHookAmbiguousRule,
                // 特性侧 [MapWhen] 谓词解析失败（镜像 Convert 族形态）
                [MapWhenAttributeResolutionRule.Id] = MapWhenAttributeResolutionRule,
                // 嵌套互调环降级诊断反查登记
                // 漏登记 = FromId 查空回落 NCMAP099（3 占位符 vs 发射点 2 实参 → FormatException）
                [NestedCycleDegradedRule.Id] = NestedCycleDegradedRule,
                // [MapFormat] 家族三码（目标非 string / 源非
                // IFormattable / 045 与 Convert 同标——漏登记 = FromId 查空回落 NCMAP099）
                [MapFormatTargetNotStringRule.Id] = MapFormatTargetNotStringRule,
                [MapFormatSourceNotFormattableRule.Id] = MapFormatSourceNotFormattableRule,
                [MapFormatWithConvertRule.Id] = MapFormatWithConvertRule,
                // 反向拦截码（ReversePlanner 反向派生命中即报）
                [MapFormatReverseNotConsumedRule.Id] = MapFormatReverseNotConsumedRule,
                // [MapNoAutoMatch] 白名单下变换特性无 [MapProperty] 头声明
                //（漏登记 = FromId 查空回落 NCMAP099，DiagnosticConsistencyTests 必红）
                [TransformWithoutMapPropertyUnderNoAutoMatchRule.Id] = TransformWithoutMapPropertyUnderNoAutoMatchRule,
            };
            // Fluent 通道描述符（NCMAP_F02/F04~F08/F11~F15）注册
            DiagnosticDescriptorsFluent.Register(map);
            return map;
        }

        /// <summary>
        /// 按诊断 Id 回查静态 Descriptor（快照发射期用；未知 Id 返回 null）
        /// </summary>
        /// <param name="id">诊断 Id（如 "NCMAP001"）</param>
        /// <returns>静态 Descriptor；未知 Id 时为 null</returns>
        public static DiagnosticDescriptor? FromId(string id)
            => ByIdLazy.Value.TryGetValue(id, out var descriptor) ? descriptor : null;

        /// <summary>
        /// 源字段未找到 — DTO 属性标注了 [MapProperty] 但指定源字段在源类型中不存在
        /// </summary>
        public static readonly DiagnosticDescriptor SourceFieldNotFoundRule = new(
            id: "NCMAP001",
            title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP001_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP001_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            category: "NCMapper",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP001_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

        /// <summary>
        /// 类型不兼容 — 目标属性类型与源字段类型无法直接赋值
        /// </summary>
        public static readonly DiagnosticDescriptor TypeIncompatibleRule = new(
            id: "NCMAP002",
            title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP002_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP002_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            category: "NCMapper",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP002_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

        /// <summary>
        /// 无可映射属性 — DTO 标注了 [MapFrom] 但没有任何属性能匹配
        /// </summary>
        public static readonly DiagnosticDescriptor NoPropertiesMappedRule = new(
            id: "NCMAP003",
            title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP003_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP003_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            category: "NCMapper",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP003_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

        /// <summary>
        /// Compose 跨表字段未找到 — P1-SHOULD-9
        /// <para>[MapProperty(typeof(SubSource), "FieldName")] 指定的字段在 SubSource 类型中未找到</para>
        /// </summary>
        public static readonly DiagnosticDescriptor ComposeFieldNotFoundRule = new(
            id: "NCMAP005",
            title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP005_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP005_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            category: "NCMapper",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP005_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

        /// <summary>
        /// 目标属性未映射 — 目标属性既无匹配源属性、也无 [MapIgnore]
        /// <para>对标 Mapperly RMG012，最高频诊断场景：重构时属性改名但忘记更新映射配置</para>
        /// </summary>
        public static readonly DiagnosticDescriptor UnmappedTargetMemberRule = new(
            id: "NCMAP006",
            title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP006_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP006_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            category: "NCMapper",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP006_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

        /// <summary>
        /// 源属性未映射 — 源属性既无匹配目标属性、也无显式忽略
        /// <para>对标 Mapperly RMG020，提示源实体新增了属性但 DTO 未同步</para>
        /// </summary>
        public static readonly DiagnosticDescriptor UnmappedSourceMemberRule = new(
            id: "NCMAP007",
            title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP007_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP007_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            category: "NCMapper",
            defaultSeverity: DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP007_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

        /// <summary>
        /// 重复 [MapProperty] 指向同一源属性 — 多个目标属性映射到同一个源字段
        /// <para>对标 Mapperly RMG074，通常为复制粘贴错误</para>
        /// </summary>
        public static readonly DiagnosticDescriptor DuplicateFieldMappingRule = new(
            id: "NCMAP009",
            title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP009_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP009_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            category: "NCMapper",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP009_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

        /// <summary>
        /// 枚举映射成员无重叠 — 源枚举和目标枚举的成员名称无任何重叠
        /// <para>对标 Mapperly RMG003/037，枚举值改动后映射静默失败</para>
        /// </summary>
        public static readonly DiagnosticDescriptor EnumNoOverlapRule = new(
            id: "NCMAP010",
            title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP010_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP010_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            category: "NCMapper",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP010_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

        /// <summary>
        /// 非 public 类型跳过源生成器 — internal/private 类型标注 [MapFrom] 时跳过代码生成
        /// <para>生成的映射器类是 internal，当前版本对 non-public 类型不生成编译时代码，
        /// 自动 fallback 到运行时引擎路径。</para>
        /// <para>典型场景：测试项目的 private 嵌套 mutant 模型、internal 内部 DTO</para>
        /// </summary>
        public static readonly DiagnosticDescriptor NonPublicTypeSkippedRule = new(
            id: "NCMAP011",
            title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP011_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP011_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            category: "NCMapper",
            defaultSeverity: DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP011_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

        /// <summary>
        /// Record 循环引用/不可解析图 DeepCopy 回退 — record 类型的属性图存在循环引用（自引用或双向引用）
        /// 或静态不可解析边（object/接口/virtual 重写等， T5 D4），
        /// 构造器路径无法在 new T(...) 前预登记 dst 到 CycleCache，存在无限递归风险。
        /// 自动回退到运行时 DeepCopyRuntimeOnly 路径（基于 ConditionalWeakTable 的循环引用检测）。
        /// </summary>
        public static readonly DiagnosticDescriptor RecordCircularReferenceRule = new(
            id: "NCMAP012",
            title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP012_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP012_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            category: "NCMapper",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP012_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

        /// <summary>
        /// DeepCopy 图静态不可解析 — 类型的深拷贝属性图含不可解析边
        /// （object/dynamic/接口类型/开放泛型/未约束泛型参数/可被重写的 virtual 引用属性），
        /// 静态分析无法证明无环，该类型 DeepCopy 保留完整循环检测（保守）。
        /// <para>Info 级别：非配置错误，是静态分析的保守处理告知。功能不受影响，
        /// 但该类型无法享受方案 F 无环图的零检测快路径。
        /// 参数 0=类型名、1=属性名、2=不可解析的属性类型名（
        /// T3）。</para>
        /// </summary>
        public static readonly DiagnosticDescriptor DeepCopyGraphUnknownRule = new(
            id: "NCMAP016",
            title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP016_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP016_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            category: "NCMapper",
            defaultSeverity: DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP016_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

        /// <summary>
        /// 反向映射属性跳过 — 正向映射中的某些属性无法反向映射（无源属性/setter 不可用/集合索引反向登记/写回中间段不可写）
        /// <para>Info 级别：这些跳过是语义上的固有限制，非配置错误</para>
        /// <para>语义收窄：点路径/扁平化反向写回已实现，不再跳过；
        /// 保留场景：Convert 无源属性、叶/中间段 setter 不可用、集合索引路径反向（登记不适用）。
        /// 中间节点不可实例化（接口/抽象/无公共无参构造器）由 NCMAP031（Error）承载。</para>
        /// </summary>
        public static readonly DiagnosticDescriptor ReverseMappingSkippedRule = new(
            id: "NCMAP013",
            title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP013_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP013_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            category: "NCMapper",
            defaultSeverity: DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP013_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

        /// <summary>
        /// 反向写回中间节点不可实例化 — 反向路径写回的目标链中间节点为接口/抽象类/无公共无参构造器，
        /// 无法在中间节点为 null 时创建新实例（不生成非法代码，Error 级编译阻断）
        /// <para>Error 级别：该配置在反向方向必然失败（正向完全合法），属配置错误——编译期阻断优于生成非法代码。
        /// 参数 0=中间节点类型名、1=段属性名、2=完整路径、3=反向源类型名（DTO）、4=反向目标类型名（实体）。</para>
        /// </summary>
        public static readonly DiagnosticDescriptor ReverseMappingWriteBackNotInstantiableRule = new(
            id: "NCMAP031",
            title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP031_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP031_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            category: "NCMapper",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP031_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

        /// <summary>
        /// NCMAP032: 更新期 init-only 目标属性跳过（仅创建路径可写）
        /// <para>skipInitOnly 跳过零诊断补齐——更新路径（已有目标对象）无法对
        /// init-only 属性赋值，属性仅由创建路径写入；正向更新与反向更新同命中。参数 0=属性名、1=源类型名、2=目标类型名。</para>
        /// </summary>
        public static readonly DiagnosticDescriptor InitOnlyUpdateSkipRule = new(
            id: "NCMAP032",
            title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP032_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP032_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            category: "NCMapper",
            defaultSeverity: DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP032_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

        /// <summary>
        /// NCMAP033: 反向映射默认不消费 [Convert] 转换器
        /// <para>：反向（DTO→实体）默认不消费 [Convert]（转换器泛型绑定正向方向），
        /// 按类型转换路径写入；如需反向自定义转换，请标注 [ConvertBack]。
        /// 参数 0=属性名、1=反向源类型名（DTO）、2=反向目标类型名（实体）。</para>
        /// </summary>
        public static readonly DiagnosticDescriptor ConvertUsingNotConsumedInReverseRule = new(
            id: "NCMAP033",
            title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP033_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP033_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            category: "NCMapper",
            defaultSeverity: DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP033_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

        /// <summary>
        /// NCMAP034: 带 [Mask] 的 DTO 属性参与反向写回
        /// <para>脱敏值 round-trip 风险——脱敏有损不可逆，反向写回会把
        /// 脱敏后的值原样写入实体。参数 0=属性名、1=反向源类型名（DTO）、2=反向目标类型名（实体）。</para>
        /// </summary>
        public static readonly DiagnosticDescriptor MaskReverseWriteBackRule = new(
            id: "NCMAP034",
            title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP034_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP034_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            category: "NCMapper",
            defaultSeverity: DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP034_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

        /// <summary>
        /// NCMAP035: 转换方法不可达 — 方法引用形态解析失败（名称不存在或可访问性不足）
        /// <para> 白名单（public / internal 同程序集 / protected internal 同程序集腿）之外拒绝；
        /// 单参形态解析域 = 标注属性所属类型自身（nameof 限定名截断在此显性报错）。
        /// 参数 0=属性名、1=方法名、2=锚定类型名。</para>
        /// </summary>
        public static readonly DiagnosticDescriptor ConverterMethodInaccessibleRule = new(
            id: "NCMAP035",
            title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP035_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP035_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            category: "NCMapper",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP035_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

        /// <summary>
        /// NCMAP036: 转换方法重载歧义 — 模板匹配后多重载同时命中（不新增按签名选择的构造形态）
        /// <para>参数 0=属性名、1=方法名、2=锚定类型名。</para>
        /// </summary>
        public static readonly DiagnosticDescriptor ConverterMethodAmbiguousRule = new(
            id: "NCMAP036",
            title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP036_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP036_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            category: "NCMapper",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP036_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

        /// <summary>
        /// NCMAP037: 转换方法签名不匹配 — 无任何重载命中四模板，或被形态排除，或锚定类型为开放泛型
        /// <para>参数 0=属性名、1=方法名、2=锚定类型名、3=目标属性类型名、4=排除/失配细节。</para>
        /// </summary>
        public static readonly DiagnosticDescriptor ConverterMethodSignatureMismatchRule = new(
            id: "NCMAP037",
            title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP037_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP037_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            category: "NCMapper",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP037_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

        /// <summary>
        /// NCMAP038: 生命周期钩子方法不可解析 — 锚定类型上无该名称静态方法或可见性不足
        /// <para>覆盖 [BeforeMapping]/[AfterMapping]（镜像 NCMAP035 两判据形态）；
        /// 消息含"考虑双参形态指定宿主"改写指引（nameof 限定名截断场景）。</para>
        /// <para>参数 0=特性名、1=方法名、2=锚定类型名。</para>
        /// </summary>
        public static readonly DiagnosticDescriptor LifecycleHookMethodUnresolvedRule = new(
            id: "NCMAP038",
            title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP038_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP038_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            category: "NCMapper",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP038_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

        /// <summary>
        /// NCMAP039: 生命周期钩子方法签名不匹配 — 非 void 返回/参数数目≠2/参数类型与映射对不兼容，或锚定类型为开放泛型
        /// <para>覆盖 [BeforeMapping]/[AfterMapping]（镜像 NCMAP037 含开放泛型判据形态；
        /// 仅声明所在对（[MapFrom] 本对）产出——跨对传播签名不适配为 D-d 正常过滤路径，静默不诊断）。</para>
        /// <para>参数 0=特性名、1=方法签名、2=期望签名形态。</para>
        /// </summary>
        public static readonly DiagnosticDescriptor LifecycleHookSignatureMismatchRule = new(
            id: "NCMAP039",
            title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP039_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP039_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            category: "NCMapper",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP039_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

        /// <summary>
        /// NCMAP040: 生命周期钩子方法重载歧义 — 多重载同时兼容映射对签名且无精确匹配
        /// <para>覆盖 [BeforeMapping]/[AfterMapping]（镜像 NCMAP036 形态）；
        /// 精确签名（参数类型与 (TSource,TTarget) 相等）命中唯一时优先，无精确多兼容时报本码。</para>
        /// <para>参数 0=特性名、1=方法名、2=锚定类型名。</para>
        /// </summary>
        public static readonly DiagnosticDescriptor LifecycleHookAmbiguousRule = new(
            id: "NCMAP040",
            title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP040_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP040_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            category: "NCMapper",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP040_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

        /// <summary>
        /// NCMAP041: [MapWhen] 特性谓词方法解析失败 — 方法不存在/非静态/签名不符（单参须 == 源字段类型
        /// 或 == 源实体类型且返回 bool）/可见性不足（private/protected 跨类不可访问）
        /// <para>覆盖特性侧 [MapWhen(methodName)]（形态镜像 Convert 族 NCMAP037）；
        /// 方法在被标注属性声明类型上按名解析（单类型自足）。</para>
        /// <para>参数 0=属性名、1=声明类型显示名、2=方法名、3=原因。</para>
        /// </summary>
        public static readonly DiagnosticDescriptor MapWhenAttributeResolutionRule = new(
            id: "NCMAP041",
            title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP041_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP041_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            category: "NCMapper",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP041_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

        /// <summary>
        /// 源生成失败 — 单个映射的代码生成过程中发生未预期异常
        /// <para>L-08→M：捕获异常后报告此诊断并继续处理其他映射，避免一个映射失败导致全部中断</para>
        /// </summary>
        public static readonly DiagnosticDescriptor GenerationFailedRule = new(
            id: "NCMAP099",
            title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP099_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP099_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            category: "NCMapper.SourceGen",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP099_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

        /// <summary>
        /// [Mask] 指定的类型未实现 IMask 接口
        /// </summary>
        public static readonly DiagnosticDescriptor MaskTypeMismatchRule = new(
            id: "NCMAP014",
            title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP014_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP014_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            category: "NCMapper",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP014_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

        /// <summary>
        /// NCMAP015: [Mask] 目标属性类型必须为 string
        /// </summary>
        public static readonly DiagnosticDescriptor MaskTargetNotStringRule = new(
            id: "NCMAP015",
            title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP015_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP015_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            category: "NCMapper",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP015_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

        /// <summary>
        /// NCMAP017: [MapNoAutoInclude] 标注在引用导航属性上通常无意义
        /// </summary>
        public static readonly DiagnosticDescriptor NoAutoIncludeOnReferenceRule = new(
            id: "NCMAP017",
            title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP017_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP017_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            category: "NCMapper",
            defaultSeverity: DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP017_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

        /// <summary>
        /// NCMAP018: 表达式树 lambda 中数组 Contains 绑定到 MemoryExtensions.Contains（.NET 10/C# 14 破坏性变更）
        /// <para>在 Expression&lt;Func&lt;...&gt;&gt; lambda 内，T[].Contains(x) 在 .NET 10/C# 14 下因重载决议变更，
        /// 绑定到 MemoryExtensions.Contains(ReadOnlySpan&lt;T&gt;, T) 而非 Enumerable.Contains(IEnumerable&lt;T&gt;, T)，
        /// 导致 FreeSql/EF Core 等表达式树解析器抛 NotSupportedException（它们仅识别 Enumerable.Contains）。</para>
        /// <para>net8/net9（C# 13）下 T[].Contains 仍绑定 Enumerable.Contains，不触发本诊断。</para>
        /// </summary>
        public static readonly DiagnosticDescriptor ArrayContainsInExpressionRule = new(
            id: "NCMAP018",
            title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP018_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP018_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            category: "NCMapper",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP018_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

        /// <summary>
        /// NCMAP019: 标注映射配置特性但缺少 [MapFrom]（场景①——配置无激活路径，Error）
        /// <para>T1 拆码收窄 + 触发场景 = 类型自身或其属性成员标注
        /// 映射配置特性（九项家族：[MapProperty]/[Convert]/[ConvertBack]/[MapIgnore]/
        /// [BeforeMapping]/[AfterMapping]/[Mask]/[MapWhen]/[MapNoAutoInclude]，均属性级）
        /// 但未标 [MapFrom]——配置无激活路径、静默失效，成对规则编译期强制信号（Error）。
        /// [MapTree] 为类级树构建定义非映射对，不入本清单。
        /// 场景②（纯 Dto 命名）拆出至 NCMAP042（Info）。</para>
        /// </summary>
        public static readonly DiagnosticDescriptor MissingMapFromWithRelationConfigRule = new(
            id: "NCMAP019",
            title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP019_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP019_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            category: "NCMapper",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP019_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

        /// <summary>
        /// NCMAP042: Dto 命名模式但缺少 [MapFrom]（场景②——AOT/Trimmed 反射回退不可用提示，Info）
        /// <para> T1 新增（后顺延）：触发场景 = 类型名以 Dto/DTO 结尾、
        /// 未标 [MapFrom]、且未命中映射配置家族（命中者归 NCMAP019）。继承原 019 的 AOT
        /// 提示语义（Info 不拦构建）；Fluent 目标类型豁免（与 019 豁免同源）。</para>
        /// </summary>
        public static readonly DiagnosticDescriptor DtoNamingWithoutMapFromRule = new(
            id: "NCMAP042",
            title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP042_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP042_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            category: "NCMapper",
            defaultSeverity: DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP042_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

        /// <summary>
        /// NCMAP043: [MapFormat] 目标属性类型必须为 string（
        /// 形态参照 NCMAP015 MaskTargetNotStringRule）
        /// </summary>
        public static readonly DiagnosticDescriptor MapFormatTargetNotStringRule = new(
            id: "NCMAP043",
            title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP043_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP043_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            category: "NCMapper",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP043_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

        /// <summary>
        /// NCMAP044: [MapFormat] 源属性类型未实现 IFormattable（
        /// 枚举源不报——Enum 实现 IFormattable，net10 实测）
        /// </summary>
        public static readonly DiagnosticDescriptor MapFormatSourceNotFormattableRule = new(
            id: "NCMAP044",
            title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP044_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP044_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            category: "NCMapper",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP044_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

        /// <summary>
        /// NCMAP045: [MapFormat] 与 [Convert] 同标矛盾（
        /// 三标（Format+Convert+Mask）由本码优先拦截）
        /// </summary>
        public static readonly DiagnosticDescriptor MapFormatWithConvertRule = new(
            id: "NCMAP045",
            title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP045_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP045_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            category: "NCMapper",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP045_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

        /// <summary>
        /// NCMAP046: [MapFormat] 不支持反向映射（路径 5
        /// 反向命中即拦截；消息给两条出路指引：[ConvertBack] / [MapIgnore(Direction.ToEntity)]）
        /// </summary>
        public static readonly DiagnosticDescriptor MapFormatReverseNotConsumedRule = new(
            id: "NCMAP046",
            title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP046_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP046_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            category: "NCMapper",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP046_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

        /// <summary>
        /// NCMAP047: [MapNoAutoMatch] 白名单模式下属性带映射变换特性但未标注 [MapProperty]
        ///——白名单不激活变换声明
        ///（对齐 Fluent 侧"变换终端须 Member 头声明"同构语义），fail-fast Error；消息给两条出路
        ///（补 [MapProperty] 显式声明 / 移除变换特性）。纯反射模式（生成器缺席）不发射本码，
        /// 由 ExpressionBuilder MapTracer 提示承载可观测性。
        /// </summary>
        public static readonly DiagnosticDescriptor TransformWithoutMapPropertyUnderNoAutoMatchRule = new(
            id: "NCMAP047",
            title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP047_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP047_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            category: "NCMapper",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP047_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

        /// <summary>
        /// NCMAP020: 全局映射配置值非法 — 该项按未设置处理，不阻断编译
        /// <para>触发场景：MSBuild/特性配置值非法（枚举越界、类型全名不可解析、
        /// 清单超 8 项、复杂泛型/嵌套全名超出 MVP 范围），对标 Mapperly RMG095（上游静态分析器诊断码）。</para>
        /// </summary>
        public static readonly DiagnosticDescriptor InvalidGlobalConfigValueRule = new(
            id: "NCMAP020",
            title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP020_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP020_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            category: "NCMapper",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP020_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

        /// <summary>
        /// NCMAP021: 显式标注（[MapProperty]/[Convert]/[Mask]/[ConvertBack]）命中全局忽略且无 OverrideIgnore
        /// <para>fail-closed：属性被忽略；诊断消息给双向处置指引（加 OverrideIgnore=true 豁免，
        /// 或删除冗余标注——全局忽略已兜底，v1.2 G5）。</para>
        /// </summary>
        public static readonly DiagnosticDescriptor ExplicitMappingHitGlobalIgnoreRule = new(
            id: "NCMAP021",
            title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP021_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP021_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            category: "NCMapper",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP021_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

        /// <summary>
        /// NCMAP022: [MapIgnore] 与 OverrideIgnore=true 同标（矛盾配置）
        /// <para>属性级 [MapIgnore] 为显式忽略，不参与 OverrideIgnore 豁免（覆盖范围）；
        /// 与带 OverrideIgnore=true 的显式特性同标即为矛盾配置——忽略生效（fail-closed 语义），报诊断指引清理。</para>
        /// </summary>
        public static readonly DiagnosticDescriptor MapIgnoreWithOverrideRule = new(
            id: "NCMAP022",
            title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP022_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP022_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            category: "NCMapper",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP022_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

        /// <summary>
        /// NCMAP023: 枚举成员名仅大小写区分（违反 CLS）→ string→enum 转换退化为精确匹配
        /// <para> T2/：全小写标签方案下成员名全小化冲突（如 Red 与 RED），
        /// 该枚举退化为精确匹配 switch（大小写敏感）+ default 抛 MapException。
        /// 退化保证不产生错误生成代码；此类枚举极罕见且违反 CLS 规范，建议重命名成员。</para>
        /// </summary>
        public static readonly DiagnosticDescriptor EnumNameCollisionDegradedRule = new(
            id: "NCMAP023",
            title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP023_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP023_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            category: "NCMapper",
            defaultSeverity: DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP023_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

        /// <summary>
        /// NCMAP024: 枚举成员数值重复（别名枚举）→ 数字串标签维度退化
        /// <para> T2/：成员数值重复时数字串标签（如 "1" => A）会产生重复 switch 标签，
        /// 数字串维度退化为精确名称匹配——数字字符串（如 "1"）将无法在生成侧转换（运行时 Enum.Parse 仍可解析，
        /// 该维度行为分叉已文档化）。建议消除数值重复或改用 [Convert]。</para>
        /// </summary>
        public static readonly DiagnosticDescriptor EnumValueCollisionDegradedRule = new(
            id: "NCMAP024",
            title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP024_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP024_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            category: "NCMapper",
            defaultSeverity: DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP024_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

        /// <summary>
        /// NCMAP025: 双侧互指映射声明— 生成侧该对不生成 + Error 级诊断
        /// <para>W3A [MapFrom(B)] 且 B [MapFrom(A)] 构成互指对。
        /// 映射双向而生成来源唯一，互指对无法界定单一生成来源 → 该对不生成 +
        /// Error 级诊断（构建失败，AOT 场景提前暴露）；运行时（源生成禁用时）按名匹配兜底。
        /// 触发面泛化为 [MapFrom] 或 Fluent For 双侧声明（检测内核
        /// DetectMutualPairs 本为通道无关交叉，文案对齐）；定位分通道——特性侧指向目标类型
        /// 声明标识符，Fluent 侧指向 For&lt;S,T&gt;() 调用点（ForInvocationLocation）。
        /// 修复指引：删除一侧声明即可恢复生成（迁移路径）。</para>
        /// </summary>
        public static readonly DiagnosticDescriptor MutualReferenceRule = new(
            id: "NCMAP025",
            title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP025_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP025_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            category: "NCMapper",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP025_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

        /// <summary>
        /// NCMAP026: [Mask] 标注位置非规范（源侧）— 非规范标注位置 Warning 级诊断
        /// <para>W4Mask 语义为"目标 string 脱敏"（MaskProvider 单向），
        /// 规范位置为目标侧（与 [MapFrom] 同侧）。[Mask] 标注所在类 ≠ [MapFrom] 标注类时
        /// 源侧标注不生效（仅诊断不消费，方案语义点②a）。互指对（NCMAP025 Error）触发时本诊断不叠加。</para>
        /// </summary>
        public static readonly DiagnosticDescriptor MaskPositionRule = new(
            id: "NCMAP026",
            title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP026_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP026_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            category: "NCMapper",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP026_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

        /// <summary>
        /// NCMAP027: [MapFrom] 自动匹配属性类型完全不相容被跳过 — Info 级诊断
        /// <para>：对齐 AutoMapper"类型不兼容成员不映射"语义——同名属性源/目标类型
        /// 完全不相容（无任何生成侧转换路径，TypeHelpers.HasMappingPath == false）时跳过映射，
        /// 输出 Info 提示。仅用于 [MapFrom] 自动匹配场景；[MapProperty(FromField=...)] 显式映射
        /// 不兼容保留 NCMAP002 fallback 语义（边界）。</para>
        /// </summary>
        public static readonly DiagnosticDescriptor SkippedIncompatibleMemberRule = new(
            id: "NCMAP027",
            title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP027_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP027_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            category: "NCMapper",
            defaultSeverity: DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP027_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

        /// <summary>
        /// NCMAP028: 生成代码访问不可访问成员被跳过 — Info 级诊断
        /// <para>：可访问性守卫——生成代码仅访问 public 可访问成员（跨程序集场景
        /// 访问 internal/protected/private 触发 CS0122）。源/目标/容器类型任一不可访问时跳过
        /// 该成员映射并输出 Info 提示。</para>
        /// </summary>
        public static readonly DiagnosticDescriptor InaccessibleMemberRule = new(
            id: "NCMAP028",
            title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP028_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP028_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            category: "NCMapper",
            defaultSeverity: DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP028_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

        /// <summary>
        /// NCMAP029: 嵌套互调环检测降级 — Info 级诊断
        /// <para>生成期静态依赖图检测嵌套互调环（长度 ≥2，
        /// A→B 与 B→A 经嵌套属性委托互调——生成代码互调无深度计数，成环数据 StackOverflow；
        /// Q7 同类，Q7 仅切断集合元素链路未根治嵌套属性互调）。环上注册对全部降级
        /// （目标属性保持默认值）；直接自引用（长度 1）由 IsSelfRecursiveMapping 深度保护维持
        /// （P0，不降级）。</para>
        /// </summary>
        public static readonly DiagnosticDescriptor NestedCycleDegradedRule = new(
            id: "NCMAP029",
            title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP029_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP029_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            category: "NCMapper",
            defaultSeverity: DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP029_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));

        /// <summary>
        /// NCMAP030: 显式点路径 × [Mask]/[Convert] 可观测诊断 — Info 级
        /// <para> （H6 可观测部分）：点路径映射成功但生成端不消费 [Mask]/[Convert]
        /// （与运行时 BuildFlattenedAssignmentFromPath L498-502 同措辞口径），消费修复登记阶段 B。</para>
        /// </summary>
        public static readonly DiagnosticDescriptor DotPathFeatureNotConsumedRule = new(
            id: "NCMAP030",
            title: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP030_Title), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            messageFormat: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP030_Message), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)),
            category: "NCMapper",
            defaultSeverity: DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(nameof(DiagnosticResources.NCMAP030_Description), DiagnosticResources.ResourceManager, typeof(DiagnosticResources)));
    }

    /// <summary>
    /// 诊断信息中间载体
    /// 实现 Equals/GetHashCode 以支持 Roslyn 增量管道缓存
    /// （Diagnostics 入 MappingInfo.Equals 的前置
    /// 无值相等时 SequenceEqual 退化为引用相等，transform 每次重跑 new 实例
    /// 将导致缓存永久失效；C1 修订）
    /// </summary>
    internal sealed class DiagnosticInfo : IEquatable<DiagnosticInfo>
    {
        /// <summary>
        /// 诊断描述符
        /// </summary>
        public DiagnosticDescriptor Descriptor { get; set; } = null!;

        /// <summary>
        /// 诊断参数
        /// </summary>
        public object[] Args { get; set; } = null!;

        /// <summary>
        /// 诊断位置
        /// </summary>
        public Location? Location { get; set; }

        /// <summary>
        /// 显式语义标记（NCMAP001：显式 FromField 错误始终报；B-6）
        /// </summary>
        public bool IsExplicit { get; set; }

        /// <summary>
        /// 判断当前诊断信息是否与另一个相等（值语义）
        /// </summary>
        /// <param name="other">另一个诊断信息</param>
        /// <returns>如果两个诊断信息相等则返回 true</returns>
        public bool Equals(DiagnosticInfo? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            // Descriptor 为 static readonly 单例（DiagnosticDescriptors），引用相等即可
            return ReferenceEquals(Descriptor, other.Descriptor)
                   // Args 元素实际为 string（属性名/类型名等），SequenceEqual 默认比较器
                   // 对 string 走内容比较（object.Equals 运行时派发）
                   // Args null 双侧防护（null 视空组
                   // 对齐 GetHashCode 三重防护形态，单侧 null 判不等）
                   && (Args ?? Array.Empty<object>()).SequenceEqual(
                       other.Args ?? Array.Empty<object>())
                   && Equals(Location, other.Location)
                   // IsExplicit 补齐（显式语义影响诊断去重/抑制面——不参与则
                   // 「增删显式标记」的增量失效漏判）
                   && IsExplicit == other.IsExplicit;
        }

        /// <summary>
        /// 判断当前诊断信息是否与指定对象相等
        /// </summary>
        /// <param name="obj">要比较的对象</param>
        /// <returns>如果对象是 DiagnosticInfo 且相等则返回 true</returns>
        public override bool Equals(object? obj) => Equals(obj as DiagnosticInfo);

        /// <summary>
        /// 获取当前诊断信息的哈希码（与 Equals 字段集一致）
        /// </summary>
        /// <returns>哈希码</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (Descriptor?.GetHashCode() ?? 0);
                if (Args != null)
                {
                    foreach (var arg in Args)
                        hash = hash * 31 + (arg?.GetHashCode() ?? 0);
                }
                hash = hash * 31 + (Location?.GetHashCode() ?? 0);
                // IsExplicit 补齐（与 Equals 字段集一致）
                hash = hash * 31 + IsExplicit.GetHashCode();
                return hash;
            }
        }
    }

    /// <summary>
    /// 映射信息中间载体 — 存储源生成阶段解析出的映射关系
    /// 实现 Equals/GetHashCode 以支持 Roslyn 增量管道缓存
    /// </summary>
    internal sealed class MappingInfo : IEquatable<MappingInfo>
    {
        /// <summary>
        /// 源类型
        /// </summary>
        public INamedTypeSymbol SourceType { get; set; } = null!;

        /// <summary>
        /// 目标类型
        /// </summary>
        public INamedTypeSymbol TargetType { get; set; } = null!;

        /// <summary>
        /// 源类型类名
        /// </summary>
        public string SourceClassName { get; set; } = "";

        /// <summary>
        /// 目标类型类名
        /// </summary>
        public string TargetClassName { get; set; } = "";

        /// <summary>
        /// 方法名后缀 — 用于解决不同命名空间同名类型的方法名冲突（空字符串表示无后缀）
        /// </summary>
        public string MethodSuffix { get; set; } = "";

        /// <summary>
        /// 目标语法节点
        /// </summary>
        public TypeDeclarationSyntax? TargetSyntax { get; set; }

        /// <summary>
        /// Fluent 通道 For&lt;S,T&gt;() 调用点位置（T2，解析期承载
        /// FluentSnapshot.ForLocation 桥接；物化期经 SnapshotLocationInternal 转纯值
        /// LocationSnapshot 进快照，MappingInfo 本体不进增量缓存）。特性通道恒 null。
        /// </summary>
        public Microsoft.CodeAnalysis.Location? ForLocation { get; set; }

        /// <summary>
        /// 跳过代码生成标记 — NCMAP011：non-public 类型不生成编译时代码
        /// <para>true 时 GenerateMapperCode 跳过此映射的方法生成，仅由 EmitDiagnostics 报告诊断；
        /// 运行时引擎仍可处理此类型对（通过 Mapper.Map 调用走运行时表达式树路径）</para>
        /// <para>触发条件：targetSymbol.DeclaredAccessibility != Public（internal/private 嵌套类等）</para>
        /// </summary>
        public bool SkipCodeGeneration { get; set; }

        /// <summary>
        /// 未映射成员诊断开关（NCMapperOptions 解析结果）
        /// <para>类级 [NCMapperOptions] &gt; 程序集级 [assembly: NCMapperOptions] &gt; 默认（目标侧 false、源侧 true）</para>
        /// </summary>
        public DiagnosticOptions DiagnosticOptions { get; set; } = new();

        /// <summary>
        /// 属性映射列表
        /// </summary>
        public List<PropertyMapping> PropertyMappings { get; set; } = new();

        /// <summary>
        /// 所有可匹配目标属性候选（含匹配失败/扁平化失败者）— 配置感知补匹配载体。
        /// <para>ExtractMappingInfo（transform 阶段，无配置）把每个可写、非 [MapIgnore] 的目标属性
        /// 记入此列表；管线 Select 阶段值版修正管线 MappingExtractor.ApplyGlobalConfig
        /// （MappingExtractor.SnapshotConfig.cs）用合并后配置对其执行
        /// 配置感知匹配（策略/前后缀归一化查表 + 全局忽略），补回 SnakeCase 等策略下
        /// 按 OrdinalIgnoreCase 匹配失败的属性（如 user_name ↔ UserName）。</para>
        /// </summary>
        public List<IPropertySymbol> AllTargetProperties { get; set; } = new();

        /// <summary>
        /// Compose 跨表字段映射列表 — 存储所有 FromSource ≠ 主源类型的 [MapProperty] 属性
        /// <para>源生成器据此生成部分更新方法和 Compose 合成方法，消除运行时反射</para>
        /// </summary>
        public List<ComposePropertyMapping> ComposeMappings { get; set; } = new();

        /// <summary>
        /// 反向无源转换器映射列表— 实体属性标 [ConvertBack] 且
        /// 未被任何正向 pm 消费（覆盖排除集外）的条目
        /// <para>条目形态：TargetProperty=实体属性（反向目标）、SourceProperty=null!（无源）、
        /// SourcePath=空、PathOrigin=Direct + 转换器三元组（ApplyConverterMethodReference）；
        /// 运行时镜像：ExpressionBuilder.BuildMapBody 无源块前置分支（暂挂+写回链后发射）；
        /// 消费端：ReversePlanner.CreateReverseMapping（追加于反向主 foreach 之后）。</para>
        /// <para>参与相等性/哈希（增量缓存正确性——与 PropertyMappings 同口径）。</para>
        /// </summary>
        public List<PropertyMapping> ReverseOnlyConvertBacks { get; set; } = new();

        /// <summary>
        /// 诊断信息列表
        /// </summary>
        public List<DiagnosticInfo> Diagnostics { get; set; } = new();

        /// <summary>
        /// 对级 NoAutoInclude（双承载）
        /// 禁用该 (S,T) 对的自动匹配/自动包含（含自动扁平化），仅映射显式声明的属性。
        /// <para>特性版属性级 HasMapNoAutoInclude（PropertyRef）不动；本字段 Fluent 通道经
        /// FluentSnapshot 桥接填充，特性（[MapFrom]）通道默认 false、标注 [MapNoAutoMatch] 时 true
        ///（提取链第四个类级读取点填充——P2 additive ≤2 字段约束之一，
        /// 零新增字段）。</para>
        /// </summary>
        public bool NoAutoInclude { get; set; }

        /// <summary>
        /// AfterMapping 后置生命周期回调方法引用（建立；
        /// 语义扩展为双侧共用——特性通道 [AfterMapping] 声明同样填充本字段，B-01 迁出条件）——
        /// 形态 "{ContainingType.ToDisplayString()}.{MethodName}"（源生成器发射 global:: 前缀调用）。
        /// <para>Fluent 通道经 FluentSnapshot 桥接填充；特性通道经 ExtractLifecycleHooks 提取填充
        ///（签名适配声明所在对）。未声明恒默认 null；发射端 IsNullOrEmpty 短路保证默认值时
        /// 生成代码与现状逐字节一致（v1.2 自查③）。</para>
        /// </summary>
        public string? AfterMappingMethodRef { get; set; }

        /// <summary>
        /// BeforeMapping 前置生命周期回调方法引用（建立，双侧共用）
        /// 形态 "{ContainingType.ToDisplayString()}.{MethodName}"（源生成器发射 global:: 前缀调用）。
        /// <para>Fluent 通道经 FluentSnapshot 桥接填充；特性通道经 ExtractLifecycleHooks 提取填充
        ///（签名适配声明所在对）；纯 Fluent 对经跨对传播注入（D-d——签名适用逐对生效）。
        /// 未声明恒默认 null；发射端 IsNullOrEmpty 短路保证默认值时生成代码与现状逐字节一致。</para>
        /// </summary>
        public string? BeforeMappingMethodRef { get; set; }

        /// <summary>
        /// Fluent 通道正向方向标记（消费补齐）——For&lt;S,T&gt; 声明
        /// S=Entity、T=DTO，正向映射写入 DTO 侧（IsToDto 语义）。特性通道方向由目标 [MapFrom]
        /// 推导（生成端 IsToDtoMapping），Fluent 声明无该特性——本字段物化恒 true 补足方向语义。
        /// <para>[MapFrom] 通道与反向派生快照（ReversePlanner 全新构造）恒默认 false；
        /// false 时生成端回落 [MapFrom] 推导——特性通道行为逐字节不变。</para>
        /// </summary>
        public bool IsFluentForward { get; set; }

        /// <summary>
        /// Fluent 通道树构建配置—— <c>.Tree()</c> 终端三元组；
        /// null = 未调用（opt-in：消费端树挂载位判定恒 false）。[MapFrom] 通道恒默认 null。
        /// <para>不参与 Equals（转嫁排除——映射面差异体现在 PropertyMappings；对级 NoAutoInclude
        /// 已随 转正参与，本字段维持排除，SnapshotConservationTests 登记在案）。</para>
        /// </summary>
        public (string IdProperty, string ParentIdProperty, string ChildrenProperty)? TreeConfig { get; set; }

        /// <summary>
        /// Fluent 通道属性级 NoAutoInclude 目标属性名集
        /// <c>.NoAutoInclude(dest)</c> 声明；空 = 无声明。对级承载（与 pm 无关——收集器消费面为
        /// 全部可写属性，pm 缺失场景语义完备）。[MapFrom] 通道恒空。
        /// <para>不参与 Equals（转嫁排除——对级 NoAutoInclude 已随 转正参与，
        /// 本字段维持排除，SnapshotConservationTests 登记在案）。</para>
        /// </summary>
        public System.Collections.Generic.List<string> PropertyNoAutoIncludeTargets { get; set; } = new();

        /// <summary>
        /// Fluent 通道含 Compose 多源声明——FromSource 声明的属性不进
        /// PropertyMappings（ComposeMappings 平行承载），运行时投影消费面无明细可读；
        /// 降级判定据此把 Compose DTO 视为不可翻译（强制降级——多源 JOIN 投影无运行时承载）。
        /// [MapFrom] 通道恒 false（特性 Compose 走 ComposeProjectionKernel 特性解析，合法投影）。
        /// <para>不参与 Equals（ComposeMappings 集合相等性已覆盖声明变化）。</para>
        /// </summary>
        public bool HasComposeMappings { get; set; }

        /// <summary>
        /// 多态子类型对登记清单（符号态）
        /// 基类对名下的 `.MapSubclass&lt;TSrcDerived, TTargetDerived&gt;()` 登记集合（Fluent 通道经
        /// FluentSnapshot.SubclassDeclarations 物化透传；特性侧 [MapSubclass]同承载注入）。
        /// <para>空 = 无登记（快照 SubclassPairs 恒空集合——发射端零分派产物，G3 字节门）。</para>
        /// <para>不参与 Equals（物化后 SubclassPairSnapshot 值相等性已覆盖声明变化）。</para>
        /// </summary>
        public List<SubclassPairMapping> SubclassPairs { get; set; } = new();

        /// <summary>
        /// MapCtorParam 显式构造器形参绑定表
        /// key = 目标构造器形参名（OrdinalIgnoreCase）、value = 源属性名；经 MappingSnapshotBuilder
        /// 校验建表（参数集校验 / 矛盾判定后）传 Materialize 填充
        /// ConstructorParameterRef.SourcePropertyName（目标类型顶层物化 + DeepCopy 目标节点双点）。
        /// <para>[MapFrom] 通道恒 null（B9 字节门前提——绑定空时目标类型构造器快照与既有逐字段等价）；
        /// 绑定变更检出经 ConstructorParameterRef.Equals/GetHashCode。</para>
        /// </summary>
        public System.Collections.Generic.IReadOnlyDictionary<string, string>? CtorParamBindings { get; set; }

        /// <summary>
        /// 判断当前映射信息是否与另一个映射信息相等（基于类型名称、属性映射列表、
        /// 诊断与配置相关字段—— 补齐 Diagnostics/
        /// AllTargetProperties/DiagnosticOptions，修复缓存击穿）
        /// </summary>
        /// <param name="other">另一个映射信息</param>
        /// <returns>如果两个映射信息相等则返回 true</returns>
        public bool Equals(MappingInfo? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return SourceClassName == other.SourceClassName
                   && TargetClassName == other.TargetClassName
                   && SourceType.ToDisplayString() == other.SourceType.ToDisplayString()
                   && TargetType.ToDisplayString() == other.TargetType.ToDisplayString()
                   && SkipCodeGeneration == other.SkipCodeGeneration
                   // C1 前置：DiagnosticInfo 已实现值相等（见上）
                   && Diagnostics.SequenceEqual(other.Diagnostics)
                   // 符号序列必须显式 SymbolEqualityComparer（跨 Compilation 符号不可引用比较）
                   && AllTargetProperties.SequenceEqual(other.AllTargetProperties, SymbolEqualityComparer.Default)
                   // DiagnosticOptions 无 Equals，两布尔字段内联比较
                   && DiagnosticOptions.ReportUnmappedTargetMembers == other.DiagnosticOptions.ReportUnmappedTargetMembers
                   && DiagnosticOptions.ReportUnmappedSourceMembers == other.DiagnosticOptions.ReportUnmappedSourceMembers
                   && PropertyMappings.SequenceEqual(other.PropertyMappings)
                   && ComposeMappings.SequenceEqual(other.ComposeMappings)
                   && ReverseOnlyConvertBacks.SequenceEqual(other.ReverseOnlyConvertBacks)
                   // 对级四字段补齐（缓存陈旧面修复——不加入则
                   // 「改 NoAutoInclude/钩子声明/Fluent 正向标记」的增量失效漏判；值化侧
                   // MappingSnapshot 对应字段已参与相等，符号侧与值侧口径对齐）
                   && NoAutoInclude == other.NoAutoInclude
                   && string.Equals(AfterMappingMethodRef, other.AfterMappingMethodRef, System.StringComparison.Ordinal)
                   && string.Equals(BeforeMappingMethodRef, other.BeforeMappingMethodRef, System.StringComparison.Ordinal)
                   && IsFluentForward == other.IsFluentForward;
        }

        /// <summary>
        /// 判断当前映射信息是否与指定对象相等
        /// </summary>
        /// <param name="obj">要比较的对象</param>
        /// <returns>如果对象是 MappingInfo 且相等则返回 true</returns>
        public override bool Equals(object? obj) => Equals(obj as MappingInfo);

        /// <summary>
        /// 获取当前映射信息的哈希码（与 Equals 字段集对齐；
        /// PropertyMapping/ComposePropertyMapping 哈希为 SymbolEqualityComparer 值语义，
        /// 见各自 GetHashCode 实现）
        /// </summary>
        /// <returns>哈希码</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + SourceClassName.GetHashCode();
                hash = hash * 31 + TargetClassName.GetHashCode();
                hash = hash * 31 + (SourceType.ToDisplayString()?.GetHashCode() ?? 0);
                hash = hash * 31 + (TargetType.ToDisplayString()?.GetHashCode() ?? 0);
                hash = hash * 31 + SkipCodeGeneration.GetHashCode();
                foreach (var d in Diagnostics)
                    hash = hash * 31 + d.GetHashCode();
                foreach (var p in AllTargetProperties)
                    hash = hash * 31 + SymbolEqualityComparer.Default.GetHashCode(p);
                hash = hash * 31 + DiagnosticOptions.ReportUnmappedTargetMembers.GetHashCode();
                hash = hash * 31 + DiagnosticOptions.ReportUnmappedSourceMembers.GetHashCode();
                foreach (var p in PropertyMappings)
                    hash = hash * 31 + p.GetHashCode();
                foreach (var c in ComposeMappings)
                    hash = hash * 31 + c.GetHashCode();
                foreach (var r in ReverseOnlyConvertBacks)
                    hash = hash * 31 + r.GetHashCode();
                // 对级四字段补齐（与 Equals 字段集对齐）
                hash = hash * 31 + NoAutoInclude.GetHashCode();
                hash = hash * 31 + (AfterMappingMethodRef != null ? System.StringComparer.Ordinal.GetHashCode(AfterMappingMethodRef) : 0);
                hash = hash * 31 + (BeforeMappingMethodRef != null ? System.StringComparer.Ordinal.GetHashCode(BeforeMappingMethodRef) : 0);
                hash = hash * 31 + IsFluentForward.GetHashCode();
                return hash;
            }
        }

        // 原「深拷贝（含列表）」无宿主文档块删除（T6.1）——原语义：管线 Select 阶段修改克隆体、上游 transform 缓存未污染
    }

    /// <summary>
    /// 单对属性映射关系
    /// 实现 Equals/GetHashCode 以支持 Roslyn 增量管道缓存
    /// </summary>
    internal sealed class PropertyMapping : IEquatable<PropertyMapping>
    {
        /// <summary>
        /// 源属性
        /// </summary>
        public IPropertySymbol SourceProperty { get; set; } = null!;

        /// <summary>
        /// 目标属性
        /// </summary>
        public IPropertySymbol TargetProperty { get; set; } = null!;

        /// <summary>
        /// 源属性访问路径
        /// </summary>
        public List<IPropertySymbol> SourcePath { get; set; } = new();

        /// <summary>
        /// 路径段索引元数据— 与 <see cref="SourcePath"/> 等长并行列表：
        /// null = 纯属性段；非 null = 该属性段后跟 <c>[n]</c> 集合索引访问（如 "B[0]"）
        /// <para>仅显式点路径写入（推导扁平化/单段恒为空列表）；参与相等性/哈希（增量缓存正确性）。</para>
        /// </summary>
        public List<int?> SourceIndexes { get; set; } = new();

        /// <summary>
        /// 路径是否含索引段— 生成端/报告端判据
        /// </summary>
        public bool HasIndex => SourceIndexes.Count > 0 && SourceIndexes.Any(i => i != null);

        /// <summary>
        /// 是否为展开映射（从嵌套对象展开到扁平属性）
        /// <para>过渡期取值：显式点路径 IsFlattened=true（统一多段语义），
        /// 来源由 <see cref="PathOrigin"/> 独立区分；最终废弃时由 SourcePath.Count &gt; 1 推导。</para>
        /// </summary>
        public bool IsFlattened { get; set; }

        /// <summary>
        /// 路径来源— Direct / DerivedFlatten / ExplicitDotPath
        /// <para>参与 NCMAP009 判定、反向诊断措辞与相等性/哈希</para>
        /// </summary>
        public PathOrigin PathOrigin { get; set; }

        /// <summary>
        /// [Convert] 静态方法引用形态 — 锚定类型 FQN
        /// <para>null = 无转换器</para>
        /// </summary>
        public string? ConverterDeclaringType { get; set; }

        /// <summary>
        /// [Convert] 静态方法名（与 <see cref="ConverterDeclaringType"/> 成对）
        /// <para>null = 无转换器</para>
        /// </summary>
        public string? ConverterMethodName { get; set; }

        /// <summary>
        /// [Convert] 静态方法签名模板（T0-T3 四模板；None = 未命中/无转换器）
        /// </summary>
        public ConverterShape ConverterShape { get; set; }

        /// <summary>
        /// Fluent ConvertBack 反向方法引用形态 — 锚定类型 FQN（
        /// additive 三字段——反向三元组的符号态承载）。
        /// <para>符号态语义：特性通道恒默认 null（[ConvertBack] 三元组经 PropertyRefFromSymbol
        /// 特性派生路径直取，不经此三字段）；Fluent 通道由 MappingSnapshotBuilder 物化注入，
        /// MaterializePropertyMapping 传参物化进 PropertyRef 反向三元组。</para>
        /// <para>null = 无反向转换器（Fluent 声明）</para>
        /// </summary>
        public string? BackConverterDeclaringType { get; set; }

        /// <summary>
        /// Fluent ConvertBack 反向方法名（与 <see cref="BackConverterDeclaringType"/> 成对）
        /// </summary>
        public string? BackConverterMethodName { get; set; }

        /// <summary>
        /// Fluent ConvertBack 反向签名模板（含 Nullable 守卫变体；None = 未命中/无声明）
        /// </summary>
        public ConverterShape BackConverterShape { get; set; }

        /// <summary>
        /// 脱敏实现类型 — 标注了 [Mask(typeof(X))] 时记录脱敏类型
        /// <para>null 表示无脱敏，走默认赋值路径</para>
        /// </summary>
        public INamedTypeSymbol? MaskType { get; set; }

        /// <summary>
        /// 格式化串 — 标注了 [MapFormat("fmt")] 时记录格式串文本（
        /// ）
        /// <para>null 表示无格式化，走默认赋值路径；与 [Mask] 双标时承载 FormatThenMask 组合</para>
        /// </summary>
        public string? FormatString { get; set; }

        /// <summary>
        /// JSON 转换标志 — 标注了 [MapProperty(IsFromJson = true)] 时为 true
        /// <para>生成代码调用 JsonConverter 完成 string ↔ 对象 转换</para>
        /// </summary>
        public bool IsFromJson { get; set; }

        /// <summary>
        /// 豁免门符号态注入位——Fluent 四终端（Convert/Map/Mask/
        /// ConvertBack）overrideIgnore 实参汇合置位；物化期叠加进 Target.HasOverrideIgnore
        /// （正反向双链全局忽略豁免——SnapshotConfig 四读点 + ReversePlanner 双侧判定）。
        /// <para>additive：特性通道恒默认 false（[MapProperty]/[Convert]/[Mask]/[ConvertBack]
        /// 的 OverrideIgnore 命名参数经 PropertyRefFromSymbol 特性采集面物化，不经此字段）。</para>
        /// </summary>
        public bool FluentOverrideIgnore { get; set; }

        /// <summary>
        /// 方向性忽略符号态注入位——Fluent .Ignore(dest, Direction)
        /// 声明值（枚举底层 int：0=Both/1=ToDto/2=ToEntity）；物化期叠加进 Target.HasMapIgnore/
        /// MapIgnoreDirection（与 [MapIgnore(Direction)] 特性采集同口径）。
        /// <para>additive：特性通道恒默认 null；null 或 0（Both）不产生值化差异（完全跳过语义走物化遍历 continue，不建 pm）。</para>
        /// </summary>
        public int? FluentIgnoreDirection { get; set; }

        /// <summary>
        /// Fluent 显式声明标记——Map/Convert/Mask/MapTree/FromJson 声明面
        /// 产物 pm 置位；物化期透传 Target.HasFluentExplicit，供全局配置替换性重匹配
        /// （ReplaceWithConfigAwareMatchValue）豁免判定——显式声明为符号级路径，
        /// 不参与 NamingStrategy 名称规范化重匹配。
        /// <para>additive：特性通道恒默认 false。</para>
        /// </summary>
        public bool HasFluentExplicit { get; set; }

        /// <summary>
        /// 条件守卫方法引用 — "全限定类型::方法名"文本
        /// （A 形态=用户类型限定名；B 形态=生成类型限定名 NCMapper.Generated.GeneratedMapper；
        /// null = 无条件）。pm 唯一条件（单条件——重复声明 F44 拒绝）。
        /// <para>双侧同承载（P-e）：Fluent .MapWhen 与特性 [MapWhen] 填同一字段；
        /// 反向派生不消费（D3——ReversePlanner 剥离）；投影恒降级（分类 MapWhen）。</para>
        /// </summary>
        public string? ConditionMethodRef { get; set; }

        /// <summary>
        /// B 形态转录产物 — 重写后的表达式体文本
        /// （InlineTranscriber 符号级限定名重写产物；A 形态 null）。
        /// </summary>
        public string? ConditionMethodBody { get; set; }

        /// <summary>
        /// B 形态生成方法形参名（转录体内标识符按此名重写；A 形态 null）。
        /// </summary>
        public string? ConditionMethodParamName { get; set; }

        /// <summary>
        /// 谓词入参形态（T1 字段值 / T2 整源；None = 无条件）
        /// 发射端实参渲染与运行时回退反射调用的实参选择依据。
        /// </summary>
        public ConditionShape ConditionShape { get; set; }

        /// <summary>
        /// B 形态转换器转录体 — 重写后的表达式体文本
        /// （InlineTranscriber 产物；A 形态（方法组）/无转换器 null）。
        /// <para>与 <see cref="ConverterDeclaringType"/>（= 生成类型限定名）+
        /// <see cref="ConverterMethodName"/>（= 生成方法名）成组——发射端
        /// GenerateConverterMethods 物化为 GeneratedMapper 私有静态方法，
        /// 调用点 BuildStaticConverterCall 零分叉复用。</para>
        /// </summary>
        public string? ConverterMethodBody { get; set; }

        /// <summary>
        /// B 形态转换器形参类型组（lambda 绑定后形参类型显示串；
        /// T0 空组/T1·T2 单/T3 双；A 形态 null）。
        /// </summary>
        public string[]? ConverterMethodParamTypes { get; set; }

        /// <summary>
        /// B 形态转换器形参名组（转录体内标识符按此名组重写；A 形态 null）。
        /// </summary>
        public string[]? ConverterMethodParamNames { get; set; }

        /// <summary>
        /// B 形态反向转换器转录体（ConvertBack 内联 lambda 转录产物；
        /// A 形态/无声明 null）——经 PropertyRef.ConverterBackMethod* 物化进反向快照，
        /// ReversePlanner 复制到反向 pm 的 ConverterMethod* 三字段。
        /// </summary>
        public string? BackConverterMethodBody { get; set; }

        /// <summary>：B 形态反向转换器形参类型组（A 形态 null）</summary>
        public string[]? BackConverterMethodParamTypes { get; set; }

        /// <summary>：B 形态反向转换器形参名组（A 形态 null）</summary>
        public string[]? BackConverterMethodParamNames { get; set; }

        // 原「深拷贝 — 增量管线克隆安全」无宿主文档块删除（T6.1）——原语义：SourcePath 须为独立新列表

        /// <summary>
        /// 判断当前属性映射是否与另一个属性映射相等（基于 IsFlattened、源/目标属性符号和转换器类型）
        /// </summary>
        /// <param name="other">另一个属性映射</param>
        /// <returns>如果两个属性映射相等则返回 true</returns>
        public bool Equals(PropertyMapping? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return IsFlattened == other.IsFlattened
                   && PathOrigin == other.PathOrigin
                   && SymbolEqualityComparer.Default.Equals(SourceProperty, other.SourceProperty)
                   && SymbolEqualityComparer.Default.Equals(TargetProperty, other.TargetProperty)
                   && SourcePath.SequenceEqual(other.SourcePath, SymbolEqualityComparer.Default)
                   && SourceIndexes.SequenceEqual(other.SourceIndexes)
                   && string.Equals(ConverterDeclaringType, other.ConverterDeclaringType, System.StringComparison.Ordinal)
                   && string.Equals(ConverterMethodName, other.ConverterMethodName, System.StringComparison.Ordinal)
                   && ConverterShape == other.ConverterShape
                   // Fluent ConvertBack 三字段镜像（定案——不加入则
                   // 「改 converter 方法组」的增量失效漏判坐实）
                   && string.Equals(BackConverterDeclaringType, other.BackConverterDeclaringType, System.StringComparison.Ordinal)
                   && string.Equals(BackConverterMethodName, other.BackConverterMethodName, System.StringComparison.Ordinal)
                   && BackConverterShape == other.BackConverterShape
                   && SymbolEqualityComparer.Default.Equals(MaskType, other.MaskType)
                   // FormatString 镜像（定案同款——不加入则
                   // 「改格式串」的增量失效漏判坐实，与 MaskType 同族）
                   && string.Equals(FormatString, other.FormatString, System.StringComparison.Ordinal)
                   && IsFromJson == other.IsFromJson
                   // Fluent 注入两字段镜像（定案同款——不加入则
                   // 「改 overrideIgnore 实参/改 Direction」的增量失效漏判）
                   && FluentOverrideIgnore == other.FluentOverrideIgnore
                   && FluentIgnoreDirection == other.FluentIgnoreDirection
                   // Fluent IgnoreSource 源侧方向镜像（定案同款
                   // 不加入则「改 IgnoreSource Direction」的增量失效漏判；与目标侧
                   // FluentIgnoreDirection 成对，值化侧 PropertyRef.MapIgnoreDirection 同承载）
                   // 显式声明标记镜像（定案同款——不加入则
                   // 「声明面增删 Map/Mask 等显式链节点」的增量失效漏判）
                   && HasFluentExplicit == other.HasFluentExplicit
                   // 条件守卫四字段镜像（定案同款——不加入则
                   // 「改 MapWhen 谓词（方法组/内联体）」的增量失效漏判）
                   && string.Equals(ConditionMethodRef, other.ConditionMethodRef, System.StringComparison.Ordinal)
                   && string.Equals(ConditionMethodBody, other.ConditionMethodBody, System.StringComparison.Ordinal)
                   && string.Equals(ConditionMethodParamName, other.ConditionMethodParamName, System.StringComparison.Ordinal)
                   && ConditionShape == other.ConditionShape
                   // B 形态转换器六字段镜像（定案同款——不加入则
                   // 「改 Convert/ConvertBack 内联 lambda 体」的增量失效漏判）
                   && string.Equals(ConverterMethodBody, other.ConverterMethodBody, System.StringComparison.Ordinal)
                   && (ConverterMethodParamTypes ?? Array.Empty<string>()).SequenceEqual(
                       other.ConverterMethodParamTypes ?? Array.Empty<string>(), System.StringComparer.Ordinal)
                   && (ConverterMethodParamNames ?? Array.Empty<string>()).SequenceEqual(
                       other.ConverterMethodParamNames ?? Array.Empty<string>(), System.StringComparer.Ordinal)
                   && string.Equals(BackConverterMethodBody, other.BackConverterMethodBody, System.StringComparison.Ordinal)
                   && (BackConverterMethodParamTypes ?? Array.Empty<string>()).SequenceEqual(
                       other.BackConverterMethodParamTypes ?? Array.Empty<string>(), System.StringComparer.Ordinal)
                   && (BackConverterMethodParamNames ?? Array.Empty<string>()).SequenceEqual(
                       other.BackConverterMethodParamNames ?? Array.Empty<string>(), System.StringComparer.Ordinal);
        }

        /// <summary>
        /// 判断当前属性映射是否与指定对象相等
        /// </summary>
        /// <param name="obj">要比较的对象</param>
        /// <returns>如果对象是 PropertyMapping 且相等则返回 true</returns>
        public override bool Equals(object? obj) => Equals(obj as PropertyMapping);

        /// <summary>
        /// 获取当前属性映射的哈希码（基于 IsFlattened、源/目标属性符号和转换器类型）
        /// </summary>
        /// <returns>哈希码</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + IsFlattened.GetHashCode();
                hash = hash * 31 + PathOrigin.GetHashCode();
                hash = hash * 31 + (SymbolEqualityComparer.Default.GetHashCode(SourceProperty));
                hash = hash * 31 + (SymbolEqualityComparer.Default.GetHashCode(TargetProperty));
                hash = hash * 31 + (ConverterDeclaringType != null ? System.StringComparer.Ordinal.GetHashCode(ConverterDeclaringType) : 0);
                hash = hash * 31 + (ConverterMethodName != null ? System.StringComparer.Ordinal.GetHashCode(ConverterMethodName) : 0);
                hash = hash * 31 + ConverterShape.GetHashCode();
                // Fluent ConvertBack 三字段镜像（与 Equals 字段集对齐）
                hash = hash * 31 + (BackConverterDeclaringType != null ? System.StringComparer.Ordinal.GetHashCode(BackConverterDeclaringType) : 0);
                hash = hash * 31 + (BackConverterMethodName != null ? System.StringComparer.Ordinal.GetHashCode(BackConverterMethodName) : 0);
                hash = hash * 31 + BackConverterShape.GetHashCode();
                hash = hash * 31 + (MaskType != null ? SymbolEqualityComparer.Default.GetHashCode(MaskType) : 0);
                hash = hash * 31 + (FormatString != null ? System.StringComparer.Ordinal.GetHashCode(FormatString) : 0);
                hash = hash * 31 + IsFromJson.GetHashCode();
                // Fluent 注入两字段镜像（与 Equals 字段集对齐）
                hash = hash * 31 + FluentOverrideIgnore.GetHashCode();
                hash = hash * 31 + (FluentIgnoreDirection ?? 0);
                // 源侧方向镜像（与 Equals 字段集对齐）
                // 显式声明标记镜像（与 Equals 字段集对齐）
                hash = hash * 31 + HasFluentExplicit.GetHashCode();
                // 以下 12 项补齐 Equals 字段集（SourcePath/SourceIndexes
                // :1244-1245、条件守卫四字段、转换器六字段）——ISymbol 族以
                // SymbolEqualityComparer.Default 聚合，string[] 以 Ordinal 聚合；null 与空组 Equals
                // 视角相等（?? Array.Empty），哈希同跳过
                if (SourcePath != null)
                    foreach (var s in SourcePath) hash = hash * 31 + (s != null ? SymbolEqualityComparer.Default.GetHashCode(s) : 0);
                if (SourceIndexes != null)
                    foreach (var i in SourceIndexes) hash = hash * 31 + (i?.GetHashCode() ?? 0);
                hash = hash * 31 + (ConditionMethodRef != null ? System.StringComparer.Ordinal.GetHashCode(ConditionMethodRef) : 0);
                hash = hash * 31 + (ConditionMethodBody != null ? System.StringComparer.Ordinal.GetHashCode(ConditionMethodBody) : 0);
                hash = hash * 31 + (ConditionMethodParamName != null ? System.StringComparer.Ordinal.GetHashCode(ConditionMethodParamName) : 0);
                hash = hash * 31 + ConditionShape.GetHashCode();
                hash = hash * 31 + (ConverterMethodBody != null ? System.StringComparer.Ordinal.GetHashCode(ConverterMethodBody) : 0);
                if (ConverterMethodParamTypes != null)
                    foreach (var t in ConverterMethodParamTypes) hash = hash * 31 + System.StringComparer.Ordinal.GetHashCode(t);
                if (ConverterMethodParamNames != null)
                    foreach (var n in ConverterMethodParamNames) hash = hash * 31 + System.StringComparer.Ordinal.GetHashCode(n);
                hash = hash * 31 + (BackConverterMethodBody != null ? System.StringComparer.Ordinal.GetHashCode(BackConverterMethodBody) : 0);
                if (BackConverterMethodParamTypes != null)
                    foreach (var t in BackConverterMethodParamTypes) hash = hash * 31 + System.StringComparer.Ordinal.GetHashCode(t);
                if (BackConverterMethodParamNames != null)
                    foreach (var n in BackConverterMethodParamNames) hash = hash * 31 + System.StringComparer.Ordinal.GetHashCode(n);
                return hash;
            }
        }
    }

    /// <summary>
    /// 多态子类型对登记（符号态）
    /// MappingInfo 名下承载，物化（MappingExtractor.Materialize）转 <see cref="SubclassPairSnapshot"/>
    /// 值化入快照。继承约束由 Fluent 泛型调用点编译期拦截（CS0311/CS0314——生成器侧不可达非法实参）；
    /// 特性侧继承校验经 F46 承载。
    /// </summary>
    internal sealed class SubclassPairMapping
    {
        /// <summary>子类源类型符号（TSrcDerived : TSource）</summary>
        public INamedTypeSymbol SourceType { get; set; } = null!;

        /// <summary>子类目标类型符号（TTargetDerived : TTarget）</summary>
        public INamedTypeSymbol TargetType { get; set; } = null!;

        /// <summary>声明位置（诊断定位——F47 重复登记仲裁）</summary>
        public Microsoft.CodeAnalysis.Location? Location { get; set; }
    }

    /// <summary>
    /// Compose 跨表字段映射 — 记录子源类型到目标属性的映射关系
    /// <para>用于源生成器生成 Compose 部分更新方法，消除运行时 MakeGenericMethod 反射</para>
    /// 实现 Equals/GetHashCode 以支持 Roslyn 增量管道缓存
    /// </summary>
    internal sealed class ComposePropertyMapping : IEquatable<ComposePropertyMapping>
    {
        /// <summary>
        /// 子源类型（[MapProperty(typeof(SubSource), ...)] 中的 SubSource）
        /// </summary>
        public INamedTypeSymbol SubSourceType { get; set; } = null!;

        /// <summary>
        /// 子源属性（SubSource 上的源属性；显式点路径时为末段属性）
        /// </summary>
        public IPropertySymbol SourceProperty { get; set; } = null!;

        /// <summary>
        /// 源属性访问路径— 写入端归一：恒非空，至少含单段
        /// <para>显式点路径（FromField 含 '.'）时为多段路径；参与相等性/哈希（门控表第 14 点）</para>
        /// </summary>
        public List<IPropertySymbol> SourcePath { get; set; } = new();

        /// <summary>
        /// 路径段索引元数据— 与 <see cref="SourcePath"/> 等长并行列表：
        /// null = 纯属性段；非 null = 该属性段后跟 <c>[n]</c> 集合索引访问
        /// </summary>
        public List<int?> SourceIndexes { get; set; } = new();

        /// <summary>
        /// 路径是否含索引段
        /// </summary>
        public bool HasIndex => SourceIndexes.Count > 0 && SourceIndexes.Any(i => i != null);

        /// <summary>
        /// 目标属性（DTO 上的目标属性）
        /// </summary>
        public IPropertySymbol TargetProperty { get; set; } = null!;

        /// <summary>
        /// [Convert] 静态方法引用形态 — 锚定类型 FQN（三字段；
        /// 跨表字段消费转换器，TSource 校验基准=子源类型）
        /// <para>null = 无转换器</para>
        /// </summary>
        public string? ConverterDeclaringType { get; set; }

        /// <summary>
        /// [Convert] 静态方法名（与 <see cref="ConverterDeclaringType"/> 成对）
        /// <para>null = 无转换器</para>
        /// </summary>
        public string? ConverterMethodName { get; set; }

        /// <summary>
        /// [Convert] 静态方法签名模板（T0-T3 四模板；None = 未命中/无转换器）
        /// </summary>
        public ConverterShape ConverterShape { get; set; }

        /// <summary>
        /// 脱敏实现类型 — 标注了 [Mask(typeof(X))] 时记录脱敏类型（已校验值——
        /// 提取端镜像主源三段式"采集→校验→置位"，
        /// 仅校验通过才写入；null = 无脱敏或校验失败）
        /// <para>与 <see cref="PropertyMapping.MaskType"/> 同构（对照 Models.cs 主源字段）</para>
        /// </summary>
        public INamedTypeSymbol? MaskType { get; set; }

        /// <summary>
        /// 格式化串 — 标注了 [MapFormat("fmt")] 时记录格式串文本（
        /// 快照四处之 Compose 符号态；已校验值——提取端镜像主源三段式，
        /// 仅校验通过才写入；null = 无格式化或校验失败）
        /// <para>与 <see cref="PropertyMapping.FormatString"/> 同构（Compose 子映射格式化承载）</para>
        /// </summary>
        public string? FormatString { get; set; }

        /// <summary>
        /// JSON 转换标志 — 标注了 [MapProperty(IsFromJson = true)] 时为 true（已校验值——
        /// 镜像主源值类型守卫（源/目标含非 string 值类型 → 整条丢弃））
        /// </summary>
        public bool IsFromJson { get; set; }

        /// <summary>
        /// FromSource 块内 MapWhen 条件守卫方法引用
        /// "全限定类型::方法名"文本（A 形态=用户类型限定名；B 形态=生成类型限定名；null = 无条件）。
        /// <para>发射落点 = 块内子映射赋值层（GenerateComposeUpdateMethods 的
        /// Update_{Sub}_To_{Target} 逐属性赋值）；主链 MapWhen 锚定 Compose 覆盖属性 → F45 拒绝。</para>
        /// </summary>
        public string? ConditionMethodRef { get; set; }

        /// <summary>：B 形态转录产物（A 形态 null）</summary>
        public string? ConditionMethodBody { get; set; }

        /// <summary>：B 形态生成方法形参名（A 形态 null）</summary>
        public string? ConditionMethodParamName { get; set; }

        /// <summary>：谓词入参形态（T1 子源字段值 / T2 子源对象；None = 无条件）</summary>
        public ConditionShape ConditionShape { get; set; }

        /// <summary>
        /// B 形态转换器转录体（块内 Convert 内联 lambda 转录产物；
        /// A 形态（方法组）/无转换器 null）——与 ConverterDeclaringType（= 生成类型限定名）+
        /// ConverterMethodName（= 生成方法名）成组，发射端 GenerateConverterMethods 消费。
        /// </summary>
        public string? ConverterMethodBody { get; set; }

        /// <summary>：B 形态转换器形参类型组（A 形态 null）</summary>
        public string[]? ConverterMethodParamTypes { get; set; }

        /// <summary>：B 形态转换器形参名组（A 形态 null）</summary>
        public string[]? ConverterMethodParamNames { get; set; }

        // 原「深拷贝 — 增量管线克隆安全（A-3，与 PropertyMapping 同口径）」无宿主文档块删除（T6.1）

        /// <summary>
        /// 判断当前 Compose 映射是否与另一个相等（基于子源类型和源/目标属性符号）
        /// </summary>
        /// <param name="other">另一个 Compose 映射</param>
        /// <returns>如果相等则返回 true</returns>
        public bool Equals(ComposePropertyMapping? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return SymbolEqualityComparer.Default.Equals(SubSourceType, other.SubSourceType)
                   && SymbolEqualityComparer.Default.Equals(SourceProperty, other.SourceProperty)
                   && SourcePath.SequenceEqual(other.SourcePath, SymbolEqualityComparer.Default)
                   && SourceIndexes.SequenceEqual(other.SourceIndexes)
                   && SymbolEqualityComparer.Default.Equals(TargetProperty, other.TargetProperty)
                   && string.Equals(ConverterDeclaringType, other.ConverterDeclaringType, System.StringComparison.Ordinal)
                   && string.Equals(ConverterMethodName, other.ConverterMethodName, System.StringComparison.Ordinal)
                   && ConverterShape == other.ConverterShape
                   // Mask/Json 新字段镜像（定案同款——不加入则
                   // 「改 [Mask] 类型/改 IsFromJson」的增量失效漏判坐实，与 PropertyMapping 口径一致）
                   && SymbolEqualityComparer.Default.Equals(MaskType, other.MaskType)
                   // FormatString 镜像（与 PropertyMapping 口径一致）
                   && string.Equals(FormatString, other.FormatString, System.StringComparison.Ordinal)
                   && IsFromJson == other.IsFromJson
                   // 条件守卫四字段镜像
                   && string.Equals(ConditionMethodRef, other.ConditionMethodRef, System.StringComparison.Ordinal)
                   && string.Equals(ConditionMethodBody, other.ConditionMethodBody, System.StringComparison.Ordinal)
                   && string.Equals(ConditionMethodParamName, other.ConditionMethodParamName, System.StringComparison.Ordinal)
                   && ConditionShape == other.ConditionShape
                   // B 形态转换器三字段镜像（定案同款——不加入则
                   // 「改块内 Convert 内联 lambda 体」的增量失效漏判）
                   && string.Equals(ConverterMethodBody, other.ConverterMethodBody, System.StringComparison.Ordinal)
                   && (ConverterMethodParamTypes ?? Array.Empty<string>()).SequenceEqual(
                       other.ConverterMethodParamTypes ?? Array.Empty<string>(), System.StringComparer.Ordinal)
                   && (ConverterMethodParamNames ?? Array.Empty<string>()).SequenceEqual(
                       other.ConverterMethodParamNames ?? Array.Empty<string>(), System.StringComparer.Ordinal);
        }

        /// <summary>
        /// 判断当前 Compose 映射是否与指定对象相等
        /// </summary>
        /// <param name="obj">要比较的对象</param>
        /// <returns>如果对象是 ComposePropertyMapping 且相等则返回 true</returns>
        public override bool Equals(object? obj) => Equals(obj as ComposePropertyMapping);

        /// <summary>
        /// 获取当前 Compose 映射的哈希码
        /// </summary>
        /// <returns>哈希码</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + SymbolEqualityComparer.Default.GetHashCode(SubSourceType);
                hash = hash * 31 + SymbolEqualityComparer.Default.GetHashCode(SourceProperty);
                foreach (var p in SourcePath) hash = hash * 31 + SymbolEqualityComparer.Default.GetHashCode(p);
                foreach (var idx in SourceIndexes) hash = hash * 31 + idx.GetHashCode();
                hash = hash * 31 + SymbolEqualityComparer.Default.GetHashCode(TargetProperty);
                hash = hash * 31 + (ConverterDeclaringType != null ? System.StringComparer.Ordinal.GetHashCode(ConverterDeclaringType) : 0);
                hash = hash * 31 + (ConverterMethodName != null ? System.StringComparer.Ordinal.GetHashCode(ConverterMethodName) : 0);
                hash = hash * 31 + ConverterShape.GetHashCode();
                // Mask/Json 新字段镜像（与 Equals 字段集对齐）
                hash = hash * 31 + (MaskType != null ? SymbolEqualityComparer.Default.GetHashCode(MaskType) : 0);
                hash = hash * 31 + (FormatString != null ? System.StringComparer.Ordinal.GetHashCode(FormatString) : 0);
                hash = hash * 31 + IsFromJson.GetHashCode();
                // 以下 7 项补齐 Equals 字段集（条件守卫四字段、
                // B 形态转换器三字段）——集合以 foreach 聚合参与；null 与空组 Equals 视角
                // 相等（?? Array.Empty），哈希同跳过
                hash = hash * 31 + (ConditionMethodRef != null ? System.StringComparer.Ordinal.GetHashCode(ConditionMethodRef) : 0);
                hash = hash * 31 + (ConditionMethodBody != null ? System.StringComparer.Ordinal.GetHashCode(ConditionMethodBody) : 0);
                hash = hash * 31 + (ConditionMethodParamName != null ? System.StringComparer.Ordinal.GetHashCode(ConditionMethodParamName) : 0);
                hash = hash * 31 + ConditionShape.GetHashCode();
                hash = hash * 31 + (ConverterMethodBody != null ? System.StringComparer.Ordinal.GetHashCode(ConverterMethodBody) : 0);
                if (ConverterMethodParamTypes != null)
                    foreach (var t in ConverterMethodParamTypes) hash = hash * 31 + System.StringComparer.Ordinal.GetHashCode(t);
                if (ConverterMethodParamNames != null)
                    foreach (var n in ConverterMethodParamNames) hash = hash * 31 + System.StringComparer.Ordinal.GetHashCode(n);
                return hash;
            }
        }
    }
