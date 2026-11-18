// ============================================================================
// P1Fix_T003 — 批次 2，接口集合深拷贝 +
// 构参默认值验收用例。
//
// （006 审计 20260827-005）：DeepCopyCompiler.TryGetCollectionCopyMethod
// 通用回退把 IList<T> 等接口集合分派 CopyEnumerable → 无 IEnumerable<T> 构造器 →
// JsonSerializerBase 往返（STJ 仅绑定公共 setter 丢 internal setter 属性 + 不接
// CycleCache 拆散 DAG + 实测 6.1× 性能退化）。修复：接口集合直接构造 List<T> 返回
//（元素逐个 DeepCopy 接 ctx，DAG 引用一致）。
//
// TryCompileRecordCreator 构参参数名与属性名不匹配时静默填 null/0，
// 不消费 HasDefaultValue。修复：默认值按构造器声明填充；无默认值保持 Default + Trace。
// ============================================================================

using Xunit;

namespace NCMapper.Tests.Tests;

public class P1Fix_T003_DeepCopy
{
    // ============================== A1/A2 ==============================

    /// <summary>
    /// A1 补充：IEnumerable&lt;T&gt; 接口形态深拷贝不走 JSON 回退——
    /// internal setter 属性不丢失（JSON 回退行为区分器，同已退役的 P0RedBaseline_S04 红测）。
    /// </summary>
    [Fact]
    public void S04_IEnumerable接口集合_不走Json回退_属性无丢失()
    {
        var src = new List<T3Item> { new() { Name = "keep-enumerable" } };
        IEnumerable<T3Item> ifaceSrc = src;

        var copy = Mapper.DeepCopy(ifaceSrc)!;

        Assert.NotNull(copy);
        var list = Assert.IsAssignableFrom<List<T3Item>>(copy); // 构造 List<T> 返回
        Assert.Single(list);
        Assert.Equal("keep-enumerable", list[0].Name);
    }

    /// <summary>
    /// A2：循环引用安全——接口集合元素引用集合自身（环）时深拷贝不爆栈
    ///（元素级 DeepCopyObjectCore 接 ctx → DeepCopy&lt;T&gt; CycleCache 环检测）。
    /// JSON 回退路径不支持循环引用（遇环即抛），为行为区分器。
    /// 注：属性级"同一实例多引用 → 同一副本"为全局既有语义（普通属性同不保证，
    /// CycleCache 语义 = 环安全），不在本缺陷修复范围。
    /// </summary>
    [Fact]
    public void S04_接口集合循环引用_深拷贝环安全不爆栈()
    {
        var list = new List<T3Item> { new() { Name = "ring" } };
        var holder = new T3Holder2 { Items = list };
        list[0].Ring = list; // 元素引用集合自身 → 环

        var copy = Mapper.DeepCopy(holder)!;

        Assert.NotNull(copy.Items);
        Assert.Single(copy.Items!);
        Assert.Equal("ring", copy.Items![0]!.Name);
    }

    /// <summary>
    /// A2 补充：IReadOnlyCollection&lt;T&gt; 形态（List 实现的接口族）同径收敛。
    /// </summary>
    [Fact]
    public void S04_IReadOnlyCollection接口_构造List返回()
    {
        var src = new List<T3Item> { new() { Name = "ro" } };
        IReadOnlyCollection<T3Item> ifaceSrc = src;

        var copy = Mapper.DeepCopy(ifaceSrc)!;

        Assert.IsAssignableFrom<List<T3Item>>(copy);
        Assert.Equal("ro", ((List<T3Item>)copy)[0].Name);
    }

    // ============================== A4 ==============================

    /// <summary>
    /// A4：构参参数名与属性名不匹配（lvl vs Level）且带默认值 -1 时，
    /// 深拷贝副本按构造器默认值填充（原静默填类型默认值 0）；init-only 属性
    /// 不被 FillProperties 回填，构造值可观察。
    /// </summary>
    [Fact]
    public void S07_构参默认值_按HasDefaultValue填充()
    {
        var src = new T7CtorItem(42, 99); // 源属性 Level=99；lvl 参数名不匹配 → 走默认值路径

        var copy = Mapper.DeepCopy(src)!;

        Assert.Equal(42, copy.A);
        Assert.Equal(-1, copy.Level); // 构造器默认值 -1（修复前静默填 0）
    }

    /// <summary>
    /// 反向锚点：按名匹配的参数不受默认值逻辑影响（属性值正常拷贝）。
    /// </summary>
    [Fact]
    public void S07_按名匹配参数_属性值正常拷贝()
    {
        var src = new T7MatchedItem { A = 7, Level = 8 };

        var copy = Mapper.DeepCopy(src)!;

        Assert.Equal(7, copy.A);
        Assert.Equal(8, copy.Level);
    }

    // ============================== 模型 ==============================

    public sealed class T3Item
    {
        public string Name { get; internal set; } = "";

        public IList<T3Item>? Ring { get; set; } // 循环引用安全用例：元素引用集合自身
    }

    public sealed class T3Holder
    {
        public IList<T3Item>? First { get; set; }
        public IList<T3Item>? Second { get; set; }
    }

    public sealed class T3Holder2
    {
        public IList<T3Item>? Items { get; set; }
    }

    public sealed class T7CtorItem
    {
        public int A { get; set; }

        public int Level { get; } // get-only：不被 FillProperties 回填，构造值可观察

        public T7CtorItem(int a, int lvl = -1)
        {
            A = a;
            Level = lvl;
        }
    }

    public sealed class T7MatchedItem
    {
        public int A { get; set; }

        public int Level { get; set; }
    }
}