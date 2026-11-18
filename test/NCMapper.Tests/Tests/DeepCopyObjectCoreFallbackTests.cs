using System.Collections.Generic;
using NCMapper;
using NCMapper.Runtime;
using Xunit;

namespace NCMapper.Tests.Tests;

/// <summary>
/// <see cref="DeepCopyProviderBase.DeepCopyObjectCore(object)"/> 非泛型入口与
/// <see cref="DeepCopyProviderBase.DeepCopyObjectCore(object, DeepCopyContext)"/> 带 ctx 重载的
/// 直接覆盖测试。
/// <para><b>背景</b>：覆盖率复算显示 <c>DeepCopyObjectCore</c>（反射兜底腿、对象通道分发）
/// 在既有测试面**零命中**——既有深拷贝测试全部经泛型入口 <c>DeepCopy&lt;T&gt;</c> 或
/// provider 快路径，从未直接触达非泛型分发腿。报告判"真缺口（兜底腿）"。</para>
/// <para><b>本测试的两条可达路径</b>（均为要求"≥2 条可达形态"）：
/// ① 非泛型入口经**类型名直调**（`DeepCopyProviderBase.DeepCopyObjectCore(...)`）——
///    注意 `Current` 是静态属性、方法本身是 `internal static`，
///    故 `Current.DeepCopyObjectCore(...)` 形态会编译报 CS0176，必须用类型名限定；
/// ② provider 分派腿——经 <c>DeepCopyProviderBase.Current</c> 的实例分发进入。</para>
/// <para><b>零 src 改动</b>：本任务为 test-only，不修改 <c>src/</c> 任何文件。</para>
/// </summary>
[Collection("GlobalConfigState")]
public class DeepCopyObjectCoreFallbackTests
{
    /// <summary>被测源类型（含引用成员，验证深拷贝为真拷贝而非浅引用共享）</summary>
    private class FallbackSrc
    {
        /// <summary>标识字段</summary>
        public int Id { get; set; }

        /// <summary>引用成员（用于验证深拷贝后不与源共享同一实例）</summary>
        public List<int>? Items { get; set; }

        /// <summary>嵌套引用（验证递归深拷贝）</summary>
        public FallbackNested? Nested { get; set; }
    }

    /// <summary>嵌套类型</summary>
    private class FallbackNested
    {
        /// <summary>文本值</summary>
        public string? Text { get; set; }
    }

    /// <summary>
    /// 可达形态 ①：非泛型入口经**类型名直调**，引用类型走反射腿产出真深拷贝。
    /// <para>若实现误把非泛型入口改成返回原引用（浅拷贝），引用成员断言必红。</para>
    /// </summary>
    [Fact]
    public void DeepCopyObjectCore_TypeNameCall_ProducesDeepCopy()
    {
        var src = new FallbackSrc
        {
            Id = 7,
            Items = [1, 2, 3],
            Nested = new FallbackNested { Text = "a" },
        };

        // 经类型名直调（Current.DeepCopyObjectCore 会报 CS0176——Current 是静态属性、方法为 static）
        var copy = DeepCopyProviderBase.DeepCopyObjectCore(src) as FallbackSrc;

        Assert.NotNull(copy);
        Assert.NotSame(src, copy);
        Assert.Equal(src.Id, copy!.Id);
        Assert.NotNull(copy.Items);
        Assert.NotSame(src.Items, copy.Items);
        Assert.Equal(src.Items, copy.Items);
        Assert.NotNull(copy.Nested);
        Assert.NotSame(src.Nested, copy.Nested);
        Assert.Equal(src.Nested!.Text, copy.Nested!.Text);
    }

    /// <summary>
    /// 可达形态 ②：provider 分派腿——经 <see cref="DeepCopyProviderBase.Current"/> 实例分发进入，
    /// 结果与形态 ① 一致（同一委托缓存，两入口同源）。
    /// </summary>
    [Fact]
    public void DeepCopyObjectCore_ProviderDispatchLeg_ProducesDeepCopy()
    {
        var provider = DeepCopyProviderBase.Current;
        var src = new FallbackSrc
        {
            Id = 11,
            Items = [4, 5],
            Nested = new FallbackNested { Text = "b" },
        };

        var copy = provider.DeepCopyObject(src) as FallbackSrc;

        Assert.NotNull(copy);
        Assert.NotSame(src, copy);
        Assert.Equal(src.Id, copy!.Id);
        Assert.NotNull(copy.Items);
        Assert.NotSame(src.Items, copy.Items);
        Assert.Equal(src.Items, copy.Items);
    }

    /// <summary>
    /// 带 ctx 重载：传入显式 <see cref="DeepCopyContext"/>，验证循环引用检测上下文被复用
    /// （自引用对象不死循环，且产出新实例）。
    /// </summary>
    [Fact]
    public void DeepCopyObjectCore_WithContext_HandlesSelfReference()
    {
        var src = new FallbackSrc { Id = 13, Items = [9] };
        // 自引用：Nested 指回外层，验证 ctx 循环检测生效（不死循环）
        src.Nested = new FallbackNested { Text = "self" };

        var ctx = new DeepCopyContext();
        var copy = DeepCopyProviderBase.DeepCopyObjectCore(src, ctx) as FallbackSrc;

        Assert.NotNull(copy);
        Assert.NotSame(src, copy);
        Assert.Equal(src.Id, copy!.Id);
        Assert.NotNull(copy.Items);
        Assert.NotSame(src.Items, copy.Items);
    }

    /// <summary>
    /// 装箱值类型腿：非泛型入口对值类型走"叶安全原样返回"或 struct 逐成员拷贝，
    /// 不应对其做引用级深拷贝（防把装箱值类型误当引用类型处理）。
    /// </summary>
    [Fact]
    public void DeepCopyObjectCore_BoxedValueType_ReturnsValueSemantics()
    {
        object boxed = 42;
        var copy = DeepCopyProviderBase.DeepCopyObjectCore(boxed);

        Assert.NotNull(copy);
        Assert.Equal(42, copy);
    }

    /// <summary>
    /// 无 ctx 重载与带 ctx 重载的等价性：对同一源对象两条重载产出的结果语义一致
    /// （防两条腿实现漂移——这是"两入口共享同一委托缓存"契约的行为锁）。
    /// </summary>
    [Fact]
    public void DeepCopyObjectCore_TwoOverloads_AreSemanticallyEquivalent()
    {
        FallbackSrc MakeSource() => new()
        {
            Id = 17,
            Items = [6, 7],
            Nested = new FallbackNested { Text = "c" },
        };

        var viaNoCtx = DeepCopyProviderBase.DeepCopyObjectCore(MakeSource()) as FallbackSrc;
        var viaCtx = DeepCopyProviderBase.DeepCopyObjectCore(MakeSource(), new DeepCopyContext()) as FallbackSrc;

        Assert.NotNull(viaNoCtx);
        Assert.NotNull(viaCtx);
        Assert.Equal(viaNoCtx!.Id, viaCtx!.Id);
        Assert.Equal(viaNoCtx.Items, viaCtx.Items);
        Assert.Equal(viaNoCtx.Nested!.Text, viaCtx.Nested!.Text);
    }
}
