// 双引擎语义收敛对照用例组模型
// 结构约定：同一输入场景成对出现 —— {X}GenDto 标注 [MapFrom]（生成侧路径），
// {X}RuntimeDto 不标注（运行时表达式树路径），测试断言两侧行为一致（SAME）
using NCMapper.Attributes;

using NCMapper;

namespace NCMapper.Tests.Models
{
    /// <summary>
    /// 对照用枚举 — R1/R2 组的 string↔enum 映射载体（底层值 1/2/3，含可数值解析输入格）
    /// </summary>
    public enum ConvColor
    {
        /// <summary>红色（值 1）</summary>
        Red = 1,
        /// <summary>蓝色（值 2）</summary>
        Blue = 2,
        /// <summary>绿色（值 3）</summary>
        Green = 3
    }

    // ===================== R1：string→enum（大小写/非法值/数字串/null）=====================

    /// <summary>
    /// R1 源实体 — 字符串状态属性（可空）
    /// <para>属性名与目标 DTO 一致（Status），保证 PropertyMatcher 按名匹配命中</para>
    /// </summary>
    public class R1Source
    {
        /// <summary>状态字符串</summary>
        public string? Status { get; set; }
    }

    /// <summary>
    /// R1 生成侧 DTO — [MapFrom] 触发源生成器（T2 改造后 switch 全小写标签 + 数字串标签 + 抛异常）
    /// </summary>
    [MapFrom(typeof(R1Source))]
    public class R1GenDto
    {
        /// <summary>转换后的枚举状态</summary>
        public ConvColor Status { get; set; }
    }

    /// <summary>
    /// R1 运行时侧 DTO — 不标 [MapFrom]，Mapper.Map 走运行时表达式树（TypeConvertHelper.Enum.Parse）
    /// </summary>
    public class R1RuntimeDto
    {
        /// <summary>转换后的枚举状态</summary>
        public ConvColor Status { get; set; }
    }

    // ===================== R2：Nullable<enum>→string（有值/无值独立格）=====================

    /// <summary>
    /// R2 源实体 — 可空枚举颜色属性
    /// </summary>
    public class R2Source
    {
        /// <summary>可空颜色（无值=null）</summary>
        public ConvColor? StatusColor { get; set; }
    }

    /// <summary>
    /// R2 生成侧 DTO — 可空枚举 → string（T1 修复后双侧走 enum→string switch）
    /// </summary>
    [MapFrom(typeof(R2Source))]
    public class R2GenDto
    {
        /// <summary>颜色字符串（无值 → ""）</summary>
        public string? StatusColor { get; set; }
    }

    /// <summary>
    /// R2 运行时侧 DTO — 不标 [MapFrom]
    /// </summary>
    public class R2RuntimeDto
    {
        /// <summary>颜色字符串（无值 → ""）</summary>
        public string? StatusColor { get; set; }
    }

    // ===================== R3/F-03 + F-04：异类型集合 Update null 语义 + 基元异类型集合载体 =====================

    /// <summary>
    /// F-04 复现载体源 — List&lt;int&gt;（审计 audit-tmp/ModelsF04.cs 同构移植）
    /// </summary>
    public class F04Source
    {
        /// <summary>整数集合</summary>
        public List<int>? Nums { get; set; }
    }

    /// <summary>
    /// F-04 生成侧载体 — List&lt;long&gt; 目标（T4 内联转换后编译通过，消除 CS0117）
    /// </summary>
    [MapFrom(typeof(F04Source))]
    public class F04Dto
    {
        /// <summary>长整型集合</summary>
        public List<long>? Nums { get; set; }
    }

    /// <summary>
    /// F-04 运行时侧对照 — 不标 [MapFrom]
    /// </summary>
    public class F04RuntimeDto
    {
        /// <summary>长整型集合</summary>
        public List<long>? Nums { get; set; }
    }

    // ===================== 容器类型 null 矩阵（F-03 验收 V1：List/接口/数组/HashSet/具体集合/字典）=====================
    // 元素类型用 class（ElemA/ElemB）而非基元异类型：运行时 ExpressionBuilder 对基元异类型集合
    // （List<int>→List<long>）构建期 MakeGenericType 违反 MapperBase TTarget:class 约束抛 TypeLoadException
    // （F-04 运行时缺陷，登记），class 元素对运行时可用，矩阵对照才能成立

    /// <summary>
    /// 矩阵源元素 — class（运行时递归映射可用）
    /// </summary>
    public class ElemA
    {
        /// <summary>值</summary>
        public int V { get; set; }
    }

    /// <summary>
    /// 矩阵目标元素 — [MapFrom] 注册（生成侧集合映射走 MapList 委托）
    /// </summary>
    [MapFrom(typeof(ElemA))]
    public class ElemB
    {
        /// <summary>值</summary>
        public int V { get; set; }
    }

    /// <summary>
    /// 容器 null 矩阵源 — 六类容器属性（矩阵测试时全部置 null；元素类型 ElemA 为 class）
    /// </summary>
    public class ContainerNullSource
    {
        /// <summary>List 目标（直接赋值分支）</summary>
        public List<ElemA>? ListProp { get; set; }

        /// <summary>IEnumerable 接口目标（直接赋值分支）</summary>
        public IEnumerable<ElemA>? EnumerableProp { get; set; }

        /// <summary>数组目标（ToArray 分支）</summary>
        public ElemA[]? ArrayProp { get; set; }

        /// <summary>ISet 接口目标（HashSet 构造分支）</summary>
        public HashSet<ElemA>? SetProp { get; set; }

        /// <summary>具体集合目标（Queue 构造器分支）</summary>
        public Queue<ElemA>? QueueProp { get; set; }

        /// <summary>字典目标（O2 连带：字典路径 null 条件构造）</summary>
        public Dictionary<string, ElemA>? DictProp { get; set; }
    }

    /// <summary>
    /// 容器 null 矩阵生成侧 DTO — [MapFrom] 触发生成路径（T3 四分支 + O2 字典）
    /// </summary>
    [MapFrom(typeof(ContainerNullSource))]
    public class ContainerNullGenDto
    {
        /// <summary>List 目标</summary>
        public List<ElemB>? ListProp { get; set; }

        /// <summary>IEnumerable 目标</summary>
        public IEnumerable<ElemB>? EnumerableProp { get; set; }

        /// <summary>数组目标</summary>
        public ElemB[]? ArrayProp { get; set; }

        /// <summary>ISet 目标</summary>
        public HashSet<ElemB>? SetProp { get; set; }

        /// <summary>具体集合目标</summary>
        public Queue<ElemB>? QueueProp { get; set; }

        /// <summary>字典目标</summary>
        public Dictionary<string, ElemB>? DictProp { get; set; }
    }

    /// <summary>
    /// 容器 null 矩阵运行时侧 DTO — 不标 [MapFrom]
    /// <para>仅含运行时 GetListExpression 支持的容器目标（List/接口/数组/字典）：
    /// HashSet/Queue 等具体集合目标在运行时构建期 Expression.Convert(List→HashSet) 抛
    /// InvalidOperationException（GetListExpression :1051 缺陷，登记），矩阵 HashSet/Queue
    /// 分支仅生成侧断言（发射形态测试 T3 已覆盖四分支）</para>
    /// </summary>
    public class ContainerNullRuntimeDto
    {
        /// <summary>List 目标</summary>
        public List<ElemB>? ListProp { get; set; }

        /// <summary>IEnumerable 目标</summary>
        public IEnumerable<ElemB>? EnumerableProp { get; set; }

        /// <summary>数组目标</summary>
        public ElemB[]? ArrayProp { get; set; }

        /// <summary>字典目标</summary>
        public Dictionary<string, ElemB>? DictProp { get; set; }
    }

    // ===================== F-08：List→Dict 不兼容（异常类型统一）=====================
    // 生成侧载体复用既有模型（EdgeCaseModels.cs ListToDictSource/ListToDictDto，List<string>→Dictionary<string,string>）；
    // 此处仅补运行时对照 DTO（未标 [MapFrom]），对照测试 F08_* 使用既有 ListToDictSource 源

    /// <summary>
    /// F-08 运行时侧 DTO — 不标 [MapFrom]（运行时 GetListExpression 抛 NotSupportedException）
    /// <para>与既有 ListToDictDto（生成侧，[MapFrom]）构成双引擎对照</para>
    /// </summary>
    public class ListToDictRuntimeDto
    {
        /// <summary>字典目标（不兼容）</summary>
        public Dictionary<string, string>? Codes { get; set; }
    }

    // ===================== F-09：[MapIgnore] 方向判定（T6）=====================

    /// <summary>
    /// F-09 象限④复现源实体 — 双侧均未标 [MapFrom]（审计 R7 复现场景）
    /// </summary>
    public class F09Entity
    {
        /// <summary>敏感字段（方向性忽略标注在 DTO 侧）</summary>
        public string? Secret { get; set; }

        /// <summary>公开字段</summary>
        public string? Public { get; set; }
    }

    /// <summary>
    /// F-09 象限④复现 DTO — 不标 [MapFrom]，属性带方向性 [MapIgnore(ToDto)]
    /// <para>双侧未标 → 方向不可推断（象限④）：运行时 isToDto=false → [MapIgnore(ToDto)] 不生效，
    /// 属性被映射；表达式构建期输出一次性 Trace 引导（T6-S2 验收 V1）</para>
    /// </summary>
    public class F09Dto
    {
        /// <summary>敏感字段（[MapIgnore(ToDto)] 在象限④下不生效）</summary>
        [MapIgnore(Direction.ToDto)]
        public string? Secret { get; set; }

        /// <summary>公开字段</summary>
        public string? Public { get; set; }
    }

    /// <summary>
    /// F-09 V3 源实体 — 未标 [MapFrom]，作为 ToEntity 映射的源
    /// <para>V3 场景修正：任务书 V3 字面场景"源标 [MapFrom] 目标未标"在运行时不可达——
    /// PropertyMatcher.GetPropertyList 对源标 [MapFrom] 的类型过滤目标列表（源属性仅保留映射到
    /// 声明源类型的属性），目标非声明源类型时源属性列表为空（什么都不映射）；
    /// 目标为声明源类型时又是注册对（走生成侧）。故 V3 改用双侧未标对验证
    /// isToDto=false 时 [MapIgnore(ToEntity)] 生效（语义等价：方向判定由 targetIsDefine=false 推导）</para>
    /// </summary>
    public class F09V3Source
    {
        /// <summary>敏感字段</summary>
        public string? Secret { get; set; }

        /// <summary>公开字段</summary>
        public string? Public { get; set; }
    }

    /// <summary>
    /// F-09 V3 目标 — 未标 [MapFrom]，属性带 [MapIgnore(ToEntity)]
    /// <para>映射 F09V3Source→F09V3Target（双侧未标 → 运行时路径）：isToDto=false（ToEntity），
    /// [MapIgnore(ToEntity)] 应生效（Secret 不被映射，T6 验收 V3）</para>
    /// </summary>
    public class F09V3Target
    {
        /// <summary>敏感字段（ToEntity 方向应被忽略）</summary>
        [MapIgnore(Direction.ToEntity)]
        public string? Secret { get; set; }

        /// <summary>公开字段</summary>
        public string? Public { get; set; }
    }
}


    /// <summary>
    /// string→int 双引擎一致性模型（生成路径 [MapFrom] vs 运行时无标注副本）
    /// </summary>
    public class T001Source
    {
        public string? StringProp { get; set; }
    }

    [MapFrom(typeof(T001Source))]
    public class T001GenDto
    {
        public int StringProp { get; set; }
    }

    public class T001RuntimeDto
    {
        public int StringProp { get; set; }
    }    /// <summary>
    /// 双引擎一致性矩阵模型（生成路径 [MapFrom] vs 运行时无标注副本）
    /// 每类场景 Gen/Runtime 两个 DTO，断言输出等价——锁定运行时 ExpressionBuilder 与
    /// 生成侧 CodeGenerator.Mapping 的语义等价性，防止单侧修复产生分叉（S2 前置）。
    /// </summary>

    // 1. 基元同类型
    public class M14PrimSource { public int IntProp { get; set; } }
    [MapFrom(typeof(M14PrimSource))]
    public class M14PrimGenDto { public int IntProp { get; set; } }
    public class M14PrimRuntimeDto { public int IntProp { get; set; } }

    // 2. 可空值类型
    public class M14NullableSource { public int? NullableProp { get; set; } }
    [MapFrom(typeof(M14NullableSource))]
    public class M14NullableGenDto { public int NullableProp { get; set; } }
    public class M14NullableRuntimeDto { public int NullableProp { get; set; } }

    // 3. 枚举 → int
    public enum M14Color { Red, Green, Blue }
    public class M14EnumToIntSource { public M14Color Color { get; set; } }
    [MapFrom(typeof(M14EnumToIntSource))]
    public class M14EnumToIntGenDto { public int Color { get; set; } }
    public class M14EnumToIntRuntimeDto { public int Color { get; set; } }

    // 4. int → 枚举
    public class M14IntToEnumSource { public int Level { get; set; } }
    [MapFrom(typeof(M14IntToEnumSource))]
    public class M14IntToEnumGenDto { public M14Color Level { get; set; } }
    public class M14IntToEnumRuntimeDto { public M14Color Level { get; set; } }

    // 5. int → string
    public class M14IntToStringSource { public int Count { get; set; } }
    [MapFrom(typeof(M14IntToStringSource))]
    public class M14IntToStringGenDto { public string Count { get; set; } = ""; }
    public class M14IntToStringRuntimeDto { public string Count { get; set; } = ""; }

    // 6. bool → string
    public class M14BoolToStringSource { public bool Active { get; set; } }
    [MapFrom(typeof(M14BoolToStringSource))]
    public class M14BoolToStringGenDto { public string Active { get; set; } = ""; }
    public class M14BoolToStringRuntimeDto { public string Active { get; set; } = ""; }

    // 7. 嵌套对象
    public class M14NestedInner { public string Name { get; set; } = ""; }
    public class M14NestedSource { public M14NestedInner Inner { get; set; } = new(); }
    [MapFrom(typeof(M14NestedSource))]
    public class M14NestedGenDto { public M14NestedInner Inner { get; set; } = new(); }
    public class M14NestedRuntimeDto { public M14NestedInner Inner { get; set; } = new(); }

    // 8. 集合（异类元素映射）
    public class M14ItemSource { public int Id { get; set; } }
    public class M14ItemDto { public int Id { get; set; } }
    public class M14ListSource { public List<M14ItemSource> Items { get; set; } = new(); }
    [MapFrom(typeof(M14ListSource))]
    public class M14ListGenDto { public List<M14ItemDto> Items { get; set; } = new(); }
    public class M14ListRuntimeDto { public List<M14ItemDto> Items { get; set; } = new(); }

    // 9. 字典（同类型复制）
    public class M14DictSource { public Dictionary<string, int> Map { get; set; } = new(); }
    [MapFrom(typeof(M14DictSource))]
    public class M14DictGenDto { public Dictionary<string, int> Map { get; set; } = new(); }
    public class M14DictRuntimeDto { public Dictionary<string, int> Map { get; set; } = new(); }

    // 10. Mask 脱敏
    public class M14MaskSource { public string Phone { get; set; } = ""; }
    [MapFrom(typeof(M14MaskSource))]
    public class M14MaskGenDto { [Mask(typeof(MaskPhone))] public string Phone { get; set; } = ""; }
[MapFrom(typeof(M14MaskSource))]     public class M14MaskRuntimeDto { [Mask(typeof(MaskPhone))] public string Phone { get; set; } = ""; }

    // 11. ConvertUsing 自定义转换
    public class M14ConvSource { public string Raw { get; set; } = ""; }
    public static class M14UpperConverter
    {
        public static string Convert(string fieldValue) => fieldValue.ToUpperInvariant();
    }
    [MapFrom(typeof(M14ConvSource))]
    public class M14ConvGenDto { [Convert(typeof(M14UpperConverter), nameof(M14UpperConverter.Convert))] public string Raw { get; set; } = ""; }
    public class M14ConvRuntimeDto { [Convert(typeof(M14UpperConverter), nameof(M14UpperConverter.Convert))] public string Raw { get; set; } = ""; }

    // 12. MapIgnore 方向（ToDto 忽略）
    public class M14IgnoreSource { public string Skip { get; set; } = ""; public string Keep { get; set; } = ""; }
    [MapFrom(typeof(M14IgnoreSource))]
    public class M14IgnoreGenDto { [MapIgnore(Direction.Both)] public string Skip { get; set; } = ""; public string Keep { get; set; } = ""; }
    [MapFrom(typeof(M14IgnoreSource))] // 补锚——承载侧合法形态
    public class M14IgnoreRuntimeDto { [MapIgnore(Direction.Both)] public string Skip { get; set; } = ""; public string Keep { get; set; } = ""; }
