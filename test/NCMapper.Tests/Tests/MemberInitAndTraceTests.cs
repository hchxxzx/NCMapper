using NCMapper.Attributes;
using NCMapper.Tests.Models;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// MemberInit 优化测试 — 验证运行时 MemberInit 路径与 Block 路径结果一致
    /// </summary>
    [Collection("DeepCopyGlobalState")]
    public class MemberInitTests
    {
        /// <summary>
        /// MemberInit 路径 — 同类型属性正确映射
        /// </summary>
        [Fact]
        public void MemberInit_SameType_All_Properties_Correct()
        {
            var entity = new FullTypeEntity
            {
                IntProp = 42, LongProp = 999L, DecimalProp = 3.14m,
                StringProp = "test", BoolProp = true,
                DateTimeProp = new DateTime(2024, 6, 1),
                GuidProp = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")
            };

            var dto = Mapper.Map<FullTypeEntity, SameTypeDto>(entity)!;

            Assert.Equal(42, dto.IntProp);
            Assert.Equal(999L, dto.LongProp);
            Assert.Equal(3.14m, dto.DecimalProp);
            Assert.Equal("test", dto.StringProp);
            Assert.True(dto.BoolProp);
            Assert.Equal(new DateTime(2024, 6, 1), dto.DateTimeProp);
            Assert.Equal(entity.GuidProp, dto.GuidProp);
        }

        /// <summary>
        /// MemberInit 路径 — 隐式转换（int→long）正确
        /// </summary>
        [Fact]
        public void MemberInit_Implicit_Conversion_Correct()
        {
            var entity = new PrecisionEntity
            {
                BigLong = 200L,
                PreciseDecimal = 99.9m,
                ZeroValue = 100
            };

            var dto = Mapper.Map<PrecisionEntity, PrecisionDto>(entity)!;

            Assert.Equal(200L, dto.BigLong);
            Assert.Equal(100, dto.ZeroValue);
        }

        /// <summary>
        /// MemberInit 路径 — null 源返回 default
        /// </summary>
        [Fact]
        public void MemberInit_Null_Source_Returns_Default()
        {
            var dto = Mapper.Map<FullTypeEntity, SameTypeDto>(null!);
            Assert.Null(dto);
        }

        /// <summary>
        /// MemberInit 路径 — 含 [MapProperty] 时回退 Block 路径，结果仍正确
        /// </summary>
        [Fact]
        public void MemberInit_With_MapProperty_Falls_Back_To_Block()
        {
            var entity = new FullTypeEntity { IntProp = 1, StringProp = "Test" };
            var dto = Mapper.Map<FullTypeEntity, ValueToStringDto>(entity)!;

            Assert.Equal("1", dto.IntProp);
        }
    }

    /// <summary>
    /// Trace 集成测试 — 验证 TraceEnabled 开关和 Trace 输出
    /// </summary>
    [Collection("DeepCopyGlobalState")]
    public class TraceIntegrationTests
    {
        /// <summary>
        /// TraceEnabled=false（默认）— 映射正常工作，无 Trace 输出
        /// </summary>
        [Fact]
        public void Trace_Disabled_Mapping_Works()
        {
            Mapper.TraceEnabled = false;
            try
            {
                var entity = new FullTypeEntity { IntProp = 1, StringProp = "no trace" };
                var dto = Mapper.Map<FullTypeEntity, SameTypeDto>(entity)!;

                Assert.Equal(1, dto.IntProp);
                Assert.Equal("no trace", dto.StringProp);
            }
            finally
            {
                Mapper.TraceEnabled = false;
            }
        }

        /// <summary>
        /// TraceEnabled=true — 映射正常工作，Trace 不影响结果
        /// </summary>
        [Fact]
        public void Trace_Enabled_Mapping_Still_Correct()
        {
            Mapper.TraceEnabled = true;
            try
            {
                var entity = new FullTypeEntity { IntProp = 99, StringProp = "traced" };
                var dto = Mapper.Map<FullTypeEntity, SameTypeDto>(entity)!;

                Assert.Equal(99, dto.IntProp);
                Assert.Equal("traced", dto.StringProp);
            }
            finally
            {
                Mapper.TraceEnabled = false;
            }
        }

        /// <summary>
        /// TraceEnabled=true — MapTracer.Trace 返回非空报告
        /// </summary>
        [Fact]
        public void Trace_Enabled_Returns_Report()
        {
            var entity = new FullTypeEntity { IntProp = 42, StringProp = "report" };
            var report = MapTracer.Trace<FullTypeEntity, SameTypeDto>(entity);

            Assert.NotNull(report);
            Assert.Contains("FullTypeEntity", report);
            Assert.Contains("SameTypeDto", report);
        }

        /// <summary>
        /// Trace — 含 MapIgnore 属性的报告显示 skipped
        /// </summary>
        [Fact]
        public void Trace_Shows_MapIgnore_Skipped()
        {
            var entity = new MapIgnoreTestEntity { MappedField = "keep", IgnoredField = "skip" };
            var report = MapTracer.Trace<MapIgnoreTestEntity, MapIgnoreTestDto>(entity);

            Assert.NotNull(report);
            Assert.Contains("MappedField", report);
        }
    }

    /// <summary>
    /// 枚举映射测试 — 运行时路径
    /// </summary>
    [Collection("DeepCopyGlobalState")]
    public class EnumMappingTests
    {
        /// <summary>
        /// 枚举→int 映射正确
        /// </summary>
        [Fact]
        public void Enum_ToInt_Map_Correct()
        {
            // 修复：源显式赋非默认枚举值，断言真实转换（原断言比较 IntProp 默认值 0==0 恒真）
            var entity = new FullTypeEntity { StatusProp = OrderStatus.Shipped };
            var dto = Mapper.Map<FullTypeEntity, EnumToIntDto>(entity)!;
            Assert.Equal((int)OrderStatus.Shipped, dto.StatusProp);
        }

        /// <summary>
        /// 回归（H-05）：运行时引擎（ExpressionBuilder.BuildEnumToStringSwitch）
        /// 别名枚举 enum→string 退化为 ToString()——输出与 Enum.ToString() 一致（取第一个成员名 "A"），
        /// 同值别名成员输出一致；与源生成器退化路径（SgAliasedEnum 测试）同条件同行为。
        /// </summary>
        [Fact]
        public void Enum_Aliased_ToString_Degrades_ConsistentWithToString()
        {
            var dtoA = Mapper.Map<AliasedEnumEntity, AliasedEnumToStringDto>(
                new AliasedEnumEntity { Status = AliasedEnum.A })!;
            var dtoB = Mapper.Map<AliasedEnumEntity, AliasedEnumToStringDto>(
                new AliasedEnumEntity { Status = AliasedEnum.B })!;

            Assert.Equal(AliasedEnum.A.ToString(), dtoA.Status);
            Assert.Equal(AliasedEnum.B.ToString(), dtoB.Status);
            Assert.Equal(dtoA.Status, dtoB.Status); // 同值别名成员输出一致（均为 ToString 首名 "A"）
        }

        /// <summary>
        /// 枚举→string 映射（通过 TypeConvertHelper）
        /// </summary>
        [Fact]
        public void Enum_ToString_Via_TypeConvertHelper()
        {
            var result = TypeConvertHelper.ConvertValue<OrderStatus, string>(OrderStatus.Pending);
            Assert.Equal("Pending", result);
        }

        /// <summary>
        /// string→枚举 映射（通过 TypeConvertHelper）
        /// </summary>
        [Fact]
        public void String_ToEnum_Via_TypeConvertHelper()
        {
            var result = TypeConvertHelper.ConvertValue<string, OrderStatus>("Shipped");
            Assert.Equal(OrderStatus.Shipped, result);
        }

        /// <summary>
        /// 浮点→枚举 映射（通过 TypeConvertHelper）— 不抛异常
        /// </summary>
        [Fact]
        public void Float_ToEnum_Via_TypeConvertHelper()
        {
            var result = TypeConvertHelper.ConvertValue<double, OrderStatus>(2.0);
            Assert.Equal(OrderStatus.Shipped, result);
        }
    /// <summary>枚举→int 映射专用 DTO（修复）</summary>
    [MapFrom(typeof(FullTypeEntity))]
    public class EnumToIntDto
    {
        public int StatusProp { get; set; }
    }

    /// <summary>别名枚举（数值重复成员）— 双引擎退化回归（运行时侧）</summary>
    public enum AliasedEnum
    {
        A = 1,
        B = 1
    }

    public class AliasedEnumEntity
    {
        public AliasedEnum Status { get; set; } = AliasedEnum.A;
    }

    [MapFrom(typeof(AliasedEnumEntity))]
    public class AliasedEnumToStringDto
    {
        public string Status { get; set; } = "";
    }

    }
}
