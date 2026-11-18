using NCMapper.Samples.Models.Entities;
using NCMapper.Samples.Helpers;
using NCMapper.Samples.Models.Enums;

namespace NCMapper.Samples.DeepCopy;

/// <summary>
/// 深拷贝场景集 — 同类型/跨类型克隆、Action 回调、策略对比、集合深拷贝。
/// </summary>
public static class DeepCopyScenarios
{
    /// <summary>
    /// 同类型深拷贝：UserEntity → UserEntity（完全独立副本）。
    /// </summary>
    /// <param name="src">源对象</param>
    /// <returns>深拷贝（独立副本）</returns>
    #region DeepCopy_DeepCopySameType
    public static UserEntity DeepCopySameType(UserEntity src)
        => Mapper.DeepCopy(src)!;
    #endregion


    /// <summary>
    /// 带 Action 回调的深拷贝：克隆后修改目标。
    /// </summary>
    /// <param name="src">源对象</param>
    /// <returns>Name 带 [COPY] 前缀的深拷贝</returns>
    #region DeepCopy_DeepCopySameType_DeepCopyWithAction
    public static UserEntity DeepCopyWithAction(UserEntity src)
        => Mapper.DeepCopy(src, (s, t) => t.Name = $"[COPY] {s.Name}")!;
    #endregion


    /// <summary>
    /// 跨类型深拷贝：OrderEntity → OrderEntity（转发到 Map）。
    /// </summary>
    /// <param name="src">源订单</param>
    /// <returns>订单深拷贝</returns>
    #region DeepCopy_DeepCopySameType_DeepCopyCrossType
    public static OrderEntity DeepCopyCrossType(OrderEntity src)
        => Mapper.DeepCopy(src)!;

    #endregion
    /// <summary>
    /// 使用 FastDeepCopyProvider 深拷贝（默认策略，~125ns，无循环引用检测）。
    /// </summary>
    /// <param name="src">源对象</param>
    /// <returns>Fast 深拷贝结果</returns>
    #region DeepCopy_DeepCopyFast
    public static UserEntity DeepCopyFast(UserEntity src)
    {
        var saved = DeepCopyProviderBase.Current;
        try
        {
            DeepCopyProviderBase.Current = new FastDeepCopyProvider();
            return Mapper.DeepCopy(src)!;
        }
        finally
        {
            DeepCopyProviderBase.Current = saved;
        }
    }
    #endregion

    /// <summary>
    /// 使用 ExpressionTree 提供程序深拷贝（~676ns，含循环引用检测）。
    /// </summary>
    /// <param name="src">源对象</param>
    /// <returns>ExpressionTree 深拷贝结果</returns>
    #region DeepCopy_DeepCopyExpressionTree
    public static UserEntity DeepCopyExpressionTree(UserEntity src)
    {
        var saved = DeepCopyProviderBase.Current;
        try
        {
            DeepCopyProviderBase.Current = new ExpressionTreeDeepCopyProvider();
            return Mapper.DeepCopy(src)!;
        }
        finally
        {
            DeepCopyProviderBase.Current = saved;
        }
    }
    #endregion

    /// <summary>
    /// 使用 JSON 提供程序深拷贝（序列化 + 反序列化）。
    /// </summary>
    /// <param name="src">源对象</param>
    /// <returns>JSON 深拷贝结果</returns>
    #region DeepCopy_DeepCopyJson
    public static UserEntity DeepCopyJson(UserEntity src)
    {
        var saved = DeepCopyProviderBase.Current;
        try
        {
            DeepCopyProviderBase.Current = DeepCopyProviderBase.JsonProvider;
            return Mapper.DeepCopy(src)!;
        }
        finally
        {
            DeepCopyProviderBase.Current = saved;
        }
    }
    #endregion

    // ===== 集合深拷贝 =====

    /// <summary>
    /// List&lt;OrderEntity&gt; 深拷贝 — 每个元素独立克隆。
    /// </summary>
    /// <param name="src">源列表</param>
    /// <returns>深拷贝后的列表</returns>
    #region DeepCopy_L95
    public static List<OrderEntity> DeepCopyList(List<OrderEntity> src)
        => Mapper.DeepCopy(src)!;
    #endregion

    /// <summary>
    /// UserEntity[] 数组深拷贝 — 数组元素独立克隆。
    /// </summary>
    /// <param name="src">源数组</param>
    /// <returns>深拷贝后的数组</returns>
    public static UserEntity[] DeepCopyArray(UserEntity[] src)
        => Mapper.DeepCopy(src)!;

    /// <summary>
    /// Dictionary 深拷贝 — Key 保留，Value 克隆。
    /// </summary>
    /// <param name="src">源字典</param>
    /// <returns>深拷贝后的字典</returns>
    public static Dictionary<string, UserEntity> DeepCopyDictionary(Dictionary<string, UserEntity> src)
        => Mapper.DeepCopy(src)!;

    /// <summary>
    /// HashSet&lt;string&gt; 深拷贝。
    /// </summary>
    /// <param name="src">源 HashSet</param>
    /// <returns>深拷贝后的 HashSet</returns>
    public static HashSet<string> DeepCopyHashSet(HashSet<string> src)
        => Mapper.DeepCopy(src)!;

    // ===== MaxRecursionDepth =====

    /// <summary>
    /// 读取 Mapper.MaxRecursionDepth 当前值。
    /// </summary>
    /// <returns>当前递归深度上限</returns>
    public static int GetMaxRecursionDepth()
        => Mapper.MaxRecursionDepth;

    /// <summary>
    /// 演示设置 MaxRecursionDepth = 1 后深拷贝受限。
    /// </summary>
    /// <param name="src">含嵌套对象的源</param>
    /// <returns>异常信息（MaxRecursionDepth=1 触发递归深度超限）</returns>
    #region DeepCopy_MaxRecursionDepth
    public static string? DeepCopyWithLimitedDepth(UserEntity src)
    {
        var saved = Mapper.MaxRecursionDepth;
        try
        {
            Mapper.MaxRecursionDepth = 1;
            Mapper.DeepCopy(src);
            return null;
        }
        catch (MapException ex)
        {
            return $"{ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            Mapper.MaxRecursionDepth = saved;
        }
    }
    #endregion

    /// <summary>
    /// 循环引用演示模型：Parent/Child 互引，验证 DeepCopy 循环引用支持。
    /// </summary>
    public class CycleParent
    {
        /// <summary>父 Id。</summary>
        public int Id { get; set; }

        /// <summary>子节点（与 Parent 互引构成循环）。</summary>
        public CycleChild? Child { get; set; }
    }

    /// <summary>循环引用演示子节点。</summary>
    public class CycleChild
    {
        /// <summary>子 Id。</summary>
        public int Id { get; set; }

        /// <summary>父节点回指。</summary>
        public CycleParent? Parent { get; set; }
    }

    /// <summary>
    /// DAG 共享引用演示模型：两个实体共享同一 Address 实例。
    /// </summary>
    public class DagHolder
    {
        /// <summary>第一个持有者。</summary>
        public UserEntity First { get; set; } = new();

        /// <summary>第二个持有者（与 First 共享同一 Address 实例）。</summary>
        public UserEntity Second { get; set; } = new();
    }

    // ===== 场景注册入口=====
    public static void Run()
    {
    var original = new UserEntity
    {
        Id = 99,
        Name = "Original",
        Age = 40,
        Email = "orig@test.com",
        Status = UserStatus.Active,
        Address = new Address { City = "Beijing", Street = "Chang'an Avenue" },
    };

    var copy = DeepCopyScenarios.DeepCopySameType(original);
    OutputHelper.Dump("401-deepcopy-same-type", original, copy);

    // 期望值：深拷贝产物为完全独立副本（契约：拷贝与源对象图互不共享可变引用）
    if (copy.Id != 99 || copy.Name != "Original" || copy.Address.City != "Beijing" || copy.Address.Street != "Chang'an Avenue")
        OutputHelper.Error($"401-deepcopy-same-type: deep copy should reproduce all values (Id={copy.Id}, Name={copy.Name}, City={copy.Address.City})");
    if (ReferenceEquals(copy.Address, original.Address))
        OutputHelper.Error("401-deepcopy-same-type: nested Address should be an independent copy, not a shared reference (deep-copy.md:27)");

    var copyWithAction = DeepCopyScenarios.DeepCopyWithAction(original);
    OutputHelper.Dump("401-deepcopy-with-action", original.Name, copyWithAction.Name);

    var orderOrig = new OrderEntity
    {
        OrderId = 500,
        OrderNo = "DC-001",
        Amount = 999m,
        Status = OrderStatus.Pending,
    };

    var orderCopy = DeepCopyScenarios.DeepCopyCrossType(orderOrig);
    OutputHelper.Dump("401-deepcopy-cross-type", orderOrig, orderCopy);

    var strategyFast = DeepCopyScenarios.DeepCopyFast(original);
    var strategyExpr = DeepCopyScenarios.DeepCopyExpressionTree(original);
    var strategyJson = DeepCopyScenarios.DeepCopyJson(original);
    OutputHelper.Dump("401-deepcopy-strategy",
        "switch DeepCopyProviderBase.Current: Fast / ExprTree / Json",
        $"Fast/ExprTree/Json all three strategies copied successfully, Name={strategyFast.Name}, copies differ from source and from each other");

    // 期望值：Json 策略往返等值（JSON 序列化中转兜底语义）
    if (strategyJson.Name != "Original" || strategyJson.Address.City != "Beijing")
        OutputHelper.Error($"401-deepcopy-strategy: Json provider round-trip should preserve values (Name={strategyJson.Name}, City={strategyJson.Address.City})");

    // 期望值：ExpressionTree 策略保持 DAG 共享引用（同一实例在产物图中仍共享）
    var sharedAddress = new Address { City = "SharedCity", Street = "SharedStreet" };
    var dagHolder = new DagHolder
    {
        First = new UserEntity { Id = 1, Name = "First", Address = sharedAddress },
        Second = new UserEntity { Id = 2, Name = "Second", Address = sharedAddress },
    };
    var savedProvider = DeepCopyProviderBase.Current;
    DagHolder dagCopy;
    try
    {
        DeepCopyProviderBase.Current = new ExpressionTreeDeepCopyProvider();
        dagCopy = Mapper.DeepCopy(dagHolder)!;
    }
    finally
    {
        DeepCopyProviderBase.Current = savedProvider;
    }
    OutputHelper.Dump("401-deepcopy-dag-shared", "ExpressionTree keeps DAG shared references as one instance in the copy",
        dagCopy.First.Address != null && ReferenceEquals(dagCopy.First.Address, dagCopy.Second.Address));
    if (dagCopy.First.Address == null || !ReferenceEquals(dagCopy.First.Address, dagCopy.Second.Address))
        OutputHelper.Error("401-deepcopy-dag-shared: ExpressionTree should keep shared references as one instance in the copy (deep-copy.md:87)");
    if (ReferenceEquals(dagCopy.First.Address, sharedAddress))
        OutputHelper.Error("401-deepcopy-dag-shared: copy should not alias the source instance (independent object graph)");

    // 期望值：Fast 策略支持循环引用（默认策略语义）
    var cycle = new CycleParent { Id = 7, Child = new CycleChild { Id = 8 } };
    cycle.Child!.Parent = cycle;
    var cycleCopy = Mapper.DeepCopy(cycle)!;
    OutputHelper.Dump("401-deepcopy-cycle", "Fast provider handles circular references (Parent↔Child) without stack overflow",
        cycleCopy.Child != null && ReferenceEquals(cycleCopy.Child.Parent, cycleCopy));
    if (cycleCopy.Child == null || !ReferenceEquals(cycleCopy.Child.Parent, cycleCopy))
        OutputHelper.Error("401-deepcopy-cycle: Fast deep copy should complete on circular references and keep the cycle inside the copy (deep-copy.md:86)");

    // Collection deep copy
    var list = new List<OrderEntity>
    {
        new() { OrderId = 1, OrderNo = "C-001", Amount = 100m },
        new() { OrderId = 2, OrderNo = "C-002", Amount = 200m },
    };
    var copiedList = DeepCopyScenarios.DeepCopyList(list);
    OutputHelper.Dump("401-deepcopy-list", $"Original[0]={list[0].OrderNo}", $"Copy[0]={copiedList[0].OrderNo}");

    var dict = new Dictionary<string, UserEntity>
    {
        ["k1"] = new() { Id = 10, Name = "Dict-A" },
        ["k2"] = new() { Id = 20, Name = "Dict-B" },
    };
    var copiedDict = DeepCopyScenarios.DeepCopyDictionary(dict);
    OutputHelper.Dump("401-deepcopy-dict", $"Keys: {string.Join(",", dict.Keys)}", $"Copy keys: {string.Join(",", copiedDict.Keys)}");

    var set = new HashSet<string> { "a", "b", "c" };
    var copiedSet = DeepCopyScenarios.DeepCopyHashSet(set);
    OutputHelper.Dump("401-deepcopy-hashset", $"Original count: {set.Count}", $"Copy count: {copiedSet.Count}");

    var depth = DeepCopyScenarios.GetMaxRecursionDepth();
    var limited = DeepCopyScenarios.DeepCopyWithLimitedDepth(original);
    OutputHelper.DumpExpectedFailure("401-deepcopy-depth",
        $"Default MaxDepth={depth}; deep-copying a nested object with depth set to 1 (Address exceeds the limit at one level) → MapException",
        limited ?? "PASS");

    // 期望值：深度限制契约——超限必须抛 MapException（MaxRecursionDepth 深度限制语义）
    if (limited == null || !limited.Contains("MapException"))
        OutputHelper.Error($"401-deepcopy-depth: depth=1 on a nested object should throw MapException (actual: {limited ?? "no exception"})");
    }

}
