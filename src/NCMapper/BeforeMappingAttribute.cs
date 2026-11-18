namespace NCMapper.Attributes;

/// <summary>
/// 前置生命周期钩子特性 — 标注在 DTO 类上，映射执行前（目标实例构造后、首条属性赋值前）调用静态回调方法
/// <para>用法：[BeforeMapping(typeof(OrderMappingHost), nameof(OrderMappingHost.Normalize))]
/// 或就近标注 [BeforeMapping(nameof(Normalize))]（方法声明于 DTO 自身）</para>
/// <para>镜像 Fluent 通道 <c>.BeforeMapping(...)</c> 终端——两侧填充同一快照承载字段（双通道等价）。</para>
/// </summary>
/// <remarks>
/// <para><b>方法契约</b>：目标方法须为 <b>public 或 internal static</b>（同程序集）、签名
/// <c>void M(TSource source, TTarget target)</c>（TSource/TTarget = 该 DTO 的 [MapFrom] 源类型与 DTO 自身）；
/// 解析失败（方法不存在/不可访问/签名不符/多重载歧义）编译期报 NCMAP038/039/040。</para>
/// <para><b>执行时点</b>：dst 已构造、属性尚未填充（属性初始化器值可被回调预处理保留/覆盖）；
/// record/init-only 构造器一体化目标（构造与赋值不可分）无插入点 → NCMAP_F37（双侧同报）。</para>
/// <para><b>继承</b>：Inherited=false——派生 DTO 不继承钩子，需显式标注（与 [MapTree] 同口径）。</para>
/// <para><b>投影</b>：IProjectionPolicy.RequiresDowngrade=true——ORM 投影（嵌套投影不执行生成代码，
/// 钩子丢失即语义错误）强制降级为内存映射（全语义）。</para>
/// <para><b>作用域</b>：钩子随 DTO（target-level）、按方法签名适用逐对生效——签名与映射对
/// (TSource,TTarget) 匹配的对发射（含纯 Fluent 声明对，经跨对传播注入）；签名不适用对不发射、不发诊断。</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class BeforeMappingAttribute : Attribute, IProjectionPolicy
{
    /// <inheritdoc/>
    public bool RequiresDowngrade => true;

    /// <summary>
    /// 方法引用形态 — 声明静态回调方法的类型（锚定类型）
    /// <para>双参构造形态非空；单参构造形态为 null，此时解析域收紧为
    /// 标注该特性的 DTO 类型自身（DeclaredOnly，不含基类继承/外层嵌套类型）。</para>
    /// <para>类型须为封闭形态（开放泛型类型不支持——typeof(X&lt;&gt;) 传入时报 NCMAP039）。</para>
    /// </summary>
    public Type? DeclaringType { get; }

    /// <summary>
    /// 方法引用形态 — 静态回调方法名（nameof 定位，编译期强校验存在性）
    /// <para>方法可访问性白名单：public、internal（同程序集）、protected internal（同程序集腿）；
    /// protected / private protected / private 拒绝（NCMAP038）。</para>
    /// </summary>
    public string MethodName { get; }

    /// <summary>
    /// 双参构造 — 类型 + 方法名强定位静态回调方法（推荐形态）
    /// <para>与 [Convert(typeof(X), nameof(X.M))] 风格统一，回调方法可集中在公共宿主类实现跨实体复用。</para>
    /// </summary>
    /// <param name="declaringType">声明静态回调方法的类型（方法须声明于该类型自身，不含基类继承）</param>
    /// <param name="methodName">方法名（nameof(类型.方法)；签名 void(TSource, TTarget)）</param>
    public BeforeMappingAttribute(Type declaringType, string methodName)
    {
        DeclaringType = declaringType;
        MethodName = methodName;
    }

    /// <summary>
    /// 单参构造 — 就近标注形态，解析域 = 标注该特性的 DTO 类型自身
    /// <para>仅当静态方法声明于当前 DTO 类型（含 partial 其他部分）时可用。</para>
    /// <para>nameof 限定名截断警示：nameof(OtherType.M) 编译产物仅为 "M"，OtherType 被静默丢弃——
    /// 方法不在当前类型自身时请改用双参构造 [BeforeMapping(typeof(OtherType), nameof(OtherType.M))]，
    /// 否则以 NCMAP038 报错并给出改写指引（不静默错配）。</para>
    /// </summary>
    /// <param name="methodName">方法名（当前 DTO 类型自身声明的静态方法）</param>
    public BeforeMappingAttribute(string methodName)
    {
        DeclaringType = null;
        MethodName = methodName;
    }
}
