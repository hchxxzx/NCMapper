> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
title: DI Integration
---

<!-- Source: NCMapper Main Technical Documentation §9 DI Integration (structural reorganization, chapter-level migration on 2026-08-19; content not reordered) -->

## 9. DI Integration

NCMapper provides the standard `IMapper` interface and the `AddNCMapper()` extension method to seamlessly integrate with ASP.NET Core / generic DI containers. By injecting `IMapper` via constructor injection instead of calling static `Mapper.Map()`, you enable easier unit testing (e.g., mocking) and service decoupling—`IMapper` internally forwards calls to static methods with zero overhead, introducing no additional performance cost.

```csharp
// Registration
services.AddNCMapper();

// Usage
public class OrderService
{
    private readonly IMapper _mapper;
    public OrderService(IMapper mapper) => _mapper = mapper;

    public OrderDto GetOrder(OrderEntity entity)
        => _mapper.Map<OrderEntity, OrderDto>(entity);
}
```

- The `IMapper` interface wraps static `Mapper` methods and forwards calls with zero additional overhead.
- `NCMapperMapper` is registered with Singleton lifetime.
- Direct calls to static `Mapper.Map()` remain unaffected.
