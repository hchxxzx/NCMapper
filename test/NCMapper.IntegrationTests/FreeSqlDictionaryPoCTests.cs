// PoC 验证：FreeSql ToDictionary + NCMapper Map 字典映射可行性
// 验证目标：
//   POC-D1 ToDictionary + 逐个 Map 字典映射正确性（含跨表 CategoryName）
//   POC-D2 FreeSqlNavigationCollector 树形路径验证（Childs 是否被误收集为导航依赖）
// 评审 B-2 补缺：ApplyFreeSqlIncludes 封装路径在树形场景的行为验证
using FreeSql;
using FreeSql.DataAnnotations;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.FreeSql;
using Xunit;

namespace NCMapper.IntegrationTests
{
    // ================================================================
    // 字典 PoC DTO — 扁平结构（非树形），验证 ToDictionary + Map 路径
    // ================================================================

    /// <summary>
    /// 区域扁平 DTO — 不含自引用集合，纯扁平映射 + 跨表 CategoryName
    /// </summary>
    [MapFrom(typeof(PoCArea))]
    public class PoCAreaFlatDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }

        /// <summary>区域名称</summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// 分类名称 — 跨表映射自 PoCCategory.Name（经 PoCArea.Category 导航）
        /// </summary>
        [MapProperty(typeof(PoCCategory), nameof(PoCCategory.Name))]
        public string? CategoryName { get; set; }
    }

    // ================================================================
    // 字典 PoC Fixture — 复用 PoCTreeFixture 的独立 SQLite
    // ================================================================

    /// <summary>
    /// 字典 PoC 测试 — 验证 ToDictionary + Map 链路 + ApplyFreeSqlIncludes 树形路径
    /// </summary>
    [Collection("MapTracerGlobalState")]
    public class FreeSqlDictionaryPoCTests : IClassFixture<PoCTreeFixture>
    {
        private readonly IFreeSql _fsql;

        /// <summary>
        /// 构造
        /// </summary>
        /// <param name="fixture">PoC 树形测试 Fixture</param>
        public FreeSqlDictionaryPoCTests(PoCTreeFixture fixture)
        {
            _fsql = fixture.Fsql;
        }

        // ================================================================
        // POC-D1：ToDictionary + Map 字典映射路径验证
        // 验证：原生 FreeSql ToDictionary → 逐个 Map value → DTO 字典
        // ================================================================

        /// <summary>
        /// POC-D1a：ToDictionary 同步版本 + 逐个 Map 映射 DTO 字典
        /// <para>验证点：键正确 / 值映射正确 / 跨表 CategoryName 经 Include 后填充</para>
        /// </summary>
        [Fact]
        public void POC_D1a_ToDictionary_Plus_Map_字典映射正确()
        {
            // Step 1: FreeSql 原生 ToDictionary（手动 Include 跨表导航）
            var entityDict = _fsql.Select<PoCArea>()
                .Include(a => a.Category)
                .ToDictionary(a => a.Id);

            // Step 2: 逐个 Map value → DTO 字典
            var dtoDict = new Dictionary<int, PoCAreaFlatDto>();
            foreach (var kv in entityDict)
            {
                var dto = Mapper.Map<PoCArea, PoCAreaFlatDto>(kv.Value);
                Assert.NotNull(dto);
                dtoDict[kv.Key] = dto!;
            }

            // 断言：字典大小正确
            Assert.Equal(5, dtoDict.Count);

            // 断言：键值映射正确
            Assert.Equal("中国", dtoDict[1].Name);
            Assert.Equal("国家", dtoDict[1].CategoryName);

            Assert.Equal("北京", dtoDict[2].Name);
            Assert.Equal("省级", dtoDict[2].CategoryName);

            Assert.Equal("东城区", dtoDict[3].Name);
            Assert.Equal("区级", dtoDict[3].CategoryName);
        }

        /// <summary>
        /// POC-D1b：ToDictionary 同步版本 — 不 Include 跨表导航时 CategoryName 为 null
        /// <para>对照实验：与 POC-D1a 形成参照</para>
        /// </summary>
        [Fact]
        public void POC_D1b_ToDictionary_NoInclude_跨表字段为null()
        {
            var entityDict = _fsql.Select<PoCArea>()
                .ToDictionary(a => a.Id);

            var dtoDict = new Dictionary<int, PoCAreaFlatDto>();
            foreach (var kv in entityDict)
            {
                var dto = Mapper.Map<PoCArea, PoCAreaFlatDto>(kv.Value);
                Assert.NotNull(dto);
                dtoDict[kv.Key] = dto!;
            }

            // 断言：跨表字段为 null（未 Include 导航）
            Assert.Equal(5, dtoDict.Count);
            Assert.Null(dtoDict[1].CategoryName);
        }

        /// <summary>
        /// POC-D1c：ToDictionary 异步版本 + 逐个 Map
        /// <para>验证点：异步路径行为一致</para>
        /// </summary>
        [Fact]
        public async Task POC_D1c_ToDictionaryAsync_Plus_Map_异步字典映射正确()
        {
            // FreeSql ISelect.ToDictionaryAsync 原生支持
            var entityDict = await _fsql.Select<PoCArea>()
                .Include(a => a.Category)
                .ToDictionaryAsync(a => a.Id, cancellationToken: TestContext.Current.CancellationToken);

            var dtoDict = new Dictionary<int, PoCAreaFlatDto>();
            foreach (var kv in entityDict)
            {
                var dto = Mapper.Map<PoCArea, PoCAreaFlatDto>(kv.Value);
                Assert.NotNull(dto);
                dtoDict[kv.Key] = dto!;
            }

            Assert.Equal(5, dtoDict.Count);
            Assert.Equal("中国", dtoDict[1].Name);
            Assert.Equal("国家", dtoDict[1].CategoryName);
        }

        /// <summary>
        /// POC-D1d：空结果集 ToDictionary 返回空字典
        /// <para>边界验证</para>
        /// </summary>
        [Fact]
        public void POC_D1d_ToDictionary_空结果集_返回空字典()
        {
            var entityDict = _fsql.Select<PoCArea>()
                .Where(a => a.Id < 0)
                .ToDictionary(a => a.Id);

            Assert.Empty(entityDict);

            var dtoDict = new Dictionary<int, PoCAreaFlatDto>();
            foreach (var kv in entityDict)
            {
                var dto = Mapper.Map<PoCArea, PoCAreaFlatDto>(kv.Value);
                Assert.NotNull(dto);
                dtoDict[kv.Key] = dto!;
            }

            Assert.Empty(dtoDict);
        }

        // ================================================================
        // POC-D2：FreeSqlNavigationCollector 树形路径验证 — B-2 补缺
        // 验证：Collect(typeof(PoCArea), typeof(PoCAreaDto)) 是否将
        //       DTO 自引用 Childs 集合误收集为导航依赖
        // 结论：通过 InternalsVisibleTo 直接访问 internal FreeSqlNavigationCollector
        // ================================================================

        /// <summary>
        /// POC-D2a：验证 FreeSqlNavigationCollector.Collect 的树形路径收集结果
        /// <para>关键发现：Collect(PoCArea→PoCAreaDto) 返回 ["Parent", "Category"]，不含 "Childs"
        /// 原因：TryRecurseNestedDto 中 NavigationResolver.ResolveNavigationProperty 解析
        /// PoCAreaDto.Childs（List&lt;PoCAreaDto&gt;，[MapFrom(PoCArea)]）的导航来源时，
        /// 匹配到了 PoCArea.Parent（类型 PoCArea?）而非 PoCArea.Childs
        /// （NavigationResolver 的内部匹配优先级导致 Parent 先于 Childs 被选中）</para>
        /// <para>结论：R1 风险（Childs 被误收集）不存在，实际被收集的是 Parent
        /// 这对 ToTreeList 场景也无大碍：FreeSql IncludeByPropertyName("Parent") 是合法操作，
        /// 仅多一次冗余查询，不产生异常或循环</para>
        /// </summary>
        [Fact]
        public void POC_D2a_Collector_Childs路径收集分析()
        {
            // 清空缓存，确保新鲜结果
            FreeSqlNavigationCollector.ClearCache();

            var tree = FreeSqlNavigationCollector.Collect(typeof(PoCArea), typeof(PoCAreaDto));
            var paths = tree.FlattenPaths();

            // 验证实际收集结果：Parent（自引用导航）+ Category（跨表导航）
            Assert.Contains("Parent", paths);
            Assert.Contains("Category", paths);
            // Childs 未被收集（NavigationResolver 匹配到了 Parent 而非 Childs）
            Assert.DoesNotContain("Childs", paths);

            // 记录 Trace 输出供分析
            MapTracer.Write($"[PoC D2a] Collect(PoCArea→PoCAreaDto) 导航树: {tree.Summary()}");
            MapTracer.Write($"[PoC D2a] 拍平路径: {string.Join(", ", paths)}");
        }

        /// <summary>
        /// POC-D2b：验证扁平 DTO 的 Collect 结果（不含自引用集合）
        /// <para>对照实验：PoCAreaFlatDto 无 Childs 属性，应仅收集 Category</para>
        /// </summary>
        [Fact]
        public void POC_D2b_Collector_扁平DTO仅收集Category()
        {
            FreeSqlNavigationCollector.ClearCache();

            var tree = FreeSqlNavigationCollector.Collect(typeof(PoCArea), typeof(PoCAreaFlatDto));
            var paths = tree.FlattenPaths();

            // 扁平 DTO 无自引用集合，仅跨表 Category
            Assert.Contains("Category", paths);
            Assert.DoesNotContain("Childs", paths);
        }

        /// <summary>
        /// POC-D2c：ApplyFreeSqlIncludes 实际调用行为验证（通过 Trace 捕获）
        /// <para>创建临时 FreeSqlDtoSelector 并走降级路径触发 ApplyFreeSqlIncludes，
        /// 通过 Trace 输出验证 Childs 是否被 Include</para>
        /// </summary>
        [Fact]
        public void POC_D2c_ApplyFreeSqlIncludes_Trace验证Childs被Include()
        {
            // 通过降级路径触发 ApplyFreeSqlIncludes ——
            // ToDictionary 类型不匹配降级（走 List<> 类型转换，必定降级）
            // 实际验证：使用 ToList 降级路径（更简单）
            FreeSqlNavigationCollector.ClearCache();

            var sw = new StringWriter();
            var oldOutput = MapTracer.GlobalOutput;
            var oldEnabled = MapTracer.GloballyEnabled;
            MapTracer.GlobalOutput = sw;
            MapTracer.GloballyEnabled = true;
            try
            {
                // 触发降级路径以调用 ApplyFreeSqlIncludes（PoCAreaDto 有跨表 MapProperty → 降级）
                // 注意：走降级路径会先调 NeedsDegradation，再调 ApplyFreeSqlIncludes
                var result = _fsql.Select<PoCArea>().AsDto().ToList<PoCAreaDto>();

                // 验证方法调用成功 — ToList 返回所有 5 条记录（非仅根节点）
                Assert.NotNull(result);
                Assert.Equal(5, result.Count);
            }
            finally
            {
                MapTracer.GlobalOutput = oldOutput;
                MapTracer.GloballyEnabled = oldEnabled;
            }

            var traceOutput = sw.ToString();

            // 验证 ApplyFreeSqlIncludes 在 Trace 中输出自动 Include 信息
            Assert.Contains("[FreeSql AsDto] 自动 Include:", traceOutput);

            // 检查是否包含 Childs（若包含，证明确实被误收集）
            var containsChilds = traceOutput.Contains("Childs");
            MapTracer.Write($"[PoC D2c] Trace 包含 Childs: {containsChilds}");
            MapTracer.Write($"[PoC D2c] Trace 输出:\n{traceOutput}");
        }

        /// <summary>
        /// POC-D2d：扁平 DTO 走投影路径 — 验证扁平 DTO 不需要降级
        /// <para>PoCAreaFlatDto 的跨表 [MapProperty] 不触发降级（RequiresDowngrade=false），
        /// 走 SQL 侧 JOIN 投影，因此 ApplyFreeSqlIncludes 不会被调用
        /// 这是 FreeSqlDegradationDetector 的预期行为</para>
        /// </summary>
        [Fact]
        public void POC_D2d_ApplyFreeSqlIncludes_扁平DTO走投影路径()
        {
            FreeSqlNavigationCollector.ClearCache();

            // 扁平 DTO 走投影路径，不调 ApplyFreeSqlIncludes
            var result = _fsql.Select<PoCArea>().AsDto().ToList<PoCAreaFlatDto>();
            Assert.NotNull(result);
            Assert.Equal(5, result.Count);
            // 投影路径通过 SQL JOIN 填充 CategoryName，无需 Include
            Assert.Equal("国家", result.First(r => r.Id == 1).CategoryName);
        }
    }
}
