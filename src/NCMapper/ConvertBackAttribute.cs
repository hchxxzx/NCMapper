namespace NCMapper.Attributes;

/// <summary>
/// 指定属性在反向映射（DTO→Entity）时使用自定义值转换器
/// <para>标注在 DTO 属性上，反向映射时调用静态反向转换方法；</para>
/// <para>也可标注在实体属性（反向目标侧）：DTO 无同名源属性时转换器仍被调用（TSource = 整个 DTO 对象）；
/// 匹配对（同名属性）场景为兜底位——DTO 侧同名属性已标注时以 DTO 侧为准；
/// 写回链实体叶属性（路径末段，如 Address.City 的 City）同构兜底，双引擎均消费。</para>
/// <para>用法：[ConvertBack(typeof(StringToDateTimeConverter), nameof(StringToDateTimeConverter.Convert))]</para>
/// <para>正向映射（Entity→DTO）不消费本特性（按类型转换路径处理，与 [Convert] 反向同语义）。</para>
/// <para>与 [Convert] 的来源互斥：同一属性同时标注两特性时，[Convert] 仅正向消费、
/// [ConvertBack] 仅反向消费，各自独立校验与生成。</para>
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class ConvertBackAttribute : Attribute, IProjectionPolicy
{
    /// <summary>
    /// 反向转换器不参与正向投影降级判定 — 正向投影不消费本特性，
    /// 实现 <see cref="IProjectionPolicy"/> 且返回 false 避免
    /// DegradationDetectorBase 将属性级未识别特性误判为投影降级触发点
    /// </summary>
    public bool RequiresDowngrade => false;

    /// <summary>
    /// 命中全局忽略清单时显式豁免（唯一豁免门）；默认 false = 服从全局忽略
    /// <para>豁免作用于映射对整体（非单侧属性）：目标属性带本特性且 OverrideIgnore=true 时，
    /// 该映射对整体跳过全局忽略判定（源属性名命中忽略清单也参与匹配）。</para>
    /// <para>与 [Convert] 的 OverrideIgnore 同语义，补齐反向侧豁免门。</para>
    /// <para>与属性级 [MapIgnore] 同标即矛盾，源生成器报 NCMAP022 诊断（[MapIgnore] 不参与豁免）。</para>
    /// </summary>
    public bool OverrideIgnore { get; set; }

    /// <summary>
    /// 方法引用形态 — 声明静态反向转换方法的类型（锚定类型）
    /// <para>双参构造形态非空；单参构造形态为 null，此时解析域收紧为
    /// 标注该特性的属性所属类型自身（DeclaredOnly，不含基类继承/外层嵌套类型）。</para>
    /// <para>方法须声明于该类型自身（不含基类继承的静态方法）；类型须为封闭形态
    /// （开放泛型类型不支持，误传时以 NCMAP035/037 指引改用双参封闭泛型 typeof(X&lt;int&gt;)）。</para>
    /// </summary>
    public Type? DeclaringType { get; }

    /// <summary>
    /// 方法引用形态 — 静态反向转换方法名（nameof 定位，编译期强校验存在性）
    /// <para>签名自动识别转换形态：M() 无参常量 / M(TField) 单值 / M(TSource) 多字段组合 /
    /// M(TSource, TField) 组合+字段值——反向时 TSource = DTO 类型（反向映射的源对象）。</para>
    /// <para>方法可访问性白名单：public、internal（同程序集）、protected internal（同程序集腿）；
    /// protected / private protected / private 拒绝（NCMAP035）。</para>
    /// </summary>
    public string MethodName { get; }

    /// <summary>
    /// 双参构造 — 类型 + 方法名强定位静态反向转换方法（推荐形态，反向转换器可跨类复用）
    /// <para>与 [MapProperty(typeof(X), nameof(X.Member))] 风格统一。</para>
    /// </summary>
    /// <param name="declaringType">声明静态反向转换方法的类型（方法须声明于该类型自身，不含基类继承）</param>
    /// <param name="methodName">方法名（nameof(类型.方法)；重载须唯一命中，多重载通过签名匹配时报 NCMAP036）</param>
    public ConvertBackAttribute(Type declaringType, string methodName)
    {
        DeclaringType = declaringType;
        MethodName = methodName;
    }

    /// <summary>
    /// 单参构造 — 就近标注形态，解析域 = 标注该特性的属性所属类型自身
    /// <para>仅当静态方法声明于当前类型（含 partial 其他部分）时可用；partial 天然合并，方法与标注可分属两个 partial 文件。</para>
    /// <para>nameof 限定名截断警示：nameof(OtherType.M) 编译产物仅为 "M"，OtherType 被静默丢弃——
    /// 方法不在当前类型自身时请改用双参构造 [ConvertBack(typeof(OtherType), nameof(OtherType.M))]，
    /// 否则以 NCMAP035 报错并给出改写指引（不静默错配）。</para>
    /// </summary>
    /// <param name="methodName">方法名（当前类型自身声明的静态方法）</param>
    public ConvertBackAttribute(string methodName)
    {
        DeclaringType = null;
        MethodName = methodName;
    }
}
