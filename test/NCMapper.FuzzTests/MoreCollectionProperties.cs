using FsCheck.Xunit;
using NCMapper.FuzzTests.Models;

namespace NCMapper.FuzzTests;

/// <summary>
/// 更多集合类型属性测试 — Queue/Stack/SortedSet/LinkedList 在 DeepCopy 和 Map 下的不变式
/// <para>直接呼应 CS8628 修复：这些可空集合类型曾生成非法 new T?() 语法</para>
/// <para>每个 [Property] 默认运行 100 次随机输入，失败时自动 shrinking 到最小复现</para>
/// </summary>
public class MoreCollectionProperties
{
    // ================================================================
    // Queue — FIFO 有序队列
    // ================================================================

    /// <summary>
    /// DeepCopy 保留 Queue 内容和 FIFO 顺序 — 任意输入下深拷贝后元素和出队顺序一致
    /// </summary>
    [Property]
    public bool DeepCopy_Queue_Preserves_Content_And_Order(int id, int[] elements)
    {
        var queue = new Queue<int>(elements);
        var entity = new FuzzExoticCollectionEntity { Id = id, TaskQueue = queue };
        var copy = Mapper.DeepCopy(entity)!;
        return copy.TaskQueue is not null
            && copy.TaskQueue!.SequenceEqual(queue);
    }

    /// <summary>
    /// DeepCopy 创建 Queue 独立副本 — 向副本入队不影响原件
    /// </summary>
    [Property]
    public bool DeepCopy_Queue_Creates_Independent_Instance(int[] elements)
    {
        var queue = new Queue<int>(elements);
        var entity = new FuzzExoticCollectionEntity { TaskQueue = queue };
        var originalCount = elements.Length;
        var copy = Mapper.DeepCopy(entity)!;
        if (copy.TaskQueue is null) return false;
        copy.TaskQueue.Enqueue(int.MaxValue);
        return entity.TaskQueue!.Count == originalCount;
    }

    // ================================================================
    // Stack — LIFO 有序栈
    // ================================================================

    /// <summary>
    /// DeepCopy 保留 Stack 内容和 LIFO 顺序 — 任意输入下深拷贝后元素和出栈顺序一致
    /// </summary>
    [Property]
    public bool DeepCopy_Stack_Preserves_Content_And_Order(int id, int[] elements)
    {
        var stack = new Stack<int>(elements);
        var entity = new FuzzExoticCollectionEntity { Id = id, HistoryStack = stack };
        var copy = Mapper.DeepCopy(entity)!;
        return copy.HistoryStack is not null
            && copy.HistoryStack!.SequenceEqual(stack);
    }

    /// <summary>
    /// DeepCopy 创建 Stack 独立副本 — 向副本压栈不影响原件
    /// </summary>
    [Property]
    public bool DeepCopy_Stack_Creates_Independent_Instance(int[] elements)
    {
        var stack = new Stack<int>(elements);
        var entity = new FuzzExoticCollectionEntity { HistoryStack = stack };
        var originalCount = elements.Length;
        var copy = Mapper.DeepCopy(entity)!;
        if (copy.HistoryStack is null) return false;
        copy.HistoryStack.Push(int.MaxValue);
        return entity.HistoryStack!.Count == originalCount;
    }

    // ================================================================
    // SortedSet — 自动排序去重集合
    // ================================================================

    /// <summary>
    /// DeepCopy 保留 SortedSet 内容 — 任意输入下深拷贝后元素集合一致（顺序由排序保证）
    /// </summary>
    [Property]
    public bool DeepCopy_SortedSet_Preserves_Content(int id, int[] elements)
    {
        var sortedSet = new SortedSet<int>(elements);
        var entity = new FuzzExoticCollectionEntity { Id = id, SortedNumbers = sortedSet };
        var copy = Mapper.DeepCopy(entity)!;
        return copy.SortedNumbers is not null
            && copy.SortedNumbers!.SetEquals(sortedSet);
    }

    /// <summary>
    /// DeepCopy 创建 SortedSet 独立引用 — 副本与原件不是同一实例
    /// </summary>
    [Property]
    public bool DeepCopy_SortedSet_Creates_Independent_Instance(int[] elements)
    {
        var sortedSet = new SortedSet<int>(elements);
        var entity = new FuzzExoticCollectionEntity { SortedNumbers = sortedSet };
        var copy = Mapper.DeepCopy(entity)!;
        return copy.SortedNumbers is not null
            && entity.SortedNumbers is not null
            && !ReferenceEquals(copy.SortedNumbers, entity.SortedNumbers);
    }

    // ================================================================
    // LinkedList — 双向链表，保持插入顺序
    // ================================================================

    /// <summary>
    /// DeepCopy 保留 LinkedList 内容和顺序 — 任意输入下深拷贝后元素和链表顺序一致
    /// </summary>
    [Property]
    public bool DeepCopy_LinkedList_Preserves_Content_And_Order(int id, int[] elements)
    {
        var linkedList = new LinkedList<int>(elements);
        var entity = new FuzzExoticCollectionEntity { Id = id, LinkedNumbers = linkedList };
        var copy = Mapper.DeepCopy(entity)!;
        return copy.LinkedNumbers is not null
            && copy.LinkedNumbers!.SequenceEqual(linkedList);
    }

    /// <summary>
    /// DeepCopy 创建 LinkedList 独立副本 — 向副本添加节点不影响原件
    /// </summary>
    [Property]
    public bool DeepCopy_LinkedList_Creates_Independent_Instance(int[] elements)
    {
        var linkedList = new LinkedList<int>(elements);
        var entity = new FuzzExoticCollectionEntity { LinkedNumbers = linkedList };
        var originalCount = elements.Length;
        var copy = Mapper.DeepCopy(entity)!;
        if (copy.LinkedNumbers is null) return false;
        copy.LinkedNumbers.AddLast(int.MaxValue);
        return entity.LinkedNumbers!.Count == originalCount;
    }

    // ================================================================
    // null 集合守卫
    // ================================================================

    /// <summary>
    /// DeepCopy null 集合返回 null — 四种集合均为 null 时深拷贝后仍为 null
    /// </summary>
    [Property]
    public bool DeepCopy_Null_Exotic_Collections_Return_Null(int id)
    {
        var entity = new FuzzExoticCollectionEntity { Id = id };
        var copy = Mapper.DeepCopy(entity)!;
        return copy.Id == id
            && copy.TaskQueue is null
            && copy.HistoryStack is null
            && copy.SortedNumbers is null
            && copy.LinkedNumbers is null;
    }

    // ================================================================
    // Map — 集合映射一致性
    // ================================================================

    /// <summary>
    /// Map 保留 Queue 内容和顺序 — 映射后元素和 FIFO 顺序一致
    /// </summary>
    [Property]
    public bool Map_Queue_Preserves_Content(int id, int[] elements)
    {
        var queue = new Queue<int>(elements);
        var entity = new FuzzExoticCollectionEntity { Id = id, TaskQueue = queue };
        var dto = Mapper.Map<FuzzExoticCollectionEntity, FuzzExoticCollectionDto>(entity)!;
        return dto.TaskQueue is not null
            && dto.TaskQueue!.SequenceEqual(queue);
    }

    /// <summary>
    /// Map 保留 Stack 内容和顺序 — 映射后元素和 LIFO 顺序一致
    /// </summary>
    [Property]
    public bool Map_Stack_Preserves_Content(int id, int[] elements)
    {
        var stack = new Stack<int>(elements);
        var entity = new FuzzExoticCollectionEntity { Id = id, HistoryStack = stack };
        var dto = Mapper.Map<FuzzExoticCollectionEntity, FuzzExoticCollectionDto>(entity)!;
        return dto.HistoryStack is not null
            && dto.HistoryStack!.SequenceEqual(stack);
    }

    /// <summary>
    /// Map 保留 SortedSet 内容 — 映射后元素集合一致
    /// </summary>
    [Property]
    public bool Map_SortedSet_Preserves_Content(int id, int[] elements)
    {
        var sortedSet = new SortedSet<int>(elements);
        var entity = new FuzzExoticCollectionEntity { Id = id, SortedNumbers = sortedSet };
        var dto = Mapper.Map<FuzzExoticCollectionEntity, FuzzExoticCollectionDto>(entity)!;
        return dto.SortedNumbers is not null
            && dto.SortedNumbers!.SetEquals(sortedSet);
    }

    /// <summary>
    /// Map 保留 LinkedList 内容和顺序 — 映射后元素和链表顺序一致
    /// </summary>
    [Property]
    public bool Map_LinkedList_Preserves_Content(int id, int[] elements)
    {
        var linkedList = new LinkedList<int>(elements);
        var entity = new FuzzExoticCollectionEntity { Id = id, LinkedNumbers = linkedList };
        var dto = Mapper.Map<FuzzExoticCollectionEntity, FuzzExoticCollectionDto>(entity)!;
        return dto.LinkedNumbers is not null
            && dto.LinkedNumbers!.SequenceEqual(linkedList);
    }
}
