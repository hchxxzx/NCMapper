<!-- 来源：NCMapper 主技术文档 附录 B 常见问题 FAQ -->
## 附录 B：常见问题（FAQ）

### 开发常见问题

**Q：标注了 `[MapFrom]` 但源生成器没有生成代码？**

检查以下几点：项目是否引用了 `NCMapper` 包（源生成器已内嵌其中，无需单独安装）；目标类型必须是 class（不支持 struct / record struct）；`[MapFrom]` 必须标注在目标类型（DTO）上而非源类型上；确认 `<NCMapperSourceGenEnabled>` 未被设为 false。IDE 中可尝试 Clean + Rebuild 触发生成器重新执行。

**Q：映射后某属性为 null，如何排查？**

开启 Trace 诊断（`Mapper.TraceEnabled = true`），查看该属性的映射方式输出。常见原因：属性名拼写不一致（NCMapper 使用 OrdinalIgnoreCase 匹配，大小写不敏感但拼写必须一致）；源属性类型与目标属性类型不兼容且无隐式转换；属性被 `[MapIgnore]` 标注；嵌套对象的源属性本身为 null（NCMapper 不会创建空对象）。也可使用 `Mapper.Validate()` 在启动时批量检查所有映射对。

**Q：NCMAP006 警告"目标属性未映射"如何处理？**

该警告的含义是：源类型中存在同名属性，但目标属性的 setter 非 public（`private`/`protected`/`internal`/`init` 或无 setter），生成代码无法赋值。两种处理方式：需要映射就把 setter 改为 `public set`；不需要映射就标注 `[MapIgnore]` 显式忽略。注意与相邻码区分：`NCMAP007` 是"源属性未映射"（源属性找不到对应目标属性），`NCMAP003` 是"标注了 `[MapFrom]` 但没有任何属性可映射"。

**Q：源生成器路径和运行时路径如何选择？**

生产环境推荐源生成器路径：标注 `[MapFrom]` 后编译期生成全部映射代码，零首次延迟、零反射、AOT 友好。运行时路径（表达式树）适用于无法修改目标类型源码（如第三方库类型）、需要动态类型映射、或原型验证阶段。两条路径可共存——有 `[MapFrom]` 标注走源生成器，无标注走运行时。

**Q：DeepCopy 该用哪个策略？**

默认 `FastDeepCopyProvider` 适合绝大多数场景（三值分类条件分发：Acyclic 零检测快路径 ~401ns；Cyclic/Unknown 转发基类完整检测，任意环长）。如果对象图需要**保持 DAG 共享引用**（同一源实例被多处引用，拷贝后仍共享同一实例），切换为 `ExpressionTreeDeepCopyProvider`（~409ns，含完整循环引用检测 + DAG 共享保持）。如果类型已标注 `[MapFrom]`，源生成器路径自动生效（~179ns，最快）。以上均为 net10.0 Linux 实测口径，不同机器实测数值差异较大。注意：record 类型若属性图存在循环引用或静态不可解析边（object/dynamic/接口/virtual 引用属性），源生成器会编译期自动检测并回退到运行时 Provider（发出 NCMAP012，详见[限制与注意事项](../explanation/limitations.md)），用户无需手工切换。

**Q：`Mapper.Map` 和扩展方法 `MapToXxx()` 有什么区别？**

`Mapper.Map<S,T>(src)` 通过泛型静态缓存查找委托，有少量间接开销。扩展方法 `src.MapToXxxDto()` 由源生成器编译时生成，直接调用 Core 映射方法，零委托开销且 JIT 可内联（`[MethodImpl(AggressiveInlining)]`），是性能最优路径。功能上两者等价，热路径推荐扩展方法。

**Q：循环引用导致 MapException 怎么办？**

NCMapper 运行时引擎使用 ThreadStatic 递归深度计数器（默认 64 层），超限抛出 `MapException` 而非栈溢出。处理方式：如果对象图确实很深（非循环），调大 `Mapper.MaxRecursionDepth`；如果是真正的循环引用，在 DTO 侧打断循环（不映射反向导航属性），或使用 `[MapIgnore]` 忽略导致循环的属性。源生成器路径编译时生成固定深度代码，不存在此问题。

**Q：`MapList` 和手动 `Select` + `Map` 有什么区别？**

`Mapper.MapList<S,T>(list)` 内部使用内联 for 循环逐元素调用 Core 映射，无逐元素委托查找开销，且对 null 集合返回 null（而非空集合）。手动 `list.Select(x => Mapper.Map<S,T>(x)).ToList()` 每个元素都经过泛型缓存查找。性能敏感场景推荐 `MapList`；需要 LINQ 链式操作（Where、OrderBy）时可用扩展方法 `src.MapToXxxDtoList()`。

### 对比与选型

**Q：NCMapper 与 AutoMapper 的核心差异？**

AutoMapper 是纯运行时反射 + 表达式树方案，配置集中在 Profile 类中，首次映射有编译延迟。NCMapper 采用双引擎架构：源生成器在编译期生成全部映射代码（零反射、零首次延迟、AOT 友好），运行时表达式树作为兜底。NCMapper 使用属性标注（`[MapFrom]`）替代 Profile 配置，映射关系与类型定义共存，IDE 可导航、重构友好。此外 NCMapper 内置深拷贝体系、ORM Projection、多源 Compose、映射验证等 AutoMapper 需要额外配置或第三方库才能实现的功能。

**Q：NCMapper 与 Mapperly 的核心差异？**

两者都使用 Roslyn 源生成器在编译期生成映射代码，设计理念相近。主要差异：NCMapper 额外提供完整的运行时引擎（表达式树），未标注的类型也能在运行时自动映射，无需为每个类型对都添加标注；NCMapper 内置深拷贝（三策略可替换）、多源 Compose、ORM Projection（FreeSQL / EF Core / Dapper）、映射验证、Trace 诊断等开箱即用功能；NCMapper 生态提供独立的 AutoMapper 自动迁移工具（NCMapper.Migrate 伴随项目）。Mapperly 在纯源生成器场景下更轻量，适合只需要编译期映射、不需要运行时动态能力的场景。

**Q：什么场景适合用 NCMapper？**

NCMapper 适合以下场景：需要极致映射性能（源生成器路径接近手写代码）；项目同时需要编译期安全和运行时灵活性（双引擎）；需要深拷贝、ORM 投影、多源合成等高级功能；从 AutoMapper 迁移（生态内提供独立自动迁移工具 NCMapper.Migrate）；AOT / NativeAOT 部署（源生成器路径完全兼容）。如果只需要简单的编译期映射且不需要运行时回退，Mapperly 也是合理选择。

**Q：性能数据在哪里查看？**

本文档的性能基准数据表（映射三路径 + 深拷贝三策略，含跨框架对比）详见独立报告。核心结论（2026-10-07 Linux 全量跑批，Intel Xeon Silver 4314 / Ubuntu 24.04.5，net10.0）：扩展方法路径 ~7~33 ns（视场景复杂度），接近手写赋值；深拷贝源生成器路径 ~179 ns，是最快的自动 DeepCopy 方案。完整数据（13 映射场景 × 3 框架 + 深拷贝三策略）请参阅项目 `benchmarks/` 目录与 `docs/` 下的性能测试报告；性能数值随测试机 CPU/OS 差异较大，以报告标注环境为准。

### 竞品功能替代方案

以下为 AutoMapper / Mapperly / Mapster 的常用能力与 NCMapper 的对应做法：多数已有专用 API 或全局配置（已在条目内点名），少数通过现有机制实现等价效果。

**Q：如何实现条件映射（AutoMapper 的 `.Condition()`、Mapster 的 `.When()`）？**

NCMapper 有专用 API——`MapWhen` 条件守卫（谓词为真才执行该属性的映射赋值；条件假则跳过赋值：新建路径保持初始化器值、更新路径保留既有值）：

```csharp
[MapFrom(typeof(User))]
public class UserDto
{
    [MapWhen(nameof(IsAgeValid))]   // 谓词为真才映射该属性
    public int Age { get; set; }

    // T1 字段值谓词：单参类型 == 源字段类型；谓词声明在被标注属性的声明类型上
    public static bool IsAgeValid(int fieldValue) => fieldValue > 0;
}
```

Fluent 通道等价终端为 Member 三段式：在 `.Member(d => d.Age)` 锚定字段后接 `MapWhen` 守卫——字段值谓词写 `.MapWhen(predicate)`（入参 == 源字段值）；整源谓词写 `.MapWhen<User>(condition)`（须显式类型参；谓词单参类型 == 源实体类型时按"整源"判，谓词读整个源对象）。谓词支持命名方法组与无捕获内联 lambda 两形态。完整语义（形态与诊断）见 [Fluent 四终端](fluent/terminals.md) 与 [特性系统](../reference/features.md) §5.8；可运行演示 `dotnet run --scenario 105`。

条件命中后需要做更复杂的处理时，仍可用 `[Convert]` 在转换方法内判断，或用 Action 回调：`Mapper.Map<User, UserDto>(user, (s, t) => { if (s.Age <= 0) t.Age = 0; })`。

**Q：如何实现空值替换（AutoMapper 的 `.NullSubstitute()`）？**

使用 `[Convert]`，在静态转换方法中做空值兜底：

```csharp
public static class NicknameConverter
{
    public static string Convert(string? fieldValue)
        => fieldValue ?? "匿名用户";
}

[MapFrom(typeof(User))]
public class UserDto
{
    [Convert(typeof(NicknameConverter), nameof(NicknameConverter.Convert))]
    [MapProperty(typeof(User), nameof(User.Nickname))]
    public string Nickname { get; set; }
}
```

`[Convert]` 引用方法的 `fieldValue` 参数即为匹配到的源字段值（源属性为 null 时传 null），直接在方法内 `??` 即可。

**Q：如何实现自定义对象映射方法（Mapperly 的 partial 用户方法）？**

Mapperly 允许用户手写一个类型对的映射方法，生成器在所有嵌套引用处自动调用。NCMapper 的等价方式有两种：

方式一：`[Convert]` — 转换方法内完整构建目标对象：

```csharp
public static class CustomerConverter
{
    public static CustomerDto? Convert(Customer? fieldValue)
    {
        if (fieldValue is null) return null;
        return new CustomerDto
        {
            FullName = $"{fieldValue.FirstName} {fieldValue.LastName}",
            Level = fieldValue.Points > 1000 ? "VIP" : "Normal"
        };
    }
}

[MapFrom(typeof(Order))]
public class OrderDto
{
    [Convert(typeof(CustomerConverter), nameof(CustomerConverter.Convert))]
    [MapProperty(typeof(Order), nameof(Order.Customer))]
    public CustomerDto? Customer { get; set; }
}
```

方式二：`[MapIgnore]` + 管道 PostMap — 跳过自动映射后自行调用：

```csharp
[MapFrom(typeof(Order))]
public class OrderDto
{
    public int Id { get; set; }

    [MapIgnore]
    public CustomerDto? Customer { get; set; }
}

// 启动时注册管道
Mapper.Pipeline.AddPostMap<Order, OrderDto>((src, dst) =>
{
    dst.Customer = MyCustomCustomerMap(src.Customer);
});
```

两者的差异：Mapperly 的 partial 方法写一次即全局生效（所有映射对遇到 `Customer → CustomerDto` 自动调用）；NCMapper 需在每个引用该类型对的属性上标注 `[Convert]`。如果同一类型对在多个 DTO 中被引用，可将转换方法类复用（标注多处但转换方法只写一个）。

**Q：如何实现继承/多态映射（AutoMapper 的 `.Include<Base, Derived>()`）？**

NCMapper 有专用 API——`MapSubclass` 多态子类型分派：基类映射运行时按源实例**实际类型**路由到对应子类映射体（isinst 分派链，最派生优先、未命中回落基类体）：

```csharp
[MapFrom(typeof(Animal))]
[MapSubclass(typeof(Dog), typeof(DogDto))]    // 每对子类型登记一个（AllowMultiple）
[MapSubclass(typeof(Cat), typeof(CatDto))]
public class AnimalDto { public string Name { get; set; } = ""; }

public class DogDto : AnimalDto { public string Breed { get; set; } = ""; }
public class CatDto : AnimalDto { public bool Indoor { get; set; } }
```

Fluent 通道等价终端 `.MapSubclass<TSrcDerived, TTargetDerived>()`（对级登记，`cfg.For` 即锚定）。特性侧须用 `[MapFrom]` 锚定基类映射对（缺锚定报 `NCMAP_F49`）；分派覆盖 `Map` / `_Update` / `MapList` / `MapListInto` 四族，反向方向同样支持。完整语义（诊断 F46~F50）见 [特性系统](../reference/features.md) §5.10 与 [declarative/advanced](declarative/advanced.md)；可运行演示 `dotnet run --scenario 107`。

仅当子类型集合编译期不可枚举（如插件式动态加载）时，才需要在业务层手动分发（`switch` 到各子类映射）。

**Q：如何实现命名约定/前缀后缀识别（AutoMapper 的 `RecognizePrefixes()`）？**

NCMapper 已提供全局配置（程序集级 `[assembly: MapDefaults]`），覆盖这两类需求：

```csharp
[assembly: MapDefaults(
    Prefixes  = new[] { "m_" },                    // 源属性名前缀剥离：m_CustomerName → CustomerName
    Postfixes = new[] { "Entity" },                // 源属性名后缀剥离：CustomerNameEntity → CustomerName
    NamingStrategy = NamingStrategy.SnakeCase)]    // 命名风格归一：user_name ↔ UserName
```

- `Prefixes` / `Postfixes`：源属性名剥离清单（**仅源侧**生效；各 ≤8 项，超限该清单降级"未设置"并报 `NCMAP020`）；剥离后生成的候选名按 `NamingStrategy` 归一化，再参与同名匹配与自动扁平化。
- `NamingStrategy`：`Default`（忽略大小写，默认）/ `CaseSensitive` / `SnakeCase`（`user_name` ↔ `UserName`）/ `UpperSnakeCase`（`USER_NAME` ↔ `UserName`）。

个别特例仍可用 `[MapProperty]` 逐属性显式指定做精确控制。候选名生成 → 归一化 → 查表匹配 → 忽略判定的完整口径见 [全局映射配置](infrastructure/global-config.md)。

