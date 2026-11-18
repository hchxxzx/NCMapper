using NCMapper.Attributes;

namespace NCMapper.IntegrationTests.Models
{
    // ================================================================
    // 单表实体 + DTO
    // ================================================================

    /// <summary>
    /// 集成测试 — 用户实体
    /// </summary>
    public class UserEntity
    {
        /// <summary>
        /// 主键标识
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// 名称
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
        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; }
        /// <summary>
        /// 是否激活
        /// </summary>
        public bool IsActive { get; set; }
        /// <summary>
        /// 薪资
        /// </summary>
        public decimal Salary { get; set; }
    }

    /// <summary>
    /// 集成测试 — 用户 DTO
    /// </summary>
    [MapFrom(typeof(UserEntity))]
    public class UserDto
    {
        /// <summary>
        /// 主键标识
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// 名称
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
        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; }
        /// <summary>
        /// 是否激活
        /// </summary>
        public bool IsActive { get; set; }
        /// <summary>
        /// 薪资
        /// </summary>
        public decimal Salary { get; set; }
    }

    /// <summary>
    /// 集成测试 — 异名字段 + 类型转换 DTO
    /// </summary>
    [MapFrom(typeof(UserEntity))]
    public class UserNameDiffDto
    {
        /// <summary>
        /// 主键标识
        /// </summary>
        public int Id { get; set; }

        [MapProperty(typeof(UserEntity), nameof(UserEntity.Name))]
        /// <summary>
        /// 全名，映射自 UserEntity.Name
        /// </summary>
        public string FullName { get; set; } = "";

        [MapProperty(typeof(UserEntity), nameof(UserEntity.Age))]
        /// <summary>
        /// 用户年龄，映射自 UserEntity.Age
        /// </summary>
        public int UserAge { get; set; }

        [MapProperty(typeof(UserEntity), nameof(UserEntity.Id))]
        /// <summary>
        /// 账户标识，映射自 UserEntity.Id（int → long 类型转换）
        /// </summary>
        public long AccountId { get; set; }  // int → long

        [MapProperty(typeof(UserEntity), nameof(UserEntity.Email))]
        /// <summary>
        /// 联系邮箱，映射自 UserEntity.Email
        /// </summary>
        public string ContactEmail { get; set; } = "";
    }

    /// <summary>
    /// 集成测试 — MapIgnore DTO
    /// </summary>
    [MapFrom(typeof(UserEntity))]
    public class UserPartialDto
    {
        /// <summary>
        /// 主键标识
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// 名称
        /// </summary>
        public string Name { get; set; } = "";

        [MapIgnore]
        /// <summary>
        /// 计算属性，映射时忽略
        /// </summary>
        public string? Computed { get; set; }
    }

    // ================================================================
    // 多表（导航属性）实体 + DTO
    // ================================================================

    /// <summary>
    /// 集成测试 — 订单实体（含 Customer 导航属性）
    /// </summary>
    public class OrderEntity
    {
        /// <summary>
        /// 主键标识
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// 订单编号
        /// </summary>
        public string OrderNo { get; set; } = "";
        /// <summary>
        /// 金额
        /// </summary>
        public decimal Amount { get; set; }
        /// <summary>
        /// 客户标识
        /// </summary>
        public int CustomerId { get; set; }

        // EF Core 导航属性
        /// <summary>
        /// 客户导航属性
        /// </summary>
        public CustomerEntity? Customer { get; set; }
    }

    /// <summary>
    /// 集成测试 — 客户实体
    /// </summary>
    public class CustomerEntity
    {
        /// <summary>
        /// 主键标识
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// 名称
        /// </summary>
        public string Name { get; set; } = "";
        /// <summary>
        /// 客户等级
        /// </summary>
        public string Level { get; set; } = "Normal";
        /// <summary>
        /// 联系电话
        /// </summary>
        public string Phone { get; set; } = "";
    }

    /// <summary>
    /// 集成测试 — 订单明细 DTO（通过导航属性跨表映射）
    /// </summary>
    [MapFrom(typeof(OrderEntity))]
    public class OrderDetailDto
    {
        [MapProperty(typeof(OrderEntity), nameof(OrderEntity.Id))]
        /// <summary>
        /// 订单标识，映射自 OrderEntity.Id
        /// </summary>
        public int OrderId { get; set; }

        [MapProperty(typeof(OrderEntity), nameof(OrderEntity.OrderNo))]
        /// <summary>
        /// 订单编号，映射自 OrderEntity.OrderNo
        /// </summary>
        public string OrderNumber { get; set; } = "";

        [MapProperty(typeof(OrderEntity), nameof(OrderEntity.Amount))]
        /// <summary>
        /// 订单总金额，映射自 OrderEntity.Amount
        /// </summary>
        public decimal TotalAmount { get; set; }

        // 多表合成 — 通过导航属性 Customer 访问
        [MapProperty(typeof(CustomerEntity), nameof(CustomerEntity.Name))]
        /// <summary>
        /// 客户名称，通过导航属性映射自 CustomerEntity.Name
        /// </summary>
        public string CustomerName { get; set; } = "";

        [MapProperty(typeof(CustomerEntity), nameof(CustomerEntity.Level))]
        /// <summary>
        /// 客户等级，通过导航属性映射自 CustomerEntity.Level
        /// </summary>
        public string CustomerLevel { get; set; } = "";

        [MapProperty(typeof(CustomerEntity), nameof(CustomerEntity.Phone))]
        /// <summary>
        /// 客户电话，通过导航属性映射自 CustomerEntity.Phone
        /// </summary>
        public string CustomerPhone { get; set; } = "";
    }

    /// <summary>
    /// 集成测试 — 嵌套对象 DTO（Order 包含 CustomerDto）
    /// </summary>
    [MapFrom(typeof(OrderEntity))]
    public class OrderWithCustomerDto
    {
        /// <summary>
        /// 主键标识
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// 订单编号
        /// </summary>
        public string OrderNo { get; set; } = "";

        // 嵌套对象 — CustomerEntity 整体映射为 CustomerDto
        [MapFrom(typeof(CustomerEntity))]
        public class CustomerDto
        {
            /// <summary>
            /// 主键标识
            /// </summary>
            public int Id { get; set; }
            /// <summary>
            /// 名称
            /// </summary>
            public string Name { get; set; } = "";
            /// <summary>
            /// 客户等级
            /// </summary>
            public string Level { get; set; } = "";
        }

        /// <summary>
        /// 嵌套客户信息
        /// </summary>
        public CustomerDto? Customer { get; set; }
    }

    // ================================================================
    // 可空类型测试
    // ================================================================

    /// <summary>
    /// 集成测试 — 可空类型实体
    /// </summary>
    public class NullableEntity
    {
        /// <summary>
        /// 主键标识
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// 分数
        /// </summary>
        public int? Score { get; set; }
        /// <summary>
        /// 生日
        /// </summary>
        public DateTime? Birthday { get; set; }
    }

    /// <summary>
    /// 集成测试 — 可空类型 DTO
    /// </summary>
    [MapFrom(typeof(NullableEntity))]
    public class NullableDto
    {
        /// <summary>
        /// 主键标识
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// 分数
        /// </summary>
        public int? Score { get; set; }
        /// <summary>
        /// 生日
        /// </summary>
        public DateTime? Birthday { get; set; }
    }

    // ================================================================
    // T02: 枚举类型 + 复杂场景测试模型
    // ================================================================

    /// <summary>
    /// 订单状态枚举
    /// </summary>
    public enum OrderStatus
    {
        /// <summary>
        /// 待处理
        /// </summary>
        Pending = 0,
        /// <summary>
        /// 已确认
        /// </summary>
        Confirmed = 1,
        /// <summary>
        /// 已完成
        /// </summary>
        Completed = 2,
        /// <summary>
        /// 已取消
        /// </summary>
        Cancelled = 3
    }

    /// <summary>
    /// 集成测试 — 含枚举字段的产品实体
    /// </summary>
    public class ProductEntity
    {
        /// <summary>
        /// 主键标识
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// 产品名称
        /// </summary>
        public string Name { get; set; } = "";
        /// <summary>
        /// 价格
        /// </summary>
        public decimal Price { get; set; }
        /// <summary>
        /// 库存状态（枚举）
        /// </summary>
        public OrderStatus Status { get; set; }
        /// <summary>
        /// 可选状态（可空枚举）
        /// </summary>
        public OrderStatus? OptionalStatus { get; set; }
    }

    /// <summary>
    /// 集成测试 — 含枚举字段的产品 DTO
    /// </summary>
    [MapFrom(typeof(ProductEntity))]
    public class ProductDto
    {
        /// <summary>
        /// 主键标识
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// 产品名称
        /// </summary>
        public string Name { get; set; } = "";
        /// <summary>
        /// 价格
        /// </summary>
        public decimal Price { get; set; }
        /// <summary>
        /// 库存状态（枚举）
        /// </summary>
        public OrderStatus Status { get; set; }
        /// <summary>
        /// 可选状态（可空枚举）
        /// </summary>
        public OrderStatus? OptionalStatus { get; set; }
    }

    /// <summary>
    /// 集成测试 — 含嵌套对象和集合的博客实体
    /// </summary>
    public class BlogEntity
    {
        /// <summary>
        /// 主键标识
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// 博客标题
        /// </summary>
        public string Title { get; set; } = "";
        /// <summary>
        /// 作者标识（外键）
        /// </summary>
        public int AuthorId { get; set; }
        /// <summary>
        /// 作者信息（嵌套对象）
        /// </summary>
        public AuthorEntity? Author { get; set; }
    }

    /// <summary>
    /// 集成测试 — 作者实体
    /// </summary>
    public class AuthorEntity
    {
        /// <summary>
        /// 主键标识
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// 作者姓名
        /// </summary>
        public string Name { get; set; } = "";
        /// <summary>
        /// 作者邮箱
        /// </summary>
        public string Email { get; set; } = "";
    }

    /// <summary>
    /// 集成测试 — 含嵌套作者信息的博客 DTO
    /// </summary>
    [MapFrom(typeof(BlogEntity))]
    public class BlogWithAuthorDto
    {
        /// <summary>
        /// 主键标识
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// 博客标题
        /// </summary>
        public string Title { get; set; } = "";

        /// <summary>
        /// 嵌套作者 DTO
        /// </summary>
        [MapFrom(typeof(AuthorEntity))]
        public class AuthorDto
        {
            /// <summary>
            /// 主键标识
            /// </summary>
            public int Id { get; set; }
            /// <summary>
            /// 作者姓名
            /// </summary>
            public string Name { get; set; } = "";
            /// <summary>
            /// 作者邮箱
            /// </summary>
            public string Email { get; set; } = "";
        }

        /// <summary>
        /// 嵌套作者信息
        /// </summary>
        public AuthorDto? Author { get; set; }
    }

    /// <summary>
    /// 集成测试 — 博客展平 DTO（嵌套对象展平为单级字段）
    /// </summary>
    [MapFrom(typeof(BlogEntity))]
    public class BlogFlattenedDto
    {
        /// <summary>
        /// 主键标识
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// 博客标题
        /// </summary>
        public string Title { get; set; } = "";
        /// <summary>
        /// 作者姓名 — 命名约定扁平化：属性名 AuthorName 自动拆解为 Author.Name
        /// <para>点路径记法 [MapProperty(typeof(BlogEntity), "Author.Name")] 在投影路径自初始提交即受支持
        /// （TryBuildDotPathExpression 按点号拆分，2026-08-15 端到端实证：EFCore_DotPath_Projection_EndToEnd_Works）；
        /// 源生成器侧不支持（FindProperty 精确匹配，NCMAP001），运行时内存映射不支持（静默跳过）</para>
        /// </summary>
        public string? AuthorName { get; set; }
        /// <summary>
        /// 作者邮箱 — 命名约定扁平化：属性名 AuthorEmail 自动拆解为 Author.Email
        /// </summary>
        public string? AuthorEmail { get; set; }
    }
}
