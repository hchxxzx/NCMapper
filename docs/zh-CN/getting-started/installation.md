---
wiki-id: getting-started/installation
库版本: v1.0.0+
更新时间: 2026-09-15
---

# 安装与包选型

> NuGet 包矩阵与选型指引。API 面选型（Fluent vs Attribute）见 [overview](overview.md) 与 [api-comparison](../reference/api-comparison.md)。

## 包矩阵

| 包 | 用途 | 何时安装 |
| -- | ---- | -------- |
| `NCMapper` | 核心库（运行时引擎 + 表达式树 + 反射回退）+ 源生成器（以 Analyzer 资产随包分发） | 必装（所有场景） |
| `NCMapper.FreeSql` | FreeSql 集成（`AsDto<T>()` 投影、分页、树构建） | 使用 FreeSql 时 |
| `NCMapper.EFCore` | EF Core 集成（`AsDto<T>()` 投影、降级检测） | 使用 EF Core 时 |
| `NCMapper.Dapper` | Dapper 集成（`AsDto` 列名映射、`ToPagedList`、`ToTreeList`） | 使用 Dapper 时 |

```bash
dotnet add package NCMapper
dotnet add package NCMapper.FreeSql    # 按需
```

## 目标框架

支持 net8.0 / net9.0 / net10.0。

## 源生成器启用

源生成器已内置于 `NCMapper` 包中，引用核心包即自动生效（以 Analyzer 资产分发），无需单独安装。验证：DTO 标注 `[MapFrom]` 后编译，应生成 `MapToXxxDto()` 扩展方法。禁用开关：`<NCMapperSourceGenEnabled>false</NCMapperSourceGenEnabled>`。

## 最小可运行示例

```csharp
using NCMapper.Attributes;

[MapFrom(typeof(UserEntity))]
public class UserDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

```

[!code-csharp[生成入口扩展方法三形态（创建/回调/更新）](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#Core_Generated_EntryExtensions)]

下一步：[快速开始](quickstart.md)、[15 分钟上手](tutorial-15min.md)。
