using System.Reflection;
using System.Reflection.Emit;
using NCMapper;
using NCMapper.Attributes;
using Xunit;

namespace NCMapper.Tests.Tests;

/// <summary>
/// 运行时 [assembly: MapDefaults] 冻结腿补例 — GlobalMapConfig.FreezeCore 反射回退分支
/// 与 FromAttribute 的 11 属性搬运，以及 MapDefaultsAttribute 的 11 组 getter/setter。
/// <para>可达性前提：FreezeCore 在注册表非空时直接走注册表分支、不扫程序集；本程序集内
/// 生成器产物与多处用例均调用 Mapper.RegisterGeneratedConfig，注册表默认非空，反射回退
/// 默认不可达。故用例须先 ResetForTests() 清空注册表、随即 GetEffective() 才进入反射回退
/// 分支，并以 SourceAssemblies 含注入程序集名自证本次走的是反射回退（注册表分支的来源
/// 清单为注册名，两分支可辨——辨异形态见「注册表非空」用例）。</para>
/// <para><b>进程级残留（AssemblyBuilderAccess.Run 产物不可卸载）</b>：「不可卸载」仅限定于该访问模式
/// （RunAndCollect 不在本类形态内）；此模式下产物在进程内常驻，
/// AppDomain.CurrentDomain.GetAssemblies() 对其后每一次反射回退冻结均可见。因此注入
/// 内容取「与内建默认等价」（NamingStrategy.Default + 空清单）：非默认内容会改判其后所有
/// 依赖「反射回退 = 内建默认」的用例（GlobalConfigTests.NoRegistration_Freeze_ReturnsBuiltInDefault
/// 断言 11 清单全空、T64_T001_GlobalConfigFreezeTraceTests 断言 NamingStrategy.Default）。
/// 代价：非默认值搬运腿与 IgnoreTypes 非空的 Select 闭包在本类注入形态下不可覆盖（已登记残留面）；
/// 该两条腿的清偿改由 GlobalMapConfigNonDefaultCarryTests 直调 FromAttribute 承载。</para>
/// <para>共享进程级冻结状态，禁止并行（同 GlobalConfigTests 集合）。</para>
/// </summary>
[Collection("GlobalConfigState")]
public class GlobalMapConfigAssemblyAttributeTests : IDisposable
{
    /// <summary>注入的动态程序集名（SourceAssemblies 两分支辨异用）。</summary>
    private const string InjectedAsmName = "NcmRuntimeMapDefaultsProbe";

    /// <summary>
    /// 注入的动态程序集 — 进程级常驻且不可卸载，全类只注入一次：
    /// 再注入一个内容不同的程序集会使其后每一次反射回退冻结判为多程序集冲突。
    /// </summary>
    private static readonly Assembly s_injected = BuildInjectedAssembly();

    public GlobalMapConfigAssemblyAttributeTests()
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
    /// 构造带 [assembly: MapDefaults] 的动态程序集：11 个命名属性全部显式赋值
    /// （6 项空数组走非 null 腿、5 项 null 走 ?? Array.Empty 腿），
    /// 使 11 个 setter 在特性实例化时被调用、11 个 getter 在搬运时被读取。
    /// </summary>
    private static Assembly BuildInjectedAssembly()
    {
        var builder = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName(InjectedAsmName), AssemblyBuilderAccess.Run);
        var ctor = typeof(MapDefaultsAttribute).GetConstructor(Type.EmptyTypes)!;

        var namedProps = new[]
        {
            typeof(MapDefaultsAttribute).GetProperty(nameof(MapDefaultsAttribute.NamingStrategy))!,
            typeof(MapDefaultsAttribute).GetProperty(nameof(MapDefaultsAttribute.Prefixes))!,
            typeof(MapDefaultsAttribute).GetProperty(nameof(MapDefaultsAttribute.Postfixes))!,
            typeof(MapDefaultsAttribute).GetProperty(nameof(MapDefaultsAttribute.IgnoreTypes))!,
            typeof(MapDefaultsAttribute).GetProperty(nameof(MapDefaultsAttribute.IgnoreNames))!,
            typeof(MapDefaultsAttribute).GetProperty(nameof(MapDefaultsAttribute.IgnoreNamePrefixes))!,
            typeof(MapDefaultsAttribute).GetProperty(nameof(MapDefaultsAttribute.IgnoreNameSuffixes))!,
            typeof(MapDefaultsAttribute).GetProperty(nameof(MapDefaultsAttribute.IgnoreTypesToEntity))!,
            typeof(MapDefaultsAttribute).GetProperty(nameof(MapDefaultsAttribute.IgnoreNamesToEntity))!,
            typeof(MapDefaultsAttribute).GetProperty(nameof(MapDefaultsAttribute.IgnoreNamePrefixesToEntity))!,
            typeof(MapDefaultsAttribute).GetProperty(nameof(MapDefaultsAttribute.IgnoreNameSuffixesToEntity))!,
        };
        var namedValues = new object?[]
        {
            NamingStrategy.Default,
            Array.Empty<string>(),
            null,
            Array.Empty<Type>(),
            null,
            Array.Empty<string>(),
            null,
            null,
            Array.Empty<string>(),
            null,
            Array.Empty<string>(),
        };

        builder.SetCustomAttribute(new CustomAttributeBuilder(
            ctor, Array.Empty<object>(), namedProps, namedValues));
        return builder;
    }

    /// <summary>
    /// 反射回退冻结：11 属性逐条搬运 + 空值兜底（null → 空数组、空数组 → 原样搬运）。
    /// <para>前置自证：注入有效（特性可读回且 11 属性与注入形态一致）＋ 本次走反射回退
    /// （SourceAssemblies 含注入程序集名）；缺任一自证即为本腿假覆盖。</para>
    /// </summary>
    [Fact]
    public void AssemblyAttribute_反射回退冻结_11属性搬运与空值兜底()
    {
        // 1) 防 vacuous：注入的程序集确实带可读回的 MapDefaultsAttribute
        var attr = s_injected.GetCustomAttribute<MapDefaultsAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(NamingStrategy.Default, attr!.NamingStrategy);
        Assert.Empty(attr.Prefixes!);
        Assert.Null(attr.Postfixes);
        Assert.Empty(attr.IgnoreTypes!);
        Assert.Null(attr.IgnoreNames);
        Assert.Empty(attr.IgnoreNamePrefixes!);
        Assert.Null(attr.IgnoreNameSuffixes);
        Assert.Null(attr.IgnoreTypesToEntity);
        Assert.Empty(attr.IgnoreNamesToEntity!);
        Assert.Null(attr.IgnoreNamePrefixesToEntity);
        Assert.Empty(attr.IgnoreNameSuffixesToEntity!);

        // 2) 触发冻结（注册表已由构造器清空 → 反射回退分支）
        var frozen = GlobalMapConfig.GetEffective();

        // 3) 反射回退自证：来源程序集清单含注入程序集名
        Assert.Contains(InjectedAsmName, frozen.SourceAssemblies);

        // 4) 搬运结果断言：11 属性逐条（内建默认等价内容 → 全部为空清单）
        Assert.Equal(NamingStrategy.Default, frozen.NamingStrategy);
        Assert.Empty(frozen.Prefixes);
        Assert.Empty(frozen.Postfixes);
        Assert.Empty(frozen.IgnoreTypeFullNames);
        Assert.Empty(frozen.IgnoreNames);
        Assert.Empty(frozen.IgnoreNamePrefixes);
        Assert.Empty(frozen.IgnoreNameSuffixes);
        Assert.Empty(frozen.IgnoreTypeFullNamesToEntity);
        Assert.Empty(frozen.IgnoreNamesToEntity);
        Assert.Empty(frozen.IgnoreNamePrefixesToEntity);
        Assert.Empty(frozen.IgnoreNameSuffixesToEntity);
    }

    /// <summary>
    /// 常驻注入的冻结结果与内建默认逐属性等价 —— 残留对后续反射回退无观测影响。
    /// </summary>
    [Fact]
    public void AssemblyAttribute_常驻注入_后续反射回退仍等价内建默认()
    {
        var first = GlobalMapConfig.GetEffective();
        Mapper.ClearCache();
        GlobalMapConfig.ResetForTests();
        var second = GlobalMapConfig.GetEffective();

        Assert.Equal(first.NamingStrategy, second.NamingStrategy);
        Assert.Equal(first.Prefixes, second.Prefixes);
        Assert.Equal(first.Postfixes, second.Postfixes);
        Assert.Equal(first.IgnoreTypeFullNames, second.IgnoreTypeFullNames);
        Assert.Equal(first.IgnoreNames, second.IgnoreNames);
        Assert.Equal(first.IgnoreNamePrefixes, second.IgnoreNamePrefixes);
        Assert.Equal(first.IgnoreNameSuffixes, second.IgnoreNameSuffixes);
        Assert.Equal(first.IgnoreTypeFullNamesToEntity, second.IgnoreTypeFullNamesToEntity);
        Assert.Equal(first.IgnoreNamesToEntity, second.IgnoreNamesToEntity);
        Assert.Equal(first.IgnoreNamePrefixesToEntity, second.IgnoreNamePrefixesToEntity);
        Assert.Equal(first.IgnoreNameSuffixesToEntity, second.IgnoreNameSuffixesToEntity);
    }

    /// <summary>
    /// 注册表非空时冻结走注册表分支、不扫程序集 —— 可达性前提的正向自证，
    /// 同时给出 SourceAssemblies 的两分支辨异依据。
    /// </summary>
    [Fact]
    public void 注册表非空_冻结走注册表分支_不扫程序集()
    {
        GlobalMapConfig.Register(
            new GeneratedMapConfig { NamingStrategy = (int)NamingStrategy.CaseSensitive },
            "NcmRegisteredProbe");

        var frozen = GlobalMapConfig.GetEffective();

        Assert.Equal(NamingStrategy.CaseSensitive, frozen.NamingStrategy);
        Assert.Contains("NcmRegisteredProbe", frozen.SourceAssemblies);
        Assert.DoesNotContain(InjectedAsmName, frozen.SourceAssemblies);
    }

    /// <summary>
    /// 冲突检测双态（等价态）：两次 Register 等价配置（清单乱序亦判同）→ 冻结不抛、
    /// 合并为单视图、来源清单含两个注册名。
    /// </summary>
    [Fact]
    public void Register两次等价配置_冻结不抛且合并单视图()
    {
        GlobalMapConfig.Register(
            new GeneratedMapConfig
            {
                NamingStrategy = (int)NamingStrategy.SnakeCase,
                IgnoreNames = ["Beta", "Alpha"],
                Prefixes = ["Get"],
            },
            "NcmConflictAsmA");
        GlobalMapConfig.Register(
            new GeneratedMapConfig
            {
                NamingStrategy = (int)NamingStrategy.SnakeCase,
                IgnoreNames = ["Alpha", "Beta"],
                Prefixes = ["Get"],
            },
            "NcmConflictAsmB");

        var frozen = GlobalMapConfig.GetEffective();

        Assert.Equal(NamingStrategy.SnakeCase, frozen.NamingStrategy);
        Assert.Equal(["Alpha", "Beta"], frozen.IgnoreNames);
        Assert.Equal(["Get"], frozen.Prefixes);
        Assert.Contains("NcmConflictAsmA", frozen.SourceAssemblies);
        Assert.Contains("NcmConflictAsmB", frozen.SourceAssemblies);
    }

    /// <summary>
    /// 冲突检测双态（不等价态）：两次 Register 内容不同 → 冻结必抛冲突异常且消息列出双方程序集名。
    /// <para>红面自证：把不等价改为等价即不抛（见上一用例）——两态共用规范化与相等比较判定。</para>
    /// </summary>
    [Fact]
    public void Register两次不等价配置_冻结抛冲突异常且列出双方程序集()
    {
        GlobalMapConfig.Register(
            new GeneratedMapConfig { IgnoreNames = ["Alpha"] }, "NcmConflictAsmA");
        GlobalMapConfig.Register(
            new GeneratedMapConfig { IgnoreNames = ["Alpha", "Beta"] }, "NcmConflictAsmB");

        var ex = Assert.Throws<MapConfigurationException>(() => GlobalMapConfig.GetEffective());

        Assert.Contains("NcmConflictAsmA", ex.Message);
        Assert.Contains("NcmConflictAsmB", ex.Message);
    }
}
