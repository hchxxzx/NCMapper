---
wiki-id: tutorials/mapping-tutorial
涉及诊断码: NCMAP033
库版本: v1.0.0+
更新时间: 2026-09-29
---

# 日常双向映射演练：用户资料页与资料更新

## 概述

本教程解决一个最常见的业务问题：用户资料模块的"查"与"改"。展示页要把实体转成 DTO 返回给前端；保存按钮要把前端提交的 DTO 写回实体入库。你会用到同名自动匹配、编译期直调扩展、就地更新、嵌套取值与方向性忽略——全部是日常项目里最高频的映射形态。

示例与 `samples/NCMapper.Samples` 的 02 场景语义一致，可运行验证；各节直接给出简明示意代码，文末附完整代码示意便于整体理解。各方法完整参数说明见 [Attribute 基础映射](../how-to/declarative/basics.md)与[点路径与嵌套映射](../how-to/mapping/dot-path.md)，本教程侧重业务流转怎么接。

---

## 1. 场景：一个资料模块的两种操作

| 操作 | 方向 | 特点 |
|------|------|------|
| 打开资料页 | 实体 → DTO | 展示字段与实体字段基本同名 |
| 保存资料 | DTO → 实体 | 回写数据库实体，展示专用字段不能写回去 |

模型（演示版）：

```csharp
public class UserEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public string Email { get; set; } = "";
    public Address Address { get; set; } = new();   // 嵌套：城市/街道
}

[MapFrom(typeof(UserEntity))]
public class UserDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public string Email { get; set; } = "";
}
```

`[MapFrom]` 标在 DTO 上，同名属性自动匹配——**一行映射代码都不用写**。

---

## 2. 打开资料页：同名映射起步

展示页最普通的取数（简明示意，与演示场景 `BasicMapping_SimpleNameMatch` 同型）：

```csharp
public static class UserScenarios
{
    // 单对象 / 列表 / 带回调三方法——可运行版本见下方绑定引用
}
```

[!code-csharp[单对象：同名属性自动匹配](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_SimpleNameMatch)]
[!code-csharp[列表映射](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_ListMap)]
[!code-csharp[带回调：映射后立刻补一个展示态字段（如给 VIP 用户加前缀）](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_MapWithAction)]

三个要点：

- `Mapper.Map<S, T>(src)` 处理单对象，`Mapper.MapList<S, T>(srcs)` 处理列表
- 带回调重载（`Map(src, (s, t) => ...)`）适合映射后立刻补一个展示态字段（如给 VIP 用户加前缀）
- 同名属性自动匹配；嵌套属性（`Address.City`）会自动扁平化为 `AddressCity`（见第 5 节）

---

## 3. 更进一步：编译期直调扩展

标了 `[MapFrom]` 的类型对，源生成器会直接生成扩展方法，连 `Mapper.Map` 的查找都不用走：

[!code-csharp[源生成扩展 using NCMapper.Generated 与直调](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#Core_Generated_EntryExtensions)]

与统一入口执行同一份生成代码，性能无差异；扩展方法的好处是**写错类型名编译不过**，且 IDE 可以直接跳转。是否使用按团队习惯选择即可。

---

## 4. 保存资料：DTO 写回实体

保存按钮的回写有三种形态，对应三种业务场景：

[!code-csharp[反向直调三形态：新建/就地更新/带回调](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#Declarative_ReverseMap_New)]

反向扩展一族共六种重载（①新建 ②更新已有 ③带回调新建 ④带回调更新 ⑤列表新建 ⑥带回调列表），由源生成器为每个 `[MapFrom]` DTO 生成——一次声明，双向全配齐：

[!code-csharp[反向直调六重载巡览](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_ReverseExtensions)]

就地更新形态（不新建对象）由 `Mapper.Map(src, target)` 承载，扩展方法与之一一对应：

[!code-csharp[更新已有对象签名](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_UpdateExisting)]

---

## 5. 嵌套取值：地址怎么展示

实体里地址是嵌套对象（`Address.City` / `Address.Street`），页面表单通常是两个扁平文本框。自动扁平化直接接住：DTO 属性叫 `AddressCity`，就能自动取到 `Address.City`：

```csharp
[MapFrom(typeof(UserEntity))]
public class UserDotPathDto
{
    public string? AddressCity { get; set; }    // 自动取 Address.City（PascalCase 扁平化）
    public string? AddressStreet { get; set; }  // 自动取 Address.Street
}

```

[!code-csharp[嵌套取值：扁平属性自动映射方法](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_DotPath)]

需要显式声明时（异名、深层路径），用 `[MapProperty(typeof(源类型), "Address.City")]` 写点路径；反向保存时扁平属性也能写回嵌套路径（源值为 null 时清空叶子字段，不会留旧值）。

---

## 6. 展示专用字段：别写回数据库

资料页上常有一类"只给看"的字段，比如 `DisplayLabel`（拼接好的显示文本）。方向性忽略控制这类字段参与哪个方向：

```csharp
[MapFrom(typeof(CustomerEntity))]
public class CustomerDto
{
    public string Name { get; set; } = "";
    public string Region { get; set; } = "";

    // DTO 独有计算字段：双向都不参与映射（值由服务层拼好）
    [MapIgnore(Direction.Both)]
    public string DisplayLabel { get; set; } = "";
}
```

保存方向：DisplayLabel 不会写回实体——

[!code-csharp[反向映射忽略展示字段（DisplayLabel 不写回）](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_MapIgnore)]

`[MapIgnore]` 的方向取值有三种，按字段角色选：

| 写法 | 行为 | 适用 |
|------|------|------|
| `[MapIgnore]` 或 `[MapIgnore(Direction.Both)]` | 双向都不映射 | DTO 独有字段（展示标签、计算列） |
| `[MapIgnore(Direction.ToEntity)]` | 仅反向（DTO → 实体）忽略 | 正向展示要取实体值、保存时不许写回 |
| `[MapIgnore(Direction.ToDto)]` | 仅正向忽略 | 只在保存方向需要的字段 |

---

## 7. 验证与常见问题

**怎么确认双向都没漏字段？** 开发期跑一次 `Mapper.Validate()`，它会扫描全部 `[MapFrom]` 类型对并输出未映射字段报告（[映射验证](../how-to/infrastructure/validation.md)）。

**为什么反向保存后，正向转换的结果没有被解析回来？** 正向声明的 `[Convert]` 在反向映射默认不消费（编译期有 `NCMAP033` 提示）。需要往返闭环时，反向用 `[ConvertBack]` 显式声明解析器——"展示时格式化、保存时解析"各自声明、各自生效。

**同名映射会覆盖我不希望动的字段吗？** 会按同名匹配写入。不想被写的字段加 `[MapIgnore]`（双向）或 `[MapIgnore(Direction.ToEntity)]`（仅保存方向），见第 6 节。

---

## 8. 完整代码示意

把第 2~6 节的形态汇成一个资料服务的完整实现（教学完整示意：`Level` 的枚举转文本由映射自动完成，模型已按教学需要精简）：

```csharp
using System;
using System.Collections.Generic;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.Generated;

// ================= 1. 模型 =================
public class Address
{
    public string City { get; set; } = "";
    public string Street { get; set; } = "";
}

public class UserEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public string Email { get; set; } = "";
    public Address Address { get; set; } = new();
}

[MapFrom(typeof(UserEntity))]
public class UserDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public string Email { get; set; } = "";
}

// 扁平化 DTO：嵌套地址展示为两个文本框
[MapFrom(typeof(UserEntity))]
public class UserDotPathDto
{
    public string? AddressCity { get; set; }
    public string? AddressStreet { get; set; }
}

public class CustomerEntity
{
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Region { get; set; } = "";
}

[MapFrom(typeof(CustomerEntity))]
public class CustomerDto
{
    public string Name { get; set; } = "";
    public string Region { get; set; } = "";

    // DTO 独有计算字段：双向都不参与映射
    [MapIgnore(Direction.Both)]
    public string DisplayLabel { get; set; } = "";
}
```

`UserProfileService` 六个业务方法全部有可运行版本（同一批类型对，`BasicScenarios.cs`）：

[!code-csharp[查：单对象](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_SimpleNameMatch)]
[!code-csharp[查：列表](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_ListMap)]
[!code-csharp[查：嵌套扁平化地址视图](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_DotPath)]
[!code-csharp[改：新建（反向直调 MapToUserEntity）](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_ReverseDirectExtension)]
[!code-csharp[改：就地更新（反向直调带 target）](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_ReverseDirectUpdate)]
[!code-csharp[改：保存客户（展示字段自动跳过）](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_ForwardMap)]

读这份代码的顺序：先看模型（一个 `[MapFrom]` 换来双向全套能力），再看服务（每个业务动作一行映射），最后留意 `CustomerDto.DisplayLabel`——它两侧都不参与映射，是"DTO 独有字段"的标准写法。

---

## 相关文档

- [Attribute 基础映射](../how-to/declarative/basics.md)：特性通道操作参考
- [点路径与嵌套映射](../how-to/mapping/dot-path.md)：嵌套取值与反向写回的完整规则
- [属性级格式化](../how-to/declarative/map-format.md)：`[MapFormat]`/`.Format` 一行格式串输出日期/数值/枚举文本
- [反向映射语义](../explanation/reverse-mapping-semantics.md)：反向派生的语义细节
- [Fluent 通道建模演练](fluent-modeling-tutorial.md)：映射关系复杂时改用 Fluent 通道集中声明
