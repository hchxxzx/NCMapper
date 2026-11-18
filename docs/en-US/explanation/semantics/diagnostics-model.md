> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
wiki-id: explanation/semantics/diagnostics-model
Affected Diagnostic IDs: NCMAP099  
Library Version: v1.0.0+  
Last Updated: 2026-09-28  
---

# Semantic Sharing Layer: Diagnostic Code Model

> This page is the **single-source-of-truth** documentation for the diagnostic code model. The complete list of diagnostic codes is available in [diagnostics/rules.md](../../diagnostics/rules.md) (the sole authoritative full table, reconciled across three parties with `AnalyzerReleases`); this page covers only the model itself.

## 1. Two Naming Families

| Family | Format | Coverage |
|--------|--------|----------|
| Feature/Engine Family | `NCMAP001`–`NCMAP042` + `NCMAP099` | Diagnostics for feature channel declarations and engine behavioral issues (e.g., deep copy, `Compose`, ORM projection, global configuration, etc.) |
| Fluent Family | `NCMAP_F02`–`NCMAP_F60`, `NCMAP_F62`–`NCMAP_F63` | Diagnostics for Fluent chain-based declarations (e.g., lambda shape validation, chain constraints, terminal semantic conflicts, explicit rendering of silent semantics, etc.) |

`NCMAP004`, `NCMAP008`, and Fluent family codes `F13`, `F33`, `F34`, and `F61` are reserved placeholders; these IDs are permanently retired and will never be reused.

## 2. Three-Tier Severity Semantics

| Level | Behavior | Design Philosophy |
|-------|----------|-------------------|
| Error | Blocks compilation | Used for “compile-time failure” scenarios such as declaration contradictions or unsatisfiable signatures |
| Warning | Does not block compilation; recommendation to address | Indicates likely unintentional code — possibly intentional but commonly an omission (messages provide bidirectional remediation guidance) |
| Info | Informative only | Highlights deliberate degradations, skips, or limitations — intended to ensure **observability**, not ignorable silence |

## 3. Tripartite Synchronization Discipline

New diagnostic IDs must be registered synchronously across all three parties (G3 gate enforced via automated checks):

1. `src/NCMapper.SourceGen/AnalyzerReleases.Shipped.md` / `Unshipped.md` (Roslyn-standard release tracking, under the *New Rules* section);  
2. `docs/zh-CN/diagnostics/rules.md` (the single user-facing authoritative full table);  
3. Source-code descriptors (ID + severity — descriptors serve as the canonical source of truth).

The script `scripts/check-diagnostics-sync.sh` (executed as a CI G3 step) asserts that:  
- ID sets are identical across all three sources,  
- Severities match exactly, and  
- No duplicate registrations exist.  

When adding new diagnostic IDs to documentation, registration across all three sources must occur simultaneously. Violations cause immediate CI failure (red build).

## 4. Three Principles vs. Analyzers

The three foundational mapping rules map one-to-one with corresponding compile-time analyzers:

| Rule | Analyzer |
|------|----------|
| P1 Channel Exclusivity (Only one channel declaration allowed per type pair) | `NCMAP_F11`, `NCMAP025` |
| P2 Side Exclusivity (`[MapFrom]` allows only one anchor per class; `For<A,B>` and `For<B,A>` must not coexist in the same declaration) | `NCMAP025`, `CS0579` |
| P3 Configuration Anchoring to Class (Property-level attributes without a class anchor are ignored) | `NCMAP019`, `NCMAP042` |

The full mapping table is available in [diagnostics/rules.md — Three Principles vs. Analyzers](../../diagnostics/rules.md).

## 5. Suppression and Fallback

- Per-ID suppression: `<NoWarn>NCMAP001</NoWarn>` (not applicable to `Error`-level diagnostics).  
- `NCMAP099`: Fallback warning triggered when a single mapping source fails to generate (fallback to runtime path; other mappings remain unaffected). If reproducible, please file an issue.

## 6. Further Reading

- [diagnostics/rules.md](../../diagnostics/rules.md) (Complete diagnostic code catalog)  
- [Validation and Diagnostics](../../how-to/infrastructure/validation.md) (Runtime validation and trace-oriented operations)
