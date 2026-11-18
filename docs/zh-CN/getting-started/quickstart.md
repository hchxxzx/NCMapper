<!-- 来源：NCMapper 主技术文档 §2 快速开始 -->
## 2. 快速开始

### 2.1 安装

```xml

<PackageReference Include="NCMapper" />
```

### 2.2 基本用法（Fluent 声明，推荐入口）

NCMapper 推荐用 **Fluent 通道**声明映射：映射规则集中写在一个配置类里，实体与 DTO 保持干净（不加任何标注）。

```csharp
// 1. 定义实体
public class UserEntity
{
    public int Id { get; set; }
    public string Name { get; set; }
    public DateTime CreatedAt { get; set; }
}

// 2. 定义 DTO（普通类，无需标注）
public class UserDto
{
    public int Id { get; set; }
    public string Name { get; set; }
    public DateTime CreatedAt { get; set; }
}

// 3. 写映射配置类：标注 [MapperConfig]，类内声明静态方法（方法名任意，签名固定）
[MapperConfig]
public class UserMapperConfig
{
    public static void Configure(FluentMappingConfig cfg)
        => cfg.For<UserEntity, UserDto>();   // 同名同类型自动匹配，无需逐属性声明
}
```

第 4 步「映射：与特性通道共用同一入口，同名属性自动赋值」的可运行版本（`Mapper.Map` 统一入口，演示类型对经 [MapFrom] 声明——与 Fluent 声明产物同源）：

[!code-csharp[同名属性自动匹配的映射调用](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_SimpleNameMatch)]

配置类声明后**无需手动注册**：源生成器在编译期扫描 Configure 方法体生成映射代码，并经模块初始化器自动注册进 `Mapper`。

同名自动匹配不够用时，在 `For(...)` 链上追加终端方法（异名映射、忽略、脱敏、自定义转换、条件守卫等），完整终端表见 [Fluent 入口](../how-to/fluent/basics.md)。下面是链式连写的完整形态（异名 `Map` + 嵌套路径 + `Ignore` + `Mask` 脱敏 + `AfterMapping` 汇总）：

[!code-csharp[Fluent 基础链：Map/Ignore/Mask/AfterMapping 连写](~/samples/NCMapper.Samples/01-Fluent/FluentModels.cs#FluentBasics_ChainOrderMapper)]

[!code-csharp[Fluent 基础链：Map/Ignore/Mask/AfterMapping 连写 · Configure](~/samples/NCMapper.Samples/01-Fluent/FluentModels.cs#FluentBasics_ChainOrderMapper_Configure)]

[!code-csharp[Fluent 基础链：Map/Ignore/Mask/AfterMapping 连写 · ApplyComputedFields](~/samples/NCMapper.Samples/01-Fluent/FluentModels.cs#FluentBasics_ChainOrderMapper_ApplyComputedFields)]

可运行验证：

```bash
cd samples/NCMapper.Samples
dotnet run --scenario 101
```

场景与文档页对照见 [samples 场景映射表](../reference/samples-index.md)。

### 2.3 等价写法：特性通道

同一对映射也可以用 **特性通道**声明：把 `[MapFrom]` 标注直接贴在 DTO 类上，规则随类自描述。两通道能力等价，源生成器对两者生成**完全相同**的映射代码，运行时零差异：

```csharp
// 标注 DTO 即完成声明，无需配置类
[MapFrom(typeof(UserEntity))]
public class UserDto
{
    public int Id { get; set; }
    public string Name { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

两条通道对**同一类型对**只能声明一次（二选一），双侧同时声明编译期报 `NCMAP_F11`。特性全表见 [Attribute 速查表](../reference/attributes-index.md)；跨 API 面选型与逐维度等价关系见 [双 API 面对照](../reference/api-comparison.md)。

### 2.4 常用调用入口

调用入口两通道通用（仍以 2.2 的 `UserEntity → UserDto` 为例）：

```csharp
// 1. 带 Action 回调：映射完成后额外处理
var dto2 = Mapper.Map<UserEntity, UserDto>(user, (s, t) =>
{
    t.Name = t.Name.ToUpper();
});

// 2. 列表映射
var users = new List<UserEntity> { ... };
var dtos = Mapper.MapList<UserEntity, UserDto>(users);

// 3. 更新已有对象（只覆盖被映射的字段）
var existing = new UserDto();
Mapper.Map(user, existing);

// 4. 扩展方法调用入口（源生成器生成的扩展方法，语法更简洁）
//    与 Mapper.Map / MapList 基于同一份源生成器映射代码：
//    Mapper.Map 内部同样优先调用源生成器委托（[ModuleInitializer] 自动注册），
//    未命中才回退表达式树——扩展方法并非"唯一走源生成器"。
//    两者性能基本等价（扩展方法仅少一层委托间接，差异可忽略），按语法偏好选择即可。
//    使用扩展方法需先引用生成的命名空间（Mapper.Map 无需）：
using NCMapper.Generated;  // 仅调用扩展方法时需要
var dto3 = user.MapToUserDto();               // 等价于 Mapper.Map<UserEntity, UserDto>(user)
var dto4 = user.MapToUserDto((s, t) => { });  // 带 Action
var dtos2 = users.MapToUserDtoList();          // 等价于 Mapper.MapList
```

### 2.5 DI 注册

```csharp
services.AddNCMapper();

// 使用
public class MyService
{
    private readonly IMapper _mapper;
    public MyService(IMapper mapper) => _mapper = mapper;
    public void Do() => _mapper.Map<Entity, Dto>(entity);
}
```

---

