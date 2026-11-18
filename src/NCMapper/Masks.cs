namespace NCMapper;

/// <summary>
/// 文本元素安全切片 — 避免切断代理对/组合字符（U+20000+ 生僻字、emoji）
/// </summary>
internal static class MaskHelper
{
    /// <summary>
    /// 取前部。
    /// </summary>
    /// <param name="value">指定值。</param>
    /// <param name="count">指定数量。</param>
    /// <returns>返回字符串。</returns>
    public static string TakeFront(string value, int count)
    {
        var si = new System.Globalization.StringInfo(value);
        return si.SubstringByTextElements(0, Math.Min(count, si.LengthInTextElements));
    }

    /// <summary>
    /// 取尾部。
    /// </summary>
    /// <param name="value">指定值。</param>
    /// <param name="count">指定数量。</param>
    /// <returns>返回字符串。</returns>
    public static string TakeBack(string value, int count)
    {
        var si = new System.Globalization.StringInfo(value);
        var len = si.LengthInTextElements;
        var take = Math.Min(count, len);
        return si.SubstringByTextElements(len - take, take);
    }

    /// <summary>
    /// 文本长度。
    /// </summary>
    /// <param name="value">指定值。</param>
    /// <returns>返回整数。</returns>
    public static int TextLength(string value)
    {
        // 全 ASCII 快路径——char 即文本元素，免 StringInfo 构造与枚举
        foreach (var ch in value)
            if (ch >= 0x80) return new System.Globalization.StringInfo(value).LengthInTextElements;
        return value.Length;
    }

    /// <summary>
    /// 中间星号段脱敏— 保留前 keepFront 与后 keepBack 文本元素，中间替换为星号。
    /// fixedStars 非 null 时星号数固定；否则按实际长度 - keepFront - keepBack 动态计算。
    /// </summary>
    /// <param name="value">原始字符串</param>
    /// <param name="keepFront">保留的前部文本元素数</param>
    /// <param name="keepBack">保留的后部文本元素数（0 表示不保留尾部）</param>
    /// <param name="fixedStars">固定星号数；null 时按实际长度动态计算</param>
    /// <returns>脱敏后的字符串</returns>
    public static string MaskMiddle(string value, int keepFront, int keepBack, int? fixedStars = null)
    {
        // 全 ASCII 快路径——手机号/证件号/卡号等常态输入，
        // char 即文本元素，免 StringInfo 分配与文本元素枚举（与慢路径语义逐字符一致）
        bool ascii = true;
        foreach (var ch in value)
            if (ch >= 0x80) { ascii = false; break; }

        if (ascii)
        {
            var len = value.Length;
            var stars = fixedStars ?? len - keepFront - keepBack;
            var tailTake = keepBack > 0 ? Math.Min(keepBack, len) : 0;
            return string.Concat(
                value.AsSpan(0, Math.Min(keepFront, len)),
                new string('*', stars < 0 ? 0 : stars),
                value.AsSpan(len - tailTake, tailTake));
        }

        // 非 ASCII：单次 StringInfo 复用三段结果（原 TextLength+TakeFront+TakeBack
        // 三次独立构造 → 一次），代理对/组合字符语义与原实现逐字节一致
        var si = new System.Globalization.StringInfo(value);
        var tlen = si.LengthInTextElements;
        var stars2 = fixedStars ?? tlen - keepFront - keepBack;
        var front = si.SubstringByTextElements(0, Math.Min(keepFront, tlen));
        var tail2 = keepBack > 0 ? si.SubstringByTextElements(tlen - Math.Min(keepBack, tlen), Math.Min(keepBack, tlen)) : string.Empty;
        return $"{front}{new string('*', stars2 < 0 ? 0 : stars2)}{tail2}";
    }

    /// <summary>
    /// 降级掩码形态——非规范输入
    /// （non-IPv4 / 无@ / 短串）不再明文返回：长度 ≥2 保留首文本元素 + 固定 4 星；
    /// 长度 1 全遮（无部分泄漏面）；空串原样。fail-safe：宁可过度脱敏不泄漏原文。
    /// </summary>
    /// <param name="value">非空输入</param>
    /// <returns>降级掩码结果</returns>
    internal static string DegradedMask(string value)
    {
        if (value.Length == 0) return value;
        // 全 ASCII 快路径（同款）：char 即文本元素
        bool ascii = true;
        foreach (var ch in value)
            if (ch >= 0x80) { ascii = false; break; }
        if (ascii) return value.Length == 1 ? "****" : $"{value[..1]}****";

        var si = new System.Globalization.StringInfo(value);
        return si.LengthInTextElements == 1
            ? "****"
            : $"{si.SubstringByTextElements(0, 1)}****";
    }
}

/// <summary>
/// 手机号脱敏：138****5678（保留前3后4，中间替换为 ****）
/// </summary>
public sealed class MaskPhone : IMask
{
    /// <summary>
    /// 手机号脱敏：保留前3后4，中间4位替换为 ****
    /// </summary>
    /// <param name="value">原始手机号</param>
    /// <returns>脱敏后的手机号；长度 ≤7 位时降级掩码（首字符+****，单字符全遮——A3-11）</returns>
    /// <remarks>
    /// 阈值须严格大于 keepFront+keepBack（3+4=7）——取 ≥7 时 7 位输入保留段恰好
    /// 覆盖全串，固定 4 星纯装饰（"1385678" → "138****5678"）而原文全量泄漏；≤7 走降级掩码 fail-safe。
    /// </remarks>
    public string Mask(string? value)
        => value is not null && MaskHelper.TextLength(value) > 7 ? MaskHelper.MaskMiddle(value, 3, 4, 4)
        : value is null ? "" : MaskHelper.DegradedMask(value);
}

/// <summary>
/// 身份证脱敏：1101**********1234（保留前4后4，中间按实际位数替换为等量 *）
/// </summary>
public sealed class MaskIdCard : IMask
{
    /// <summary>
    /// 身份证号脱敏：保留前4后4，中间按实际位数替换为等量 *
    /// </summary>
    /// <param name="value">原始身份证号</param>
    /// <returns>脱敏后的身份证号；长度不足10位时降级掩码（首字符+****，单字符全遮——A3-11）</returns>
    public string Mask(string? value)
        => value is not null && MaskHelper.TextLength(value) >= 10 ? MaskHelper.MaskMiddle(value, 4, 4)
        : value is null ? "" : MaskHelper.DegradedMask(value);
}

/// <summary>
/// 银行卡脱敏：6222****1234（保留前4后4，中间替换为 ****）
/// </summary>
public sealed class MaskBankCard : IMask
{
    /// <summary>
    /// 银行卡号脱敏：保留前4后4，中间替换为 ****
    /// </summary>
    /// <param name="value">原始银行卡号</param>
    /// <returns>脱敏后的银行卡号；长度不足10位时降级掩码（首字符+****，单字符全遮——A3-11）</returns>
    public string Mask(string? value)
        => value is not null && MaskHelper.TextLength(value) >= 10 ? MaskHelper.MaskMiddle(value, 4, 4, 4)
        : value is null ? "" : MaskHelper.DegradedMask(value);
}

/// <summary>
/// 邮箱脱敏：z***@example.com（保留首字符和@后域名，中间替换为 ***）
/// </summary>
public sealed class MaskEmail : IMask
{
    /// <summary>
    /// 邮箱脱敏：保留首字符和 @ 后域名，中间替换为 ***
    /// </summary>
    /// <param name="value">原始邮箱地址</param>
    /// <returns>脱敏后的邮箱；不含 @ 符号（或 @ 起始）时降级掩码（首字符+****，单字符全遮——A3-11）</returns>
    public string Mask(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? "";
        var atIndex = value.IndexOf('@');
        if (atIndex <= 0) return MaskHelper.DegradedMask(value);
        return $"{MaskHelper.TakeFront(value, 1)}***{value[atIndex..]}";
    }
}

/// <summary>
/// 中文姓名脱敏：张三丰→张*丰，张三→张*（保留姓和末字，中间替换为 *）
/// </summary>
public sealed class MaskChineseName : IMask
{
    /// <summary>
    /// 中文姓名脱敏：保留姓和末字，中间替换为 *（如张三→张*，张三丰→张*丰）
    /// </summary>
    /// <param name="value">原始中文姓名</param>
    /// <returns>脱敏后的姓名；单字姓名降级掩码全遮（****——A3-11，原返回原值属明文泄漏）</returns>
    public string Mask(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? "";

        // 使用 StringInfo 按文本元素（而非 char）计数，正确处理 CJK Ext B+ 生僻字（U+20000+，代理对占 2 char）
        var si = new System.Globalization.StringInfo(value);
        var length = si.LengthInTextElements;

        return length switch
        {
            1 => MaskHelper.DegradedMask(value),
            2 => $"{si.SubstringByTextElements(0, 1)}*",
            _ => $"{si.SubstringByTextElements(0, 1)}{new string('*', length - 2)}{si.SubstringByTextElements(length - 1, 1)}"
        };
    }
}

/// <summary>
/// 地址脱敏：北京市朝阳区****（保留前6字符，后续替换为 ****）
/// </summary>
public sealed class MaskAddress : IMask
{
    /// <summary>
    /// 地址脱敏：保留前6字符（省市），详细地址替换为 ****
    /// </summary>
    /// <param name="value">原始地址</param>
    /// <returns>脱敏后的地址；长度不足等于6位时降级掩码（首字符+****，单字符全遮——A3-11）</returns>
    public string Mask(string? value)
        => value is not null && MaskHelper.TextLength(value) > 6 ? MaskHelper.MaskMiddle(value, 6, 0, 4)
        : value is null ? "" : MaskHelper.DegradedMask(value);
}

/// <summary>
/// IP地址脱敏：192.168.*.*（保留前两段，后两段替换为 *）
/// </summary>
public sealed class MaskIp : IMask
{
    /// <summary>
    /// IP 地址脱敏：保留前两段，后两段替换为 *（如 192.168.*.*）
    /// </summary>
    /// <param name="value">原始 IP 地址</param>
    /// <returns>脱敏后的 IP 地址；格式非 IPv4（含 IPv6/段数≠4）时降级掩码（首字符+****，单字符全遮——A3-11）</returns>
    public string Mask(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? "";
        var parts = value.Split('.');
        if (parts.Length != 4) return MaskHelper.DegradedMask(value);
        return $"{parts[0]}.{parts[1]}.*.*";
    }
}

/// <summary>
/// 车牌脱敏：京A***45（保留前2后2，中间按实际位数替换为等量 *）
/// </summary>
public sealed class MaskLicensePlate : IMask
{
    /// <summary>
    /// 车牌号脱敏：保留前2后2，中间按实际位数替换为等量 *
    /// </summary>
    /// <param name="value">原始车牌号</param>
    /// <returns>脱敏后的车牌号；长度不足5位时降级掩码（首字符+****，单字符全遮——A3-11）</returns>
    public string Mask(string? value)
        => value is not null && MaskHelper.TextLength(value) >= 5 ? MaskHelper.MaskMiddle(value, 2, 2)
        : value is null ? "" : MaskHelper.DegradedMask(value);
}

/// <summary>
/// 固话脱敏：010-****5678（保留区号含分隔符+后4位，中间替换为 ****）
/// </summary>
public sealed class MaskLandline : IMask
{
    /// <summary>
    /// 固话脱敏：保留区号（含分隔符）+ 后4位，中间4位替换为 ****
    /// </summary>
    /// <param name="value">原始固话号码</param>
    /// <returns>脱敏后的固话号码；长度不足时降级掩码（首字符+****，单字符全遮——A3-11）</returns>
    public string Mask(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? "";
        var dashIndex = value.IndexOf('-');
        if (dashIndex < 0)
        {
            // 无分隔符：保留前3后4（对齐文本元素口径
            // 原 char 级切片 value[..3]/value[^4..] 可切出 lone surrogate（代理对输入）；
            // MaskMiddle 快/慢路径均按文本元素切片，语义等价且代理对安全）
            return MaskHelper.TextLength(value) >= 7 ? MaskHelper.MaskMiddle(value, 3, 4, 4) : MaskHelper.DegradedMask(value);
        }
        // 有分隔符：保留区号（含-）+ 后4位（localPart 尾 4 位同样对齐文本元素口径）
        var localPart = value[(dashIndex + 1)..];
        if (MaskHelper.TextLength(localPart) <= 4) return MaskHelper.DegradedMask(value);
        return $"{value[..(dashIndex + 1)]}****{MaskHelper.TakeBack(localPart, 4)}";
    }
}
