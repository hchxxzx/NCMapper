using NCMapper.Tests.Models;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// 嵌套类映射测试 — 实体包含子对象时，递归调用 MapperBase.Map 映射子对象
    /// </summary>
    public class NestedObjectTests
    {
        /// <summary>
        /// CustomerEntity 包含 Address 子对象 → CustomerDto 包含 AddressDto 子对象
        /// MapperBase 检测到源/目标属性均为 class 且类型不同时，
        /// 自动调用 MapperBase&lt;Address, AddressDto&gt;.Map() 递归映射
        /// </summary>
        [Fact]
        public void Nested_Class_Recursively_Mapped()
        {
            var entity = new CustomerEntity
            {
                Id = 1,
                Name = "Alice",
                HomeAddress = new Address { City = "Shanghai", Street = "Nanjing Road", ZipCode = 200001 }
            };
            var dto = Mapper.Map<CustomerEntity, CustomerDto>(entity)!;

            // 顶层字段直接映射
            Assert.Equal(1, dto.Id);
            Assert.Equal("Alice", dto.Name);

            // 嵌套子对象自动递归映射
            Assert.NotNull(dto.HomeAddress);
            Assert.Equal("Shanghai", dto.HomeAddress!.City);
            Assert.Equal("Nanjing Road", dto.HomeAddress.Street);
            Assert.Equal(200001, dto.HomeAddress.ZipCode);
        }

        /// <summary>
        /// 子对象为 null 时，DTO 的对应属性保持 null（不会创建空对象）
        /// </summary>
        [Fact]
        public void Nested_Class_Null_SubObject()
        {
            var entity = new CustomerEntity { Id = 2, Name = "Bob", HomeAddress = null };
            var dto = Mapper.Map<CustomerEntity, CustomerDto>(entity)!;

            // 顶层字段正常映射
            Assert.Equal(2, dto.Id);
            // 源子对象为 null → 目标子对象也为 null
            Assert.Null(dto.HomeAddress);
        }
    }
}
