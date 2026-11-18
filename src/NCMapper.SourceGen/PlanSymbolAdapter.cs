using Microsoft.CodeAnalysis;
using NCMapper.Shared;

namespace NCMapper.SourceGen;

/// <summary>
/// 生成侧计划 adapter — 将 TypeRef/ITypeSymbol 形态判定映射为 AssignmentKind（② 枢纽深化， T2）。
/// <para>消除 SymbolAssignmentKind 双宇宙：生成侧不再有独立 Kind 枚举，
/// 复用 NCMapper.Shared.AssignmentKind（通过 SourceGen.csproj Link assignmentplan.cs 共享）。</para>
/// <para>adapter 只产出 Kind + 形态 Type?（决策结果），不携带特性实例——
/// 生成侧特性信息由渲染器从 PropertyMappingSnapshot 三字段
/// （ConverterDeclaringType/ConverterMethodName/ConverterShape，T-004b）
/// 与 MaskType（TypeRef）按 Kind 自行取。
/// 这是编译期/运行时类型系统差异的客观约束。</para>
/// <para>adapter 前置判定逻辑与运行时 AssignmentPlanner.Plan(PropertyInfo) 同构，
/// 等价性由 一致性测试验证。类型形态判定改为直接调用 TypeRefHelpers public 形态判定方法
/// （收敛来源为已删除的 TypeRefHelpers.PlanSymbol，T6 删除 SymbolAssignmentKind 后落地）。</para>
/// </summary>
internal static class PlanSymbolAdapter
{
    /// <summary>
    /// 符号版赋值决策 — 产出 AssignmentPlan（Kind + 形态 Type?，与运行时 Kind 同构）。
    /// <para>前置判定（ConvertUsing/Mask/ConvertBackUsing）由调用方（CodeGenerator）从
    /// MappingInfo/PropertyMappingSnapshot 提取后通过参数传入；类型形态判定复用 TypeRefHelpers。</para>
    /// <para>注：Type? 辅助字段填 null（生成侧无运行时 Type，当前无消费方）。</para>
    /// </summary>
    /// <param name="source">源类型引用</param>
    /// <param name="target">目标类型引用</param>
    /// <param name="hasConvertUsing">目标/源属性带 [Convert]</param>
    /// <param name="hasMask">目标属性带 [Mask]</param>
    /// <param name="hasConvertBackUsing">源/目标属性带 [ConvertBack]（仅反向）</param>
    /// <param name="isToDto">方向</param>
    /// <param name="isFromJson">[MapProperty(IsFromJson=true)]</param>
    /// <param name="crossTableFromSource">跨表导航源类型引用（null 表示非跨表）</param>
    /// <returns>赋值计划（Kind + 形态 Type?，Type? 填 null）</returns>
    public static AssignmentPlan Plan(
        TypeRef? source, TypeRef? target,
        bool hasConvertUsing, bool hasMask, bool hasConvertBackUsing,
        bool isToDto, bool isFromJson,
        TypeRef? crossTableFromSource)
    {
        // 1. source/target null → Ignore
        if (source == null || target == null)
            return new AssignmentPlan(AssignmentKind.Ignore);

        // 2. ConvertBackUsing 判定（反向专用，!isToDto 时）
        if (hasConvertBackUsing && !isToDto)
            return new AssignmentPlan(AssignmentKind.ConvertBackUsing);

        // 3. ConvertUsing 判定
        bool isConvertedFromConvertUsing = false;
        if (hasConvertUsing && !isToDto)
        {
            // 反向降级：不消费转换器，与生成端 NCMAP033 同语义
            hasConvertUsing = false;
            isConvertedFromConvertUsing = true;
        }

        // 4. Mask 判定（ConvertUsing 未命中或反向降级后）
        if (hasMask && TypeRefHelpers.IsStringType(target))
        {
            if (hasConvertUsing && isToDto)
                return new AssignmentPlan(AssignmentKind.ConvertUsingThenMask);
            return new AssignmentPlan(AssignmentKind.Mask);
        }

        // 5. ConvertUsing 正向命中（Mask 未命中或非 string）
        if (hasConvertUsing && isToDto)
            return new AssignmentPlan(AssignmentKind.ConvertUsing);

        // 6. isFromJson → Json
        if (isFromJson)
            return new AssignmentPlan(AssignmentKind.Json);

        // 7. crossTableFromSource != null → CrossTable
        if (crossTableFromSource != null)
            return new AssignmentPlan(AssignmentKind.CrossTable);

        // 8. 类型形态决策：直接调用 TypeRefHelpers public 形态判定方法（T6 重构——移除 MapSymbolKindToPlan）
        // 逻辑与已删除的 TypeRefHelpers.PlanSymbol 同构：Nested→Collection→可空→类型不同→等类型
        if (TypeRefHelpers.IsNestedClassProperty(source, target))
            return new AssignmentPlan(AssignmentKind.Nested);
        if (TypeRefHelpers.IsCollectionShape(source, target))
            return new AssignmentPlan(AssignmentKind.Collection);
        if (TypeRefHelpers.IsNullableToNonNullable(source, target)
            || TypeRefHelpers.IsNonNullableToNullable(source, target))
            return new AssignmentPlan(AssignmentKind.Convert, IsConvertedFromConvertUsing: isConvertedFromConvertUsing);
        if (!TypeRefHelpers.IsSameType(source, target))
            return new AssignmentPlan(AssignmentKind.Convert, IsConvertedFromConvertUsing: isConvertedFromConvertUsing);
        return new AssignmentPlan(AssignmentKind.Direct);
    }

    /// <summary>
    /// 分发链赋值决策— 生成侧
    /// GenerateSinglePropertyAssignment 分发链 ⑥⑦⑨⑩⑪⑫⑬⑭ 八分支判定的前移承载。
    /// <para>与既有 <see cref="Plan"/> 为并存独立判定路径（' 执行口径）：
    /// 本方法判定序对齐分发链（同型→隐式→可空三态→Nested→集合→字典）；
    /// 既有 Plan（TryBuildSimpleAssignments 消费）判定序与语义零改动。</para>
    /// <para>返回 null = 其余形态（⑮ List→Dict / ⑯ 枚举 / ⑰ fallback），发射端走保留分支。
    /// ⑧ 死分支（⑤ 首分支全捕获 SourcePath.Count&gt;1，恒 false）不前移——发射端原样保留标记。</para>
    /// <para>⑨⑩⑪ 子分支（同底层/隐式底层/ConvertValue 回退）与 ⑬⑭ 元素/键值类型提取
    /// 由发射端按 NullableCompatibility + 底层类型判定渲染（后注——与现状逐行等价）。</para>
    /// </summary>
    /// <param name="source">有效源类型（GetEffectiveSourceType——与分发链形态判定输入一致）</param>
    /// <param name="target">目标属性类型</param>
    /// <param name="nestedSourceType">⑫ 注册对判定输入（pm.Source.Type 原始属性类型——与
    /// GenerateNestedClassMapping 现状 IsMapFromRegistered 输入一致；末段索引路径下与 source 不同；
    /// null 时回退 source）</param>
    /// <returns>赋值计划（Kind + 承载字段）；null = 保留分支形态</returns>
    internal static AssignmentPlan? PlanForAssignment(TypeRef source, TypeRef target, TypeRef? nestedSourceType = null)
    {
        // ⑥ 同类型非集合（分发链 判据逐行同构——排除字典/string 外 IEnumerable）
        if (TypeRefHelpers.IsSameType(source, target)
            && !TypeRefHelpers.IsDictionaryType(source)
            && (source.SpecialType == SpecialType.System_String
                || !TypeRefHelpers.ImplementsIEnumerable(source)))
            return new AssignmentPlan(AssignmentKind.Direct);

        // ⑦ 隐式数值转换（int→long 等）
        if (TypeRefHelpers.IsImplicitConvertible(source, target))
            return new AssignmentPlan(AssignmentKind.Convert);

        // ⑨ Nullable<T> → Nullable<U>
        if (TypeRefHelpers.IsNullableToNullable(source, target))
            return new AssignmentPlan(AssignmentKind.Convert, NullableCompatibility: NullableKind.NullableToNullable);

        // ⑩ Nullable<T> → 非可空 U
        if (TypeRefHelpers.IsNullableToNonNullable(source, target))
            return new AssignmentPlan(AssignmentKind.Convert, NullableCompatibility: NullableKind.NullableToNonNullable);

        // ⑪ 非可空 T → Nullable<U>
        if (TypeRefHelpers.IsNonNullableToNullable(source, target))
            return new AssignmentPlan(AssignmentKind.Convert, NullableCompatibility: NullableKind.NonNullableToNullable);

        // ⑫ 嵌套 class 映射（注册对判定输入 = pm.Source.Type 原始属性类型，与现状 一致）
        if (TypeRefHelpers.IsNestedClassProperty(source, target))
            return new AssignmentPlan(AssignmentKind.Nested,
                IsRegisteredMapFromPair: CodeGenerator.IsMapFromRegistered(nestedSourceType ?? source, target));

        // ⑬ 集合形态（判定序对齐分发链：先于 ⑭——字典对若被本判定捕获，现状亦然，行为等价保持）
        if (TypeRefHelpers.TryGetCollectionElementTypes(source, target, out _, out _))
            return new AssignmentPlan(AssignmentKind.Collection);

        // ⑭ 双字典形态（复用 Collection Kind + IsDictionaryShape 承载，M6-②）
        if (TypeRefHelpers.TryGetDictionaryKeyValueTypes(source, out _, out _)
            && TypeRefHelpers.TryGetDictionaryKeyValueTypes(target, out _, out _))
            return new AssignmentPlan(AssignmentKind.Collection, IsDictionaryShape: true);

        // 其余（⑮ List→Dict / ⑯ 枚举 / ⑰ fallback）→ 保留分支
        return null;
    }
}
