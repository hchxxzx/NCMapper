using NCMapper.Attributes;

namespace NCMapper.Tests.Models;

// ================================================================
// Compose 嵌套测试模型
// ================================================================

/// <summary>
/// 子源 — 包含嵌套对象
/// </summary>
public class ComposeNestedSubSource
{
    /// <summary>
    /// 子源 ID
    /// </summary>
    public int Id { get; set; } = 1;
    /// <summary>
    /// 嵌套子对象
    /// </summary>
    public ComposeNestedChild? Child { get; set; }
}

/// <summary>
/// 子源嵌套子对象
/// </summary>
public class ComposeNestedChild
{
    /// <summary>
    /// 子对象名称
    /// </summary>
    public string Name { get; set; } = "ChildName";
    /// <summary>
    /// 子对象年龄
    /// </summary>
    public int Age { get; set; } = 5;
}

/// <summary>
/// 主源 — 包含子源引用
/// </summary>
public class ComposeNestedMainSource
{
    /// <summary>
    /// 主源 ID
    /// </summary>
    public int Id { get; set; } = 100;
    /// <summary>
    /// 子源引用
    /// </summary>
    public ComposeNestedSubSource? Sub { get; set; }
}

/// <summary>
/// Compose 嵌套 DTO — 从主源和子源合成，子源包含嵌套属性
/// </summary>
[MapFrom(typeof(ComposeNestedMainSource))]
public class ComposeNestedDto
{
    /// <summary>
    /// 主源 ID（同名字段映射）
    /// </summary>
    public int Id { get; set; }

    // 从主源的 SubSource.Id 映射
    /// <summary>
    /// 从子源映射的子 ID
    /// </summary>
    [MapProperty(typeof(ComposeNestedSubSource), "Id")]
    public int SubId { get; set; }

    // 从主源的 SubSource.Child.Name 映射（扁平化）
    /// <summary>
    /// 从子源的子对象扁平映射的名称
    /// </summary>
    public string? ChildName { get; set; }
}

// ================================================================
// 多构造函数 Record 测试模型
// ================================================================

/// <summary>
/// 多构造函数 Record — 两个参数数量相同的构造函数
/// </summary>
public record MultiCtorRecord
{
    /// <summary>
    /// 记录 ID
    /// </summary>
    public int Id { get; init; }
    /// <summary>
    /// 记录名称
    /// </summary>
    public string? Name { get; init; }
    /// <summary>
    /// 额外信息
    /// </summary>
    public string? Extra { get; init; }

    /// <summary>
    /// 2 参数构造函数
    /// </summary>
    public MultiCtorRecord(int id, string name)
    {
        Id = id;
        Name = name;
        Extra = "two-params";
    }

    /// <summary>
    /// 3 参数构造函数（参数更多，应优先选择）
    /// </summary>
    public MultiCtorRecord(int id, string name, string? extra)
    {
        Id = id;
        Name = name;
        Extra = extra ?? "three-params";
    }
}

/// <summary>
/// 多构造函数 Record 源实体
/// </summary>
public class MultiCtorSource
{
    /// <summary>
    /// 源实体 ID
    /// </summary>
    public int Id { get; set; } = 42;
    /// <summary>
    /// 源实体名称
    /// </summary>
    public string Name { get; set; } = "TestRecord";
    /// <summary>
    /// 源实体额外信息
    /// </summary>
    public string? Extra { get; set; } = "source-extra";
}

/// <summary>
/// 多构造函数 Record DTO
/// </summary>
[MapFrom(typeof(MultiCtorSource))]
public record MultiCtorDto
{
    /// <summary>
    /// DTO ID
    /// </summary>
    public int Id { get; init; }
    /// <summary>
    /// DTO 名称
    /// </summary>
    public string? Name { get; init; }
    /// <summary>
    /// DTO 额外信息
    /// </summary>
    public string? Extra { get; init; }
}
