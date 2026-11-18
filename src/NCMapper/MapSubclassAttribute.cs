namespace NCMapper.Attributes;

/// <summary>
/// 多态子类型对登记特性 — 标注在承载 <c>[MapFrom]</c> 的基类 DTO 上，为基类映射对
/// 显式登记一对子类型映射（方案 1
/// type-level 多实例标注基类 DTO）。
/// <para>用法：<c>[MapFrom(typeof(Animal))] [MapSubclass(typeof(Dog), typeof(DogDto))]
/// [MapSubclass(typeof(Cat), typeof(CatDto))] class AnimalDto { ... }</c>——
/// 镜像 Fluent 通道 <c>.For&lt;TSource, TTarget&gt;().MapSubclass&lt;TSrcDerived, TTargetDerived&gt;()</c>
/// 终端（双通道等价，P-f 引擎共享）。表达力不对称（D-n 原则）：特性实参为 typeof 常量，
/// 无泛型约束编译期拦截——继承关系不满足编译期报 NCMAP_F46（Fluent 侧由 CS0311/CS0314 拦截）。</para>
/// </summary>
/// <remarks>
/// <para><b>继承阻断</b>：Inherited=false——派生 DTO 不继承父类的
/// 子类登记（运行时反射面阻断传播；编译期 Roslyn 符号面天然零传播，O-1 Spike Q1 实测）。</para>
/// <para><b>分派语义</b>：与 Fluent 侧同源——编译期 isinst 分派链（可赋值匹配，未登记更深
/// 派生命中最近登记祖先层）+ 最派生优先仲裁 + 基类兜底；覆盖 Map/_Update/MapList/MapListInto
/// 四族（Update/MapListInto 双实例匹配——dst 固定型约束）。</para>
/// <para><b>锚定</b>：标注类须同时标注 <c>[MapFrom(typeof(基类源))]</c>（基类映射对存在）；
/// 缺锚定的孤儿登记编译期报 NCMAP_F49（未被任何分派面引用）。</para>
/// <para><b>边界</b>：完全同对重复登记 / 登记基类自身（TSrcDerived == 基类源）报 NCMAP_F47；
/// 子类目标无可映射面报 NCMAP_F48；TTargetDerived == 基类目标（目标收敛）合法。</para>
/// <para><b>反向映射</b>：子类对经反向注册臂隐式产出（登记非单向）；反向基类对不消费分派
///（dst 固定型），不对称由 NCMAP_F50 Info 可见。</para>
/// <para><b>投影</b>：IProjectionPolicy.RequiresDowngrade=true——isinst 分派链不可投影，
/// ORM 投影恒降级（分类 SubclassDispatch）。</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class MapSubclassAttribute : Attribute, IProjectionPolicy
{
    /// <summary>
    /// IProjectionPolicy 协议位 — 恒 true：多态 isinst 分派链 SQL 不可翻译，
    /// ORM 投影强制降级（与 [Mask]/[Convert]/[MapWhen] 同口径）
    /// </summary>
    public bool RequiresDowngrade => true;

    /// <summary>
    /// 子类源类型 — 须派生自基类映射对的源类型（[MapFrom(typeof(基类源))] 的实参）；
    /// 不满足编译期报 NCMAP_F46
    /// </summary>
    public Type SourceType { get; }

    /// <summary>
    /// 子类目标类型 — 须派生自基类映射对的目标类型（被标注的基类 DTO）；
    /// 可等于基类目标（源侧多态、目标收敛，合法用例）
    /// </summary>
    public Type TargetType { get; }

    /// <summary>
    /// 构造 — typeof 双参形态（每实例登记一对子类型；多实例多对）
    /// </summary>
    /// <param name="sourceType">子类源类型（typeof(TSrcDerived)）</param>
    /// <param name="targetType">子类目标类型（typeof(TTargetDerived)）</param>
    public MapSubclassAttribute(Type sourceType, Type targetType)
    {
        SourceType = sourceType;
        TargetType = targetType;
    }
}
