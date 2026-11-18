using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using NCMapper.Attributes;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// 整改任务书验收测试 — 覆盖 2026-07-22 整改任务书中缺失的专项验收用例
    /// 注：C-01(string→int?)/C-02(ObservableCollection)/H-07(private init) 的修复在源生成器路径，
    /// 运行时表达式树路径行为不同，相关验证在 NCMapper.SourceGen.Tests 中完成。
    /// </summary>
    [Collection("DeepCopyGlobalState")]
    public class AuditAcceptanceTests
    {
        // ================================================================
        // C-05: DeepCopy 对无公共构造器类型不 StackOverflow
        // ================================================================

        /// <summary>仅含 private 无参构造器的类型</summary>
        public class PrivateCtorOnlyType
        {
            public string Name { get; set; } = "";
            public int Value { get; set; }
            private PrivateCtorOnlyType() { }
            public static PrivateCtorOnlyType Create(string name, int value)
                => new() { Name = name, Value = value };
        }

        /// <summary>
        /// C-05 验收：无公共构造器类型 DeepCopy 不 StackOverflow（循环 1000 次）
        /// 使用 FastDeepCopyProvider（JSON 路径可处理私有构造器）
        /// </summary>
        [Fact]
        public void C05_PrivateCtor_DeepCopy_No_StackOverflow()
        {
            var original = DeepCopyProviderBase.Current;
            try
            {
                DeepCopyProviderBase.Current = new FastDeepCopyProvider();

                // 模拟源生成器为无公共构造器类型生成的代码：调用 DeepCopyRuntimeOnly 走运行时路径
                Mapper.RegisterGeneratedDeepCopy<PrivateCtorOnlyType>((src, ctx) =>
                    Mapper.DeepCopyRuntimeOnly<PrivateCtorOnlyType>(src, ctx)!);

                var instance = PrivateCtorOnlyType.Create("Test", 42);

                // 循环 1000 次验证不 StackOverflow（核心验收标准）
                for (int i = 0; i < 1000; i++)
                {
                    var copy = Mapper.DeepCopy(instance);
                    Assert.NotNull(copy);
                    Assert.NotSame(instance, copy);
                }
            }
            finally
            {
                Mapper.RegisterGeneratedDeepCopy<PrivateCtorOnlyType>((Func<PrivateCtorOnlyType, DeepCopyContext, PrivateCtorOnlyType>?)null);
                DeepCopyProviderBase.Current = original;
            }
        }

        /// <summary>
        /// C-05 验收：正常 POCO DeepCopy 行为不变
        /// </summary>
        [Fact]
        public void C05_Normal_POCO_DeepCopy_Unchanged()
        {
            var original = DeepCopyProviderBase.Current;
            try
            {
                DeepCopyProviderBase.Current = new FastDeepCopyProvider();

                Mapper.RegisterGeneratedDeepCopy<SimplePocoForDeepCopy>((src, ctx) =>
                {
                    var dst = new SimplePocoForDeepCopy();
                    Mapper.RegisterDeepCopyTarget(src, dst, ctx);
                    dst.Name = src.Name;
                    dst.Number = src.Number;
                    return dst;
                });

                var instance = new SimplePocoForDeepCopy { Name = "Hello", Number = 99 };
                var copy = Mapper.DeepCopy(instance);

                Assert.NotNull(copy);
                Assert.Equal("Hello", copy!.Name);
                Assert.Equal(99, copy.Number);
                Assert.NotSame(instance, copy);
            }
            finally
            {
                Mapper.RegisterGeneratedDeepCopy<SimplePocoForDeepCopy>((Func<SimplePocoForDeepCopy, DeepCopyContext, SimplePocoForDeepCopy>?)null);
                DeepCopyProviderBase.Current = original;
            }
        }

        public class SimplePocoForDeepCopy
        {
            public string Name { get; set; } = "";
            public int Number { get; set; }
        }

        // ================================================================
        // C-06: Provider 切换导致 DeepCopy 中途 NRE
        // ================================================================

        /// <summary>
        /// C-06 验收：Fast Provider 递归中途切换 Current 为 ExpressionTree Provider，不抛 NRE
        /// </summary>
        [Fact]
        public void C06_Provider_Switch_MidRecursion_No_NRE()
        {
            var original = DeepCopyProviderBase.Current;
            try
            {
                // 使用 Fast Provider 开始（不初始化 CycleCache）
                DeepCopyProviderBase.Current = new FastDeepCopyProvider();

                var node = new DeepNodeForC06 { Id = 1, Name = "Root" };
                node.Child = new DeepNodeForC06 { Id = 2, Name = "Child" };
                node.Child.Child = new DeepNodeForC06 { Id = 3, Name = "GrandChild" };

                // 注册生成委托：在第二层递归时切换 Provider
                int depth = 0;
                Mapper.RegisterGeneratedDeepCopy<DeepNodeForC06>((src, ctx) =>
                {
                    depth++;
                    if (depth == 2)
                    {
                        // 中途切换到 ExpressionTree Provider（其基类路径使用 CycleCache）
                        DeepCopyProviderBase.Current = new ExpressionTreeDeepCopyProvider();
                    }
                    // 走运行时路径（模拟 C-05 修复后的行为）
                    return Mapper.DeepCopyRuntimeOnly<DeepNodeForC06>(src, ctx)!;
                });

                // 不应抛 NRE
                var copy = Mapper.DeepCopy(node);
                Assert.NotNull(copy);
                Assert.Equal(1, copy!.Id);
            }
            finally
            {
                Mapper.RegisterGeneratedDeepCopy<DeepNodeForC06>((Func<DeepNodeForC06, DeepCopyContext, DeepNodeForC06>?)null);
                DeepCopyProviderBase.Current = original;
            }
        }

        /// <summary>
        /// C-07 验收：并发切换 Current（Fast/ExpressionTree）+ 并发 DeepCopy — 单次调用结果一致（C7 补测）
        /// <para>断言形态（报告 §五 C7）：零异常 + 结果等价 + 切换计数守恒（期望总量法 E18 参照）。
        /// Current 切换瞬间单次 DeepCopy 应完整使用调用时的 provider 快照，产物字段与源一致
        /// （无混合策略产物）；切换计数守恒验证并发切换确实发生（防测试空转）。</para>
        /// </summary>
        [Fact]
        public async Task C07_Provider_ConcurrentSwitch_DeepCopy_ResultsConsistent()
        {
            var original = DeepCopyProviderBase.Current;
            int switchCount = 0;
            try
            {
                using var cts = new CancellationTokenSource();
                var failures = new System.Collections.Concurrent.ConcurrentBag<string>();

                // 线程1: 并发切换 Fast/ExpressionTree provider（切换计数守恒）
                // do-while 保证至少执行一次切换（net10 线程调度延迟可致 20ms 窗口内未获 CPU → 空转误报，D6 修复）
                var switcher = Task.Run(() =>
                {
                    int toggle = 0;
                    do
                    {
                        try
                        {
                            DeepCopyProviderBase.Current = (toggle++ & 1) == 0
                                ? new FastDeepCopyProvider()
                                : new ExpressionTreeDeepCopyProvider();
                            System.Threading.Interlocked.Increment(ref switchCount);
                        }
                        catch (Exception ex) { failures.Add($"切换异常: {ex.Message}"); }
                    } while (!cts.Token.IsCancellationRequested);
                }, TestContext.Current.CancellationToken);

                // 线程2: 并发 DeepCopy，断言结果等价（固定字段守恒）
                var copier = Task.Run(() =>
                {
                    var src = new SimplePocoForDeepCopy { Name = "concurrent", Number = 7 };
                    while (!cts.Token.IsCancellationRequested)
                    {
                        try
                        {
                            var copy = DeepCopyProviderBase.Current.DeepCopyObject(src);
                            if (copy is not SimplePocoForDeepCopy typed)
                            {
                                failures.Add($"产物类型异常: {copy?.GetType().Name ?? "null"}");
                                continue;
                            }

                            // 结果等价：固定字段守恒（无混合策略产物）
                            if (typed.Name != "concurrent" || typed.Number != 7)
                                failures.Add($"字段不守恒: Name={typed.Name}, Number={typed.Number}");
                        }
                        catch (Exception ex) { failures.Add($"DeepCopy 异常: {ex.Message}"); }
                    }
                }, TestContext.Current.CancellationToken);

                Thread.Sleep(20);
                cts.Cancel();
                await Task.WhenAll(switcher, copier);

                // 零异常 + 结果等价
                Assert.True(failures.IsEmpty,
                    $"并发切换/深拷贝问题: {string.Join("; ", failures.Take(3))}");
                // 切换计数守恒：并发切换确实发生
                Assert.True(switchCount > 0, "provider 切换空转（未发生任何切换）");
            }
            finally
            {
                DeepCopyProviderBase.Current = original;
            }
        }

        /// <summary>
        /// C-06 验收：Fast Provider 无环快路径 CycleCache 零写入（零开销验证）
        /// <para>不变式变更（方案 F -2，任务书验收标准 #3）：
        /// 层 1 Provider 转发前补 <c>ctx.CycleCache ??= new(...)</c> 修复嵌套无环调用 NPE，
        /// 取代方案 E 的"转发前保持 null"不变式。新不变式：CycleCache 允许被懒初始化，
        /// 但 Fast Provider 自身路径不写入（Count == 0，零环检测开销语义不变）。</para>
        /// <para>模型变更：方案 F 分类分发后，零写入语义仅适用于
        /// Acyclic 快路径；原模型 DeepNodeForC06 为自引用类型（分类 Cyclic，转发基类完整检测
        /// 会写入 CycleCache），故改用无自引用的 AcyclicNodeForC06 保持本用例原语义。
        /// Cyclic 对照见 <see cref="C06_FastProvider_CyclicType_FullDetection_WritesCache"/>。</para>
        /// </summary>
        [Fact]
        public void C06_FastProvider_CycleCache_NoEntries()
        {
            var original = DeepCopyProviderBase.Current;
            try
            {
                DeepCopyProviderBase.Current = new FastDeepCopyProvider();

                var ctx = new DeepCopyContext();
                // 调用前 CycleCache 未初始化
                Assert.Null(ctx.CycleCache);

                var node = new AcyclicNodeForC06 { Id = 1, Name = "Test" };
                var copy = Mapper.DeepCopy(node, ctx);

                Assert.NotNull(copy);
                // 层 1 转发分支懒初始化 CycleCache（方案 F -2 NPE 修复），
                // 但 Acyclic 快路径自身不写入任何条目（零环检测开销）
                Assert.NotNull(ctx.CycleCache);
                Assert.Empty(ctx.CycleCache);
            }
            finally
            {
                DeepCopyProviderBase.Current = original;
            }
        }

        /// <summary>
        /// C-06 对照：Cyclic 类型经分发器转发基类完整检测，
        /// CycleCache 正常写入（分类分发的正确性验证，与零写入快路径互为对照）
        /// </summary>
        [Fact]
        public void C06_FastProvider_CyclicType_FullDetection_WritesCache()
        {
            var original = DeepCopyProviderBase.Current;
            try
            {
                DeepCopyProviderBase.Current = new FastDeepCopyProvider();

                var ctx = new DeepCopyContext();
                var node = new DeepNodeForC06 { Id = 1, Name = "Test" };

                var copy = Mapper.DeepCopy(node, ctx);

                Assert.NotNull(copy);
                // Cyclic 转发基类完整检测：节点进入 CycleCache（至少根节点一条）
                Assert.NotNull(ctx.CycleCache);
                Assert.NotEmpty(ctx.CycleCache);
            }
            finally
            {
                DeepCopyProviderBase.Current = original;
            }
        }

        /// <summary>
        /// C-06 验收：基类路径 CycleCache 正常初始化和使用
        /// </summary>
        [Fact]
        public void C06_BaseProvider_CycleCache_Initialized()
        {
            var original = DeepCopyProviderBase.Current;
            try
            {
                DeepCopyProviderBase.Current = new ExpressionTreeDeepCopyProvider();

                var node = new DeepNodeForC06 { Id = 1, Name = "Root" };
                node.Child = new DeepNodeForC06 { Id = 2, Name = "Child" };

                var ctx = new DeepCopyContext();
                var copy = Mapper.DeepCopy(node, ctx);

                Assert.NotNull(copy);
                // 基类路径在 Depth==0 时初始化 CycleCache
                Assert.NotNull(ctx.CycleCache);
            }
            finally
            {
                DeepCopyProviderBase.Current = original;
            }
        }

        public class DeepNodeForC06
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public DeepNodeForC06? Child { get; set; }
        }

        /// <summary>
        /// C-06 无环模型：无自引用 → 分类 Acyclic，
        /// 走 Fast 快路径验证 CycleCache 零写入
        /// </summary>
        public class AcyclicNodeForC06
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
        }

        // ================================================================
        // M-02: Nullable<T>→Nullable<U> 编译期处理
        // ================================================================

        public class NullableSourceEntity
        {
            public int? IntNullable { get; set; }
            public long? LongNullable { get; set; }
            public decimal? DecimalNullable { get; set; }
            public int? SameTypeNullable { get; set; }
        }

        [MapFrom(typeof(NullableSourceEntity))]
        public class NullableTargetDto
        {
            public long? IntNullable { get; set; }       // int? → long?（可隐式转换）
            public decimal? LongNullable { get; set; }   // long? → decimal?（可隐式转换）
            public decimal? DecimalNullable { get; set; } // decimal? → decimal?（同类型）
            public int? SameTypeNullable { get; set; }   // int? → int?（同类型）
        }

        /// <summary>
        /// M-02 验收：int? → long? 值正确
        /// </summary>
        [Fact]
        public void M02_IntNullable_To_LongNullable_Mapped()
        {
            var src = new NullableSourceEntity { IntNullable = 42 };
            var dto = Mapper.Map<NullableSourceEntity, NullableTargetDto>(src)!;
            Assert.Equal(42L, dto.IntNullable);
        }

        /// <summary>
        /// M-02 验收：long? → decimal? 值正确
        /// </summary>
        [Fact]
        public void M02_LongNullable_To_DecimalNullable_Mapped()
        {
            var src = new NullableSourceEntity { LongNullable = 9999999999L };
            var dto = Mapper.Map<NullableSourceEntity, NullableTargetDto>(src)!;
            Assert.Equal(9999999999m, dto.LongNullable);
        }

        /// <summary>
        /// M-02 验收：int? → int?（同类型）直接赋值
        /// </summary>
        [Fact]
        public void M02_SameType_Nullable_DirectAssign()
        {
            var src = new NullableSourceEntity { SameTypeNullable = 7 };
            var dto = Mapper.Map<NullableSourceEntity, NullableTargetDto>(src)!;
            Assert.Equal(7, dto.SameTypeNullable);
        }

        /// <summary>
        /// M-02 验收：源为 null 时目标为 null（不抛异常）
        /// </summary>
        [Fact]
        public void M02_Null_Source_Nullable_Target_Null()
        {
            var src = new NullableSourceEntity
            {
                IntNullable = null,
                LongNullable = null,
                DecimalNullable = null,
                SameTypeNullable = null
            };
            var dto = Mapper.Map<NullableSourceEntity, NullableTargetDto>(src)!;
            Assert.Null(dto.IntNullable);
            Assert.Null(dto.LongNullable);
            Assert.Null(dto.DecimalNullable);
            Assert.Null(dto.SameTypeNullable);
        }

        // ================================================================
        // C-01: IsNonNullableToNullable — int → int?、long → decimal? 正常映射
        // 注：string→int? 的回退运行时转换在源生成器路径验证（SourceGen.Tests）
        // ================================================================

        public class NonNullToNullableSource
        {
            public int Count { get; set; } = 5;
            public long BigNumber { get; set; } = 123456789L;
        }

        [MapFrom(typeof(NonNullToNullableSource))]
        public class NonNullToNullableDto
        {
            public int? Count { get; set; }         // int → int?（正常 Nullable 提升）
            public decimal? BigNumber { get; set; } // long → decimal?（可隐式转换）
        }

        /// <summary>
        /// C-01 验收：int → int? 正常生成且映射正确
        /// </summary>
        [Fact]
        public void C01_Int_To_IntNullable_Mapped()
        {
            var src = new NonNullToNullableSource { Count = 5 };
            var dto = Mapper.Map<NonNullToNullableSource, NonNullToNullableDto>(src)!;
            Assert.Equal(5, dto.Count);
        }

        /// <summary>
        /// C-01 验收：long → decimal? 正常生成且映射正确
        /// </summary>
        [Fact]
        public void C01_Long_To_DecimalNullable_Mapped()
        {
            var src = new NonNullToNullableSource { BigNumber = 123456789L };
            var dto = Mapper.Map<NonNullToNullableSource, NonNullToNullableDto>(src)!;
            Assert.Equal(123456789m, dto.BigNumber);
        }

        // ================================================================
        // C-04: 继承的目标属性永远不会被映射
        // ================================================================

        public class DerivedSourceEntity
        {
            public int Id { get; set; } = 1;
            public DateTime CreatedAt { get; set; } = new(2024, 6, 1);
            public string Name { get; set; } = "Test";
        }

        /// <summary>目标基类 DTO</summary>
        public class BaseDto
        {
            public int Id { get; set; }
            public DateTime CreatedAt { get; set; }
        }

        /// <summary>目标派生类 DTO — 继承 BaseDto 的 Id/CreatedAt + 自身 Name</summary>
        [MapFrom(typeof(DerivedSourceEntity))]
        public class DerivedDto : BaseDto
        {
            public string Name { get; set; } = "";
        }

        /// <summary>
        /// C-04 验收：BaseDto { Id, CreatedAt } ← DerivedDto : BaseDto { Name }，继承属性被映射
        /// </summary>
        [Fact]
        public void C04_Inherited_Target_Properties_Mapped()
        {
            var src = new DerivedSourceEntity
            {
                Id = 42,
                CreatedAt = new DateTime(2025, 3, 15),
                Name = "InheritedTest"
            };
            var dto = Mapper.Map<DerivedSourceEntity, DerivedDto>(src)!;

            // 继承自 BaseDto 的属性
            Assert.Equal(42, dto.Id);
            Assert.Equal(new DateTime(2025, 3, 15), dto.CreatedAt);
            // 自身属性
            Assert.Equal("InheritedTest", dto.Name);
        }

        // ================================================================
        // H-07: public init 正常映射（private init/internal set 在 SourceGen.Tests 验证）
        // ================================================================

        public class AccessControlSource
        {
            public string PublicInit { get; set; } = "pub";
            public string NormalProp { get; set; } = "normal";
        }

        [MapFrom(typeof(AccessControlSource))]
        public class AccessControlDto
        {
            public string PublicInit { get; init; } = "";
            public string NormalProp { get; set; } = "";
        }

        /// <summary>
        /// H-07 验收：public init 属性正常映射
        /// </summary>
        [Fact]
        public void H07_PublicInit_Mapped()
        {
            var src = new AccessControlSource { PublicInit = "hello" };
            var dto = Mapper.Map<AccessControlSource, AccessControlDto>(src)!;
            Assert.Equal("hello", dto.PublicInit);
        }

        /// <summary>
        /// H-07 验收：正常 public set 属性正常映射
        /// </summary>
        [Fact]
        public void H07_NormalProp_Mapped()
        {
            var src = new AccessControlSource { NormalProp = "works" };
            var dto = Mapper.Map<AccessControlSource, AccessControlDto>(src)!;
            Assert.Equal("works", dto.NormalProp);
        }

        // ================================================================
        // M-09: PostMap 反转缓存过期
        // ================================================================

        /// <summary>
        /// M-09 验收：ExecutePostMap → AddPostMap → ExecutePostMap，新回调被执行
        /// </summary>
        [Fact]
        public void M09_PostMap_Cache_Invalidation()
        {
            var pipeline = new MapPipeline();
            var results = new List<string>();

            // 第一次注册
            pipeline.AddPostMap<AccessControlSource, AccessControlDto>((src, tgt) =>
                results.Add("first"));

            // 第一次执行
            pipeline.ExecutePostMap(new AccessControlSource(), new AccessControlDto());
            Assert.Single(results);
            Assert.Equal("first", results[0]);

            // 追加新回调（应清除缓存）
            pipeline.AddPostMap<AccessControlSource, AccessControlDto>((src, tgt) =>
                results.Add("second"));

            // 第二次执行 — 新回调应被执行（PostMap 逆序执行：second 先于 first）
            pipeline.ExecutePostMap(new AccessControlSource(), new AccessControlDto());
            Assert.Equal(3, results.Count);  // first + second + first
            // 关键验证：新注册的 "second" 回调确实被执行了（缓存已失效）
            Assert.Contains("second", results);

            pipeline.Clear();
        }

        // ================================================================
        // M-11: AddNCMapper() 非幂等
        // ================================================================

        /// <summary>
        /// M-11 验收：两次 AddNCMapper() 只注册一个 IMapper 实例
        /// </summary>
        [Fact]
        public void M11_Double_AddNCMapper_Idempotent()
        {
            var services = new ServiceCollection();
            services.AddNCMapper();
            services.AddNCMapper();

            var mapperRegistrations = services.Where(s => s.ServiceType == typeof(IMapper)).ToList();
            Assert.Single(mapperRegistrations);
        }

        /// <summary>
        /// M-11 验收：带 configure 的重载同样幂等
        /// </summary>
        [Fact]
        public void M11_AddNCMapper_With_Configure_Idempotent()
        {
            var services = new ServiceCollection();
            services.AddNCMapper(opts => { });
            services.AddNCMapper();

            var mapperRegistrations = services.Where(s => s.ServiceType == typeof(IMapper)).ToList();
            Assert.Single(mapperRegistrations);
        }

        // ================================================================
        // M-12: Trace 输出并发交错
        // ================================================================

        /// <summary>
        /// M-12 验收：并发 10 线程 Map + Trace，输出无交错（每行报告完整）
        /// </summary>
        [Fact]
        public async Task M12_Concurrent_Trace_No_Interleaving()
        {
            var output = new ConcurrentBag<string>();
            var originalEnabled = MapTracer.GloballyEnabled;
            var originalCallback = MapTracer.LogCallback;

            try
            {
                MapTracer.GloballyEnabled = true;
                MapTracer.LogCallback = report => output.Add(report);

                var tasks = Enumerable.Range(0, 10).Select(i => Task.Run(() =>
                {
                    for (int j = 0; j < 20; j++)
                    {
                        var src = new DerivedSourceEntity { Id = i, Name = $"Thread{i}_{j}" };
                        Mapper.Map<DerivedSourceEntity, DerivedDto>(src);
                    }
                })).ToArray();

                await Task.WhenAll(tasks);

                // 验证：每份报告都是完整的（包含类型名标识）
                Assert.True(output.Count >= 200, $"Expected >= 200 trace reports, got {output.Count}");
                foreach (var report in output)
                {
                    // 每份报告应包含源类型和目标类型名称（完整性标志）
                    Assert.Contains("DerivedSourceEntity", report);
                    Assert.Contains("DerivedDto", report);
                }
            }
            finally
            {
                MapTracer.GloballyEnabled = originalEnabled;
                MapTracer.LogCallback = originalCallback;
            }
        }
    }
}
