// P1-7 语义形态等价护栏（覆盖矩阵维度 1/2——见 v1.1）：
//   护栏一 A/B 行为等价——同谓词语义分别以 A 静态方法组 / B 无捕获内联转录声明（同构类型对，
//   同对重复声明触发 NCMAP_F16 故分对承载），同输入集断言运行输出一致
//   （条件真/假 × 新建/更新 × T1 字段值/T2 整源）——防转录语义退化/包裹丢失（生成器重构护栏）。
//   护栏二 守卫有无对照——有 MapWhen vs 无守卫声明三向对照：条件假时有守卫保持
//   初始化器/既有值、无守卫无条件赋值（行为层系统化，字节不变门 G3 之外的行为面护栏）。
// 先验锚（绝对断言）：条件假输出 = 初始值/既有值——防 A/B 同错双绿假绿（经验 #67）。
// 覆盖缺口依据（覆盖矩阵，审计登记落）：既有 MapWhenFluentExecutionTests 各形态
//   独立断言（WgFA/WgFB 谓词宿主不同），无 A/B 交叉等价断言、无守卫有无对照。
// 隔离范式（经验库 #71）：[Collection("GlobalConfigState")]；fixture 编译进测试程序集 →
//   module initializer 注册 GeneratedCache（独立容器——注册表清理不影响本文件断言）。
using NCMapper;
using Xunit;

namespace NCMapper.Tests.Runtime;

[Collection("GlobalConfigState")]
public class MapWhenEquivalenceGuardTests
{
    // ==== 护栏一模型：A/B 同谓词语义（T1 字段值形态） ====
    // 谓词语义统一 "长度 > 2"：条件假源值（"ab"/"x"）≠ 初始值（""）——守卫丢失可区分（M2 检出性）
    public class EgAEntity { public int Id { get; set; } public string Name { get; set; } = ""; }
    public class EgADto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";

        public static bool HasLengthOver2(string value) => value.Length > 2; // 与 B 内联同语义
    }

    public class EgBEntity { public int Id { get; set; } public string Name { get; set; } = ""; }
    public class EgBDto { public int Id { get; set; } public string Name { get; set; } = ""; }

    [MapperConfig]
    internal class EgAT1Mapper
    {
        public static void Configure(FluentMappingConfig cfg)
        {
            cfg.For<EgAEntity, EgADto>()
               .Member(d => d.Name).MapWhen(EgADto.HasLengthOver2); // A 形态：方法组
        }
    }

    [MapperConfig]
    internal class EgBT1Mapper
    {
        public static void Configure(FluentMappingConfig cfg)
        {
            cfg.For<EgBEntity, EgBDto>()
               .Member(d => d.Name).MapWhen(v => v.Length > 2); // B 形态：无捕获内联（转录）
        }
    }

    // ==== 护栏一模型：A/B 同谓词语义（T2 整源形态） ====
    // 谓词语义统一 "Score > 0"：负/零假、正真——与 T1 不同包裹路径（整源直呼 vs 局部变量承载）
    public class EgA2Entity { public int Id { get; set; } public int Score { get; set; } }
    public class EgA2Dto
    {
        public int Id { get; set; }
        public int Score { get; set; }

        public static bool ScorePositive(EgA2Entity source) => source.Score > 0; // 与 B 内联同语义
    }

    public class EgB2Entity { public int Id { get; set; } public int Score { get; set; } }
    public class EgB2Dto { public int Id { get; set; } public int Score { get; set; } }

    [MapperConfig]
    internal class EgAT2Mapper
    {
        public static void Configure(FluentMappingConfig cfg)
        {
            cfg.For<EgA2Entity, EgA2Dto>()
               .Member(d => d.Score).MapWhen<EgA2Entity>(EgA2Dto.ScorePositive);
        }
    }

    [MapperConfig]
    internal class EgBT2Mapper
    {
        public static void Configure(FluentMappingConfig cfg)
        {
            cfg.For<EgB2Entity, EgB2Dto>()
               .Member(d => d.Score).MapWhen<EgB2Entity>(s => s.Score > 0);
        }
    }

    // ==== 护栏二模型：无守卫直通（与 EgA 同谓词语义对照的有/无声明组） ====
    public class EgNEntity { public int Id { get; set; } public string Name { get; set; } = ""; }
    public class EgNDto { public int Id { get; set; } public string Name { get; set; } = ""; }

    [MapperConfig]
    internal class EgNGuardlessMapper
    {
        public static void Configure(FluentMappingConfig cfg)
        {
            cfg.For<EgNEntity, EgNDto>(); // 无 MapWhen——守卫路径直通
        }
    }

    // T1 输入集：条件真（>2）/条件假（≤2）可区分
    private static readonly string[] T1Inputs = { "abc", "ab", "x", "abcd" };

    /// <summary>
    /// 护栏一核心：A/B 行为等价（T1 字段值形态，新建态）——同输入集双形态输出逐项一致；
    /// 先验锚：条件假输出 == 初始值 ""（绝对断言，防 A/B 同错假绿）。
    /// </summary>
    [Fact]
    public void Guard1_AB_T1_New_EquivalencePerInput()
    {
        foreach (var input in T1Inputs)
        {
            var a = Mapper.Map<EgAEntity, EgADto>(new EgAEntity { Id = 1, Name = input });
            var b = Mapper.Map<EgBEntity, EgBDto>(new EgBEntity { Id = 1, Name = input });
            Assert.NotNull(a);
            Assert.NotNull(b);
            Assert.Equal(a!.Name, b!.Name); // A/B 行为等价核心断言（B 转录退化/包裹丢失在此翻红）
        }

        // 先验锚：条件假 → 守卫生效保持初始值（非透传源值）
        Assert.Equal("", Mapper.Map<EgAEntity, EgADto>(new EgAEntity { Id = 2, Name = "ab" })!.Name);
        Assert.Equal("", Mapper.Map<EgBEntity, EgBDto>(new EgBEntity { Id = 2, Name = "ab" })!.Name);
        // 条件真 → 覆盖
        Assert.Equal("abc", Mapper.Map<EgAEntity, EgADto>(new EgAEntity { Id = 3, Name = "abc" })!.Name);
        Assert.Equal("abc", Mapper.Map<EgBEntity, EgBDto>(new EgBEntity { Id = 3, Name = "abc" })!.Name);
    }

    /// <summary>护栏一扩展：A/B 行为等价（T1 更新态）——条件假保留 dst 既有值、条件真覆盖，双形态一致。</summary>
    [Fact]
    public void Guard1_AB_T1_Update_Equivalence()
    {
        var aTarget = new EgADto { Name = "keep" };
        var bTarget = new EgBDto { Name = "keep" };

        foreach (var input in T1Inputs)
        {
            Mapper.Map(new EgAEntity { Id = 1, Name = input }, aTarget);
            Mapper.Map(new EgBEntity { Id = 1, Name = input }, bTarget);
            Assert.Equal(aTarget.Name, bTarget.Name); // 更新态等价
        }

        // 先验锚：序列末态 == "abcd"（条件真）；中途条件假保留 "keep" 已由等价断言携带
        Assert.Equal("abcd", aTarget.Name);

        // 条件假保留既有值锚点（fresh target）
        var aFresh = new EgADto { Name = "existing" };
        Mapper.Map(new EgAEntity { Id = 2, Name = "ab" }, aFresh);
        Assert.Equal("existing", aFresh.Name);
    }

    /// <summary>护栏一扩展：A/B 行为等价（T2 整源形态，新建+更新态）——不同包裹路径同语义等价。</summary>
    [Fact]
    public void Guard1_AB_T2_Equivalence()
    {
        var scores = new[] { 5, -5, 0, 7 }; // 正真/负假/零假/正真

        foreach (var score in scores)
        {
            var a = Mapper.Map<EgA2Entity, EgA2Dto>(new EgA2Entity { Id = 1, Score = score });
            var b = Mapper.Map<EgB2Entity, EgB2Dto>(new EgB2Entity { Id = 1, Score = score });
            Assert.NotNull(a);
            Assert.NotNull(b);
            Assert.Equal(a!.Score, b!.Score);
        }

        // 先验锚：条件假（-5/0）保持默认 0；条件真覆盖
        Assert.Equal(0, Mapper.Map<EgA2Entity, EgA2Dto>(new EgA2Entity { Id = 2, Score = -5 })!.Score);
        Assert.Equal(0, Mapper.Map<EgB2Entity, EgB2Dto>(new EgB2Entity { Id = 2, Score = -5 })!.Score);
        Assert.Equal(7, Mapper.Map<EgA2Entity, EgA2Dto>(new EgA2Entity { Id = 3, Score = 7 })!.Score);
        Assert.Equal(7, Mapper.Map<EgB2Entity, EgB2Dto>(new EgB2Entity { Id = 3, Score = 7 })!.Score);

        // 更新态：条件假保留既有值，双形态一致
        var aTarget = new EgA2Dto { Score = 99 };
        var bTarget = new EgB2Dto { Score = 99 };
        Mapper.Map(new EgA2Entity { Id = 4, Score = -1 }, aTarget);
        Mapper.Map(new EgB2Entity { Id = 4, Score = -1 }, bTarget);
        Assert.Equal(aTarget.Score, bTarget.Score);
        Assert.Equal(99, aTarget.Score); // 锚点：保留
    }

    /// <summary>
    /// 护栏二：守卫有无对照（三向）——无守卫直通输出 == 源值；有守卫（A/B 两形态）条件假
    /// 输出 == 初始值；条件真三向全等。守卫包裹丢失（有守卫输出 == 无守卫输出）在此翻红。
    /// </summary>
    [Fact]
    public void Guard2_GuardedVsUnguarded_ThreeWayContact()
    {
        foreach (var input in T1Inputs)
        {
            var n = Mapper.Map<EgNEntity, EgNDto>(new EgNEntity { Id = 1, Name = input });
            var a = Mapper.Map<EgAEntity, EgADto>(new EgAEntity { Id = 1, Name = input });
            var b = Mapper.Map<EgBEntity, EgBDto>(new EgBEntity { Id = 1, Name = input });
            Assert.NotNull(n);
            Assert.NotNull(a);
            Assert.NotNull(b);

            // 无守卫：无条件赋值（直通）
            Assert.Equal(input, n!.Name);
            // 有守卫与无守卫的判定一致面：条件真时三向全等
            if (input.Length > 2)
            {
                Assert.Equal(n.Name, a!.Name);
                Assert.Equal(n.Name, b!.Name);
            }
            else
            {
                // 条件假：有守卫保持初始值（≠ 无守卫透传）——守卫语义可区分
                Assert.Equal("", a!.Name);
                Assert.Equal("", b!.Name);
                Assert.NotEqual(n.Name, a.Name);
            }
        }
    }

    /// <summary>护栏二扩展（更新态）：无守卫直通覆盖既有值；有守卫条件假保留既有值——双向可区分。</summary>
    [Fact]
    public void Guard2_GuardedVsUnguarded_Update_Contact()
    {
        var nTarget = new EgNDto { Name = "keep" };
        var aTarget = new EgADto { Name = "keep" };

        Mapper.Map(new EgNEntity { Id = 1, Name = "ab" }, nTarget);
        Mapper.Map(new EgAEntity { Id = 1, Name = "ab" }, aTarget);
        Assert.Equal("ab", nTarget.Name);   // 无守卫：直通覆盖
        Assert.Equal("keep", aTarget.Name); // 有守卫：条件假保留
        Assert.NotEqual(nTarget.Name, aTarget.Name);

        Mapper.Map(new EgNEntity { Id = 2, Name = "abcd" }, nTarget);
        Mapper.Map(new EgAEntity { Id = 2, Name = "abcd" }, aTarget);
        Assert.Equal("abcd", nTarget.Name); // 条件真：三向一致
        Assert.Equal("abcd", aTarget.Name);
    }
}
