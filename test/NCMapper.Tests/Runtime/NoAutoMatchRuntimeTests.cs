// 防线测试三例（评审 002-9.2 红测先行——全册唯一 P1 正确性任务不依赖 Batch-2 兜底）：
//   ①白名单开 × 纯反射 × 反向（E4 同名腿 + 门面无序对探测/纯反射腿——F6 否定条件拦截点）
//  ②E5 写回段 AddressCity 负例（主循环门覆盖不到的独立路径，E5/回验 F-2）
//   ③投影降级 reason 精确文案断言（协议位消静默链；同时反向锁定"本特性不得加入
//     DegradationDetectorBase.s_projectionIrrelevantAttrs 白名单"纪律——误入即放行列裁剪静默泄漏）
// 夹具口径：private 嵌套类 → 源生成器 NCMAP011 跳过代码生成（SkipCodeGeneration），Mapper.Map 走
//   运行时 ExpressionBuilder 反射路径（范式同 DotPathGoldenTripleTests S12d / ConverterStaticMethodRuntimeTests）；
//   该形态同时天然构成"生成器不在场"的 D1 场景（提取链在 NCMAP011 短路处返回，NCMAP047 不发射）。
//  反向元数据可达性探针定案：生成期不发射 (Dto,Entity) 反向注册条目，
//   故注册表腿经正向条目 (Entity,Dto) 的无序对查找命中——本文件用例期望值按该结论写。
// 隔离：每用例首尾 RuntimeSnapshotStore.Clear() + Mapper.ClearCache()（新探针缓存
//   runtime-noautomatch-probe 随 ClearCache 全链清理——验收 2/F7 跨用例串扰防线）。
using NCMapper;
using NCMapper.Attributes;
using NCMapper.EFCore;
using NCMapper.Runtime;
using System;
using Xunit;

namespace NCMapper.Tests.Runtime;

/// <summary>
/// [MapNoAutoMatch] 反射腿防线用例（首切片；承接扩展：注册表腿 / 对侧标注 /
/// Inherited 分腿 / Q6·Q7 实测 / 缓存全链）
/// <para>隔离范式（经验库 #60/#65/#366）：<c>[Collection("GlobalConfigState")]</c>
///（DisableParallelization——本类读写 RuntimeSnapshotStore 全局注册表与全链缓存，
/// 与其他注册表消费者并行会交错 Clear/Register）+ 每用例首尾自足清理。</para>
/// </summary>
[Collection("GlobalConfigState")]
public class NoAutoMatchRuntimeTests : IDisposable
{
    // ================================================================
    // 夹具（private 嵌套类——NCMAP011 跳过生成，运行时反射路径实走腿）
    // ================================================================

    private class NamEntity
    {
        public int Id { get; set; }
        public string Declared { get; set; } = "";
        public string Renamed { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public NamAddress Address { get; set; } = new();
    }

    private class NamAddress
    {
        public string City { get; set; } = "";
    }

    /// <summary>白名单对 DTO：仅 [MapProperty] 点名的 Declared / Alias(←Renamed) 参与映射。</summary>
    [MapFrom(typeof(NamEntity))]
    [MapNoAutoMatch]
    private class NamWhitelistDto
    {
        public int Id { get; set; }

        [MapProperty(typeof(NamEntity), nameof(NamEntity.Declared))]
        public string Declared { get; set; } = "";

        [MapProperty(typeof(NamEntity), nameof(NamEntity.Renamed))]
        public string Alias { get; set; } = "";

        /// <summary>同名未声明 → 白名单下不接（Pair A CustomerName 模式）。</summary>
        public string CustomerName { get; set; } = "";

        /// <summary>扁平化候选未声明 → 正向不拼、反向不写回（E3/E5 负例载体）。</summary>
        public string AddressCity { get; set; } = "";
    }

    /// <summary>对照组 DTO：同形状无白名单开关——自动匹配/扁平化/反向写回照常（先验锚，排除恒绿）。</summary>
    [MapFrom(typeof(NamEntity))]
    private class NamPlainDto
    {
        public int Id { get; set; }

        [MapProperty(typeof(NamEntity), nameof(NamEntity.Declared))]
        public string Declared { get; set; } = "";

        [MapProperty(typeof(NamEntity), nameof(NamEntity.Renamed))]
        public string Alias { get; set; } = "";

        public string CustomerName { get; set; } = "";
        public string AddressCity { get; set; } = "";
    }

    private static NamEntity NewEntity() => new()
    {
        Id = 42,
        Declared = "D-src",
        Renamed = "R-src",
        CustomerName = "C-src",
        Address = new NamAddress { City = "OriginCity" },
    };

    public void Dispose()
    {
        try { RuntimeSnapshotStore.Clear(); Mapper.ClearCache(); } catch { }
    }

    // ================================================================
    // ① 白名单开 × 纯反射 × 反向（F6 否定条件拦截点）
    // ================================================================

    /// <summary>
    /// 防线①：白名单开 × 纯反射 × 反向——反向仅翻转正向显式声明（D4/S-3a），
    /// 自动匹配属性（CustomerName/Id）不参与反向、扁平化（AddressCity→Address.City）不写回。
    /// <para>门面 IsNoAutoMatch 注册表腿经无序对 (Entity,Dto) 命中正向条目；纯反射腿经
    /// [MapNoAutoMatch] inherit 探针——两腿任一失效即本用例红（E4 门 + E5 门 + 门面三处联动）。</para>
    /// </summary>
    [Fact]
    [Trait("Capability", "A-25")]
    public void WhitelistOn_PureReflection_Reverse_AutoMatchNotApplied()
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            Mapper.ClearCache();

            var dto = new NamWhitelistDto
            {
                Id = 7,
                Declared = "D-dto",
                Alias = "R-dto",
                CustomerName = "C-dto",
                AddressCity = "ZCity",
            };

            // 就地更新形态（Mapper.Map(src, dst)）——"未被覆盖"须以既有值为基准断言，
            // 新建形态下默认值与"未写回"不可区分（假绿面）
            var entity = NewEntity();
            Mapper.Map(dto, entity);

            // 显式声明翻转照常（正向 Declared/Alias ← 反向 entity.Declared/entity.Renamed）
            Assert.Equal("D-dto", entity.Declared);
            Assert.Equal("R-dto", entity.Renamed);
            // 自动匹配不参与反向：同名未声明属性保持实体原值
            Assert.Equal("C-src", entity.CustomerName);
            Assert.Equal(42, entity.Id);
            // E5：未声明扁平化不写回
            Assert.Equal("OriginCity", entity.Address.City);
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            Mapper.ClearCache();
        }
    }

    /// <summary>
    /// 防线①对照组（先验锚）：白名单关 × 纯反射 × 反向——同名自动匹配照常参与反向、
    /// 扁平化照常写回（排除"反射腿本来就不写回/不反向"的恒绿假测试）。
    /// </summary>
    [Fact]
    [Trait("Capability", "A-25")]
    public void WhitelistOff_PureReflection_Reverse_AutoMatchApplied_Control()
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            Mapper.ClearCache();

            var dto = new NamPlainDto
            {
                Id = 7,
                Declared = "D-dto",
                Alias = "R-dto",
                CustomerName = "C-dto",
                AddressCity = "ZCity",
            };

            var entity = NewEntity();
            Mapper.Map(dto, entity);

            Assert.Equal("D-dto", entity.Declared);
            Assert.Equal("R-dto", entity.Renamed);
            Assert.Equal("C-dto", entity.CustomerName);
            Assert.Equal(7, entity.Id);
            Assert.Equal("ZCity", entity.Address.City);
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            Mapper.ClearCache();
        }
    }

    /// <summary>
    /// 防线①正向腿：白名单开 × 纯反射 × 正向——显式声明映射 ✓ / 同名不接 / 扁平化不拼 /
    /// 未声明归 default（语义四象限反射侧镜像，Pair A 断言模板）。
    /// </summary>
    [Fact]
    [Trait("Capability", "A-25")]
    public void WhitelistOn_PureReflection_Forward_SemanticsQuadrant()
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            Mapper.ClearCache();

            var dto = Mapper.Map<NamEntity, NamWhitelistDto>(NewEntity())!;

            Assert.Equal("D-src", dto.Declared);          // 显式同名声明 ✓
            Assert.Equal("R-src", dto.Alias);             // 显式异名声明 ✓
            Assert.Equal("", dto.CustomerName);           // 同名未声明 → 不接（归 default）
            Assert.Equal(0, dto.Id);                      // 未声明 → 归 default
            Assert.Equal("", dto.AddressCity);            // 扁平化不拼
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            Mapper.ClearCache();
        }
    }

    // ================================================================
    // ② E5 写回段负例（主循环门覆盖不到的独立路径）
    // ================================================================

    /// <summary>
    /// 防线②：E5 写回段——白名单下未声明扁平化属性（AddressCity）不折叠写回实体嵌套路径
    /// （E5；门位定案 (b) 仅 BuildReverseFlattenWriteBack 支路短路，E6 显式点路径照常）。
    /// </summary>
    [Fact]
    [Trait("Capability", "A-25")]
    public void WhitelistOn_ReverseWriteBack_DerivedFlatten_NotApplied()
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            Mapper.ClearCache();

            var dto = new NamWhitelistDto { AddressCity = "ZCity", Declared = "D-dto" };

            var entity = NewEntity();
            Mapper.Map(dto, entity);

            Assert.Equal("OriginCity", entity.Address.City); // E5 门生效：不写回
            Assert.Equal("D-dto", entity.Declared);          // 同用例内显式声明仍写回（门不误伤 E6 同族显式面）
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            Mapper.ClearCache();
        }
    }

    // ================================================================
    // ③ 投影降级 reason 精确文案（协议位消静默链 + 白名单禁入反向锁定）
    // ================================================================

    /// <summary>
    /// 防线③：白名单对 ORM 投影强制降级（IProjectionPolicy 协议位经 DegradationDetectorBase
    /// EvaluateTopLayers 类级扫描臂命中），reason 精确文案含 "MapNoAutoMatchAttribute（需要降级）"——
    /// 若协议位被错写为 false 或本特性误入 s_projectionIrrelevantAttrs 白名单，本断言即红
    ///（前者落兜底臂泛化文案、后者完全不降级 = 未声明列泄漏静默链）。
    /// </summary>
    [Fact]
    [Trait("Capability", "A-25")]
    public void ProjectionDowngrade_ReasonNamesMapNoAutoMatchAttribute()
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            Mapper.ClearCache();
            EfCoreDegradationDetector.ClearCache();

            var needs = EfCoreDegradationDetector.NeedsDegradation(typeof(NamEntity), typeof(NamWhitelistDto), out var reason);

            Assert.True(needs, "白名单对投影强制降级（未声明列不得被读出）");
            Assert.Contains("MapNoAutoMatchAttribute（需要降级）", reason, StringComparison.Ordinal);
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            Mapper.ClearCache();
            EfCoreDegradationDetector.ClearCache();
        }
    }

    // ================================================================
    // 承接扩展：注册表腿（手工注册 meta，RuntimeMapperMetadata:33 契约）
    // ================================================================

    /// <summary>手工注册元数据条目（沿 TreeConfigConsumptionTests.Meta 形态——自足注册，不依赖生成产物）。</summary>
    private static RuntimeMapperMetadata Meta(Type src, Type tgt, bool pairNoAutoInclude)
        => new()
        {
            SourceType = src,
            TargetType = tgt,
            SourceTypeFqn = src.FullName!,
            TargetTypeFqn = tgt.FullName!,
            Channel = RuntimeMappingChannel.Fluent,
            IsFluentForward = true,
            NoAutoInclude = pairNoAutoInclude,
            Properties = Array.Empty<RuntimePropertyMapping>(),
        };

    /// <summary>
    /// 注册表腿夹具（**每场景独立类型对**——经验库 #60/#65 隔离纪律：
    /// <c>MapperBase&lt;TSource,TTarget&gt;</c> 的编译委托是闭合泛型静态字段、不随
    /// <c>Mapper.ClearCache()</c> 清理，同一 (S,T) 对跨白名单状态复用会读到陈旧委托）。
    /// </summary>
    private class RegOnEntity
    {
        public int Id { get; set; }
        public string CustomerName { get; set; } = "";
        public string Declared { get; set; } = "";
    }

    [MapFrom(typeof(RegOnEntity))]
    private class RegOnDto
    {
        public int Id { get; set; }
        public string CustomerName { get; set; } = "";

        [MapProperty(typeof(RegOnEntity), nameof(RegOnEntity.Declared))]
        public string Declared { get; set; } = "";
    }

    private class RegOffEntity
    {
        public int Id { get; set; }
        public string CustomerName { get; set; } = "";
    }

    private class RegOffDto
    {
        public int Id { get; set; }
        public string CustomerName { get; set; } = "";
    }

    /// <summary>
    /// 注册表腿正向：两侧均无 [MapNoAutoMatch]，仅手工注册 (RegOnEntity,RegOnDto) 条目带
    /// NoAutoInclude=true → 门面经 <c>s_byPair[(S,T)]</c> 命中，白名单生效（同名不接）。
    /// <para>业务逻辑：Fluent 通道 .NoAutoMatch() 的对级语义在运行时的消费入口就是本注册表条目——
    /// 门面若只走反射探针，Fluent 对的白名单在反射引擎会整体失效（R1 通道内分裂形态）。</para>
    /// </summary>
    [Fact]
    [Trait("Capability", "A-25")]
    public void RegistryLeg_PairEntryWithWhitelist_AutoMatchNotApplied()
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            Mapper.ClearCache();

            RuntimeSnapshotStore.Register(Meta(typeof(RegOnEntity), typeof(RegOnDto), pairNoAutoInclude: true));

            var dto = new RegOnDto { Id = 7, CustomerName = "C-dto", Declared = "D-dto" };
            var entity = new RegOnEntity { Id = 42, CustomerName = "C-src", Declared = "D-src" };
            Mapper.Map(dto, entity);

            Assert.Equal("D-dto", entity.Declared);       // 显式声明（[MapProperty]）照常
            Assert.Equal("C-src", entity.CustomerName);   // 注册表腿命中 → 同名不接
            Assert.Equal(42, entity.Id);
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            Mapper.ClearCache();
        }
    }

    /// <summary>
    /// 注册表腿反向（开工探针结论承载，④）：生成期不发射 (Dto,Entity) 反向条目，
    /// 仅注册正向条目 → 反向映射门面经**无序对第二次查找**命中，白名单同样生效
    ///（两侧对称探测为唯一实现口径的直证）。
    /// </summary>
    [Fact]
    [Trait("Capability", "A-25")]
    public void RegistryLeg_OppositeDirection_HitsForwardEntryByUnorderedPair()
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            Mapper.ClearCache();

            RuntimeSnapshotStore.Register(Meta(typeof(RegOnEntity), typeof(RegOnDto), pairNoAutoInclude: true));

            // 只注册正向 (Entity,Dto)；本用例执行 (Dto→Entity) 方向
            var entity = new RegOnEntity { Id = 42, CustomerName = "C-src", Declared = "D-src" };
            var dto = new RegOnDto { Id = 7, CustomerName = "C-dto", Declared = "D-dto" };
            Mapper.Map(dto, entity);

            Assert.Equal("D-dto", entity.Declared);
            Assert.Equal("C-src", entity.CustomerName);   // 无序对命中 → 反向同样受白名单约束
            Assert.Equal(42, entity.Id);
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            Mapper.ClearCache();
        }
    }

    /// <summary>
    /// 注册表腿对照组（排除恒绿）：独立类型对注册条目 NoAutoInclude=false → 自动匹配照常。
    /// </summary>
    [Fact]
    [Trait("Capability", "A-25")]
    public void RegistryLeg_PairEntryWithoutWhitelist_AutoMatchApplied_Control()
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            Mapper.ClearCache();

            RuntimeSnapshotStore.Register(Meta(typeof(RegOffEntity), typeof(RegOffDto), pairNoAutoInclude: false));

            var dto = new RegOffDto { Id = 7, CustomerName = "C-dto" };
            var entity = new RegOffEntity { Id = 42, CustomerName = "C-src" };
            Mapper.Map(dto, entity);

            Assert.Equal("C-dto", entity.CustomerName);
            Assert.Equal(7, entity.Id);
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            Mapper.ClearCache();
        }
    }

    // ================================================================
    // 承接扩展：对侧标注（纯反射腿两侧对称探测）
    // ================================================================

    /// <summary>
    /// 纯反射腿对侧标注：白名单特性标在**源侧**（NamSourceAnnotatedDto → NamSourceSideEntity）时，
    /// 反射门面经 <c>ProbeNoAutoMatchAttribute(sourceType) || ProbeNoAutoMatchAttribute(targetType)</c>
    /// 命中 → 白名单生效。
    /// <para>口径注：这是门面**两侧对称探测**（评审 002-P2-1 正解 + 开工探针定案）的既定语义——
    /// 反向映射时"DTO 侧"就是源侧，故任一侧标注均须生效；**生成侧不对应**（提取链只读目标类型
    /// 继承链，实体侧标注不激活白名单——负例见 SourceGen.Tests
    /// <c>NoAutoMatchDiagnosticsTests.EntitySideAnnotation_DoesNotActivateWhitelist</c>）。
    /// 两腿该处口径差异是"生成期声明语义 vs 运行时探测语义"的既有分界，登记。</para>
    /// </summary>
    [Fact]
    [Trait("Capability", "A-25")]
    public void ReflectionLeg_SourceSideAnnotation_ActivatesWhitelist()
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            Mapper.ClearCache();

            var target = new NamSourceSideTarget { Id = 42, CustomerName = "C-src", Declared = "D-src" };
            var source = new NamSourceAnnotatedDto { Id = 7, CustomerName = "C-dto", Declared = "D-dto" };
            Mapper.Map(source, target);

            Assert.Equal("C-src", target.CustomerName);   // 源侧标注 → 反射腿白名单生效，同名不接
            Assert.Equal(42, target.Id);
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            Mapper.ClearCache();
        }
    }

    [MapNoAutoMatch]
    private class NamSourceAnnotatedDto
    {
        public int Id { get; set; }
        public string Declared { get; set; } = "";
        public string CustomerName { get; set; } = "";
    }

    private class NamSourceSideTarget
    {
        public int Id { get; set; }
        public string Declared { get; set; } = "";
        public string CustomerName { get; set; } = "";
    }

    // ================================================================
    // 承接扩展：Inherited 分腿（D5③ 纯反射腿 / D5④ 投影腿 = Q6 实测）
    // ================================================================

    private class NamInheritBase
    {
        public int Id { get; set; }
        public string Declared { get; set; } = "";
        public string CustomerName { get; set; } = "";
    }

    [MapFrom(typeof(NamEntity))]
    [MapNoAutoMatch]
    private class NamInheritWhitelistBase
    {
        public int Id { get; set; }

        [MapProperty(typeof(NamEntity), nameof(NamEntity.Declared))]
        public string Declared { get; set; } = "";

        public string CustomerName { get; set; } = "";
    }

    /// <summary>派生 DTO 自身不再标注——白名单须经 <c>IsDefined(inherit: true)</c> 继承（D5③）。</summary>
    private class NamInheritDerivedDto : NamInheritWhitelistBase
    {
    }

    /// <summary>
    /// D5③ 纯反射腿继承：派生 DTO 未重复标注，白名单经特性继承（AttributeUsage Inherited=true）
    /// 在反射探针侧生效——同名未声明属性不参与映射。
    /// </summary>
    [Fact]
    [Trait("Capability", "A-25")]
    public void Inherited_Leg3_PureReflection_DerivedDtoInheritsWhitelist()
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            Mapper.ClearCache();

            var entity = NewEntity();
            var dto = new NamInheritDerivedDto { Id = 7, Declared = "D-dto", CustomerName = "C-dto" };
            Mapper.Map(dto, entity);

            Assert.Equal("D-dto", entity.Declared);
            Assert.Equal("C-src", entity.CustomerName);   // 继承的白名单生效
            Assert.Equal(42, entity.Id);
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            Mapper.ClearCache();
        }
    }

    /// <summary>
    /// D5④ 投影腿继承（Q6 实测用例）。
    /// <para><b>三列结论（评审 002-9.8 格式，直接搬运登记簿 #18）</b>
    /// 结论 = 派生 DTO（自身无标注、白名单在基类）经 <c>EfCoreDegradationDetector.NeedsDegradation</c>
    /// 的类级特性扫描腿（<c>DegradationDetectorBase.cs:343</c>）**感知**基类标注 →
    /// 触发投影降级；缺口可感知 = 是（已修复）。</para>
    /// <para><b>本用例口径已翻转（2026-10-20 甲案）</b>原为"定桩当前实态"（断言 <c>Assert.False</c>
    /// ——基类标注不被感知，即缺口存在）；将三处 <c>GetCustomAttributes(inherit: false)</c>
    /// 改为 <c>inherit: true</c> 后，基类标注被感知，本用例翻转为**修复验证断言**（<c>Assert.True</c>）。</para>
    /// <para><b>翻转依据（探针实测）</b>：改 <c>inherit:true</c> 后全量测试仅本用例失败（×3 TFM），
    /// 其余 2530 全过——失败正是本定桩断言被推翻，即缺口被修复的直接证据。</para>
    /// </summary>
    [Fact]
    [Trait("Capability", "A-25")]
    public void Inherited_Leg4_ProjectionScan_DerivedDto_Q6_Measured()
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            Mapper.ClearCache();
            EfCoreDegradationDetector.ClearCache();

            var needs = EfCoreDegradationDetector.NeedsDegradation(typeof(NamEntity), typeof(NamInheritDerivedDto), out var reason);

            // 修复验证（甲案翻转）：投影腿现已感知基类白名单（D5④ 缺口已闭合）
            Assert.True(needs,
                "Q6 修复验证：投影类级扫描腿 inherit:true，基类标注应被感知（已修复；"
                + "若本断言失败，说明 inherit:true 被回退或继承链断裂）");
            Assert.Contains("MapNoAutoMatchAttribute", reason ?? "", StringComparison.Ordinal);

            // 对照：同基类直接标注（自身即带标注）时投影腿命中——证明差异源于继承腿而非协议位
            var needsSelf = EfCoreDegradationDetector.NeedsDegradation(typeof(NamEntity), typeof(NamInheritWhitelistBase), out _);
            Assert.True(needsSelf, "对照组：自身标注时投影腿须命中（排除协议位失效导致的恒 False）");
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            Mapper.ClearCache();
            EfCoreDegradationDetector.ClearCache();
        }
    }

    // ================================================================
    // 承接扩展：Q7 嵌套 DTO 投影实测
    // ================================================================

    private class NamNestedLeafDto
    {
        public string City { get; set; } = "";
    }

    [MapFrom(typeof(NamEntity))]
    [MapNoAutoMatch]
    private class NamNestedWhitelistDto
    {
        public int Id { get; set; }

        [MapProperty(typeof(NamEntity), nameof(NamEntity.Declared))]
        public string Declared { get; set; } = "";

        /// <summary>嵌套复杂属性（未声明）——白名单下不入候选。</summary>
        public NamNestedLeafDto? AddressRef { get; set; }
    }

    /// <summary>
    /// Q7 实测：嵌套 DTO 属性 × 白名单 × 投影。
    /// <para><b>三列结论（评审 002-9.8）</b>：结论 = 白名单对含未声明嵌套复杂属性时，投影仍强制降级
    /// （协议位臂命中，与嵌套属性无关——降级后走内存映射，嵌套属性按白名单语义不填充）；
    /// 缺口可感知 = 否（本特性在投影维度无新增泄漏面，嵌套场景由"对级降级"整体覆盖）。</para>
    /// </summary>
    [Fact]
    [Trait("Capability", "A-25")]
    public void NestedDto_Whitelist_ProjectionDowngrade_Q7_Measured()
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            Mapper.ClearCache();
            EfCoreDegradationDetector.ClearCache();

            var needs = EfCoreDegradationDetector.NeedsDegradation(typeof(NamEntity), typeof(NamNestedWhitelistDto), out var reason);

            Assert.True(needs, "Q7 实测：含未声明嵌套复杂属性的白名单对仍强制降级");
            Assert.Contains("MapNoAutoMatchAttribute（需要降级）", reason, StringComparison.Ordinal);

            // 内存映射腿：嵌套属性未声明 → 不填充（白名单语义在嵌套场景同样成立）
            var dto = Mapper.Map<NamEntity, NamNestedWhitelistDto>(NewEntity())!;
            Assert.Null(dto.AddressRef);
            Assert.Equal("D-src", dto.Declared);
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            Mapper.ClearCache();
            EfCoreDegradationDetector.ClearCache();
        }
    }

    // ================================================================
    // 承接扩展：探针缓存登记与 ClearCache 全链（F7 防线）
    // ================================================================

    /// <summary>
    /// 缓存全链：新探针缓存 <c>runtime-noautomatch-probe</c> 已登记（Full 层）且随
    /// <c>Mapper.ClearCache()</c> 全链清理（ClearCount 递增 + 清理后重新探测仍正确）——
    /// 沿 AnchorGateCacheTests 登记制范式；不复用 s_mapFromProbe（独立缓存实例）。
    /// </summary>
    [Fact]
    [Trait("Capability", "A-25")]
    public void ProbeCache_RegisteredUnderFullLayer_ClearedByClearCacheChain()
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            Mapper.ClearCache();

            var snapshot = NCMapper.Internal.CacheRegistry.Snapshot();
            var entry = Assert.Single(snapshot, e => e.Name == "runtime-noautomatch-probe");
            Assert.Equal(NCMapper.Internal.CacheLayer.Full, entry.Layer);

            // 预热探针（填充 CWT 条目）后清理，ClearCount 必增（登记表驱动全链清理）
            Assert.True(RuntimeSnapshotStore.IsNoAutoMatch(typeof(NamEntity), typeof(NamWhitelistDto)));

            var before = NCMapper.Internal.CacheRegistry.ClearCount;
            var failuresBefore = NCMapper.Internal.CacheRegistry.ClearFailureCount;
            Mapper.ClearCache();
            var after = NCMapper.Internal.CacheRegistry.ClearCount;

            Assert.True(after > before, "Mapper.ClearCache 未触发登记表全链清理（探针缓存游离）");
            // ClearFailureCount 为进程级累计（CacheRegistry 仅增量、测试内不复位）——本对只断言
            // "本次链式清理零新增失败"（相对口径，沿 A2-08 failuresBefore 范式）；绝对零断言对
            // 同进程更早用例（如 T64 A2-08 注入的清理委托异常）顺序敏感，全量跑偶发红（2026-10-11 复跑实测）。
            Assert.Equal(failuresBefore, NCMapper.Internal.CacheRegistry.ClearFailureCount);

            // 清理后重新探测仍正确（无陈旧负缓存）
            Assert.True(RuntimeSnapshotStore.IsNoAutoMatch(typeof(NamEntity), typeof(NamWhitelistDto)));
            Assert.False(RuntimeSnapshotStore.IsNoAutoMatch(typeof(NamEntity), typeof(NamPlainDto)));
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            Mapper.ClearCache();
        }
    }
}
