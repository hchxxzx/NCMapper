using NCMapper.Attributes;
using System.ComponentModel.DataAnnotations.Schema;

namespace NCMapper.Tests.Models
{
    // ================================================================
    // DTO 1：同类型映射 — 字段名和类型与实体完全一致
    // ================================================================

    /// <summary>
    /// 同类型映射 DTO — 字段名与 Entity 完全一致，类型也相同，直接赋值
    /// </summary>
    [MapFrom(typeof(FullTypeEntity))]
    public class SameTypeDto
    {
        /// <summary>
        /// 整型属性
        /// </summary>
        public int IntProp { get; set; }
        /// <summary>
        /// 长整型属性
        /// </summary>
        public long LongProp { get; set; }
        /// <summary>
        /// 十进制属性
        /// </summary>
        public decimal DecimalProp { get; set; }
        /// <summary>
        /// 浮点属性
        /// </summary>
        public float FloatProp { get; set; }
        /// <summary>
        /// 双精度属性
        /// </summary>
        public double DoubleProp { get; set; }
        /// <summary>
        /// 字符串属性
        /// </summary>
        public string StringProp { get; set; } = "";
        /// <summary>
        /// 布尔属性
        /// </summary>
        public bool BoolProp { get; set; }
        /// <summary>
        /// 日期时间属性
        /// </summary>
        public DateTime DateTimeProp { get; set; }
        /// <summary>
        /// GUID 属性
        /// </summary>
        public Guid GuidProp { get; set; }
    }

    // ================================================================
    // DTO 2：值类型 → string — Entity 的值类型转 DTO 的字符串
    // ================================================================

    /// <summary>
    /// 值类型 → string DTO — 所有字段类型为 string，
    /// 对应 Entity 的值类型字段通过 TypeConvertHelper 转为字符串
    /// </summary>
    [MapFrom(typeof(FullTypeEntity))]
    public class ValueToStringDto
    {
        /// <summary>
        /// int → string：42 → "42"
        /// </summary>
        public string IntProp { get; set; } = "";

        /// <summary>
        /// long → string：9999999999 → "9999999999"
        /// </summary>
        public string LongProp { get; set; } = "";

        /// <summary>
        /// decimal → string：123.45 → "123.45"
        /// </summary>
        public string DecimalProp { get; set; } = "";

        /// <summary>
        /// float → string：3.14 → "3.14"
        /// </summary>
        public string FloatProp { get; set; } = "";

        /// <summary>
        /// double → string：2.71828 → "2.71828"
        /// </summary>
        public string DoubleProp { get; set; } = "";

        /// <summary>
        /// bool → string：true → "True"
        /// </summary>
        public string BoolProp { get; set; } = "";

        /// <summary>
        /// DateTime → string：包含日期字符串
        /// </summary>
        public string DateTimeProp { get; set; } = "";

        /// <summary>
        /// Guid → string：小写连字符格式
        /// </summary>
        public string GuidProp { get; set; } = "";
    }

    // ================================================================
    // DTO 3：string → 值类型 + 同名字段 — 字段名匹配优先
    // ================================================================

    /// <summary>
    /// string → 值类型 DTO：
    /// StringProp 字段名匹配 Entity.StringProp（string），类型不同 → TypeConvertHelper 转换；
    /// LongProp 字段名匹配 Entity.LongProp（long），类型相同 → 直接赋值
    /// </summary>
    [MapFrom(typeof(FullTypeEntity))]
    public class StringToValueDto
    {
        /// <summary>
        /// 字段名匹配 Entity.StringProp，类型不同 string→int
        /// </summary>
        public int StringProp { get; set; }

        /// <summary>
        /// 字段名匹配 Entity.LongProp，类型相同 long→long
        /// </summary>
        public long LongProp { get; set; }
    }

    // ================================================================
    // DTO 4：[MapProperty] string → numeric — 跨字段名 + 跨类型
    // ================================================================

    /// <summary>
    /// 通过 [MapProperty] 显式指定源字段名，实现 string→numeric 的跨名+跨类型转换
    /// </summary>
    [MapFrom(typeof(FullTypeEntity))]
    public class StringToNumericDto
    {
        /// <summary>
        /// Entity.StringProp → DTO.IntValue（string→int）
        /// </summary>
        [MapProperty(typeof(FullTypeEntity), nameof(FullTypeEntity.StringProp))]
        public int IntValue { get; set; }

        /// <summary>
        /// Entity.StringProp → DTO.LongValue（string→long）
        /// </summary>
        [MapProperty(typeof(FullTypeEntity), nameof(FullTypeEntity.StringProp))]
        public long LongValue { get; set; }

        /// <summary>
        /// Entity.StringProp → DTO.DecimalValue（string→decimal）
        /// </summary>
        [MapProperty(typeof(FullTypeEntity), nameof(FullTypeEntity.StringProp))]
        public decimal DecimalValue { get; set; }

        /// <summary>
        /// Entity.StringProp → DTO.DateValue（string→DateTime）
        /// </summary>
        [MapProperty(typeof(FullTypeEntity), nameof(FullTypeEntity.StringProp))]
        public DateTime DateValue { get; set; }
    }

    // ================================================================
    // DTO 4b：[MapProperty] string → numeric only — 无 DateTime 字段
    // ================================================================

    /// <summary>
    /// T13: 纯数值版 string→numeric DTO，不含 DateValue 字段，
    /// 用于多字段跨类型映射测试，避免 DateTime 转换干扰
    /// </summary>
    [MapFrom(typeof(FullTypeEntity))]
    public class StringToNumericOnlyDto
    {
        /// <summary>
        /// Entity.StringProp → DTO.IntValue（string→int）
        /// </summary>
        [MapProperty(typeof(FullTypeEntity), nameof(FullTypeEntity.StringProp))]
        public int IntValue { get; set; }

        /// <summary>
        /// Entity.StringProp → DTO.LongValue（string→long）
        /// </summary>
        [MapProperty(typeof(FullTypeEntity), nameof(FullTypeEntity.StringProp))]
        public long LongValue { get; set; }

        /// <summary>
        /// Entity.StringProp → DTO.DecimalValue（string→decimal）
        /// </summary>
        [MapProperty(typeof(FullTypeEntity), nameof(FullTypeEntity.StringProp))]
        public decimal DecimalValue { get; set; }
    }

    // ================================================================
    // DTO 5/6：枚举映射 — enum → int / string
    // ================================================================

    /// <summary>
    /// 枚举 → int DTO — OrderStatus → 底层数值
    /// </summary>
    [MapFrom(typeof(FullTypeEntity))]
    public class EnumToIntDto
    {
        /// <summary>
        /// OrderStatus → int：Shipped=2、Delivered=3
        /// </summary>
        public int StatusProp { get; set; }
    }

    /// <summary>
    /// 枚举 → string DTO — OrderStatus → 成员名称字符串
    /// </summary>
    [MapFrom(typeof(FullTypeEntity))]
    public class EnumToStringDto
    {
        /// <summary>
        /// OrderStatus → string："Cancelled"、"Pending"
        /// </summary>
        public string StatusProp { get; set; } = "";
    }

    // ================================================================
    // DTO 7：[MapProperty] — 字段名不同的映射
    // ================================================================

    /// <summary>
    /// [MapProperty] 字段名映射 DTO —
    /// 通过特性指定源字段名，实现 Entity 与 DTO 不同字段名的映射
    /// </summary>
    [MapFrom(typeof(FullTypeEntity))]
    public class FieldMappingDto
    {
        /// <summary>
        /// [MapProperty] Entity.StringProp → DTO.FullName
        /// </summary>
        [MapProperty(typeof(FullTypeEntity), nameof(FullTypeEntity.StringProp))]
        public string FullName { get; set; } = "";

        /// <summary>
        /// [MapProperty] Entity.IntProp → DTO.Age
        /// </summary>
        [MapProperty(typeof(FullTypeEntity), nameof(FullTypeEntity.IntProp))]
        public int Age { get; set; }

        /// <summary>
        /// 同名字段 LongProp，无需 [MapProperty]
        /// </summary>
        public long LongProp { get; set; }
    }

    // ================================================================
    // DTO 8：JSON 序列化映射 — IsFromJson
    // ================================================================

    /// <summary>
    /// JSON 反序列化 DTO —
    /// 使用 [MapProperty(IsFromJson = true)] 将 Entity 的 JSON 字符串反序列化为强类型对象
    /// </summary>
    [MapFrom(typeof(JsonFieldEntity))]
    public class JsonToObjectDto
    {
        /// <summary>
        /// 同名字段 Id，直接赋值
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// JSON string "{"Theme":"Dark",...}" → AppConfig 对象
        /// </summary>
        [MapProperty(typeof(JsonFieldEntity), nameof(JsonFieldEntity.ConfigJson), IsFromJson = true)]
        public AppConfig? Config { get; set; }

        /// <summary>
        /// JSON string "["csharp",...]" → List&lt;string&gt;
        /// </summary>
        [MapProperty(typeof(JsonFieldEntity), nameof(JsonFieldEntity.TagsJson), IsFromJson = true)]
        public List<string>? Tags { get; set; }
    }

    // ================================================================
    // T13: 扩展 JSON 映射 DTO
    // ================================================================

    /// <summary>
    /// T13: 嵌套 JSON + 字典 JSON DTO — 反序列化复杂 JSON 结构
    /// </summary>
    [MapFrom(typeof(JsonFieldEntity))]
    public class JsonNestedDto
    {
        /// <summary>T13: Id 同名直接映射</summary>
        public int Id { get; set; }

        /// <summary>T13: JSON 对象数组 → List&lt;AppConfig&gt;</summary>
        [MapProperty(typeof(JsonFieldEntity), nameof(JsonFieldEntity.NestedObjectsJson), IsFromJson = true)]
        public List<AppConfig>? NestedConfigs { get; set; }

        /// <summary>T13: JSON 对象 → Dictionary&lt;string, int&gt;</summary>
        [MapProperty(typeof(JsonFieldEntity), nameof(JsonFieldEntity.MetadataJson), IsFromJson = true)]
        public Dictionary<string, int>? Metadata { get; set; }
    }

    /// <summary>
    /// T13: 空 JSON 集合 DTO — 验证空数组/空对象反序列化
    /// </summary>
    [MapFrom(typeof(JsonFieldEntity))]
    public class JsonEmptyDto
    {
        /// <summary>T13: Id 同名直接映射</summary>
        public int Id { get; set; }

        /// <summary>T13: 空 JSON 数组 → 空 List&lt;string&gt;</summary>
        [MapProperty(typeof(JsonFieldEntity), nameof(JsonFieldEntity.EmptyJsonArray), IsFromJson = true)]
        public List<string>? EmptyTags { get; set; }

        /// <summary>T13: 空 JSON 对象 → 空 Dictionary</summary>
        [MapProperty(typeof(JsonFieldEntity), nameof(JsonFieldEntity.MetadataJson), IsFromJson = true)]
        public Dictionary<string, int>? EmptyMetadata { get; set; }
    }

    // ================================================================
    // DTO 9：可空类型映射
    // ================================================================

    /// <summary>
    /// 可空类型 DTO — int? → long：HasValue?Convert:Default
    /// </summary>
    [MapFrom(typeof(FullTypeEntity))]
    public class NullableDto
    {
        /// <summary>
        /// int? 有值 100 → long 100
        /// </summary>
        public long NullableIntProp { get; set; }

        /// <summary>
        /// int? null → long 0（默认值）
        /// </summary>
        public long NullIntNull { get; set; }
    }

    // ================================================================
    // DTO 10：[MapIgnore] 忽略字段
    // ================================================================

    /// <summary>
    /// [MapIgnore] DTO — 标记了 [MapIgnore] 的属性不参与映射，保持原始值
    /// </summary>
    [MapFrom(typeof(FullTypeEntity))]
    public class MapIgnoreDto
    {
        /// <summary>
        /// 正常映射
        /// </summary>
        public int IntProp { get; set; }
        /// <summary>
        /// 正常映射
        /// </summary>
        public string StringProp { get; set; } = "";

        /// <summary>
        /// [MapIgnore] 标记 — 双向忽略，保持 "ORIGINAL_VALUE"
        /// </summary>
        [MapIgnore]
        public string IgnoredField { get; set; } = "ORIGINAL_VALUE";
    }

    // ================================================================
    // DTO 11：嵌套类映射
    // ================================================================

    /// <summary>
    /// 地址 DTO — 嵌套在 CustomerDto 中的子对象
    /// </summary>
    [MapFrom(typeof(Address))]
    public class AddressDto
    {
        /// <summary>
        /// 城市
        /// </summary>
        public string City { get; set; } = "";
        /// <summary>
        /// 街道
        /// </summary>
        public string Street { get; set; } = "";
        /// <summary>
        /// 邮政编码
        /// </summary>
        public int ZipCode { get; set; }
    }

    /// <summary>
    /// 客户 DTO — 包含嵌套 AddressDto，Mapper 自动递归映射子对象
    /// </summary>
    [MapFrom(typeof(CustomerEntity))]
    public class CustomerDto
    {
        /// <summary>
        /// 客户ID
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// 客户名称
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// 嵌套 Address → AddressDto（MapperBase 检测到 class↔class 自动递归）
        /// </summary>
        public AddressDto? HomeAddress { get; set; }
    }

    // ================================================================
    // DTO 12：多源混合映射
    // ================================================================

    /// <summary>
    /// 多源混合 DTO — 主映射从 CustomerEntity 取值，
    /// [MapProperty] 和 Action 回调从 FullTypeEntity 补充字段
    /// </summary>
    [MapFrom(typeof(CustomerEntity))]
    public class MixedSourceDto
    {
        /// <summary>
        /// 来自 CustomerEntity.Id（同名字段）
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// 来自 CustomerEntity.Name（同名字段）
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// 通过 Action 从 FullTypeEntity.IntProp 填入
        /// </summary>
        [MapProperty(typeof(FullTypeEntity), nameof(FullTypeEntity.IntProp))]
        public int Score { get; set; }

        /// <summary>
        /// 通过 Action 从 FullTypeEntity.StatusProp 填入
        /// </summary>
        [MapProperty(typeof(FullTypeEntity), nameof(FullTypeEntity.StatusProp))]
        public string StatusText { get; set; } = "";
    }

    // ================================================================
    // DTO 13：多级嵌套映射 — Company → Department → Team → Member
    // ================================================================

    /// <summary>
    /// 公司 DTO — 四级嵌套的顶层，每层子对象均有对应的 DTO
    /// </summary>
    [MapFrom(typeof(Company))]
    public class CompanyDto
    {
        /// <summary>
        /// 公司名（同名字段）
        /// </summary>
        public string Name { get; set; } = "";
        /// <summary>
        /// 一级嵌套：部门 DTO 列表
        /// </summary>
        public List<DepartmentDto>? Departments { get; set; }
    }

    /// <summary>
    /// 部门 DTO — 二级嵌套
    /// </summary>
    [MapFrom(typeof(Department))]
    public class DepartmentDto
    {
        /// <summary>
        /// 部门名
        /// </summary>
        public string Name { get; set; } = "";
        /// <summary>
        /// 二级嵌套：团队 DTO 列表
        /// </summary>
        public List<TeamDto>? Teams { get; set; }
    }

    /// <summary>
    /// 团队 DTO — 三级嵌套
    /// </summary>
    [MapFrom(typeof(Team))]
    public class TeamDto
    {
        /// <summary>
        /// 团队名
        /// </summary>
        public string Name { get; set; } = "";
        /// <summary>
        /// 三级嵌套：成员 DTO 列表
        /// </summary>
        public List<MemberDto>? Members { get; set; }
    }

    /// <summary>
    /// 成员 DTO — 四级嵌套的叶子节点
    /// </summary>
    [MapFrom(typeof(Member))]
    public class MemberDto
    {
        /// <summary>
        /// 成员姓名（同名字段）
        /// </summary>
        public string Name { get; set; } = "";
        /// <summary>
        /// 年龄（同名字段）
        /// </summary>
        public int Age { get; set; }
        /// <summary>
        /// 是否在职
        /// </summary>
        public bool IsActive { get; set; }
    }

    // ================================================================
    // DTO 14：集合属性映射 — Blog 包含 List&lt;Post&gt; + Dictionary
    // ================================================================

    /// <summary>
    /// 博客 DTO — 包含文章列表和标签字典
    /// </summary>
    [MapFrom(typeof(Blog))]
    public class BlogDto
    {
        /// <summary>
        /// 博客标题
        /// </summary>
        public string Title { get; set; } = "";
        /// <summary>
        /// 文章 DTO 列表（List&lt;Post&gt; → List&lt;PostDto&gt;）
        /// </summary>
        public List<PostDto>? Posts { get; set; }
        /// <summary>
        /// 标签字典（Dictionary&lt;string,int&gt; → Dictionary&lt;string,int&gt;）
        /// </summary>
        public Dictionary<string, int>? TagCounts { get; set; }
    }

    /// <summary>
    /// 文章 DTO — 集合中的元素 DTO
    /// </summary>
    [MapFrom(typeof(Post))]
    public class PostDto
    {
        /// <summary>
        /// 文章标题
        /// </summary>
        public string Title { get; set; } = "";
        /// <summary>
        /// 阅读量
        /// </summary>
        public int Views { get; set; }
    }

    // ================================================================
    // DTO 15：树形结构（自引用）映射
    // ================================================================

    /// <summary>
    /// 分类 DTO — 树形结构，Children 属性是同类型列表，递归映射子节点
    /// </summary>
    [MapFrom(typeof(Category))]
    public class CategoryDto
    {
        /// <summary>
        /// 分类名称
        /// </summary>
        public string Name { get; set; } = "";
        /// <summary>
        /// 排序号
        /// </summary>
        public int Sort { get; set; }
        /// <summary>
        /// 子分类（同类型嵌套 → 递归映射）
        /// </summary>
        public List<CategoryDto>? Children { get; set; }
    }

    // ================================================================
    // DTO 16：字典跨类型映射 — Dictionary 值类型跨越映射
    // ================================================================

    /// <summary>
    /// 文章集合 DTO — Dictionary 的值从 Post 映射到 PostDto
    /// </summary>
    [MapFrom(typeof(ArticleCollection))]
    public class ArticleCollectionDto
    {
        /// <summary>
        /// 集合名称
        /// </summary>
        public string Name { get; set; } = "";
        /// <summary>
        /// 文章字典（Post → PostDto 跨类型映射）
        /// </summary>
        public Dictionary<string, PostDto>? PostsByCategory { get; set; }
    }

    // ================================================================
    // DTO 17：多样化集合类型映射 — HashSet/Queue/Stack/Array 同类型拷贝
    // ================================================================

    // ================================================================
    // 可空类型扩展测试模型 — DateTime?/Guid?/decimal?/enum? 转换
    // ================================================================

    /// <summary>
    /// 可空类型源实体 — 包含 DateTime?/Guid?/decimal?/OrderStatus?
    /// </summary>
    public class NullableTypesEntity
    {
        /// <summary>
        /// 可空日期时间（有值）
        /// </summary>
        public DateTime? NullableDateTime { get; set; } = new DateTime(2024, 6, 15);
        /// <summary>
        /// 可空日期时间（null）
        /// </summary>
        public DateTime? NullDateTime { get; set; } = null;
        /// <summary>
        /// 可空 GUID（有值）
        /// </summary>
        public Guid? NullableGuid { get; set; } = Guid.Parse("aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb");
        /// <summary>
        /// 可空 GUID（null）
        /// </summary>
        public Guid? NullGuid { get; set; } = null;
        /// <summary>
        /// 可空十进制（有值）
        /// </summary>
        public decimal? NullableDecimal { get; set; } = 987.65m;
        /// <summary>
        /// 可空十进制（null）
        /// </summary>
        public decimal? NullDecimal { get; set; } = null;
        /// <summary>
        /// 可空枚举（有值）
        /// </summary>
        public OrderStatus? NullableStatus { get; set; } = OrderStatus.Delivered;
        /// <summary>
        /// 可空枚举（null）
        /// </summary>
        public OrderStatus? NullStatus { get; set; } = null;
        /// <summary>
        /// 非可空日期时间
        /// </summary>
        public DateTime NonNullDateTime { get; set; } = new DateTime(2023, 12, 31);
    }

    /// <summary>
    /// 可空→非可空 DTO — 有值时正常映射，null 时返回 default
    /// </summary>
    [MapFrom(typeof(NullableTypesEntity))]
    public class NullableToNonNullDto
    {
        /// <summary>
        /// 可空日期时间（有值）映射到非可空
        /// </summary>
        public DateTime NullableDateTime { get; set; }
        /// <summary>
        /// 可空日期时间（null）映射到非可空默认值
        /// </summary>
        public DateTime NullDateTime { get; set; }
        /// <summary>
        /// 可空 GUID（有值）映射到非可空
        /// </summary>
        public Guid NullableGuid { get; set; }
        /// <summary>
        /// 可空 GUID（null）映射到非可空默认值
        /// </summary>
        public Guid NullGuid { get; set; }
        /// <summary>
        /// 可空十进制（有值）映射到非可空
        /// </summary>
        public decimal NullableDecimal { get; set; }
        /// <summary>
        /// 可空十进制（null）映射到非可空默认值
        /// </summary>
        public decimal NullDecimal { get; set; }
        /// <summary>
        /// 可空枚举（有值）映射到非可空
        /// </summary>
        public OrderStatus NullableStatus { get; set; }
        /// <summary>
        /// 可空枚举（null）映射到非可空默认值
        /// </summary>
        public OrderStatus NullStatus { get; set; }
    }

    /// <summary>
    /// 非可空→可空 DTO — DateTime → DateTime?
    /// </summary>
    [MapFrom(typeof(NullableTypesEntity))]
    public class NonNullToNullableDto
    {
        /// <summary>
        /// 非可空日期时间映射到可空
        /// </summary>
        public DateTime? NonNullDateTime { get; set; }
    }

    // ================================================================
    // DTO 17：多样化集合类型映射 — HashSet/Queue/Stack/Array 同类型拷贝
    // ================================================================

    /// <summary>
    /// 多集合类型 DTO — 全部字段同类型，验证 ConvertListExpression 对各种容器的支持
    /// </summary>
    [MapFrom(typeof(MultiCollectionEntity))]
    public class MultiCollectionDto
    {
        /// <summary>
        /// List&lt;string&gt; → 走 CopyList
        /// </summary>
        public List<string>? NameList { get; set; }

        /// <summary>
        /// HashSet&lt;string&gt; → 走 CopyHashSet（构造器拷贝）
        /// </summary>
        public HashSet<string>? TagSet { get; set; }

        /// <summary>
        /// Queue&lt;int&gt; → 走 CopyQueue（构造器拷贝）
        /// </summary>
        public Queue<int>? TaskQueue { get; set; }

        /// <summary>
        /// Stack&lt;int&gt; → 走 CopyStack（构造器+Reverse）
        /// </summary>
        public Stack<int>? HistoryStack { get; set; }

        /// <summary>
        /// int[] → 走 CopyArray (Clone)
        /// </summary>
        public int[]? Scores { get; set; }
    }

    // ================================================================
    // T10: 扩展枚举 DTO — [Flags] / byte 底层 / long 底层
    // ================================================================

    /// <summary>
    /// [Flags] 枚举 → int DTO — Permissions.Read|Write = 3
    /// </summary>
    [MapFrom(typeof(FlagsEnumEntity))]
    public class FlagsEnumToIntDto
    {
        /// <summary>Permissions → int（位组合数值）</summary>
        public int Access { get; set; }
    }

    /// <summary>
    /// [Flags] 枚举 → string DTO — "Read, Write"
    /// </summary>
    [MapFrom(typeof(FlagsEnumEntity))]
    public class FlagsEnumToStringDto
    {
        /// <summary>Permissions → string（组合名称）</summary>
        public string Access { get; set; } = "";
    }

    /// <summary>
    /// byte 底层枚举 → int DTO — ByteStatus.Active = 1
    /// </summary>
    [MapFrom(typeof(ByteEnumEntity))]
    public class ByteEnumToIntDto
    {
        /// <summary>ByteStatus → int</summary>
        public int Status { get; set; }
    }

    /// <summary>
    /// byte 底层枚举 → string DTO — "Active"
    /// </summary>
    [MapFrom(typeof(ByteEnumEntity))]
    public class ByteEnumToStringDto
    {
        /// <summary>ByteStatus → string</summary>
        public string Status { get; set; } = "";
    }

    /// <summary>
    /// long 底层枚举 → long DTO — FileSize.Large = 1073741824
    /// </summary>
    [MapFrom(typeof(LongEnumEntity))]
    public class LongEnumToLongDto
    {
        /// <summary>FileSize → long</summary>
        public long Size { get; set; }
    }

    /// <summary>
    /// long 底层枚举 → string DTO — "Large"
    /// </summary>
    [MapFrom(typeof(LongEnumEntity))]
    public class LongEnumToStringDto
    {
        /// <summary>FileSize → string</summary>
        public string Size { get; set; } = "";
    }

    // ================================================================
    // 接口源映射 DTO — 验证接口继承链属性完整映射
    // ================================================================

    /// <summary>
    /// 接口源 DTO — 源为 IProductEntity（继承 IEntityBase），验证继承接口属性也能映射
    /// </summary>
    [MapFrom(typeof(IProductEntity))]
    public class ProductFromInterfaceDto
    {
        /// <summary>来自 IEntityBase.Id</summary>
        public int Id { get; set; }
        /// <summary>来自 IEntityBase.CreatedAt</summary>
        public DateTime CreatedAt { get; set; }
        /// <summary>来自 IProductEntity.Name</summary>
        public string Name { get; set; } = "";
        /// <summary>来自 IProductEntity.Price</summary>
        public decimal Price { get; set; }
        /// <summary>来自 IProductEntity.Category</summary>
        public string Category { get; set; } = "";
    }

    /// <summary>
    /// 简单接口源 DTO — 无继承，直接属性映射
    /// </summary>
    [MapFrom(typeof(ISimpleService))]
    public class SimpleServiceDto
    {
        /// <summary>服务名</summary>
        public string Name { get; set; } = "";
        /// <summary>版本号</summary>
        public int Version { get; set; }
        /// <summary>是否活跃</summary>
        public bool IsActive { get; set; }
    }

    /// <summary>
    /// 嵌套地址 DTO（接口源测试专用）
    /// </summary>
    [MapFrom(typeof(AddressInfo))]
    public class OrderAddressDto
    {
        /// <summary>城市</summary>
        public string City { get; set; } = "";
        /// <summary>街道</summary>
        public string Street { get; set; } = "";
    }

    /// <summary>
    /// 接口源 + 嵌套对象 DTO
    /// </summary>
    [MapFrom(typeof(IOrderSource))]
    public class OrderFromInterfaceDto
    {
        /// <summary>订单号</summary>
        public int Id { get; set; }
        /// <summary>收货地址（嵌套映射）</summary>
        public OrderAddressDto? Address { get; set; }
    }

    /// <summary>
    /// 接口源 + 集合属性 DTO
    /// </summary>
    [MapFrom(typeof(ICollectionSource))]
    public class CollectionFromInterfaceDto
    {
        /// <summary>标识</summary>
        public int Id { get; set; }
        /// <summary>标签列表</summary>
        public List<string>? Tags { get; set; }
        /// <summary>分数数组</summary>
        public int[]? Scores { get; set; }
    }
}
