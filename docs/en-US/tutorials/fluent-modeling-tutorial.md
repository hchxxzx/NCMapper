> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
wiki-id: tutorials/fluent-modeling-tutorial  
Diagnostic codes involved: NCMAP_F12, NCMAP_F15, NCMAP_F16  
Library version: v1.0.0+  
Last updated: 2026-09-29  
---

# Fluent Channel Modeling Tutorial: Building an Order Converter from Scratch  

## Overview  

This tutorial addresses a concrete business requirement: the order detail API must convert an `Order` entity into a page-specific DTO, with varying field requirements — including name remapping, nested property access, phone number masking, exclusion of internal fields, and computation of a summary value. You’ll start from an empty configuration class and progressively build this mapping using Fluent-style chained declarations — all verified at compile time.

The examples align semantically with Scenario 01 in `samples/NCMapper.Samples` (covering basic chains, conversions, conditional guards, and constructor binding), and are fully executable for validation. Each section provides concise illustrative code; complete working code is included at the end for holistic understanding. Full parameter documentation for each terminal method is available in [Fluent Basics](../how-to/fluent/basics.md) and [Fluent Terminals](../how-to/fluent/terminals.md); this tutorial focuses on the *modeling process* — translating requirements directly into configuration.

---  

## 1. Scenario: Which Fields Does the Order Detail Page Need?  

Product requirements:  

| Page Field        | Data Source           | Requirement                                  |
|-------------------|-----------------------|----------------------------------------------|
| Order ID          | `Id`                  | Name differs (`OrderId` on page)             |
| Customer Name     | `Customer.Name`       | Nested object access                         |
| Customer Phone    | `CustomerPhone`       | **Masked display** (compliance requirement)  |
| Internal Note     | `InternalNote`        | **Excluded from page**                       |
| Total Weight      | `Items` collection    | Computed summary (no pre-existing field)     |

Corresponding models (simplified):  

```csharp
public class ChainOrderEntity
{
    public int Id { get; set; }
    public ChainCustomerInfo Customer { get; set; } = new();
    public string CustomerPhone { get; set; } = "";
    public string InternalNote { get; set; } = "internal";
    public List<ChainOrderItemEntity> Items { get; set; } = new();
}

public class ChainOrderDto
{
    public int OrderId { get; set; }
    public string CustomerName { get; set; } = "";
    public string CustomerPhone { get; set; } = "";
    public string InternalNote { get; set; } = "";
    public double TotalWeight { get; set; }
}
```

Why choose the Fluent channel? This mapping has **order dependencies** (e.g., name remapping must precede computed-field callbacks), requires conditional guards, and the team prefers keeping DTO classes clean — with field definitions driven by product specs and mapping logic centralized in a configuration class for unified review.

---  

## 2. Step 1: Create a Configuration Class  

Create a new mapper configuration class annotated with `[MapperConfig]`, containing a static method with a fixed signature (**method name is arbitrary**, but the parameter must be `FluentMappingConfig`):  

```csharp
[MapperConfig]
public class ChainOrderMapper
{
    public static void Configure(FluentMappingConfig cfg)
    {
        // Fluent chain declarations go here
    }
}
```  

Three key conventions:  

- No attributes or annotations are added to the DTO class — all intent resides solely in this configuration class.  
- The source generator scans the method body at compile time and emits mapping code functionally identical to that produced by the `[MapFrom]` channel.  
- If no matching-signature method exists in the class, the compiler reports `NCMAP_F15` — enabling early detection of misconfigured mapper classes.  

---  

## 3. Step 2: One Chain Satisfies All Five Requirements  

Translate each requirement from Section 1 into a Fluent terminal call:  

| Requirement                     | Terminal Method                                      |
|---------------------------------|------------------------------------------------------|
| Name remapping (`Id` → `OrderId`)         | `.Member(d => d.OrderId).From(s => s.Id)`            |
| Nested access (`Customer.Name` → `CustomerName`) | `.Member(d => d.CustomerName).From(s => s.Customer.Name)` |
| Exclude internal note           | `.Member(d => d.InternalNote).Ignore()`              |
| Mask phone number               | `.Member(d => d.CustomerPhone).Mask<MaskPhone>()`     |
| Compute total weight            | `.AfterMapping(ApplyComputedFields)`                 |

Concise illustration (matching sample `FluentBasics_ChainOrderMapper`):  

[!code-csharp[Fluent Basic Chain: From (name remap/nested) + Ignore + Mask + AfterMapping](~/samples/NCMapper.Samples/01-Fluent/FluentModels.cs#FluentBasics_ChainOrderMapper_Configure)]  

Note: The method referenced by `.AfterMapping` must be `public` or `internal static`; declaring it `private` triggers diagnostic `NCMAP_F12`.

---  

## 4. Step 3: Format Dates Using `.Format`  

Requirement iteration: The page also needs “Last Modified Time” as formatted text. The entity holds a `DateTime`, but the page expects a `yyyy-MM-dd` string — a case where formatting is expressible purely via a format string. Use `.Format` — **no custom converter method required**:  

```csharp
// Same-name anchor: DTO property name matches entity property name (CreatedAt: DateTime → CreatedAt: string).
// The format string becomes a compile-time constant in generated code (AOT-friendly); output respects current culture — 
// but fixed-format dates like "yyyy-MM-dd" are inherently culture-agnostic.
cfg.For<ChainOrderEntity, ChainOrderDto>()
   .Member(d => d.CreatedAt).Format("yyyy-MM-dd");
```  

Two key usage points for `.Format`:  

- **Same-name anchor**: The source property is auto-matched by name against the target property. The DTO property name *must* match the entity property name exactly. Using a different name (e.g., `CreatedText`) triggers `NCMAP_F65` — for name-remapped scenarios, first declare the source explicitly via `.Member(d => d.X).From(s => s.Y)`, or rename the property.  
- **One-way display-only declaration, zero configuration overhead**: Formatting is a lossy, one-way transformation (e.g., `yyyy-MM-dd` discards time components). The library does *not* reverse-parse based on the format string. For display-only DTOs, use bare `.Format` directly — no extra setup needed. During reverse mapping (DTO → entity), such fields are skipped (entity values remain unchanged), with only a warning (`NCMAP_F67`) issued. For truly bidirectional fields (e.g., editable forms), explicitly declare reverse parsing using `.ConvertBack`.  

For custom conversion logic beyond format strings (e.g., full-entity aggregation, constant injection, multi-field composition), use `.Convert` to bind a converter method. Choose among four overloads based on input granularity (matching sample `FluentConvert_Convert`):  

[!code-csharp[Fluent Convert: Four Overloads (T0 const / T1 field / T2 full source / T3 composite)](~/samples/NCMapper.Samples/01-Fluent/CvModels.cs#FluentConvert_Convert)]  

Converter methods are ordinary static methods — they may reside in the configuration class or in separate utility classes.  

Important reminder: **`.Format` and forward-declared `.Convert` are *not consumed* during reverse mapping (DTO → entity) by default.** If bidirectional support is needed for the same type pair:  
- `.Format`-bound properties require explicit opt-in at compile time (`NCMAP_F67`).  
- `.Convert`-bound properties are silently skipped in reverse direction — use `.ConvertBack` to explicitly declare reverse parsing (symmetric to `.Convert`; see [Fluent Conversion](../how-to/fluent/convert.md)). Full details on formatting are in [Fluent Formatting](../how-to/fluent/format.md).  

---  

## 5. Step 4: Conditional Mapping for Selected Fields  

Requirement iteration: Score fields should only appear if the score value is non-zero. Use `.MapWhen` to attach a conditional guard to a property — supply either a named predicate method or an inline lambda:  

```csharp
// Predicate methods: ordinary static methods (same rules as Convert methods)
public static class MwRules
{
    public static bool HasContent(string value) => !string.IsNullOrWhiteSpace(value);
    public static bool IsKindA(MwItemEntity src) => src.Kind == "A";
}
```  

[!code-csharp[MapWhen Conditional Guards: Named predicate + inline lambda + same-anchor composition](~/samples/NCMapper.Samples/01-Fluent/MwModels.cs#MapWhen_MapWhenFluentMapper_Configure)]  

`.From` and `.MapWhen` can **coexist on the same anchor** (both begin with `.Member(d => d.X)` — anchoring the destination first, then specifying source or condition). Execution order follows chain order — a primary reason for choosing the Fluent channel. Two notes:  
① Inline lambda predicates receive the *entire source object* — **type inference is unavailable, so `<TSource>` must be explicitly specified**. Named predicate methods with parameter names matching source field names can omit the type argument.  

---  

## 6. Step 5: Constructor Binding for Immutable Target Types  

If the target DTO is designed as immutable (get-only properties + constructor initialization), property assignment fails — instead, use `.MapCtorParam` to bind source values to constructor parameters. Two supported forms, plus a `record` special case:  

[!code-csharp[Fluent MapCtorParam: Two Constructor Parameter Binding Forms](~/samples/NCMapper.Samples/01-Fluent/CpModels.cs#CtorParam_CtorParamFluentMapper_Configure)]  

We recommend Form 1 (source lambda, symbol-level checking). In Form 2, a typo in the property name triggers `NCMAP_F51` at compile time. For `record` targets, positional parameters map directly to same-named properties — standard `.Map` suffices to feed them to the constructor; no `MapCtorParam` is needed.

---  

## 7. Invocation and Verification  

No manual registration is required — configuration classes activate automatically when their assembly loads. Two invocation styles produce identical results:  

[!code-csharp[Unified runtime fallback (for undeclared type pairs)](~/samples/NCMapper.Samples/01-Fluent/FluentModels.cs#FluentBasics_ChainOrderMapper)]  

Both styles execute the same generated code — differing only in the entry point layer (see [Dual-Engine Architecture](../explanation/dual-engine.md)).  

Validate using the models from Section 1:  
- `OrderId` equals the entity’s `Id`;  
- `CustomerName` is retrieved from the nested `Customer.Name`;  
- `CustomerPhone` is masked (e.g., `138****5678`);  
- `InternalNote` remains its default empty string;  
- `TotalWeight` equals the sum of item weights.  

---

## 8. Complete Code Example

Integrating all requirements from Steps 2–6 into a single order model yields the complete implementation below (educational full example: for demonstration purposes, two new fields—`CreatedAt` and `Score`—are added to the entity to support the transformation examples in Step 4 and conditional mapping examples in Step 5; all declaration patterns match those shown in Sections 3–6):

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using NCMapper;

// ================= 1. Models =================
public class ChainOrderEntity
{
    public int Id { get; set; }
    public ChainCustomerInfo Customer { get; set; } = new();
    public string CustomerPhone { get; set; } = "";
    public string InternalNote { get; set; } = "internal";
    public List<ChainOrderItemEntity> Items { get; set; } = new();
    public DateTime CreatedAt { get; set; }   // Educational addition: used in Step 4 transformation example
    public int Score { get; set; }            // Educational addition: used in Step 5 conditional example
}

public class ChainCustomerInfo
{
    public string Name { get; set; } = "";
}

public class ChainOrderItemEntity
{
    public double Weight { get; set; }
}

public class ChainOrderDto
{
    public int OrderId { get; set; }
    public string CustomerName { get; set; } = "";
    public string CustomerPhone { get; set; } = "";
    public string InternalNote { get; set; } = "";
    public double TotalWeight { get; set; }
    public string CreatedAt { get; set; } = "";   // Same name as entity property (format anchor): DateTime → formatted string
    public int Score { get; set; }
}

// ================= 2. Converter and Predicate (Plain Static Methods) =================
public static class OrderRules
{
    // Predicate: map this field only if score ≥ 90
    public static bool IsHighScore(int score) => score >= 90;
}

// ================= 3. Configuration Class: One Chain Holds All Declarations =================
[MapperConfig]
public class ChainOrderMapper
{
    // Method name is arbitrary; signature is fixed: static void Xxx(FluentMappingConfig cfg)
    public static void Configure(FluentMappingConfig cfg)
    {
        cfg.For<ChainOrderEntity, ChainOrderDto>()
           // —— Step 3: Basic Chain ——
           .Member(d => d.OrderId).From(s => s.Id)                        // Different names: Id → OrderId
           .Member(d => d.CustomerName).From(s => s.Customer.Name)        // Nested property access
           .Member(d => d.InternalNote).Ignore()                          // Ignore: not displayed on UI
           .Member(d => d.CustomerPhone).Mask<MaskPhone>()                // Masking: e.g., "138****5678"
           // —— Step 4: Date Formatting Output (Iterative additions go directly into the same chain) ——
           .Member(d => d.CreatedAt).Format("yyyy-MM-dd")             // Same-name anchor: compile-time constant; no converter method needed;
                                                                       // Unidirectional display declaration—bare declaration is valid (reverse mapping leaves this field unassigned)
           // —— Step 5: Conditional Guard (Same-name property auto-anchored; inline predicate requires explicit type argument, e.g., MapWhen<ChainOrderEntity>(s => s.Score >= 90)) ——
           .Member(d => d.Score).MapWhen(OrderRules.IsHighScore)
           // —— Final Step: Aggregation Callback ——
           .AfterMapping(ApplyComputedFields);
    }

    // AfterMapping callback: must be public or internal static (private triggers NCMAP_F12)
    public static void ApplyComputedFields(ChainOrderEntity src, ChainOrderDto dst)
    {
        dst.TotalWeight = src.Items.Sum(i => i.Weight);           // Aggregate item weights
    }
}

// ================= 4. Usage =================
public class OrderPageService
{
    public ChainOrderDto Load(ChainOrderEntity entity)
    {
        // Approach 1: Unified entry point (works even for undeclared type pairs; runtime fallback applies)
        var dto = Mapper.Map<ChainOrderEntity, ChainOrderDto>(entity)!;

        // Approach 2: Source generator extension method (compile-time direct call; requires `using NCMapper.Generated`)
        // var dto = entity.MapToChainOrderDto();

        return dto;
    }
}
```

Read this code in the following order: first examine the models (what the UI needs), then review the configuration class (where each field originates and how it’s processed), and finally inspect the usage (a one-liner completes the mapping). All declarations for a given type pair must reside within the *same* chain—subsequent iterations should append to this chain rather than introducing a new `For<...>()` block.

---


## 9. Common Pitfalls

| Symptom | Cause and Resolution |
|---------|----------------------|
| Two `For<...>()` chains defined for the same type pair | Compilation error `NCMAP_F16`—all declarations for a given type pair must be consolidated into a single chain |
| `AfterMapping` method triggers `NCMAP_F12` | Callback method must be `public` or `internal static`; `private` is not allowed |
| Configuration class has no effect | Verify presence of `[MapperConfig]` attribute and correct method signature `static void Xxx(FluentMappingConfig cfg)` (missing either triggers `NCMAP_F15`) |
| Reverse mapping loses forward-mapping results | Forward `.Format`/`.Convert` operations are *not consumed* during reverse mapping—`.Format` properties remain *unassigned* in reverse direction (warning `NCMAP_F67` only); bidirectional formatting requires explicit `.ConvertBack`; `.Convert` properties fall back to standard conversion in reverse direction (hint `NCMAP033`) |
| Same type pair declared via both Fluent API and `[MapFrom]` attribute | Compilation error `NCMAP_F11`—only one declaration channel is permitted per type pair |

---


## Related Documentation

- [Fluent Basics (Chained Declaration Entry Point)](../how-to/fluent/basics.md): Full reference of terminal methods and F-family diagnostic codes
- [Fluent Conversion](../how-to/fluent/convert.md): Complete explanation of all four `Convert`/`ConvertBack` variants
- [Fluent Formatting](../how-to/fluent/format.md): `.Format` terminal, same-name anchor semantics, and guard diagnostics `F64`–`F67`
- [Property-Level Formatting (`MapFormat`)](../how-to/declarative/map-format.md): Equivalent capability via attribute-based declaration
- [Fluent Terminals](../how-to/fluent/terminals.md): `MapWhen`, `MapCtorParam`, `MapSubclass`, and `FromJson`
- [Dual-Channel Architecture](../explanation/dual-channel.md): Architectural relationship between Fluent and Attribute declaration channels
- [Multi-Source Composition (`Compose`) Tutorial](compose-tutorial.md): When a single target depends on multiple source objects, use `Compose` instead
