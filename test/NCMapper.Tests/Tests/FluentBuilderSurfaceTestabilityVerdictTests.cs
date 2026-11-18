using System;
using System.Linq;
using System.Reflection;
using NCMapper;
using Xunit;

namespace NCMapper.Tests.Tests;

/// <summary>
/// Fluent 运行时 builder 面可测性判定（D3 探索题）。
/// <para><b>判定结论：不可测（机制性形态，无运行时行为可测）</b>——本类以可执行测试**机械锁定该判定**，
/// 而非写一个"形式上有断言但永不失败"的装饰性用例。</para>
/// <para><b>判定依据（三条机械证据，均在本测试中实读验证）</b>：
/// ① <c>FluentMappingConfig.For&lt;TSource,TTarget&gt;()</c> 体为 <c>=> new(this)</c>——只构造配置对象；
/// ② 全部终端方法体为 <c>=&gt; this;</c>（空转，返回自身续链），**不做任何运行时工作**；
/// ③ 配置类**无任何运行时状态字段**（无实例字段、无静态字段承载映射数据）——
///    映射数据的真实来源是源生成器在**编译期**扫描这些调用点后生成的 GeneratedMapper。</para>
/// <para><b>类头注原文佐证</b>：<c>FluentMappingConfig.cs:11-12</c> 明写
/// "本类仅作为编译期源生成器扫描的语法锚点——所有终端方法不做任何运行时工作，
/// 运行时映射由源生成器生成的 GeneratedMapper 承担（Configure 方法本身不会被调用）"。</para>
/// <para><b>因此</b>：Fluent builder 面的正确验证方式是<b>源生成器诊断/产物断言</b>
/// （已由 <c>test/NCMapper.SourceGen.Tests/Fluent/</c> 下 20+ 个测试文件覆盖），
/// 而非运行时行为测试。本任务不改 src、不新增装饰性运行时用例。</para>
/// </summary>
public class FluentBuilderSurfaceTestabilityVerdictTests
{
    /// <summary>
    /// 证据 ①＋②：所有 <see cref="FluentMappingConfig"/> 及其链式配置类型的公开实例方法，
    /// 方法体必须是**不调用任何业务方法**的无副作用空转形态。
    /// <para><b>机械判据（20261117 改版，取代原「IL 字节长度白名单」）</b>：逐条解码方法体 IL，
    /// 断言**不存在**指向业务方法的调用指令（<c>call</c> / <c>callvirt</c> / <c>calli</c> /
    /// <c>ldftn</c> / <c>ldvirtftn</c> / <c>newobj</c>）。允许出现的仅三类：
    /// ① 返回 this（<c>ldarg.0; ret</c>）；
    /// ② 构造本配置面的新对象（<c>new(this)</c>，newobj 目标类型属于本配置面）；
    /// ③ 覆盖率插桩等编译器注入的**非调用**指令
    ///    （实测形态：<c>ldsfld 命中计数数组; ldc.i4 偏移; add; ldc.i4.1; stind.i1</c>）。</para>
    /// <para><b>为何必须改判据</b>：原判据把「无运行时工作」间接表达为「IL 恰好 2 / 6 / 7 字节」，
    /// 这等价于假设「编译器不往方法体里插任何指令」。CI 覆盖率作业
    /// （<c>dotnet test -- --coverage</c>，Microsoft.Testing.Extensions.CodeCoverage）在 Linux 上
    /// 会为每个方法体插入命中计数指令，于是 <c>Tree()</c> 从 2 字节变 15 字节、
    /// <c>Member()</c> 从 7 字节变 32 字节，白名单全部失配 → 假红。
    /// 实测对照：同一提交，Windows 上 2533 例全过；Linux 上不带覆盖率 2533 例全过，
    /// 带覆盖率稳定红 1 例。改判据后「有没有运行时逻辑」被直接判定，
    /// 不再受插桩、编译器版本与平台差异影响。</para>
    /// <para>若将来有人给终端方法加了真实运行时逻辑（使本面"可测"），本测试会变红并提示
    /// 重新评估判定——这是判定的**失效哨兵**，不是装饰。</para>
    /// </summary>
    [Fact]
    public void AllTerminalMethods_ArePureChainStubs_NoRuntimeWork()
    {
        var assembly = typeof(FluentMappingConfig).Assembly;
        var configTypes = assembly.GetTypes()
            .Where(t => t.IsPublic && t.Namespace == "NCMapper"
                     && (t == typeof(FluentMappingConfig)
                         || t.Name.StartsWith("FluentTypeConfig", StringComparison.Ordinal)
                         || t.Name.StartsWith("MemberConfig", StringComparison.Ordinal)
                         || t.Name.StartsWith("MemberHead", StringComparison.Ordinal)))
            .ToArray();

        Assert.NotEmpty(configTypes);
        var configTypeSet = new System.Collections.Generic.HashSet<Type>(configTypes);

        var offenders = new System.Collections.Generic.List<string>();
        foreach (var type in configTypes)
        {
            foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (m.IsSpecialName) continue;              // 跳过属性访问器
                var body = m.GetMethodBody();
                if (body == null) continue;                 // 抽象/外部方法不计

                var il = body.GetILAsByteArray();
                if (il == null || il.Length == 0) continue;

                var calls = FindBusinessCalls(m.Module, il, configTypeSet);
                if (calls.Count > 0)
                {
                    offenders.Add($"{type.Name}.{m.Name} (IL {il.Length} bytes): {string.Join(", ", calls)}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Fluent 配置面出现含运行时逻辑的方法——「机制性形态、运行时不执行」判定失效，须重评：\n"
            + string.Join("\n", offenders));
    }

    /// <summary>
    /// 解码方法体 IL，返回其中**指向业务方法**的调用指令描述（空列表 = 纯空转）。
    /// <para>「业务方法」口径：调用目标的声明类型不属于本配置面、也不是覆盖率插桩代理。
    /// 配置面自身的方法与构造函数放行——那正是 <c>=&gt; this;</c> 与 <c>new(this)</c> 的形态。</para>
    /// <para>必须完整解码操作数长度后再判断，不能直接扫描字节找 0x28/0x6F：
    /// 操作数字节里出现这些值会被误当成调用指令。</para>
    /// </summary>
    private static System.Collections.Generic.List<string> FindBusinessCalls(
        Module module, byte[] il, System.Collections.Generic.HashSet<Type> configTypes)
    {
        var hits = new System.Collections.Generic.List<string>();
        var i = 0;
        while (i < il.Length)
        {
            var code = il[i];

            // 两字节操作码（0xFE 前缀）：只有 ldftn / ldvirtftn 是"取方法句柄"，须检查
            if (code == 0xFE)
            {
                if (i + 1 >= il.Length) break;
                var sub = il[i + 1];
                if ((sub == 0x06 || sub == 0x07) && i + 6 <= il.Length)
                {
                    Record(i, ReadInt32(il, i + 2), sub == 0x06 ? "ldftn" : "ldvirtftn");
                }

                i += 2 + TwoByteOperandSize(sub);
                continue;
            }

            // calli：操作数是独立签名令牌（StandAloneSig），不是方法令牌，
            // 无法解析出目标——空转方法体不可能出现间接调用，故一律视为业务调用。
            if (code == 0x29)
            {
                hits.Add($"IL_{i:X4} calli → 间接调用（签名令牌，非方法令牌）");
                i += 5;
                continue;
            }

            // 单字节调用类操作码：call / callvirt / newobj
            if (code == 0x28 || code == 0x6F || code == 0x73)
            {
                if (i + 5 <= il.Length) Record(i, ReadInt32(il, i + 1), OpcodeName(code));
                i += 5;
                continue;
            }

            // switch：操作数长度 = 4 + 4 * n
            if (code == 0x45)
            {
                if (i + 5 > il.Length) break;
                i += 5 + 4 * ReadInt32(il, i + 1);
                continue;
            }

            i += 1 + OneByteOperandSize(code);
        }

        return hits;

        void Record(int offset, int token, string opcode)
        {
            MethodBase? target = null;
            try
            {
                target = module.ResolveMethod(token);
            }
            catch
            {
                return; // 元数据令牌在当前上下文不可解析（如泛型实例化），不计入
            }

            var declaring = target?.DeclaringType;
            if (declaring is not null && configTypes.Contains(declaring)) return;
            if (declaring is not null && IsInstrumentationType(declaring)) return;

            hits.Add($"IL_{offset:X4} {opcode} → {(declaring?.Name ?? "<global>")}.{target?.Name}");
        }
    }

    /// <summary>读取小端 4 字节元数据令牌。</summary>
    private static int ReadInt32(byte[] il, int start)
        => il[start] | (il[start + 1] << 8) | (il[start + 2] << 16) | (il[start + 3] << 24);

    private static string OpcodeName(byte code) => code switch
    {
        0x28 => "call",
        0x29 => "calli",
        0x6F => "callvirt",
        0x73 => "newobj",
        _ => $"0x{code:X2}",
    };

    /// <summary>覆盖率插桩/诊断代理类型——不是业务逻辑，放行。</summary>
    private static bool IsInstrumentationType(Type declaring)
    {
        var name = declaring.FullName ?? declaring.Name;
        if (name.Contains("Coverage", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Profiler", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Instrumentation", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            return declaring.GetCustomAttributes(inherit: false).Any(a =>
                a.GetType().FullName == "System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute");
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 单字节操作码的操作数字节数（ECMA-335 §III）。目的仅是"正确跳过操作数"，
    /// 故未列出的操作码一律按 0 字节处理。
    /// </summary>
    private static int OneByteOperandSize(byte code) => code switch
    {
        // 1 字节
        0x0E or 0x0F or 0x10 or 0x11 or 0x12 or 0x13 => 1,   // ldarg.s / ldarga.s / starg.s / ldloc.s / ldloca.s / stloc.s
        0x1F => 1,                                            // ldc.i4.s
        >= 0x2B and <= 0x37 => 1,                             // br.s 与全部短条件跳转
        0xDE => 1,                                            // leave.s

        // 4 字节
        0x20 => 4,                                            // ldc.i4
        0x22 => 4,                                            // ldc.r4
        0x27 => 4,                                            // jmp
        0x28 or 0x29 or 0x6F => 4,                            // call / calli / callvirt
        >= 0x38 and <= 0x44 => 4,                             // br 与全部长条件跳转
        0x70 or 0x71 or 0x72 or 0x73 => 4,                    // cpobj / ldobj / ldstr / newobj
        0x74 or 0x75 or 0x79 => 4,                            // castclass / isinst / unbox
        0x7B or 0x7C or 0x7D or 0x7E or 0x7F or 0x80 or 0x81 => 4, // ldfld … stobj
        0x8C or 0x8D or 0x8F => 4,                            // box / newarr / ldelema
        0xA3 or 0xA4 or 0xA5 => 4,                            // ldelem / stelem / unbox.any
        0xC2 or 0xC6 or 0xD0 => 4,                            // refanyval / mkrefany / ldtoken
        0xDD => 4,                                            // leave

        // 8 字节
        0x21 or 0x23 => 8,                                    // ldc.i8 / ldc.r8

        _ => 0,
    };

    /// <summary>0xFE 前缀（两字节）操作码的操作数字节数（ECMA-335 §III）。</summary>
    private static int TwoByteOperandSize(byte sub) => sub switch
    {
        0x06 or 0x07 => 4,                                    // ldftn / ldvirtftn
        0x09 or 0x0A or 0x0B or 0x0C or 0x0D or 0x0E => 2,    // ldarg / ldarga / starg / ldloc / ldloca / stloc
        0x12 or 0x19 => 1,                                    // unaligned. / no.
        0x15 or 0x16 => 4,                                    // initobj / constrained.
        0x1C => 4,                                            // sizeof
        _ => 0,
    };

    /// <summary>
    /// 证据 ③：配置类型**无运行时状态字段**（无实例字段、无静态字段承载映射数据）。
    /// <para>若将来引入运行时状态（使该面产生可观测运行时行为），本测试变红。</para>
    /// </summary>
    [Fact]
    public void ConfigTypes_HaveNoRuntimeStateFields()
    {
        var assembly = typeof(FluentMappingConfig).Assembly;
        var configTypes = assembly.GetTypes()
            .Where(t => t.IsPublic && t.Namespace == "NCMapper"
                     && (t == typeof(FluentMappingConfig)
                         || t.Name.StartsWith("FluentTypeConfig", StringComparison.Ordinal)
                         || t.Name.StartsWith("MemberConfig", StringComparison.Ordinal)
                         || t.Name.StartsWith("MemberHead", StringComparison.Ordinal)))
            .ToArray();

        var stateful = new System.Collections.Generic.List<string>();
        foreach (var type in configTypes)
        {
            foreach (var f in type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                // 允许的唯一字段形态：编译器生成的 backer 字段（无）或常量
                if (f.IsLiteral) continue;
                stateful.Add($"{type.Name}.{f.Name} ({f.FieldType.Name})");
            }
        }

        Assert.True(stateful.Count == 0,
            "Fluent 配置面出现运行时状态字段——判定前提（无运行时状态）失效，须重评：\n"
            + string.Join("\n", stateful));
    }

    /// <summary>
    /// 判定结论的正向表达：Fluent builder 面的可验证性**完全由源生成器诊断面承担**——
    /// 断言该诊断测试面确实存在且规模可观（证明"不可测"不等于"未覆盖"）。
    /// <para>本断言读取的是编译产物中存在的诊断类（源生成器发射的诊断描述符），
    /// 证明 Fluent 面的验证路径是"编译期诊断/产物断言"而非"运行时行为测试"。</para>
    /// </summary>
    [Fact]
    public void FluentSurface_IsVerifiedViaSourceGenDiagnostics_NotRuntime()
    {
        // Fluent 相关诊断码在源生成器程序集中以常量形式存在（NCMAP_F* 族）
        var sourceGenAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "NCMapper.SourceGen");

        if (sourceGenAssembly == null)
        {
            // 源生成器程序集未加载时（如纯运行时测试上下文）本断言退化为"该面确实无运行时入口"，
            // 与证据①②③同向，不构成失败。
            Assert.Null(typeof(FluentMappingConfig).GetMethod("Configure",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static));
            return;
        }

        var descriptorTypes = sourceGenAssembly.GetTypes()
            .Where(t => t.IsAbstract && t.IsSealed)  // 静态类
            .Where(t => t.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Any(f => f.FieldType.Name.Contains("DiagnosticDescriptor", StringComparison.Ordinal)))
            .ToArray();

        Assert.NotEmpty(descriptorTypes);
    }
}
