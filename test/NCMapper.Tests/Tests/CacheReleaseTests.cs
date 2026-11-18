using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace NCMapper.Tests.Tests;

/// <summary>
/// T3-6: ClearCache() 缓存释放测试 — 验证 WeakReference 在 ClearCache+GC 后断开
/// </summary>
public class CacheReleaseTests
{
    private class CacheEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    private class CacheDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    [Fact]
    public void ClearCache_ClearsCachedEntries()
    {
        // 触发深拷贝——DeepCopyProviderBase.s_hasParameterlessCtor 在深拷贝路径必被填充（GetOrAdd）
        var src = new CacheEntity { Id = 1, Name = "cache" };
        var copy = Mapper.DeepCopy(src);
        Assert.NotNull(copy);
        Assert.Equal(1, copy!.Id);

        // H-11 修复：原测试的 WeakReference 追踪与 Mapper 缓存毫无关联的 new object()，
        // 只证明临时对象被 GC——"ClearCache 释放缓存"从未被验证。
        // 事实确认（5.2）：映射委托本体被 MapperBase<TSource,TTarget> 静态 Lazy 持有（Mapper.cs L303 设计事实：
        // 一经物化不受 ClearCache 影响），不可做 GC 回收断言；反射/表达式缓存对纯属性映射不产生条目。
        // 修复：改测 ClearCache 契约行为——深拷贝路径填充的缓存字典在 ClearCache 后被清空。
        var field = typeof(DeepCopyCompiler).GetField("s_createCache", BindingFlags.NonPublic | BindingFlags.Static)!;
        var cache = (System.Collections.Concurrent.ConcurrentDictionary<Type, Lazy<Delegate?>>)field.GetValue(null)!;

        Assert.NotEmpty(cache); // DeepCopy 已填充缓存条目

        Mapper.ClearCache();

        Assert.Empty(cache);    // ClearCache 后缓存字典为空
    }

    [Fact]
    public void ClearCache_SubsequentMap_StillWorks()
    {
        // 先建立缓存
        var src = new CacheEntity { Id = 42, Name = "before" };
        var dto1 = Mapper.Map<CacheEntity, CacheDto>(src);
        Assert.Equal(42, dto1!.Id);

        // 清空缓存
        Mapper.ClearCache();

        // 清空后重新映射仍正常（重建缓存）
        var dto2 = Mapper.Map<CacheEntity, CacheDto>(src);
        Assert.NotNull(dto2);
        Assert.Equal(42, dto2!.Id);
        Assert.Equal("before", dto2.Name);
    }
}
