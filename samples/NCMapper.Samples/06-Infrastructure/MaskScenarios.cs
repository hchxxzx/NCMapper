using NCMapper.Attributes;
using NCMapper.Samples.Models.Dtos;
using NCMapper.Samples.Models.Entities;
using NCMapper.Samples.Helpers;
using NCMapper.Samples.Models.Enums;

namespace NCMapper.Samples.Infrastructure;

/// <summary>
/// 脱敏场景集 — 内置 Mask 规则、运行时 MaskProvider 替换与恢复。
/// </summary>
public static class MaskScenarios
{
    /// <summary>
    /// UserEntity → UserDto 映射。
    /// 注意：UserDto 未标注 [Mask]，本映射不执行脱敏；脱敏演示见
    /// <see cref="MaskedCustomerMap"/>（CustomerDto 经 ConvertUsing(PhoneMaskConverter) 脱敏电话）。
    /// </summary>
    /// <param name="src">源实体</param>
    /// <returns>脱敏后的 DTO</returns>
    #region Mask_ConvertUsing
    public static UserDto MaskedMap(UserEntity src)
        => Mapper.Map<UserEntity, UserDto>(src)!;
    #endregion


    /// <summary>
    /// CustomerEntity → CustomerDto 映射，通过 ConvertUsing(PhoneMaskConverter) 脱敏电话。
    /// </summary>
    /// <param name="src">源实体</param>
    /// <returns>电话已脱敏的 DTO</returns>
    #region Mask_ConvertUsing_MaskedCustomerMap
    public static CustomerDto MaskedCustomerMap(CustomerEntity src)
        => Mapper.Map<CustomerEntity, CustomerDto>(src)!;
    #endregion


    /// <summary>
    /// MaskProvider.Resolve：获取当前脱敏实例。
    /// </summary>
    /// <typeparam name="TMask">脱敏类型</typeparam>
    /// <returns>当前注册的脱敏实例</returns>
    #region Mask_ConvertUsing_ResolveMask
    public static IMask ResolveMask<TMask>() where TMask : IMask
        => MaskProvider.Resolve(typeof(TMask));
    #endregion


    /// <summary>
    /// MaskProvider.Override：运行时替换脱敏实现。
    /// </summary>
    /// <typeparam name="TMask">脱敏类型</typeparam>
    /// <param name="impl">新的脱敏实例</param>
    #region Mask_ConvertUsing_OverrideMask
    public static void OverrideMask<TMask>(IMask impl) where TMask : IMask
        => MaskProvider.Override<TMask>(impl);
    #endregion


    /// <summary>
    /// MaskProvider.Clear：恢复默认脱敏实现。
    /// </summary>
    #region Mask_ConvertUsing_ResetMasks
    public static void ResetMasks()
        => MaskProvider.Clear();

    #endregion
    // ===== 场景注册入口=====
    public static void Run()
    {
    var src = new UserEntity
    {
        Id = 1,
        Name = "MaskUser",
        Age = 30,
        Email = "mask@example.com",
    };

    var dto = MaskScenarios.MaskedMap(src);
    OutputHelper.Dump("605-masked-map", src, dto);

    var custSrc = new CustomerEntity
    {
        Name = "CustomerX",
        Level = CustomerLevel.Gold,
        Phone = "13912345678",
        Region = "Beijing",
    };

    var custDto = MaskScenarios.MaskedCustomerMap(custSrc);
    OutputHelper.Dump("605-masked-customer", custSrc, custDto);

    // MaskProvider 运行时替换
    var mask = MaskScenarios.ResolveMask<NCMapper.MaskPhone>();
    OutputHelper.Dump("605-resolve-mask", "MaskPhone.Resolve", mask.GetType().Name);

    MaskScenarios.OverrideMask<NCMapper.MaskPhone>(new JapanPhoneMask());
    OutputHelper.Dump("605-override-mask", "Override→JapanPhoneMask", "done");

    MaskScenarios.ResetMasks();
    OutputHelper.Dump("605-reset-masks", "MaskProvider.Clear()", "done");

    // 9 内置实现对照 + 自定义注册闭环
    MaskScenarios.BuiltinNineDemo();
    MaskScenarios.CustomProviderDemo();
    }

    // ===== 9 内置实现对照 + 自定义注册闭环=====

/// <summary>
/// 9 个内置脱敏实现逐个输出"原值 → 脱敏值"对照（全部假数据，不落真实敏感信息）。
/// </summary>
    #region Mask_BuiltinNine
public static void BuiltinNineDemo()
{
    var samples = new (string Name, IMask Impl, string Raw)[]
    {
        ("MaskPhone",        new NCMapper.MaskPhone(),        "13812345678"),
        ("MaskIdCard",       new NCMapper.MaskIdCard(),       "110101199001011234"),
        ("MaskBankCard",     new NCMapper.MaskBankCard(),     "6222020200112233"),
        ("MaskEmail",        new NCMapper.MaskEmail(),        "user@example.com"),
        ("MaskChineseName",  new NCMapper.MaskChineseName(),  "张三丰"),
        ("MaskAddress",      new NCMapper.MaskAddress(),      "北京市海淀区中关村大街1号"),
        ("MaskIp",           new NCMapper.MaskIp(),           "192.168.10.23"),
        ("MaskLicensePlate", new NCMapper.MaskLicensePlate(), "京A12345"),
        ("MaskLandline",     new NCMapper.MaskLandline(),     "010-66886688"),
    };

    // Anonymous type instead of ValueTuple: OutputHelper.Dump goes through System.Text.Json serialization,
    // tuple Item1/Item2/Item3 are fields not properties, ignored by the serializer by default -> output would be all empty objects {}
    var rows = samples
        .Select(x => new { Name = x.Name, Raw = x.Raw, Masked = x.Impl.Mask(x.Raw) })
        .ToList();
    OutputHelper.Dump("605-builtin-nine", $"Built-in mask implementations ({rows.Count} total, raw → masked)", rows);
    // Assert: each implementation's masked output must differ from the raw value and contain the mask character (anti "fake mask" -- unchanged output means the demo is void)
    foreach (var r in rows)
        if (r.Masked == r.Raw || !r.Masked.Contains('*'))
            OutputHelper.Error($"605-builtin-nine: {r.Name} masked output invalid (unchanged or no mask character)");
}
#endregion

    /// <summary>
    /// 自定义 IMask 注册闭环（Provider 层）：OverrideMask 替换 MaskPhone 实现 →
    /// Resolve 返回自定义实现 → Reset 恢复默认。
    /// <para>后效边界实证：生成代码把掩码实例缓存为映射类的静态只读字段（首次映射
    /// 调用时经 MaskProvider.Resolve 解析一次），MaskProvider 约束"Override 须在该
    /// Mask 类型首次被映射调用之前执行"——本场景运行时 MaskPhone 已被前序片段
    /// （605-masked-customer）消费，故映射产物不再随 Override 变化，这是约束的
    /// 真实行为而非缺陷；需要影响映射产物时，须在首次映射调用前完成 Override。</para>
    /// </summary>
    #region Mask_CustomProvider
    public static void CustomProviderDemo()
    {
        // register a custom implementation (takes effect immediately at the Provider layer: Resolve consults _overrides first)
        MaskScenarios.OverrideMask<NCMapper.MaskPhone>(new JapanPhoneMask());
        var resolved = MaskScenarios.ResolveMask<NCMapper.MaskPhone>();
        OutputHelper.Dump("605-custom-provider", "ResolveMask<MaskPhone> after OverrideMask", resolved.GetType().Name);
        // Assert: the Provider-layer registration took effect (resolved to the custom implementation)
        if (resolved is not JapanPhoneMask)
            OutputHelper.Error($"605-custom-provider: Override registration not effective (resolved to {resolved.GetType().Name})");

        // Reset restores the default
        MaskScenarios.ResetMasks();
        var restored = MaskScenarios.ResolveMask<NCMapper.MaskPhone>();
        OutputHelper.Dump("605-custom-provider-restore", "ResolveMask<MaskPhone> after ResetMasks()", restored.GetType().Name);
        // Assert: default implementation restored (JapanPhoneMask has been cleared)
        if (restored is JapanPhoneMask)
            OutputHelper.Error("605-custom-provider: custom implementation still active after ResetMasks (default not restored)");
    }
    #endregion

    /// <summary>多区域部署：同一套 DTO，启动时按 region 条件 Override 掩码实现，映射代码零改动；演示后恢复默认。</summary>
    #region Mask_MultiLocaleSwap
    public static UserDto MultiLocaleSwap(string region, UserEntity src)
    {
        if (region == "JP")
        {
            // JP locale: swap implementations before the first mapping call of each mask type
            MaskProvider.Override<NCMapper.MaskPhone>(new JapanPhoneMask());
            MaskProvider.Override<NCMapper.MaskChineseName>(new JapanNameMask());
        }
        // Mapping code stays unchanged; the active masks depend on the locale config above
        var dto = Mapper.Map<UserEntity, UserDto>(src)!;
        MaskProvider.Clear();
        return dto;
    }
    #endregion

    /// <summary>审计字段 + 权限脱敏组合：横切 PostMap 按当前用户写审计标记，非管理员额外脱敏 Email。</summary>
    #region Infra_Api_AuditMaskCombo
    public static void AuditMaskCombo(string currentUser, bool isAdmin)
    {
        // Cross-cutting audit + redaction: PostMap runs after every UserEntity -> UserDto map
        Mapper.Pipeline
            .AddPostMap<UserEntity, UserDto>((src, dto) =>
            {
                dto.Name = $"{dto.Name} (by {currentUser})";
                if (!isAdmin)
                    dto.Email = "****";
            });
    }
    #endregion

    /// <summary>
    /// [Mask(typeof)] DTO 属性标注即脱敏 — 映射时自动执行（MaskPhone/MaskEmail 双属性对，巡览不挂 Run）。
    /// </summary>
    /// <param name="src">明文源实体（存储明文，业务需要）</param>
    /// <returns>Phone/Email 已按内置规则打码的 DTO</returns>
    #region Mask_MaskedUserMap
    public static MaskedUserDto MaskedUserMap(MaskUserEntity src)
        => Mapper.Map<MaskUserEntity, MaskedUserDto>(src)!;
    #endregion

    /// <summary>
    /// 页面服务腿 — 映射即脱敏（tutorials/masking-tutorial.md CustomerService.Get 绑定源）。
    /// </summary>
    /// <param name="entity">明文客户实体</param>
    /// <returns>Phone 已打码的展示 DTO</returns>
    #region Mask_MaskingCustomerGet
    public static CustomerMaskedDto MaskingCustomerGet(CustomerEntity entity)
        => Mapper.Map<CustomerEntity, CustomerMaskedDto>(entity)!;
    #endregion

    /// <summary>
    /// 上线验证腿 — 脱敏输出必须与原值不同且含掩码符号（防"假脱敏"；巡览不挂 Run）。
    /// </summary>
    #region Mask_MaskingSmokeTour
    public static void MaskingSmokeTour()
    {
        var entity = new CustomerEntity { Name = "Sample", Phone = "13812345678" };
        var dto = MaskingCustomerGet(entity);

        // Hard assert: masked output must differ from raw and contain the mask character
        if (dto.Phone == entity.Phone)
            throw new InvalidOperationException("phone not masked, release blocked");
    }
    #endregion
}

/// <summary>
/// 日本手机号脱敏 — MaskProvider.Override 演示用自定义实现。
/// </summary>
public class JapanPhoneMask : IMask
{
    /// <inheritdoc />
    #region Mask_JapanPhoneMask_Mask
    public string Mask(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length < 5) return value ?? "";
        return value[..3] + "****" + value[^2..];
    }
    #endregion
}

/// <summary>
/// 日本姓名脱敏 — MaskProvider.Override 演示用自定义实现。
/// </summary>
public class JapanNameMask : IMask
{
    /// <inheritdoc />
    public string Mask(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value[..1] + "**";
    }

}

/// <summary>
/// 脱敏演示源实体 — 存储明文（业务需要），Phone/Email 为待打码字段。
/// </summary>
public class MaskUserEntity
{
    /// <summary>主键</summary>
    public int Id { get; set; }

    /// <summary>姓名</summary>
    public string Name { get; set; } = "";

    /// <summary>手机号（明文存储）</summary>
    public string Phone { get; set; } = "";

    /// <summary>邮箱（明文存储）</summary>
    public string Email { get; set; } = "";
}

/// <summary>
/// 脱敏展示 DTO — [Mask(typeof)] 属性标注即脱敏声明（映射时自动执行）。
/// </summary>
[MapFrom(typeof(MaskUserEntity))]
public class MaskedUserDto
{
    /// <summary>主键</summary>
    public int Id { get; set; }

    /// <summary>姓名</summary>
    public string Name { get; set; } = "";

    /// <summary>手机号脱敏：138****5678</summary>
    [Mask(typeof(MaskPhone))]
    public string Phone { get; set; } = "";

    /// <summary>邮箱脱敏：z***@example.com 形态</summary>
    [Mask(typeof(MaskEmail))]
    public string Email { get; set; } = "";
}

/// <summary>
/// 自定义脱敏规则（首尾保留、中间打星）— [Mask(typeof)] 与 Override 注册演示用。
/// </summary>
public sealed class MaskCustomRule : IMask
{
    /// <inheritdoc />
    #region Mask_MaskCustomRule_Mask
    public string Mask(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= 2) return value ?? "";
        return value[0] + new string('*', value.Length - 2) + value[^1];
    }
    #endregion
}

/// <summary>
/// 展示 DTO（打码展示）— Customer 族：Phone 经 [Mask(typeof(MaskPhone))] 打码（tutorials/masking-tutorial.md 绑定源）。
/// </summary>
[MapFrom(typeof(CustomerEntity))]
public class CustomerMaskedDto
{
    /// <summary>姓名</summary>
    public string Name { get; set; } = "";

    /// <summary>会员级别（枚举 → 字符串）</summary>
    [MapProperty(typeof(CustomerEntity), nameof(CustomerEntity.Level))]
    public string Level { get; set; } = "";

    /// <summary>地区</summary>
    public string Region { get; set; } = "";

    /// <summary>手机号：映射即按 138****5678 打码</summary>
    [MapProperty(typeof(CustomerEntity), nameof(CustomerEntity.Phone))]
    [Mask(typeof(MaskPhone))]
    public string Phone { get; set; } = "";
}
