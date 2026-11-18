// ============================================================================
// / 修复验收用例
//
// 覆盖验收锚点：
//   ① 环 record 场景：构参外 set 属性成环 + 占位回拷后属性完整、环引用一致；
//   ② getter-only/init-only 口径：构参属性不回填破坏、不抛异常（init-only 非构参属性
//      经填充器 Assign + 回拷承接，验证表达式树对 init setter 的运行时语义）；
//   ③ 缓存一致性：多节点环中所有环回边指向同一最终对象（无 result/placeholder 分裂）；
//  ④（裁决 D3=甲）：Fast 引擎下带参构造类型（Acyclic）统一转 base 占位路径，
//      深拷贝结果完整（分发条件行为验证）；带参构造成环不误抛 RecursionDepthExceeded；
//   ⑤ 双引擎对照：Fast vs ExpressionTree 对同一带参构造环图行为一致。
//
// 模型说明：构参属性成环的 record（位置参数 init-only）受构造顺序悖论限制不可构造
// （a.Next=b 与 b.Next=a 互为先决），环 record 场景以「record + 构参外 set 属性闭合环」
// 形态承载（环回边在构参外属性上，构参内属性非环）；纯构参成环由 class 形态
// （Red_B02 的 B02Node：构参 next + set 属性 Next）覆盖。
// ============================================================================

using Xunit;

namespace NCMapper.Tests.Tests;

public class DeepCopyB02B03S08AcceptanceTests
{
    // ================================================================
    // ① 环 record：构参外 set 属性闭合双向环 + 构参外 Tag 属性
    // ================================================================

    /// <summary>
    /// 环 record（Cyclic → base 占位路径）：占位回拷后构参属性（Name）、
    /// 构参外环属性（Next）、构参外普通属性（Tag）均完整，环回边引用一致。
    /// </summary>
    [Fact]
    public void 环record_构参外属性成环_占位回拷后完整且环引用一致()
    {
        var a = new RingRecord("a") { Tag = "ta" };
        var b = new RingRecord("b") { Tag = "tb" };
        a.Next = b;
        b.Next = a; // 双向环闭合：a↔b（构参外 set 属性）

        var copy = Mapper.DeepCopy(a)!;

        Assert.Equal("a", copy.Name);          // 构参属性：构造器设置 + 回拷
        Assert.Equal("ta", copy.Tag);           // 构参外普通属性：FillProperties 补填 + 回拷
        Assert.Equal("b", copy.Next!.Name);
        Assert.Equal("tb", copy.Next!.Tag);
        Assert.Same(copy, copy.Next!.Next);     // 环回边指向同一最终对象（占位即最终对象）
        Assert.Equal("a", copy.Next!.Next!.Name);
    }

    /// <summary>
    /// 环 record 经 ExpressionTree 引擎深拷贝 — 与 Fast 引擎同构行为（base 路径共享）。
    /// </summary>
    [Fact]
    public void 环record_ExpressionTree引擎_行为与Fast引擎一致()
    {
        var original = DeepCopyProviderBase.Current;
        try
        {
            var a = new RingRecord("a") { Tag = "ta" };
            var b = new RingRecord("b") { Tag = "tb" };
            a.Next = b;
            b.Next = a;

            DeepCopyProviderBase.Current = new ExpressionTreeDeepCopyProvider();
            var copyByExpr = Mapper.DeepCopy(a)!;

            Assert.Equal("a", copyByExpr.Name);
            Assert.Equal("ta", copyByExpr.Tag);
            Assert.Equal("tb", copyByExpr.Next!.Tag);
            Assert.Same(copyByExpr, copyByExpr.Next!.Next);
        }
        finally
        {
            DeepCopyProviderBase.Current = original;
        }
    }

    // ================================================================
    // ② getter-only / init-only 口径
    // ================================================================

    /// <summary>
    /// getter-only 自动属性（带参构造类）：无 setter 可走，经 backing-field 反射回写
    /// 回拷到占位实例（裁决 D1=甲核心场景——record 位置参数无 setter 的等价形态）。
    /// </summary>
    [Fact]
    public void GetterOnly自动属性_带参构造类_回拷完整不抛异常()
    {
        var src = new CtorOnlyNode("hello", 42);

        var copy = Mapper.DeepCopy(src)!;

        Assert.NotSame(src, copy);
        Assert.Equal("hello", copy.Name);  // getter-only：backing field 回写
        Assert.Equal(42, copy.Age);
    }

    /// <summary>
    /// init-only 非构参属性（record）：填充器对 CanWrite（含 init-only）属性的表达式树
    /// Assign 运行时合法——构参外 init-only 属性补填 + 回拷完整，不抛异常。
    /// </summary>
    [Fact]
    public void InitOnly构参外属性_补填与回拷完整()
    {
        var src = new InitNode("n") { Score = 99 };

        var copy = Mapper.DeepCopy(src)!;

        Assert.Equal("n", copy.Name);    // 构参属性（init-only）：构造器 + 回拷
        Assert.Equal(99, copy.Score);    // 构参外 init-only 属性：填充器 Assign + 回拷
    }

    // ================================================================
    // ③ 缓存一致性：多节点环所有环回边指向同一最终对象
    // ================================================================

    /// <summary>
    /// 三节点环（带参构造类）：所有环回边指向同一最终对象——缓存与返回均指向占位，
    /// 无 result/placeholder 分裂。
    /// </summary>
    [Fact]
    public void 三节点环_所有环回边指向同一最终对象()
    {
        var a = new RingNode("a");
        var b = new RingNode("b");
        var c = new RingNode("c");
        a.Next = b;
        b.Next = c;
        c.Next = a;

        var copyA = Mapper.DeepCopy(a)!;
        var copyB = copyA.Next!;
        var copyC = copyB.Next!;

        Assert.Same(copyA, copyC.Next);          // a→b→c→a 闭合
        Assert.Same(copyB, copyA.Next);
        Assert.Same(copyC, copyB.Next);
        Assert.Equal("a", copyC.Next!.Name);    // 环上属性完整（非空壳占位）
        Assert.Equal("b", copyC.Next!.Next!.Name);
        Assert.Equal("c", copyC.Next!.Next!.Next!.Name);
    }

    // ================================================================
    // ④（裁决 D3=甲）：Fast 引擎分发条件行为验证 + 环回归护栏
    // ================================================================

    /// <summary>
    /// Fast 引擎下带参构造 + Acyclic 类型（属性图无环、无无参构造器）：
    /// 修复后统一转发 base 占位路径，深拷贝结果完整（分发条件行为验证）。
    /// </summary>
    [Fact]
    public void Fast引擎_带参构造Acyclic类型_深拷贝完整()
    {
        var src = new AcyclicCtorNode("n") { Note = "extra" };

        var copy = Mapper.DeepCopy(src)!;

        Assert.NotSame(src, copy);
        Assert.Equal("n", copy.Name);       // 构参属性（getter-only：回拷承接）
        Assert.Equal("extra", copy.Note);    // 构参外 set 属性（填充补填）
    }

    /// <summary>
    /// Fast 引擎下带参构造 + 构参链成环（双向环）：正常拷贝不误抛
    /// RecursionDepthExceeded，环引用一致（窗口回归护栏——统一转 base
    /// 占位路径后构参递归经 CycleCache 占位命中，环检测不依赖环形缓冲时序）。
    /// </summary>
    [Fact]
    public void Fast引擎_带参构造构参链成环_不误抛且环引用一致()
    {
        var a = new RingNode("a");
        var b = new RingNode("b");
        a.Next = b;
        b.Next = a; // 双向环：a 的构参递归（构造 b'）内 b.Next 指回 a

        var copy = Mapper.DeepCopy(a)!;

        Assert.Equal("a", copy.Name);
        Assert.Equal("b", copy.Next!.Name);
        Assert.Same(copy, copy.Next!.Next); // 环回边指向同一最终对象
    }

    /// <summary>
    /// Fast 引擎下带参构造 Acyclic 类型经 ExpressionTree 引擎对照 — 双引擎行为一致
    /// （语义在 Fast/ExpressionTree 双引擎下同构：Fast 统一转 base 占位路径后
    /// 与 ExpressionTree 共享 ProviderBase 主流程）。
    /// </summary>
    [Fact]
    public void 双引擎对照_带参构造类型_构参外属性行为一致()
    {
        var original = DeepCopyProviderBase.Current;
        try
        {
            var src = new AcyclicCtorNode("n") { Note = "extra" };

            var copyByFast = Mapper.DeepCopy(src)!;

            DeepCopyProviderBase.Current = new ExpressionTreeDeepCopyProvider();
            var copyByExpr = Mapper.DeepCopy(src)!;

            Assert.Equal(copyByFast.Name, copyByExpr.Name);
            Assert.Equal(copyByFast.Note, copyByExpr.Note);
            Assert.NotSame(copyByFast, copyByExpr);
        }
        finally
        {
            DeepCopyProviderBase.Current = original;
        }
    }

    // ================================================================
    // ⑤ SourceGen 交叉核验（006 决策树分支②）：双引擎对照
    // ================================================================

    /// <summary>
    /// Record 构参外属性双引擎对照 — 生成侧（[MapFrom] 触发生成 DeepCopy，record
    /// 构造器路径 + 构参外属性填充）与运行时侧（无标注 → Fast → base 占位路径：
    /// 统一转发 + 占位回拷 + 构参外补填）行为一致（Note 均不静默丢失）。
    /// </summary>
    [Fact]
    public void 双引擎对照_Record构参外属性_生成侧与运行时行为一致()
    {
        // 生成侧：B03GenRecord 有 [MapFrom] → 生成 DeepCopy（record 路径）
        var genSrc = new Models.B03GenRecord("n") { Note = "extra" };
        var genCopy = Mapper.DeepCopy(genSrc)!;
        Assert.True(Mapper.HasGeneratedDeepCopy<Models.B03GenRecord>());
        Assert.Equal("n", genCopy.Name);
        Assert.Equal("extra", genCopy.Note);   // 生成侧构参外属性填充（修复锚点）

        // 运行时侧：B03RuntimeRecord 无 [MapFrom] → 不触发生成侧，走 Provider 运行时路径
        var rtSrc = new Models.B03RuntimeRecord("n") { Note = "extra" };
        var rtCopy = Mapper.DeepCopy(rtSrc)!;
        Assert.False(Mapper.HasGeneratedDeepCopy<Models.B03RuntimeRecord>());
        Assert.Equal("n", rtCopy.Name);
        Assert.Equal("extra", rtCopy.Note);   // 运行时构参外属性补填（修复锚点）

        // 双引擎行为一致性
        Assert.Equal(genCopy.Name, rtCopy.Name);
        Assert.Equal(genCopy.Note, rtCopy.Note);
    }

    // ================================================================
    // 模型
    // ================================================================

    /// <summary>环 record：构参 Name + 构参外 set 属性 Next（闭合环）+ Tag。</summary>
    private sealed record RingRecord(string Name)
    {
        public RingRecord? Next { get; set; }
        public string? Tag { get; set; }
    }

    /// <summary>带参构造类：getter-only 自动属性（Name/Age 均无 setter，backing field 回写）。</summary>
    private sealed class CtorOnlyNode(string name, int age)
    {
        public string Name { get; } = name;
        public int Age { get; } = age;
    }

    /// <summary>record：构参 Name + 构参外 init-only 属性 Score。</summary>
    private sealed record InitNode(string Name)
    {
        public int Score { get; init; }
    }

    /// <summary>带参构造类：构参 name + set 属性 Next（成环载体）。</summary>
    private sealed class RingNode(string name)
    {
        public string Name { get; } = name;
        public RingNode? Next { get; set; }
    }

    /// <summary>带参构造类（属性图 Acyclic：Name/Note 均为 string 叶子，无无参构造器）。</summary>
    private sealed class AcyclicCtorNode(string name)
    {
        public string Name { get; } = name;
        public string? Note { get; set; }
    }
}
