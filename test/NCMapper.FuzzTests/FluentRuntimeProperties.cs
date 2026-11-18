// （属性测试层 Fluent 破零 目标 ≈4 例）
// Fluent 形态属性测试（FsCheck）——[MapperConfig] fixture 编译进测试程序集（SourceGen Analyzer
// 引用，本批新增边界变更登记）→ Mapper.Map 真生成通道实跑。
// 不变式 = 随机输入下异名声明映射值一致 + 集合逐项 + null 守卫 + Update 写回（A-01 运行时面）。
using System.Linq;
using FsCheck.Xunit;
using NCMapper;
using Xunit;

namespace NCMapper.FuzzTests;

public class FfEntity { public int Id { get; set; } public string Name { get; set; } = ""; }
public class FfView { public int Id { get; set; } public string AliasName { get; set; } = ""; }

[MapperConfig]
internal class FfMapper
{
    public static void Configure(FluentMappingConfig cfg)
        => cfg.For<FfEntity, FfView>()
              .Member(d => d.AliasName).From(s => s.Name);
}

/// <summary>Fluent 生成通道属性测试（FsCheck 100 随机输入 + shrinking）。</summary>
public class FluentFuzzProperties
{
    /// <summary>异名声明：随机输入下值恒一致（A-01 运行时面不变式）。</summary>
    [Property]
    [Trait("Capability", "A-01")]
    public bool Fluent_Alias_Map_ValueInvariant(int id, string name)
    {
        var dto = Mapper.Map<FfEntity, FfView>(new FfEntity { Id = id, Name = name })!;
        return dto.Id == id && dto.AliasName == name;
    }

    /// <summary>MapList 集合逐项不变式。</summary>
    [Property]
    [Trait("Capability", "A-01")]
    public bool Fluent_MapList_ElementInvariant(int[] ids)
    {
        var entities = ids.Select(i => new FfEntity { Id = i, Name = "n" + i }).ToList();
        var list = Mapper.MapList<FfEntity, FfView>(entities)!;
        return list.Count == entities.Count
            && list.Zip(entities).All(p => p.First.Id == p.Second.Id && p.First.AliasName == p.Second.Name);
    }

    /// <summary>null 源顶层守卫不变式。</summary>
    [Property]
    [Trait("Capability", "A-01")]
    public bool Fluent_Null_Source_NullOrEmpty(FfEntity? user)
    {
        var dto = Mapper.Map<FfEntity, FfView>(user!);
        return user is null ? dto is null : dto is not null;
    }

    /// <summary>Update 就地更新不变式（写入值与源一致）。</summary>
    [Property]
    [Trait("Capability", "A-01")]
    public bool Fluent_Update_WritesValues(int id, string name)
    {
        var dst = new FfView();
        Mapper.Map(new FfEntity { Id = id, Name = name }, dst);
        return dst.Id == id && dst.AliasName == name;
    }
}
