---
wiki-id: how-to/aot/deployment
涉及诊断码: NCMAP019
库版本: v1.0.0+
更新时间: 2026-09-15
---

# AOT / NativeAOT 部署

## 概述

NCMapper 采用**源生成器 + 运行时表达式树**双引擎架构，其中源生成器路径**编译期生成全部映射代码**（零反射），因此可以完整发布为 NativeAOT 可执行文件——这是与依赖运行时反射的 AutoMapper 类库的关键差异。

**包级兼容声明**：四个库（NCMapper / EFCore / FreeSql / Dapper）的项目文件均同时声明 `<IsAotCompatible>true</IsAotCompatible>` 与 `<IsTrimmable>true</IsTrimmable>`，启用 trim / AOT 分析时不会因缺失标注而误报。

**可运行对照**：`samples/NCMapper.AotSample` 是最小 AOT 部署示例（`PublishAot=true`，net10.0），全部 DTO 标 `[MapFrom]` 走源生成器路径，冒烟验证 Map / MapList / DeepCopy / Validate / Compose 五个入口后可完整发布运行。

## 快速上手

```bash
cd samples/NCMapper.AotSample
dotnet run                                  # 冒烟验证（五入口断言）
dotnet publish -c Release -r win-x64 --self-contained   # NativeAOT 发布
./bin/Release/net10.0/win-x64/publish/NCMapper.AotSample.exe
```

核心示例源码（单点同源，`dotnet run` 可运行验证）：

[!code-csharp[AOT 入口验证（Map/MapList/DeepCopy/Validate/Compose）](~/samples/NCMapper.AotSample/Program.cs#AotSample_EntryValidation)]

[!code-csharp[AOT 入口验证（Map/MapList/DeepCopy/Validate/Compose） · Program_Main](~/samples/NCMapper.AotSample/Program.cs#AotSample_EntryValidation_Program_Main)]

[!code-csharp[AOT 入口验证（Map/MapList/DeepCopy/Validate/Compose） · Program_Assert](~/samples/NCMapper.AotSample/Program.cs#AotSample_EntryValidation_Program_Assert)]

[!code-csharp[AOT 入口验证（Map/MapList/DeepCopy/Validate/Compose） · UserEntity](~/samples/NCMapper.AotSample/Program.cs#AotSample_EntryValidation_UserEntity)]

[!code-csharp[AOT 入口验证（Map/MapList/DeepCopy/Validate/Compose） · UserEntity_Id](~/samples/NCMapper.AotSample/Program.cs#AotSample_EntryValidation_UserEntity_Id)]

[!code-csharp[AOT 入口验证（Map/MapList/DeepCopy/Validate/Compose） · UserEntity_Name](~/samples/NCMapper.AotSample/Program.cs#AotSample_EntryValidation_UserEntity_Name)]

[!code-csharp[AOT 入口验证（Map/MapList/DeepCopy/Validate/Compose） · UserEntity_Email](~/samples/NCMapper.AotSample/Program.cs#AotSample_EntryValidation_UserEntity_Email)]

[!code-csharp[AOT 入口验证（Map/MapList/DeepCopy/Validate/Compose） · UserDto](~/samples/NCMapper.AotSample/Program.cs#AotSample_EntryValidation_UserDto)]

[!code-csharp[AOT 入口验证（Map/MapList/DeepCopy/Validate/Compose） · UserDto_Id](~/samples/NCMapper.AotSample/Program.cs#AotSample_EntryValidation_UserDto_Id)]

[!code-csharp[AOT 入口验证（Map/MapList/DeepCopy/Validate/Compose） · UserDto_Name](~/samples/NCMapper.AotSample/Program.cs#AotSample_EntryValidation_UserDto_Name)]

[!code-csharp[AOT 入口验证（Map/MapList/DeepCopy/Validate/Compose） · UserDto_Email](~/samples/NCMapper.AotSample/Program.cs#AotSample_EntryValidation_UserDto_Email)]

[!code-csharp[AOT 入口验证（Map/MapList/DeepCopy/Validate/Compose） · OrderEntity](~/samples/NCMapper.AotSample/Program.cs#AotSample_EntryValidation_OrderEntity)]

[!code-csharp[AOT 入口验证（Map/MapList/DeepCopy/Validate/Compose） · OrderEntity_OrderId](~/samples/NCMapper.AotSample/Program.cs#AotSample_EntryValidation_OrderEntity_OrderId)]

[!code-csharp[AOT 入口验证（Map/MapList/DeepCopy/Validate/Compose） · OrderEntity_Amount](~/samples/NCMapper.AotSample/Program.cs#AotSample_EntryValidation_OrderEntity_Amount)]

[!code-csharp[AOT 入口验证（Map/MapList/DeepCopy/Validate/Compose） · CustomerEntity](~/samples/NCMapper.AotSample/Program.cs#AotSample_EntryValidation_CustomerEntity)]

[!code-csharp[AOT 入口验证（Map/MapList/DeepCopy/Validate/Compose） · CustomerEntity_Name](~/samples/NCMapper.AotSample/Program.cs#AotSample_EntryValidation_CustomerEntity_Name)]

[!code-csharp[AOT 入口验证（Map/MapList/DeepCopy/Validate/Compose） · OrderDetailDto](~/samples/NCMapper.AotSample/Program.cs#AotSample_EntryValidation_OrderDetailDto)]

[!code-csharp[AOT 入口验证（Map/MapList/DeepCopy/Validate/Compose） · OrderDetailDto_OrderId](~/samples/NCMapper.AotSample/Program.cs#AotSample_EntryValidation_OrderDetailDto_OrderId)]

[!code-csharp[AOT 入口验证（Map/MapList/DeepCopy/Validate/Compose） · OrderDetailDto_Amount](~/samples/NCMapper.AotSample/Program.cs#AotSample_EntryValidation_OrderDetailDto_Amount)]

[!code-csharp[AOT 入口验证（Map/MapList/DeepCopy/Validate/Compose） · OrderDetailDto_CustomerName](~/samples/NCMapper.AotSample/Program.cs#AotSample_EntryValidation_OrderDetailDto_CustomerName)]

发布需满足 NativeAOT 前置要求（Windows SDK + MSVC / Linux + clang 等，具体见 .NET 官方 NativeAOT 文档）。

## AOT 部署三原则

1. **全部 DTO 标注 `[MapFrom]`**：映射走源生成器路径（编译时生成全部代码），这是 AOT 兼容的基石
2. **避免未标回退路径**：未标注的类型对会回退运行时反射/表达式树编译，NativeAOT 下不可用（编译期收到 IL 警告）
3. **ORM 扩展注意**：投影路径（`AsDto` → `ToList` 等）AOT 下不可用（无源生成器替代）；降级路径（内存映射）标 `[MapFrom]` 后条件可用——**Fluent DTO 同样条件可用**：Fluent 类型对经 RuntimeSnapshot 注册表承载（`NCMapperEmitRuntimeSnapshot` 默认 on），注册表命中免除反射特性读取；off 时回退反射，AOT 下不可用

## API 能力矩阵

| API | AOT 可用性 | 说明 |
|-----|-----------|------|
| `Mapper.Map` / `MapList` | ✅ 可用 | DTO 标 `[MapFrom]` 走源生成委托 |
| `Mapper.Map`（Fluent DTO） | ✅ 可用 | Fluent 类型对经 RuntimeSnapshot 注册表承载（快照默认 on，off 回退反射不可用） |
| `Mapper.DeepCopy` | ✅ 可用（源生成路径） | 运行时表达式树 Provider 路径不可用（有 `[RequiresDynamicCode]` 标注） |
| `Mapper.Validate()`（无参） | ✅ 可用 | 使用源生成器编译期验证结果，零反射；`[Experimental("NCMapper001")]` 为 API 稳定性警告，非 AOT 限制 |
| `Mapper.Compose` | ✅ 条件可用 | 源生成器已注册 `RegisterGeneratedCompose` 委托时可用；回退运行时 `MakeGenericType` 路径不可用 |
| `Mapper.Validate(Assembly[])` | ❌ 不可用 | 反射扫描程序集，标注 `[RequiresDynamicCode]` + `[RequiresUnreferencedCode]` |
| ORM 投影（`AsDto` → `ToList`/`ToChunkDto` 等） | ❌ 不可用 | `BuildProjection` 用 `MakeGenericMethod` 构建 SQL 投影表达式树，无源生成器替代（`RegisterGenerated*` 七种不含 Projection） |
| ORM 降级路径（内存映射） | ✅ 条件可用 | 标 `[MapFrom]` 走源生成器委托；依赖 ORM 运行时自身的 AOT 兼容性 |
| ORM 降级路径（Fluent DTO） | ✅ 条件可用 | 经 RuntimeSnapshot 注册表（同上）——纯可翻译声明投影/降级判定读注册表明细，off 态保守降级回退反射 |
| Dapper `Query` 委托 | ❌ 不可用 | 委托编译用 `Expression.Compile()`；映射部分标 `[MapFrom]` 走源生成器（AOT 安全） |

> 完整边界见 [限制与注意事项 §18.4](../../explanation/limitations.md)。

## 运行时回退行为

未标 `[MapFrom]` 的映射对在**普通运行**下会静默回退运行时反射/表达式树路径；但在 AOT 发布下该路径被裁剪或不可执行，编译期即收到 `IL2026` / `IL3050` 警告。源生成器对 7 处已知安全回退调用点（Compose Pipeline 列表降级、嵌套映射回退、init-only 回退、无参构造器回退、集合元素回退、DeepCopy 循环引用回退、DeepCopy 编译失败兜底）已加精确 `#pragma warning disable IL2026, IL3050` + 原因注释，AotSample 构建零警告。

## 裁剪与守护

- **trim 声明**：四库 `<IsTrimmable>true</IsTrimmable>` 与 `<IsAotCompatible>true</IsAotCompatible>` 并存，无遮蔽标注的完整包级兼容声明。
- **AOT 反射守护**：`tools/AotReflectionGuard` 在 CI build 阶段以 MetadataLoadContext + PEReader 双通道扫描三个 ORM 扩展包的公开方法体 IL，发现「含反射调用但无 `Requires*` 标注」即 CI 失败，确保 AOT 反射约束（24 项）不静默断裂。
- **测试防线**：`test/NCMapper.AotTests`（AOT 编译兼容性验证，同 slnx 纳入）与 CI samples job 的 AotSample 构建（`--warnaserror` 零警告门禁）+ 冒烟运行双保险。

## 诊断规则

| 诊断 | 严重度 | 触发条件 |
|------|--------|----------|
| `IL2026` / `IL3050` | Warning | 映射对未标 `[MapFrom]` 走运行时回退路径，或调用 AOT 不兼容 API（AOT 发布时编译期暴露） |
| NCMAP019 | Info | DTO 命名模式（类型名以 `Dto`/`DTO` 结尾）但未标 `[MapFrom]` 时提示——AOT/Trimmed 场景下运行时映射将回退反射路径 |

## 相关资源

- 可运行示例：[samples/NCMapper.AotSample](~/samples/NCMapper.AotSample/README.md)
- 全功能演示（非 AOT 面）：[samples/NCMapper.Samples](~/samples/NCMapper.Samples/README.md)
- [限制与注意事项 §18.4](../../explanation/limitations.md)
- 测试项目：`test/NCMapper.AotTests`
