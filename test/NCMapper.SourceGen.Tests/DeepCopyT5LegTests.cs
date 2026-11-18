// 深拷贝生成腿测试（动作 1/2/4）
// 判据口径：容器例断言三要素＝①容器自身 !ReferenceEquals（新实例）②元素 !ReferenceEquals（递归 DeepCopy——
//   元素一律需深拷贝类型 Dc5Leaf，v1.3 约束）③值相等；Dc5DictHost.Dict 为例外——降级浅拷贝为预期行为
//   （582-586 差异登记：无公共无参构造 → 引用赋值，断言 ReferenceEquals 固化该语义）。
// 全链走 Mapper.DeepCopy（生成腿经 ModuleInitializer 注册 GeneratedDeepCopyCache 命中）。
using NCMapper;
using NCMapper.SourceGen.Tests.Models;
using Xunit;

namespace NCMapper.SourceGen.Tests
{
    [Collection("DeepCopyGlobalState")]
    public class DeepCopyT5LegTests
    {
        // ================================================================
        // 动作 1：record + init-only + 构参外嵌套集合（273-290 四分流）
        // ================================================================

        [Fact]
        public void Gen_Record_InitOnly_NestedCollections_DeepCopied()
        {
            var leaf = new Dc5Leaf { N = 7 };
            var src = new Dc5Rec(1, "rec")
            {
                Tags = [new Dc5Leaf { N = 1 }, new Dc5Leaf { N = 2 }],
                Map = new System.Collections.Generic.Dictionary<string, Dc5Leaf> { ["k"] = leaf },
                Items = [new Dc5Leaf { N = 3 }],
                Leaf = leaf
            };

            var copy = Mapper.DeepCopy(src)!;

            // 断言本体独立（record 构造路径生成新实例）
            Assert.False(ReferenceEquals(src, copy), "Dc5Rec 拷贝应为新实例");
            Assert.Equal(1, copy.Id);
            Assert.Equal("rec", copy.Name);
            // 构参外数组支（273-275）：容器独立＋元素独立＋值相等
            Assert.False(ReferenceEquals(src.Tags, copy.Tags), "Tags 数组应深拷贝");
            Assert.Equal(2, copy.Tags.Length);
            Assert.False(ReferenceEquals(src.Tags[0], copy.Tags[0]), "数组元素应深拷贝");
            Assert.Equal(1, copy.Tags[0].N);
            // 字典支（277-280）
            Assert.False(ReferenceEquals(src.Map, copy.Map), "Map 应深拷贝");
            Assert.False(ReferenceEquals(src.Map["k"], copy.Map["k"]), "字典值应深拷贝");
            Assert.Equal(7, copy.Map["k"].N);
            // 集合支（282-284）
            Assert.False(ReferenceEquals(src.Items, copy.Items), "Items 应深拷贝");
            Assert.Equal(3, copy.Items[0].N);
            // 引用递归支（289-290）——同源 leaf 在两支中各自独立拷贝
            Assert.False(ReferenceEquals(src.Leaf, copy.Leaf), "Leaf 应深拷贝");
            Assert.Equal(7, copy.Leaf!.N);
        }

        // ================================================================
        // 动作 2：生成腿容器清单（元素一律 Dc5Leaf——非简单元素支锁定）
        // ================================================================

        [Fact]
        public void Gen_Stack_ElementsDeepCopied_LifoOrder()
        {
            // Stack 支（446-467——ToArray+Reverse+Push 保 LIFO 顺序）
            var src = new Dc5Host { Id = 1 };
            src.Stack.Push(new Dc5Leaf { N = 1 });
            src.Stack.Push(new Dc5Leaf { N = 2 });

            var copy = Mapper.DeepCopy(src)!;

            Assert.False(ReferenceEquals(src.Stack, copy.Stack), "Stack 应深拷贝");
            Assert.Equal(2, copy.Stack.Count);
            // LIFO 顺序保持（ToArray+Reverse+Push 序列语义）
            Assert.Equal(2, copy.Stack.Pop().N);
            Assert.Equal(1, copy.Stack.Pop().N);
        }

        [Fact]
        public void Gen_SortedSet_ElementsDeepCopied()
        {
            // SortedSet 支（470-489——Add，元素 IComparable 排序）
            var src = new Dc5Host { Id = 1 };
            src.Set.Add(new Dc5Leaf { N = 5 });
            src.Set.Add(new Dc5Leaf { N = 3 });

            var copy = Mapper.DeepCopy(src)!;

            Assert.False(ReferenceEquals(src.Set, copy.Set), "SortedSet 应深拷贝");
            Assert.Equal(2, copy.Set.Count);
            Assert.Equal(3, copy.Set.Min!.N);
            // 元素独立（引用判等——SortedSet.Contains 走 IComparable 比较器，N 相同即"包含"，不能用于引用断言）
            var refSet = new System.Collections.Generic.HashSet<Dc5Leaf>(src.Set, System.Collections.Generic.ReferenceEqualityComparer.Instance);
            Assert.All(copy.Set, e => Assert.False(refSet.Contains(e), $"SortedSet 元素应独立拷贝（N={e.N}）"));
            Assert.Equal(new[] { 3, 5 }, copy.Set.Select(x => x.N).ToArray());
        }

        [Fact]
        public void Gen_Queue_ElementsDeepCopied_FifoOrder()
        {
            // Queue 支（414-437——Enqueue）
            var src = new Dc5Host { Id = 1 };
            src.Queue.Enqueue(new Dc5Leaf { N = 1 });
            src.Queue.Enqueue(new Dc5Leaf { N = 2 });

            var copy = Mapper.DeepCopy(src)!;

            Assert.False(ReferenceEquals(src.Queue, copy.Queue), "Queue 应深拷贝");
            Assert.Equal(2, copy.Queue.Count);
            // FIFO 顺序保持
            Assert.Equal(1, copy.Queue.Dequeue().N);
            Assert.Equal(2, copy.Queue.Dequeue().N);
        }

        [Fact]
        public void Gen_DictSubclass_NoParameterlessCtor_DegradesToShallow()
        {
            // 字典子类无公共无参构造（582-586 差异登记）——降级浅拷贝引用赋值为预期行为，
            // 断言 ReferenceEquals 固化该语义（防回归：静默变深拷贝或抛错均应可见）
            var src = new Dc5DictHost { Id = 1 };
            src.Dict["k"] = new Dc5Leaf { N = 9 };

            var copy = Mapper.DeepCopy(src)!;

            Assert.Equal(1, copy.Id);
            // 降级浅拷贝：引用共享（预期行为固化）
            Assert.True(ReferenceEquals(src.Dict, copy.Dict),
                "Dc5Dict 无公共无参构造应降级浅拷贝引用赋值（CodeGenerator.DeepCopy.cs:582-586 差异登记语义）");
            Assert.Equal(9, copy.Dict["k"].N);
        }

        [Fact]
        public void Gen_RegularContainers_RegressionGroup()
        {
            // 常规容器回归组：数组/List/HashSet（492+ 通用支，发射分支在册——回归非新覆盖）
            var src = new Dc5Host { Id = 1 };
            src.Arr = [new Dc5Leaf { N = 4 }];
            src.List = [new Dc5Leaf { N = 5 }];
            src.Hash = [new Dc5Leaf { N = 6 }];

            var copy = Mapper.DeepCopy(src)!;

            Assert.False(ReferenceEquals(src.Arr, copy.Arr), "数组应深拷贝");
            Assert.False(ReferenceEquals(src.Arr[0], copy.Arr[0]), "数组元素应深拷贝");
            Assert.Equal(4, copy.Arr[0].N);
            Assert.False(ReferenceEquals(src.List, copy.List), "List 应深拷贝");
            Assert.False(ReferenceEquals(src.List[0], copy.List[0]), "List 元素应深拷贝");
            Assert.Equal(5, copy.List[0].N);
            Assert.False(ReferenceEquals(src.Hash, copy.Hash), "HashSet 应深拷贝");
            Assert.False(ReferenceEquals(src.Hash.First(), copy.Hash.First()), "HashSet 元素应深拷贝");
            Assert.Equal(6, copy.Hash.First().N);
        }

        // ================================================================
        // 执行期补例（203-209 回退支对应用例——9 例清单外，台账登记）
        // ================================================================

        [Fact]
        public void Gen_NoPublicCtor_FallsBackToRuntimeDeepCopy()
        {
            // 无公共实例构造器（Dc5NoCtor 私有无参构造）→ 构造路径 ctor==null →
            // :203-209 回退 DeepCopyRuntimeOnly（避免 GeneratedDeepCopyCache 重入）→ 运行时行为
            var src = Dc5NoCtor.Create(42);

            var copy = Mapper.DeepCopy(src)!;

            Assert.False(ReferenceEquals(src, copy), "Dc5NoCtor 拷贝应为新实例（运行时回退）");
            Assert.Equal(42, copy.Id);
            Assert.True(Mapper.HasGeneratedDeepCopy<Dc5NoCtor>(),
                "Dc5NoCtor 应注册生成 DeepCopy 委托（委托体内为回退运行时形态）");
        }

        // ================================================================
        // 动作 4：record 路径循环引用——Cyclic 回退运行时（136-150），环被打破
        // ================================================================

        [Fact]
        public void Gen_RecordCyclicReference_FallsBackToRuntime_NoStackOverflow()
        {
            // record（位置参数 → 无公共无参构造 → record 构造路径）＋自环 → 图 Cyclic →
            // :136-150 回退 DeepCopyRuntimeOnly（运行时 ConditionalWeakTable 打破环）
            var node = new Dc5CycRec(3);
            node.Self = node;

            var copy = Mapper.DeepCopy(node)!;

            Assert.Equal(3, copy.Id);
            Assert.NotNull(copy.Self);
            Assert.Same(copy, copy.Self);
            Assert.NotSame(node, copy.Self);
        }
    }
}

namespace NCMapper.SourceGen.Tests
{
    using NCMapper.SourceGen.Tests.Helpers;

    // ================================================================
    // 生成器动态编译形态（收口轮补——覆盖率采集根因修正）：
    // 上方 8 例走 Mapper.DeepCopy 运行时全链（生成委托已注册，生成器代码不执行）；
    // CodeGenerator.DeepCopy.cs 的承诺行在"生成器发射段"，须以 TestHelper.GenerateMapper
    // 动态编译含目标模型的源码触发生成器执行，断言生成产物形态锁定行覆盖。
    // ================================================================
    public class DeepCopyT5EmissionTests
    {
        /// <summary>回退支（203-209）：无公共构造器类型 → 生成 DeepCopyRuntimeOnly 回退形态。</summary>
        [Fact]
        public void Emission_NoPublicCtor_FallbackRuntimeEmitted()
        {
            var source = """
                using NCMapper;
                public class Ec1Entity { public int Id { get; set; } }
                [NCMapper.Attributes.MapFrom(typeof(Ec1Entity))]
                public class Ec1NoCtor
                {
                    private Ec1NoCtor() { }
                    public int Id { get; set; }
                }
                """;
            var result = TestHelper.GenerateMapper(source);
            // 回退支 :203-209 产物形态：DeepCopyRuntimeOnly 回退＋pragma 包裹
            Assert.Contains("DeepCopyRuntimeOnly<Ec1NoCtor>", result.GeneratedCode);
            Assert.Contains("#pragma warning disable IL2026, IL3050", result.GeneratedCode);
        }

        /// <summary>四分流（273-290）：record 构参外可写属性 数组/字典/集合/引用递归 四支产物形态。</summary>
        [Fact]
        public void Emission_RecordExternalProps_FourBranchesEmitted()
        {
            var source = """
                using NCMapper;
                public class Ec2Leaf { public int N { get; set; } }
                public class Ec2Entity { public int Id { get; set; } public string Name { get; set; } = ""; }
                [NCMapper.Attributes.MapFrom(typeof(Ec2Entity))]
                public record Ec2Rec(int Id, string Name)
                {
                    public Ec2Leaf[] Tags { get; set; } = System.Array.Empty<Ec2Leaf>();
                    public System.Collections.Generic.Dictionary<string, Ec2Leaf> Map { get; set; } = new();
                    public System.Collections.Generic.List<Ec2Leaf> Items { get; set; } = new();
                    public Ec2Leaf? Leaf { get; set; }
                }
                """;
            var result = TestHelper.GenerateMapper(source);
            // 数组支（273-275）/ 字典支（277-280）/ 集合支（282-284）/ 引用递归支（289-290）
            Assert.Contains("dst.Tags = ", result.GeneratedCode);
            Assert.Contains("dst.Map = ", result.GeneratedCode);
            Assert.Contains("dst.Items = ", result.GeneratedCode);
            Assert.Contains("if (src.Leaf is not null)", result.GeneratedCode);
            Assert.Contains("dst.Leaf = NCMapper.Mapper.DeepCopy(src.Leaf, ctx)!;", result.GeneratedCode);
        }

        /// <summary>字典子类降级（582-586）：无公共无参构造 → 降级浅拷贝引用赋值＋差异登记注释。</summary>
        [Fact]
        public void Emission_DictSubclass_NoParameterlessCtor_ShallowRefEmitted()
        {
            var source = """
                using NCMapper;
                public class Ec3Leaf { public int N { get; set; } }
                public class Ec3Dict : System.Collections.Generic.Dictionary<string, Ec3Leaf>
                {
                    public Ec3Dict(int capacity) : base(capacity) { }
                }
                public class Ec3Entity { public int Id { get; set; } }
                [NCMapper.Attributes.MapFrom(typeof(Ec3Entity))]
                public class Ec3Host
                {
                    public int Id { get; set; }
                    public Ec3Dict Dict { get; set; } = new(4);
                }
                """;
            var result = TestHelper.GenerateMapper(source);
            // :582-584 产物形态：差异登记注释＋引用赋值
            Assert.Contains("降级为浅拷贝引用赋值", result.GeneratedCode);
            Assert.Contains("dst.Dict = src.Dict;", result.GeneratedCode);
        }
    }
}
