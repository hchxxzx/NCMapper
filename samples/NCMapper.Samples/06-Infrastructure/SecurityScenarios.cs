namespace NCMapper.Samples.Infrastructure;

// ================================================================
// 安全阈值调整场景集（06-Infra 域）— 直转文档绑定源（B1 第一波）。
// 文档演示专用：不注册 ScenarioRegistry 执行路径，仅编译与 DFM 引用。
// ================================================================

/// <summary>
/// 安全阈值场景集（直转文档 Core_Security_RecursionDepth 的绑定源）。
/// </summary>
public static class SecurityScenarios
{
    /// <summary>调整最大递归深度（默认 64，硬上界 4096）：容纳更深对象图，用后恢复默认。</summary>
    #region Core_Security_RecursionDepth
    public static void RecursionDepthTune()
    {
        int saved = Mapper.MaxRecursionDepth;
        try
        {
            // Raise the cycle-detection ceiling for deeper object graphs (default 64, hard bound 4096)
            Mapper.MaxRecursionDepth = 128;
        }
        finally
        {
            // Restore default: global knob, must not leak across scenarios
            Mapper.MaxRecursionDepth = saved;
        }
    }
    #endregion
}
