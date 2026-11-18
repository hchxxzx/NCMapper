---
wiki-id: tutorials/web-integration-tutorial
涉及诊断码:
库版本: v1.0.0+
更新时间: 2026-09-29
---

# Web 项目集成演练：ASP.NET Core 接入

## 概述

本教程解决接入问题：一个 ASP.NET Core 项目要从零用上 NCMapper——装哪个包、怎么注册、控制器/服务里怎么拿到映射能力、常用配置有哪些。照着做完，项目就具备"依赖注入 + 声明式映射 + 编译期生成"的完整能力。

示例与 `samples/NCMapper.Samples` 的 06 场景（603）语义一致，可运行验证；各节直接给出简明示意代码，文末附完整代码示意便于整体理解。注册与配置的完整说明见 [DI 集成与配置](../how-to/infrastructure/di.md)，本教程侧重从零接到能跑。

---

## 1. 安装与注册

Web 项目安装核心包（`NCMapper`）与 DI 扩展（已包含在核心包内，无需单独引用）：

```bash
dotnet add package NCMapper
```

在 `Program.cs` 里注册（三行接入）：

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddNCMapper();          // 默认配置注册
builder.Services.AddControllers();

var app = builder.Build();
app.MapControllers();
app.Run();
```

`AddNCMapper` 有三种重载，按配置来源选：

| 重载 | 用途 |
|------|------|
| `AddNCMapper()` | 默认配置（验证开启、Trace 关闭、递归深度 64） |
| `AddNCMapper(opts => ...)` | 代码里直接配置 |
| `AddNCMapper(configuration.GetSection("NCMapper"))` | 从 appsettings.json 读配置 |

---

## 2. 注入使用：控制器与服务里怎么映射

注册后 `IMapper` 可以直接注入到控制器或服务（与演示场景 `DI_ResolveMapper` 同型）：

[!code-csharp[通用宿主 DI：AddNCMapper 注册与 IMapper 解析](~/samples/NCMapper.Samples/06-Infrastructure/DIScenarios.cs#DI_ResolveMapper)]

放到真实项目里的样子：

```csharp
[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly IMapper _mapper;

    public UsersController(IMapper mapper) => _mapper = mapper;

    [HttpGet("{id}")]
    public IActionResult Get(int id)
    {
        var entity = _userService.Find(id);
        var dto = _mapper.Map<UserEntity, UserDto>(entity);
        return Ok(dto);
    }
}
```

`IMapper` 接口暴露的就是 `Map` / `MapList` / `Map(src, target)` 就地更新这一组统一入口；声明式扩展方法（`src.MapToUserDto()`）不走注入，按 `using NCMapper.Generated` 直接调用，两者并存不冲突。

---

## 3. 声明映射：两通道随项目风格选

接入后要声明类型对，两条通道按团队习惯选（原理见 [双通道架构](../explanation/dual-channel.md)）：

- **Attribute 通道**：`[MapFrom]` 标在 DTO 类上，同名属性自动匹配——改 DTO 就改映射，上手最快
- **Fluent 通道**：`[MapperConfig]` 标在独立配置类，链式声明——映射关系复杂、DTO 要保持干净时用

两通道编译期汇成同一份生成代码，混用不同类型对完全没问题；同一类型对不要两边都声明（编译期 `NCMAP_F11` 拦截）。

---

## 4. 常用配置项

代码或 appsettings.json 两种写法等价：

```json
"NCMapper": {
  "ValidationEnabled": true,
  "TraceEnabled": false,
  "MaxRecursionDepth": 128,
  "DeepCopyStrategy": "Fast"
}
```

| 配置项 | 默认 | 说明 |
|--------|------|------|
| `ValidationEnabled` | `true` | 启动时是否执行映射验证体检 |
| `TraceEnabled` | `false` | 映射诊断日志开关（排降级问题时装） |
| `MaxRecursionDepth` | `64` | 嵌套映射递归深度上限，超过视为循环引用 |
| `DeepCopyStrategy` | `Fast` | 深拷贝实现（Fast / ExpressionTree / Json） |

Web 项目建议保持验证开启（启动期暴露配置问题）；遇到投影降级排查时临时开 `TraceEnabled`。

---

## 5. 项目里的分层建议

映射代码放哪，一个可执行的约定：

| 层 | 职责 | 映射用法 |
|----|------|---------|
| DTO 项目 | 定义 DTO + 映射声明（`[MapFrom]` 或 Fluent 配置类） | 只声明，不调用 |
| 服务层 | 业务编排 | 在服务边界完成实体 ↔ DTO 转换 |
| 控制器 | HTTP 协议处理 | **不写映射逻辑**，只收发 DTO |

把映射压在服务边界的原因：映射声明集中可审计（`Mapper.Validate()` 一键体检全部类型对）、控制器保持薄、DTO 与实体的对应关系一处可查。

---

## 6. 上线前自检清单

| 检查 | 方法 |
|------|------|
| 所有声明类型对能生成、无冲突 | 编译零诊断 + `Mapper.Validate()` 无报告项 |
| 注入可用 | 任一服务注入 `IMapper` 后跑一次真实映射 |
| AOT/裁剪部署（如有） | 类型对全部走声明，参照 [AOT 部署](../how-to/aot/deployment.md) |
| 投影场景（如有） | 开 `TraceEnabled` 看是否意外降级，参照 [ORM 降级机制](../explanation/orm-degradation.md) |

---

## 7. 常见问题

**`AddNCMapper` 注册了什么？** `IMapper` 实例与相关配置。源生成器生成的映射代码是编译进程序集的静态方法（程序集加载时自动注册），不依赖 DI 注册——DI 只是给了 `IMapper` 一个统一的注入入口。

**静态 `Mapper.Map` 与注入 `IMapper` 冲突吗？** 不冲突，底层共享同一份生成代码与缓存。静态入口写起来快，注入入口便于测试替换；同一项目混用没有问题。

**配置写错会怎样？** `AddNCMapper(configuration...)` 读到缺失字段时用默认值；代码配置写错类型直接编译报错。启动后跑 `Mapper.ValidateOrThrow()` 可把配置问题在启动期强制暴露。

---

## 8. 完整代码示意

从零接到能跑的完整代码（教学完整示意；`Find` 等数据访问方法按你的项目实际实现）：

```csharp
// ================= 1. Program.cs：注册 =================
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddNCMapper(opts =>
{
    opts.ValidationEnabled = true;     // 启动期暴露映射配置问题
    opts.MaxRecursionDepth = 128;      // 深嵌套模型按需调大
});
builder.Services.AddControllers();

var app = builder.Build();
app.MapControllers();
app.Run();

// ================= 2. Models/UserDto.cs：声明映射 =================
using NCMapper.Attributes;

[MapFrom(typeof(UserEntity))]
public class UserDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public string Email { get; set; } = "";
}

// ================= 3. Services/UserService.cs：服务边界完成映射 =================
using NCMapper;

public class UserService
{
    private readonly IMapper _mapper;

    public UserService(IMapper mapper) => _mapper = mapper;

    public UserEntity? Find(int id)
    {
        // 数据访问按你的项目实际实现（EF Core / Dapper / 仓储……）
        throw new NotImplementedException();
    }

    public UserDto ToView(UserEntity entity)
        => _mapper.Map<UserEntity, UserDto>(entity)!;   // 映射压在服务边界
}

// ================= 4. Controllers/UsersController.cs：控制器保持薄 =================
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly UserService _service;

    public UsersController(UserService service) => _service = service;

    [HttpGet("{id}")]
    public IActionResult Get(int id)
    {
        var entity = _service.Find(id);
        if (entity is null) return NotFound();
        return Ok(_service.ToView(entity));   // 只收发 DTO，不写映射逻辑
    }
}
```

读这份代码的顺序：注册（三行接入）→ 声明（一个 `[MapFrom]` 全配双向）→ 服务（映射压在边界）→ 控制器（只收发 DTO）。

---

## 相关文档

- [DI 集成与配置](../how-to/infrastructure/di.md)：注册与配置的完整参考
- [映射验证](../how-to/infrastructure/validation.md)：`Mapper.Validate()` 体检报告解读
- [双通道架构](../explanation/dual-channel.md)：两条声明通道的选型依据
- [日常双向映射演练](mapping-tutorial.md)：接入后的第一个映射怎么写
