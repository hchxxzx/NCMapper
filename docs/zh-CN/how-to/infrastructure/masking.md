---
wiki-id: how-to/infrastructure/masking
涉及诊断码: NCMAP034
库版本: v1.0.0+
更新时间: 2026-09-15
---

# 数据脱敏

## 概述

NCMapper 内置 9 种脱敏规则，通过 `[Mask(typeof(XxxMask))]` 标注在 DTO 属性上，映射时自动脱敏。支持运行时通过 `MaskProvider.Override<TMask>()` 替换脱敏实现，实现不同部署环境（如国际化场景）的策略切换，无需修改 DTO 特性标注。

核心 API：`[Mask(typeof(MaskPhone))]` / `MaskProvider.Override<TMask>(impl)` / `MaskProvider.Resolve(type)` / `MaskProvider.Clear()`

> **声明式双通道**：脱敏在 Fluent 通道有等价终端 `.Mask(...)`（链式声明，无需改 DTO），两通道等价并存（同类型对互斥声明 `NCMAP_F11`），见 [Fluent 入口](../fluent/basics.md)。

### 声明通道与生效面

脱敏声明经两条通道写入注册表，在运行时内存映射的生效面如下：

| 通道 | 声明形态 | 内存映射（`Mapper.Map`） | ORM 投影（AsDto） |
|---|---|---|---|
| 特性通道 | DTO 属性 `[Mask(typeof(XxxMask))]` | 生效（特性声明步骤） | 触发降级，二步映射生效 |
| Fluent 通道 | `.Member(d => d.X).Mask<TMask>()` 及手工注册明细 | 生效（注册表声明步骤） | 强制降级，二步映射生效 |
| Fluent 通道（Compose 跨表明细） | `From<Sub>(…).Mask<TMask>()` 跨表字段声明 | 生效（降级后二步映射的内存映射步骤消费 Compose 明细掩码位） | 强制降级（cm 级 Mask 不可译），二步映射生效 |

分派与边界要点：

- **特性声明优先、注册表明细补位，同一属性不双重施加**：注册表明细仅在特性声明未命中时消费（两通道同类型对互斥由 `NCMAP_F11` 编译期拦截，运行时手工双声明直接抛异常）
- **双标组合按序执行**：`[Convert]` / `[MapFormat]` 与注册表脱敏声明同属性时，先转换/格式化、后脱敏
- **点路径与命名扁平化叶节点同样生效**：`AddressCity → src.Address.City` 自动拆解与显式点路径的叶节点均消费注册表脱敏声明
- **Compose 跨表明细掩码在内存映射步骤生效**：注册表明细解析同时覆盖属性明细（pm 级）与 Compose 跨表明细（cm 级，仅 Mask 位）——cm 级带 Mask 的字段按"不可译"强制降级后，二步映射的内存映射经同一注册表明细消费该掩码（同目标属性 pm 优先 cm 补位）；跨表明细的取值以同名直配形态为限（非同名/多段路径取值属已登记限制，见[已知限制](../../explanation/limitations.md)）
- **不可解析类型名显式失败**：脱敏类型名按"目标程序集 → 源程序集 → `Type.GetType` → 已加载程序集扫描"四段解析，全 miss 抛 `MapException`，不静默降级
- **可观测性**：带脱敏声明但无类型名、目标面无同名属性、非 `string` 目标，均输出 Trace 警告并照常映射
- **已知限制**：先映射后注册的类型对，已物化的映射委托不因后续注册改变；新类型对"先注册后映射"即生效

---

## 1. 9 种内置脱敏规则

NCMapper 内置以下 9 种脱敏规则，覆盖常见 PII（个人身份信息）字段：

| 规则 | 类 | 脱敏效果 | 保留规则 |
|---|---|---|---|
| 手机号 | `MaskPhone` | `138****5678` | 前 3 后 4 |
| 身份证 | `MaskIdCard` | `1101**********1234` | 前 4 后 4，中间等量 `*` |
| 银行卡 | `MaskBankCard` | `6222****1234` | 前 4 后 4 |
| 邮箱 | `MaskEmail` | `z***@example.com` | 首字符 + `@` 后域名 |
| 中文姓名 | `MaskChineseName` | `张*丰` | 姓 + 末字，中间 `*` |
| 地址 | `MaskAddress` | `北京市朝****` | 前 6 字符（省市） |
| IP 地址 | `MaskIp` | `192.168.*.*` | 前两段 |
| 车牌 | `MaskLicensePlate` | `京A***45` | 前 2 后 2，中间等量 `*` |
| 固话 | `MaskLandline` | `010-****5678` | 区号（含 `-`）+ 后 4 |

### 用法

在 DTO 属性上标注 `[Mask(typeof(对应规则类))]`：

```csharp
using NCMapper;
using NCMapper.Attributes;

// MaskUserEntity 为明文存储源（Phone/Email 均为明文字段）
[MapFrom(typeof(MaskUserEntity))]
public class MaskedUserDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    // 手机号脱敏：138****5678
    [Mask(typeof(MaskPhone))]
    public string Phone { get; set; } = "";

    // 邮箱脱敏：z***@example.com
    [Mask(typeof(MaskEmail))]
    public string Email { get; set; } = "";
}
```

映射时自动脱敏（`dto.Phone == "138****5678"`、`dto.Email` 打码形态）的可运行版本：

[!code-csharp[映射即脱敏（MaskPhone/MaskEmail 标注对）](~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs#Mask_MaskedUserMap)]

**要点**：

- `[Mask]` 标注在 **DTO 属性**上（不是 Entity），脱敏是 DTO 层面的责任
- 映射执行时，源生成器在静态字段初始化阶段通过 `MaskProvider.Resolve<TMask>()` 获取脱敏实例
- 脱敏在属性赋值后、映射结果返回前执行——对使用者透明
- 内置规则使用 `StringInfo` 按文本元素切片，正确处理 CJK Ext B+ 生僻字（U+20000+ 代理对）和 emoji

### 自定义脱敏规则

实现 `IMask` 接口即可创建自定义规则：

```csharp
public class JapanPhoneMask : IMask
{
}
```

`Mask` 方法的可运行实现（samples 场景 605，前 3 后 2 保留、中间打星）：

[!code-csharp[自定义脱敏实现 JapanPhoneMask.Mask](~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs#Mask_JapanPhoneMask_Mask)]

---

## 2. 运行时替换 MaskProvider

`MaskProvider` 是脱敏实现的全局注册表，允许在应用启动时替换内置 `IMask` 实现，无需修改 DTO 上的 `[Mask]` 特性标注。

### 2.1 Override：替换脱敏实现

[!code-csharp[运行时替换掩码实现（Override）](~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs#Mask_ConvertUsing_OverrideMask)]

### 2.2 Resolve：获取当前脱敏实例

[!code-csharp[MaskProvider.Resolve 获取当前脱敏实例（泛型包装与非泛型双形态）](~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs#Mask_ConvertUsing_ResolveMask)]

### 2.3 Clear：恢复默认实现

[!code-csharp[清空 Override 注册恢复内置默认](~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs#Mask_ConvertUsing_ResetMasks)]

### 2.4 完整示例：国际化场景切换

[!code-csharp[多区域部署条件 Override，映射代码零改动](~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs#Mask_MultiLocaleSwap)]

**要点**：

- `Override` 是全局的，影响后续所有映射调用
- `Override` 须在该 Mask 类型**首次被映射调用之前**执行（源生成器在静态字段初始化时解析一次，后续不再经过 `Resolve`）
- `Clear()` 清除所有 Override，但已初始化的生成代码静态字段不会自动重置——需配合重新触发生成或重启进程
- `Mapper.ClearCache()` 会自动调用 `MaskProvider.Clear()`，是完整的缓存重置入口

> ⚠️ **Mask 运行时 fail-fast**：如果 `[Mask]` 标注的类型不实现 `IMask` 接口，或无法实例化，`CreateMapFunc` 最前端会抛 `MapConfigurationException`，而非静默跳过。这是设计决策：脱敏涉及合规要求（GDPR/个保法），配置错误必须尽早暴露。

---

## 3. IMask 接口与实现规范

```csharp
/// <summary>
/// 脱敏接口 — 所有脱敏规则必须实现此接口。
/// </summary>
public interface IMask
{
    /// <summary>
    /// 对输入值执行脱敏处理。
    /// </summary>
    /// <param name="value">原始值（可能为 null）</param>
    /// <returns>脱敏后的字符串</returns>
    string Mask(string? value);
}
```

**实现规范**：

1. `Mask` 方法必须处理 `null` 和空字符串（返回空字符串或原值，视业务而定）
2. 非规范输入降级掩码：长度不足/格式不符时**不再明文返回原值**——统一降级为「首文本元素 + `****`（单元素全遮 `****`）」形态（如 `MaskPhone("123456")` → `"1****"`、`MaskIp("2001:db8::1")` → `"2****"`）。脱敏通道 fail-safe：宁可过度脱敏不泄漏原文。自定义 `IMask` 实现建议遵循同一口径
3. 使用 `StringInfo` 按文本元素切片，避免在代理对中间切分（CJK Ext B+ 生僻字、emoji）
4. 脱敏逻辑应是纯函数（无副作用，不依赖外部状态）

---

## 4. Mask 与 Convert 的区别

| 维度 | `[Mask]` | `[Convert]` |
|---|---|---|
| 职责 | 脱敏（合规要求，不可逆） | 数据变换（格式化、组合等） |
| 接口/形态 | `IMask.Mask(string?)` 实例方法 | 静态转换方法（T0–T3 模板） |
| 输入 | 单个字符串 | 源字段值 / 源对象（按模板） |
| 输出 | 脱敏后的字符串 | 任意目标类型 |
| 运行时替换 | 支持（`MaskProvider.Override`） | 不支持（编译时绑定） |
| 典型场景 | 手机号/身份证/邮箱脱敏 | 日期格式化、多字段组合 |

**选择建议**：如果目的是"隐藏敏感信息以满足合规要求"，用 `[Mask]`；如果目的是"变换数据格式"，用 `[Convert]`。

---

## 常见误用

> ⚠️ **脱敏 DTO 请勿反向写回**：`[Mask]` 仅正向消费——反向映射（DTO → Entity，含 `dto.MapToXxxEntity()` 直调）**不调用脱敏器**，而是把 DTO 属性当前值原样写入实体。若该值是正向脱敏的结果，写回的就是**脱敏值**，将覆盖实体中的完整数据（round-trip 有损）。生成器对此报 NCMAP034（Info）警示。CRUD 更新场景建议对脱敏 DTO 属性标注 `[MapIgnore(Direction.ToEntity)]` 阻断反向写回：
>
> ```csharp
> [MapFrom(typeof(UserEntity))]
> public class UserDto
> {
>     [Mask(typeof(MaskPhone))]
>     [MapIgnore(Direction.ToEntity)]  // 脱敏值不写回实体
>     public string Phone { get; set; } = "";
> }
> ```

### 误用 1：在 Entity 属性上标注 [Mask]

```csharp
// ❌ 误用：[Mask] 标注在 Entity 上不生效
public class UserEntity
{
    [Mask(typeof(MaskPhone))]  // 脱敏是 DTO 层面的责任
    public string Phone { get; set; }
}

// ✅ 正确：[Mask] 标注在 DTO 属性上
[MapFrom(typeof(UserEntity))]
public class UserDto
{
    [Mask(typeof(MaskPhone))]
    public string Phone { get; set; } = "";
}
```

### 误用 2：Override 在映射调用后执行

```csharp
// ❌ 误用：先映射再 Override，Override 不生效
var dto = Mapper.Map<UserEntity, UserDto>(entity);  // 此时已用默认 MaskPhone 解析
MaskProvider.Override<MaskPhone>(new JapanPhoneMask());  // 太晚了

// ✅ 正确：Override 须在首次映射调用之前
MaskProvider.Override<MaskPhone>(new JapanPhoneMask());
var dto = Mapper.Map<UserEntity, UserDto>(entity);  // 走 JapanPhoneMask
```

> 原因：源生成器在静态字段初始化时调用 `MaskProvider.Resolve<TMask>()` 一次，后续映射直接使用缓存的实例。`Override` 只影响后续的 `Resolve` 调用，不改变已初始化的静态字段。

### 误用 3：自定义 Mask 不处理 null

```csharp
// ❌ 误用：不处理 null，运行时 NullReferenceException
public class BadMask : IMask
{
    public string Mask(string? value) => value[..3] + "****";  // value 为 null 时崩溃
}

// ✅ 正确：处理 null 和空字符串
public class GoodMask : IMask
{
    public string Mask(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? "";
        return value.Length >= 5 ? value[..3] + "****" + value[^2..] : value;
    }
}
```

---

## 小结

| 能力 | API | 特性 |
|---|---|---|
| 内置脱敏 | `[Mask(typeof(MaskPhone))]` | 9 种规则，标注即生效 |
| 自定义脱敏 | 实现 `IMask` 接口 | 任意脱敏逻辑 |
| 运行时替换 | `MaskProvider.Override<TMask>(impl)` | 全局替换，无需改 DTO |
| 获取实例 | `MaskProvider.Resolve<TMask>()` | 泛型（源生成器）/ 非泛型（运行时） |
| 恢复默认 | `MaskProvider.Clear()` | 清除所有 Override |
| 完整重置 | `Mapper.ClearCache()` | 清除映射缓存 + MaskProvider |

---


本页可运行演示（场景 605）：

[!code-csharp[9 个内置脱敏实现对照（原值 → 脱敏值）](~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs#Mask_BuiltinNine)]

[!code-csharp[自定义 IMask 注册与恢复（Provider 层）](~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs#Mask_CustomProvider)]

## 配套示例

```bash
cd samples/NCMapper.Samples
dotnet run --scenario 605
```

核心示例源码（单点同源，`dotnet run --scenario` 可运行验证）：

[!code-csharp[脱敏映射](~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs#Mask_ConvertUsing)]

[!code-csharp[脱敏映射 · MaskedCustomerMap](~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs#Mask_ConvertUsing_MaskedCustomerMap)]

[!code-csharp[脱敏映射 · ResolveMask](~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs#Mask_ConvertUsing_ResolveMask)]

[!code-csharp[脱敏映射 · OverrideMask](~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs#Mask_ConvertUsing_OverrideMask)]

[!code-csharp[脱敏映射 · ResetMasks](~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs#Mask_ConvertUsing_ResetMasks)]

代码见 `samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs`，涵盖内置脱敏、运行时 Override、Resolve 验证、Clear 恢复四个子场景。

---

## 进一步阅读

- 相关篇目：[基础映射](../declarative/basics.md) · [Pipeline](pipeline.md)
- 技术文档深挖：[特性系统](../../reference/features.md) §5.5
- 脱敏规则源码：`src/NCMapper/Masks.cs`（9 种内置规则）
- Provider 源码：`src/NCMapper/MaskProvider.cs`（Override/Resolve/Clear）
