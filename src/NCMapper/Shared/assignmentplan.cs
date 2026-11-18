using System;

namespace NCMapper.Shared;

/// <summary>
/// 赋值决策类别 — 三渲染器统一分发依据（② 枢纽深化）。
/// <para>消除 MemberInit 引擎 / Block 引擎 / 源生成器三份决策树，
/// 三渲染器按 Kind 分发渲染，只渲染计划不决策。</para>
/// <para>与运行时 BuildAssignmentForProperty 决策链（ExpressionBuilder.cs:286-358）和
/// MemberInit 快速路径（MapperBase.TryBuildSingleBinding）逐条对照，
/// 含第 1 轮补充的 Json/CrossTable 两类 + ② 新增 4 类前置判定 Kind。</para>
/// </summary>
internal enum AssignmentKind
{
    /// <summary>
    /// 直接赋值（等类型；含同类型集合/字典的复制路径由执行器区分）
    /// </summary>
    Direct,

    /// <summary>
    /// 类型转换（ConvertValue / 可空条件转换）
    /// </summary>
    Convert,

    /// <summary>
    /// 集合赋值（同型走 BuildSameTypeCollectionAssignment；异型走 BuildCrossTypeAssignment 集合分支）
    /// </summary>
    Collection,

    /// <summary>
    /// 嵌套类映射（GetClassExpression 递归）
    /// </summary>
    Nested,

    /// <summary>
    /// 忽略（MapIgnore/全局忽略，调用方已判定）
    /// </summary>
    Ignore,

    /// <summary>
    /// 跳过（不可达即跳过；生成侧对应 NCMAP027 语义）
    /// </summary>
    Skip,

    /// <summary>
    /// [MapProperty(IsFromJson=true)] JSON 反序列化赋值
    /// </summary>
    Json,

    /// <summary>
    /// 跨表导航赋值（[MapProperty] FromSource≠源类型，补充）
    /// </summary>
    CrossTable,

    /// <summary>
    /// ConvertUsing 自定义转换器赋值（前置判定收进 Plan）
    /// <para>运行时渲染器按此 Kind 反射查询 ConvertAttribute 实例；
    /// 生成侧渲染器按此 Kind 从 PropertyMappingSnapshot 方法引用三字段取特性信息。</para>
    /// </summary>
    ConvertUsing,

    /// <summary>
    /// Mask 脱敏赋值（前置判定收进 Plan）
    /// <para>运行时渲染器按此 Kind 反射查询 MaskAttribute 实例；
    /// 生成侧渲染器按此 Kind 从 PropertyMappingSnapshot.MaskType（TypeRef）取特性信息。</para>
    /// </summary>
    Mask,

    /// <summary>
    /// ConvertUsing + Mask 组合赋值（双标有序执行——先转换再脱敏）
    /// <para>运行时渲染器反射查询两个特性实例；生成侧从 Snapshot 取方法引用三字段 + MaskType。</para>
    /// </summary>
    ConvertUsingThenMask,

    /// <summary>
    /// ConvertBackUsing 反向转换器赋值（仅反向消费）
    /// <para>运行时渲染器反射查询 ConvertBackAttribute 实例；
    /// 生成侧从 PropertyMappingSnapshot 方法引用三字段取（C-2 快照复用承载）。</para>
    /// </summary>
    ConvertBackUsing,

    /// <summary>
    /// 格式化赋值（[MapFormat] / Fluent .Format；源 IFormattable → 目标 string）
    /// <para>运行时渲染器按此 Kind 反射查询 MapFormatAttribute 实例（PropertyIgnoreChecker 缓存方法）；
    /// 生成侧渲染器按此 Kind 从 PropertyMappingSnapshot.FormatString（条件发射字段）取格式串。
    /// 枚举值追加于末尾（策略数组按 (int)Kind 下标注册，追加不移动既有槽位）。</para>
    /// </summary>
    Format,

    /// <summary>
    /// 格式化 + 脱敏组合赋值（双标有序执行——先格式化再脱敏；
    /// 与 ConvertUsingThenMask 同型）
    /// <para>运行时渲染器反射查询两个特性实例（MapFormatAttribute + MaskAttribute）；
    /// 生成侧从 Snapshot 取 FormatString + MaskType。</para>
    /// </summary>
    FormatThenMask,
}

/// <summary>
/// 可空组合形态— 生成侧 C2b 分发链
/// ⑨⑩⑪（IsNullableToNullable / IsNullableToNonNullable / IsNonNullableToNullable）三分支的决策承载。
/// <para>仅生成侧 PlanForAssignment 产出；运行时 AssignmentPlanner 置默认值 None 零消费
/// （运行时三渲染器 switch(Kind) 零变化，Q6 同构约束）。</para>
/// </summary>
internal enum NullableKind
{
    /// <summary>
    /// 非可空组合（⑥⑦⑫⑬⑭ 等其余分支；运行时默认值）
    /// </summary>
    None,

    /// <summary>
    /// Nullable&lt;T&gt; → Nullable&lt;U&gt;（分发链 ⑨，三子分支：同底层直赋/隐式三元/ConvertValue 回退）
    /// </summary>
    NullableToNullable,

    /// <summary>
    /// Nullable&lt;T&gt; → 非可空 U（分发链 ⑩，三子分支：同底层/隐式底层/ConvertValue 回退）
    /// </summary>
    NullableToNonNullable,

    /// <summary>
    /// 非可空 T → Nullable&lt;U&gt;（分发链 ⑪，两子分支：底层兼容 new 构造/ConvertValue 回退）
    /// </summary>
    NonNullableToNullable,
}

/// <summary>
/// 赋值计划 — AssignmentPlanner 的决策输出，三渲染器按 Kind 分发渲染。
/// <para>Plan 只携带决策结果（Kind + 形态 Type?），不携带特性实例——
/// 特性实例由渲染器按 Kind 自行查询（运行时反射 / 生成侧从 Snapshot）。
/// 这是编译期/运行时类型系统差异的客观约束：生成侧无运行时特性实例，
/// 只有符号信息（PropertyMappingSnapshot 方法引用三字段/MaskType）。</para>
/// <para>注：Type? 辅助字段当前无消费方——两运行时渲染器（BuildAssignmentForProperty:439-486、
/// TryBuildSingleBinding:602-651）switch plan.Kind 时均未读取 ConvertTargetType/NestedTargetType/
/// JsonTargetType/CrossTableSourceType（直接用 PropertyInfo 类型）。生成侧 adapter 产出时 Type? 填 null
/// 不构成障碍。若 ④ 策略化深化需消费该字段，须重新设计载体。</para>
/// <para>A2-10处置登记：四字段为死字段保留——生成侧预留，当前
/// 运行时渲染器零读取、生成侧恒填 null；删除须同步生成侧 adapter 构造点（record 契约面），
/// 按最小修改原则注释事实化保留，字段删除属 B5-2 域。</para>
/// <para>新增 3 承载字段（NullableCompatibility / IsDictionaryShape /
/// IsRegisteredMapFromPair）——仅生成侧 PlanForAssignment 产出，运行时 AssignmentPlanner 全构造点
/// 置默认值零消费（readonly record struct 值相等性：运行时新字段恒默认，缓存键相等性不变，Q6）。</para>
/// </summary>
internal readonly record struct AssignmentPlan(
    AssignmentKind Kind,
    // Direct/Convert：转换目标类型（当前无消费方，生成侧填 null）
    Type? ConvertTargetType = null,
    // Nested：嵌套映射目标类型（当前无消费方，生成侧填 null）
    Type? NestedTargetType = null,
    // Json：反序列化目标类型；CrossTable：跨表导航源类型（当前无消费方，生成侧填 null）
    Type? JsonTargetType = null,
    Type? CrossTableSourceType = null,
    // 规则 7（可空→非可空值类型条件转换）标志：执行器走 Conditional 构建而非 ConvertValue
    bool IsNullableConditional = false,
    // ②反向 ConvertUsing 降级标记
    // 反向 [Convert] 命中时 Plan 不产 ConvertUsing Kind 而降级为 Convert Kind，
    // 此标记=true 使 RenderPlanAsBinding 见此标记即 shouldFallback（保持现状走 Block ConvertValue 路径）
    bool IsConvertedFromConvertUsing = false,
    // ⑨⑩⑪ 可空组合承载（生成侧决策；运行时默认 None 零消费）
    NullableKind NullableCompatibility = NullableKind.None,
    // ⑭ 双字典形态承载（复用 Collection Kind，AssignmentKind 无 Dictionary 值
    // Direct 注释"含同类型集合/字典的复制路径由执行器区分"先例；生成侧决策，运行时默认 false 零消费）
    bool IsDictionaryShape = false,
    // ⑫ 注册对子分支承载（[MapFrom] 注册对 → GeneratedMapper 委托；
    // 生成侧决策（输入与 GenerateNestedClassMapping 现状一致：pm.Source.Type 原始属性类型），
    // 运行时默认 false 零消费）
    bool IsRegisteredMapFromPair = false,
    // 注册表腿承载位（运行时决策腿产出；null = 特性腿现状语义；
    // 运行时全构造点默认值零消费场景下缓存键相等性不变，位段先例同形态）
    string? RegistryMaskTypeName = null);
