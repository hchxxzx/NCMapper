using FsCheck.Xunit;
using NCMapper.FuzzTests.Models;

namespace NCMapper.FuzzTests;

/// <summary>
/// 集合类型属性测试 — List/HashSet/Dictionary 在 DeepCopy 和 Map 下的不变式
/// <para>覆盖路径：内容保留、独立副本（深拷贝隔离）、null 守卫、空集合守卫</para>
/// <para>M-15: 每个 [Property] 运行 500 次随机输入（关键路径加强），失败时自动 shrinking 到最小复现</para>
/// </summary>
public class CollectionProperties
{
    // ================================================================
    // List<int> — 有序集合
    // ================================================================

    /// <summary>
    /// DeepCopy 保留 List 内容和顺序 — 任意输入下深拷贝后元素序列一致
    /// </summary>
    [Property(MaxTest = 500)]
    public bool DeepCopy_List_Preserves_Content(int id, int[] numbers)
    {
        var entity = new FuzzRichCollectionEntity
        {
            Id = id,
            Numbers = numbers.ToList()
        };
        var copy = Mapper.DeepCopy(entity)!;
        return copy.Id == id
            && copy.Numbers is not null
            && copy.Numbers.Count == numbers.Length
            && copy.Numbers.SequenceEqual(numbers);
    }

    /// <summary>
    /// DeepCopy 创建 List 独立副本 — 修改副本不影响原件
    /// </summary>
    [Property(MaxTest = 500)]
    public bool DeepCopy_List_Creates_Independent_Instance(int[] numbers)
    {
        var entity = new FuzzRichCollectionEntity { Numbers = numbers.ToList() };
        var originalCount = numbers.Length;
        var copy = Mapper.DeepCopy(entity)!;
        if (copy.Numbers is null) return false;
        copy.Numbers.Add(int.MaxValue);
        return entity.Numbers!.Count == originalCount;
    }

    // ================================================================
    // HashSet<string> — 无序去重集合
    // ================================================================

    /// <summary>
    /// DeepCopy 保留 HashSet 内容 — 深拷贝后元素集合一致（用 SetEquals 忽略顺序）
    /// </summary>
    [Property(MaxTest = 500)]
    public bool DeepCopy_HashSet_Preserves_Content(string[] tags)
    {
        var expected = new HashSet<string>(tags);
        var entity = new FuzzRichCollectionEntity { Tags = expected };
        var copy = Mapper.DeepCopy(entity)!;
        return copy.Tags is not null && copy.Tags.SetEquals(expected);
    }

    /// <summary>
    /// DeepCopy 创建 HashSet 独立副本 — 向副本添加元素不影响原件
    /// </summary>
    [Property(MaxTest = 500)]
    public bool DeepCopy_HashSet_Creates_Independent_Instance(string[] tags)
    {
        var original = new HashSet<string>(tags);
        var originalCount = original.Count;
        var entity = new FuzzRichCollectionEntity { Tags = original };
        var copy = Mapper.DeepCopy(entity)!;
        if (copy.Tags is null) return false;
        copy.Tags.Add("__FUZZ_INJECTED__");
        return entity.Tags!.Count == originalCount;
    }

    // ================================================================
    // Dictionary<string, int> — 键值对集合
    // ================================================================

    /// <summary>
    /// DeepCopy 保留 Dictionary 内容 — 深拷贝后所有键值对一致
    /// </summary>
    [Property(MaxTest = 500)]
    public bool DeepCopy_Dictionary_Preserves_Content(string[] keys, int[] values)
    {
        var dict = BuildDictionary(keys, values);
        var entity = new FuzzRichCollectionEntity { Counts = dict };
        var copy = Mapper.DeepCopy(entity)!;
        return copy.Counts is not null
            && copy.Counts.Count == dict.Count
            && dict.All(kv => copy.Counts.TryGetValue(kv.Key, out var v) && v == kv.Value);
    }

    /// <summary>
    /// DeepCopy 创建 Dictionary 独立副本 — 向副本添加键值对不影响原件
    /// </summary>
    [Property(MaxTest = 500)]
    public bool DeepCopy_Dictionary_Creates_Independent_Instance(string[] keys, int[] values)
    {
        var dict = BuildDictionary(keys, values);
        var originalCount = dict.Count;
        var entity = new FuzzRichCollectionEntity { Counts = dict };
        var copy = Mapper.DeepCopy(entity)!;
        if (copy.Counts is null) return false;
        copy.Counts["__FUZZ_INJECTED__"] = -1;
        return entity.Counts!.Count == originalCount;
    }

    // ================================================================
    // null / 空集合守卫
    // ================================================================

    /// <summary>
    /// DeepCopy null 集合返回 null — 集合属性为 null 时深拷贝后仍为 null
    /// </summary>
    [Property(MaxTest = 500)]
    public bool DeepCopy_Null_Collections_Return_Null(int id)
    {
        var entity = new FuzzRichCollectionEntity { Id = id };
        var copy = Mapper.DeepCopy(entity)!;
        return copy.Id == id
            && copy.Numbers is null
            && copy.Tags is null
            && copy.Counts is null;
    }

    /// <summary>
    /// DeepCopy 空 List 返回空 List — 有实例但无元素的集合深拷贝后仍为空
    /// </summary>
    [Property(MaxTest = 500)]
    public bool DeepCopy_Empty_List_Returns_Empty(int id)
    {
        var entity = new FuzzRichCollectionEntity
        {
            Id = id,
            Numbers = new List<int>()
        };
        var copy = Mapper.DeepCopy(entity)!;
        return copy.Numbers is not null && copy.Numbers.Count == 0;
    }

    // ================================================================
    // Map — 集合映射一致性（源生成器路径）
    // ================================================================

    /// <summary>
    /// Map 保留 List 内容和顺序 — 映射后元素序列与源一致
    /// </summary>
    [Property(MaxTest = 500)]
    public bool Map_List_Preserves_Content(int id, int[] numbers)
    {
        var entity = new FuzzRichCollectionEntity
        {
            Id = id,
            Numbers = numbers.ToList()
        };
        var dto = Mapper.Map<FuzzRichCollectionEntity, FuzzRichCollectionDto>(entity)!;
        return dto.Id == id
            && dto.Numbers is not null
            && dto.Numbers.Count == numbers.Length
            && dto.Numbers.SequenceEqual(numbers);
    }

    /// <summary>
    /// Map 保留 HashSet 内容 — 映射后元素集合一致
    /// </summary>
    [Property(MaxTest = 500)]
    public bool Map_HashSet_Preserves_Content(string[] tags)
    {
        var expected = new HashSet<string>(tags);
        var entity = new FuzzRichCollectionEntity { Tags = expected };
        var dto = Mapper.Map<FuzzRichCollectionEntity, FuzzRichCollectionDto>(entity)!;
        return dto.Tags is not null && dto.Tags.SetEquals(expected);
    }

    /// <summary>
    /// Map 保留 Dictionary 内容 — 映射后所有键值对一致
    /// </summary>
    [Property(MaxTest = 500)]
    public bool Map_Dictionary_Preserves_Content(string[] keys, int[] values)
    {
        var dict = BuildDictionary(keys, values);
        var entity = new FuzzRichCollectionEntity { Counts = dict };
        var dto = Mapper.Map<FuzzRichCollectionEntity, FuzzRichCollectionDto>(entity)!;
        return dto.Counts is not null
            && dto.Counts.Count == dict.Count
            && dict.All(kv => dto.Counts.TryGetValue(kv.Key, out var v) && v == kv.Value);
    }

    // ================================================================
    // 辅助方法
    // ================================================================

    /// <summary>
    /// 从 keys 和 values 数组构建 Dictionary — 重复 key 后值覆盖，长度不齐取较短者
    /// </summary>
    /// <param name="keys">键数组</param>
    /// <param name="values">值数组</param>
    /// <returns>构建好的 Dictionary</returns>
    private static Dictionary<string, int> BuildDictionary(string[] keys, int[] values)
    {
        var dict = new Dictionary<string, int>();
        var len = Math.Min(keys.Length, values.Length);
        for (int i = 0; i < len; i++)
            dict[keys[i]] = values[i];
        return dict;
    }
}
