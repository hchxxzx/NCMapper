// ============================================================================
// FreeSql 行为红测
//（C-04 ApplyFreeSqlIncludes 原地改写污染用户声明判定 / C-08① -1 短路静默 /
//  C-14 伪异步 CT 契约锁定 / C-23 文案订正无行为面免红测）
//
// 修复前形态：标注【现状红】的用例必红；修复 commit 后全部转绿；回退任一字态 → 复红。
// ============================================================================

using System.Reflection;
using FreeSql;
using FreeSql.DataAnnotations;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.FreeSql;
using Xunit;

namespace NCMapper.IntegrationTests;

/// <summary>C-04 承载实体：主表（集合导航 Items，触发自动 Include + 降级路径）</summary>
public class T64B7Order
{
    public int Id { get; set; }
    public string No { get; set; } = "";

    [Navigate(nameof(T64B7Item.OrderId))]
    public List<T64B7Item> Items { get; set; } = new();
}

/// <summary>C-04 承载实体：子表</summary>
public class T64B7Item
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public string Sku { get; set; } = "";
}

/// <summary>C-04 降级承载 DTO（[Mask] 保底触发降级 + 集合导航递归映射）</summary>
[MapFrom(typeof(T64B7Order))]
public class T64B7OrderDto
{
    public int Id { get; set; }
    public string No { get; set; } = "";

    [Mask(typeof(MaskPhone))]
    public string SkuMaskHolder { get; set; } = "";

    public List<T64B7ItemDto> Items { get; set; } = new();
}

/// <summary>C-04 子表 DTO</summary>
[MapFrom(typeof(T64B7Item))]
public class T64B7ItemDto
{
    public int Id { get; set; }
    public string Sku { get; set; } = "";
}

/// <summary>C-14 树承载实体（自引用集合导航）</summary>
public class T64B7Node
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int? ParentId { get; set; }

    [Navigate(nameof(ParentId))]
    public List<T64B7Node> Children { get; set; } = new();
}

/// <summary>C-14 树 DTO</summary>
[MapFrom(typeof(T64B7Node))]
public class T64B7NodeDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<T64B7NodeDto> Children { get; set; } = new();
}

[Collection("DapperGlobalState")]
public class T64Batch7RedTests
{
    private static (IFreeSql Fsql, string DbPath) CreateFsql()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"ncmapper_b7_{Guid.NewGuid():N}.db");
        var fsql = new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, $"Data Source={dbPath}")
            .UseAutoSyncStructure(true)
            .Build();
        return (fsql, dbPath);
    }

    private static (System.IO.StringWriter Sw, Action Restore) CaptureTrace()
    {
        var sw = new System.IO.StringWriter();
        var oldEnabled = MapTracer.GloballyEnabled;
        var oldOutput = MapTracer.GlobalOutput;
        MapTracer.GloballyEnabled = true;
        MapTracer.GlobalOutput = sw;
        return (sw, () => { MapTracer.GloballyEnabled = oldEnabled; MapTracer.GlobalOutput = oldOutput; });
    }

    // ==================================================================
    // C-04：同一 selector 二次终端调用——首次自动 Include 不得被误判为用户已声明
    // ==================================================================

    /// <summary>【现状红】首次 ToListAsync 自动 Include Items 后，第二次同 selector 调用读到被污染的
    /// _includeInfo → trace 含"跳过自动 Include（用户已声明）"且无自动 Include 记录。</summary>
    [Fact]
    public async Task C04_同一selector二次终端调用_首次自动Include不被误判为用户已声明()
    {
        var (fsql, dbPath) = CreateFsql();
        try
        {
            fsql.Insert(new T64B7Order { Id = 1, No = "A", Items = { new() { Id = 1, OrderId = 1, Sku = "s1" } } })
                .ExecuteAffrows();
            fsql.Insert(new T64B7Item { Id = 1, OrderId = 1, Sku = "s1" }).ExecuteAffrows();

            var selector = fsql.Select<T64B7Order>().AsDto();

            var (sw1, restore1) = CaptureTrace();
            List<T64B7OrderDto>? first;
            try { first = await selector.ToListAsync<T64B7OrderDto>(); }
            finally { restore1(); }
            Assert.NotNull(first);
            Assert.Contains("自动 Include", sw1.ToString()); // 首次确有自动 Include

            var (sw2, restore2) = CaptureTrace();
            List<T64B7OrderDto>? second;
            try { second = await selector.ToListAsync<T64B7OrderDto>(); }
            finally { restore2(); }
            Assert.NotNull(second);

            // 修复后：第二次仍应正常自动 Include（基线快照不受首次自动写入污染）
            Assert.DoesNotContain("跳过自动 Include", sw2.ToString());
            Assert.Contains("自动 Include: Items", sw2.ToString());
        }
        finally { fsql.Dispose(); try { File.Delete(dbPath); } catch (IOException) { } }
    }

    // ==================================================================
    // C-08①：反射注入坏字段 → 首次告警可见（行为锁定）+ -1 短路后二次调用会话级告警
    // ==================================================================

    private static readonly FieldInfo IncludeFieldCacheField = typeof(FreeSqlDtoSelector<T64B7Order>)
        .GetField("s_includeFieldCache", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly FieldInfo IncludeReflectionStateField = typeof(NCMapperFreeSqlExtensions)
        .GetField("IncludeReflectionState", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static System.Collections.Concurrent.ConcurrentDictionary<Type, FieldInfo?> GetFieldCache()
        => (System.Collections.Concurrent.ConcurrentDictionary<Type, FieldInfo?>)IncludeFieldCacheField.GetValue(null)!;

    private static int ReadReflectionState() => (int)IncludeReflectionStateField.GetValue(null)!;
    private static void WriteReflectionState(int value) => IncludeReflectionStateField.SetValue(null, value);

    /// <summary>行为锁定（现状绿）：注入坏字段（field==null）→ 首次调用输出"未找到 FreeSql 内部字段"一次性告警。</summary>
    [Fact]
    public void C08_注入坏字段_首次告警可见_行为锁定()
    {
        var (fsql, dbPath) = CreateFsql();
        var cache = GetFieldCache();
        var selectType = fsql.Select<T64B7Order>().GetType();
        cache[selectType] = null; // 注入：探测结果为 null（字段未找到形态）
        var oldState = ReadReflectionState();
        try
        {
            Assert.Equal(0, oldState); // 前置：本用例独占态
            var (sw, restore) = CaptureTrace();
            try
            {
                var selector = fsql.Select<T64B7Order>().AsDto();
                _ = selector.ToList<T64B7OrderDto>();
                Assert.Contains("未找到 FreeSql 内部字段", sw.ToString());
            }
            finally { restore(); }
        }
        finally
        {
            cache.TryRemove(selectType, out _);
            WriteReflectionState(0);
            fsql.Dispose(); try { File.Delete(dbPath); } catch (IOException) { }
        }
    }

    /// <summary>【现状红】-1 短路后二次调用：快速路径完全静默（零告警）——修复后补一次性会话级告警。</summary>
    [Fact]
    public void C08_短路后二次调用_会话级告警可见()
    {
        var (fsql, dbPath) = CreateFsql();
        var cache = GetFieldCache();
        var selectType = fsql.Select<T64B7Order>().GetType();
        cache[selectType] = null;
        var oldState = ReadReflectionState();
        try
        {
            Assert.Equal(0, oldState); // 前置：本用例独占态
            var selector = fsql.Select<T64B7Order>().AsDto();

            var (sw1, restore1) = CaptureTrace();
            try { _ = selector.ToList<T64B7OrderDto>(); }
            finally { restore1(); }
            Assert.Equal(-1, ReadReflectionState()); // 首次已置 -1

            var (sw2, restore2) = CaptureTrace();
            try { _ = selector.ToList<T64B7OrderDto>(); }
            finally { restore2(); }

            // 修复后：-1 短路路径不再完全静默——补一次性会话级告警
            Assert.Contains("已标记不可用", sw2.ToString());
        }
        finally
        {
            cache.TryRemove(selectType, out _);
            WriteReflectionState(0);
            fsql.Dispose(); try { File.Delete(dbPath); } catch (IOException) { }
        }
    }

    // ==================================================================
    // C-14：伪异步 CT 契约锁定（已取消 CT 入口即抛）
    // ==================================================================

    /// <summary>行为锁定（现状绿）：ToTreeListAsync 传已取消 CT → 入口 ThrowIfCancellationRequested 即抛。</summary>
    [Fact]
    public async Task C14_已取消CT_入口即抛_行为锁定()
    {
        var (fsql, dbPath) = CreateFsql();
        try
        {
            fsql.Insert(new T64B7Node { Id = 1, Name = "root" }).ExecuteAffrows();
            var selector = fsql.Select<T64B7Node>().AsDto();
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => selector.ToTreeListAsync<T64B7NodeDto>(cts.Token));
        }
        finally { fsql.Dispose(); try { File.Delete(dbPath); } catch (IOException) { } }
    }
}
