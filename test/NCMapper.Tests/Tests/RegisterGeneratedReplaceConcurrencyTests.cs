using System.Collections.Concurrent;
using NCMapper;
using Xunit;

namespace NCMapper.Tests.Tests;

/// <summary>
/// RegisterGenerated* 运行中替换竞态测试（C6 补测）— 结果必为新/旧委托之一
/// <para>报告 §五 C6 断言形态：结果必为新/旧委托之一（volatile 单引用验证）。
/// 锚点：<see cref="Mapper.RegisterGeneratedCore{TSource, TTarget}"/>（CoreFunc 槽位）与
/// <see cref="Mapper.RegisterGenerated{TSource, TTarget}"/>（Func 槽位）写入的委托引用
/// 均为 volatile 字段，多线程下替换注册应保持引用读写的原子可见性，杜绝半更新/混合产物。</para>
/// </summary>
[Collection("GlobalConfigState")]
public class RegisterGeneratedReplaceConcurrencyTests
{
    /// <summary>
    /// 被测源类型 — 用于 RegisterGenerated 替换竞态验证
    /// </summary>
    private class RgSrc
    {
        /// <summary>
        /// 源标识字段
        /// </summary>
        public int Id { get; set; }
    }

    /// <summary>
    /// 被测目标类型 — Id 取值区分新旧委托产物（0=旧，1=新）
    /// </summary>
    private class RgTgt
    {
        /// <summary>
        /// 目标标识字段（0=旧委托产物，1=新委托产物）
        /// </summary>
        public int Id { get; set; }
    }

    /// <summary>
    /// CoreFunc（无 Action 热路径）替换竞态 — 并发 Map 结果必为新/旧委托之一
    /// <para>线程1 交替注册 Id=0/Id=1 两个委托；线程2 并发 Map。
    /// volatile 单引用语义下，任何一次 Map 读取的委托引用要么旧要么新，
    /// 产物 Id 必 ∈ {0, 1}；出现其他值即为引用撕裂/半更新证据。</para>
    /// </summary>
    [Fact]
    public async Task RegisterGeneratedCore_ReplaceDuringConcurrentMap_ResultIsOldOrNew()
    {
        Mapper.RegisterGeneratedCore<RgSrc, RgTgt>(src => new RgTgt { Id = 0 });
        try
        {
            using var cts = new CancellationTokenSource();
            var violations = new ConcurrentBag<string>();

            // 线程1: 交替替换新旧委托（写侧）
            var writer = Task.Run(() =>
            {
                int toggle = 0;
                while (!cts.Token.IsCancellationRequested)
                {
                    if ((toggle++ & 1) == 0)
                        Mapper.RegisterGeneratedCore<RgSrc, RgTgt>(src => new RgTgt { Id = 1 });
                    else
                        Mapper.RegisterGeneratedCore<RgSrc, RgTgt>(src => new RgTgt { Id = 0 });
                }
            }, TestContext.Current.CancellationToken);

            // 线程2: 并发 Map（读侧），断言产物必为新/旧之一
            var reader = Task.Run(() =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    var dto = Mapper.Map<RgSrc, RgTgt>(new RgSrc { Id = 99 });
                    if (dto is null || (dto.Id != 0 && dto.Id != 1))
                        violations.Add($"混合/空产物: Id={dto?.Id.ToString() ?? "null"}（既非新亦非旧）");
                }
            }, TestContext.Current.CancellationToken);

            Thread.Sleep(20);
            cts.Cancel();
            await Task.WhenAll(writer, reader);

            // volatile 单引用验证：替换期间任何一次读取均为完整的新或旧委托
            Assert.True(violations.IsEmpty,
                $"volatile 单引用违规: {string.Join("; ", violations.Take(3))}");
        }
        finally
        {
            Mapper.RegisterGeneratedCore<RgSrc, RgTgt>(null);
        }
    }

    /// <summary>
    /// 带 Action 路径（Func）替换竞态 — 结果必为新/旧委托之一
    /// <para>与 CoreFunc 路径互补：走 <see cref="GeneratedCache{T}"/>.Func 分支，
    /// 覆盖带 Action 的映射入口（Mapper.Map(source, action)）。</para>
    /// </summary>
    [Fact]
    public async Task RegisterGenerated_ReplaceDuringConcurrentMapWithAction_ResultIsOldOrNew()
    {
        Mapper.RegisterGenerated<RgSrc, RgTgt>((src, action) =>
        {
            var dst = new RgTgt { Id = 0 };
            action?.Invoke(src, dst);
            return dst;
        });
        try
        {
            using var cts = new CancellationTokenSource();
            var violations = new ConcurrentBag<string>();

            // 线程1: 交替替换新旧委托（写侧）
            var writer = Task.Run(() =>
            {
                int toggle = 0;
                while (!cts.Token.IsCancellationRequested)
                {
                    if ((toggle++ & 1) == 0)
                        Mapper.RegisterGenerated<RgSrc, RgTgt>((src, action) =>
                        {
                            var dst = new RgTgt { Id = 1 };
                            action?.Invoke(src, dst);
                            return dst;
                        });
                    else
                        Mapper.RegisterGenerated<RgSrc, RgTgt>((src, action) =>
                        {
                            var dst = new RgTgt { Id = 0 };
                            action?.Invoke(src, dst);
                            return dst;
                        });
                }
            }, TestContext.Current.CancellationToken);

            // 线程2: 并发 Map（带 Action），断言产物必为新/旧之一
            var reader = Task.Run(() =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    var dto = Mapper.Map<RgSrc, RgTgt>(new RgSrc { Id = 99 }, (src, dst) => { });
                    if (dto is null || (dto.Id != 0 && dto.Id != 1))
                        violations.Add($"混合/空产物: Id={dto?.Id.ToString() ?? "null"}（既非新亦非旧）");
                }
            }, TestContext.Current.CancellationToken);

            Thread.Sleep(20);
            cts.Cancel();
            await Task.WhenAll(writer, reader);

            Assert.True(violations.IsEmpty,
                $"volatile 单引用违规: {string.Join("; ", violations.Take(3))}");
        }
        finally
        {
            Mapper.RegisterGenerated<RgSrc, RgTgt>(null);
        }
    }
}
