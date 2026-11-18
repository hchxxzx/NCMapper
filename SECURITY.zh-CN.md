# 安全策略

> **English version:** [SECURITY.md](SECURITY.md)

本文件描述 NCMapper 项目的安全报告流程、贡献者安全要求与依赖安全策略。

## 报告安全漏洞

如果您发现 NCMapper 存在安全漏洞，请按以下流程报告：

1. **不要**在公开的 GitHub Issue 中提交安全漏洞。
2. 通过 GitHub 的 [Security Advisory](https://github.com/hchxxzx/NCMapper/security/advisories/new) 功能 privately 报告，或发送邮件至维护者邮箱（见仓库 maintainer 列表）。
3. 报告内容请包含：
   - 漏洞描述与影响范围
   - 复现步骤（最小可复现示例最佳）
   - 受影响的版本
   - 建议的修复方案（如有）
4. 维护者将在 72 小时内确认收到报告，并在评估后于 14 天内给出修复计划。
5. 修复发布前请勿公开披露漏洞详情。

## 双因素认证（2FA）要求

为保护代码仓库与发布管线，NCMapper 强制要求所有贡献者与维护者启用双因素认证：

- **所有维护者**（仓库 Direct/Collaborator 权限）必须启用 GitHub 双因素认证（2FA）。
- **所有贡献者**在提交 Pull Request 前建议启用 2FA；对于频繁贡献者，维护者有权要求其启用 2FA 后再合入 PR。
- 发布到 NuGet 的 API Key 仅由启用 2FA 的维护者持有，并定期轮换。
- 未启用 2FA 的维护者账号将被移除仓库权限，直至启用后恢复。

> GitHub 2FA 启用方法：<https://docs.github.com/en/authentication/securing-your-account-with-two-factor-authentication-2fa>

## 依赖安全策略

NCMapper 通过以下机制监控与管理第三方依赖安全：

- **NuGetAudit**：在 `Directory.Build.props` 中配置 `<NuGetAuditMode>direct</NuGetAuditMode>`，并在 `NuGet.config` 配置 `auditSources`。每次还原与构建时自动扫描已知漏洞数据库，对直接与传递依赖进行漏洞审计。
- **Dependabot**：通过 `.github/dependabot.yml` 启用 Dependabot 安全更新与版本更新，自动针对存在漏洞的依赖提交 PR。
- **Central Package Management**：使用 `Directory.Packages.props` 集中管理 NuGet 包版本，版本变更需经维护者审查，避免供应链注入。
- **包源锁定**：`NuGet.config` 使用 `<clear />` 清除继承的源，仅保留官方 `nuget.org`，并通过 `packageSourceMapping` 限制包来源。
- **锁文件**：启用 `RestorePackagesWithLockFile`，CI 构建使用 `--locked-mode` 还原，确保依赖可复现。

## 安全相关配置参考

- `NuGet.config` — 包源、包源映射、漏洞审计源
- `Directory.Build.props` — `NuGetAuditMode`、Source Link、确定性构建
- `Directory.Packages.props` — 集中包版本管理
- `.github/dependabot.yml` — Dependabot 配置（如有）
