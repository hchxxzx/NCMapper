---
title: DI 集成
---

<!-- 来源：NCMapper 主技术文档 §9 DI 集成（结构重组，2026-08-19 章级迁移，内容未重排） -->
## 9. DI 集成

NCMapper 提供标准的 `IMapper` 接口与 `AddNCMapper()` 扩展，可无缝接入 ASP.NET Core / 通用 DI 容器。通过构造函数注入 `IMapper` 替代静态 `Mapper.Map()` 调用，便于单元测试 mock 与服务解耦——`IMapper` 内部零开销转发到静态方法，不引入额外性能成本。

```csharp
// 注册
services.AddNCMapper();

// 使用
public class OrderService
{
    private readonly IMapper _mapper;
    public OrderService(IMapper mapper) => _mapper = mapper;

    public OrderDto GetOrder(OrderEntity entity)
        => _mapper.Map<OrderEntity, OrderDto>(entity);
}
```

- `IMapper` 接口包装静态 `Mapper` 方法，零额外开销转发
- `NCMapperMapper` 以 Singleton 生命周期注册
- 不影响静态 `Mapper.Map()` 的直接调用

---

