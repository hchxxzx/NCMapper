#pragma warning disable NCMapper001 // [Experimental] Compose API — 镜像 Mapper.Compose.cs:1
using NCMapper;
using NCMapper.Tests.Models;
using Xunit;

namespace NCMapper.Tests.Tests;

/// <summary>
/// 正向消费（commit① 负向对照的翻转）— Fluent 块内 Block-Mask/FromJson
/// D-1 降级解除后的运行时行为：
/// <para>① Map 面：Compose 命中属性（Phone/Config/Email）缺席主链（物化分流），Name 照常；
/// ② Compose 面：块内 Mask 脱敏生效（138****8000）、FromJson string→对象反序列化生效、
/// 嵌套块内 Mask flatten 生效（u***@example.com）——G2 Fluent 通道运行时行为级投影；
/// ③ Map+Mask 合并（评审 P1-4）：同 dest 块内 Map 显式路径 + Mask 脱敏同时生效，
/// 值取自子源路径且已脱敏（B2 系）。</para>
/// <para>纪律：[Collection("GlobalConfigState")] + 每测试首行幂等重注册（规则 5）——
/// 生成端为 BmDto/Bm2Dto 产 Compose 委托（层1），module initializer 只跑一次，ClearGenerated
/// 竞态由重注册压缩至语句级。</para>
/// </summary>
[Collection("GlobalConfigState")]
public class ComposeFluentBlockMaskConsumedTests
{
    private static void EnsureGeneratedComposeRegistered()
    {
        Mapper.RegisterGeneratedCompose<BmDto>(NCMapper.Generated.GeneratedMapper.Compose_BmDto);
        Mapper.RegisterGeneratedCompose<Bm2Dto>(NCMapper.Generated.GeneratedMapper.Compose_Bm2Dto);
    }

    private static (BmEntity primary, BmSub sub, BmSubNested nested) MakeSources()
        => (
            new BmEntity { Name = "张三", Phone = "13900000000" },
            new BmSub { Phone = "13800138000", Config = """{"Id":7,"Level":"P1"}""" },
            new BmSubNested { Email = "user@example.com" });

    /// <summary>
    /// Map 面：Compose 命中属性缺席主链（显式跨源优先，:101 命中判定去降级后 Phone 命中）；
    /// Name 走主链照常。负向对照的"明文进 DTO"缺陷随降级解除而消除。
    /// </summary>
    [Fact]
    public void Consumed_MapPrimary_ComposeFieldsAbsent_MainChainIntact()
    {
        EnsureGeneratedComposeRegistered();

        var (primary, _, _) = MakeSources();

        var dto = Mapper.Map<BmEntity, BmDto>(primary);

        Assert.NotNull(dto);
        Assert.Equal("张三", dto!.Name);
        // Phone 命中 Compose 分流：主链自动匹配被跳过（明文不再进 DTO）
        Assert.Equal("", dto.Phone);
        Assert.Null(dto.Config);
        Assert.Equal("", dto.Email);
    }

    /// <summary>
    /// Compose 面正向：块内 Mask 脱敏生效（MaskPhone 保前3后4）、FromJson string→对象
    /// 反序列化生效（JsonConvertToObject）、嵌套块内 Mask flatten 生效（MaskEmail 保留首字+域名）。
    /// 主源/子源/嵌套子源三源并存，主链字段与跨源字段同 DTO 齐备。
    /// </summary>
    [Fact]
    public void Consumed_Compose_MaskAndFromJsonAndNestedApplied()
    {
        EnsureGeneratedComposeRegistered();

        var (primary, sub, nested) = MakeSources();

        var dto = Mapper.Compose<BmDto>(primary, sub, nested);

        Assert.NotNull(dto);
        Assert.Equal("张三", dto!.Name);
        // 块内 Mask：MaskPhone（13800138000 → 138****8000）
        Assert.Equal("138****8000", dto.Phone);
        // 块内 FromJson：string → BmConfig 反序列化
        Assert.NotNull(dto.Config);
        Assert.Equal(7, dto.Config!.Id);
        Assert.Equal("P1", dto.Config.Level);
        // 嵌套块内 Mask：MaskEmail（user@example.com → u***@example.com）
        Assert.Equal("u***@example.com", dto.Email);
    }

    /// <summary>
    /// Map+Mask 合并端到端（评审 P1-4）：同 dest 块内 Map（显式路径）+ Mask（脱敏）双声明
    /// 合并生效——Compose 面 Phone 取子源 Phone 值且已脱敏（两语义并存同一 cm，B2 系）。
    /// </summary>
    [Fact]
    public void Consumed_MapPlusMask_Merge_PathAndMaskingBothApplied()
    {
        EnsureGeneratedComposeRegistered();

        var primary = new Bm2Entity { Name = "李四" };
        var sub = new Bm2Sub { Phone = "13800138000" };

        var dto = Mapper.Compose<Bm2Dto>(primary, sub);

        Assert.NotNull(dto);
        Assert.Equal("李四", dto!.Name);
        // Map 显式路径取子源值 + Mask 脱敏，两语义同时生效
        Assert.Equal("138****8000", dto.Phone);
    }
}
