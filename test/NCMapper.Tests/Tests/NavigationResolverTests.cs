using System.Reflection;
using Xunit;

namespace NCMapper.Tests.Tests;

/// <summary>
/// T3-1: NavigationResolver 独立单测 — 覆盖全部分支
/// </summary>
public class NavigationResolverTests
{
    // 测试用类型
    private class Order { public int Id { get; set; } }
    private class Customer { public int Id { get; set; } }
    private class OrderDetail { public int Id { get; set; } }

    private class SourceWithRef
    {
        public int Id { get; set; }
        public Customer? Customer { get; set; }
        public Order? Order { get; set; }
    }

    private class SourceWithCollection
    {
        public int Id { get; set; }
        public List<Order> Orders { get; set; } = new();
        public Order[] OrderArray { get; set; } = Array.Empty<Order>();
        public ICollection<OrderDetail> Details { get; set; } = new List<OrderDetail>();
    }

    private class SourceWithDict
    {
        public int Id { get; set; }
        public Dictionary<string, Order> OrderDict { get; set; } = new();
    }

    private class CustomOrders : List<Order> { }

    private class SourceWithInheritedCollection
    {
        public int Id { get; set; }
        public CustomOrders Orders { get; set; } = new();
    }

    private class SourceWithMultiRef
    {
        public int Id { get; set; }
        public Customer? PrimaryCustomer { get; set; }
        public Customer? SecondaryCustomer { get; set; }
    }

    private static List<PropertyInfo> GetProps<T>() =>
        typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance).ToList();

    // === ResolveNavigationProperty ===

    [Fact]
    public void Resolve_ExactTypeAndName_Matches()
    {
        var props = GetProps<SourceWithRef>();
        var result = NavigationResolver.ResolveNavigationProperty(props, typeof(Customer), "SourceWithRef");
        Assert.NotNull(result);
        Assert.Equal("Customer", result!.Name);
    }

    [Fact]
    public void Resolve_TypeOnly_NoName_MatchesFirst()
    {
        var props = GetProps<SourceWithRef>();
        var result = NavigationResolver.ResolveNavigationProperty(props, typeof(Customer), "SourceWithRef");
        Assert.NotNull(result);
        Assert.Equal(typeof(Customer), result!.PropertyType);
    }

    [Fact]
    public void Resolve_NotFound_ReturnsNull()
    {
        var props = GetProps<SourceWithRef>();
        var result = NavigationResolver.ResolveNavigationProperty(props, typeof(OrderDetail), "SourceWithRef");
        Assert.Null(result);
    }

    [Fact]
    public void Resolve_CollectionNavigation_MatchesElementType()
    {
        var props = GetProps<SourceWithCollection>();
        var result = NavigationResolver.ResolveNavigationProperty(props, typeof(Order), "SourceWithCollection");
        Assert.NotNull(result);
        // List<Order> 或 Order[] 都匹配，取第一个
        Assert.True(result!.Name is "Orders" or "OrderArray");
    }

    [Fact]
    public void Resolve_CollectionWithExactName()
    {
        var props = GetProps<SourceWithCollection>();
        var result = NavigationResolver.ResolveNavigationProperty(props, typeof(OrderDetail), "SourceWithCollection");
        Assert.NotNull(result);
        Assert.Equal("Details", result!.Name);
    }

    [Fact]
    public void Resolve_MultiSameTypeNav_TakesFirstAndTraces()
    {
        var props = GetProps<SourceWithMultiRef>();
        using var sw = new System.IO.StringWriter();
        var prevEnabled = MapTracer.GloballyEnabled;
        var prevOutput = MapTracer.GlobalOutput;
        MapTracer.GloballyEnabled = true;
        MapTracer.GlobalOutput = sw;
        try
        {
            // 同类型多导航：纯类型匹配取第一个（按反射顺序），并输出 Trace 提示
            var result = NavigationResolver.ResolveNavigationProperty(props, typeof(Customer), "SourceWithMultiRef");
            Assert.NotNull(result);
            Assert.Equal("PrimaryCustomer", result!.Name);

            // Trace 三要素：源类型名 / 匹配数量 / 选取属性名
            var output = sw.ToString();
            Assert.Contains("SourceWithMultiRef", output);
            Assert.Contains("存在 2 个类型", output);
            Assert.Contains("已选取第一个：PrimaryCustomer", output);
        }
        finally
        {
            MapTracer.GloballyEnabled = prevEnabled;
            MapTracer.GlobalOutput = prevOutput;
        }
    }

    // === GetCollectionElementType ===

    [Fact]
    public void GetElementType_String_ReturnsNull()
    {
        Assert.Null(NavigationResolver.GetCollectionElementType(typeof(string)));
    }

    [Fact]
    public void GetElementType_Array_ReturnsElement()
    {
        Assert.Equal(typeof(Order), NavigationResolver.GetCollectionElementType(typeof(Order[])));
    }

    [Fact]
    public void GetElementType_GenericList_ReturnsElement()
    {
        Assert.Equal(typeof(Order), NavigationResolver.GetCollectionElementType(typeof(List<Order>)));
    }

    [Fact]
    public void GetElementType_ICollection_ReturnsElement()
    {
        Assert.Equal(typeof(OrderDetail), NavigationResolver.GetCollectionElementType(typeof(ICollection<OrderDetail>)));
    }

    [Fact]
    public void GetElementType_Dictionary_ReturnsNull()
    {
        Assert.Null(NavigationResolver.GetCollectionElementType(typeof(Dictionary<string, Order>)));
    }

    [Fact]
    public void GetElementType_InheritedGenericCollection_ReturnsElement()
    {
        Assert.Equal(typeof(Order), NavigationResolver.GetCollectionElementType(typeof(CustomOrders)));
    }

    [Fact]
    public void GetElementType_NonCollection_ReturnsNull()
    {
        Assert.Null(NavigationResolver.GetCollectionElementType(typeof(int)));
        Assert.Null(NavigationResolver.GetCollectionElementType(typeof(Customer)));
    }
}
