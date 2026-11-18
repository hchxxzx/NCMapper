namespace NCMapper;

/// <summary>
/// 脱敏接口 — 实现此接口的类可对字符串属性值进行脱敏变换。
/// <para>用法：[Mask(typeof(MaskPhone))] 标注 DTO 属性</para>
/// <para>生成器将 dst.Prop = src.Prop 替换为 dst.Prop = __mask_X.Mask(src.Prop)</para>
/// <para>运行时替换：MaskProvider.Override&lt;TMask&gt;(impl) 可在启动时替换任意内置实现</para>
/// </summary>
public interface IMask
{
    /// <summary>
    /// 对原始值进行脱敏，返回脱敏后的字符串
    /// </summary>
    /// <param name="value">指定值。</param>
    /// <returns>返回字符串。</returns>
    string Mask(string? value);
}
