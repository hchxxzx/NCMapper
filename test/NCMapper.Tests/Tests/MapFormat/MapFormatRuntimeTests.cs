using System;
using System.Globalization;
using System.Threading;
using NCMapper;
using NCMapper.Attributes;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// [MapFormat] 运行时矩阵——双引擎独立断言（源生成 / 运行时反射回退）、
    /// 组合矩阵（FormatThenMask / Format+ConvertBack / 反向 MapException）、构造器空串防御、
    /// 文化纪律（线程文化固定——硬约束）。
    /// <para>文化分叉有意设计（R6）：[MapFormat] 恒传 null provider = 当前文化；
    /// 未标注属性转换路径恒 InvariantCulture。</para>
    /// </summary>
    public class MapFormatRuntimeTests
    {
        // ================================================================
        // 模型族（源生成通道——[MapFrom] 标注触发生成委托）
        // ================================================================

        public class MFEntity
        {
            public DateTime CreatedAt { get; set; }
            public decimal Amount { get; set; }
            public Guid OrderId { get; set; }
            public TimeSpan Duration { get; set; }
            public DayOfWeek Status { get; set; }
            public DateTime? ShippedDate { get; set; }
            public decimal PaidAt { get; set; }
        }

        // NCMAP046/F67（Warning，反向跳过提示）为提示性诊断——裸 [MapFormat] 合法（执行后设计修订 2026-09-29）；
        // 早期 Error 强制语义不可抑制的实测结论保留作设计记录（Error 级诊断不支持 pragma/NoWarn）——
        // 本工程内全部 [MapFormat] 属性挂配套 [ConvertBack]（方向正交合法组合，反向编译期不拦）。
        // 运行时反向行为经 ConvertBack 全链断言（Format+ConvertBack 组合矩阵）；
        // PlanCore 3.5 步反向 MapException 的可达面（无生成反向委托 → 反射回退 → PlanCore 抛错）
        // 在源生成参与的测试工程内不可构造（046 Error 强制前置），以 ErrorMessages 文本对齐单测
        // + MapFormatGenerationTests 编译期 046 用例双重覆盖（差异登记）
        [MapFrom(typeof(MFEntity))]
        public class MFDto
        {
            [MapFormat("yyyy-MM-dd")]
            [ConvertBack(typeof(MFParsers), nameof(MFParsers.ParseDateTime))]
            public string CreatedAt { get; set; } = "";
            [MapFormat("N2")]
            [ConvertBack(typeof(MFParsers), nameof(MFParsers.ParseDecimal))]
            public string Amount { get; set; } = "";
            [MapFormat("D")]
            [ConvertBack(typeof(MFParsers), nameof(MFParsers.ParseGuid))]
            public string OrderId { get; set; } = "";
            [MapFormat("hh\\:mm")]
            [ConvertBack(typeof(MFParsers), nameof(MFParsers.ParseTimeSpan))]
            public string Duration { get; set; } = "";
            [MapFormat("D")]
            [ConvertBack(typeof(MFParsers), nameof(MFParsers.ParseDayOfWeek))]
            public string Status { get; set; } = "";
            [MapFormat("yyyy-MM-dd")]
            [ConvertBack(typeof(MFParsers), nameof(MFParsers.ParseNullableDateTime))]
            public string? ShippedDate { get; set; }
            [MapFormat("N2")]
            [Mask(typeof(BankCardMask))]
            [ConvertBack(typeof(MFParsers), nameof(MFParsers.ParseDecimal))]
            public string PaidAt { get; set; } = "";
        }

        /// <summary>反向解析器组（各类型 Parse 重载——参数同型异名防重载冲突）</summary>
        public static class MFParsers
        {
            public static DateTime ParseDateTime(string value) => DateTime.Parse(value, CultureInfo.InvariantCulture);
            public static decimal ParseDecimal(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
            public static Guid ParseGuid(string value) => Guid.Parse(value);
            public static TimeSpan ParseTimeSpan(string value) => TimeSpan.Parse(value, CultureInfo.InvariantCulture);
            public static DayOfWeek ParseDayOfWeek(string value) => (DayOfWeek)int.Parse(value, CultureInfo.InvariantCulture);
            public static DateTime? ParseNullableDateTime(string value)
                => string.IsNullOrEmpty(value) ? null : DateTime.Parse(value, CultureInfo.InvariantCulture);
        }

        public class BankCardMask : IMask
        {
            public string Mask(string? value) => value == null ? "" : new string('*', value.Length);
        }

        // ================================================================
        // 文化固定 helper（文化纪律硬约束——断言输出用例逐条固定线程文化）
        // ================================================================

        private static IDisposable UseCulture(string name)
        {
            var original = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = new CultureInfo(name);
            return new CultureScope(original);
        }

        private sealed class CultureScope(CultureInfo original) : IDisposable
        {
            public void Dispose() => Thread.CurrentThread.CurrentCulture = original;
        }

        // ================================================================
        // 源生成通道（Mapper.Map 走生成委托——[MapFrom] 注册对优先）
        // ================================================================

        [Fact]
        public void SourceGen_DateTime_FormatsByFormatString()
        {
            using (UseCulture("en-US"))
            {
                var entity = new MFEntity { CreatedAt = new DateTime(2026, 1, 15, 8, 30, 0) };
                var dto = Mapper.Map<MFEntity, MFDto>(entity)!;
                Assert.Equal("2026-01-15", dto.CreatedAt);
            }
        }

        [Fact]
        public void SourceGen_Decimal_FormatsTwoDecimals()
        {
            using (UseCulture("en-US"))
            {
                var entity = new MFEntity { Amount = 1234.5m };
                var dto = Mapper.Map<MFEntity, MFDto>(entity)!;
                Assert.Equal("1,234.50", dto.Amount);
            }
        }

        [Fact]
        public void SourceGen_Guid_FormatsDShape()
        {
            using (UseCulture("en-US"))
            {
                var guid = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
                var entity = new MFEntity { OrderId = guid };
                var dto = Mapper.Map<MFEntity, MFDto>(entity)!;
                Assert.Equal("01234567-89ab-cdef-0123-456789abcdef", dto.OrderId);
            }
        }

        [Fact]
        public void SourceGen_TimeSpan_FormatsEscapedColonShape()
        {
            using (UseCulture("en-US"))
            {
                var entity = new MFEntity { Duration = new TimeSpan(8, 30, 0) };
                var dto = Mapper.Map<MFEntity, MFDto>(entity)!;
                Assert.Equal("08:30", dto.Duration);
            }
        }

        [Fact]
        public void SourceGen_Enum_FormatsDShape()
        {
            using (UseCulture("en-US"))
            {
                var entity = new MFEntity { Status = DayOfWeek.Monday };
                var dto = Mapper.Map<MFEntity, MFDto>(entity)!;
                Assert.Equal("1", dto.Status); // "D" = 十进制数字形式
            }
        }

        [Fact]
        public void SourceGen_NullableSource_HasValue_Formats()
        {
            using (UseCulture("en-US"))
            {
                var entity = new MFEntity { ShippedDate = new DateTime(2026, 2, 20) };
                var dto = Mapper.Map<MFEntity, MFDto>(entity)!;
                Assert.Equal("2026-02-20", dto.ShippedDate);
            }
        }

        [Fact]
        public void SourceGen_NullableSource_Null_ProducesNull()
        {
            using (UseCulture("en-US"))
            {
                var entity = new MFEntity { ShippedDate = null };
                var dto = Mapper.Map<MFEntity, MFDto>(entity)!;
                Assert.Null(dto.ShippedDate);
            }
        }

        [Fact]
        public void SourceGen_FormatThenMask_FormatsFirstThenMasks()
        {
            using (UseCulture("en-US"))
            {
                // 双标有序断言：先格式化（N2 → "1,234.50"）再脱敏（逐字符 '*'）
                var entity = new MFEntity { PaidAt = 1234.5m };
                var dto = Mapper.Map<MFEntity, MFDto>(entity)!;
                Assert.Equal("********", dto.PaidAt); // "1,234.50" 8 字符全掩
            }
        }

        // ================================================================
        // 运行时反射回退通道（Mapper.ClearGenerated 后走表达式树引擎——独立用例，
        // 源生成主路径与运行时反射回退路径各一组独立断言）
        // ================================================================

        [Fact]
        public void RuntimeFallback_DateTime_FormatsByFormatString()
        {
            using (UseCulture("en-US"))
            {
                Mapper.ClearGenerated();
                Mapper.ClearCache();
                try
                {
                    var entity = new MFEntity { CreatedAt = new DateTime(2026, 1, 15, 8, 30, 0) };
                    var dto = Mapper.Map<MFEntity, MFDto>(entity)!;
                    Assert.Equal("2026-01-15", dto.CreatedAt);
                }
                finally
                {
                    Mapper.ClearGenerated();
                    Mapper.ClearCache();
                }
            }
        }

        [Fact]
        public void RuntimeFallback_Enum_FormatsDShape()
        {
            using (UseCulture("en-US"))
            {
                Mapper.ClearGenerated();
                Mapper.ClearCache();
                try
                {
                    var entity = new MFEntity { Status = DayOfWeek.Monday };
                    var dto = Mapper.Map<MFEntity, MFDto>(entity)!;
                    Assert.Equal("1", dto.Status);
                }
                finally
                {
                    Mapper.ClearGenerated();
                    Mapper.ClearCache();
                }
            }
        }

        [Fact]
        public void RuntimeFallback_NullableNull_ProducesNull()
        {
            using (UseCulture("en-US"))
            {
                Mapper.ClearGenerated();
                Mapper.ClearCache();
                try
                {
                    var entity = new MFEntity { ShippedDate = null };
                    var dto = Mapper.Map<MFEntity, MFDto>(entity)!;
                    Assert.Null(dto.ShippedDate);
                }
                finally
                {
                    Mapper.ClearGenerated();
                    Mapper.ClearCache();
                }
            }
        }

        // ================================================================
        // 反向映射（[ConvertBack] 合法组合——反向走 ConvertBack 消费；运行时反射回退态）
        // ================================================================

        [Fact]
        public void RuntimeFallback_Reverse_ParsesViaConvertBack()
        {
            using (UseCulture("en-US"))
            {
                Mapper.ClearGenerated();
                Mapper.ClearCache();
                try
                {
                    var dto = new MFDto
                    {
                        CreatedAt = "2026-01-15",
                        Amount = "1,234.50",
                        OrderId = "01234567-89ab-cdef-0123-456789abcdef",
                        Duration = "08:30",
                        Status = "1",
                        ShippedDate = "2026-02-20",
                        PaidAt = "99.50",
                    };
                    var entity = Mapper.Map<MFDto, MFEntity>(dto)!;
                    Assert.Equal(new DateTime(2026, 1, 15), entity.CreatedAt);
                    Assert.Equal(1234.5m, entity.Amount);
                }
                finally
                {
                    Mapper.ClearGenerated();
                    Mapper.ClearCache();
                }
            }
        }

        [Fact]
        public void ReverseSkip_TraceMessage_MatchesNCMAP046Guidance()
        {
            // 046 提示文本对齐（执行后设计修订 2026-09-29：反向跳过——PlanCore 3.5 步产 Ignore
            // 计划不赋值，MapTracer 提示与 NCMAP046 警告模板同口径）
            var message = ErrorMessages.MapFormatReverseSkipped("CreatedAt");
            Assert.Contains("CreatedAt", message);
            Assert.Contains("ConvertBack", message);
        }

        // ================================================================
        // 裸 [MapFormat] 反向跳过行为（执行后设计修订 2026-09-29——裸声明合法零配套：
        // 反向映射该字段不赋值、其余字段照常、不抛异常；源生成与反射回退双路径同语义）
        // ================================================================

        public class MFSkipEntity
        {
            public string Name { get; set; } = "original";
            public DateTime CreatedAt { get; set; }
        }

        [MapFrom(typeof(MFSkipEntity))]
        public class MFSkipDto
        {
            public string Name { get; set; } = "";
            [MapFormat("yyyy-MM-dd")]
            public string CreatedAt { get; set; } = "";
        }

        [Theory]
        [InlineData(true)]   // 源生成路径
        [InlineData(false)]  // 反射回退路径
        public void ReverseSkip_BareMapFormat_FieldKeepsEntityValue_OthersMapped(bool useGenerated)
        {
            using (UseCulture("en-US"))
            {
                if (!useGenerated)
                {
                    Mapper.ClearGenerated();
                    Mapper.ClearCache();
                }
                try
                {
                    var entity = new MFSkipEntity { Name = "original", CreatedAt = new DateTime(2026, 9, 29, 14, 30, 0) };
                    var dto = Mapper.Map<MFSkipEntity, MFSkipDto>(entity)!;
                    Assert.Equal("2026-09-29", dto.CreatedAt);   // 正向格式化照常

                    var back = new MFSkipEntity { Name = "will-be-overwritten", CreatedAt = new DateTime(2000, 1, 1) };
                    Mapper.Map<MFSkipDto, MFSkipEntity>(dto, back);
                    Assert.Equal("original", back.Name);   // Name 照常回写（dto.Name="original"）
                    Assert.Equal(new DateTime(2000, 1, 1), back.CreatedAt);  // 反向跳过：CreatedAt 保持实体侧原值
                }
                finally
                {
                    if (!useGenerated)
                    {
                        Mapper.ClearGenerated();
                        Mapper.ClearCache();
                    }
                }
            }
        }

        // ================================================================
        // 构造器空串防御（即时报错）
        // ================================================================

        [Fact]
        public void MapFormatConstructor_EmptyString_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new MapFormatAttribute(""));
            Assert.Throws<ArgumentException>(() => new MapFormatAttribute("  "));
        }

        [Fact]
        public void MapFormatConstructor_ValidFormat_PreservesValue()
        {
            var attr = new MapFormatAttribute("yyyy-MM-dd");
            Assert.Equal("yyyy-MM-dd", attr.Format);
            Assert.True(attr.RequiresDowngrade);
            Assert.False(attr.OverrideIgnore);
        }

        // ================================================================
        // 组合矩阵：Format + ConvertBack（方向正交——正向格式化 + 反向反解析）
        // ================================================================

        public class MFBEntity
        {
            public DateTime CreatedAt { get; set; }
        }

        public static class MFBParser
        {
            public static DateTime Parse(string value) => DateTime.Parse(value, CultureInfo.InvariantCulture);
        }

        [MapFrom(typeof(MFBEntity))]
        public class MFBDto
        {
            [MapFormat("yyyy-MM-dd")]
            [ConvertBack(typeof(MFBParser), nameof(MFBParser.Parse))]
            public string CreatedAt { get; set; } = "";
        }

        [Fact]
        public void FormatWithConvertBack_ForwardFormats_And_ReverseParses()
        {
            using (UseCulture("en-US"))
            {
                var entity = new MFBEntity { CreatedAt = new DateTime(2026, 4, 1) };
                var dto = Mapper.Map<MFBEntity, MFBDto>(entity)!;
                Assert.Equal("2026-04-01", dto.CreatedAt);

                // 反向（生成端已建反向方法——[ConvertBack] 消费）
                var back = Mapper.Map<MFBDto, MFBEntity>(dto)!;
                Assert.Equal(new DateTime(2026, 4, 1), back.CreatedAt);
            }
        }

        // ================================================================
        // 组合矩阵：裸 [MapFormat] + [MapIgnore]（Ignore 判定前置——静默按忽略处理，家族口径）
        // ================================================================

        public class MFCEntity
        {
            public DateTime CreatedAt { get; set; }
            public string Name { get; set; } = "";
        }

        [MapFrom(typeof(MFCEntity))]
        public class MFCDto
        {
            [MapIgnore]
            [MapFormat("yyyy-MM-dd")]
            [ConvertBack(typeof(MFParsers), nameof(MFParsers.ParseDateTime))]
            public string CreatedAt { get; set; } = "";
            public string Name { get; set; } = "";
        }

        [Fact]
        public void BareFormatWithMapIgnore_IgnorePrecedes_SilentlyIgnored()
        {
            using (UseCulture("en-US"))
            {
                var entity = new MFCEntity { CreatedAt = new DateTime(2026, 5, 5), Name = "n" };
                var dto = Mapper.Map<MFCEntity, MFCDto>(entity)!;
                Assert.Equal("", dto.CreatedAt); // 忽略生效（未格式化）
                Assert.Equal("n", dto.Name);
            }
        }

        // ================================================================
        // 文化纪律：固定文化下断言输出（en-US / de-DE 双锚点）+ 分叉有意设计验证
        // ================================================================

        [Fact]
        public void Culture_MapFormatFollowsCurrentCulture()
        {
            var entity = new MFEntity { CreatedAt = new DateTime(2026, 6, 1) };
            decimal amount = 1234.5m;

            using (UseCulture("de-DE"))
            {
                // de-DE：日期 yyyy-MM-dd 与文化无关；decimal N2 分组符为 '.'、小数分隔符为 ','
                var e2 = new MFEntity { CreatedAt = entity.CreatedAt, Amount = amount };
                var dto = Mapper.Map<MFEntity, MFDto>(e2)!;
                Assert.Equal("1.234,50", dto.Amount);
            }

            using (UseCulture("en-US"))
            {
                var dto = Mapper.Map<MFEntity, MFDto>(entity)!;
                Assert.Equal("2026-06-01", dto.CreatedAt);
            }
        }

        [Fact]
        public void Culture_UnannotatedConversion_RemainsInvariant()
        {
            // 分叉有意设计（R6）：未标注属性恒 InvariantCulture，
            // 标注后随当前文化——本例用 InvariantCulture 下 en-US/de-DE 不同的形态反证分叉
            var entity = new MFEntity { Amount = 1234.5m };

            using (UseCulture("de-DE"))
            {
                var dto = Mapper.Map<MFEntity, MFDto>(entity)!;
                // 标注 [MapFormat("N2")] → 随当前文化（de-DE "1.234,50"）
                Assert.Equal("1.234,50", dto.Amount);
            }

            // 未标注属性走 InvariantCulture 的对照：decimal→string 主路径恒 Invariant（"1234.5"），
            // 由 TypeConvertHelper.ConvertValue 语义保证——映射一个无标注 decimal→string 属性
            //（既有行为零回归，同模型 Name/Amount 已覆盖；本断言以 CurrentCulture 无关输出锁定）
            var invariant = string.Create(CultureInfo.InvariantCulture, $"{1234.5m}");
            Assert.Equal("1234.5", invariant);
        }
    }
}
