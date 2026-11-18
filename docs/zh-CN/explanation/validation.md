<!-- 来源：NCMapper 主技术文档 §12 映射验证 -->
## 12. 映射验证

映射规则声明后，属性名不匹配、类型不兼容、漏标注等问题往往要等到运行时才暴露。`Mapper.Validate()` 在运行时扫描所有 `[MapFrom]` 映射对，逐个属性检查是否可正确映射，输出 OK / Warn / Error 报告——让开发者主动发现配置错误，而非等待运行时失败。验证结果聚合两部分：运行时反射扫描 + 源生成器编译时验证委托（由 `RegisterGeneratedValidate` 注册，覆盖 NCMAP001–034[缺004] 全部诊断），二者合并去重后统一出具报告。

### 12.1 运行时验证

[!code-csharp[全量扫描 MapFrom 标注类型对 + 单类型验证 + 打印报告](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_ValidateAll)]

**验证消息语言**：报表行/汇总前缀/明细项/`PrintReport` 报表头与总计行/降级原因行前缀随 `CurrentUICulture` 切换（zh-CN 默认中文，en-US 为英文形态如 `⚠️ Src → Dst: 3/5 properties mapped, 1 unmapped: ...`）；源生成器写进生成代码的验证消息固定英文（编译期固化，不随运行时文化——zh 环境下可能出现"外层中文 + 生成对内层英文"混排，设计使然）。

### 12.2 验证级别

| 级别    | 含义                   |
| ----- | -------------------- |
| OK    | 所有属性可正确映射            |
| Warn  | 部分属性未映射或类型不匹配        |
| Error | 未标注 [MapFrom] 或无可用映射 |

**典型应用场景** —

| 场景       | 说明                                                 |
| -------- | -------------------------------------------------- |
| 启动时校验    | `Program.cs` 中调用 `ValidateOrThrow()`，配置错误时应用直接启动失败 |
| CI 流水线集成 | 测试中调用 `Validate()`，映射配置错误导致 CI 失败                  |
| 重构后验证    | 改 DTO/Entity 属性后运行验证，确认映射仍正确                       |
| 上线前检查    | 部署前运行 `PrintReport` 检查所有 Warn/Error                |

```csharp
// 启动时校验 — 配置错误直接阻止启动
builder.Services.AddNCMapper();
Mapper.ValidateOrThrow();  // 任何 Error 级别问题抛异常，应用启动失败

// CI 中输出完整报告
var results = Mapper.Validate();
MapValidator.PrintReport(results, showOk: true);
```

---

