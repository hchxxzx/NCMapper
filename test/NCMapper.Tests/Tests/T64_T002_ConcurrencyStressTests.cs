// ============================================================================
// T64 / B5-5 Batch-2 — 并发撕裂红测（A3-06/A3-20）
//
// 缺陷承载：
//   A3-06  RuntimeSnapshotStore 写侧锁内原地改写 List（Register :50-68）vs
//          读侧锁外枚举（FindByTarget :85 / IsPropertyNoAutoInclude :134 /
//          IsFluentRegisteredDto :164 / TryGetFluentDetailMeta :183 /
//          GetPrimaryMeta :256）——并发撕裂（List 版本检查抛 InvalidOperationException
//          或读到撕裂快照）；
//   A3-20  RuntimeSnapshotStore.Clear 分清非原子（242-247——s_all 在锁内、
//          byTarget/byPair 在锁外）→ 并发 Register 交错产生孤儿条目
//          （s_all 有而 byPair/byTarget 无，三容器不一致）；MapValidator CWT 换新
//          非原子（141/:177——非 volatile，换新后可见性无保证）。
// ============================================================================

using System.Reflection;
using System.Reflection.Emit;
using NCMapper;
using NCMapper.Internal;
using NCMapper.Runtime;
using Xunit;

namespace NCMapper.Tests.Tests;

[Collection("GlobalConfigState")]
public class T64_T002_ConcurrencyStressTests
{
    // ============================== A3-06 ==============================

    /// <summary>
    /// A3_06 红测（判据修复）：写线程持续 Register（动态源类型 →
    /// 目标 List 持续增长）+ 读线程循环锁外枚举（IsFluentRegisteredDto foreach 路径）——
    /// 现状 List 版本检查抛 InvalidOperationException（红：readerEx 非空）；修复后读侧
    /// 锁内快照，零异常。
    /// <para>判据（v1.1 完成锚定型，任何机器负载下结果唯一）：① reader 在超时窗口内完成；
    /// ② 真契约 readerEx == null。<b>readCount 降为观测登记值不作断言</b>——原吞吐判据
    /// <c>readCount &gt; 10_000</c> 随机器负载浮动（实测 8994/9285/9819 未达，2026-09-22
    /// TFM 翻面转正缺陷项）。</para>
    /// <para>交错验证意图由<b>测试结构保证</b>（评审 002-F6）：readerRunning 握手使写侧
    /// 等 reader 起转后再 Register——读侧旋转与写侧活跃期构造性重叠；握手仅作结构同步
    /// 不断言（reader 极端调度延迟时本轮零交错，行为由双边界用例②登记承接）。零交错
    /// 场景的行为登记见 <see cref="A3_06_零交错场景判据不误报"/>。</para>
    /// </summary>
    [Fact]
    public void A3_06_写读并发_读侧枚举零撕裂异常()
    {
        RuntimeSnapshotStore.Clear();
        try
        {
            var target = typeof(A3_06_Dto);
            // 预注册 50 条 Channel=MapFrom 条目（读侧全量枚举燃料——Fluent 早退会缩短枚举窗口）
            for (var i = 0; i < 50; i++)
            {
                RuntimeSnapshotStore.Register(Meta(typeof(A3_06_Fuel), target));
            }

            const int dynCount = 3000;
            var dynTypes = new Type[dynCount];
            for (var i = 0; i < dynCount; i++)
            {
                dynTypes[i] = CreateBareDynamicType(i);
            }

            var stop = new ManualResetEventSlim(false);
            var readerRunning = new ManualResetEventSlim(false);
            Exception? readerEx = null;
            long readCount = 0; // 观测值：交错密度登记用，不作断言（负载敏感判据缺陷的教训）
            var reader = Task.Run(() =>
            {
                try
                {
                    readerRunning.Set(); // 握手：reader 已进入调度
                    while (!stop.IsSet)
                    {
                        _ = RuntimeSnapshotStore.IsFluentRegisteredDto(target);
                        readCount++;
                    }
                }
                catch (Exception ex)
                {
                    readerEx = ex;
                }
            });

            // 交错结构保证（非断言）：写侧等 reader 起转（最多 5s）后再 Register，
            // 使读侧旋转与写侧活跃期构造性重叠。握手失败仅表示本轮零交错（环境极端
            // 线程池饥饿），不判红——零交错行为由双边界用例②登记承接。
            var readerStarted = readerRunning.Wait(TimeSpan.FromSeconds(5));

            // 写线程：紧凑循环 Register（无类型创建开销——最大化写读交错密度）
            foreach (var t in dynTypes)
            {
                RuntimeSnapshotStore.Register(Meta(t, target));
                if (readerEx != null) break;
            }
            stop.Set();
            var readerCompleted = reader.Wait(TimeSpan.FromSeconds(30)); // 启用返回值（原被丢弃）

            // 判据①：reader 在超时窗口内完成（健全性——卡死/未调度的测试无效态显式暴露，
            // 不再像旧版静默吞掉 Wait 返回值）；判定与机器快慢无关（只依赖"已调度"前提）。
            Assert.True(readerCompleted, $"reader 未在超时窗口内完成（观测 readCount={readCount}，握手={readerStarted}）");
            // 判据②（真契约，原样保留）：读侧枚举零撕裂异常。
            Assert.Null(readerEx); // 现状：InvalidOperationException("Collection was modified") → 红
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
        }
    }

    /// <summary>
    /// A3_06 双边界用例 B（登记性验证）：读线程被剥夺场景——
    /// 写侧先完成全部 Register，reader 后启动且立即收到停止信号 → <b>零交错</b>。
    /// 预期判据<b>判绿</b>（零交错不误报）：完成锚定型判据对"交错/零交错"无区分度，
    /// 这是设计属性而非守卫真空——交错场景的守卫由主测试握手结构（用例①）承载。
    /// <para>登记值：零交错下 readCount 可为 0（reader 起转即退），判据不受其影响。</para>
    /// </summary>
    [Fact]
    public void A3_06_零交错场景判据不误报()
    {
        RuntimeSnapshotStore.Clear();
        try
        {
            var target = typeof(A3_06_Dto);
            var dynTypes = new Type[100];
            for (var i = 0; i < 100; i++)
            {
                dynTypes[i] = CreateBareDynamicType(10_000 + i);
            }
            // 写侧先完成全部 Register（reader 尚未启动 → 结构性零交错）
            foreach (var t in dynTypes)
            {
                RuntimeSnapshotStore.Register(Meta(t, target));
            }

            var stop = new ManualResetEventSlim(true); // 预置停止：reader 起转即退（零交错登记）
            Exception? readerEx = null;
            long readCount = 0; // 观测值：预期 0 或极小（起转即退）
            var reader = Task.Run(() =>
            {
                try
                {
                    while (!stop.IsSet)
                    {
                        _ = RuntimeSnapshotStore.IsFluentRegisteredDto(target);
                        readCount++;
                    }
                }
                catch (Exception ex)
                {
                    readerEx = ex;
                }
            });
            var readerCompleted = reader.Wait(TimeSpan.FromSeconds(30));

            Assert.True(readerCompleted, $"reader 未在超时窗口内完成（观测 readCount={readCount}）");
            Assert.Null(readerEx); // 零交错 + 修复后读侧锁内快照 → 零异常（判绿，零交错不误报）
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
        }
    }

    // ============================== A3-20 ==============================

    /// <summary>
    /// A3-20 红测：Clear 分清非原子——写线程持续 Register（重建注册面）+ 清线程持续
    /// Clear（s_all 锁内清、byTarget/byPair 锁外清——分清窗口）；现状交错产生孤儿
    /// 条目（s_all 有而 byPair 无，红：FindPair 断言失败）；修复后三清整体入锁，
    /// 终态三容器一致。
    /// <para>动态类型池做大字典（Clear 耗时随容量上升 → 分清窗口展宽 → 交错可复现）。</para>
    /// </summary>
    [Fact]
    public void A3_20_Clear分清非原子_并发Register终态三容器一致()
    {
        RuntimeSnapshotStore.Clear();
        try
        {
            var target = typeof(A3_20_Dto);
            const int poolSize = 2500;
            var pool = new Type[poolSize];
            for (var i = 0; i < poolSize; i++)
            {
                pool[i] = CreateBareDynamicType(5_000 + i);
            }

            var stop = new ManualResetEventSlim(false);
            var clearer = Task.Run(() =>
            {
                while (!stop.IsSet) RuntimeSnapshotStore.Clear();
            });
            var writer = Task.Run(() =>
            {
                while (!stop.IsSet)
                {
                    foreach (var t in pool)
                    {
                        RuntimeSnapshotStore.Register(Meta(t, target));
                    }
                }
            });

            Thread.Sleep(600); // 交错窗口
            stop.Set();
            Task.WaitAll(clearer, writer);

            // 终态一致性（孤儿断言）：s_all 每条在 byPair 与 byTarget 均可查
            var orphans = 0;
            foreach (var e in RuntimeSnapshotStore.All)
            {
                if (RuntimeSnapshotStore.FindByPair(e.SourceType, e.TargetType) == null) orphans++;
                else if (!RuntimeSnapshotStore.FindByTarget(e.TargetType).Contains(e)) orphans++;
            }
            Assert.Equal(0, orphans); // 现状：分清窗口交错 → 孤儿 > 0（红）
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
        }
    }

    // ============================== 辅助 ==============================

    private static RuntimeMapperMetadata Meta(Type src, Type tgt)
        => new()
        {
            SourceType = src,
            TargetType = tgt,
            SourceTypeFqn = src.FullName!,
            TargetTypeFqn = tgt.FullName!,
            Channel = RuntimeMappingChannel.MapFrom,
            PropertyNoAutoIncludeTargets = Array.Empty<string>(),
            Properties = Array.Empty<RuntimePropertyMapping>(),
        };

    private static Type CreateBareDynamicType(int seed)
    {
        var asmName = new AssemblyName($"T64Stress_{seed}_{Guid.NewGuid():N}");
        var assembly = AssemblyBuilder.DefineDynamicAssembly(asmName, AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule("Main");
        var tb = module.DefineType($"Dyn{seed}", TypeAttributes.Public | TypeAttributes.Sealed);
        _ = tb.DefineDefaultConstructor(MethodAttributes.Public);
        return tb.CreateType()!;
    }

    // ============================== 模型 ==============================

    private sealed class A3_06_Dto { public int Id { get; set; } }
    private sealed class A3_06_Fuel { public int Id { get; set; } }
    private sealed class A3_20_Dto { public int Id { get; set; } }
}
