using NCMapper;

namespace NCMapper.Tests.Models;

/// <summary>
/// 明文负向对照 fixture（两段式 commit①，BM 系列）。
/// <para>Fluent Compose 分组块内声明 <c>.Member(dest).Mask&lt;TMask&gt;()</c> / <c>.Member(dest).FromJson()</c> /
/// 嵌套块内 <c>.Member(dest).Mask&lt;TMask&gt;()</c>（嵌套 FromSource flatten 复用 20260905-003 骨架形态，
/// 仅追加内层声明）。现状（降级排除，MappingSnapshotBuilder.IsEffectiveComposeDeclaration）
/// 这些声明不产 ComposeMapping：Phone 走主链自动匹配明文进 DTO、Config/Email 不被填充——
/// 负向测试锁定该缺陷存在性；commit② 去降级后断言翻转为正向（脱敏/展开生效）。</para>
/// <para>命名约定：BM = Block-Mask。嵌套块形态与 CfeH（20260905-003）一致——
/// 外层 <c>FromSource&lt;BmSub&gt;</c> 块内 <c>c.FromSource&lt;BmSubNested&gt;</c> 嵌套，
/// flatten 语义等价主链独立 FromSource 声明。</para>
/// </summary>

/// <summary>主源实体 — Name 走主链；Phone 与 dest 同名（降级态下自动匹配明文进 DTO = 缺陷证据）</summary>
public class BmEntity
{
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
}

/// <summary>子源 — 块内 Mask（Phone）与 FromJson（Config，按名解析）的来源</summary>
public class BmSub
{
    public string Phone { get; set; } = "";
    public string Config { get; set; } = "";
}

/// <summary>嵌套块子源 — 嵌套 FromSource flatten 后块内 Mask（Email）的来源</summary>
public class BmSubNested
{
    public string Email { get; set; } = "";
}

/// <summary>FromJson string→对象形态的目标类型（JsonConverter.JsonConvertToObject 反序列化落点）</summary>
public class BmConfig
{
    public int Id { get; set; }
    public string Level { get; set; } = "";
}

/// <summary>
/// Fluent 通道 Compose DTO — 明文负向对照目标。
/// <para>Phone（块内 Mask 声明）、Config（块内 FromJson 声明）、Email（嵌套块内 Mask 声明）；
/// Name 主链。降级态：Phone 明文（自动匹配）、Config=null、Email=""；去降级后：Phone 脱敏、
/// Config 反序列化、Email 脱敏。</para>
/// </summary>
public class BmDto
{
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public BmConfig? Config { get; set; }
    public string Email { get; set; } = "";
}

/// <summary>
/// Fluent 配置器 — 分组块内 Mask/FromJson + 嵌套块内 Mask（去降级后消费形态）。
/// <para>终端一律用更名后 <c>FromSource</c>（20260905-003）；块内 Mask/FromJson 方法名不受更名影响。</para>
/// </summary>
[MapperConfig]
public class BmMapper
{
    public static void Configure(FluentMappingConfig cfg)
    {
        cfg.For<BmEntity, BmDto>()
           .FromSource<BmSub>(c =>
           {
               c.Member(d => d.Phone).Mask<MaskPhone>();
               c.Member(d => d.Config!).FromJson();
               c.FromSource<BmSubNested>(x =>
               {
                   x.Member(d => d.Email).Mask<MaskEmail>();
               });
           });
    }
}

// ── Bm2 系列：同 dest Map+Mask 合并语义（评审 P1-4）端到端用例 ──────────────────────────

/// <summary>合并用例主源 — Name 主链</summary>
public class Bm2Entity
{
    public string Name { get; set; } = "";
}

/// <summary>合并用例子源 — Phone 来源（Map 显式路径）</summary>
public class Bm2Sub
{
    public string Phone { get; set; } = "";
}

/// <summary>
/// 合并用例 DTO — Phone 同 dest 双声明（块内 Map 显式路径 + 块内 Mask 脱敏）：
/// 合并生效 = Compose 面 Phone 取子源值**且**脱敏（Map 路径 + Mask 语义并存同一 cm）。
/// </summary>
public class Bm2Dto
{
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
}

/// <summary>
/// 合并用例配置器 — 块内 Map 先声明、Mask 后声明（顺序无关性由快照级 Theory 双向钉死，
/// 此处固定一序做端到端投影）。
/// </summary>
[MapperConfig]
public class Bm2Mapper
{
    public static void Configure(FluentMappingConfig cfg)
    {
        cfg.For<Bm2Entity, Bm2Dto>()
           .FromSource<Bm2Sub>(c =>
           {
               c.Member(d => d.Phone).From(s => s.Phone);
               c.Member(d => d.Phone).Mask<MaskPhone>();
           });
    }
}
