using System.Collections.Concurrent;

namespace NCMapper.FuzzTests.Models;

/// <summary>
/// 属性测试用简单实体 — 含基本类型字段，FsCheck 内置生成器可直接生成
/// </summary>
public class FuzzUser
{
    /// <summary>
    /// 用户 ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 用户名
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 年龄
    /// </summary>
    public int Age { get; set; }

    /// <summary>
    /// 是否激活
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// 分数
    /// </summary>
    public double Score { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// 属性测试用 DTO — [MapFrom] 标注，与 FuzzUser 同字段
/// </summary>
[NCMapper.Attributes.MapFrom(typeof(FuzzUser))]
public class FuzzUserDto
{
    /// <summary>
    /// 用户 ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 用户名
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 年龄
    /// </summary>
    public int Age { get; set; }

    /// <summary>
    /// 是否激活
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// 分数
    /// </summary>
    public double Score { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// 属性测试用含集合的实体 — 验证集合映射不变式
/// </summary>
public class FuzzCollectionEntity
{
    /// <summary>
    /// 唯一标识
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 标签列表
    /// </summary>
    public List<string>? Tags { get; set; }

    /// <summary>
    /// 分数数组
    /// </summary>
    public int[]? Scores { get; set; }
}

/// <summary>
/// 属性测试用含集合的 DTO
/// </summary>
[NCMapper.Attributes.MapFrom(typeof(FuzzCollectionEntity))]
public class FuzzCollectionDto
{
    /// <summary>
    /// 唯一标识
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 标签列表
    /// </summary>
    public List<string>? Tags { get; set; }

    /// <summary>
    /// 分数数组
    /// </summary>
    public int[]? Scores { get; set; }
}

// ================================================================
// 多样化集合测试模型 — List/HashSet/Dictionary
// ================================================================

/// <summary>
/// 属性测试用多样化集合实体 — 覆盖 List/HashSet/Dictionary 三大集合类型
/// <para>标注 [MapFrom] 自引用，走源生成器 DeepCopy 路径（呼应 CS8628 修复）</para>
/// </summary>
[NCMapper.Attributes.MapFrom(typeof(FuzzRichCollectionEntity))]
public class FuzzRichCollectionEntity
{
    /// <summary>
    /// 唯一标识
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 有序数字列表
    /// </summary>
    public List<int>? Numbers { get; set; }

    /// <summary>
    /// 无序去重标签集合
    /// </summary>
    public HashSet<string>? Tags { get; set; }

    /// <summary>
    /// 键值对计数字典
    /// </summary>
    public Dictionary<string, int>? Counts { get; set; }
}

/// <summary>
/// 多样化集合 DTO — 与源实体同字段，走源生成器 Map 路径
/// </summary>
[NCMapper.Attributes.MapFrom(typeof(FuzzRichCollectionEntity))]
public class FuzzRichCollectionDto
{
    /// <summary>
    /// 唯一标识
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 有序数字列表
    /// </summary>
    public List<int>? Numbers { get; set; }

    /// <summary>
    /// 无序去重标签集合
    /// </summary>
    public HashSet<string>? Tags { get; set; }

    /// <summary>
    /// 键值对计数字典
    /// </summary>
    public Dictionary<string, int>? Counts { get; set; }
}

// ================================================================
// 类型转换测试模型 — int→long / int→decimal / enum→int
// ================================================================

/// <summary>
/// 测试用状态枚举 — 三个已定义值
/// </summary>
public enum FuzzStatus
{
    /// <summary>
    /// 激活状态
    /// </summary>
    Active = 0,

    /// <summary>
    /// 未激活状态
    /// </summary>
    Inactive = 1,

    /// <summary>
    /// 暂停状态
    /// </summary>
    Suspended = 2
}

/// <summary>
/// 类型转换测试源实体 — int/enum 类型字段
/// </summary>
public class FuzzTypeEntity
{
    /// <summary>
    /// 标识（int 类型，目标为 long）
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 分数（int 类型，目标为 decimal）
    /// </summary>
    public int Score { get; set; }

    /// <summary>
    /// 状态（enum 类型，目标为 int）
    /// </summary>
    public FuzzStatus Status { get; set; }
}

/// <summary>
/// 类型转换测试 DTO — 验证隐式类型转换：int→long、int→decimal、enum→int
/// </summary>
[NCMapper.Attributes.MapFrom(typeof(FuzzTypeEntity))]
public class FuzzTypeDto
{
    /// <summary>
    /// 标识（long 类型，int→long 隐式扩展转换）
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// 分数（decimal 类型，int→decimal 隐式转换）
    /// </summary>
    public decimal Score { get; set; }

    /// <summary>
    /// 状态（int 类型，enum→int 取底层值）
    /// </summary>
    public int Status { get; set; }
}

// ================================================================
// 嵌套对象深拷贝测试模型
// ================================================================

/// <summary>
/// 嵌套子对象 — 地址信息（不标注 [MapFrom]，DeepCopy 走 FastDeepCopyProvider）
/// </summary>
public class FuzzAddress
{
    /// <summary>
    /// 城市名称
    /// </summary>
    public string City { get; set; } = "";

    /// <summary>
    /// 邮政编码
    /// </summary>
    public int ZipCode { get; set; }
}

/// <summary>
/// 嵌套对象测试实体 — 含 FuzzAddress 子对象
/// <para>标注 [MapFrom] 自引用，走源生成器 DeepCopy 路径</para>
/// <para>子对象 FuzzAddress 不标注 [MapFrom]，走 FastDeepCopyProvider — 覆盖混合路径</para>
/// </summary>
[NCMapper.Attributes.MapFrom(typeof(FuzzNestedEntity))]
public class FuzzNestedEntity
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
    /// 嵌套地址子对象
    /// </summary>
    public FuzzAddress? Address { get; set; }
}

// ================================================================
// 更多集合类型测试模型 — Queue/Stack/SortedSet/LinkedList
// ================================================================

/// <summary>
/// 属性测试用特殊集合实体 — 覆盖 Queue/Stack/SortedSet/LinkedList 四种集合类型
/// <para>标注 [MapFrom] 自引用，走源生成器 DeepCopy 路径</para>
/// <para>直接呼应 CS8628 修复：这些可空集合类型曾生成非法 new T?() 语法</para>
/// </summary>
[NCMapper.Attributes.MapFrom(typeof(FuzzExoticCollectionEntity))]
public class FuzzExoticCollectionEntity
{
    /// <summary>
    /// 唯一标识
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 任务队列（FIFO 有序）
    /// </summary>
    public Queue<int>? TaskQueue { get; set; }

    /// <summary>
    /// 历史栈（LIFO 有序）
    /// </summary>
    public Stack<int>? HistoryStack { get; set; }

    /// <summary>
    /// 排序数字集合（自动去重+排序）
    /// </summary>
    public SortedSet<int>? SortedNumbers { get; set; }

    /// <summary>
    /// 链表数字（双向链表，保持插入顺序）
    /// </summary>
    public LinkedList<int>? LinkedNumbers { get; set; }
}

/// <summary>
/// 特殊集合 DTO — 与源实体同字段，走源生成器 Map 路径
/// </summary>
[NCMapper.Attributes.MapFrom(typeof(FuzzExoticCollectionEntity))]
public class FuzzExoticCollectionDto
{
    /// <summary>
    /// 唯一标识
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 任务队列（FIFO 有序）
    /// </summary>
    public Queue<int>? TaskQueue { get; set; }

    /// <summary>
    /// 历史栈（LIFO 有序）
    /// </summary>
    public Stack<int>? HistoryStack { get; set; }

    /// <summary>
    /// 排序数字集合（自动去重+排序）
    /// </summary>
    public SortedSet<int>? SortedNumbers { get; set; }

    /// <summary>
    /// 链表数字（双向链表，保持插入顺序）
    /// </summary>
    public LinkedList<int>? LinkedNumbers { get; set; }
}

// ================================================================
// ConvertUsing 自定义转换器测试模型
// ================================================================

/// <summary>
/// ConvertUsing 测试源实体 — 含姓名和价格数量字段
/// </summary>
public class FuzzConverterEntity
{
    /// <summary>
    /// 唯一标识
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 名（用于多字段组合 FullName）
    /// </summary>
    public string FirstName { get; set; } = "";

    /// <summary>
    /// 姓（用于多字段组合 FullName）
    /// </summary>
    public string LastName { get; set; } = "";

    /// <summary>
    /// 数量（用于多字段计算 TotalPrice）
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// 单价（用于多字段计算 TotalPrice）
    /// </summary>
    public int Price { get; set; }
}

/// <summary>
/// 多字段组合转换器（静态方法引用形态，T2 模板 M(TSource)）— FullName = FirstName + " " + LastName
/// </summary>
public static class FuzzFullNameConverter
{
    /// <summary>
    /// 将源实体的 FirstName 和 LastName 组合为 FullName
    /// </summary>
    /// <param name="source">整个源实体对象</param>
    /// <returns>组合后的全名字符串</returns>
    public static string Convert(FuzzConverterEntity source)
        => $"{source.FirstName} {source.LastName}";
}

/// <summary>
/// 多字段计算转换器（静态方法引用形态，T2 模板 M(TSource)）— TotalPrice = Price * Quantity（结果转 decimal 避免溢出）
/// </summary>
public static class FuzzTotalPriceConverter
{
    /// <summary>
    /// 计算源实体的 Price * Quantity 作为总价
    /// </summary>
    /// <param name="source">整个源实体对象</param>
    /// <returns>总价（decimal 类型）</returns>
    public static decimal Convert(FuzzConverterEntity source)
        => (decimal)source.Price * source.Quantity;
}

/// <summary>
/// ConvertUsing 测试 DTO — 使用 [Convert] 标注属性
/// </summary>
[NCMapper.Attributes.MapFrom(typeof(FuzzConverterEntity))]
public class FuzzConverterDto
{
    /// <summary>
    /// 唯一标识（非 ConvertUsing 字段，正常映射）
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 全名（多字段组合：FirstName + LastName）
    /// </summary>
    [NCMapper.Attributes.Convert(typeof(FuzzFullNameConverter), nameof(FuzzFullNameConverter.Convert))]
    [NCMapper.Attributes.MapProperty(typeof(FuzzConverterEntity), nameof(FuzzConverterEntity.FirstName))]
    public string FullName { get; set; } = "";

    /// <summary>
    /// 总价（多字段计算：Price * Quantity）
    /// </summary>
    [NCMapper.Attributes.Convert(typeof(FuzzTotalPriceConverter), nameof(FuzzTotalPriceConverter.Convert))]
    [NCMapper.Attributes.MapProperty(typeof(FuzzConverterEntity), nameof(FuzzConverterEntity.Price))]
    public decimal TotalPrice { get; set; }
}

// ================================================================
// Compose 多源合成测试模型
// ================================================================

/// <summary>
/// Compose 主源实体 — 订单信息
/// </summary>
public class FuzzOrderEntity
{
    /// <summary>
    /// 订单 ID
    /// </summary>
    public int OrderId { get; set; }

    /// <summary>
    /// 订单金额
    /// </summary>
    public int Amount { get; set; }
}

/// <summary>
/// Compose 辅源实体 — 客户信息
/// </summary>
public class FuzzCustomerEntity
{
    /// <summary>
    /// 客户名称
    /// </summary>
    public string CustomerName { get; set; } = "";

    /// <summary>
    /// 客户等级
    /// </summary>
    public int CustomerLevel { get; set; }
}

/// <summary>
/// Compose 合成 DTO — 主映射 FuzzOrderEntity，辅映射 FuzzCustomerEntity
/// <para>[MapFrom(typeof(FuzzOrderEntity))] 声明主源，FuzzOrderEntity 创建 DTO 实例</para>
/// <para>[MapProperty(typeof(FuzzCustomerEntity), ...)] 指定从辅源取值的字段</para>
/// </summary>
[NCMapper.Attributes.MapFrom(typeof(FuzzOrderEntity))]
public class FuzzComposeDto
{
    /// <summary>
    /// 订单 ID（从主源 FuzzOrderEntity 映射）
    /// </summary>
    [NCMapper.Attributes.MapProperty(typeof(FuzzOrderEntity), nameof(FuzzOrderEntity.OrderId))]
    public int OrderId { get; set; }

    /// <summary>
    /// 订单金额（从主源 FuzzOrderEntity 映射）
    /// </summary>
    [NCMapper.Attributes.MapProperty(typeof(FuzzOrderEntity), nameof(FuzzOrderEntity.Amount))]
    public int Amount { get; set; }

    /// <summary>
    /// 客户名称（从辅源 FuzzCustomerEntity 映射）
    /// </summary>
    [NCMapper.Attributes.MapProperty(typeof(FuzzCustomerEntity), nameof(FuzzCustomerEntity.CustomerName))]
    public string CustomerName { get; set; } = "";

    /// <summary>
    /// 客户等级（从辅源 FuzzCustomerEntity 映射）
    /// </summary>
    [NCMapper.Attributes.MapProperty(typeof(FuzzCustomerEntity), nameof(FuzzCustomerEntity.CustomerLevel))]
    public int CustomerLevel { get; set; }
}

// ================================================================
// MapIgnore 属性忽略测试模型
// ================================================================

/// <summary>
/// MapIgnore 测试源实体 — 含敏感字段
/// </summary>
public class FuzzIgnoreEntity
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
    /// 敏感密钥（DTO 中标注 [MapIgnore] 不参与映射）
    /// </summary>
    public string SecretKey { get; set; } = "";
}

/// <summary>
/// MapIgnore 测试 DTO — SecretKey 标注 [MapIgnore] 双向忽略
/// </summary>
[NCMapper.Attributes.MapFrom(typeof(FuzzIgnoreEntity))]
public class FuzzIgnoreDto
{
    /// <summary>
    /// 唯一标识（正常映射）
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 名称（正常映射）
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 敏感密钥（双向忽略，不参与 Entity↔DTO 映射，保持默认值）
    /// </summary>
    [NCMapper.Attributes.MapIgnore]
    public string SecretKey { get; set; } = "DEFAULT";
}

// ================================================================
// MapIgnore 方向控制测试模型 — Direction.ToDto / Direction.ToEntity
// ================================================================

/// <summary>
/// MapIgnore 方向控制测试源实体 — 含只写字段和只读字段
/// </summary>
public class FuzzDirectionEntity
{
    /// <summary>
    /// 唯一标识
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 只写字段（Entity→DTO 时忽略，DTO→Entity 时映射）
    /// </summary>
    public string WriteOnly { get; set; } = "";

    /// <summary>
    /// 只读字段（Entity→DTO 时映射，DTO→Entity 时忽略）
    /// </summary>
    public string ReadOnly { get; set; } = "";
}

/// <summary>
/// MapIgnore 方向控制测试 DTO — 方向忽略标注
/// <para>[MapIgnore(Direction.ToDto)] — 仅正向映射时忽略，反向映射时正常</para>
/// <para>[MapIgnore(Direction.ToEntity)] — 仅反向映射时忽略，正向映射时正常</para>
/// </summary>
[NCMapper.Attributes.MapFrom(typeof(FuzzDirectionEntity))]
public class FuzzDirectionDto
{
    /// <summary>
    /// 唯一标识（正常映射，不受方向控制影响）
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 只写字段（Entity→DTO 时忽略保持默认，DTO→Entity 时正常映射）
    /// </summary>
    [NCMapper.Attributes.MapIgnore(NCMapper.Attributes.Direction.ToDto)]
    public string WriteOnly { get; set; } = "DTO_DEFAULT";

    /// <summary>
    /// 只读字段（Entity→DTO 时正常映射，DTO→Entity 时忽略保持默认）
    /// </summary>
    [NCMapper.Attributes.MapIgnore(NCMapper.Attributes.Direction.ToEntity)]
    public string ReadOnly { get; set; } = "DTO_DEFAULT";
}

// ================================================================
// Compose 三源合成测试模型
// ================================================================

/// <summary>
/// Compose 第三源实体 — 产品信息
/// </summary>
public class FuzzProductEntity
{
    /// <summary>
    /// 产品名称
    /// </summary>
    public string ProductName { get; set; } = "";

    /// <summary>
    /// 库存数量
    /// </summary>
    public int Stock { get; set; }
}

/// <summary>
/// 三源合成 DTO — 主映射 FuzzOrderEntity，辅映射 FuzzCustomerEntity + FuzzProductEntity
/// </summary>
[NCMapper.Attributes.MapFrom(typeof(FuzzOrderEntity))]
public class FuzzTripleComposeDto
{
    /// <summary>
    /// 订单 ID（从主源 FuzzOrderEntity 映射）
    /// </summary>
    [NCMapper.Attributes.MapProperty(typeof(FuzzOrderEntity), nameof(FuzzOrderEntity.OrderId))]
    public int OrderId { get; set; }

    /// <summary>
    /// 订单金额（从主源 FuzzOrderEntity 映射）
    /// </summary>
    [NCMapper.Attributes.MapProperty(typeof(FuzzOrderEntity), nameof(FuzzOrderEntity.Amount))]
    public int Amount { get; set; }

    /// <summary>
    /// 客户名称（从辅源 FuzzCustomerEntity 映射）
    /// </summary>
    [NCMapper.Attributes.MapProperty(typeof(FuzzCustomerEntity), nameof(FuzzCustomerEntity.CustomerName))]
    public string CustomerName { get; set; } = "";

    /// <summary>
    /// 客户等级（从辅源 FuzzCustomerEntity 映射）
    /// </summary>
    [NCMapper.Attributes.MapProperty(typeof(FuzzCustomerEntity), nameof(FuzzCustomerEntity.CustomerLevel))]
    public int CustomerLevel { get; set; }

    /// <summary>
    /// 产品名称（从第三源 FuzzProductEntity 映射）
    /// </summary>
    [NCMapper.Attributes.MapProperty(typeof(FuzzProductEntity), nameof(FuzzProductEntity.ProductName))]
    public string ProductName { get; set; } = "";

    /// <summary>
    /// 库存数量（从第三源 FuzzProductEntity 映射）
    /// </summary>
    [NCMapper.Attributes.MapProperty(typeof(FuzzProductEntity), nameof(FuzzProductEntity.Stock))]
    public int Stock { get; set; }
}

// ================================================================
// 循环引用测试模型 — ExpressionTreeDeepCopyProvider 专用
// ================================================================

/// <summary>
/// 循环引用节点 — Self 属性指向自身，用于测试 ExpressionTreeDeepCopyProvider 的循环引用检测
/// </summary>
public class FuzzCircularNode
{
    /// <summary>
    /// 节点唯一标识
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 节点名称
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 指向自身的循环引用
    /// </summary>
    public FuzzCircularNode? Self { get; set; }
}

// ================================================================
// M-07: record 循环引用端到端测试模型 — 源生成器检测循环 → DeepCopyRuntimeOnly 回退
// ================================================================

/// <summary>
/// 纯位置 record — Parent 为构造器参数，TryCompileRecordCreator 能匹配并递归 DeepCopy
/// <para>循环引用测试通过反射设置 &lt;Parent&gt;k__BackingField 创建自引用环
/// （positional record 属性为 init-only，无法在构造后直接赋值）。</para>
/// </summary>
[NCMapper.Attributes.MapFrom(typeof(FuzzRecordHolder))]
public record FuzzRecordHolder(int Id, string Name, FuzzRecordHolder? Parent);

// ================================================================
// Concurrent 集合测试模型 — ConcurrentDictionary/ConcurrentQueue/ConcurrentStack
// ================================================================

/// <summary>
/// Concurrent 集合测试实体 — 覆盖并发集合类型
/// <para>标注 [MapFrom] 自引用，走源生成器 DeepCopy 路径</para>
/// </summary>
[NCMapper.Attributes.MapFrom(typeof(FuzzConcurrentCollectionEntity))]
public class FuzzConcurrentCollectionEntity
{
    /// <summary>
    /// 唯一标识
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 并发字典
    /// </summary>
    public ConcurrentDictionary<string, int>? Counts { get; set; }

    /// <summary>
    /// 并发队列（FIFO）
    /// </summary>
    public ConcurrentQueue<int>? TaskQueue { get; set; }

    /// <summary>
    /// 并发栈（LIFO）
    /// </summary>
    public ConcurrentStack<int>? HistoryStack { get; set; }
}

/// <summary>
/// Concurrent 集合 DTO — 与源实体同字段
/// </summary>
[NCMapper.Attributes.MapFrom(typeof(FuzzConcurrentCollectionEntity))]
public class FuzzConcurrentCollectionDto
{
    /// <summary>
    /// 唯一标识
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 并发字典
    /// </summary>
    public ConcurrentDictionary<string, int>? Counts { get; set; }

    /// <summary>
    /// 并发队列（FIFO）
    /// </summary>
    public ConcurrentQueue<int>? TaskQueue { get; set; }

    /// <summary>
    /// 并发栈（LIFO）
    /// </summary>
    public ConcurrentStack<int>? HistoryStack { get; set; }
}

// ================================================================
// IsFromJson 测试模型 — JSON 字符串反序列化
// ================================================================

/// <summary>
/// JSON 配置对象 — 从 JSON 字符串反序列化
/// </summary>
public class FuzzJsonConfig
{
    /// <summary>
    /// 主题名称
    /// </summary>
    public string Theme { get; set; } = "";

    /// <summary>
    /// 字体大小
    /// </summary>
    public int FontSize { get; set; }
}

/// <summary>
/// IsFromJson 测试源实体 — JSON 字符串存储在 string 字段中
/// </summary>
public class FuzzJsonEntity
{
    /// <summary>
    /// 唯一标识
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 配置 JSON 字符串 — 反序列化为 FuzzJsonConfig 对象
    /// </summary>
    public string ConfigJson { get; set; } = "";

    /// <summary>
    /// 标签 JSON 字符串 — 反序列化为 List&lt;string&gt;
    /// </summary>
    public string TagsJson { get; set; } = "[]";
}

/// <summary>
/// IsFromJson 测试 DTO — 使用 [MapProperty(IsFromJson = true)] 反序列化
/// </summary>
[NCMapper.Attributes.MapFrom(typeof(FuzzJsonEntity))]
public class FuzzJsonDto
{
    /// <summary>
    /// 唯一标识（正常映射）
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 配置对象（从 ConfigJson JSON 字符串反序列化）
    /// </summary>
    [NCMapper.Attributes.MapProperty(typeof(FuzzJsonEntity), nameof(FuzzJsonEntity.ConfigJson), IsFromJson = true)]
    public FuzzJsonConfig? Config { get; set; }

    /// <summary>
    /// 标签列表（从 TagsJson JSON 字符串反序列化）
    /// </summary>
    [NCMapper.Attributes.MapProperty(typeof(FuzzJsonEntity), nameof(FuzzJsonEntity.TagsJson), IsFromJson = true)]
    public List<string>? Tags { get; set; }
}
