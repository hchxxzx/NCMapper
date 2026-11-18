using System.Collections.Concurrent;
using FsCheck.Xunit;
using NCMapper.FuzzTests.Models;

namespace NCMapper.FuzzTests;

/// <summary>
/// Concurrent 集合属性测试 — ConcurrentDictionary/ConcurrentQueue/ConcurrentStack 在 DeepCopy 下的不变式
/// <para>覆盖路径：并发字典内容保留、并发队列 FIFO 顺序、并发栈 LIFO 顺序</para>
/// <para>每个 [Property] 默认运行 100 次随机输入，失败时自动 shrinking 到最小复现</para>
/// </summary>
public class ConcurrentCollectionProperties
{
    // ================================================================
    // ConcurrentDictionary — 并发字典
    // ================================================================

    /// <summary>
    /// DeepCopy 保留 ConcurrentDictionary 内容 — 任意输入下深拷贝后键值对一致
    /// </summary>
    [Property]
    public bool DeepCopy_ConcurrentDictionary_Preserves_Content(int id, string[] keys, int[] values)
    {
        var dict = BuildConcurrentDictionary(keys, values);
        var entity = new FuzzConcurrentCollectionEntity { Id = id, Counts = dict };
        var copy = Mapper.DeepCopy(entity)!;
        return copy.Counts is not null
            && copy.Counts!.Count == dict.Count
            && dict.All(kv => copy.Counts.TryGetValue(kv.Key, out var v) && v == kv.Value);
    }

    /// <summary>
    /// DeepCopy 创建 ConcurrentDictionary 独立引用 — 副本与原件不是同一实例
    /// </summary>
    [Property]
    public bool DeepCopy_ConcurrentDictionary_Creates_Independent_Instance(string[] keys, int[] values)
    {
        var dict = BuildConcurrentDictionary(keys, values);
        var entity = new FuzzConcurrentCollectionEntity { Counts = dict };
        var copy = Mapper.DeepCopy(entity)!;
        return copy.Counts is not null
            && entity.Counts is not null
            && !ReferenceEquals(copy.Counts, entity.Counts);
    }

    // ================================================================
    // ConcurrentQueue — 并发队列（FIFO）
    // ================================================================

    /// <summary>
    /// DeepCopy 保留 ConcurrentQueue 内容和 FIFO 顺序 — 任意输入下深拷贝后元素和顺序一致
    /// </summary>
    [Property]
    public bool DeepCopy_ConcurrentQueue_Preserves_Order(int id, int[] elements)
    {
        var queue = new ConcurrentQueue<int>(elements);
        var entity = new FuzzConcurrentCollectionEntity { Id = id, TaskQueue = queue };
        var copy = Mapper.DeepCopy(entity)!;
        return copy.TaskQueue is not null
            && copy.TaskQueue!.SequenceEqual(queue);
    }

    // ================================================================
    // ConcurrentStack — 并发栈（LIFO）
    // ================================================================

    /// <summary>
    /// DeepCopy 保留 ConcurrentStack 内容和 LIFO 顺序 — 任意输入下深拷贝后元素和顺序一致
    /// </summary>
    [Property]
    public bool DeepCopy_ConcurrentStack_Preserves_Order(int id, int[] elements)
    {
        var stack = new ConcurrentStack<int>(elements);
        var entity = new FuzzConcurrentCollectionEntity { Id = id, HistoryStack = stack };
        var copy = Mapper.DeepCopy(entity)!;
        return copy.HistoryStack is not null
            && copy.HistoryStack!.SequenceEqual(stack);
    }

    // ================================================================
    // null 集合守卫
    // ================================================================

    /// <summary>
    /// DeepCopy null 并发集合返回 null — 三种集合均为 null 时深拷贝后仍为 null
    /// </summary>
    [Property]
    public bool DeepCopy_Null_Concurrent_Collections_Return_Null(int id)
    {
        var entity = new FuzzConcurrentCollectionEntity { Id = id };
        var copy = Mapper.DeepCopy(entity)!;
        return copy.Id == id
            && copy.Counts is null
            && copy.TaskQueue is null
            && copy.HistoryStack is null;
    }

    // ================================================================
    // 辅助方法
    // ================================================================

    /// <summary>
    /// 从 keys 和 values 数组构建 ConcurrentDictionary — 重复 key 后值覆盖
    /// </summary>
    /// <param name="keys">键数组</param>
    /// <param name="values">值数组</param>
    /// <returns>构建的并发字典</returns>
    private static ConcurrentDictionary<string, int> BuildConcurrentDictionary(string[] keys, int[] values)
    {
        var dict = new ConcurrentDictionary<string, int>();
        var len = Math.Min(keys.Length, values.Length);
        for (int i = 0; i < len; i++)
            dict[keys[i]] = values[i];
        return dict;
    }
}
