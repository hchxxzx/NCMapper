using NCMapper;
using NCMapper.Attributes;
using Xunit;

namespace NCMapper.Tests;

/// <summary>
/// 优先级链（显式标注 > 全局配置）+ 构造器参数匹配恒 OrdinalIgnoreCase（运行时回退路径）
/// <para>对应任务 S3：
/// ① 优先级链逐层覆盖（MSBuild/特性合并结果经快照注入；运行时侧验证"属性级显式标注 > 全局配置"，
/// 三层合并语义已由 GlobalConfigReaderTests.Merge_ThreeLayerPrecedence 覆盖）；
/// ② 构造器参数匹配（v1.2 W4：构造器配对恒 OrdinalIgnoreCase，不随策略）。</para>
/// <para><b>测试隔离约束</b>：运行时映射委托缓存在泛型静态字段（MapperBase 的 Lazy），
/// ClearCache 无法清除；每个测试使用独立类型对。</para>
/// <para>本集合与 GlobalConfigTests 共享进程级冻结状态，禁止并行。</para>
/// </summary>
[Collection("GlobalConfigState")]
public class PriorityChainAndCtorTests : IDisposable
{
    /// <summary>
    /// 每个测试前重置全局配置冻结状态（测试隔离）
    /// </summary>
    public PriorityChainAndCtorTests()
    {
        Mapper.ClearCache();        // 迁移：Reset 不再清注册表，测试隔离需显式全清
        GlobalMapConfig.ResetForTests();
    }

    /// <summary>
    /// 类级清理 — 消除集合结束后冻结配置残留（进程级单例，
    /// 残留会污染并行/后续执行的未感知测试类，差异 #12 同类项）
    /// </summary>
    public void Dispose() { Mapper.ClearCache(); GlobalMapConfig.ResetForTests(); } // 迁移

    // ===== ① 优先级链：显式标注 > 全局配置 =====

    private class P1_Entity
    {
        public string user_name { get; set; } = "";

        public string OtherName { get; set; } = "";
    }

    /// <summary>
    /// 目标 — [MapProperty(FromField)] 显式标注（G3：字面匹配，不随命名策略）
    /// <para>标注 [MapFrom] 使 sourceIsDefine/targetIsDefine 进入显式标注分支
    /// （运行时回退路径下 [MapProperty] 仅在单侧 Define 时参与匹配，既有设计）。</para>
    /// </summary>
    [MapFrom(typeof(P1_Entity))]
    private class P1_Dto
    {
        // 显式指向 user_name：SnakeCase 策略下仍按 FromField 字面匹配
        [MapProperty(typeof(P1_Entity), "user_name")]
        public string DisplayName { get; set; } = "";
    }

    /// <summary>
    /// 验证：SnakeCase 策略下，[MapProperty(FromField="user_name")] 显式标注优先于策略匹配
    /// <para>DisplayName 非策略候选（DisplayName 与 user_name 无策略等价关系），
    /// 仅显式 FromField 能建立映射 → 证明显式标注 > 全局配置。</para>
    /// </summary>
    [Fact]
    public void ExplicitMapProperty_OverridesNamingStrategy()
    {
        Mapper.RegisterGeneratedConfig(new GeneratedMapConfig { NamingStrategy = (int)NamingStrategy.SnakeCase }, "S3Test");

        var dto = Mapper.Map<P1_Entity, P1_Dto>(new P1_Entity { user_name = "alice" });

        Assert.Equal("alice", dto!.DisplayName);
    }

    private class P2_Entity
    {
        public string Password { get; set; } = "";
    }

    /// <summary>
    /// 目标 — [MapProperty(OverrideIgnore=true)] 豁免优先于全局忽略
    /// </summary>
    [MapFrom(typeof(P2_Entity))] // 补锚——豁免语义以有锚映射对为前提
    private class P2_Dto
    {
        [MapProperty(typeof(P2_Entity), "Password", OverrideIgnore = true)]
        public string Password { get; set; } = "";
    }

    /// <summary>
    /// 验证：全局忽略清单命中 + OverrideIgnore=true → 显式豁免优先于全局配置（fail-open 唯一例外）
    /// </summary>
    [Fact]
    public void OverrideIgnore_PriorityOverGlobalIgnore()
    {
        Mapper.RegisterGeneratedConfig(new GeneratedMapConfig { IgnoreNames = ["Password"] }, "S3Test");

        var dto = Mapper.Map<P2_Entity, P2_Dto>(new P2_Entity { Password = "pwd" });

        Assert.Equal("pwd", dto!.Password);
    }

    // ===== ② 构造器参数匹配：恒 OrdinalIgnoreCase（不随策略） =====

    // 场景 A：SnakeCase 下构造器注入与属性初始化行为一致（独立类型对 P3）

    private class P3_Entity
    {
        public int user_id { get; set; }

        public string user_name { get; set; } = "";
    }

    /// <summary>
    /// 构造器注入 DTO — record 主构造函数（无无参构造，走 BuildConstructorArgs）
    /// </summary>
    private record P3_CtorDto(int user_id, string user_name);

    /// <summary>
    /// 属性初始化 DTO
    /// </summary>
    private class P3_PropDto
    {
        public int UserId { get; set; }

        public string UserName { get; set; } = "";
    }

    /// <summary>
    /// 验证：SnakeCase 下构造器注入 DTO 与属性初始化 DTO 行为一致（均映射成功）
    /// <para>构造器参数 user_name 与源 user_name 同形（OrdinalIgnoreCase 恒匹配）；
    /// 属性 UserName 经 SnakeCase 策略匹配。两条路径结果一致。</para>
    /// </summary>
    [Fact]
    public void SnakeCase_CtorAndPropertyPaths_Consistent()
    {
        Mapper.RegisterGeneratedConfig(new GeneratedMapConfig { NamingStrategy = (int)NamingStrategy.SnakeCase }, "S3Test");

        var viaCtor = Mapper.Map<P3_Entity, P3_CtorDto>(new P3_Entity { user_id = 7, user_name = "alice" });
        var viaProp = Mapper.Map<P3_Entity, P3_PropDto>(new P3_Entity { user_id = 7, user_name = "alice" });

        Assert.Equal(7, viaCtor!.user_id);
        Assert.Equal("alice", viaCtor.user_name);
        Assert.Equal(7, viaProp!.UserId);
        Assert.Equal("alice", viaProp.UserName);
    }

    // 场景 B：CaseSensitive 下构造器仍恒 OrdinalIgnoreCase 匹配，属性路径不匹配（独立类型对 P4）

    private class P4_Entity
    {
        public string username { get; set; } = "";
    }

    /// <summary>
    /// 构造器注入 DTO — 参数 username（与源同形）
    /// </summary>
    private record P4_CtorDto(string username);

    /// <summary>
    /// 属性初始化 DTO — 属性 UserName（CaseSensitive 下与源 username 不同）
    /// </summary>
    private class P4_PropDto
    {
        public string UserName { get; set; } = "";
    }

    /// <summary>
    /// 验证：CaseSensitive 策略下构造器参数匹配恒 OrdinalIgnoreCase（v1.2 W4）
    /// <para>源 username → 构造器参数 username（同形）匹配成功；
    /// 属性路径 UserName 在 CaseSensitive 下与 username 不匹配 → 留空。
    /// 锁定"构造器配对恒 OrdinalIgnoreCase、不随策略"语义。</para>
    /// </summary>
    [Fact]
    public void CaseSensitive_CtorStillOrdinalIgnoreCase()
    {
        Mapper.RegisterGeneratedConfig(new GeneratedMapConfig { NamingStrategy = (int)NamingStrategy.CaseSensitive }, "S3Test");

        var viaCtor = Mapper.Map<P4_Entity, P4_CtorDto>(new P4_Entity { username = "bob" });
        var viaProp = Mapper.Map<P4_Entity, P4_PropDto>(new P4_Entity { username = "bob" });

        // 构造器：恒 OrdinalIgnoreCase → username 命中
        Assert.Equal("bob", viaCtor!.username);
        // 属性：CaseSensitive → username ≠ UserName → 留空
        Assert.Equal("", viaProp!.UserName);
    }
}
