<!-- wiki-sync:langbar -->
[ 🌐 [English](En-Home) | **中文** ]

# NCMapper Wiki

> .NET 对象映射库：**双引擎**（编译期源生成器 + 运行时表达式树）、**双通道**（Fluent 链式 + 特性标注）。
> 一次声明，同一套规则驱动内存映射、ORM 投影、深拷贝与多源合成。
> Apache-2.0 永久免费，Native AOT 友好。

**仓库**：[github.com/hchxxzx/NCMapper](https://github.com/hchxxzx/NCMapper) · **NuGet**：[NCMapper](https://www.nuget.org/packages/NCMapper) · **文档**：[中文](https://github.com/hchxxzx/NCMapper/tree/main/docs/zh-CN) / [English](https://github.com/hchxxzx/NCMapper/tree/main/docs/en-US)

```bash
dotnet add package NCMapper --prerelease
```

## 三行上手

```csharp
[MapperConfig]
public class UserMapperConfig
{
    public static void Configure(FluentMappingConfig cfg)
        => cfg.For<UserEntity, UserDto>();   // 同名自动匹配，全部声明就这一行
}

var dto = Mapper.Map<UserEntity, UserDto>(user);   // 编译期生成代码，运行时零配置
```

## 从这里开始

| 目标 | 入口 |
| --- | --- |
| 先跑起来 | [快速开始](Zh-GettingStarted-Quickstart) · [安装](Zh-GettingStarted-Installation) · [15 分钟教程](Zh-GettingStarted-Tutorial15min) |
| 了解整体设计 | [总览](Zh-GettingStarted-Overview) |
| 从 AutoMapper 迁移 | [迁移指南](Zh-GettingStarted-MigratingFromOtherMappers) |
| 动手场景 | 左侧「操作指南」：Fluent 声明 / ORM 投影 / 深拷贝 / 脱敏 / AOT / DI |
| 跟着教程做 | [映射](Zh-Tutorials-MappingTutorial) · [Fluent 建模](Zh-Tutorials-FluentModelingTutorial) · [ORM 投影](Zh-Tutorials-OrmProjectionTutorial) · [Web 集成](Zh-Tutorials-WebIntegrationTutorial) |
| 理解原理 | [双引擎](Zh-Explanation-DualEngine) · [双通道](Zh-Explanation-DualChannel) · [自适应选路](Zh-Explanation-AdaptiveRouting) · [投影降级](Zh-Explanation-OrmDegradation) |
| 排错 | [FAQ](Zh-Faq) · [常见陷阱](Zh-Pitfalls) · [已知限制](Zh-Explanation-Limitations) |

## 核心能力

- **编译期优先** —— 声明过的类型对由源生成器直接产出映射代码，未声明的运行时表达式树兜底，两条路径语义一致
- **三 ORM 投影** —— EF Core / FreeSql / Dapper 各有独立包，`AsDto()` 一条 SQL 直出 DTO；无法翻译时自动降级为两步映射
- **深拷贝** —— 三级策略，源生成器路径约 179 ns
- **数据脱敏** —— 9 种内置规则 + 自定义 `IMask`
- **多源合成** —— `Compose()` 从多个源装配聚合 DTO
- **编译期诊断** —— `NCMAP0xx` / `NCMAP_Fxx` 共 68 条规则，Error / Warn / Info 三级

## 目标框架

net8.0 / net9.0 / net10.0

---

<!-- wiki-sync:generated -->
<!-- 页面由 docs/zh-CN 自动同步，请勿在 Wiki 直接编辑；修改请提 PR 到仓库 docs 目录 -->

完整目录见左侧侧边栏。所有页面由
[`docs/zh-CN`](https://github.com/hchxxzx/NCMapper/tree/main/docs/zh-CN) 自动同步生成，
**请勿直接在 Wiki 编辑**；修改请提 PR 到仓库文档目录。

| 域 | 内容 |
| --- | --- |
| Aot | 裁剪与 NativeAOT 部署 |
| Fluent | 链式声明：基础 / 类型转换 / 格式化 / 从源读值 / 钩子 / 终结算子 |
| Declarative | 特性标注：基础 / 进阶 / 映射形态 |
| Orm | EF Core / FreeSql / Dapper 投影与分页 |
| DeepCopy | 深拷贝策略 |
| Compose | 多源合成 |
| Infrastructure | DI / 全局配置 / 脱敏 / 性能 / 管道 / 校验 |
| Mapping | 点号路径 / 列表映射到父对象 |
| 其他 | FAQ、常见陷阱等独立操作页 |
