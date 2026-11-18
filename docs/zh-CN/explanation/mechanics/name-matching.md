<!-- 来源：NCMapper 主技术文档 §6.1 基本映射 -->
## 6. 映射场景详解

### 6.1 基本映射

NCMapper 的基本映射遵循"同名自动匹配"原则：源类型与目标类型中**名称相同**（大小写不敏感）的属性自动建立映射关系，无需任何额外标注。

#### 匹配规则

属性匹配使用 `StringComparer.OrdinalIgnoreCase`（序号忽略大小写），即 `UserName`、`username`、`USERNAME` 视为同一属性。匹配沿继承链向上搜索，子类属性覆盖基类同名属性（shadow property 支持）。

**参与匹配的属性须满足**：

| 条件                  | 说明                                                          |
| ------------------- | ----------------------------------------------------------- |
| 非索引器                | `GetIndexParameters().Length == 0`                          |
| 源属性有 getter         | `CanRead == true`                                           |
| 目标属性有 setter        | `CanWrite == true`（init-only 仅源生成器 object initializer 路径支持） |
| 未被 `[MapIgnore]` 标记 | 方向感知的忽略控制                                                   |
| 类型为 public 可见       | 非 public 属性不参与映射                                            |

```csharp
public class UserEntity
{
    public int Id { get; set; }
    public string Name { get; set; }
    public DateTime CreatedAt { get; set; }
    internal string Secret { get; set; }          // ✗ 非 public，不参与
    public string ReadOnly => "computed";          // ✗ 无 setter，目标侧不匹配
}

[MapFrom(typeof(UserEntity))]
public class UserDto
{
    public int Id { get; set; }                   // ✓ 同名同类型，直接赋值
    public string name { get; set; }              // ✓ 大小写不敏感，匹配 Name
    public DateTime CreatedAt { get; set; }       // ✓ 同名同类型
}
```

#### 类型兼容性分发

同名属性匹配成功后，按以下优先级链决定赋值方式：

```
1. [Convert] 自定义转换器     → 最高优先级（详见 Convert 转换）
2. 同类型（srcType == tgtType）    → 直接赋值 dst.Prop = src.Prop
3. 隐式数值转换（C# 规范 10.3.2 节）     → 显式 cast: dst.Prop = (long)src.Prop
4. Nullable → NonNullable         → dst.Prop = src.Prop.HasValue ? src.Prop.Value : default
5. NonNullable → Nullable         → dst.Prop = new int?(src.Prop)
6. 嵌套 class（异类型）            → 递归映射（详见嵌套对象映射）
7. 集合类型                       → 集合映射（详见回调与集合）
8. 字典类型                       → 字典映射
9. 枚举转换                       → switch 表达式（详见构造器与 record 类型）
10. TypeConvertHelper 回退         → 运行时转换（string↔基元、IConvertible 等）
```

**隐式数值转换**基于 C# 语言规范 10.3.2 节的预计算查找表，覆盖 `byte/sbyte → short/ushort → int/uint → long/ulong → float → double → decimal` 全部合法路径。枚举类型被排除在隐式转换之外（走枚举专用路径）。

**TypeConvertHelper 回退路径**覆盖源生成器无法在编译时解决的转换：

| 转换                  | 实现                             |
| ------------------- | ------------------------------ |
| `source is TTarget` | 直接返回（已是目标类型或子类）                |
| string → 基元类型       | `Convert.ChangeType`           |
| 值类型互转               | `IConvertible.ChangeType`      |
| enum → string       | `source.ToString()`            |
| string → enum       | `Enum.Parse(ignoreCase: true)` |
| 数值 → enum           | `Enum.ToObject`（`float`/`double` 除外——对齐下方参考表④） |
| 任意 → string         | `source.ToString()`            |
| 任意 → Guid           | `Guid.TryParse`                |
| 全部失败                | 抛出 `MapException`              |

> 不支持项：`string` ↔ `byte[]` **无自动转换**（`byte[]` 非 `IConvertible`，JSON/文本语义需显式声明）——需要 Base64 中转请用 `[Convert]` 自定义转换。

#### 自动转换类型清单（参考表）

以下完整列举同名属性**类型不同**时可自动转换的全部组合（编译期判据与运行时 `TypeConvertHelper` 能力面的并集）。

**① 同类型直接赋值**

| 源类型 | 目标类型 | 实现 |
| --- | --- | --- |
| 任意类型 `T` | 相同 `T`（含子类） | `dst.Prop = src.Prop` 直接赋值 |

**② 隐式数值拓宽**（C# 语言规范 10.3.2 节的完整镜像）

| 源类型 | 可自动转换为 |
| --- | --- |
| `byte` | `short`、`ushort`、`int`、`uint`、`long`、`ulong`、`float`、`double`、`decimal` |
| `sbyte` | `short`、`int`、`long`、`float`、`double`、`decimal` |
| `short` | `int`、`long`、`float`、`double`、`decimal` |
| `ushort` | `int`、`uint`、`long`、`ulong`、`float`、`double`、`decimal` |
| `int` | `long`、`float`、`double`、`decimal` |
| `uint` | `long`、`ulong`、`float`、`double`、`decimal` |
| `long` | `float`、`double`、`decimal` |
| `ulong` | `float`、`double`、`decimal` |
| `char` | `ushort`、`int`、`uint`、`long`、`ulong`、`float`、`double`、`decimal` |
| `float` | `double` |

**③ 可空类型 `Nullable<T>`**

| 源类型 | 目标类型 | 说明 |
| --- | --- | --- |
| `T?` | `T` | 同底层类型，`HasValue ? Value : default` |
| `T` | `T?` | 同底层类型，`new T?(src)` |
| 数值族 `A?` | 数值族 `B?` | 底层属数值族（含 bool/char）即可互转，如 `int? → long?` |

**④ 枚举转换**

| 源类型 | 目标类型 | 实现 |
| --- | --- | --- |
| `enum` | `enum` | 直接转换 |
| `enum` | `string` | `source.ToString()` |
| `string` | `enum` | `Enum.Parse(ignoreCase: true)` |
| `enum` | 数值类型 | 底层数值 `Convert.ChangeType` |
| 数值类型（**`float`/`double` 除外**） | `enum` | `Enum.ToObject` |

**⑤ `string` ↔ IConvertible 值类型**

| 源类型 | 目标类型 | 实现 |
| --- | --- | --- |
| `string` | 数值类型（`sbyte`…`decimal` 11 种） | `Convert.ChangeType` + InvariantCulture |
| `string` | `bool`、`char` | 同上 |
| `string` | `DateTime` | 同上 |
| `string` | `Guid` | `Guid.TryParse` |
| 数值类型、`bool`、`char`、`DateTime`、`Guid`、`enum` | `string` | `ToString()` / `ChangeType` |

**⑥ 数值族 ↔ 数值族（显式窄化）**

| 源类型 | 目标类型 | 说明 |
| --- | --- | --- |
| `sbyte`、`byte`、`short`、`ushort`、`int`、`uint`、`long`、`ulong`、`float`、`double`、`decimal`、`bool`、`char` 中任意类型 | 族内另一类型 | 含窄化（`long→int`、`double→int`、`bool↔数值`、`char↔数值`） |

**⑦ 结构映射（自动递归，非"类型转换"）**

| 类别 | 源类型 | 目标类型 | 说明 |
| --- | --- | --- | --- |
| 嵌套类 | 任意 class（非泛型、非 string、非委托） | 另一 class | 逐属性递归映射 |
| 集合 | `List<T>`、`T[]`、`IEnumerable` 等 | 任意集合形态 | 元素级递归转换，如 `List<A> → List<B>` |
| 字典 | `IDictionary`、`IReadOnlyDictionary` 系 | 字典形态 | K/V 逐项复制转换 |
| List → 字典 | `List<T>`、数组 | 字典形态 | 受控路径（`string` 源除外） |

#### 源生成器代码生成

源生成器根据类型兼容性生成不同的赋值代码：

```csharp
// 同类型 → 直接赋值
dst.Id = src.Id;

// 隐式转换 → 显式 cast
dst.LongValue = (long)src.IntValue;

// Nullable → NonNullable
dst.Age = src.Age.HasValue ? src.Age.Value : default(int);

// NonNullable → Nullable
dst.Score = new int?(src.Score);

// TypeConvertHelper 回退（生成代码前插入 fallback 注释）
// fallback to runtime: TypeConvertHelper.ConvertValue<string, int>(src.Code)
dst.Code = NCMapper.TypeConvertHelper.ConvertValue<string, int>(src.Code)!;
```

当所有属性都是"同类型直接赋值"或"隐式转换"时，源生成器使用 **object initializer** 语法（`new T { Prop1 = ..., Prop2 = ... }`），利于 JIT 优化；任何复杂映射（嵌套/集合/枚举/Convert）则回退到逐行赋值。

#### 未匹配属性的行为

| 引擎   | 行为                                                               |
| ---- | ---------------------------------------------------------------- |
| 源生成器 | 编译时输出诊断：**NCMAP006**（Warn，目标属性无匹配源）/ **NCMAP007**（Info，源属性无对应目标） |
| 运行时  | 静默跳过，目标属性保持默认值，无诊断输出                                             |

```csharp
[MapFrom(typeof(UserEntity))]
public class UserDto
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string NickName { get; set; }   // ⚠️ NCMAP006: 源类型无 NickName，保持 null
}
// UserEntity.CreatedAt 无对应目标属性 → ℹ️ NCMAP007
```

> 使用 `Mapper.Validate()` 可在运行时批量检测所有未匹配属性（见[映射验证](../validation.md)）。

