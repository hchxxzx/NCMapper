> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

<!-- Source: NCMapper Main Technical Documentation §22 Common Pitfalls and Diagnostics -->

## 22. Common Pitfalls and Diagnostics

| Pitfall | Phenomenon | Correct Handling |
|---------|------------|------------------|
| `[MapProperty]` with dotted path `xx.xx` | **Supported by all four engines**; failure is explicitly observable: source generator emits NCMAP001/005 Warning (with failing segment name) and omits mapping generation; runtime throws `MapConfigurationException` (explicit failure—no silent skip); projection skips the property + logs a trace | Dotted paths are parsed segment-by-segment (segment matching uses `OrdinalIgnoreCase`; intermediate nodes must be classes/interfaces—not value types or `string`); `[Mask]`/`[Convert]` × dotted paths are currently not consumed (observable diagnostics: NCMAP030 warning, not yet consumed); cross-table navigation remains available via explicit `[MapProperty(typeof(NavType), "Field")]` specifying the navigation entity type |
| `T[].Contains(T)` in expression tree lambdas | In .NET 10 / C# 14, binds to `MemoryExtensions.Contains`, which FreeSql cannot translate → throws `NotSupportedException` at runtime | Replace with `idArray.AsEnumerable().Contains(...)`; source generator issues NCMAP018 warning as a reminder |
| FreeSql multi-table queries replace only single-table data sources | In `WithSql(sqlT1, sqlT2, …)`, `sqlT2` is mandatory—cannot replace only `T1` | For multi-table `WithSql`, data sources must be provided for *all* tables involved |
| Naming convention flattening ≠ dotted path | `AddressCity → src.Address.City` automatically splits the target property name at uppercase boundaries; unrelated to manually written `.`-delimited paths | Dotted path `"Address.City"` requires explicit declaration via `[MapProperty(typeof(T), "Address.City")]`; flattening is auto-triggered by naming convention deduction |
