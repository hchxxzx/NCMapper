> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

<!-- Source: NCMapper Main Technical Documentation §13 Mapping Trace -->

## 13. Mapping Trace

The mapping process is a black box to developers: why a certain property wasn’t mapped, whether direct assignment or type conversion was used, and how nested objects are handled—these decisions are invisible by default. When `TraceEnabled` is turned on, each mapping operation outputs the mapping strategy applied to every property (`direct` / `nested` / `json` / `convert` / `ignored`), transforming the black-box process into an observable one and enabling rapid root-cause analysis when mapping results deviate from expectations.

[!code-csharp[Enable mapping trace globally (toggle + callback channel)](~/samples/NCMapper.Samples/06-Infrastructure/TraceScenarios.cs#Trace_GlobalEnabled)]

**Typical use cases**—

| Scenario | Description |
|----------|-------------|
| Diagnosing `null` fields | A property maps to `null`; inspect the Trace output to determine whether it was `ignored` or whether the source property simply does not exist. |
| Identifying type conversion paths | Confirm whether a field used `direct` assignment or `convert`, helping diagnose unexpected conversion results. |
| Validating ignore configurations | Verify whether `[MapIgnore]` takes effect—the ignored properties appear as `ignored` in the trace. |
| Analyzing nested mappings | Check whether nested objects were handled via `nested` or `json` strategies, aiding diagnosis of deep-copy or nesting issues. |
| Log archiving | Output trace to a file for post-hoc analysis of mapping behavior in production environments. |

[!code-csharp[Redirect trace output to file for post-hoc analysis](~/samples/NCMapper.Samples/06-Infrastructure/TraceScenarios.cs#Trace_OutputToFile)]

#### 13.1 AsDto Projection Diagnostics

When `MapTracer` is enabled, the full decision chain for `AsDto` projections becomes fully observable—normal projection, fallback triggers, property skips, dot-path matching, and flattening matching all emit trace entries.

[!code-csharp[Concise trace toggle](~/samples/NCMapper.Samples/06-Infrastructure/TraceScenarios.cs#Trace_GlobalEnabled)]

**Typical diagnostic scenarios**:

| Scenario | Example Trace Output |
|----------|----------------------|
| Fallback triggered | `[EFCore AsDto] Fallback: OrderEntity → OrderDto, Reason: Property Amount is annotated with Convert (requires fallback)` |
| Dot-path match | `[EFCore Projection] Dot-path 'Address.City' → Address.City matched successfully` |
| Dot-path failure | `[EFCore Projection] Dot-path 'A.B' failed at segment 'B' (property not found in type X)` |
| Collection projection skipped | `[EFCore Projection] Skipped collection projection Items→OrderItemDto (Reason: Depth 9 exceeds maximum depth 8)` |
| FreeSql fallback | `[FreeSql AsDto] Fallback: OrderEntity → OrderDto, Reason: Collection navigation projection unsupported by FreeSql’s subquery translation` |
| FreeSql automatic `Include` | `[FreeSql AsDto] Auto-included: Order→Customer, Order→Items` |
| Property skipped | `[EFCore Projection] Skipped target property Phone (Reason: Source field Phone not found in source type UserEntity; no matching dot-path or flattened path exists)` |

> **Note**: `MapTracer` output includes type names and property names. In production environments, enable tracing only during diagnostics.

#### 13.2 `Enable()` Factory and Fluent Configuration

In addition to the global `Mapper.TraceEnabled` / `Mapper.TraceOutput` settings, `MapTraceOptions` provides the `Enable()` factory method to create `TraceOptions` instances configured per call—ideal for one-off mappings or test scenarios.

Fluent enablement (per-call configuration)—`Mapper.Map` overloads without `options` parameters are unaffected; per-call trace analysis is provided via `MapTracer.Trace` (which generates a mapping relationship report *without* executing the mapping). Runnable version:

[!code-csharp[Per-call trace report capture (with in-memory containment check)](~/samples/NCMapper.Samples/06-Infrastructure/TraceScenarios.cs#Trace_PerCallOptions)]

Output to `StringWriter` (test scenario)—a runnable version using `TextWriter`-based capture:

[!code-csharp[Capture trace output to StringWriter](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_TraceToWriter)]

All three `MapTraceOptions` configuration properties are optional and fall back to global defaults if unset:
- `Enabled` — Whether to enable trace output (default: `false`)
- `Output` — Target `TextWriter` for output (default: `Console.Out`)
- `ShowValues` — Whether to output property values (default: `false`; when enabled, `[Mask]`-annotated properties display `[MASKED]` instead of raw values)

> **Related API**: `MapTracer.GloballyEnabled` — Global trace toggle (`public static bool`, default `false`; when enabled, all mapping operations emit trace output unless overridden per-call). Paired with the per-call `MapTraceOptions.Enable()`: use the former for global scenarios, the latter for one-off mappings or testing (source anchor: `MapTracer.cs:64`).

#### 13.3 `LogCallback` Integration with Logging Frameworks

`MapTracer.LogCallback` is a logging callback channel independent of `GlobalOutput`, accepting an `Action<string>` delegate. When set, trace output is written to *both* `GlobalOutput` and the callback—ideal for integrating with logging frameworks such as `ILogger`, Serilog, or NLog:

[!code-csharp[LogCallback integration (collector pattern, restore after use)](~/samples/NCMapper.Samples/06-Infrastructure/TraceScenarios.cs#Trace_GlobalEnabled)]

> **Output channel behavior**: `GlobalOutput` (default `Console.Out`) and `LogCallback` are two independent channels—trace content is written to both simultaneously. `GlobalOutput` can be replaced globally via `Mapper.TraceOutput` or overridden per-call via `MapTraceOptions.Output`; `LogCallback` is configurable only globally (`MapTracer.LogCallback`).

## Runnable Demos (Scenario 607)

[!code-csharp[Full workflow for global `TraceEnabled` toggle (including restoration)](~/samples/NCMapper.Samples/06-Infrastructure/TraceScenarios.cs#Trace_GlobalEnabled)]

[!code-csharp[Per-call trace report (comprehensive assertion covering all four mapping strategies)](~/samples/NCMapper.Samples/06-Infrastructure/TraceScenarios.cs#Trace_PerCallOptions)]

---
