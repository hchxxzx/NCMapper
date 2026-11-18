# 606 全局映射配置

**场景**：`[assembly: MapDefaults]` 全局映射配置 — "逐属性标注 → 全局配置"迁移对照。

## 迁移前（逐属性标注）

每个异名字段用 `[MapProperty]` 逐个声明：

```csharp
[MapProperty(typeof(GlobalCfgUserEntity), "user_name")]
public string UserName { get; set; } = "";
```

- 需要忽略的字段逐个标 `[MapIgnore]`
- 命名约定不一致时每个属性都要手动对齐

## 迁移后（全局配置）

程序集级一次性声明，**所有**映射自动生效：

```csharp
[assembly: MapDefaults(
    NamingStrategy = NamingStrategy.SnakeCase,   // user_name ↔ UserName 自动匹配
    IgnoreNames = new[] { "RowVersion" })]       // 全局忽略（fail-closed）
```

- DTO 零标注（仅 `[MapFrom]`），`user_name` / `phone_number` 自动匹配 `UserName` / `PhoneNumber`
- `RowVersion` 全局忽略，无需逐属性 `[MapIgnore]`
- 三层优先链：属性级标注 > `[assembly: MapDefaults]` > MSBuild 属性

## 运行

```bash
dotnet run --project samples/NCMapper.Samples -- --scenario 606
```

输出两路径映射结果并校验一致性（逐属性标注版 == 全局配置版）。

## 文档

- [操作指南：全局映射配置](../../../docs/zh-CN/how-to/infrastructure/global-config.md)

## 关键 API

| API | 说明 |
|-----|------|
| `[assembly: MapDefaults]` | 程序集级全局配置特性（命名策略/前后缀/忽略清单/方向清单） |
| `NamingStrategy` | 枚举：`Default` / `CaseSensitive` / `SnakeCase` / `UpperSnakeCase` |
| `Mapper.GetGlobalConfig()` | 冻结配置只读视图（调试/日志/断言） |
| `Mapper.RegisterGeneratedConfig` | 源生成器生成的快照注册入口（仅供生成代码调用） |
