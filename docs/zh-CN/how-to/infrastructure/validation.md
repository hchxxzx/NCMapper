---
wiki-id: how-to/infrastructure/validation
涉及诊断码: NCMAP001, NCMAP002, NCMAP005, NCMAP018, NCMAP019, NCMAP032, NCMAP033, NCMAP034, NCMAP035, NCMAP036, NCMAP037, NCMAP099
库版本: v1.0.0+
更新时间: 2026-09-15
---

# 验证与诊断

## 概述

NCMapper 提供两个运行时诊断工具：`MapValidator` 检查映射属性完整性，`MapTracer` 输出映射过程的详细诊断日志。两者配合使用，可在开发阶段快速定位映射配置问题，在生产环境按需开启诊断。

核心 API：`Mapper.Validate()` / `MapValidator.ValidateMapping(srcType, tgtType)` / `MapTracer.GloballyEnabled` / `MapTraceOptions.Enable()` + `MapTracer.Trace`

---

## 1. MapValidator：属性完整性检查

### 用法

[!code-csharp[扫描全部与验证单类型对](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_ValidateMapping)]

### 验证结果级别

| 级别 | 含义 | 典型场景 |
|---|---|---|
| `OK` | 全部属性匹配 | 映射配置正确 |
| `Warning` | 部分属性未匹配 | `[MapProperty]` 源类型不一致（NCMAP002） |
| `Error` | 映射不可用 | 目标类型无公共构造函数、源类型不存在等 |

> **相关 API**：`MapValidationResult`——单个映射对的验证结果类型（含 `SourceType` / `TargetType` / `Level` / `MappedCount` / `TotalCount` / `UnmappedProperties` / `TypeMismatchProperties` / `Message` 等属性），即 `MapValidator.ValidateMapping(srcType, tgtType)` 的返回值与 `Mapper.Validate()` 列表的元素类型（源码锚：MapValidator.cs:30）。

### 用法

[!code-csharp[全量扫描/限定程序集/单类型对验证](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_ValidateAll)]

**要点**：

- `Validate()` 扫描当前 AppDomain 所有程序集中标注了 `[MapFrom]` 的类型对
- `Validate(params Assembly[])` 限定扫描范围
- 结果按级别分类：OK / Warning / Error
- `ValidateOrThrow()` 在有 Error 级别结果时抛 `MapConfigurationException`

### 验证报告输出

[!code-csharp[验证结果打印报告](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_ValidateAll)]

---

## 2. MapTracer：映射过程诊断

### 用法

```csharp
// 方式 1：全局开关
MapTracer.GloballyEnabled = true;

var dto = Mapper.Map<UserEntity, UserDto>(entity);
// → 控制台输出每步映射细节：
//   [Map] UserEntity → UserDto
//   [Property] Id: 1 → 1 (int → int)
//   [Property] Name: "ZhangSan" → "ZhangSan" (string → string)
//   [Property] Age: 30 → 30 (int → int)
//   [Skip] Email: 源类型不匹配
//   [Convert] Status: Active → "Active" (enum → string)

MapTracer.GloballyEnabled = false;

// 方式 2：重定向到 TextWriter
using var writer = new StreamWriter("trace.log");
var opts = MapTraceOptions.Enable(writer);
var report = MapTracer.Trace<UserEntity, UserDto>(entity, opts);
// → trace.log 文件中写入映射关系报告（MapTracer.Enable 不存在，
// 按调用 Trace 通过 MapTraceOptions.Enable + MapTracer.Trace 提供）
```

**要点**：

- Trace 输出映射的每一步细节：属性匹配、类型转换、跳过原因
- 生产环境默认关闭（有 ~15-20% 性能开销）
- 可通过 `MapTraceOptions.Enable(TextWriter)` 重定向到文件
- 适用于调试映射问题（如"为什么某属性没映射"）

### Trace 输出内容

| 标签 | 含义 |
|---|---|
| `[Map]` | 类型对映射开始 |
| `[Property]` | 属性映射成功（含源值→目标值、类型信息） |
| `[Skip]` | 属性跳过（含原因：类型不匹配、MapIgnore 等） |
| `[Convert]` | 类型转换（含转换方式：内置/Convert） |
| `[Mask]` | 脱敏处理（含 Mask 类型） |
| `[PostMap]` | PostMap 回调执行 |

---

### 2.1 LogCallback 与 Enable() 工厂

除全局开关和重定向到 TextWriter 外，MapTracer 还提供两种进阶用法。

**集成日志框架（LogCallback）**

设置 `MapTracer.LogCallback` 后，所有 Trace 输出会同时写入该回调，可用于接入 ILogger / Serilog / NLog：

[!code-csharp[LogCallback 接入日志框架（收集器形态）](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_TraceLogCallback)]

**要点**：

- `LogCallback` 是 `static Action<string>?`，用 `volatile` 保证多线程可见性
- 回调接收完整 Trace 报告文本（一次映射一条），适合转发到结构化日志
- 回调仅在 Trace 实际启用（全局或 per-call）时触发

**Enable() 工厂方法（不修改全局状态）**

`MapTraceOptions.Enable()` 创建一份「已启用」的 Trace 选项实例，不触碰全局 `GloballyEnabled`：

[!code-csharp[按调用捕获 Trace 报告（MapTracer.Trace）](~/samples/NCMapper.Samples/06-Infrastructure/TraceScenarios.cs#Trace_PerCallOptions)]

**映射路径类型分类**：Trace 报告对每个属性标注映射方式，便于快速定位问题：

| 标记 | 含义 |
|---|---|
| `direct` | 同名同类型直接映射 |
| `nested (A→B)` | 嵌套引用类型映射 |
| `json (A↔B)` | IsFromJson 对象↔string 转换 |
| `convert (A→B)` | 类型转换（内置 / Convert） |
| `nullable→T` / `T→nullable` | 可空性转换 |
| `skipped (MapIgnore)` | 被 `[MapIgnore]` 跳过 |
| `skipped (source type ≠)` | 源类型不匹配跳过 |
| `⚠️ source field 'x' not found` | 源字段不存在（与 NCMAP001 / NCMAP002 相关） |

> 💡 `ShowValues` 默认 `false`（CWE-209 修复）：开启后标注 `[Mask]` 的属性显示为 `[MASKED]` 而非原值，防止敏感数据泄露到日志。

---

## 3. MapException 与诊断码

NCMapper 的编译期诊断码（NCMAP 全码见[诊断规则](../../diagnostics/rules.md)）和运行时异常配合，提供完整的错误诊断链：

### 编译期诊断码

| 诊断码 | 含义 | 级别 |
|---|---|---|
| NCMAP001 | 源字段未找到（`[MapProperty]` 指定的源字段在源类型中不存在） | Warning |
| NCMAP002 | `[MapProperty]` 目标属性类型与指定源字段类型不兼容（无法赋值） | Warning |
| NCMAP005 | `[MapProperty(typeof(SubSource), "field")]` 指定的跨表字段在 SubSource 中不存在 | Warning |
| NCMAP018 | 数组 `Contains` 在 net10 中不支持 | Warning |
| NCMAP099 | 源生成器内部错误 | Warning |
| NCMAP019 | 缺少 `[MapFrom]` 特性（标注映射特性但未标 `[MapFrom]`，SourceGen 不生成代码，运行时仍可用） | Info |
| NCMAP032 | 更新期 init-only 目标属性跳过（仅创建路径可写） | Info |
| NCMAP033 | `[Convert]` 反向默认不消费（按类型转换路径写入；如需反向自定义转换请标注 `[ConvertBack]`） | Info |
| NCMAP034 | `[Mask]` 属性参与反向写回（脱敏值原样写回，有损） | Info |
| NCMAP035 | `[Convert]`/`[ConvertBack]` 方法引用形态：锚定类型上不存在可访问的静态方法（DeclaredOnly 语义 + 可访问性白名单） | Error |
| NCMAP036 | `[Convert]`/`[ConvertBack]` 方法引用形态：多个重载同时命中签名模板（歧义） | Error |
| NCMAP037 | `[Convert]`/`[ConvertBack]` 方法引用形态：无任何重载命中四模板（签名不匹配） | Error |

> 完整诊断码列表见 [诊断码全索引](../../diagnostics/rules.md)。

### 运行时异常

```csharp
try
{
    var dto = Mapper.Map<SourceEntity, TargetDto>(src);
}
catch (MapException ex)
{
    // 映射过程中的异常（如类型不兼容、属性访问失败）
    Console.WriteLine($"映射失败: {ex.Message}");
}
catch (MapConfigurationException ex)
{
    // 配置错误（如 [Mask] 类型不实现 IMask）
    Console.WriteLine($"配置错误: {ex.Message}");
}
```

| 异常类型 | 基类 | 触发场景 |
|---|---|---|
| `MapException` | `InvalidOperationException` | 映射执行过程中的错误 |
| `MapConfigurationException` | `MapException` | 配置错误（如 Mask 类型不实现 IMask） |

---

### 3.1 MapException 诊断属性

`MapException` 携带三个诊断属性，便于在 `catch` 中精确定位失败位置：

[!code-csharp[MapException 诊断三字段读取](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Infra_Validation_ExceptionFields)]

| 属性 | 类型 | 含义 |
|---|---|---|
| `SourceType` | `Type?` | 映射源类型 |
| `TargetType` | `Type?` | 映射目标类型 |
| `PropertyName` | `string?` | 触发异常的属性名（可选，可能为 null） |

**异常继承关系**：

```
InvalidOperationException
    └── MapException                   （映射执行过程中的错误）
            └── MapConfigurationException  （配置错误，如 [Mask] 类型不实现 IMask）
```

**区分配置错误与运行时错误**：

```csharp
try
{
    var dto = Mapper.Map<SourceEntity, TargetDto>(src);
}
catch (MapConfigurationException ex)
{
    // 配置错误：检查 [MapFrom] / [MapProperty] / [Mask] 标注是否正确
    Console.WriteLine($"[配置错误] {ex.Message}");
}
catch (MapException ex)
{
    // 运行时错误：数据或类型转换失败
    Console.WriteLine($"[运行时错误] {ex.Message}");
}
```

> 由于 `MapConfigurationException` 继承自 `MapException`，应把 `MapConfigurationException` 的 `catch` 放在前面（更具体的异常优先）。

---

## 4. 集成到 CI 验证

### 用法

在 CI 流水线中自动验证所有映射完整性：

```csharp
// 编译后自动验证所有映射完整性
var results = Mapper.Validate(typeof(Startup).Assembly);
if (results.Any(r => r.Level == MapValidationLevel.Error))
    throw new InvalidOperationException("Mapping validation failed");

// 或直接使用 ValidateOrThrow
Mapper.ValidateOrThrow();
```

**要点**：

- CI 中调用 `Validate()` 或 `ValidateOrThrow()` 确保映射配置正确
- 重点关注 Error 级别结果（Warning 可接受但建议修复）
- 可结合 `PrintReport` 输出验证报告到 CI 日志

---

## 5. MapTracer 与 DeepCopy 的配合

启用 MapTracer 后，DeepCopy 的 Action 回调执行前会输出引用赋值风险警示：

[!code-csharp[MapTracer 开关包裹 DeepCopy+Action 引用警告](~/samples/NCMapper.Samples/06-Infrastructure/TraceScenarios.cs#Infra_Tracer_DeepCopyProbe)]

---

## 6. 映射关系可视化报告

NCMapper 源生成器可在编译期生成 Markdown 格式的映射关系报告，无需运行时依赖。

**开启方式**：在 csproj 中添加 MSBuild 属性：

```xml
<PropertyGroup>
  <NCMapperGenerateReport>true</NCMapperGenerateReport>
</PropertyGroup>
```

开启后，编译时在 `obj/Generated/` 目录生成 `NCMapperMappingReport.g.cs`，其中包含 `NCMapper.Generated.MappingReport.Report` 常量（Markdown 文本）：

```csharp
// obj/Generated/NCMapperMappingReport.g.cs（节选）
namespace NCMapper.Generated
{
    internal static class MappingReport
    {
        internal const string Report = @"# NCMapper 映射关系报告
映射对数量: 12

## UserEntity → UserDto
| 源字段 | 目标字段 | 映射方式 | 备注 |
|--------|---------|---------|------|
| Id | Id | direct | |
| Name | DisplayName | [MapProperty] rename | |
| Address | AddressCity | navigation flatten | 自动推断导航属性 |
...";
    }
}
```

**报告内容**：

- 每个 `[MapFrom]` 类型对的源→目标映射关系
- 逐字段表格：源字段、目标字段、映射方式（`direct` / `[MapProperty] rename` / `navigation flatten` / `[Convert]` / `[Mask]` 等）、备注
- 映射对总数统计

**要点**：

- 该报告仅用于审查 / 文档生成，不参与运行时映射
- 确定性构建：相同源码 + 相同编译器版本产生完全一致的输出（无时间戳），适合增量编译
- 报告中的映射方式与 Trace 报告的路径分类口径一致

---

## 常见误用

### 误用 1：生产环境长期开启 Trace

```csharp
// ❌ 误用：生产环境开启 MapTracer
MapTracer.GloballyEnabled = true;  // 性能开销 ~15-20%
// 每次映射都输出详细诊断日志，严重影响性能

// ✅ 正确：仅在调试/排查时临时开启
if (env.IsDevelopment())
{
    MapTracer.GloballyEnabled = true;
}
// 生产环境保持关闭
```

### 误用 2：忽略 Validate 的 Warning

```csharp
// ❌ 误用：只看 Error，忽略 Warning
var results = Mapper.Validate();
if (results.Any(r => r.Level == MapValidationLevel.Error))
    throw new InvalidOperationException("Mapping failed");
// Warning 被忽略 → [MapProperty] 源类型不一致等问题未修复

// ✅ 正确：Warning 也应修复
var hasIssues = results.Any(r => r.Level >= MapValidationLevel.Warning);
if (hasIssues)
{
    MapValidator.PrintReport(results, Console.Out, showOk: false);
    // 人工审查后决定是否修复
}
```

### 误用 3：用 Trace 代替性能分析

```csharp
// ❌ 误用：用 MapTracer 分析性能
MapTracer.GloballyEnabled = true;
var sw = Stopwatch.StartNew();
var dto = Mapper.Map<UserEntity, UserDto>(entity);
sw.Stop();
// Trace 本身有 ~15-20% 开销，测量数据失真

// ✅ 正确：性能分析时关闭 Trace，用 BenchmarkDotNet 等专业工具
MapTracer.GloballyEnabled = false;
// 使用 BenchmarkDotNet 或 Stopwatch（预热后）测量
```

---

## 小结

| 能力 | API | 特性 |
|---|---|---|
| 全量验证 | `Mapper.Validate()` | 扫描所有 [MapFrom] 类型对 |
| 范围验证 | `Mapper.Validate(Assembly[])` | 限定扫描范围 |
| 单类型对验证 | `MapValidator.ValidateMapping(src, tgt)` | 检查特定类型对 |
| 验证并抛异常 | `Mapper.ValidateOrThrow()` | 有 Error 时抛异常 |
| 验证报告 | `MapValidator.PrintReport(results, writer, showOk)` | 格式化输出 |
| 全局 Trace | `MapTracer.GloballyEnabled` | 开关，~15-20% 开销 |
| 文件 Trace | `MapTraceOptions.Enable(TextWriter)` + `MapTracer.Trace` | 重定向到文件 |
| 日志框架集成 | `MapTracer.LogCallback` | 接入 ILogger / Serilog / NLog |
| 分析式 Trace | `MapTracer.Trace<T,S>(src, opts)` | 不执行映射，返回报告 |
| 异常类型 | `MapException` / `MapConfigurationException` | 运行时诊断 |
| 异常诊断属性 | `ex.SourceType` / `ex.TargetType` / `ex.PropertyName` | catch 中定位失败位置 |
| 映射关系报告 | `<NCMapperGenerateReport>true</NCMapperGenerateReport>` | 编译期生成 Markdown |

---

## 配套示例

```bash
cd samples/NCMapper.Samples
dotnet run --scenario 602
```

核心示例源码（单点同源，`dotnet run --scenario` 可运行验证）：

[!code-csharp[全量验证](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios)]

[!code-csharp[全量验证 · ValidateAll](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_ValidateAll)]

[!code-csharp[全量验证 · ValidateMapping](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_ValidateMapping)]

[!code-csharp[全量验证 · ValidateOrThrowDemo](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_ValidateOrThrowDemo)]

[!code-csharp[全量验证 · ValidateAssembly](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_ValidateAssembly)]

[!code-csharp[全量验证 · MapNullSource](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_MapNullSource)]

[!code-csharp[全量验证 · TraceMapping](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_TraceMapping)]

[!code-csharp[全量验证 · TraceToWriter](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_TraceToWriter)]

[!code-csharp[全量验证 · TraceLogCallback](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_TraceLogCallback)]

[!code-csharp[全量验证 · TraceOptionsFactory](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_TraceOptionsFactory)]

[!code-csharp[全量验证 · ToggleValidationSwitch](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_ToggleValidationSwitch)]

[!code-csharp[全量验证 · PrintReportToWriters](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_PrintReportToWriters)]

[!code-csharp[全量验证 · DiagnoseError](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_DiagnoseError)]

[!code-csharp[全量验证 · Run](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_Run)]

代码见 `samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs`，涵盖 Validate/ValidateMapping/ValidateOrThrow/TraceToWriter/TraceLogCallback 等子场景。

---

## 进一步阅读

- 相关篇目：[DI 集成](di.md) · [Pipeline](pipeline.md) · [诊断码全索引](../../diagnostics/rules.md)
- 技术文档深挖：[映射验证](../../explanation/validation.md) §12
- 验证源码：`src/NCMapper/MapValidator.cs` · `src/NCMapper/MapTracer.cs`
