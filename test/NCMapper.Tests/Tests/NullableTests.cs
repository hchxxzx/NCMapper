using NCMapper.Tests.Models;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// 可空类型测试 — int? ↔ long 转换、null 值处理
    /// </summary>
    public class NullableTests
    {
        /// <summary>
        /// int? 有值时，通过 HasValue 检查 → Expression.Convert → 正常转换
        /// int? 为 null 时，Condition(HasValue, Convert, Default) → 返回目标类型默认值
        /// </summary>
        [Fact]
        public void Nullable_HasValue_Mapped()
        {
            var entity = new FullTypeEntity { NullableIntProp = 100, NullIntNull = null };
            var dto = Mapper.Map<FullTypeEntity, NullableDto>(entity)!;

            // int? 100 → long 100（HasValue=true，执行 Convert）
            Assert.Equal(100L, dto.NullableIntProp);
            // int? null → long 0（HasValue=false，执行 Default(long)=0）
            Assert.Equal(0L, dto.NullIntNull);
        }

        /// <summary>
        /// 全部可空字段均为 null 时，统一返回默认值
        /// </summary>
        [Fact]
        public void Nullable_Null_Defaults()
        {
            var entity = new FullTypeEntity { NullableIntProp = null, NullIntNull = null };
            var dto = Mapper.Map<FullTypeEntity, NullableDto>(entity)!;

            Assert.Equal(0L, dto.NullableIntProp);
            Assert.Equal(0L, dto.NullIntNull);
        }

        // ================================================================
        // DateTime? → DateTime
        // ================================================================

        [Fact]
        public void NullableDateTime_HasValue_Mapped()
        {
            var entity = new NullableTypesEntity { NullableDateTime = new DateTime(2024, 6, 15) };
            var dto = Mapper.Map<NullableTypesEntity, NullableToNonNullDto>(entity)!;

            Assert.Equal(new DateTime(2024, 6, 15), dto.NullableDateTime);
        }

        [Fact]
        public void NullableDateTime_Null_Returns_Default()
        {
            var entity = new NullableTypesEntity { NullDateTime = null };
            var dto = Mapper.Map<NullableTypesEntity, NullableToNonNullDto>(entity)!;

            Assert.Equal(default(DateTime), dto.NullDateTime);
        }

        // ================================================================
        // Guid? → Guid
        // ================================================================

        [Fact]
        public void NullableGuid_HasValue_Mapped()
        {
            var guid = Guid.NewGuid();
            var entity = new NullableTypesEntity { NullableGuid = guid };
            var dto = Mapper.Map<NullableTypesEntity, NullableToNonNullDto>(entity)!;

            Assert.Equal(guid, dto.NullableGuid);
        }

        [Fact]
        public void NullableGuid_Null_Returns_Default()
        {
            var entity = new NullableTypesEntity { NullGuid = null };
            var dto = Mapper.Map<NullableTypesEntity, NullableToNonNullDto>(entity)!;

            Assert.Equal(default(Guid), dto.NullGuid);
        }

        // ================================================================
        // decimal? → decimal
        // ================================================================

        [Fact]
        public void NullableDecimal_HasValue_Mapped()
        {
            var entity = new NullableTypesEntity { NullableDecimal = 987.65m };
            var dto = Mapper.Map<NullableTypesEntity, NullableToNonNullDto>(entity)!;

            Assert.Equal(987.65m, dto.NullableDecimal);
        }

        [Fact]
        public void NullableDecimal_Null_Returns_Default()
        {
            var entity = new NullableTypesEntity { NullDecimal = null };
            var dto = Mapper.Map<NullableTypesEntity, NullableToNonNullDto>(entity)!;

            Assert.Equal(default(decimal), dto.NullDecimal);
        }

        // ================================================================
        // enum? → enum (OrderStatus? → OrderStatus)
        // ================================================================

        [Fact]
        public void NullableEnum_HasValue_Mapped()
        {
            var entity = new NullableTypesEntity { NullableStatus = OrderStatus.Delivered };
            var dto = Mapper.Map<NullableTypesEntity, NullableToNonNullDto>(entity)!;

            Assert.Equal(OrderStatus.Delivered, dto.NullableStatus);
        }

        [Fact]
        public void NullableEnum_Null_Returns_Default()
        {
            var entity = new NullableTypesEntity { NullStatus = null };
            var dto = Mapper.Map<NullableTypesEntity, NullableToNonNullDto>(entity)!;

            Assert.Equal(default(OrderStatus), dto.NullStatus);
        }

        // ================================================================
        // Non-nullable → nullable (DateTime → DateTime?)
        // ================================================================

        [Fact]
        public void NonNull_To_Nullable_Mapped()
        {
            var entity = new NullableTypesEntity { NonNullDateTime = new DateTime(2023, 12, 31) };
            var dto = Mapper.Map<NullableTypesEntity, NonNullToNullableDto>(entity)!;

            Assert.NotNull(dto.NonNullDateTime);
            Assert.Equal(new DateTime(2023, 12, 31), dto.NonNullDateTime!.Value);
        }
    }
}
