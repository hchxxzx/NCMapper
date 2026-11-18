<!-- 来源：NCMapper 主技术文档 §19 测试体系 -->
## 19. 测试体系

NCMapper 采用分层、多 TFM 的测试策略，全量测试用例（含参数化展开用例，三 TFM net8/9/10）覆盖核心库、源生成器、ORM 集成、模糊测试与 AOT 编译：

| 测试项目                      | 主题        | 说明                                                                                                          |
| ------------------------- | --------- | ----------------------------------------------------------------------------------------------------------- |
| NCMapper.Tests            | 核心库单元测试   | 深拷贝 / 集合 / 组合 / 管道 / 验证 / 脱敏 / 类型转换 / 基础映射 / 枚举 / Nullable / 配置 DI / 表达式树 / 边界异常等，含 Stryker 变异测试（变异分数 100%） |
| NCMapper.SourceGen.Tests  | 源生成器测试    | 生成正确性、诊断码触发（NCMAP001–034[缺004] / 099）、Mask 生成、循环引用、扁平化深度、回归                                                       |
| NCMapper.IntegrationTests | ORM 集成测试  | EFCore / FreeSql / Dapper 投影、Mask+投影降级、Convert+投影                                                      |
| NCMapper.FuzzTests        | 模糊测试      | 属性级随机输入                                                                                                     |
| NCMapper.AotTests         | AOT 编译兼容性 | NativeAOT 部署验证                                                                                              |

变异测试（Stryker）用于度量测试对源码突变的捕获率，是防御"测试通过但逻辑错误"的关键手段。

### 19.1 双通道等价性测试（Equivalence Tests）

NCMapper 的 `[MapFrom]` 特性通道与 Fluent 通道（`[MapperConfig]` + `Configure(FluentMappingConfig)`）长期并存，两通道产物均物化为 `MappingSnapshot`。**等价性测试机制**守护两通道产物同构：特性通道物化逻辑变更若未同步 Fluent 通道，等价对测试将在 CI 阶段失败。

**何时必须附等价对**：PR 变更触碰以下物化面文件（前缀匹配）且语义可能影响任一通道的 `MappingSnapshot` 字段时，须同步新增或更新 `test/NCMapper.SourceGen.Tests/Equivalence/` 下的等价对 fixture（CI `equivalence-gate` job 强制核查）：

- `src/NCMapper.SourceGen/MappingExtractor*.cs`
- `src/NCMapper.SourceGen/FluentExtractor*.cs`
- `src/NCMapper.SourceGen/ChainCallParser.cs`
- `src/NCMapper.SourceGen/Models.Snapshots.cs`
- `src/NCMapper.SourceGen/MappingSnapshotBuilder*.cs`

**如何写等价对 fixture**（参照 `Equivalence/EquivalenceTests.*.cs` 既有 8 对）：

1. **双源约定**：同一映射语义两通道各写一遍——特性侧 S/T 类带标注，Fluent 侧 S/T 干净类 + `[MapperConfig]` 类，两侧类名/命名空间/属性集一致
2. **双层断言**：继承 `EquivalenceFixtureBase`，调用 `AssertEquivalent(attrSource, fluentSource, pairId, mapMethodName)`——L1 字段级（`SnapshotEquivalenceComparer` 归一比较，差异清单为空）+ L2 文本层（生成 `Map_{S}_To_{T}` 方法体逐字节一致）；L2 不适用时（如嵌套递归调用形态差异）降级 `AssertL1Only` 并登记原因
3. **标记与登记**：测试方法标 `[EqPair("A-xx")]` + `[Trait("EqPair", "A-xx")]`（支持 `--filter-trait "EqPair=A-xx"` 单对运行），并在 `EquivalenceRegistry` 登记 A 类条目（`EquivalenceRegistryIntegrityTests` 核查 1/2 双向校验登记完整性）
4. **能力分类**：仅双通道共有的能力建等价对（A 类 8 项）；单通道专属能力（AfterMapping/NoAutoInclude 等 B/C 类）不入等价断言，仅在 registry 登记通道支持性

**逃生门**：确属无误伤的物化面变更（如纯诊断措辞调整），可在 PR 打 `skip-equivalence-gate` label 跳过 gate（job 级跳过），**须在 PR 描述登记理由**供评审确认。

**本地验证**：`powershell -File scripts/verify-equivalence-gate.ps1 -BaseRef origin/main`（与 CI 同参）。

### 19.2 快照字段注册表（SnapshotFieldRegistry）维护

 引入 `SnapshotFieldRegistry`（`test/NCMapper.SourceGen.Tests/Equivalence/SnapshotFieldRegistry.cs`）作为等价比较器剥离集的唯一数据源，替代散落的 `TopLevelDenyList` 字符串数组 + `Get*Coverage` 硬编码数组 + `Compare*` 方法体内联 STRIPPED 注释。

**三列登记规则**（`FieldRegistration` record）：

| 列 | 含义 |
|----|------|
| `Field` | 字段名（与反射 `PropertyInfo.Name` 逐字一致） |
| `Role` | `Compared`（参与比较）/ `Stripped`（剥离，不参与比较） |
| `Reason` | 剥离/比较理由（符号引用，不带行号） |
| `MigrationCondition` | 迁出条件（Stripped 项的"何时可比"；为空须显式"长期/暂无"） |

**登记口径**：
- 顶层 `MappingSnapshot` 仅登记 Stripped 4 项（`TargetIdentifierLocation` / `NoAutoInclude` / `AfterMappingMethodRef` / `IsFluentForward`）；Compared = 反射全集 − Stripped（运行时免手列）。
- 6 嵌套手写投影类型（`PropertyRef` / `PropertyMappingSnapshot` / `DiagnosticSnapshot` / `DeepCopyNodeShape` / `DeepCopyPropertyShape` / `ComposeMappingSnapshot`）逐字段全列（Compared 按 `Compare*` 实际比较字段，Stripped 按注释组）。
- ConverterBack quartet（`HasConvertBack` 等 4 项）Stripped 须含落差声明（E-1 不达成迁出条件）。

**冻结断言消费**（`SnapshotFieldRegistryTests`）：
- `AllTypes_ComparedUnionStripped_EqualsReflectionFullSet`：每类型 `Compared ∪ Stripped = 反射全集`——新增字段未登记即红。
- `AllStrippedEntries_HaveNonEmptyReasonAndMigrationCondition`：三列完整性。
- `ContainerElementTypes_AreSubsetOfRegistryTypes`：类型级结构断言（容器元素类型集 ⊆ 注册表类型集，双向闭合）。

**新增字段/嵌套类型登记流程**：
1. 在 `Models.Snapshots.cs` 新增字段后，立即在 `SnapshotFieldRegistry` 对应类型的 `IReadOnlyList<FieldRegistration>` 登记（Compared 或 Stripped + 理由 + 迁出条件）。
2. 新增嵌套容器元素类型（`List<T>` 元素）须在注册表建对应类型登记，否则 `ContainerElementTypes_AreSubsetOfRegistryTypes` 红。
3. 跑 `SnapshotFieldRegistryTests` 全绿确认登记完整。

**变异验证**：注册表层/结构层变异（删字段/理由清空/删嵌套类型）须使对应冻结断言红——预测失败集 = 实测失败集（无假绿，反向验证）。


