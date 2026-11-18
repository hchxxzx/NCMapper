---
wiki-id: explanation/dual-channel
涉及诊断码: NCMAP_F11、NCMAP_F16、NCMAP_F39、NCMAP_F15、NCMAP025
库版本: v1.0.0+
更新时间: 2026-09-29
---

# 双通道架构原理

## 概述

NCMapper 提供两条声明映射的通道：

- **Fluent 通道**（第一主线）：在一个独立的配置类上标注 `[MapperConfig]`，用链式方法声明映射。
- **Attribute 通道**（第二体系）：把 `[MapFrom]` 族特性直接标注在 DTO 类上。

两条通道声明的是同一件事（"源类型怎么变成目标类型"），最终也走同一套执行代码。本篇解释为什么要有两条通道、两条通道如何共享同一套语义、同一对为什么禁止双声明、以及声明在编译期如何汇成一份生成代码。

引擎层面（源生成器与表达式树）的分工见 [双引擎架构](./dual-engine.md)；两通道 API 的逐项对照与选型表见 [Fluent 与 Attribute 双 API 面对照](../reference/api-comparison.md)——本篇聚焦架构原理，不重复对照表。

---

## 1. 为什么有两条通道

一条通道解决不了两类用户的同时诉求：

| 诉求 | Fluent 通道 | Attribute 通道 |
|------|:---:|:---:|
| DTO 类保持干净，不掺映射标注 | 能（映射意图写在独立配置类里） | 不能（特性标在 DTO 上） |
| 打开 DTO 就能看到映射关系 | 不能（要翻到配置类） | 能（所见即所得） |
| 链式顺序依赖、同链多终端组合 | 能 | 不能 |
| 构造器形参绑定（MapCtorParam） | 能（Fluent 独有） | 无特性侧形态 |
| 团队习惯"类上标注"的声明风格 | 不习惯 | 习惯 |

设计结论不是二选一，而是**两条通道并存、语义等价**：用户按声明位置偏好选择，不同类型对之间可以自由混用两条通道；只有同一个类型对被两条通道同时声明时才禁止（见第 4 节）。

---

## 2. 两条通道长什么样

### 2.1 Attribute 通道：DTO 自描述

```csharp
[MapFrom(typeof(UserEntity))]
public class UserDto
{
    public string Name { get; set; }
    [MapProperty("user_name")]
    public string UserName { get; set; }
}
```

`[MapFrom]` 标在 DTO 类上，属性级特性（`[MapProperty]`、`[Convert]`、`[Mask]` 等）跟着属性走。优点是"打开类就看到全部映射关系"，缺点是 DTO 类被映射标注"污染"。

### 2.2 Fluent 通道：配置类集中声明

```csharp
[MapperConfig]
public class UserMapper
{
    public static void Configure(FluentMappingConfig cfg)
    {
        cfg.For<UserEntity, UserDto>()
           .Member(d => d.UserName).From(s => s.user_name)
           .Member(d => d.Secret).Ignore();
    }
}
```

`[MapperConfig]` 标在一个独立配置类上（方法名任意，方法签名必须是 `static void Xxx(FluentMappingConfig cfg)`），映射意图全部写在方法体的链式调用里。DTO 类一个特性都不用加。类里没有任何匹配签名的方法时会触发 `NCMAP_F15` 提示。

入口约定定义位置：`MapperConfigAttribute.cs`（特性定义）、`FluentMappingConfig.cs`（链式配置对象）。

---

## 3. 语义共享层：一套规则，两个入口

两条通道不是两套独立实现。编译期，源生成器把两条通道的声明**统一翻译成同一份内部映射清单**（内部类型 `MappingSnapshot`，记录"哪个类型对、每个属性从哪来、怎么转换、怎么忽略"）。从这一步起，生成代码只认清单、不认声明来自哪条通道。

因此：

- 属性匹配、类型转换、忽略规则、嵌套处理等核心语义由两通道**共享同一套实现**（语义单点同源，详见语义三篇：[映射规则](./semantics/mapping-rules.md)、[双向方向语义](./semantics/direction-semantics.md)、[诊断码体系模型](./semantics/diagnostics-model.md)）
- 两条通道为同一类型对生成**完全相同**的映射方法（正向 6 形态 + 反向 4 形态派生、深拷贝、运行时注册），运行时行为零差异
- 等价能力逐项对齐（Fluent 的 `.Convert` 对特性侧 `[Convert]`、`.Ignore` 对 `[MapIgnore]` 等，等价对编号 A-xx），对齐关系由测试与机械面门禁逐行锁定，防止两条通道的语义随版本漂移

---

## 4. 编译期汇流与互斥规则

两条通道的提取产物在源生成器内部有一个**合并点**（内部实现为双通道合并仲裁器 `DualDriverArbiter`）：按"源类型 + 目标类型"的键值把两边的声明合到一张清单里，再交给代码生成。合并时执行一组互斥检查——**同一件事被声明两次且说法可能不一致时，宁可不出代码也不猜**。

### 4.1 四条互斥/冲突规则

| 诊断码 | 场景 | 处置 |
|--------|------|------|
| `NCMAP_F11` | 同一类型对被**两条通道同时声明**（跨通道双声明） | 两个声明位置各报一条（消息互相指出对方在哪），两侧声明都不生成；运行时由统一入口的表达式树兜底 |
| `NCMAP025` | **同通道内**双向互指（A 声明映射到 B、B 又声明映射到 A，如 `A [MapFrom(B)]` 且 `B [MapFrom(A)]`） | Error 级诊断、该对不生成；运行时按名匹配兜底，删除一侧声明可恢复生成 |
| `NCMAP_F16` | Fluent 通道**内部**同一类型对重复声明（如两个配置类都写了 `For<A, B>()`） | 每处声明各报一条（消息列出全部冲突方），全部不生成 |
| `NCMAP_F39` | 同一类型对的生命周期钩子跨通道同类冲突（两侧都声明 Before 或都声明 After，且不是同一方法） | 两个位置各报一条，指明两侧方法名 |

注意 `NCMAP_F11` 与 `NCMAP025` 的分工：**F11 管跨通道**（Fluent 与 Attribute 抢同一对），**025 管同通道内的双向互指**。类型对按无序判定——`For<A, B>()` 之后声明 `For<B, A>()` 同样算同一对。

### 4.2 模式并存是默认

互斥只针对"同一类型对"。不同类型对之间，两条通道随便混用：

```csharp
[MapperConfig]
public class OrderMapper
{
    public static void Configure(FluentMappingConfig cfg)
    {
        cfg.For<OrderEntity, OrderDto>()...;   // 订单走 Fluent
    }
}

[MapFrom(typeof(ProductEntity))]              // 商品走 Attribute
public class ProductDto { ... }
```

这是设计推荐的常态：按"这个映射需要什么能力、团队把映射写在哪里更顺手"逐对选择，互不影响。

### 4.3 显式声明优先于自动合成

启用多态子类型映射（`MapSubclass`）时，源生成器会为子类对**自动合成**声明。合成遵循"显式优先"：子类对若已有任一通道的显式声明，以显式为准，自动合成让位且不报冲突——合成的意义只是补空白，不是抢地盘。

---

## 5. 诊断码分域

两条通道的诊断码分两个号段，从码号就能看出声明写在哪一侧：

| 号段 | 归属 | 示例 |
|------|------|------|
| `NCMAP0xx`（三位数字） | Attribute 通道（标在 DTO 类上的特性） | NCMAP025 互指声明、NCMAP033 反向转换器不消费 |
| `NCMAP_Fxx`（F 前缀） | Fluent 通道（写在配置类里的链式声明） | NCMAP_F11 双通道冲突、NCMAP_F16 通道内重复、NCMAP_F15 配置类无有效方法 |

跨通道问题（F11、F39）归入 Fluent 号段，因为修复动作通常落在"两侧之一删声明"，Fluent 侧的诊断流负责牵头报出。全族索引见 [Fluent 编译期诊断](../how-to/fluent/basics.md#编译期诊断ncmap_f-系列全族索引)与 [诊断规则](../diagnostics/rules.md)。

---

## 6. 能力差异面

语义等价不等于能力完全一一对应。少数能力只在一侧存在：

- **Fluent 独有**：构造器形参绑定（`.MapCtorParam`）、链式顺序依赖（同链多终端按书写顺序生效）、条件守卫 `.MapWhen` 的内联 lambda 写法（直接在调用处写判断条件，不用先定义命名方法）
- **Attribute 侧保留**：实体侧（非 DTO 侧）的 `[ConvertBack]` 反向转换器——这类表达在特性侧有原生形态，不强求翻成 Fluent

完整等价关系与差异逐行对照见 [双 API 面对照 §2](../reference/api-comparison.md)；新增能力默认先进 Fluent 主线，再按等价对机制回补特性侧。

---

## 速查表

| 概念 | 说明 |
|------|------|
| 双通道 | Fluent 通道（`[MapperConfig]` 配置类 + 链式声明，第一主线）与 Attribute 通道（`[MapFrom]` 标在 DTO 上，第二体系） |
| 语义共享 | 两通道声明统一翻译成同一份内部映射清单，匹配/转换/忽略/嵌套语义单点同源，生成代码完全相同 |
| 汇流点 | 源生成器内部按"源类型 + 目标类型"键值合并两通道声明，合并时执行互斥检查 |
| 同对双声明 | `NCMAP_F11`：两个位置各报一条、两侧不生成、运行时表达式树兜底 |
| 同通道互指 | `NCMAP025`：双向声明 Error 级拦截；`NCMAP_F16`：Fluent 通道内同对重复 |
| 模式并存 | 不同类型对可自由混用两通道，互不影响 |
| 诊断分域 | `NCMAP0xx` = Attribute 侧；`NCMAP_Fxx` = Fluent 侧 |

---

## 常见疑问

**两条通道会各自生成一份映射方法吗？**

不会。汇流点合并后只按类型对生成一份；同对双声明时两侧都不生成（F11 拦截），不会出现重复方法。

**混用两条通道会互相干扰吗？**

不同类型对之间完全隔离，混用是设计推荐用法。会出问题的只有"同一类型对被两边都声明"这一种，编译期就会报 `NCMAP_F11`，不会带病运行。

**同一对双声明了，运行时还能映射吗？**

能，但走的是通用兜底路径（表达式树引擎），且编译告警必须处理——因为两条声明可能不一致，兜底结果不保证与任一声明相符。正确做法是删掉一侧声明。

**为什么新增能力默认先进 Fluent？**

Fluent 是第一主线：配置类集中声明便于表达链式顺序等复杂意图，且不侵入 DTO。能力在 Fluent 侧稳定后，再按等价对机制回补特性侧形态，保证两条通道逐步对齐。

---

## 相关文档

- [双引擎架构](./dual-engine.md)：声明汇成生成代码后，源生成器与表达式树两个引擎如何分工
- [自适应路由机制](./adaptive-routing.md)：声明完成后组件如何在各决策点自动选路
- [Fluent 与 Attribute 双 API 面对照](../reference/api-comparison.md)：两通道逐项等价对照与选型表（唯一对照页）
- [Fluent 基础（链式声明入口）](../how-to/fluent/basics.md)：Fluent 通道操作指南与 F 族诊断全索引
- [Attribute 基础映射](../how-to/declarative/basics.md)：Attribute 通道操作指南
- [映射规则（语义）](./semantics/mapping-rules.md)：两通道共享的语义单点
