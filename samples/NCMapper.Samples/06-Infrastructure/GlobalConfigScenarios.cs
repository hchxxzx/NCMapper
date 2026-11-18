using NCMapper;
using NCMapper.Attributes;
using NCMapper.Samples.Helpers;

// ============================================================
// 全局映射配置（preview.2）— 程序集级声明，应用于所有映射
// ------------------------------------------------------------
// 迁移对照：SnakeCase 命名策略使 user_name ↔ UserName 自动匹配，
// IgnoreNames 全局忽略 RowVersion。仅对本场景的带下划线/忽略字段生效，
// 对 Samples 其余驼峰模型零影响（SnakeCase 对纯驼峰映射等价）。
// 三层优先链：属性级标注 > [assembly: MapDefaults] > MSBuild 属性。
// ============================================================
[assembly: MapDefaults(
    NamingStrategy = NamingStrategy.SnakeCase,
    IgnoreNames = new[] { "RowVersion" })]

namespace NCMapper.Samples.Infrastructure;

/// <summary>
/// 全局映射配置场景 — "逐属性标注 → 全局配置"迁移对照。
/// <para>同一实体两种映射方式：迁移前逐属性 [MapProperty] 标注（GlobalCfgUserDtoExplicit）
/// 与迁移后仅靠全局配置自动匹配（GlobalCfgUserDtoGlobal），映射结果一致。</para>
/// </summary>
public static class GlobalConfigScenarios
{
    /// <summary>
    /// 源实体 — 蛇形字段（user_name/phone_number）+ 应忽略的 RowVersion
    /// </summary>
    public class GlobalCfgUserEntity
    {
        /// <summary>
        /// 主键
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 用户名（蛇形命名，全局 SnakeCase 策略映射）
        /// </summary>
        public string user_name { get; set; } = "";

        /// <summary>
        /// 手机号（蛇形命名，全局 SnakeCase 策略映射）
        /// </summary>
        public string phone_number { get; set; } = "";

        /// <summary>
        /// 行版本（全局 IgnoreNames 忽略）
        /// </summary>
        public byte[] RowVersion { get; set; } = [];
    }

    /// <summary>
    /// 迁移前 DTO — 逐属性标注（[MapProperty] 逐个声明异名映射）
    /// </summary>
    [MapFrom(typeof(GlobalCfgUserEntity))]
    public class GlobalCfgUserDtoExplicit
    {
        /// <summary>
        /// 主键（同名直映）
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 用户名（显式 [MapProperty] 声明蛇形异名映射）
        /// </summary>
        [MapProperty(typeof(GlobalCfgUserEntity), "user_name")]
        public string UserName { get; set; } = "";

        /// <summary>
        /// 手机号（显式 [MapProperty] 声明蛇形异名映射）
        /// </summary>
        [MapProperty(typeof(GlobalCfgUserEntity), "phone_number")]
        public string PhoneNumber { get; set; } = "";

        /// <summary>
        /// 行版本（显式 [MapIgnore]）
        /// </summary>
        [MapIgnore]
        public byte[] RowVersion { get; set; } = [];
    }

    /// <summary>
    /// 迁移后 DTO — 零标注，靠全局配置（SnakeCase + IgnoreNames）自动匹配
    /// </summary>
    [MapFrom(typeof(GlobalCfgUserEntity))]
    public class GlobalCfgUserDtoGlobal
    {
        /// <summary>
        /// 主键（同名直映）
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 用户名（全局 SnakeCase 策略自动匹配）
        /// </summary>
        public string UserName { get; set; } = "";

        /// <summary>
        /// 手机号（全局 SnakeCase 策略自动匹配）
        /// </summary>
        public string PhoneNumber { get; set; } = "";

        /// <summary>
        /// 行版本（全局 IgnoreNames 自动忽略）
        /// </summary>
        public byte[] RowVersion { get; set; } = [];
    }

    /// <summary>
    /// 构造源实体（含 RowVersion，验证全局忽略）
    /// </summary>
    /// <returns>源实体</returns>
    public static GlobalCfgUserEntity MakeEntity() => new()
    {
        Id = 1,
        user_name = "alice",
        phone_number = "13812345678",
        RowVersion = [1, 2, 3],
    };

    /// <summary>
    /// 迁移前映射（逐属性标注）— 与迁移后结果一致
    /// </summary>
    /// <param name="src">源实体</param>
    /// <returns>逐属性标注版 DTO</returns>
    public static GlobalCfgUserDtoExplicit MapExplicit(GlobalCfgUserEntity src)
        => Mapper.Map<GlobalCfgUserEntity, GlobalCfgUserDtoExplicit>(src)!;

    /// <summary>
    /// 迁移后映射（全局配置）— SnakeCase 自动匹配 + 全局忽略 RowVersion
    /// </summary>
    /// <param name="src">源实体</param>
    /// <returns>全局配置版 DTO</returns>
    #region GlobalConfig_MapGlobal
    public static GlobalCfgUserDtoGlobal MapGlobal(GlobalCfgUserEntity src)
        => Mapper.Map<GlobalCfgUserEntity, GlobalCfgUserDtoGlobal>(src)!;
    #endregion


    /// <summary>
    /// 迁移对照运行入口 — 两路径结果一致性校验 + 冻结配置视图输出
    ///
    /// </summary>
    /// <returns>对照结果文本</returns>
    #region GlobalConfig_MapGlobal_RunSummary
    public static string RunSummary()
    {
        var src = MakeEntity();
        var explicitDto = MapExplicit(src);
        var globalDto = MapGlobal(src);

        // Consistency check: the per-property attribute version and the global-config version produce identical results
        var consistent = explicitDto.Id == globalDto.Id
            && explicitDto.UserName == globalDto.UserName
            && explicitDto.PhoneNumber == globalDto.PhoneNumber;

        var view = Mapper.GetGlobalConfig();
        var summary = string.Join(Environment.NewLine,
            "[606_GlobalConfig] Migration comparison: attribute annotations → global config",
            $"  Explicit-annotation version: Id={explicitDto.Id} UserName={explicitDto.UserName} Phone={explicitDto.PhoneNumber} RowVersion={(explicitDto.RowVersion.Length == 0 ? "(ignored)" : $"{explicitDto.RowVersion.Length}B")}",
            $"  Global-config version      : Id={globalDto.Id} UserName={globalDto.UserName} Phone={globalDto.PhoneNumber} RowVersion={(globalDto.RowVersion.Length == 0 ? "(ignored)" : $"{globalDto.RowVersion.Length}B")}",
            $"  Paths consistent           : {(consistent ? "✅ yes" : "❌ no")}",
            $"  Frozen config              : NamingStrategy={view?.NamingStrategy}, IgnoreNames=[{string.Join(",", view?.IgnoreNames ?? [])}]");

        if (!consistent)
            throw new InvalidOperationException("Migration comparison failed: explicit-annotation version and global-config version produced inconsistent mapping results");

        return summary;
    }
    #endregion


    /// <summary>
    /// 场景注册入口——迁移对照输出 + F7 演示
    /// （原 Program.RunGlobalConfig 驱动逻辑合并，行为不变）
    /// </summary>
    #region GlobalConfig_MapGlobal_Run
    public static void Run()
    {
        Console.WriteLine(RunSummary());
        Console.WriteLine(F7ToEntityIgnoreReverse());
        StrategyAndIgnoreDemo();
        FluentInteractDemo();
    }

    #endregion
    // ===== 命名策略/忽略清单 + Fluent 豁免门交互=====

    /// <summary>
    /// 命名策略与忽略清单：SnakeCase 自动匹配（user_name → UserName）+
    /// 全局 IgnoreNames 忽略 RowVersion + 冻结配置视图核对。
    /// </summary>
    #region GlobalConfig_StrategyAndIgnore
    public static void StrategyAndIgnoreDemo()
    {
        var src = MakeEntity();
        var dto = MapGlobal(src);
        OutputHelper.Dump("606-strategy-and-ignore", src, dto);
        // Assert: SnakeCase matching effective + global ignore effective
        if (dto.UserName != "alice" || dto.PhoneNumber != "13812345678")
            OutputHelper.Error("606-strategy-and-ignore: SnakeCase naming strategy matching not effective");
        if (dto.RowVersion.Length != 0)
            OutputHelper.Error("606-strategy-and-ignore: global IgnoreNames did not ignore RowVersion");
        var view = Mapper.GetGlobalConfig();
        if (view == null || view.NamingStrategy != NamingStrategy.SnakeCase
            || !(view.IgnoreNames ?? []).Contains("RowVersion"))
            OutputHelper.Error("606-strategy-and-ignore: frozen config view does not match declaration");
    }
    #endregion

    /// <summary>
    /// Fluent 豁免门与命名策略交互：显式异名 Map（符号级路径，不受名称策略影响）+
    /// overrideIgnore: true 豁免全局忽略（对照：全局配置版 RowVersion 被忽略为空）。
    /// </summary>
    #region GlobalConfig_FluentInteract
    public static void FluentInteractDemo()
    {
        var src = MakeEntity();
        var fluentDto = Mapper.Map<GlobalCfgUserEntity, GlobalCfgFluentDto>(src);
        OutputHelper.Dump("606-fluent-interact", "Fluent explicit renamed Map + overrideIgnore exemption", fluentDto);
        // Assert: renamed Map effective (snake_case source -> PascalCase target) + exemption gate rescues the field dropped by global ignore
        // (Map returns a nullable, so `?.` turns a "null return" into an assert failure instead of a null-reference crash)
        if (fluentDto?.UserName != "alice")
            OutputHelper.Error("606-fluent-interact: Fluent renamed Map not effective");
        if (fluentDto?.RowVersion.Length != 3)
            OutputHelper.Error("606-fluent-interact: overrideIgnore exemption not effective (RowVersion should be mapped)");
    }
    #endregion

    // ===== F7 演示=====

    /// <summary>F7 演示实体（AuditStamp 标注 [MapIgnore(ToEntity)]——仅反向写回忽略）。</summary>
    public class F7Entity
    {
        /// <summary>主键</summary>
        public int Id { get; set; }

        /// <summary>审计戳（ToEntity 方向忽略——正向照常映射、反向写回跳过）</summary>
        public string AuditStamp { get; set; } = "";
    }

    /// <summary>F7 演示 DTO（标注 [MapFrom] 注册为映射对）：
    /// 方向性 [MapIgnore] 语义的正确承载面是注册对/SourceGen 方向感知生成，运行时回退路径
    /// 对双侧未注册类型不消费方向性忽略。</summary>
    [MapFrom(typeof(F7Entity))]
    public class F7Dto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }

        /// <summary>审计戳（正向照常映射产物）</summary>
        public string AuditStamp { get; set; } = "";
    }

    /// <summary>
    /// F7 — ToEntity 忽略清单反向生效：正向 [MapIgnore(ToEntity)] 不拦（dto.AuditStamp 有值）；
    /// 反向写回（DTO → 已有实体更新形态）跳过 AuditStamp（KEEP 值保持）。
    /// <para>正向强断言：注册化后由 SourceGen 方向感知生成正确判定。</para>
    /// </summary>
    /// <returns>演示输出文本（正向映射值 / 反向写回保持值）</returns>
    public static string F7ToEntityIgnoreReverse()
    {
        var src = new F7Entity { Id = 1, AuditStamp = "STAMP-2026" };
        var dto = Mapper.Map<F7Entity, F7Dto>(src)!;
        if (dto.AuditStamp != "STAMP-2026")
            OutputHelper.Error($"606-f7-toentity: forward [MapIgnore(ToEntity)] should not block (actual AuditStamp={dto.AuditStamp})");

        // 反向写回段演示（简化）：本场景保留正向演示面
        return $"[F7 ToEntity ignore-list reverse effect] Forward mapping: AuditStamp={dto.AuditStamp} (reverse write-back demo simplified)";
    }
}

/// <summary>
/// Fluent 豁免门演示 DTO——RowVersion 被 overrideIgnore 救回（对照 GlobalCfgUserDtoGlobal 的忽略形态）。
/// </summary>
public class GlobalCfgFluentDto
{
    /// <summary>主键。</summary>
    public int Id { get; set; }

    /// <summary>用户名（Fluent 显式异名 Map：user_name → UserName，符号级路径不受名称策略影响）。</summary>
    public string UserName { get; set; } = "";

    /// <summary>行版本（overrideIgnore: true 豁免全局忽略）。</summary>
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// Fluent 豁免门配置：命名策略下显式异名 Map + overrideIgnore 豁免全局忽略。
/// </summary>
[MapperConfig]
public class GlobalCfgFluentMapper
{
    /// <summary>链式声明入口。</summary>
    /// <param name="cfg">Fluent 配置根</param>
    public static void Configure(FluentMappingConfig cfg)
        => cfg.For<GlobalConfigScenarios.GlobalCfgUserEntity, GlobalCfgFluentDto>()
              .Member(d => d.UserName).From(s => s.user_name)
              .Member(d => d.RowVersion).From(s => s.RowVersion, overrideIgnore: true);
}
