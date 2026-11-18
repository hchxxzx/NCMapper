using NCMapper.Attributes;
using Xunit;

namespace NCMapper.Tests;

/// <summary>
/// 双目标反向映射缓存键测试（AUD-5 回归）
/// <para>场景：同一 [MapFrom] DTO 源依次映射两个不同非 DTO 目标，非默认配置路径（SnakeCase）下
/// 归一化源字典缓存键须含 targetType——修复前三元组键 (sourceType, sourceIsDefine, targetIsDefine)
/// 同键命中不同子集，按调用顺序产生索引错位/静默匹配丢失（PropertyMatcher.cs:226 四元组键）。</para>
/// </summary>
[Collection("PipelineGlobalState")]
public class ReverseMapMultiTargetCacheTests : IDisposable
{
    /// <summary>
    /// 测试前重置全局配置冻结状态（测试隔离）
    /// </summary>
    public ReverseMapMultiTargetCacheTests()
    {
        Mapper.ClearCache();        // 迁移：Reset 不再清注册表，测试隔离需显式全清
        GlobalMapConfig.ResetForTests();
    }

    /// <summary>
    /// 测试后重置冻结状态 — 防止配置冻结残留污染其他测试类
    /// </summary>
    public void Dispose()
    {
        Mapper.ClearCache();        GlobalMapConfig.ResetForTests(); // 迁移

    }

    /// <summary>
    /// 验证：同一 DTO 源映射两个不同非 DTO 目标，两次匹配均正确
    /// <para>修复前：先映射 TwoTargetA 建缓存 (DTO,true,false)，再映射 TwoTargetB 同键命中 A 的字典，
    /// B 的目标属性 Id 经缓存索引 0 访问空 withoutAttr 子集 → ArgumentOutOfRangeException（或静默错位）。
    /// 修复后：B 独立缓存键；Id 按反向分支设计语义不映射（DTO.Id 的 propToSource=TwoTargetA ≠ TwoTargetB → 0）。</para>
    /// </summary>
    [Fact]
    public void SameDtoSource_TwoTargets_BothMapCorrectly()
    {
        Mapper.RegisterGeneratedConfig(
            new GeneratedMapConfig { NamingStrategy = (int)NamingStrategy.SnakeCase },
            "MultiTargetCache");

        var src = new TwoTargetSrcDto
        {
            Id = 1,
            AName = "Alpha",
            BName = "Beta"
        };

        // 第一次映射：构建归一化字典缓存（键含 targetType=TwoTargetA）
        var a = Mapper.Map<TwoTargetSrcDto, TwoTargetA>(src);
        Assert.Equal(1, a!.Id);
        Assert.Equal("Alpha", a.AName);

        // 第二次映射：修复前同键（DTO,true,false）命中 A 的字典 → 越界/错位；修复后独立键
        var b = Mapper.Map<TwoTargetSrcDto, TwoTargetB>(src);
        Assert.Equal("Beta", b!.BName);
        // 反向分支设计语义：DTO.Id 不归属 TwoTargetB → 不映射
        Assert.Equal(0, b.Id);
    }

    /// <summary>
    /// 验证：ClearCache 后缓存重建，双目标映射结果仍正确
    /// <para>四元组键自动纳入既有 s_normalizedKeysCache.Clear()（PropertyMatcher.cs:344），
    /// 清空后重建不得引入旧键残留</para>
    /// </summary>
    [Fact]
    public void SameDtoSource_TwoTargets_ClearCache_RebuildsCorrectly()
    {
        Mapper.RegisterGeneratedConfig(
            new GeneratedMapConfig { NamingStrategy = (int)NamingStrategy.SnakeCase },
            "MultiTargetCache");

        var src = new TwoTargetSrcDto
        {
            Id = 2,
            AName = "Alpha2",
            BName = "Beta2"
        };

        var a1 = Mapper.Map<TwoTargetSrcDto, TwoTargetA>(src);
        Mapper.ClearCache();
        var a2 = Mapper.Map<TwoTargetSrcDto, TwoTargetA>(src);
        var b2 = Mapper.Map<TwoTargetSrcDto, TwoTargetB>(src);

        Assert.Equal(2, a1!.Id);
        Assert.Equal("Alpha2", a1.AName);
        Assert.Equal(2, a2!.Id);
        Assert.Equal("Alpha2", a2.AName);
        Assert.Equal("Beta2", b2!.BName);
        // 反向分支设计语义：DTO.Id 不归属 TwoTargetB → 不映射
        Assert.Equal(0, b2.Id);
    }
}

/// <summary>
/// 双目标反向映射测试源 DTO — 带 [MapFrom]，BName 跨源指向 TwoTargetB
/// </summary>
[MapFrom(typeof(TwoTargetA))]
public class TwoTargetSrcDto
{
    /// <summary>
    /// 主键（默认匹配 TwoTargetA.Id）
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// A 目标名称（默认匹配 TwoTargetA.AName）
    /// </summary>
    public string AName { get; set; } = "";

    /// <summary>
    /// B 目标名称（跨源：FromSource=TwoTargetB，反向分支按 propToSource 过滤保留）
    /// </summary>
    [MapProperty(typeof(TwoTargetB), "BName")]
    public string BName { get; set; } = "";
}

/// <summary>
/// 目标 A（非 DTO，无 [MapFrom]）— 匹配 DTO.Id / DTO.AName
/// </summary>
public class TwoTargetA
{
    /// <summary>
    /// 主键
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// A 目标名称
    /// </summary>
    public string AName { get; set; } = "";
}

/// <summary>
/// 目标 B（非 DTO，无 [MapFrom]）— 反向分支下仅 BName 保留（FromSource=TwoTargetB）
/// </summary>
public class TwoTargetB
{
    /// <summary>
    /// 主键（修复前因缓存索引错位被赋 BName 值）
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// B 目标名称
    /// </summary>
    public string BName { get; set; } = "";
}
