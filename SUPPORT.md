# Support

## Where to get help

| I want to... | Go to |
|---|---|
| Read the documentation | [README](README.md) → [docs/zh-CN](docs/zh-CN/index.md) (authoritative) / [docs/en-US](docs/en-US/index.md) |
| Check whether a behaviour is intended | [How-to FAQ](docs/en-US/how-to/faq.md) and [Common pitfalls](docs/en-US/how-to/pitfalls.md) |
| Ask how to do something | [GitHub Discussions](https://github.com/hchxxzx/NCMapper/discussions) |
| Report a defect | [Open a bug report](https://github.com/hchxxzx/NCMapper/issues/new?template=bug_report.yml) |
| Request a feature | [Open a feature request](https://github.com/hchxxzx/NCMapper/issues/new?template=feature_request.yml) |
| Report a security vulnerability | See [SECURITY.md](SECURITY.md) — do **not** open a public issue |
| Ask about contributing | See [CONTRIBUTING.md](CONTRIBUTING.md) |

## What is supported

**Supported**

- The `net8.0`, `net9.0`, and `net10.0` target frameworks, with the .NET SDK version pinned in `global.json`.
- The public API surface as published in the released NuGet packages.
- The documented configuration channels: the Fluent configuration class, the attribute channel, and the ORM integrations.

**Not supported**

- Internal types, members marked `internal`, and anything not visible in the public API snapshot. These may change in any release without notice.
- Building from a source snapshot that is not a tagged release.
- The benchmark and sample projects as production code — they exist to demonstrate and measure, not to be depended upon.
- Modified source trees. If you patch the library locally, please reproduce the problem against a clean release before filing an issue.

## Response expectations

NCMapper is maintained by a single person in their own time. There is no service-level agreement, and no guaranteed response time. Reports that include a minimal reproduction and exact version numbers are handled first, because they can usually be resolved in one pass. Issues without a reproduction may be closed as unactionable.

## Before filing an issue

Please work through this list — a large share of incoming reports turn out to be one of these:

1. Confirm you are on the latest release.
2. Run `dotnet build` and read the full compiler output. NCMapper reports problems as diagnostics (`NCMAP019` and friends) rather than as runtime surprises; the diagnostic code and its documentation page usually explain the cause.
3. If a mapping silently does not apply, check the ownership rules: mapping configuration is attached to the class that carries the mapping anchor, and configuration declared somewhere else is not consumed. See the three principles in [Mapping Configuration Ownership](docs/en-US/explanation/semantics/mapping-config-ownership.md).
4. Reduce your case to the smallest types that still show the problem. Most reports shrink to under twenty lines during this step, and a fair number turn out to be a naming mismatch.
5. Search the existing issues and discussions.

## Commercial support

None is offered. The project is provided under the Apache-2.0 license, without warranty, on an as-is basis.
