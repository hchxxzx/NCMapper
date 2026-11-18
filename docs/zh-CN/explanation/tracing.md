<!-- 来源：NCMapper 主技术文档 §13 映射诊断 Trace -->
## 13. 映射诊断 Trace

映射过程对开发者是黑盒：某属性为何没映射、走的是直接赋值还是类型转换、嵌套对象如何处理——这些决策默认不可见。开启 `TraceEnabled` 后，每次映射输出每个属性的映射方式（direct / nested / json / convert / ignored），将黑盒变为可观测过程，快速定位映射结果不符合预期的根因。

[!code-csharp[全局启用映射追踪（开关 + 回调通道）](~/samples/NCMapper.Samples/06-Infrastructure/TraceScenarios.cs#Trace_GlobalEnabled)]

**典型应用场景** —

| 场景         | 说明                                         |
| ---------- | ------------------------------------------ |
| 排查字段为 null | 某属性映射后为 null，查看 Trace 确认是 ignored 还是源属性不存在 |
| 定位类型转换路径   | 确认某字段走 direct 还是 convert，排查转换结果异常          |
| 验证忽略配置     | 确认 `[MapIgnore]` 是否生效，被忽略的属性显示 ignored     |
| 嵌套映射分析     | 查看嵌套对象走 nested 还是 json，排查深拷贝/嵌套问题          |
| 日志归档       | 输出到文件，事后分析生产环境的映射行为                        |

[!code-csharp[Trace 输出重定向文件事后分析](~/samples/NCMapper.Samples/06-Infrastructure/TraceScenarios.cs#Trace_OutputToFile)]

#### 13.1 AsDto 投影诊断

开启 MapTracer 后，AsDto 投影的决策链路完整可观测——正常投影、降级触发、属性跳过、点路径匹配、扁平化匹配均有 Trace 输出。

[!code-csharp[追踪开关简形](~/samples/NCMapper.Samples/06-Infrastructure/TraceScenarios.cs#Trace_GlobalEnabled)]

**典型诊断场景**：

| 场景                 | Trace 输出示例                                                                          |
| ------------------ | ----------------------------------------------------------------------------------- |
| 降级触发               | `[EFCore AsDto] 降级: OrderEntity → OrderDto, 原因: 属性 Amount 标注了 Convert（需要降级）`   |
| 点路径匹配              | `[EFCore Projection] 点路径 'Address.City' → Address.City 匹配成功`                        |
| 点路径失败              | `[EFCore Projection] 点路径 'A.B' 在分段 'B' 处失败（类型 X 中未找到属性）`                            |
| 集合投影跳过             | `[EFCore Projection] 跳过集合投影 Items→OrderItemDto（原因: 深度 9 超过上限 8）`                    |
| FreeSql 降级         | `[FreeSql AsDto] 降级: OrderEntity → OrderDto, 原因: 集合导航投影，FreeSql 不支持集合子查询翻译`         |
| FreeSql 自动 Include | `[FreeSql AsDto] 自动 Include: Order→Customer, Order→Items`                           |
| 属性跳过               | `[EFCore Projection] 跳过目标属性 Phone（原因: 源类型 UserEntity 中未找到源字段 Phone，且点路径/扁平化均无匹配路径）` |

> **注意**：MapTracer 输出包含类型名和属性名，生产环境建议仅在诊断时开启。

#### 13.2 Enable() 工厂与链式配置

除全局 `Mapper.TraceEnabled` / `Mapper.TraceOutput` 方式外，`MapTraceOptions` 提供 `Enable()` 工厂方法创建按调用配置的 TraceOptions 实例，适合单次映射或测试场景：

链式启用（按调用配置）— Mapper.Map 无 options 重载；按调用 Trace 分析通过 `MapTracer.Trace` 提供（生成映射关系报告，不执行映射），可运行版本：

[!code-csharp[按调用 Trace 报告捕获（内存包含检查）](~/samples/NCMapper.Samples/06-Infrastructure/TraceScenarios.cs#Trace_PerCallOptions)]

输出到 StringWriter（测试场景）——TextWriter 捕获形态的可运行版本：

[!code-csharp[StringWriter 捕获 Trace 输出](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_TraceToWriter)]

`MapTraceOptions` 三个配置项均为可选，未设置时回退到全局配置：
- `Enabled` — 是否启用 Trace 输出（默认 `false`）
- `Output` — 输出目标 `TextWriter`（默认 `Console.Out`）
- `ShowValues` — 是否输出属性值（默认 `false`，开启后 [Mask] 属性显示 `[MASKED]` 而非原值）

> **相关 API**：`MapTracer.GloballyEnabled`——全局 Trace 开关（`public static bool`，默认 `false`；开启后所有映射操作输出 Trace，除非按调用覆盖）；与上述按调用配置的 `MapTraceOptions.Enable()` 成对：全局场景用前者，单次映射/测试场景用后者（源码锚：MapTracer.cs:64）。

#### 13.3 LogCallback 集成日志框架

`MapTracer.LogCallback` 是独立于 `GlobalOutput` 的日志回调通道，接受 `Action<string>` 委托。设置后 Trace 输出会同时写入 `GlobalOutput` 和回调——适合集成 ILogger、Serilog、NLog 等日志框架：

[!code-csharp[LogCallback 日志集成（收集器形态，用后恢复）](~/samples/NCMapper.Samples/06-Infrastructure/TraceScenarios.cs#Trace_GlobalEnabled)]

> **输出通道行为**：`GlobalOutput`（默认 `Console.Out`）和 `LogCallback` 是两个独立通道——Trace 内容会同时写入两者。`GlobalOutput` 可通过 `Mapper.TraceOutput` 全局替换或 `MapTraceOptions.Output` 按调用覆盖；`LogCallback` 仅全局设置（`MapTracer.LogCallback`）。

## 可运行演示（场景 607）

[!code-csharp[全局 TraceEnabled 开关全流程（含恢复）](~/samples/NCMapper.Samples/06-Infrastructure/TraceScenarios.cs#Trace_GlobalEnabled)]

[!code-csharp[按调用追踪报告（四类映射方式标记全覆盖断言）](~/samples/NCMapper.Samples/06-Infrastructure/TraceScenarios.cs#Trace_PerCallOptions)]

---

