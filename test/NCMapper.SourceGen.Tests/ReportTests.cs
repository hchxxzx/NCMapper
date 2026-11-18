using Xunit;
using NCMapper.Generated;

namespace NCMapper.SourceGen.Tests
{
    /// <summary>
    /// T4: 映射关系可视化测试 — 验证源生成器在开启 NCMapperGenerateReport 时生成正确的报告
    /// <para>测试项目 csproj 已设置 &lt;NCMapperGenerateReport&gt;true&lt;/NCMapperGenerateReport&gt;</para>
    /// </summary>
    public class ReportTests
    {
        /// <summary>
        /// 开关开启时生成报告 — MappingReport.Report 不为空
        /// </summary>
        [Fact]
        public void Report_Generated_WhenEnabled()
        {
            Assert.False(string.IsNullOrEmpty(MappingReport.Report));
        }

        /// <summary>
        /// 报告包含所有 [MapFrom] 类型 — 至少包含 SgFlatEntity 映射对
        /// </summary>
        [Fact]
        public void Report_ContainsAllMapFromTypes()
        {
            var report = MappingReport.Report;

            // 测试项目中有多个 [MapFrom] 标注的 DTO，报告应包含它们
            Assert.Contains("NCMapper 映射关系报告", report);
            Assert.Contains("SgFlatEntity", report);
            Assert.Contains("SgFlatDto", report);
        }

        /// <summary>
        /// 同名同类型属性 — 映射方式 = "direct"
        /// </summary>
        [Fact]
        public void Report_DirectMapping_Shown()
        {
            var report = MappingReport.Report;

            // SgFlatEntity → SgFlatDto 中 Id/Name/Age 等都是同名直接映射
            Assert.Contains("direct", report);
        }

        /// <summary>
        /// [MapProperty] 异名 — 映射方式 = "[MapProperty] rename"
        /// </summary>
        [Fact]
        public void Report_MapPropertyRename_Shown()
        {
            var report = MappingReport.Report;

            // SgFlatEntity → SgNameDiffDto 中 Name→FullName, Age→UserAge 是异名映射
            Assert.Contains("[MapProperty] rename", report);
            Assert.Contains("FullName", report);
            Assert.Contains("UserAge", report);
        }

        /// <summary>
        /// [Convert] 属性 — 映射方式 = "[Convert]"
        /// </summary>
        [Fact]
        public void Report_ConvertUsing_Shown()
        {
            var report = MappingReport.Report;

            // SgConverterDto 有 4 个 [Convert] 标注的属性（CreateTime/Code/FullName/TotalPrice）
            // 报告中必须包含 "[Convert]" 映射方式标记和具体转换器类型名
            Assert.Contains("[Convert]", report);
            Assert.Contains("SgDateTimeToStringConverter", report);
            Assert.Contains("SgIntToPaddedStringConverter", report);
            Assert.Contains("SgFullNameCalculator", report);
        }

        /// <summary>
        /// 类型转换标注 — 隐式转换（int→long）在备注列标注
        /// </summary>
        [Fact]
        public void Report_TypeConversion_Shown()
        {
            var report = MappingReport.Report;

            // SgFlatEntity → SgNameDiffDto 中 Id(int) → AccountId(long) 是隐式转换
            // 报告备注列应包含 "int → long" 类型转换标注
            Assert.Contains("int → long", report);
        }

        /// <summary>
        /// 报告生成不影响编译 — 映射代码正常生成
        /// </summary>
        [Fact]
        public void Report_Compilation_Unaffected()
        {
            // 如果编译成功且此测试在运行，说明报告生成没有影响编译
            // 进一步验证映射代码正常工作
            var entity = new Models.SgFlatEntity { Id = 42, Name = "Report Test" };
            var dto = GeneratedMapper.Map_SgFlatEntity_To_SgFlatDto(entity)!;

            Assert.Equal(42, dto.Id);
            Assert.Equal("Report Test", dto.Name);

            // 报告内容也应有效
            Assert.NotEmpty(MappingReport.Report);
        }
    }
}
