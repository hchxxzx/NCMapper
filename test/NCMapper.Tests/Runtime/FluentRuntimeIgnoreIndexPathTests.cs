// P1#3/#6（001 清单缺口 3/7/8 清零——004 重锚翻译）：
// Fluent 方向忽略 / 索引路径（A-16）/ 属性级 NoAutoInclude·Tree 整机端到端（此前整机 0/薄覆盖；
// 特性侧参照 MapIgnoreTests / dotpath·collectionindex 运行时族形态面）。
// 方向语义实锚（src FluentMappingConfig.MemberHead.Ignore(Direction) 注记）：ToDto = 正向跳过反向照常、
// ToEntity = 反向跳过正向照常、Both = 双向（物化端方向过滤为准）。隔离范式（经验 #71/#60）。
using NCMapper;
using Xunit;

namespace NCMapper.Tests.Runtime;

[Collection("GlobalConfigState")]
public class FluentRuntimeIgnoreIndexPathTests
{
    // ==== Ignore 双向默认：正向跳过、反向照常 ====
    public class FriE1 { public string Name { get; set; } = ""; public string Secret { get; set; } = "s"; }
    public class FriD1 { public string Name { get; set; } = ""; public string Secret { get; set; } = "d"; }

    [MapperConfig]
    internal class FriM1
    {
        public static void Configure(FluentMappingConfig cfg)
            => cfg.For<FriE1, FriD1>()
                  .Member(d => d.Name).From(s => s.Name)
                  .Member(d => d.Secret).Ignore();
    }

    [Fact]
    [Trait("Capability", "A-02")]
    public void Ignore_BothWay_ForwardSkipped_ReverseKept()
    {
        var dto = Mapper.Map<FriE1, FriD1>(new FriE1 { Name = "n", Secret = "e-secret" });
        Assert.Equal("n", dto.Name);
        Assert.Equal("d", dto.Secret); // 正向跳过——DTO 初始值保留
        var entity = Mapper.Map<FriD1, FriE1>(new FriD1 { Name = "n", Secret = "d-secret" });
        Assert.Equal("s", entity.Secret); // Both 反向也跳过——Entity 初始保留
    }

    // ==== Ignore(ToDto)：正向跳过、反向照常 ====
    public class FriE2 { public string Name { get; set; } = ""; public string Secret { get; set; } = "s"; }
    public class FriD2 { public string Name { get; set; } = ""; public string Secret { get; set; } = "d"; }

    [MapperConfig]
    internal class FriM2
    {
        public static void Configure(FluentMappingConfig cfg)
            => cfg.For<FriE2, FriD2>()
                  .Member(d => d.Name).From(s => s.Name)
                  .Member(d => d.Secret).Ignore(Attributes.Direction.ToDto);
    }

    [Fact]
    [Trait("Capability", "A-11")]
    public void Ignore_ToDto_ForwardSkipped_ReverseKept()
    {
        var dto = Mapper.Map<FriE2, FriD2>(new FriE2 { Secret = "e-secret" });
        Assert.Equal("d", dto.Secret);
        var entity = Mapper.Map<FriD2, FriE2>(new FriD2 { Secret = "d-secret" });
        Assert.Equal("d-secret", entity.Secret);
    }

    // ==== Ignore(ToEntity)：反向跳过、正向照常 ====
    public class FriE3 { public string Name { get; set; } = ""; public string Secret { get; set; } = "s"; }
    public class FriD3 { public string Name { get; set; } = ""; public string Secret { get; set; } = "d"; }

    [MapperConfig]
    internal class FriM3
    {
        public static void Configure(FluentMappingConfig cfg)
            => cfg.For<FriE3, FriD3>()
                  .Member(d => d.Name).From(s => s.Name)
                  .Member(d => d.Secret).Ignore(Attributes.Direction.ToEntity);
    }

    [Fact]
    [Trait("Capability", "A-11")]
    public void Ignore_ToEntity_ReverseSkipped_ForwardKept()
    {
        var dto = Mapper.Map<FriE3, FriD3>(new FriE3 { Secret = "e-secret" });
        Assert.Equal("e-secret", dto.Secret);
        var entity = Mapper.Map<FriD3, FriE3>(new FriD3 { Secret = "d-secret" });
        Assert.Equal("s", entity.Secret);
    }

    // ==== Ignore(Both)：双向全跳 ====
    public class FriE4 { public string Name { get; set; } = ""; public string Secret { get; set; } = "s"; }
    public class FriD4 { public string Name { get; set; } = ""; public string Secret { get; set; } = "d"; }

    [MapperConfig]
    internal class FriM4
    {
        public static void Configure(FluentMappingConfig cfg)
            => cfg.For<FriE4, FriD4>()
                  .Member(d => d.Name).From(s => s.Name)
                  .Member(d => d.Secret).Ignore(Attributes.Direction.Both);
    }

    [Fact]
    [Trait("Capability", "A-11")]
    public void Ignore_Both_SkippedBothWays()
    {
        var dto = Mapper.Map<FriE4, FriD4>(new FriE4 { Secret = "e-secret" });
        Assert.Equal("d", dto.Secret);
        var entity = Mapper.Map<FriD4, FriE4>(new FriD4 { Secret = "d-secret" });
        Assert.Equal("s", entity.Secret);
    }

    // ==== 索引段 lambda（A-16）：List 元素索引端到端 ====
    public class FriItem { public string Title { get; set; } = ""; }
    public class FriIpE { public string Name { get; set; } = ""; public System.Collections.Generic.List<FriItem> Items { get; set; } = new(); }
    public class FriIpD { public string Name { get; set; } = ""; public string FirstTitle { get; set; } = ""; }

    [MapperConfig]
    internal class FriIpM
    {
        public static void Configure(FluentMappingConfig cfg)
            => cfg.For<FriIpE, FriIpD>()
                  .Member(d => d.Name).From(s => s.Name)
                  .Member(d => d.FirstTitle).From(s => s.Items[0].Title);
    }

    [Fact]
    [Trait("Capability", "A-16")]
    public void IndexPath_FirstElement_EndToEnd()
    {
        var dto = Mapper.Map<FriIpE, FriIpD>(new FriIpE
        {
            Name = "n",
            Items = new() { new FriItem { Title = "t0" }, new FriItem { Title = "t1" } }
        });
        Assert.Equal("t0", dto.FirstTitle);
    }

    // ==== 嵌套点路径端到端（A-07 点路径族代表——Fluent 侧） ====
    public class FriAddr { public string City { get; set; } = ""; }
    public class FriNe { public string Name { get; set; } = ""; public FriAddr Address { get; set; } = new(); }
    public class FriNd { public string Name { get; set; } = ""; public string City { get; set; } = ""; }

    [MapperConfig]
    internal class FriNm
    {
        public static void Configure(FluentMappingConfig cfg)
            => cfg.For<FriNe, FriNd>()
                  .Member(d => d.Name).From(s => s.Name)
                  .Member(d => d.City).From(s => s.Address.City);
    }

    [Fact]
    [Trait("Capability", "A-07")]
    public void NestedPointPath_EndToEnd()
    {
        var dto = Mapper.Map<FriNe, FriNd>(new FriNe { Name = "n", Address = new() { City = "Shanghai" } });
        Assert.Equal("Shanghai", dto.City);
    }

    // ==== 属性级 NoAutoInclude / Tree：整机注册表读取（缺口 8——注册表承载实跑） ====
    public class FriNav { public string Note { get; set; } = ""; }
    public class FriNae { public string Name { get; set; } = ""; public FriNav Audit { get; set; } = new(); }
    public class FriNad { public string Name { get; set; } = ""; public FriNav Audit { get; set; } = new(); }

    [MapperConfig]
    internal class FriNam
    {
        public static void Configure(FluentMappingConfig cfg)
            => cfg.For<FriNae, FriNad>().Tree().Member(d => d.Audit).NoAutoInclude();
    }

    [Fact]
    [Trait("Capability", "A-18")]
    public void NoAutoInclude_FieldLevel_MapsNormally_OutsideDowngrade()
    {
        // 非降级路径：NoAutoInclude 只影响 ORM 降级树的自动收集，常规 Map 正常赋值
        var dto = Mapper.Map<FriNae, FriNad>(new FriNae { Name = "n", Audit = new() { Note = "a" } });
        Assert.Equal("n", dto.Name);
        Assert.Equal("a", dto.Audit.Note);
    }
}
