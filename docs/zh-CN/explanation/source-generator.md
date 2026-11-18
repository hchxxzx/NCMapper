<!-- 来源：NCMapper 主技术文档 §7 源生成器 -->
## 7. 源生成器

**工作原理** — NCMapper.SourceGen 是一个 Roslyn **增量源生成器**（`IsRoslynComponent=true`），作为 analyzer 随 NuGet 包分发，编译期自动运行，零配置。它在编译期扫描所有标注 `[MapFrom]` 的 DTO，提取映射规则（源类型、属性映射、`[Convert]` 转换器等），生成等价的映射代码：`GeneratedMapper`（核心映射方法）与 `GeneratedExtensions`（`MapToXxxDto` 扩展方法）。生成的委托通过 `[ModuleInitializer]` 在程序集加载时自动注册到 `Mapper`，使 `Mapper.Map` 也能优先调用源生成器委托，未命中才回退运行时表达式树。

**意义** —

| 维度     | 说明                                                                         |
| ------ | -------------------------------------------------------------------------- |
| 零反射    | 映射代码编译期生成，运行时直接调用，无反射开销                                                    |
| AOT 友好 | 编译期确定全部代码，NativeAOT 部署无运行时反射/表达式树编译问题                                      |
| 类型安全   | 映射规则编译期校验，属性重命名时编译器即时报错，而非运行时静默失效                                          |
| 编译时诊断  | 配置错误（转换器类型不匹配、枚举映射不匹配等）以 NCMAP001-034[缺004] + NCMAP099 警告/错误/提示输出 |
| 性能最优路径 | 生成的扩展方法零委托、JIT 可内联；`Mapper.Map` 优先查源生成委托                                   |

> 下方 7.1 架构展示 Roslyn 增量管线的具体阶段。

### 7.1 架构

```
Roslyn 增量管线
  │
  ├── ForAttributeWithMetadataName("NCMapper.Attributes.MapFromAttribute")
  │     └── 过滤所有 [MapFrom] 标注的 class
  │
  ├── MappingExtractor.ExtractMappingInfo()
  │     └── 提取语义信息 + 生成映射配置相关诊断（含 099）
  │
  ├── Collect() + Combine(globalOptions)
  │
  └── RegisterSourceOutput()
        ├── CodeGenerator.GenerateMapperCode()  — 生成映射代码
        └── EmitDiagnostics()                    — 输出诊断警告
```

> **CancellationToken 支持**：管线在循环体中检查 `CancellationToken`，支持 IDE 取消长时间运行的代码生成操作。
>
> **ModuleInitializer 自动注册**：生成的 `GeneratedMapperInitializer` 类使用 `[ModuleInitializer]` 特性，在模块加载时自动注册所有映射委托到 `Mapper`，无需用户手动调用。

### 7.2 生成内容

| 类                            | 内容                                             |
| ---------------------------- | ---------------------------------------------- |
| `GeneratedMapper`            | Core 映射方法、Action 包装、MapList、DeepCopy           |
| `GeneratedExtensions`        | `MapTo[Target]()` / `MapTo[Target]List()` 扩展方法 |
| `GeneratedMapperInitializer` | `[ModuleInitializer]` 自动注册所有委托到 Mapper         |

> **fallback 注释**：当源生成器无法在编译时解决类型转换（非隐式转换、枚举等），生成的赋值代码前会插入 `// fallback to runtime: TypeConvertHelper.ConvertValue<S, T>(src.Prop)` 注释，帮助开发者识别哪些属性走运行时回退路径。

### 7.3 扩展方法（GeneratedExtensions）

源生成器编译时为每个 `[MapFrom]` 标注的 DTO 自动生成扩展方法，放在 `NCMapper.Generated` 命名空间下。扩展方法直接调用 Core 映射方法，**零委托开销**，JIT 可内联（`[MethodImpl(AggressiveInlining)]`），是性能最优的调用方式。

#### 可用方法

对每个 `[MapFrom(typeof(XxxEntity))]` 标注的 `XxxDto`，自动生成：

| 扩展方法                          | 签名                                                     | 说明        |
| ----------------------------- | ------------------------------------------------------ | --------- |
| `MapToXxxDto()`               | `this XxxEntity → XxxDto?`                             | 基础映射，零委托  |
| `MapToXxxDto(Action)`         | `this XxxEntity + Action → XxxDto?`                    | 带回调映射     |
| `MapToXxxDtoList()`           | `this IEnumerable<XxxEntity> → List<XxxDto>?`          | 列表映射      |
| `MapToXxxDtoList(Action)`     | `this IEnumerable<XxxEntity> + Action → List<XxxDto>?` | 列表映射带回调   |
| `MapToXxxDto(target)`         | `this XxxEntity + XxxDto → void`                       | 更新已有对象    |
| `MapToXxxDto(target, Action)` | `this XxxEntity + XxxDto + Action → void`              | 更新已有对象带回调 |

> ORM 投影不通过扩展方法暴露。投影入口为 AsDto 终端操作（详见 [ORM Projection 设计](orm-projection-design.md)），内部自动处理降级逻辑。

#### 使用示例

[!code-csharp[源生成器入口四形态：直调/带 Action/列表/更新已有](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#Core_Generated_EntryExtensions)]

#### 与 Mapper.Map 的区别

| 维度     | `Mapper.Map<TEntity, TDto>()` | `entity.MapToUserDto()`    |
| ------ | ----------------------------- | -------------------------- |
| 调用方式   | 泛型方法                          | 实例扩展方法                     |
| 委托开销   | 有（泛型缓存查找）                     | 零（直调 GeneratedMapper.Core） |
| JIT 内联 | 否                             | 是（AggressiveInlining）      |
| 适用场景   | 通用/动态/非源生成器路径                 | 性能敏感的热路径                   |
| 可用性    | 永远可用                          | 需源生成器编译通过                  |

### 7.4 控制开关

```xml

<PropertyGroup>
  <NCMapperSourceGenEnabled>false</NCMapperSourceGenEnabled>
</PropertyGroup>
```

### 7.5 映射关系可视化

```xml

<NCMapperGenerateReport>true</NCMapperGenerateReport>
```

编译时在 `obj/Generated/` 目录生成 `NCMapperMappingReport.g.cs`，包含 Markdown 格式的映射关系报告，涵盖 direct / [MapProperty] rename / navigation flatten / [Convert] 映射方式分类。

### 7.6 NCMAP018 分析器：数组 Contains 表达式树绑定检测

`ArrayContainsInExpressionAnalyzer` 是源生成器程序集（`NCMapper.SourceGen.dll`，随核心包以 Analyzer 资产分发）中的独立 `DiagnosticAnalyzer`，与增量源生成器共享同一程序集但职责分离——源生成器负责代码生成，分析器负责编译期检测。

**触发机制：**

1. 检测 `InvocationExpression` 中调用 `Contains` 方法的语法节点
2. 通过语义模型判定方法符号是否为 `MemoryExtensions.Contains(ReadOnlySpan<T>, T)`（而非 `Enumerable.Contains(IEnumerable<T>, T)`）
3. 判定调用点是否处于 `Expression<Func<...>>` lambda 的表达式树上下文中

三个条件全部满足时 emit NCMAP018（Warning），因为 .NET 10/C# 14 的重载决议变更会导致 `T[].Contains()` 在表达式树中绑定到 `MemoryExtensions.Contains`，而 FreeSql/EF Core 等 ORM 表达式树解析器仅识别 `Enumerable.Contains`——运行时抛 `NotSupportedException`。

**修复建议：**

```csharp
// 触发 NCMAP018 的写法（.NET 10 + C# 14）
fsql.Select<OrderEntity>()
    .Where(o => ids.Contains(o.Id))  // ids 为 int[] → MemoryExtensions.Contains → NotSupportedException
    .ToList();

// 修复方式一：使用 Enumerable.Contains 显式限定
fsql.Select<OrderEntity>()
    .Where(o => Enumerable.Contains(ids, o.Id))
    .ToList();

// 修复方式二：将数组转为 List<T>（List<T>.Contains 不触发重载变更）
var idList = ids.ToList();
fsql.Select<OrderEntity>()
    .Where(o => idList.Contains(o.Id))
    .ToList();
```

> **自动 TFM 适配**：分析器通过方法符号判定（而非显式判断目标框架），在 net8/net9（C# 13）下 `T[].Contains` 仍绑定 `Enumerable.Contains`，不会误报 NCMAP018。

---

