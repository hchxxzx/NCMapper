using System.Diagnostics;
using NCMapper;
using NCMapper.Benchmarks.Models;
using NCMapper.Benchmarks;
using NCMapper.Generated;

// 改进版微基准 — 10轮，丢弃前2轮热身，取 P50 中位数 + Avg + Min
Console.OutputEncoding = System.Text.Encoding.UTF8;

// 循环引用测试数据（自引用，环路径节点数=1）
var circularBenchmarkData = new CircularBenchmarkNode { Value = 1 };
circularBenchmarkData.Self = circularBenchmarkData;

// 大对象图测试数据（50 节点链表，无循环，< MaxRecursionDepth=64）
var largeChainData = new ChainNode { Value = 0 };
var chainCur = largeChainData;
for (int ci = 1; ci < 50; ci++)
{
    chainCur.Next = new ChainNode { Value = ci };
    chainCur = chainCur.Next;
}

// T8 新增：20 节点单向环（Fast Cyclic 路径完整检测验证）
var cyclicRingData = new RingNode { Value = 0 };
var ringCur = cyclicRingData;
for (int ri = 1; ri < 20; ri++)
{
    ringCur.Next = new RingNode { Value = ri };
    ringCur = ringCur.Next;
}
ringCur.Next = cyclicRingData; // 闭合成环

// T8 新增：100 节点 Acyclic 树（Fast Acyclic 零检测快路径性能量化）
var acyclicTreeData = BuildAcyclicTree(100);

// 预热
Console.WriteLine("Warming up...");
for (int i = 0; i < 10000; i++)
{
    Mapper.Map<FlatEntity, FlatDto>(BenchmarkData.FlatEntity);
    Mapper.Map<NameDiffEntity, NameDiffDto>(BenchmarkData.NameDiffEntity);
    Mapper.Map<NestedEntity, NestedDto>(BenchmarkData.NestedEntity);
    Mapper.MapList<FlatEntity, FlatDto>(BenchmarkData.FlatEntities100);
    Mapper.DeepCopy(BenchmarkData.DeepCopyEntityData);
    Mapper.DeepCopy(largeChainData);
    Mapper.DeepCopy(cyclicRingData);   // T8 warmup: Fast Cyclic
    Mapper.DeepCopy(acyclicTreeData);  // T8 warmup: Fast Acyclic
    Mapper.DeepCopy(BenchmarkData.DeepCopyMapFromEntityData); // T8 warmup: Generated Acyclic
    Mapper.Map<RecordEntity, RecordDto>(BenchmarkData.RecordEntityData);
    Mapper.Map<NullableEntity, NullableDto>(BenchmarkData.NullableEntityData);
    Mapper.Map<FlattenParentEntity, FlattenDto>(BenchmarkData.FlattenEntityData);
    Mapper.Map<EnumEntity, EnumToStringDto>(BenchmarkData.EnumEntityData);
    Mapper.Map<FlatEntity, FlatDto>(BenchmarkData.FlatEntity);
    Mapper.Map<FlatEntity, FlatDto>(BenchmarkData.FlatEntity, (s, d) => d.Name += "!");
    Mapper.Map<FlatDto, FlatEntity>(BenchmarkData.FlatDto);
    Mapper.Map<RecordBigEntity, RecordBigDto>(BenchmarkData.RecordBigEntityData);
    Mapper.Map<NameDiffBigEntity, NameDiffBigDto>(BenchmarkData.NameDiffBigEntityData);
}
Console.WriteLine("Warmup done.\n");

Console.WriteLine("=== Source Generator Status ===");
Console.WriteLine($"  Flat→Dto:          {Mapper.HasGenerated<FlatEntity, FlatDto>()}");
Console.WriteLine($"  Nested→Dto:        {Mapper.HasGenerated<NestedEntity, NestedDto>()}");
Console.WriteLine($"  Record→Dto:        {Mapper.HasGenerated<RecordEntity, RecordDto>()}");
Console.WriteLine($"  DeepCopyMapFrom:   {Mapper.HasGeneratedDeepCopy<DeepCopyMapFromDto>()}");
Console.WriteLine($"  DeepCopy (Runtime):{Mapper.HasGeneratedDeepCopy<DeepCopyEntity>()}");
Console.WriteLine("================================\n");

var scenarios = new List<BenchScenario>
{
    new("Flat_Map",           () => Mapper.Map<FlatEntity, FlatDto>(BenchmarkData.FlatEntity), false),
    new("Flat_Ext",           () => Mapper.Map<FlatEntity, FlatDto>(BenchmarkData.FlatEntity), false),
    new("Flat_Action",        () => Mapper.Map<FlatEntity, FlatDto>(BenchmarkData.FlatEntity, (s,d) => d.Name += "!"), false),
    new("NameDiff_Map",       () => Mapper.Map<NameDiffEntity, NameDiffDto>(BenchmarkData.NameDiffEntity), false),
    new("Nested_Map",         () => Mapper.Map<NestedEntity, NestedDto>(BenchmarkData.NestedEntity), false),
    new("Nested_Ext",         () => Mapper.Map<NestedEntity, NestedDto>(BenchmarkData.NestedEntity), false),
    new("Record_Map",         () => Mapper.Map<RecordEntity, RecordDto>(BenchmarkData.RecordEntityData), false),
    new("Nullable_Map",       () => Mapper.Map<NullableEntity, NullableDto>(BenchmarkData.NullableEntityData), false),
    new("AutoFlatten_Map",    () => Mapper.Map<FlattenParentEntity, FlattenDto>(BenchmarkData.FlattenEntityData), false),
    new("EnumString_Map",     () => Mapper.Map<EnumEntity, EnumToStringDto>(BenchmarkData.EnumEntityData), false),
    new("Bidir_DE",           () => Mapper.Map<FlatDto, FlatEntity>(BenchmarkData.FlatDto), false),
    new("RecordBig_Map",      () => Mapper.Map<RecordBigEntity, RecordBigDto>(BenchmarkData.RecordBigEntityData), false),
    new("NameDiffBig_Map",    () => Mapper.Map<NameDiffBigEntity, NameDiffBigDto>(BenchmarkData.NameDiffBigEntityData), false),
    new("Collection_MapList", () => Mapper.MapList<FlatEntity, FlatDto>(BenchmarkData.FlatEntities100), true),
    new("Collection_Ext",     () => Mapper.MapList<FlatEntity, FlatDto>(BenchmarkData.FlatEntities100), true),
    new("DeepCopy_Fast",      () => Mapper.DeepCopy(BenchmarkData.DeepCopyEntityData), false),
    new("DeepCopy_Generated", () => Mapper.DeepCopy(BenchmarkData.DeepCopyMapFromEntityData), false),
    new("DeepCopy_ExprTree",  () => { var s = DeepCopyProviderBase.Current; DeepCopyProviderBase.Current = new ExpressionTreeDeepCopyProvider(); try { Mapper.DeepCopy(BenchmarkData.DeepCopyEntityData); } finally { DeepCopyProviderBase.Current = s; } }, false),
    new("DeepCopy_Fast_LargeGraph", () => Mapper.DeepCopy(largeChainData), false),
    new("DeepCopy_Fast_Circular",   () => Mapper.DeepCopy(circularBenchmarkData), false),
    // T8 新增：三值分类条件分发量化
    new("DeepCopy_Fast_Cyclic",         () => Mapper.DeepCopy(cyclicRingData), false),  // Cyclic 路径（完整检测）
    new("DeepCopy_Fast_Acyclic",        () => Mapper.DeepCopy(acyclicTreeData), false), // Acyclic 零检测快路径
    new("DeepCopy_Generated_Acyclic",   () => Mapper.DeepCopy(BenchmarkData.DeepCopyMapFromEntityData), false), // 源生成 Acyclic 变体
};

const int singleIter = 2_000_000;  // 增加到 200 万次
const int collIter = 20_000;
const int rounds = 10;  // 10 轮

Console.WriteLine($"Iterations: {singleIter:N0} (single) / {collIter:N0} (collection) | Rounds: {rounds} (discard first 2)");
Console.WriteLine();
Console.WriteLine($"{"Scenario",-25} {"R1",-9} {"R2",-9} {"R3",-9} {"R4",-9} {"R5",-9} {"R6",-9} {"R7",-9} {"R8",-9} {"P50",-9} {"Avg",-9} {"Min",-9}");
Console.WriteLine(new string('-', 120));

var results = new List<(string name, double p50, double avg, double min)>();

foreach (var scenario in scenarios)
{
    var iter = scenario.IsCollection ? collIter : singleIter;
    var allResults = new double[rounds];

    for (int r = 0; r < rounds; r++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iter; i++)
            scenario.Work();
        sw.Stop();
        allResults[r] = (double)sw.ElapsedTicks / iter * 1_000_000_000.0 / Stopwatch.Frequency;
    }

    // 丢弃前 2 轮（JIT 热身），取剩余 8 轮统计
    var valid = allResults.Skip(2).ToArray();
    var avg = valid.Average();
    var min = valid.Min();
    var sorted = valid.OrderBy(x => x).ToArray();
    var p50 = sorted[sorted.Length / 2]; // 中位数

    results.Add((scenario.Name, p50, avg, min));

    Console.WriteLine($"{scenario.Name,-25} {allResults[0],-9:F1} {allResults[1],-9:F1} {allResults[2],-9:F1} {allResults[3],-9:F1} {allResults[4],-9:F1} {allResults[5],-9:F1} {allResults[6],-9:F1} {allResults[7],-9:F1} {p50,-9:F1} {avg,-9:F1} {min,-9:F1}");
}

Console.WriteLine();
Console.WriteLine("=== Summary (P50 ns/op, sorted) ===");
foreach (var (name, p50, avg, min) in results.OrderBy(r => r.p50))
    Console.WriteLine($"  {name,-25} P50:{p50,8:F1}  Avg:{avg,8:F1}  Min:{min,8:F1}");

/// <summary>
/// T8 辅助：构建 N 节点 Acyclic 二叉树（层序填充）
/// </summary>
static TreeNode BuildAcyclicTree(int n)
{
    var root = new TreeNode { Value = 0 };
    var queue = new Queue<TreeNode>();
    queue.Enqueue(root);
    for (int i = 1; i < n; i++)
    {
        var parent = queue.Peek();
        var child = new TreeNode { Value = i };
        if (parent.Left == null) parent.Left = child;
        else { parent.Right = child; queue.Dequeue(); }
        queue.Enqueue(child);
    }
    return root;
}

record BenchScenario(string Name, Action Work, bool IsCollection);

/// <summary>
/// 循环引用基准测试节点 — Self 指向自身（环路径节点数=1）
/// </summary>
public class CircularBenchmarkNode
{
    /// <summary>
    /// 节点值
    /// </summary>
    public int Value { get; set; }

    /// <summary>
    /// 自引用（指向自身形成循环）
    /// </summary>
    public CircularBenchmarkNode? Self { get; set; }
}

/// <summary>
/// 大对象图基准测试节点 — 链表式 100 节点（无循环）
/// </summary>
public class ChainNode
{
    /// <summary>
    /// 节点值
    /// </summary>
    public int Value { get; set; }

    /// <summary>
    /// 下一个节点（链表式，尾节点为 null）
    /// </summary>
    public ChainNode? Next { get; set; }
}

/// <summary>
/// T8 新增：环形基准测试节点 — 20 节点单向成环（Fast Cyclic 路径验证）
/// </summary>
public class RingNode
{
    /// <summary>节点值</summary>
    public int Value { get; set; }

    /// <summary>下一个节点（成环后形成循环引用）</summary>
    public RingNode? Next { get; set; }
}

/// <summary>
/// T8 新增：Acyclic 树节点 — 二叉树结构（零检测快路径量化）
/// </summary>
public class TreeNode
{
    /// <summary>节点值</summary>
    public int Value { get; set; }

    /// <summary>左子树</summary>
    public TreeNode? Left { get; set; }

    /// <summary>右子树</summary>
    public TreeNode? Right { get; set; }
}
