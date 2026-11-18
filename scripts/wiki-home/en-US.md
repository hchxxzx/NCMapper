<!-- wiki-sync:langbar -->
[ 🌐 **English** | [中文](Zh-Home) ]

# NCMapper Wiki

> Object mapping for .NET with a **dual engine** (compile-time source generator + runtime expression trees)
> and **dual authoring channels** (fluent chains + attributes).
> Declare once — the same rules drive in-memory mapping, ORM projection, deep copy and multi-source composition.
> Apache-2.0, free forever. Native AOT friendly.

**Repo**: [github.com/hchxxzx/NCMapper](https://github.com/hchxxzx/NCMapper) · **NuGet**: [NCMapper](https://www.nuget.org/packages/NCMapper) · **Docs**: [English](https://github.com/hchxxzx/NCMapper/tree/main/docs/en-US) / [中文](https://github.com/hchxxzx/NCMapper/tree/main/docs/zh-CN)

```bash
dotnet add package NCMapper --prerelease
```

## Three lines to get going

```csharp
[MapperConfig]
public class UserMapperConfig
{
    public static void Configure(FluentMappingConfig cfg)
        => cfg.For<UserEntity, UserDto>();   // same-name auto-matching — that's the whole declaration
}

var dto = Mapper.Map<UserEntity, UserDto>(user);   // code generated at compile time, zero runtime setup
```

## Start here

| Goal | Entry point |
| --- | --- |
| Get it running | [Quickstart](En-GettingStarted-Quickstart) · [Installation](En-GettingStarted-Installation) · [15-minute tutorial](En-GettingStarted-Tutorial15min) |
| Understand the design | [Overview](En-GettingStarted-Overview) |
| Migrating from AutoMapper | [Migration guide](En-GettingStarted-MigratingFromOtherMappers) |
| Hands-on scenarios | "How-to Guides" in the sidebar: fluent declarations / ORM projection / deep copy / masking / AOT / DI |
| Follow a tutorial | [Mapping](En-Tutorials-MappingTutorial) · [Fluent modeling](En-Tutorials-FluentModelingTutorial) · [ORM projection](En-Tutorials-OrmProjectionTutorial) · [Web integration](En-Tutorials-WebIntegrationTutorial) |
| Understand the mechanics | [Dual engine](En-Explanation-DualEngine) · [Dual channel](En-Explanation-DualChannel) · [Adaptive routing](En-Explanation-AdaptiveRouting) · [Projection degradation](En-Explanation-OrmDegradation) |
| Troubleshooting | [FAQ](En-Faq) · [Pitfalls](En-Pitfalls) · [Known limitations](En-Explanation-Limitations) |

## What it gives you

- **Compile-time first** — declared type pairs are turned into mapping code by a source generator; anything undeclared falls back to runtime expression trees, with identical semantics on both paths.
- **Projection for three ORMs** — dedicated packages for EF Core, FreeSql and Dapper. A single `AsDto()` terminal drives a straight SQL-to-DTO query, with automatic two-step fallback when a rule can't be translated.
- **Deep copy** — three strategies; the source-generated path runs at roughly 179 ns.
- **Data masking** — nine built-in rules plus custom `IMask` implementations.
- **Multi-source composition** — `Compose()` assembles an aggregate DTO from several sources.
- **Compile-time diagnostics** — 68 `NCMAP0xx` / `NCMAP_Fxx` rules across Error / Warn / Info levels.

## Target frameworks

net8.0 / net9.0 / net10.0

---

<!-- wiki-sync:generated -->
<!-- 页面由 docs/en-US 自动同步，请勿在 Wiki 直接编辑；修改请提 PR 到仓库 docs 目录 -->

The full page index is in the sidebar. All pages are synced automatically from
[`docs/en-US`](https://github.com/hchxxzx/NCMapper/tree/main/docs/en-US) —
**please don't edit them directly in the Wiki**; send a PR to the docs directory instead.

| Domain | What's in it |
| --- | --- |
| Aot | Trimming and Native AOT deployment |
| Fluent | Chain declarations: basics / conversion / formatting / read-from-source / hooks / terminals |
| Declarative | Attribute-based mapping: basics / advanced / map formats |
| Orm | EF Core / FreeSql / Dapper projection and paging |
| DeepCopy | Deep-copy strategies |
| Compose | Multi-source composition |
| Infrastructure | DI / global config / masking / performance / pipeline / validation |
| Mapping | Dot paths and mapping lists into a parent object |
| Other | FAQ, pitfalls and other standalone how-to pages |
