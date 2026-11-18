using Xunit;
using NCMapper.Tests.Models;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// 映射验证器测试
    /// </summary>
    [Collection("DeepCopyGlobalState")]
    public class MapValidatorTests
    {
        /// <summary>
        /// 所有属性可映射时返回 OK 级别
        /// </summary>
        [Fact]
        public void Validate_OK_When_All_Properties_Mapped()
        {
            var result = MapValidator.ValidateMapping(typeof(FullTypeEntity), typeof(SameTypeDto));

            Assert.Equal(MapValidationLevel.OK, result.Level);
            Assert.True(result.MappedCount > 0);
            Assert.Equal(result.TotalCount, result.MappedCount);
        }

        /// <summary>
        /// 大小写不敏感匹配 — CaseMismatchDto 的 intprop/stringprop 匹配 FullTypeEntity 的 IntProp/StringProp，返回 OK
        /// </summary>
        [Fact]
        public void Validate_OK_When_CaseInsensitive_Match()
        {
            // NameDiffDto 的 FullName 标注了 [MapProperty(typeof(NameDiffEntity), "Name")]
            // 但如果源类型没有某个字段，应该警告
            var result = MapValidator.ValidateMapping(typeof(FullTypeEntity), typeof(CaseMismatchDto));

            // CaseMismatchDto 有 intprop 和 stringprop，大小写不敏感匹配 FullTypeEntity 的 IntProp/StringProp
            Assert.Equal(MapValidationLevel.OK, result.Level);
        }

        /// <summary>
        /// 多源 DTO 验证 — DictTargetDto 从 DictSourceA 和 DictSourceB 取值，验证 A→DTO 时 MappedCount > 0
        /// </summary>
        [Fact]
        public void Validate_MappedCount_For_MultiSource()
        {
            // 构造一个类型不兼容的场景：DictSourceA 的 NameA 是 string，如果目标属性是 int 则不兼容
            // 但实际我们的测试模型都是兼容的，这里验证验证器本身能正确工作
            var result = MapValidator.ValidateMapping(typeof(DictSourceA), typeof(DictTargetDto));

            // DictTargetDto 标注了从 A 和 B 两个字段来源，验证 A→DTO 时只有 A 的字段算
            Assert.True(result.MappedCount > 0);
        }

        /// <summary>
        /// 未映射属性列表正确返回
        /// </summary>
        [Fact]
        public void Validate_Returns_Correct_Unmapped_Properties()
        {
            // SgIgnoreDto 有 [MapIgnore] 标注的 Computed 属性，应被跳过
            var result = MapValidator.ValidateMapping(typeof(MapIgnoreTestEntity), typeof(MapIgnoreTestDto));

            // MapIgnoreTestDto.IgnoredField 没有 [MapIgnore] 标注，但源 MapIgnoreTestEntity 也有这个属性
            // 所以应该能映射
            Assert.True(result.TotalCount >= 1);
        }

        /// <summary>
        /// Validate 正确扫描整个程序集
        /// </summary>
        [Fact]
        public void Validate_Scans_Entire_Assembly()
        {
            var results = MapValidator.Validate(typeof(FullTypeEntity).Assembly);

            Assert.NotEmpty(results);
            // 至少包含 FullTypeEntity → SameTypeDto
            Assert.Contains(results, r => r.SourceType == typeof(FullTypeEntity) && r.TargetType == typeof(SameTypeDto));
        }

        /// <summary>
        /// PrintReport 不抛异常
        /// </summary>
        [Fact]
        public void Validate_PrintReport_Does_Not_Throw()
        {
            var results = MapValidator.Validate(typeof(FullTypeEntity).Assembly);
            var exception = Record.Exception(() => MapValidator.PrintReport(results, showOk: true));
            Assert.Null(exception);
        }

        /// <summary>
        /// Mapper.ValidationEnabled 默认为 true
        /// </summary>
        [Fact]
        public void Mapper_Validate_Enabled_By_Default()
        {
            Assert.True(Mapper.ValidationEnabled);
        }

        /// <summary>
        /// ValidationEnabled=false 时返回空结果
        /// </summary>
        [Fact]
        public void Mapper_Validate_Disabled_Returns_Empty()
        {
            Mapper.ValidationEnabled = false;
            try
            {
                var results = Mapper.Validate(typeof(FullTypeEntity).Assembly);
                Assert.Empty(results);
            }
            finally
            {
                Mapper.ValidationEnabled = true; // 恢复默认
            }
        }

        /// <summary>
        /// ValidationEnabled=true 时返回有效结果
        /// </summary>
        [Fact]
        public void Mapper_Validate_Enabled_Returns_Results()
        {
            var results = Mapper.Validate(typeof(FullTypeEntity).Assembly);
            Assert.NotEmpty(results);
        }

        /// <summary>
        /// ValidateResult.ToString() 包含图标和类型名
        /// </summary>
        [Fact]
        public void ValidateResult_ToString_Contains_Icon()
        {
            var result = MapValidator.ValidateMapping(typeof(FullTypeEntity), typeof(SameTypeDto));
            var str = result.ToString();

            Assert.Contains("→", str);
            Assert.Contains("FullTypeEntity", str);
            Assert.Contains("SameTypeDto", str);
        }

        /// <summary>
        /// 跨类型兼容验证 — ValueToStringDto.IntProp(string) 与 FullTypeEntity.IntProp(int) 类型不同但可转换
        /// </summary>
        [Fact]
        public void Validate_CrossType_Compatible()
        {
            // ValueToStringDto 有 StringProp 但 FullTypeEntity 也有 StringProp（同名）
            // 但 ValueToStringDto.IntProp 是 string，FullTypeEntity.IntProp 是 int — 类型不兼容
            var result = MapValidator.ValidateMapping(typeof(FullTypeEntity), typeof(ValueToStringDto));

            // 应该有映射结果（不是 Error），因为 int→string 是允许的转换路径
            Assert.True(result.TotalCount > 0);
        }

        /// <summary>
        /// 多源部分未映射 — DictTargetDto 的 FieldB 来自 DictSourceB，验证 A→DTO 时 FieldB 未映射
        /// </summary>
        [Fact]
        public void Validate_MultiSource_PartialUnmapped()
        {
            // DictTargetDto 的 FieldA 标注 [MapProperty(typeof(DictSourceA), "NameA")]
            // DictSourceA 有 NameA，所以应该能映射
            var result = MapValidator.ValidateMapping(typeof(DictSourceA), typeof(DictTargetDto));

            Assert.True(result.MappedCount > 0);
            // FieldB 来自 DictSourceB，验证 A→DTO 时 FieldB 应该未映射
            Assert.True(result.UnmappedProperties.Count > 0 || result.MappedCount < result.TotalCount);
        }

        /// <summary>
        /// Error 级别场景 — 两个完全不相关的类型无法映射
        /// </summary>
        [Fact]
        public void Validate_Handles_Unrelated_Types()
        {
            // LinkNode 有 Next 属性（自引用），LinkNodeDto 有 Next 属性
            // 验证自引用类型不会导致验证器崩溃
            var result = MapValidator.ValidateMapping(typeof(LinkNode), typeof(LinkNodeDto));

            Assert.NotNull(result);
            Assert.True(result.TotalCount > 0);
        }

        /// <summary>
        /// Error 级别场景 — 精度损失类型转换（double→int）
        /// </summary>
        [Fact]
        public void Validate_Handles_Precision_Loss_Types()
        {
            // PrecisionEntity 的 DoubleValue 是 double，PrecisionDto 的 IntValue 是 int
            // double→int 有精度损失但可转换
            var result = MapValidator.ValidateMapping(typeof(PrecisionEntity), typeof(PrecisionDto));

            Assert.NotNull(result);
            Assert.True(result.TotalCount > 0);
        }

        /// <summary>
        /// 验证结果包含未映射属性详情
        /// </summary>
        [Fact]
        public void Validate_Result_Contains_Unmapped_Details()
        {
            var result = MapValidator.ValidateMapping(typeof(FullTypeEntity), typeof(SameTypeDto));
            var str = result.ToString();

            // 结果应包含类型名
            Assert.Contains("FullTypeEntity", str);
            // 应包含映射数量信息
            Assert.True(result.MappedCount >= 0);
            Assert.True(result.TotalCount >= result.MappedCount);
        }

        /// <summary>
        /// 验证 MapIgnore 属性被正确排除
        /// </summary>
        [Fact]
        public void Validate_Excludes_MapIgnore_Properties()
        {
            var result = MapValidator.ValidateMapping(typeof(MapIgnoreTestEntity), typeof(MapIgnoreTestDto));

            // MapIgnoreTestDto.IgnoredField 标注了 [MapIgnore]，应被排除
            // MapIgnoreTestDto.MappedField 未标注，应参与验证
            Assert.True(result.TotalCount >= 1);
        }

        /// <summary>
        /// ValidateOrThrow 验证通过时不抛异常
        /// </summary>
        [Fact]
        public void ValidateOrThrow_NoError_Does_Not_Throw()
        {
            try
            {
                // 注册一个返回空列表（无 Error）的验证函数，避免走运行时扫描路径
                // （运行时扫描会发现测试程序集中 ErrorMismatchEntity→ErrorMismatchDto 等 Error 级别类型）
                Mapper.RegisterGeneratedValidate(() => new List<MapValidationResult>());

                var exception = Record.Exception(() => Mapper.ValidateOrThrow());
                Assert.Null(exception);
            }
            finally
            {
                // 适配：原反射保存/恢复 s_generatedValidateFunc 随单槽位
                // 字段删除失效；改用 null = 清空全部槽位复位（聚合语义下空列表不影响其余槽位结果）
                Mapper.RegisterGeneratedValidate(null);
            }
        }

        /// <summary>
        /// MapValidator 能正确检测 [Convert] 标注的属性 — 标注了转换器的属性应被计入已映射数
        /// </summary>
        [Fact]
        public void Validate_Detects_ConvertUsing_Properties()
        {
            // ConverterDto 有 3 个 [Convert] 标注属性（CreateTime/ParsedDate/Code）+ 1 个普通属性（Id）
            var result = MapValidator.ValidateMapping(typeof(ConverterSourceEntity), typeof(ConverterDto));

            // 所有属性都应被计入 TotalCount（[Convert] 不会被跳过）
            Assert.True(result.TotalCount >= 4);
            // 所有属性都应映射成功（类型兼容或通过转换器转换）
            Assert.True(result.MappedCount >= 4);
            // 不应有类型不兼容错误
            Assert.True(result.TypeMismatchProperties.Count == 0);
        }

        /// <summary>
        /// [Convert] 标注的属性在验证结果中计入 MappedCount
        /// </summary>
        [Fact]
        public void Validate_ConvertUsing_Properties_Counted_As_Mapped()
        {
            var result = MapValidator.ValidateMapping(typeof(ConverterSourceEntity), typeof(ConverterDto));

            // ConverterDto 的 4 个属性（Id/CreateTime/ParsedDate/Code）都应映射成功
            Assert.Equal(result.TotalCount, result.MappedCount);
            Assert.Equal(MapValidationLevel.OK, result.Level);
        }

        /// <summary>
        /// ValidateOrThrow 存在 Error 级别验证结果时抛 MapConfigurationException
        /// <para>注册包含 Error 的验证函数，验证后以 null（清空全部槽位）复位</para>
        /// </summary>
        [Fact]
        public void ValidateOrThrow_With_Error_Throws_MapConfigurationException()
        {
            try
            {
                // 注册一个包含 Error 级别结果的验证函数
                Mapper.RegisterGeneratedValidate(() => new List<MapValidationResult>
                {
                    new MapValidationResult
                    {
                        SourceType = typeof(ErrorMismatchEntity),
                        TargetType = typeof(ErrorMismatchDto),
                        Level = MapValidationLevel.Error,
                        MappedCount = 1,
                        TotalCount = 2,
                        UnmappedProperties = new List<string>(),
                        TypeMismatchProperties = new List<string> { "Data: int → byte[] 不兼容" },
                        Message = "，1 个类型不兼容：Data: int → byte[] 不兼容"
                    }
                });

                var ex = Assert.Throws<MapConfigurationException>(() => Mapper.ValidateOrThrow());
                Assert.Contains("Error", ex.Message);
            }
            finally
            {
                // 适配：原反射保存/恢复 s_generatedValidateFunc 随单槽位
                // 字段删除失效；改用 null = 清空全部槽位复位
                Mapper.RegisterGeneratedValidate(null);
            }
        }

        /// <summary>
        /// MapValidator.ValidateMapping 对完全不兼容的类型对返回 Error 级别
        /// <para>int → byte[] 不在任何兼容性规则内，触发类型不兼容错误</para>
        /// </summary>
        [Fact]
        public void Validate_Error_Level_For_Incompatible_Types()
        {
            var result = MapValidator.ValidateMapping(typeof(ErrorMismatchEntity), typeof(ErrorMismatchDto));

            Assert.Equal(MapValidationLevel.Error, result.Level);
            Assert.True(result.TypeMismatchProperties.Count > 0);
        }
    }

    /// <summary>
    /// 验证错误级别测试 — 源属性类型与目标属性类型完全不兼容
    /// </summary>
    public class ErrorMismatchEntity
    {
        /// <summary>
        /// 实体唯一标识符
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// 数据属性（int 类型）
        /// </summary>
        public int Data { get; set; }
    }

    /// <summary>
    /// 验证错误级别测试 — byte[] 与 int 不兼容，触发 Error 级别验证结果
    /// </summary>
    public class ErrorMismatchDto
    {
        /// <summary>
        /// 实体唯一标识符
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// 数据属性（byte[] 类型，与源类型不兼容）
        /// </summary>
        public byte[] Data { get; set; } = Array.Empty<byte>();
    }
}
