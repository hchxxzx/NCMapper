> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
title: Multilingual Support and Localization
---

# Multilingual Support and Localization

NCMapper’s localization is implemented at two independent levels:

1. **Runtime error message localization** (`ErrorMessages`) — The library automatically selects **Chinese or English** error messages based on the current thread’s `CultureInfo` when throwing exceptions.
2. **Documentation multilingual support** — The primary user documentation is in Chinese, with translations stored under the `locales/` directory.

These two mechanisms are entirely separate; each is described below.

---

## 1. Runtime Error Message Localization (`ErrorMessages`)

All runtime exception messages, validation failure messages, and diagnostic text in the library are uniformly provided by the `ErrorMessages.cs` resource class. It ships with built-in **bilingual (Chinese/English) message strings**, automatically switching between them based on the current thread’s `CultureInfo.CurrentUICulture` — **no configuration required**.

### Language Selection Rules

| Current Thread `CurrentUICulture.Name` | Error Message Language |
|---|---|
| Starts with `en` (e.g., `en`, `en-US`, `en-GB`) | **English** |
| All others (including `zh-CN`, `zh-Hans`, default) | **Chinese** (default) |

> **Runtime switching**: Language selection is performed **dynamically on every access**, reading `CurrentUICulture` each time. Thus, within the same process, switching the thread’s culture at runtime immediately yields messages in the corresponding language.

### Coverage Scope

`ErrorMessages` covers all runtime exception and diagnostic text, including (but not limited to):

- **Mapping Engine**: Recursion depth exceeded, missing `[MapFrom]` attribute, no public constructor, no mappable properties, null field value for `[MapProperty]`, `[Convert]` converter exceptions, `[Mask]` desensitization parsing failures, type conversion failures (including empty/invalid enum strings), dot-path segment failures, reverse write-back path failures, etc.
- **Validator Family** (`MapValidator`): Validation messages for report rows / summary prefixes / detail items (unmapped, type-incompatible), `PrintReport` header and total rows, downgrade reason row prefixes, Fluent declaration-to-attribute mismatches — all switch with `CurrentUICulture`; **validator messages emitted by the source generator are hardcoded in English** (frozen at compile time, not culture-aware — see boundary notes below).
- **Tree Capabilities**: Missing `Children` property in tree DTOs, mismatched `Children` property name, missing `Id`/`ParentId`, invalid tree mount position, etc.
- **Collections / Deep Copying**: Unknown collection types, unsupported `List` ↔ `Dictionary` conversions, complete deep-copy fallback failures, incompatible constructors, etc.
- **ORM Extensions**: Missing Dapper/`MapList` overloads, DTO missing `[MapFrom]`, invalid pagination/batch parameters, empty/multi-element sequences, etc.
- **Global Configuration**: Assembly conflicts in `GlobalConfig`, out-of-range naming strategy enum values, etc.

A complete list of methods is available in `ErrorMessages.cs` (`src/NCMapper/ErrorMessages.cs`).

### Usage Example

A runnable demonstration of bilingual error message switching (switching `CurrentUICulture` before and after mapping a deliberately failing scenario, then printing `MapException.Message` — e.g., `zh-CN` yields “类型转换失败…”, `en-US` yields "Type conversion failed…"; this demo does not integrate into any UI menu, and restores the thread culture afterward):

[!code-csharp[Error Message Bilingual Switching (`CurrentUICulture` → `MapException.Message` Comparison)](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_CultureBilingualTour)]

### Boundary Notes

> **Compile-time Diagnostic Bilingual Support**: Source generator and compile-time diagnostics (`NCMAP001`–`NCMAP034`) are already localized via Roslyn resource files (`resx`) — `DiagnosticResources.resx` (default English) + `DiagnosticResources.zh-CN.resx` (Chinese) — operating independently from the runtime `ErrorMessages` mechanism. The IDE’s UI culture (`UICulture`) determines which language is displayed.
>
> **Source Generator Validator Messages Are Hardcoded in English**: Validator delegates emitted by the source generator (i.e., the `Message` prefix/detail inside results returned by `Mapper.Validate()`) are compiled as fixed English strings (e.g., `, 1 incompatible type(s): ...`) and do **not** change with runtime culture. In Chinese environments, mixed output may occur ("outer Chinese + inner English") — this is by design. In contrast, messages from reflection-based runtime validation paths (`MapValidator.ValidateMapping`) *do* respect culture.

---

## 2. Documentation Multilingual Support

The Chinese documentation is the authoritative source. Translations are generated via an automated translation workflow and stored in language-specific subdirectories under the repository root `locales/` (e.g., `locales/en/NCMapper.xml`). Translations are derived from and must be kept synchronized with the Chinese source; after updating the Chinese documentation, translations must be regenerated and manually reviewed.

---

## 3. Related References

- Runtime exception types: [MapException](./core-api.md) (`MapException` / `MapConfigurationException`)
- Compile-time diagnostic rules: [Diagnostic Code Rules](../diagnostics/rules.md)
