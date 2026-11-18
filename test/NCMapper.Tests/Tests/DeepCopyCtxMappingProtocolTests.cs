using System.Collections.Generic;
using NCMapper;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// T-P2 — 映射路径 ctx 贯通测试
    /// <para>覆盖：① 同一映射内跨集合边界环检测恢复（A→Dictionary&lt;string,B&gt;→B.Parent→A 正确打破）；</para>
    /// <para>② 同一映射内同源同拷贝（两个集合属性共享同一源对象时返回同一拷贝实例）；</para>
    /// <para>③ 生命周期协议嵌套交互（DeepCopy 顶层内嵌套 Map 不误清环表）。</para>
    /// <para>模型均无 [MapFrom]，走 MapperBase 运行时回退路径（ExpressionBuilder 表达式）。</para>
    /// </summary>
    [Collection("DeepCopyGlobalState")]
    public class DeepCopyCtxMappingProtocolTests
    {
        // ================================================================
        // 测试模型（无 [MapFrom]，回退路径）
        // ================================================================

        /// <summary>
        /// 键简单（string）值复杂（Node）的字典容器 — 映射时走 ConvertListExpression → CopyDictionary（元素深拷贝 + ctx）
        /// </summary>
        public class Container
        {
            public int Id { get; set; }
            public Dictionary<string, Node>? Map1 { get; set; }
            public Dictionary<string, Node>? Map2 { get; set; }
        }

        public class Node
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public Container? Parent { get; set; }
        }

        public class ContainerDto
        {
            public int Id { get; set; }
            public Dictionary<string, Node>? Map1 { get; set; }
            public Dictionary<string, Node>? Map2 { get; set; }
        }

        public class NodeDto
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public ContainerDto? Parent { get; set; }
        }

        // ================================================================
        // ① 跨集合边界环检测恢复（Core 发现 6 语义）
        // ================================================================

        /// <summary>
        /// 同一映射内 A→Dictionary&lt;string,B&gt;→B.Parent→A 环：ctx 贯通后正确打破（不抛 MapException）
        /// <para>改造前：集合拷贝每次 new ctx → 环检测失效 → 递归深度防护抛 MapException；</para>
        /// <para>改造后：共享 ThreadStatic ctx → CycleCache 命中返回已拷贝对象 → 正常完成。</para>
        /// </summary>
        [Fact]
        public void Map_CrossCollectionCycle_Resolved_NotStackOverflow()
        {
            var a = new Container { Id = 1 };
            var b = new Node { Id = 2, Name = "b", Parent = a };
            a.Map1 = new Dictionary<string, Node> { ["b"] = b };

            var dto = Mapper.Map<Container, ContainerDto>(a);

            Assert.NotNull(dto);
            Assert.NotNull(dto!.Map1);
            Assert.NotNull(dto.Map1!["b"]);
            Assert.NotSame(b, dto.Map1!["b"]);
            Assert.Equal("b", dto.Map1!["b"].Name);
            // 环正确打破：B.Parent 映射回 A 的拷贝，且其 Map1["b"] 与顶层 B 拷贝为同一实例
            Assert.NotNull(dto.Map1!["b"].Parent);
            Assert.Same(dto.Map1!["b"], dto.Map1!["b"].Parent!.Map1!["b"]);
        }

        // ================================================================
        // ② 同源同拷贝（行为登记 -2）
        // ================================================================

        /// <summary>
        /// 同一映射内两个字典属性共享同一源 Node：贯通后两处拷贝为同一实例
        /// <para>改造前（每次 new ctx）：Map2 重新深拷贝 → 两个独立拷贝；</para>
        /// <para>改造后（共享 ctx）：Map2 命中 Map1 已登记拷贝 → 同一实例（单次映射"源→拷贝"契约一致）。</para>
        /// </summary>
        [Fact]
        public void Map_SameSourceAcrossCollections_ReturnsSameCopy()
        {
            var a = new Container { Id = 1 };
            var b = new Node { Id = 2, Name = "shared" };
            a.Map1 = new Dictionary<string, Node> { ["b"] = b };
            a.Map2 = new Dictionary<string, Node> { ["b"] = b };

            var dto = Mapper.Map<Container, ContainerDto>(a);

            Assert.NotNull(dto);
            Assert.NotNull(dto!.Map1);
            Assert.NotNull(dto.Map2);
            Assert.Same(dto.Map1!["b"], dto.Map2!["b"]);
            Assert.NotSame(b, dto.Map1!["b"]);
        }

        // ================================================================
        // ③ 生命周期协议嵌套交互（四项裁决之场景 2）
        // ================================================================

        /// <summary>
        /// DeepCopy 顶层执行中嵌套 Map 顶层调用：内层 Map 退出（Depth 未归 0）不得 Clear 外层环表，
        /// 外层 DeepCopy 环检测保持有效（重入后 CycleCache 仍能正确打破外层环）
        /// </summary>
        [Fact]
        public void DeepCopy_NestedMap_DoesNotClearOuterCycleCache()
        {
            var a = new Container { Id = 1 };
            var b = new Node { Id = 2, Name = "b", Parent = a };
            a.Map1 = new Dictionary<string, Node> { ["b"] = b };

            // 外层 DeepCopy（环图：a.Map1["b"].Parent == a）→ 环检测依赖外层 CycleCache
            var copy = Mapper.DeepCopy(a, (src, tgt) =>
            {
                // 嵌套 Map（同线程、同 ctx）：内层 Map 退出时 Depth>0，不得 Clear
                var mapped = Mapper.Map<Container, ContainerDto>(a);
                Assert.NotNull(mapped);
                Assert.NotNull(mapped!.Map1);
            });

            Assert.NotNull(copy);
            Assert.NotNull(copy!.Map1);
            Assert.NotNull(copy.Map1!["b"]);
            Assert.Same(copy, copy.Map1!["b"].Parent); // 外层环被正确打破（环表未被内层清掉）
        }
    }
}
