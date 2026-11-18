using NCMapper;
using NCMapper.Attributes;
using Xunit;

namespace NCMapper.Tests;

/// <summary>
/// 全局忽略 4 原语 × 方向 + OverrideIgnore 三特性豁免 + 判定面四象限（运行时回退路径）
/// <para>对应任务 S2。源生成侧四象限断言见
/// NCMapper.SourceGen.Tests.GlobalConfigMatchingTests。</para>
/// <para><b>测试隔离约束</b>：运行时映射委托缓存在泛型静态字段（MapperBase 的 Lazy），
/// ClearCache 无法清除（文档明示）；每个测试使用独立类型对，保证各自在目标配置下首次编译。</para>
/// <para>本集合与 GlobalConfigTests 共享进程级冻结状态，禁止并行。</para>
/// </summary>
[Collection("GlobalConfigState")]
public class GlobalIgnoreRuntimeTests : IDisposable
{
    /// <summary>
    /// 每个测试前重置全局配置冻结状态（测试隔离）
    /// </summary>
    public GlobalIgnoreRuntimeTests()
    {
        Mapper.ClearCache();        // 迁移：Reset 不再清注册表，测试隔离需显式全清
        GlobalMapConfig.ResetForTests();
    }

    /// <summary>
    /// 类级清理 — 消除集合结束后冻结配置残留（进程级单例，
    /// 残留会污染并行/后续执行的未感知测试类，差异 #12 同类项）
    /// </summary>
    public void Dispose() { Mapper.ClearCache(); GlobalMapConfig.ResetForTests(); } // 迁移

    // ===== 测试 1：4 原语 × 双向忽略（独立类型对 S2_1） =====

    private class S2_1_Entity
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";

        public string Password { get; set; } = "";

        public string InternalCode { get; set; } = "";

        public byte[] RowVersion { get; set; } = [];

        public string SecretToken { get; set; } = "";

        public string CreatedStamp { get; set; } = "";
    }

    [MapFrom(typeof(S2_1_Entity))] // （补锚——承载侧合法形态）
    private class S2_1_Dto
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";

        public string Password { get; set; } = "";

        public string InternalCode { get; set; } = "";

        public byte[] RowVersion { get; set; } = [];

        public string SecretToken { get; set; } = "";

        public string CreatedStamp { get; set; } = "";
    }

    /// <summary>
    /// 验证：4 原语 × 双向忽略 — 类型全名/精确名/前缀/后缀各自命中后目标属性留空
    /// </summary>
    [Fact]
    public void FourPrimitives_BidirectionalIgnore()
    {
        Mapper.RegisterGeneratedConfig(new GeneratedMapConfig
        {
            // 原语 1：类型全名（byte[]）
            IgnoreTypeFullNames = ["System.Byte[]"],
            // 原语 2：精确名
            IgnoreNames = ["Password"],
            // 原语 3：前缀
            IgnoreNamePrefixes = ["Secret"],
            // 原语 4：后缀
            IgnoreNameSuffixes = ["Stamp"],
        }, "S2Test");

        var dto = Mapper.Map<S2_1_Entity, S2_1_Dto>(new S2_1_Entity
        {
            Id = 1,
            Name = "alice",
            Password = "pwd",
            InternalCode = "SECRET-1",
            RowVersion = [1, 2, 3],
            SecretToken = "tok",
            CreatedStamp = "stamp",
        });

        Assert.Equal(1, dto!.Id);
        Assert.Equal("alice", dto.Name);
        // 4 原语各自命中 → 留空
        Assert.Empty(dto.RowVersion);
        Assert.Equal("", dto.Password);
        Assert.Equal("", dto.SecretToken);
        Assert.Equal("", dto.CreatedStamp);
        // 未命中属性正常映射
        Assert.Equal("SECRET-1", dto.InternalCode);
    }

    // ===== 测试 2：ToEntity 方向清单（独立类型对 S2_2） =====

    private class S2_2_Entity
    {
        public int Id { get; set; }

        public string CreatedStamp { get; set; } = "";
    }

    [MapFrom(typeof(S2_2_Entity))] // （补锚——承载侧合法形态）
    private class S2_2_Dto
    {
        public int Id { get; set; }

        public string CreatedStamp { get; set; } = "";
    }

    /// <summary>
    /// 验证：ToEntity 方向清单仅在 ToEntity 生效（ToDto 不受影响）
    /// <para>方向判定（PropertyIgnoreChecker）：目标类型标注 [MapFrom] → ToDto，否则 ToEntity。
    /// ToDto 侧用例的目标 DTO 标注 [MapFrom]，使 isToDto=true（测试项目无 analyzer，
    /// [MapFrom] 仅影响方向判定，映射仍走运行时回退路径）。</para>
    /// </summary>
    [Fact]
    public void ToEntityList_OnlyAppliesToEntityDirection()
    {
        Mapper.RegisterGeneratedConfig(new GeneratedMapConfig
        {
            IgnoreNamesToEntity = ["CreatedStamp"],
        }, "S2Test");

        // ToDto（目标 DTO 有 [MapFrom]）：IgnoreNamesToEntity 不生效 → CreatedStamp 映射
        var dto = Mapper.Map<S2_2_Entity, S2_2_DtoToDto>(new S2_2_Entity { Id = 1, CreatedStamp = "stamp" });
        Assert.Equal("stamp", dto!.CreatedStamp);

        // ToEntity（目标实体无 [MapFrom]）：IgnoreNamesToEntity 生效 → CreatedStamp 留空
        var entity = Mapper.Map<S2_2_DtoToDto, S2_2_Entity>(new S2_2_DtoToDto { Id = 1, CreatedStamp = "stamp" });
        Assert.Equal("", entity!.CreatedStamp);
    }

    /// <summary>
    /// ToDto 侧目标 — 标注 [MapFrom] 使方向判定为 ToDto（运行时回退执行）
    /// </summary>
    [MapFrom(typeof(S2_2_Entity))]
    private class S2_2_DtoToDto
    {
        public int Id { get; set; }

        public string CreatedStamp { get; set; } = "";
    }

    // ===== 测试 3-5：OverrideIgnore × 三特性豁免 =====

    private class S2_3_Entity
    {
        public int Id { get; set; }

        public string Password { get; set; } = "";
    }

    /// <summary>
    /// 豁免目标 1 — [MapProperty(OverrideIgnore=true)]
    /// </summary>
    [MapFrom(typeof(S2_3_Entity))] // 补锚
    private class S2_3_Dto
    {
        public int Id { get; set; }

        [MapProperty(typeof(S2_3_Entity), "Password", OverrideIgnore = true)]
        public string Password { get; set; } = "";
    }

    /// <summary>
    /// 验证：OverrideIgnore=true 的 [MapProperty] 豁免全局忽略（忽略清单命中 Password）
    /// </summary>
    [Fact]
    public void OverrideIgnore_MapProperty_ExemptsGlobalIgnore()
    {
        Mapper.RegisterGeneratedConfig(new GeneratedMapConfig { IgnoreNames = ["Password"] }, "S2Test");

        var dto = Mapper.Map<S2_3_Entity, S2_3_Dto>(new S2_3_Entity { Id = 1, Password = "pwd" });

        Assert.Equal(1, dto!.Id);
        // 豁免生效 → Password 仍映射
        Assert.Equal("pwd", dto.Password);
    }

    private class S2_4_Entity
    {
        public int Id { get; set; }

        public string InternalCode { get; set; } = "";
    }

    /// <summary>
    /// 大写转换器（ConvertUsing 豁免用例，静态方法形态 T1 单值模板）
    /// </summary>
    private static class UpperCaseConverter
    {
        /// <summary>
        /// 源 → 目标转换
        /// </summary>
        public static string Convert(string fieldValue) => fieldValue.ToUpperInvariant();
    }

    /// <summary>
    /// 豁免目标 2 — [Convert(..., OverrideIgnore=true)]
    /// </summary>
    [MapFrom(typeof(S2_4_Entity))] // 补锚
    private class S2_4_Dto
    {
        public int Id { get; set; }

        [Convert(typeof(UpperCaseConverter), nameof(UpperCaseConverter.Convert), OverrideIgnore = true)]
        public string InternalCode { get; set; } = "";
    }

    /// <summary>
    /// 验证：OverrideIgnore=true 的 [Convert] 豁免全局忽略（忽略清单命中 InternalCode）
    /// </summary>
    [Fact]
    public void OverrideIgnore_ConvertUsing_ExemptsGlobalIgnore()
    {
        Mapper.RegisterGeneratedConfig(new GeneratedMapConfig { IgnoreNames = ["InternalCode"] }, "S2Test");

        var dto = Mapper.Map<S2_4_Entity, S2_4_Dto>(new S2_4_Entity { Id = 1, InternalCode = "SECRET-1" });

        // 豁免生效 → 转换仍执行
        Assert.Equal("SECRET-1", dto!.InternalCode);
    }

    private class S2_5_Entity
    {
        public int Id { get; set; }

        public string SecretToken { get; set; } = "";
    }

    /// <summary>
    /// 豁免目标 3 — [Mask(OverrideIgnore=true)]
    /// </summary>
    [MapFrom(typeof(S2_5_Entity))] // 补锚
    private class S2_5_Dto
    {
        public int Id { get; set; }

        [Mask(typeof(MaskPhone), OverrideIgnore = true)]
        public string SecretToken { get; set; } = "";
    }

    /// <summary>
    /// 验证：OverrideIgnore=true 的 [Mask] 豁免全局忽略（忽略清单命中 SecretToken）
    /// </summary>
    [Fact]
    public void OverrideIgnore_Mask_ExemptsGlobalIgnore()
    {
        Mapper.RegisterGeneratedConfig(new GeneratedMapConfig { IgnoreNames = ["SecretToken"] }, "S2Test");

        var dto = Mapper.Map<S2_5_Entity, S2_5_Dto>(new S2_5_Entity { Id = 1, SecretToken = "13812345678" });

        // 豁免生效 → 脱敏仍执行（11 位手机号：保留前3后4）
        Assert.Equal("138****5678", dto!.SecretToken);
    }

    // ===== 测试 6：ConvertBackUsing × OverrideIgnore 反向豁免（评审）=====

    private class S2_6_Entity
    {
        public int Id { get; set; }

        public string InternalCode { get; set; } = "";
    }

    /// <summary>
    /// 豁免目标 4 — [ConvertBack(..., OverrideIgnore=true)]（反向写回豁免门）
    /// <para>DTO 属性标 [ConvertBack] 且命中全局忽略清单（IgnoreNamesToEntity）时，
    /// OverrideIgnore=true 豁免映射对，反向写回仍执行转换器。</para>
    /// </summary>
    [MapFrom(typeof(S2_6_Entity))]
    private class S2_6_Dto
    {
        public int Id { get; set; }

        [ConvertBack(typeof(S2_6_BackConverter), nameof(S2_6_BackConverter.Convert), OverrideIgnore = true)]
        public string InternalCode { get; set; } = "";
    }

    /// <summary>
    /// 反向编码转换器（ConvertBackUsing 豁免用例，静态方法形态 T1 单值模板）— 旧接口形态 TSource = DTO 类型
    /// </summary>
    private static class S2_6_BackConverter
    {
        /// <summary>
        /// 源（DTO）→ 目标（实体）转换 — 加方括号标记转换已执行
        /// </summary>
        public static string Convert(string fieldValue) => $"[{fieldValue}]";
    }

    /// <summary>
    /// 验证：OverrideIgnore=true 的 [ConvertBack] 反向豁免全局忽略（IgnoreNamesToEntity 命中 InternalCode）
    /// </summary>
    [Fact]
    public void OverrideIgnore_ConvertBackUsing_ExemptsGlobalIgnore_ToEntity()
    {
        Mapper.RegisterGeneratedConfig(new GeneratedMapConfig { IgnoreNamesToEntity = ["InternalCode"] }, "S2Test");

        // 反向（DTO→Entity）：豁免生效 → 反向写回仍执行转换器
        var entity = Mapper.Map<S2_6_Dto, S2_6_Entity>(new S2_6_Dto { Id = 1, InternalCode = "SECRET-2" });

        Assert.Equal(1, entity!.Id);
        Assert.Equal("[SECRET-2]", entity.InternalCode);
    }

    private class S2_7_Entity
    {
        public int Id { get; set; }

        public string InternalCode { get; set; } = "";
    }

    /// <summary>
    /// 负向对照 DTO — [ConvertBack] 无 OverrideIgnore（默认服从全局忽略）
    /// </summary>
    [MapFrom(typeof(S2_7_Entity))]
    private class S2_7_Dto
    {
        public int Id { get; set; }

        [ConvertBack(typeof(S2_7_BackConverter), nameof(S2_7_BackConverter.Convert))]
        public string InternalCode { get; set; } = "";
    }

    /// <summary>
    /// 反向编码转换器（负向对照用例，静态方法形态 T1 单值模板）
    /// </summary>
    private static class S2_7_BackConverter
    {
        /// <summary>
        /// 源（DTO）→ 目标（实体）转换 — 加方括号标记转换已执行
        /// </summary>
        public static string Convert(string fieldValue) => $"[{fieldValue}]";
    }

    /// <summary>
    /// 验证：无 OverrideIgnore 的 [ConvertBack] 命中全局忽略 → fail-closed 忽略（InternalCode 留空）
    /// </summary>
    [Fact]
    public void ConvertBackUsing_NoOverrideIgnore_HitGlobalIgnore_Ignored()
    {
        Mapper.RegisterGeneratedConfig(new GeneratedMapConfig { IgnoreNamesToEntity = ["InternalCode"] }, "S2Test");

        // 反向（DTO→Entity）：无豁免 → 属性被忽略，转换器不执行
        var entity = Mapper.Map<S2_7_Dto, S2_7_Entity>(new S2_7_Dto { Id = 1, InternalCode = "SECRET-3" });

        Assert.Equal(1, entity!.Id);
        Assert.Equal("", entity.InternalCode);
    }

    // ===== 判定面四象限（运行时侧，每象限独立类型对） =====

    // 象限 1：源属性命中忽略清单

    private class S2_Q1_Entity
    {
        public string user_name { get; set; } = "";

        public string UserName { get; set; } = "";
    }

    private class S2_Q1_Dto
    {
        public string user_name { get; set; } = "";

        public string UserName { get; set; } = "";
    }

    /// <summary>
    /// 验证：象限 1 — 仅源属性命中忽略清单（user_name 在源侧）→ 整对忽略
    /// </summary>
    [Fact]
    public void Quadrant1_SourceHit_Ignored()
    {
        Mapper.RegisterGeneratedConfig(new GeneratedMapConfig { IgnoreNames = ["user_name"] }, "S2Test");

        var dto = Mapper.Map<S2_Q1_Entity, S2_Q1_Dto>(new S2_Q1_Entity { user_name = "src", UserName = "tgt" });

        // 源侧 user_name 命中 → 目标 user_name 不映射；UserName 不受影响
        Assert.Equal("", dto!.user_name);
        Assert.Equal("tgt", dto.UserName);
    }

    // 象限 2：目标属性命中忽略清单

    private class S2_Q2_Entity
    {
        public string user_name { get; set; } = "";

        public string UserName { get; set; } = "";
    }

    private class S2_Q2_Dto
    {
        public string user_name { get; set; } = "";

        public string UserName { get; set; } = "";
    }

    /// <summary>
    /// 验证：象限 2 — 仅目标属性命中忽略清单（UserName 在目标侧）→ 整对忽略
    /// </summary>
    [Fact]
    public void Quadrant2_TargetHit_Ignored()
    {
        Mapper.RegisterGeneratedConfig(new GeneratedMapConfig { IgnoreNames = ["UserName"] }, "S2Test");

        var dto = Mapper.Map<S2_Q2_Entity, S2_Q2_Dto>(new S2_Q2_Entity { user_name = "src", UserName = "tgt" });

        // 目标侧 UserName 命中 → 不映射；user_name 仍按名匹配（源 user_name → 目标 user_name）
        Assert.Equal("src", dto!.user_name);
        Assert.Equal("", dto.UserName);
    }

    // 象限 3：源命中 + 目标 OverrideIgnore → 豁免恢复映射

    private class S2_Q3_Entity
    {
        public string Exempted { get; set; } = "";
    }

    [MapFrom(typeof(S2_Q3_Entity))] // 补锚
    private class S2_Q3_Dto
    {
        [MapProperty(typeof(S2_Q3_Entity), "Exempted", OverrideIgnore = true)]
        public string Exempted { get; set; } = "";
    }

    /// <summary>
    /// 验证：象限 3 — 源命中 + 目标带 OverrideIgnore → 豁免恢复映射
    /// <para>目标 Exempted 带 [MapProperty(OverrideIgnore=true)]，源 Exempted 命中忽略清单 → 整对豁免。</para>
    /// </summary>
    [Fact]
    public void Quadrant3_SourceHitWithExemption_Recovered()
    {
        Mapper.RegisterGeneratedConfig(new GeneratedMapConfig { IgnoreNames = ["Exempted"] }, "S2Test");

        var dto = Mapper.Map<S2_Q3_Entity, S2_Q3_Dto>(new S2_Q3_Entity { Exempted = "keep" });

        // 豁免生效 → Exempted 仍映射（豁免作用于映射对整体）
        Assert.Equal("keep", dto!.Exempted);
    }
}
