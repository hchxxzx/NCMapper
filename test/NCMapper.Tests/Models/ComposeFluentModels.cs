using NCMapper;

namespace NCMapper.Tests.Models;

/// <summary>
/// 双面向运行时测试模型（Fluent 通道 FromSource 补充源绑定，原名 Compose，20260905-003 更名）。
/// <para>Fluent 声明经源生成器物化：主源 A13Entity → A13Dto（主链 Map）、子源 A13Sub 经
/// <c>FromSource&lt;A13Sub&gt;</c> 速记/分组块绑定跨源字段 → 生成端 <c>RegisterGeneratedCompose&lt;A13Dto&gt;</c>
/// 注册层1 生成委托（CodeGenerator.cs:450-456），<c>Mapper.Compose&lt;A13Dto&gt;</c> 走零反射编译时代码路径。</para>
/// <para>双面向：同一 Fluent 声明下 <c>Mapper.Map&lt;A13Entity, A13Dto&gt;</c>（主源面）与
/// <c>Mapper.Compose&lt;A13Dto&gt;(主源, 子源)</c>（合成面）同时可用——FromSource 命中字段在
/// Map 面缺席、在 Compose 合成面填充（物化分流）。</para>
/// </summary>

/// <summary>
/// 主源实体 — 承载主链映射（A 面）。
/// <para>注意：不得声明与 FromSource 命中字段同名的属性（A13Dto.Phone/Title/Code）——
/// 主链自动匹配 × FromSource 同 dest 触发 NCMAP_F29 矛盾（四组合语义：主链×FromSource→F29）。</para>
/// </summary>
public class A13Entity
{
    public string Name { get; set; } = "";
    public int Age { get; set; }
}

/// <summary>子源 — FromSource 跨源字段来源（B 面）</summary>
public class A13Sub
{
    public string Title { get; set; } = "";
    public string Phone { get; set; } = "";
    /// <summary>块内 .Convert 源字段（T1 字段值形态——A13CodeConverter 大写化）</summary>
    public string Code { get; set; } = "";
}

/// <summary>子源二 — 顺序覆盖语义（同 dest 不同子源，放行）</summary>
public class A13Sub2
{
    public string Title { get; set; } = "";
}

/// <summary>
/// 块内 .Convert 静态转换器 — T1 字段值形态（<c>static string M(string)</c>）。
/// <para>语义镜像特性侧 <c>[Convert]</c>：子源 Code 字段值入参 → 大写化写入目标 Code
///（ComposeMappingSnapshot.Converter 三字段消费，CodeGenerator.Compose.cs:269-273）。</para>
/// </summary>
public static class A13CodeConverter
{
    public static string Convert(string fieldValue) => fieldValue.ToUpperInvariant();
}

/// <summary>
/// Fluent 通道 Compose DTO — 双面向目标。
/// <para>子源字段（Title/Phone/Code）仅经 Compose 声明跨源绑定，主链自动匹配不消费；
/// 主源字段（Name/Age）主链映射。速记与分组块并存：Title 速记绑定 A13Sub/A13Sub2，
/// Phone 块内绑定 A13Sub，Code 块内 .Convert 绑定 A13Sub（T1 字段值形态）。</para>
/// </summary>
public class A13Dto
{
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public string Phone { get; set; } = "";
    public string Title { get; set; } = "";
    public string Code { get; set; } = "";
}

/// <summary>
/// Fluent 配置器 — FromSource 速记 + 分组块并存声明（主形态，20260905-003 终端更名）。
/// <para>Title 双绑定：A13Sub 速记 + A13Sub2 速记——同 dest 不同子源（四组合放行），
/// 运行时顺序覆盖（后写者胜）；Phone 经 A13Sub 分组块绑定（块内 = For 的 Map）；
/// Code 经 A13Sub 分组块 .Convert 绑定（块内 T1 字段值转换，A13CodeConverter 大写化）。</para>
/// </summary>
[MapperConfig]
public class A13Mapper
{
    public static void Configure(FluentMappingConfig cfg)
    {
        cfg.For<A13Entity, A13Dto>()
           .Member(d => d.Title).FromSource<A13Sub>(s => s.Title)
           .Member(d => d.Title).FromSource<A13Sub2>(s => s.Title)
           .FromSource<A13Sub>(c =>
           {
               c.Member(d => d.Phone).From(s => s.Phone);
               c.Member(d => d.Code).Convert<string>(A13CodeConverter.Convert);
           });
    }
}
