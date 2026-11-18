using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NCMapper.Shared;
using NCMapper.SourceGen;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// 三向判定一致性测试（W2-3 ③ 升级，TG-A）：
/// <c>TypeCompatView.CanConvert</c>（Type 版） × <c>TypeHelpers.HasMappingPath</c>（ITypeSymbol 版）
/// × <c>TypeRefHelpers.HasMappingPath</c>（TypeRef 版）× 期望表——四方一致 + 期望双向锁定。
/// <para>三向判据链已收敛至 <c>TypeCompatibilityCore</c> 单点（判据链步骤 0/1-10 + ImplicitConversions
/// 单点表），本测试网覆盖全部三份门面（修复原 TypeRef 版不在断言范围的遗漏 M4）。</para>
/// <para>X-1/X-2 分叉收敛 = 预期行为变更（任务书 D1.3 清单）：float/double→enum、string→非白名单
/// 值类型由 TypeRef 版 true 收敛为 false（新增跳过 + NCMAP027，生成侧行为由
/// TypeCompatConvergenceTests 锁定）。委托对（D1.3 #5）与 IReadOnlyDictionary 门控（D1.3 #6）
/// 为 Type 版向符号版权威的对齐收敛。</para>
/// <para>已知判定面/执行面差异（设计裁决，2026-08-12，登记，不进入断言）：
/// ① float/double→enum：判定 false（生成侧直转路径不支持），执行面 ConvertValue :86-89 可转（先 ToInt64）；
/// ② bool↔char：判定 true（数值族），ChangeType 执行面可能失败；
/// ③ 任意对象→string：执行面 ToString 兜底全放行，判定面仅 IConvertible 侧放行（含 string→非 IConvertible 引用类型 false）；
/// ④ Stream→string：判定 false（非 IConvertible），执行面 ToString 可转。</para>
/// </summary>
public class TypeCompatViewConsistencyTests
{
    // ---- 矩阵类型（嵌套定义，typeof 与符号双通道） ----
    public enum Color { Red, Green }
    public enum Size : long { S = 1, L = 2 }
    public class A { public int X { get; set; } }
    public class B { public int X { get; set; } }
    public delegate void Del1();
    public delegate int Del2(string s);

    /// <summary>
    /// 类型对矩阵 + 期望结论（判定语义，与判据核心步骤 0/1-10 一一对应）。
    /// 同一期望值同时断言 TypeCompatView（Type 版）/ HasMappingPath（符号版）/ HasMappingPath（TypeRef 版）
    /// ——三向直接比对 + 期望表双向锁定（TG-A）。
    /// </summary>
    public static TheoryData<Type, Type, bool> Matrix => new()
    {
        // 1. 等类型
        { typeof(int), typeof(int), true },
        { typeof(string), typeof(string), true },
        { typeof(List<int>), typeof(List<int>), true },
        { typeof(Color), typeof(Color), true },

        // 2. 隐式数值转换
        { typeof(int), typeof(long), true },
        { typeof(float), typeof(double), true },
        { typeof(int), typeof(decimal), true },

        // 3. 枚举路径（enum?enum / enum?string / enum?底层数值）
        { typeof(Color), typeof(Size), true },
        { typeof(Color), typeof(string), true },
        { typeof(string), typeof(Color), true },
        { typeof(Color), typeof(int), true },
        { typeof(int), typeof(Color), true },
        { typeof(Color), typeof(long), true },

        // 4. 嵌套类（非 string、非泛型、类型不同）
        { typeof(A), typeof(B), true },
        { typeof(A), typeof(string), false },
        { typeof(string), typeof(A), false },

        // 5. 集合?集合（元素级映射，排除字典/string）
        { typeof(List<int>), typeof(List<long>), true },
        { typeof(int[]), typeof(List<int>), true },
        { typeof(List<string>), typeof(IEnumerable<string>), true },
        { typeof(ArrayList), typeof(List<int>), false },          // 非泛型源无元素类型
        { typeof(List<int>), typeof(string), false },

        // 6. 字典?字典（K/V 复制）
        { typeof(Dictionary<string, int>), typeof(Dictionary<string, long>), true },
        { typeof(Dictionary<string, int>), typeof(IDictionary<string, int>), true },

        // 7. 真 List/数组 → Dictionary（受控异常路径，排除 string）
        { typeof(List<string>), typeof(Dictionary<string, string>), true },
        { typeof(string), typeof(Dictionary<string, string>), false },

        // 8. 值类型可空性组合
        { typeof(int?), typeof(int), true },
        { typeof(int), typeof(int?), true },
        { typeof(int?), typeof(long?), true },
        { typeof(int?), typeof(string), true },   // 步骤 9 IConvertible 交叉兜住（int 属 IConvertible 值类型）
        { typeof(string), typeof(int?), true },   // 同上（string?IConvertible）
        { typeof(int?), typeof(Stream), false },

        // 9. IConvertible 交叉转换
        { typeof(string), typeof(int), true },
        { typeof(int), typeof(string), true },
        { typeof(string), typeof(Guid), true },
        { typeof(Guid), typeof(string), true },
        { typeof(string), typeof(DateTime), true },
        { typeof(DateTime), typeof(string), true },
        { typeof(string), typeof(bool), true },
        { typeof(bool), typeof(string), true },
        { typeof(long), typeof(int), true },      // 数值族显式窄化
        { typeof(decimal), typeof(float), true },
        { typeof(char), typeof(int), true },

        // 10. 完全不相容
        { typeof(int), typeof(DateTime), false },
        { typeof(DateTime), typeof(Guid), false },
        { typeof(int), typeof(Guid), false },
        { typeof(string), typeof(Stream), false },   // string→非 IConvertible 引用类型（M4 宽松点收敛）
        { typeof(Stream), typeof(string), false },   // Stream→string：判定面 false（裁决，执行面差异见类注释 ④）
        { typeof(int), typeof(Stream), false },

        // ---- X-1 收敛（预期变更 D1.3 #1）：float/double→enum 由 TypeRef 版 true 收敛为 false ----
        { typeof(float), typeof(Color), false },
        { typeof(double), typeof(Color), false },

        // ---- X-2 收敛（预期变更 D1.3 #2-4）：string→非白名单值类型由 TypeRef 版 true 收敛为 false ----
        { typeof(string), typeof(TimeSpan), false },
        { typeof(string), typeof(DateTimeOffset), false },

        // ---- 委托对收敛（预期变更 D1.3 #5）：Type 版 true→false（对齐符号版 TypeKind.Delegate） ----
        { typeof(Del1), typeof(Del2), false },

        // ---- IReadOnlyDictionary 门控补齐（预期变更 D1.3 #6）：Type 版 false→true（对齐符号版） ----
        { typeof(List<int>), typeof(IReadOnlyDictionaryImpl), true },

        // ---- 非泛型字典对（Q4 场景覆盖：形状成立/K/V 不可提取，步骤 7 兜底 true——两链一致） ----
        { typeof(List<int>), typeof(Hashtable), true },
    };

    /// <summary>仅实现 IReadOnlyDictionary&lt;,&gt; 的字典类（D1.3 #6：Type 版门控补齐对齐符号版）</summary>
    public class IReadOnlyDictionaryImpl : IReadOnlyDictionary<string, int>
    {
        public int Count => 0;
        public IEnumerable<string> Keys => Array.Empty<string>();
        public IEnumerable<int> Values => Array.Empty<int>();
        public int this[string key] => 0;
        public bool ContainsKey(string key) => false;
        public bool TryGetValue(string key, out int value) { value = 0; return false; }
        public IEnumerator<KeyValuePair<string, int>> GetEnumerator()
            => ((IEnumerable<KeyValuePair<string, int>>)Array.Empty<KeyValuePair<string, int>>()).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    // 编译缓存（矩阵用例共享，避免每用例重建 Compilation）
    private static readonly Lazy<CSharpCompilation> s_compilation = new(() =>
        (CSharpCompilation)TestHelper.BuildCompilation("class MatrixMarker { }"));

    /// <summary>运行时 Type → 符号（数组/泛型/嵌套类型桥接）。</summary>
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

        // 嵌套类型 FullName 含 '+'（metadata name 形式），GetTypeByMetadataName 直接接受
        var symbol = compilation.GetTypeByMetadataName(type.FullName!);
        Assert.NotNull(symbol);
        return symbol!;
    }

    /// <summary>运行时 Type → TypeRef（物化通道：TypeToSymbol + MappingExtractor.TypeRefFromSymbol）。</summary>
    private static TypeRef TypeToRef(Type type) => MappingExtractor.TypeRefFromSymbol(TypeToSymbol(type));

    /// <summary>
    /// 三向一致 + 期望双向锁定（TG-A）：TypeCompatView.CanConvert == HasMappingPath（符号版）
    /// == HasMappingPath（TypeRef 版）== 期望。
    /// </summary>
    [Theory]
    [MemberData(nameof(Matrix))]
    public void TriDirectional_Judgment_Consistent(Type source, Type target, bool expected)
    {
        // 向 1：Type 版门面（经 TypeCompatAdapter → 核心）
        Assert.Equal(expected, TypeCompatView.CanConvert(source, target));

        // 向 2：ITypeSymbol 版门面（经 SymbolCompatAdapter → 核心）
        Assert.Equal(expected, TypeHelpers.HasMappingPath(TypeToSymbol(source), TypeToSymbol(target)));

        // 向 3：TypeRef 版门面（经 TypeRefCompatAdapter → 核心；X-1/X-2 收敛后纳入断言——修复 M4 遗漏）
        Assert.Equal(expected, TypeRefHelpers.HasMappingPath(TypeToRef(source), TypeToRef(target)));
    }

    /// <summary>
    /// 向 4：ConvertValue 执行面抽查（判定语义口径）——仅选取执行面无争议的用例；
    /// 既有裁决差异用例（类注释 ①-④）不进入断言。
    /// </summary>
    public static TheoryData<object?, Type, bool> ConvertValueSamples => new()
    {
        // 判定 true → 实际可转换
        { "123", typeof(int), true },
        { 123, typeof(string), true },
        { "2024-01-01", typeof(DateTime), true },
        { "110E8400-E29B-11D4-A716-446655440000", typeof(Guid), true },
        { Color.Red, typeof(string), true },
        { Color.Red, typeof(int), true },
        { 1, typeof(Color), true },
        { 5L, typeof(int), true },
        { 1.5f, typeof(double), true },
        { 3.14m, typeof(float), true },
        { "42", typeof(int?), true },
        // 判定 false → 实际抛 MapException
        { "abc", typeof(Stream), false },
        { 42, typeof(DateTime), false },
        { "not-a-guid", typeof(Guid), false },
        { DateTime.Now, typeof(Guid), false },
    };

    [Theory]
    [MemberData(nameof(ConvertValueSamples))]
    public void ConvertValue_Execution_Matches_Judgment(object? value, Type targetType, bool expectedConvertible)
    {
        var method = typeof(TypeConvertHelper)
            .GetMethod(nameof(TypeConvertHelper.ConvertValue), BindingFlags.Public | BindingFlags.Static)!;
        var generic = method.MakeGenericMethod(value?.GetType() ?? typeof(object), targetType);
        try
        {
            generic.Invoke(null, new[] { value });
            Assert.True(expectedConvertible, $"判定为不可转换但执行成功: {value?.GetType().Name} → {targetType.Name}");
        }
        catch (TargetInvocationException ex) when (ex.InnerException is MapException)
        {
            Assert.False(expectedConvertible, $"判定为可转换但执行失败: {value?.GetType().Name} → {targetType.Name}");
        }
    }
}
