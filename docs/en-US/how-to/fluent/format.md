> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
wiki-id: fluent/format
Diagnostic codes involved: NCMAP_F64, NCMAP_F65, NCMAP_F66, NCMAP_F67  
Library version: v1.1.0+  
Last updated: 2026-10-23  
---

# Fluent Formatting (`.Format` Terminal)

## Overview

`.Format(format[, overrideIgnore])` is a Fluent channel terminal (a *Member*-style three-part configuration segment) for formatting — semantically mirroring the attribute-based channel `[MapFormat]`. The source type must implement `IFormattable` (e.g., `DateTime`, `DateTimeOffset`, `TimeSpan`, `Guid`, numeric types, enums, including `Nullable<T>` wrappers), and the target property must be of type `string`. Both channels share the same snapshot context, identical guard logic and validation rules, and their outputs are locked down via equivalence testing.

Terminal signature (configuration segment — may appear directly after either `MemberHead` or `MemberConfig`; `TProp` is inferred by `.Member` and thus disappears from the signature):

```csharp
MemberConfig<TSource, TTarget, TProp> Format(
    string format,
    bool overrideIgnore = false);
```

## Basic Usage

The entry point follows standard Fluent declaration patterns (`[MapperConfig]` + static `Configure(FluentMappingConfig)` method). Field binding uses the three-part `.Member` syntax:

```csharp
[MapperConfig]
public class OrderFluentConfig
{
    public static void Configure(FluentMappingConfig cfg)
    {
        cfg.For<Order, OrderDto>()
           .Member(d => d.CreatedAtText).Format("yyyy-MM-dd")               // DateTime
           .Member(d => d.AmountText).Format("N2")                          // Numeric
           .Member(d => d.StatusCode).Format("D")                           // Enum
           .Member(d => d.DurationText).Format("hh\\:mm")                   // TimeSpan (colon must be escaped)
           .Member(d => d.CardNoMasked).Format("N2", overrideIgnore: true); // Global ignore exemption gate (mirrored on attribute side)
    }
}
```

- `For<Order, OrderDto>()` itself serves as the mapping anchor — no `[MapFrom]` required;  
- The target property path in `.Member` must be a single-segment plain property name (multi-segment or indexed paths are unsupported — same interception behavior as F55/F06);  
- Source properties are automatically matched by name to the target property (same anchoring semantics as `.Mask`); cross-field binding requires explicit source specification via `.From` ("custom source + formatting" composition), or use `[MapProperty]` in the attribute-based channel;  
- The format string must be a compile-time constant — non-constant arguments are silently skipped (except where the compiler already reports a type error).

## Guard Diagnostics (F64–F67)

| Syntax | Result |
|---|---|
| Target property anchored by `.Member` is not of type `string` | **NCMAP_F64** (Error) |
| Source property is inaccessible or does not implement `IFormattable` | **NCMAP_F65** (Error) |
| `.Format` and `.Convert` are used on the same anchor | **NCMAP_F66** (Error — mutually exclusive) |
| Reverse mapping hits this property but `.ConvertBack` is not declared | **NCMAP_F67** (Warning — reverse mapping skips this property; no assignment occurs) |

## Key Behavioral Notes

- **Culture handling**: Always passes `null` as the `IFormatProvider` (= current culture). This intentionally diverges from invariant output for unannotated properties; use `.Convert` for full control over culture-invariant formatting.  
- **Three narrow-format domains**:  
  - `Guid`: only supports format specifiers `N`, `D`, `B`, `P`, `X`;  
  - `TimeSpan`: custom format strings require escaping colons and periods (e.g., `hh\:mm`);  
  - Enums: format strings must use enum-specific specifiers such as `G`, `D`, `X`, `F`, etc.  
- **Reverse-mapping skip**: `.Format` is a one-way display-only declaration — bare usage is fully valid. During reverse mapping, such properties are skipped (original values remain unchanged; other fields map normally), and only an `F67` warning is issued. For bidirectional support, pair `.Format` with `.ConvertBack` on the same anchor (when combined, reverse mapping proceeds normally without warnings).  
- **Projection**: Behaves identically to the attribute-based channel — EF Core / FreeSql `AsDto()` automatically degrades to two-step mapping.

## Runnable Demo (Scenario 202)

[!code-csharp[Fluent-side `.Format` terminal paired with `.ConvertBack` for round-trip mapping (same context, guards, and output as attribute-based channel)](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_FluentChannel)]

[!code-csharp[Fluent-side `.Format` terminal paired with `.ConvertBack` for round-trip mapping (same context, guards, and output as attribute-based channel) · MfFluentDto_CreatedAt](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_FluentChannel_MfFluentDto_CreatedAt)]

[!code-csharp[Fluent-side `.Format` terminal paired with `.ConvertBack` for round-trip mapping (same context, guards, and output as attribute-based channel) · MfFluentDto_Amount](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_FluentChannel_MfFluentDto_Amount)]

[!code-csharp[Fluent-side `.Format` terminal paired with `.ConvertBack` for round-trip mapping (same context, guards, and output as attribute-based channel) · MfFluentDto_ShippedDate](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_FluentChannel_MfFluentDto_ShippedDate)]

[!code-csharp[Fluent-side `.Format` terminal paired with `.ConvertBack` for round-trip mapping (same context, guards, and output as attribute-based channel) · MfFluentConfig](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_FluentChannel_MfFluentConfig)]

[!code-csharp[Fluent-side `.Format` terminal paired with `.ConvertBack` for round-trip mapping (same context, guards, and output as attribute-based channel) · MfFluentConfig_Configure](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_FluentChannel_MfFluentConfig_Configure)]

## Related Pages

- [MapFormat Channel](../declarative/map-format.md) — Attribute-based syntax (functionally equivalent to Fluent)  
- [Fluent Conversion](convert.md) — `.Convert` terminal (for multi-field or fixed-culture scenarios)  
- [Fluent Four Terminals](terminals.md) — Quick reference for all existing terminals  
- [Diagnostic Code Index](../../diagnostics/rules.md) — Full entries for NCMAP_F64–F67
