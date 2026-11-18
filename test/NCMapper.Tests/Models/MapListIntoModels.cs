using NCMapper.Attributes;

namespace NCMapper.Tests.Models
{
    // ================================================================
    // MapListInto 批量就地映射专项测试模型
    // 标注 [MapFrom] 的对 → 源生成器注册（ListInto/Update/Map/ListFunc 槽位齐全）；
    // 无标注的对（UnregListSource/UnregListDto）→ 运行时回退（路径 4 / MapperBase 直调）。
    // ================================================================

    /// <summary>
    /// 扁平源实体（含可选嵌套子对象）— MapListInto 测试主对
    /// </summary>
    public class ListIntoFlatSource
    {
        /// <summary>
        /// 标识符
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 名称
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// 激活标志
        /// </summary>
        public bool Active { get; set; }

        /// <summary>
        /// 分值
        /// </summary>
        public double Score { get; set; }

        /// <summary>
        /// 嵌套子对象（C7：每轮新建）
        /// </summary>
        public ListIntoFlatChild? Child { get; set; }
    }

    /// <summary>
    /// 扁平目标 DTO — 含 Extra 未映射字段（C6：上轮残留不清空）
    /// </summary>
    [MapFrom(typeof(ListIntoFlatSource))]
    public class ListIntoFlatDto
    {
        /// <summary>
        /// 标识符
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 名称
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// 激活标志
        /// </summary>
        public bool Active { get; set; }

        /// <summary>
        /// 分值
        /// </summary>
        public double Score { get; set; }

        /// <summary>
        /// 嵌套子对象
        /// </summary>
        public ListIntoFlatChildDto? Child { get; set; }

        /// <summary>
        /// 未映射字段 — 源无对应属性，映射不清空（C6 残留语义）
        /// </summary>
        public string Extra { get; set; } = "";
    }

    /// <summary>
    /// 嵌套子对象源
    /// </summary>
    public class ListIntoFlatChild
    {
        /// <summary>
        /// 子对象标识
        /// </summary>
        public int ChildId { get; set; }

        /// <summary>
        /// 子对象标签
        /// </summary>
        public string Label { get; set; } = "";
    }

    /// <summary>
    /// 嵌套子对象目标 DTO
    /// </summary>
    [MapFrom(typeof(ListIntoFlatChild))]
    public class ListIntoFlatChildDto
    {
        /// <summary>
        /// 子对象标识
        /// </summary>
        public int ChildId { get; set; }

        /// <summary>
        /// 子对象标签
        /// </summary>
        public string Label { get; set; } = "";
    }

    /// <summary>
    /// 未注册源（无 [MapFrom]）— 运行时回退对（路径 4 / MapperBase 直调）
    /// </summary>
    public class UnregListSource
    {
        /// <summary>
        /// 标识符
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 名称（可空）
        /// </summary>
        public string? Name { get; set; }
    }

    /// <summary>
    /// 未注册目标 DTO（无 [MapFrom]）— 运行时回退对
    /// </summary>
    public class UnregListDto
    {
        /// <summary>
        /// 标识符
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 名称（可空）
        /// </summary>
        public string? Name { get; set; }
    }
}
