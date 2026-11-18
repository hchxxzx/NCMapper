---
wiki-id: tutorials/fluent-modeling-tutorial
涉及诊断码: NCMAP_F12、NCMAP_F15、NCMAP_F16
库版本: v1.0.0+
更新时间: 2026-09-29
---

# Fluent 通道建模演练：从零建一个订单转换器

## 概述

本教程解决一个具体业务问题：订单详情接口要把订单实体转成页面 DTO，字段要求各不相同——异名、嵌套取值、手机号打码、内部字段不给展示、还要算一个汇总值。你会从一个空的配置类开始，用 Fluent 链式声明一步步把这条映射建出来，并在编译期得到全部检查。

示例与 `samples/NCMapper.Samples` 的 01 场景（基础链、转换、条件守卫、构造器绑定四组演示）语义一致，可运行验证；各节直接给出简明示意代码，文末附完整代码示意便于整体理解。各终端方法的完整参数说明见 [Fluent 基础](../how-to/fluent/basics.md)与 [Fluent 四终端](../how-to/fluent/terminals.md)，本教程侧重"从需求到配置"的建模过程。

---

## 1. 场景：订单详情页需要哪些字段

产品给出的需求清单：

| 页面字段 | 数据来源 | 要求 |
|---------|---------|------|
| 订单号 | `Id` | 字段名不同（页面叫 `OrderId`） |
| 客户姓名 | `Customer.Name` | 在嵌套对象里 |
| 客户手机号 | `CustomerPhone` | **脱敏展示**（合规要求） |
| 内部备注 | `InternalNote` | **不给页面看** |
| 总重量 | `Items` 集合 | 页面要汇总值，实体里没有现成字段 |

对应的模型（演示版）：

```csharp
public class ChainOrderEntity
{
    public int Id { get; set; }
    public ChainCustomerInfo Customer { get; set; } = new();
    public string CustomerPhone { get; set; } = "";
    public string InternalNote { get; set; } = "internal";
    public List<ChainOrderItemEntity> Items { get; set; } = new();
}

public class ChainOrderDto
{
    public int OrderId { get; set; }
    public string CustomerName { get; set; } = "";
    public string CustomerPhone { get; set; } = "";
    public string InternalNote { get; set; } = "";
    public double TotalWeight { get; set; }
}
```

选择 Fluent 通道的理由：这条映射有**顺序依赖**（先声明异名，再挂汇总回调）、要挂条件守卫，且团队希望 DTO 类保持干净（字段清单来自产品，映射意图放配置类便于集中评审）。

---

## 2. 第一步：建配置类

新建一个 Mapper 配置类，标注 `[MapperConfig]`，类里写一个签名固定的静态方法（**方法名任意**，参数必须是 `FluentMappingConfig`）：

```csharp
[MapperConfig]
public class ChainOrderMapper
{
    public static void Configure(FluentMappingConfig cfg)
    {
        // 链式声明写在这里
    }
}
```

三条约定：

- DTO 类上什么都不加，全部意图集中在这个配置类
- 源生成器在编译期扫描这个方法体，生成的映射代码与 `[MapFrom]` 通道完全相同
- 类里没有匹配签名的方法会报 `NCMAP_F15` 提示——编译期就能发现配置类写错了

---

## 3. 第二步：一条链满足五个需求

针对第 1 节的需求清单，逐条翻译成链式调用：

| 需求 | 终端方法 |
|------|---------|
| 异名（`Id` → `OrderId`） | `.Member(d => d.OrderId).From(s => s.Id)` |
| 嵌套取值（`Customer.Name` → `CustomerName`） | `.Member(d => d.CustomerName).From(s => s.Customer.Name)` |
| 不展示内部备注 | `.Member(d => d.InternalNote).Ignore()` |
| 手机号脱敏 | `.Member(d => d.CustomerPhone).Mask<MaskPhone>()` |
| 汇总总重量 | `.AfterMapping(ApplyComputedFields)` |

简明示意（与演示片段 `FluentBasics_ChainOrderMapper` 同型）：

[!code-csharp[Fluent 基础链：From 异名/嵌套 + Ignore + Mask + AfterMapping 全链](~/samples/NCMapper.Samples/01-Fluent/FluentModels.cs#FluentBasics_ChainOrderMapper_Configure)]

注意 `AfterMapping` 指向的方法必须是 `public` 或 `internal static`——写成 `private` 会报 `NCMAP_F12`。

---

## 4. 第三步：日期格式输出用 Format

需求迭代：页面还要显示"最后修改时间"的文本格式。实体里是 `DateTime`，页面要 `yyyy-MM-dd` 字符串——这种"格式串就能表达"的输出用 `.Format` 一行搞定，**不用写转换器方法**：

```csharp
// 同名锚点：DTO 属性名与实体属性名相同（CreatedAt: DateTime → CreatedAt: string），
// 格式串在生成代码里是编译期常量（AOT 友好；输出随当前文化——固定形态的日期格式本身文化无关）
cfg.For<ChainOrderEntity, ChainOrderDto>()
   .Member(d => d.CreatedAt).Format("yyyy-MM-dd");
```

`.Format` 两个使用要点：

- **同名锚点**：源按目标属性名自动匹配，DTO 属性名必须与实体属性名相同（拼成 `CreatedText` 这类异名会报 `NCMAP_F65`——异名场景先用 `.Member(d => d.X).From(s => s.Y)` 处理来源，或改属性命名）。
- **单向展示声明，独立使用零配套**：格式化是有损的单向变换（`yyyy-MM-dd` 输出会丢掉时间部分），库不会按格式串反解析。展示页面的 DTO 只管正向输出，裸 `.Format` 直接用、什么都不用配；反向映射（DTO→实体）遇到该字段时不赋值（保持实体侧原值，其余字段照常），仅给 `NCMAP_F67` 警告提示。确实需要往返的属性（编辑页面回写），再自行补 `.ConvertBack` 写反解析。

超出"格式串"能力的自定义转换逻辑（整实体汇总、常量注入、多字段组合等），才用 `.Convert` 挂转换器方法。四种形态按入参粒度选（与演示片段 FluentConvert_Convert 同型）：

[!code-csharp[Fluent Convert 四形态（T0 常量/T1 字段/T2 整源/T3 组合）](~/samples/NCMapper.Samples/01-Fluent/CvModels.cs#FluentConvert_Convert)]

转换器本体就是普通静态方法，放到配置类或独立工具类里都行。

一个重要提醒：**`.Format` 与正向声明的 `.Convert` 在反向映射（DTO → 实体）时默认不消费**。如果同一对还需要反向，`.Format` 属性编译期就要求表态（`NCMAP_F67`），`.Convert` 则静默跳过——反向解析用 `.ConvertBack` 显式声明（形态与 Convert 对称，见 [Fluent 转换](../how-to/fluent/convert.md)；格式化输出的完整说明见 [Fluent 格式化](../how-to/fluent/format.md)）。

---

## 5. 第四步：部分字段按条件才映射

需求再迭代：分值为空（0）的条目不显示分数字段。用 `.MapWhen` 给属性加条件守卫——条件是方法时写方法名，简单条件也可以内联：

```csharp
// 条件谓词：普通静态方法（与 Convert 转换器同规则）
public static class MwRules
{
    public static bool HasContent(string value) => !string.IsNullOrWhiteSpace(value);
    public static bool IsKindA(MwItemEntity src) => src.Kind == "A";
}

```

[!code-csharp[MapWhen 条件守卫：命名谓词 + 内联 lambda + 同锚点组合](~/samples/NCMapper.Samples/01-Fluent/MwModels.cs#MapWhen_MapWhenFluentMapper_Configure)]

`From` 与 `MapWhen` 可以**同锚点共存**（两条都以 `.Member(d => d.X)` 起头，先锚定来源再锚定条件），链上顺序就是执行顺序——这正是选 Fluent 通道的理由之一。注意两个要点：① 内联 lambda 谓词读整个源对象，**无推断位，必须显式写 `<TSource>`**；入参与源字段类型一致的命名方法组可直接省类型参。

---

## 6. 第五步：目标类型不可变时绑定构造器

如果目标 DTO 设计为不可变（get-only 属性 + 构造器赋值），属性赋值走不通，改用 `.MapCtorParam` 按构造器形参绑定来源。两种形态加一个 record 特例：

[!code-csharp[Fluent MapCtorParam 构造器形参绑定两形态](~/samples/NCMapper.Samples/01-Fluent/CpModels.cs#CtorParam_CtorParamFluentMapper_Configure)]

推荐形态 1（源 lambda，符号级检查）；形态 2 的属性名拼错会在编译期报 `NCMAP_F51`。record 目标更简单：位置参数就是同名属性，普通 `.Map` 即可喂给构造器，不必写 `MapCtorParam`。

---

## 7. 调用与验证

配置类不需要手动注册——程序集加载时自动生效。调用侧两种写法结果一致：

[!code-csharp[统一入口运行时兜底（未声明类型对）](~/samples/NCMapper.Samples/01-Fluent/FluentModels.cs#FluentBasics_ChainOrderMapper)]

两种写法执行同一份生成代码，只差一层调用入口（原理见 [双引擎架构](../explanation/dual-engine.md)）。

用第 1 节的模型验证：`OrderId` 等于实体 `Id`；`CustomerName` 取自嵌套对象；`CustomerPhone` 已按 `138****5678` 形态打码；`InternalNote` 保持默认空串；`TotalWeight` 等于明细重量之和。

---

## 8. 完整代码示意

把第 2~6 步的需求全部落到同一个订单模型上，最终落地的完整代码如下（教学完整示意：为承接第 4~5 步的迭代需求，实体新增了 `CreatedAt`、`Score` 两个演示字段；各声明形态与第 3~6 节引用的演示片段同型）：

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using NCMapper;

// ================= 1. 模型 =================
public class ChainOrderEntity
{
    public int Id { get; set; }
    public ChainCustomerInfo Customer { get; set; } = new();
    public string CustomerPhone { get; set; } = "";
    public string InternalNote { get; set; } = "internal";
    public List<ChainOrderItemEntity> Items { get; set; } = new();
    public DateTime CreatedAt { get; set; }   // 教学示意新增：第 4 步转换示例用
    public int Score { get; set; }            // 教学示意新增：第 5 步条件示例用
}

public class ChainCustomerInfo
{
    public string Name { get; set; } = "";
}

public class ChainOrderItemEntity
{
    public double Weight { get; set; }
}

public class ChainOrderDto
{
    public int OrderId { get; set; }
    public string CustomerName { get; set; } = "";
    public string CustomerPhone { get; set; } = "";
    public string InternalNote { get; set; } = "";
    public double TotalWeight { get; set; }
    public string CreatedAt { get; set; } = "";   // 与实体同名（Format 同名锚点）：DateTime → 格式化文本
    public int Score { get; set; }
}

// ================= 2. 转换器与谓词（普通静态方法） =================
public static class OrderRules
{
    // 谓词：分数达 90 才映射该字段
    public static bool IsHighScore(int score) => score >= 90;
}

// ================= 3. 配置类：一条链承载全部声明 =================
[MapperConfig]
public class ChainOrderMapper
{
    // 方法名任意，签名固定：static void Xxx(FluentMappingConfig cfg)
    public static void Configure(FluentMappingConfig cfg)
    {
        cfg.For<ChainOrderEntity, ChainOrderDto>()
           // —— 第 3 步：基础链 ——
           .Member(d => d.OrderId).From(s => s.Id)                        // 异名：Id → OrderId
           .Member(d => d.CustomerName).From(s => s.Customer.Name)        // 嵌套取值
           .Member(d => d.InternalNote).Ignore()                          // 忽略：页面不展示
           .Member(d => d.CustomerPhone).Mask<MaskPhone>()                // 脱敏：138****5678
           // —— 第 4 步：日期格式输出（迭代直接并入同一条链）——
           .Member(d => d.CreatedAt).Format("yyyy-MM-dd")             // 同名锚点：编译期常量，不用写转换器方法；
                                                                       // 单向展示声明——裸声明即合法（反向该字段不赋值）
           // —— 第 5 步：条件守卫（同名属性直接锚定；内联谓词须显式类型参，如 MapWhen<ChainOrderEntity>(s => s.Score >= 90)） ——
           .Member(d => d.Score).MapWhen(OrderRules.IsHighScore)
           // —— 收尾：汇总回调 ——
           .AfterMapping(ApplyComputedFields);
    }

    // AfterMapping 回调：必须 public 或 internal static（private 报 NCMAP_F12）
    public static void ApplyComputedFields(ChainOrderEntity src, ChainOrderDto dst)
    {
        dst.TotalWeight = src.Items.Sum(i => i.Weight);           // 汇总明细重量
    }
}

// ================= 4. 调用 =================
public class OrderPageService
{
    public ChainOrderDto Load(ChainOrderEntity entity)
    {
        // 写法一：统一入口（未声明类型对也可走，运行时兜底）
        var dto = Mapper.Map<ChainOrderEntity, ChainOrderDto>(entity)!;

        // 写法二：源生成器扩展方法（编译期直调，using NCMapper.Generated）
        // var dto = entity.MapToChainOrderDto();

        return dto;
    }
}
```

读这份代码的顺序：先看模型（页面要什么），再看配置类（每个字段从哪来、怎么处理），最后看调用（一行完成）。同一类型对的全部声明必须在同一条链上——后续迭代往这条链里追加即可，不要另起 `For<...>()`。

---

## 9. 常见坑

| 现象 | 原因与处理 |
|------|-----------|
| 同一类型对写了两条 `For<...>()` 链 | 编译报 `NCMAP_F16`——同一对的所有声明必须并进同一条链 |
| `AfterMapping` 方法报 `NCMAP_F12` | 回调方法须 `public`/`internal static`，`private` 不行 |
| 配置类没生效 | 检查 `[MapperConfig]` 标注与方法签名 `static void Xxx(FluentMappingConfig cfg)`（缺失报 `NCMAP_F15`） |
| 反向映射丢了正向的转换结果 | 正向 `.Format`/`.Convert` 反向不消费——`.Format` 属性反向**不赋值**（仅 `NCMAP_F67` 警告提示），需要往返自行补 `.ConvertBack`；`.Convert` 属性反向降级为普通转换（`NCMAP033` 提示） |
| 同一对被 Fluent 和 `[MapFrom]` 都声明了 | 编译报 `NCMAP_F11`——同一对只留一条通道的声明 |

---

## 相关文档

- [Fluent 基础（链式声明入口）](../how-to/fluent/basics.md)：全部终端方法参考与 F 族诊断索引
- [Fluent 转换](../how-to/fluent/convert.md)：Convert/ConvertBack 四形态完整说明
- [Fluent 格式化](../how-to/fluent/format.md)：.Format 终端、同名锚点语义与 F64~F67 守门
- [属性级格式化（MapFormat）](../how-to/declarative/map-format.md)：特性通道的同款能力
- [Fluent 四终端](../how-to/fluent/terminals.md)：MapWhen/MapCtorParam/MapSubclass/FromJson
- [双通道架构](../explanation/dual-channel.md)：Fluent 与 Attribute 两条通道的架构关系
- [多源合成（Compose）场景演练](compose-tutorial.md)：一个目标要吃多个源对象时改用 Compose
