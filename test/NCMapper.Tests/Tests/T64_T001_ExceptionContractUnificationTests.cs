// ============================================================================
// T64 / B5-5 Batch-1 — 异常契约统一红测（A1-02/A1-09/A3-09）
//
// 缺陷承载（审计 20260917-001 -B5-5）：
//   A1-02  MapWhen 谓词解析失败 null 写入 s_whenPredicateCache 后，同键命中
//          `return cached!` 不抛（PropertyMatcher.cs:634）→ 调用方
//          ExpressionBuilder.cs:580-581 直取 method.GetParameters() NRE；
//   A1-09  枚举→非数值值类型目标（DateTime/Guid/TimeSpan）裸 InvalidCastException
//          逃逸（TypeConvertHelper.cs:80-89 仅捕 OverflowException）——执行期探针实证
//          校正任务书"bool/char"前提：.NET 10 上 bool/char 经 IConvertible 路径返回
//          底层值语义不抛异常（行为锁定，见 A1_09_枚举转bool与char_底层值语义_行为锁定）；
//   A3-09  JSON 中转序列化异常（STJ NotSupportedException）在 try 外裸逃逸
//          （JsonConverter.cs:64 Serialize 在 try 外 + :45-49 ObjectConvertToJson
//          全程无 try）——违背 D7 MapException 契约。
//
// 红测纪律：先红后绿；InnerException 类型断言（评审 002 P2-7 /
// 统一要求）。
// ============================================================================

using NCMapper;
using Xunit;

namespace NCMapper.Tests.Tests;

public class T64_T001_ExceptionContractUnificationTests
{
    // ============================== A1-02 ==============================

    /// <summary>
    /// A1-02 红测：methodRef 指向不存在方法 → 首调抛 MapException 且失败 null 入缓存；
    /// <b>不清缓存</b>同键再调（ClearCache 会清 s_whenPredicateCache，故本测试不调用）——
    /// 现状命中 `return cached!` 返 null（红：二次调用不抛）；修复后同键再调仍抛同款
    /// MapException（缓存 null 语义 = 失败标记，单点收敛）。
    /// </summary>
    [Fact]
    public void A1_02_谓词解析失败缓存null_同键二次调用仍抛MapException()
    {
        const string neverExist = "T64Ctx::A1_02_NeverExist_20260919";

        // 首调：解析失败 → null 入缓存 + 抛 MapException（既有行为，回归锁定）
        var first = Assert.Throws<MapException>(() =>
            PropertyMatcher.ResolveWhenPredicate(typeof(T64Ctx), methodRef: neverExist, methodName: null));
        Assert.Contains("A1_02_NeverExist_20260919", first.Message);

        // 同键二次调用（未 ClearCache）：现状返 null（红）→ 修复后抛同款 MapException
        var second = Assert.Throws<MapException>(() =>
            PropertyMatcher.ResolveWhenPredicate(typeof(T64Ctx), methodRef: neverExist, methodName: null));
        Assert.Contains("A1_02_NeverExist_20260919", second.Message);
    }

    /// <summary>
    /// A1-02 反幻觉锚点：解析成功的键二次调用命中缓存返回同一 MethodInfo（缓存语义不变）。
    /// </summary>
    [Fact]
    public void A1_02_谓词解析成功_同键二次调用命中缓存返回同一实例()
    {
        var first = PropertyMatcher.ResolveWhenPredicate(typeof(T64Ctx), methodRef: null, methodName: "T64_When");
        var second = PropertyMatcher.ResolveWhenPredicate(typeof(T64Ctx), methodRef: null, methodName: "T64_When");

        Assert.Same(first, second);
        Assert.True((bool)first!.Invoke(null, new object[] { 0 })!);
    }

    // ============================== A1-09 ==============================
    // 编制前提核正（探针实证，artifacts/t64/probe-a322）：.NET 10 上枚举→bool/char 经
    // IConvertible 路径返回底层值语义（False/'\0'，不抛异常）——任务书"bool/char 裸逃逸"
    // 前提不成立；实测裸逃逸面 = DateTime/Guid/TimeSpan 等非数值值类型目标。红测矩阵按实测面。

    /// <summary>
    /// A1-09 红测：枚举→DateTime——现状 Convert.ChangeType 的 InvalidCastException 裸逃逸
    /// （80-89 仅捕 OverflowException）；修复后统一为 MapException，
    /// InnerException 保留 InvalidCastException（异常契约统一要求）。
    /// </summary>
    [Fact]
    public void A1_09_枚举转DateTime_抛MapException含InnerInvalidCast()
    {
        var ex = Assert.Throws<MapException>(() =>
            TypeConvertHelper.ConvertValue<Color, DateTime>(Color.Red));

        Assert.IsType<InvalidCastException>(ex.InnerException);
    }

    /// <summary>A1-09 红测：枚举→Guid——同族非数值值类型目标，同 A1-09 契约。</summary>
    [Fact]
    public void A1_09_枚举转Guid_抛MapException含InnerInvalidCast()
    {
        var ex = Assert.Throws<MapException>(() =>
            TypeConvertHelper.ConvertValue<Color, Guid>(Color.Red));

        Assert.IsType<InvalidCastException>(ex.InnerException);
    }

    /// <summary>A1-09 红测：枚举→TimeSpan——同族非数值值类型目标，同 A1-09 契约。</summary>
    [Fact]
    public void A1_09_枚举转TimeSpan_抛MapException含InnerInvalidCast()
    {
        var ex = Assert.Throws<MapException>(() =>
            TypeConvertHelper.ConvertValue<Color, TimeSpan>(Color.Red));

        Assert.IsType<InvalidCastException>(ex.InnerException);
    }

    /// <summary>
    /// A1-09 行为锁定（编制期实证，非缺陷面）：枚举→bool/char 经 IConvertible 路径
    /// 返回底层值语义（0→false/'\0'，非零→true/底层值字符）——本批不改该语义。
    /// </summary>
    [Fact]
    public void A1_09_枚举转bool与char_底层值语义_行为锁定()
    {
        Assert.False(TypeConvertHelper.ConvertValue<Color, bool>(Color.Red));
        Assert.True(TypeConvertHelper.ConvertValue<Color, bool>(Color.Green));
        Assert.Equal('\0', TypeConvertHelper.ConvertValue<Color, char>(Color.Red));
    }

    /// <summary>
    /// A1-09 反幻觉锚点：枚举→数值（既有 OverflowException→MapException 契约）与
    /// 枚举→string 行为不变。
    /// </summary>
    [Fact]
    public void A1_09_枚举转数值与字符串_既有行为不变()
    {
        Assert.Equal(1, TypeConvertHelper.ConvertValue<Color, int>(Color.Green));
        Assert.Equal(2, TypeConvertHelper.ConvertValue<Color, decimal>(Color.Blue));
        Assert.Equal("Red", TypeConvertHelper.ConvertValue<Color, string>(Color.Red));
    }

    // ============================== A3-09 ==============================

    /// <summary>
    /// A3-09 红测：JSON 中转（JsonConvertToTarget）序列化方向遇 STJ 不支持类型
    /// （IntPtr 成员）——现状 NotSupportedException 在 try 外裸逃逸（64）；
    /// 修复后统一为 MapException，InnerException 保留 NotSupportedException。
    /// </summary>
    [Fact]
    public void A3_09_Json中转_序列化不支持类型_抛MapException含InnerNotSupported()
    {
        var src = new A3_09_Unsupported { Handle = 42 };

        var ex = Assert.Throws<MapException>(() =>
            JsonConverter.JsonConvertToTarget<A3_09_Unsupported, A3_09_Unsupported>(src));

        Assert.IsType<NotSupportedException>(ex.InnerException);
    }

    /// <summary>
    /// A3-09 红测：ObjectConvertToJson（全程无 try，:45-49）同场景——现状裸逃逸；
    /// 修复后统一为 MapException + InnerException。
    /// </summary>
    [Fact]
    public void A3_09_ObjectConvertToJson_序列化不支持类型_抛MapException含InnerNotSupported()
    {
        var src = new A3_09_Unsupported { Handle = 42 };

        var ex = Assert.Throws<MapException>(() => JsonConverter.ObjectConvertToJson(src));

        Assert.IsType<NotSupportedException>(ex.InnerException);
    }

    /// <summary>
    /// A3-09 反幻觉锚点：可序列化对象的中转与序列化行为不变；
    /// null 入参行为不变（空串/default）。
    /// </summary>
    [Fact]
    public void A3_09_可序列化对象与null入参_既有行为不变()
    {
        var ok = new A3_09_Plain { Name = "n" };
        Assert.Contains("\"Name\"", JsonConverter.ObjectConvertToJson(ok));

        var round = JsonConverter.JsonConvertToTarget<A3_09_Plain, A3_09_Plain>(ok);
        Assert.Equal("n", round!.Name);

        Assert.Equal("", JsonConverter.ObjectConvertToJson(null!));
        Assert.Null(JsonConverter.JsonConvertToTarget<A3_09_Plain, A3_09_Plain>(null));
    }

    // ============================== 模型 ==============================

    private sealed class T64Ctx
    {
        public static bool T64_When(int v) => v >= 0;
    }

    private enum Color { Red, Green, Blue }

    /// <summary>含 STJ 不支持类型成员（IntPtr）——序列化方向抛 NotSupportedException。</summary>
    private sealed class A3_09_Unsupported
    {
        public nint Handle { get; set; }
    }

    private sealed class A3_09_Plain
    {
        public string Name { get; set; } = "";
    }
}
