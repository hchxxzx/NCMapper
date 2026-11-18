> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
wiki-id: declarative/map-format
Diagnostic codes involved: NCMAP019, NCMAP021, NCMAP022, NCMAP043, NCMAP044, NCMAP045, NCMAP046  
Library version: v1.1.0+  
Last updated: 2026-09-30  
---

# Property-Level Formatting (MapFormat Channel)

## Overview

`[MapFormat("format string")]` enables direct field mapping that converts the source value into a formatted string using the specified format string—eliminating the need to write `[Convert]`-based static converters (static class + static method) for common display scenarios such as dates, numbers, and enumerations.

**Applicable to**: Single-field → string formatting (e.g., `"yyyy-MM-dd"`, `"N2"`, `"D"`).  
**Not applicable to**: Multi-field composition, business logic computation, or scenarios requiring culture-invariant output—continue using `[Convert]`.

**Whitelisted source types**: `DateTime`, `DateTimeOffset`, `TimeSpan`, `Guid`, all numeric types, and **enumerations**—all optionally wrapped in `Nullable<T>`. Enumerations are supported via `IFormattable`; verified on .NET 10 with examples like `"D"` → `"1"`, `"G"` → `"Monday"`, `"X"` → `"00000001"`.

> This page documents the attribute-based syntax. The equivalent Fluent API endpoint `.Format(...)` is covered in [Fluent Formatting](../fluent/format.md). Both channels share the same snapshot model and validation rules; their equivalence is guaranteed by comprehensive test coverage. A runnable demo is available in sample scenario `202`.

## Basic Usage

```csharp
[MapFrom(typeof(Order))]                      // ← Mapping anchor: without an anchor, property-level attributes are ignored (compile-time error NCMAP019)
public class OrderDto
{
    [MapFormat("yyyy-MM-dd")]                 // DateTime → string
    public string CreatedAtText { get; set; }

    [MapFormat("N2")]                         // Numeric → thousands separator + two decimal places
    public string AmountText { get; set; }

    [MapFormat("D")]                          // Enumeration → integer representation (G=name / X=hex / F=flag combination)
    public string StatusCode { get; set; }

    [MapFormat("hh\\:mm")]                    // TimeSpan (colons and periods in custom format strings must be escaped)
    public string DurationText { get; set; }

    [MapFormat("D")]                          // Guid (only five standard format specifiers supported: N/D/B/P/X)
    public string OrderIdText { get; set; }

    [MapFormat("yyyy-MM-dd")]
    public string? ShippedDate { get; set; }  // Source is DateTime?; null maps to null (no exception thrown)
}
```

## Composite Patterns

### Cross-Field Binding (combined with `[MapProperty]`)

```csharp
[MapProperty(typeof(Order), nameof(Order.CreatedAt))]   // Use MapProperty when source and target names differ
[MapFormat("yyyy-MM-dd")]
public string CreatedDate { get; set; }
```

### Format-Then-Mask (combined with `[Mask]`)

```csharp
[MapFormat("N2")]                              // Execution order is fixed: first ToString("N2"), then Mask(...)
[Mask(typeof(MaskBankCard))]
public string CardNoMasked { get; set; }
```

### Forward Formatting + Reverse Parsing (combined with `[ConvertBack]`)

```csharp
[MapFormat("yyyy-MM-dd")]                      // Forward (Entity→DTO): formatted output
[ConvertBack(typeof(DateParser), nameof(DateParser.Parse))]   // Reverse (DTO→Entity): uses ConvertBack for parsing
public string BirthDate { get; set; }
```

> The two attributes operate orthogonally and do not interfere. **A property marked only with `[MapFormat]` is skipped during reverse mapping** (its value remains unchanged on the entity side; other properties are written normally)—since formatting is lossy and culture-sensitive, the library does not attempt to infer reverse parsing logic and instead emits warning **NCMAP046**. Add `[ConvertBack]` manually if round-trip support is required.

### Global Ignore Override Gate

```csharp
[MapFormat("N2", OverrideIgnore = true)]       // Remains active even when matched by global ignore rules (the sole override mechanism)
public string TotalText { get; set; }
```

## Validation Rules & Diagnostic Code Quick Reference

| Usage | Result |
|---|---|
| Target property type is not `string` | **NCMAP043** (Error) |
| Source property type does not implement `IFormattable` (e.g., `string` source, custom struct) | **NCMAP044** (Error) |
| `[MapFormat]` and `[Convert]` applied together | **NCMAP045** (Error — mutually exclusive) |
| Reverse mapping hits this property but `[ConvertBack]` is absent | **NCMAP046** (Warn — property is skipped during reverse mapping, no assignment occurs) |
| `[MapFormat]` applied on a type lacking a mapping anchor | **NCMAP019** (existing diagnostic code, family-wide convention) |
| `[MapFormat(OverrideIgnore = true)]` and `[MapIgnore]` applied together | **NCMAP022** (existing diagnostic code, contradictory configuration; ignore takes precedence) |
| Bare `[MapFormat]` + `[MapIgnore]` | Silently treated as ignored (ignore check runs first, family-wide convention) |
| Matches global ignore list and no `OverrideIgnore` is set | Skipped + **NCMAP021** guidance (existing diagnostic code) |
| Format string is empty or whitespace | `ArgumentException` thrown at attribute construction time |

## Key Behavioral Notes (Must-Know Before Production)

| Note | Description |
|---|---|
| **Culture** | Defaults to the **current culture** (`IFormattable.ToString(formatString, null)` semantics). This intentionally diverges from the existing behavior where unannotated properties always use invariant culture output—adding `[MapFormat]` makes output culture-dependent (e.g., `"1234,56"`). For machine-invariant output, use `[Convert]` and control culture explicitly. |
| **Dual Engines** | Source generation path: format string is embedded as a compile-time constant (zero reflection, AOT-friendly); fallback runtime path: expression trees replicate identical semantics. Output is consistent across both paths. |
| **Projection (EF Core / FreeSql AsDto)** | Automatically downgrades to two-step mapping (first fetch entity via SQL, then format in-memory)—same behavior as `[Convert]`-based formatting; **downgrade applies per-mapping-pair** (if any property in the DTO carries `[MapFormat]`, the entire DTO’s `AsDto` operation uses two-step mapping). |
| **Dapper** | Zero impact (Dapper channel already performs entity retrieval followed by in-memory mapping). |
| **Invalid format string** | Not validated at compile time (.NET format string semantics are runtime-only); throws `FormatException` at runtime. `Guid`/`TimeSpan`/enum format strings have narrow valid ranges—see notes above. |
| **Enumerations** | Applying date-style format strings to enums throws `FormatException` at runtime—enum-specific format specifiers include `G`/`D`/`X`/`F`, etc. |

## Syntax Comparison (Choosing Between `[MapFormat]` and `[Convert]`)

```csharp
// Legacy approach: requires a dedicated static converter class for one line of formatting
public static class DateFmt
{
    public static string ToShort(DateTime v) => v.ToString("yyyy-MM-dd");
}
public class OrderDtoOld
{
    [MapProperty(typeof(Order), nameof(Order.CreatedAt))]
    [Convert(typeof(DateFmt), nameof(DateFmt.ToShort))]
    public string CreatedDate { get; set; }
}

// Modern approach: single-line format string
public class OrderDtoNew
{
    [MapProperty(typeof(Order), nameof(Order.CreatedAt))]
    [MapFormat("yyyy-MM-dd")]
    public string CreatedDate { get; set; }
}
```

**Selection guideline**: Use `[MapFormat]` for single-field → string conversion; use `[Convert]` for multi-field composition, business logic, or culture-invariant output.

## Runnable Demo (Scenario 202)

[!code-csharp[Attribute-side `\[MapFormat\]` usage paired with `\[ConvertBack\]` for round-trip (full coverage: date, number, enum, TimeSpan, nullable, FormatThenMask)](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_AttrChannel)]

[!code-csharp[Attribute-side `\[MapFormat\]` usage paired with `\[ConvertBack\]` for round-trip (full coverage: date, number, enum, TimeSpan, nullable, FormatThenMask) · MfOrder_CreatedAt](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_AttrChannel_MfOrder_CreatedAt)]

[!code-csharp[Attribute-side `\[MapFormat\]` usage paired with `\[ConvertBack\]` for round-trip (full coverage: date, number, enum, TimeSpan, nullable, FormatThenMask) · MfOrder_Amount](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_AttrChannel_MfOrder_Amount)]

[!code-csharp[Attribute-side `\[MapFormat\]` usage paired with `\[ConvertBack\]` for round-trip (full coverage: date, number, enum, TimeSpan, nullable, FormatThenMask) · MfOrder_Status](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_AttrChannel_MfOrder_Status)]

[!code-csharp[Attribute-side `\[MapFormat\]` usage paired with `\[ConvertBack\]` for round-trip (full coverage: date, number, enum, TimeSpan, nullable, FormatThenMask) · MfOrder_Duration](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_AttrChannel_MfOrder_Duration)]

[!code-csharp[Attribute-side `\[MapFormat\]` usage paired with `\[ConvertBack\]` for round-trip (full coverage: date, number, enum, TimeSpan, nullable, FormatThenMask) · MfOrder_ShippedDate](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_AttrChannel_MfOrder_ShippedDate)]

[!code-csharp[Attribute-side `\[MapFormat\]` usage paired with `\[ConvertBack\]` for round-trip (full coverage: date, number, enum, TimeSpan, nullable, FormatThenMask) · MfOrder_CardNo](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_AttrChannel_MfOrder_CardNo)]

[!code-csharp[Attribute-side `\[MapFormat\]` usage paired with `\[ConvertBack\]` for round-trip (full coverage: date, number, enum, TimeSpan, nullable, FormatThenMask) · MfOrderDto](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_AttrChannel_MfOrderDto)]

[!code-csharp[Attribute-side `\[MapFormat\]` usage paired with `\[ConvertBack\]` for round-trip (full coverage: date, number, enum, TimeSpan, nullable, FormatThenMask) · MfOrderDto_CreatedAt](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_AttrChannel_MfOrderDto_CreatedAt)]

[!code-csharp[Attribute-side `\[MapFormat\]` usage paired with `\[ConvertBack\]` for round-trip (full coverage: date, number, enum, TimeSpan, nullable, FormatThenMask) · MfOrderDto_Amount](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_AttrChannel_MfOrderDto_Amount)]

[!code-csharp[Attribute-side `\[MapFormat\]` usage paired with `\[ConvertBack\]` for round-trip (full coverage: date, number, enum, TimeSpan, nullable, FormatThenMask) · MfOrderDto_Status](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_AttrChannel_MfOrderDto_Status)]

[!code-csharp[Attribute-side `\[MapFormat\]` usage paired with `\[ConvertBack\]` for round-trip (full coverage: date, number, enum, TimeSpan, nullable, FormatThenMask) · MfOrderDto_Duration](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_AttrChannel_MfOrderDto_Duration)]

[!code-csharp[Attribute-side `\[MapFormat\]` usage paired with `\[ConvertBack\]` for round-trip (full coverage: date, number, enum, TimeSpan, nullable, FormatThenMask) · MfOrderDto_ShippedDate](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_AttrChannel_MfOrderDto_ShippedDate)]

[!code-csharp[Attribute-side `\[MapFormat\]` usage paired with `\[ConvertBack\]` for round-trip (full coverage: date, number, enum, TimeSpan, nullable, FormatThenMask) · MfOrderDto_CardNo](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_AttrChannel_MfOrderDto_CardNo)]

## Related Pages

- [Fluent Formatting](../fluent/format.md) — `.Format` endpoint (semantically equivalent to attribute channel)  
- [Data Masking](../infrastructure/masking.md) — Semantic details of `[Mask]` combined with FormatThenMask  
- [Diagnostic Code Index](../../diagnostics/rules.md) — Full entries for NCMAP043–046  
- [Fluent Entry Points](../fluent/basics.md) — Declaration rules for dual channels (F11 arbitration)
