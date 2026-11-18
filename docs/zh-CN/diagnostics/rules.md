---
wiki-id: diagnostics/rules
涉及诊断码: 全索引（NCMAP001-047[缺004 空号], 099；NCMAP_F02-F71[F61 空号] 见 Fluent 各 how-to 页诊断表）
库版本: v1.0.0+
更新时间: 2026-10-23
---

# 诊断规则（NCMAP 数字族全表 + Fluent 族分页转指）

> 本表为 NCMAP 诊断码**主表**：数字族 `NCMAP0xx`（含 099）在此全量单点同源——与 `src/NCMapper.SourceGen/AnalyzerReleases.Shipped/Unshipped.md` 经 G3 门禁（`scripts/check-diagnostics-sync.sh`）机械对账，ID 集合与严重度严格一致；Fluent 族 `NCMAP_Fxx` 为**主表 + 分页转指**布局（逐码全量分布见文末转指句：basics 诊断表 / terminals MapSubclass 专节 / 本表 F 行）。体系模型（两族命名/三级语义/三方登记纪律）见 [explanation/semantics/diagnostics-model](../explanation/semantics/diagnostics-model.md)。

## 16. 诊断规则

| 规则       |   严重度   | 说明                                                                                                                               |
| -------- | :-----: | -------------------------------------------------------------------------------------------------------------------------------- |
| NCMAP001 | ⚠️ Warn | `[MapProperty]` 标注的源字段在源类型中不存在                                                                                                   |
| NCMAP002 | ⚠️ Warn | 属性类型与源字段类型不兼容                                                                                                                    |
| NCMAP003 | ⚠️ Warn | DTO 标注了 `[MapFrom]` 但没有任何属性可以映射                                                                                                  |
| NCMAP005 | ⚠️ Warn | `Compose` 跨表字段未找到                                                                                                                |
| NCMAP006 | ⚠️ Warn | 目标属性存在匹配源属性但 setter 非 public（private set / get-only），无法生成赋值代码                                                                    |
| NCMAP007 | ℹ️ Info | 源属性未映射（无匹配目标属性，提示 DTO 未同步）                                                                                                       |
| NCMAP008 | ❌ Error | 保留编号，不再触发 |
| NCMAP009 | ⚠️ Warn | 重复 `[MapProperty]` 指向同一源属性                                                                                                       |
| NCMAP010 | ⚠️ Warn | 枚举映射成员无重叠（按值强转可能非预期）                                                                                                             |
| NCMAP011 | ℹ️ Info | 非 public 类型跳过源生成器（internal/private 类不生成映射代码）                                                                                     |
| NCMAP012 | ⚠️ Warn | Record 循环引用 DeepCopy 回退运行时（源生成器无法处理递归 record 结构）                                                                                 |
| NCMAP013 | ℹ️ Info | 反向映射属性跳过（Entity←DTO 方向部分属性不可映射； 语义收窄：点路径/扁平化反向已实现写回，保留 Convert 无源/setter 不可用/集合索引反向登记场景）
| NCMAP031 | ❌ Error | 反向写回路径中间节点不可实例化（接口/抽象类/无公共无参构造器，编译期阻断不生成非法代码）
| NCMAP014 | ❌ Error | `[Mask]` 指定的类型未实现 `IMask` 接口                                                                                                     |
| NCMAP015 | ❌ Error | `[Mask]` 仅支持 `string` 类型属性，目标属性类型错误                                                                                              |
| NCMAP016 | ℹ️ Info | 源生成深拷贝：类型存在静态不可解析引用边（object/dynamic/接口/virtual 引用属性等），分类为 Unknown，运行时走完整检测 |
| NCMAP017 | ℹ️ Info | `[MapNoAutoInclude]` 标注在引用导航属性上无意义（导航属性本就由用户显式 Include）                                                                          |
| NCMAP018 | ⚠️ Warn | 数组 `T[].Contains(T)` 在表达式树 lambda 中绑定 `MemoryExtensions.Contains`（.NET 10 / C# 14 下 FreeSql 无法翻译），建议改为 `AsEnumerable().Contains` |
| NCMAP019 | ❌ Error | 类型标注了映射配置特性（属性级十一项 `[MapProperty]`/`[Convert]`/`[ConvertBack]`/`[MapIgnore]`/`[BeforeMapping]`/`[AfterMapping]`/`[Mask]`/`[MapWhen]`/`[MapNoAutoInclude]`/`[MapFormat]`/`[MapNoAutoMatch]`）但未标 `[MapFrom]`——配置无激活路径、静默失效，成对规则编译期强制；请在配置承载侧补标 `[MapFrom]` 或移除特性；Fluent 通道声明的目标类型（`cfg.For<S,T>()`）豁免不报；`[MapTree]` 为类级树构建定义（非映射对），不触发本码 |
| NCMAP042 | ℹ️ Info | 类型命名符合 DTO 模式（`Dto`/`DTO` 后缀）但未标 `[MapFrom]`，无法生成源生成映射代码，运行时回退反射路径在 AOT/Trimmed 场景不可用；携带映射配置特性的类型改报 NCMAP019（Error）；Fluent 通道声明的目标类型豁免不报 |
| NCMAP020 | ⚠️ Warn | 全局映射配置值非法（枚举越界、类型全名不可解析、清单超 8 项等），该项按未设置处理，不阻断编译（对标 Mapperly RMG095） |
| NCMAP021 | ⚠️ Warn | 显式标注（`[MapProperty]`/`[Convert]`/`[ConvertBack]`/`[Mask]`/`[MapFormat]`）命中全局忽略且无 `OverrideIgnore` 时 fail-closed 忽略，消息给双向处置指引 |
| NCMAP022 | ⚠️ Warn | 属性同时标注 `[MapIgnore]` 与 `OverrideIgnore=true` 为矛盾配置，忽略生效（`[MapIgnore]` 不参与全局忽略豁免），指引清理 |
| NCMAP023 | ℹ️ Info | 枚举成员名仅大小写区分（违反 CLS 规范），string→enum 生成 switch 退化为精确匹配（大小写敏感），非法值仍抛 `MapException` |
| NCMAP024 | ℹ️ Info | 枚举成员数值重复（别名枚举），string→enum 数字串标签维度退化为精确名称匹配，数字字符串在生成侧无法转换 |
| NCMAP025 | ❌ Error | 同一无序类型对的双重映射声明（`A [MapFrom(B)]` 且 `B [MapFrom(A)]`；特性通道与 Fluent `For<S,T>()` 混合双声明同属本码），生成侧不生成该对映射（映射双向而生成来源唯一），构建失败；运行时按名匹配兜底，删除一侧声明可恢复；Fluent 侧诊断定位于 `For<S,T>()` 调用位 |
| NCMAP026 | ⚠️ Warn | `[Mask]` 标注位置非规范（源侧，与 `[MapFrom]` 不同侧），源侧标注不生效（Mask 语义单向：目标 string 脱敏），提示移至目标属性 |
| NCMAP027 | ℹ️ Info | [MapFrom] 自动匹配属性类型完全不相容（如 string→Stream）时跳过该成员并提示 |
| NCMAP028 | ℹ️ Info | 映射属性被跳过：成员不可访问（可见性非 public，生成代码仅访问 public；跨程序集访问 internal/protected/private 触发 CS0122） |
| NCMAP029 | ℹ️ Info | 嵌套互调环已降级（两个注册对的 DTO 经嵌套属性互调，环上注册对降级跳过，防止成环数据 StackOverflow；直接自引用由深度保护维持） |
| NCMAP030 | ℹ️ Info | 显式点路径不消费 `[Mask]` / `[Convert]`（映射值不脱敏/不转换，当前版本已知限制，本诊断仅保证可观测） |
| NCMAP032 | ℹ️ Info | 更新路径 init-only 目标属性跳过（init 仅构造/创建期可赋值；更新路径（已有目标对象）无法再次赋值，属性跳过） |
| NCMAP033 | ℹ️ Info | 反向映射默认不消费 [Convert] 转换器（转换器泛型绑定正向方向），反向按类型转换路径写入；反向自定义转换请标注 [ConvertBack] |
| NCMAP034 | ℹ️ Info | [Mask] 属性参与反向写回（脱敏值将原样写入目标实体；脱敏有损不可逆，CRUD 更新场景建议对脱敏 DTO 属性标注 [MapIgnore(ToEntity)]） |
| NCMAP035 | ❌ Error | `[Convert]`/`[ConvertBack]` 方法引用形态：锚定类型上不存在可访问的静态方法（DeclaredOnly 语义，可访问性白名单 public / internal 同程序集 / protected internal 同程序集） |
| NCMAP036 | ❌ Error | `[Convert]`/`[ConvertBack]` 方法引用形态：同名静态方法多个重载同时命中签名模板（歧义，刻意不提供按签名选择重载的构造形态） |
| NCMAP037 | ❌ Error | `[Convert]`/`[ConvertBack]` 方法引用形态：无任何重载命中四模板（非泛型静态、无可选/params/ref-out-in 参数、返回目标属性类型、参数完全相等） |
| NCMAP038 | ❌ Error | `[BeforeMapping]`/`[AfterMapping]` 钩子方法不可解析：锚定类型上不存在可访问的静态方法（须 public 或 internal 同程序集）；方法声明在标注 DTO 之外的宿主类型上时须改用双参构造形态显式指定宿主 |
| NCMAP039 | ❌ Error | `[BeforeMapping]`/`[AfterMapping]` 钩子方法不满足 `void(TSource, TTarget)` 契约（返回非 void/参数数≠2/参数类型与映射对不兼容/锚定类型为开放泛型） |
| NCMAP040 | ❌ Error | `[BeforeMapping]`/`[AfterMapping]` 钩子方法存在多个与映射对签名兼容且无精确匹配的可访问重载（歧义，刻意不提供按签名选择重载的构造形态） |
| NCMAP041 | ❌ Error | `[MapWhen]` 谓词方法解析失败：方法不存在/多重载歧义/非静态/可见性不足/签名不符（单参须 == 源字段类型（T1）或 == 源实体类型（T2）） |
| NCMAP043 | ❌ Error | `[MapFormat]` 目标属性类型非 string（格式化输出须为文本属性），请改为 string 属性或改用 `[Convert]` |
| NCMAP044 | ❌ Error | `[MapFormat]` 源属性类型未实现 `IFormattable`（支持 DateTime/DateTimeOffset/TimeSpan/Guid/数值/枚举，含 Nullable 包装；string 源不支持），请改用 `[Convert]` |
| NCMAP045 | ❌ Error | `[MapFormat]` 与 `[Convert]` 同时标注于同一属性（语义重叠互斥），请二选一 |
| NCMAP046 | ⚠️ Warn | `[MapFormat]` 属性反向不赋值——格式化输出不可反解析（有损且随文化），反向映射跳过该属性（实体侧保持原值）；需要往返请自行声明 `[ConvertBack]` |
| NCMAP047 | ❌ Error | `[MapNoAutoMatch]` 白名单模式下属性带映射变换特性（`[Convert]`/`[Mask]`/`[MapFormat]`/`[MapWhen]`）但未同身标注 `[MapProperty]`——白名单只映射点名声明的属性，该属性不映射、不入候选。最小示例：`[MapFrom(typeof(U))] [MapNoAutoMatch] class UDto { [Convert(typeof(C))] public string Name { get; set; } }` → 本码（`Name` 无 `[MapProperty]`）。两条出路：补 `[MapProperty]` 显式声明，或移除变换特性 |
| NCMAP_F55 | ⚠️ Warn | Fluent 多段目标路径不支持（`Map(d => d.Address.City, ...)` 形态当前无实现），该声明跳过 |
| NCMAP_F18 | ❌ Error | `ConvertBack` 反向写回目标不存在：实体类型上不存在锚点对应属性（写回目标按「同名 → 自动扁平化叶 → lambda 体」三级顺序解析均未命中，或 lambda 体不在收窄规则内——只认形参上的单层成员访问 / 直接使用字段形参，复合表达式不猜测）。出路：为实体类型补充该属性、修改锚点名称、移除 `ConvertBack` 声明，或异名时用 lambda 形态自行指明写回目标 |
| NCMAP_F56 | ❌ Error | Fluent 属性实参不是 lambda 表达式（方法组/委托变量等可编译形态），整条声明不生成，编译期显式报错 |
| NCMAP_F57 | ⚠️ Warn | Fluent lambda 含语句块体或三元表达式，无法归约为属性路径，声明跳过（`MapWhen`/`Convert` 内联谓词经内联转录器处理，不受影响） |
| NCMAP_F58 | ❌ Error | Fluent 实参不可静态求值（`overrideIgnore`/方向参数传变量等），该实参被忽略并按默认值生效（声明照常生成，映射行为不变） |
| NCMAP_F59 | ❌ Error | 同一锚点属性的真冲突：同槽位重复声明，或映射声明与 Ignore 并存——真冲突直接拒绝，保留其一或按冲突清单合法组合（2026-10-09 语义改写：原"后写覆盖先写（警告）"收窄为"仅真冲突报错"，级别 Warn → Error） |
| NCMAP_F60 | ⚠️ Warn | Fluent 链上出现未识别的方法名（如 object 继承成员等可编译但非终端的调用），该节点声明被跳过（主链场景整条链丢弃） |
| NCMAP_F62 | ⚠️ Warn | Fluent lambda 源链成员访问深度超过上限（16 段），深链声明丢弃，提示拆分中间投影或拍平源模型 |
| NCMAP_F63 | ⚠️ Warn | 环检测访问预算耗尽（单起点深搜访问数超上限），该起点环检测结果未决（不发射 NCMAP029、不标记降级），仅本码提示 |
| NCMAP_F64 | ❌ Error | Fluent `.Format` 目标属性类型非 string，请改为 string 属性或改用 `.Convert` |
| NCMAP_F65 | ❌ Error | Fluent `.Format` 绑定的源属性未实现 `IFormattable`（支持 DateTime/DateTimeOffset/TimeSpan/Guid/数值/枚举，含 Nullable 包装），请改用 `.Convert` |
| NCMAP_F66 | ❌ Error | Fluent `.Format` 与 `.Convert` 同时指向同一属性（语义重叠互斥），请二选一 |
| NCMAP_F67 | ⚠️ Warn | Fluent `.Format` 属性反向不赋值——格式化输出不可反解析（有损且随文化），反向映射跳过该属性（实体属性保持默认值）。该属性需要往返时自行声明反向转换，两条出路：实体侧存在同名属性用 `.ConvertBack(converter)`；两侧名字不同用 lambda 形态 `.ConvertBack<TEntityProp>((dto) => ...)`，写回目标由 lambda 体解析（取自形参上的成员访问），不再按名查找 |
| NCMAP_F68 | ⚠️ Warn | Member 字段段无任何跟随声明（裸 Member）——请补来源（From / FromSource）、忽略（Ignore）或变换（Mask / Format 等） |
| NCMAP_F69 | ❌ Error | 字段段省略来源且源类型（或 FromSource 子源）无同名成员——旧写法该声明静默消失，新形态显式报错；请用 `.From(...)` 指定来源 |
| NCMAP_F70 | ❌ Error | 同一属性重复声明（同槽位重复——来源槽/变换槽各只允许一次），报告首个与重复的声明种类；保留其一 |
| NCMAP_F71 | ❌ Error | MapWhen 整源形态类型参数既不匹配目标属性类型也不匹配源类型——整源形态用 `.MapWhen<源类型>(...)`，字段值形态（条件入参为目标属性类型）省略类型参数 |
| NCMAP099 | ⚠️ Warn | 单映射源生成失败（其余映射不受影响，降级运行时）                                                                                                         |

> 诊断码全集：`NCMAP001`–`NCMAP047`（`NCMAP004` 为预留空号）+ `NCMAP099` + Fluent 族 `NCMAP_F02`–`NCMAP_F71`（`F61` 为预留空号，码号不再使用）；其中 `NCMAP014/015/019/025/031/035/036/037/038/039/040/041/043/044/045/047` 与 Fluent 族 `F11/F12/F14/F16/F17/F18/F19/F20/F21/F23/F24/F25/F26/F27/F29/F30/F31/F32/F35/F36/F37/F38/F39/F40/F41/F42/F43/F44/F45/F46/F47/F48/F49/F51/F52/F53/F56/F58/F59/F64/F65/F66/F69/F70/F71` 为 Error 级诊断码。

> Fluent 通道诊断族 `NCMAP_F02`–`NCMAP_F71`（Fluent 声明面；F46–F50 = MapSubclass 族；F20/F26 因转录引擎收窄至闭包类残余；F55–F58 = 静默语义治理族，F59/F60/F62/F63 = 链解析显式化族，F64–F67 = Format 终端族，F68–F71 = Member 三段式防呆族——F68 裸字段提醒 Warning，F69 省略来源无同名 / F70 同槽位重复 / F71 MapWhen 类型参不符 = Error）：逐码条目分布为「主表 + 分页转指」——本族大多数码（F02–F45、F51–F54、F59、F60、F64–F71）逐码见 [Fluent 入口](../how-to/fluent/basics.md) 诊断表；F46–F50 = MapSubclass 族逐码见 [terminals](../how-to/fluent/terminals.md) 的 MapSubclass 专节诊断表；F55–F58、F62、F63 逐码条目在本主表上方各行。特性侧双通道解析族 `NCMAP038`–`NCMAP041`（生命周期钩子/条件守卫）见表内条目；`NCMAP043`–`NCMAP046` = [MapFormat] 特性族见表内条目；`NCMAP047` = [MapNoAutoMatch] 白名单门（变换特性缺 [MapProperty] 头声明）见表内条目。

---

## 三原则与检测器对照

映射声明的三条基本规则各有对应的编译期检测器（规则被违反时必给编译期信号，"原则-检测器成对"）。三原则的正式定义与设计意图见 [映射规则（语义）§6](../explanation/semantics/mapping-rules.md)：

| 规则 | 一句话表述 | 检测器 |
|------|-----------|--------|
| P1 通道互斥 | 同一对类型（无论方向）只能由特性通道或 Fluent 通道之一声明；不同类型对可自由混用 | `NCMAP_F11`（双声明互斥）· `NCMAP025`（无序互指） |
| P2 侧别互斥 | `[MapFrom]` 一个类至多标注一次（重复标注编译期 CS0579）；`For<A, B>` 与 `For<B, A>` 不得同时声明 | `NCMAP025` · CS0579 |
| P3 配置跟随类锚 | 属性级映射配置标注十一项只在所在类有映射声明锚（`[MapFrom]` 或 `For<S,T>()`）时生效；无锚一律不认 | `NCMAP019`（Error，十一项家族含 `[MapFormat]`/`[MapNoAutoMatch]`）· `NCMAP042`（Info，仅 Dto 命名启发式） |


---

## 如何处理诊断

1. **Warning 不阻断编译**，但建议全部处理（避免运行时意外）
2. **Error 阻断编译**，必须修复
3. **Info 仅提示**，不阻断编译（用于标记可观测的已知限制或退化行为）
4. 单码抑制：项目文件 `<NoWarn>NCMAP001</NoWarn>`（Error 级不可抑制）
5. 确认误报或已在其他路径处理时，可单独抑制并附注释说明

---

## 相关页

- [explanation/semantics/diagnostics-model](../explanation/semantics/diagnostics-model.md) — 体系模型（两族命名/三级语义/三方登记纪律）
- [验证与诊断](../how-to/infrastructure/validation.md) — 运行时验证、追踪与异常诊断操作向
- [reference/api-comparison](../reference/api-comparison.md) — 双 API 面诊断族归属对照