---
wiki-id: getting-started/tutorial-15min
涉及诊断码:
库版本: v1.0.0+
更新时间: 2026-09-28
---

# 15 分钟上手教程

## 概述

本教程带你完成一次完整的映射流程：定义实体与 DTO、声明映射配置、单对象映射、列表映射、投影、校验。全部示例来自 `samples/NCMapper.Samples` 项目，可运行验证。

适合第一次接触 NCMapper 的读者。安装与最简用法见[快速开始](./quickstart.md)；本教程侧重完整演练，步骤按顺序执行，每步都有可验证的结果。

> **声明式双通道**：本教程以 **Fluent 通道**（`[MapperConfig]` 配置类链式声明）为主线；特性通道（`[MapFrom]` 标注）作为等价写法在步骤 1 附带说明，两者生成的映射代码完全相同。Fluent 全部终端见 [Fluent 入口](../how-to/fluent/basics.md)，本教程示例可在两通道间平移。

---

## 0. 前置准备

### 0.1 获取示例项目

克隆仓库并进入 `samples/NCMapper.Samples` 目录：

```bash
git clone <仓库地址>
cd NCMapper
```

运行场景 201，确认环境就绪：

```bash
dotnet run --project samples/NCMapper.Samples -- --scenario 201
```

输出显示基础映射各子场景结果（单对象、列表、更新已有对象、扩展方法）。命令中的 `--scenario 201` 与示例程序编号一一对应，本教程步骤 2/3 的验证都依赖场景 201。

### 0.2 你会用到什么

| 组件 | 用途 | 本教程步骤 |
|------|------|-----------|
| NCMapper 核心包 | 映射能力（`Mapper.Map` / `MapList` / `Validate`） | 1/2/3/5 |
| NCMapper.FreeSql 扩展包 | 投影（`AsDto` 终端操作） | 4 |
| samples 项目 | 示例单点同源，可运行验证 | 全部 |

若项目尚未安装 NCMapper 包，参照[快速开始 §2.1](./quickstart.md)添加 `PackageReference`。

---

## 1. 定义实体与 DTO

先定义源实体。示例使用 `UserEntity`，包含一个嵌套对象 `Address`：

[!code-csharp[UserEntity 实体定义](~/samples/NCMapper.Samples/Models/Common/Entities/UserEntity.cs#Common_UserEntity)]

[!code-csharp[UserEntity 实体定义 · Id](~/samples/NCMapper.Samples/Models/Common/Entities/UserEntity.cs#Common_UserEntity_Id)]

[!code-csharp[UserEntity 实体定义 · Name](~/samples/NCMapper.Samples/Models/Common/Entities/UserEntity.cs#Common_UserEntity_Name)]

[!code-csharp[UserEntity 实体定义 · Age](~/samples/NCMapper.Samples/Models/Common/Entities/UserEntity.cs#Common_UserEntity_Age)]

[!code-csharp[UserEntity 实体定义 · Email](~/samples/NCMapper.Samples/Models/Common/Entities/UserEntity.cs#Common_UserEntity_Email)]

[!code-csharp[UserEntity 实体定义 · Status](~/samples/NCMapper.Samples/Models/Common/Entities/UserEntity.cs#Common_UserEntity_Status)]

[!code-csharp[UserEntity 实体定义 · CreatedAt](~/samples/NCMapper.Samples/Models/Common/Entities/UserEntity.cs#Common_UserEntity_CreatedAt)]

[!code-csharp[UserEntity 实体定义 · Address](~/samples/NCMapper.Samples/Models/Common/Entities/UserEntity.cs#Common_UserEntity_Address)]

再定义目标 DTO。Fluent 通道下 DTO 是普通类，不加任何标注：

```csharp
public class UserDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public string Email { get; set; } = "";
}
```

**声明映射（Fluent 配置类）**：新建一个配置类，标注 `[MapperConfig]`，类内声明静态方法（方法名任意，签名固定），用 `cfg.For<源, 目标>()` 声明类型对：

```csharp
[MapperConfig]
public class UserMapperConfig
{
    public static void Configure(FluentMappingConfig cfg)
        => cfg.For<UserEntity, UserDto>();   // 同名同类型自动匹配，无需逐属性声明
}
```

配置类声明后无需手动注册：源生成器在编译期扫描 Configure 方法体生成映射代码，并经模块初始化器自动注册进 `Mapper`。属性按名称约定自动匹配：`Id` 对应 `Id`、`Name` 对应 `Name`，类型一致即可映射。需要异名映射、忽略、脱敏等微调时，在 `For(...)` 链上追加终端方法，完整形态见下例（异名 `Map` + 嵌套路径 + `Ignore` + `Mask` 脱敏 + `AfterMapping` 汇总连写）：

[!code-csharp[Fluent 基础链：Map/Ignore/Mask/AfterMapping 连写](~/samples/NCMapper.Samples/01-Fluent/FluentModels.cs#FluentBasics_ChainOrderMapper)]

[!code-csharp[Fluent 基础链：Map/Ignore/Mask/AfterMapping 连写 · Configure](~/samples/NCMapper.Samples/01-Fluent/FluentModels.cs#FluentBasics_ChainOrderMapper_Configure)]

[!code-csharp[Fluent 基础链：Map/Ignore/Mask/AfterMapping 连写 · ApplyComputedFields](~/samples/NCMapper.Samples/01-Fluent/FluentModels.cs#FluentBasics_ChainOrderMapper_ApplyComputedFields)]

**等价写法（特性通道）**：如果你偏好"标注直接贴在 DTO 类上"，也可以在 DTO 上标注 `[MapFrom(typeof(源类型))]`，无需配置类。两通道能力等价，源生成器生成完全相同的映射代码（samples 的 `UserDto` 即特性形态）：

[!code-csharp[UserDto DTO 定义（特性通道等价写法）](~/samples/NCMapper.Samples/Models/Common/Dtos/UserDto.cs#Common_UserDto)]

[!code-csharp[UserDto DTO 定义（特性通道等价写法） · Id](~/samples/NCMapper.Samples/Models/Common/Dtos/UserDto.cs#Common_UserDto_Id)]

[!code-csharp[UserDto DTO 定义（特性通道等价写法） · Name](~/samples/NCMapper.Samples/Models/Common/Dtos/UserDto.cs#Common_UserDto_Name)]

[!code-csharp[UserDto DTO 定义（特性通道等价写法） · Age](~/samples/NCMapper.Samples/Models/Common/Dtos/UserDto.cs#Common_UserDto_Age)]

[!code-csharp[UserDto DTO 定义（特性通道等价写法） · Email](~/samples/NCMapper.Samples/Models/Common/Dtos/UserDto.cs#Common_UserDto_Email)]

注意：两条通道对**同一类型对**只能声明一次（二选一），双侧同时声明编译期报 `NCMAP_F11`。

NCMapper 是双引擎组件：

- **源生成器引擎**：编译期为每个已声明的类型对（Fluent 配置类或 `[MapFrom]` 标注）生成静态映射代码，零首次延迟，附带编译期诊断
- **运行时表达式树引擎**：未注册的类型对在首次调用时编译表达式树并缓存

你不需要选择走哪条路径，`Mapper.Map` 自动使用已注册的生成代码，未命中时回退表达式树。

---

## 2. 单对象映射

调用 `Mapper.Map<TSource, TTarget>(src)` 完成一次映射。调用入口两通道通用，以特性通道的 samples 示例为例：

[!code-csharp[单对象映射](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_SimpleNameMatch)]

映射结果：`dto.Id = 1`、`dto.Name = "张三"`，同名同类型属性全部复制。目标对象由 `Mapper.Map` 新建，源对象不被修改。

要点：

- 映射是对象间属性复制，与类型转换（`[Convert]`）是两回事：同名同类型属性直接赋值；属性名不同或类型不同时，需要显式声明（Fluent 用 `.Member(d => d.目标).From(s => s.源)` 链式声明，特性通道用 `[MapProperty]`）或自定义转换（见 [映射规则（语义）](../explanation/semantics/mapping-rules.md)、[declarative/basics](../how-to/declarative/basics.md)）
- 映射后需要额外处理（如格式化字段），用带 Action 回调的重载：

[!code-csharp[带 Action 回调的映射](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_MapWithAction)]

回调在映射完成后执行，参数为目标对象，可以直接修改。

更新已有对象（不创建新实例）用 `Map(src, target)`：

[!code-csharp[更新已有对象](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_UpdateExisting)]

源生成器还会为每个已声明的类型对生成扩展方法，语法更简洁（需要 `using NCMapper.Generated;`）：

[!code-csharp[源生成器扩展方法](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_SourceGenExtension)]

[!code-csharp[源生成器扩展方法 · SourceGenWithAction](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_SourceGenExtension_SourceGenWithAction)]

[!code-csharp[源生成器扩展方法 · SourceGenUpdateExisting](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_SourceGenExtension_SourceGenUpdateExisting)]

[!code-csharp[源生成器扩展方法 · SourceGenList](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_SourceGenExtension_SourceGenList)]

扩展方法与 `Mapper.Map` 基于同一份生成代码，按语法偏好选择即可。

---

## 3. 列表映射

批量映射集合用 `Mapper.MapList<TSource, TTarget>(srcs)`：

[!code-csharp[列表映射](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_ListMap)]

`MapList` 对集合逐元素执行同一份映射逻辑，并为每个元素创建独立的目标实例，不共享引用。源列表为 null 时返回 null，不会抛异常。

列表映射适合批量查询结果转换（列表页、导出、接口响应），与单对象映射共用同一份映射配置，不存在第二条规则。

运行验证：

```bash
dotnet run --project samples/NCMapper.Samples -- --scenario 201
```

场景 201 的输出中，列表映射子场景即 `MapList` 的结果。

---

## 4. 简单投影

投影是 ORM 查询中把实体直接投影为 DTO 的 SQL 侧操作（`AsDto`），一条 SQL 完成查询与映射。投影需要 ORM 扩展包（`NCMapper.FreeSql` / `NCMapper.EFCore` / `NCMapper.Dapper` 三选一），核心包不包含 ORM 依赖。

FreeSql 单表投影示例：

[!code-csharp[单表 AsDto 投影](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_SingleTableAsDto)]

[!code-csharp[单表 AsDto 投影 · SingleTableFirst](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_SingleTableAsDto_SingleTableFirst)]

`AsDto()` 在 SQL 侧裁剪列并映射为 DTO，配合 `Where` / `OrderBy` / `ToPagedList` 在数据库完成过滤、排序与分页。遇到 SQL 无法翻译的特性（如 `[Convert]`、`[Mask]`）时，自动降级为二步内存映射，结果不变，仅多一次查询（[FreeSql 单表](../how-to/orm/freesql-single-table.md)）。

降级是透明行为：投影与内存映射共用同一份映射配置，SQL 可翻译时走 SQL，遇到不可翻译特性时自动降级，无需改动调用代码。

运行验证：

```bash
dotnet run --project samples/NCMapper.Samples -- --scenario 501
```

samples 的投影场景使用 FreeSql 内存模式并预置数据，可离线运行。

---

## 5. 校验

映射配置错误（如属性类型不兼容）会在运行时暴露。用 `Mapper.Validate()` 在开发期提前发现：

[!code-csharp[扫描程序集校验](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios)]

[!code-csharp[扫描程序集校验 · ValidateAll](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_ValidateAll)]

[!code-csharp[扫描程序集校验 · ValidateMapping](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_ValidateMapping)]

[!code-csharp[扫描程序集校验 · ValidateOrThrowDemo](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_ValidateOrThrowDemo)]

[!code-csharp[扫描程序集校验 · ValidateAssembly](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_ValidateAssembly)]

[!code-csharp[扫描程序集校验 · MapNullSource](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_MapNullSource)]

[!code-csharp[扫描程序集校验 · TraceMapping](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_TraceMapping)]

[!code-csharp[扫描程序集校验 · TraceToWriter](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_TraceToWriter)]

[!code-csharp[扫描程序集校验 · TraceLogCallback](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_TraceLogCallback)]

[!code-csharp[扫描程序集校验 · TraceOptionsFactory](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_TraceOptionsFactory)]

[!code-csharp[扫描程序集校验 · ToggleValidationSwitch](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_ToggleValidationSwitch)]

[!code-csharp[扫描程序集校验 · PrintReportToWriters](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_PrintReportToWriters)]

[!code-csharp[扫描程序集校验 · DiagnoseError](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_DiagnoseError)]

[!code-csharp[扫描程序集校验 · Run](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_Run)]

`Validate()` 扫描程序集中全部已声明的映射类型对，返回 OK / Warn / Error 三级结果。Error 级配置意味着运行时映射会失败，应在发布前修正；Warn 级提示可改进项（如非规范标注位置）。

生产环境可在启动阶段用 `ValidateOrThrow()` 全量校验，Error 级配置直接抛异常拦截（[验证与诊断](../how-to/infrastructure/validation.md)）：

[!code-csharp[启动期全量校验](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios)]

[!code-csharp[启动期全量校验 · ValidateAll](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_ValidateAll)]

[!code-csharp[启动期全量校验 · ValidateMapping](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_ValidateMapping)]

[!code-csharp[启动期全量校验 · ValidateOrThrowDemo](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_ValidateOrThrowDemo)]

[!code-csharp[启动期全量校验 · ValidateAssembly](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_ValidateAssembly)]

[!code-csharp[启动期全量校验 · MapNullSource](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_MapNullSource)]

[!code-csharp[启动期全量校验 · TraceMapping](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_TraceMapping)]

[!code-csharp[启动期全量校验 · TraceToWriter](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_TraceToWriter)]

[!code-csharp[启动期全量校验 · TraceLogCallback](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_TraceLogCallback)]

[!code-csharp[启动期全量校验 · TraceOptionsFactory](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_TraceOptionsFactory)]

[!code-csharp[启动期全量校验 · ToggleValidationSwitch](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_ToggleValidationSwitch)]

[!code-csharp[启动期全量校验 · PrintReportToWriters](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_PrintReportToWriters)]

[!code-csharp[启动期全量校验 · DiagnoseError](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_DiagnoseError)]

[!code-csharp[启动期全量校验 · Run](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_Run)]

运行验证：

```bash
dotnet run --project samples/NCMapper.Samples -- --scenario 602
```

---

## 6. 下一步

- 场景演练：多源合成（Compose）、ORM 投影、深拷贝，见 tutorials 目录
- 操作指南：按具体问题检索，见 how-to 目录（基础映射、类型转换、集合、深拷贝等）
- 概念原理：双引擎架构动机、ORM 降级机制、反向映射语义，见 explanation 目录

---

## 常见误用

| 误用 | 现象 | 改法 |
|------|------|------|
| 目标类型未声明映射（无配置类也无 `[MapFrom]`）就调用 `Mapper.Map` | 回退表达式树，功能可用但缺失编译期诊断与生成代码 | 用 `cfg.For<源, 目标>()` 声明，或标注 `[MapFrom(typeof(源类型))]` |
| 属性名不一致，未显式声明 | 目标属性保持默认值（漏映射），无提示 | Fluent 用 `.Member(d => d.目标).From(s => s.源)` 显式声明；特性通道用 `[MapProperty]` |
| 混淆映射与转换 | 期望格式转换，只得到原值复制 | 属性复制用映射；类型/格式变换用 `[Convert]` 或 Fluent `.Convert` |

---

## 速查表

| 步骤 | API | 说明 |
|------|-----|------|
| Fluent 声明映射 | `[MapperConfig]` + `cfg.For<TSource, TTarget>()` | 配置类声明，编译期生成映射代码 |
| 特性声明映射（等价写法） | `[MapFrom(typeof(T))]` | 标注 DTO，编译期生成映射代码 |
| 单对象映射 | `Mapper.Map<TSource, TTarget>(src)` | 新建目标对象 |
| 映射后处理 | `Mapper.Map(src, (s, t) => ...)` | Action 回调修改目标 |
| 更新已有对象 | `Mapper.Map(src, target)` | 不创建新实例 |
| 列表映射 | `Mapper.MapList<TSource, TTarget>(srcs)` | 批量复制集合 |
| 扩展方法 | `src.MapToXxxDto()` | 源生成器直调，需 `using NCMapper.Generated` |
| 投影 | `fsql.Select<T>().AsDto().ToList<TDto>()` | SQL 侧投影（ORM 扩展包） |
| 校验 | `Mapper.Validate()` | 扫描程序集验证映射配置 |

---

## 相关文档

- [快速开始](./quickstart.md)：安装与最简用法
- [Fluent 入口](../how-to/fluent/basics.md)：链式声明全部终端
- [基础映射](../how-to/declarative/basics.md)：映射规则详解
- [反向自定义转换](../how-to/declarative/basics.md)：`[Convert]`/`[ConvertBack]` 与类型转换
- [explanation 双引擎](../explanation/source-generator.md)：源生成器与表达式树原理
