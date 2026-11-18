using NCMapper.Internal;
using NCMapper.FreeSql;
using Xunit;

namespace NCMapper.IntegrationTests.Models;

/// <summary>
/// T4 行为锁定：FreeSql 降级链导航树收集（集合索引路径）
/// <para>索引路径收集集合导航（首段）；元素内导航子段不收集（FreeSql IncludeByPropertyName 集合内点分路径 NRE 实证）。</para>
/// </summary>
public class IndexNavTreeProbeTests
{
    [Fact]
    public void Probe_CollectionIndexPath_CollectsPostsRootOnly()
    {
        NCMapperFreeSqlClearCache();

        var tree = FreeSqlNavigationCollectorProbe.Collect(typeof(IdxBlog), typeof(IdxBlogDto));
        var paths = tree.FlattenPaths();

        Assert.Contains("Posts", paths);
        Assert.DoesNotContain("Posts.Author", paths); // 元素内导航不收集（FreeSql NRE 实证）
    }

    private static void NCMapperFreeSqlClearCache()
    {
        // FreeSqlNavigationCollector.ClearCache 为 internal —— 经公开入口清理
        NCMapper.Mapper.ClearCache();
    }
}

/// <summary>internal 访问桥（同程序集测试可用 internal）——直接调用收集器</summary>
internal static class FreeSqlNavigationCollectorProbe
{
    public static NavigationTree Collect(System.Type sourceType, System.Type targetType)
        => FreeSqlNavigationCollector.Collect(sourceType, targetType);
}
