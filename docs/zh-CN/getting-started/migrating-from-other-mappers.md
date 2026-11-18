---
wiki-id: getting-started/migrating-from-other-mappers
库版本: Unreleased
更新时间: 2026-09-08
---

# 从其他映射库迁移：概念对照

本页面向从 MapStruct、AutoMapper、Mapster、Mapperly 等映射库迁入的用户，给出竞品概念到 NCMapper 的对照关系。NCMapper 采用源生成器在编译期物化映射代码，所有配置在编译期完成诊断，运行时无反射配置解析（AOT 友好）。

## 命名关联

NCMapper 引入新终端时遵循"默认双侧同名"的命名规则：Fluent 与特性两条通道使用同一核词，形态随通道语法变化。与竞品习惯用法存在差异的命名，见下表：

| 能力 | 竞品概念 | NCMapper 终端 | 通道 | 命名定案依据 |
|---|---|---|---|---|
| 条件映射 | AutoMapper `Condition`、Mapster `Map(dest, src, cond)`、MapStruct `@Condition`、ModelMapper `when()` | `MapWhen` | 双侧同名 | 核词取 When 表达守卫语义；条件家族单极性，负向入口（类似 IgnoreWhen）不提供，谓词取反即可表达 |
| 多态/继承分派 | AutoMapper `Include`/`IncludeBase`、MapStruct `@SubclassMapping`、Mapperly `MapDerivedType`、Mapster 继承族 | `MapSubclass` | 双侧同名 | 竞品锚定 MapStruct `@SubclassMapping`；AutoMapper 的 `Include` 一词被否决，因其描述的是"注册处收录"而非"子类型分派"本身 |
| 构造器参数绑定 | AutoMapper `ForCtorParam` | `MapCtorParam` | 仅 Fluent | 核词取 CtorParam，与 AutoMapper 对齐 |
| 前置钩子 | Mapster `BeforeMapping`、AutoMapper `BeforeMap`、MapStruct `@BeforeMapping` | `BeforeMapping` | 双侧同名 | 直接沿用行业通名 |
| 后置钩子 | AutoMapper `AfterMap`、MapStruct `@AfterMapping` | `AfterMapping` | 双侧同名 | 直接沿用行业通名 |
| 自定义转换 | AutoMapper resolver/value resolver、MapStruct `qualified@` | `Convert`（Fluent）/ `Convert`（特性） | 双通道分叉保留 | 核词一致，两通道各自沿用同名（现状设计） |

命名差异的两类情形：

1. **同名对齐**：MapWhen、MapSubclass、MapCtorParam、BeforeMapping、AfterMapping 在 Fluent 与特性通道核词一致，书写形态随通道变化（方法调用 vs 特性标注）。
2. **两通道各自的 Convert**：Convert（Fluent）与 Convert（特性）并存使用，不做更名。

## 能力对照矩阵

下表为能力终态对照。列以四家主流组件为参照，NCMapper 分列展示双通道：

| 能力 | NCMapper（Fluent） | NCMapper（特性） | MapStruct | AutoMapper | Mapster | Mapperly |
|---|:--:|:--:|:--:|:--:|:--:|:--:|
| 属性映射（跨类型/嵌套） | `Map` | `[MapProperty]` | 支持 | `ForMember` | 支持 | 支持 |
| 条件守卫 | `MapWhen` | `[MapWhen]` | `@Condition` | `Condition` | 三参 Map | 不支持 |
| 多态/继承分派 | `MapSubclass` | `[MapSubclass]` | `@SubclassMapping` | `Include` | 继承族 | `MapDerivedType` |
| 构造器参数绑定 | `MapCtorParam` | 无对应特性 | 构造器注入 | `ForCtorParam` | 无 | 构造器映射 |
| 生命周期钩子 | `BeforeMapping` / `AfterMapping` | `[BeforeMapping]` / `[AfterMapping]` | 支持 | `BeforeMap` / `AfterMap` | 支持 | 仅 After |
| 自定义转换 | `Convert` 四形态 | `[Convert]` | 支持 | resolver | 支持 | 支持 |
| 反向转换 | `ConvertBack` 四形态 | `[ConvertBack]` | 支持 | `ReverseMap` | `Reverse` | 在途 PR |
| 脱敏 | `Mask` | `[Mask]` | 无 | 无 | 无 | 无 |
| JSON 字段展开 | `FromJson` | `[MapProperty(IsFromJson)]` | 无 | 无 | 无 | 无 |
| 多源合成 | `Compose` / `FromSource` | `[MapFrom]` + `[MapProperty]` | 多源参数 | 无 | 无 | 多参 |
| ORM 投影 | EF Core / FreeSql / Dapper | 同左 | 不适用 | `ProjectTo` | `ProjectTo` | 支持 |
| 编译期诊断 | NCMAP_F 族 | NCMAP0xx 族 | unmapped 检查 | 运行时 Assert | Compile 校验 | 编译期 |

双通道不对称要点（迁移时最容易踩的差异）：

1. **MapCtorParam 仅 Fluent 可用**，特性通道无对应特性类型。构造器注入触发条件为"目标无公共无参构造器"，显式 `MapCtorParam` 声明对指定形参按两点注入（构造器实参 + 源属性名）。
2. **MapSubclass 双侧等价**，Fluent `MapSubclass<TSub,TSubDto>()` 与特性 `[MapSubclass]` 生成同一分派结构，两通道可共存于同一程序集。
3. **MapWhen 与钩子双侧等价**，守卫与回调形态一致，仅书写语法不同。

可运行对照——同名匹配的基础映射（上表「属性映射」行 NCMapper 特性通道的活演示，`dotnet run --scenario 201` 实跑验证）：

[!code-csharp[同名属性匹配基础映射：UserEntity → UserDto（特性通道）](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_SimpleNameMatch)]

## 行为差异要点

从运行时反射类组件（AutoMapper、Mapster）迁入时，以下差异需要留意：

1. **配置时机**：NCMapper 的映射配置在编译期被源生成器消费并生成代码，运行时不再解析配置对象。配置错误以编译诊断（Error 级）报出，而非运行时异常或 Assert。
2. **内联表达式转录**：MapWhen 谓词与 Convert 转换器支持无捕获内联 lambda（经转录引擎物化为生成代码内的静态方法）；捕获变量、语句体、私有成员访问会被拒绝并给出精确诊断（F41/F42 等）。这与反射组件"任意委托都可传"的习惯不同，静态方法组总是可用。
3. **ORM 投影降级边界**：声明了 MapWhen、BeforeMapping、AfterMapping、MapSubclass 的 DTO 在 ORM 投影（IQueryable）场景强制降级为内存映射（投影内无法表达守卫与回调）；MapCtorParam 不触发降级。
4. **反向映射**：Fluent 与特性通道均自动生成反向映射入口；MapSubclass 额外合成反向子类对（反向调用按 DTO 实际子类型分派）。
5. **多构造器歧义**：无参构造缺失时默认选参数最多的公共构造器；多构造器歧义消解尚未提供配置位，必要时用 `MapCtorParam` 显式锚定形参。

## 迁移步骤建议

1. 盘点源库中的自定义逻辑（resolver、condition、before/after 回调），按上表映射到 NCMapper 对应终端。
2. 被映射的 DTO 侧改用 `[MapperConfig]` + Fluent 链或 `[MapFrom]` 特性（按团队偏好选择通道，两者产物等价）。
3. 编译期逐条消化 NCMAP 诊断（每条都有精确位置与修复指引，见[诊断规则](../diagnostics/rules.md)）。
4. 跑通后用 `Mapper.Validate()` 做一次全量校验，确认映射对完整性。

相关页面：[Fluent 映射总览](../how-to/fluent/basics.md)、[映射规则（语义）](../explanation/semantics/mapping-rules.md)、[诊断码全索引](../diagnostics/rules.md)。
