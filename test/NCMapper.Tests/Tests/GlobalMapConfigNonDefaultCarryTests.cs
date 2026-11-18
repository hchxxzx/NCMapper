using System.Reflection;
using System.Reflection.Emit;
using NCMapper;
using NCMapper.Attributes;
using Xunit;

namespace NCMapper.Tests.Tests;

/// <summary>
/// 非默认值搬运腿直调清偿 — 经 IVT 直接调用 <c>GlobalMapConfig.FromAttribute</c>，逐条断言
/// 11 属性搬运结果，绕开「注入程序集 → 反射回退」这条已被证伪的外环。
/// <para><b>为何直调</b>：进程内 <c>AssemblyBuilderAccess.Run</c> 动态程序集不可卸载，注入非默认
/// 内容会改判其后每一次反射回退冻结（多程序集内容冲突），故非默认腿只能直调被测搬运函数本身；
/// 本类不注入任何程序集级特性。</para>
/// <para><b>护栏自证</b>：① 直调前后 <c>GetEffective()</c> 返回同一实例；② 直调不写冻结态、不改
/// 来源清单；③ 反射断言 <c>FromAttribute</c> 可见性与签名，防改名后用例静默空转。</para>
/// <para>共享进程级冻结状态，禁止并行（同 GlobalConfigState 集合）。</para>
/// </summary>
[Collection("GlobalConfigState")]
public class GlobalMapConfigNonDefaultCarryTests : IDisposable
{
    /// <summary>同进程内另一个测试类常驻注入的探针程序集名（反射回退来源清单辨异用）。</summary>
    private const string InjectedProbeName = "NcmRuntimeMapDefaultsProbe";

    /// <summary>兜底腿探针泛型参数的名称。</summary>
    private const string FallbackTypeName = "T";

    public GlobalMapConfigNonDefaultCarryTests()
    {
        Mapper.ClearCache();
        GlobalMapConfig.ResetForTests();
    }

    public void Dispose()
    {
        Mapper.ClearCache();
        GlobalMapConfig.ResetForTests();
    }

    /// <summary>
    /// 构造 11 属性全部非默认、且互不相同的特性实例 —— 任一属性被搬运成默认值或串项，
    /// 逐条断言即红。
    /// </summary>
    private static MapDefaultsAttribute BuildNonDefaultAttribute() => new()
    {
        NamingStrategy = NamingStrategy.SnakeCase,
        Prefixes = ["Get"],
        Postfixes = ["Dto"],
        IgnoreTypes = [typeof(Guid), typeof(DateTime)],
        IgnoreNames = ["Internal"],
        IgnoreNamePrefixes = ["m_"],
        IgnoreNameSuffixes = ["Raw"],
        IgnoreTypesToEntity = [typeof(Guid)],
        IgnoreNamesToEntity = ["Secret"],
        IgnoreNamePrefixesToEntity = ["s_"],
        IgnoreNameSuffixesToEntity = ["Stamp"],
    };

    /// <summary>
    /// 直接调用搬运函数：11 属性逐条断言，含两条闭包的决定性格
    /// （<c>IgnoreTypeFullNames</c> 与 <c>IgnoreTypeFullNamesToEntity</c> 均非空且为全名，
    /// 非空数组 ⇒ 两个 Select 闭包真迭代，防「执行了搬运但闭包零次迭代」的假绿）。
    /// </summary>
    [Fact]
    public void 直调FromAttribute_11属性非默认值_逐条搬运正确()
    {
        var cfg = GlobalMapConfig.FromAttribute(BuildNonDefaultAttribute());

        Assert.NotNull(cfg);
        Assert.Equal((int)NamingStrategy.SnakeCase, cfg.NamingStrategy);
        Assert.Equal(["Get"], cfg.Prefixes);
        Assert.Equal(["Dto"], cfg.Postfixes);
        Assert.Equal(["System.Guid", "System.DateTime"], cfg.IgnoreTypeFullNames);
        Assert.Equal(["Internal"], cfg.IgnoreNames);
        Assert.Equal(["m_"], cfg.IgnoreNamePrefixes);
        Assert.Equal(["Raw"], cfg.IgnoreNameSuffixes);
        Assert.Equal(["System.Guid"], cfg.IgnoreTypeFullNamesToEntity);
        Assert.Equal(["Secret"], cfg.IgnoreNamesToEntity);
        Assert.Equal(["s_"], cfg.IgnoreNamePrefixesToEntity);
        Assert.Equal(["Stamp"], cfg.IgnoreNameSuffixesToEntity);
    }

    /// <summary>
    /// 直接调用搬运函数：空引用腿（6 项 null / 5 项空数组）逐条兜底为空数组。
    /// </summary>
    [Fact]
    public void 直调FromAttribute_全空特性_逐条兜底为空数组()
    {
        var cfg = GlobalMapConfig.FromAttribute(new MapDefaultsAttribute());

        Assert.Equal((int)NamingStrategy.Default, cfg.NamingStrategy);
        Assert.Empty(cfg.Prefixes);
        Assert.Empty(cfg.Postfixes);
        Assert.Empty(cfg.IgnoreTypeFullNames);
        Assert.Empty(cfg.IgnoreNames);
        Assert.Empty(cfg.IgnoreNamePrefixes);
        Assert.Empty(cfg.IgnoreNameSuffixes);
        Assert.Empty(cfg.IgnoreTypeFullNamesToEntity);
        Assert.Empty(cfg.IgnoreNamesToEntity);
        Assert.Empty(cfg.IgnoreNamePrefixesToEntity);
        Assert.Empty(cfg.IgnoreNameSuffixesToEntity);
    }

    /// <summary>
    /// <c>FullName ?? Name</c> 兜底腿：动态泛型参数类型的 FullName 为 null、Name 非 null，
    /// 两处 Select 均须回落到 Name。前提（FullName 确为 null）先自证，防「形态变了仍全绿」。
    /// </summary>
    [Fact]
    public void 直调FromAttribute_FullName为null的类型_回落取Name()
    {
        var probe = BuildFullNameNullType();
        Assert.Null(probe.FullName);
        Assert.Equal(FallbackTypeName, probe.Name);

        var attr = new MapDefaultsAttribute
        {
            IgnoreTypes = [probe],
            IgnoreTypesToEntity = [probe],
        };
        var cfg = GlobalMapConfig.FromAttribute(attr);

        Assert.Equal([FallbackTypeName], cfg.IgnoreTypeFullNames);
        Assert.Equal([FallbackTypeName], cfg.IgnoreTypeFullNamesToEntity);
    }

    /// <summary>
    /// 护栏自证：直调不写冻结态、不改来源清单 —— 调用前后 GetEffective() 返回同一实例，
    /// 且冻结结果逐属性恒等；无外部常驻注入程序集时来源清单仍为空。
    /// </summary>
    [Fact]
    public void 护栏_直调不改冻结态且不放宽来源清单()
    {
        var before = GlobalMapConfig.GetEffective();
        var beforeSnapshot = Snapshot(before);

        var cfg = GlobalMapConfig.FromAttribute(BuildNonDefaultAttribute());
        Assert.NotNull(cfg);

        var after = GlobalMapConfig.GetEffective();
        Assert.Same(before, after);
        Assert.Equal(beforeSnapshot, Snapshot(after));

        // 反射回退的来源清单：仅当同进程内无其它测试类常驻注入探针时为空。
        if (HasInjectedProbe())
            Assert.Equal([InjectedProbeName], before.SourceAssemblies);
        else
            Assert.Empty(before.SourceAssemblies);
    }

    /// <summary>
    /// 护栏自证：<c>FromAttribute</c> 的存在性与签名（internal static / 返回快照载体 /
    /// 单参 <c>MapDefaultsAttribute</c>）——将来改名或改签名时本格即红，用例不会静默空转。
    /// </summary>
    [Fact]
    public void 护栏_FromAttribute签名与可见性未变()
    {
        var method = typeof(GlobalMapConfig).GetMethod(
            "FromAttribute",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

        Assert.NotNull(method);
        Assert.True(method!.IsAssembly, "FromAttribute 应为 internal（程序集内可见）");
        Assert.Equal(typeof(GeneratedMapConfig), method.ReturnType);

        var parameters = method.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(MapDefaultsAttribute), parameters[0].ParameterType);
    }

    /// <summary>
    /// 冻结结果逐属性快照（护栏恒等比较用）。
    /// </summary>
    private static string[] Snapshot(GlobalMapConfig config) =>
    [
        config.NamingStrategy.ToString(),
        string.Join(",", config.Prefixes),
        string.Join(",", config.Postfixes),
        string.Join(",", config.IgnoreTypeFullNames),
        string.Join(",", config.IgnoreNames),
        string.Join(",", config.IgnoreNamePrefixes),
        string.Join(",", config.IgnoreNameSuffixes),
        string.Join(",", config.IgnoreTypeFullNamesToEntity),
        string.Join(",", config.IgnoreNamesToEntity),
        string.Join(",", config.IgnoreNamePrefixesToEntity),
        string.Join(",", config.IgnoreNameSuffixesToEntity),
        string.Join(",", config.SourceAssemblies),
    ];

    /// <summary>
    /// 同进程内是否已有常驻注入的探针程序集（决定来源清单断言的形态）。
    /// </summary>
    private static bool HasInjectedProbe() =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Any(a => a.GetName().Name == InjectedProbeName);

    /// <summary>
    /// 构造一个 <c>FullName</c> 为 null 的 <see cref="Type"/> —— 动态泛型参数类型（无全名）。
    /// 探针类型定义不完整、<c>GetTypes()</c> 必失败：用 Run 会常驻为全进程扫描的永久失败源，改 RunAndCollect 方可回收。
    /// </summary>
    private static Type BuildFullNameNullType()
    {
        var assemblyName = "NcmNonDefaultCarryProbe_" + Guid.NewGuid().ToString("N")[..8];
        var builder = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName(assemblyName), AssemblyBuilderAccess.RunAndCollect);
        var typeBuilder = builder.DefineDynamicModule("m")
            .DefineType("Probe`1", TypeAttributes.Public);
        return typeBuilder.DefineGenericParameters(FallbackTypeName)[0];
    }
}
