using NCMapper;
using Xunit;

namespace NCMapper.Tests;

/// <summary>
/// 全局命名策略 × 前后缀剥离测试（运行时回退路径）
/// <para>对应任务 S1：4 策略 × 运行时回退 happy path +
/// 前后缀剥离（仅前/仅后/组合/空清单）。源生成侧同策略用例见
/// NCMapper.SourceGen.Tests.GlobalConfigMatchingTests（生成代码注入断言）。</para>
/// <para><b>测试隔离约束</b>：运行时映射委托缓存在泛型静态字段（MapperBase 的 Lazy），
/// ClearCache 无法清除（文档明示）；每个测试使用独立类型对，保证各自在目标配置下首次编译。</para>
/// <para>本集合与 GlobalConfigTests 共享进程级冻结状态，禁止并行。</para>
/// </summary>
[Collection("GlobalConfigState")]
public class GlobalNamingStrategyTests : IDisposable
{
    /// <summary>
    /// 每个测试前重置全局配置冻结状态（测试隔离）
    /// </summary>
    public GlobalNamingStrategyTests()
    {
        Mapper.ClearCache();        // 迁移：Reset 不再清注册表，测试隔离需显式全清
        GlobalMapConfig.ResetForTests();
    }

    /// <summary>
    /// 类级清理 — 消除集合结束后冻结配置残留（进程级单例，
    /// 残留会污染并行/后续执行的未感知测试类，差异 #12 同类项）
    /// </summary>
    public void Dispose() { Mapper.ClearCache(); GlobalMapConfig.ResetForTests(); } // 迁移

    // ===== 测试 1：Default（独立类型对 S1_1） =====

    private class S1_1_Entity
    {
        public int User_id { get; set; }

        public string User_name { get; set; } = "";
    }

    private class S1_1_Dto
    {
        public int UserId { get; set; }

        public string UserName { get; set; } = "";
    }

    /// <summary>
    /// 验证：Default 策略（内建默认）— 按名 OrdinalIgnoreCase 匹配，蛇形/驼峰不互通（等价现状）
    /// </summary>
    [Fact]
    public void Default_SnakeVsCamel_NotMatched()
    {
        var dto = Mapper.Map<S1_1_Entity, S1_1_Dto>(new S1_1_Entity { User_id = 7, User_name = "alice" });

        // 蛇形源属性名与驼峰目标属性名不同名 → 不匹配（保持现状）
        Assert.Equal(0, dto!.UserId);
        Assert.Equal("", dto.UserName);
    }

    // ===== 测试 2：SnakeCase（独立类型对 S1_2） =====

    private class S1_2_Entity
    {
        public int User_id { get; set; }

        public string User_name { get; set; } = "";

        public string? Phone_number { get; set; }
    }

    private class S1_2_Dto
    {
        public int UserId { get; set; }

        public string UserName { get; set; } = "";

        public string? PhoneNumber { get; set; }
    }

    /// <summary>
    /// 验证：SnakeCase 策略 — user_name ↔ UserName 匹配成功（运行时回退路径）
    /// </summary>
    [Fact]
    public void SnakeCase_MatchesSnakeToCamel()
    {
        Mapper.RegisterGeneratedConfig(new GeneratedMapConfig { NamingStrategy = (int)NamingStrategy.SnakeCase }, "S1Test");

        var dto = Mapper.Map<S1_2_Entity, S1_2_Dto>(new S1_2_Entity { User_id = 7, User_name = "alice", Phone_number = "12345" });

        Assert.Equal(7, dto!.UserId);
        Assert.Equal("alice", dto.UserName);
        Assert.Equal("12345", dto.PhoneNumber);
    }

    // ===== 测试 3：UpperSnakeCase（独立类型对 S1_3） =====

    private class S1_3_Entity
    {
        public int USER_ID { get; set; }

        public string USER_NAME { get; set; } = "";
    }

    private class S1_3_Dto
    {
        public int UserId { get; set; }

        public string UserName { get; set; } = "";
    }

    /// <summary>
    /// 验证：UpperSnakeCase 策略 — USER_NAME 归一化后命中（运行时回退路径）
    /// </summary>
    [Fact]
    public void UpperSnakeCase_Matches()
    {
        Mapper.RegisterGeneratedConfig(new GeneratedMapConfig { NamingStrategy = (int)NamingStrategy.UpperSnakeCase }, "S1Test");

        var dto = Mapper.Map<S1_3_Entity, S1_3_Dto>(new S1_3_Entity { USER_ID = 7, USER_NAME = "alice" });

        Assert.Equal(7, dto!.UserId);
        Assert.Equal("alice", dto.UserName);
    }

    // ===== 测试 4：CaseSensitive（独立类型对 S1_4a / S1_4b） =====

    private class S1_4a_Entity
    {
        public string user_name { get; set; } = "";
    }

    private class S1_4a_Dto
    {
        public string UserName { get; set; } = "";
    }

    private class S1_4b_Entity
    {
        public string UserName { get; set; } = "";
    }

    private class S1_4b_Dto
    {
        public string UserName { get; set; } = "";
    }

    /// <summary>
    /// 验证：CaseSensitive 策略 — 大小写不同不匹配，完全相同才匹配（运行时回退路径）
    /// </summary>
    [Fact]
    public void CaseSensitive_ExactMatchOnly()
    {
        Mapper.RegisterGeneratedConfig(new GeneratedMapConfig { NamingStrategy = (int)NamingStrategy.CaseSensitive }, "S1Test");

        // 源 user_name vs 目标 UserName：CaseSensitive 下不一致 → 不匹配
        var dto = Mapper.Map<S1_4a_Entity, S1_4a_Dto>(new S1_4a_Entity { user_name = "alice" });
        Assert.Equal("", dto!.UserName);

        // 源 UserName vs 目标 UserName：精确一致 → 匹配
        var exact = Mapper.Map<S1_4b_Entity, S1_4b_Dto>(new S1_4b_Entity { UserName = "bob" });
        Assert.Equal("bob", exact!.UserName);
    }

    // ===== 前后缀剥离 =====

    // 测试 5：仅前缀（独立类型对 S1_5）

    private class S1_5_Entity
    {
        public int TblId { get; set; }

        public string TblName { get; set; } = "";
    }

    private class S1_5_Dto
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";
    }

    /// <summary>
    /// 验证：仅前缀剥离 — TblName ↔ Name
    /// </summary>
    [Fact]
    public void PrefixOnly_StripsAndMatches()
    {
        Mapper.RegisterGeneratedConfig(new GeneratedMapConfig { Prefixes = ["Tbl"] }, "S1Test");

        var dto = Mapper.Map<S1_5_Entity, S1_5_Dto>(new S1_5_Entity { TblId = 1, TblName = "x" });

        Assert.Equal(1, dto!.Id);
        Assert.Equal("x", dto.Name);
    }

    // 测试 6：仅后缀（独立类型对 S1_6）

    private class S1_6_Entity
    {
        public int IdEntity { get; set; }

        public string NameEntity { get; set; } = "";
    }

    private class S1_6_Dto
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";
    }

    /// <summary>
    /// 验证：仅后缀剥离 — NameEntity ↔ Name
    /// </summary>
    [Fact]
    public void PostfixOnly_StripsAndMatches()
    {
        Mapper.RegisterGeneratedConfig(new GeneratedMapConfig { Postfixes = ["Entity"] }, "S1Test");

        var dto = Mapper.Map<S1_6_Entity, S1_6_Dto>(new S1_6_Entity { IdEntity = 2, NameEntity = "y" });

        Assert.Equal(2, dto!.Id);
        Assert.Equal("y", dto.Name);
    }

    // 测试 7：前后缀组合（独立类型对 S1_7）

    private class S1_7_Entity
    {
        public string TblNameEntity { get; set; } = "";
    }

    private class S1_7_Dto
    {
        public string Name { get; set; } = "";
    }

    /// <summary>
    /// 验证：前后缀组合剥离 — TblNameEntity ↔ Name
    /// </summary>
    [Fact]
    public void PrefixAndPostfix_ComboStrips()
    {
        Mapper.RegisterGeneratedConfig(new GeneratedMapConfig { Prefixes = ["Tbl"], Postfixes = ["Entity"] }, "S1Test");

        var dto = Mapper.Map<S1_7_Entity, S1_7_Dto>(new S1_7_Entity { TblNameEntity = "z" });

        Assert.Equal("z", dto!.Name);
    }

    // 测试 8：空清单（独立类型对 S1_8）

    private class S1_8_Entity
    {
        public int TblId { get; set; }

        public string TblName { get; set; } = "";
    }

    private class S1_8_Dto
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";
    }

    /// <summary>
    /// 验证：空清单（无剥缀、无策略）— 与内建默认逐字节一致（OrdinalIgnoreCase 按名匹配）
    /// </summary>
    [Fact]
    public void EmptyLists_BehavesAsBuiltInDefault()
    {
        Mapper.RegisterGeneratedConfig(new GeneratedMapConfig { Prefixes = [], Postfixes = [] }, "S1Test");

        var dto = Mapper.Map<S1_8_Entity, S1_8_Dto>(new S1_8_Entity { TblId = 1, TblName = "x" });

        // 无剥缀配置 → TblId 不与 Id 匹配（现状行为）
        Assert.Equal(0, dto!.Id);
        Assert.Equal("", dto.Name);
    }
}
