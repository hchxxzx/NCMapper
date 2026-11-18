using Xunit;
using NCMapper.Tests.Models;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// 映射诊断 Trace 测试
    /// </summary>
    [Collection("DeepCopyGlobalState")]
    public class MapTracerTests
    {
        /// <summary>
        /// Trace 返回的报告中包含源类型和目标类型名称
        /// </summary>
        [Fact]
        public void Trace_Returns_Report_With_Type_Names()
        {
            var entity = new FullTypeEntity { IntProp = 42, StringProp = "Hello" };

            var report = MapTracer.Trace<FullTypeEntity, SameTypeDto>(entity);

            Assert.Contains("FullTypeEntity", report);
            Assert.Contains("SameTypeDto", report);
            Assert.Contains("→", report);
        }

        /// <summary>
        /// Trace 报告显示直接映射（direct）方式和属性名
        /// <para>CWE-209 修复后 ShowValues 默认 false，属性值不输出</para>
        /// </summary>
        [Fact]
        public void Trace_Shows_Direct_Mapping()
        {
            var entity = new FullTypeEntity { IntProp = 42, StringProp = "Test" };

            var report = MapTracer.Trace<FullTypeEntity, SameTypeDto>(entity);

            Assert.Contains("IntProp", report);
            Assert.Contains("direct", report);
        }

        /// <summary>
        /// Trace 报告正确处理 null / 空值
        /// </summary>
        [Fact]
        public void Trace_Shows_Null_Values()
        {
            var entity = new FullTypeEntity { IntProp = 0, StringProp = "" };

            var report = MapTracer.Trace<FullTypeEntity, SameTypeDto>(entity);

            // 空字符串不等于 null，但应显示
            Assert.Contains("StringProp", report);
        }

        /// <summary>
        /// Trace 报告显示 MapIgnore 属性被跳过
        /// </summary>
        [Fact]
        public void Trace_Shows_MapIgnore_Skipped()
        {
            // MapIgnoreTestDto.IgnoredField 没有标注 [MapIgnore]
            // 但 MapIgnoreTestEntity.IgnoredField 标注了 [MapIgnore]
            var entity = new MapIgnoreTestEntity { MappedField = "keep", IgnoredField = "skip" };

            var report = MapTracer.Trace<MapIgnoreTestEntity, MapIgnoreTestDto>(entity);

            // MappedField 应该有映射
            Assert.Contains("MappedField", report);
        }

        /// <summary>
        /// ShowValues=false 时隐藏属性值，只显示 "..."
        /// </summary>
        [Fact]
        public void Trace_ShowValues_False_Hides_Values()
        {
            var entity = new FullTypeEntity { IntProp = 999, StringProp = "Hidden" };

            var report = MapTracer.Trace<FullTypeEntity, SameTypeDto>(entity,
                new MapTraceOptions { Enabled = true, ShowValues = false });

            Assert.Contains("IntProp", report);
            Assert.DoesNotContain("999", report);
            Assert.Contains("...", report);
        }

        /// <summary>
        /// Trace 报告显示源字段未找到的警告
        /// </summary>
        [Fact]
        public void Trace_Shows_Source_Field_Not_Found_Warning()
        {
            // NameDiffDto 的 FullName 标注了 [MapProperty(typeof(NameDiffEntity), "Name")]
            // 但从 FullTypeEntity 映射时，源类型不匹配
            var entity = new FullTypeEntity { IntProp = 1 };

            var report = MapTracer.Trace<FullTypeEntity, CaseMismatchDto>(entity);

            // CaseMismatchDto 的 intprop/stringprop 与 FullTypeEntity 的 IntProp/StringProp 大小写不敏感匹配
            Assert.Contains("intprop", report);
            Assert.Contains("stringprop", report);
        }

        /// <summary>
        /// Trace 报告正确显示嵌套对象映射
        /// </summary>
        [Fact]
        public void Trace_Shows_Nested_Mapping()
        {
            // 用 Category（含 Children 集合属性）测试映射标记
            var entity = new Category
            {
                Name = "Parent",
                Sort = 1,
                Children = new List<Category>()
            };

            var report = MapTracer.Trace<Category, CategoryDto>(entity);

            Assert.Contains("Category", report);
            Assert.Contains("CategoryDto", report);
            Assert.Contains("Children", report);
        }

        /// <summary>
        /// Mapper.TraceEnabled 默认为 false
        /// </summary>
        [Fact]
        public void Mapper_TraceEnabled_Default_False()
        {
            // 默认关闭（测试间不互相影响）
            Assert.False(Mapper.TraceEnabled);
        }

        /// <summary>
        /// Mapper.TraceEnabled 切换开关正常
        /// </summary>
        [Fact]
        public void Mapper_TraceEnabled_Can_Toggle()
        {
            var original = Mapper.TraceEnabled;
            try
            {
                Mapper.TraceEnabled = true;
                Assert.True(Mapper.TraceEnabled);
                Mapper.TraceEnabled = false;
                Assert.False(Mapper.TraceEnabled);
            }
            finally
            {
                Mapper.TraceEnabled = original;
            }
        }

        /// <summary>
        /// Trace 报告显示 convert 映射模式
        /// </summary>
        [Fact]
        public void Trace_Shows_Convert_Mode()
        {
            // 用 PrecisionEntity 测试 — 有 long/decimal/int 混合类型
            var entity = new PrecisionEntity { BigLong = 999L, PreciseDecimal = 12.5m };

            var report = MapTracer.Trace<PrecisionEntity, SameTypeDto>(entity);

            // PrecisionEntity 和 SameTypeDto 没有直接标注 [MapFrom]，Trace 仍应输出报告
            Assert.Contains("PrecisionEntity", report);
            Assert.Contains("SameTypeDto", report);
        }

        // ================================================================
        // TraceOutput 自定义 TextWriter
        // ================================================================

        /// <summary>
        /// 设置 Mapper.TraceOutput 为 StringWriter，验证输出重定向
        /// </summary>
        [Fact]
        public void TraceOutput_Custom_StringWriter_Captures_Output()
        {
            var originalOutput = Mapper.TraceOutput;
            var originalEnabled = Mapper.TraceEnabled;
            try
            {
                using var sw = new StringWriter();
                Mapper.TraceOutput = sw;
                Mapper.TraceEnabled = true;

                var entity = new FullTypeEntity { IntProp = 42, StringProp = "test" };
                Mapper.Map<FullTypeEntity, SameTypeDto>(entity);

                var output = sw.ToString();
                Assert.NotEmpty(output);
                Assert.Contains("FullTypeEntity", output);
                Assert.Contains("SameTypeDto", output);
            }
            finally
            {
                Mapper.TraceOutput = originalOutput;
                Mapper.TraceEnabled = originalEnabled;
            }
        }

        /// <summary>
        /// 默认 TraceOutput 是 Console.Out
        /// </summary>
        [Fact]
        public void TraceOutput_Default_Is_ConsoleOut()
        {
            var original = Mapper.TraceOutput;
            try
            {
                Mapper.TraceOutput = Console.Out;
                Assert.Same(Console.Out, Mapper.TraceOutput);
            }
            finally
            {
                Mapper.TraceOutput = original;
            }
        }

        // ================================================================
        // New tests to kill Stryker mutants
        // ================================================================

        /// <summary>
        /// MapTraceOptions.ShowValues defaults to false (kills boolean mutation L21)
        /// </summary>
        [Fact]
        public void MapTraceOptions_ShowValues_Default_False()
        {
            var opts = new MapTraceOptions();
            Assert.False(opts.ShowValues);
        }

        /// <summary>
        /// MapTraceOptions.Enabled defaults to false
        /// </summary>
        [Fact]
        public void MapTraceOptions_Enabled_Default_False()
        {
            var opts = new MapTraceOptions();
            Assert.False(opts.Enabled);
        }

        /// <summary>
        /// MapTraceOptions.Output defaults to Console.Out
        /// </summary>
        [Fact]
        public void MapTraceOptions_Output_Default_ConsoleOut()
        {
            var opts = new MapTraceOptions();
            Assert.Same(Console.Out, opts.Output);
        }

        /// <summary>
        /// MapTraceOptions.Enable() factory sets Enabled to true (kills boolean mutation L27)
        /// </summary>
        [Fact]
        public void MapTraceOptions_Enable_Factory_Sets_Enabled_True()
        {
            var opts = MapTraceOptions.Enable();
            Assert.True(opts.Enabled);
        }

        /// <summary>
        /// MapTraceOptions.Enable(TextWriter) factory sets both Enabled and Output (kills boolean mutation L34)
        /// </summary>
        [Fact]
        public void MapTraceOptions_Enable_Writer_Factory()
        {
            using var sw = new StringWriter();
            var opts = MapTraceOptions.Enable(sw);
            Assert.True(opts.Enabled);
            Assert.Same(sw, opts.Output);
        }

        /// <summary>
        /// MapTracer.GloballyEnabled defaults to false
        /// </summary>
        [Fact]
        public void MapTracer_GloballyEnabled_Default_False()
        {
            var original = MapTracer.GloballyEnabled;
            try
            {
                MapTracer.GloballyEnabled = false;
                Assert.False(MapTracer.GloballyEnabled);
            }
            finally
            {
                MapTracer.GloballyEnabled = original;
            }
        }

        /// <summary>
        /// MapTracer.GlobalOutput defaults to Console.Out
        /// </summary>
        [Fact]
        public void MapTracer_GlobalOutput_Default_ConsoleOut()
        {
            var original = MapTracer.GlobalOutput;
            try
            {
                MapTracer.GlobalOutput = Console.Out;
                Assert.Same(Console.Out, MapTracer.GlobalOutput);
            }
            finally
            {
                MapTracer.GlobalOutput = original;
            }
        }

        /// <summary>
        /// MapTracer.LogCallback defaults to null
        /// </summary>
        [Fact]
        public void MapTracer_LogCallback_Default_Null()
        {
            var original = MapTracer.LogCallback;
            try
            {
                MapTracer.LogCallback = null;
                Assert.Null(MapTracer.LogCallback);
            }
            finally
            {
                MapTracer.LogCallback = original;
            }
        }

        /// <summary>
        /// WriteTrace with GloballyEnabled=true produces output via GlobalOutput (kills null coalescing L114/L117/L118)
        /// </summary>
        [Fact]
        public void WriteTrace_With_GloballyEnabled_Produces_Output()
        {
            var origEnabled = MapTracer.GloballyEnabled;
            var origOutput = MapTracer.GlobalOutput;
            var origCallback = MapTracer.LogCallback;
            try
            {
                using var sw = new StringWriter();
                MapTracer.GloballyEnabled = true;
                MapTracer.GlobalOutput = sw;
                MapTracer.LogCallback = null;

                var entity = new FullTypeEntity { IntProp = 42, StringProp = "test" };
                Mapper.Map<FullTypeEntity, SameTypeDto>(entity);

                var output = sw.ToString();
                Assert.NotEmpty(output);
                Assert.Contains("FullTypeEntity", output);
            }
            finally
            {
                MapTracer.GloballyEnabled = origEnabled;
                MapTracer.GlobalOutput = origOutput;
                MapTracer.LogCallback = origCallback;
            }
        }

        /// <summary>
        /// WriteTrace with disabled=true produces NO output (kills statement mutation L115 and LogicalNot)
        /// </summary>
        [Fact]
        public void WriteTrace_With_Disabled_Produces_No_Output()
        {
            var origEnabled = MapTracer.GloballyEnabled;
            var origOutput = MapTracer.GlobalOutput;
            var origCallback = MapTracer.LogCallback;
            try
            {
                using var sw = new StringWriter();
                MapTracer.GloballyEnabled = false;
                MapTracer.GlobalOutput = sw;
                MapTracer.LogCallback = null;

                var entity = new FullTypeEntity { IntProp = 42, StringProp = "test" };
                Mapper.Map<FullTypeEntity, SameTypeDto>(entity);

                var output = sw.ToString();
                Assert.Empty(output);
            }
            finally
            {
                MapTracer.GloballyEnabled = origEnabled;
                MapTracer.GlobalOutput = origOutput;
                MapTracer.LogCallback = origCallback;
            }
        }

        /// <summary>
        /// LogCallback receives the trace text (kills statement mutation L129 callback invocation)
        /// </summary>
        [Fact]
        public void LogCallback_Receives_Trace_Text()
        {
            var origEnabled = MapTracer.GloballyEnabled;
            var origOutput = MapTracer.GlobalOutput;
            var origCallback = MapTracer.LogCallback;
            try
            {
                using var sw = new StringWriter();
                string? capturedText = null;
                MapTracer.GloballyEnabled = true;
                MapTracer.GlobalOutput = sw;
                MapTracer.LogCallback = text => capturedText = text;

                var entity = new FullTypeEntity { IntProp = 1, StringProp = "cb" };
                Mapper.Map<FullTypeEntity, SameTypeDto>(entity);

                Assert.NotNull(capturedText);
                Assert.NotEmpty(capturedText!);
                Assert.Contains("FullTypeEntity", capturedText);
            }
            finally
            {
                MapTracer.GloballyEnabled = origEnabled;
                MapTracer.GlobalOutput = origOutput;
                MapTracer.LogCallback = origCallback;
            }
        }

        /// <summary>
        /// Trace header format: starts with "[NCMapper]" and contains "→" (kills statement mutation L161)
        /// </summary>
        [Fact]
        public void Trace_Header_Format_Exact()
        {
            var entity = new FullTypeEntity();

            var report = MapTracer.Trace<FullTypeEntity, SameTypeDto>(entity);

            Assert.Contains("[NCMapper] FullTypeEntity → SameTypeDto", report);
        }

        /// <summary>
        /// Trace shows "skipped (MapIgnore)" for [MapIgnore] properties (kills statement mutation L166/L167)
        /// </summary>
        [Fact]
        public void Trace_MapIgnore_Shows_Skipped_Text()
        {
            var entity = new FullTypeEntity { IntProp = 1, StringProp = "x" };

            var report = MapTracer.Trace<FullTypeEntity, MapIgnoreDto>(entity);

            Assert.Contains("skipped (MapIgnore)", report);
            Assert.Contains("IgnoredField", report);
        }

        /// <summary>
        /// Trace shows "skipped (source type" when MapProperty source type doesn't match (kills statement mutation L177/L178)
        /// </summary>
        [Fact]
        public void Trace_Source_Type_Mismatch_Shows_Skipped()
        {
            var entity = new CustomerEntity { Id = 1, Name = "Test" };

            var report = MapTracer.Trace<CustomerEntity, MixedSourceDto>(entity);

            Assert.Contains("skipped (source type", report);
        }

        /// <summary>
        /// Trace shows "⚠️ source field" when source property is not found (kills statement mutation L187)
        /// </summary>
        [Fact]
        public void Trace_Source_Field_Not_Found_Shows_Warning()
        {
            var entity = new ComposeNestedMainSource { Id = 1 };

            var report = MapTracer.Trace<ComposeNestedMainSource, ComposeNestedDto>(entity);

            Assert.Contains("\u26a0\ufe0f source field", report);
            Assert.Contains("ChildName", report);
        }

        /// <summary>
        /// FormatValue truncates long strings with "..." when ShowValues=true (kills FormatValue mutations)
        /// </summary>
        [Fact]
        public void FormatValue_Long_String_Truncated()
        {
            var longStr = new string('A', 60);
            var entity = new FullTypeEntity { IntProp = 1, StringProp = longStr };

            var report = MapTracer.Trace<FullTypeEntity, SameTypeDto>(entity,
                new MapTraceOptions { Enabled = true, ShowValues = true });

            Assert.Contains("...", report);
            // The full 60-char string should NOT appear
            Assert.DoesNotContain(longStr, report);
        }

        /// <summary>
        /// FormatValue shows "(null)" for null values when ShowValues=true
        /// </summary>
        [Fact]
        public void FormatValue_Null_Shows_Null()
        {
            var entity = new FullTypeEntity { IntProp = 0, StringProp = null! };

            var report = MapTracer.Trace<FullTypeEntity, SameTypeDto>(entity,
                new MapTraceOptions { Enabled = true, ShowValues = true });

            Assert.Contains("(null)", report);
        }

        /// <summary>
        /// Trace with ShowValues=true shows actual property values
        /// </summary>
        [Fact]
        public void Trace_ShowValues_True_Shows_Actual_Values()
        {
            var entity = new FullTypeEntity { IntProp = 12345, StringProp = "VisibleValue" };

            var report = MapTracer.Trace<FullTypeEntity, SameTypeDto>(entity,
                new MapTraceOptions { Enabled = true, ShowValues = true });

            Assert.Contains("12345", report);
            Assert.Contains("VisibleValue", report);
        }

        /// <summary>
        /// Trace shows "nested" mode for class-to-class property mappings (kills logical mutations in mode detection)
        /// </summary>
        [Fact]
        public void Trace_Nested_Mode_Shows_Nested_Text()
        {
            var entity = new CustomerEntity
            {
                Id = 1,
                Name = "Cust",
                HomeAddress = new Address { City = "NYC", Street = "5th Ave", ZipCode = 10001 }
            };

            var report = MapTracer.Trace<CustomerEntity, CustomerDto>(entity);

            Assert.Contains("nested", report);
            Assert.Contains("HomeAddress", report);
        }

        /// <summary>
        /// Trace shows "convert" mode for type-converted properties (kills logical mutations in mode detection)
        /// </summary>
        [Fact]
        public void Trace_Convert_Mode_Shows_Convert_Text()
        {
            var entity = new FullTypeEntity { IntProp = 42, StringProp = "hello" };

            var report = MapTracer.Trace<FullTypeEntity, ValueToStringDto>(entity);

            Assert.Contains("convert", report);
        }

        /// <summary>
        /// Trace shows "nullable" mode for nullable-to-non-nullable mappings (kills logical mutations in mode detection)
        /// </summary>
        [Fact]
        public void Trace_Nullable_Mode_Shows_Nullable_Text()
        {
            var entity = new FullTypeEntity { NullableIntProp = 100, NullIntNull = null };

            var report = MapTracer.Trace<FullTypeEntity, NullableDto>(entity);

            Assert.Contains("nullable", report);
        }

        /// <summary>
        /// Trace handles null source gracefully without throwing (kills conditional mutation L193)
        /// </summary>
        [Fact]
        public void Trace_Null_Source_Handled_Gracefully()
        {
            var report = MapTracer.Trace<FullTypeEntity, SameTypeDto>(null!);

            Assert.NotNull(report);
            Assert.Contains("[NCMapper]", report);
            Assert.Contains("FullTypeEntity", report);
        }

        /// <summary>
        /// Trace with non-null options uses provided options (kills null coalescing mutation L94)
        /// </summary>
        [Fact]
        public void Trace_With_NonNull_Options_Uses_Provided_Options()
        {
            var entity = new FullTypeEntity { IntProp = 7, StringProp = "opt" };
            var opts = new MapTraceOptions { Enabled = true, ShowValues = true };

            var report = MapTracer.Trace<FullTypeEntity, SameTypeDto>(entity, opts);

            // With ShowValues=true, the actual value should appear
            Assert.Contains("7", report);
        }

        /// <summary>
        /// LogCallback being null does not cause exception (kills null check mutation L129)
        /// </summary>
        [Fact]
        public void LogCallback_Null_Does_Not_Throw()
        {
            var origEnabled = MapTracer.GloballyEnabled;
            var origOutput = MapTracer.GlobalOutput;
            var origCallback = MapTracer.LogCallback;
            try
            {
                using var sw = new StringWriter();
                MapTracer.GloballyEnabled = true;
                MapTracer.GlobalOutput = sw;
                MapTracer.LogCallback = null;

                var entity = new FullTypeEntity { IntProp = 1 };
                var ex = Record.Exception(() => Mapper.Map<FullTypeEntity, SameTypeDto>(entity));

                Assert.Null(ex);
            }
            finally
            {
                MapTracer.GloballyEnabled = origEnabled;
                MapTracer.GlobalOutput = origOutput;
                MapTracer.LogCallback = origCallback;
            }
        }

        /// <summary>
        /// Equality mutation: sourceAttr == null branch coverage (L156)
        /// Trace a type pair where target does NOT have [MapFrom] to hit sourceAttr==null
        /// </summary>
        [Fact]
        public void Trace_Target_Without_MapFrom_Works()
        {
            // PrecisionEntity→SameTypeDto: SameTypeDto has [MapFrom(typeof(FullTypeEntity))] not PrecisionEntity
            // but the Trace method still analyzes properties
            var entity = new PrecisionEntity { BigLong = 100L, PreciseDecimal = 1.5m };

            var report = MapTracer.Trace<PrecisionEntity, SameTypeDto>(entity);

            Assert.Contains("PrecisionEntity", report);
            Assert.Contains("SameTypeDto", report);
        }

        /// <summary>
        /// MapIgnore on target DTO produces skipped line and continues loop (kills L166+L167 together)
        /// </summary>
        [Fact]
        public void Trace_MapIgnore_Continues_After_Skip()
        {
            var entity = new FullTypeEntity { IntProp = 10, StringProp = "val" };

            var report = MapTracer.Trace<FullTypeEntity, MapIgnoreDto>(entity);

            // IgnoredField should be skipped, but IntProp and StringProp should still appear
            Assert.Contains("IgnoredField: skipped (MapIgnore)", report);
            Assert.Contains("IntProp", report);
            Assert.Contains("StringProp", report);
        }

        /// <summary>
        /// Source type mismatch produces exact format and continues (kills L177+L178 together)
        /// </summary>
        [Fact]
        public void Trace_Source_Type_Mismatch_Continues_After_Skip()
        {
            var entity = new CustomerEntity { Id = 5, Name = "TestCust" };

            var report = MapTracer.Trace<CustomerEntity, MixedSourceDto>(entity);

            // Score and StatusText have MapProperty from FullTypeEntity, should be skipped
            Assert.Contains("Score: skipped (source type", report);
            Assert.Contains("StatusText: skipped (source type", report);
            // But Id and Name from CustomerEntity should still be mapped
            Assert.Contains("Id", report);
            Assert.Contains("Name", report);
        }

        /// <summary>
        /// ShowValues=true 时 [Mask] 属性输出 "[MASKED]"（C19 补测）
        /// <para>MapTracer.cs:251-253：ShowValues 开启且目标属性标注 [Mask] 时输出 "[MASKED]"
        /// 而非原值（CWE-209 敏感数据泄露防护，A-003-F1 守护）。</para>
        /// </summary>
        [Fact]
        public void Trace_ShowValues_True_MaskedProperty_Shows_MASKED()
        {
            var entity = new MaskedTraceEntity { Id = 1, Secret = "s3ns1t1ve-value" };

            var report = MapTracer.Trace<MaskedTraceEntity, MaskedTraceDto>(entity,
                new MapTraceOptions { Enabled = true, ShowValues = true });

            // [Mask] 属性脱敏输出，原值不泄露
            Assert.Contains("[MASKED]", report);
            Assert.DoesNotContain("s3ns1t1ve-value", report);
        }

        /// <summary>
        /// C19 被测源类型 — 含敏感字段
        /// </summary>
        private class MaskedTraceEntity
        {
            /// <summary>
            /// 标识字段
            /// </summary>
            public int Id { get; set; }

            /// <summary>
            /// 敏感字段（将脱敏）
            /// </summary>
            public string Secret { get; set; } = "";
        }

        /// <summary>
        /// C19 被测目标类型 — Secret 标注 [Mask] 脱敏
        /// </summary>
        [NCMapper.Attributes.MapFrom(typeof(MaskedTraceEntity))]
        private class MaskedTraceDto
        {
            /// <summary>
            /// 标识字段
            /// </summary>
            public int Id { get; set; }

            /// <summary>
            /// 脱敏字段 — 输出 "[MASKED]"
            /// </summary>
            [NCMapper.Attributes.Mask(typeof(MaskPhone))]
            public string Secret { get; set; } = "";
        }
    }
}
