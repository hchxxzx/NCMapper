// ============================================================================
// Red_S23 — ClearCache 后全局配置静默回退（P1 正确性）
//
// 缺陷（006 第 7 项）：ClearCache 后配置静默回退 ——GlobalMapConfig.Reset
// （242-249）同时清空 s_registered 与 s_frozen，导致 Mapper.ClearCache 后已注册的
// 全局映射配置丢失，重新冻结时经反射回退（无 [MapDefaults]）落到内建默认。
//
// 修复后期望（断言口径，006）：
//   Reset 只清 s_frozen 不清 s_registered —— ClearCache/Reset 后已注册配置保持，
//   重新冻结的生效配置与注册内容一致（不静默回退内建默认）。
//
// 当前基线（e59f1c9）落红形态（留档）：ClearCache（经 CacheRegistry 级联调用
// GlobalMapConfig.Reset）后 s_registered 被清空 → 重新冻结走反射回退 → 内建默认
//（IgnoreNames 空）→ after 视图不含注册项 → 断言失败 = 预期落红。
// ============================================================================

using Xunit;

namespace NCMapper.Tests.Tests;

public class Red_S23_ClearCacheConfigRetention
{
    [Fact]
    public void ClearCache后_已注册全局配置保持_不静默回退内建默认()
    {
        // 隔离前置：清冻结状态，防同进程其他测试已触发冻结导致 Register 冲突异常
        Mapper.ClearCache();

        // 模拟生成器注册特征配置（GlobalMapConfig.Register — internal，InternalsVisibleTo 可访问）
        GlobalMapConfig.Register(new GeneratedMapConfig
        {
            NamingStrategy = (int)NamingStrategy.CaseSensitive,
            IgnoreNames = ["RedS23SecretIgnoreName"],
        }, "Red_S23_Probe");

        // 冻结并验证已生效
        var before = Mapper.GetGlobalConfig()!;
        Assert.Contains("RedS23SecretIgnoreName", before.IgnoreNames);

        // ClearCache（级联 GlobalMapConfig.Reset）
        Mapper.ClearCache();

        // 修复后期望：Reset 只清 s_frozen 不清 s_registered → 重新冻结后配置保持；
        // 当前形态（留档）：s_registered 被清空 → 反射回退（无 [assembly: MapDefaults]）
        // → 内建默认（IgnoreNames 空）→ 落红
        var after = Mapper.GetGlobalConfig()!;
        Assert.Contains("RedS23SecretIgnoreName", after.IgnoreNames);
        Assert.Equal(NamingStrategy.CaseSensitive, after.NamingStrategy);

        // 修复后 Reset 保留注册表 → 本用例注册的配置驻留进程，须显式清理防污染后续用例
        GlobalMapConfig.ResetForTests();
    }
}
