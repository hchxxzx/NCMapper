using FsCheck.Xunit;
using NCMapper.FuzzTests.Models;

namespace NCMapper.FuzzTests;

/// <summary>
/// 嵌套对象深拷贝属性测试 — 验证 DeepCopy 对嵌套引用类型的递归深拷贝行为
/// <para>覆盖路径：内容保留、引用独立性（深拷贝而非浅拷贝）、null 守卫</para>
/// <para>路径说明：FuzzNestedEntity 走源生成器 DeepCopy，子对象 FuzzAddress 走 FastDeepCopyProvider — 混合路径</para>
/// <para>M-15: 每个 [Property] 运行 500 次随机输入（关键路径加强），失败时自动 shrinking 到最小复现</para>
/// </summary>
public class NestedObjectProperties
{
    // ================================================================
    // 嵌套对象内容保留
    // ================================================================

    /// <summary>
    /// DeepCopy 保留嵌套对象内容 — 任意输入下子对象字段值一致
    /// </summary>
    [Property(MaxTest = 500)]
    public bool DeepCopy_Nested_Preserves_Content(int id, string name, string city, int zipCode)
    {
        var entity = new FuzzNestedEntity
        {
            Id = id,
            Name = name,
            Address = new FuzzAddress { City = city, ZipCode = zipCode }
        };
        var copy = Mapper.DeepCopy(entity)!;
        return copy.Id == id
            && copy.Name == name
            && copy.Address is not null
            && copy.Address.City == city
            && copy.Address.ZipCode == zipCode;
    }

    // ================================================================
    // 嵌套对象引用独立性 — 深拷贝的核心不变式
    // ================================================================

    /// <summary>
    /// DeepCopy 创建嵌套对象独立引用 — 修改副本的子对象不影响原件
    /// <para>这是深拷贝与浅拷贝的关键区别：浅拷贝共享子对象引用，深拷贝递归复制</para>
    /// </summary>
    [Property(MaxTest = 500)]
    public bool DeepCopy_Nested_Creates_Independent_Reference(int id, string city, int zipCode)
    {
        var entity = new FuzzNestedEntity
        {
            Id = id,
            Address = new FuzzAddress { City = city, ZipCode = zipCode }
        };
        var copy = Mapper.DeepCopy(entity)!;
        if (copy.Address is null) return false;

        // 修改副本的嵌套对象
        copy.Address.City = "__MODIFIED__";
        copy.Address.ZipCode = int.MinValue;

        // 原件的嵌套对象应保持不变
        return entity.Address is not null
            && entity.Address.City == city
            && entity.Address.ZipCode == zipCode;
    }

    /// <summary>
    /// DeepCopy 嵌套对象引用不同 — copy.Address 与 entity.Address 不是同一实例
    /// </summary>
    [Property(MaxTest = 500)]
    public bool DeepCopy_Nested_Reference_Not_Same(int id)
    {
        var entity = new FuzzNestedEntity
        {
            Id = id,
            Address = new FuzzAddress { City = "Test", ZipCode = 0 }
        };
        var copy = Mapper.DeepCopy(entity)!;
        return copy.Address is not null
            && entity.Address is not null
            && !ReferenceEquals(copy.Address, entity.Address);
    }

    // ================================================================
    // null 嵌套对象守卫
    // ================================================================

    /// <summary>
    /// DeepCopy null 嵌套对象返回 null — 子对象为 null 时深拷贝后仍为 null
    /// </summary>
    [Property(MaxTest = 500)]
    public bool DeepCopy_Null_Nested_Returns_Null(int id, string name)
    {
        var entity = new FuzzNestedEntity { Id = id, Name = name, Address = null };
        var copy = Mapper.DeepCopy(entity)!;
        return copy.Id == id
            && copy.Name == name
            && copy.Address is null;
    }
}
