namespace NCMapper.Tests.Models
{
    // ================================================================
    // 测试实体（源对象）— 供 Mapper 作为数据源
    // ================================================================

    /// <summary>
    /// 全类型字段实体 — 覆盖所有常见基元类型，用于测试同类型映射和跨类型转换
    /// </summary>
    public class FullTypeEntity
    {
        /// <summary>
        /// 整型
        /// </summary>
        public int IntProp { get; set; } = 42;

        /// <summary>
        /// 长整型
        /// </summary>
        public long LongProp { get; set; } = 9999999999L;

        /// <summary>
        /// 高精度小数
        /// </summary>
        public decimal DecimalProp { get; set; } = 123.45m;

        /// <summary>
        /// 单精度浮点
        /// </summary>
        public float FloatProp { get; set; } = 3.14f;

        /// <summary>
        /// 双精度浮点
        /// </summary>
        public double DoubleProp { get; set; } = 2.71828;

        /// <summary>
        /// 字符串（同时也是 string→值类型 转换的测试源）
        /// </summary>
        public string StringProp { get; set; } = "hello";

        /// <summary>
        /// 布尔
        /// </summary>
        public bool BoolProp { get; set; } = true;

        /// <summary>
        /// 日期时间
        /// </summary>
        public DateTime DateTimeProp { get; set; } = new(2024, 6, 1, 12, 30, 0);

        /// <summary>
        /// GUID
        /// </summary>
        public Guid GuidProp { get; set; } = Guid.Parse("11111111-2222-3333-4444-555555555555");

        /// <summary>
        /// 枚举（测试 enum→int / enum→string）
        /// </summary>
        public OrderStatus StatusProp { get; set; } = OrderStatus.Shipped;

        /// <summary>
        /// 可空整型（有值）
        /// </summary>
        public int? NullableIntProp { get; set; } = 100;

        /// <summary>
        /// 可空整型（null）
        /// </summary>
        public int? NullIntNull { get; set; } = null;
    }

    /// <summary>
    /// 订单状态枚举 — 测试 enum ↔ int / string 互转
    /// </summary>
    public enum OrderStatus
    {
        /// <summary>
        /// 待处理 = 0
        /// </summary>
        Pending = 0,
        /// <summary>
        /// 处理中 = 1
        /// </summary>
        Processing = 1,
        /// <summary>
        /// 已发货 = 2
        /// </summary>
        Shipped = 2,
        /// <summary>
        /// 已送达 = 3
        /// </summary>
        Delivered = 3,
        /// <summary>
        /// 已取消 = 4
        /// </summary>
        Cancelled = 4
    }

    // ================================================================
    // 嵌套实体 — 测试嵌套类递归映射
    // ================================================================

    /// <summary>
    /// 地址实体 — 嵌套在 CustomerEntity 中作为子对象
    /// </summary>
    public class Address
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
        /// 邮编
        /// </summary>
        public int ZipCode { get; set; }
    }

    /// <summary>
    /// 客户实体 — 包含 Address 子对象，测试嵌套映射
    /// </summary>
    public class CustomerEntity
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
        /// 家庭地址（嵌套对象）
        /// </summary>
        public Address? HomeAddress { get; set; }
        /// <summary>
        /// 订单列表（集合类型）
        /// </summary>
        public List<OrderItem>? Orders { get; set; }
    }

    /// <summary>
    /// 订单项 — 集合元素类型
    /// </summary>
    public class OrderItem
    {
        /// <summary>
        /// 商品名
        /// </summary>
        public string ProductName { get; set; } = "";
        /// <summary>
        /// 数量
        /// </summary>
        public int Quantity { get; set; }
        /// <summary>
        /// 单价
        /// </summary>
        public decimal Price { get; set; }
    }

    // ================================================================
    // JSON 字段实体 — 测试 [MapProperty(IsFromJson = true)]
    // ================================================================

    /// <summary>
    /// 包含 JSON 字符串字段的实体 — 测试 JSON 反序列化映射
    /// </summary>
    public class JsonFieldEntity
    {
        /// <summary>
        /// 实体ID
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// 存储为 JSON 字符串的配置数据 → 反序列化为 AppConfig 对象
        /// </summary>
        public string ConfigJson { get; set; } = "{\"Theme\":\"Dark\",\"FontSize\":14}";
        /// <summary>
        /// 存储为 JSON 字符串的标签列表 → 反序列化为 List&lt;string&gt;
        /// </summary>
        public string TagsJson { get; set; } = "[\"csharp\",\"dotnet\",\"mapper\"]";

        // ================================================================
        // T13: 扩展 JSON 测试属性
        // ================================================================

        /// <summary>
        /// T13: 嵌套 JSON 对象数组 — 反序列化为 List&lt;AppConfig&gt;
        /// </summary>
        public string NestedObjectsJson { get; set; } = "[]";

        /// <summary>
        /// T13: JSON 键值对象 — 反序列化为 Dictionary&lt;string, int&gt;
        /// </summary>
        public string MetadataJson { get; set; } = "{}";

        /// <summary>
        /// T13: 空 JSON 数组 — 反序列化为空 List&lt;string&gt;
        /// </summary>
        public string EmptyJsonArray { get; set; } = "[]";
    }

    /// <summary>
    /// 应用配置对象 — 从 ConfigJson 反序列化而来
    /// </summary>
    public class AppConfig
    {
        /// <summary>
        /// 主题
        /// </summary>
        public string Theme { get; set; } = "";
        /// <summary>
        /// 字体大小
        /// </summary>
        public int FontSize { get; set; }
    }

    // ================================================================
    // 多层嵌套实体 — 测试四级深度嵌套递归映射
    // ================================================================

    /// <summary>
    /// 公司实体 — 多级嵌套的顶层（Company → Department → Team → Member）
    /// </summary>
    public class Company
    {
        /// <summary>
        /// 公司名
        /// </summary>
        public string Name { get; set; } = "";
        /// <summary>
        /// 一级嵌套：部门列表
        /// </summary>
        public List<Department>? Departments { get; set; }
    }

    /// <summary>
    /// 部门实体 — 二级嵌套
    /// </summary>
    public class Department
    {
        /// <summary>
        /// 部门名
        /// </summary>
        public string Name { get; set; } = "";
        /// <summary>
        /// 三级嵌套：团队列表
        /// </summary>
        public List<Team>? Teams { get; set; }
    }

    /// <summary>
    /// 团队实体 — 三级嵌套
    /// </summary>
    public class Team
    {
        /// <summary>
        /// 团队名
        /// </summary>
        public string Name { get; set; } = "";
        /// <summary>
        /// 四级嵌套：成员列表
        /// </summary>
        public List<Member>? Members { get; set; }
    }

    /// <summary>
    /// 成员实体 — 四级嵌套的叶子节点，包含基元类型字段
    /// </summary>
    public class Member
    {
        /// <summary>
        /// 成员姓名
        /// </summary>
        public string Name { get; set; } = "";
        /// <summary>
        /// 年龄
        /// </summary>
        public int Age { get; set; }
        /// <summary>
        /// 是否在职
        /// </summary>
        public bool IsActive { get; set; }
    }

    // ================================================================
    // 集合属性实体 — 测试实体包含 List/Dictionary 属性的映射
    // ================================================================

    /// <summary>
    /// 博客实体 — 包含文章列表（List&lt;Post&gt;）
    /// </summary>
    public class Blog
    {
        /// <summary>
        /// 博客标题
        /// </summary>
        public string Title { get; set; } = "";
        /// <summary>
        /// 文章列表
        /// </summary>
        public List<Post>? Posts { get; set; }
        /// <summary>
        /// 标签字典（key=标签名, value=使用次数）
        /// </summary>
        public Dictionary<string, int>? TagCounts { get; set; }
    }

    /// <summary>
    /// 文章实体 — 集合元素类型
    /// </summary>
    public class Post
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
    // 树形结构实体 — 测试自引用嵌套（Category → Children List&lt;Category&gt;）
    // ================================================================

    /// <summary>
    /// 分类实体 — 自引用树形结构，子分类是同类型列表
    /// </summary>
    public class Category
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
        /// 子分类（同类型递归）
        /// </summary>
        public List<Category>? Children { get; set; }
    }

    // ================================================================
    // 字典跨类型映射实体 — 测试 Dictionary 值类型跨越映射
    // ================================================================

    /// <summary>
    /// 文章集合实体 — Dictionary 的值是 Post 对象，映射到 DTO 时 Post → PostDto
    /// </summary>
    public class ArticleCollection
    {
        /// <summary>
        /// 集合名称
        /// </summary>
        public string Name { get; set; } = "";
        /// <summary>
        /// 文章字典（key=分类, value=Post）→ 需要跨类型映射
        /// </summary>
        public Dictionary<string, Post>? PostsByCategory { get; set; }
    }

    // ================================================================
    // 多样化集合类型实体 — 测试 HashSet/Queue/Stack 等集合的映射
    // ================================================================

    /// <summary>
    /// 含多种集合类型的实体 — 验证 ConvertListExpression 对各种容器类型的支持
    /// </summary>
    public class MultiCollectionEntity
    {
        /// <summary>
        /// 字符串列表
        /// </summary>
        public List<string>? NameList { get; set; }

        /// <summary>
        /// 字符串不重复集合
        /// </summary>
        public HashSet<string>? TagSet { get; set; }

        /// <summary>
        /// 整数队列
        /// </summary>
        public Queue<int>? TaskQueue { get; set; }

        /// <summary>
        /// 整数栈
        /// </summary>
        public Stack<int>? HistoryStack { get; set; }

        /// <summary>
        /// 整型数组
        /// </summary>
        public int[]? Scores { get; set; }
    }

    // ================================================================
    // T10: 扩展枚举类型 — [Flags] / byte 底层 / long 底层
    // ================================================================

    /// <summary>
    /// [Flags] 权限枚举 — 底层类型 byte，支持位组合
    /// </summary>
    [System.Flags]
    public enum Permissions : byte
    {
        /// <summary>无权限 = 0</summary>
        None = 0,
        /// <summary>读取 = 1</summary>
        Read = 1,
        /// <summary>写入 = 2</summary>
        Write = 2,
        /// <summary>执行 = 4</summary>
        Execute = 4,
        /// <summary>全部权限 = 7</summary>
        All = Read | Write | Execute
    }

    /// <summary>
    /// byte 底层枚举 — 无 Flags，测试 byte → int 的枚举映射
    /// </summary>
    public enum ByteStatus : byte
    {
        /// <summary>活跃 = 1</summary>
        Active = 1,
        /// <summary>不活跃 = 2</summary>
        Inactive = 2,
        /// <summary>暂停 = 3</summary>
        Suspended = 3
    }

    /// <summary>
    /// long 底层枚举 — 测试 long 值域枚举映射
    /// </summary>
    public enum FileSize : long
    {
        /// <summary>小文件 = 1024</summary>
        Small = 1024L,
        /// <summary>中文件 = 1048576 (1MB)</summary>
        Medium = 1048576L,
        /// <summary>大文件 = 1073741824 (1GB)</summary>
        Large = 1073741824L
    }

    /// <summary>
    /// [Flags] 枚举源实体
    /// </summary>
    public class FlagsEnumEntity
    {
        /// <summary>组合权限：Read | Write = 3</summary>
        public Permissions Access { get; set; } = Permissions.Read | Permissions.Write;
    }

    /// <summary>
    /// byte 底层枚举源实体
    /// </summary>
    public class ByteEnumEntity
    {
        /// <summary>状态</summary>
        public ByteStatus Status { get; set; } = ByteStatus.Active;
    }

    /// <summary>
    /// long 底层枚举源实体
    /// </summary>
    public class LongEnumEntity
    {
        /// <summary>文件大小</summary>
        public FileSize Size { get; set; } = FileSize.Large;
    }

    // ================================================================
    // 接口源测试 — 验证源生成器对接口继承链的属性发现
    // ================================================================

    /// <summary>
    /// 基础接口 — 声明通用属性
    /// </summary>
    public interface IEntityBase
    {
        /// <summary>唯一标识</summary>
        int Id { get; set; }
        /// <summary>创建时间</summary>
        DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// 派生接口 — 继承 IEntityBase，声明额外属性
    /// </summary>
    public interface IProductEntity : IEntityBase
    {
        /// <summary>产品名称</summary>
        string Name { get; set; }
        /// <summary>价格</summary>
        decimal Price { get; set; }
        /// <summary>分类</summary>
        string Category { get; set; }
    }

    /// <summary>
    /// 具体实现类 — 实现 IProductEntity（含继承的 IEntityBase）
    /// </summary>
    public class ProductEntity : IProductEntity
    {
        /// <inheritdoc/>
        public int Id { get; set; } = 1001;
        /// <inheritdoc/>
        public DateTime CreatedAt { get; set; } = new(2025, 3, 15, 10, 30, 0);
        /// <inheritdoc/>
        public string Name { get; set; } = "NCMapper Pro";
        /// <inheritdoc/>
        public decimal Price { get; set; } = 99.99m;
        /// <inheritdoc/>
        public string Category { get; set; } = "Library";
    }

    // ================================================================
    // 接口源扩展测试 — 简单接口 / 嵌套对象 / 集合属性
    // ================================================================

    /// <summary>
    /// 简单接口（无继承）— 验证最基本的接口源映射
    /// </summary>
    public interface ISimpleService
    {
        /// <summary>服务名</summary>
        string Name { get; set; }
        /// <summary>版本号</summary>
        int Version { get; set; }
        /// <summary>是否活跃</summary>
        bool IsActive { get; set; }
    }

    /// <summary>
    /// 简单接口实现
    /// </summary>
    public class SimpleService : ISimpleService
    {
        /// <inheritdoc/>
        public string Name { get; set; } = "CacheService";
        /// <inheritdoc/>
        public int Version { get; set; } = 3;
        /// <inheritdoc/>
        public bool IsActive { get; set; } = true;
    }

    /// <summary>
    /// 嵌套对象地址信息（class，非接口）
    /// </summary>
    public class AddressInfo
    {
        /// <summary>城市</summary>
        public string City { get; set; } = "Beijing";
        /// <summary>街道</summary>
        public string Street { get; set; } = "Chang'an Ave";
    }

    /// <summary>
    /// 含嵌套对象属性的接口 — 验证接口源 + 嵌套 class 属性映射
    /// </summary>
    public interface IOrderSource
    {
        /// <summary>订单号</summary>
        int Id { get; set; }
        /// <summary>收货地址（嵌套对象）</summary>
        AddressInfo Address { get; set; }
    }

    /// <summary>
    /// 含嵌套对象属性的接口实现
    /// </summary>
    public class OrderSource : IOrderSource
    {
        /// <inheritdoc/>
        public int Id { get; set; } = 5001;
        /// <inheritdoc/>
        public AddressInfo Address { get; set; } = new();
    }

    /// <summary>
    /// 含集合属性的接口 — 验证接口源 + List/数组属性映射
    /// </summary>
    public interface ICollectionSource
    {
        /// <summary>标识</summary>
        int Id { get; set; }
        /// <summary>标签列表</summary>
        List<string> Tags { get; set; }
        /// <summary>分数数组</summary>
        int[] Scores { get; set; }
    }

    /// <summary>
    /// 含集合属性的接口实现
    /// </summary>
    public class CollectionSourceImpl : ICollectionSource
    {
        /// <inheritdoc/>
        public int Id { get; set; } = 7001;
        /// <inheritdoc/>
        public List<string> Tags { get; set; } = new() { "fast", "reliable" };
        /// <inheritdoc/>
        public int[] Scores { get; set; } = { 95, 87, 92 };
    }
}
