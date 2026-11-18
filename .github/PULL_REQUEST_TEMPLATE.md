<!--
  Thanks for contributing to NCMapper. Please fill in each section below.
  Reviewers read the "Verification" section first — an empty one usually means
  the PR is not ready yet.
-->

## Summary

<!-- What does this change, and why? One or two sentences. -->

## Related issue

<!-- Fixes #123 / Closes #123 / No issue (explain why a PR is still warranted). -->

## Type of change

- [ ] Bug fix (non-breaking change that fixes an issue)
- [ ] New feature (non-breaking change that adds capability)
- [ ] Breaking change (existing behaviour or public API changes)
- [ ] Documentation only
- [ ] Tooling / CI / build
- [ ] Performance

## Verification

<!--
  Do not delete this section. Paste the commands you ran and their outcome.
  CI runs the same gates; if you cannot run one of them, say so explicitly.
-->

- [ ] `dotnet build NCMapper.Lib.slnf -c Release --warnaserror` passes
- [ ] `dotnet test NCMapper.slnx -c Release` passes on net8.0 / net9.0 / net10.0
- [ ] `dotnet test NCMapper.slnx` (full solution, all phases) passes
- [ ] Public API snapshot updated if the public surface changed (`PublicApiTests`)
- [ ] Generated-code byte baseline updated if codegen output changed
- [ ] New behaviour is covered by tests

<details>
<summary>Command output</summary>

```
# Paste build / test output here.
```

</details>

## Documentation

<!--
  User-visible changes need matching docs. The Chinese site (docs/zh-CN) is the
  authoritative source; the English site (docs/en-US) mirrors its structure.
-->

- [ ] `docs/zh-CN/` updated (if user-visible behaviour changed)
- [ ] `docs/en-US/` updated to mirror the Chinese change
- [ ] `CHANGELOG.md` updated under `## [Unreleased]`
- [ ] Not applicable — internal change with no user-visible effect

## Breaking change checklist

<!-- Skip this section entirely if the change is not breaking. -->

- [ ] The breaking change is described in `CHANGELOG.md` with migration guidance
- [ ] Affected documentation pages are updated
- [ ] Affected sample scenarios are updated
- [ ] An ADR is added under `docs/zh-CN/explanation/adr/` if the decision is architectural

## Notes for reviewers

<!-- Anything tricky, anything you are unsure about, anything you deliberately left out. -->
