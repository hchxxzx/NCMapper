// Fluent 通道 MapWhen 生成路径执行语义（矩阵维度 1/2/3——B/A 形态 × T1/T2 ×
// 三态 + 内联 Convert 协同 + 块内合成面执行）。
//   特性面（Wg 系）执行语义已由 MapWhenExecutionSemanticsTests 覆盖；本文件补 Fluent 声明面
//   （[MapperConfig] fixture 编译进测试程序集 → GeneratedCache 委托——B 形态谓词方法发射 +
//   守卫包裹的真实运行验证；M2-2/M2-8 变异检出器）。
// 隔离范式（经验库 #71/#60）：[Collection("GlobalConfigState")]（DisableParallelization）；
//   GeneratedCache 由 ModuleInitializer 注册（独立容器——注册表清理不影响本文件断言）。
// 合成面例外：s_generatedComposeFuncs 受 ClearGenerated/ClearCache 清理且 module
//   initializer 不重填，Fluent DTO 无 [MapFrom] 层2 不可兜底——测试前幂等重注册
//   （ComposeFluentRuntimeTests 同模式，#71）。
using NCMapper;
using Xunit;

namespace NCMapper.Tests.Runtime;

[Collection("GlobalConfigState")]
public class MapWhenFluentExecutionTests
{
    /// <summary>
    /// 合成面层1注册自足：与生成端 module initializer 注册同一委托（Compose_WgFBlkDto），
    /// 幂等覆盖 WgFBlkDto 键、不触碰其他类型注册（ComposeFluentRuntimeTests 同模式）。
    /// </summary>
    private static void EnsureGeneratedComposeRegistered()
        => Mapper.RegisterGeneratedCompose<WgFBlkDto>(NCMapper.Generated.GeneratedMapper.Compose_WgFBlkDto);

    // ==== 模型：B 形态 T1（内联谓词——字段值形态） ====
    public class WgFB1Entity { public int Id { get; set; } public string Name { get; set; } = ""; }
    public class WgFB1Dto { public int Id { get; set; } public string Name { get; set; } = ""; }

    [MapperConfig]
    internal class WgFB1Mapper
    {
        public static void Configure(FluentMappingConfig cfg)
        {
            cfg.For<WgFB1Entity, WgFB1Dto>()
               .Member(d => d.Name).MapWhen(v => v.Length > 2);
        }
    }

    // ==== 模型：B 形态 T2（内联谓词——整源形态） ====
    public class WgFB2Entity { public int Id { get; set; } public int Score { get; set; } }
    public class WgFB2Dto { public int Id { get; set; } public int Score { get; set; } }

    [MapperConfig]
    internal class WgFB2Mapper
    {
        public static void Configure(FluentMappingConfig cfg)
        {
            cfg.For<WgFB2Entity, WgFB2Dto>()
               .Member(d => d.Score).MapWhen<WgFB2Entity>(s => s.Score > 0);
        }
    }

    // ==== 模型：A 形态方法组（执行层——提取层 Combo_A_* 已覆盖提取语义） ====
    // 谓词取"长度 > 2"：条件假源值（"ab"）≠ 初始值（""）——守卫丢失时可区分（M2 变异检出性）
    public class WgFAEntity { public int Id { get; set; } public string Name { get; set; } = ""; }
    public class WgFADto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";

        public static bool HasLength3(string value) => value.Length > 2;
    }

    [MapperConfig]
    internal class WgFAMapper
    {
        public static void Configure(FluentMappingConfig cfg)
        {
            cfg.For<WgFAEntity, WgFADto>()
               .Member(d => d.Name).MapWhen(WgFADto.HasLength3);
        }
    }

    // ==== 模型：协同（B 形态 MapWhen × 内联 Convert——条件假时转换器不执行）====
    // 转换器输出带 "X" 前缀：守卫缺席时空串也会产出 "X"——输出可区分转换器是否被调用
    public class WgFCEntity { public int Id { get; set; } public string Name { get; set; } = ""; }
    public class WgFCDto { public int Id { get; set; } public string Name { get; set; } = ""; }

    [MapperConfig]
    internal class WgFCMapper
    {
        public static void Configure(FluentMappingConfig cfg)
        {
            cfg.For<WgFCEntity, WgFCDto>()
               .Member(d => d.Name).MapWhen(v => v.Length > 0)
               .Member(d => d.Name).Convert((string v) => "X" + v);
        }
    }

    // ==== 模型：块内 MapWhen（FromSource 分组块——合成面 Mapper.Compose 执行）====
    // 谓词取"Trim 后非空"：条件假源值（"  "）≠ 初始值（""）——守卫丢失时可区分（M2 变异检出性）
    public class WgFBlkEntity { public int Id { get; set; } }
    public class WgFBlkSub { public string Name { get; set; } = ""; }
    public class WgFBlkDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";

        public static bool HasTrimmedContent(string value) => value.Trim().Length > 0;
    }

    [MapperConfig]
    internal class WgFBlkMapper
    {
        public static void Configure(FluentMappingConfig cfg)
        {
            cfg.For<WgFBlkEntity, WgFBlkDto>()
               .FromSource<WgFBlkSub>(block =>
               {
                   block.Member(d => d.Name).From(s => s.Name);
                   block.Member(d => d.Name).MapWhen(WgFBlkDto.HasTrimmedContent);
               });
        }
    }

    /// <summary> 新建（B 形态 T1）：条件真映射；条件假保持初始化器 ""。</summary>
    [Fact]
    [Trait("Capability", "A-21")] // P5④ 回补——Fluent MapWhen 整机执行锚
    public void FluentB_T1_New_ConditionFalse_KeepsInitializer()
    {
        var dto = Mapper.Map<WgFB1Entity, WgFB1Dto>(new WgFB1Entity { Id = 1, Name = "abc" });
        Assert.NotNull(dto);
        Assert.Equal("abc", dto!.Name);

        var dto2 = Mapper.Map<WgFB1Entity, WgFB1Dto>(new WgFB1Entity { Id = 2, Name = "ab" });
        Assert.NotNull(dto2);
        Assert.Equal("", dto2!.Name); // 条件假（长度 ≤ 2）→ 跳过赋值
    }

    /// <summary> 更新（B 形态 T1）：条件假保留 dst 既有值；条件真覆盖。</summary>
    [Fact]
    public void FluentB_T1_Update_ConditionFalse_KeepsExisting()
    {
        var target = new WgFB1Dto { Name = "existing" };
        Mapper.Map(new WgFB1Entity { Id = 1, Name = "ab" }, target);
        Assert.Equal("existing", target.Name); // 条件假 → 保留

        Mapper.Map(new WgFB1Entity { Id = 1, Name = "fresh" }, target);
        Assert.Equal("fresh", target.Name); // 条件真 → 覆盖
    }

    /// <summary>T2 整源（B 形态内联）：谓词收整个源对象。</summary>
    [Fact]
    public void FluentB_T2_WholeSource_Guard()
    {
        var dto = Mapper.Map<WgFB2Entity, WgFB2Dto>(new WgFB2Entity { Id = 1, Score = 5 });
        Assert.NotNull(dto);
        Assert.Equal(5, dto!.Score);

        var dto2 = Mapper.Map<WgFB2Entity, WgFB2Dto>(new WgFB2Entity { Id = 2, Score = -5 });
        Assert.NotNull(dto2);
        Assert.Equal(0, dto2!.Score); // 条件假 → 保持默认
    }

    /// <summary>A 形态方法组（执行层）：与 B 形态同语义（S8 同源——生成端同一包裹点）。</summary>
    [Fact]
    public void FluentA_MethodGroup_Guard()
    {
        var dto = Mapper.Map<WgFAEntity, WgFADto>(new WgFAEntity { Id = 1, Name = "abc" });
        Assert.NotNull(dto);
        Assert.Equal("abc", dto!.Name);

        var dto2 = Mapper.Map<WgFAEntity, WgFADto>(new WgFAEntity { Id = 2, Name = "ab" });
        Assert.NotNull(dto2);
        Assert.Equal("", dto2!.Name); // 条件假（长度 ≤ 2）→ 跳过赋值（非 "ab"——守卫丢失可区分）
    }

    /// <summary>
    /// 协同（B 形态 MapWhen × 内联 Convert）：条件假时内联转换器不执行——
    /// 输出可区分（守卫缺席时空串产出 "X"；守卫生效时空串保持 ""）。
    /// </summary>
    [Fact]
    public void FluentB_MapWhen_InlineConvert_S6_ConverterNotInvoked()
    {
        var dto = Mapper.Map<WgFCEntity, WgFCDto>(new WgFCEntity { Id = 1, Name = "abc" });
        Assert.NotNull(dto);
        Assert.Equal("Xabc", dto!.Name); // 条件真 → 内联 Convert 执行

        var dto2 = Mapper.Map<WgFCEntity, WgFCDto>(new WgFCEntity { Id = 2, Name = "" });
        Assert.NotNull(dto2);
        Assert.Equal("", dto2!.Name); // 条件假 → 转换不执行（非 "X"——转换前求值）
    }

    /// <summary>
    /// 块内 MapWhen（合成面执行）：FromSource 块内守卫在 Mapper.Compose 路径生效——
    /// 子源条件假 → 目标保持默认；条件真 → 跨源映射（Update_{Sub}_To_{Target} 赋值层守卫）。
    /// </summary>
    [Fact]
    public void S4_BlockWhen_ComposeExecution_GuardSkips()
    {
        EnsureGeneratedComposeRegistered(); // 层1注册自足（清理型测试先跑时幂等补注册）
        var dto = Mapper.Compose<WgFBlkDto>(
            new WgFBlkEntity { Id = 1 },
            new WgFBlkSub { Name = "sub-value" });
        Assert.NotNull(dto);
        Assert.Equal(1, dto!.Id);
        Assert.Equal("sub-value", dto.Name); // 块内条件真 → 跨源映射

        var dto2 = Mapper.Compose<WgFBlkDto>(
            new WgFBlkEntity { Id = 2 },
            new WgFBlkSub { Name = "   " });
        Assert.NotNull(dto2);
        Assert.Equal(2, dto2!.Id);
        Assert.Equal("", dto2.Name); // 块内条件假（Trim 后空）→ 跳过赋值（非 " "——守卫丢失可区分，执行语义）
    }
}
