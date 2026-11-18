using NCMapper.Attributes;
using System.ComponentModel.DataAnnotations.Schema;

namespace NCMapper.Tests.Models
{
    // ================================================================
    // 边界测试模型 — 字符串/数字 → 枚举、只读属性、继承、精度
    // ================================================================

    /// <summary>
    /// 优先级枚举 — 用于测试 string/int → enum 的反向转换
    /// </summary>
    public enum Priority
    {
        /// <summary>
        /// 低 = 0
        /// </summary>
        Low = 0,
        /// <summary>
        /// 普通 = 1
        /// </summary>
        Normal = 1,
        /// <summary>
        /// 高 = 2
        /// </summary>
        High = 2
    }

    /// <summary>
    /// 枚举值来源实体 — 提供字符串和数值两种形式的优先级
    /// </summary>
    public class EnumSourceEntity
    {
        /// <summary>
        /// 字符串形式优先级："High"
        /// </summary>
        public string PriorityStr { get; set; } = "High";

        /// <summary>
        /// 数值形式优先级：2
        /// </summary>
        public int PriorityNum { get; set; } = 2;
    }

    /// <summary>
    /// 从字符串/数值反推枚举的 DTO — 验证 TypeConvertHelper 的 enum 反向路径
    /// </summary>
    [MapFrom(typeof(EnumSourceEntity))]
    public class EnumFromValueDto
    {
        /// <summary>
        /// "High" → Priority.High（字符串→枚举）
        /// </summary>
        [MapProperty(typeof(EnumSourceEntity), nameof(EnumSourceEntity.PriorityStr))]
        public Priority Priority { get; set; }

        /// <summary>
        /// 2 → Priority.High（数值→枚举）
        /// </summary>
        [MapProperty(typeof(EnumSourceEntity), nameof(EnumSourceEntity.PriorityNum))]
        public Priority PriorityByNum { get; set; }
    }

    // ================================================================
    // 只读属性测试
    // ================================================================

    /// <summary>
    /// 包含只读属性的目标 DTO — 只读属性无 setter，应被跳过不抛异常
    /// </summary>
    [MapFrom(typeof(FullTypeEntity))]
    public class ReadonlyTargetDto
    {
        /// <summary>
        /// 可写属性，正常映射
        /// </summary>
        public int IntProp { get; set; }

        /// <summary>
        /// 只读属性（无 setter），映射时跳过，保持初始值
        /// </summary>
        public string FixedField { get; } = "INITIAL_FIXED";
    }

    // ================================================================
    // 继承层次测试
    // ================================================================

    /// <summary>
    /// 基类实体 — 包含基类字段
    /// </summary>
    public class BaseEntity
    {
        /// <summary>
        /// 基类ID
        /// </summary>
        public int BaseId { get; set; } = 100;
        /// <summary>
        /// 基类名称
        /// </summary>
        public string BaseName { get; set; } = "Base";
    }

    /// <summary>
    /// 派生类实体 — 继承 BaseEntity 并新增字段
    /// </summary>
    public class DerivedEntity : BaseEntity
    {
        /// <summary>
        /// 派生字段
        /// </summary>
        public string DerivedField { get; set; } = "Derived";
    }

    /// <summary>
    /// 派生类 DTO — 同时包含基类与派生类字段
    /// </summary>
    [MapFrom(typeof(DerivedEntity))]
    public class DerivedDto
    {
        /// <summary>
        /// 来自基类
        /// </summary>
        public int BaseId { get; set; }
        /// <summary>
        /// 来自基类
        /// </summary>
        public string BaseName { get; set; } = "";
        /// <summary>
        /// 来自派生类
        /// </summary>
        public string DerivedField { get; set; } = "";
    }

    // ================================================================
    // 数值精度测试
    // ================================================================

    /// <summary>
    /// 数值精度实体 — 包含大数和高精度小数
    /// </summary>
    public class PrecisionEntity
    {
        /// <summary>
        /// 大长整型
        /// </summary>
        public long BigLong { get; set; } = 9223372036854775807L; // long.MaxValue

        /// <summary>
        /// 高精度小数
        /// </summary>
        public decimal PreciseDecimal { get; set; } = 123456789.987654321m;

        /// <summary>
        /// 负数
        /// </summary>
        public decimal NegativeDecimal { get; set; } = -9999.99m;

        /// <summary>
        /// 零
        /// </summary>
        public int ZeroValue { get; set; } = 0;
    }

    /// <summary>
    /// 数值精度 DTO — 同类型映射，验证精度无损
    /// </summary>
    [MapFrom(typeof(PrecisionEntity))]
    public class PrecisionDto
    {
        /// <summary>
        /// 大长整型
        /// </summary>
        public long BigLong { get; set; }

        /// <summary>
        /// 高精度小数
        /// </summary>
        public decimal PreciseDecimal { get; set; }

        /// <summary>
        /// 负数
        /// </summary>
        public decimal NegativeDecimal { get; set; }

        /// <summary>
        /// 零
        /// </summary>
        public int ZeroValue { get; set; }
    }

    /// <summary>
    /// 跨数值类型 DTO — long→decimal（拓宽）、decimal→long（截断）
    /// </summary>
    [MapFrom(typeof(PrecisionEntity))]
    public class CrossNumericDto
    {
        /// <summary>
        /// long.MaxValue → decimal（拓宽，无损）
        /// </summary>
        public decimal BigLong { get; set; }

        /// <summary>
        /// decimal → int（截断小数部分）
        /// </summary>
        public int PreciseDecimal { get; set; }
    }

    // ================================================================
    // 深度嵌套链 — 测试递归深度
    // ================================================================

    /// <summary>
    /// 链表节点 — 自引用，用于构造深度递归链
    /// </summary>
    public class LinkNode
    {
        /// <summary>
        /// 节点值
        /// </summary>
        public int Value { get; set; }
        /// <summary>
        /// 下一节点（自引用）
        /// </summary>
        public LinkNode? Next { get; set; }
    }

    /// <summary>
    /// 链表节点 DTO
    /// </summary>
    [MapFrom(typeof(LinkNode))]
    public class LinkNodeDto
    {
        /// <summary>
        /// 节点值
        /// </summary>
        public int Value { get; set; }
        /// <summary>
        /// 下一节点
        /// </summary>
        public LinkNodeDto? Next { get; set; }
    }

    // ================================================================
    // List→Dictionary 不兼容转换测试（B3 回归测试）
    // ================================================================

    /// <summary>
    /// 源实体 — Codes 为 List&lt;string&gt;
    /// </summary>
    public class ListToDictSource
    {
        /// <summary>
        /// 代码列表（List 类型）
        /// </summary>
        public List<string> Codes { get; set; } = new();
    }

    /// <summary>
    /// 目标 DTO — Codes 为 Dictionary&lt;string,string&gt;，与源 List 类型不兼容
    /// 映射时应抛 NotSupportedException（B3 修复后）
    /// </summary>
    [MapFrom(typeof(ListToDictSource))]
    public class ListToDictDto
    {
        /// <summary>
        /// 代码字典（Dictionary 类型，与源 List 不兼容）
        /// </summary>
        public Dictionary<string, string>? Codes { get; set; }
    }

    // ================================================================
    // 单对象自引用循环测试（B12 补充）
    // ================================================================

    /// <summary>
    /// 自引用节点 — Self 指向自身，可构造单对象循环（非集合路径）
    /// </summary>
    public class SelfRefNode
    {
        /// <summary>
        /// 节点值
        /// </summary>
        public int Value { get; set; }
        /// <summary>
        /// 自引用（可指向自身形成环）
        /// </summary>
        public SelfRefNode? Self { get; set; }
    }

    /// <summary>
    /// 自引用节点 DTO
    /// </summary>
    [MapFrom(typeof(SelfRefNode))]
    public class SelfRefNodeDto
    {
        /// <summary>
        /// 节点值
        /// </summary>
        public int Value { get; set; }
        /// <summary>
        /// 自引用
        /// </summary>
        public SelfRefNodeDto? Self { get; set; }
    }

    // ================================================================
    // B4 回归测试 — 多表合成 DTO 的混合类型字典映射
    // 体现 [MapFrom]+[MapProperty] 多表合成设计：DTO 从 A/B 两个类合成
    // ================================================================

    /// <summary>
    /// 源类 A — 主映射类，提供 NameA/ValueA 字段
    /// </summary>
    public class DictSourceA
    {
        /// <summary>
        /// A 类名称字段
        /// </summary>
        public string NameA { get; set; } = "";
        /// <summary>
        /// A 类数值字段
        /// </summary>
        public int ValueA { get; set; }
    }

    /// <summary>
    /// 源类 B — 辅助映射类，提供 NameB/ValueB 字段
    /// </summary>
    public class DictSourceB
    {
        /// <summary>
        /// B 类名称字段
        /// </summary>
        public string NameB { get; set; } = "";
        /// <summary>
        /// B 类数值字段
        /// </summary>
        public int ValueB { get; set; }
    }

    /// <summary>
    /// 合成 DTO — 主映射 DictSourceA，部分字段从 DictSourceB 映射
    /// [MapFrom(typeof(A))] 声明主映射类 A，未标注 MapProperty 的字段默认从 A 同名映射
    /// [MapProperty(typeof(A), "NameA")] 显式指定从 A.NameA 映射
    /// [MapProperty(typeof(B), "NameB")] 指定从 B.NameB 映射（当 source 是 B 时生效）
    /// 这样：A→DTO 映射 NameA/ValueA；B→DTO 映射 NameB/ValueB；DTO→A 反填 NameA/ValueA；DTO→B 反填 NameB/ValueB
    /// </summary>
    [MapFrom(typeof(DictSourceA))]
    public class DictTargetDto
    {
        /// <summary>
        /// 从 A.NameA 映射（source 是 A 时）
        /// </summary>
        [MapProperty(typeof(DictSourceA), nameof(DictSourceA.NameA))]
        public string NameA { get; set; } = "";

        /// <summary>
        /// 从 A.ValueA 映射（source 是 A 时）
        /// </summary>
        [MapProperty(typeof(DictSourceA), nameof(DictSourceA.ValueA))]
        public int ValueA { get; set; }

        /// <summary>
        /// 从 B.NameB 映射（source 是 B 时）
        /// </summary>
        [MapProperty(typeof(DictSourceB), nameof(DictSourceB.NameB))]
        public string NameB { get; set; } = "";

        /// <summary>
        /// 从 B.ValueB 映射（source 是 B 时）
        /// </summary>
        [MapProperty(typeof(DictSourceB), nameof(DictSourceB.ValueB))]
        public int ValueB { get; set; }
    }

    // ================================================================
    // B2 回归测试 — 大小写不敏感匹配
    // ================================================================

    /// <summary>
    /// 大小写不匹配 DTO — 属性名全小写，验证与 FullTypeEntity（首字母大写）的匹配
    /// </summary>
    [MapFrom(typeof(FullTypeEntity))]
    public class CaseMismatchDto
    {
        /// <summary>
        /// 对应 IntProp（大小写不敏感匹配）
        /// </summary>
        public int intprop { get; set; }
        /// <summary>
        /// 对应 StringProp（大小写不敏感匹配）
        /// </summary>
        public string stringprop { get; set; } = "";
    }

    // ================================================================
    // B7+B8 测试 — string[] 数组映射
    // ================================================================

    /// <summary>
    /// string 数组实体
    /// </summary>
    public class StringArrayEntity
    {
        /// <summary>
        /// 字符串数组
        /// </summary>
        public string[]? Tags { get; set; }
    }

    /// <summary>
    /// string 数组 DTO
    /// </summary>
    [MapFrom(typeof(StringArrayEntity))]
    public class StringArrayDto
    {
        /// <summary>
        /// 字符串数组
        /// </summary>
        public string[]? Tags { get; set; }
    }

    // ================================================================
    // B10 测试 — MapIgnore 过滤一致性
    // ================================================================

    /// <summary>
    /// 含 MapIgnore 字段的实体
    /// </summary>
    public class MapIgnoreTestEntity
    {
        /// <summary>
        /// 正常映射字段
        /// </summary>
        public string? MappedField { get; set; }

        /// <summary>
        /// 标注 MapIgnore，不应参与映射
        /// </summary>
        public string? IgnoredField { get; set; }
    }

    /// <summary>
    /// 目标 DTO — IgnoredField 不应被填充
    /// </summary>
    [MapFrom(typeof(MapIgnoreTestEntity))]
    public class MapIgnoreTestDto
    {
        /// <summary>
        /// 正常映射字段
        /// </summary>
        public string? MappedField { get; set; }

        /// <summary>
        /// IgnoredField 在源端标注 MapIgnore，映射后应保持 null
        /// </summary>
        public string? IgnoredField { get; set; }
    }

    // ================================================================
    // B11 测试 — record/无无参构造类型支持
    // ================================================================

    /// <summary>
    /// 普通实体 — 用于映射到 record DTO
    /// </summary>
    public class RecordPersonEntity
    {
        /// <summary>
        /// 姓名
        /// </summary>
        public string Name { get; set; } = "";
        /// <summary>
        /// 年龄
        /// </summary>
        public int Age { get; set; }
        /// <summary>
        /// 邮箱
        /// </summary>
        public string Email { get; set; } = "";
    }

    /// <summary>
    /// record DTO — 主构造函数带参，无无参构造
    /// Name/Age/Email 通过构造器参数传入，也作为 init 属性
    /// </summary>
    /// <param name="Name">姓名</param>
    /// <param name="Age">年龄</param>
    /// <param name="Email">邮箱</param>
    public record RecordPersonDto(string Name, int Age, string Email);

    /// <summary>
    /// record + 额外属性 — 主构造函数只含 Name/Age，Extra 是额外的可写属性
    /// 验证构造器参数匹配 + 额外属性赋值（构造后通过 MapAction 赋值）
    /// </summary>
    /// <param name="Name">姓名</param>
    /// <param name="Age">年龄</param>
    public record RecordWithExtraDto(string Name, int Age)
    {
        /// <summary>
        /// 额外属性 — 不在构造器参数中，需构造后赋值
        /// </summary>
        public string? Extra { get; set; }
    }

    /// <summary>
    /// 嵌套 record 测试 — Parent 包含 Child record
    /// </summary>
    public class NestedRecordEntity
    {
        /// <summary>
        /// 标题
        /// </summary>
        public string Title { get; set; } = "";
        /// <summary>
        /// 子对象
        /// </summary>
        public RecordPersonEntity? Child { get; set; }
    }

    /// <summary>
    /// 嵌套 record DTO — Child 是 record 类型
    /// 验证 GetClassExpression 对 record 嵌套类型的 MakeGenericType 不再因 new() 约束失败
    /// </summary>
    [MapFrom(typeof(NestedRecordEntity))]
    public class NestedRecordDto
    {
        /// <summary>
        /// 标题
        /// </summary>
        public string Title { get; set; } = "";
        /// <summary>
        /// 子对象（record 类型，走 GetClassExpression 嵌套映射）
        /// </summary>
        public RecordPersonDto? Child { get; set; }
    }

    /// <summary>
    /// 多构造器 record — 有无参构造和带参构造
    /// 验证构造器选择逻辑：无参构造优先（走现有路径）
    /// </summary>
    public record MultiCtorRecordDto
    {
        /// <summary>
        /// 姓名
        /// </summary>
        public string Name { get; set; } = "";
        /// <summary>
        /// 年龄
        /// </summary>
        public int Age { get; set; }

        /// <summary>
        /// 无参构造
        /// </summary>
        public MultiCtorRecordDto() { }

        /// <summary>
        /// 带参构造
        /// </summary>
        /// <param name="name">姓名</param>
        /// <param name="age">年龄</param>
        public MultiCtorRecordDto(string name, int age) { Name = name; Age = age; }
    }

    // ================================================================
    // 同名属性（new/override）测试模型
    // ================================================================

    /// <summary>
    /// 基类 — Id 为 string 类型
    /// </summary>
    public abstract class ShadowBaseEntity
    {
        /// <summary>
        /// 基类 Id（string）
        /// </summary>
        public virtual string? Id { get; set; }
        /// <summary>
        /// 基类 Code
        /// </summary>
        public string Code { get; set; } = "BASE";
    }

    /// <summary>
    /// 子类 — Id 用 new 隐藏为 int 类型，Code 用 new 隐藏为不同默认值
    /// </summary>
    public class ShadowChildEntity : ShadowBaseEntity
    {
        /// <summary>
        /// 子类 Id（int）— new 隐藏基类 string Id
        /// </summary>
        public new int Id { get; set; } = 42;
        /// <summary>
        /// 子类 Code — new 隐藏基类 Code
        /// </summary>
        public new string Code { get; set; } = "CHILD";
        /// <summary>
        /// 子类独有属性
        /// </summary>
        public string Name { get; set; } = "ShadowChild";
    }

    /// <summary>
    /// 目标 DTO — Id 为 int，应映射子类的 Id 而非基类的 string Id
    /// </summary>
    [MapFrom(typeof(ShadowChildEntity))]
    public class ShadowChildDto
    {
        /// <summary>
        /// 期望映射子类 int Id = 42
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// 期望映射子类 Code = "CHILD"
        /// </summary>
        public string Code { get; set; } = "";
        /// <summary>
        /// 子类独有属性
        /// </summary>
        public string Name { get; set; } = "";
    }

    /// <summary>
    /// override 场景基类
    /// </summary>
    public class OverrideBaseEntity
    {
        /// <summary>
        /// 基类 Amount（double）
        /// </summary>
        public virtual double Amount { get; set; } = 1.5;
    }

    /// <summary>
    /// 子类 — override 改变行为但不改类型
    /// </summary>
    public class OverrideChildEntity : OverrideBaseEntity
    {
        /// <summary>
        /// override Amount — 返回基类值乘以 10
        /// </summary>
        public override double Amount { get => base.Amount * 10; set => base.Amount = value; }
        /// <summary>
        /// 子类独有
        /// </summary>
        public string Label { get; set; } = "Override";
    }

    /// <summary>
    /// override 场景 DTO
    /// </summary>
    [MapFrom(typeof(OverrideChildEntity))]
    public class OverrideChildDto
    {
        /// <summary>
        /// 期望映射 override 后的值（15.0）
        /// </summary>
        public double Amount { get; set; }
        /// <summary>
        /// 子类独有
        /// </summary>
        public string Label { get; set; } = "";
    }

    // ================================================================
    // T06: 接口/抽象类/struct 作为映射目标的负面测试模型
    // ================================================================

    /// <summary>
    /// 接口目标类型 — 接口无法实例化，作为映射目标应抛出异常
    /// </summary>
    public interface ITargetInterface
    {
        /// <summary>
        /// 整型属性
        /// </summary>
        int IntProp { get; set; }
        /// <summary>
        /// 字符串属性
        /// </summary>
        string StringProp { get; set; }
    }

    /// <summary>
    /// 抽象类目标类型 — 抽象类无法实例化，作为映射目标应抛出异常
    /// </summary>
    public abstract class AbstractTargetBase
    {
        /// <summary>
        /// 整型属性
        /// </summary>
        public int IntProp { get; set; }
        /// <summary>
        /// 字符串属性
        /// </summary>
        public string StringProp { get; set; } = "";
    }

    /// <summary>
    /// 值类型（struct）目标 — Mapper.Map 的 class 泛型约束阻止 struct 作为目标类型
    /// 用于 MapValidator.ValidateMapping 验证 struct 类型的兼容性检查行为
    /// </summary>
    public struct StructTarget
    {
        /// <summary>
        /// 整型属性
        /// </summary>
        public int IntProp { get; set; }
        /// <summary>
        /// 字符串属性
        /// </summary>
        public string StringProp { get; set; }
    }
}
