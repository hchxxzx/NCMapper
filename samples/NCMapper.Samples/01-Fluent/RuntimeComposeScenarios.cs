using NCMapper;
using NCMapper.Runtime;
using NCMapper.Samples.Helpers;

namespace NCMapper.Samples.Fluent;

// ================================================================
// 109 Fluent Compose 运行时元数据 — RuntimeComposeMapping 演示
// 演示面三件事：
//   ① Compose 跨源声明（FromSource 速记 + 分组块）在运行时经
//      RuntimeSnapshotStore.TryGetFluentDetailMeta 可读（ComposeMappings 明细）；
//   ② 新语法叠加组合：From+Mask+MapWhen 三重叠加 / From+Format+Mask（FormatThenMask）/
//      跨型 From（int → string）；
//   ③ 断言可真的失败：断言值与元数据字段值严格相等，写错即 Error。
// ================================================================

/// <summary>主源订单实体（Compose 运行时元数据演示主源）。</summary>
public class RcOrderEntity
{
    /// <summary>主键</summary>
    public int Id { get; set; }

    /// <summary>订单号</summary>
    public string OrderNo { get; set; } = "";

    /// <summary>金额</summary>
    public decimal Amount { get; set; }

    /// <summary>分值（跨型 From 演示：int 源 → string 目标）</summary>
    public int Score { get; set; }

    /// <summary>创建时间（From+Format+Mask 三重叠加演示源）</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>卡号（From+Mask+MapWhen 三重叠加演示源）</summary>
    public string CardNo { get; set; } = "";
}

/// <summary>客户子源（Compose 跨源绑定演示子源）。</summary>
public class RcCustomerSub
{
    /// <summary>客户名（速记 FromSource 绑定目标 CustomerName）</summary>
    public string Name { get; set; } = "";

    /// <summary>客户手机号（块内 From+Mask 绑定目标 CustomerPhone）</summary>
    public string Phone { get; set; } = "";

    /// <summary>入会时间（块内 From+Format+Mask 绑定目标 JoinedAt——Compose 侧 FormatThenMask）</summary>
    public DateTime MemberSince { get; set; }
}

/// <summary>订单 DTO（Compose 运行时元数据演示目标）。</summary>
public class RcOrderDto
{
    /// <summary>主键</summary>
    public int Id { get; set; }

    /// <summary>订单号</summary>
    public string OrderNo { get; set; } = "";

    /// <summary>金额</summary>
    public decimal Amount { get; set; }

    /// <summary>分值文本（跨型 From：源 int Score → string）</summary>
    public string ScoreText { get; set; } = "";

    /// <summary>创建时间文本（From+Format+Mask——先格式化再脱敏；与源同名异型，跨型 From）</summary>
    public string CreatedAt { get; set; } = "";

    /// <summary>卡号脱敏（From+Mask+MapWhen——条件假则跳过赋值）</summary>
    public string CardMasked { get; set; } = "";

    /// <summary>客户名（Compose 子源速记绑定）</summary>
    public string CustomerName { get; set; } = "";

    /// <summary>客户手机号（Compose 块内 From+Mask）</summary>
    public string CustomerPhone { get; set; } = "";

    /// <summary>入会日期（Compose 块内 From+Format+Mask——FormatThenMask）</summary>
    public string JoinedAt { get; set; } = "";
}

/// <summary>
/// 年份掩码（FormatThenMask 组合演示——先格式化再脱敏）：保留前 4 位年份，月日替换为 **。
/// </summary>
public sealed class RcYearMask : IMask
{
    /// <summary>脱敏：保留年份前 4 位。</summary>
    /// <param name="value">格式化后的日期文本（yyyy-MM-dd）</param>
    /// <returns>脱敏文本</returns>
    public string Mask(string? value)
        => string.IsNullOrEmpty(value) ? "" : value.Length <= 4 ? value : value[..4] + "-**-**";
}

/// <summary>反向解析器（Format 的单向声明配套 ConvertBack——消除 NCMAP_F67 并闭合往返）。</summary>
public static class RcParsers
{
    /// <summary>解析 yyyy-MM-dd 日期文本。</summary>
    /// <param name="value">日期文本</param>
    /// <returns>解析结果</returns>
    public static DateTime ParseDate(string value)
        => DateTime.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Compose 运行时元数据演示 Mapper（对应架构图 `RuntimeComposeMapping` 行）——
/// 三类新语法叠加形态同链：
/// <list type="bullet">
/// <item>跨型 From：<c>.Member(d =&gt; d.ScoreText).From(s =&gt; s.Score)</c>（int → string 由生成代码内建转换承接）</item>
/// <item>From+Format+Mask：<c>.Member(d =&gt; d.CreatedAtText).From(s =&gt; s.CreatedAt).Format(..).Mask&lt;..&gt;()</c>（FormatThenMask）</item>
/// <item>From+Mask+MapWhen：<c>.Member(d =&gt; d.CardMasked).From(s =&gt; s.CardNo).Mask&lt;..&gt;().MapWhen(..)</c>（条件→转换→赋值）</item>
/// </list>
/// 另含 Compose 跨源声明（速记 + 分组块）——其明细在运行时由 <c>RuntimeComposeMapping</c> 承载，
/// 场景 109 经 <see cref="RuntimeSnapshotStore.TryGetFluentDetailMeta"/> 读取并断言。
/// </summary>
    #region RuntimeCompose_Declarations
[MapperConfig]
public class RcComposeMapper
{
    #endregion

    /// <summary>
    /// 链式声明入口（方法名任意，签名固定）。
    /// </summary>
    /// <param name="cfg">Fluent 配置根</param>
    #region RuntimeCompose_Declarations_Configure
    public static void Configure(FluentMappingConfig cfg)
    {
        cfg.For<RcOrderEntity, RcOrderDto>()
           // cross-type From: int source -> string target (a type pair with no viable conversion reports NCMAP027 at generation time)
           .Member(d => d.ScoreText).From(s => s.Score)
           // From + Format + Mask (FormatThenMask: format first, then mask)
           // From + Format + Mask + ConvertBack written as one chained segment (FormatThenMask forward; ConvertBack closes the round-trip and clears NCMAP_F67)
           .Member(d => d.CreatedAt).From(s => s.CreatedAt).Format("yyyy-MM-dd").Mask<RcYearMask>()
           .Member(d => d.CreatedAt).ConvertBack((Func<string, DateTime>)RcParsers.ParseDate)
           // From + Mask + MapWhen (triple stacking: condition -> mask -> assign; the condition looks at the source field value before conversion)
           .Member(d => d.CardMasked).From(s => s.CardNo).Mask<MaskBankCard>().MapWhen(v => !string.IsNullOrWhiteSpace(v))
           // Compose shorthand: bind a single field to a sub-source
           .Member(d => d.CustomerName).FromSource<RcCustomerSub>(s => s.Name)
           // Compose group block: multiple fields on the same sub-source (From + Mask / From + Format + Mask inside the block)
           .FromSource<RcCustomerSub>(c =>
           {
               c.Member(d => d.CustomerPhone).From(s => s.Phone).Mask<MaskPhone>();
               c.Member(d => d.JoinedAt).From(s => s.MemberSince).Format("yyyy-MM-dd").Mask<RcYearMask>();
           });
    }
}
    #endregion

/// <summary>
/// 场景 109 — Compose 运行时元数据（<c>RuntimeComposeMapping</c>）+ 新语法叠加组合演示。
/// <para>断言口径：输出值与元数据字段值均按精确期望值比较——写错值即 <see cref="OutputHelper.Error"/>，
/// 断言不恒真（元数据断言另设"不存在的目标属性应查不到"负向探针）。</para>
/// </summary>
public static class RuntimeComposeScenarios
{
    /// <summary>演示主源订单。</summary>
    /// <returns>订单实体</returns>
    public static RcOrderEntity NewOrder() => new()
    {
        Id = 9001,
        OrderNo = "ORD-RC-001",
        Amount = 1288.50m,
        Score = 42,
        CreatedAt = new DateTime(2026, 3, 5),
        CardNo = "6222021234567890123",
    };

    /// <summary>演示客户子源。</summary>
    /// <returns>客户子源</returns>
    public static RcCustomerSub NewCustomer() => new()
    {
        Name = "ZhaoLiu",
        Phone = "13700002222",
        MemberSince = new DateTime(2024, 11, 20),
    };

    /// <summary>
    /// 元数据读取探针 — 走用户可达门面 <see cref="RuntimeSnapshotStore.TryGetFluentDetailMeta"/>，
    /// 返回该 DTO 的 Compose 明细（无 Compose 声明时为空数组）。
    /// </summary>
    /// <param name="dtoType">目标 DTO 类型</param>
    /// <returns>(是否命中 on 态明细条目, Compose 明细)</returns>
    public static (bool Hit, RuntimeComposeMapping[] ComposeMappings) ReadComposeMetadata(Type dtoType)
    {
        if (!RuntimeSnapshotStore.TryGetFluentDetailMeta(dtoType, out var meta))
            return (false, Array.Empty<RuntimeComposeMapping>());
        return (true, meta.ComposeMappings);
    }

    /// <summary>按目标属性名索引 Compose 明细（未命中返回 null）。</summary>
    /// <param name="compose">Compose 明细数组</param>
    /// <param name="targetPropertyName">目标属性名</param>
    /// <returns>命中的明细；未命中 null</returns>
    public static RuntimeComposeMapping? FindByTarget(RuntimeComposeMapping[] compose, string targetPropertyName)
        => Array.Find(compose, c => c.TargetPropertyName == targetPropertyName);

    /// <summary>
    /// 场景调度 — 顺序执行全部子场景并输出。
    /// </summary>
    #region RuntimeCompose_Run
    public static void Run()
    {
        var order = NewOrder();
        var customer = NewCustomer();

        // 1. Primary face: Mapper.Map (FromSource fields keep defaults; all main-chain stacking forms take full effect)
        var primary = Mapper.Map<RcOrderEntity, RcOrderDto>(order)!;
        OutputHelper.Dump("109-compose-primary-face",
            new { order, customer },
            new { primary.ScoreText, primary.CreatedAt, primary.CardMasked, primary.CustomerName, primary.CustomerPhone, primary.JoinedAt });

        if (primary.ScoreText != "42")
            OutputHelper.Error($"109-compose-primary-face: cross-type From (int -> string) output mismatch (actual: \"{primary.ScoreText}\")");
        if (primary.CreatedAt != "2026-**-**")
            OutputHelper.Error($"109-compose-primary-face: FormatThenMask (From+Format+Mask) output mismatch (actual: \"{primary.CreatedAt}\")");
        if (primary.CardMasked != "6222****0123")
            OutputHelper.Error($"109-compose-primary-face: From+Mask+MapWhen output mismatch (actual: \"{primary.CardMasked}\")");
        if (primary.CustomerName != "" || primary.CustomerPhone != "" || primary.JoinedAt != "")
            OutputHelper.Error("109-compose-primary-face: FromSource fields should keep defaults on primary face (materialized split)");

        // 2. MapWhen false condition: blank card number -- skip assignment, CardMasked keeps default
        var blankCard = NewOrder();
        blankCard.CardNo = "   ";
        var blankMapped = Mapper.Map<RcOrderEntity, RcOrderDto>(blankCard)!;
        OutputHelper.Dump("109-compose-mapwhen-false", new { blankCard.CardNo }, new { blankMapped.CardMasked });
        if (blankMapped.CardMasked != "")
            OutputHelper.Error($"109-compose-mapwhen-false: false guard should skip assignment (actual: \"{blankMapped.CardMasked}\")");

        // 3. Compose face: main chain + sub-source composed in one pass (fields carried by Compose details take values here)
        var composed = Mapper.Compose<RcOrderDto>(order, customer)!;
        OutputHelper.Dump("109-compose-composed-face",
            new { order, customer },
            new { composed.CustomerName, composed.CustomerPhone, composed.JoinedAt, composed.ScoreText, composed.CreatedAt });

        if (composed.CustomerName != "ZhaoLiu")
            OutputHelper.Error($"109-compose-composed-face: shorthand FromSource binding mismatch (actual: \"{composed.CustomerName}\")");
        if (composed.CustomerPhone != "137****2222")
            OutputHelper.Error($"109-compose-composed-face: in-block From+Mask output mismatch (actual: \"{composed.CustomerPhone}\")");
        if (composed.JoinedAt != "2024-**-**")
            OutputHelper.Error($"109-compose-composed-face: in-block Format output mismatch (actual: \"{composed.JoinedAt}\")");

        // 4. Runtime metadata: ComposeMappings non-empty and key fields aligned item by item (RuntimeComposeMapping consumer face)
        var (hit, composeMappings) = ReadComposeMetadata(typeof(RcOrderDto));
        OutputHelper.Dump("109-runtime-compose-meta", new
        {
            dto = typeof(RcOrderDto).Name,
            hit,
            count = composeMappings.Length,
            mappings = Array.ConvertAll(composeMappings, c => new
            {
                c.TargetPropertyName,
                c.SubSourceTypeName,
                c.SourcePathNames,
                c.HasMask,
                c.MaskTypeName,
                c.HasConverter,
                c.IsFromJson,
                c.HasIndex,
                c.FormatString,
            }),
        });

        if (!hit)
        {
            OutputHelper.Error("109-runtime-compose-meta: TryGetFluentDetailMeta should hit the on-state Fluent detail entry for RcOrderDto");
            return;
        }
        if (composeMappings.Length != 3)
            OutputHelper.Error($"109-runtime-compose-meta: ComposeMappings should carry 3 rows (CustomerName/CustomerPhone/JoinedAt), actual: {composeMappings.Length}");

        // 4a. Shorthand binding: target property name / sub-source type / source path segments
        var nameCm = FindByTarget(composeMappings, "CustomerName");
        if (nameCm is null)
            OutputHelper.Error("109-runtime-compose-meta: ComposeMappings missing row for TargetPropertyName=\"CustomerName\"");
        else
        {
            if (nameCm.SubSourceTypeRef != typeof(RcCustomerSub))
                OutputHelper.Error($"109-runtime-compose-meta: CustomerName SubSourceTypeRef mismatch (actual: {nameCm.SubSourceTypeRef?.Name ?? "<null>"})");
            if (nameCm.SubSourceTypeName != typeof(RcCustomerSub).FullName)
                OutputHelper.Error($"109-runtime-compose-meta: CustomerName SubSourceTypeName mismatch (actual: \"{nameCm.SubSourceTypeName}\")");
            if (nameCm.SourcePathNames is not { Length: 1 } || nameCm.SourcePathNames[0] != "Name")
                OutputHelper.Error($"109-runtime-compose-meta: CustomerName SourcePathNames should be [\"Name\"] (actual: [{string.Join(",", nameCm.SourcePathNames)}])");
            if (nameCm.HasMask || nameCm.HasConverter || nameCm.IsFromJson || nameCm.HasIndex || nameCm.FormatString != null)
                OutputHelper.Error("109-runtime-compose-meta: CustomerName should carry no transform slot (HasMask/HasConverter/IsFromJson/HasIndex/FormatString all clear)");
        }

        // 4b. In-block From+Mask: the mask carrier lands on the Compose detail
        var phoneCm = FindByTarget(composeMappings, "CustomerPhone");
        if (phoneCm is null)
            OutputHelper.Error("109-runtime-compose-meta: ComposeMappings missing row for TargetPropertyName=\"CustomerPhone\"");
        else
        {
            if (phoneCm.SourcePathNames is not { Length: 1 } || phoneCm.SourcePathNames[0] != "Phone")
                OutputHelper.Error($"109-runtime-compose-meta: CustomerPhone SourcePathNames should be [\"Phone\"] (actual: [{string.Join(",", phoneCm.SourcePathNames)}])");
            if (!phoneCm.HasMask || phoneCm.MaskTypeName != typeof(MaskPhone).FullName)
                OutputHelper.Error($"109-runtime-compose-meta: CustomerPhone HasMask/MaskTypeName mismatch (HasMask={phoneCm.HasMask}, MaskTypeName=\"{phoneCm.MaskTypeName}\")");
        }

        // 4c. In-block From+Format+Mask: FormatString and Mask carried together (FormatThenMask on the Compose side)
        var joinedCm = FindByTarget(composeMappings, "JoinedAt");
        if (joinedCm is null)
            OutputHelper.Error("109-runtime-compose-meta: ComposeMappings missing row for TargetPropertyName=\"JoinedAt\"");
        else
        {
            if (joinedCm.SourcePathNames is not { Length: 1 } || joinedCm.SourcePathNames[0] != "MemberSince")
                OutputHelper.Error($"109-runtime-compose-meta: JoinedAt SourcePathNames should be [\"MemberSince\"] (actual: [{string.Join(",", joinedCm.SourcePathNames)}])");
            if (!joinedCm.HasMask || joinedCm.MaskTypeName != typeof(RcYearMask).FullName)
                OutputHelper.Error($"109-runtime-compose-meta: JoinedAt HasMask/MaskTypeName mismatch (HasMask={joinedCm.HasMask}, MaskTypeName=\"{joinedCm.MaskTypeName}\")");
            if (joinedCm.FormatString != "yyyy-MM-dd")
                OutputHelper.Error($"109-runtime-compose-meta: JoinedAt FormatString should be \"yyyy-MM-dd\" (actual: \"{joinedCm.FormatString}\")");
        }

        // 4d. Negative probe: undeclared target properties are unreachable in the details (guards against tautological asserts of the "index always hits" kind)
        if (FindByTarget(composeMappings, "NoSuchProperty") is not null)
            OutputHelper.Error("109-runtime-compose-meta: FindByTarget should miss for undeclared target property \"NoSuchProperty\"");

        // 5. Read-facade semantics for non-Compose DTOs: unregistered type -- false (conservative degradation path on the consumer side)
        if (RuntimeSnapshotStore.TryGetFluentDetailMeta(typeof(string), out _))
            OutputHelper.Error("109-runtime-compose-meta: TryGetFluentDetailMeta should return false for unregistered type System.String");
    }
    #endregion
}
