using NCMapper.Attributes;
using NCMapper.Tests.Models;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// Mutant-killing tests for MapValidator.cs and MapTracer.cs.
    /// Uses IDisposable to save/restore global state (MapTracer.GloballyEnabled, LogCallback, GlobalOutput).
    /// </summary>
    [Collection("DeepCopyGlobalState")]
    public class MapValidatorTracerMutantTests : IDisposable
    {
        // Save original global state
        private readonly bool _origGloballyEnabled;
        private readonly TextWriter? _origGlobalOutput;
        private readonly Action<string>? _origLogCallback;

        public MapValidatorTracerMutantTests()
        {
            _origGloballyEnabled = MapTracer.GloballyEnabled;
            _origGlobalOutput = MapTracer.GlobalOutput;
            _origLogCallback = MapTracer.LogCallback;
            Mapper.ClearCache();
        }

        public void Dispose()
        {
            MapTracer.GloballyEnabled = _origGloballyEnabled;
            MapTracer.GlobalOutput = _origGlobalOutput!;
            MapTracer.LogCallback = _origLogCallback;
            Mapper.ClearCache();
        }

        // ================================================================
        // MapValidator: PrintReport validation level checks (L261-263, L265, L269, L273)
        // r.Level != MapValidationLevel.OK → ==, r.Level != MapValidationLevel.Warn → ==
        // ================================================================

        /// <summary>
        /// PrintReport with showOk=false → only Warn/Error shown.
        /// Kills L269: !showOk && r.Level == MapValidationLevel.OK → continue.
        /// If mutated to showOk==false without the !showOk check, OK entries would appear.
        /// </summary>
        [Fact]
        public void PrintReport_ShowOkFalse_OnlyWarnErrorShown()
        {
            var results = new List<MapValidationResult>
            {
                new() { SourceType = typeof(FullTypeEntity), TargetType = typeof(SameTypeDto),
                    Level = MapValidationLevel.OK, MappedCount = 5, TotalCount = 5, Message = "" },
                new() { SourceType = typeof(FullTypeEntity), TargetType = typeof(ValueToStringDto),
                    Level = MapValidationLevel.Warn, MappedCount = 3, TotalCount = 5,
                    UnmappedProperties = new List<string> { "DecimalProp", "FloatProp" },
                    Message = ", 2 unmapped: DecimalProp, FloatProp" },
                new() { SourceType = typeof(ErrorMismatchEntity), TargetType = typeof(ErrorMismatchDto),
                    Level = MapValidationLevel.Error, MappedCount = 1, TotalCount = 2,
                    TypeMismatchProperties = new List<string> { "Data: int → byte[] incompatible" },
                    Message = ", 1 type mismatch" }
            };

            var writer = new StringWriter();
            MapValidator.PrintReport(results, writer, showOk: false);
            var output = writer.ToString();

            // OK entry should NOT appear (SameTypeDto should not be in detail lines)
            // But it can appear in the summary line
            var lines = output.Split('\n');
            var detailLines = lines.Where(l => l.TrimStart().StartsWith("✅") || l.TrimStart().StartsWith("⚠️") || l.TrimStart().StartsWith("❌")).ToArray();

            // Only Warn and Error detail lines should appear
            Assert.Equal(2, detailLines.Length);
            Assert.Contains("ValueToStringDto", output);
            Assert.Contains("ErrorMismatchDto", output);
        }

        /// <summary>
        /// PrintReport with showOk=true → all results shown including OK.
        /// Kills L269: if showOk==true, the !showOk check is false, OK entries appear.
        /// </summary>
        [Fact]
        public void PrintReport_ShowOkTrue_AllResultsShown()
        {
            var results = new List<MapValidationResult>
            {
                new() { SourceType = typeof(FullTypeEntity), TargetType = typeof(SameTypeDto),
                    Level = MapValidationLevel.OK, MappedCount = 5, TotalCount = 5, Message = "" },
                new() { SourceType = typeof(FullTypeEntity), TargetType = typeof(ValueToStringDto),
                    Level = MapValidationLevel.Warn, MappedCount = 3, TotalCount = 5,
                    Message = ", 2 unmapped" }
            };

            var writer = new StringWriter();
            MapValidator.PrintReport(results, writer, showOk: true);
            var output = writer.ToString();

            // Both OK and Warn should appear in detail lines
            Assert.Contains("SameTypeDto", output);
            Assert.Contains("ValueToStringDto", output);
        }

        /// <summary>
        /// PrintReport shows correct counts for each level in summary line.
        /// Kills L261-263: Count predicates for OK, Warn, Error.
        /// </summary>
        [Fact]
        public void PrintReport_CorrectCountsForEachLevel()
        {
            // 验证消息双语化后本断言锁定 zh 形态，固定 zh-CN 文化
            //（B2 特例登记：断言文本零改动，仅消除新引入的文化依赖；先例 ErrorMessagesLocalizationTests 模式）
            var originalCurrent = System.Globalization.CultureInfo.CurrentCulture;
            var originalUi = System.Globalization.CultureInfo.CurrentUICulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("zh-CN");
                System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("zh-CN");

                var results = new List<MapValidationResult>
                {
                    new() { SourceType = typeof(FullTypeEntity), TargetType = typeof(SameTypeDto),
                        Level = MapValidationLevel.OK, MappedCount = 5, TotalCount = 5, Message = "" },
                    new() { SourceType = typeof(FullTypeEntity), TargetType = typeof(SameTypeDto),
                        Level = MapValidationLevel.OK, MappedCount = 3, TotalCount = 3, Message = "" },
                    new() { SourceType = typeof(FullTypeEntity), TargetType = typeof(ValueToStringDto),
                        Level = MapValidationLevel.Warn, MappedCount = 3, TotalCount = 5, Message = "" },
                    new() { SourceType = typeof(ErrorMismatchEntity), TargetType = typeof(ErrorMismatchDto),
                        Level = MapValidationLevel.Error, MappedCount = 1, TotalCount = 2, Message = "" }
                };

                var writer = new StringWriter();
                MapValidator.PrintReport(results, writer, showOk: true);
                var output = writer.ToString();

                // Summary should contain counts: 2 OK, 1 Warn, 1 Error
                Assert.Contains("总计 4 个映射对", output);
                Assert.Contains("2 OK", output);
                Assert.Contains("1 警告", output);
                Assert.Contains("1 错误", output);
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = originalCurrent;
                System.Globalization.CultureInfo.CurrentUICulture = originalUi;
            }
        }

        /// <summary>
        /// PrintReport header and footer lines present.
        /// Kills L260/L273: WriteLine header/footer mutations.
        /// </summary>
        [Fact]
        public void PrintReport_HeaderAndFooterPresent()
        {
            var results = new List<MapValidationResult>();
            var writer = new StringWriter();
            MapValidator.PrintReport(results, writer, showOk: false);
            var output = writer.ToString();

            Assert.Contains("NCMapper", output);
            Assert.Contains("========", output);
        }

        // ================================================================
        // MapValidator: IsTypeCompatible Guid/string (L324-325)
        // ================================================================

        /// <summary>
        /// Validate mapping Guid source → string target → compatible.
        /// Kills L324: sourceType == typeof(Guid) && targetType == typeof(string).
        /// </summary>
        [Fact]
        public void ValidateMapping_Guid_To_String_Compatible()
        {
            var result = MapValidator.ValidateMapping(typeof(ValidatorGuidSource), typeof(ValidatorGuidToStringDto));
            // Guid → string should be compatible via L324
            Assert.True(result.MappedCount > 0);
            Assert.DoesNotContain(result.TypeMismatchProperties, p => p.Contains("GuidProp"));
        }

        /// <summary>
        /// Validate mapping string source → Guid target → compatible.
        /// Kills L325: sourceType == typeof(string) && targetType == typeof(Guid).
        /// </summary>
        [Fact]
        public void ValidateMapping_String_To_Guid_Compatible()
        {
            var result = MapValidator.ValidateMapping(typeof(ValidatorStringSource), typeof(ValidatorStringToGuidDto));
            // string → Guid should be compatible via L325
            Assert.True(result.MappedCount > 0);
            Assert.DoesNotContain(result.TypeMismatchProperties, p => p.Contains("GuidProp"));
        }

        /// <summary>
        /// Validate mapping Guid → int → NOT compatible (unless there's another rule).
        /// Verifies that the Guid↔string rule doesn't accidentally allow Guid↔anything.
        /// </summary>
        [Fact]
        public void ValidateMapping_Guid_To_Int_Not_Compatible()
        {
            var result = MapValidator.ValidateMapping(typeof(ValidatorGuidSource), typeof(ValidatorGuidToIntDto));
            // Guid → int is not in any compatibility rule → should be incompatible
            Assert.True(result.TypeMismatchProperties.Count > 0 || result.UnmappedProperties.Count > 0);
        }

        // ================================================================
        // MapValidator: Validate assemblies parameter (L146)
        // ================================================================

        /// <summary>
        /// Validate with specific assembly → only scans that assembly.
        /// Kills L146: assemblies.Length > 0 ? assemblies : AppDomain...
        /// </summary>
        [Fact]
        public void Validate_WithSpecificAssembly_OnlyScansThat()
        {
            var asm = typeof(FullTypeEntity).Assembly;
            var results = MapValidator.Validate(asm);

            Assert.NotEmpty(results);
            // Should contain at least one mapping from this assembly
            Assert.Contains(results, r => r.TargetType.Assembly == asm);
        }

        /// <summary>
        /// Validate with no args → scans all AppDomain assemblies. Doesn't throw.
        /// Kills: the ternary's else branch (AppDomain.CurrentDomain.GetAssemblies()).
        /// </summary>
        [Fact]
        public void Validate_NoArgs_ScansAllAssemblies()
        {
            // Just verify it doesn't throw
            var ex = Record.Exception(() => MapValidator.Validate());
            Assert.Null(ex);
        }

        // ================================================================
        // MapValidator: !type.IsClass || type.IsAbstract (L110)
        // ================================================================

        /// <summary>
        /// Abstract classes and interfaces are skipped during assembly scanning.
        /// Kills L110: !type.IsClass || type.IsAbstract → continue.
        /// </summary>
        [Fact]
        public void Validate_Skips_AbstractClasses_And_Interfaces()
        {
            var asm = typeof(AbstractTargetBase).Assembly;
            var results = MapValidator.Validate(asm);

            // AbstractTargetBase is abstract → should not appear as target
            Assert.DoesNotContain(results, r => r.TargetType == typeof(AbstractTargetBase));
            // ITargetInterface is an interface → should not appear as target
            Assert.DoesNotContain(results, r => r.TargetType == typeof(ITargetInterface));
        }

        // ================================================================
        // MapTracer: GloballyEnabled / GlobalOutput null coalescing (L114-118)
        // ================================================================

        /// <summary>
        /// Set GloballyEnabled=true, LogCallback captures output via Mapper.Map.
        /// Kills L114: options?.Enabled ?? GloballyEnabled.
        /// </summary>
        [Fact]
        public void GloballyEnabled_True_LogCallbackInvoked()
        {
            using var sw = new StringWriter();
            string? capturedText = null;

            MapTracer.GloballyEnabled = true;
            MapTracer.GlobalOutput = sw;
            MapTracer.LogCallback = text => capturedText = text;

            var entity = new FullTypeEntity { IntProp = 42, StringProp = "trace-test" };
            Mapper.Map<FullTypeEntity, SameTypeDto>(entity);

            Assert.NotNull(capturedText);
            Assert.NotEmpty(capturedText!);
            Assert.Contains("FullTypeEntity", capturedText);
        }

        /// <summary>
        /// GloballyEnabled=false → WriteTrace returns early, LogCallback NOT invoked.
        /// Kills L115: if (!enabled) return.
        /// </summary>
        [Fact]
        public void GloballyEnabled_False_LogCallbackNotInvoked()
        {
            using var sw = new StringWriter();
            string? capturedText = null;

            MapTracer.GloballyEnabled = false;
            MapTracer.GlobalOutput = sw;
            MapTracer.LogCallback = text => capturedText = text;

            var entity = new FullTypeEntity { IntProp = 1, StringProp = "no-trace" };
            Mapper.Map<FullTypeEntity, SameTypeDto>(entity);

            Assert.Null(capturedText);
            Assert.Empty(sw.ToString());
        }

        /// <summary>
        /// GlobalOutput used when options is null.
        /// Kills L117: options?.Output ?? GlobalOutput.
        /// </summary>
        [Fact]
        public void GlobalOutput_UsedWhenOptionsNull()
        {
            using var sw = new StringWriter();
            MapTracer.GloballyEnabled = true;
            MapTracer.GlobalOutput = sw;
            MapTracer.LogCallback = null;

            var entity = new FullTypeEntity { IntProp = 5, StringProp = "output" };
            Mapper.Map<FullTypeEntity, SameTypeDto>(entity);

            var output = sw.ToString();
            Assert.NotEmpty(output);
            Assert.Contains("FullTypeEntity", output);
        }

        // ================================================================
        // MapTracer: WriteTrace with options (L88-100)
        // ================================================================

        /// <summary>
        /// MapTracer.Trace returns a non-empty string containing property names.
        /// Kills L88-100: Trace method body.
        /// </summary>
        [Fact]
        public void Trace_ReturnsString_WithPropertyNames()
        {
            var entity = new FullTypeEntity { IntProp = 99, StringProp = "tracer" };
            var report = MapTracer.Trace<FullTypeEntity, SameTypeDto>(entity);

            Assert.NotNull(report);
            Assert.NotEmpty(report);
            Assert.Contains("IntProp", report);
            Assert.Contains("StringProp", report);
        }

        /// <summary>
        /// Trace with null source doesn't throw.
        /// Kills L94: new MapTraceOptions { Enabled = true, Output = sw } default initializer.
        /// </summary>
        [Fact]
        public void Trace_NullSource_DoesNotThrow()
        {
            var ex = Record.Exception(() => MapTracer.Trace<FullTypeEntity, SameTypeDto>(null!));
            Assert.Null(ex);
        }

        // ================================================================
        // MapTracer: Object initializer mutation (L94)
        // ================================================================

        /// <summary>
        /// MapTraceOptions.Enable() sets Enabled=true and Output is not null.
        /// Kills L27: new MapTraceOptions { Enabled = true }.
        /// </summary>
        [Fact]
        public void MapTraceOptions_Enable_SetsEnabledTrue_OutputNotNull()
        {
            var opts = MapTraceOptions.Enable();
            Assert.True(opts.Enabled);
            Assert.NotNull(opts.Output);
        }

        /// <summary>
        /// MapTraceOptions.Enable(TextWriter) sets both Enabled and Output.
        /// Kills L34: new MapTraceOptions { Enabled = true, Output = writer }.
        /// </summary>
        [Fact]
        public void MapTraceOptions_EnableWriter_SetsBoth()
        {
            using var sw = new StringWriter();
            var opts = MapTraceOptions.Enable(sw);
            Assert.True(opts.Enabled);
            Assert.Same(sw, opts.Output);
        }

        /// <summary>
        /// Default MapTraceOptions has Enabled=false, Output=Console.Out, ShowValues=false.
        /// Kills default value mutations.
        /// </summary>
        [Fact]
        public void MapTraceOptions_Defaults()
        {
            var opts = new MapTraceOptions();
            Assert.False(opts.Enabled);
            Assert.Same(Console.Out, opts.Output);
            Assert.False(opts.ShowValues);
        }

        // ================================================================
        // MapTracer: IsNullable / FormatValue (L207-209, L248)
        // ================================================================

        /// <summary>
        /// Trace with nullable source property → verify "(null)" appears when ShowValues=true.
        /// Kills L207-209: IsNullable checks in mode detection.
        /// </summary>
        [Fact]
        public void Trace_NullableProperty_NullValue_ShowsNull()
        {
            var entity = new FullTypeEntity
            {
                IntProp = 0,
                StringProp = "x",
                NullableIntProp = 100,
                NullIntNull = null
            };

            var report = MapTracer.Trace<FullTypeEntity, NullableDto>(entity,
                new MapTraceOptions { Enabled = true, ShowValues = true });

            // NullIntNull is null → FormatValue returns "(null)"
            Assert.Contains("(null)", report);
        }

        /// <summary>
        /// Trace with long string value → verify truncation with "...".
        /// Kills L248: s.Length > MaxValueDisplayLength ? ... : ...
        /// </summary>
        [Fact]
        public void Trace_LongStringValue_Truncated()
        {
            var longStr = new string('X', 100);
            var entity = new FullTypeEntity { IntProp = 1, StringProp = longStr };

            var report = MapTracer.Trace<FullTypeEntity, SameTypeDto>(entity,
                new MapTraceOptions { Enabled = true, ShowValues = true });

            Assert.Contains("...", report);
            // The full 100-char string should NOT appear in the output
            Assert.DoesNotContain(longStr, report);
        }

        // ================================================================
        // MapTracer: MaxValueDisplayLength (L248)
        // FormatValue truncates strings longer than MaxValueDisplayLength
        // ================================================================

        /// <summary>
        /// String exactly at MaxValueDisplayLength (50) → NOT truncated.
        /// Kills L248: s.Length > MaxValueDisplayLength boundary check.
        /// </summary>
        [Fact]
        public void Trace_StringAtExactLimit_NotTruncated()
        {
            // MaxValueDisplayLength is 50
            var exactStr = new string('Y', 50);
            var entity = new FullTypeEntity { IntProp = 1, StringProp = exactStr };

            var report = MapTracer.Trace<FullTypeEntity, SameTypeDto>(entity,
                new MapTraceOptions { Enabled = true, ShowValues = true });

            // Exactly 50 chars → NOT truncated
            Assert.Contains(exactStr, report);
            Assert.DoesNotContain("...", report);
        }

        /// <summary>
        /// String at MaxValueDisplayLength + 1 (51) → IS truncated.
        /// Kills L248: boundary condition.
        /// </summary>
        [Fact]
        public void Trace_StringJustOverLimit_Truncated()
        {
            var overStr = new string('Z', 51);
            var entity = new FullTypeEntity { IntProp = 1, StringProp = overStr };

            var report = MapTracer.Trace<FullTypeEntity, SameTypeDto>(entity,
                new MapTraceOptions { Enabled = true, ShowValues = true });

            Assert.Contains("...", report);
            Assert.DoesNotContain(overStr, report);
        }

        /// <summary>
        /// FormatValue for null value returns "(null)".
        /// Kills L247: if (value == null) return "(null)".
        /// </summary>
        [Fact]
        public void Trace_NullStringValue_ShowsNull()
        {
            var entity = new FullTypeEntity { IntProp = 0, StringProp = null! };

            var report = MapTracer.Trace<FullTypeEntity, SameTypeDto>(entity,
                new MapTraceOptions { Enabled = true, ShowValues = true });

            Assert.Contains("(null)", report);
        }

        /// <summary>
        /// FormatValue for non-string, non-null value returns ToString().
        /// Kills L249: return value.ToString() ?? "(null)".
        /// </summary>
        [Fact]
        public void Trace_IntValue_ShowsNumber()
        {
            var entity = new FullTypeEntity { IntProp = 54321, StringProp = "x" };

            var report = MapTracer.Trace<FullTypeEntity, SameTypeDto>(entity,
                new MapTraceOptions { Enabled = true, ShowValues = true });

            Assert.Contains("54321", report);
        }

        // ================================================================
        // MapTracer: WriteTrace callback integration (L121-130)
        // ================================================================

        /// <summary>
        /// WriteTrace with StringWriter output invokes LogCallback with trace text content.
        /// Kills L125-126: if (output is StringWriter sw) traceText = sw.ToString().
        /// </summary>
        [Fact]
        public void WriteTrace_StringWriterOutput_CallbackGetsTraceText()
        {
            using var sw = new StringWriter();
            string? capturedText = null;

            MapTracer.GloballyEnabled = true;
            MapTracer.GlobalOutput = sw;
            MapTracer.LogCallback = text => capturedText = text;

            var entity = new FullTypeEntity { IntProp = 7, StringProp = "callback-test" };
            Mapper.Map<FullTypeEntity, SameTypeDto>(entity);

            Assert.NotNull(capturedText);
            // When output is StringWriter, callback gets the actual trace content
            Assert.Contains("FullTypeEntity", capturedText!);
            Assert.Contains("SameTypeDto", capturedText);
        }

        /// <summary>
        /// WriteTrace with non-StringWriter output invokes LogCallback with fallback message.
        /// Kills L128: traceText = $"[NCMapper] ... (trace written to {output.GetType().Name})".
        /// </summary>
        [Fact]
        public void WriteTrace_NonStringWriterOutput_CallbackGetsFallbackMessage()
        {
            string? capturedText = null;
            var customWriter = new CustomTextWriter();

            MapTracer.GloballyEnabled = true;
            MapTracer.GlobalOutput = customWriter;
            MapTracer.LogCallback = text => capturedText = text;

            var entity = new FullTypeEntity { IntProp = 3, StringProp = "custom" };
            Mapper.Map<FullTypeEntity, SameTypeDto>(entity);

            Assert.NotNull(capturedText);
            // When output is NOT StringWriter, callback gets fallback message
            Assert.Contains("[NCMapper]", capturedText!);
            Assert.Contains("FullTypeEntity", capturedText);
        }

        // ================================================================
        // MapTracer: mode detection branches
        // ================================================================

        /// <summary>
        /// Trace shows "json" mode for IsFromJson properties.
        /// Kills L203-204: isFromJson check.
        /// </summary>
        [Fact]
        public void Trace_JsonMode_ShowsJson()
        {
            var entity = new JsonFieldEntity { Id = 1, ConfigJson = "{}" };

            var report = MapTracer.Trace<JsonFieldEntity, JsonToObjectDto>(entity);

            Assert.Contains("json", report);
        }

        /// <summary>
        /// Trace with nullable→non-nullable shows "nullable→" mode.
        /// Kills L205-206: IsNullable(srcType) && !IsNullable(tgtType).
        /// </summary>
        [Fact]
        public void Trace_NullableToNonNull_ShowsNullableMode()
        {
            var entity = new FullTypeEntity { NullableIntProp = 50, NullIntNull = null };

            var report = MapTracer.Trace<FullTypeEntity, NullableDto>(entity);

            Assert.Contains("nullable", report);
        }

        /// <summary>
        /// Trace with non-nullable→nullable shows "→nullable" mode.
        /// Kills L207-208: !IsNullable(srcType) && IsNullable(tgtType).
        /// </summary>
        [Fact]
        public void Trace_NonNullToNullable_ShowsNullableMode()
        {
            var entity = new NullableTypesEntity { NonNullDateTime = DateTime.Now };

            var report = MapTracer.Trace<NullableTypesEntity, NonNullToNullableDto>(entity);

            Assert.Contains("nullable", report);
        }
    }

    // ================================================================
    // Custom TextWriter for testing non-StringWriter path
    // ================================================================

    /// <summary>
    /// Custom TextWriter that is NOT a StringWriter, for testing the fallback callback path.
    /// </summary>
    internal class CustomTextWriter : TextWriter
    {
        private readonly System.Text.StringBuilder _sb = new();
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
        public override void Write(char value) => _sb.Append(value);
        public override void Write(string? value) => _sb.Append(value);
        public override void WriteLine(string? value) => _sb.AppendLine(value);
        public override string ToString() => _sb.ToString();
    }

    // ================================================================
    // Test models for MapValidator Guid/string tests
    // ================================================================

    /// <summary>
    /// Source with Guid property.
    /// </summary>
    public class ValidatorGuidSource
    {
        public Guid GuidProp { get; set; } = Guid.NewGuid();
    }

    /// <summary>
    /// DTO mapping Guid → string.
    /// </summary>
    [MapFrom(typeof(ValidatorGuidSource))]
    public class ValidatorGuidToStringDto
    {
        public string GuidProp { get; set; } = "";
    }

    /// <summary>
    /// Source with string property.
    /// </summary>
    public class ValidatorStringSource
    {
        public string GuidProp { get; set; } = "11111111-2222-3333-4444-555555555555";
    }

    /// <summary>
    /// DTO mapping string → Guid.
    /// </summary>
    [MapFrom(typeof(ValidatorStringSource))]
    public class ValidatorStringToGuidDto
    {
        public Guid GuidProp { get; set; }
    }

    /// <summary>
    /// DTO mapping Guid → int (NOT compatible).
    /// </summary>
    [MapFrom(typeof(ValidatorGuidSource))]
    public class ValidatorGuidToIntDto
    {
        public int GuidProp { get; set; }
    }
}
