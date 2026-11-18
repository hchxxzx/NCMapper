using NCMapper;
using NCMapper.EFCore;
using NCMapper.FreeSql;
using NCMapper.Internal;
using NCMapper.IntegrationTests.Models;
using Xunit;

namespace NCMapper.IntegrationTests
{
    /// <summary>
    /// FreeSql-only 部署清理闭环测试
    /// <para>背景：RegisterProjectionCacheClear 全库仅 EFCore 一处注册（EfCoreProjectionBuilder.cs:31），
    /// FreeSql-only 部署下 #17 子表（orm-extension-clears）为空 → ProjectionExpressionFactory
    /// 共享内核缓存无人清理（R-1 漏清）。</para>
    /// <para>修复：FreeSql 扩展模块静态构造直登 CacheRegistry（#20 free-sql-projection 等），
    /// 不依赖 #17 子表。本测试验证该闭环。</para>
    /// <para>模拟方式（评审 I-1 定案）：单元级模拟——经 internal RegisterProjectionCacheClear(null)
    /// 清空子表（等价 FreeSql-only 时序：EFCore 静态构造从未执行），规避同进程 EFCore
    /// 静态构造已注册的时序问题；测试末尾恢复 EFCore 槽位（.cctor 不重跑，防污染后续测试）。</para>
    /// </summary>
    [Collection("PipelineGlobalState")]
    public class FreeSqlOnlyClearingTests
    {
        /// <summary>
        /// FreeSql-only 场景：#17 子表空置时，Mapper.ClearCache 经 CacheRegistry #20 条目
        /// 仍清空 ProjectionExpressionFactory 共享内核缓存（R-1 闭环）+ ClearCount 遥测递增
        /// </summary>
        [Fact]
        public void FreeSqlOnly_ProjectionFactoryCache_ClearedViaRegistry()
        {
            try
            {
                // 前置：触发 EFCore/FreeSql 扩展静态构造（EFCore 注册 #17 子表；FreeSql 直登 CacheRegistry）
                EfCoreProjectionBuilder.ClearCache();
                FreeSqlProjectionBuilder.ClearCache();
                Mapper.ClearCache(); // 确定性起点（清全部登记缓存，含共享内核）
                Assert.Equal(0, ProjectionExpressionFactory.CacheEntryCount);

                // 模拟 FreeSql-only：清空 #17 子表（EFCore 槽位移除，.cctor 不重跑）
                Mapper.RegisterProjectionCacheClear(null);

                // 构建 FreeSql 投影缓存（走共享内核，策略键 = FreeSql 深度 3）
#pragma warning disable IL2026 // Build 带 RequiresUnreferencedCode（测试项目不 trim，既有直调先例）
#pragma warning disable IL3050 // Build 带 RequiresDynamicCode（测试项目非 AOT）
                _ = FreeSqlProjectionBuilder.Build<UserEntity, UserDto>();
#pragma warning restore IL3050
#pragma warning restore IL2026
                Assert.True(ProjectionExpressionFactory.CacheEntryCount > 0, "前置：共享内核投影缓存非空");

                // ClearCache 走 CacheRegistry：#17 子表空转（无 EFCore 委托），#20 free-sql-projection 直接清理
                var clearCountBefore = CacheRegistry.ClearCount;
                Mapper.ClearCache();

                Assert.Equal(0, ProjectionExpressionFactory.CacheEntryCount); // R-1 闭环：无 EFCore 也能清
                Assert.Equal(clearCountBefore + 1, CacheRegistry.ClearCount);  // 遥测递增（ClearFull 入口 +1）
            }
            finally
            {
                // 恢复 EFCore 槽位（.cctor 不重跑，须手动重注册防污染同进程后续 EFCore 测试）
                Mapper.RegisterProjectionCacheClear(EfCoreProjectionBuilder.ClearCache);
            }
        }
    }
}
