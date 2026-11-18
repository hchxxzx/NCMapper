---
wiki-id: how-to/infrastructure/di
涉及诊断码: （无）
库版本: v1.0.0+
更新时间: 2026-09-15
---

# DI 集成与配置

## 概述

NCMapper 支持通过 `IServiceCollection.AddNCMapper()` 注册到 .NET DI 容器，注入 `IMapper` 接口使用。支持 `NCMapperOptions` 自定义配置（深拷贝策略、递归深度等），以及从 `IConfiguration` 绑定配置。

核心 API：`services.AddNCMapper()` / `services.AddNCMapper(opts => { ... })` / `services.AddNCMapper(configuration)` / `IMapper`

---

## 1. 基本注册

### 用法

[!code-csharp[通用宿主 DI 注册完整形态](~/samples/NCMapper.Samples/06-Infrastructure/DIScenarios.cs#DI_ResolveMapper)]

**要点**：

- `AddNCMapper()` 注册 `IMapper` 为单例（Singleton）
- `IMapper` 接口与静态 `Mapper` 类行为完全一致
- `IMapper.Map<T, U>(src)` 接口与 `Mapper.Map<T, U>(src)` 行为完全一致
- 适用于 ASP.NET Core、Worker Service 等依赖注入场景

### IMapper 接口

```csharp
public interface IMapper
{
    TTarget? Map<TSource, TTarget>(TSource source);
    TTarget? Map<TSource, TTarget>(TSource source, Action<TSource, TTarget>? action);
    void Map<TSource, TTarget>(TSource source, TTarget target);
    void Map<TSource, TTarget>(TSource source, TTarget target, Action<TSource, TTarget>? action);
    List<TTarget>? MapList<TSource, TTarget>(IEnumerable<TSource> sources);
    List<TTarget>? MapList<TSource, TTarget>(IEnumerable<TSource> sources, Action<TSource, TTarget>? action);
    TTarget? Compose<TTarget>(params object[] sources);  // 实现为 params object[]，无 primarySource 参数
    TTarget? DeepCopy<T>(T source);
    TTarget? DeepCopy<TTarget>(object source);  // 此重载实现中不存在，异类深拷贝请用 Mapper.DeepCopy<TSource, TTarget>
}
```

---

## 2. NCMapperOptions 配置

### 用法

[!code-csharp[AddNCMapper(options 回调)注册自定义配置（可运行包装）](~/samples/NCMapper.Samples/06-Infrastructure/DIScenarios.cs#DI_ResolveMapperWithOptions)]

### 配置选项

| 选项 | 类型 | 默认值 | 说明 |
|---|---|---|---|
| `ValidationEnabled` | `bool` | `true` | 是否启用映射验证 |
| `TraceEnabled` | `bool` | `false` | 是否启用映射诊断 Trace |
| `MaxRecursionDepth` | `int` | `64` | 映射 / 深拷贝递归深度上限（防止栈溢出） |
| `MaxInlineDepth` | `int` | `5` | 表达式树构建时的最大内联深度 |
| `TraceOutput` | `TextWriter?` | `null`（用 `Console.Out`） | Trace 输出目标；`TextWriter` 无法从 JSON 绑定，仅代码配置 |
| `DeepCopyStrategy` | `DeepCopyStrategy` | `Fast` | 深拷贝策略（Fast / ExpressionTree / Json） |
| `EFCoreProjectionAsNoTrackingDefault` | `static bool` | `true` | EF Core AsDto 投影默认 `AsNoTracking`；可全局关闭 |

> `EFCoreProjectionAsNoTrackingDefault` 是 `static` 属性，修改影响全局 EF Core 投影行为（非 per-container 隔离，不随 DI 容器隔离）。

### DeepCopyStrategy 枚举

```csharp
public enum DeepCopyStrategy
{
    /// <summary>
    /// Fast 策略（默认）— 高性能，支持循环引用（Acyclic 零检测 + Cyclic/Unknown 完整检测），不保持 DAG 共享引用。
    /// </summary>
    Fast,

    /// <summary>
    /// ExpressionTree 策略 — 含循环引用检测，安全优先。
    /// </summary>
    ExpressionTree,

    /// <summary>
    /// JSON 策略 — 序列化+反序列化，兼容性兜底。
    /// </summary>
    Json
}
```

**要点**：

- `MaxRecursionDepth` 控制映射和深拷贝的递归深度上限
- `DeepCopyStrategy` 控制全局深拷贝策略（影响 `Mapper.DeepCopy` 和 `IMapper.DeepCopy`）
- `ValidationEnabled` / `TraceEnabled` 控制验证与诊断开关
- `TraceOutput` 指定 Trace 输出目标，仅代码配置（无法从 `appsettings.json` 绑定）
- 实例级选项在 `AddNCMapper` 时一次性设置，运行时不可变；`EFCoreProjectionAsNoTrackingDefault` 为 `static` 全局开关

---

## 3. 从 IConfiguration 绑定

`AddNCMapper(IConfiguration)` 重载内部就是调用 `configuration.Bind(options)`，将配置节反序列化为 `NCMapperOptions` 的实例级属性。适用于 ASP.NET Core 等已构建好 `IConfiguration` 的场景。

### appsettings.json

```json
{
  "NCMapper": {
    "ValidationEnabled": false,
    "TraceEnabled": false,
    "MaxRecursionDepth": 128,
    "MaxInlineDepth": 4,
    "DeepCopyStrategy": "ExpressionTree"
  }
}
```

### 注册

```csharp
// Program.cs（ASP.NET Core）
var builder = WebApplication.CreateBuilder(args);

// 从配置节 "NCMapper" 绑定
builder.Services.AddNCMapper(builder.Configuration.GetSection("NCMapper"));

var app = builder.Build();
```

> 配置节名默认为 `NCMapper`。若你的配置节名不同，传入对应 `GetSection` 即可，例如 `configuration.GetSection("MyMapper")`。

### 可绑定键

| 配置键 | 类型 | 能否从配置绑定 |
|---|---|---|
| `ValidationEnabled` | `bool` | ✅ 可绑定 |
| `TraceEnabled` | `bool` | ✅ 可绑定 |
| `MaxRecursionDepth` | `int` | ✅ 可绑定 |
| `MaxInlineDepth` | `int` | ✅ 可绑定 |
| `DeepCopyStrategy` | `enum` | ✅ 可绑定（枚举名字符串：`Fast` / `ExpressionTree` / `Json`） |
| `TraceOutput` | `TextWriter` | ❌ **无法绑定**：JSON 无对应类型，仅能在代码中赋值 |
| `EFCoreProjectionAsNoTrackingDefault` | `static bool` | ❌ **无法绑定**：`static` 成员不被 `ConfigurationBinder.Bind` 处理，必须用 `Action` 重载设置 |

### 两个绑定盲点（重要）

```csharp
// ❌ 以下两项通过 appsettings.json 配置无效，会被静默忽略：

// 1. TraceOutput（TextWriter，JSON 无对应类型）
//    → 改用 Action 重载：
builder.Services.AddNCMapper(o =>
{
    o.TraceOutput = Console.Out;   // 仅代码可设
});

// 2. EFCoreProjectionAsNoTrackingDefault（static 属性，Bind 只处理实例属性）
//    → 改用 Action 重载（注意它是 static 属性）：
builder.Services.AddNCMapper(o =>
{
    NCMapperOptions.EFCoreProjectionAsNoTrackingDefault = false;
});
```

> 若同一调用既要绑定配置又要补充代码项，可叠加两个重载：先 `AddNCMapper(config.GetSection("NCMapper"))` 绑定，再 `AddNCMapper(o => { o.TraceOutput = ...; })` 补充——后者在前者的基础上修改同一个 `NCMapperOptions` 实例（最后一次设置生效）。

### 其它配置源

`IConfiguration` 支持多源叠加，因此以下来源同样生效：

- **环境变量**：`NCMapper__MaxRecursionDepth=256`（双下划线表示分层）
- **命令行**：`--NCMapper:DeepCopyStrategy=Json`
- **用户机密 / 其它自定义配置提供程序**

**要点**：

- `AddNCMapper(IConfiguration)` 等价实现为 `AddNCMapper(opts => configuration.GetSection(...).Bind(opts))`
- 配置节名默认 `NCMapper`，可自定义
- 仅**实例级可读写属性**被绑定；`TraceOutput` 与 `static` 的 `EFCoreProjectionAsNoTrackingDefault` 必须走 `Action` 重载
- 支持所有 .NET 配置源（`appsettings` / 环境变量 / 命令行 / 自定义提供程序）

---

## 4. 静态 Mapper 与 DI IMapper 的关系

| 维度 | 静态 `Mapper` | DI `IMapper` |
|---|---|---|
| 注册方式 | 无需注册 | `services.AddNCMapper()` |
| 配置来源 | 全局静态 | DI 容器注入 |
| 生命周期 | 进程级 | 单例（Singleton） |
| 配置隔离 | 无（全局共享） | 无（全局共享，后注册覆盖先注册） |
| 扩展方法 | ✅ `src.MapToXxxDto()` | ❌ 仅接口方法 |
| 适用场景 | 控制台应用、简单场景 | ASP.NET Core、Worker Service |

### 配置隔离示例（实现为进程级全局共享）

```csharp
// ⚠️ AddNCMapper 的 options 直接写入全局静态（Mapper.* / DeepCopyProviderBase.Current），
// 配置为进程级全局共享：后注册的容器覆盖先注册的配置，两个 mapper 不独立。
// 如需真隔离：使用不同 AppDomain/进程，或等待未来实例化配置支持。
var services1 = new ServiceCollection();
services1.AddNCMapper(opts => opts.MaxRecursionDepth = 32);
var mapper1 = services1.BuildServiceProvider().GetRequiredService<IMapper>();

var services2 = new ServiceCollection();
services2.AddNCMapper(opts => opts.MaxRecursionDepth = 64);
var mapper2 = services2.BuildServiceProvider().GetRequiredService<IMapper>();
// 实际上 mapper1 与 mapper2 共享同一份进程级全局配置——后者（MaxRecursionDepth=64）覆盖前者，不独立。
```

---

## 5. ASP.NET Core 集成

### 用法

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// 注册 NCMapper
builder.Services.AddNCMapper(opts =>
{
    opts.MaxRecursionDepth = 32;
    opts.DeepCopyStrategy = DeepCopyStrategy.Fast;
});

var app = builder.Build();

// 在 Controller 中注入使用
app.MapGet("/users/{id}", (int id, IMapper mapper, IUserService userService) =>
{
    var entity = userService.Get(id);
    return mapper.Map<UserEntity, UserDto>(entity);
});

app.Run();
```

**要点**：

- `AddNCMapper()` 在 `ConfigureServices` 阶段注册
- Controller/Handler 通过构造函数注入 `IMapper`
- 单例注册，线程安全

---

## 常见误用

### 误用 1：在 DI 场景下仍使用静态 Mapper.Map

```csharp
// ❌ 误用：在 DI 场景下使用静态 Mapper.Map
// 如果有自定义配置，静态 Mapper.Map 不会继承 DI 配置
public class UserService
{
    public UserDto GetUser(int id)
    {
        var entity = _repo.Get(id);
        return Mapper.Map<UserEntity, UserDto>(entity);  // 不走 DI 配置
    }
}

// ✅ 正确：使用注入的 IMapper 实例
public class UserService
{
    private readonly IMapper _mapper;
    public UserService(IMapper mapper) => _mapper = mapper;

    public UserDto GetUser(int id)
    {
        var entity = _repo.Get(id);
        return _mapper.Map<UserEntity, UserDto>(entity);  // 走 DI 配置
    }
}
```

### 误用 2：AddNCMapper 多次注册

```csharp
// ❌ 误用：多次调用 AddNCMapper
services.AddNCMapper(opts => opts.MaxRecursionDepth = 32);
services.AddNCMapper(opts => opts.MaxRecursionDepth = 64);  // 覆盖前一个
// 最后一次注册生效，前面的配置被覆盖

// ✅ 正确：只注册一次
services.AddNCMapper(opts =>
{
    opts.MaxRecursionDepth = 32;
    // 所有配置在一个地方
});
```

### 误用 3：IMapper 注册为 Scoped/Transient

```csharp
// ❌ 误用：手动修改 IMapper 生命周期
services.AddScoped<IMapper, NCMapperMapper>();  // 不应手动注册
// AddNCMapper 已注册为 Singleton，手动注册会导致重复注册

// ✅ 正确：使用 AddNCMapper，它已正确注册为 Singleton
services.AddNCMapper();
```

> IMapper 是线程安全的，单例注册是正确选择。映射缓存是全局共享的，多线程并发访问安全。

---

## 小结

| 能力 | API | 特性 |
|---|---|---|
| 基本注册 | `services.AddNCMapper()` | Singleton |
| 配置注册 | `services.AddNCMapper(opts => { ... })` | NCMapperOptions |
| 配置绑定 | `services.AddNCMapper(configuration)` | 从 IConfiguration |
| 映射接口 | `IMapper.Map<T, U>(src)` | 与静态 Mapper 行为一致 |
| 深拷贝接口 | `IMapper.DeepCopy<T>(src)` | 走 DI 配置的策略 |
| Compose 接口 | `IMapper.Compose<T>(p, ...)` | 多源合成 |

---

## 配套示例

```bash
cd samples/NCMapper.Samples
dotnet run --scenario 603
```

核心示例源码（单点同源，`dotnet run --scenario` 可运行验证）：

[!code-csharp[DI 解析](~/samples/NCMapper.Samples/06-Infrastructure/DIScenarios.cs#DI_ResolveMapper)]

代码见 `samples/NCMapper.Samples/06-Infrastructure/DIScenarios.cs`，涵盖 DI 注册、IMapper 注入、映射使用等子场景。

---

## 进一步阅读

- 相关篇目：[验证与诊断](validation.md) · [基础映射](../declarative/basics.md)
- 技术文档深挖：[DI 集成](../../reference/di-integration.md) §9
- DI 源码：`src/NCMapper/NCMapperServiceExtensions.cs` · `src/NCMapper/NCMapperOptions.cs` · `src/NCMapper/IMapper.cs`
