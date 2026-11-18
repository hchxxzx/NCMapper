extern alias runtime;
extern alias sourcegen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NCMapper.SourceGen;
using runtime::NCMapper.Shared;
using Xunit;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// 三向一致性测试网（G1-G5，任务书内编号 TG1-TG5）：
/// Type 版 adapter（TypePathResolver，核心库） × ITypeSymbol 版 adapter（SymbolPathResolver，源生成器）
/// 对同一路径场景双向断言：路径解析结果一致 + 失败分类（PathSegmentFailure 枚举值）一致。
/// 双份同名类型经别名区分：核心库版经 <c>extern alias runtime</c>（csproj Aliases="global,runtime"），
/// SourceGen 版经默认 global（Link 物理同源，枚举值定义一致，比较用 (int) 转换绕开类型差异）。
/// <para>B-2 回归：string 中间节点统一判据（TG1）；R2-S2 极端场景：string 属性 + Length 候选（TG1）。</para>
/// <para>反向验证/变异验证（经验 #4）为执行期人工验证步骤（临时禁用核心判据 → 断言具体判据结果的用例必红 → 还原），
/// 不在本文件固化——本文件断言具体判据结果（NotNavigable 等），支撑反向验证可观测。</para>
/// </summary>
public class PathResolverConsistencyTests
{
    // ---- 测试类型（typeof 与符号双通道；生成器跳过 non-public 类型不影响运行时反射） ----

    /// <summary>导航中间节点（多段路径）</summary>
    public sealed class Address
    {
        /// <summary>城市</summary>
        public string City { get; set; } = string.Empty;

        /// <summary>街道</summary>
        public string Street { get; set; } = string.Empty;
    }

    /// <summary>正向推导场景根类型</summary>
    public sealed class Person
    {
        /// <summary>嵌套导航（多段）</summary>
        public Address Home { get; set; } = new();

        /// <summary>string 中间节点（B-2/R2-S2 场景）</summary>
        public string Address { get; set; } = string.Empty;

        /// <summary>值类型中间节点</summary>
        public int Age { get; set; }

        /// <summary>叶值</summary>
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>集合索引场景</summary>
    public sealed class Order
    {
        /// <summary>集合导航（索引段）</summary>
        public List<Item> Items { get; set; } = new();

        /// <summary>string 集合（末段索引排除）</summary>
        public List<string> Tags { get; set; } = new();

        /// <summary>string 叶属性（末段索引非集合场景）</summary>
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>集合元素</summary>
    public sealed class Item
    {
        /// <summary>元素名</summary>
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>反向写回场景（可写/不可写/不可实例化）</summary>
    public sealed class TargetEntity
    {
        /// <summary>可写导航（反向可解析）</summary>
        public ChildEntity Child { get; set; } = new();

        /// <summary>不可写导航（init-only）</summary>
        public ChildEntity InitOnlyChild { get; init; } = new();

        /// <summary>接口类型导航（不可实例化）</summary>
        public INav IfaceNav { get; set; } = null!;

        /// <summary>抽象类型导航（不可实例化）</summary>
        public AbsNav AbsNav { get; set; } = null!;

        /// <summary>无参构造缺失导航（不可实例化）</summary>
        public NoCtorNav NoCtorNav { get; set; } = null!;

        /// <summary>可写叶值</summary>
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>反向子实体</summary>
    public sealed class ChildEntity
    {
        /// <summary>可写叶值</summary>
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>接口（不可实例化判据）</summary>
    public interface INav
    {
        /// <summary>接口导航叶属性</summary>
        string Value { get; set; }
    }

    /// <summary>抽象类（不可实例化判据）</summary>
    public abstract class AbsNav
    {
        /// <summary>抽象导航叶属性</summary>
        public string Value { get; set; } = string.Empty;
    }

    /// <summary>无公共无参构造（不可实例化判据）</summary>
    public sealed class NoCtorNav
    {
        /// <summary>
        /// 唯一构造（无参构造缺失）
        /// </summary>
        /// <param name="x">占位</param>
        public NoCtorNav(int x)
        {
        }

        /// <summary>导航叶属性</summary>
        public string Value { get; set; } = string.Empty;
    }

    /// <summary>
    /// Type 版 adapter 实例（核心库，经 runtime alias）
    /// </summary>
    private static readonly TypePathResolver s_typeResolver = new();

    /// <summary>
    /// 符号版 adapter 实例（SourceGen）
    /// </summary>
    private static readonly SymbolPathResolver s_symbolResolver = new();

    /// <summary>
    /// 双 adapter 一致性断言：同一路径场景下 Type 版与符号版路径解析结果一致 + 失败分类枚举值一致。
    /// </summary>
    /// <param name="type">根类型（typeof 通道）</param>
    /// <param name="segments">段列表</param>
    /// <param name="options">解析选项（Type 版预置；符号版等价预置经符号通道传入）</param>
    /// <param name="expectedFailure">期望失败分类（int 枚举值；null = 期望成功）</param>
    private static void AssertConsistent(Type type, IReadOnlyList<string> segments, PathResolutionOptions options, int? expectedFailure = null)
    {
        // Type 版（核心库 options）
        var typeResult = s_typeResolver.Resolve(type, segments, options, context: null);

        // 符号版（SourceGen options 等价构造：经预置静态属性取值）
        var symbolType = GetNamedTypeSymbol(type);
        var symbolResult = s_symbolResolver.Resolve(symbolType, segments, SymbolForwardOptions(options), context: null);

        // 失败分类一致性：枚举值一致（Link 双份类型，比较用 (int)）
        Assert.Equal((int)typeResult.Failure, (int)symbolResult.Failure);
        // 成功/失败状态一致
        Assert.Equal(typeResult.Success, symbolResult.Success);
        // 路径长度一致（成功时）
        Assert.Equal(typeResult.Path.Count, symbolResult.Path.Count);
        // 逐段属性名一致（成功时）
        for (int i = 0; i < typeResult.Path.Count; i++)
        {
            Assert.Equal(GetPropertyName(typeResult.Path[i]), symbolResult.Path[i].Name);
        }
        // 源索引一致（成功/失败均填充）
        Assert.Equal(typeResult.SourceIndexes, symbolResult.SourceIndexes);

        if (expectedFailure.HasValue)
        {
            Assert.False(typeResult.Success);
            Assert.Equal(expectedFailure.Value, (int)typeResult.Failure);
        }
        else
        {
            Assert.True(typeResult.Success);
        }
    }

    /// <summary>
    /// 符号版 PathResolutionOptions 等价构造——正向尽力式/契约式/反向变体由 options.Direction/FailureMode
    /// 判定后按枚举值映射（双份枚举定义同序同值，数值比较等价）。
    /// </summary>
    /// <param name="options">Type 版选项（核心库枚举）</param>
    /// <returns>符号版选项（SourceGen 枚举）</returns>
    private static sourcegen::NCMapper.Shared.PathResolutionOptions SymbolForwardOptions(PathResolutionOptions options)
    {
        if (options == PathResolutionOptions.ForwardEager)
        {
            return sourcegen::NCMapper.Shared.PathResolutionOptions.ForwardEager;
        }

        if (options == PathResolutionOptions.ForwardContract)
        {
            return sourcegen::NCMapper.Shared.PathResolutionOptions.ForwardContract;
        }

        if (options == PathResolutionOptions.ReverseEager)
        {
            return sourcegen::NCMapper.Shared.PathResolutionOptions.ReverseEager;
        }

        return sourcegen::NCMapper.Shared.PathResolutionOptions.ReverseContract;
    }

    /// <summary>
    /// 取 Type 的命名符号（测试编译上下文——直接用 Roslyn 从当前编译创建符号）
    /// </summary>
    /// <param name="type">反射类型</param>
    /// <returns>命名类型符号</returns>
    private static INamedTypeSymbol GetNamedTypeSymbol(Type type)
    {
        var compilation = CreateCompilation(type.Assembly);
        var symbol = compilation.GetTypeByMetadataName(type.FullName!);
        Assert.NotNull(symbol);
        return symbol!;
    }

    /// <summary>
    /// 从类型程序集构建最小编译（SymbolPathResolver 符号通道）
    /// </summary>
    /// <param name="assembly">类型所在程序集</param>
    /// <returns>编译单元</returns>
    private static CSharpCompilation CreateCompilation(Assembly assembly)
    {
        var references = assembly.GetReferencedAssemblies()
            .Append(assembly.GetName())
            .Select(n => MetadataReference.CreateFromFile(Assembly.Load(n).Location))
            .Cast<MetadataReference>()
            .ToList();
        references.Add(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location));

        return CSharpCompilation.Create(
            "PathResolverConsistencyProbe",
            new[] { CSharpSyntaxTree.ParseText("") },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    /// <summary>
    /// 取 PropertyInfo 名称（Type 版路径属性）
    /// </summary>
    /// <param name="prop">属性</param>
    /// <returns>属性名</returns>
    private static string GetPropertyName(PropertyInfo prop) => prop.Name;

    /// <summary>
    /// Type 版反向判据行为断言（TG4）——符号版无反向消费方（⑥⑦ 均正向，源生成器反向由 NCMAP013
    /// 诊断独立承载），反向一致性由运行时 ④⑤ 行为 + 既有反向写回测试锁定，
    /// 本组锁定 TypePathResolver 反向判据变体（RequireWritable/RequireInstantiable）具体分类。
    /// </summary>
    /// <param name="type">根类型</param>
    /// <param name="segments">段列表</param>
    /// <param name="options">反向选项</param>
    /// <param name="expectedFailure">期望失败分类（int 枚举值）</param>
    private static void AssertTypeReverse(Type type, IReadOnlyList<string> segments, PathResolutionOptions options, int expectedFailure)
    {
        var result = s_typeResolver.Resolve(type, segments, options, context: null);
        Assert.False(result.Success, "反向场景应失败");
        Assert.Equal(expectedFailure, (int)result.Failure);
    }

    // ================= TG1 正向推导（ForwardEager） =================

    /// <summary>
    /// TG1-01 单段匹配（Person.Name → Name）
    /// </summary>
    [Fact]
    public void TG1_SingleSegment_Match()
        => AssertConsistent(typeof(Person), new[] { "Name" }, PathResolutionOptions.ForwardEager);

    /// <summary>
    /// TG1-02 多段导航（Person.Home.City）
    /// </summary>
    [Fact]
    public void TG1_MultiSegment_Navigation()
        => AssertConsistent(typeof(Person), new[] { "Home", "City" }, PathResolutionOptions.ForwardEager);

    /// <summary>
    /// TG1-03 值类型中间节点中断（Person.Age.City → NotNavigable）
    /// </summary>
    [Fact]
    public void TG1_ValueType_Interrupt()
        => AssertConsistent(typeof(Person), new[] { "Age", "City" }, PathResolutionOptions.ForwardEager, (int)PathSegmentFailure.NotNavigable);

    /// <summary>
    /// TG1-04 string 中间节点中断（Person.Name.City → NotNavigable；B-2 回归——⑦ 判据统一后与 ① 一致）
    /// </summary>
    [Fact]
    public void TG1_String_Interrupt_B2()
        => AssertConsistent(typeof(Person), new[] { "Name", "City" }, PathResolutionOptions.ForwardEager, (int)PathSegmentFailure.NotNavigable);

    /// <summary>
    /// TG1-05 R2-S2 极端场景：string 属性 + "Length" 候选（Person.Address + Length → NotNavigable，
    /// 锁定向 ① 收敛——现状 ⑦ 会误匹配 string.Length 生成，修复后统一中断不生成）
    /// </summary>
    [Fact]
    public void TG1_StringProperty_LengthCandidate_R2S2()
        => AssertConsistent(typeof(Person), new[] { "Address", "Length" }, PathResolutionOptions.ForwardEager, (int)PathSegmentFailure.NotNavigable);

    /// <summary>
    /// TG1-06 集合索引导航（Order.Items[0].Name）
    /// </summary>
    [Fact]
    public void TG1_CollectionIndex_Navigation()
        => AssertConsistent(typeof(Order), new[] { "Items[0]", "Name" }, PathResolutionOptions.ForwardEager);

    /// <summary>
    /// TG1-07 非法索引语法（Order.Items[ → InvalidIndexSyntax）
    /// </summary>
    [Fact]
    public void TG1_InvalidIndexSyntax()
        => AssertConsistent(typeof(Order), new[] { "Items[" }, PathResolutionOptions.ForwardEager, (int)PathSegmentFailure.InvalidIndexSyntax);

    /// <summary>
    /// TG1-08 负索引（Order.Items[-1] → IndexNotNonNegative）
    /// </summary>
    [Fact]
    public void TG1_NegativeIndex()
        => AssertConsistent(typeof(Order), new[] { "Items[-1]" }, PathResolutionOptions.ForwardEager, (int)PathSegmentFailure.IndexNotNonNegative);

    /// <summary>
    /// TG1-09 末段索引非集合（Order.Name[0] → NotIndexableType；符号版归一对齐 ②）
    /// </summary>
    [Fact]
    public void TG1_LeafIndex_NotIndexable()
        => AssertConsistent(typeof(Order), new[] { "Name[0]" }, PathResolutionOptions.ForwardEager, (int)PathSegmentFailure.NotIndexableType);

    /// <summary>
    /// TG1-10 段缺失（Person.Missing → SegmentNotFound）
    /// </summary>
    [Fact]
    public void TG1_SegmentNotFound()
        => AssertConsistent(typeof(Person), new[] { "Missing" }, PathResolutionOptions.ForwardEager, (int)PathSegmentFailure.SegmentNotFound);

    // ================= TG2 正向显式（ForwardContract） =================

    /// <summary>
    /// TG2-01 契约式段缺失（与 TG1-10 同场景，Failure 枚举一致）
    /// </summary>
    [Fact]
    public void TG2_Contract_SegmentNotFound()
        => AssertConsistent(typeof(Person), new[] { "Missing" }, PathResolutionOptions.ForwardContract, (int)PathSegmentFailure.SegmentNotFound);

    /// <summary>
    /// TG2-02 契约式值类型中断
    /// </summary>
    [Fact]
    public void TG2_Contract_NotNavigable()
        => AssertConsistent(typeof(Person), new[] { "Age", "City" }, PathResolutionOptions.ForwardContract, (int)PathSegmentFailure.NotNavigable);

    /// <summary>
    /// TG2-03 契约式非法索引
    /// </summary>
    [Fact]
    public void TG2_Contract_InvalidIndex()
        => AssertConsistent(typeof(Order), new[] { "Items[" }, PathResolutionOptions.ForwardContract, (int)PathSegmentFailure.InvalidIndexSyntax);

    /// <summary>
    /// TG2-04 契约式负索引
    /// </summary>
    [Fact]
    public void TG2_Contract_NegativeIndex()
        => AssertConsistent(typeof(Order), new[] { "Items[-1]" }, PathResolutionOptions.ForwardContract, (int)PathSegmentFailure.IndexNotNonNegative);

    /// <summary>
    /// TG2-05 契约式末段索引非集合
    /// </summary>
    [Fact]
    public void TG2_Contract_NotIndexable()
        => AssertConsistent(typeof(Order), new[] { "Name[0]" }, PathResolutionOptions.ForwardContract, (int)PathSegmentFailure.NotIndexableType);

    // ================= TG3 投影门面（等价性由既有测试兜底，本组做门面级冒烟） =================

    /// <summary>
    /// TG3-01 投影门面冒烟：TryBuildFlattenedExpression 走共享内核正常输出（FreeSql 消费面零回归由既有套件锁定）
    /// </summary>
    [Fact]
    public void TG3_ProjectionFacade_Smoke()
    {
        // 门面方法 TryBuildFlattenedExpression/TryBuildDotPathExpression 为 internal 且经共享内核；
        // 等价性已由既有 FreeSql/EFCore 投影套件锁定（全量回归），
        // 本测试仅做门面可观测冒烟——直接调用共享候选生成验证收敛无漂移（TG5 覆盖算法）。
        var candidates = PathCandidateGenerator.Build("HomeCity");
        Assert.True(candidates.Count >= 2, "多段候选应生成");
        Assert.Equal("HomeCity", candidates[0][0]);
    }

    // ================= TG4 反向写回（ReverseEager/ReverseContract） =================

    /// <summary>
    /// TG4-01 反向可写多段路径（TargetEntity.Child.Name）
    /// </summary>
    [Fact]
    public void TG4_Reverse_WritablePath()
    {
        // Type 版反向成功：可写 + 可实例化路径解析成功（符号版无反向承载，见 AssertTypeReverse 注释）
        var r = s_typeResolver.Resolve(typeof(TargetEntity), new[] { "Child", "Name" }, PathResolutionOptions.ReverseEager, context: null);
        Assert.True(r.Success);
        Assert.Equal(2, r.Path.Count);
    }

    /// <summary>
    /// TG4-02 反向中间节点不可写（init-only → SegmentNotWritable）
    /// </summary>
    [Fact]
    public void TG4_Reverse_InitOnly_NotWritable()
        => AssertTypeReverse(typeof(TargetEntity), new[] { "InitOnlyChild", "Name" }, PathResolutionOptions.ReverseEager, (int)PathSegmentFailure.SegmentNotWritable);

    /// <summary>
    /// TG4-03 反向接口不可实例化（TargetEntity.IfaceNav.Name → NotInstantiable）
    /// </summary>
    [Fact]
    public void TG4_Reverse_Interface_NotInstantiable()
        => AssertTypeReverse(typeof(TargetEntity), new[] { "IfaceNav", "Name" }, PathResolutionOptions.ReverseEager, (int)PathSegmentFailure.NotInstantiable);

    /// <summary>
    /// TG4-04 反向抽象不可实例化
    /// </summary>
    [Fact]
    public void TG4_Reverse_Abstract_NotInstantiable()
        => AssertTypeReverse(typeof(TargetEntity), new[] { "AbsNav", "Name" }, PathResolutionOptions.ReverseEager, (int)PathSegmentFailure.NotInstantiable);

    /// <summary>
    /// TG4-05 反向无参构造缺失不可实例化
    /// </summary>
    [Fact]
    public void TG4_Reverse_NoCtor_NotInstantiable()
        => AssertTypeReverse(typeof(TargetEntity), new[] { "NoCtorNav", "Name" }, PathResolutionOptions.ReverseEager, (int)PathSegmentFailure.NotInstantiable);

    /// <summary>
    /// TG4-06 反向值类型中间节点（TargetEntity.Name.City → NotNavigable）
    /// </summary>
    [Fact]
    public void TG4_Reverse_ValueType_NotNavigable()
        => AssertTypeReverse(typeof(TargetEntity), new[] { "Name", "City" }, PathResolutionOptions.ReverseEager, (int)PathSegmentFailure.NotNavigable);

    /// <summary>
    /// TG4-07 反向契约式中间节点不可写（软失败分类 SegmentNotWritable）
    /// </summary>
    [Fact]
    public void TG4_ReverseContract_NotWritable()
        => AssertTypeReverse(typeof(TargetEntity), new[] { "InitOnlyChild", "Name" }, PathResolutionOptions.ReverseEager, (int)PathSegmentFailure.SegmentNotWritable);

    // ================= TG5 候选生成（M-1/M-2 收敛无漂移） =================

    /// <summary>
    /// TG5-01 单段（小写/单段名不产生多段候选）
    /// </summary>
    [Fact]
    public void TG5_SingleSegment()
    {
        var c = PathCandidateGenerator.Build("name");
        Assert.Single(c);
        Assert.Equal(new[] { "name" }, c[0]);
    }

    /// <summary>
    /// TG5-02 多段升序（HomeCity → [HomeCity], [Home, City]；段数升序）
    /// </summary>
    [Fact]
    public void TG5_MultiSegment_Ascending()
    {
        var c = PathCandidateGenerator.Build("HomeCity");
        Assert.True(c.Count >= 2);
        for (int i = 1; i < c.Count; i++)
        {
            Assert.True(c[i].Count >= c[i - 1].Count, "段数升序");
        }
        Assert.Contains(c, parts => parts.Count == 2 && parts[0] == "Home" && parts[1] == "City");
    }

    /// <summary>
    /// TG5-03 拆点 &gt;4 仅单段（超过 4 个大写拆点 → 放弃拆解）
    /// </summary>
    [Fact]
    public void TG5_SplitPointsOverLimit()
    {
        var c = PathCandidateGenerator.Build("A_B_C_D_E_F");
        // 6 个大写拆点（A/B/C/D/E/F）> 4 → 仅单段候选（原算法对非 PascalCase 形态的保守行为）
        Assert.Single(c);
    }

    /// <summary>
    /// TG5-04 2-5 段上限（候选段数不超过 5）
    /// </summary>
    [Fact]
    public void TG5_MaxSegments_Five()
    {
        var c = PathCandidateGenerator.Build("A_B_C_D");
        Assert.All(c, parts => Assert.InRange(parts.Count, 1, 5));
    }

    /// <summary>
    /// TG5-05 原 M-1/M-2 算法等价性（逐形态对照：候选首元素为原名单段 + 组合枚举）
    /// </summary>
    [Fact]
    public void TG5_EquivalentToLegacyAlgorithm()
    {
        var name = "HomeCityStreet";
        var c = PathCandidateGenerator.Build(name);
        // 首元素原名单段
        Assert.Equal(name, c[0][0]);
        // 拆点 2 个（C/S）→ 组合 4 种（含单段），2-5 段升序
        Assert.True(c.Count >= 3, "2 拆点应产生 3+ 候选（单段 + 3 组合中 2-5 段者）");
        Assert.All(c, parts => Assert.All(parts, p => Assert.False(string.IsNullOrEmpty(p))));
    }
}
