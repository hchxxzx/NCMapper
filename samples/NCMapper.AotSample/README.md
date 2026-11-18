# NCMapper AotSample — NativeAOT 部署示例

NCMapper 的 **NativeAOT 最小部署示例**：演示在 AOT 发布环境下，映射如何完整走源生成器路径（编译期生成全部代码，零反射），可执行文件可直接发布为 NativeAOT 并正常运行。

与 [NCMapper.Samples](../NCMapper.Samples/README.md) 刻意分工——Samples 是全功能交互式演示（覆盖 ORM/DI/反射等全部能力，因此**不适合** AOT 发布）；本示例只保留 AOT 兼容的最小面，作为生产项目 AOT 部署的对照基准。

## 运行方式

```bash
# 普通运行（冒烟验证，五入口全部断言通过后进程以 0 退出）
cd samples/NCMapper.AotSample
dotnet run

# NativeAOT 发布（需 Windows SDK + MSVC，参见 NativeAOT 前置要求）
dotnet publish -c Release -r win-x64 --self-contained

# 发布产物直接运行
./bin/Release/net10.0/win-x64/publish/NCMapper.AotSample.exe
```

`PublishAot=true` 已写入项目文件，发布即自动启用 NativeAOT 编译（含裁剪与 AOT 分析）。

## 冒烟验证入口

程序固定执行 5 个核心入口的 AOT 安全用法，任一步骤失败即抛异常并以非零码退出（CI 冒烟检测）：

| 入口 | 验证内容 | 说明 |
|------|---------|------|
| `Mapper.Map` | 单对象映射（Entity → DTO） | DTO 标 `[MapFrom]`，运行时走源生成委托 |
| `Mapper.MapList` | 集合映射 | 源生成器批量路径，零逐元素委托 |
| `Mapper.DeepCopy` | 深拷贝对象隔离 | 源生成器 DeepCopy，AOT 安全 |
| `Mapper.Validate` | 编译期验证结果读取 | 无参版使用源生成器结果，零反射 |
| `Mapper.Compose` | 多源合成（Order + Customer → OrderDetailDto） | 源生成器 `RegisterGeneratedCompose` 委托 |

## AOT 部署三原则

1. **全部 DTO 标注 `[MapFrom]`**：让映射走源生成器路径（编译时生成全部代码），这是 AOT 兼容的基石
2. **避免未标回退路径**：未标注的类型对会回退运行时反射/表达式树编译，NativeAOT 下不可用（编译期会收到 IL 警告）
3. **ORM 扩展注意**：投影路径（`AsDto` → `ToList` 等）AOT 下不可用（无源生成器替代）；降级路径（内存映射）标 `[MapFrom]` 后条件可用

## 模型定义

模型内嵌于 `Program.cs`（自包含设计，零共享依赖）：

| 类型 | 角色 | 说明 |
|------|------|------|
| `UserEntity` / `UserDto` | Map / MapList / DeepCopy 演示 | `UserDto` 标 `[MapFrom(typeof(UserEntity))]` |
| `OrderEntity` / `CustomerEntity` / `OrderDetailDto` | Compose 演示 | `OrderDetailDto` 标 `[MapFrom]` + 三个 `[MapProperty]` 声明多源来源 |

## 相关文档

- AOT / NativeAOT 能力边界与限制：[限制与注意事项 §18.4](../../docs/zh-CN/explanation/limitations.md)
- NativeAOT 部署完整指南：[AOT / NativeAOT 部署](../../docs/zh-CN/how-to/aot/deployment.md)
- 全功能交互式演示：[NCMapper.Samples](../NCMapper.Samples/README.md)
