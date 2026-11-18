using System.Globalization;
using NCMapper;
using NCMapper.Attributes;

namespace NCMapper.Samples.Fluent;

// ================================================================
// 02 Fluent Convert — 模型、静态转换器与 Mapper 配置类
// Convert 四形态（正向）/ ConvertBack 四形态（反向）/ 同锚点配对 /
// 方向性忽略 / 全局忽略豁免门 / FromJson / 反向转换（T 侧锚 ConvertBack + action 写回）。
// 源侧锚点均采用同名属性形态（Fluent Mask 源侧仅同名解析——执行期发现已登记）。
// ================================================================

/// <summary>转换演示订单实体（Convert/ConvertBack 四形态 + 配对源）。</summary>
public class CvConvEntity
{
    /// <summary>主键</summary>
    public int Id { get; set; }

    /// <summary>订单号</summary>
    public string OrderNo { get; set; } = "";

    /// <summary>客户名</summary>
    public string CustomerName { get; set; } = "";

    /// <summary>年龄段（T1 字段值源，int）</summary>
    public int AgeBand { get; set; }

    /// <summary>整要源占位（T2 整源形态同名锚点——值不读取）</summary>
    public string FullSummary { get; set; } = "";

    /// <summary>修改时间（T3 组合形态字段源）</summary>
    public DateTime ModifiedText { get; set; }

    /// <summary>创建时间（配对场景字段源，DateTime→string）</summary>
    public DateTime CreatedText { get; set; }

    /// <summary>审计标签（ConvertBack T0 写回点）</summary>
    public string AuditTag { get; set; } = "";

    /// <summary>创建时间（ConvertBack T1 写回点，DateTime）</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>变更备注（ConvertBack T2 写回点）</summary>
    public string ChangeNote { get; set; } = "";

    /// <summary>组合键（ConvertBack T3 写回点）</summary>
    public string CompositeKey { get; set; } = "";

    /// <summary>批次号（Convert T0 常量形态同名锚点——值不读取）</summary>
    public string BatchNo { get; set; } = "";
}

/// <summary>转换演示订单 DTO。</summary>
public class CvConvDto
{
    /// <summary>主键</summary>
    public int Id { get; set; }

    /// <summary>订单号</summary>
    public string OrderNo { get; set; } = "";

    /// <summary>客户名</summary>
    public string CustomerName { get; set; } = "";

    /// <summary>年龄段（T1：int → string）</summary>
    public string AgeBand { get; set; } = "";

    /// <summary>整要（T2 整源多字段组合产物）</summary>
    public string FullSummary { get; set; } = "";

    /// <summary>修改时间（T3：DateTime → string）</summary>
    public string ModifiedText { get; set; } = "";

    /// <summary>创建文本（配对场景：Convert 正向 + ConvertBack 反向同锚点）</summary>
    public string CreatedText { get; set; } = "";

    /// <summary>审计标签（ConvertBack T0 常量写入源）</summary>
    public string AuditTag { get; set; } = "";

    /// <summary>创建文本（ConvertBack T1 锚点，string → DateTime 解析）</summary>
    public string CreatedAt { get; set; } = "";

    /// <summary>变更备注（ConvertBack T2 整 DTO 入参）</summary>
    public string ChangeNote { get; set; } = "";

    /// <summary>组合键（ConvertBack T3 双入参）</summary>
    public string CompositeKey { get; set; } = "";

    /// <summary>批次号（Convert T0 常量产物）</summary>
    public string BatchNo { get; set; } = "";
}

/// <summary>反向转换演示实体（ConvertBack 四形态 + 配对写回点；属性与 DTO 同型）。</summary>
public class CvPairEntity
{
    /// <summary>主键</summary>
    public int Id { get; set; }

    /// <summary>创建时间（CB T1 写回点 + 配对正向源，DateTime）</summary>
    public DateTime CreatedText { get; set; }

    /// <summary>审计标签（CB T0 写回点）</summary>
    public string AuditTag { get; set; } = "";

    /// <summary>变更备注（CB T2 写回点）</summary>
    public string ChangeNote { get; set; } = "";

    /// <summary>组合键（CB T3 写回点）</summary>
    public string CompositeKey { get; set; } = "";
}

/// <summary>反向转换演示 DTO（属性与实体同型——反向演示用）。</summary>
public class CvPairDto
{
    /// <summary>主键</summary>
    public int Id { get; set; }

    /// <summary>创建文本（CB T1 锚点 + 配对正向产物，string）</summary>
    public string CreatedText { get; set; } = "";

    /// <summary>审计标签（CB T0 常量写入源）</summary>
    public string AuditTag { get; set; } = "";

    /// <summary>变更备注（CB T2 整 DTO 入参）</summary>
    public string ChangeNote { get; set; } = "";

    /// <summary>组合键（CB T3 双入参）</summary>
    public string CompositeKey { get; set; } = "";
}

/// <summary>方向忽略演示实体（Ignore 方向 + OverrideIgnore 豁免门源）。</summary>
public class CvDirectionEntity
{
    /// <summary>主键</summary>
    public int Id { get; set; }

    /// <summary>标签（ToDto 方向忽略）</summary>
    public string Tag { get; set; } = "";

    /// <summary>审计注（ToEntity 方向忽略）</summary>
    public string AuditNote { get; set; } = "";

    /// <summary>行版本（全局 IgnoreNames 命中——OverrideIgnore 豁免演示）</summary>
    public string RowVersion { get; set; } = "";

    /// <summary>行版本对照</summary>
    public string RowVersionPlain { get; set; } = "";
}

/// <summary>方向忽略演示 DTO。</summary>
public class CvDirectionDto
{
    /// <summary>主键</summary>
    public int Id { get; set; }

    /// <summary>标签</summary>
    public string Tag { get; set; } = "";

    /// <summary>审计注</summary>
    public string AuditNote { get; set; } = "";

    /// <summary>行版本（豁免门：全局忽略命中仍生成映射）</summary>
    public string RowVersion { get; set; } = "";

    /// <summary>行版本对照（无豁免——保持默认）</summary>
    public string RowVersionPlain { get; set; } = "";
}

/// <summary>JSON 载荷（FromJson 反序列化目标）。</summary>
public class CvPayload
{
    /// <summary>城市</summary>
    public string City { get; set; } = "";

    /// <summary>邮编</summary>
    public string Zip { get; set; } = "";
}

/// <summary>实体侧演示实体（方向性忽略/反向转换/FromJson 源）。</summary>
public class CvSideEntity
{
    /// <summary>主键</summary>
    public int Id { get; set; }

    /// <summary>密钥（方向性忽略双向拦截）</summary>
    public string SecretCode { get; set; } = "";

    /// <summary>遗留注（ToDto 仅正向忽略）</summary>
    public string LegacyNote { get; set; } = "";

    /// <summary>JSON 载荷（FromJson 源侧 string）</summary>
    public string Payload { get; set; } = "";

    /// <summary>创建时间（ConvertBack 反向解析写回点）</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>同步戳（action 重载反向写回点——时点性动作显式化）</summary>
    public string SyncStamp { get; set; } = "";
}

/// <summary>实体侧锚点演示 DTO。</summary>
public class CvSideDto
{
    /// <summary>主键</summary>
    public int Id { get; set; }

    /// <summary>密钥（方向性忽略拦截——正向保持默认）</summary>
    public string SecretCode { get; set; } = "";

    /// <summary>遗留注（ToDto 仅正向忽略）</summary>
    public string LegacyNote { get; set; } = "";

    /// <summary>载荷对象（FromJson 反序列化产物）</summary>
    public CvPayload Payload { get; set; } = new();

    /// <summary>创建文本（ConvertBack T 侧锚点属性，string）</summary>
    public string CreatedAt { get; set; } = "";
}

/// <summary>
/// 静态转换器组 —— 全部为 public static（生成代码同程序集可见；private 报 NCMAP_F25/F19）。
/// </summary>
public static class CvConverters
{
    /// <summary>Convert T0 常量形态（无参）。</summary>
    public static string BatchNo() => "BATCH-2026";

    /// <summary>Convert T1 字段值形态（int → string）。</summary>
    public static string ToBand(int age) => age >= 30 ? "senior" : "junior";

    /// <summary>Convert T2 整源形态（多字段组合；委托入参 TSource 类级固定——方法组可直用）。</summary>
    public static string BuildSummary(CvConvEntity src) => $"{src.OrderNo}/{src.CustomerName}";

    /// <summary>Convert T3 组合形态（整源 + 字段值双入参）。</summary>
    public static string FormatModified(CvConvEntity src, DateTime modified)
        => $"mod@{modified:yyyy-MM-dd}";

    /// <summary>ConvertBack T0 常量形态（反向写固定值）。</summary>
    public static string AuditTagConst() => "AUDIT-001";

    /// <summary>ConvertBack T1 单值形态（string → DateTime 解析）。</summary>
    public static DateTime ParseCreatedAt(string text)
        => DateTime.Parse(text, CultureInfo.InvariantCulture);

    /// <summary>ConvertBack T2 整源形态（整个 DTO 入参）。</summary>
    public static string BuildChangeNote(CvPairDto dto) => $"note:{dto.CreatedText}";

    /// <summary>ConvertBack T3 组合形态（整 DTO + 字段值双入参）。</summary>
    public static string BuildCompositeKey(CvPairDto dto, string field) => $"{dto.Id}:{field}";

    /// <summary>配对场景正向（DateTime → string 格式化）。</summary>
    public static string FormatCreated(DateTime created)
        => created.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>配对场景反向（string → DateTime 解析，与正向互逆）。</summary>
    public static DateTime ParseCreated(string text)
        => DateTime.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>反向写回合成（整个 DTO 入参；供 action 重载调用）。</summary>
    public static string BuildSyncStamp(CvSideDto dto) => $"sync@{dto.Id}";
}

/// <summary>
/// 转换演示 Mapper 配置 —— 四条 For 链承载八类场景（同一 (S,T) 对的全部声明须并入单链，F16）。
/// <para>Convert 四形态（对 A1）与 ConvertBack 四形态（对 A2）分对承载：正向显式 Convert 声明
/// 反向翻转时不消费（特性侧 F3 同语义——反向按类型转换路径处理，跨类型 string→int 无路径将抛
/// MapException），故反向演示使用同型属性对。</para>
/// </summary>
[MapperConfig]
public class CvMapper
{
    /// <summary>
    /// 链式声明入口。
    /// </summary>
    /// <param name="cfg">Fluent 配置根</param>
    public static void Configure(FluentMappingConfig cfg)
    {
        #region FluentConvert_Convert
        // Pair A1: four Convert forms (T0 constant / T1 field value / T2 whole source / T3 composite) -- forward demo
        cfg.For<CvConvEntity, CvConvDto>()
           .Member(d => d.FullSummary).Convert(CvConverters.BuildSummary)                      // T2 whole source (method group used directly)
           .Member(d => d.BatchNo).Convert(CvConverters.BatchNo)                               // T0 constant (method group used directly)
           .Member(d => d.AgeBand).Convert<int>(CvConverters.ToBand)                   // T1 field value (explicit type arg -- inference guidance)
           .Member(d => d.ModifiedText).Convert<DateTime>(CvConverters.FormatModified); // T3 composite (explicit type arg)
        #endregion

        #region FluentConvert_ConvertBack
        // Pair A2: four ConvertBack forms + same-anchor pairing (reverse demo; write-back point resolves by the entity-side same name)
        cfg.For<CvPairEntity, CvPairDto>()
           .Member(d => d.AuditTag).ConvertBack<string>(CvConverters.AuditTagConst)                    // CB T0 constant
           .Member(d => d.CreatedText).ConvertBack((Func<string, DateTime>)CvConverters.ParseCreated)          // CB T1 single value (no inference at input position -- explicit delegate)
           .Member(d => d.ChangeNote).ConvertBack(CvConverters.BuildChangeNote)                // CB T2 whole DTO (TTarget fixed at class level -- used directly)
           .Member(d => d.CompositeKey).ConvertBack((Func<CvPairDto, string, string>)CvConverters.BuildCompositeKey) // CB T3 composite (explicit delegate)
           .Member(d => d.CreatedText).Convert<DateTime>(CvConverters.FormatCreated);  // paired forward direction (coexists with CB T1 on the same anchor)
        #endregion

        #region FluentConvert_Ignore
        // Pair B: directional ignore + global-ignore exemption gate
        cfg.For<CvDirectionEntity, CvDirectionDto>()
           .Member(d => d.Tag).Ignore(Direction.ToDto)        // ignore forward direction only (reverse still derives as usual)
           .Member(d => d.AuditNote).Ignore(Direction.ToEntity) // ignore reverse direction only (forward still maps as usual)
           .Member(d => d.RowVersion).From(s => s.RowVersion, overrideIgnore: true); // global IgnoreNames hit still generates the mapping
        #endregion

        #region FluentConvert_FromJson
        // Pair C: directional ignore (T-side anchor) + FromJson + ConvertBack reverse
        cfg.For<CvSideEntity, CvSideDto>()
           .Member(d => d.SecretCode).Ignore()                                 // both directions: no forward mapping, reverse write-back skipped (matching pair: T-side equivalent)
           .Member(d => d.LegacyNote).Ignore(Direction.ToDto)                // ignore forward direction only
           .Member(d => d.Payload).FromJson()                                  // source-side same-name string -> complex type
           .Member(d => d.CreatedAt).ConvertBack((Func<string, DateTime>)CvConverters.ParseCreatedAt); // reverse parsing (T-side anchor -- string -> DateTime)
        // SyncStamp reverse write-back migrated to the action overload (call-site level explicit -- see FluentConvertScenarios.ConvertBackProbe).
        #endregion
    }
}
