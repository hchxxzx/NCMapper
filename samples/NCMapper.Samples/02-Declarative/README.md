# 201 Basic Mapping

Core mapping capabilities of NCMapper: single object, list, Action callback, custom property mapping, bidirectional mapping, source-generated extension methods, reverse direct-call (DTO→Entity, `MapToXxxEntity()`), and reverse custom conversion (`[ConvertBack]`).

## Run

```bash
dotnet run --scenario 201
```

## Documentation

- [操作指南：基础映射](../../../docs/zh-CN/how-to/declarative/basics.md)

## Code

Core logic in `BasicScenarios.cs`. Each static method corresponds to a sub-topic, returning results for the dispatcher to output.
