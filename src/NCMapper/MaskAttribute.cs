namespace NCMapper.Attributes;

/// <summary>
/// 标注属性在映射时应用脱敏变换 — 生成器通过 MaskProvider.Resolve 创建静态单例并调用 IMask.Mask 方法
/// <para>用法：[Mask(typeof(MaskPhone))]</para>
/// <para>指定的类型必须实现 <see cref="NCMapper.IMask"/> 接口</para>
/// <para>运行时替换：MaskProvider.Override&lt;MaskPhone&gt;(new MyMask()) 可在启动时替换实现</para>
/// <para>投影路径：实现 IProjectionPolicy（RequiresDowngrade=true），触发 AsDto 自动降级为二步映射</para>
/// <para><b>路径说明（T5 契约改写）</b>：[Mask] 属性级标注在其所在类标
/// [MapFrom]（或经 Fluent 声明为映射目标）时生效——源生成器路径与运行时回退路径均经
/// MaskProvider.Resolve 解析 IMask 实现并执行脱敏；所在类无锚时标注不消费（无锚不消费口径，
/// 无锚属性级标注由 NCMAP019 编译期拦截）。</para>
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public class MaskAttribute : Attribute, IProjectionPolicy
{
    /// <summary>
    /// 脱敏实现类型 — 必须实现 IMask 接口
    /// </summary>
    public Type MaskType { get; }

    /// <summary>
    /// 脱敏为 CLR 委托路径，不可翻译为 SQL，需触发投影降级
    /// </summary>
    public bool RequiresDowngrade => true;

    /// <summary>
    /// 命中全局忽略清单时显式豁免（唯一豁免门）；默认 false = 服从全局忽略
    /// <para>豁免作用于映射对整体（非单侧属性）：目标属性带本特性且 OverrideIgnore=true 时，
    /// 该映射对整体跳过全局忽略判定（源属性名命中忽略清单也参与匹配）。</para>
    /// <para>与属性级 [MapIgnore] 同标即矛盾，源生成器报 NCMAP022 诊断（[MapIgnore] 不参与豁免）。</para>
    /// </summary>
    public bool OverrideIgnore { get; set; }

    /// <summary>
    /// 创建指定脱敏类型的特性实例
    /// </summary>
    /// <param name="maskType">脱敏实现类型，必须实现 <see cref="NCMapper.IMask"/> 接口</param>
    public MaskAttribute(Type maskType)
    {
        MaskType = maskType ?? throw new ArgumentNullException(nameof(maskType));
    }
}
