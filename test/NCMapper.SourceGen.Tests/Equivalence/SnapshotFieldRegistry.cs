using System;
using System.Collections.Generic;

namespace NCMapper.SourceGen.Tests.Equivalence;

/// <summary>
/// 等价字段注册表——
/// 把 <see cref="SnapshotEquivalenceComparer"/> 散落的剥离集（TopLevelDenyList 字符串数组 +
/// Get*Coverage 硬编码数组 + Compare* 方法体内联 STRIPPED 注释）收敛为单点三列登记载体。
/// <para>
/// <b>三列结构</b>（<see cref="FieldRegistration"/>）：
/// <list type="bullet">
/// <item><c>Field</c>——字段名（与反射 <c>PropertyInfo.Name</c> 逐字一致，供全类型断言消费）。</item>
/// <item><c>Role</c>——<see cref="FieldRole.Compared"/>（参与比较）/ <see cref="FieldRole.Stripped"/>（剥离，不参与比较）。</item>
/// <item><c>Reason</c>——剥离/比较理由（符号引用，不带行号——评审 I2）。</item>
/// <item><c>MigrationCondition</c>——迁出条件（Stripped 项的"何时可比"，落差声明在此）。</item>
/// </list>
/// </para>
/// <para><b>铁律（B12）</b>注册表只加元数据，<c>Compare*</c> 比较逻辑零变更。</para>
/// <para><b>登记口径</b>：顶层 <see cref="NCMapper.SourceGen.MappingSnapshot"/> 登记 Stripped 6 项 +
/// 显式 Compared 3 项（N-1 实数核写——原记"仅登记 Stripped 4 项"与登记实态
/// 不符，A-25 转类翻转后实数 = Stripped 6 / Compared 3；评审 002-P2-8）
///（Compared = 反射全集 − Stripped 免手列，与比较器 <c>TopLevelDenyList</c> 实际行为一致——
/// 006 新增 <c>IsFluentForward</c> 必须入册，否则注册表 Compared 虚标 vs 比较器实际不比 → 假红/假绿）；
/// 各嵌套类型节逐字段全列（Compared 按 <c>Compare*</c> 实际比较字段，Stripped 按注释组；
/// 顶层 <c>MappingSnapshot</c> 走"反射全集 − Stripped"免手列口径）。</para>
/// <para><b>新增快照字段 SOP 检查单</b>（按序执行，每步标注护栏名与漏项后果）：
/// <list type="number">
/// <item><description>模型属性（<c>Models.Snapshots.cs</c> 加 <c>{ get; init; }</c>）——护栏：无（此为新增点本身）。</description></item>
/// <item><description>类型 <c>Equals</c>（手写 Equals 体纳入该字段）——护栏：<c>SnapshotConservationTests.EqualsAndGetHashCode_FieldCoverage_MatchesRegisteredExpectations</c> 臂②（EqualsCover 覆盖）；漏项后果：增量缓存把不同快照判等 → 生成期陈旧复用（缺陷家族 007②/008/010）。</description></item>
/// <item><description>类型 <c>GetHashCode</c>——护栏：同上臂④（GhcCover 覆盖）；漏项后果：哈希分布与 Equals 不一致，缓存正确性与命中率双损。</description></item>
/// <item><description>L1 注册表（本类对应类型节加 Compared/Stripped 行）——护栏：<c>SnapshotFieldRegistryTests.AllTypes_ComparedUnionStripped_EqualsReflectionFullSet</c>（未登记即红）+ A4 类型域 / A5 分类蕴含；漏项后果：登记面虚标，双通道共享语义契约缺位。</description></item>
/// <item><description>比较器体（<c>SnapshotEquivalenceComparer.Compare*</c> 补比较段）——护栏：<c>ComparerFieldCoverageTests</c>（正向臂：登记 Compared 必须有比较性提及；反向臂：Stripped 不得现于比较位）；漏项后果：L1 等价门对该字段空转 = A-1 型缺陷（FormatString 账实不符存活 12 天实证）。</description></item>
/// <item><description>守恒期望集（<c>SnapshotConservationTests.FieldCoverageExpectations</c> 与重建点白名单）——护栏：该类四臂守恒断言；漏项后果：快照重建漏透传字段，反向/抑制臂产物缺字段。</description></item>
/// <item><description>运行时发射与缓存键（生成器消费该字段 + 缓存键参与）——护栏：<c>CacheKeyEquivalenceTests</c> / <c>RuntimeSnapshotMaterializationConsistencyTests</c> / 生成基线门；漏项后果：源码变更不触发增量失效，或产物漂移不可见。</description></item>
/// </list></para>
/// </summary>
internal static class SnapshotFieldRegistry
{
    // ----------------------------------------------------------------
    // 顶层 MappingSnapshot：Stripped 6 项 + 显式 Compared 3 项（BeforeMappingMethodRef /
    // AfterMappingMethodRef 迁移 / NoAutoInclude A-25 迁移；
    // 余 Compared = 反射全集 − 登记集，运行时免手列）
    // ----------------------------------------------------------------
    public static IReadOnlyList<FieldRegistration> MappingSnapshot { get; } = new[]
    {
        // BeforeMappingMethodRef 显式 Compared 登记（任务书验收 #6——双侧等价对
        // 覆盖：Fluent .BeforeMapping 与特性 [BeforeMapping]填充同一承载字段）。顶层显式
        // Compared 行（显式声明的语义锚点）；余顶层字段仍按"反射全集 − Stripped"免手列口径自动 Compared。
 Compared("BeforeMappingMethodRef", "Dual-channel lifecycle-hook carry (Fluent .BeforeMapping and attribute [BeforeMapping] fill the same field — equivalence coverage)"),
        // AfterMappingMethodRef 迁移（Stripped→Compared）——迁出条件
        // "特性侧编译期 AfterMapping 等价物立项时"兑现（[AfterMapping] 落地，等价 fixture A-19 同批）；
        // 比较器 TopLevelDenyList 派生本注册表，行角色翻转即自动参与 L1 比较
 Compared("AfterMappingMethodRef", "Dual-channel lifecycle-hook carry (migration — attribute [AfterMapping] landed, migration condition fulfilled; fixture A-19)"),
        Stripped("TargetIdentifierLocation", "Location class (Table 5 #1)", "长期/暂无"),
        // ForInvocationLocation（NCMAP025 定位分通道——Fluent 侧 For invocation，
        // 特性侧恒 null；通道固有 Location 类字段，同 TargetIdentifierLocation 剥离依据）
        Stripped("ForInvocationLocation", "Location class (Table 5 #1) + channel-default asymmetry Fluent-only (NCMAP025 Fluent-side location points at the For<S,T>() invocation)", "长期/暂无"),
        // NoAutoInclude Stripped→Compared 翻转——本行迁出
        // 条件"Fluent 落地对级 NoAutoMatch（原 NoAutoInclude 改名）等价物"由 [MapNoAutoMatch]（契约 /
        // 提取链门 / 反射腿门）兑现；比较器 TopLevelDenyList 派生本注册表，行角色翻转即自动
        // 参与 L1 字段级比较（特性侧与 Fluent 侧该位须同值，否则 A-25 fixture L1 红）。
        // 假绿防线：变异回退本行 → A-25 fixture 应红（字段层缺证），变异清单第 ③ 条。
        Compared("NoAutoInclude", "Dual-channel pair-level whitelist carry (A-25 migration — attribute [MapNoAutoMatch] and Fluent .NoAutoMatch() fill the same field; migration condition fulfilled; fixture EquivalenceTests.NoAutoMatch.cs)"),
        Stripped("IsFluentForward", "Channel-default asymmetry Fluent-only forward-direction marker (F-2; attribute channel derives direction from [MapFrom])", "特性侧编译期方向标记等价物立项时"),
        Stripped("TreeConfig", "Channel-default asymmetry Fluent-only tree-build config (attribute side reads [MapTree] via runtime reflection — registry carry is Fluent-only by design, opt-in)", "特性侧编译期树配置等价物立项时（A-17 运行时行为断言覆盖）"),
        Stripped("PropertyNoAutoIncludeTargets", "Channel-default asymmetry Fluent-only pair-level NoAutoInclude targets (attribute side reads [MapNoAutoInclude] via runtime reflection — pair-level carry decoupled from pm existence)", "特性侧编译期属性级 NoAutoInclude 等价物立项时（A-18 运行时行为断言覆盖）"),
        Stripped("HasComposeMappings", "Channel-default asymmetry Fluent-only compose-declaration marker (degrade-classification input — attribute-side Compose goes ComposeProjectionKernel attribute parsing, legal projection)", "特性侧编译期 Compose 声明标记立项时（Compose 走特性解析为合法投影，无等价缺口）"),
    };

    // ----------------------------------------------------------------
    // PropertyRef：Compared = 符号结构位；Stripped = attr-snapshot 通道输入镜像组 + Location。
    // 计数不在此登记（防漂移）——精确集与计数由既有锁钉死：
    //   SnapshotEquivalenceComparerTests.PropertyRef_CoverageProjectionUnionStripped_EqualsFullSet
    //   （投影面/剥离面计数锁）+ PropertyRef_StrippedFieldsMatchTable4AttrSnapshotGroup（Stripped 精确集）
    //   + SnapshotFieldRegistryTests.AllTypes_ComparedUnionStripped_EqualsReflectionFullSet（反射全集冻结）
    //   + ComparerFieldCoverageTests（比较器体双向臂 + 总计数守门）
    // ----------------------------------------------------------------
    public static IReadOnlyList<FieldRegistration> PropertyRef { get; } = new[]
    {
        // Compared — symbol structure (Table 4: 10)
        Compared("Name", "Symbol identity"),
        Compared("Type", "TypeRef projection"),
        Compared("IsStatic", "Symbol structure"),
        Compared("IsIndexer", "Symbol structure"),
        Compared("DeclaredAccessibility", "Symbol structure"),
        Compared("GetMethodAccessibility", "Symbol structure"),
        Compared("SetMethodAccessibility", "Symbol structure"),
        Compared("IsInitOnly", "Symbol structure"),
        Compared("IsImplicitlyDeclared", "Symbol structure"),
        Compared("ContainingType", "TypeRef projection"),
        // Stripped — attr-snapshot group（通道输入镜像 + 反向转换器四元组 + Location）
        // 组内计数不在此登记——精确集与计数由 SnapshotEquivalenceComparerTests.
        // PropertyRef_StrippedFieldsMatchTable4AttrSnapshotGroup 钉死（该锁的期望集即本组定义）
        Stripped("HasMapIgnore", "Channel-input mirror (attr reads [MapIgnore], fluent doesn't)", "长期/暂无"),
        Stripped("MapIgnoreDirection", "Channel-input mirror", "长期/暂无"),
        Stripped("HasMapProperty", "Channel-input mirror", "长期/暂无"),
        Stripped("IsFromJson", "Channel-input mirror", "长期/暂无"),
        Stripped("HasOverrideIgnore", "Channel-input mirror", "长期/暂无"),
        Stripped("HasConvertUsing", "Channel-input mirror", "长期/暂无"),
        Stripped("ConverterDeclaringType", "Channel-input mirror", "长期/暂无"),
        Stripped("ConverterMethodName", "Channel-input mirror", "长期/暂无"),
        Stripped("ConverterShape", "Channel-input mirror", "长期/暂无"),
        // ConvertBack quartet (4) — STRIPPED, Q3 final decision (ComparePropertyRef comment block)
        // closure (2026-09-05): MigrationCondition absolute wording below.
        Stripped("HasConvertBackUsing", "Channel-input mirror asymmetry (AllTargetProperties independent enumeration mirror)", "维持剥离 (实读定案): AllTargetProperties 双侧枚举语义不可镜像统一——特性侧无条件候选集 (MappingExtractor.cs:130-158, 仅 [MapIgnore(Both)] 过滤) vs Fluent 侧条件性参与集 (MappingSnapshotBuilder.cs:253-:595, Ignore/矛盾门/NoAutoInclude/声明失败/Compose 空组均不入候选)，且 PropertyRef 镜像字段值域分叉 (attr-mirror vs 恒默认)——对齐必改物化行为或注入点 (S-4a 零行为契约); 迁出复审闭合"),
        Stripped("ConverterBackDeclaringType", "Channel-input mirror asymmetry (AllTargetProperties independent enumeration mirror)", "维持剥离 (闭合，依据同 HasConvertBackUsing 行): AllTargetProperties 双侧枚举语义不可镜像统一，对齐需改物化行为或发射期注入点"),
        Stripped("ConverterBackMethodName", "Channel-input mirror asymmetry (AllTargetProperties independent enumeration mirror)", "维持剥离 (闭合，依据同 HasConvertBackUsing 行): AllTargetProperties 双侧枚举语义不可镜像统一，对齐需改物化行为或发射期注入点"),
        Stripped("ConverterBackShape", "Channel-input mirror asymmetry (AllTargetProperties independent enumeration mirror)", "维持剥离 (闭合，依据同 HasConvertBackUsing 行): AllTargetProperties 双侧枚举语义不可镜像统一，对齐需改物化行为或发射期注入点"),
        // B 形态反向转换器方法体三字段（F26/F20 收窄——Fluent ConvertBack
        // 内联 lambda 转录产物；ConvertBack quartet 同组通道输入镜像，特性侧恒 null——剥离）
        Stripped("ConverterBackMethodBody", "Channel-input mirror (B 形态 Fluent 独有，D-n)", "特性侧内联转换器立项时（[Convert] 保持字符串方法名——表达力不对称原则）"),
        Stripped("ConverterBackMethodParamTypes", "Channel-input mirror (B 形态 Fluent 独有，D-n)", "特性侧内联转换器立项时（表达力不对称原则）"),
        Stripped("ConverterBackMethodParamNames", "Channel-input mirror (B 形态 Fluent 独有，D-n)", "特性侧内联转换器立项时（表达力不对称原则）"),
        Stripped("HasMask", "Channel-input mirror", "长期/暂无"),
        // （接入面 g 派生登记）：[MapFormat] 存在性标志（NCMAP021 触发载体）——
        // 通道输入镜像组（与 HasMask 同口径），PropertyRef 层无格式串文本承载（pm 级 FormatString 承载）
        Stripped("HasMapFormat", "Channel-input mirror", "长期/暂无"),
        Stripped("MaskType", "Channel-input mirror", "长期/暂无"),
        Stripped("MapPropertyFromField", "Channel-input mirror", "长期/暂无"),
        Stripped("MapPropertyFromSource", "Channel-input mirror", "长期/暂无"),
        Stripped("HasMapNoAutoInclude", "Channel-input mirror", "长期/暂无"),
        // Fluent 显式声明标记（声明面通道输入镜像，非判定产物——剥离）
        Stripped("HasFluentExplicit", "Channel-input mirror", "长期/暂无"),
        // Location group (Table 5 #3)
        Stripped("Location", "Location class (Table 5 #3)", "长期/暂无"),
    };

    // ----------------------------------------------------------------
    // PropertyMappingSnapshot：Compared = init 存储位；Stripped = 表达式体派生位。
    // 计数不在此登记——由 SnapshotEquivalenceComparerTests.PropertyMappingSnapshot_CoverageProjectionUnionStripped_EqualsFullSet
    // + SnapshotFieldRegistryTests.AllTypes_ComparedUnionStripped_EqualsReflectionFullSet
    // + ComparerFieldCoverageTests（双向臂 + 总计数守门）钉死
    // ----------------------------------------------------------------
    public static IReadOnlyList<FieldRegistration> PropertyMappingSnapshot { get; } = new[]
    {
        // Compared — init storage (Table 2: 12)
        Compared("Target", "PropertyRef projection"),
        Compared("Source", "PropertyRef projection"),
        Compared("SourcePath", "Per-segment PropertyRef projection"),
        Compared("SourceIndexes", "Direct SequenceEqual"),
        Compared("ReverseWritePath", "Direct"),
        Compared("IsFlattened", "Direct"),
        Compared("PathOrigin", "Direct"),
        Compared("ConverterDeclaringType", "Direct"),
        Compared("ConverterMethodName", "Direct"),
        Compared("ConverterShape", "Direct"),
        Compared("MaskType", "Direct"),
        // 格式化串承载（Compared——双侧同承载：特性 [MapFormat] 与
        // Fluent .Format 填同一字段；增量缓存正确性——改格式串必须触发失效）
        Compared("FormatString", "Direct (双侧同承载——格式化串)"),
        Compared("IsFromJson", "Direct"),
        // 条件守卫四字段（Compared——双侧同承载 P-e：Fluent .MapWhen 与
        // 特性 [MapWhen] 填同一字段；B 形态 Body/ParamName 为 Fluent 独有（D-n），特性侧恒 null）
        Compared("ConditionMethodRef", "Direct (双侧同承载 P-e)"),
        Compared("ConditionMethodBody", "Direct (B 形态转录产物——Fluent 独有，D-n)"),
        Compared("ConditionMethodParamName", "Direct (B 形态形参名——Fluent 独有，D-n)"),
        Compared("ConditionShape", "Direct (谓词入参形态)"),
        // B 形态转换器方法体三字段（Compared——与条件守卫 B 形态字段同口径：
        // 三元组双侧同承载（特性 [Convert] 与 Fluent .Convert 填同一 Converter* 三字段），
        // 方法体三字段为 Fluent 独有（D-n），特性侧恒 null——null==null 比较 Through）
        Compared("ConverterMethodBody", "Direct (B 形态转录产物——Fluent 独有，D-n)"),
        Compared("ConverterMethodParamTypes", "SequenceEqual Ordinal (B 形态形参类型组——Fluent 独有，D-n)"),
        Compared("ConverterMethodParamNames", "SequenceEqual Ordinal (B 形态形参名组——Fluent 独有，D-n)"),
        // Stripped — expression-bodied derived (no comparison gap — pure function of covered fields)
        Stripped("HasIndex", "Expression-bodied derived (pure function of SourceIndexes)", "长期/暂无"),
        Stripped("IsReverseWriteBack", "Expression-bodied derived (pure function of ReverseWritePath)", "长期/暂无"),
    };

    // ----------------------------------------------------------------
    // DiagnosticSnapshot：Compared = 诊断标识与参数位；Stripped = Location（Location 类）。
    // 计数不在此登记——由 SnapshotFieldRegistryTests.AllTypes_ComparedUnionStripped_EqualsReflectionFullSet
    // + ComparerFieldCoverageTests（双向臂 + 总计数守门）钉死
    // ----------------------------------------------------------------
    public static IReadOnlyList<FieldRegistration> DiagnosticSnapshot { get; } = new[]
    {
        Compared("DescriptorId", "Ordinal string"),
        Compared("Args", "Sequence equal (Q4: if order unstable, normalize by sorting)"),
        Compared("IsExplicit", "Direct"),
        Stripped("Location", "Location class (Table 5 #2)", "长期/暂无"),
    };

    // ----------------------------------------------------------------
    // DeepCopyNodeShape：Compared = 图节点形状位；Stripped = Location + IsString/IsDelegate（预登记）。
    // 计数不在此登记——由 SnapshotFieldRegistryTests.AllTypes_ComparedUnionStripped_EqualsReflectionFullSet
    // + ComparerFieldCoverageTests（双向臂 + 总计数守门）钉死
    // ----------------------------------------------------------------
    public static IReadOnlyList<FieldRegistration> DeepCopyNodeShape { get; } = new[]
    {
        Compared("Type", "TypeRef native Equals (recursive; no Location field)"),
        Compared("HasPublicParameterlessCtor", "Direct"),
        Compared("PublicConstructors", "Recursive (ConstructorRef pure value type, native Equals)"),
        Compared("Properties", "Recursive (DeepCopyPropertyShape projection)"),
        Compared("IsLeaf", "Direct"),
        Compared("IsCollectionLike", "Direct"),
        Compared("IsDictionary", "Direct"),
        Compared("KeyType", "TypeRef native Equals"),
        Compared("ValueType", "TypeRef native Equals"),
        Compared("ElementType", "TypeRef native Equals"),
        Compared("IsNonGenericEnumerable", "Direct"),
        Compared("IsArray", "Direct"),
        Compared("ArrayElementType", "TypeRef native Equals"),
        Compared("IsNullableValueType", "Direct"),
        Compared("UnderlyingType", "TypeRef native Equals"),
        Compared("IsInterface", "Direct"),
        Compared("IsTypeParameter", "Direct"),
        Compared("IsPointer", "Direct"),
        Compared("IsValueType", "Direct"),
        // Stripped
        Stripped("Location", "Location class (Table 5 #5)", "长期/暂无"),
        Stripped("IsString", "TypeRef 派生冗余 (string 由 Type 推导)", "长期/暂无"),
        Stripped("IsDelegate", "TypeRef 派生冗余 (delegate 由 Type 推导)", "长期/暂无"),
    };

    // ----------------------------------------------------------------
    // DeepCopyPropertyShape：Compared = 属性形状位；Stripped = Location + HasMapIgnore。
    // 计数不在此登记——由 SnapshotFieldRegistryTests.AllTypes_ComparedUnionStripped_EqualsReflectionFullSet
    // + ComparerFieldCoverageTests（双向臂 + 总计数守门）钉死
    // ----------------------------------------------------------------
    public static IReadOnlyList<FieldRegistration> DeepCopyPropertyShape { get; } = new[]
    {
        Compared("Name", "Ordinal string"),
        Compared("Type", "TypeRef native Equals"),
        Compared("GetterIsPublic", "Direct"),
        Compared("GetterIsVirtual", "Direct"),
        Compared("GetterIsSealed", "Direct"),
        Compared("ContainingTypeIsSealed", "Direct"),
        Compared("HasSetter", "Direct"),
        Compared("SetterIsPublic", "Direct"),
        Compared("SetterIsInitOnly", "Direct"),
        // Stripped
        Stripped("HasMapIgnore", "Channel-input mirror (attr reads [MapIgnore], fluent doesn't; same caliber as PropertyRef attr-snapshot group)", "长期/暂无"),
        Stripped("Location", "Location class (Table 5 #4)", "长期/暂无"),
    };

    // ----------------------------------------------------------------
    // ComposeMappingSnapshot：Compared = 组合映射位（MaskType/IsFromJson
    // 入册 Compared——已校验镜像通道，行为驱动字段，与主源 PropertyMappingSnapshot.MaskType
    // Compared 同构）；Stripped = HasIndex（derived——模型无 Location 字段，假设的
    // Location 在该类型不存在，实测反射核对后删登——冻结断言首跑红）；Target.PropertyRef
    // 原始 Mask/JSON 值维持 Channel-input mirror Stripped（PropertyRef 组）——双承载比较口径闭环（P0-4）。
    // 计数不在此登记——由 SnapshotFieldRegistryTests.AllTypes_ComparedUnionStripped_EqualsReflectionFullSet
    // + ComparerFieldCoverageTests（双向臂 + 总计数守门）钉死
    // ----------------------------------------------------------------
    public static IReadOnlyList<FieldRegistration> ComposeMappingSnapshot { get; } = new[]
    {
        Compared("SubSource", "TypeRef native Equals"),
        Compared("Source", "PropertyRef projection"),
        Compared("SourcePath", "Per-segment PropertyRef projection"),
        Compared("SourceIndexes", "Direct SequenceEqual (normalized: if both HasIndex=false, treat equivalent)"),
        Compared("Target", "PropertyRef projection"),
        Compared("ConverterDeclaringType", "Ordinal string"),
        Compared("ConverterMethodName", "Ordinal string"),
        Compared("ConverterShape", "Direct"),
        Compared("MaskType", "Direct (已校验镜像通道——行为驱动字段)"),
        // Compose 格式化串承载（Compared——已校验镜像通道，与主源
        // PropertyMappingSnapshot.FormatString Compared 同构；物化管线出口值化漏项即静默失效）
        Compared("FormatString", "Direct (已校验镜像通道——Compose 格式化串)"),
        Compared("IsFromJson", "Direct (已校验镜像通道——行为驱动字段)"),
        // 块内 MapWhen 条件守卫四字段（Compared——与主源
        // PropertyMappingSnapshot 条件四字段同构；块内声明为 Fluent 独有承载位（特性侧无块形态），
        // 特性对 ComposeMappings 恒无该值——null==null 比较 Through）
        Compared("ConditionMethodRef", "Direct (块内守卫)"),
        Compared("ConditionMethodBody", "Direct (块内守卫 B 形态)"),
        Compared("ConditionMethodParamName", "Direct (块内守卫 B 形态)"),
        Compared("ConditionShape", "Direct (块内守卫形态)"),
        // B 形态转换器方法体三字段（Compared——块内 Convert 内联转录；
        // 块形态为 Fluent 独有承载位（特性侧无块），特性对 ComposeMappings 恒无该值——null==null Through）
        Compared("ConverterMethodBody", "Direct (块内 Convert B 形态转录产物)"),
        Compared("ConverterMethodParamTypes", "SequenceEqual Ordinal (块内 Convert B 形态形参类型组)"),
        Compared("ConverterMethodParamNames", "SequenceEqual Ordinal (块内 Convert B 形态形参名组)"),
        // Stripped
        Stripped("HasIndex", "Expression-bodied derived (pure function of SourceIndexes; S2 — derived redundancy)", "长期/暂无"),
    };

    // ----------------------------------------------------------------
    // SubclassPairSnapshot：Compared 全列（B-4 入册）——
    // 字段集与 SnapshotEquivalenceComparer.CompareSubclassPair 手写投影实比字段逐一对应，无 Stripped 项。
    // 入册前该类型不享 A1 冻结断言（新增字段零强制，登记面与 Equals 册不对称）；入册后 A1 第 8 类型、
    // A4 类型域、A5 分类蕴含即时生效，新增字段未登记即红。
    // ----------------------------------------------------------------
    public static IReadOnlyList<FieldRegistration> SubclassPairSnapshot { get; } = new[]
    {
        Compared("SourceType", "Direct (手写投影既有实比 4 字段——B-4 补登记面冻结)"),
        Compared("TargetType", "Direct (手写投影既有实比 4 字段——B-4 补登记面冻结)"),
        Compared("SourceClassName", "Direct (手写投影既有实比 4 字段——B-4 补登记面冻结)"),
        Compared("TargetClassName", "Direct (手写投影既有实比 4 字段——B-4 补登记面冻结)"),
    };

    // ----------------------------------------------------------------
    // All 入口——遍历全部快照类型节（供全类型断言 + 注册表完整性核查消费）
    // ----------------------------------------------------------------
    public static IEnumerable<(string TypeName, IReadOnlyList<FieldRegistration> Fields)> All
    {
        get
        {
            yield return (nameof(MappingSnapshot), MappingSnapshot);
            yield return (nameof(PropertyRef), PropertyRef);
            yield return (nameof(PropertyMappingSnapshot), PropertyMappingSnapshot);
            yield return (nameof(DiagnosticSnapshot), DiagnosticSnapshot);
            yield return (nameof(DeepCopyNodeShape), DeepCopyNodeShape);
            yield return (nameof(DeepCopyPropertyShape), DeepCopyPropertyShape);
            yield return (nameof(ComposeMappingSnapshot), ComposeMappingSnapshot);
            yield return (nameof(SubclassPairSnapshot), SubclassPairSnapshot);
        }
    }

    // ----------------------------------------------------------------
    // 类型级"类型 → 比较器方法"映射列（B-4-2）——
    // B-3 比较器体护栏（ComparerFieldCoverageTests）与顶层 case 分派穷举断言的
    // 单点数据源：类型→方法映射只在此登记一次，不在测试类内硬编码（消灭"多处登记、单处生效"形态）。
    // 键 = All 的类型名（nameof 口径）；键集与 All 类型集集合相等护栏在 ComparerFieldCoverageTests。
    // 值 = SnapshotEquivalenceComparer 内对应 Compare* 方法名；MappingSnapshot → null——顶层走
    // 反射枚举 + denylist 派生（TopLevelDenyList 读注册表 Stripped），无手写投影体可扫，B-3 两臂跳过
    // 该类型（扫描面矩阵见）。
    // 方法名以字符串登记而非 nameof：Compare* 为 private，跨类 nameof 不可达；其存在性由
    // ComparerFieldCoverageTests 的类内方法定位机械保证（定位失败即红并指名类型与方法名）。
    // ----------------------------------------------------------------
    internal static readonly IReadOnlyDictionary<string, string?> ComparerMethodByType =
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [nameof(MappingSnapshot)] = null,
            [nameof(PropertyRef)] = "ComparePropertyRef",
            [nameof(PropertyMappingSnapshot)] = "ComparePropertyMapping",
            [nameof(DiagnosticSnapshot)] = "CompareDiagnostic",
            [nameof(DeepCopyNodeShape)] = "CompareDeepCopyNode",
            [nameof(DeepCopyPropertyShape)] = "CompareDeepCopyProperty",
            [nameof(ComposeMappingSnapshot)] = "CompareComposeMapping",
            [nameof(SubclassPairSnapshot)] = "CompareSubclassPair",
        };


    // ----------------------------------------------------------------
    // 工厂方法
    // ----------------------------------------------------------------
    private static FieldRegistration Compared(string field, string reason)
        => new(field, FieldRole.Compared, reason, "N/A — compared field, long-term compared");

    private static FieldRegistration Stripped(string field, string reason, string migrationCondition)
        => new(field, FieldRole.Stripped, reason, migrationCondition);
}

/// <summary>
/// 字段角色：Compared（参与比较）/ Stripped（剥离，不参与比较）。
/// </summary>
internal enum FieldRole
{
    /// <summary>参与比较（Compare* 方法体实际比较的字段）。</summary>
    Compared,
    /// <summary>剥离（不参与比较；Reason + MigrationCondition 登记剥离依据与迁出条件）。</summary>
    Stripped,
}

/// <summary>
/// 字段登记记录（三列：Field / Role / Reason / MigrationCondition）。
/// </summary>
internal sealed record FieldRegistration(string Field, FieldRole Role, string Reason, string MigrationCondition);
