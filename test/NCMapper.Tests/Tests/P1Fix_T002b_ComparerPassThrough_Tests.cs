// ============================================================================
// P1Fix_T002b — 批次 3 Batch-3b：集合 Comparer 透传
// 验收用例。
//
// 缺陷（006 审计 20260827-005）：CollectionCopier 三处构造
//（CopyDictionary 值类型直拷 ToDictionary / 复杂元素 new Dictionary(count) /
// CopyHashSet 两分支）不透传来源 Comparer——OrdinalIgnoreCase 字典/哈希集合
// 深拷贝后查语义退化为 Ordinal（"abc" miss，静默行为变更）。
//
// 修复：三处透传 source.Comparer（副本等价语义）。
// ============================================================================

using Xunit;

namespace NCMapper.Tests.Tests;

public class P1Fix_T002b_ComparerPassThrough
{
    /// <summary>
    /// A2：OrdinalIgnoreCase 字典深拷贝后 copy["abc"] 命中
    ///（值类型元素直拷分支：ToDictionary 透传 Comparer）。
    /// </summary>
    [Fact]
    public void S10_值元素字典_OrdinalIgnoreCase深拷贝后小写命中()
    {
        var src = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["ABC"] = 42,
        };

        var copy = Mapper.DeepCopy(src)!;

        Assert.Equal(42, copy["abc"]); // 原：Ordinal 下 miss（抛 KeyNotFoundException）
        Assert.Equal(42, copy["ABC"]);
        Assert.Single(copy);
    }

    /// <summary>
    /// A2 补充：复杂元素字典分支（new Dictionary(count, Comparer) 透传）。
    /// </summary>
    [Fact]
    public void S10_复杂元素字典_OrdinalIgnoreCase深拷贝后小写命中()
    {
        var src = new Dictionary<string, S10Item>(StringComparer.OrdinalIgnoreCase)
        {
            ["KEY"] = new S10Item { Name = "v" },
        };

        var copy = Mapper.DeepCopy(src)!;

        Assert.Equal("v", copy["key"].Name); // 原：miss
        Assert.Single(copy);
    }

    /// <summary>
    /// A3：HashSet OrdinalIgnoreCase 深拷贝保查语义
    ///（值类型元素分支 + Contains 命中）。
    /// </summary>
    [Fact]
    public void S10_HashSet_OrdinalIgnoreCase深拷贝保查语义()
    {
        var src = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Hello" };

        var copy = Mapper.DeepCopy(src)!;

        Assert.Contains("HELLO", copy); // 原：Ordinal 下 miss
        Assert.Contains("Hello", copy);
        Assert.Single(copy);
    }

    /// <summary>
    /// 反向锚点：默认 Comparer（Ordinal）行为不变。
    /// </summary>
    [Fact]
    public void S10_默认Comparer_行为不变()
    {
        var dict = new Dictionary<string, int> { ["a"] = 1 };
        var copy = Mapper.DeepCopy(dict)!;
        Assert.Equal(1, copy["a"]);
        Assert.Throws<KeyNotFoundException>(() => _ = copy["A"]); // 大小写敏感保持
    }

    public sealed class S10Item
    {
        public string Name { get; set; } = "";
    }
}