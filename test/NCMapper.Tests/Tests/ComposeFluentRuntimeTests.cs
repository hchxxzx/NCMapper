#pragma warning disable NCMapper001 // [Experimental] Compose API — 镜像 Mapper.Compose.cs:1
using NCMapper;
using NCMapper.Tests.Models;
using Xunit;

namespace NCMapper.Tests.Tests;

/// <summary>
/// 双面向运行时测试 — Fluent 通道 Compose 多源组合（F-4/C-03 落地）。
/// <para>验证业务逻辑：</para>
/// <para>① 双面向：同一 Fluent 声明下 <c>Mapper.Map&lt;主源, DTO&gt;</c>（主源面）与
/// <c>Mapper.Compose&lt;DTO&gt;(主源, 子源)</c>（合成面）同时可用且值正确——
/// Compose 命中字段在 Map 面缺席、在 Compose 面填充（物化分流的运行时投影）。</para>
/// <para>② 顺序覆盖（Mapper.Compose.cs:15-17 规则）：同 dest 不同子源（放行）按传入数组顺序
/// 后写覆盖先写——子源一先传取子源一值、子源二先传取子源二值。</para>
/// <para>③ 异常按路径钉死（_004 I-3）：生成委托路径（层1，s_generatedComposeFuncs 命中）主源缺失
/// → <see cref="System.InvalidOperationException"/>，消息体为 <c>ErrorMessages.ComposeSourceNotFound</c>
/// （CodeGenerator.Compose.cs:364-366）；不得断言为 MapConfigurationException（层2 ComposeRuntime 口径，
/// Mapper.Compose.cs:169-171）——两路径异常类型不同，本测试工程生成器在途恒走层1。</para>
/// <para>④ 块内 .Member(dest).Convert 运行断言（决策点）：块内 Member().Convert T1 字段值形态转换器
/// （A13CodeConverter 大写化）在合成值中生效——ComposeMappingSnapshot.Converter 三字段经
/// CodeGenerator.Compose.cs:269-273 消费，生成端发射静态方法调用。</para>
/// <para>⑤ 层1 注册自足：s_generatedComposeFuncs 登记 CacheRegistry "generated-compose-funcs"
/// （Mapper.cs:311，Generated 层），受 ClearGenerated/ClearCache 清理；module initializer 仅执行
/// 一次不重填（ClearCache 既定语义，MapperRegistrationTests.ClearCache_Removes_Compose_Registration
/// 钉死）。Fluent DTO 无 [MapFrom]，层2 ComposeRuntime 不可兜底（抛 MapFromNotApplied），
/// 故每个测试方法执行前幂等重注册（Mapper.RegisterGeneratedCompose 端 TryRemove+赋值幂等，
/// 与生成端 module initializer 注册同一委托），将"注册→使用"竞态窗口压缩至语句级；
/// [Collection("GlobalConfigState")]（DisableParallelization）保留以规避非并发 ClearCache
/// 调用方的常规交错。</para>
/// </summary>
[Collection("GlobalConfigState")]
public class ComposeFluentRuntimeTests
{
    /// <summary>
    /// 层1 注册自足：与生成端 module initializer 注册同一委托（Compose_A13Dto），
    /// 幂等覆盖 A13Dto 键、不触碰其他类型注册。
    /// </summary>
    private static void EnsureGeneratedComposeRegistered()
        => Mapper.RegisterGeneratedCompose<A13Dto>(NCMapper.Generated.GeneratedMapper.Compose_A13Dto);

    private static (A13Entity primary, A13Sub sub) MakePair(string title, string phone, string code)
    {
        var primary = new A13Entity { Name = "张三", Age = 30 };
        var sub = new A13Sub { Title = title, Phone = phone, Code = code };
        return (primary, sub);
    }

    // ── ① 双面向 ──────────────────────────────────────────────

    /// <summary>
    /// 主源面：Mapper.Map 仅填充主链字段（Name/Age）；
    /// Compose 命中字段（Title/Phone/Code）不参与主链映射，保持默认值。
    /// 验证物化分流的运行时投影：命中属性只入 ComposeMappings 不入 PropertyMappings。
    /// </summary>
    [Fact]
    public void DualFacing_MapPrimaryOnly_FillsMainChainFields_LeavesComposeFieldsDefault()
    {
        EnsureGeneratedComposeRegistered();

        var (primary, _) = MakePair("博士", "13800000000", "abc123");

        var dto = Mapper.Map<A13Entity, A13Dto>(primary);

        Assert.NotNull(dto);
        Assert.Equal("张三", dto!.Name);
        Assert.Equal(30, dto.Age);
        Assert.Equal("", dto.Title);
        Assert.Equal("", dto.Phone);
        Assert.Equal("", dto.Code);
    }

    /// <summary>
    /// 合成面：Mapper.Compose 主源建 DTO + 子源补跨源字段（速记 Title + 块内 Phone + 块内 Member().Convert Code）；
    /// 主链与子源字段并存且值正确。
    /// </summary>
    [Fact]
    public void DualFacing_Compose_FillsAllFields()
    {
        EnsureGeneratedComposeRegistered();

        var (primary, sub) = MakePair("博士", "13800000000", "abc123");

        var dto = Mapper.Compose<A13Dto>(primary, sub);

        Assert.NotNull(dto);
        Assert.Equal("张三", dto!.Name);
        Assert.Equal(30, dto.Age);
        Assert.Equal("博士", dto.Title);
        Assert.Equal("13800000000", dto.Phone);
    }

    // ── ② 顺序覆盖（同 dest 不同子源，放行）──────────────────────────────────────────────

    /// <summary>
    /// 子源二先传、子源一后传：pending 按传入顺序处理（sub1 最后）→ Title 取子源一值"博士"（后写者胜）；
    /// Phone/Code 仅子源一提供。
    /// </summary>
    [Fact]
    public void OrderOverride_Sub2First_TitleFromSub1()
    {
        EnsureGeneratedComposeRegistered();

        var (primary, sub1) = MakePair("博士", "13800000000", "abc123");
        var sub2 = new A13Sub2 { Title = "教授" };

        var dto = Mapper.Compose<A13Dto>(primary, sub2, sub1);

        Assert.Equal("博士", dto!.Title);
        Assert.Equal("13800000000", dto.Phone);
    }

    /// <summary>
    /// 子源一先传、子源二后传：pending 按传入顺序处理（sub2 最后）→ Title 取子源二值"教授"（后写者胜）；
    /// 验证 pending 两遍扫描按传入数组顺序更新（生成端 switch 顺序，CodeGenerator.Compose.cs:368-399）。
    /// </summary>
    [Fact]
    public void OrderOverride_Sub1First_TitleFromSub2()
    {
        EnsureGeneratedComposeRegistered();

        var (primary, sub1) = MakePair("博士", "13800000000", "abc123");
        var sub2 = new A13Sub2 { Title = "教授" };

        var dto = Mapper.Compose<A13Dto>(primary, sub1, sub2);

        Assert.Equal("教授", dto!.Title);
        Assert.Equal("13800000000", dto.Phone);
    }

    // ── ③ 异常按路径钉死（层1 生成委托） ──────────────────────────────────────────────

    /// <summary>
    /// 层1 生成委托路径主源缺失：仅传子源 → src is A13Entity 不命中 → result 恒 null
    /// → 发射 InvalidOperationException，消息体 = ErrorMessages.ComposeSourceNotFound("A13Entity")
    /// （CodeGenerator.Compose.cs:364-366）。类型钉死为 InvalidOperationException——
    /// 不得断言 MapConfigurationException（层2 口径，本测试工程生成器在途不可达）。
    /// </summary>
    [Fact]
    public void PrimaryMissing_GeneratedPath_ThrowsInvalidOperationException_WithComposeSourceNotFoundMessage()
    {
        EnsureGeneratedComposeRegistered();

        var sub = new A13Sub { Title = "博士", Phone = "13800000000", Code = "abc123" };

        var ex = Assert.Throws<System.InvalidOperationException>(() =>
            Mapper.Compose<A13Dto>(sub));

        Assert.Contains("A13Entity", ex.Message);
    }

    // ── ④ 块内 Member().Convert 运行断言（T1 字段值形态） ──────────────────────────────────────────────

    /// <summary>
    /// 块内 .Member(dest).Convert&lt;string&gt;（A13CodeConverter 大写化，T1 字段值形态）在合成值中生效：
    /// 子源 Code = "abc123" → 合成 DTO.Code = "ABC123"（A13CodeConverter.Convert 大写化）。
    /// 验证 ComposeMappingSnapshot.Converter 三字段经生成端消费（CodeGenerator.Compose.cs:269-273）
    /// 并在运行时正确发射静态方法调用——与特性侧 [Convert] 同语义。
    /// </summary>
    [Fact]
    public void BlockConvert_T1FieldValue_ConverterAppliedInComposedValue()
    {
        EnsureGeneratedComposeRegistered();

        var (primary, sub) = MakePair("博士", "13800000000", "abc123");

        var dto = Mapper.Compose<A13Dto>(primary, sub);

        Assert.NotNull(dto);
        Assert.Equal("ABC123", dto!.Code);
    }
}
