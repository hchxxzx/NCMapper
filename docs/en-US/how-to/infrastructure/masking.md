> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
wiki-id: how-to/infrastructure/masking
Diagnostic Code: NCMAP034  
Library Version: v1.0.0+  
Last Updated: 2026-09-15  
---

# Data Masking

## Overview

NCMapper provides nine built-in masking rules. Annotate DTO properties with `[Mask(typeof(XxxMask))]` to enable automatic masking during mapping. At runtime, you can replace masking implementations via `MaskProvider.Override<TMask>()`, enabling policy switching across deployment environments (e.g., internationalization scenarios) without modifying DTO attribute annotations.

Core APIs: `[Mask(typeof(MaskPhone))]` / `MaskProvider.Override<TMask>(impl)` / `MaskProvider.Resolve(type)` / `MaskProvider.Clear()`

> **Declarative Dual Channels**: Masking also has an equivalent terminal `.Mask(...)` in the Fluent channel (chainable declaration, no DTO modification required). Both channels coexist and are semantically equivalent (mutually exclusive declarations of the same type pair are enforced at compile time via `NCMAP_F11`). See [Fluent Entry Point](../fluent/basics.md).

### Declaration Channels and Application Surfaces

Masking declarations are registered via two channels; their runtime memory-mapped application surfaces during mapping are as follows:

| Channel | Declaration Form | Memory Mapping (`Mapper.Map`) | ORM Projection (`AsDto`) |
|---|---|---|---|
| Attribute Channel | DTO property `[Mask(typeof(XxxMask))]` | Applied (attribute registration step) | Triggers fallback → two-step mapping applies |
| Fluent Channel | `.Member(d => d.X).Mask<TMask>()` and manual explicit registrations | Applied (registry registration step) | Enforces fallback → two-step mapping applies |
| Fluent Channel (`Compose` cross-table details) | `From<Sub>(…).Mask<TMask>()` for cross-table field declarations | Applied (during memory mapping step of fallback two-step mapping, consuming Compose-detail mask bits) | Enforces fallback (cm-level `Mask` is non-translatable), two-step mapping applies |

Dispatch and boundary notes:

- **Attribute declarations take precedence; registry details fill gaps — no double application on the same property**: Registry details are consumed only when no matching attribute declaration exists (mutual exclusivity of same-type pairs across both channels is enforced at compile time by `NCMAP_F11`; manual dual declarations at runtime throw an exception immediately).
- **Ordered execution for combined annotations**: When `[Convert]` or `[MapFormat]` coexist with registry-based masking on the same property, conversion/formatting occurs *before* masking.
- **Dot-path and flattened leaf nodes apply equally**: Both auto-decomposed paths (e.g., `AddressCity → src.Address.City`) and explicit dot-paths consume registry masking declarations at their leaf nodes.
- **Compose cross-table mask bits apply during memory mapping**: Registry detail resolution simultaneously covers property-level details (pm-level) and Compose cross-table details (cm-level, mask-only). Fields marked with cm-level `Mask` are treated as “non-translatable”, triggering enforced fallback; during the memory mapping phase of the resulting two-step mapping, the same registry detail consumes that mask bit (pm-level takes priority; cm-level fills gaps for the same target property). Cross-table detail value extraction is limited to direct name-matching forms (non-name-matched or multi-segment path extractions fall under documented restrictions — see [Known Limitations](../../explanation/limitations.md)).
- **Unresolvable type names fail explicitly**: Mask type names are resolved in four phases: “target assembly → source assembly → `Type.GetType` → scan loaded assemblies”. A full miss across all phases throws a `MapException`; silent fallback does *not* occur.
- **Observability**: Trace warnings are emitted (but mapping proceeds normally) for cases including: missing type name in mask declaration, no matching property on the target surface, or non-`string` target type.
- **Known Limitation**: Type pairs registered *after* initial mapping have already materialized mapping delegates; those delegates are *not* updated retroactively by later registrations. New type pairs registered *before first mapping* take effect immediately.

---

## 1. Nine Built-in Masking Rules

NCMapper includes the following nine built-in masking rules, covering common PII (Personally Identifiable Information) fields:

| Rule | Class | Masking Effect | Retention Rule |
|---|---|---|---|
| Phone Number | `MaskPhone` | `138****5678` | First 3 + last 4 digits |
| ID Card Number | `MaskIdCard` | `1101**********1234` | First 4 + last 4 digits; middle replaced with equal-length `*`s |
| Bank Card Number | `MaskBankCard` | `6222****1234` | First 4 + last 4 digits |
| Email Address | `MaskEmail` | `z***@example.com` | First character + domain after `@` |
| Chinese Name | `MaskChineseName` | `张*丰` | Family name + last character; middle replaced with `*` |
| Address | `MaskAddress` | `北京市朝****` | First 6 characters (province/city) |
| IP Address | `MaskIp` | `192.168.*.*` | First two octets |
| License Plate | `MaskLicensePlate` | `京A***45` | First 2 + last 2 characters; middle replaced with equal-length `*`s |
| Landline Number | `MaskLandline` | `010-****5678` | Area code (including `-`) + last 4 digits |

### Usage

Annotate DTO properties with `[Mask(typeof(YourMaskClass))]`:

```csharp
using NCMapper;
using NCMapper.Attributes;

// MaskUserEntity stores plaintext (Phone/Email are unmasked)
[MapFrom(typeof(MaskUserEntity))]
public class MaskedUserDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    // Phone masking: 138****5678
    [Mask(typeof(MaskPhone))]
    public string Phone { get; set; } = "";

    // Email masking: z***@example.com
    [Mask(typeof(MaskEmail))]
    public string Email { get; set; } = "";
}
```

A runnable version where masking happens automatically during mapping (`dto.Phone == "138****5678"`, `dto.Email` in masked form):

[!code-csharp[Mapping with immediate masking (`MaskPhone`/`MaskEmail` annotations)](~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs#Mask_MaskedUserMap)]

**Key Points**:

- `[Mask]` is applied to **DTO properties**, *not* Entity properties — masking is a DTO-layer responsibility.
- During mapping execution, the source generator retrieves the masking instance via `MaskProvider.Resolve<TMask>()` during static field initialization.
- Masking runs *after* property assignment but *before* returning the final mapped result — fully transparent to users.
- Built-in rules use `StringInfo` to slice strings by text elements, correctly handling CJK Ext B+ rare characters (U+20000+ surrogate pairs) and emoji.

### Custom Masking Rules

Implement the `IMask` interface to define custom masking logic:

```csharp
public class JapanPhoneMask : IMask
{
}
```

A runnable implementation of `Mask` (Sample Scenario 605: retain first 3 + last 2 digits, mask middle with `*`):

[!code-csharp[Custom masking implementation: `JapanPhoneMask.Mask`](~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs#Mask_JapanPhoneMask_Mask)]

---

## 2. Runtime Replacement of `MaskProvider`

`MaskProvider` serves as the global registry for masking implementations, allowing replacement of built-in `IMask` implementations at application startup — without modifying `[Mask]` attributes on DTOs.

### 2.1 `Override`: Replace a Masking Implementation

[!code-csharp[Runtime mask implementation replacement (`Override`)](~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs#Mask_ConvertUsing_OverrideMask)]

### 2.2 `Resolve`: Retrieve the Current Masking Instance

[!code-csharp[Retrieving current mask instance via `MaskProvider.Resolve` (generic and non-generic overloads)](~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs#Mask_ConvertUsing_ResolveMask)]

### 2.3 `Clear`: Restore Default Implementations

[!code-csharp[Clearing all overrides to restore built-in defaults](~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs#Mask_ConvertUsing_ResetMasks)]

### 2.4 Complete Example: Internationalization Scenario Switching

[!code-csharp[Conditional `Override` for multi-region deployments — zero changes to mapping code](~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs#Mask_MultiLocaleSwap)]

**Key Points**:

- `Override` is global and affects *all subsequent* mapping calls.
- `Override` must be called *before the first mapping invocation* that uses the given `Mask` type (the source generator resolves `MaskProvider.Resolve<TMask>()` once during static field initialization; subsequent mappings bypass `Resolve` entirely).
- `Clear()` removes all overrides, but *does not reset* already-initialized static fields in generated code — requires either re-triggering code generation or process restart.
- `Mapper.ClearCache()` automatically invokes `MaskProvider.Clear()` — it is the complete cache reset entry point.

> ⚠️ **Mask Runtime Fail-Fast**: If a type referenced in `[Mask]` does *not* implement `IMask`, or cannot be instantiated, `CreateMapFunc` throws `MapConfigurationException` *immediately* at the very beginning — never silently skipping. This is intentional: masking involves compliance requirements (GDPR, PIPL), so misconfiguration must be surfaced early.

---

## 3. `IMask` Interface and Implementation Guidelines

```csharp
/// <summary>
/// Masking interface — all masking rules must implement this interface.
/// </summary>
public interface IMask
{
    /// <summary>
    /// Applies masking transformation to the input value.
    /// </summary>
    /// <param name="value">Original value (may be null)</param>
    /// <returns>Masked string</returns>
    string Mask(string? value);
}
```

**Implementation Guidelines**:

1. The `Mask` method *must* handle `null` and empty strings (return empty string or original value, per business needs).
2. **Degraded masking for non-conforming inputs**: For insufficient length or invalid format, *do not return the original plaintext*. Instead, uniformly degrade to “first text element + `****`” (e.g., `MaskPhone("123456")` → `"1****"`, `MaskIp("2001:db8::1")` → `"2****"`). This fail-safe behavior prioritizes over-masking over accidental leakage. Custom `IMask` implementations should follow the same contract.
3. Use `StringInfo` to slice strings by text elements — avoid splitting inside surrogate pairs (critical for CJK Ext B+ rare characters and emoji).
4. Masking logic must be pure (no side effects, no external state dependencies).

---

## 4. Difference Between `Mask` and `Convert`

| Dimension | `[Mask]` | `[Convert]` |
|---|---|---|
| Responsibility | Masking (compliance-driven, irreversible) | Data transformation (formatting, composition, etc.) |
| Interface/Form | Instance method `IMask.Mask(string?)` | Static conversion methods (T0–T3 templates) |
| Input | Single string | Source field value / source object (per template) |
| Output | Masked string | Any target type |
| Runtime Replacement | Supported (`MaskProvider.Override`) | Not supported (compile-time bound) |
| Typical Use Cases | Phone numbers, ID cards, email addresses | Date formatting, multi-field composition |

**Recommendation**: Use `[Mask]` when the goal is *hiding sensitive information to meet compliance requirements*. Use `[Convert]` when the goal is *transforming data format*.

---

## Common Misuses

> ⚠️ **Do NOT write masked DTOs back to Entities**: `[Mask]` is *forward-only*. Reverse mapping (DTO → Entity, including direct calls like `dto.MapToXxxEntity()`) **does not invoke the masker**, but instead writes the DTO property’s *current value* directly into the entity. If that value is the result of forward masking, the *masked value* will overwrite the entity’s original full data (lossy round-trip). The generator emits diagnostic warning `NCMAP034` (Info) for this case. In CRUD update scenarios, block reverse writes for masked DTO properties using `[MapIgnore(Direction.ToEntity)]`:
>
> ```csharp
> [MapFrom(typeof(UserEntity))]
> public class UserDto
> {
>     [Mask(typeof(MaskPhone))]
>     [MapIgnore(Direction.ToEntity)]  // Prevent masked value from writing back to entity
>     public string Phone { get; set; } = "";
> }
> ```

### Misuse 1: Applying `[Mask]` to Entity Properties

```csharp
// ❌ Incorrect: `[Mask]` on Entity has no effect
public class UserEntity
{
    [Mask(typeof(MaskPhone))]  // Masking is a DTO-layer concern
    public string Phone { get; set; }
}

// ✅ Correct: `[Mask]` applied to DTO property
[MapFrom(typeof(UserEntity))]
public class UserDto
{
    [Mask(typeof(MaskPhone))]
    public string Phone { get; set; } = "";
}
```

### Misuse 2: Calling `Override` After Mapping Has Started

```csharp
// ❌ Incorrect: Mapping first, then `Override` — override has no effect
var dto = Mapper.Map<UserEntity, UserDto>(entity);  // Already resolved default `MaskPhone`
MaskProvider.Override<MaskPhone>(new JapanPhoneMask());  // Too late!

// ✅ Correct: `Override` must happen before any mapping call
MaskProvider.Override<MaskPhone>(new JapanPhoneMask());
var dto = Mapper.Map<UserEntity, UserDto>(entity);  // Uses `JapanPhoneMask`
```

> Reason: The source generator calls `MaskProvider.Resolve<TMask>()` *once*, during static field initialization. Subsequent mappings reuse the cached instance. `Override` only affects *future* `Resolve` calls — it does *not* mutate already-initialized static fields.

### Misuse 3: Custom `Mask` That Does Not Handle `null`

```csharp
// ❌ Incorrect: No null handling → `NullReferenceException` at runtime
public class BadMask : IMask
{
    public string Mask(string? value) => value[..3] + "****";  // Crashes if `value` is null
}

// ✅ Correct: Handles null and empty strings
public class GoodMask : IMask
{
    public string Mask(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? "";
        return value.Length >= 5 ? value[..3] + "****" + value[^2..] : value;
    }
}
```

## Summary

| Capability | API | Feature |
|---|---|---|
| Built-in masking | `[Mask(typeof(MaskPhone))]` | 9 built-in rules; annotation alone activates masking |
| Custom masking | Implement the `IMask` interface | Arbitrary masking logic |
| Runtime replacement | `MaskProvider.Override<TMask>(impl)` | Global replacement without modifying DTOs |
| Instance resolution | `MaskProvider.Resolve<TMask>()` | Generic (source generator) / non-generic (runtime) |
| Restore defaults | `MaskProvider.Clear()` | Clear all overrides |
| Full reset | `Mapper.ClearCache()` | Clear mapping cache + `MaskProvider` state |

---

This page contains runnable demos (Scenario 605):

[!code-csharp[Comparison of 9 built-in masking implementations (original value → masked value)](~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs#Mask_BuiltinNine)]

[!code-csharp[Custom `IMask` registration and restoration (Provider layer)](~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs#Mask_CustomProvider)]

## Companion Examples

```bash
cd samples/NCMapper.Samples
dotnet run --scenario 605
```

Core example source code (single-source-of-truth; verifiable via `dotnet run --scenario`):

[!code-csharp[Masking mapping](~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs#Mask_ConvertUsing)]

[!code-csharp[Masking mapping · `MaskedCustomerMap`](~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs#Mask_ConvertUsing_MaskedCustomerMap)]

[!code-csharp[Masking mapping · `ResolveMask`](~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs#Mask_ConvertUsing_ResolveMask)]

[!code-csharp[Masking mapping · `OverrideMask`](~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs#Mask_ConvertUsing_OverrideMask)]

[!code-csharp[Masking mapping · `ResetMasks`](~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs#Mask_ConvertUsing_ResetMasks)]

Source code is located at `samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs`, covering four sub-scenarios: built-in masking, runtime `Override`, `Resolve` validation, and `Clear` restoration.

---

## Further Reading

- Related topics: [Basic Mapping](../declarative/basics.md) · [Pipeline](pipeline.md)
- In-depth technical documentation: [Feature System](../../reference/features.md) §5.5
- Masking rule source code: `src/NCMapper/Masks.cs` (9 built-in rules)
- Provider source code: `src/NCMapper/MaskProvider.cs` (`Override`/`Resolve`/`Clear`)
