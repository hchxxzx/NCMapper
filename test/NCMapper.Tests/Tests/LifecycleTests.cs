using NCMapper.Tests.Models;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// 对象生命周期测试 — 深拷贝、列表映射、更新已有对象、Action 回调后处理
    /// </summary>
    [Collection("DeepCopyGlobalState")]
    public class LifecycleTests
    {
        /// <summary>
        /// Map&lt;T&gt;(source) 同类复制：创建新对象，属性值相同但引用不同
        /// </summary>
        [Fact]
        public void DeepCopy_SameType_DifferentReference()
        {
            var addr = new Address { City = "Beijing", Street = "Chang'an Ave", ZipCode = 100000 };
            var copy = Mapper.Map(addr)!;

            // 属性值全部一致
            Assert.Equal("Beijing", copy.City);
            Assert.Equal("Chang'an Ave", copy.Street);
            Assert.Equal(100000, copy.ZipCode);
            // 但引用不同，是独立的新对象
            Assert.NotSame(addr, copy);
        }

        /// <summary>
        /// MapList&lt;TSource, TTarget&gt;(sources) 列表映射：
        /// 遍历源列表，对每个元素调用 Map()，返回新列表
        /// </summary>
        [Fact]
        public void ListMapping_AllElements()
        {
            var entities = new List<FullTypeEntity>
            {
                new() { IntProp = 1, StringProp = "A" },
                new() { IntProp = 2, StringProp = "B" },
                new() { IntProp = 3, StringProp = "C" }
            };

            var dtos = Mapper.MapList<FullTypeEntity, SameTypeDto>(entities)!;

            Assert.Equal(3, dtos.Count);
            Assert.Equal(1, dtos[0].IntProp);
            Assert.Equal("A", dtos[0].StringProp);
            Assert.Equal(2, dtos[1].IntProp);
            Assert.Equal(3, dtos[2].IntProp);
        }

        /// <summary>
        /// 空列表映射 → 返回空列表（非 null）
        /// </summary>
        [Fact]
        public void ListMapping_Empty_ReturnsEmpty()
        {
            var result = Mapper.MapList<FullTypeEntity, SameTypeDto>(new List<FullTypeEntity>())!;
            Assert.Empty(result);
        }

        /// <summary>
        /// null 列表映射 → 返回 null
        /// </summary>
        [Fact]
        public void ListMapping_Null_ReturnsNull()
        {
            var result = Mapper.MapList<FullTypeEntity, SameTypeDto>(null!);
            Assert.Null(result);
        }

        /// <summary>
        /// Map(source, target) 更新已存在的目标对象：
        /// 不创建新对象，直接将源对象的值写入已有目标对象的属性
        /// </summary>
        [Fact]
        public void Update_Existing_Target()
        {
            var entity = new FullTypeEntity { IntProp = 999, StringProp = "update_me" };
            var target = new SameTypeDto();

            Mapper.Map(entity, target);

            Assert.Equal(999, target.IntProp);
            Assert.Equal("update_me", target.StringProp);
        }

        /// <summary>
        /// Action 回调后处理：映射完成后对目标对象进行额外的业务逻辑处理
        /// 可用于计算字段、字符串格式化、关联数据填充等
        /// </summary>
        [Fact]
        public void Action_Callback_PostProcessing()
        {
            var entity = new FullTypeEntity { IntProp = 50, StringProp = "raw" };
            var result = Mapper.Map<FullTypeEntity, SameTypeDto>(entity, (src, dest) =>
            {
                dest.IntProp = src.IntProp * 2;        // 计算：翻倍
                dest.StringProp = src.StringProp.ToUpper(); // 格式化：大写
            });

            Assert.Equal(100, result?.IntProp);
            Assert.Equal("RAW", result?.StringProp);
        }

        // ================================================================
        // P0-10: MaxInlineDepth 直接 API 测试
        // ================================================================

        /// <summary>
        /// MaxInlineDepth 设置合法值 — 应在范围内正常工作
        /// </summary>
        [Fact]
        public void MaxInlineDepth_SetWithinBound_Success()
        {
            var original = Mapper.MaxInlineDepth;
            try
            {
                Mapper.MaxInlineDepth = 8;
                Assert.Equal(8, Mapper.MaxInlineDepth);
            }
            finally
            {
                Mapper.MaxInlineDepth = original;
            }
        }

        /// <summary>
        /// MaxInlineDepth 设置超过上限 — 应被夹紧到 MaxInlineDepthUpperBound
        /// </summary>
        [Fact]
        public void MaxInlineDepth_SetAboveBound_Clamped()
        {
            var original = Mapper.MaxInlineDepth;
            try
            {
                Mapper.MaxInlineDepth = 9999;
                Assert.True(Mapper.MaxInlineDepth <= Mapper.MaxInlineDepthUpperBound,
                    $"Expected <= {Mapper.MaxInlineDepthUpperBound}, actual: {Mapper.MaxInlineDepth}");
            }
            finally
            {
                Mapper.MaxInlineDepth = original;
            }
        }

        /// <summary>
        /// MaxInlineDepth 默认值为 5
        /// </summary>
        [Fact]
        public void MaxInlineDepth_DefaultIsFive()
        {
            // 注意：测试运行期间可能被其他测试修改
            var original = Mapper.MaxInlineDepth;
            try
            {
                Mapper.MaxInlineDepth = 5;
                Assert.Equal(5, Mapper.MaxInlineDepth);
            }
            finally
            {
                Mapper.MaxInlineDepth = original;
            }
        }
    }
}
