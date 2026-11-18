namespace NCMapper.Attributes;

/// <summary>
/// 条件守卫特性 — 标注在 DTO 属性上，仅当谓词方法对源值求值返回 true 时才执行该属性的映射赋值
/// （真才映射）。
/// <para>用法：<c>[MapWhen(nameof(HasContent))]</c>（方法声明于被标注属性所在类型）——
/// 属性级独立特性，自动匹配属性与显式 [MapProperty] 属性两类映射均可锚定。</para>
/// <para>镜像 Fluent 通道 <c>.MapWhen(dest, condition)</c> 终端——两侧填充同一 pm 级承载字段
/// ConditionMethodRef（双通道等价，P-e）。表达力不对称（D-n 原则）：特性实参为编译期常量，
/// 仅命名单形态（A）；内联谓词（B 形态转录）为 Fluent 独有。</para>
/// </summary>
/// <remarks>
/// <para><b>方法契约</b>：方法在被标注属性的声明类型上按名解析，须为
/// <b>public 或 internal static</b>（同程序集可见），签名二选一——
/// <c>static bool M(TSrcProp fieldValue)</c>（T1 字段值形态，TSrcProp = 该属性映射的源字段类型）或
/// <c>static bool M(TSource source)</c>（T2 整源形态，TSource = 该 DTO 的 [MapFrom] 源类型）；
/// 解析失败/非静态/签名不符/可见性不足编译期报 NCMAP041。</para>
/// <para><b>语义</b>：链路 = 条件 → 转换 → 赋值（条件看到转换前的源值）；条件假 → 跳过赋值
/// （新建保持初始化器值 / 更新保留既有值）；构造器映射目标显式落 default。</para>
/// <para><b>谓词入参形态判定</b>：单参 == 源实体类型判整源，否则判字段值
/// 与 [Convert] 族同规则（提取期自动判定，无需用户指定）。</para>
/// <para><b>边界</b>：同属性重复标注由 AllowMultiple=false 拒绝（编译期）；属性无映射（无匹配源）
/// 时声明无附着点（NCMAP001 由属性自身承载）；反向映射不消费本声明。</para>
/// <para><b>投影</b>：IProjectionPolicy.RequiresDowngrade=true——守卫谓词为 CLR 委托路径，
/// SQL 不可翻译，投影恒降级为内存映射（分类 MapWhen）。</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class MapWhenAttribute : Attribute, IProjectionPolicy
{
    /// <summary>
    /// IProjectionPolicy 协议位 — 恒 true：守卫谓词 CLR 委托路径，SQL 不可翻译，
    /// ORM 投影强制降级（与 [Mask]/[Convert] 同口径）
    /// </summary>
    public bool RequiresDowngrade => true;

    /// <summary>
    /// 谓词方法名 — 在被标注属性的声明类型上按名解析
    /// <para>签名 <c>public/internal static bool M(TSrcProp)</c>或
    /// <c>static bool M(TSource)</c>；解析失败报 NCMAP041。</para>
    /// </summary>
    public string MethodName { get; }

    /// <summary>
    /// 构造 — 谓词方法名单参形态（方法声明于被标注属性所在类型，单类型自足——
    /// 与 [Convert(string)] 单参形态镜像；守卫多为 DTO 内聚规则，无外置宿主双参形态）
    /// </summary>
    /// <param name="methodName">谓词静态方法名（nameof 定位）</param>
    public MapWhenAttribute(string methodName)
    {
        MethodName = methodName;
    }
}
