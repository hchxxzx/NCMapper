# 参考（Reference）

NCMapper 参考资料分两层：

- **API 参考（自动生成）**：由 DocFX 从主库 XML 注释生成（`src/NCMapper/*.csproj` → `docfx metadata`），与代码单点同源、始终同步。
- **叙事参考章**：讲「为什么这样设计」与「完整配置面」的参考性章节，不含签名罗列。

## API 参考（自动生成）

[API 参考（NCMapper 命名空间）](api/NCMapper.yml)

## 叙事参考章

| 章节 | 内容 |
|------|------|
| [公开 API 架构总览](architecture-map.md) | 公开可调用 API 分层（L1–L6）与演示覆盖对账清单（单一事实源） |
| [双 API 面对照](api-comparison.md) | 特性通道 ↔ Fluent 通道等价关系与全终端对照总表 |
| [Attribute 速查表](attributes-index.md) | 全部 17 个映射特性速查（用法/参数/诊断码） |
| [Fluent 终端速查表](fluent-index.md) | 全部 18 个 Fluent 终端名 / 45 个重载速查与豁免门（不含入口 `For`） |
| [项目结构](project-structure.md) | 仓库布局、各项目职责、构建拓扑 |
| [核心 API](core-api.md) | 门面类型与核心方法的能力面说明 |
| [特性系统](features.md) | 全部映射特性（Attribute）的语义与组合 |
| [DI 集成](di-integration.md) | ServiceCollection 扩展与配置绑定 |
| [安全机制](security.md) | 反射面收敛、AOT 兼容边界 |
| [配置与扩展](configuration.md) | NCMapperOptions 全配置面与扩展点 |
| [示例索引](samples-index.md) | NCMapper.Samples / NCMapper.AotSample 场景索引 |
| [多语言与本地化](localization.md) | 运行时错误消息双语（ErrorMessages）+ 文档多语言机制 |
