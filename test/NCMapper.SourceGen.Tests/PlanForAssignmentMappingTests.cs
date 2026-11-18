using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NCMapper.SourceGen;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// TG-P2 PlanForAssignment 映射锁定测试（W3_1，C2b /）：
/// GenerateSinglePropertyAssignment 分发链 ⑥⑦⑨⑩⑪⑫⑬⑭ 八分支判定前移后的映射逐分支锁定。
/// <para>锁定目标（M6 映射表 + 评审 '）：Kind/承载字段与改造前分发链判定一致——
/// ⑥ Direct / ⑦ Convert / ⑨⑩⑪ Convert + NullableCompatibility 三态 / ⑫ Nested +
/// IsRegisteredMapFromPair / ⑬ Collection / ⑭ Collection + IsDictionaryShape（复用 Collection Kind，
/// 不新增枚举值）。</para>
/// <para>交叉形态，字典 vs 集合（⑬ :186 IsDictionary 双侧排除 → 字典对落 ⑭）、
/// 判定序交叉（(int?, int?) 同型先于 ⑨ 走 ⑥——若判定序倒置则本用例红）、NullableToNullable vs Nested。
/// ⑧ 死分支剔除（评审 B-2）；⑮ List→Dict 保留分支反例（"不被误前移"——PlanForAssignment 返回 null，
/// 发射端走 ⑮ 受控异常，T5/F-08 行为由逐字节基线端到端锁定）；⑯⑰ 保留分支反例。</para>
/// <para>IsRegisteredMapFromPair 正例说明：TypeToRef 单测通道不物化 MapFromSources（仅 mapping 物化期
/// Materialize.cs:392 填充），本测试网锁定反例 false；正例（注册对 → GeneratedMapper 委托调用形态）
/// 由 RepresentativeSnapshot 逐字节基线端到端锁定。</para>
/// </summary>
public class PlanForAssignmentMappingTests
{
    // ---- 矩阵类型（typeof → 符号 → TypeRef 三通道桥接，与 TypeCompatViewConsistencyTests 同模式） ----
    public enum Color { Red, Green }
    public class A { public int X { get; set; } }
    public class B { public int X { get; set; } }

    /// <summary>
    /// 八分支映射矩阵（判定序对齐 PlanForAssignment：同型→隐式→可空三态→Nested→集合→字典）：
    /// expectedKind = "null" 表示保留分支（⑧死分支/⑮List→Dict/⑯枚举/⑰fallback——"不被误前移"反例）。
    /// </summary>
    public static TheoryData<Type, Type, string, string, bool, bool> Matrix => new()
    {
        // ============ ⑥ 同类型非集合 → Direct（判据：IsSameType + !IsDictionary + string/!IEnumerable） ============
        { typeof(int), typeof(int), "Direct", "None", false, false },
        { typeof(string), typeof(string), "Direct", "None", false, false },   // string 实现 IEnumerable 但显式排除（标量）
        { typeof(Guid), typeof(Guid), "Direct", "None", false, false },
        // 判定序交叉：同型可空先于 ⑨（⑥ IsSameType("int?") Ordinal 相等命中；若 ⑨ 前置则本用例红）
        { typeof(int?), typeof(int?), "Direct", "None", false, false },
        // ⑥ 反例：同类型集合被 !ImplementsIEnumerable 排除 → 落 ⑬ 复制路径（新实例，行为等价）
        { typeof(List<int>), typeof(List<int>), "Collection", "None", false, false },

        // ============ ⑦ 隐式数值转换 → Convert（NullableCompatibility=None 区分于 ⑨⑩⑪） ============
        { typeof(int), typeof(long), "Convert", "None", false, false },
        { typeof(int), typeof(double), "Convert", "None", false, false },
        { typeof(float), typeof(double), "Convert", "None", false, false },
        // ⑦ 反例：long→int 无隐式 → 保留分支（⑰ ConvertValue fallback）
        { typeof(long), typeof(int), "null", "None", false, false },

        // ============ ⑨ Nullable<T> → Nullable<U> → Convert + NullableToNullable ============
        // 隐式子分支（底层 int→long，渲染端 HasValue 三元——评审 I-3 精确形态）
        { typeof(int?), typeof(long?), "Convert", "NullableToNullable", false, false },
        // ConvertValue 回退子分支（底层 DateTime→TimeSpan 不可隐式）
        { typeof(DateTime?), typeof(TimeSpan?), "Convert", "NullableToNullable", false, false },

        // ============ ⑩ Nullable<T> → 非可空 U → Convert + NullableToNonNullable ============
        // 同底层子分支（HasValue ? Value : default）
        { typeof(int?), typeof(int), "Convert", "NullableToNonNullable", false, false },
        // 隐式底层子分支 + 底层不可转换回退均以承载字段同一锁定（Kind + NullableCompatibility）
        { typeof(int?), typeof(long), "Convert", "NullableToNonNullable", false, false },

        // ============ ⑪ 非可空 T → Nullable<U> → Convert + NonNullableToNullable ============
        // new 构造子分支（底层兼容）
        { typeof(int), typeof(long?), "Convert", "NonNullableToNullable", false, false },
        // ConvertValue 回退子分支（底层不可隐式/显式）
        { typeof(DateTime), typeof(TimeSpan?), "Convert", "NonNullableToNullable", false, false },

        // ============ ⑫ 嵌套 class → Nested + IsRegisteredMapFromPair ============
        // 交叉反例：引用类型对非可空值类型组合——NullableCompatibility=None（NullableToNullable vs Nested 交叉锁定）
        // IsRegisteredMapFromPair 反例：TypeToRef 通道不物化 MapFromSources（正例由逐字节基线端到端锁定）
        { typeof(A), typeof(B), "Nested", "None", false, false },

        // ============ ⑬ 集合形态 → Collection（字典对被 :186 IsDictionary 双侧排除） ============
        { typeof(List<int>), typeof(List<long>), "Collection", "None", false, false },
        { typeof(int[]), typeof(List<int>), "Collection", "None", false, false },   // 数组对
        { typeof(List<string>), typeof(IEnumerable<string>), "Collection", "None", false, false },
        // ⑬ 反例：目标非集合（标量）→ 保留分支
        { typeof(List<int>), typeof(int), "null", "None", false, false },

        // ============ ⑭ 双字典形态 → Collection + IsDictionaryShape=true（复用 Collection Kind，M6-②） ============
        // 交叉形态：字典 vs 集合——⑬ :186 对字典返回 false → 字典对落 ⑭ 承载 IsDictionaryShape=true
        // （若 ⑬ 未排除字典则本组 IsDictionaryShape=false 红——判定序与排除判据双重锁定）
        { typeof(Dictionary<string, int>), typeof(Dictionary<string, long>), "Collection", "None", true, false },
        { typeof(Dictionary<string, int>), typeof(IDictionary<string, int>), "Collection", "None", true, false },

        // ============ ⑮ List→Dict 保留分支反例（"不被误前移"——T5/F-08 受控异常路径） ============
        // ⑬ :186 target.IsDictionary 排除 + ⑭ source 非字典排除 → PlanForAssignment 返回 null，
        // 发射端走 ⑮ 受控异常（NotSupportedException，与运行时 GetListExpression 同构）
        { typeof(List<string>), typeof(Dictionary<string, string>), "null", "None", false, false },
        { typeof(int[]), typeof(Dictionary<int, string>), "null", "None", false, false },

        // ============ ⑯⑰ 保留分支反例（枚举 / fallback——发射端原样） ============
        { typeof(Color), typeof(int), "null", "None", false, false },        // ⑯ TryGenerateEnumConversion 保留
        { typeof(int), typeof(DateTime), "null", "None", false, false },     // ⑰ fallback（完全不相容，前置 NCMAP027）
    };

    // 编译缓存（矩阵用例共享，避免每用例重建 Compilation）
    private static readonly Lazy<CSharpCompilation> s_compilation = new(() =>
        (CSharpCompilation)TestHelper.BuildCompilation("class MatrixMarker { }"));

    /// <summary>运行时 Type → 符号（数组/泛型/嵌套类型桥接，与 TypeCompatViewConsistencyTests 同模式）。</summary>
    private static ITypeSymbol TypeToSymbol(Type type)
    {
        var compilation = s_compilation.Value;
        if (type.IsArray)
            return compilation.CreateArrayTypeSymbol(TypeToSymbol(type.GetElementType()!));

        if (type.IsGenericType && !type.IsGenericTypeDefinition)
        {
            var def = compilation.GetTypeByMetadataName(type.GetGenericTypeDefinition().FullName!);
            Assert.NotNull(def);
            return def!.Construct(type.GetGenericArguments().Select(TypeToSymbol).ToArray());
        }

        var symbol = compilation.GetTypeByMetadataName(type.FullName!);
        Assert.NotNull(symbol);
        return symbol!;
    }

    /// <summary>运行时 Type → TypeRef（物化通道：TypeToSymbol + MappingExtractor.TypeRefFromSymbol）。</summary>
    private static TypeRef TypeToRef(Type type) => MappingExtractor.TypeRefFromSymbol(TypeToSymbol(type));

    /// <summary>
    /// TG-P2 主断言：PlanForAssignment 的 Kind + 三承载字段与 17 分支映射表逐分支一致。
    /// expectedKind = "null" → 计划为 null（保留分支，不被误前移）。
    /// </summary>
    [Theory]
    [MemberData(nameof(Matrix))]
    public void PlanForAssignment_KindAndCarriers_MatchDispatchChain(
        Type source, Type target, string expectedKind, string expectedNullable,
        bool expectedIsDictShape, bool expectedIsRegisteredPair)
    {
        var plan = PlanSymbolAdapter.PlanForAssignment(TypeToRef(source), TypeToRef(target));

        if (expectedKind == "null")
        {
            // 保留分支反例：⑧⑮⑯⑰ 不被误前移——判定端返回 null，发射端走原样保留路径
            Assert.Null(plan);
            return;
        }

        Assert.NotNull(plan);
        Assert.Equal(expectedKind, plan!.Value.Kind.ToString());
        Assert.Equal(expectedNullable, plan.Value.NullableCompatibility.ToString());
        Assert.Equal(expectedIsDictShape, plan.Value.IsDictionaryShape);
        Assert.Equal(expectedIsRegisteredPair, plan.Value.IsRegisteredMapFromPair);
    }
}
