---
title: 多语言与本地化
---

# 多语言与本地化

NCMapper 的本地化分为两个独立层面：

1. **运行时错误消息本地化**（`ErrorMessages`）——库在抛出异常时，按当前线程 `CultureInfo` 自动择取**中文或英文**消息文本。
2. **文档多语言**——仓库用户文档的中文主文档与 `locales/` 下的翻译产物。

两者机制完全不同，下面分别说明。

---

## 1. 运行时错误消息本地化（ErrorMessages）

库中所有运行时异常、验证失败与诊断消息文本统一由 `ErrorMessages.cs` 资源类提供。它内置**中英双语**文案，并依据当前线程的 `CultureInfo.CurrentUICulture` 自动切换，**无需任何配置**。

### 语言判定规则

| 当前线程 `CurrentUICulture.Name` | 错误消息语言 |
|---|---|
| 以 `en` 开头（如 `en`、`en-US`、`en-GB`）| **英文** |
| 其余（含 `zh-CN`、`zh-Hans`、默认）| **中文**（默认）|

> **运行时切换**：语言判定在**每次访问**消息时动态读取 `CurrentUICulture`，因此同一进程内可在运行时切换线程文化后立即得到对应语言的消息。

### 覆盖范围

`ErrorMessages` 覆盖全部运行时异常/诊断文本，涵盖（不完全列举）：

- **映射引擎**：递归深度超限、`[MapFrom]` 未标注、无公共构造函数、无映射属性、`[MapProperty]` 字段空值、`[Convert]` 转换器异常、`[Mask]` 脱敏解析失败、类型转换失败（含枚举空串/无匹配）、点路径（Dot Path）段失败、反向写回路径失败等。
- **验证器族**（MapValidator）：报表行/汇总前缀/明细项（未映射、类型不兼容）/`PrintReport` 报表头与总计行/降级原因行前缀/Fluent 声明对注记等验证消息，随 `CurrentUICulture` 切换；**源生成器写进生成代码的验证消息固定英文**（编译期固化不随文化，见下方边界说明）。
- **树能力**：树 DTO 缺 Children 属性、Children 名不匹配、Id/ParentId 缺失、树挂载位无效等。
- **集合 / 深度复制**：未知集合类型、List↔Dictionary 不支持转换、深拷贝全部回退失败、构造函数不兼容等。
- **ORM 扩展**：Dapper/MapList 重载缺失、DTO 缺 `[MapFrom]`、分页/批处理参数非法、序列空/多元素等。
- **全局配置**：`GlobalConfig` 装配集冲突、命名策略枚举越界等。

完整方法清单见 `ErrorMessages.cs`（`src/NCMapper/ErrorMessages.cs`）。

### 使用示例

错误消息双语切换的可运行巡览（切换 `CurrentUICulture` 前后对同一必然失败的映射打印 `MapException.Message` 语言样本——zh-CN 得「类型转换失败…」、en-US 得 "Type conversion failed…"；巡览不挂场景菜单，线程文化用后恢复）：

[!code-csharp[错误消息双语切换（CurrentUICulture → MapException.Message 对照）](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_CultureBilingualTour)]

### 边界说明

> **编译期诊断双语**：源生成器/编译期诊断（`NCMAP001`~`NCMAP034`）已通过 Roslyn 资源化（`resx`）实现中英双语（`DiagnosticResources.resx` 默认英文 + `DiagnosticResources.zh-CN.resx` 中文），与运行时 `ErrorMessages` 的运行时双语机制相互独立。IDE 语言环境（`UICulture`）决定展示语言。
>
> **源生成器验证消息固定英文**：源生成器发射的验证委托（`Mapper.Validate()` 返回的生成对结果）其 `Message` 前缀/明细在编译期固化为英文（如 `, 1 incompatible type(s): ...`），不随运行时文化切换。zh 环境下可能出现"外层中文 + 生成对内层英文"混排（设计使然）；运行时反射验证路径（`MapValidator.ValidateMapping`）的消息则随文化。

---

## 2. 文档多语言

中文主文档为权威源。多语言翻译由翻译流程辅助生成，翻译产物存放于仓库根 `locales/` 对应语言目录（如 `locales/en/NCMapper.xml`）。翻译内容以中文主文档为权威源，更新主文档后需重新生成并人工校对。

---

## 3. 相关参考

- 运行时异常类型：[MapException](./core-api.md)（`MapException` / `MapConfigurationException`）
- 编译期诊断规则：[诊断码规则](../diagnostics/rules.md)
