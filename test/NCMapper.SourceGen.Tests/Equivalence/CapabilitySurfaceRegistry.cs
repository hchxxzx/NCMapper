using System;
using System.Collections.Generic;

namespace NCMapper.SourceGen.Tests.Equivalence;

/// <summary>
/// 能力面登记分类（三层门禁）：
/// <list type="bullet">
/// <item>A：双通道等价对已覆盖（Registration = <see cref="EquivalenceRegistry"/> A 类编号）</item>
/// <item>B：Fluent 通道独有（Registration = B 类编号）</item>
/// <item>C：[MapFrom] 特性通道独有缺口（Registration = 追踪任务号 T-0xx；关闭前 CI 可见）</item>
/// <item>N-A：非映射能力面（Registration = 理由，必填）</item>
/// </list>
/// </summary>
internal enum CapabilitySurfaceClass
{
    A,
    B,
    C,
    NA,
}

/// <summary>
/// 能力面登记行 —— (AttributeType, Member) 二元键的唯一人工登记。
/// <para>Descriptor 为漂移检测锚：type-level 行 = [AttributeUsage] ValidOn 归一化串
///（如 "Class"、"Property"、"Assembly|Class"）；member 行 = 属性类型名（如 "Type"、"Boolean"）。
/// 特性新增/参数新增/AttributeUsage 位变化而未登记 → RunCheck4 必红（CI 门禁语义）。</para>
/// <para><b>P5 分层测试锚点（004 方案书甲方案——机制先于用例）</b>
/// EndToEndAnchor = L3 整机端到端测试文件（NCMapper.Tests）；OrmAnchor = L4 ORM 集成测试文件
///（IntegrationTests）。按 Registration（A-xx）归组——Check 5 断言每组 ≥1 行锚点非空，
/// 或显式 "N/A:&lt;理由&gt;"（规则性退出/不适用格——004 口径，非目标承引不做用例级镜像）。
/// L2 生成端锚 = EquivalenceRegistry A 行 fixture（既有 Check 1/2 已覆盖，不另设列）。</para>
/// </summary>
internal sealed record CapabilitySurfaceRow(
    string AttributeType,
    string? Member,
    string Descriptor,
    CapabilitySurfaceClass Class,
    string Registration,
    string Note,
    string? EndToEndAnchor = null,
    string? OrmAnchor = null);

/// <summary>
/// 能力面完备性登记表（主件）——
/// 机械枚举源：NCMapper.Attributes 命名空间全部 Attribute 派生类型（type-level 行）
/// ＋ 各类型 DeclaredOnly 公共实例属性（member 行）。每机械行必须恰好对应一行人工登记。
/// <para>首跑红名单（C 类 5 行，挂追踪号——关闭依据，禁止隐瞒豁免；5/5 已随落地清账）：</para>
/// <list type="number">
/// <item>[MapTree] 树构建配置 →（A-05 仅覆盖嵌套递归映射，树三元组配置无 Fluent 出口）——已关闭转 A-17</item>
/// <item>实体侧 [MapIgnore] →（A-02/A-11 覆盖 DTO 侧忽略；实体侧锚定无 Fluent 出口）——已关闭转 A-14</item>
/// <item>实体侧反向无源 [ConvertBack] →（A-09 覆盖 DTO 侧 .ConvertBack；实体侧锚定无源形态无出口）——已关闭转 A-15</item>
/// <item>FromField 索引路径 →（A-07 覆盖 FromField 点路径；"A.B[0].C" 索引段 lambda 不可表达）——已关闭转 A-16</item>
/// <item>属性级 [MapNoAutoInclude] →（对级 NoAutoMatch〔原 NoAutoInclude 改名〕为 Fluent 独有；属性级特性无 Fluent 出口）——已关闭转 A-18</item>
/// </list>
/// <para>C 类行随对应批次落地转 A 并更新本表（与 EquivalenceRegistry 的 C→A 迁移同模式）。</para>
/// </summary>
internal static class CapabilitySurfaceRegistry
{
    /// <summary>人工登记全集（与机械枚举逐行比对——RunCheck4）。</summary>
    internal static readonly IReadOnlyList<CapabilitySurfaceRow> Rows = new CapabilitySurfaceRow[]
    {
        // ================= MapFromAttribute（特性通道声明锚点）=================
        new("MapFromAttribute", null, "Class", CapabilitySurfaceClass.A, "A-01",
            "对级映射声明能力（声明 (S,T) 对 + 自动属性匹配）——Fluent 侧为 cfg.For<S,T>()，A-01 fixture 覆盖",
            "test/NCMapper.Tests/Runtime/FluentRuntimeHooksTests.cs::PairDeclaration_AutoMatch_ValuesPreserved",
            "test/NCMapper.IntegrationTests/FluentProjectionMatrixTests.cs::PureMapFluent_AsDto_ProjectionPath_ConsumesRegistryAlias"),
        new("MapFromAttribute", "FromSource", "Type", CapabilitySurfaceClass.A, "A-01",
            "源类型声明并入 A-01 对级声明能力"),
        new("MapFromAttribute", "RequiresDowngrade", "Boolean", CapabilitySurfaceClass.NA,
            "IProjectionPolicy 协议位（恒 false）",
            "投影降级判定协议位而非映射能力声明面"),

        // ================= MapIgnoreAttribute =================
        new("MapIgnoreAttribute", null, "Property", CapabilitySurfaceClass.A, "A-02",
            "DTO 侧忽略（双向）双通道等价——Fluent .Member(d).Ignore()",
            "test/NCMapper.Tests/Runtime/FluentRuntimeIgnoreIndexPathTests.cs::Ignore_BothWay_ForwardSkipped_ReverseKept",
            "test/NCMapper.IntegrationTests/FluentProjectionMatrixP2Tests.cs::Translatable_IgnoreBoth_SkipsBinding_NotDegraded"),
        // 改挂：A-14 退役（S 侧锚定形态双通道规则性退出）→ A-11 承载侧声明
        //（fixture 特性侧锚 DTO 属性、DTO 标 [MapFrom]——实体侧（源侧锚定）形态为已知退出）
        new("MapIgnoreAttribute", "Direction", "Direction", CapabilitySurfaceClass.A, "A-11",
            "承载侧方向忽略 [MapIgnore(Direction)] ↔ Fluent .Ignore(dest, Direction) 双通道等价（改挂自 A-14——登记面收缩为承载侧声明，实体侧形态为已知退出）",
            "test/NCMapper.Tests/Runtime/FluentRuntimeIgnoreIndexPathTests.cs::Ignore_ToDto_ForwardSkipped_ReverseKept",
            "test/NCMapper.IntegrationTests/FluentProjectionMatrixP2Tests.cs::Translatable_IgnoreDirectionToEntity_BindsForward_NotDegraded"),
        new("MapIgnoreAttribute", "RequiresDowngrade", "Boolean", CapabilitySurfaceClass.NA,
            "IProjectionPolicy 协议位（恒 false）",
            "非映射能力声明面"),

        // ================= MaskAttribute =================
        new("MaskAttribute", null, "Property", CapabilitySurfaceClass.A, "A-03",
            "脱敏声明双通道等价——Fluent .Member(d).Mask<M>()",
            "test/NCMapper.Tests/Runtime/FluentRuntimeFormatFromJsonMaskTests.cs::Mask_MaskedValue_NotPlaintext",
            "test/NCMapper.IntegrationTests/FluentProjectionMatrixP2Tests.cs::Untranslatable_Mask_ForcesDegrade"),
        new("MaskAttribute", "MaskType", "Type", CapabilitySurfaceClass.A, "A-03",
            "脱敏类型参数并入 A-03 能力"),
        new("MaskAttribute", "OverrideIgnore", "Boolean", CapabilitySurfaceClass.A, "A-03",
            "忽略豁免参数并入 A-03 能力（与 Convert 系 OverrideIgnore 同语义）"),
        new("MaskAttribute", "RequiresDowngrade", "Boolean", CapabilitySurfaceClass.NA,
            "IProjectionPolicy 协议位（=true 触发投影降级）",
            "行为由 A-03 等价对锁定，非声明面"),

        // ================= MapPropertyAttribute =================
        new("MapPropertyAttribute", null, "Property", CapabilitySurfaceClass.A, "A-07",
            "显式属性映射双通道等价——Fluent .Member(d).From(s)",
            "test/NCMapper.Tests/Runtime/FluentRuntimeIgnoreIndexPathTests.cs::NestedPointPath_EndToEnd",
            "test/NCMapper.IntegrationTests/FluentProjectionMatrixP2Tests.cs::Translatable_FlattenedPointPath_Projected"),
        new("MapPropertyAttribute", "FromSource", "Type", CapabilitySurfaceClass.A, "A-07",
            "跨源声明并入 A-07（Compose 跨源形态另见 A-13）"),
        new("MapPropertyAttribute", "FromField", "String", CapabilitySurfaceClass.A, "A-16",
            "FromField 索引路径 ↔ Fluent lambda 索引段双通道等价（G7 落地，红名单第 3 行关闭——A-16 fixture 锁定；点路径由 A-07 覆盖）",
            "test/NCMapper.Tests/Runtime/FluentRuntimeIgnoreIndexPathTests.cs::IndexPath_FirstElement_EndToEnd",
            "test/NCMapper.IntegrationTests/FluentProjectionMatrixP2Tests.cs::Translatable_IndexSegment_Projected"),
        new("MapPropertyAttribute", "IsFromJson", "Boolean", CapabilitySurfaceClass.A, "A-12",
            "JSON 反序列化形态双通道等价——Fluent .Member(d).FromJson()",
            "test/NCMapper.Tests/Runtime/FluentRuntimeFormatFromJsonMaskTests.cs::FromJson_Deserializes_EndToEnd",
            "test/NCMapper.IntegrationTests/FluentProjectionMatrixP2Tests.cs::Untranslatable_IsFromJson_ForcesDegrade"),
        new("MapPropertyAttribute", "OverrideIgnore", "Boolean", CapabilitySurfaceClass.A, "A-07",
            "忽略豁免参数并入 A-07 能力"),
        new("MapPropertyAttribute", "RequiresDowngrade", "Boolean", CapabilitySurfaceClass.NA,
            "IProjectionPolicy 协议位（=IsFromJson 触发投影降级）",
            "非声明面"),

        // ================= ConvertAttribute =================
        new("ConvertAttribute", null, "Property", CapabilitySurfaceClass.A, "A-10",
            "静态转换器双通道等价——Fluent .Convert",
            "test/NCMapper.Tests/Runtime/FluentRuntimeConvertTests.cs::Convert_T1_Lambda_EndToEnd",
            "test/NCMapper.IntegrationTests/FluentProjectionMatrixP2Tests.cs::Untranslatable_Converter_ForcesDegrade"),
        new("ConvertAttribute", "DeclaringType", "Type", CapabilitySurfaceClass.A, "A-10",
            "转换器锚定类型并入 A-10"),
        new("ConvertAttribute", "MethodName", "String", CapabilitySurfaceClass.A, "A-10",
            "转换器方法名并入 A-10"),
        new("ConvertAttribute", "OverrideIgnore", "Boolean", CapabilitySurfaceClass.A, "A-10",
            "忽略豁免参数并入 A-10 能力"),
        new("ConvertAttribute", "RequiresDowngrade", "Boolean", CapabilitySurfaceClass.NA,
            "IProjectionPolicy 协议位（=true 触发投影降级）",
            "非声明面"),

        // ================= ConvertBackAttribute =================
        // 改挂：A-15 退役（同 A-14）→ A-09 承载侧声明
        new("ConvertBackAttribute", null, "Property", CapabilitySurfaceClass.A, "A-09",
            "承载侧反向锚点 [ConvertBack] ↔ Fluent .Member(d).ConvertBack(...) 双通道等价（改挂自 A-15——登记面收缩为承载侧声明，实体侧形态为已知退出）",
            "test/NCMapper.Tests/Runtime/FluentRuntimeConvertTests.cs::ConvertBack_T1_Reverse_EndToEnd",
            "test/NCMapper.IntegrationTests/FluentProjectionMatrixP2Tests.cs::ConvertBack_NotDegrading_ForwardProjection"),
        new("ConvertBackAttribute", "DeclaringType", "Type", CapabilitySurfaceClass.A, "A-09",
            "反向转换器锚定并入 A-09 能力（同 ConvertBackAttribute 类型行）"),
        new("ConvertBackAttribute", "MethodName", "String", CapabilitySurfaceClass.A, "A-09",
            "反向转换器方法并入 A-09 能力（同 ConvertBackAttribute 类型行）"),
        new("ConvertBackAttribute", "OverrideIgnore", "Boolean", CapabilitySurfaceClass.A, "A-09",
            "反向豁免并入 A-09 能力（同 ConvertBackAttribute 类型行）"),
        new("ConvertBackAttribute", "RequiresDowngrade", "Boolean", CapabilitySurfaceClass.NA,
            "IProjectionPolicy 协议位（恒 false）",
            "非声明面"),

        // ================= MapTreeAttribute =================
        new("MapTreeAttribute", null, "Class", CapabilitySurfaceClass.A, "A-17",
            "[MapTree] 树构建配置 ↔ Fluent .Tree() 双通道等价（G2 落地，红名单第 4 行关闭——A-17 fixture 锁定；A-05 仅覆盖嵌套递归映射等价，互不重叠）",
            "test/NCMapper.Tests/Runtime/TreeConfigConsumptionTests.cs::ResolveConfig_RegistryCarries_FluentTreeTriplet",
            "test/NCMapper.IntegrationTests/FluentProjectionMatrixP2Tests.cs::TreeConfig_Whitelisted_NotDegrading"),
        new("MapTreeAttribute", "ChildrenProperty", "String", CapabilitySurfaceClass.A, "A-17",
            "树 Children 字段名配置并入 A-17 能力（同 MapTreeAttribute 类型行；J-4 三处默认一致断言覆盖）"),
        new("MapTreeAttribute", "IdProperty", "String", CapabilitySurfaceClass.A, "A-17",
            "树 Id 字段名配置并入 A-17 能力（同 MapTreeAttribute 类型行；J-4 三处默认一致断言覆盖）"),
        new("MapTreeAttribute", "ParentIdProperty", "String", CapabilitySurfaceClass.A, "A-17",
            "树 ParentId 字段名配置并入 A-17 能力（同 MapTreeAttribute 类型行；J-4 三处默认一致断言覆盖）"),

        // ================= MapNoAutoIncludeAttribute =================
        new("MapNoAutoIncludeAttribute", null, "Property", CapabilitySurfaceClass.A, "A-18",
 "属性级 [MapNoAutoInclude] ↔ Fluent .Member(d).NoAutoInclude() 双通道等价（D1 落地，红名单第 5 行关闭——A-18 fixture 锁定；对级 NoAutoMatch()〔原 NoAutoInclude 改名〕仍为 Fluent 独有，语义粒度不同双承载并行）",
            "test/NCMapper.Tests/Runtime/FluentRuntimeIgnoreIndexPathTests.cs::NoAutoInclude_FieldLevel_MapsNormally_OutsideDowngrade",
            "test/NCMapper.IntegrationTests/FluentProjectionMatrixP2Tests.cs::PropertyNoAutoInclude_Consumed_NotDegrading"),

        // ================= MapDefaultsAttribute（程序集级全局配置——G3 已撤销范围 X1）=================
        new("MapDefaultsAttribute", null, "Assembly", CapabilitySurfaceClass.NA,
            "程序集级全局映射配置特性（G3 已撤销，X1 不修项备案）",
            "非属性级映射能力面"),
        new("MapDefaultsAttribute", "NamingStrategy", "NamingStrategy", CapabilitySurfaceClass.NA,
            "X1 不修项",
            "全局命名策略配置——非属性级映射能力面"),
        new("MapDefaultsAttribute", "Prefixes", "String[]", CapabilitySurfaceClass.NA,
            "非映射能力面（全局配置项）——非属性级映射能力面，无等价 A 编号",
            "源属性名前缀剥离（ProjectionNameResolver 消费）；已被覆盖，锚点=MapDefaultsAttributePathCoverageTests.cs::AttrPath_Prefixes_StrippedPrefix_Matches"),
        new("MapDefaultsAttribute", "Postfixes", "String[]", CapabilitySurfaceClass.NA,
            "非映射能力面（全局配置项）——非属性级映射能力面，无等价 A 编号",
            "源属性名后缀剥离（ProjectionNameResolver 消费）；已被覆盖，锚点=MapDefaultsAttributePathCoverageTests.cs::AttrPath_Postfixes_StrippedSuffix_Matches"),
        new("MapDefaultsAttribute", "IgnoreTypes", "Type[]", CapabilitySurfaceClass.NA,
            "X1 不修项",
            "全局忽略类型配置——非属性级映射能力面"),
        new("MapDefaultsAttribute", "IgnoreNames", "String[]", CapabilitySurfaceClass.NA,
            "X1 不修项",
            "全局忽略名配置——非属性级映射能力面"),
        new("MapDefaultsAttribute", "IgnoreNamePrefixes", "String[]", CapabilitySurfaceClass.NA,
            "非映射能力面（全局配置项）——非属性级映射能力面，无等价 A 编号",
            "正反向忽略名前缀；已被覆盖，锚点=MapDefaultsAttributePathCoverageTests.cs::AttrPath_IgnoreNamePrefixes_Suppressed"),
        new("MapDefaultsAttribute", "IgnoreNameSuffixes", "String[]", CapabilitySurfaceClass.NA,
            "非映射能力面（全局配置项）——非属性级映射能力面，无等价 A 编号",
            "正反向忽略名后缀；已被覆盖，锚点=MapDefaultsAttributePathCoverageTests.cs::AttrPath_IgnoreNameSuffixes_Suppressed"),
        new("MapDefaultsAttribute", "IgnoreTypesToEntity", "Type[]", CapabilitySurfaceClass.A,
            "A-11",
            "仅反向生效的忽略类型配置 ≡ DirectionalIgnore 实体向；锚点=MapDefaultsAttributePathCoverageTests.cs::AttrPath_IgnoreTypesToEntity_ForwardStillMaps"),
        new("MapDefaultsAttribute", "IgnoreNamesToEntity", "String[]", CapabilitySurfaceClass.NA,
            "X1 不修项",
            "全局忽略名（实体向）配置——非属性级映射能力面"),
        new("MapDefaultsAttribute", "IgnoreNamePrefixesToEntity", "String[]", CapabilitySurfaceClass.A,
            "A-11",
            "仅反向生效的忽略名前缀配置 ≡ DirectionalIgnore 实体向；锚点=MapDefaultsAttributePathCoverageTests.cs::AttrPath_IgnoreNamePrefixesToEntity_ForwardStillMaps"),
        new("MapDefaultsAttribute", "IgnoreNameSuffixesToEntity", "String[]", CapabilitySurfaceClass.A,
            "A-11",
            "仅反向生效的忽略名后缀配置 ≡ DirectionalIgnore 实体向；锚点=MapDefaultsAttributePathCoverageTests.cs::AttrPath_IgnoreNameSuffixesToEntity_ForwardStillMaps"),

        // ================= NCMapperOptionsAttribute（程序集/类级诊断开关——G3 已撤销范围 X1）=================
        new("NCMapperOptionsAttribute", null, "Assembly|Class", CapabilitySurfaceClass.NA,
            "诊断报告开关特性（G3 已撤销，X1 不修项）",
            "非属性级映射能力面"),
        new("NCMapperOptionsAttribute", "ReportUnmappedTargetMembers", "Boolean", CapabilitySurfaceClass.NA,
            "X1 不修项",
            "诊断开关配置——非属性级映射能力面"),
        new("NCMapperOptionsAttribute", "ReportUnmappedSourceMembers", "Boolean", CapabilitySurfaceClass.NA,
            "X1 不修项",
            "诊断开关配置——非属性级映射能力面"),

        // ================= MapperConfigAttribute（Fluent 通道声明锚点）=================
        new("MapperConfigAttribute", null, "Class", CapabilitySurfaceClass.A, "A-01",
            "Fluent 通道声明锚点——与 [MapFrom] 同为对级声明能力的两侧入口（A-01 fixture 双通道覆盖）"),

        // ================= BeforeMappingAttribute / AfterMappingAttribute（特性侧钩子）=================
        // BeforeMappingAttribute / AfterMappingAttribute（特性侧钩子）——
        // 双钩子随 →A-20 / →A-19 迁移升 A（等价 fixture EquivalenceTests.LifecycleHooks.cs）
        new("BeforeMappingAttribute", null, "Class", CapabilitySurfaceClass.A, "A-20",
            "生命周期前置钩子对级声明——Fluent .BeforeMapping 终端↔ 特性侧 [BeforeMapping]，等价 fixture ",
            "test/NCMapper.Tests/Runtime/FluentRuntimeHooksTests.cs::BeforeMapping_RunsBeforeAssignments_SourceMutationConsumed",
            "test/NCMapper.IntegrationTests/FluentProjectionMatrixP2Tests.cs::Untranslatable_BeforeMapping_ForcesDegrade"),
        new("BeforeMappingAttribute", "DeclaringType", "Type", CapabilitySurfaceClass.A, "A-20",
            "宿主类型参数（双 ctor 形态）并入 A-20 前置钩子能力（单参=DTO 自身锚定）"),
        new("BeforeMappingAttribute", "MethodName", "String", CapabilitySurfaceClass.A, "A-20",
            "钩子方法名参数并入 A-20 前置钩子能力（public/internal static void(TSource,TTarget)）"),
        new("BeforeMappingAttribute", "RequiresDowngrade", "Boolean", CapabilitySurfaceClass.NA,
            "IProjectionPolicy 协议位（恒 true）",
            "投影降级判定协议位而非映射能力声明面"),
        new("AfterMappingAttribute", null, "Class", CapabilitySurfaceClass.A, "A-19",
 "生命周期后置钩子对级声明——Fluent .AfterMapping 终端 ↔ 特性侧 [AfterMapping]（→ A-19 迁移，等价 fixture EquivalenceTests.LifecycleHooks.cs）",
            "test/NCMapper.Tests/Runtime/FluentRuntimeHooksTests.cs::AfterMapping_RunsAfterAssignments_TargetMutationSticks",
            "test/NCMapper.IntegrationTests/FluentProjectionMatrixP2Tests.cs::Untranslatable_AfterMapping_ForcesDegrade"),
        new("AfterMappingAttribute", "DeclaringType", "Type", CapabilitySurfaceClass.A, "A-19",
            "宿主类型参数（双 ctor 形态）并入 A-19 后置钩子能力（单参=DTO 自身锚定）"),
        new("AfterMappingAttribute", "MethodName", "String", CapabilitySurfaceClass.A, "A-19",
            "钩子方法名参数并入 A-19 后置钩子能力（public/internal static void(TSource,TTarget)）"),
        new("AfterMappingAttribute", "RequiresDowngrade", "Boolean", CapabilitySurfaceClass.NA,
            "IProjectionPolicy 协议位（恒 true）",
            "投影降级判定协议位而非映射能力声明面"),

        // ================= MapWhenAttribute（条件守卫）=================
        // 表达力不对称（D-n 原则）：Fluent 侧双谓词形态（A 命名方法组 + B 内联 lambda 转录），
        // 特性侧仅命名单形态（A）——特性实参为编译期常量，lambda 语法非法；B 形态为 Fluent 独有
        //（承载字段 ConditionMethodBody/ParamName 特性侧恒 null，非独立能力行——P-e 同承载语义）
        new("MapWhenAttribute", null, "Property", CapabilitySurfaceClass.A, "A-21",
            "条件守卫（真才映射）双通道等价——Fluent .MapWhen(dest, cond)（A 命名方法组形态；B 内联转录为 Fluent 独有表达力，D-n）",
            "test/NCMapper.Tests/Runtime/MapWhenFluentExecutionTests.cs::FluentB_T1_New_ConditionFalse_KeepsInitializer",
            "test/NCMapper.IntegrationTests/FluentProjectionMatrixP2Tests.cs::Untranslatable_MapWhenCondition_ForcesDegrade"),
        new("MapWhenAttribute", "MethodName", "String", CapabilitySurfaceClass.A, "A-21",
            "谓词方法名并入 A-21 条件守卫能力（在被标注属性声明类型上按名解析，T1/T2 自动判定）"),
        new("MapWhenAttribute", "RequiresDowngrade", "Boolean", CapabilitySurfaceClass.NA,
            "IProjectionPolicy 协议位（恒 true——守卫谓词 CLR 委托路径，SQL 不可翻译，投影恒降级）",
            "投影降级判定协议位而非映射能力声明面"),

        // ================= MapSubclassAttribute（多态子类型对登记）=================
        // O-1 方案 1（type-level 多实例标注基类 DTO，Inherited=false）；与 Fluent .MapSubclass 终端
        // 同承载 SubclassPairs（P-f 引擎共享）。表达力不对称（D-n）：特性 typeof 实参无泛型约束编译期
        // 拦截——继承校验经 F46 提取期承载（Fluent 侧 CS0311/CS0314 调用点拦截）。
        new("MapSubclassAttribute", null, "Class", CapabilitySurfaceClass.A, "A-22",
            "多态子类型对登记双通道等价——Fluent .MapSubclass<TSrcDerived,TTargetDerived>()（泛型约束调用点拦截）↔ 特性侧 [MapSubclass(typeof, typeof)]（F46 提取期校验）",
            "test/NCMapper.Tests/Tests/MapSubclassRuntimeTests.cs::Map_RegisteredDog_RoutesToDogDto",
            "test/NCMapper.IntegrationTests/FluentProjectionMatrixP2Tests.cs::Untranslatable_SubclassDispatch_ForcesDegrade"),
        new("MapSubclassAttribute", "SourceType", "Type", CapabilitySurfaceClass.A, "A-22",
            "子类源类型实参并入 A-22 多态登记能力（须派生自基类对源——F46）"),
        new("MapSubclassAttribute", "TargetType", "Type", CapabilitySurfaceClass.A, "A-22",
            "子类目标类型实参并入 A-22 多态登记能力（须派生自基类对目标；可等于基类目标——目标收敛 I-2）"),
        new("MapSubclassAttribute", "RequiresDowngrade", "Boolean", CapabilitySurfaceClass.NA,
            "IProjectionPolicy 协议位（恒 true——isinst 分派链 SQL 不可翻译，投影恒降级，分类 SubclassDispatch）",
            "投影降级判定协议位而非映射能力声明面"),

        // ================= MapFormatAttribute（属性级格式化）=================
        // 批内机械登记（评审 P1-1 方案①）→ C→A 迁移收口（2026-09-29）：
        // 等价 fixture EquivalenceTests.MapFormat.cs 已落地（A-23），三行摘除追踪号转 A 类
        new("MapFormatAttribute", null, "Property", CapabilitySurfaceClass.A, "A-23",
            "格式化声明双通道等价——Fluent .Format(dest, format) ↔ 特性 [MapFormat]（A-23 fixture EquivalenceTests.MapFormat.cs 锁定）",
            "test/NCMapper.Tests/Runtime/FluentRuntimeFormatFromJsonMaskTests.cs::Format_EndToEnd_FormattedValue",
            "test/NCMapper.IntegrationTests/FluentProjectionMatrixP2Tests.cs::Untranslatable_FormatString_ForcesDegrade"),
        new("MapFormatAttribute", "Format", "String", CapabilitySurfaceClass.A, "A-23",
            "格式串参数并入 A-23 格式化能力（同类型行）"),
        new("MapFormatAttribute", "OverrideIgnore", "Boolean", CapabilitySurfaceClass.A, "A-23",
            "忽略豁免参数并入 A-23 格式化能力（同类型行）"),
        new("MapFormatAttribute", "RequiresDowngrade", "Boolean", CapabilitySurfaceClass.NA,
            "IProjectionPolicy 协议位（恒 true——格式化为 CLR 路径，SQL 不可翻译，投影恒降级，分类 Format）",
            "投影降级判定协议位而非映射能力声明面"),

        // ================= MapNoAutoMatchAttribute（特性侧对级白名单开关）=================
        // 批内登记为 C 类挂追踪号（Check1 要求 A 行须有 fixture + [EqPair]，A-25 fixture
        // EquivalenceTests.NoAutoMatch.cs 随落地——同文件 C→A 迁移先例，本表 :56 口径）；
        // 转类：本行改 A 类挂 A-25（FluentOnly 转出，等价 fixture 同批落地）。
        new("MapNoAutoMatchAttribute", null, "Class", CapabilitySurfaceClass.A, "A-25",
            "对级白名单开关双通道等价成立（A-25：Fluent For<S,T>().NoAutoMatch() ↔ 特性 [MapNoAutoMatch]，fixture EquivalenceTests.NoAutoMatch.cs——L1 快照字段级含 NoAutoInclude 参与比较 + L2 指定方法体文本级（单方法，非全产物）+ 运行时四象限/反向/写回段负例/NCMAP007 双零）",
            "test/NCMapper.Tests/Runtime/NoAutoMatchRuntimeTests.cs::WhitelistOn_PureReflection_Reverse_AutoMatchNotApplied",
            "test/NCMapper.IntegrationTests/FluentProjectionMatrixP2Tests.cs::Untranslatable_MapNoAutoMatch_ForcesDegrade"),
        new("MapNoAutoMatchAttribute", "RequiresDowngrade", "Boolean", CapabilitySurfaceClass.NA,
            "IProjectionPolicy 协议位（恒 true——白名单语义无法在 SQL 投影中表达，未声明列不得被读出，投影恒降级为二步内存映射）",
            "投影降级判定协议位而非映射能力声明面"),
    };

    /// <summary>Fluent 出口锚点行（B-5）。
    /// <param name="DeclaringType">声明类简单名 = <c>Type.Name</c> 去 <c>`n</c> arity 后缀
    ///（<c>FluentTypeConfig`2 → FluentTypeConfig</c>）——与反射投影共用同一归一函数（001 建议-3）。</param>
    /// <param name="MethodName">公开实例方法名（同名跨类挂载各成一行）。</param>
    /// <param name="CapabilityIds">绑定能力条目号列表，多号以 "/" 分隔；每个号须存在于
    /// <see cref="EquivalenceRegistry.AllCapabilities"/>。</param>
    internal sealed record FluentExitAnchorRow(string DeclaringType, string MethodName, string CapabilityIds);

    /// <summary>
    /// Fluent 出口锚点子表（B-5 数据面——表逐行转写，(声明类, 方法名) 对全集）。
    /// <para><b>契约</b>：每行 = (声明类简单名, 方法名, 能力条目号列表，多号以 "/" 分隔)；
    /// 与 <see cref="FluentSurfaceIntegrityTests"/> 的反射枚举面双向锁定——
    /// 反射多出的对 = UNREGISTERED 红、子表多出的对 = STALE 红；
    /// 行数 / 去重方法名数 / 类数由该测试的计数守门钉死（29 对 / 19 名 / 5 类，基准 = 合计）；
    /// 每行 CapabilityIds 拆分后须存在于 <see cref="EquivalenceRegistry.AllCapabilities"/>。</para>
    /// <para><b>与 <see cref="Rows"/> 的关系（R6 定案）</b>：本子表独立于 <c>Rows</c>——不同数据源
    /// （Fluent 配置类公开面 vs 特性面机械枚举）、不同断言，不参与 RunCheck4；
    /// 能力语义对应关系仍由 <c>Rows</c> 人工行承载，不在本锁射程（检出力边界）。</para>
    /// </summary>
    internal static readonly IReadOnlyList<FluentExitAnchorRow> FluentExitAnchors = new FluentExitAnchorRow[]
    {
        // ---- FluentMappingConfig（对级入口）----
        new("FluentMappingConfig", "For", "A-01"),

        // ---- FluentTypeConfig<,>（对级终端）----
        new("FluentTypeConfig", "Member", "A-02"),
 new("FluentTypeConfig", "MapCtorParam", "B-04"),
        new("FluentTypeConfig", "Tree", "A-17"),
        new("FluentTypeConfig", "NoAutoMatch", "A-25"),
        new("FluentTypeConfig", "AfterMapping", "A-19"),
        new("FluentTypeConfig", "BeforeMapping", "A-20"),
        new("FluentTypeConfig", "MapSubclass", "A-22"),
        new("FluentTypeConfig", "FromSource", "A-13"),

        // ---- MemberHead<,,>（成员链头终端；From / Ignore 为本类独有）----
        new("MemberHead", "From", "A-07/A-16"),
        new("MemberHead", "FromSource", "A-13"),
        new("MemberHead", "Ignore", "A-02/A-11"),
        new("MemberHead", "Mask", "A-03"),
        new("MemberHead", "Format", "A-23"),
        new("MemberHead", "FromJson", "A-12"),
        new("MemberHead", "MapTree", "A-05"),
        new("MemberHead", "Convert", "A-04/A-10"),
        new("MemberHead", "ConvertBack", "A-09"),
        new("MemberHead", "MapWhen", "A-21"),
        new("MemberHead", "NoAutoInclude", "A-18"),

        // ---- MemberConfig<,,>（成员配置段终端；与 MemberHead 同名跨类挂载各成一对）----
        new("MemberConfig", "Mask", "A-03"),
        new("MemberConfig", "Format", "A-23"),
        new("MemberConfig", "FromJson", "A-12"),
        new("MemberConfig", "MapTree", "A-05"),
        new("MemberConfig", "Convert", "A-04/A-10"),
        new("MemberConfig", "ConvertBack", "A-09"),
        new("MemberConfig", "MapWhen", "A-21"),
        new("MemberConfig", "NoAutoInclude", "A-18"),

        // ---- AfterIgnore<,>（Ignore 之后回挂段）----
        new("AfterIgnore", "NoAutoInclude", "A-18"),
    };

    /// <summary>
    /// 投影降级承载登记（捎带门禁）——
    /// IProjectionPolicy 实现特性的 RequiresDowngrade 语义与其 Fluent 承载等价物字段的映射登记。
    /// <para><b>门禁语义</b>：①机械面（反射 NCMapper.Attributes 中全部 IProjectionPolicy 实现）
    /// 必须与本表逐行对应，新特性无登记行 → CI 红；②登记的 Fluent 承载路径必须于
    /// <c>RuntimePropertyMapping</c> 反射可达，缺失即红。<b>边界</b>：只做登记完整性门禁，
    /// 不做自动推导（两通道独立声明是架构原则）。</para>
    /// </summary>
    /// <remarks>
    /// <para>承载路径语义（自 <c>RuntimePropertyMapping</c> 起的反射路径，"." 分段；null = 无 pm 级承载）：
    /// HasMask/HasConverter/IsFromJson 为 pm 直接字段；Target.HasMapIgnore 为信号槽级（Fluent .Ignore
    /// 方向声明物化——投影正向过滤消费，段二）。特性侧语义对照：Mask/ConvertUsing 恒 true、
    /// MapProperty=>IsFromJson（条件降级）、MapFrom/MapIgnore/ConvertBackUsing 恒 false（纯元数据）。</para>
    /// </remarks>
    internal static IReadOnlyDictionary<string, ProjectionDowngradeCarryRow> ProjectionDowngradeCarry { get; }
        = new Dictionary<string, ProjectionDowngradeCarryRow>(StringComparer.Ordinal)
        {
            // ClassifyCategory（门禁④）= ClassifyUntranslatableDeclaration 分类分支名（承载路径
            // 存在 ∨ Classify 实际消费双核查）；ComposeMappings 明细扫描为 Mask/Convert/FromJson/Format/
            // MapWhen 五类的第二承载面（pm 级之后——G-5 收口），随 pm 行同列登记
            ["MaskAttribute"] = new(true, "HasMask",
                "[Mask] 脱敏为 CLR 委托路径，SQL 不可翻译——2026-09-05 裁决：全部形态一律降级",
                ClassifyCategory: "Mask"),
            ["ConvertAttribute"] = new(true, "HasConverter",
                "[Convert] 自定义转换器 CLR 委托，SQL 不可翻译——恒降级",
                ClassifyCategory: "Convert"),
            ["MapPropertyAttribute"] = new(true, "IsFromJson",
                "[MapProperty(IsFromJson=true)] JSON 反序列化 CLR 路径，SQL 不可翻译——条件降级（IsFromJson=true 时）",
                ClassifyCategory: "FromJson"),
            ["MapFromAttribute"] = new(false, null,
                "纯元数据（FromSource 类型声明），恒不降级；Fluent 通道经 IsFluentRegisteredDto/身份键判定承载"),
            ["MapIgnoreAttribute"] = new(false, "Target.HasMapIgnore",
                "纯元数据方向忽略，恒不降级；Fluent .Ignore 声明物化 pm 信号槽方向（投影正向过滤消费——段二）"),
            ["ConvertBackAttribute"] = new(false, null,
                "反向转换器元数据，恒不降级；运行时回退通道不消费 BackConverter*（G6/ 边界，双通道同构）"),
            // 门禁①（门禁红线提前至 Batch-1 收口——新特性不登记 CI 即红）：
            // 钩子为对级承载（RuntimeMapperMetadata.{Before,After}MappingMethodRef，ClassifyUntranslatableDeclaration
            // 直读对级字段），非 pm 级 → FluentCarryPath=null + PairCarryPath 登记；RequiresDowngrade=true（恒降级）
            ["BeforeMappingAttribute"] = new(true, null,
                "生命周期前置钩子 CLR 委托，SQL 不可翻译——恒降级（对级 BeforeMappingMethodRef 承载，非 pm 级）",
                PairCarryPath: "BeforeMappingMethodRef", ClassifyCategory: "BeforeMapping"),
            ["AfterMappingAttribute"] = new(true, null,
                "生命周期后置钩子 CLR 委托，SQL 不可翻译——恒降级（对级 AfterMappingMethodRef 承载，非 pm 级）",
                PairCarryPath: "AfterMappingMethodRef", ClassifyCategory: "AfterMapping"),
            // MapWhen 门禁①登记（计数 8→9——承载链 pm 级 ConditionMethodRef，
            // ClassifyUntranslatableDeclaration 分类 "MapWhen" 消费；Fluent 承载等价物同为
            // pm.ConditionMethodRef（.MapWhen 终端——P-e 双侧同承载））
            ["MapWhenAttribute"] = new(true, "ConditionMethodRef",
                "条件守卫谓词 CLR 委托，SQL 不可翻译——恒降级（pm 级 ConditionMethodRef 承载；Fluent .MapWhen 同字段）",
                ClassifyCategory: "MapWhen"),
            // MapSubclass 门禁登记（计数 9→10——对级承载
            // RuntimeMapperMetadata.HasSubclassDispatch，ClassifyUntranslatableDeclaration 分类
            // "SubclassDispatch" 消费；Fluent 承载等价物同为对级 HasSubclassDispatch
            //（.MapSubclass 终端——P-f 双侧同承载）；isinst 分派链 SQL 不可翻译，恒降级）
            ["MapSubclassAttribute"] = new(true, null,
                "多态 isinst 分派链 SQL 不可翻译——恒降级（对级 HasSubclassDispatch 承载，非 pm 级；Fluent .MapSubclass 同字段）",
                PairCarryPath: "HasSubclassDispatch", ClassifyCategory: "SubclassDispatch"),
            // 批内登记（评审 P1-1 方案①）：MapFormat 门禁登记（计数 10→11——
            // 承载链 pm 级 FormatString，ClassifyUntranslatableDeclaration 分类 "Format" 消费；
            // Fluent 承载等价物同为 pm.FormatString（.Format 终端——双侧同承载）；
            // ToString(格式串) 为 CLR 路径 SQL 不可翻译，恒降级）
            ["MapFormatAttribute"] = new(true, "FormatString",
                "格式化 ToString(format, null) 为 CLR 路径，SQL 不可翻译——恒降级（pm 级 FormatString 承载；Fluent .Format 同字段）",
                ClassifyCategory: "Format"),
            // 批内登记（评审 002-P1-1 承载门禁——计数 11→12）：MapNoAutoMatch 门禁登记——
            // 对级承载 RuntimeMapperMetadata.NoAutoInclude（Fluent 对级 NoAutoMatch() 同字段，双侧同承载）；
            // 白名单开关本身纯元数据，但开启即关自动三样（同名配对/配置感知补匹配/自动扁平化），
            // 未声明列不得被读出 → 投影强制降级（内存映射）——协议位沿 MapFormat/MapWhen/AfterMapping 全族先例。
            // NoAutoMatch 分类落地（对齐表 #9——通道限定 Channel==MapFrom：
            // 特性侧协议位强制降级 / Fluent 侧提取期收窄承载维持不降级，双通道不对称属机制差异）
            ["MapNoAutoMatchAttribute"] = new(true, null,
                "对级白名单开关纯元数据但开启即关自动三样，未声明列不得被读出——投影强制降级（内存映射；对级 NoAutoInclude 承载，Fluent For<S,T>().NoAutoMatch() 同字段）",
                PairCarryPath: "NoAutoInclude", ClassifyCategory: "NoAutoMatch"),
        };
    }

/// <summary>
/// 投影降级承载登记行（捎带门禁数据行；门禁前移增
/// <see cref="ClassifyCategory"/>——分类集登记：实现者 × Classify 分类分支名 × 承载位三列闭环）
/// </summary>
/// <param name="RequiresDowngrade">特性侧 RequiresDowngrade 语义（true=CLR 路径需降级 / false=纯元数据）</param>
/// <param name="FluentCarryPath">Fluent 承载等价物于 RuntimePropertyMapping 起的反射路径（"." 分段；null=无 pm 级承载）</param>
/// <param name="Note">语义说明</param>
/// <param name="PairCarryPath">对级承载字段名（RuntimeMapperMetadata 起的单段反射名；钩子为对级承载）</param>
/// <param name="ClassifyCategory">ClassifyUntranslatableDeclaration 分类分支名（门禁④"Classify 实际消费"
/// 核查的期望值；null = RequiresDowngrade=false 纯元数据无分类——门禁④对 null 行跳过）</param>
internal sealed record ProjectionDowngradeCarryRow(
    bool RequiresDowngrade,
    string? FluentCarryPath,
    string Note,
    string? PairCarryPath = null,
    string? ClassifyCategory = null);
