using FreeSql;
using NCMapper;
using NCMapper.Attributes;
using FreeSql.DataAnnotations;

namespace NCMapper.IntegrationTests
{
    // ================================================================
    // 测试基础设施 — 实体定义（Fs 前缀，与现有实体隔离）
    // 参照 NestedCrossTableProjectionTests.cs:20-80 实体定义模式
    // ================================================================

    #region 实体

    /// <summary>
    /// 主表实体 — Topic，跨全部用例场景
    /// </summary>
    public class FsTopic
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>标题</summary>
        public string Title { get; set; } = "";
        /// <summary>内容（可为 null）</summary>
        public string? Content { get; set; }
        /// <summary>点击数</summary>
        public int Clicks { get; set; }
        /// <summary>创建时间</summary>
        public DateTime CreateTime { get; set; }
        /// <summary>所属分类外键</summary>
        public int CategoryId { get; set; }
        /// <summary>是否已删除</summary>
        public bool IsDeleted { get; set; }

        /// <summary>
        /// 分类导航属性（多对一）
        /// </summary>
        [Navigate(nameof(CategoryId))]
        public FsCategory? Category { get; set; }

        /// <summary>
        /// 评论集合导航属性（一对多）
        /// </summary>
        [Navigate(nameof(FsComment.TopicId))]
        public List<FsComment> Comments { get; set; } = new();

        /// <summary>
        /// 无 [Navigate] 属性的引用类型属性 — T1-08 catch 兜底测试专用
        /// <para>FreeSql 不认识此属性为导航，IncludeByPropertyName 会失败</para>
        /// </summary>
        public FsOtherEntity? Nav { get; set; }
    }

    /// <summary>
    /// 分类实体 — 双表/三表 Compose 使用
    /// </summary>
    public class FsCategory
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>分类名称</summary>
        public string Name { get; set; } = "";
        /// <summary>分类类型外键（可为 null）</summary>
        public int? ParentId { get; set; }

        /// <summary>
        /// 分类类型导航属性（多对一）
        /// </summary>
        [Navigate(nameof(ParentId))]
        public FsCategoryType? CategoryType { get; set; }
    }

    /// <summary>
    /// 分类类型实体 — 三表 Compose 最深一层
    /// </summary>
    public class FsCategoryType
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>类型名称</summary>
        public string TypeName { get; set; } = "";
    }

    /// <summary>
    /// 评论实体 — 集合导航 / WhereCascade 场景
    /// </summary>
    public class FsComment
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>所属 Topic 外键</summary>
        public int TopicId { get; set; }
        /// <summary>评论内容</summary>
        public string Body { get; set; } = "";
        /// <summary>是否已删除</summary>
        public bool IsDeleted { get; set; }
        /// <summary>是否已审核</summary>
        public bool IsApproved { get; set; }
        /// <summary>创建时间</summary>
        public DateTime CreateTime { get; set; }
    }

    /// <summary>
    /// 无 FreeSql 关系特性的实体 — T1-08 catch 兜底测试专用
    /// <para>FsTopic.Nav 为此类型，但没有 [Navigate] 属性</para>
    /// </summary>
    public class FsOtherEntity
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>值（可为 null）</summary>
        public string? Value { get; set; }
    }

    #endregion

    // ================================================================
    // DTO 定义
    // ================================================================

    #region DTO — 三表字段（T1-01/03）

    /// <summary>
    /// 三表 DTO — Topic + Category + CategoryType 字段，无 ConvertUsing，走正常投影路径
    /// </summary>
    [MapFrom(typeof(FsTopic))]
    public class FsTopicFullDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>标题</summary>
        public string? Title { get; set; }

        /// <summary>
        /// 分类名称 — 跨表映射自 FsCategory.Name（经 Topic.Category 导航）
        /// </summary>
        [MapProperty(typeof(FsCategory), nameof(FsCategory.Name))]
        public string? CategoryName { get; set; }

        /// <summary>
        /// 分类类型名称 — 跨表映射自 FsCategoryType.TypeName（经 Topic.Category.CategoryType 导航）
        /// </summary>
        [MapProperty(typeof(FsCategoryType), nameof(FsCategoryType.TypeName))]
        public string? CategoryTypeName { get; set; }
    }

    /// <summary>
    /// 三表 DTO — 含 ConvertUsing 触发降级（T1-02/03/06）
    /// </summary>
    [MapFrom(typeof(FsTopic))]
    public class FsTopicFullConvDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>标题</summary>
        public string? Title { get; set; }

        /// <summary>
        /// 内容 — 标注 ConvertUsing 触发 NeedsTwoStepMapping 降级
        /// </summary>
        [Convert(typeof(NoOpTopicConverter), nameof(NoOpTopicConverter.Convert))]
        public string? Content { get; set; }

        /// <summary>分类名称 — 跨表映射自 FsCategory.Name</summary>
        [MapProperty(typeof(FsCategory), nameof(FsCategory.Name))]
        public string? CategoryName { get; set; }

        /// <summary>分类类型名称 — 跨表映射自 FsCategoryType.TypeName</summary>
        [MapProperty(typeof(FsCategoryType), nameof(FsCategoryType.TypeName))]
        public string? CategoryTypeName { get; set; }
    }

    #endregion

    #region DTO — 双表字段（T1-04）

    /// <summary>
    /// 双表 DTO — Topic + Category 字段，无 ConvertUsing，走正常 Compose 投影
    /// </summary>
    [MapFrom(typeof(FsTopic))]
    public class FsTopicCategoryDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>标题</summary>
        public string? Title { get; set; }

        /// <summary>分类名称 — 跨表映射自 FsCategory.Name</summary>
        [MapProperty(typeof(FsCategory), nameof(FsCategory.Name))]
        public string? CategoryName { get; set; }
    }

    #endregion

    #region DTO — 单表 + ConvertUsing（T1-05/07b）

    /// <summary>
    /// 单表 DTO — 含 ConvertUsing（降级触发器）+ 嵌套 Category
    /// <para>T1-05：异步降级路径（ConvertUsing 触发 NeedsTwoStepMapping）</para>
    /// <para>T1-07b：ConvertUsing 触发自动 Include（嵌套 Category 自动加载）</para>
    /// </summary>
    [MapFrom(typeof(FsTopic))]
    public class FsTopicDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>标题</summary>
        public string? Title { get; set; }
        /// <summary>点击数</summary>
        public int Clicks { get; set; }

        /// <summary>内容 — ConvertUsing 触发降级</summary>
        [Convert(typeof(NoOpTopicConverter), nameof(NoOpTopicConverter.Convert))]
        public string? Content { get; set; }

        /// <summary>
        /// 嵌套分类 DTO — T1-07b 自动 Include 验证
        /// </summary>
        public FsCategoryDto? Category { get; set; }
    }

    /// <summary>
    /// 单表 DTO — 含 ConvertUsing + MapProperty(FsOtherEntity)，不含嵌套 Category
    /// <para>T1-08：catch 兜底验证（MapProperty(FsOtherEntity) 的 IncludeByPropertyName 失败）</para>
    /// <para>与 FsTopicDto 分离以避免 FsOtherEntity 导航失败影响 Category 加载断言</para>
    /// </summary>
    [MapFrom(typeof(FsTopic))]
    public class FsTopicWithOtherDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>标题</summary>
        public string? Title { get; set; }

        /// <summary>内容 — ConvertUsing 触发降级</summary>
        [Convert(typeof(NoOpTopicConverter), nameof(NoOpTopicConverter.Convert))]
        public string? Content { get; set; }

        /// <summary>
        /// 跨表到 FsOtherEntity.Value — T1-08 catch 兜底验证
        /// <para>FsTopic.Nav 无 [Navigate]，IncludeByPropertyName("Nav") 会失败</para>
        /// </summary>
        [MapProperty(typeof(FsOtherEntity), nameof(FsOtherEntity.Value))]
        public string? OtherValue { get; set; }
    }

    #endregion

    #region DTO — 嵌套降级（T1-07a）

    /// <summary>
    /// 主 DTO 无 ConvertUsing，但嵌套 DTO 有 ConvertUsing — 触发 NeedsNestedDegradation
    /// </summary>
    [MapFrom(typeof(FsTopic))]
    public class FsTopicNestedDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>标题</summary>
        public string? Title { get; set; }

        /// <summary>
        /// 嵌套分类 DTO（含 ConvertUsing）— 触发 NeedsNestedDegradation + 自动 Include
        /// </summary>
        public FsCategoryConvDto? Category { get; set; }
    }

    #endregion

    #region DTO — 分类（嵌套用）

    /// <summary>
    /// 分类 DTO — 无 ConvertUsing，纯嵌套用
    /// </summary>
    [MapFrom(typeof(FsCategory))]
    public class FsCategoryDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>名称</summary>
        public string? Name { get; set; }
    }

    /// <summary>
    /// 分类 DTO — 含 ConvertUsing，用于触发 NeedsNestedDegradation（T1-07a）
    /// </summary>
    [MapFrom(typeof(FsCategory))]
    public class FsCategoryConvDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }

        /// <summary>名称 — ConvertUsing 触发嵌套降级</summary>
        [Convert(typeof(NoOpCategoryConverter), nameof(NoOpCategoryConverter.Convert))]
        public string? Name { get; set; }
    }

    #endregion

    #region DTO — 集合导航（T1-07a/07b/08, T2-02/03/04/13）

    /// <summary>
    /// Topic DTO — 含 Comments 集合导航
    /// <para>含 ConvertUsing 强制走降级路径，确保 IncludeMany/WhereCascade 配置被尊重</para>
    /// </summary>
    [MapFrom(typeof(FsTopic))]
    public class FsTopicWithCommentsDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>标题</summary>
        public string? Title { get; set; }
        /// <summary>是否已删除</summary>
        public bool IsDeleted { get; set; }

        /// <summary>
        /// 评论集合 — 集合导航投影
        /// </summary>
        public List<FsCommentDto>? Comments { get; set; }

        /// <summary>
        /// 内容 — ConvertUsing 强制触发降级路径
        /// </summary>
        [Convert(typeof(NoOpTopicConverter), nameof(NoOpTopicConverter.Convert))]
        public string? Content { get; set; }
    }

    /// <summary>
    /// 评论 DTO
    /// </summary>
    [MapFrom(typeof(FsComment))]
    public class FsCommentDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>内容</summary>
        public string? Body { get; set; }
        /// <summary>是否已删除</summary>
        public bool IsDeleted { get; set; }
        /// <summary>是否已审核</summary>
        public bool IsApproved { get; set; }
    }

    #endregion

    #region DTO — 聚合（T2-01）

    /// <summary>
    /// 聚合 DTO — GroupBy 场景（无 [MapFrom]，手动构造投影）
    /// </summary>
    public class FsCategoryCountDto
    {
        /// <summary>分类 ID</summary>
        public int CategoryId { get; set; }
        /// <summary>数量</summary>
        public int Count { get; set; }
    }

    #endregion

    // ================================================================
    // 自定义 Converter（用于触发降级）
    // ================================================================

    #region Converter

    /// <summary>
    /// NoOp Converter（静态方法引用形态，T1 模板 M(TField)）— 用于 [Convert] 触发 NeedsTwoStepMapping 降级
    /// <para>转换行为：空值返回空字符串，非空原样返回（加前缀标记以便断言区分）</para>
    /// </summary>
    public static class NoOpTopicConverter
    {
        /// <summary>
        /// 转换方法 — 非空值加前缀标记
        /// </summary>
        /// <param name="fieldValue">源字段值</param>
        /// <returns>转换后的字符串</returns>
        public static string Convert(string? fieldValue)
            => fieldValue == null ? "" : "[Conv]" + fieldValue;
    }

    /// <summary>
    /// NoOp Converter（静态方法引用形态，T1 模板 M(TField)）— 用于嵌套 DTO [Convert] 触发 NeedsNestedDegradation（T1-07a）
    /// </summary>
    public static class NoOpCategoryConverter
    {
        /// <summary>
        /// 转换方法 — 非空值加前缀标记
        /// </summary>
        /// <param name="fieldValue">源字段值</param>
        /// <returns>转换后的字符串</returns>
        public static string Convert(string? fieldValue)
            => fieldValue == null ? "" : "[CatConv]" + fieldValue;
    }

    #endregion

    #region Wiki 转测试实体/DTO（投影语义）

    /// <summary>
    /// Wiki 演示用文章实体 — 自包含，避免与现有 Fs* 实体命名冲突
    /// </summary>
    [Table(Name = "Wiki_W1_Article")]
    public class WArticle
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>标题</summary>
        public string Title { get; set; } = "";
        /// <summary>点击数</summary>
        public int Clicks { get; set; }
        /// <summary>创建时间</summary>
        public DateTime CreateTime { get; set; }
        /// <summary>分类外键</summary>
        public int CategoryId { get; set; }

        /// <summary>分类导航属性</summary>
        [Navigate(nameof(CategoryId))]
        public WCategory? Category { get; set; }
    }

    /// <summary>
    /// Wiki 演示用分类实体
    /// </summary>
    [Table(Name = "Wiki_W1_Category")]
    public class WCategory
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>名称</summary>
        public string Name { get; set; } = "";
    }

    /// <summary>
    /// Wiki 演示用订单实体
    /// </summary>
    [Table(Name = "Wiki_W2_Order")]
    public class WOrder
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>订单号</summary>
        public string OrderNo { get; set; } = "";
        /// <summary>客户外键</summary>
        public int CustomerId { get; set; }
        /// <summary>金额</summary>
        public decimal Amount { get; set; }
    }

    /// <summary>
    /// Wiki 演示用客户实体
    /// </summary>
    [Table(Name = "Wiki_W2_Customer")]
    public class WCustomer
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>客户名</summary>
        public string Name { get; set; } = "";
        /// <summary>城市</summary>
        public string City { get; set; } = "";
    }

    /// <summary>
    /// Wiki 演示用订单明细实体
    /// </summary>
    [Table(Name = "Wiki_W2_OrderDetail")]
    public class WOrderDetail
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>订单外键</summary>
        public int OrderId { get; set; }
        /// <summary>商品名</summary>
        public string Product { get; set; } = "";
        /// <summary>数量</summary>
        public int Qty { get; set; }
    }

    /// <summary>
    /// 基础 DTO — 仅映射 WArticle 自身字段
    /// </summary>
    [MapFrom(typeof(WArticle))]
    public class WArticleDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>标题</summary>
        public string Title { get; set; } = "";
        /// <summary>点击数</summary>
        public int Clicks { get; set; }
    }

    /// <summary>
    /// 含跨表字段的 DTO — CategoryName 映射自 WCategory.Name
    /// </summary>
    [MapFrom(typeof(WArticle))]
    public class WArticleWithCategoryDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>标题</summary>
        public string Title { get; set; } = "";

        /// <summary>
        /// 分类名称 — 跨表映射自 WCategory.Name（经 WArticle.Category 导航）
        /// </summary>
        [MapProperty(typeof(WCategory), nameof(WCategory.Name))]
        public string? CategoryName { get; set; }
    }

    /// <summary>
    /// 构造函数参数映射 DTO — 无无参构造、无写入属性，用于固化"NCMapper 不支持构造函数参数映射"行为
    /// </summary>
    [MapFrom(typeof(WArticle))]
    public class WArticleCtorDto
    {
        /// <summary>
        /// 构造
        /// </summary>
        /// <param name="id">标识</param>
        /// <param name="title">标题</param>
        public WArticleCtorDto(int id, string title)
        {
            Id = id;
            Title = title;
        }

        /// <summary>主键（只读）</summary>
        public int Id { get; }
        /// <summary>标题（只读）</summary>
        public string Title { get; }
    }

    /// <summary>
    /// 双表 Compose DTO — WOrder + WCustomer
    /// </summary>
    [MapFrom(typeof(WOrder))]
    public class WOrderCustomerDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>订单号</summary>
        public string OrderNo { get; set; } = "";
        /// <summary>金额</summary>
        public decimal Amount { get; set; }

        /// <summary>客户名称 — 跨表映射自 WCustomer.Name</summary>
        [MapProperty(typeof(WCustomer), nameof(WCustomer.Name))]
        public string? CustomerName { get; set; }
    }

    /// <summary>
    /// 三表 Compose DTO — WOrder + WCustomer + WOrderDetail
    /// </summary>
    [MapFrom(typeof(WOrder))]
    public class WOrderFullDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>订单号</summary>
        public string OrderNo { get; set; } = "";

        /// <summary>客户名称 — 跨表映射自 WCustomer.Name</summary>
        [MapProperty(typeof(WCustomer), nameof(WCustomer.Name))]
        public string? CustomerName { get; set; }

        /// <summary>商品名 — 跨表映射自 WOrderDetail.Product</summary>
        [MapProperty(typeof(WOrderDetail), nameof(WOrderDetail.Product))]
        public string? Product { get; set; }
    }

    #endregion

    // ================================================================
    // FreeSqlTestFixture — 独立 SQLite 临时文件，自动建表+种子数据
    // ================================================================

    /// <summary>
    /// FreeSql 集成测试共享 Fixture — 独立 SQLite 临时文件（Guid 命名），
    /// <para>UseAutoSyncStructure(true) 自动同步表结构，种子数据覆盖全部用例需要</para>
    /// <para>用法：测试类 implement IClassFixture&lt;FreeSqlTestFixture&gt; 获取共享实例</para>
    /// </summary>
    public class FreeSqlTestFixture : IDisposable
    {
        /// <summary>
        /// FreeSql 实例
        /// </summary>
        public IFreeSql Fsql { get; }

        private readonly string _dbPath;

        /// <summary>
        /// 构造 — 创建独立 SQLite 临时文件并植入种子数据
        /// </summary>
        public FreeSqlTestFixture()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"ncmapper_fstest_{Guid.NewGuid():N}.db");

            Fsql = new FreeSqlBuilder()
                .UseConnectionString(DataType.Sqlite, $"DataSource={_dbPath}")
                .UseAutoSyncStructure(true)
                .Build();

            SeedData();
        }

        /// <summary>
        /// 释放 FreeSql 并删除临时数据库文件
        /// </summary>
        public void Dispose()
        {
            Fsql.Dispose();
            if (File.Exists(_dbPath))
            {
                try { File.Delete(_dbPath); } catch { }
            }
        }

        /// <summary>
        /// 种子数据植入 — 覆盖全部用例需要
        /// <para>Category 类型：技术/生活/新闻（3 条）</para>
        /// <para>Category：.NET/Python（技术）、旅游/美食（生活）、时事（新闻）— 5 条</para>
        /// <para>Topic：5 条（含 1 条 IsDeleted=true、2 条同一分类、分布 3 个分类）</para>
        /// <para>FsOtherEntity：2 条（T1-08 用）</para>
        /// <para>Comment：4 条（含 IsDeleted=true、IsApproved=false）</para>
        /// </summary>
        private void SeedData()
        {
            // CategoryType
            Fsql.Insert(new List<FsCategoryType>
            {
                new() { Id = 1, TypeName = "技术" },
                new() { Id = 2, TypeName = "生活" },
                new() { Id = 3, TypeName = "新闻" }
            }).ExecuteAffrows();

            // Category（ParentId 指向 CategoryType.Id）
            Fsql.Insert(new List<FsCategory>
            {
                new() { Id = 1, Name = ".NET", ParentId = 1 },
                new() { Id = 2, Name = "Python", ParentId = 1 },
                new() { Id = 3, Name = "旅游", ParentId = 2 },
                new() { Id = 4, Name = "美食", ParentId = 2 },
                new() { Id = 5, Name = "时事", ParentId = 3 }
            }).ExecuteAffrows();

            // Topic（含 IsDeleted=true 的第 5 条）
            Fsql.Insert(new List<FsTopic>
            {
                new() { Id = 1, Title = "NCMapper 入门", Content = "内容1", Clicks = 100, CreateTime = new DateTime(2024, 1, 1), CategoryId = 1, IsDeleted = false },
                new() { Id = 2, Title = "FreeSql 实战", Content = "内容2", Clicks = 200, CreateTime = new DateTime(2024, 2, 1), CategoryId = 1, IsDeleted = false },
                new() { Id = 3, Title = "Python 爬虫", Content = "内容3", Clicks = 50, CreateTime = new DateTime(2024, 3, 1), CategoryId = 2, IsDeleted = false },
                new() { Id = 4, Title = "旅行攻略", Content = "内容4", Clicks = 300, CreateTime = new DateTime(2024, 4, 1), CategoryId = 3, IsDeleted = false },
                new() { Id = 5, Title = "已删除帖子", Content = "已删除", Clicks = 0, CreateTime = new DateTime(2024, 5, 1), CategoryId = 1, IsDeleted = true },
            }).ExecuteAffrows();

            // FsOtherEntity（T1-08 用）
            Fsql.Insert(new List<FsOtherEntity>
            {
                new() { Id = 1, Value = "OtherValue1" },
                new() { Id = 2, Value = "OtherValue2" }
            }).ExecuteAffrows();

            // Comment（分布到 3 个 Topic，含软删除和未审核）
            Fsql.Insert(new List<FsComment>
            {
                new() { Id = 1, TopicId = 1, Body = "好文章", IsDeleted = false, IsApproved = true, CreateTime = new DateTime(2024, 1, 2) },
                new() { Id = 2, TopicId = 1, Body = "已删除评论", IsDeleted = true, IsApproved = false, CreateTime = new DateTime(2024, 1, 3) },
                new() { Id = 3, TopicId = 2, Body = "学习了", IsDeleted = false, IsApproved = true, CreateTime = new DateTime(2024, 2, 2) },
                new() { Id = 4, TopicId = 3, Body = "不错", IsDeleted = false, IsApproved = false, CreateTime = new DateTime(2024, 3, 2) },
            }).ExecuteAffrows();

            // Wiki 转测试实体
            Fsql.Insert(new List<WCategory>
            {
                new() { Id = 1, Name = ".NET" },
                new() { Id = 2, Name = "Python" },
            }).ExecuteAffrows();

            Fsql.Insert(new List<WArticle>
            {
                new() { Id = 1, Title = "NCMapper 入门", Clicks = 100, CreateTime = new DateTime(2024, 1, 1), CategoryId = 1 },
                new() { Id = 2, Title = "FreeSql 实战", Clicks = 200, CreateTime = new DateTime(2024, 2, 1), CategoryId = 1 },
                new() { Id = 3, Title = "Python 爬虫", Clicks = 50, CreateTime = new DateTime(2024, 3, 1), CategoryId = 2 },
            }).ExecuteAffrows();

            Fsql.Insert(new List<WCustomer>
            {
                new() { Id = 1, Name = "张三", City = "北京" },
                new() { Id = 2, Name = "李四", City = "上海" },
            }).ExecuteAffrows();

            Fsql.Insert(new List<WOrder>
            {
                new() { Id = 1, OrderNo = "ORD-001", CustomerId = 1, Amount = 100m },
                new() { Id = 2, OrderNo = "ORD-002", CustomerId = 1, Amount = 200m },
                new() { Id = 3, OrderNo = "ORD-003", CustomerId = 2, Amount = 150m },
                new() { Id = 4, OrderNo = "ORD-004", CustomerId = 99, Amount = 999m }, // 无匹配客户 → InnerJoin 过滤
            }).ExecuteAffrows();

            Fsql.Insert(new List<WOrderDetail>
            {
                new() { Id = 1, OrderId = 1, Product = "键盘", Qty = 2 },
                new() { Id = 2, OrderId = 2, Product = "鼠标", Qty = 1 },
            }).ExecuteAffrows();
        }
    }

    // ================================================================
    // 补测基础设施
    // ================================================================

    #region DTO — 导航依赖精确检测（NT-003 用）

    /// <summary>
    /// 导航依赖 DTO 降级路径 Trace 守卫专用
    /// <para>CategoryName 为合法跨表字段（[MapProperty(typeof(FsCategory), "Name")]，经 FsTopic.Category 类型匹配解析），使 FreeSqlNavigationCollector.Collect 产出含 "Category" 根的导航树（树空则降级检测提前 return）</para>
    /// <para>Content 标注 ConvertUsing 强制走 Compose 降级路径，从而触发 ApplyFreeSqlComposeIncludes 的 Trace 输出</para>
    /// <para>设计约束：本 DTO 仅使用合法 [MapProperty] 形式（FromField 为简单属性名）。P1 修复后（ApplyFreeSqlComposeIncludes 改为 fieldSourceType != T1 判定），本 DTO 的 CategoryName 合法跨表字段会触发精确依赖串；NT-003 用其做正向验证。</para>
    /// </summary>
    [MapFrom(typeof(FsTopic))]
    public class FsTopicNavDepDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>标题</summary>
        public string? Title { get; set; }

        /// <summary>内容 — ConvertUsing 触发降级</summary>
        [Convert(typeof(NoOpTopicConverter), nameof(NoOpTopicConverter.Convert))]
        public string? Content { get; set; }

        /// <summary>分类名称 — 合法跨表映射自 FsCategory.Name（经 FsTopic.Category 导航解析，保证导航树非空）</summary>
        [MapProperty(typeof(FsCategory), nameof(FsCategory.Name))]
        public string? CategoryName { get; set; }
    }

    /// <summary>
    /// 纯 T1 字段 DTO — NT-003 反向验证专用
    /// <para>所有字段均来自 T1（FsTopic）本身：Title 同源 [MapFrom]，Content 经 ConvertUsing 同源转换；无跨表 [MapProperty]。</para>
    /// <para>P1 修复后，ApplyFreeSqlComposeIncludes 的 fieldSourceType != T1 判定使本 DTO 不触发精确依赖分支，走 else 兜底分支（输出"自动 Include 不支持多表"）。</para>
    /// <para>用于验证修复的精确性：精确检测仅对合法跨表字段触发，不对纯 T1 字段误报。</para>
    /// </summary>
    [MapFrom(typeof(FsTopic))]
    public class FsTopicPureT1Dto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>标题 — 同源字段</summary>
        public string? Title { get; set; }

        /// <summary>内容 — ConvertUsing 触发降级（同源 FsTopic → string）</summary>
        [Convert(typeof(NoOpTopicConverter), nameof(NoOpTopicConverter.Convert))]
        public string? Content { get; set; }
    }

    #endregion

    /// <summary>
    /// null 过滤专用 Fixture（NT-001/NT-002）— 独立 SQLite 临时文件，与共享 FreeSqlTestFixture 完全隔离
    /// <para>种子 = 共享 Fixture 的 5 条 Topic + 1 条 orphan Topic（Id=6, CategoryId=999 无匹配 Category）</para>
    /// <para>orphan 在双表 LeftJoin(a.CategoryId==b.Id) 产生 b=null 行，三表再 LeftJoin(b.ParentId==c.Id) 产生 c=null 行</para>
    /// </summary>
    public class FreeSqlNullFilterFixture : IDisposable
    {
        /// <summary>
        /// FreeSql 实例
        /// </summary>
        public IFreeSql Fsql { get; }

        private readonly string _dbPath;

        /// <summary>
        /// 构造 — 创建独立 SQLite 临时文件并植入含 orphan 的种子数据
        /// </summary>
        public FreeSqlNullFilterFixture()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"ncmapper_fsnull_{Guid.NewGuid():N}.db");

            Fsql = new FreeSqlBuilder()
                .UseConnectionString(DataType.Sqlite, $"DataSource={_dbPath}")
                .UseAutoSyncStructure(true)
                .Build();

            SeedData();
        }

        /// <summary>
        /// 释放 FreeSql 并删除临时数据库文件
        /// </summary>
        public void Dispose()
        {
            Fsql.Dispose();
            if (File.Exists(_dbPath))
            {
                try { File.Delete(_dbPath); } catch { }
            }
        }

        /// <summary>
        /// 种子数据 — CategoryType 3 条 / Category 5 条与共享 Fixture 一致；
        /// Topic 6 条 = 共享 5 条 + orphan（Id=6, CategoryId=999, IsDeleted=false, Content 非空以便 ConvertUsing 断言）
        /// </summary>
        private void SeedData()
        {
            Fsql.Insert(new List<FsCategoryType>
            {
                new() { Id = 1, TypeName = "技术" },
                new() { Id = 2, TypeName = "生活" },
                new() { Id = 3, TypeName = "新闻" }
            }).ExecuteAffrows();

            Fsql.Insert(new List<FsCategory>
            {
                new() { Id = 1, Name = ".NET", ParentId = 1 },
                new() { Id = 2, Name = "Python", ParentId = 1 },
                new() { Id = 3, Name = "旅游", ParentId = 2 },
                new() { Id = 4, Name = "美食", ParentId = 2 },
                new() { Id = 5, Name = "时事", ParentId = 3 }
            }).ExecuteAffrows();

            Fsql.Insert(new List<FsTopic>
            {
                new() { Id = 1, Title = "NCMapper 入门", Content = "内容1", Clicks = 100, CreateTime = new DateTime(2024, 1, 1), CategoryId = 1, IsDeleted = false },
                new() { Id = 2, Title = "FreeSql 实战", Content = "内容2", Clicks = 200, CreateTime = new DateTime(2024, 2, 1), CategoryId = 1, IsDeleted = false },
                new() { Id = 3, Title = "Python 爬虫", Content = "内容3", Clicks = 50, CreateTime = new DateTime(2024, 3, 1), CategoryId = 2, IsDeleted = false },
                new() { Id = 4, Title = "旅行攻略", Content = "内容4", Clicks = 300, CreateTime = new DateTime(2024, 4, 1), CategoryId = 3, IsDeleted = false },
                new() { Id = 5, Title = "已删除帖子", Content = "已删除", Clicks = 0, CreateTime = new DateTime(2024, 5, 1), CategoryId = 1, IsDeleted = true },
                // orphan：CategoryId=999 无匹配 Category → LeftJoin 产生 null T2 行
                new() { Id = 6, Title = "孤儿帖子", Content = "孤儿内容", Clicks = 1, CreateTime = new DateTime(2024, 6, 1), CategoryId = 999, IsDeleted = false },
            }).ExecuteAffrows();
        }
    }
}
