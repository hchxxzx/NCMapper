using System;
using System.Collections.Generic;
using System.Linq;
using NCMapper.Shared;
using Xunit;

namespace NCMapper.Tests.Tests;

/// <summary>
/// 判据核心纯函数单元测试（W2-3 ③）：
/// TG-B 转换表单点（D1.2 裁决集逐组）/ TG-D 判据分支覆盖（步骤 0/1-10 每步正反 + Error/Unknown 兜底
/// + X-1/X-2 收敛边界）/ TG-E 边界（字典形状成立但 K/V 提取失败——I-4、嵌套极端组合）。
/// <para>TypeShape 经 TypeCompatAdapter（Type 模型）构造；Error/Unknown 态手造（Type 模型不可达）。
/// 本工程 SourceGen 以 Analyzer 形式引用（非编译引用），核心类型无二义性。</para>
/// </summary>
public class TypeCompatibilityCoreTests
{
    private static TypeShape Shape(Type t) => TypeCompatAdapter.Resolve(t);

    private static readonly TypeShape s_error = new() { Kind = TypeShapeKind.Error };
    private static readonly TypeShape s_unknown = new() { Kind = TypeShapeKind.Unknown };

    private enum Color { Red, Green }
    private enum Size : long { S = 1, L = 2 }
    private class A { }
    private class B { }
    public delegate void Del1();
    public delegate int Del2(string s);
    private struct CustomStruct { public int X { get; set; } }

    // ============ TG-B：转换表单点（C3，D1.2 裁决集逐组） ============

    /// <summary>核心表 == C# 规范 §10.3.2 隐式数值转换 10 组（三表比对裁决集）</summary>
    [Fact]
    public void TG_B_ImplicitConversions_Table_Matches_Ruling()
    {
        var t = TypeCompatibilityCore.ImplicitConversions;
        Assert.Equal(10, t.Count);

        void AssertGroup(TypeCompatSpecialTypeId from, params TypeCompatSpecialTypeId[] targets)
            => Assert.True(targets.ToHashSet().SetEquals(t[from]),
                $"转换组 {from} 内容与裁决集不一致");

        AssertGroup(TypeCompatSpecialTypeId.Byte,
            TypeCompatSpecialTypeId.Int16, TypeCompatSpecialTypeId.UInt16,
            TypeCompatSpecialTypeId.Int32, TypeCompatSpecialTypeId.UInt32,
            TypeCompatSpecialTypeId.Int64, TypeCompatSpecialTypeId.UInt64,
            TypeCompatSpecialTypeId.Single, TypeCompatSpecialTypeId.Double, TypeCompatSpecialTypeId.Decimal);
        AssertGroup(TypeCompatSpecialTypeId.SByte,
            TypeCompatSpecialTypeId.Int16, TypeCompatSpecialTypeId.Int32, TypeCompatSpecialTypeId.Int64,
            TypeCompatSpecialTypeId.Single, TypeCompatSpecialTypeId.Double, TypeCompatSpecialTypeId.Decimal);
        AssertGroup(TypeCompatSpecialTypeId.Int16,
            TypeCompatSpecialTypeId.Int32, TypeCompatSpecialTypeId.Int64,
            TypeCompatSpecialTypeId.Single, TypeCompatSpecialTypeId.Double, TypeCompatSpecialTypeId.Decimal);
        AssertGroup(TypeCompatSpecialTypeId.UInt16,
            TypeCompatSpecialTypeId.Int32, TypeCompatSpecialTypeId.UInt32,
            TypeCompatSpecialTypeId.Int64, TypeCompatSpecialTypeId.UInt64,
            TypeCompatSpecialTypeId.Single, TypeCompatSpecialTypeId.Double, TypeCompatSpecialTypeId.Decimal);
        AssertGroup(TypeCompatSpecialTypeId.Int32,
            TypeCompatSpecialTypeId.Int64, TypeCompatSpecialTypeId.Single,
            TypeCompatSpecialTypeId.Double, TypeCompatSpecialTypeId.Decimal);
        AssertGroup(TypeCompatSpecialTypeId.UInt32,
            TypeCompatSpecialTypeId.Int64, TypeCompatSpecialTypeId.UInt64,
            TypeCompatSpecialTypeId.Single, TypeCompatSpecialTypeId.Double, TypeCompatSpecialTypeId.Decimal);
        AssertGroup(TypeCompatSpecialTypeId.Int64,
            TypeCompatSpecialTypeId.Single, TypeCompatSpecialTypeId.Double, TypeCompatSpecialTypeId.Decimal);
        AssertGroup(TypeCompatSpecialTypeId.UInt64,
            TypeCompatSpecialTypeId.Single, TypeCompatSpecialTypeId.Double, TypeCompatSpecialTypeId.Decimal);
        AssertGroup(TypeCompatSpecialTypeId.Char,
            TypeCompatSpecialTypeId.UInt16, TypeCompatSpecialTypeId.Int32, TypeCompatSpecialTypeId.UInt32,
            TypeCompatSpecialTypeId.Int64, TypeCompatSpecialTypeId.UInt64,
            TypeCompatSpecialTypeId.Single, TypeCompatSpecialTypeId.Double, TypeCompatSpecialTypeId.Decimal);
        AssertGroup(TypeCompatSpecialTypeId.Single,
            TypeCompatSpecialTypeId.Double);
    }

    // ============ TG-D：步骤 0 / 步骤 10（Error 兜底 true 与 Unknown 保守 false 显式区分） ============

    [Fact]
    public void TG_D_Step0_Error_Fallback_True()
    {
        Assert.True(TypeCompatibilityCore.CanConvert(s_error, Shape(typeof(int))));
        Assert.True(TypeCompatibilityCore.CanConvert(Shape(typeof(int)), s_error));
        Assert.True(TypeCompatibilityCore.CanConvert(s_error, s_error));
    }

    [Fact]
    public void TG_D_Step10_Unknown_Conservative_False()
    {
        Assert.False(TypeCompatibilityCore.CanConvert(s_unknown, Shape(typeof(int))));
        Assert.False(TypeCompatibilityCore.CanConvert(Shape(typeof(int)), s_unknown));
        Assert.False(TypeCompatibilityCore.CanConvert(s_unknown, s_unknown));
    }

    // ============ TG-D：步骤 1-10 分支正反用例 ============

    // 步骤 1：等类型（含泛型构造）
    [Fact]
    public void TG_D_Step1_Identity_True()
    {
        Assert.True(TypeCompatibilityCore.CanConvert(Shape(typeof(int)), Shape(typeof(int))));
        Assert.True(TypeCompatibilityCore.CanConvert(
            Shape(typeof(List<Dictionary<string, int>>)), Shape(typeof(List<Dictionary<string, int>>))));
    }

    // 步骤 2：隐式数值转换（表内 true / 表外 false）
    [Theory]
    [InlineData(typeof(int), typeof(long), true)]
    [InlineData(typeof(byte), typeof(short), true)]
    [InlineData(typeof(float), typeof(double), true)]
    [InlineData(typeof(int), typeof(byte), true)]       // 隐式表外，但步骤 9 数值族窄化兜住（整体 true）
    [InlineData(typeof(long), typeof(int), true)]        // 同上：步骤 9 数值族窄化兜住
    [InlineData(typeof(int), typeof(int), true)]        // 身份（步骤 1 承载，此处验证整体）
    public void TG_D_Step2_ImplicitNumeric(Type source, Type target, bool expected)
        => Assert.Equal(expected, TypeCompatibilityCore.CanConvert(Shape(source), Shape(target)));

    // 步骤 3：枚举路径（enum?enum / enum?string / enum?底层数值）
    [Theory]
    [InlineData(typeof(Color), typeof(Size), true)]
    [InlineData(typeof(Color), typeof(string), true)]
    [InlineData(typeof(string), typeof(Color), true)]
    [InlineData(typeof(Color), typeof(int), true)]
    [InlineData(typeof(int), typeof(Color), true)]
    public void TG_D_Step3_EnumPath(Type source, Type target, bool expected)
        => Assert.Equal(expected, TypeCompatibilityCore.CanConvert(Shape(source), Shape(target)));

    // 步骤 4：嵌套类（双方 Class、非 string、非泛型；委托收敛 D1.3 #5）
    [Theory]
    [InlineData(typeof(A), typeof(B), true)]
    [InlineData(typeof(A), typeof(string), false)]
    [InlineData(typeof(A), typeof(List<int>), false)]   // 泛型排除
    [InlineData(typeof(Del1), typeof(Del2), false)]     // 委托不归入嵌套类（向符号版权威收敛）
    public void TG_D_Step4_NestedClass(Type source, Type target, bool expected)
        => Assert.Equal(expected, TypeCompatibilityCore.CanConvert(Shape(source), Shape(target)));

    // 步骤 5：集合?集合（元素可提取；非泛型枚举 false）
    [Theory]
    [InlineData(typeof(List<int>), typeof(List<long>), true)]
    [InlineData(typeof(int[]), typeof(List<int>), true)]
    [InlineData(typeof(System.Collections.ArrayList), typeof(List<int>), false)] // 元素不可提取
    [InlineData(typeof(List<int>), typeof(string), false)]
    public void TG_D_Step5_Collection(Type source, Type target, bool expected)
        => Assert.Equal(expected, TypeCompatibilityCore.CanConvert(Shape(source), Shape(target)));

    // 步骤 6：字典?字典（K/V 复制）
    [Fact]
    public void TG_D_Step6_Dictionary_True()
        => Assert.True(TypeCompatibilityCore.CanConvert(
            Shape(typeof(Dictionary<string, int>)), Shape(typeof(Dictionary<string, long>))));

    // 步骤 7：List/数组 → Dictionary（排除 string）
    [Theory]
    [InlineData(typeof(List<string>), typeof(Dictionary<string, string>), true)]
    [InlineData(typeof(string), typeof(Dictionary<string, string>), false)]
    public void TG_D_Step7_ListToDictionary(Type source, Type target, bool expected)
        => Assert.Equal(expected, TypeCompatibilityCore.CanConvert(Shape(source), Shape(target)));

    // 步骤 8：可空组合三态
    [Theory]
    [InlineData(typeof(int?), typeof(int), true)]
    [InlineData(typeof(int), typeof(int?), true)]
    [InlineData(typeof(int?), typeof(long?), true)]
    [InlineData(typeof(int?), typeof(Stream), false)]
    [InlineData(typeof(string), typeof(CustomStruct?), false)] // C-01 + 白名单外：string→Nullable<自定义结构> 无路径
    public void TG_D_Step8_NullableCombination(Type source, Type target, bool expected)
        => Assert.Equal(expected, TypeCompatibilityCore.CanConvert(Shape(source), Shape(target)));

    // 步骤 9：X-1 收敛（float/double→enum 排除——符号版权威，D1.3 #1）
    [Theory]
    [InlineData(typeof(int), typeof(Color), true)]
    [InlineData(typeof(float), typeof(Color), false)]
    [InlineData(typeof(double), typeof(Color), false)]
    [InlineData(typeof(Color), typeof(float), true)]    // enum→数值不排除
    public void TG_D_Step9_X1_NumericToEnum(Type source, Type target, bool expected)
        => Assert.Equal(expected, TypeCompatibilityCore.CanConvert(Shape(source), Shape(target)));

    // 步骤 9：X-2 收敛（string 白名单——符号版权威，D1.3 #2-4）
    [Theory]
    [InlineData(typeof(string), typeof(int), true)]          // 白名单内
    [InlineData(typeof(string), typeof(Guid), true)]         // 白名单内
    [InlineData(typeof(string), typeof(DateTime), true)]     // 白名单内
    [InlineData(typeof(string), typeof(TimeSpan), false)]    // 白名单外值类型
    [InlineData(typeof(string), typeof(CustomStruct), false)]// 白名单外自定义 struct
    [InlineData(typeof(string), typeof(Stream), false)]      // 非 IConvertible 引用类型
    public void TG_D_Step9_X2_StringWhitelist(Type source, Type target, bool expected)
        => Assert.Equal(expected, TypeCompatibilityCore.CanConvert(Shape(source), Shape(target)));

    // 步骤 10：完全不相容
    [Theory]
    [InlineData(typeof(int), typeof(DateTime), false)]
    [InlineData(typeof(DateTime), typeof(Guid), false)]
    [InlineData(typeof(int), typeof(Guid), false)]
    [InlineData(typeof(int), typeof(Stream), false)]
    public void TG_D_Step10_Incompatible(Type source, Type target, bool expected)
        => Assert.Equal(expected, TypeCompatibilityCore.CanConvert(Shape(source), Shape(target)));

    // ============ TG-E：边界 ============

    /// <summary>字典形状成立但 K/V 提取失败 → 步骤 6 false（I-4 对齐符号版；不可枚举使步骤 7 不兜底）</summary>
    [Fact]
    public void TG_E_DictionaryShape_WithoutKeyValue_False()
    {
        var dictA = new TypeShape
        {
            Kind = TypeShapeKind.Interface, IdentityKey = "a",
            IsDictionaryShape = true, HasDictionaryKeyValue = false,
            ImplementsIEnumerable = false,
        };
        var dictB = new TypeShape
        {
            Kind = TypeShapeKind.Interface, IdentityKey = "b",
            IsDictionaryShape = true, HasDictionaryKeyValue = false,
            ImplementsIEnumerable = false,
        };
        Assert.False(TypeCompatibilityCore.CanConvert(dictA, dictB));
    }

    /// <summary>嵌套极端组合：List&lt;Dictionary&lt;K,V&gt;&gt; 同型 true / 异元素型 true（元素级判定按形状，不递归）</summary>
    [Fact]
    public void TG_E_NestedListDictionary()
    {
        Assert.True(TypeCompatibilityCore.CanConvert(
            Shape(typeof(List<Dictionary<string, int>>)),
            Shape(typeof(List<Dictionary<string, int>>))));
        // 元素类型不同（int→long），但步骤 5 仅判元素可提取性（不递归判据）→ true
        Assert.True(TypeCompatibilityCore.CanConvert(
            Shape(typeof(List<Dictionary<string, int>>)),
            Shape(typeof(List<Dictionary<string, long>>))));
    }

    /// <summary>Error 与 Unknown 混合：Error 侧优先兜底（步骤 0 在步骤 10 之前）</summary>
    [Fact]
    public void TG_E_ErrorOverridesUnknown()
    {
        Assert.True(TypeCompatibilityCore.CanConvert(s_error, s_unknown));
        Assert.True(TypeCompatibilityCore.CanConvert(s_unknown, s_error));
    }
}
