---
title: 核心 API
---

<!-- 来源：NCMapper 主技术文档 §4 核心 API（结构重组，2026-08-19 章级迁移，内容未重排） -->
## 4. 核心 API

### 4.1 映射入口

| 方法                              | 签名                                                       | 说明                           |
| ------------------------------- | -------------------------------------------------------- | ---------------------------- |
| `Map<TSource>`                  | `(TSource, Action?) → TSource?`                          | 同类型拷贝                        |
| `Map<TSource,TTarget>`          | `(TSource, Action?) → TTarget?`                          | 跨类型映射                        |
| `Map<TSource,TTarget>`          | `(TSource, TTarget, Action?)`                            | 更新已有对象                       |
| `MapList<TTarget>`              | `(IEnumerable, Action?) → List?`                         | 同类型列表映射                      |
| `MapList<TSource,TTarget>`      | `(IEnumerable<TSource>, Action?) → List<TTarget>?`       | 跨类型列表映射                      |
| `Compose<TTarget>`              | `(params object[]) → TTarget?`                           | 多源合成                         |
| `Compose<TTarget>`              | `(Action<TTarget>?, params object[]) → TTarget?`         | 多源合成 + 回调                    |
| `ComposeList<TTarget>`          | `(params object[][]) → List<TTarget>?`                   | 批量多源合成                       |
| `ComposeList<TTarget>`          | `(Action<TTarget>?, params object[][]) → List<TTarget>?` | 批量合成 + 回调                    |
| `DeepCopy<T>`                   | `(T) → T?`                                               | 同类全深拷贝                       |
| `DeepCopy<T>`                   | `(T, Action<T,T>?) → T?`                                 | 同类深拷贝 + 回调                   |
| `DeepCopy<TSource,TTarget>`     | `(TSource) → TTarget?`                                   | 异类深拷贝（转发 Map）                |
| `DeepCopy<TSource,TTarget>`     | `(TSource, Action<S,T>?) → TTarget?`                     | 异类深拷贝 + 回调                   |
| `Validate()`                    | `() → List<MapValidationResult>`                         | 验证所有映射对                      |
| `ValidateOrThrow()`             | `() → void`                                              | 验证不通过抛异常                     |
| `HasGenerated<TSource,TTarget>` | `() → bool`                                              | 检查源生成器是否已注册                  |
| `HasGeneratedDeepCopy<T>`       | `() → bool`                                              | 检查深拷贝是否已注册                   |
| `ClearGenerated()`              | `()`                                                     | 清除所有源生成器委托                   |
| `ClearCache()`                  | `()`                                                     | 清除所有运行时缓存                    |
| `ResetMetrics()`                | `()`                                                     | 重置性能指标                       |
| `src.MapToTarget()`             | 扩展方法                                                     | 源生成器生成，与 Mapper.Map 走同一份映射代码 |

> **注意**：`Register*` / `Clear*` / `Has*` 方法标注了 `[EditorBrowsable(Never)]`，IntelliSense 中不可见，供源生成器内部调用。

### 4.2 源生成器注册方法

源生成器在编译时自动调用以下方法注册委托，用户无需手动调用：

| 方法                                   | 说明                   |
| ------------------------------------ | -------------------- |
| `RegisterGeneratedCore<TS,TT>`       | 注册核心映射委托（无Action热路径） |
| `RegisterGenerated<TS,TT>`           | 注册带 Action 映射委托      |
| `RegisterGeneratedList<TS,TT>`       | 注册列表映射委托             |
| `RegisterGeneratedUpdate<TS,TT>`     | 注册更新已有对象委托           |
| `RegisterGeneratedDeepCopy<T>`       | 注册深拷贝委托              |
| `RegisterGeneratedCompose<TT>`       | 注册多源合成委托             |
| `RegisterGeneratedValidate`          | 注册编译时验证委托            |
| `RegisterGeneratedConfig`            | 注册源生成器生成的全局映射配置（按 assembly 注入 `[MapDefaults]` 等） |

### 4.3 运行时控制

| 属性                  | 类型         |     默认值     | 说明                                   |
| ------------------- | ---------- | :---------: | ------------------------------------ |
| `ValidationEnabled` | bool       |     true    | 映射验证开关                               |
| `TraceEnabled`      | bool       |    false    | 诊断 Trace 开关                          |
| `TraceOutput`       | TextWriter | Console.Out | Trace 输出目标                           |
| `MetricsEnabled`    | bool       |    false    | 性能指标开关（开启后 Interlocked 计数）                 |
| `MaxRecursionDepth` | int        |      64     | 最大递归深度（防循环引用栈溢出）                     |
| `MaxInlineDepth`    | int        |      5      | 嵌套对象表达式树内联最大深度                       |

> **相关 API**：`MaxRecursionDepthUpperBound`（= 4096）与 `MaxInlineDepthUpperBound`（= 16）——递归/内联深度设置的取值上限常量（`public const int`；`Mapper.MaxRecursionDepth` / `Mapper.MaxInlineDepth` 设值超上限即被钳制到上限）（源码锚：Mapper.cs:847/:879）。
>
> **相关 API**：`TypeConvertHelper`——通用类型转换辅助类（引擎内部调用，覆盖 string↔基元、值类型互转、枚举互转、可空类型互转等；公开入口 `ConvertValue<TSource, TTarget>`，自定义转换器实现可参照）（源码锚：TypeConvertHelper.cs:7/:23）。

### 4.4 性能指标

```csharp
Mapper.MetricsEnabled = true;

// 执行映射操作...

Console.WriteLine($"总调用: {Mapper.TotalMapCalls}");        // Interlocked 原子递增
Console.WriteLine($"缓存命中: {Mapper.CacheHits}");           // 源生成器路径命中
Console.WriteLine($"运行时回退: {Mapper.CompilationCount}");  // 生成缓存未命中回退运行时引擎的调用次数（仅首次真正编译）

Mapper.ResetMetrics();  // 重置
```

> `MetricsEnabled = false`（默认）避免 Interlocked 开销，建议仅诊断时开启。

### 4.4.1 缓存与生成层管理

| 方法 | 说明 |
| --- | --- |
| `ClearCache()` | 清除全部运行时缓存（登记表驱动：`CacheRegistry.ClearFull` 级联 17 项）。注意：重冻结仅对尚未编译的类型对生效，测试隔离同类型对须改用未预热场景或进程级隔离 |
| `ClearGenerated()` | 仅清 Generated 层登记条目（generated-* 4 项）。泛型静态字段（`GeneratedCache<T,S>`）无法运行时统一清除，需经 `RegisterGenerated<TS,TT>(null)` 传 null 逐个覆盖 |
| `HasGenerated<TS,TT>()` | 查询指定类型对是否有源生成器注册的映射委托（检查字典缓存，与 `ClearGenerated` 口径一致）。返回 true=已注册 / false=无注册 |

用法（测试隔离典型形态）：

[!code-csharp[生成层探测 HasGenerated 与两级缓存清理](~/samples/NCMapper.Samples/06-Infrastructure/PerformanceScenarios.cs#Core_Api_GeneratedProbe)]

### 4.5 AOT 兼容性标注

| API        | 标注                              | 说明               |
| :--------- | :------------------------------ | :--------------- |
| 源生成器路径     | —（编译时生成代码）  | AOT 友好，编译时生成全部代码（零反射，不依赖运行时类型元数据） |
| `Compose`  | `[RequiresDynamicCode]` + `[RequiresUnreferencedCode]` | 注册源生成 Compose 委托（`RegisterGeneratedCompose`）时 AOT 可用；未注册回退运行时表达式树路径（AOT 下不可用） |
| `Validate` | `Validate()` 无参版：`[Experimental("NCMapper001")]`（API 稳定性） | `Validate()` AOT 可用（源生成验证结果，零反射）；`Validate(params Assembly[])` 标注 `[RequiresDynamicCode]` + `[RequiresUnreferencedCode]`（反射扫描程序集，AOT 下不可用） |

> 生产环境推荐全部 `[MapFrom]` 标注走源生成器路径以获得 AOT 兼容性。

### 4.6 映射管道

可插拔的后处理（PostMap）回调，管道为空时 `volatile bool` 检查零开销。

```csharp
Mapper.Pipeline
    .AddPostMap<UserEntity, UserDto>((src, dto) =>
    {
        dto.FormattedDate = src.CreatedAt.ToString("yyyy-MM-dd");
        dto.CreatedBy = "system";  // 审计字段在 PostMap 内直接设置
    });

var dto = Mapper.Map<UserEntity, UserDto>(user);
// 执行顺序：映射核心 → Action → PostMap（按注册逆序）

// 注：本片段为演示用途，末尾 Clear 仅作收尾。
// 业务代码中 PostMap 是进程级全局注册，通常「注册一次、长期保留」，不要每次调用后 Clear；
// 若只需单次定制变换，请改用 Mapper.Map(src, action) 的 Action 回调（随调用自动清理）。
// Mapper.Pipeline.Clear();  // 仅测试 / 受控作用域的 teardown 才需要
```

> PostMap 按 `(TSource, TTarget)` 类型过滤，只对匹配类型对生效。
>
> PostMap 回调在首次执行时构建逆序数组并缓存，后续直接遍历，无每次 `ToArray()` 分配。

**典型应用场景** — 映射管道适合处理跨多个映射对的横切关注点，避免在每个 `Action` 回调里重复编写：

| 场景    | 用法                                                         |
| ----- | ---------------------------------------------------------- |
| 审计字段  | PostMap 填充 `CreatedBy`/`UpdatedAt`                      |
| 权限脱敏  | PostMap 按角色掩码手机号、身份证等敏感字段                    |
| 数据格式化 | PostMap 将 `DateTime` 格式化为字符串、金额转为货币显示格式                    |
| 数据增强  | PostMap 补充 DTO 中源实体没有的关联字段                |
| 耗时监控  | PostMap 输出单次映射耗时                            |

[!code-csharp[PostMap 审计字段 + 权限脱敏组合](~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs#Infra_Api_AuditMaskCombo)]

> 管道为空时 `volatile bool` 检查零开销，不影响未使用管道的映射性能。仅在需要横切处理时启用。

### 4.7 异常体系

NCMapper 异常分两层，均位于 `NCMapper` 命名空间：

| 异常类型                        | 基类                          | 触发场景                                                                                                                          |
| --------------------------- | --------------------------- | ----------------------------------------------------------------------------------------------------------------------------- |
| `MapException`              | `InvalidOperationException` | 运行时映射错误（循环引用超 `MaxRecursionDepth`、深拷贝全策略失败、类型转换失败、`[Convert]` 方法引用解析失败——静态方法不存在 / 不可访问 / 签名不匹配 T0–T3 模板等，对应编译期 NCMAP035/036/037 的运行时镜像） |
| `MapConfigurationException` | `MapException`              | 配置错误（DTO 未标注 `[MapFrom]`、Compose 找不到主源类型、`[MapProperty]` 字段不存在等）                                                              |

> **`MapException` 与序列化**：`MapException` 不实现 `ISerializable`（无序列化构造器），跨 AppDomain/远程场景不会自动重建异常对象。

> **运行时异常统一包装**：运行时表达式树路径的底层异常（反射/转换异常等）统一包装为 `MapException`（保留 `InnerException`），与源生成器路径异常类型一致，调用方 `catch` 不会漏捕。

诊断属性（`MapException` 暴露，便于定位）：

| 属性             | 类型        | 说明       |
| -------------- | --------- | -------- |
| `SourceType`   | `Type?`   | 映射源类型    |
| `TargetType`   | `Type?`   | 映射目标类型   |
| `PropertyName` | `string?` | 触发异常的属性名 |

> `MapException` 继承 `InvalidOperationException`，已有 `catch (InvalidOperationException)` 仍能捕获；需区分配置错误与运行时错误时单独 `catch (MapConfigurationException)`。

```csharp
try
{
    var dto = Mapper.Map<UserEntity, UserDto>(user);
}
catch (MapConfigurationException ex)  // 配置错误（如 DTO 未标注 [MapFrom]）
{
    Console.WriteLine($"配置错误: {ex.Message} [{ex.SourceType?.Name} → {ex.TargetType?.Name}]");
}
catch (MapException ex)               // 运行时映射错误
{
    Console.WriteLine($"映射失败: {ex.Message} 属性={ex.PropertyName}");
}
```

> **双语错误消息**：所有异常与诊断消息文本由 `ErrorMessages.cs` 资源提供中英双语，按当前线程 `CultureInfo` 自动择取对应语言，无需用户配置。

---

