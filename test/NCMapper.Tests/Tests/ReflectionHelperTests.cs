using System.Reflection;
using Xunit;

namespace NCMapper.Tests.Tests;

/// <summary>
/// T3-2: ReflectionHelper 独立单测 — 正向+异常+缓存
/// </summary>
public class ReflectionHelperTests
{
    private class Sample
    {
        public int Value { get; set; }
        public Sample() { }
        public Sample(int value) { Value = value; }
        public string GetName() => "sample";
        public int Add(int a, int b) => a + b;
        private static void Hidden() { }
    }

    [Fact]
    public void GetRequiredMethod_NoArgs_Found()
    {
        var method = ReflectionHelper.GetRequiredMethod(typeof(Sample), "GetName");
        Assert.Equal("GetName", method.Name);
        Assert.Equal(typeof(string), method.ReturnType);
    }

    [Fact]
    public void GetRequiredMethod_NoArgs_NotFound_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ReflectionHelper.GetRequiredMethod(typeof(Sample), "NonExistent"));
        Assert.Contains("NonExistent", ex.Message);
    }

    [Fact]
    public void GetRequiredMethod_WithArgs_Found()
    {
        var method = ReflectionHelper.GetRequiredMethod(typeof(Sample), "Add", new[] { typeof(int), typeof(int) });
        Assert.Equal("Add", method.Name);
        Assert.Equal(2, method.GetParameters().Length);
    }

    [Fact]
    public void GetRequiredMethod_WithArgs_WrongSignature_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => ReflectionHelper.GetRequiredMethod(typeof(Sample), "Add", new[] { typeof(string) }));
    }

    [Fact]
    public void GetRequiredMethod_BindingFlags_NonPublicStatic()
    {
        var method = ReflectionHelper.GetRequiredMethod(typeof(Sample), "Hidden",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.Equal("Hidden", method.Name);
        Assert.True(method.IsPrivate);
    }

    [Fact]
    public void GetRequiredMethod_BindingFlags_NotFound_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => ReflectionHelper.GetRequiredMethod(typeof(Sample), "GetName",
                BindingFlags.NonPublic | BindingFlags.Static));
    }

    [Fact]
    public void GetRequiredProperty_Found()
    {
        var prop = ReflectionHelper.GetRequiredProperty(typeof(Sample), "Value");
        Assert.Equal("Value", prop.Name);
        Assert.Equal(typeof(int), prop.PropertyType);
    }

    [Fact]
    public void GetRequiredProperty_NotFound_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ReflectionHelper.GetRequiredProperty(typeof(Sample), "Missing"));
        Assert.Contains("Missing", ex.Message);
    }

    [Fact]
    public void GetRequiredConstructor_Found()
    {
        var ctor = ReflectionHelper.GetRequiredConstructor(typeof(Sample), new[] { typeof(int) });
        Assert.Single(ctor.GetParameters());
    }

    [Fact]
    public void GetRequiredConstructor_NotFound_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => ReflectionHelper.GetRequiredConstructor(typeof(Sample), new[] { typeof(string) }));
    }

    [Fact]
    public void ClearCache_ThenReacquire_Works()
    {
        // 先触发缓存
        var m1 = ReflectionHelper.GetRequiredMethod(typeof(Sample), "GetName");
        ReflectionHelper.ClearCache();
        // 清空后重新获取仍正常
        var m2 = ReflectionHelper.GetRequiredMethod(typeof(Sample), "GetName");
        Assert.Equal(m1.Name, m2.Name);
    }

    [Fact]
    public void CacheHit_SameInstance_Returned()
    {
        var m1 = ReflectionHelper.GetRequiredMethod(typeof(Sample), "GetName");
        var m2 = ReflectionHelper.GetRequiredMethod(typeof(Sample), "GetName");
        Assert.Same(m1, m2); // 缓存命中应为同一 MethodInfo 实例
    }
}
