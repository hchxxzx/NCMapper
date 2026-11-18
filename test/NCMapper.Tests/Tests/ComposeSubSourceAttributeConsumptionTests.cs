using Xunit;
using NCMapper;
using NCMapper.Attributes;

namespace NCMapper.Tests.Tests
{
    // ================================================================
    // 探针模型（spike → G5 永久对照测试）
    //
    // 双 DTO 对照纪律（评审 _003 P2-1）：探针 A 摘除生成注册走运行时引擎（ComposeRuntime），
    // 独立对照 B 保留生成注册走源生成引擎——两个专用 DTO 互不混用，弃"同 DTO 先摘后留"
    //（顺序状态依赖脆弱：摘除与恢复之间的测试失败会留下脏注册状态）。
    //
    // 探针模型纪律（评审 _003 P2-2）：DTO 必含 [MapFrom(主源)] 三件套
    //（ComposeRuntime 入口强依赖 [MapFrom]，Mapper.Compose.cs:136-140）；主源实体不含
    // Sub 导航属性（避免 CrossTable 路径①先写明文 + ComposeRuntime 直连路径②覆盖脱敏的
    // 语义分叉——探针只观察终态时两路径不可区分）。
    // ================================================================

    /// <summary>探针 A 主源实体（运行时引擎探针）</summary>
    public class ProbeMaskAPrimary
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    /// <summary>探针 A 副源实体</summary>
    public class ProbeMaskASub
    {
        public string Phone { get; set; } = "";
    }

    /// <summary>
    /// 探针 A DTO — 副源字段 [Mask]（运行时引擎：摘除生成注册后 Compose 走 ComposeRuntime 两趟路径）
    /// </summary>
    [MapFrom(typeof(ProbeMaskAPrimary))]
    public class ProbeMaskADto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";

        [MapProperty(typeof(ProbeMaskASub), nameof(ProbeMaskASub.Phone))]
        [Mask(typeof(MaskPhone))]
        public string Phone { get; set; } = "";
    }

    /// <summary>对照 B 主源实体（生成引擎对照）</summary>
    public class ProbeMaskBPrimary
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    /// <summary>对照 B 副源实体</summary>
    public class ProbeMaskBSub
    {
        public string Phone { get; set; } = "";
    }

    /// <summary>
    /// 对照 B DTO — 副源字段 [Mask]（保留生成注册，Compose 走源生成引擎 Compose_X 编译时代码）
    /// </summary>
    [MapFrom(typeof(ProbeMaskBPrimary))]
    public class ProbeMaskBDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";

        [MapProperty(typeof(ProbeMaskBSub), nameof(ProbeMaskBSub.Phone))]
        [Mask(typeof(MaskPhone))]
        public string Phone { get; set; } = "";
    }

    // ================================================================
    // fixture 模型（两段式 commit①：仅模型，不含发射修复与断言）
    // ================================================================

    /// <summary>IsFromJson 探针主源实体（fixture）</summary>
    public class ProbeJsonPrimary
    {
        public int Id { get; set; }
    }

    /// <summary>IsFromJson 探针副源实体（string 载荷）</summary>
    public class ProbeJsonSub
    {
        public string Payload { get; set; } = "";
    }

    /// <summary>IsFromJson 探载荷对象（反序列化目标）</summary>
    public class ProbeJsonDetail
    {
        public string Value { get; set; } = "";
    }

    /// <summary>
    /// IsFromJson 探针 DTO — 副源字段 [MapProperty(IsFromJson=true)]（string → 对象反序列化形态）
    /// </summary>
    [MapFrom(typeof(ProbeJsonPrimary))]
    public class ProbeJsonDto
    {
        public int Id { get; set; }

        [MapProperty(typeof(ProbeJsonSub), nameof(ProbeJsonSub.Payload), IsFromJson = true)]
        public ProbeJsonDetail Payload { get; set; } = new();
    }

    /// <summary>converter+mask 探针 A 主源实体（运行时引擎探针，fixture）</summary>
    public class ProbeConvMaskAPrimary
    {
        public int Id { get; set; }
    }

    /// <summary>converter+mask 探针 A 副源实体</summary>
    public class ProbeConvMaskASub
    {
        public string? Phone { get; set; }
    }

    /// <summary>
    /// converter+mask 探针 A DTO — 副源字段 [Convert]+[Mask]（运行时引擎：
    /// 摘除生成注册后走 ComposeRuntime，锁定 ConvertUsingThenMaskStrategy 实测行为——D2 裁定基准）
    /// </summary>
    [MapFrom(typeof(ProbeConvMaskAPrimary))]
    public class ProbeConvMaskADto
    {
        public int Id { get; set; }

        [MapProperty(typeof(ProbeConvMaskASub), nameof(ProbeConvMaskASub.Phone))]
        [Convert(typeof(ProbeConvMaskConverter), nameof(ProbeConvMaskConverter.ExtractDigits))]
        [Mask(typeof(MaskPhone))]
        public string? Phone { get; set; }
    }

    /// <summary>converter+mask 对照 B 主源实体（生成引擎对照）</summary>
    public class ProbeConvMaskBPrimary
    {
        public int Id { get; set; }
    }

    /// <summary>converter+mask 对照 B 副源实体</summary>
    public class ProbeConvMaskBSub
    {
        public string? Phone { get; set; }
    }

    /// <summary>
    /// converter+mask 对照 B DTO — 副源字段 [Convert]+[Mask]（保留生成注册走源生成引擎）
    /// </summary>
    [MapFrom(typeof(ProbeConvMaskBPrimary))]
    public class ProbeConvMaskBDto
    {
        public int Id { get; set; }

        [MapProperty(typeof(ProbeConvMaskBSub), nameof(ProbeConvMaskBSub.Phone))]
        [Convert(typeof(ProbeConvMaskConverter), nameof(ProbeConvMaskConverter.ExtractDigits))]
        [Mask(typeof(MaskPhone))]
        public string? Phone { get; set; }
    }

    /// <summary>
    /// converter+mask 探针共用转换器（T1 形态：末段值 → string）——
    /// null 输入 → null 输出（D2 null 语义裁定观测点）
    /// </summary>
    public static class ProbeConvMaskConverter
    {
        public static string? ExtractDigits(string? value)
            => value is null ? null : new string(value.Where(char.IsDigit).ToArray());
    }

    /// <summary>
    /// Compose 副源字段 [Mask]/[IsFromJson] 消费测试— 双引擎对照。
    /// <para>
    /// spike 结论（G5 永久对照测试，不删）：
    /// 运行时引擎（ComposeRuntime 第二趟 MapperBase&lt;副源,DTO&gt;.Map 经 AssignmentPlanner
    /// 从 target 读 [Mask]）对副源字段正确脱敏；生成引擎（Compose_X → Update_副源_To_DTO
    /// 无 mask 分支）不脱敏——生成侧单引擎 bug 复现。
    /// </para>
    /// <para>
    /// 修复后（当前态）：生成引擎四段序镜像（converter+mask / 纯 mask / IsFromJson /
    /// 类型转换链），SourceGen 用例断言已脱敏/JSON 化——双引擎一致性（G5）全程承载。
    /// </para>
    /// </summary>
    public class ComposeSubSourceAttributeConsumptionTests : IDisposable
    {
        public ComposeSubSourceAttributeConsumptionTests()
        {
            // 探针 A：逐类型摘除生成 Compose 注册（TryRemove 语义，Mapper.cs:228-231）
            // → Mapper.Compose<ProbeMaskADto> 落入层 2 ComposeRuntime（运行时反射路径）。
            // 不用全局 ClearGenerated()——一次清掉全部类型对的 generated 注册 + 运行时缓存族，
            // 污染同会话其他依赖生成注册的测试（先例 SourceGenConsistencyTests 逐类型 null 覆盖）。
            // converter+mask 探针 A 同口径摘除（D2 裁定基准——运行时 ConvertUsingThenMaskStrategy 实测）。
            Mapper.RegisterGeneratedCompose<ProbeMaskADto>(null!);
            Mapper.RegisterGeneratedCompose<ProbeConvMaskADto>(null!);
        }

        public void Dispose()
        {
            // 恢复生成注册：原委托为生成器发射的 GeneratedMapper.Compose_X 静态方法
            //（CodeGenerator.cs:455 注册形态），与测试同程序集可直接引用恢复——
            // 摘除仅存在于本测试实例生命周期内，失败也由 Dispose 兜底恢复，不留脏注册状态。
            Mapper.RegisterGeneratedCompose<ProbeMaskADto>(NCMapper.Generated.GeneratedMapper.Compose_ProbeMaskADto);
            Mapper.RegisterGeneratedCompose<ProbeConvMaskADto>(NCMapper.Generated.GeneratedMapper.Compose_ProbeConvMaskADto);
        }

        /// <summary>
        /// 运行时引擎：Compose 副源字段 [Mask] 已脱敏（探针 A 结论）
        /// <para>ComposeRuntime 第二趟 MapperBase&lt;ProbeMaskASub, ProbeMaskADto&gt;.Map 经
        /// AssignmentPlanner 从 target 属性读 [Mask]（assignmentplanner.cs Mask 判定）→ 已脱敏。</para>
        /// </summary>
        [Fact]
        public void Runtime_Compose_SubSource_Mask_IsApplied()
        {
            var primary = new ProbeMaskAPrimary { Id = 1, Name = "Alice" };
            var sub = new ProbeMaskASub { Phone = "13812345678" };

            var dto = Mapper.Compose<ProbeMaskADto>(primary, sub)!;

            Assert.Equal(1, dto.Id);
            Assert.Equal("Alice", dto.Name);
            // 运行时引擎：副源字段脱敏（MaskPhone：13812345678 → 138****5678）
            Assert.Equal("138****5678", dto.Phone);
        }

        /// <summary>
        /// 生成引擎：Compose 副源字段 [Mask] 已脱敏（修复——核心 bug 消除）
        /// <para>Compose_ProbeMaskBDto → Update_ProbeMaskBSub_To_ProbeMaskBDto 四段序分支②：
        /// <c>dst.Phone = __mask_ProbeMaskBDto_Phone.Mask(src.Phone)</c>（直包，与运行时/主源一致）。</para>
        /// <para>修复前本用例断言明文（bug 复现锚点）；后翻转为脱敏（双引擎一致性 G5）。</para>
        /// </summary>
        [Fact]
        public void SourceGen_Compose_SubSource_Mask_IsApplied()
        {
            Mapper.RegisterGeneratedCompose<ProbeMaskBDto>(NCMapper.Generated.GeneratedMapper.Compose_ProbeMaskBDto);

            var primary = new ProbeMaskBPrimary { Id = 2, Name = "Bob" };
            var sub = new ProbeMaskBSub { Phone = "13812345678" };

            var dto = Mapper.Compose<ProbeMaskBDto>(primary, sub)!;

            Assert.Equal(2, dto.Id);
            Assert.Equal("Bob", dto.Name);
            // 生成引擎（修复后）：副源字段脱敏，与运行时引擎一致（G5）
            Assert.Equal("138****5678", dto.Phone);
        }

        /// <summary>
        /// 生成引擎：Compose 副源字段 [MapProperty(IsFromJson=true)] 按 JSON 反序列化（string → 对象）
        /// </summary>
        [Fact]
        public void SourceGen_Compose_SubSource_IsFromJson_Deserializes()
        {
            Mapper.RegisterGeneratedCompose<ProbeJsonDto>(NCMapper.Generated.GeneratedMapper.Compose_ProbeJsonDto);

            var primary = new ProbeJsonPrimary { Id = 3 };
            var sub = new ProbeJsonSub { Payload = """{"Value":"abc"}""" };

            var dto = Mapper.Compose<ProbeJsonDto>(primary, sub)!;

            Assert.Equal(3, dto.Id);
            // 修复前：TypeConvertHelper 兜底明文转换（非 JSON）；修复后：JsonConvertToObject 反序列化
            Assert.NotNull(dto.Payload);
            Assert.Equal("abc", dto.Payload.Value);
        }

        /// <summary>
        /// 运行时引擎：Compose 副源字段 [Convert]+[Mask] 先转换后脱敏
        ///（D2 裁定基准——ConvertUsingThenMaskStrategy 实测行为，null 转换输出 → Mask(null)）
        /// </summary>
        [Fact]
        public void Runtime_Compose_SubSource_ConverterThenMask_Applied()
        {
            var primary = new ProbeConvMaskAPrimary { Id = 4 };
            var sub = new ProbeConvMaskASub { Phone = "Tel:138-1234-5678" };

            var dto = Mapper.Compose<ProbeConvMaskADto>(primary, sub)!;

            Assert.Equal(4, dto.Id);
            // 先转换（提取数字段）再脱敏：138-1234-5678 → 13812345678 → 138****5678
            Assert.Equal("138****5678", dto.Phone);

            // D2 null 语义（converter+mask 形）：null 转换输出 → Mask(null) → ""
            //（运行时 ConvertUsingThenMaskStrategy 直包无守卫；生成侧四段序分支①同形）
            var subNull = new ProbeConvMaskASub { Phone = null };
            var dtoNull = Mapper.Compose<ProbeConvMaskADto>(primary, subNull)!;
            Assert.Equal("", dtoNull.Phone);
        }

        /// <summary>
        /// 生成引擎：Compose 副源字段 [Convert]+[Mask] 先转换后脱敏（四段序分支①，
        /// 与运行时引擎一致——G5 双引擎一致性）
        /// </summary>
        [Fact]
        public void SourceGen_Compose_SubSource_ConverterThenMask_Applied()
        {
            Mapper.RegisterGeneratedCompose<ProbeConvMaskBDto>(NCMapper.Generated.GeneratedMapper.Compose_ProbeConvMaskBDto);

            var primary = new ProbeConvMaskBPrimary { Id = 5 };
            var sub = new ProbeConvMaskBSub { Phone = "Tel:138-1234-5678" };

            var dto = Mapper.Compose<ProbeConvMaskBDto>(primary, sub)!;

            Assert.Equal(5, dto.Id);
            // 先转换（提取数字段）再脱敏（与运行时探针一致）
            Assert.Equal("138****5678", dto.Phone);

            // D2 null 语义（converter+mask 形）：null 转换输出 → Mask(null) → ""（与运行时一致）
            var subNull = new ProbeConvMaskBSub { Phone = null };
            var dtoNull = Mapper.Compose<ProbeConvMaskBDto>(primary, subNull)!;
            Assert.Equal("", dtoNull.Phone);
        }
    }
}
