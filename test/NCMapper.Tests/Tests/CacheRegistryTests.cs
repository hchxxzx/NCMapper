using System.Runtime.CompilerServices;
using NCMapper.Attributes;
using NCMapper.Internal;
using NCMapper.Shared;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// CacheRegistry 登记制测试 —（W3-2 ⑤ 生命周期治理）
    /// <para>覆盖测试矩阵：登记表完整性（防漏必红）/ Layer 分层语义 /
    /// 清理生效性（3 钩子 + ClearCount 遥测）/ 补漏等价（assignment-planner /
    /// navigation-tree-collector）/ MapperDelegateRegistry 边界（X3）/ Register 幂等 /
    /// 双清幂等（I-1）/ 并发快照安全 / ResetMetrics 级联（Q2）。</para>
    /// <para>登记表完整性断言采用"与基准表全量一致"（集合相等，Z-5 路径③）——
    /// 新增缓存模块漏登记 → 快照缺项必红；误登记边界项（MapperDelegateRegistry /
    /// GeneratedCache / GeneratedDeepCopyCache / MapperBase，X3 + 死角 1）→ 快照多项必红。</para>
    /// <para>因测试触发全局 ClearCache/ClearGenerated/ResetMetrics，加入 GlobalConfigState
    /// 串行集合（DisableParallelization，既有 CacheGovernanceTests 惯例）。</para>
    /// </summary>
    [Collection("GlobalConfigState")]
    public class CacheRegistryTests
    {
        /// <summary>
        /// 登记表基准（T-0 冻结 21 项 + 纳入 #22，Z-5 路径②：基准表更新为 22 项）
        /// <para>数据源 = #1-21 + FreeSqlDegradationDetector（原零调用者清理方法，
        /// Q3 评估结论：纳入，防扩展库漏登记）。快照完整性测试以此为断言数据源（防漏必红）。</para>
        /// </summary>
        private static readonly (string Name, CacheLayer Layer)[] BaselineEntries =
        {
            // Generated 层（ClearGenerated 清理）—— Mapper 静态构造注册 #1-4
            ("generated-funcs", CacheLayer.Generated),
            ("generated-list-funcs", CacheLayer.Generated),
            ("generated-compose-funcs", CacheLayer.Generated),
            ("generated-validate-funcs", CacheLayer.Generated),
            // Full 层（ClearCache 全清）—— Mapper 静态构造注册 #5-6/#17
            ("compose-map-delegate", CacheLayer.Full),
            ("compose-update-delegate", CacheLayer.Full),
            ("orm-extension-clears", CacheLayer.Full),
            // Full 层 —— 核心库 10 模块静态构造注册 #7-16
            ("property-matcher", CacheLayer.Full),
            ("map-validator", CacheLayer.Full),
            ("collection-copier", CacheLayer.Full),
            ("deep-copy-compiler", CacheLayer.Full),
            ("deep-copy-provider", CacheLayer.Full),
            ("deep-copy-classifier", CacheLayer.Full),
            ("reflection-helper", CacheLayer.Full),
            ("expression-builder", CacheLayer.Full),
            ("mask-provider", CacheLayer.Full),
            ("global-map-config", CacheLayer.Full),
            // Full 层 —— T-3 补漏注册 #18/#19（原 ClearCache 手写清单漏接清理链 2 处）
            ("assignment-planner", CacheLayer.Full),
            ("navigation-tree-collector", CacheLayer.Full),
            // Full 层 —— T-4 扩展库注册 #20/#21（R-1 闭环）+ Q3 纳入 #22
            ("free-sql-projection", CacheLayer.Full),
            ("free-sql-navigation", CacheLayer.Full),
            ("free-sql-degradation-detector", CacheLayer.Full),
            // Full 层 ——C-09：EFCore 检测器对齐 FreeSql 三件套登记制
            // （Instance 判定缓存纳入 Mapper.ClearCache 链，消除残留泄漏）
            ("ef-core-degradation-detector", CacheLayer.Full),
            // Full 层 ——#23（DegradationDetectorBase.Agnostic 静态构造登记：
            // EvaluateChannelAgnostic 独立缓存纳入 Mapper.ClearCache 全链清理）
            ("degradation-detector-agnostic", CacheLayer.Full),
            // Full 层 ——C-13：EFCore 树挂载位编译委托缓存（C-17 登记制，
            // NCMapperEFCoreExtensions cctor 注册）；补登第 25 项
            // （上游 20260921-001"防漏登记门按设计正确报警"定性）
            ("efcore-tree-slot-accessors", CacheLayer.Full),
            // Full 层 ——#26（RuntimeSnapshotStore cctor 登记：M-14-B
            // 反射腿正/负结果缓存 s_mapFromProbe 纳入 Mapper.ClearCache 全链清理）
            ("runtime-snapshot-probe", CacheLayer.Full),
            // Full 层 ——#27（LeafStructGuard cctor 登记：M-19 补
            // ClearCache 链唯一漏网，s_cache 纳入全链清理）
            ("leaf-struct-guard", CacheLayer.Full),
            // Full 层 ——#28（RuntimeSnapshotStore cctor 登记：
            // [MapNoAutoMatch] 白名单探针 s_noAutoMatchProbe 纳入 Mapper.ClearCache 全链清理——
            // 独立探针缓存不复用 s_mapFromProbe，v1.2 评审方案书侧-）
            ("runtime-noautomatch-probe", CacheLayer.Full),
            // Full 层 —— 运行时注册表明细腿新增（脱敏类型名解析缓存纳入 Mapper.ClearCache 全链）
            ("mask-type-name-resolver", CacheLayer.Full),
        };

        /// <summary>
        /// 每个测试用例执行前清空全局缓存，避免跨用例缓存状态泄漏（既有 GlobalConfigState 集合惯例）
        /// </summary>
        public CacheRegistryTests()
        {
            Mapper.ClearCache();
        }

        /// <summary>
        /// 预热全部登记模块 ——显式触碰各模块静态成员触发静态构造（评审，未触发 .cctor
        /// 的模块不在登记表 → 快照断言假红）。核心库 19 项直接触碰；FreeSql 3 项因
        /// NCMapper.Tests 无 FreeSql internal 编译期访问权（csproj 仅放行 IntegrationTests），
        /// 经 public 入口类型取程序集后按名反射 RunClassConstructor 强制触发；
        /// EFCore 2 项（efcore-tree-slot-accessors + ef-core-degradation-detector，
        /// 补预热）同样按名反射触发——二者此前依赖跨测试竞速触发
        /// （实测基态全量 net8/net9 失败集翻面、单类跑缺项），不预热则快照缺项（详见下方块注）。
        /// </summary>
        private static void WarmUpAllModules()
        {
            // Mapper（#1-6/#17）—— ClearGenerated 触发 Mapper .cctor
            Mapper.ClearGenerated();
            // 核心库 10 模块（#7-16）—— 各自成员访问触发 .cctor（Clear/Reset 幂等安全）
            PropertyMatcher.ClearCache();
            MapValidator.ClearCache();
            CollectionCopier.ClearCache();
            DeepCopyCompiler.ClearCache();
            DeepCopyProviderBase.ClearCache();
            _ = DeepCopyTypeClassifier.CacheEntryCount;
            ReflectionHelper.ClearCache();
            _ = ExpressionBuilder.CacheEntryCount;
            MaskProvider.Clear();
            GlobalMapConfig.Reset();
            // T-3 补漏 2 项（#18/#19）
            AssignmentPlanner.ClearCache();
            NavigationTreeCollector.ClearCache();
            // 运行时注册表明细腿新增（解析缓存登记）—— 访问即触发静态构造（确定性预热）
            NCMapper.Runtime.MaskTypeNameResolver.ClearCache();
            // （#26）——RuntimeSnapshotStore 静态构造（runtime-snapshot-probe 登记；
            // Clear 幂等安全，访问即触发 .cctor）
            NCMapper.Runtime.RuntimeSnapshotStore.Clear();
            // （#27）——LeafStructGuard 静态构造（leaf-struct-guard 登记；
            // ClearCache 幂等安全，访问即触发 .cctor）
            LeafStructGuard.ClearCache();
            // （#23）——DegradationDetectorBase 静态构造（agnostic 登记）
            DegradationDetectorBase.ClearAgnosticCache();
            // FreeSql 扩展 3 项（#20/#21/#22）—— 反射触发 .cctor（RunClassConstructor 幂等：已初始化则 no-op）
            var freeSqlAssembly = typeof(NCMapper.FreeSql.NCMapperFreeSqlExtensions).Assembly;
            foreach (var typeName in new[]
            {
                "NCMapper.FreeSql.FreeSqlProjectionBuilder",
                "NCMapper.FreeSql.FreeSqlNavigationCollector",
                "NCMapper.FreeSql.FreeSqlDegradationDetector",
            })
            {
                var type = freeSqlAssembly.GetType(typeName)
                    ?? throw new InvalidOperationException($"FreeSql 程序集缺少类型 {typeName}（登记模块移位？）");
                RuntimeHelpers.RunClassConstructor(type.TypeHandle);
            }
            // EFCore 扩展 2 项（efcore-tree-slot-accessors + ef-core-degradation-detector）——
            // 按名反射触发各自 cctor。二者注册此前依赖跨测试触发，
            // 基态 0bb0b4d 实测：全量跑中与其他集合并行竞速（net8/net9 失败集翻面）、单类跑
            // 快照 Actual 23（ef-core-degradation-detector 缺席）——顺序敏感 flaky；WarmUp
            // 确定性补齐后竞态消解。EfCoreDegradationDetector 为 internal（对齐 FreeSql 侧
            // 按名反射先例）；RunClassConstructor 幂等：已初始化则 no-op。
            var efCoreAssembly = typeof(NCMapper.EFCore.NCMapperEFCoreExtensions).Assembly;
            foreach (var typeName in new[]
            {
                "NCMapper.EFCore.NCMapperEFCoreExtensions",
                "NCMapper.EFCore.EfCoreDegradationDetector",
            })
            {
                var type = efCoreAssembly.GetType(typeName)
                    ?? throw new InvalidOperationException($"EFCore 程序集缺少类型 {typeName}（登记模块移位？）");
                RuntimeHelpers.RunClassConstructor(type.TypeHandle);
            }
        }

        // ================================================================
        // 1. 登记表完整性（防漏必红）——评审
        // ================================================================

        /// <summary>
        /// 预热后快照与基准表全量一致（集合相等）—— 防漏登记核心断言：
        /// 新增缓存模块未注册 → 快照缺项（数量/名称均不符）必红；
        /// 误登记 X3 边界项（MapperDelegateRegistry 清空不可恢复）/死角 1 泛型槽位
        /// （GeneratedCache/GeneratedDeepCopyCache/MapperBase）→ 快照多项必红。
        /// </summary>
        [Fact]
        public void Snapshot_AfterWarmUp_MatchesBaselineExactly()
        {
            WarmUpAllModules();

            var snapshot = CacheRegistry.Snapshot();
            var actual = snapshot.ToDictionary(e => e.Name, e => e.Layer);

            Assert.Equal(BaselineEntries.Length, actual.Count);
            foreach (var (name, layer) in BaselineEntries)
            {
                Assert.True(actual.TryGetValue(name, out var actualLayer),
                    $"登记表缺少条目 [{name}]（新增缓存模块漏登记——防漏必红）");
                Assert.Equal(layer, actualLayer);
            }
        }

        // ================================================================
        // 2. Layer 分层语义 —— ClearGenerated 只清 Generated 层，Full 层不动
        // ================================================================

        /// <summary>
        /// ClearGenerated 后 Full 层三类可观测缓存不受影响 + ClearCount 遥测递增
        /// <para>Generated 层（4 项生成字典）被清的行为由既有 20+ 测试类隔离模式回归兜底
        /// （方案 B：钩子 + 遥测 + 回归兜底三件套）。</para>
        /// <para>载体用独立类型对组 B（语义：MapperBase 静态 Lazy 一经物化不受 ClearCache
        /// 清除，同类型对二次 Map 不再触发编译——须独立类型对隔离测试间物化顺序）。</para>
        /// </summary>
        [Fact]
        public void ClearGenerated_LeavesFullLayerIntact()
        {
            WarmUpAllModules();
            Mapper.ClearCache();

            // 构建 Full 层可观测缓存：属性字典 + 深拷贝分类（组 B 独立类型对，防 Lazy 物化顺序影响）
            _ = Mapper.Map<PlanSrcB, PlanDtoB>(new PlanSrcB { Id = 1, Name = "b" });
            _ = DeepCopyTypeClassifier.Classify(typeof(PlanSrcB));
            Assert.True(PropertyMatcher.CacheEntryCount.Properties > 0, "前置：PropertyMatcher 缓存非空");
            Assert.True(DeepCopyTypeClassifier.CacheEntryCount > 0, "前置：DeepCopyTypeClassifier 缓存非空");

            var clearCountBefore = CacheRegistry.ClearCount;
            Mapper.ClearGenerated();

            // Full 层钩子不受 ClearGenerated 影响（Generated ⊆ Full 子集关系的逆否：ClearGenerated 不触 Full）
            Assert.True(PropertyMatcher.CacheEntryCount.Properties > 0,
                "ClearGenerated 不得清理 Full 层条目（property-matcher）");
            Assert.True(DeepCopyTypeClassifier.CacheEntryCount > 0,
                "ClearGenerated 不得清理 Full 层条目（deep-copy-classifier）");
            Assert.Equal(clearCountBefore + 1, CacheRegistry.ClearCount);
        }

        // ================================================================
        // 3. 清理生效性（方案 B：3 钩子精确断言 + ClearCount 遥测）
        // ================================================================

        /// <summary>
        /// ClearCache 后三类钩子归零 + ClearCount 遥测递增 ——R-1 方案 B 核心
        /// <para>钩子：PropertyMatcher.CacheEntryCount（属性字典）/
        /// DeepCopyTypeClassifier.CacheEntryCount（深拷贝分类）/
        /// ExpressionBuilder.CacheEntryCount（方向歧义去重集，既有钩子）。</para>
        /// </summary>
        [Fact]
        public void ClearCache_ThreeHooks_Zeroed()
        {
            WarmUpAllModules();
            Mapper.ClearCache();

            // 构建 PropertyMatcher + DeepCopyTypeClassifier 缓存
            _ = Mapper.Map<PlanSrcA, PlanDtoA>(new PlanSrcA { Id = 1, Name = "a" });
            _ = DeepCopyTypeClassifier.Classify(typeof(PlanSrcA));
            Assert.True(PropertyMatcher.CacheEntryCount.Properties > 0, "前置：PropertyMatcher 缓存非空");
            Assert.True(DeepCopyTypeClassifier.CacheEntryCount > 0, "前置：DeepCopyTypeClassifier 缓存非空");

            var clearCountBefore = CacheRegistry.ClearCount;
            Mapper.ClearCache();

            Assert.Equal(0, PropertyMatcher.CacheEntryCount.Properties);
            Assert.Equal(0, DeepCopyTypeClassifier.CacheEntryCount);
            Assert.Equal(clearCountBefore + 1, CacheRegistry.ClearCount);
        }

        // ================================================================
        // 4. 补漏等价 —— T-3 #18/#19 清后重算结果一致（P1 可重算前提）
        // ================================================================

        /// <summary>
        /// AssignmentPlanner 清后重规划结果一致（#18 补漏等价，证伪 F1 巡检项）
        /// <para>AssignmentPlan 为 readonly record struct，值语义等价断言。</para>
        /// </summary>
        [Fact]
        public void AssignmentPlanner_ClearThenReplan_Equivalent()
        {
            WarmUpAllModules();

            var srcProp = typeof(PlanSrcA).GetProperty(nameof(PlanSrcA.Id))!;
            var dstProp = typeof(PlanDtoA).GetProperty(nameof(PlanDtoA.Id))!;
            var plan1 = AssignmentPlanner.Plan(srcProp, dstProp, isToDto: true, attr: null, crossTableFromSource: null);

            Mapper.ClearCache(); // 含 #18 补漏清理（原手写清单漏接）

            var plan2 = AssignmentPlanner.Plan(srcProp, dstProp, isToDto: true, attr: null, crossTableFromSource: null);
            Assert.Equal(plan1, plan2); // 可重算等价：清后重规划结果一致
        }

        /// <summary>
        /// NavigationTreeCollector 清后重建导航树结果一致（#19 补漏等价，证伪 F1 巡检项）
        /// </summary>
        [Fact]
        public void NavigationTreeCollector_ClearThenRebuild_Equivalent()
        {
            WarmUpAllModules();

            var tree1 = NavigationTreeCollector.Collect(typeof(NavOrder), typeof(NavOrderDto));
            Assert.False(tree1.IsEmpty, "前置：载体类型对确有跨表导航");

            Mapper.ClearCache(); // 含 #19 补漏清理（原清理方法源码零调用者）

            var tree2 = NavigationTreeCollector.Collect(typeof(NavOrder), typeof(NavOrderDto));
            Assert.Equal(tree1.Roots.Count, tree2.Roots.Count); // 重建导航根数一致
            for (var i = 0; i < tree1.Roots.Count; i++)
            {
                Assert.Equal(tree1.Roots[i].Name, tree2.Roots[i].Name); // 逐根导航属性名一致
            }
        }

        // ================================================================
        // 5. MapperDelegateRegistry 边界 —— X3：不接清理链（清空不可恢复语义保持）
        // ================================================================

        /// <summary>
        /// ClearCache 后 MapperDelegateRegistry 已初始化类型对 Resolve 仍命中（行为不变）
        /// <para>边界语义（IMapperBaseDelegate.cs:99-104）：注册中心不在登记表——.cctor 不重跑，
        /// 清空不可恢复；ClearCache 不得影响其已注册条目（测试④幂等性验证对象不受影响）。</para>
        /// </summary>
        [Fact]
        public void MapperDelegateRegistry_Boundary_PreservedAfterClearCache()
        {
            WarmUpAllModules();

            // 先解析触发 MapperBase<,> .cctor 注册 + 字典命中
            var impl1 = MapperDelegateRegistry.Resolve(typeof(PlanSrcA), typeof(PlanDtoA));

            Mapper.ClearCache();

            // 边界保持：已初始化类型对仍命中且为同一实例（ClearCache 不清注册中心）
            var impl2 = MapperDelegateRegistry.Resolve(typeof(PlanSrcA), typeof(PlanDtoA));
            Assert.Same(impl1, impl2);
        }

        // ================================================================
        // 6. Register 幂等覆盖 —— 同名重注册条目数不增
        // ================================================================

        /// <summary>
        /// 同名重复注册（等价方法组）→ 条目数不增（幂等覆盖，无累加）
        /// <para>覆盖物为与 .cctor 注册完全相同的方法组（NavigationTreeCollector.ClearCache），
        /// 覆盖后登记状态等价——测试自愈零污染（Register 无 Unregister，等价覆盖是唯一
        /// 无损幂等验证路径）。</para>
        /// </summary>
        [Fact]
        public void Register_SameName_IdempotentNoAccumulation()
        {
            WarmUpAllModules();
            var before = CacheRegistry.Snapshot().Count;

            CacheRegistry.Register("navigation-tree-collector", CacheLayer.Full, NavigationTreeCollector.ClearCache);
            CacheRegistry.Register("navigation-tree-collector", CacheLayer.Full, NavigationTreeCollector.ClearCache);

            Assert.Equal(before, CacheRegistry.Snapshot().Count); // 索引器覆盖语义：同名不累加
        }

        // ================================================================
        // 7. 双清幂等 —— I-1：#19 + #21 双清同一 NavigationTreeCollector 缓存无害
        // ================================================================

        /// <summary>
        /// ClearFull 连续两次执行无异常且重建守恒 —— #19 navigation-tree-collector 与
        /// #21 free-sql-navigation 的清理委托均指向 NavigationTreeCollector.ClearCache
        /// （薄壳委托内核），单次 ClearFull 内同一缓存被双清——幂等无害（I-1 断言；
        /// 审计维度留备注防未来误判为缺陷）。
        /// </summary>
        [Fact]
        public void ClearFull_Twice_NavigationCache_IdempotentNoThrow()
        {
            WarmUpAllModules();

            var tree = NavigationTreeCollector.Collect(typeof(NavOrder), typeof(NavOrderDto));
            Assert.False(tree.IsEmpty, "前置：载体类型对确有跨表导航");

            Mapper.ClearCache(); // 单次 ClearFull：#19 与 #21 依次执行同一 ClearCache（双清）
            Mapper.ClearCache(); // 连续两次：重复清理幂等无害

            var rebuilt = NavigationTreeCollector.Collect(typeof(NavOrder), typeof(NavOrderDto));
            Assert.Equal(tree.Roots.Count, rebuilt.Roots.Count); // 重建守恒
        }

        // ================================================================
        // 8. 并发快照安全 —— ToArray 显式快照（ClearCore 遍历期间并发 Register）
        // ================================================================

        /// <summary>
        /// 并发 Register + ClearFull 不抛异常 —— ConcurrentDictionary 原子写 +
        /// ToArray 显式快照（遍历期间并发注册仅影响下次清理）
        /// <para>并发覆盖物为等价方法组（与 .cctor 注册相同），覆盖语义下最终状态等价。</para>
        /// </summary>
        [Fact]
        public async Task ClearFull_ConcurrentRegister_NoThrow()
        {
            WarmUpAllModules();

            var errors = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
            var tasks = new List<Task>();
            for (var t = 0; t < 4; t++)
            {
                tasks.Add(Task.Run(() =>
                {
                    for (var i = 0; i < 200; i++)
                    {
                        try
                        {
                            CacheRegistry.Register("property-matcher", CacheLayer.Full, PropertyMatcher.ClearCache);
                            Mapper.ClearCache(); // ClearFull 遍历 ToArray 快照
                        }
                        catch (Exception ex)
                        {
                            errors.Enqueue(ex);
                        }
                    }
                }, TestContext.Current.CancellationToken));
            }

            await Task.WhenAll(tasks.ToArray());
            Assert.Empty(errors);
        }

        // ================================================================
        // 9. ResetMetrics 级联 ——与 CacheMetrics.Reset 同层级联 ResetCounters
        // ================================================================

        /// <summary>
        /// Mapper.ResetMetrics 级联 CacheRegistry.ResetCounters ——遥测归零断言
        /// </summary>
        [Fact]
        public void ResetMetrics_Cascades_CacheRegistryCounters()
        {
            WarmUpAllModules();
            Mapper.ClearCache(); // ClearCount 递增
            CacheRegistry.Register("property-matcher", CacheLayer.Full, PropertyMatcher.ClearCache); // RegisterCount 递增
            Assert.True(CacheRegistry.ClearCount > 0, "前置：ClearCount 已递增");
            Assert.True(CacheRegistry.RegisterCount > 0, "前置：RegisterCount 已递增");

            Mapper.ResetMetrics(); // 级联点（Mapper.cs ResetMetrics 与 CacheMetrics.Reset 同层）

            Assert.Equal(0, CacheRegistry.ClearCount);
            Assert.Equal(0, CacheRegistry.RegisterCount);
        }

        // ================================================================
        // 测试载体类型（无 [MapFrom] —— 走运行时编译路径触发各缓存模块）
        // ================================================================

        /// <summary>
        /// 载体 — 源类型（Plan 等价 + 钩子观测 + MapperDelegateRegistry 边界共用）
        /// </summary>
        private class PlanSrcA
        {
            /// <summary>主键</summary>
            public int Id { get; set; }

            /// <summary>名称</summary>
            public string Name { get; set; } = "";
        }

        /// <summary>
        /// 载体 — 目标类型
        /// </summary>
        private class PlanDtoA
        {
            /// <summary>主键</summary>
            public int Id { get; set; }

            /// <summary>名称</summary>
            public string Name { get; set; } = "";
        }

        /// <summary>
        /// 载体 — 源类型组 B（ClearGenerated 分层测试专用，隔离 MapperBase Lazy 物化顺序）
        /// </summary>
        private class PlanSrcB
        {
            /// <summary>主键</summary>
            public int Id { get; set; }

            /// <summary>名称</summary>
            public string Name { get; set; } = "";
        }

        /// <summary>
        /// 载体 — 目标类型组 B
        /// </summary>
        private class PlanDtoB
        {
            /// <summary>主键</summary>
            public int Id { get; set; }

            /// <summary>名称</summary>
            public string Name { get; set; } = "";
        }

        /// <summary>
        /// 载体 — 导航树源实体（含导航属性 Customer）
        /// </summary>
        private class NavOrder
        {
            /// <summary>主键</summary>
            public int Id { get; set; }

            /// <summary>客户导航属性</summary>
            public NavCustomer? Customer { get; set; }
        }

        /// <summary>
        /// 载体 — 导航属性类型
        /// </summary>
        private class NavCustomer
        {
            /// <summary>客户名</summary>
            public string Name { get; set; } = "";
        }

        /// <summary>
        /// 载体 — 导航树目标 DTO（[MapProperty] 跨表属性触发导航收集；
        /// 补锚——[MapProperty] 消费以所在类有锚为前提）
        /// </summary>
        [MapFrom(typeof(NavOrder))]
        private class NavOrderDto
        {
            /// <summary>主键</summary>
            public int Id { get; set; }

            /// <summary>跨表属性：从 NavCustomer.Name 收集（Customer != NavOrder → 导航树非空）</summary>
            [MapProperty(typeof(NavCustomer), "Name")]
            public string CustomerName { get; set; } = "";
        }

        // ------------------------------------------------------------------
        // A2-02：Plan 缓存清理链回归——守卫分支不影响既有清理
        // ------------------------------------------------------------------

        /// <summary>
        /// 静态+动态混合 Plan 入缓存后 Mapper.ClearCache()（Full 层 → "assignment-planner" 登记项）
        /// → s_planCache.Count 归零。A2-02 动态直算守卫只影响 Plan 入口路径，缓存清理链
        /// （静态构造 CacheRegistry.Register 接入）行为不变——缺陷态（清理漏接/守卫误伤清理）红。
        /// </summary>
        [Fact]
        public void AssignmentPlanner_ClearCache_EmptiesPlanCache_AfterMixedPlans()
        {
            var planCache = (System.Collections.IDictionary)(typeof(AssignmentPlanner)
                .GetField("s_planCache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                ?? throw new InvalidOperationException("s_planCache field missing"))
                .GetValue(null)!;

            var staticProp = typeof(PlanSrcB).GetProperty("Id")!;
            AssignmentPlanner.Plan(staticProp, staticProp, isToDto: true, attr: null, crossTableFromSource: null);
            Assert.True(planCache.Count >= 1); // 静态对已入缓存（守卫不拦截静态路径）

            Mapper.ClearCache();
            Assert.Equal(0, planCache.Count); // Full 层清理覆盖 s_planCache（守卫分支不影响清理链）
        }
    }
}
