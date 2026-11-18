> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
wiki-id: tutorials/masking-tutorial
Related diagnostic codes:
Library version: v1.0.0+
Last updated: 2026-09-29
---

# Sensitive Data Masking Tutorial: Compliance-Driven Masking in a Customer Support Backend

## Overview

This tutorial addresses a compliance requirement: a customer support backend must display customer information, and security policies mandate that personally identifiable information (PII) — such as phone numbers, ID card numbers, and bank account numbers — **must be masked upon display**. Data is retrieved unchanged from storage, but masking is applied automatically at the presentation layer. Furthermore, masking strategies must be swappable across environments (e.g., overseas deployments) without modifying application code.

You will transform a customer profile page from “plaintext output” to “automatic masking”, then verify the behavior end-to-end.

The examples align semantically with scenario 06 (ID: 605) in `samples/NCMapper.Samples` and are fully executable for validation. Each section provides concise illustrative code snippets; complete working code is provided at the end for holistic understanding. For full coverage of masking capabilities, see [Data Masking](../how-to/infrastructure/masking.md); this tutorial focuses specifically on integrating masking into a realistic page flow.

---

## 1. Scenario: Customer Profile Page in the Support Backend

Security and compliance requirements:

| Field | Requirement | Example Output |
|-------|-------------|----------------|
| Phone number | Mask middle digits | `138****5678` |
| ID card number | Retain only first 4 and last 2 digits | `1101**********34` |
| Bank card number | Retain only last 4 digits | `************2233` |
| Email address | Mask local part (username) | `u***@example.com` |
| Chinese name | Retain surname only | `张**` |

Key design constraint: **Database entities remain unmodified** (data is always stored in plaintext — required by business logic). Masking occurs *only* during the “Entity → DTO” mapping step used for display.

---

## 2. Step 1: Declare Masking Rules on DTO Properties

NCMapper ships with 9 built-in masking rules. Use the `[Mask]` attribute directly on DTO properties:

```csharp
[MapFrom(typeof(CustomerEntity))]
public class CustomerMaskedDto
{
    public string Name { get; set; } = "";

    [MapProperty(typeof(CustomerEntity), nameof(CustomerEntity.Phone))]
    [Mask(typeof(MaskPhone))]
    public string Phone { get; set; } = "";
}
```

Masking is applied automatically during mapping — no changes required to business logic. The 9 built-in rules and their behaviors:

| Rule | Type | Effect |
|------|------|--------|
| Phone number | `MaskPhone` | `138****5678` |
| ID card number | `MaskIdCard` | `1101**********34` |
| Bank card number | `MaskBankCard` | `************2233` |
| Email address | `MaskEmail` | `u***@example.com` |
| Chinese name | `MaskChineseName` | `张**` |
| Address | `MaskAddress` | Retain province/city, mask remaining parts |
| IP address | `MaskIp` | `192.168.*.*` |
| License plate | `MaskLicensePlate` | `京A***45` |
| Landline | `MaskLandline` | `010-66****88` |

---

## 3. Step 2: Custom Masking Rules (Converter-Based)

When built-in rules are insufficient (e.g., your organization has proprietary masking standards), use `[Convert]` to attach a custom converter — as demonstrated in the sample project’s handling of customer phone numbers (excerpted from shared model `CustomerDto`):

```csharp
public class CustomerDto
{
    public string Name { get; set; } = "";

    [MapProperty(typeof(CustomerEntity), nameof(CustomerEntity.Phone))]
    [Convert(typeof(PhoneMaskConverter), nameof(PhoneMaskConverter.Convert))]
    public string Phone { get; set; } = "";
}
```

The calling code remains unchanged — mapping proceeds normally (identical to demo scenario `Mask_ConvertUsing`):

[!code-csharp[Masking via converter declaration](~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs#Mask_ConvertUsing_MaskedCustomerMap)]

Recommendation: Use `[Mask]` with built-in rules for common patterns (phone, ID, etc.). Use `[Convert]` + custom converters for organization-specific conventions.

---

## 4. Step 3: Fluent API Equivalent Syntax

Prefer to keep DTOs free of masking annotations? The Fluent API offers an equivalent chainable terminal method `.Mask<TMask>()`, declared inside the configuration class’s `Member` section:

[!code-csharp[Fluent API equivalent: .Member().Mask<TMask>()](~/samples/NCMapper.Samples/01-Fluent/FluentModels.cs#FluentBasics_ChainOrderMapper_Configure)]

Here, `TMask` is the masking implementation type (must implement `IMask`, e.g., `MaskPhone`, `MaskIdCard`, `MaskBankCard`, `MaskEmail`, or `MaskChineseName`). The target property is bound via `.Member(d => d.CustomerPhone)` — **no need to repeat the field type inside `Mask<>`**. If `.From(...)` is omitted, the source property is resolved automatically by name; alternatively, you may explicitly specify the source using `.From(s => s.X)` before applying masking (enabling combinations not expressible in attribute-based syntax).

Both approaches are semantically equivalent (and subject to the same mutual-exclusion constraint `NCMAP_F11` for duplicate declarations on the same type pair). Choose based on your team’s established convention.

---

## 5. Step 4: Switch Masking Strategies by Environment

Overseas deployments require phone numbers to be masked per local regulations — **without changing DTO attributes**, swap the masking implementation at runtime:

[!code-csharp[Mask provider: inspect current implementation via Resolve](~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs#Mask_ConvertUsing_ResolveMask)]
[!code-csharp[Mask provider: globally override via Override](~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs#Mask_ConvertUsing_OverrideMask)]
[!code-csharp[Mask provider: restore built-in defaults via Reset](~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs#Mask_ConvertUsing_ResetMasks)]

Overrides apply globally (all mappings using `MaskPhone` switch simultaneously), making them ideal for environment-specific setup at application startup. Frequent switching per request is discouraged.

---

## 6. Reference Table: Quick Verification of Masking Effects

Validation before deployment: compare all 9 built-in masking outputs side-by-side against original values to confirm every field is actually masked:

[!code-csharp[Built-in nine masking types demo](~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs#Mask_BuiltinNine)]

The sample includes a strict assertion worth adopting in your test suite: **masked output must differ from the original value *and* contain masking symbols** — preventing “false masking” (e.g., misconfigured rules resulting in plaintext output mistakenly assumed to be masked).

---

## 7. Integration Note: Working with ORM Projections

If customer lists are projected directly via FreeSql / EF Core (`AsDto` producing DTOs directly), note: **masking is .NET code and cannot be translated to SQL**. DTOs annotated with masking attributes automatically trigger projection fallback (SQL fetches entities, then in-memory mapping applies all attributes). For large lists, this incurs an extra round-trip; see [ORM Degradation Mechanism](../explanation/orm-degradation.md) for detection and optimization guidance.

---

## 8. Frequently Asked Questions

**Does masking modify data in the database?**  
No. Masking applies *only* to mapping results (DTOs). Entities and persisted data remain unaltered in plaintext. Only if a masked DTO is accidentally written back to an entity should you exercise caution (display DTOs must never be used for persistence — see directional ignore guidance in [Daily Bidirectional Mapping Tutorial §6](mapping-tutorial.md)).

**Can I customize the masking symbol (e.g., `*` → `X`)?**  
Yes — via `[Convert]` + custom converter (see Step 3). Built-in rule retention counts are not configurable.

**Can I apply both formatting and masking to the same field?**  
We currently discourage stacking multiple rules on a single property. Composite behavior is defined in [Data Masking](../how-to/infrastructure/masking.md); when needed, split the concern across two distinct fields with separate declarations.

---

## 9. Complete Code Illustration

A consolidated, production-ready implementation combining Steps 2–6 (educational completeness; `JapanPhoneMask` shown as a custom implementation example):

```csharp
using NCMapper;
using NCMapper.Attributes;

// ================= 1. Entity (stored in plaintext — business requirement) =================
public class CustomerEntity
{
    public string Name { get; set; } = "";
    public string Level { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Region { get; set; } = "";
}

// ================= 2. Display DTO (masked for presentation) =================
[MapFrom(typeof(CustomerEntity))]
public class CustomerMaskedDto
{
    public string Name { get; set; } = "";

    [MapProperty(typeof(CustomerEntity), nameof(CustomerEntity.Level))]
    public string Level { get; set; } = "";

    public string Region { get; set; } = "";

    [MapProperty(typeof(CustomerEntity), nameof(CustomerEntity.Phone))]
    [Mask(typeof(MaskPhone))]
    public string Phone { get; set; } = "";
}
```

Step 3 — “Page Service (Mapping = Automatic Masking)” — runnable version where `Phone` renders as `138****5678`:

[!code-csharp[Page service GET: mapping triggers masking](~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs#Mask_MaskingCustomerGet)]

Step 4 — “Environment-Based Strategy Switch (executed once at app startup)” — runnable wrapper for `MaskProvider.Override<MaskPhone>(new JapanPhoneMask())` (domestic deployments retain built-in defaults — zero code required):

[!code-csharp[Environment-based Override registration (startup-time only)](~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs#Mask_ConvertUsing_OverrideMask)]

Step 5 — “Production Smoke Test (place in unit tests)” — runnable hard assertion guarding against “false masking”:

[!code-csharp[Smoke test: masked output must differ from original](~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs#Mask_MaskingSmokeTour)]

Recommended reading order: Entity (plaintext storage) → DTO (`[Mask]` declares rules) → Service (one-line mapping activates masking) → Startup config (environment-driven strategy swap) → Smoke assertion (prevents misconfiguration).

---

## Related Documentation

- [Data Masking](../how-to/infrastructure/masking.md): Full reference of all masking usages and rule tables  
- [Daily Bidirectional Mapping Tutorial](mapping-tutorial.md): Guidance on isolating display DTOs from persistence DTOs  
- [ORM Degradation Mechanism](../explanation/orm-degradation.md): How masking triggers automatic projection fallback in ORM scenarios  
- [Fluent Conversion](../how-to/fluent/convert.md): Declaring converters via the Fluent API
