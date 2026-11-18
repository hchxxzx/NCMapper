using NCMapper.Tests.Models;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// 双向转换测试 — 验证 Mapper 支持 Entity → DTO 和 DTO → Entity 两个方向
    /// </summary>
    [Collection("DeepCopyGlobalState")]
    public class BidirectionalTests
    {
        // ================================================================
        // 同名字段双向映射：Entity → DTO 和 DTO → Entity
        // ================================================================

        /// <summary>
        /// Entity → DTO：正向映射（基准验证）
        /// </summary>
        [Fact]
        public void Entity_To_Dto_Forward()
        {
            var entity = new FullTypeEntity
            {
                IntProp = 42,
                LongProp = 999,
                StringProp = "正向测试",
                BoolProp = true,
                DecimalProp = 99.99m
            };

            var dto = Mapper.Map<FullTypeEntity, SameTypeDto>(entity)!;

            Assert.Equal(42, dto.IntProp);
            Assert.Equal(999L, dto.LongProp);
            Assert.Equal("正向测试", dto.StringProp);
            Assert.True(dto.BoolProp);
            Assert.Equal(99.99m, dto.DecimalProp);
        }

        /// <summary>
        /// DTO → Entity：反向映射 — DTO 作为源，Entity 作为目标
        /// SameTypeDto 有 [MapFrom(typeof(FullTypeEntity))]，Mapper 识别后自动过滤 DTO 中匹配 Entity 的字段
        /// </summary>
        [Fact]
        public void Dto_To_Entity_Reverse()
        {
            var dto = new SameTypeDto
            {
                IntProp = 88,
                LongProp = 777L,
                StringProp = "反向测试",
                BoolProp = false,
                DecimalProp = 55.5m,
                DateTimeProp = new DateTime(2025, 5, 20),
                GuidProp = Guid.Parse("AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE")
            };

            var entity = Mapper.Map<SameTypeDto, FullTypeEntity>(dto)!;

            Assert.Equal(88, entity.IntProp);
            Assert.Equal(777L, entity.LongProp);
            Assert.Equal("反向测试", entity.StringProp);
            Assert.False(entity.BoolProp);
            Assert.Equal(55.5m, entity.DecimalProp);
            Assert.Equal(2025, entity.DateTimeProp.Year);
            Assert.Equal(Guid.Parse("AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE"), entity.GuidProp);
        }

        /// <summary>
        /// 循环一致性：Entity → DTO → Entity（两次映射后应与原始 Entity 一致）
        /// 验证双向映射的可逆性
        /// </summary>
        [Fact]
        public void RoundTrip_Entity_Dto_Entity_Consistent()
        {
            var original = new FullTypeEntity
            {
                IntProp = 123,
                LongProp = 456L,
                StringProp = "循环",
                BoolProp = true,
                DecimalProp = 78.9m,
                DateTimeProp = new DateTime(2024, 12, 25),
                GuidProp = Guid.NewGuid()
            };

            // Entity → DTO
            var dto = Mapper.Map<FullTypeEntity, SameTypeDto>(original)!;

            // DTO → Entity（回来的 entity）
            var roundTrip = Mapper.Map<SameTypeDto, FullTypeEntity>(dto)!;

            Assert.Equal(original.IntProp, roundTrip.IntProp);
            Assert.Equal(original.LongProp, roundTrip.LongProp);
            Assert.Equal(original.StringProp, roundTrip.StringProp);
            Assert.Equal(original.BoolProp, roundTrip.BoolProp);
            Assert.Equal(original.DecimalProp, roundTrip.DecimalProp);
            Assert.Equal(original.DateTimeProp, roundTrip.DateTimeProp);
            Assert.Equal(original.GuidProp, roundTrip.GuidProp);
        }

        // ================================================================
        // [MapProperty] 双向映射
        // ================================================================

        /// <summary>
        /// Entity → DTO：[MapProperty] 跨字段名映射
        /// Entity.StringProp → DTO.FullName（正向）
        /// </summary>
        [Fact]
        public void FieldMapping_Forward()
        {
            var entity = new FullTypeEntity { StringProp = "WangWu" };
            var dto = Mapper.Map<FullTypeEntity, FieldMappingDto>(entity)!;

            Assert.Equal("WangWu", dto.FullName);
        }

        /// <summary>
        /// DTO → Entity：[MapProperty] 跨字段名反向映射
        /// DTO.FullName → Entity.StringProp（反向）
        /// FieldMappingDto 的 [MapProperty] 标注了 FromSource=FullTypeEntity 和 FromField=StringProp，
        /// 反向时 Mapper 识别到这个关系并正确映射
        /// </summary>
        [Fact]
        public void FieldMapping_Reverse()
        {
            var dto = new FieldMappingDto { FullName = "ZhaoLiu", Age = 30 };
            var entity = Mapper.Map<FieldMappingDto, FullTypeEntity>(dto)!;

            Assert.Equal("ZhaoLiu", entity.StringProp); // FullName → StringProp
            Assert.Equal(30, entity.IntProp);             // Age → IntProp
        }

        // ================================================================
        // 更新已有对象 — 双向
        // ================================================================

        /// <summary>
        /// 正向更新：用 Entity 更新已有 DTO
        /// </summary>
        [Fact]
        public void Update_Existing_Dto_From_Entity()
        {
            var entity = new FullTypeEntity { IntProp = 999, StringProp = "new_value" };
            var target = new SameTypeDto { IntProp = 0, StringProp = "old_value" };

            Mapper.Map(entity, target);

            Assert.Equal(999, target.IntProp);
            Assert.Equal("new_value", target.StringProp);
        }

        /// <summary>
        /// 反向更新：用 DTO 更新已有 Entity
        /// </summary>
        [Fact]
        public void Update_Existing_Entity_From_Dto()
        {
            var dto = new SameTypeDto { IntProp = 555, StringProp = "from_dto" };
            var target = new FullTypeEntity { IntProp = 0, StringProp = "original" };

            Mapper.Map(dto, target);

            Assert.Equal(555, target.IntProp);
            Assert.Equal("from_dto", target.StringProp);
        }

        // ================================================================
        // 双向列表映射
        // ================================================================

        /// <summary>
        /// Entity 列表 → DTO 列表（正向）
        /// </summary>
        [Fact]
        public void ListMapping_Forward()
        {
            var entities = new List<FullTypeEntity>
            {
                new() { IntProp = 1, StringProp = "甲" },
                new() { IntProp = 2, StringProp = "乙" }
            };

            var dtos = Mapper.MapList<FullTypeEntity, SameTypeDto>(entities)!;

            Assert.Equal(2, dtos.Count);
            Assert.Equal(1, dtos[0].IntProp);
            Assert.Equal("乙", dtos[1].StringProp);
        }

        /// <summary>
        /// DTO 列表 → Entity 列表（反向）
        /// </summary>
        [Fact]
        public void ListMapping_Reverse()
        {
            var dtos = new List<SameTypeDto>
            {
                new() { IntProp = 10, StringProp = "A" },
                new() { IntProp = 20, StringProp = "B" },
                new() { IntProp = 30, StringProp = "C" }
            };

            var entities = Mapper.MapList<SameTypeDto, FullTypeEntity>(dtos)!;

            Assert.Equal(3, entities.Count);
            Assert.Equal(10, entities[0].IntProp);
            Assert.Equal("A", entities[0].StringProp);
            Assert.Equal(20, entities[1].IntProp);
            Assert.Equal("C", entities[2].StringProp);
        }

        // ================================================================
        // 跨类型转换 — 双向
        // ================================================================

        /// <summary>
        /// DTO → Entity：DTO 的字符串字段反向转回 Entity 的值类型字段
        /// ValueToStringDto 的 string 字段（如 "42"）→ FullTypeEntity 的 int 字段 → TypeConvertHelper 处理
        /// </summary>
        [Fact]
        public void Reverse_StringToValue()
        {
            var dto = new ValueToStringDto
            {
                IntProp = "42",
                LongProp = "9999999999",
                DecimalProp = "99.99",
                FloatProp = "3.14",
                DoubleProp = "2.71828",
                BoolProp = "True",
                DateTimeProp = "2025-01-01",
                GuidProp = "11111111-2222-3333-4444-555555555555"
            };

            var entity = Mapper.Map<ValueToStringDto, FullTypeEntity>(dto)!;

            Assert.Equal(42, entity.IntProp);
            Assert.Equal(99.99m, entity.DecimalProp);
            Assert.True(entity.BoolProp);
        }

        /// <summary>
        /// 嵌套对象双向映射：Dto → Entity
        /// </summary>
        [Fact]
        public void Nested_Reverse()
        {
            var dto = new CustomerDto
            {
                Id = 42,
                Name = "Reverse Customer",
                HomeAddress = new AddressDto
                {
                    City = "Hangzhou",
                    Street = "West Lake Road",
                    ZipCode = 310000
                }
            };

            var entity = Mapper.Map<CustomerDto, CustomerEntity>(dto)!;

            Assert.Equal(42, entity.Id);
            Assert.Equal("Reverse Customer", entity.Name);
            Assert.NotNull(entity.HomeAddress);
            Assert.Equal("Hangzhou", entity.HomeAddress!.City);
            Assert.Equal("West Lake Road", entity.HomeAddress.Street);
            Assert.Equal(310000, entity.HomeAddress.ZipCode);
        }
    }
}
