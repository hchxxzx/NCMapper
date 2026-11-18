using NCMapper.Tests.Models;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// 同名属性（new/override）测试 — 验证子类属性覆盖父类属性，只映射最外层
    /// </summary>
    public class ShadowPropertyTests
    {
        /// <summary>
        /// new 隐藏 — 子类 int Id 覆盖基类 string Id，映射子类的值
        /// </summary>
        [Fact]
        public void Shadow_New_Property_Maps_Child_Not_Base()
        {
            var entity = new ShadowChildEntity { Id = 99, Code = "X", Name = "Test" };
            var dto = Mapper.Map<ShadowChildEntity, ShadowChildDto>(entity)!;

            Assert.Equal(99, dto.Id);         // 子类 int Id，不是基类 string Id
            Assert.Equal("X", dto.Code);      // 子类 Code
            Assert.Equal("Test", dto.Name);
        }

        /// <summary>
        /// new 隐藏 — 默认值正确映射（子类 Id=42 而非基类 null）
        /// </summary>
        [Fact]
        public void Shadow_New_Property_Default_Value_Correct()
        {
            var entity = new ShadowChildEntity();  // Id=42, Code="CHILD"
            var dto = Mapper.Map<ShadowChildEntity, ShadowChildDto>(entity)!;

            Assert.Equal(42, dto.Id);
            Assert.Equal("CHILD", dto.Code);
        }

        /// <summary>
        /// new 隐藏 — 反向映射也正确（DTO→Entity 映射到子类属性）
        /// </summary>
        [Fact]
        public void Shadow_New_Property_Reverse_Maps_Correct()
        {
            var dto = new ShadowChildDto { Id = 777, Code = "REV", Name = "Reverse" };
            var entity = Mapper.Map<ShadowChildDto, ShadowChildEntity>(dto)!;

            Assert.Equal(777, entity.Id);
            Assert.Equal("REV", entity.Code);
            Assert.Equal("Reverse", entity.Name);
        }

        /// <summary>
        /// override — 映射 override 后的属性值
        /// </summary>
        [Fact]
        public void Override_Property_Maps_Overridden_Value()
        {
            var entity = new OverrideChildEntity { Amount = 2.0, Label = "L" };
            // override Amount: get => base.Amount * 10 → 2.0 * 10 = 20.0
            var dto = Mapper.Map<OverrideChildEntity, OverrideChildDto>(entity)!;

            Assert.Equal(20.0, dto.Amount);
            Assert.Equal("L", dto.Label);
        }

        /// <summary>
        /// new 隐藏 + 不同类型 — 子类 int Code 覆盖基类 string Code 不崩溃
        /// （此场景 Code 类型相同，Id 类型不同但子类优先）
        /// </summary>
        [Fact]
        public void Shadow_Different_Type_No_Exception()
        {
            var entity = new ShadowChildEntity { Id = 100, Code = "OK", Name = "Diff" };

            // 不应抛 InvalidCastException（不会尝试将 string Id 赋给 int Id）
            var dto = Mapper.Map<ShadowChildEntity, ShadowChildDto>(entity)!;

            Assert.Equal(100, dto.Id);
            Assert.Equal("OK", dto.Code);
        }
    }
}
