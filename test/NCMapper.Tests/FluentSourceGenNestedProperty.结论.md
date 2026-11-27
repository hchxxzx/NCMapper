P1 结论：Properties 含嵌套=true，落点=运行时

# P1 源生成器产物核验结论（TB-20261127-01 §4 卡 P1）

| 字段 | 内容 |
|---|---|
| 任务书 | TB-20261127-01 —— FreeSql Compose 嵌套投影缺陷修复（路线四） |
| 卡号 | P1（执行前 POC） |
| 工作区 | `D:\OpenCode\NCMapper`（开源库，HEAD `995b7cb`，任务书基准 `1ee65bf` 之上仅 benchmarks 变更） |
| 执行人 | dev-1 |
| 结论日期 | 2026-11-27 |
| 用例文件 | `test/NCMapper.Tests/FluentSourceGenNestedPropertyTests.cs` |
| 验证命令 | `dotnet test test/NCMapper.Tests -- --filter-class "*FluentSourceGenNestedProperty*"` |

---

## 1. 一句话结论

**真实 `cfg.For<TSource,TTarget>()` 经源生成器编译后，`RuntimeMapperMetadata.Properties` 已经包含嵌套属性条目。**
因此卡 C2 的修法落点是**运行时**（给 `ComposeProjectionKernel` 的属性级 miss 分支补嵌套递归例外），
**不是注册侧**（注册侧无需补发射逻辑——嵌套条目本来就发射了）。

> 注意区分两个不同的问题：
> - **「嵌套条目在不在 Properties 里」** —— 在（本卡已证，`true`）。
> - **「Compose 腿在什么情况下丢掉这个条目」** —— 当目标属性落到「属性级 miss」分支时，
>   单表链保留递归入口而 Compose 腿直接 `continue`，这是缺陷④的真实成因（见 §4）。

---

## 2. 观测证据（实跑读数）

### 2.1 Fluent 通道主对 `cfg.For<Site, SiteNestedDto>()`

声明只显式写了标量成员 `.Member(d => d.Name).From(s => s.Name)`，**嵌套属性 `Dept` 未显式声明**。

产物条目（`FindByPair(typeof(Site), typeof(SiteNestedDto))`）：

```
channel = Fluent, IsFluentForward = true
Properties（3 条，目标属性名）：Id, Name, Dept
  - Id    : SourceName=Id   SourcePropertyTypeName=int     Target.TypeName=int
  - Name  : SourceName=Name SourcePropertyTypeName=string  Target.TypeName=string
  - Dept  : SourceName=Dept SourcePropertyTypeName=<...>.Dept  Target.TypeName=<...>.DeptDto
AllTargetSlots（3 条）：Id, Name, Dept
```

关键点：`Dept` 这条 `RuntimePropertyMapping` 的
- `Target.TypeName` = `DeptDto`（目标侧嵌套 DTO），
- `SourcePropertyTypeName` = `Dept`（源侧实体类型）。

即这是一条**真正的嵌套属性条目**，由自动匹配（同名同形）建立，不是空占位。

嵌套对自身也独立成对注册：`FindByPair(typeof(Dept), typeof(DeptDto))` 命中，`Properties` 含 `Id, Label`。

### 2.2 特性通道同形状 DTO（`[MapFrom(typeof(AttrSite))]`）

```
channel = MapFrom, IsFluentForward = false
Properties（2 条）：Id, Dept
```

两个通道形态一致——特性通道同样已含嵌套条目。这解释了 POC N 组「特性 vs Fluent 同形状对照」为何同口径。

### 2.3 U 组（`cfg.For` 声明后可读到 Properties）

`FindByPair(typeof(UCfgForSource), typeof(UCfgForDto))` 命中，`Properties` = `Id, Title`（2 条）。

源生成器确在运行——编译产物落盘于
`test/NCMapper.Tests/obj/Generated/NCMapper.SourceGen/NCMapper.SourceGen.RuntimeSnapshotGenerator/NCMapper_RuntimeSnapshotMetadata.g.cs`
（该文件 2 288 430 字节，含 313 条注册条目，其中 53 条为 Fluent 通道）。

---

## 3. 为什么落点是「运行时」而不是「注册侧」

本卡对三种形态做了对照实跑（探针见 §6，已删除不入库）：

| 形态 | Properties 是否含嵌套 | 单表链产出 | Compose 腿产出 |
|---|---|---|---|
| 默认（自动匹配 on），嵌套同名同形 | **含**（`Dept`） | `Dept = IIF(t1.Dept==null, null, new ADeptDto{...})` | 同左（一致） |
| `NoAutoMatch()`，嵌套完全不声明 | 不含（`Properties` 仅 1 条） | 无 `Dept` 绑定（`AllTargetSlots` 也只剩 1 条） | 无 `Dept` 绑定（一致） |
| 目标属性在 `AllTargetSlots` 但**不在** `Properties`（源侧异名，如 `Department`→`Dept`） | 不含 | **保留递归入口**，尝试构建嵌套后失败并 Trace | **直接跳过**，不进递归 |

只有第三种形态出现「单表与 Compose 结果不同」，而这正是任务书缺陷④的描述。

对应的源码落点（已核对，与任务书 §3.2 一致）：

- 单表链 `src/NCMapper/Internal/ProjectionExpressionFactory.cs:1199-1214`：
  ```csharp
  // ⑤ 属性级 miss（矩阵——跳过 + Trace，提取期权威）。
  // 嵌套/集合 DTO 属性除外（递归入口保留——与顶层快照腿 ⑤ 同口径；跳过即嵌套投影断链）。
  if (Runtime.RuntimeSnapshotStore.IsRegisteredDto(tgtProp.PropertyType)
      || ContainsRegisteredDtoElement(tgtProp.PropertyType))
  {
      var nestedValueExpr = BuildProjectionValueExpression(...);
      ...
      nestedBindings.Add(Expression.Bind(tgtProp, nestedValueExpr));
      continue;
  }
  ```
- Compose 腿 `src/NCMapper.FreeSql/Internal/ComposeProjectionKernel.cs:300-308`：
  ```csharp
  // ── ③ 属性级 miss（矩阵——跳过 + Trace，提取期权威，不调特性读取、不按名重试）──
  ... Trace ...
  continue;   // ← 没有嵌套/集合 DTO 例外
  ```

两边对同一份元数据的行为差就差在这一个分支的判断上。**给 Compose 腿补同样的递归例外即可对齐**，
不需要动 `SnapshotMetadataEmitter`（嵌套条目已经在发射）。

> 结论对 C2 的直接含义：C2 不得采用「注册侧补 Properties 条目」作为方案（任务书已写死这条边界），
> 本卡实测也从数据上证明那条路是多余的——条目本来就在。

---

## 4. 对后续卡的附带读数（供 C1/C2 参考，非本卡验收项）

1. **`AllTargetSlots` 与 `Properties` 会不齐**：第三种形态下 `AllTargetSlots` 有 3 条而 `Properties` 只有 2 条。
   Compose 腿 miss 分支的 Trace 文案据此二分（`ComposeProjectionKernel.cs:303-305`），
   排障时可据「目标属性在不在 AllTargetSlots」区分两种 miss 成因。
2. **`NoAutoMatch()` 会让 `AllTargetSlots` 一起缩水**（不是只缩 `Properties`），
   故该形态下两边都不绑定、也就不构成缺陷④——不要把它当成缺陷④的复现形态。
3. **Fluent Compose 对的主源探测**依赖 `Properties.Length > 0 || ComposeMappings.Length > 0`
   （`ComposeProjectionKernel.cs:55-57`）。若将来有人把 `Properties` 清空来「修」问题，
   会连带打断 Fluent Compose 的主源探测——这条对 C2 是红线。

---

## 5. 复现方式

```powershell
cd D:\OpenCode\NCMapper
dotnet test test/NCMapper.Tests -- --filter-class "*FluentSourceGenNestedProperty*"
```

MTP（Microsoft.Testing.Platform）在用例通过时不显示 stdout。要看打印的属性名列表，直接跑测试可执行文件：

```powershell
& "test\NCMapper.Tests\bin\Debug\net10.0\NCMapper.Tests.exe" `
    --filter-class "*FluentSourceGenNestedProperty*" --show-live-output on
```

实跑输出（节选，`[P1]` 前缀为本卡打印）：

```
[P1] Fluent cfg.For<Site, SiteNestedDto> Properties 目标属性名列表（3 条）：Id, Name, Dept
[P1]   - Id
[P1]   - Name
[P1]   - Dept
[P1] [MapFrom(typeof(AttrSite))] 特性通道 Properties 目标属性名列表（2 条）：Id, Dept
[P1]   - Id
[P1]   - Dept
[P1] U 组 cfg.For<UCfgForSource, UCfgForDto> Properties 目标属性名列表（2 条）：Id, Title
[P1]   - Id
[P1]   - Title
[P1] 负向确认：未找到映射元数据：(...UndeclearedSource, ...UndeclearedDto) 没有生成产物条目。...
```

---

## 6. 取数口径与已删除的探针

本卡最终入库只有测试文件 + 本结论文件。取数用的临时探针（`ZzP1Probe*Tests.cs`）
与落盘中间产物（`temp/p1probe*.txt`）**已全部删除**，未入库。

探针确实读过生成产物文本本身（`obj/Generated/.../NCMapper_RuntimeSnapshotMetadata.g.cs`）
做交叉验证，例如 `M14NestedSource -> M14NestedGenDto` 这对（`MapFrom` 通道）的 `Properties`
只有一条 `Target=Inner / TypeName=M14NestedInner`，与运行时读到的形态一致。

测试隔离口径：本类标 `[Collection("GlobalConfigState")]`，每条用例开头调用
`RuntimeSnapshotStore.Clear()` + `RuntimeSnapshotMetadataInitializer.Initialize()`
（与编译产物的 `[ModuleInitializer]` 同一入口），把注册表恢复到「模块加载态」，
避免同进程其他用例的 `Clear()` 造成串扰。

---

## 7. 已知限制（如实记录）

1. 本卡只核验了 **SQLite/内存元数据层**，未连任何数据库——P1 是「编译产物核验」，不涉及 SQL 生成。
   嵌套在真实 SQL 上的可译性属于 C1/C2/V1 的范围。
2. 结论基于 `NCMapper.Tests` 工程（唯一把 SourceGen 作 Analyzer 引用的测试工程）。
   `NCMapper.IntegrationTests` 无源生成器，其运行时元数据来自手工注册或反射回退，
   **不能**用来复核本结论。
3. 「嵌套条目在 Properties 里」与「该条目在 `Properties` 中是否走 `SourcePathNames` 多段路径」是两件事。
   本卡观测到的嵌套条目都是单段（`SourcePathNames = ["Dept"]`）；
   多段嵌套路径（如 `a.b.c`）不在本卡范围。
