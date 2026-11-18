using Xunit;

namespace NCMapper.SourceGen.Tests.Models
{
    /// <summary>
    /// 源生成器测试 — 扁平实体
    /// </summary>
    public class SgFlatEntity
    {
        /// <summary>
        /// 唯一标识
        /// </summary>
        public int Id { get; set; } = 1;
        /// <summary>
        /// 名称
        /// </summary>
        public string Name { get; set; } = "Test";
        /// <summary>
        /// 年龄
        /// </summary>
        public int Age { get; set; } = 30;
        /// <summary>
        /// 薪资
        /// </summary>
        public decimal Salary { get; set; } = 5000m;
        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; } = new(2024, 1, 1);
        /// <summary>
        /// 是否激活
        /// </summary>
        public bool IsActive { get; set; } = true;
        /// <summary>
        /// 分数
        /// </summary>
        public double Score { get; set; } = 95.5;
        /// <summary>
        /// 用户 ID
        /// </summary>
        public long UserId { get; set; } = 123456;
        /// <summary>
        /// 全局唯一标识
        /// </summary>
        public Guid Guid { get; set; } = Guid.NewGuid();
    }

    /// <summary>
    /// 源生成器测试 — 扁平 DTO
    /// </summary>
    [NCMapper.Attributes.MapFrom(typeof(SgFlatEntity))]
    public class SgFlatDto
    {
        /// <summary>
        /// 唯一标识
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
        /// 薪资
        /// </summary>
        public decimal Salary { get; set; }
        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; }
        /// <summary>
        /// 是否激活
        /// </summary>
        public bool IsActive { get; set; }
        /// <summary>
        /// 分数
        /// </summary>
        public double Score { get; set; }
        /// <summary>
        /// 用户 ID
        /// </summary>
        public long UserId { get; set; }
        /// <summary>
        /// 全局唯一标识
        /// </summary>
        public Guid Guid { get; set; }
    }

    /// <summary>
    /// 源生成器测试 — 异名字段 DTO
    /// </summary>
    [NCMapper.Attributes.MapFrom(typeof(SgFlatEntity))]
    public class SgNameDiffDto
    {
        /// <summary>
        /// 唯一标识
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 全名（映射自 SgFlatEntity.Name）
        /// </summary>
        [NCMapper.Attributes.MapProperty(typeof(SgFlatEntity), nameof(SgFlatEntity.Name))]
        public string FullName { get; set; } = "";

        /// <summary>
        /// 用户年龄（映射自 SgFlatEntity.Age）
        /// </summary>
        [NCMapper.Attributes.MapProperty(typeof(SgFlatEntity), nameof(SgFlatEntity.Age))]
        public int UserAge { get; set; }

        /// <summary>
        /// 账户 ID（映射自 SgFlatEntity.Id）
        /// </summary>
        [NCMapper.Attributes.MapProperty(typeof(SgFlatEntity), nameof(SgFlatEntity.Id))]
        public long AccountId { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// 总金额（映射自 SgFlatEntity.Salary）
        /// </summary>
        [NCMapper.Attributes.MapProperty(typeof(SgFlatEntity), nameof(SgFlatEntity.Salary))]
        public decimal TotalAmount { get; set; }
    }

    /// <summary>
    /// 源生成器测试 — 嵌套内部实体
    /// </summary>
    public class SgInnerEntity
    {
        /// <summary>
        /// 城市
        /// </summary>
        public string City { get; set; } = "Shanghai";
        /// <summary>
        /// 街道
        /// </summary>
        public string Street { get; set; } = "Nanjing Road";
        /// <summary>
        /// 邮政编码
        /// </summary>
        public int ZipCode { get; set; } = 200001;
    }

    /// <summary>
    /// 源生成器测试 — 嵌套外层实体（含 Child 导航）
    /// </summary>
    public class SgNestedEntity
    {
        /// <summary>
        /// 唯一标识
        /// </summary>
        public int Id { get; set; } = 1;
        /// <summary>
        /// 名称
        /// </summary>
        public string Name { get; set; } = "Parent";
        /// <summary>
        /// 嵌套子对象
        /// </summary>
        public SgInnerEntity? Child { get; set; }
    }

    /// <summary>
    /// 源生成器测试 — 嵌套内部 DTO
    /// </summary>
    [NCMapper.Attributes.MapFrom(typeof(SgInnerEntity))]
    public class SgInnerDto
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
    /// 源生成器测试 — 嵌套外层 DTO
    /// </summary>
    [NCMapper.Attributes.MapFrom(typeof(SgNestedEntity))]
    public class SgNestedDto
    {
        /// <summary>
        /// 唯一标识
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// 名称
        /// </summary>
        public string Name { get; set; } = "";
        /// <summary>
        /// 嵌套子对象
        /// </summary>
        public SgInnerDto? Child { get; set; }
    }

    /// <summary>
    /// 源生成器测试 — MapIgnore 实体
    /// </summary>
    public class SgIgnoreEntity
    {
        /// <summary>
        /// 唯一标识
        /// </summary>
        public int Id { get; set; } = 1;
        /// <summary>
        /// 名称
        /// </summary>
        public string Name { get; set; } = "Test";
        /// <summary>
        /// 计算字段（不应被映射）
        /// </summary>
        public string Computed { get; set; } = "should not map";
    }

    /// <summary>
    /// 源生成器测试 — MapIgnore DTO
    /// </summary>
    [NCMapper.Attributes.MapFrom(typeof(SgIgnoreEntity))]
    public class SgIgnoreDto
    {
        /// <summary>
        /// 唯一标识
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// 名称
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// 计算字段（忽略映射）
        /// </summary>
        [NCMapper.Attributes.MapIgnore]
        public string? Computed { get; set; }
    }

    /// <summary>
    /// 源生成器测试 — MapIgnore Direction 实体
    /// </summary>
    public class SgDirectionEntity
    {
        /// <summary>
        /// 唯一标识
        /// </summary>
        public int Id { get; set; } = 1;
        /// <summary>
        /// 名称
        /// </summary>
        public string Name { get; set; } = "Test";
        /// <summary>
        /// 仅 DTO 方向可见的字段
        /// </summary>
        public string ToDtoOnly { get; set; } = "dto_skip";
        /// <summary>
        /// 仅 Entity 方向可见的字段
        /// </summary>
        public string ToEntityOnly { get; set; } = "entity_skip";
    }

    /// <summary>
    /// 源生成器测试 — MapIgnore Direction DTO
    /// </summary>
    [NCMapper.Attributes.MapFrom(typeof(SgDirectionEntity))]
    public class SgDirectionDto
    {
        /// <summary>
        /// 唯一标识
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// 名称
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// 仅 DTO 方向可见（ToDto 方向忽略）
        /// </summary>
        [NCMapper.Attributes.MapIgnore(NCMapper.Attributes.Direction.ToDto)]
        public string? ToDtoOnly { get; set; }

        /// <summary>
        /// 仅 Entity 方向可见（ToEntity 方向忽略）
        /// </summary>
        [NCMapper.Attributes.MapIgnore(NCMapper.Attributes.Direction.ToEntity)]
        public string? ToEntityOnly { get; set; }
    }

    /// <summary>
    /// 源生成器测试 — 订单实体（多源合成）
    /// </summary>
    public class SgOrderEntity
    {
        /// <summary>
        /// 订单 ID
        /// </summary>
        public int Id { get; set; } = 1001;
        /// <summary>
        /// 订单金额
        /// </summary>
        public decimal Amount { get; set; } = 299.99m;
    }

    /// <summary>
    /// 源生成器测试 — 客户实体（多源合成）
    /// </summary>
    public class SgCustomerEntity
    {
        /// <summary>
        /// 客户名称
        /// </summary>
        public string Name { get; set; } = "ZhangSan";
        /// <summary>
        /// 客户等级
        /// </summary>
        public int Level { get; set; } = 3;
    }

    /// <summary>
    /// 源生成器测试 — 多源合成 DTO
    /// </summary>
    [NCMapper.Attributes.MapFrom(typeof(SgOrderEntity))]
    public class SgMultiSourceDto
    {
        /// <summary>
        /// 订单 ID（映射自 SgOrderEntity.Id）
        /// </summary>
        [NCMapper.Attributes.MapProperty(typeof(SgOrderEntity), nameof(SgOrderEntity.Id))]
        public int OrderId { get; set; }

        /// <summary>
        /// 订单金额（映射自 SgOrderEntity.Amount）
        /// </summary>
        [NCMapper.Attributes.MapProperty(typeof(SgOrderEntity), nameof(SgOrderEntity.Amount))]
        public decimal OrderAmount { get; set; }

        /// <summary>
        /// 客户名称（映射自 SgCustomerEntity.Name）
        /// </summary>
        [NCMapper.Attributes.MapProperty(typeof(SgCustomerEntity), nameof(SgCustomerEntity.Name))]
        public string CustomerName { get; set; } = "";

        /// <summary>
        /// 客户等级（映射自 SgCustomerEntity.Level）
        /// </summary>
        [NCMapper.Attributes.MapProperty(typeof(SgCustomerEntity), nameof(SgCustomerEntity.Level))]
        public int CustomerLevel { get; set; }
    }

    /// <summary>
    /// 源生成器测试 — 集合项实体
    /// </summary>
    public class SgCollectionItemEntity
    {
        /// <summary>
        /// 唯一标识
        /// </summary>
        public int Id { get; set; } = 1;
        /// <summary>
        /// 名称
        /// </summary>
        public string Name { get; set; } = "Item";
    }

    /// <summary>
    /// 源生成器测试 — 集合项 DTO
    /// </summary>
    [NCMapper.Attributes.MapFrom(typeof(SgCollectionItemEntity))]
    public class SgCollectionItemDto
    {
        /// <summary>
        /// 唯一标识
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// 名称
        /// </summary>
        public string Name { get; set; } = "";
    }

    /// <summary>
    /// 源生成器测试 — 含集合属性的实体
    /// </summary>
    public class SgCollectionEntity
    {
        /// <summary>
        /// 唯一标识
        /// </summary>
        public int Id { get; set; } = 1;
        /// <summary>
        /// 名称
        /// </summary>
        public string Name { get; set; } = "Test";
        /// <summary>
        /// 集合项列表
        /// </summary>
        public List<SgCollectionItemEntity>? Items { get; set; }
    }

    /// <summary>
    /// 源生成器测试 — 含集合属性的 DTO
    /// </summary>
    [NCMapper.Attributes.MapFrom(typeof(SgCollectionEntity))]
    public class SgCollectionDto
    {
        /// <summary>
        /// 唯一标识
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// 名称
        /// </summary>
        public string Name { get; set; } = "";
        /// <summary>
        /// 集合项列表
        /// </summary>
        public List<SgCollectionItemDto>? Items { get; set; }
    }

    // ================================================================
    #region 自动扁平化测试模型
    // ================================================================

    /// <summary>
    /// 源生成器测试 — 嵌套实体（用于自动扁平化）
    /// </summary>
    public class SgFlatChildEntity
    {
        /// <summary>
        /// 名称
        /// </summary>
        public string Name { get; set; } = "Child";
        /// <summary>
        /// 层级
        /// </summary>
        public int Level { get; set; } = 5;
    }

    /// <summary>
    /// 源生成器测试 — 含嵌套的父实体
    /// </summary>
    public class SgFlatParentEntity
    {
        /// <summary>
        /// 唯一标识
        /// </summary>
        public int Id { get; set; } = 1;
        /// <summary>
        /// 嵌套子对象
        /// </summary>
        public SgFlatChildEntity? Child { get; set; }
    }

    /// <summary>
    /// 源生成器测试 — 扁平化 DTO（无 [MapProperty] 标注，靠自动拆解匹配）
    /// </summary>
    [NCMapper.Attributes.MapFrom(typeof(SgFlatParentEntity))]
    public class SgFlatParentDto
    {
        /// <summary>
        /// 唯一标识
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// 子对象名称（自动扁平化：ChildName ← Child.Name）
        /// </summary>
        public string ChildName { get; set; } = "";
    }

    #endregion

    // ================================================================
    #region 枚举映射测试模型
    // ================================================================

    /// <summary>
    /// 源生成器测试 — 枚举类型
    /// </summary>
    public enum SgStatus
    {
        /// <summary>
        /// 激活
        /// </summary>
        Active = 0,
        /// <summary>
        /// 停用
        /// </summary>
        Inactive = 1,
        /// <summary>
        /// 待定
        /// </summary>
        Pending = 2
    }

    /// <summary>
    /// 源生成器测试 — 含枚举的实体
    /// </summary>
    public class SgEnumEntity
    {
        /// <summary>
        /// 唯一标识
        /// </summary>
        public int Id { get; set; } = 1;
        /// <summary>
        /// 状态
        /// </summary>
        public SgStatus Status { get; set; } = SgStatus.Pending;
    }

    /// <summary>
    /// 源生成器测试 — 枚举→字符串 DTO
    /// </summary>
    [NCMapper.Attributes.MapFrom(typeof(SgEnumEntity))]
    public class SgEnumToStringDto
    {
        /// <summary>
        /// 唯一标识
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// 状态（枚举值映射为字符串）
        /// </summary>
        public string Status { get; set; } = "";
    }

    /// <summary>
    /// 源生成器测试 — 字符串→枚举 DTO
    /// </summary>
    [NCMapper.Attributes.MapFrom(typeof(SgEnumEntity))]
    public class SgStringToEnumDto
    {
        /// <summary>
        /// 唯一标识
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// 状态（字符串映射为枚举值）
        /// </summary>
        public SgStatus Status { get; set; }
    }

    #endregion

    // ================================================================
    #region record 构造器映射测试模型
    // ================================================================

    /// <summary>
    /// 源生成器测试 — record 类型实体
    /// </summary>
    /// <param name="Id">唯一标识</param>
    /// <param name="Name">名称</param>
    /// <param name="Age">年龄</param>
    public record SgRecordEntity(int Id, string Name, int Age);

    /// <summary>
    /// 源生成器测试 — record 类型 DTO
    /// </summary>
    /// <param name="Id">唯一标识</param>
    /// <param name="Name">名称</param>
    /// <param name="Age">年龄</param>
    [NCMapper.Attributes.MapFrom(typeof(SgRecordEntity))]
    public record SgRecordDto(int Id, string Name, int Age);

    /// <summary>
    /// 源生成器测试 — 含静态不可解析边（object 属性）的 record 实体
    /// <para>（D4）：分类 Unknown → record 构造器路径不可防御
    /// 不可解析边背后的环 → NCMAP012 回退运行时 DeepCopyRuntimeOnly。</para>
    /// </summary>
    /// <param name="Id">唯一标识</param>
    /// <param name="Payload">不可解析载荷（object 属性触发 Unknown 边）</param>
    // NCMAP012 为 D4 回退的预期诊断输出，抑制以保持"零新增警告"验收口径；
    // 诊断触发验证由生成器级用例 Record_Unknown_RuntimeFallback_D4_Ncmap012And016_AcyclicFalse 承担
#pragma warning disable NCMAP012
    public record SgRecordUnknownEntity(int Id, object? Payload);

    /// <summary>
    /// 源生成器测试 — 含静态不可解析边的 record DTO（D4 回退运行时 E2E 模型）
    /// </summary>
    /// <param name="Id">唯一标识</param>
    /// <param name="Payload">不可解析载荷</param>
    [NCMapper.Attributes.MapFrom(typeof(SgRecordUnknownEntity))]
    public record SgRecordUnknownDto(int Id, object? Payload);
#pragma warning restore NCMAP012

    #endregion

    // ================================================================
    #region D8 DAG 共享语义 E2E 模型
    // ================================================================

    /// <summary>
    /// 源生成器测试 — D8 DAG 共享语义商品节点
    /// <para>100 个订单行共享同一商品的纯 DAG（无环）→
    /// 编译期分类 Acyclic，层 1 走 IsAcyclic 免检测分支。D8 破坏性变更验证载体：
    /// 无环类型的 DAG 共享引用从"保持共享"变为"逐处独立复制"。</para>
    /// </summary>
    public class SgDagProduct
    {
        /// <summary>商品唯一标识</summary>
        public int Id { get; set; }

        /// <summary>商品名称</summary>
        public string Name { get; set; } = "";
    }

    /// <summary>
    /// 源生成器测试 — D8 DAG 共享语义订单行（多行共享同一 Product）
    /// </summary>
    public class SgDagOrderLine
    {
        /// <summary>订单行唯一标识</summary>
        public int Id { get; set; }

        /// <summary>商品引用（DAG 共享节点）</summary>
        public SgDagProduct? Product { get; set; }
    }

    /// <summary>
    /// 源生成器测试 — D8 DAG 共享语义订单（含订单行集合）
    /// </summary>
    public class SgDagOrder
    {
        /// <summary>订单唯一标识</summary>
        public int Id { get; set; }

        /// <summary>订单行集合</summary>
        public List<SgDagOrderLine> Lines { get; set; } = new();
    }

    /// <summary>
    /// 源生成器测试 — D8 DAG 共享语义订单 DTO（[MapFrom] 使实体链路参与源生成注册）
    /// </summary>
    [NCMapper.Attributes.MapFrom(typeof(SgDagOrder))]
    public class SgDagOrderDto
    {
        /// <summary>订单唯一标识</summary>
        public int Id { get; set; }
    }

    #endregion

    // ================================================================
    #region null 守卫测试模型
    // ================================================================

    /// <summary>
    /// 源生成器测试 — 嵌套 null 守卫实体
    /// </summary>
    public class SgNullGuardEntity
    {
        /// <summary>
        /// 唯一标识
        /// </summary>
        public int Id { get; set; } = 1;
        /// <summary>
        /// 嵌套子对象（可能为 null）
        /// </summary>
        public SgFlatChildEntity? Child { get; set; }
    }

    /// <summary>
    /// 源生成器测试 — 嵌套 null 守卫 DTO
    /// </summary>
    [NCMapper.Attributes.MapFrom(typeof(SgNullGuardEntity))]
    public class SgNullGuardDto
    {
        /// <summary>
        /// 唯一标识
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// 子对象名称（从 Child?.Name 映射）
        /// </summary>
        public string? ChildName { get; set; }
    }

    #endregion

    // ================================================================
    #region 接口类型属性测试模型
    // ================================================================

    /// <summary>
    /// 源生成器测试 — 含只读接口属性的实体
    /// </summary>
    public class SgInterfaceEntity
    {
        /// <summary>
        /// 唯一标识
        /// </summary>
        public int Id { get; set; } = 1;
        /// <summary>
        /// 集合项列表
        /// </summary>
        public List<SgCollectionItemEntity> Items { get; set; } = new() { new() { Id = 10, Name = "A" } };
    }

    /// <summary>
    /// 源生成器测试 — 含只读接口属性的 DTO
    /// </summary>
    [NCMapper.Attributes.MapFrom(typeof(SgInterfaceEntity))]
    public class SgInterfaceDto
    {
        /// <summary>
        /// 唯一标识
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// 只读集合（IReadOnlyList 接口类型）
        /// </summary>
        public IReadOnlyList<SgCollectionItemDto>? Items { get; set; }
    }

    #endregion

    // ================================================================
    #region 扁平化路径段数测试模型 — Level1 → Level2 → Level3 → Level4 → Level5
    // ================================================================

    /// <summary>
    /// 扁平化测试 — Level1（根实体）
    /// </summary>
    public class SgFlattenLevel1
    {
        /// <summary>
        /// 值
        /// </summary>
        public int Value { get; set; } = 1;
        /// <summary>
        /// 下一层级
        /// </summary>
        public SgFlattenLevel2? Level2 { get; set; }
    }

    /// <summary>
    /// 扁平化测试 — Level2
    /// </summary>
    public class SgFlattenLevel2
    {
        /// <summary>
        /// 值
        /// </summary>
        public int Value { get; set; } = 2;
        /// <summary>
        /// 下一层级
        /// </summary>
        public SgFlattenLevel3? Level3 { get; set; }
    }

    /// <summary>
    /// 扁平化测试 — Level3
    /// </summary>
    public class SgFlattenLevel3
    {
        /// <summary>
        /// 值
        /// </summary>
        public int Value { get; set; } = 3;
        /// <summary>
        /// 下一层级
        /// </summary>
        public SgFlattenLevel4? Level4 { get; set; }
    }

    /// <summary>
    /// 扁平化测试 — Level4
    /// </summary>
    public class SgFlattenLevel4
    {
        /// <summary>
        /// 值
        /// </summary>
        public int Value { get; set; } = 4;
        /// <summary>
        /// 下一层级
        /// </summary>
        public SgFlattenLevel5? Level5 { get; set; }
    }

    /// <summary>
    /// 扁平化测试 — Level5（叶子节点）
    /// </summary>
    public class SgFlattenLevel5
    {
        /// <summary>
        /// 值
        /// </summary>
        public int Value { get; set; } = 5;
    }

    /// <summary>
    /// 4 段路径扁平化 DTO — Level1.Level2.Level3.Level4.Value → Level2Level3Level4Value
    /// </summary>
    [NCMapper.Attributes.MapFrom(typeof(SgFlattenLevel1))]
    public class SgFlatten4SegmentsDto
    {
        /// <summary>
        /// 4 段路径自动扁平化（Level2, Level3, Level4, Value）
        /// </summary>
        public int Level2Level3Level4Value { get; set; }
    }

    /// <summary>
    /// 5 段路径扁平化 DTO — Level1.Level2.Level3.Level4.Level5.Value → Level2Level3Level4Level5Value
    /// </summary>
    [NCMapper.Attributes.MapFrom(typeof(SgFlattenLevel1))]
    public class SgFlatten5SegmentsDto
    {
        /// <summary>
        /// 5 段路径自动扁平化（Level2, Level3, Level4, Level5, Value）
        /// </summary>
        public int Level2Level3Level4Level5Value { get; set; }
    }

    /// <summary>
    /// 6 段路径扁平化 DTO — 超过 5 段，SG 不支持，回退到运行时路径
    /// </summary>
    [NCMapper.Attributes.MapFrom(typeof(SgFlattenLevel1))]
    public class SgFlatten6SegmentsDto
    {
        /// <summary>
        /// 6 段路径 — SG 不支持，属性保持默认值
        /// </summary>
        public int Level2Level3Level4Level5ValueExtra { get; set; } = -999;
    }

    #endregion

    // ================================================================
    #region Dictionary 属性映射测试模型
    // ================================================================

    /// <summary>
    /// 源生成器测试 — 含同类型字典属性的实体
    /// </summary>
    public class SgDictEntity
    {
        /// <summary>
        /// 唯一标识
        /// </summary>
        public int Id { get; set; } = 1;
        /// <summary>
        /// 标签计数字典
        /// </summary>
        public Dictionary<string, int>? TagCounts { get; set; }
    }

    /// <summary>
    /// 源生成器测试 — 含同类型字典属性的 DTO（KV 类型相同，逐对拷贝）
    /// </summary>
    [NCMapper.Attributes.MapFrom(typeof(SgDictEntity))]
    public class SgDictDto
    {
        /// <summary>
        /// 唯一标识
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// 标签计数字典
        /// </summary>
        public Dictionary<string, int>? TagCounts { get; set; }
    }

    /// <summary>
    /// 源生成器测试 — 含跨类型字典属性的实体（值为嵌套对象）
    /// </summary>
    public class SgDictCrossTypeEntity
    {
        /// <summary>
        /// 唯一标识
        /// </summary>
        public int Id { get; set; } = 1;
        /// <summary>
        /// 分类帖子字典（值为 SgCollectionItemEntity）
        /// </summary>
        public Dictionary<string, SgCollectionItemEntity>? PostsByCategory { get; set; }
    }

    /// <summary>
    /// 源生成器测试 — 含跨类型字典属性的 DTO（值 Post → PostDto 映射）
    /// </summary>
    [NCMapper.Attributes.MapFrom(typeof(SgDictCrossTypeEntity))]
    public class SgDictCrossTypeDto
    {
        /// <summary>
        /// 唯一标识
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// 分类帖子字典（值映射为 SgCollectionItemDto）
        /// </summary>
        public Dictionary<string, SgCollectionItemDto>? PostsByCategory { get; set; }
    }

    /// <summary>
    /// 源生成器测试 — 目标为 IDictionary 接口的字典属性
    /// </summary>
    [NCMapper.Attributes.MapFrom(typeof(SgDictEntity))]
    public class SgDictInterfaceDto
    {
        /// <summary>
        /// 唯一标识
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// 标签计数字典（IDictionary 接口类型）
        /// </summary>
        public System.Collections.Generic.IDictionary<string, int>? TagCounts { get; set; }
    }

    #endregion

    #region ConvertUsing 转换器运行时测试模型

    /// <summary>
    /// 源生成器测试 — ConvertUsing 转换器源实体
    /// </summary>
    public class SgConverterEntity
    {
        public int Id { get; set; }
        public System.DateTime CreateTime { get; set; }
        public int Code { get; set; }
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public decimal Price { get; set; }
        public int Quantity { get; set; }
    }

    /// <summary>
    /// DateTime → string (yyyy-MM-dd) 转换器（T1 单值模板）
    /// </summary>
    public static class SgDateTimeToStringConverter
    {
        public static string Convert(System.DateTime fieldValue)
            => fieldValue.ToString("yyyy-MM-dd");
    }

    /// <summary>
    /// int → string (D6 零填充) 转换器（T1 单值模板）
    /// </summary>
    public static class SgIntToPaddedStringConverter
    {
        public static string Convert(int fieldValue)
            => fieldValue.ToString("D6");
    }

    /// <summary>
    /// 多字段组合：FirstName + LastName → FullName 计算器（T2 多字段模板 M(TSource)）
    /// </summary>
    public static class SgFullNameCalculator
    {
        public static string Convert(SgConverterEntity source)
            => $"{source.FirstName} {source.LastName}";
    }

    /// <summary>
    /// 多字段计算：Price × Quantity → TotalPrice 计算器（T2 多字段模板 M(TSource)）
    /// </summary>
    public static class SgTotalPriceCalculator
    {
        public static decimal Convert(SgConverterEntity source)
            => source.Price * source.Quantity;
    }

    /// <summary>
    /// 源生成器测试 — ConvertUsing 转换器 DTO
    /// </summary>
    [NCMapper.Attributes.MapFrom(typeof(SgConverterEntity))]
    public class SgConverterDto
    {
        /// <summary>
        /// 唯一标识（普通映射，无转换器）
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// DateTime → string 格式化（ConvertUsing T1 单值转换）
        /// </summary>
        [NCMapper.Attributes.Convert(typeof(SgDateTimeToStringConverter), nameof(SgDateTimeToStringConverter.Convert))]
        public string CreateTime { get; set; } = "";

        /// <summary>
        /// int → string 零填充（ConvertUsing T1 单值转换）
        /// </summary>
        [NCMapper.Attributes.Convert(typeof(SgIntToPaddedStringConverter), nameof(SgIntToPaddedStringConverter.Convert))]
        public string Code { get; set; } = "";

        /// <summary>
        /// FirstName + LastName → FullName（ConvertUsing T2 多字段组合）
        /// </summary>
        [NCMapper.Attributes.Convert(typeof(SgFullNameCalculator), nameof(SgFullNameCalculator.Convert))]
        [NCMapper.Attributes.MapProperty(typeof(SgConverterEntity), nameof(SgConverterEntity.FirstName))]
        public string FullName { get; set; } = "";

        /// <summary>
        /// Price × Quantity → TotalPrice（ConvertUsing T2 多字段计算）
        /// </summary>
        [NCMapper.Attributes.Convert(typeof(SgTotalPriceCalculator), nameof(SgTotalPriceCalculator.Convert))]
        [NCMapper.Attributes.MapProperty(typeof(SgConverterEntity), nameof(SgConverterEntity.Price))]
        public decimal TotalPrice { get; set; }
    }

    #endregion

    // ================================================================
    // NRE 回归测试模型 — 覆盖 [Convert] 无匹配源属性场景下
    // 源生成器 4 处 NRE 的回归测试（2026-07-18 修复）
    //
    // 背景：
    //   [Convert] 无匹配源属性时 MappingExtractor 将 SourceProperty 设为 null!，
    //   下游 4 处代码访问 pm.SourceProperty.Type / .SetMethod 抛 NullReferenceException，
    //   导致整个源生成器崩溃（CS8785），所有 [MapFrom] DTO 失去源生成器代码生成能力。
    //
    // 4 处 NRE 位置：
    //   1. GenerateSinglePropertyAssignment（CodeGenerator.Mapping.cs）— 正向 Map
    //   2. TryBuildSimpleAssignments（CodeGenerator.Mapping.cs）— MapList 内联元素
    //   3. CollectProjectionBindings（CodeGenerator.Projection.cs）— BuildProjection
    //   4. CreateReverseMapping（MappingExtractor.cs）— 反向映射
    // ================================================================

    /// <summary>
    /// NRE 回归测试源实体 — 含嵌套对象 Address，用于扁平化 + ConvertUsing 共存场景
    /// </summary>
    public class SgNreUserEntity
    {
        /// <summary>
        /// 用户 ID
        /// </summary>
        public int Id { get; set; } = 1;

        /// <summary>
        /// 用户名
        /// </summary>
        public string Name { get; set; } = "TestUser";

        /// <summary>
        /// 嵌套地址对象 — 用于扁平化属性测试
        /// </summary>
        public SgNreAddress? Address { get; set; } = new();
    }

    /// <summary>
    /// NRE 回归测试嵌套地址对象
    /// </summary>
    public class SgNreAddress
    {
        /// <summary>
        /// 城市
        /// </summary>
        public string City { get; set; } = "Shanghai";

        /// <summary>
        /// 街道
        /// </summary>
        public string Street { get; set; } = "Nanjing Road";
    }

    /// <summary>
    /// 无匹配源属性转换器 — 以源对象为入参（T2 多字段模板 M(TSource)）
    /// <para>模拟 UserDetailDto.FullNameConverter 场景：DTO 属性名在源类型中无同名属性</para>
    /// </summary>
    public static class SgNreComputedConverter
    {
        /// <summary>
        /// 通过 source.Name 组合生成计算字段值
        /// </summary>
        /// <param name="source">源实体对象</param>
        /// <returns>组合后的计算字段值</returns>
        public static string Convert(SgNreUserEntity source)
            => $"User-{source.Name}";
    }

    /// <summary>
    /// NRE 回归测试 DTO — 同时含扁平化属性 + ConvertUsing 无源属性
    /// <para>复现 UserDetailDto 配置：AddressCity（扁平化）+ ComputedField（无源属性 ConvertUsing）</para>
    /// <para>此组合在 2026-07-18 之前会触发 GenerateSinglePropertyAssignment NRE</para>
    /// </summary>
    [NCMapper.Attributes.MapFrom(typeof(SgNreUserEntity))]
    public class SgNreFlattenAndConverterDto
    {
        /// <summary>
        /// 用户 ID — 同名映射
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 用户名 — 同名映射
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// 城市 — PascalCase 扁平化：自动匹配 SgNreUserEntity.Address.City
        /// <para>源生成器编译时解析为 src.Address?.City</para>
        /// </summary>
        public string AddressCity { get; set; } = "";

        /// <summary>
        /// 街道 — PascalCase 扁平化：自动匹配 SgNreUserEntity.Address.Street
        /// <para>源生成器编译时解析为 src.Address?.Street</para>
        /// </summary>
        public string AddressStreet { get; set; } = "";

        /// <summary>
        /// 计算字段 — [Convert] 无匹配源属性场景
        /// <para>SgNreUserEntity 中无 ComputedField 同名属性，SourceProperty 被设为 null!</para>
        /// <para>转换器以源对象为入参（T2 模板）读取 Name 字段生成值</para>
        /// </summary>
        [NCMapper.Attributes.Convert(typeof(SgNreComputedConverter), nameof(SgNreComputedConverter.Convert))]
        public string ComputedField { get; set; } = "";
    }

    // ================================================================
    // CS0102 回归测试模型 — 转换器字段命名冲突
    //
    // 背景：
    //   GenerateConverterFields 用 __conv_{PropertyName} 作为字段名，
    //   不同 DTO 有同名属性 + 不同转换器时字段名冲突（CS0102）。
    //
    // 修复：
    //   字段名格式改为 __conv_{TargetClassName}_{PropertyName}，加 DTO 类名前缀消除冲突
    // ================================================================

    /// <summary>
    /// CS0102 回归测试源实体
    /// </summary>
    public class SgConflictSourceEntity
    {
        /// <summary>
        /// ID
        /// </summary>
        public int Id { get; set; } = 1;

        /// <summary>
        /// 名称
        /// </summary>
        public string Name { get; set; } = "Test";
    }

    /// <summary>
    /// 转换器 A — 用于 DtoA.ComputedField（T2 多字段模板 M(TSource)）
    /// </summary>
    public static class SgComputedConverterA
    {
        /// <summary>
        /// 生成 "A-{Name}"
        /// </summary>
        public static string Convert(SgConflictSourceEntity source)
            => $"A-{source.Name}";
    }

    /// <summary>
    /// 转换器 B — 用于 DtoB.ComputedField（与 A 不同类型，但属性名相同；T2 多字段模板）
    /// </summary>
    public static class SgComputedConverterB
    {
        /// <summary>
        /// 生成 "B-{Name}"
        /// </summary>
        public static string Convert(SgConflictSourceEntity source)
            => $"B-{source.Name}";
    }

    /// <summary>
    /// CS0102 回归测试 DTO A — 含 ComputedField 属性 + SgComputedConverterA
    /// <para>与 DtoB 有同名属性 ComputedField，但使用不同转换器类型</para>
    /// <para>修复前：__conv_ComputedField 字段名冲突（CS0102）</para>
    /// <para>修复后：__conv_SgConflictDtoA_ComputedField（加类名前缀）</para>
    /// </summary>
    [NCMapper.Attributes.MapFrom(typeof(SgConflictSourceEntity))]
    public class SgConflictDtoA
    {
        /// <summary>
        /// ID
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 计算字段 — 使用转换器 A
        /// </summary>
        [NCMapper.Attributes.Convert(typeof(SgComputedConverterA), nameof(SgComputedConverterA.Convert))]
        public string ComputedField { get; set; } = "";
    }

    /// <summary>
    /// CS0102 回归测试 DTO B — 含 ComputedField 属性 + SgComputedConverterB
    /// <para>与 DtoA 有同名属性 ComputedField，但使用不同转换器类型</para>
    /// <para>修复前：__conv_ComputedField 字段名冲突（CS0102）</para>
    /// <para>修复后：__conv_SgConflictDtoB_ComputedField（加类名前缀）</para>
    /// </summary>
    [NCMapper.Attributes.MapFrom(typeof(SgConflictSourceEntity))]
    public class SgConflictDtoB
    {
        /// <summary>
        /// ID
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 计算字段 — 使用转换器 B
        /// </summary>
        [NCMapper.Attributes.Convert(typeof(SgComputedConverterB), nameof(SgComputedConverterB.Convert))]
        public string ComputedField { get; set; } = "";
    }

    // ================================================================
    // CS8628 回归测试模型 — 可空集合属性触发 new T?() 非法语法
    //
    // 背景：
    //   GenerateDeepCopyCollectionProperty / GetCollectionCreationExpression 用
    //   propType.ToDisplayString() 拼接 new 表达式。当属性为可空引用类型集合
    //   （如 Queue<int>?）时，ToDisplayString() 返回 "Queue<int>?"，
    //   生成 "new Queue<int>?()" —— C# 不允许 new 表达式后跟可空注解 ?（CS8628）。
    //
    // 受影响 4 处 new：
    //   1. Queue 分支 new {fullTypeStr}()
    //   2. Stack 分支 new {fullTypeStr}()
    //   3. SortedSet 分支 new {fullTypeStr}()
    //   4. GetCollectionCreationExpression new {fullTypeStr}({listVarName})
    //
    // 修复：
    //   用 WithNullableAnnotation(NotAnnotated) 去掉外层可空注解再 ToDisplayString()，
    //   不递归改泛型参数注解（Queue<string?>? → Queue<string?>，元素注解保留）。
    // ================================================================

    /// <summary>
    /// CS8628 回归测试实体 — 含多种可空集合属性，覆盖 4 个 new 分支
    /// <para>所有 [MapFrom] 的 Source/Target 类型都会自动生成 DeepCopy 方法，
    /// 因此这些可空集合属性会触发 GenerateDeepCopyCollectionProperty</para>
    /// </summary>
    public class SgNullableCollectionEntity
    {
        /// <summary>
        /// 唯一标识
        /// </summary>
        public int Id { get; set; } = 1;

        /// <summary>
        /// 可空整数队列 — 触发 Queue 分支 new {fullTypeStr}()
        /// </summary>
        public Queue<int>? TaskQueue { get; set; }

        /// <summary>
        /// 可空整数栈 — 触发 Stack 分支 new {fullTypeStr}()
        /// </summary>
        public Stack<int>? HistoryStack { get; set; }

        /// <summary>
        /// 可空有序集合 — 触发 SortedSet 分支 new {fullTypeStr}()
        /// </summary>
        public SortedSet<int>? SortedTags { get; set; }

        /// <summary>
        /// 可空链表 — 触发 GetCollectionCreationExpression 的 new {fullTypeStr}(list)
        /// </summary>
        public LinkedList<int>? LinkedItems { get; set; }
    }

    /// <summary>
    /// CS8628 回归测试 DTO — [MapFrom] 标注使源类型进入 DeepCopy 生成
    /// </summary>
    [NCMapper.Attributes.MapFrom(typeof(SgNullableCollectionEntity))]
    public class SgNullableCollectionDto
    {
        /// <summary>
        /// 唯一标识
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 可空整数队列（与源同类型，直接赋值）
        /// </summary>
        public Queue<int>? TaskQueue { get; set; }

        /// <summary>
        /// 可空整数栈
        /// </summary>
        public Stack<int>? HistoryStack { get; set; }

        /// <summary>
        /// 可空有序集合
        /// </summary>
        public SortedSet<int>? SortedTags { get; set; }

        /// <summary>
        /// 可空链表
        /// </summary>
        public LinkedList<int>? LinkedItems { get; set; }
    }

    // ================================================================
    // T1 巩固测试模型 — 源生成路径下的循环引用检测
    // 标注 [MapFrom] 使源生成器为 Source/Target 类型生成并注册 DeepCopy 委托，
    // 调用 Mapper.DeepCopy 时进入层 1（源生成路径），直接触达 T1 修复的
    // s_deepCopyInProgress / s_deepCopyDepth 循环引用检测逻辑。
    // ================================================================

    /// <summary>
    /// 自引用实体 — Self 属性指向自身类型，可形成 A.Self = A 的循环
    /// </summary>
    public class SgSelfRefEntity
    {
        /// <summary>节点值</summary>
        public int Value { get; set; }

        /// <summary>自引用（可指向自身形成环）</summary>
        public SgSelfRefEntity? Self { get; set; }
    }

    /// <summary>
    /// 自引用 DTO — 标 [MapFrom] 触发源生成器为 SgSelfRefEntity/SgSelfRefDto 生成 DeepCopy 委托
    /// </summary>
    [NCMapper.Attributes.MapFrom(typeof(SgSelfRefEntity))]
    public class SgSelfRefDto
    {
        /// <summary>节点值</summary>
        public int Value { get; set; }

        /// <summary>自引用</summary>
        public SgSelfRefDto? Self { get; set; }
    }

    /// <summary>
    /// A↔B 双向引用实体 A — A.B 指向 B，B.A 反向指向 A，形成循环
    /// </summary>
    public class SgCircularAEntity
    {
        /// <summary>节点 A 唯一标识</summary>
        public int Id { get; set; }

        /// <summary>引用 B，形成 A→B 的循环链</summary>
        public SgCircularBEntity? B { get; set; }
    }

    /// <summary>
    /// A↔B 双向引用实体 B — B.A 反向指向 A
    /// </summary>
    public class SgCircularBEntity
    {
        /// <summary>节点 B 唯一标识</summary>
        public int Id { get; set; }

        /// <summary>反向引用 A，形成 B→A 的循环链</summary>
        public SgCircularAEntity? A { get; set; }
    }

    /// <summary>
    /// A↔B 双向引用 DTO — 标 [MapFrom(SgCircularAEntity)] 触发源生成器为 SgCircularAEntity/SgCircularADto 生成 DeepCopy 委托
    /// </summary>
    [NCMapper.Attributes.MapFrom(typeof(SgCircularAEntity))]
    public class SgCircularADto
    {
        /// <summary>节点 A 唯一标识</summary>
        public int Id { get; set; }

        /// <summary>引用 B</summary>
        public SgCircularBDto? B { get; set; }
    }

    /// <summary>
    /// A↔B 双向引用 DTO — 标 [MapFrom(SgCircularBEntity)] 触发源生成器为 SgCircularBEntity/SgCircularBDto 生成 DeepCopy 委托
    /// </summary>
    [NCMapper.Attributes.MapFrom(typeof(SgCircularBEntity))]
    public class SgCircularBDto
    {
        /// <summary>节点 B 唯一标识</summary>
        public int Id { get; set; }

        /// <summary>反向引用 A</summary>
        public SgCircularADto? A { get; set; }
    }

    // ================================================================
    // 集合导航投影测试模型
    // ================================================================

    /// <summary>集合投影 — 订单实体</summary>
    public class SgCollOrder
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>订单号</summary>
        public string? OrderNo { get; set; }
        /// <summary>明细集合</summary>
        public List<SgCollItem> Items { get; set; } = new();
    }

    /// <summary>集合投影 — 明细实体</summary>
    public class SgCollItem
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>数量</summary>
        public int Quantity { get; set; }
        /// <summary>产品导航</summary>
        public SgCollProduct? Product { get; set; }
        /// <summary>标签集合（嵌套集合）</summary>
        public List<SgCollTag> Tags { get; set; } = new();
    }

    /// <summary>集合投影 — 产品实体</summary>
    public class SgCollProduct
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>SKU</summary>
        public string? Sku { get; set; }
    }

    /// <summary>集合投影 — 标签实体</summary>
    public class SgCollTag
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>标签名</summary>
        public string? Label { get; set; }
    }

    /// <summary>集合投影 — 标签 DTO</summary>
    [NCMapper.Attributes.MapFrom(typeof(SgCollTag))]
    public class SgCollTagDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>标签名</summary>
        public string? Label { get; set; }
    }

    /// <summary>集合投影 — 明细 DTO（含跨表 + 嵌套集合）</summary>
    [NCMapper.Attributes.MapFrom(typeof(SgCollItem))]
    public class SgCollItemDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>数量</summary>
        public int Quantity { get; set; }
        /// <summary>跨表字段 — 产品 SKU</summary>
        [NCMapper.Attributes.MapProperty(typeof(SgCollProduct), nameof(SgCollProduct.Sku))]
        public string? ProductSku { get; set; }
        /// <summary>嵌套集合 — 标签</summary>
        public List<SgCollTagDto>? Tags { get; set; }
    }

    /// <summary>集合投影 — 订单 DTO</summary>
    [NCMapper.Attributes.MapFrom(typeof(SgCollOrder))]
    public class SgCollOrderDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>订单号</summary>
        public string? OrderNo { get; set; }
        /// <summary>集合导航投影</summary>
        public List<SgCollItemDto>? Items { get; set; }
    }

    /// <summary>非集合泛型实体 — 验证 Lazy&lt;T&gt; 不进入集合分支</summary>
    public class SgLazyEntity
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>延迟加载属性</summary>
        public Lazy<SgCollProduct>? LazyProduct { get; set; }
    }

    /// <summary>非集合泛型 DTO</summary>
    [NCMapper.Attributes.MapFrom(typeof(SgLazyEntity))]
    public class SgLazyDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>延迟加载属性</summary>
        public Lazy<SgCollProduct>? LazyProduct { get; set; }
    }

    // ================================================================
    // 别名枚举回归模型（H-05）
    // ================================================================

    /// <summary>别名枚举（数值重复成员）— 双引擎退化回归</summary>
    public enum SgAliasedEnum
    {
        A = 1,
        B = 1
    }

    public class SgAliasedEnumEntity
    {
        public int Id { get; set; } = 1;
        public SgAliasedEnum Status { get; set; } = SgAliasedEnum.A;
    }

    [NCMapper.Attributes.MapFrom(typeof(SgAliasedEnumEntity))]
    public class SgAliasedEnumToStringDto
    {
        public int Id { get; set; }
        public string Status { get; set; } = "";
    }
}
