---
wiki-id: tutorials/masking-tutorial
涉及诊断码:
库版本: v1.0.0+
更新时间: 2026-09-29
---

# 敏感数据脱敏演练：客服后台合规打码

## 概述

本教程解决一个合规场景：客服后台要展示客户资料，安全要求手机号、身份证号、银行卡号等个人敏感信息**必须打码展示**——数据照常查出，展示层自动脱敏，且不同环境（如海外部署）可以换脱敏策略而不改代码。你会把一套客户资料页从"明文直出"改造成"自动打码"，并验证效果。

示例与 `samples/NCMapper.Samples` 的 06 场景（605）语义一致，可运行验证；各节直接给出简明示意代码，文末附完整代码示意便于整体理解。脱敏全部用法见 [数据脱敏](../how-to/infrastructure/masking.md)，本教程侧重把脱敏接进一个真实页面。

---

## 1. 场景：客服后台的客户资料页

安全合规给出的要求清单：

| 字段 | 要求 | 效果示例 |
|------|------|---------|
| 手机号 | 中间打码 | `138****5678` |
| 身份证号 | 只留头尾 | `1101**********34` |
| 银行卡号 | 只留后四位 | `************2233` |
| 邮箱 | 账号名打码 | `u***@example.com` |
| 中文姓名 | 保留姓氏 | `张**` |

关键设计约束：**数据库实体不动**（存储永远是明文，这是业务需要），脱敏只发生在"实体 → DTO"的展示映射层。

---

## 2. 第一步：在 DTO 属性上声明脱敏规则

NCMapper 内置 9 种脱敏规则，`[Mask]` 特性直接标注在 DTO 属性上：

```csharp
[MapFrom(typeof(CustomerEntity))]
public class CustomerMaskedDto
{
    public string Name { get; set; } = "";

    [MapProperty(typeof(CustomerEntity), nameof(CustomerEntity.Phone))]
    [Mask(typeof(MaskPhone))]
    public string Phone { get; set; } = "";
}
```

映射时自动打码，业务代码零改动。9 种内置规则与保留位数：

| 规则 | 类 | 效果 |
|------|-----|------|
| 手机号 | `MaskPhone` | `138****5678` |
| 身份证 | `MaskIdCard` | `1101**********34` |
| 银行卡 | `MaskBankCard` | `************2233` |
| 邮箱 | `MaskEmail` | `u***@example.com` |
| 中文姓名 | `MaskChineseName` | `张**` |
| 地址 | `MaskAddress` | 保留省市，后面打码 |
| IP | `MaskIp` | `192.168.*.*` |
| 车牌 | `MaskLicensePlate` | `京A***45` |
| 座机 | `MaskLandline` | `010-66****88` |

---

## 3. 第二步：自定义打码规则（转换器形态）

内置规则不够用时（比如公司内部有自己的打码规范），用 `[Convert]` 挂一个自定义转换器——演示项目里的客户电话就是这么做的（摘自共享模型 `CustomerDto`）：

```csharp
public class CustomerDto
{
    public string Name { get; set; } = "";

    [MapProperty(typeof(CustomerEntity), nameof(CustomerEntity.Phone))]
    [Convert(typeof(PhoneMaskConverter), nameof(PhoneMaskConverter.Convert))]
    public string Phone { get; set; } = "";
}
```

调用侧不变，照常映射即可（与演示场景 `Mask_ConvertUsing` 同型）：

[!code-csharp[脱敏映射声明形态](~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs#Mask_ConvertUsing_MaskedCustomerMap)]

选择建议：规则与主流一致（手机号、身份证等）用 `[Mask]` 内置规则；公司自有规范用 `[Convert]` + 自定义转换器。

---

## 4. 第三步：Fluent 通道的等价写法

DTO 不想被脱敏标注污染？Fluent 通道有等价的链式终端 `.Mask<TMask>()`，写在配置类的 Member 段里：

[!code-csharp[Fluent 通道等价脱敏 .Member().Mask<TMask>()](~/samples/NCMapper.Samples/01-Fluent/FluentModels.cs#FluentBasics_ChainOrderMapper_Configure)]

`TMask` 是脱敏实现类型（须实现 `IMask`，如内置的 `MaskPhone`/`MaskIdCard`/`MaskBankCard`/`MaskEmail`/`MaskChineseName`）。目标属性类型由 `.Member(d => d.CustomerPhone)` 锚定，**不再需要在 `Mask<>` 里重复写一遍字段类型**。源侧省略 `From` 时按目标字段名自动解析同名属性；也可先 `.From(s => s.X)` 显式指定来源再脱敏（旧写法表达不出的组合）。

两条通道语义等价（同一类型对仍受双声明互斥约束 `NCMAP_F11`），按项目的通道选择惯例走即可。

---

## 5. 第四步：按环境切换脱敏策略

海外部署要求手机号按当地规范打码——**不改 DTO 特性标注**，运行时替换脱敏实现：

[!code-csharp[脱敏提供器：查看当前实现 Resolve](~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs#Mask_ConvertUsing_ResolveMask)]
[!code-csharp[脱敏提供器：全局替换 Override](~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs#Mask_ConvertUsing_OverrideMask)]
[!code-csharp[脱敏提供器：恢复内置默认 Reset](~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs#Mask_ConvertUsing_ResetMasks)]

替换是全局生效的（所有用 `MaskPhone` 的映射一起切），适合按部署环境在启动时配置，不建议在请求中频繁切换。

---

## 6. 对照表：一眼验证打码效果

上线的验证方法：把 9 种内置规则与原值逐一对照输出，确认每个字段都真的变了：

[!code-csharp[内置九类掩码演示](~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs#Mask_BuiltinNine)]

演示代码里有一条硬断言值得搬进项目测试：**脱敏输出必须与原值不同且含掩码符号**——防止"假脱敏"（规则配错导致明文直出还以为打了码）。

---

## 7. 与 ORM 投影的配合提醒

如果客户列表走 FreeSql / EF Core 投影（`AsDto` 直接出 DTO），要注意：**脱敏是 .NET 代码，SQL 翻译不了**——标了脱敏的 DTO 会自动触发投影降级（SQL 查实体 + 内存映射，特性全量生效）。列表量大时可感知多一次查询，判断与优化建议见 [ORM 降级机制](../explanation/orm-degradation.md)。

---

## 8. 常见问题

**脱敏会改数据库里的数据吗？** 不会。脱敏只发生在映射产物（DTO）上，实体与存储始终是明文；只有把 DTO 再写回实体时才需要留意（展示 DTO 不该用于保存，见 [日常双向映射演练 §6](mapping-tutorial.md) 的方向性忽略）。

**掩码符号能换吗？** 自定义规范用 `[Convert]` + 自定义转换器实现（第 3 节），内置规则的保留位数不支持配置。

**同一字段既要格式化又要脱敏行不行？** 目前不建议在同一属性上叠两套规则，组合行为以 [数据脱敏](../how-to/infrastructure/masking.md) 的说明为准；需要时拆成两个字段各自声明。

---

## 9. 完整代码示意

把第 2~6 步汇成脱敏模块的完整落地代码（教学完整示意；`JapanPhoneMask` 为自定义实现示例）：

```csharp
using NCMapper;
using NCMapper.Attributes;

// ================= 1. 实体（存储明文，业务需要） =================
public class CustomerEntity
{
    public string Name { get; set; } = "";
    public string Level { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Region { get; set; } = "";
}

// ================= 2. 展示 DTO（打码展示） =================
[MapFrom(typeof(CustomerEntity))]
public class CustomerMaskedDto
{
    public string Name { get; set; } = "";

    [MapProperty(typeof(CustomerEntity), nameof(CustomerEntity.Level))]
    public string Level { get; set; } = "";

    public string Region { get; set; } = "";

    [MapProperty(typeof(CustomerEntity), nameof(CustomerEntity.Phone))]
    [Mask(typeof(MaskPhone))]
    public string Phone { get; set; } = "";
}
```

第 3 段「页面服务（映射即脱敏）」——`Phone` 已按 138\*\*\*\*5678 打码的可运行版本：

[!code-csharp[页面服务 Get：映射即脱敏](~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs#Mask_MaskingCustomerGet)]

第 4 段「按环境切换策略（应用启动时执行一次）」——`MaskProvider.Override<MaskPhone>(new JapanPhoneMask())` 的可运行包装（国内环境保持内置默认，无需任何代码）：

[!code-csharp[按环境 Override 注册（启动期执行一次）](~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs#Mask_ConvertUsing_OverrideMask)]

第 5 段「上线验证（放测试里）」——硬断言防"假脱敏"的可运行版本：

[!code-csharp[上线验证：脱敏输出必须与原值不同](~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs#Mask_MaskingSmokeTour)]

读这份代码的顺序：实体（明文存储）→ DTO（`[Mask]` 声明打码规则）→ 服务（一行映射自动生效）→ 启动配置（按环境换策略）→ 冒烟断言（防配错明文直出）。

---

## 相关文档

- [数据脱敏](../how-to/infrastructure/masking.md)：脱敏全部用法与规则表
- [日常双向映射演练](mapping-tutorial.md)：展示 DTO 与保存 DTO 的隔离做法
- [ORM 降级机制](../explanation/orm-degradation.md)：投影场景下脱敏触发的自动降级
- [Fluent 转换](../how-to/fluent/convert.md)：Fluent 通道的转换器声明
