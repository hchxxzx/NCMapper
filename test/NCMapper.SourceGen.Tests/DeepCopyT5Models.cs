// 深拷贝生成腿测试模型
// 成对惯例（同骨架 SgSelfRefEntity/SgSelfRefDto）：Entity 侧用实体类型体系（*Entity），
// DTO 侧用 DTO 类型体系——映射为跨类型递归（构造教训：两侧同类型容器元素会触发
// MapList_X_To_X 同类型映射调用发射而无方法本体的生成缺口，源码级 CS0117）。
// [MapFrom] 触发生成器为映射图内全部类型注册 DeepCopy 委托（GeneratedDeepCopyCache）。
// 承诺行锚点（CodeGenerator.DeepCopy.cs）：
//   Dc5Rec   → 构造路径构参外可写属性四分流 :273-275(array)/:277-280(dict)/:282-284(collection)/:289-290(引用递归)
//   Dc5Host  → 容器分支 :428-433(Queue)/:458-462(Stack)/:480-484(SortedSet)（经 :282-284 调用）＋常规容器 :492+ 通用支
//   Dc5DictHost → 字典子类无公共无参构造降级浅拷贝 :582-586
//   Dc5NoCtor → 构造路径回退支 :203-209（无公共实例构造器 → DeepCopyRuntimeOnly）
//   Dc5CycRec → record 路径循环引用回退 :136-150（非承诺行，行为锁定）
using NCMapper.Attributes;

namespace NCMapper.SourceGen.Tests.Models
{
    // ================================================================
    // 叶子类型对（需深拷贝元素——各容器例共用；实现 IComparable 供 SortedSet 无参构造）
    // ================================================================

    public class Dc5LeafEntity : IComparable<Dc5LeafEntity>
    {
        public int N { get; set; }
        public int CompareTo(Dc5LeafEntity? other) => N - (other?.N ?? 0);
    }

    [MapFrom(typeof(Dc5LeafEntity))]
    public class Dc5Leaf : IComparable<Dc5Leaf>
    {
        public int N { get; set; }
        public int CompareTo(Dc5Leaf? other) => N - (other?.N ?? 0);
    }

    // ================================================================
    // 动作 1：record + init-only 位置参数 + 构参外可写嵌套集合/数组/字典/引用
    // ================================================================

    public class Dc5RecEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public Dc5LeafEntity[] Tags { get; set; } = System.Array.Empty<Dc5LeafEntity>();
        public System.Collections.Generic.Dictionary<string, Dc5LeafEntity> Map { get; set; } = new();
        public System.Collections.Generic.List<Dc5LeafEntity> Items { get; set; } = new();
        public Dc5LeafEntity? Leaf { get; set; }
    }

    [MapFrom(typeof(Dc5RecEntity))]
    public record Dc5Rec(int Id, string Name)
    {
        /// <summary>构参外可写——数组支（273-275）</summary>
        public Dc5Leaf[] Tags { get; set; } = System.Array.Empty<Dc5Leaf>();

        /// <summary>构参外可写——字典支（277-280）</summary>
        public System.Collections.Generic.Dictionary<string, Dc5Leaf> Map { get; set; } = new();

        /// <summary>构参外可写——集合支（282-284）</summary>
        public System.Collections.Generic.List<Dc5Leaf> Items { get; set; } = new();

        /// <summary>构参外可写——引用类型递归 DeepCopy 支（289-290）</summary>
        public Dc5Leaf? Leaf { get; set; }
    }

    // ================================================================
    // 动作 2：生成腿容器清单宿主（元素一律需深拷贝类型——v1.3 约束）
    // ================================================================

    public class Dc5HostEntity
    {
        public int Id { get; set; }
        public System.Collections.Generic.Stack<Dc5LeafEntity> Stack { get; set; } = new();
        public System.Collections.Generic.SortedSet<Dc5LeafEntity> Set { get; set; } = new();
        public System.Collections.Generic.Queue<Dc5LeafEntity> Queue { get; set; } = new();
        public Dc5LeafEntity[] Arr { get; set; } = System.Array.Empty<Dc5LeafEntity>();
        public System.Collections.Generic.List<Dc5LeafEntity> List { get; set; } = new();
        public System.Collections.Generic.HashSet<Dc5LeafEntity> Hash { get; set; } = new();
    }

    [MapFrom(typeof(Dc5HostEntity))]
    public class Dc5Host
    {
        public int Id { get; set; }
        /// <summary>Stack 支（446-467——ToArray+Reverse+Push 保 LIFO）</summary>
        public System.Collections.Generic.Stack<Dc5Leaf> Stack { get; set; } = new();
        /// <summary>SortedSet 支（470-489——Add）</summary>
        public System.Collections.Generic.SortedSet<Dc5Leaf> Set { get; set; } = new();
        /// <summary>Queue 支（414-437——Enqueue）</summary>
        public System.Collections.Generic.Queue<Dc5Leaf> Queue { get; set; } = new();
        /// <summary>常规容器回归组：数组/List/HashSet（492+ 通用支）</summary>
        public Dc5Leaf[] Arr { get; set; } = System.Array.Empty<Dc5Leaf>();
        public System.Collections.Generic.List<Dc5Leaf> List { get; set; } = new();
        public System.Collections.Generic.HashSet<Dc5Leaf> Hash { get; set; } = new();
    }

    // ================================================================
    // 动作 2（字典子类）：仅带参公共构造 → 无公共无参构造 → :582-586 降级浅拷贝
    // ================================================================

    public class Dc5DictEntity : System.Collections.Generic.Dictionary<string, Dc5LeafEntity>
    {
        public Dc5DictEntity(int capacity) : base(capacity) { }
    }

    public class Dc5DictHostEntity
    {
        public int Id { get; set; }
        public Dc5DictEntity Dict { get; set; } = new(4);
    }

    [MapFrom(typeof(Dc5DictHostEntity))]
    public class Dc5DictHost
    {
        public int Id { get; set; }
        public Dc5Dict Dict { get; set; } = new(4);
    }

    /// <summary>字典子类（DTO 侧）——仅带参公共构造器（无公共无参构造）</summary>
    public class Dc5Dict : System.Collections.Generic.Dictionary<string, Dc5Leaf>
    {
        public Dc5Dict(int capacity) : base(capacity) { }
    }

    // ================================================================
    // 执行期补例（203-209 回退支对应用例）：无公共实例构造器 → 回退 DeepCopyRuntimeOnly
    // ================================================================

    public class Dc5NoCtorEntity { public int Id { get; set; } }

    [MapFrom(typeof(Dc5NoCtorEntity))]
    public class Dc5NoCtor
    {
        private Dc5NoCtor() { }
        public int Id { get; set; }
        public static Dc5NoCtor Create(int id) => new() { Id = id };
    }

    // ================================================================
    // 动作 4：record 路径循环引用——自环 → Cyclic → :136-150 回退运行时（ConditionalWeakTable 打破）
    //（Self 双侧各自指向自身类型——骨架 SgSelfRefEntity/SgSelfRefDto 同款）
    // ================================================================

    public class Dc5CycEntity
    {
        public int Id { get; set; }
        public Dc5CycEntity? Self { get; set; }
    }

    [MapFrom(typeof(Dc5CycEntity))]
    public record Dc5CycRec(int Id)
    {
        public Dc5CycRec? Self { get; set; }
    }
}
