using NCMapper.Tests.Models;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// 枚举映射测试 — enum ↔ int / string
    /// </summary>
    public class EnumTests
    {
        /// <summary>
        /// 枚举值 → 整数：取枚举的底层数值（C# 默认从 0 开始）
        /// OrderStatus.Shipped=2 → 2，OrderStatus.Delivered=3 → 3
        /// </summary>
        [Fact]
        public void Enum_To_Int()
        {
            var shipped = new FullTypeEntity { StatusProp = OrderStatus.Shipped };
            var dto1 = Mapper.Map<FullTypeEntity, EnumToIntDto>(shipped)!;
            Assert.Equal(2, dto1.StatusProp);

            var delivered = new FullTypeEntity { StatusProp = OrderStatus.Delivered };
            var dto2 = Mapper.Map<FullTypeEntity, EnumToIntDto>(delivered)!;
            Assert.Equal(3, dto2.StatusProp);
        }

        /// <summary>
        /// 枚举值 → 字符串：取枚举成员的名称（如 OrderStatus.Cancelled → "Cancelled"）
        /// </summary>
        [Fact]
        public void Enum_To_String()
        {
            var cancelled = new FullTypeEntity { StatusProp = OrderStatus.Cancelled };
            var dto1 = Mapper.Map<FullTypeEntity, EnumToStringDto>(cancelled)!;
            Assert.Equal("Cancelled", dto1.StatusProp);

            var pending = new FullTypeEntity { StatusProp = OrderStatus.Pending };
            var dto2 = Mapper.Map<FullTypeEntity, EnumToStringDto>(pending)!;
            Assert.Equal("Pending", dto2.StatusProp);
        }

        // ================================================================
        // T10: [Flags] 组合枚举测试
        // ================================================================

        /// <summary>
        /// [Flags] 枚举 → int：Read|Write 组合 = 3（位运算数值）
        /// </summary>
        [Fact]
        public void FlagsEnum_To_Int()
        {
            var entity = new FlagsEnumEntity { Access = Permissions.Read | Permissions.Write };
            var dto = Mapper.Map<FlagsEnumEntity, FlagsEnumToIntDto>(entity)!;
            Assert.Equal(3, dto.Access);

            var allEntity = new FlagsEnumEntity { Access = Permissions.All };
            var allDto = Mapper.Map<FlagsEnumEntity, FlagsEnumToIntDto>(allEntity)!;
            Assert.Equal(7, allDto.Access);
        }

        /// <summary>
        /// [Flags] 枚举 → string：Read|Write → "Read, Write"
        /// </summary>
        [Fact]
        public void FlagsEnum_To_String()
        {
            var entity = new FlagsEnumEntity { Access = Permissions.Read | Permissions.Write };
            var dto = Mapper.Map<FlagsEnumEntity, FlagsEnumToStringDto>(entity)!;
            Assert.Equal("Read, Write", dto.Access);

            var singleEntity = new FlagsEnumEntity { Access = Permissions.Execute };
            var singleDto = Mapper.Map<FlagsEnumEntity, FlagsEnumToStringDto>(singleEntity)!;
            Assert.Equal("Execute", singleDto.Access);
        }

        // ================================================================
        // T10: byte 底层枚举测试
        // ================================================================

        /// <summary>
        /// byte 底层枚举 → int：Active=1 → 1
        /// </summary>
        [Fact]
        public void ByteEnum_To_Int()
        {
            var entity = new ByteEnumEntity { Status = ByteStatus.Active };
            var dto = Mapper.Map<ByteEnumEntity, ByteEnumToIntDto>(entity)!;
            Assert.Equal(1, dto.Status);

            var suspended = new ByteEnumEntity { Status = ByteStatus.Suspended };
            var suspendedDto = Mapper.Map<ByteEnumEntity, ByteEnumToIntDto>(suspended)!;
            Assert.Equal(3, suspendedDto.Status);
        }

        /// <summary>
        /// byte 底层枚举 → string：Active → "Active"
        /// </summary>
        [Fact]
        public void ByteEnum_To_String()
        {
            var entity = new ByteEnumEntity { Status = ByteStatus.Inactive };
            var dto = Mapper.Map<ByteEnumEntity, ByteEnumToStringDto>(entity)!;
            Assert.Equal("Inactive", dto.Status);
        }

        // ================================================================
        // T10: long 底层枚举测试
        // ================================================================

        /// <summary>
        /// long 底层枚举 → long：Large=1073741824 → 1073741824L
        /// </summary>
        [Fact]
        public void LongEnum_To_Long()
        {
            var entity = new LongEnumEntity { Size = FileSize.Large };
            var dto = Mapper.Map<LongEnumEntity, LongEnumToLongDto>(entity)!;
            Assert.Equal(1073741824L, dto.Size);

            var smallEntity = new LongEnumEntity { Size = FileSize.Small };
            var smallDto = Mapper.Map<LongEnumEntity, LongEnumToLongDto>(smallEntity)!;
            Assert.Equal(1024L, smallDto.Size);
        }
    }
}
