// ============================================================================
// T64 / B5-5 Batch-1 — A3-22① 冻结路径裸 catch 留痕红测
//
// 缺陷承载（A3-22①）：GlobalMapConfig.cs:294-302（GetCustomAttribute
// 裸 catch{continue;}）与 :324-333（SafeGetAssemblies 裸 catch{return 空}）——冻结路径
// 静默降级无任何可观测记录。
//
// 红测注入形态（探针实证，artifacts/t64/probe-a322）：MapDefaultsAttribute 为 sealed
// 不可派生——用 AssemblyBuilder.SetCustomAttribute(ctor, 畸形 blob) 使
// GetCustomAttribute&lt;MapDefaultsAttribute&gt;() 抛 CustomAttributeFormatException
// （截断 blob 01 00 → Binary format of the specified custom attribute was invalid）。
//
// 修复后期望：冻结不阻断（静默降级语义保持）+ MapTracer 留痕含程序集名（GloballyEnabled
// 时输出到 GlobalOutput）。
// ============================================================================

using System.Reflection;
using System.Reflection.Emit;
using NCMapper;
using NCMapper.Attributes;
using Xunit;

namespace NCMapper.Tests.Tests;

/// <summary>共享进程级冻结状态，禁止并行（同 GlobalConfigTests 集合）。</summary>
[Collection("GlobalConfigState")]
public class T64_T001_GlobalConfigFreezeTraceTests : IDisposable
{
    public T64_T001_GlobalConfigFreezeTraceTests()
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
    /// A3-22① 红测：坏程序集（特性查询抛 CustomAttributeFormatException）参与冻结反射
    /// 回退——现状静默 continue 无任何记录（红：断言留痕失败）；修复后冻结仍成功（降级
    /// 语义不变，返回内建默认）且 Trace 输出含该程序集名。
    /// </summary>
    [Fact]
    public void A3_22_冻结反射回退_坏程序集_静默降级且留痕可观测()
    {
        // 1) 构造特性查询抛异常的动态程序集（畸形 blob：截断的 01 00）
        var asmName = "T64BadAsm_" + Guid.NewGuid().ToString("N")[..8];
        var builder = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(asmName), AssemblyBuilderAccess.Run);
        var ctor = typeof(MapDefaultsAttribute).GetConstructor(Type.EmptyTypes)!;
        builder.SetCustomAttribute(ctor, new byte[] { 0x01, 0x00 });

        // 先证注入有效：该程序集的特性查询确实抛异常（防 vacuous——注入失败时测试天然全绿）
        Assert.ThrowsAny<Exception>(() => builder.GetCustomAttribute<MapDefaultsAttribute>());

        // 2) 启用 Trace 捕获 GlobalOutput（Save/Restore 防污染）
        var sw = new System.IO.StringWriter();
        var oldEnabled = MapTracer.GloballyEnabled;
        var oldOutput = MapTracer.GlobalOutput;
        MapTracer.GloballyEnabled = true;
        MapTracer.GlobalOutput = sw;
        try
        {
            // 3) 触发冻结（ResetForTests 后走反射回退路径，扫描含坏程序集）
            //  口径注：若 GlobalMapConfigAssemblyAttributeTests 已执行，
            //    反射分支还会扫到其常驻注入的 MapDefaults 探针程序集；该特性内容按构造与内建默认逐项等价，
            //    故本例的 Default 断言与留痕断言均不受影响（断言未改，仅口径注记）。
            var frozen = GlobalMapConfig.GetEffective();

            // 4) 冻结不阻断（静默降级语义保持）——返回内建默认
            Assert.Equal(NamingStrategy.Default, frozen.NamingStrategy);

            // 5) 留痕断言（红：现状无记录；修复后含坏程序集名）
            Assert.Contains(asmName, sw.ToString());
        }
        finally
        {
            MapTracer.GloballyEnabled = oldEnabled;
            MapTracer.GlobalOutput = oldOutput;
        }
    }
}
