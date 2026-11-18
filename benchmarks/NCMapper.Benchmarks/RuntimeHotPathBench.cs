using System.Collections.Concurrent;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using NCMapper;
using NCMapper.EFCore;

namespace NCMapper.Benchmarks;

/// <summary>
/// 运行期热路径基准。
/// <para>为运行期性能条目提供"改造前"基线。
/// 输入形态按对照表逐行落地；GlobalSetup 内置探针守门（数据链路不通即抛错，防假基准，
/// 参见 SourceGenDriverBench 先例）。</para>
/// <para>纪律约束：本文件所有模型类不得标注 [MapFrom]/[MapperConfig] 等会触发源生成器发射的
/// 特性（夹具仅用 [MapTree]，生成器对其零发射）——保证 generated-baseline 字节门一致；
/// 编译期夹具（source 串形态）见 <see cref="SourceGenDriverBench"/> 嵌套类。</para>
/// <para>入口：<c>dotnet run -c Release -f net10.0 --project benchmarks/NCMapper.Benchmarks -- RuntimeHotPathBench</c>
/// （经 Program.cs 通用类名分发，走 10s 进程预热后进入 BDN）。</para>
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 15)]
public class RuntimeHotPathBench
{
    // ================================================================
    // MapPipelinePostMap —— Pipeline 启用后每次 Map 对"未注册类型对"的负查找
    // （MapPipeline.cs TryGetReversed :37-50：负结果不缓存 → lock + 双字典查找每 Map 一次）
    // ================================================================

    public sealed class PostMapAnchorEntity { public int Id { get; set; } }
    public sealed class PostMapAnchorDto { public int Id { get; set; } }
    public sealed class NegMissEntity { public int Id { get; set; } = 1; public string Name { get; set; } = "neg"; }
    public sealed class NegMissDto { public int Id { get; set; } public string Name { get; set; } = ""; }

    private NegMissEntity _negSource = null!;

    /// <summary>Pipeline 启用守门：仅在 MapPipelinePostMap 首次（warmup 迭代）惰性执行，
    /// 避免共享 GlobalSetup 把 Pipeline 开启泄漏到同类其他基准（否则其余基准的 Map 全部多付负查找成本，基线被污染）。</summary>
    private bool _postMapEnabled;

    [Benchmark]
    public NegMissDto MapPipelinePostMap()
    {
        if (!_postMapEnabled)
        {
            Mapper.Pipeline.AddPostMap<PostMapAnchorEntity, PostMapAnchorDto>((s, t) => t.Id = s.Id);
            _postMapEnabled = true;
        }
        return Mapper.Map<NegMissEntity, NegMissDto>(_negSource)!;
    }

    // ================================================================
    // ConvertValueBulk —— IConvertible 异常作控制流
    // （TypeConvertHelper.cs ConvertValue :40-64：string→枚举/Guid、脏串→数值、溢出均先抛异常再捕获续走）
    // ================================================================

    public enum ConvOrderStatus { Created = 1, Shipped = 2, Delivered = 3 }

    private string[] _enumValid = null!;      // string→枚举：ChangeType 抛 InvalidCastException → Enum.Parse 成功
    private string[] _enumDirty = null!;      // string→枚举：全路径失败 → MapException（异常面本体）
    private string[] _guidValid = null!;      // string→Guid：ChangeType 抛异常 → Guid.TryParse 成功
    private string[] _intOverflow = null!;    // string→int：ChangeType 抛 OverflowException → TryParse 失败 → MapException
    private string[] _intValid = null!;       // 对照组：IConvertible 无异常直达

    [Benchmark]
    public int ConvertValueBulk()
    {
        var sum = 0;
        var caught = 0;
        for (var i = 0; i < _enumValid.Length; i++)
            sum += (int)TypeConvertHelper.ConvertValue<string, ConvOrderStatus>(_enumValid[i])!;
        for (var i = 0; i < _enumDirty.Length; i++)
        {
            try { TypeConvertHelper.ConvertValue<string, ConvOrderStatus>(_enumDirty[i]); }
            catch (MapException) { caught++; }
        }
        for (var i = 0; i < _guidValid.Length; i++)
            sum += TypeConvertHelper.ConvertValue<string, Guid>(_guidValid[i]).GetHashCode();
        for (var i = 0; i < _intOverflow.Length; i++)
        {
            try { TypeConvertHelper.ConvertValue<string, int>(_intOverflow[i]); }
            catch (MapException) { caught++; }
        }
        for (var i = 0; i < _intValid.Length; i++)
            sum += TypeConvertHelper.ConvertValue<string, int>(_intValid[i]);
        return sum + caught;
    }

    // ================================================================
    // CollectionCopy —— 元素直调内核（List/HashSet/Queue/Stack/ConcurrentQueue/Dictionary 六通道）
    // （CollectionCopier.cs DeepCopyList :111-133 / CopyHashSet :437 / CopyQueue :457 / CopyStack :483
    //   / CopyConcurrentQueue :415 / CopyDictionary :275-276；含 10% 派生实例 → :128 多态回退路径）
    // ================================================================

    public class CollItem { public int Id { get; set; } public string Name { get; set; } = ""; }
    public sealed class CollItemDerived : CollItem { public string Extra { get; set; } = "d"; }

    public sealed class CollectionHostEntity
    {
        public int Id { get; set; }
        public List<CollItem> Items { get; set; } = new();
        public HashSet<CollItem> Set { get; set; } = new();
        public Queue<CollItem> Queue { get; set; } = new();
        public Stack<CollItem> Stack { get; set; } = new();
        public ConcurrentQueue<CollItem> Concurrent { get; set; } = new();
        public Dictionary<string, CollItem> Map { get; set; } = new();
    }

    private CollectionHostEntity _collHost = null!;

    [Benchmark]
    public CollectionHostEntity CollectionCopy() => Mapper.DeepCopy(_collHost)!;

    // ================================================================
    // FastPathAcyclic —— Acyclic 快路径固定扫 8 槽（FastDeepCopyProvider.cs :115-128）
    // sealed 链（不可被 override）保证 DeepCopyTypeClassifier 判 Acyclic；链深 4 ≤ FastRingSize 8
    // ================================================================

    public sealed class FastL1 { public int A { get; set; } public FastL2? Next { get; set; } }
    public sealed class FastL2 { public string B { get; set; } = ""; public FastL3? Next { get; set; } }
    public sealed class FastL3 { public long C { get; set; } }
    public sealed class FastAcyclicRoot { public int Id { get; set; } public FastL1? Head { get; set; } }

    private FastAcyclicRoot _fastRoot = null!;

    [Benchmark]
    public FastAcyclicRoot FastPathAcyclic() => Mapper.DeepCopy(_fastRoot)!;

    // ================================================================
    // DictValueMapperMixed —— s_dictValueMapperCache 无界缓存的稳态查找成本
    // （CollectionCopier.cs :555 / GetDictValueMapper GetOrAdd+Compile :637-651；
    //   触发通道 = Mapper.Map 跨类型字典属性（ExpressionBuilder.Collections.cs :478-516），
    //   值端为引用类型对：8 种运行时派生类型 × 目标 MixDto → 8 条缓存条目）
    // ================================================================

    public class MixBase { public int Id { get; set; } public string Name { get; set; } = ""; }
    public sealed class MixA : MixBase { public int Va { get; set; } }
    public sealed class MixB : MixBase { public int Vb { get; set; } }
    public sealed class MixC : MixBase { public int Vc { get; set; } }
    public sealed class MixD : MixBase { public int Vd { get; set; } }
    public sealed class MixE : MixBase { public int Ve { get; set; } }
    public sealed class MixF : MixBase { public int Vf { get; set; } }
    public sealed class MixG : MixBase { public int Vg { get; set; } }
    public sealed class MixH : MixBase { public int Vh { get; set; } }
    public sealed class MixDto { public int Id { get; set; } public string Name { get; set; } = ""; }

    public sealed class DictMixedEntity { public int Id { get; set; } public Dictionary<string, MixBase> Data { get; set; } = new(); }
    public sealed class DictMixedDto { public int Id { get; set; } public Dictionary<string, MixDto> Data { get; set; } = new(); }

    private DictMixedEntity _dictSource = null!;

    [Benchmark]
    public DictMixedDto DictValueMapperMixed() => Mapper.Map<DictMixedEntity, DictMixedDto>(_dictSource)!;

    // ================================================================
    // ClearTreeChildren —— 树挂载位防御清空逐元素反射读写装箱
    // （NCMapperEFCoreExtensions.cs ClearTreeChildrenSlot :173-192，:186-191 逐元素 GetValue/SetValue）
    // 通道 = ToTreeList 降级路径：LINQ-to-Objects IQueryable + MapList 导航预填（父行 Children 非空）
    //   → ClearTreeChildrenSlot 逐行清空 → TreeBuilder.Build 按 Id/ParentId 挂载。
    //   EFCoreProjectionAsNoTrackingDefault=false（:105 直返原 query）使 LINQ-to-Objects 免 EF Provider。
    // ================================================================

    public sealed class TreeEntity
    {
        public int Id { get; set; }
        public int ParentId { get; set; }
        public string Name { get; set; } = "";
        public List<TreeEntity> Children { get; set; } = new();
    }

    public sealed class TreeDto
    {
        public int Id { get; set; }
        public int ParentId { get; set; }
        public string Name { get; set; } = "";
        public List<TreeDto> Children { get; set; } = new();
    }

    private List<TreeEntity> _treeRows = null!;
    private bool _treeDegradeArmed;

    /// <summary>
    /// 通道强制降级：无 [MapFrom] 时 ToTreeList 的 project 通道会抛
    /// "cannot use Projection"；按 DegradationDetectorBase.Agnostic :98 的 PostMap 检测，
    /// 注册 (TreeEntity, TreeDto) PostMap 使 NeedsDegradation=true → 走 MapList 降级通道
    /// （MapList 导航预填 → ClearTreeChildrenSlot 清空 → TreeBuilder 挂载），零 src 改动。
    /// </summary>
    private void EnsureTreeDegradeArmed()
    {
        if (!_treeDegradeArmed)
        {
            Mapper.Pipeline.AddPostMap<TreeEntity, TreeDto>((s, t) => { });
            _treeDegradeArmed = true;
        }
    }

    [Benchmark]
    public List<TreeDto> ClearTreeChildren()
    {
        EnsureTreeDegradeArmed();
        return _treeRows.AsQueryable().AsDto().ToTreeList<TreeDto>();
    }

    // ================================================================
    // 数据构建 + 探针守门（GlobalSetup 每基准用例各跑一次，耗时不含在度量内）
    // ================================================================

    [GlobalSetup]
    public void Setup()
    {
        // MapPipelinePostMap
        _negSource = new NegMissEntity();
        var negDto = Mapper.Map<NegMissEntity, NegMissDto>(_negSource);
        if (negDto is null || negDto.Id != 1 || negDto.Name != "neg")
            throw new InvalidOperationException("RuntimeHotPathBench/MapPipelinePostMap probe failed: Map result mismatch.");

        // ConvertValueBulk
        _enumValid = BuildArray(64, i => ((ConvOrderStatus)(i % 3 + 1)).ToString());
        _enumDirty = BuildArray(64, i => $"BAD_{i}");
        _guidValid = BuildArray(64, i => new Guid(i, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0).ToString());
        _intOverflow = BuildArray(64, _ => "99999999999999999999");
        _intValid = BuildArray(64, i => (10000 + i).ToString());
        var convChecksum = 0;
        for (var i = 0; i < _enumValid.Length; i++)
            convChecksum += (int)TypeConvertHelper.ConvertValue<string, ConvOrderStatus>(_enumValid[i])!;
        for (var i = 0; i < _guidValid.Length; i++)
            convChecksum += TypeConvertHelper.ConvertValue<string, Guid>(_guidValid[i]).GetHashCode();
        for (var i = 0; i < _intValid.Length; i++)
            convChecksum += TypeConvertHelper.ConvertValue<string, int>(_intValid[i]);
        var dirtyCaught = 0;
        for (var i = 0; i < _enumDirty.Length; i++) { try { TypeConvertHelper.ConvertValue<string, ConvOrderStatus>(_enumDirty[i]); } catch (MapException) { dirtyCaught++; } }
        var overflowCaught = 0;
        for (var i = 0; i < _intOverflow.Length; i++) { try { TypeConvertHelper.ConvertValue<string, int>(_intOverflow[i]); } catch (MapException) { overflowCaught++; } }
        if (convChecksum <= 0 || dirtyCaught != 64 || overflowCaught != 64)
            throw new InvalidOperationException(
                $"RuntimeHotPathBench/ConvertValueBulk probe failed: checksum={convChecksum}, dirtyCaught={dirtyCaught}, overflowCaught={overflowCaught}.");

        // CollectionCopy（1000 元素/通道，10% 派生实例）
        _collHost = BuildCollectionHost(1000);
        var collCopy = Mapper.DeepCopy(_collHost);
        if (collCopy is null
            || collCopy.Items.Count != 1000 || collCopy.Set.Count != 1000 || collCopy.Queue.Count != 1000
            || collCopy.Stack.Count != 1000 || collCopy.Concurrent.Count != 1000 || collCopy.Map.Count != 1000
            || collCopy.Items[0].Name != _collHost.Items[0].Name)
            throw new InvalidOperationException("RuntimeHotPathBench/CollectionCopy probe failed: collection copy shape mismatch.");

        // FastPathAcyclic
        _fastRoot = new FastAcyclicRoot { Id = 7, Head = new FastL1 { A = 1, Next = new FastL2 { B = "b", Next = new FastL3 { C = 3 } } } };
        var fastCopy = Mapper.DeepCopy(_fastRoot);
        if (fastCopy is null || fastCopy.Head?.Next?.Next?.C != 3 || fastCopy.Head.A != 1)
            throw new InvalidOperationException("RuntimeHotPathBench/FastPathAcyclic probe failed: acyclic copy mismatch.");

        // DictValueMapperMixed（500 条目 × 8 种运行时值类型；先跑一次 Map 预热编译缓存，稳态测查找+委托调用）
        _dictSource = BuildDictSource(500);
        var dictDto = Mapper.Map<DictMixedEntity, DictMixedDto>(_dictSource);
        if (dictDto is null || dictDto.Data.Count != 500 || dictDto.Data["k0"].Name != _dictSource.Data["k0"].Name)
            throw new InvalidOperationException("RuntimeHotPathBench/DictValueMapperMixed probe failed: dict mapping shape mismatch.");

        // ClearTreeChildren（100 棵树 × (1 父 + 9 子) = 1000 平铺行；父行 Children 导航非空 → MapList 预填 → 清空热路径）
        _treeRows = BuildTreeRows(100, 9);
        NCMapperEFCoreExtensions.EFCoreProjectionAsNoTrackingDefault = false;
        EnsureTreeDegradeArmed();
        // 预填探针：MapList 必须把父行 Children 导航填满（否则 ClearTreeChildrenSlot 零操作，夹具 vacuous）
        var flatMapped = Mapper.MapList<TreeEntity, TreeDto>(_treeRows);
        var prefillParents = 0;
        foreach (var dto in flatMapped!)
            if (dto.Children.Count > 0) prefillParents++;
        if (prefillParents != 100)
            throw new InvalidOperationException(
                $"RuntimeHotPathBench/ClearTreeChildren probe failed: expected 100 prefilled parents, got {prefillParents} (clear slot would be vacuous).");
        // 端到端探针：树构建 + 清空链路连通
        var tree = _treeRows.AsQueryable().AsDto().ToTreeList<TreeDto>();
        var totalNodes = CountNodes(tree);
        if (tree.Count != 100 || totalNodes != 1000)
            throw new InvalidOperationException(
                $"RuntimeHotPathBench/ClearTreeChildren probe failed: roots={tree.Count}, totalNodes={totalNodes} (expected 100/1000).");

        Console.WriteLine(
            "[RuntimeHotPathBench] probes OK: postmap-neg / convert(64+64+64+64+64) / collections 6x1000 / fast-chain / dict 500x8 / tree 100x9");
    }

    // ---------------- 数据构建器 ----------------

    private static T[] BuildArray<T>(int count, Func<int, T> factory)
    {
        var arr = new T[count];
        for (var i = 0; i < count; i++) arr[i] = factory(i);
        return arr;
    }

    private static CollectionHostEntity BuildCollectionHost(int perCollection)
    {
        var host = new CollectionHostEntity { Id = 1 };
        for (var i = 0; i < perCollection; i++)
        {
            CollItem item = i % 10 == 0 ? new CollItemDerived { Id = i, Name = $"n{i}" } : new CollItem { Id = i, Name = $"n{i}" };
            host.Items.Add(item);
            host.Set.Add(item);
            host.Queue.Enqueue(item);
            host.Stack.Push(item);
            host.Concurrent.Enqueue(item);
            host.Map[$"k{i}"] = item;
        }
        return host;
    }

    private static DictMixedEntity BuildDictSource(int count)
    {
        var entity = new DictMixedEntity { Id = 1 };
        for (var i = 0; i < count; i++)
        {
            MixBase value = (i % 8) switch
            {
                0 => new MixA { Id = i, Name = $"v{i}", Va = i },
                1 => new MixB { Id = i, Name = $"v{i}", Vb = i },
                2 => new MixC { Id = i, Name = $"v{i}", Vc = i },
                3 => new MixD { Id = i, Name = $"v{i}", Vd = i },
                4 => new MixE { Id = i, Name = $"v{i}", Ve = i },
                5 => new MixF { Id = i, Name = $"v{i}", Vf = i },
                6 => new MixG { Id = i, Name = $"v{i}", Vg = i },
                _ => new MixH { Id = i, Name = $"v{i}", Vh = i },
            };
            entity.Data[$"k{i}"] = value;
        }
        return entity;
    }

    private static List<TreeEntity> BuildTreeRows(int treeCount, int childrenPerTree)
    {
        var rows = new List<TreeEntity>(treeCount * (1 + childrenPerTree));
        for (var t = 0; t < treeCount; t++)
        {
            var parent = new TreeEntity { Id = t + 1, ParentId = 0, Name = $"p{t}" };
            for (var c = 0; c < childrenPerTree; c++)
            {
                var child = new TreeEntity { Id = treeCount + t * childrenPerTree + c + 1, ParentId = parent.Id, Name = $"c{t}_{c}" };
                parent.Children.Add(child);
                rows.Add(child);
            }
            rows.Add(parent);
        }
        return rows;
    }

    private static int CountNodes(IEnumerable<TreeDto> roots)
    {
        var total = 0;
        var stack = new Stack<TreeDto>(roots);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            total++;
            foreach (var child in node.Children) stack.Push(child);
        }
        return total;
    }
}
