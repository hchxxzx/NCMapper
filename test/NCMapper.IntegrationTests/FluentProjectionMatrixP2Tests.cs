// （ORM 集成缺口清零翻译规则逐类矩阵）
// Fluent 投影翻译矩阵扩充。双形态（转正后实态）
// ① 手工注册模拟快照（可翻译类——DTO 不带生成锚，走注册表消费；扁平化/索引段用例）；
// ② 特性锚形态（Mask 用例——[MapFrom]+[Mask]：SourceGen 经核心库 Analyzer 自动挂载〔M-13〕，
//    P2ViewMask 对有生成资产，Mapper 降级二步映射优先取生成列——HasGenerated 先验锚锁定实走腿）。
// 可翻译类断言投影消费；不可翻译类（Convert / Format / FromJson / MapWhen）断言强制降级
// （NeedsDegradation=true）。边界：运行时明细消费拓展缺口由需求登记簿承接。
// 隔离范式：[Collection("PipelineGlobalState")] + ClearRegistryState 四清（快照 + Mapper.ClearCache
// + 两检测器；增补投影缓存清理链，防 s_cache 跨用例串扰）。
using Microsoft.EntityFrameworkCore;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.EFCore;
using NCMapper.Runtime;
using Xunit;

namespace NCMapper.IntegrationTests;

[Collection("PipelineGlobalState")]
public class FluentProjectionMatrixP2Tests : IDisposable
{
    // ================================================================
    // 夹具（命名避开 Dto 后缀——NCMAP019 启发式噪音规避）
    // ================================================================

    public class P2Entity
    {
        public int Id { get; set; }
        public string RealName { get; set; } = "";
        public string Phone { get; set; } = "";
        public System.DateTime Stamp { get; set; } = new(2024, 6, 1);
        public string Blob { get; set; } = "";
        public P2Addr Address { get; set; } = new();
        public System.Collections.Generic.List<P2Item> Items { get; set; } = new();
    }

    public class P2Addr { public string City { get; set; } = ""; }
    public class P2Item { public string Title { get; set; } = ""; }

    /// <summary>A-11 方向忽略专用视图（独立类型对——ProjectionExpressionFactory.s_cache 键 = (S,T)
    /// 无注册状态维度，与 A-02 用例共对会命中陈旧缓存表达式）。</summary>
    public class P2ViewDir { public int Id { get; set; } public string Phone { get; set; } = ""; }

    /// <summary>A-03 Mask 转正专用视图（特性锚形态——独立类型对防 s_cache / MapperBase 静态字段
    /// 物化串扰，P2ViewDir 先例同型；SourceGen 经 M-13 自动挂载，本对有生成资产）。</summary>
    [MapFrom(typeof(P2Entity))]
    public class P2ViewMask
    {
        public int Id { get; set; }

        [Mask(typeof(P2MaskImpl))]
        public string Phone { get; set; } = "";
    }

    /// <summary>A-03 Mask 实现（若被消费输出固定值 "MASKED"——GtBlogMask 先例同型）。</summary>
    public sealed class P2MaskImpl : IMask
    {
        public string Mask(string? value) => "MASKED";
    }

    /// <summary>对级白名单专用视图（[MapNoAutoMatch] 协议位降级承载锚
    /// 独立类型对防 s_cache 串扰，P2ViewMask 先例同型）。类级标注，属性无标注。</summary>
    [MapFrom(typeof(P2Entity))]
    [MapNoAutoMatch]
    public class P2ViewNoAutoMatch
    {
        public int Id { get; set; }
        public string RealName { get; set; } = "";
    }

    public class P2View
    {
        public int Id { get; set; }
        public string AliasName { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Stamp { get; set; } = "";
        public string Blob { get; set; } = "";
        public string City { get; set; } = "";
        public string FirstTitle { get; set; } = "";
    }

    private static void ClearRegistryState()
    {
        RuntimeSnapshotStore.Clear();
        // 隔离增强进投影缓存清理链——Mapper.ClearCache → CacheRegistry.ClearFull
        //（登记表驱动）含 ProjectionExpressionFactory.s_cache（键 = (Strategy, Source, Target)，无注册状态
        // 维度——经验库 #145），防手工注册形态跨用例命中陈旧投影表达式。
        Mapper.ClearCache();
        EfCoreDegradationDetector.ClearCache();
        NCMapper.FreeSql.FreeSqlDegradationDetector.ClearCache();
    }

    public FluentProjectionMatrixP2Tests()
    {
        Mapper.ClearCache(); // 构造器对齐黄金三角隔离范式（DotPathGoldenTripleTests 同款）——类入口净缓存
        var connStr = $"DataSource=file:p2mx{Guid.NewGuid():N}?mode=memory&cache=shared";
        _db = new P2DbContext(connStr);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        try { ClearRegistryState(); } catch { }
    }

    private sealed class P2DbContext : DbContext
    {
        private readonly string _connStr;
        public P2DbContext(string connStr) : base() => _connStr = connStr;
        public DbSet<P2Entity> P2Entities => Set<P2Entity>();
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // 导航复杂类型表化（模型表化——三 Skip 转正共同前提之一）
            // 原 Ignore 形态下 EF 对 Address/Items 客户端求值兜底，投影取值恒为空对象；
            // 表化（owned entity）后 SQL 直取列（Address_City / Items 元素子查询——P1 探针实证形态）
            modelBuilder.Entity<P2Entity>().OwnsOne(e => e.Address);
            modelBuilder.Entity<P2Entity>().OwnsMany(e => e.Items, b =>
            {
                b.Property<int>("Id");
                b.HasKey("Id");
            });
        }
        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseSqlite(_connStr);
    }

    private readonly P2DbContext _db;

    private static RuntimePropertyMapping Pm(string target, string source) => new()
    {
        Target = new RuntimeTargetPropertySlot { Name = target, TypeName = "String" },
        SourceName = source,
    };

    private static RuntimeMapperMetadata Meta(RuntimePropertyMapping[] props,
        string? beforeRef = null, string? afterRef = null, bool subclass = false,
        NCMapper.Runtime.RuntimeTreeConfig? tree = null)
    {
        var all = new System.Collections.Generic.List<RuntimePropertyMapping>
        {
            new() { Target = new RuntimeTargetPropertySlot { Name = "Id", TypeName = "Int32" }, SourceName = "Id" },
        };
        all.AddRange(props);
        return new RuntimeMapperMetadata
        {
            SourceType = typeof(P2Entity),
            TargetType = typeof(P2View),
            SourceTypeFqn = typeof(P2Entity).FullName!,
            TargetTypeFqn = typeof(P2View).FullName!,
            Channel = RuntimeMappingChannel.Fluent,
            IsFluentForward = true,
            Properties = all.ToArray(),
            AllTargetSlots = all.Select(p => p.Target).ToArray(),
            BeforeMappingMethodRef = beforeRef,
            AfterMappingMethodRef = afterRef,
            HasSubclassDispatch = subclass,
            TreeConfig = tree,
        };
    }

    private void SeedRow() => _db.P2Entities.Add(new P2Entity
    {
        Id = 1,
        RealName = "张三",
        Phone = "13812345678",
        Blob = """{"Id":1}""",
        Address = new() { City = "Shanghai" },
        Items = new() { new P2Item { Title = "t0" } },
    });

    // ================================================================
    // 可翻译类（投影消费）
    // ================================================================

    [Fact]
    [Trait("Capability", "A-07")] // 点路径扁平化 ORM 承载（转正——原 Skip 定性经探针修正为测试面：模型表化即绿）
    public void Translatable_FlattenedPointPath_Projected()
    {
        try
        {
            ClearRegistryState();
            RuntimeSnapshotStore.Register(Meta(new[]
            {
                new RuntimePropertyMapping
                {
                    Target = new RuntimeTargetPropertySlot { Name = "City", TypeName = "String" },
                    SourceName = "City",
                    SourcePathNames = new[] { "Address", "City" },
                    SourcePropertyTypeName = "String",
                    IsFlattened = true,
                },
            }));
            Assert.False(EfCoreDegradationDetector.NeedsDegradation(typeof(P2Entity), typeof(P2View), out _),
                "点路径扁平化为可翻译声明——不应强制降级");
            SeedRow();
            _db.SaveChanges();
            var rows = _db.P2Entities.OrderBy(e => e.Id).AsDto().ToList<P2View>();
            Assert.Single(rows);
            Assert.Equal("Shanghai", rows[0].City);
        }
        finally { ClearRegistryState(); }
    }

    [Fact]
    [Trait("Capability", "A-16")] // 索引段 ORM 承载（转正 src 修复落地后索引段消费闭环）
    public void Translatable_IndexSegment_Projected()
    {
        try
        {
            ClearRegistryState();
            RuntimeSnapshotStore.Register(Meta(new[]
            {
                new RuntimePropertyMapping
                {
                    Target = new RuntimeTargetPropertySlot { Name = "FirstTitle", TypeName = "String" },
                    SourceName = "Title",
                    SourcePathNames = new[] { "Items", "Title" },
                    SourceIndexes = new[] { 0, -1 }, // 对齐发射契约（等长 + -1 占位）索引段重建输入
                    SourcePropertyTypeName = "String",
                    HasIndex = true,
                },
            }));
            Assert.False(EfCoreDegradationDetector.NeedsDegradation(typeof(P2Entity), typeof(P2View), out _),
                "索引段为可翻译声明（翻译规则表）");
            SeedRow();
            _db.SaveChanges();
            var rows = _db.P2Entities.OrderBy(e => e.Id).AsDto().ToList<P2View>();
            Assert.Single(rows);
            Assert.Equal("t0", rows[0].FirstTitle);
        }
        finally { ClearRegistryState(); }
    }

    /// <summary>兼容形态：段名手带索引字面量 + SourceIndexes 并存重建防重语义锁定
    ///（段名已含 '[' 不叠加，防 Items[0][0] 双重索引；断言同主用例）。</summary>
    [Fact]
    [Trait("Capability", "A-16")]
    public void Translatable_IndexSegment_InlineForm_Projected()
    {
        try
        {
            ClearRegistryState();
            RuntimeSnapshotStore.Register(Meta(new[]
            {
                new RuntimePropertyMapping
                {
                    Target = new RuntimeTargetPropertySlot { Name = "FirstTitle", TypeName = "String" },
                    SourceName = "Title",
                    SourcePathNames = new[] { "Items[0]", "Title" }, // 手写索引字面量形态
                    SourceIndexes = new[] { 0, -1 }, // 并存——防重判据输入（已含 '[' 不叠加）
                    SourcePropertyTypeName = "String",
                    HasIndex = true,
                },
            }));
            Assert.False(EfCoreDegradationDetector.NeedsDegradation(typeof(P2Entity), typeof(P2View), out _),
                "手写索引字面量形态为可翻译声明（翻译规则表）");
            SeedRow();
            _db.SaveChanges();
            var rows = _db.P2Entities.OrderBy(e => e.Id).AsDto().ToList<P2View>();
            Assert.Single(rows);
            Assert.Equal("t0", rows[0].FirstTitle);
        }
        finally { ClearRegistryState(); }
    }

    // ================================================================
    // 不可翻译类（强制降级判定）
    // ================================================================

    [Fact]
    [Trait("Capability", "A-10")] // Convert ORM 强制降级承载
    public void Untranslatable_Converter_ForcesDegrade()
    {
        try
        {
            ClearRegistryState();
            RuntimeSnapshotStore.Register(Meta(new[]
            {
                new RuntimePropertyMapping
                {
                    Target = new RuntimeTargetPropertySlot { Name = "AliasName", TypeName = "String" },
                    SourceName = "RealName",
                    HasConverter = true,
                    ConverterDeclaringType = "Generated",
                    ConverterMethodName = "Map_Cv_X",
                },
            }));
            var needs = EfCoreDegradationDetector.NeedsDegradation(typeof(P2Entity), typeof(P2View), out _);
            Assert.True(needs, "Convert 承载不可投影翻译（委托/方法调用无 SQL 承载）——强制降级");
        }
        finally { ClearRegistryState(); }
    }

    [Fact]
    [Trait("Capability", "A-03")] // Mask ORM 强制降级承载
    public void Untranslatable_Mask_ForcesDegrade()
    {
        try
        {
            ClearRegistryState();
            RuntimeSnapshotStore.Register(Meta(new[]
            {
                new RuntimePropertyMapping
                {
                    Target = new RuntimeTargetPropertySlot { Name = "Phone", TypeName = "String" },
                    SourceName = "Phone",
                    HasMask = true,
                    MaskTypeName = "MaskPhone",
                },
            }));
            var needs = EfCoreDegradationDetector.NeedsDegradation(typeof(P2Entity), typeof(P2View), out _);
            Assert.True(needs, "Mask 脱敏（RequiresDowngrade 协议位）——强制降级");
        }
        finally { ClearRegistryState(); }
    }

    [Fact]
    [Trait("Capability", "A-23")] // Format ORM 强制降级承载
    public void Untranslatable_FormatString_ForcesDegrade()
    {
        try
        {
            ClearRegistryState();
            RuntimeSnapshotStore.Register(Meta(new[]
            {
                new RuntimePropertyMapping
                {
                    Target = new RuntimeTargetPropertySlot { Name = "Stamp", TypeName = "String" },
                    SourceName = "Stamp",
                    FormatString = "yyyy-MM-dd",
                },
            }));
            var needs = EfCoreDegradationDetector.NeedsDegradation(typeof(P2Entity), typeof(P2View), out _);
            Assert.True(needs, "Format 格式化不可投影翻译——强制降级");
        }
        finally { ClearRegistryState(); }
    }

    /// <summary> [MapNoAutoMatch] 协议位强制降级承载
    /// 白名单语义无法在 SQL 投影中表达（未声明列不得被读出），类级扫描臂（DegradationDetectorBase
    /// EvaluateTopLayers IProjectionPolicy 臂）命中即降级；reason 精确文案断言同时反向锁定
    /// "本特性不得加入 s_projectionIrrelevantAttrs 白名单"纪律（误入即放行列裁剪静默泄漏）。</summary>
    [Fact]
    [Trait("Capability", "A-25")] // MapNoAutoMatch ORM 强制降级承载（协议位腿）
    public void Untranslatable_MapNoAutoMatch_ForcesDegrade()
    {
        try
        {
            ClearRegistryState();
            var needs = EfCoreDegradationDetector.NeedsDegradation(typeof(P2Entity), typeof(P2ViewNoAutoMatch), out var reason);
            Assert.True(needs, "[MapNoAutoMatch] 白名单对投影强制降级（未声明列不得被读出）");
            Assert.Contains("MapNoAutoMatchAttribute（需要降级）", reason, StringComparison.Ordinal);
        }
        finally { ClearRegistryState(); }
    }

    [Fact]
    [Trait("Capability", "A-12")] // FromJson ORM 强制降级承载
    public void Untranslatable_IsFromJson_ForcesDegrade()
    {
        try
        {
            ClearRegistryState();
            RuntimeSnapshotStore.Register(Meta(new[]
            {
                new RuntimePropertyMapping
                {
                    Target = new RuntimeTargetPropertySlot { Name = "Blob", TypeName = "String" },
                    SourceName = "Blob",
                    IsFromJson = true,
                },
            }));
            var needs = EfCoreDegradationDetector.NeedsDegradation(typeof(P2Entity), typeof(P2View), out _);
            Assert.True(needs, "FromJson JSON 展开不可投影翻译——强制降级");
        }
        finally { ClearRegistryState(); }
    }

    [Fact]
    [Trait("Capability", "A-21")] // MapWhen ORM 强制降级承载
    public void Untranslatable_MapWhenCondition_ForcesDegrade()
    {
        try
        {
            ClearRegistryState();
            RuntimeSnapshotStore.Register(Meta(new[]
            {
                new RuntimePropertyMapping
                {
                    Target = new RuntimeTargetPropertySlot { Name = "AliasName", TypeName = "String" },
                    SourceName = "RealName",
                    ConditionMethodRef = "Generated::Map_When_X",
                },
            }));
            var needs = EfCoreDegradationDetector.NeedsDegradation(typeof(P2Entity), typeof(P2View), out _);
            Assert.True(needs, "MapWhen 条件守卫不可投影翻译——强制降级");
        }
        finally { ClearRegistryState(); }
    }

    // ================================================================
    // 降级后语义完整（Mask 不落明文——通用场景扩展，001 P2#2）
    // ================================================================

    [Fact]
    [Trait("Capability", "A-03")] // 降级后 Mask 安全语义（特性锚改写——原手工注册 HasMask 形态触运行时明细消费缺口留档承接）
    public void Degrade_MaskSemantic_NoPlaintextLeak()
    {
        // 实走腿先验锚（运行时实锚 @Batch-1：M-13 的 ProjectReference OutputItemType="Analyzer"
        // 不跨项目传递——IntegrationTests 实际未挂 SourceGen，HasGenerated=false 实证）：
        // 本用例实走腿 = 运行时回退列（反射降级引擎，特性锚读特性——20261010-001 整改后口径）。
        // 断言固化实态，防未来挂载形态变化（如显式挂 SourceGen）时本用例静默漂移。
        Assert.False(Mapper.HasGenerated<P2Entity, P2ViewMask>(),
            "P2ViewMask 对无生成资产（IntegrationTests 未挂 SourceGen——Analyzer 引用不跨项目传递）——本用例实走腿 = 运行时回退列");
        Assert.True(EfCoreDegradationDetector.NeedsDegradation(typeof(P2Entity), typeof(P2ViewMask), out _),
            "Mask 脱敏（RequiresDowngrade 协议位）——强制降级");
        SeedRow();
        _db.SaveChanges();
        var rows = _db.P2Entities.OrderBy(e => e.Id).AsDto().ToList<P2ViewMask>();
        Assert.Single(rows);
        // 降级二步映射：Mask 管线仍生效（安全语义不因降级丢失）——强化断言（原 NotEqual 弱断言堵空值漏洞）
        Assert.Equal("MASKED", rows[0].Phone);
    }

    // ================================================================
    // 补强批（剩余能力 ORM 承载面：忽略/ConvertBack/树/NoAutoInclude/钩子/多态）
    // 语义依据：DegradationDetectorBase.ClassifyUntranslatableDeclaration + s_projectionIrrelevantAttrs 白名单
    // ================================================================

    /// <summary>A-02：Ignore(Both) 纯可翻译——不降级 + 投影跳过绑定（目标属性保持初始化器）。</summary>
    [Fact]
    [Trait("Capability", "A-02")]
    public void Translatable_IgnoreBoth_SkipsBinding_NotDegraded()
    {
        try
        {
            ClearRegistryState();
            RuntimeSnapshotStore.Register(Meta(new[]
            {
                new RuntimePropertyMapping
                {
                    Target = new RuntimeTargetPropertySlot
                    {
                        Name = "Phone", TypeName = "String",
                        HasMapIgnore = true, MapIgnoreDirection = (int)NCMapper.Attributes.Direction.Both,
                    },
                    SourceName = "Phone",
                },
            }));
            Assert.False(EfCoreDegradationDetector.NeedsDegradation(typeof(P2Entity), typeof(P2View), out _),
                "Ignore 声明纯可翻译（MapIgnore.RequiresDowngrade=false）——不应强制降级");
            SeedRow();
            _db.SaveChanges();
            var rows = _db.P2Entities.OrderBy(e => e.Id).AsDto().ToList<P2View>();
            Assert.Single(rows);
            Assert.Equal("", rows[0].Phone); // Both/ToDto 忽略——投影跳过绑定（ProjectionExpressionFactory :210）
        }
        finally { ClearRegistryState(); }
    }

    /// <summary>A-11：方向忽略 ToEntity——正向投影照常绑定（方向位语义；独立类型对防表达式缓存串扰）。</summary>
    [Fact]
    [Trait("Capability", "A-11")]
    public void Translatable_IgnoreDirectionToEntity_BindsForward_NotDegraded()
    {
        try
        {
            ClearRegistryState();
            RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
            {
                SourceType = typeof(P2Entity),
                TargetType = typeof(P2ViewDir),
                SourceTypeFqn = typeof(P2Entity).FullName!,
                TargetTypeFqn = typeof(P2ViewDir).FullName!,
                Channel = RuntimeMappingChannel.Fluent,
                IsFluentForward = true,
                Properties = new[]
                {
                    new RuntimePropertyMapping
                    {
                        Target = new RuntimeTargetPropertySlot { Name = "Id", TypeName = "Int32" },
                        SourceName = "Id",
                    },
                    new RuntimePropertyMapping
                    {
                        Target = new RuntimeTargetPropertySlot
                        {
                            Name = "Phone", TypeName = "String",
                            HasMapIgnore = true, MapIgnoreDirection = (int)NCMapper.Attributes.Direction.ToEntity,
                        },
                        SourceName = "Phone",
                    },
                },
                AllTargetSlots = new[]
                {
                    new RuntimeTargetPropertySlot { Name = "Id", TypeName = "Int32" },
                    new RuntimeTargetPropertySlot { Name = "Phone", TypeName = "String" },
                },
            });
            Assert.False(EfCoreDegradationDetector.NeedsDegradation(typeof(P2Entity), typeof(P2ViewDir), out _),
                "ToEntity 方向忽略不拦截正向投影——不应强制降级");
            SeedRow();
            _db.SaveChanges();
            var rows = _db.P2Entities.OrderBy(e => e.Id).AsDto().ToList<P2ViewDir>();
            Assert.Single(rows);
            Assert.Equal("13812345678", rows[0].Phone); // 恒正向投影：ToEntity 忽略照常绑定
        }
        finally { ClearRegistryState(); }
    }

    /// <summary>A-09：ConvertBack 承载不触发正向降级（ConvertBack.RequiresDowngrade=false 协议位）。</summary>
    [Fact]
    [Trait("Capability", "A-09")]
    public void ConvertBack_NotDegrading_ForwardProjection()
    {
        try
        {
            ClearRegistryState();
            RuntimeSnapshotStore.Register(Meta(new[]
            {
                new RuntimePropertyMapping
                {
                    Target = new RuntimeTargetPropertySlot
                    {
                        Name = "AliasName", TypeName = "String",
                        HasConvertBack = true,
                        ConverterBackDeclaringType = "Generated",
                        ConverterBackMethodName = "Map_Cvb_X",
                    },
                    SourceName = "RealName",
                },
            }));
            Assert.False(EfCoreDegradationDetector.NeedsDegradation(typeof(P2Entity), typeof(P2View), out _),
                "ConvertBack 反向转换不影响正向投影（RequiresDowngrade=false）——不应强制降级");
        }
        finally { ClearRegistryState(); }
    }

    /// <summary>A-17：TreeConfig 树契约白名单——不触发降级（投影构建细节非不可翻译声明）。</summary>
    [Fact]
    [Trait("Capability", "A-17")]
    public void TreeConfig_Whitelisted_NotDegrading()
    {
        try
        {
            ClearRegistryState();
            RuntimeSnapshotStore.Register(Meta(Array.Empty<RuntimePropertyMapping>(),
                tree: new NCMapper.Runtime.RuntimeTreeConfig()));
            Assert.False(EfCoreDegradationDetector.NeedsDegradation(typeof(P2Entity), typeof(P2View), out _),
                "树契约声明不属不可翻译分类（ClassifyUntranslatableDeclaration 无 Tree 分支；MapTreeAttribute 在投影无关白名单）——不应强制降级");
        }
        finally { ClearRegistryState(); }
    }

    /// <summary>A-18：属性级 NoAutoInclude 纯可翻译——不降级 + 注册表消费位可判读。</summary>
    [Fact]
    [Trait("Capability", "A-18")]
    public void PropertyNoAutoInclude_Consumed_NotDegrading()
    {
        try
        {
            ClearRegistryState();
            RuntimeSnapshotStore.Register(Meta(new[]
            {
                new RuntimePropertyMapping
                {
                    Target = new RuntimeTargetPropertySlot { Name = "AliasName", TypeName = "String" },
                    SourceName = "RealName",
                    IsPropertyNoAutoInclude = true,
                },
            }));
            Assert.False(EfCoreDegradationDetector.NeedsDegradation(typeof(P2Entity), typeof(P2View), out _),
                "属性级 NoAutoInclude 仅影响降级路径 Include 过滤——不应强制降级");
            Assert.True(RuntimeSnapshotStore.IsPropertyNoAutoInclude(typeof(P2View), "AliasName"),
                "注册表属性级 NoAutoInclude 消费位（RuntimeSnapshotStore.IsPropertyNoAutoInclude）");
            Assert.False(RuntimeSnapshotStore.IsPropertyNoAutoInclude(typeof(P2View), "Phone"),
                "未声明属性不受误伤");
        }
        finally { ClearRegistryState(); }
    }

    /// <summary>A-20：BeforeMapping 钩子承载不可投影翻译——强制降级（RequiresDowngrade=true 同构自声明）。</summary>
    [Fact]
    [Trait("Capability", "A-20")]
    public void Untranslatable_BeforeMapping_ForcesDegrade()
    {
        try
        {
            ClearRegistryState();
            RuntimeSnapshotStore.Register(Meta(Array.Empty<RuntimePropertyMapping>(),
                beforeRef: "NCMapper.Tests.Runtime.HkHooks::Before"));
            var needs = EfCoreDegradationDetector.NeedsDegradation(typeof(P2Entity), typeof(P2View), out _);
            Assert.True(needs, "BeforeMapping 前置钩子（CLR 委托路径）不可投影翻译——强制降级");
        }
        finally { ClearRegistryState(); }
    }

    /// <summary>A-19：AfterMapping 钩子承载不可投影翻译——强制降级。</summary>
    [Fact]
    [Trait("Capability", "A-19")]
    public void Untranslatable_AfterMapping_ForcesDegrade()
    {
        try
        {
            ClearRegistryState();
            RuntimeSnapshotStore.Register(Meta(Array.Empty<RuntimePropertyMapping>(),
                afterRef: "NCMapper.Tests.Runtime.HkHooks::After"));
            var needs = EfCoreDegradationDetector.NeedsDegradation(typeof(P2Entity), typeof(P2View), out _);
            Assert.True(needs, "AfterMapping 后置钩子（CLR 委托路径）不可投影翻译——强制降级");
        }
        finally { ClearRegistryState(); }
    }

    /// <summary>A-22：多态分派承载不可投影翻译——强制降级（isinst 分派链恒降级）。</summary>
    [Fact]
    [Trait("Capability", "A-22")]
    public void Untranslatable_SubclassDispatch_ForcesDegrade()
    {
        try
        {
            ClearRegistryState();
            RuntimeSnapshotStore.Register(Meta(Array.Empty<RuntimePropertyMapping>(), subclass: true));
            var needs = EfCoreDegradationDetector.NeedsDegradation(typeof(P2Entity), typeof(P2View), out _);
            Assert.True(needs, "SubclassDispatch isinst 分派链不可投影翻译——强制降级");
        }
        finally { ClearRegistryState(); }
    }
}
