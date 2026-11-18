using NCMapper;
using Xunit;

namespace NCMapper.Tests.Tests.Strategies
{
    /// <summary>
    /// MemberInit 双渲染器等价测（W2-1 ④，B-6）。
    /// <para>MemberInit 快速路径（MapperBase.TryBuildSingleBinding——本期不动的渲染器）与
    /// Block 引擎策略产出（12 策略类经主循环分发）在重叠 Kind（Direct/Convert）上
    /// 映射结果等价——锁定 MemberInit 渲染器行为不漂移。</para>
    /// <para>设计：A 型（全简单属性）走 MemberInit 快速路径；B 型（含复杂属性使 MemberInit
    /// 回退）走 Block 策略路径；断言两者共享的 Direct/Convert 属性映射值一致。</para>
    /// </summary>
    public class MemberInitEquivalenceTests
    {
        /// <summary>A 型：全简单属性（Direct + Convert 重叠 Kind）——MemberInit 快速路径</summary>
        public class SimpleAllSrc { public int Id { get; set; } public string Name { get; set; } = ""; public int Count { get; set; } }
        /// <summary>A 型目标</summary>
        public class SimpleAllDto { public int Id { get; set; } public string Name { get; set; } = ""; public long Count { get; set; } }

        /// <summary>B 型：同 A 型共享属性 + 复杂属性（迫使 MemberInit 回退 Block 策略路径）</summary>
        public class WithComplexSrc
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public int Count { get; set; }
            public SimpleAllSrc Child { get; set; } = new();
        }
        /// <summary>B 型目标</summary>
        public class WithComplexDto
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public long Count { get; set; }
            public SimpleAllDto Child { get; set; } = new();
        }

        /// <summary>
        /// 重叠 Kind（Direct：Id/Name；Convert：int→long Count）上双渲染器映射结果等价。
        /// </summary>
        [Fact]
        public void MemberInit_And_BlockStrategy_ProduceEquivalentResults()
        {
            var src = new SimpleAllSrc { Id = 7, Name = "b6", Count = 42 };
            var srcB = new WithComplexSrc { Id = 7, Name = "b6", Count = 42, Child = src };

            // A 型：MemberInit 快速路径（全简单赋值）
            var dtoA = Mapper.Map<SimpleAllSrc, SimpleAllDto>(src);
            // B 型：含嵌套复杂属性 → MemberInit 不可用 → Block 引擎策略分发路径
            var dtoB = Mapper.Map<WithComplexSrc, WithComplexDto>(srcB);

            Assert.NotNull(dtoA);
            Assert.NotNull(dtoB);
            // Direct Kind（等类型直接赋值）
            Assert.Equal(dtoA!.Id, dtoB!.Id);
            Assert.Equal(dtoA.Name, dtoB.Name);
            // Convert Kind（int→long 隐式转换）
            Assert.Equal(dtoA.Count, dtoB.Count);
            // 值正确性锚点（双路径与期望一致，而非仅互相一致）
            Assert.Equal(7, dtoB.Id);
            Assert.Equal("b6", dtoB.Name);
            Assert.Equal(42L, dtoB.Count);
            Assert.Equal(42, dtoB.Child.Count);
        }
    }
}
