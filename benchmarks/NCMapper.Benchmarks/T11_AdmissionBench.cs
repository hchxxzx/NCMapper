using BenchmarkDotNet.Attributes;
using NCMapper.Attributes;
using NCMapper.Benchmarks.Models;

namespace NCMapper.Benchmarks;

// ================================================================
// T11 准入裁决基准
// 性能/分配/构建期类基准（正确性/计数类见
// test/NCMapper.Tests/Tests/T11AdmissionProbeTests.cs 探针）。
// 运行：dotnet run -c Release -- T11（Program.cs 前缀匹配 T11_* 基准类）。
// 判据：运行时吞吐类 ≥30% 降幅 /
// 构建期耗时·分配量类按预期值（分配 50%+、耗时 30%+）。
// 噪声控制：项目口径 5 launch × 3 warmup × 15 iteration，取中位数（≥3 轮）。
// ================================================================

// ---------- 模型（T11 专用，勿与其他场景混用） ----------

/// <summary>场景源：3 个 string 字段（手机/证件/卡号 —— 纯 ASCII 常态输入）</summary>
public class T11MaskSource
{
    public string Phone { get; set; } = "13812345678";
    public string IdCard { get; set; } = "110101199001011234";
    public string BankCard { get; set; } = "6222021234561234";
}

/// <summary>场景目标：3 × [Mask]（[MapFrom] 生成路径；StringInfo 成本在 MaskMiddle 内部，与引擎路径无关）</summary>
[MapFrom(typeof(T11MaskSource))]
public class T11MaskDto
{
    [Mask(typeof(MaskPhone))] public string Phone { get; set; } = "";
    [Mask(typeof(MaskIdCard))] public string IdCard { get; set; } = "";
    [Mask(typeof(MaskBankCard))] public string BankCard { get; set; } = "";
}

/// <summary>场景元素：引用类型 POCO（深拷贝需逐元素独立副本）</summary>
public class T11RefItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public T11RefItem(int id, string name) { Id = id; Name = name; }
}

/// <summary>场景：同型嵌套对（30 属性 —— 让构建成本显著，运行时表达式路径 TryGetInlineMapBody）</summary>
public class T11NestedChild
{
    public string P00 { get; set; } = "a"; public string P01 { get; set; } = "b";
    public string P02 { get; set; } = "c"; public string P03 { get; set; } = "d";
    public string P04 { get; set; } = "e"; public string P05 { get; set; } = "f";
    public string P06 { get; set; } = "g"; public string P07 { get; set; } = "h";
    public string P08 { get; set; } = "i"; public string P09 { get; set; } = "j";
    public string P10 { get; set; } = "k"; public string P11 { get; set; } = "l";
    public string P12 { get; set; } = "m"; public string P13 { get; set; } = "n";
    public string P14 { get; set; } = "o"; public string P15 { get; set; } = "p";
    public string P16 { get; set; } = "q"; public string P17 { get; set; } = "r";
    public string P18 { get; set; } = "s"; public string P19 { get; set; } = "t";
    public string P20 { get; set; } = "u"; public string P21 { get; set; } = "v";
    public string P22 { get; set; } = "w"; public string P23 { get; set; } = "x";
    public string P24 { get; set; } = "y"; public string P25 { get; set; } = "z";
    public string P26 { get; set; } = "A"; public string P27 { get; set; } = "B";
    public string P28 { get; set; } = "C"; public string P29 { get; set; } = "D";
}

/// <summary>单嵌套对照（同型嵌套对出现 1 次）—— 无 [MapFrom] 走运行时表达式构建</summary>
public class T11NestedOnceSource { public T11NestedChild? Child { get; set; } }
public class T11NestedOnceDto { public T11NestedChild? Child { get; set; } }

/// <summary>双嵌套（同型嵌套对出现 2 次 —— 缺陷：TryGetInlineMapBody 无类型对级缓存，第二次全量重建）</summary>
public class T11NestedTwiceSource
{
    public T11NestedChild? ChildA { get; set; }
    public T11NestedChild? ChildB { get; set; }
}
public class T11NestedTwiceDto
{
    public T11NestedChild? ChildA { get; set; }
    public T11NestedChild? ChildB { get; set; }
}

// ---------- 共享数据 ----------

internal static class T11Data
{
    public static readonly List<T11MaskSource> MaskSources1000
        = Enumerable.Range(1, 1000).Select(i => new T11MaskSource()).ToList();

    public static readonly List<T11RefItem> RefItems100
        = Enumerable.Range(1, 100).Select(i => new T11RefItem(i, $"N{i}")).ToList();

    public static readonly T11RefItem[] RefItemArray100 = RefItems100.ToArray();

    public static readonly T11NestedChild NestedChild = new();

    public static readonly T11NestedOnceSource NestedOnce = new() { Child = NestedChild };
    public static readonly T11NestedTwiceSource NestedTwice = new() { ChildA = NestedChild, ChildB = NestedChild };
}

// ---------- 手写对照（强类型 copy，理论最优） ----------

internal static class T11Manual
{
    public static List<T11RefItem> CopyList(List<T11RefItem> src)
    {
        var list = new List<T11RefItem>(src.Count);
        foreach (var it in src) list.Add(new T11RefItem(it.Id, it.Name));
        return list;
    }

    public static T11RefItem[] CopyArray(T11RefItem[] src)
    {
        var arr = new T11RefItem[src.Length];
        for (int i = 0; i < src.Length; i++) arr[i] = new T11RefItem(src[i].Id, src[i].Name);
        return arr;
    }
}

// ================================================================
// MapTracer 关闭态 DeepCopy 带 action 插值白分配
// 度量点：MemoryDiagnoser 对比修复前后 Trace 关闭时分配归零（~260B+/次）
// ================================================================

// 带 action 路径（Mapper.cs:1150 插值点 —— 每次 DeepCopy+action 必经，GloballyEnabled 默认 false 时纯白分配）
[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class T11_S03_DeepCopyWithAction
{
    [Benchmark] public DeepCopyEntity Run() => NCMapper.Mapper.DeepCopy(BenchmarkData.DeepCopyEntityData, (s, t) => { })!;
}

// 无 action 对照（同数据同路径，不含插值点）
[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class T11_S03_DeepCopyNoAction
{
    [Benchmark] public DeepCopyEntity Run() => NCMapper.Mapper.DeepCopy(BenchmarkData.DeepCopyEntityData)!;
}

// ================================================================
// Fast 环扫描固定全 8 槽 —— 浅图空扫成本隔离
// 度量点：浅图微基准（n=Math.Min(FastSlot,8) 后扫描量降为有效祖先链长度）
// ================================================================

// Fast 浅图深拷贝总耗时（DeepCopyEntity：Child 1 层 + Tags —— 深度 <8，环扫描空扫 5-8 槽/节点）
[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class T11_S06_FastDeepCopyShallow
{
    [Benchmark] public DeepCopyEntity Run() => NCMapper.Mapper.DeepCopy(BenchmarkData.DeepCopyEntityData)!;
}

// 环扫描本体微基准（FastDeepCopyProvider.cs:121 循环形态 —— 8 次 ReferenceEquals 数组探测）
[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class T11_S06_RingScanLoop8
{
    private readonly object?[] _slots = new object?[8];
    private readonly object _probe = new();

    [Benchmark]
    public int Run()
    {
        int hit = -1;
        for (int i = 0; i < 8; i++)
            if (ReferenceEquals(_slots[i], _probe)) { hit = i; break; }
        return hit;
    }
}

// ================================================================
// 集合深拷贝热路径反射（ctor.Invoke / Activator / GetValue/SetValue）
// 度量点：BenchmarkDotNet before/after + 深拷贝全量等价（数组路径 GetValue/SetValue 比强类型慢 5-10×）
// ================================================================

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class T11_S13_ListDeepCopy
{
    [Benchmark] public List<T11RefItem> Run() => NCMapper.Mapper.DeepCopy(T11Data.RefItems100)!;
}

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class T11_S13_ListManual
{
    [Benchmark] public List<T11RefItem> Run() => T11Manual.CopyList(T11Data.RefItems100);
}

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class T11_S13_ArrayDeepCopy
{
    [Benchmark] public T11RefItem[] Run() => NCMapper.Mapper.DeepCopy(T11Data.RefItemArray100)!;
}

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class T11_S13_ArrayManual
{
    [Benchmark] public T11RefItem[] Run() => T11Manual.CopyArray(T11Data.RefItemArray100);
}

// ================================================================
// TryGetInlineMapBody 无类型对级缓存 —— 同型嵌套重复全量构建（构建期耗时）
// 度量点：2 个同型嵌套属性类型对 BuildMapBody(嵌套对) 调用计数为 1（现状 2）
// 每次 iteration 含 ClearCache + 全量构建（两场景同扣 ClearCache 基线，差值 = 重复构建成本）
// ================================================================

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class T11_S20_BuildNestedOnce
{
    [Benchmark]
    public T11NestedOnceDto Run()
    {
        NCMapper.Mapper.ClearCache();
        return NCMapper.Mapper.Map<T11NestedOnceSource, T11NestedOnceDto>(T11Data.NestedOnce)!;
    }
}

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class T11_S20_BuildNestedTwice
{
    [Benchmark]
    public T11NestedTwiceDto Run()
    {
        NCMapper.Mapper.ClearCache();
        return NCMapper.Mapper.Map<T11NestedTwiceSource, T11NestedTwiceDto>(T11Data.NestedTwice)!;
    }
}

// ================================================================
// 掩码热路径 3×StringInfo 重复构造（分配基线）
// 度量点：MapList 1000 行×3 Mask 字段基准分配降 50%+（修复后复测对照本基线）
// ================================================================

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class T11_S26_MapListMask1000
{
    [Benchmark] public List<T11MaskDto> Run() => NCMapper.Mapper.MapList<T11MaskSource, T11MaskDto>(T11Data.MaskSources1000)!;
}
