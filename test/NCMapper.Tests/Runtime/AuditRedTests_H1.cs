using System;
using NCMapper.Attributes;
using NCMapper.Runtime;
using Xunit;

namespace NCMapper.Tests.Runtime;

/// <summary>
/// 审计缺陷 H-1 红测（探针转正——来源＝20261013-001 审计报告附录 A）。
/// <para>缺陷形态：白名单对（[MapNoAutoMatch]）在反射腿被两条无门通道绕过——005 册白名单消费门
/// 只加在 Block 路径属性赋值循环（ExpressionBuilder.cs:158-178）：①MemberInit 快路径
/// （MapperBase.cs TryBuildMemberInit，CreateMapFunc 快路径优先）无门；②构造器按名匹配腿
/// （MapperBase.cs BuildConstructorArgs，按参数名大小写不敏感自动匹配源属性，且执行序先于
/// 快路径/Block 分流）无门——"未声明同名不配对"经两通道同样静默失效。</para>
/// <para>锁定用例按缺陷触发形态逐腿配对（经验库 #202）：属性同名腿 / 构造器参数腿（record 目标）。
/// 夹具纪律＝经验库 #201 ①：private 嵌套类/record → 生成器 NCMAP011 跳过 → 纯反射腿
///（无生成通道遮蔽）；[Collection("GlobalConfigState")] + 构造器/Dispose 双清（隔离惯例）。</para>
/// </summary>
[Collection("GlobalConfigState")]
public class AuditRedTests_H1 : IDisposable
{
    public AuditRedTests_H1() { RuntimeSnapshotStore.Clear(); Mapper.ClearCache(); }
    public void Dispose() { RuntimeSnapshotStore.Clear(); Mapper.ClearCache(); }

    // ---- 腿①：属性同名腿（附录 A 形态：白名单对 + 全同名简单属性 → 反射腿 MemberInit 快路径） ----
    private class H1Ent { public int Id { get; set; } public string Name { get; set; } = ""; }

    [MapFrom(typeof(H1Ent))]
    [MapNoAutoMatch]
    private class H1Dto { public int Id { get; set; } public string Name { get; set; } = ""; }

    [Fact]
    public void H1_WhitelistPair_AllSameName_ZeroAutoMatch()
    {
        var dto = Mapper.Map<H1Ent, H1Dto>(new H1Ent { Id = 7, Name = "alpha" });
        // 白名单语义：未声明同名不配对 → 零自动匹配（修复前实红：Id=7, Name='alpha'——快路径绕过）
        Assert.True(dto.Id == 0 && dto.Name == "",
            $"属性腿白名单被绕过：Id={dto.Id}, Name='{dto.Name}'（期望 0 与空串）");
    }

    // ---- 腿②：构造器参数腿（白名单 × record 目标、ctor 参数全同名——评审 001 P1-3 / 003 P1-3） ----
    private class H1RecEnt { public int Id { get; set; } public string Name { get; set; } = ""; }

    [MapFrom(typeof(H1RecEnt))]
    [MapNoAutoMatch]
    private sealed record H1RecDto(int Id, string Name);

    [Fact]
    public void H1_WhitelistPair_RecordTarget_CtorLegZeroAutoMatch()
    {
        var dto = Mapper.Map<H1RecEnt, H1RecDto>(new H1RecEnt { Id = 7, Name = "alpha" });
        // record 无无参构造 → CreateMapFunc 先行调 BuildConstructorArgs（快路径/Block 分流之前）——
        // :831 按参数名自动匹配源属性，白名单对静默失效（修复前实红：Id=7, Name='alpha'）。
        // 修复后：门拦截 → 参数留 Default → Id=0, Name=null（零自动匹配）
        Assert.True(dto.Id == 0 && dto.Name == null,
            $"构造器腿白名单被绕过：Id={dto.Id}, Name='{dto.Name}'（期望 0 与 null）");
    }
}
