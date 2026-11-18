# Security Policy

> **中文版本：** [SECURITY.zh-CN.md](SECURITY.zh-CN.md)

This document describes the security reporting process, contributor security requirements, and dependency security policy for the NCMapper project.

## Reporting a Vulnerability

If you discover a security vulnerability in NCMapper, please follow this process:

1. **Do not** report security vulnerabilities in public GitHub Issues.
2. Report privately via GitHub's [Security Advisory](https://github.com/hchxxzx/NCMapper/security/advisories/new) feature, or email the maintainers (see the repository's maintainer list).
3. Please include in your report:
   - Description of the vulnerability and its impact
   - Reproduction steps (a minimal repro is best)
   - Affected versions
   - A suggested fix, if any
4. Maintainers will confirm receipt within 72 hours and provide a remediation plan within 14 days of triage.
5. Please do not publicly disclose vulnerability details before a fix is released.

## Two-Factor Authentication (2FA) Requirement

To protect the code repository and release pipeline, NCMapper requires all contributors and maintainers to enable two-factor authentication:

- **All maintainers** (Direct/Collaborator access) must enable GitHub two-factor authentication (2FA).
- **All contributors** are strongly encouraged to enable 2FA before submitting Pull Requests; for frequent contributors, maintainers may require 2FA before merging.
- NuGet publishing API keys are held only by maintainers with 2FA enabled, and are rotated periodically.
- Maintainer accounts without 2FA enabled will have repository access removed until 2FA is restored.

> GitHub 2FA enablement: <https://docs.github.com/en/authentication/securing-your-account-with-two-factor-authentication-2fa>

## Dependency Security Policy

NCMapper monitors and manages third-party dependency security through the following mechanisms:

- **NuGetAudit**: `<NuGetAuditMode>direct</NuGetAuditMode>` is configured in `Directory.Build.props`, and `auditSources` in `NuGet.config`. Every restore and build scans known vulnerability databases for both direct and transitive dependencies.
- **Dependabot**: Enabled via `.github/dependabot.yml` for security and version updates, automatically opening PRs for vulnerable dependencies.
- **Central Package Management**: NuGet package versions are centrally managed in `Directory.Packages.props`; version changes require maintainer review to prevent supply-chain injection.
- **Source pinning**: `NuGet.config` uses `<clear />` to drop inherited sources, keeping only official `nuget.org`, with `packageSourceMapping` restricting package origins.
- **Lock files**: `RestorePackagesWithLockFile` is enabled; CI restores with `--locked-mode` for reproducible builds.

## Security-Related Configuration Reference

- `NuGet.config` — package sources, source mapping, vulnerability audit sources
- `Directory.Build.props` — `NuGetAuditMode`, Source Link, deterministic builds
- `Directory.Packages.props` — central package version management
- `.github/dependabot.yml` — Dependabot configuration
