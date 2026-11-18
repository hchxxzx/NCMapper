using System.Globalization;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.Internal;
using Xunit;

namespace NCMapper.Tests.Tests;

/// <summary>
/// TreeBuilder 树契约单元测试（树契约落地）
/// <para>覆盖矩阵：默认名命中 / [MapTree] 三属性自定义 / 2 参旧调用兼容 /
/// 缺失 Children 抛改写版异常（报期望名 + 引导 + 扁平提示）/ 名命中类型不符（消息区分失败因）/
/// 名命中只读 / 空列表 / 多根 / 深层嵌套 / 多自引用集合歧义消除（精确名 vs FirstOrDefault）/
/// 缺失 Id/ParentId / 挂载接口类型塞 List 实例（锁定）/ ResolveConfig 解析。</para>
/// </summary>
public class TreeBuilderTests
{
    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _originalUi = CultureInfo.CurrentUICulture;
        private readonly CultureInfo _originalCurrent = CultureInfo.CurrentCulture;

        public CultureScope(string name)
        {
            var culture = new CultureInfo(name);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _originalCurrent;
            CultureInfo.CurrentUICulture = _originalUi;
        }
    }

    // ===== 测试 DTO =====

    /// <summary>默认契约 DTO（无 [MapTree]，Id/ParentId/Children 全默认名）</summary>
    private sealed class DefaultTreeDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int? ParentId { get; set; }
        public List<DefaultTreeDto> Children { get; set; } = new();
    }

    /// <summary>[MapTree] 三属性自定义 DTO（位置参数，get-only 属性命名实参不可用；
    /// 补锚——[MapTree] 消费以所在类有锚为前提，承载侧合法形态）</summary>
    [MapFrom(typeof(DefaultTreeDto))]
    [MapTree("NodeId", "ParentNodeId", "Nodes")]
    private sealed class CustomTreeDto
    {
        public int NodeId { get; set; }
        public string Name { get; set; } = "";
        public int? ParentNodeId { get; set; }
        public List<CustomTreeDto> Nodes { get; set; } = new();
    }

    /// <summary>[MapTree] 2 参旧调用 DTO（源码兼容，Children 走默认名；T5 补锚同上）</summary>
    [MapFrom(typeof(DefaultTreeDto))]
    [MapTree("NodeId", "ParentNodeId")]
    private sealed class LegacyTwoParamTreeDto
    {
        public int NodeId { get; set; }
        public string Name { get; set; } = "";
        public int? ParentNodeId { get; set; }
        public List<LegacyTwoParamTreeDto> Children { get; set; } = new();
    }

    /// <summary>缺失 Children 的 DTO（平铺结构，无自引用集合）</summary>
    private sealed class FlatTreeDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int? ParentId { get; set; }
    }

    /// <summary>Children 名命中但类型不符（string 而非自引用集合）</summary>
    private sealed class WrongTypeTreeDto
    {
        public int Id { get; set; }
        public int? ParentId { get; set; }
        public string Children { get; set; } = "";
    }

    /// <summary>Children 名命中且类型正确但只读</summary>
    private sealed class ReadOnlyTreeDto
    {
        public int Id { get; set; }
        public int? ParentId { get; set; }
        public List<ReadOnlyTreeDto> Children { get; } = new();
    }

    /// <summary>多自引用集合 DTO（SubItems 声明在前 + Children 在后——旧 FirstOrDefault 自动查找非确定，新规则精确命中 Children）</summary>
    private sealed class AmbiguousTreeDto
    {
        public int Id { get; set; }
        public int? ParentId { get; set; }
        public List<AmbiguousTreeDto> SubItems { get; set; } = new();
        public List<AmbiguousTreeDto> Children { get; set; } = new();
    }

    /// <summary>缺失 Id/ParentId 的 DTO</summary>
    private sealed class MissingIdTreeDto
    {
        public string Name { get; set; } = "";
        public List<MissingIdTreeDto> Children { get; set; } = new();
    }

    /// <summary>Children 声明为接口类型（挂载惰性创建塞 List 实例，锁定）</summary>
    private sealed class InterfaceChildrenTreeDto
    {
        public int Id { get; set; }
        public int? ParentId { get; set; }
        public ICollection<InterfaceChildrenTreeDto>? Children { get; set; }
    }

    // ===== 正常构建 =====

    [Fact]
    public void Build_默认名Children_构建正确树()
    {
        var flat = new List<DefaultTreeDto>
        {
            new() { Id = 1, Name = "Root" },
            new() { Id = 2, Name = "Child1", ParentId = 1 },
            new() { Id = 3, Name = "Child2", ParentId = 1 },
            new() { Id = 4, Name = "GrandChild", ParentId = 2 },
        };

        var roots = TreeBuilder.Build(flat, TreeBuilder.ResolveConfig(typeof(DefaultTreeDto)));

        Assert.Single(roots);
        Assert.Equal("Root", roots[0].Name);
        Assert.Equal(2, roots[0].Children.Count);
        Assert.Single(roots[0].Children[0].Children);
        Assert.Equal("GrandChild", roots[0].Children[0].Children[0].Name);
    }

    [Fact]
    public void Build_MapTree三属性自定义_位置参数命中()
    {
        var flat = new List<CustomTreeDto>
        {
            new() { NodeId = 1, Name = "Root" },
            new() { NodeId = 2, Name = "Child", ParentNodeId = 1 },
        };

        var roots = TreeBuilder.Build(flat, TreeBuilder.ResolveConfig(typeof(CustomTreeDto)));

        Assert.Single(roots);
        Assert.Single(roots[0].Nodes);
        Assert.Equal("Child", roots[0].Nodes[0].Name);
        Assert.Empty(roots[0].Nodes[0].Nodes);
    }

    [Fact]
    public void Build_MapTree两参旧调用_Children走默认名()
    {
        var flat = new List<LegacyTwoParamTreeDto>
        {
            new() { NodeId = 1, Name = "Root" },
            new() { NodeId = 2, Name = "Child", ParentNodeId = 1 },
        };

        var roots = TreeBuilder.Build(flat, TreeBuilder.ResolveConfig(typeof(LegacyTwoParamTreeDto)));

        Assert.Single(roots);
        Assert.Single(roots[0].Children);
        Assert.Equal("Child", roots[0].Children[0].Name);
    }

    [Fact]
    public void Build_空列表_原样返回()
    {
        var flat = new List<DefaultTreeDto>();
        var roots = TreeBuilder.Build(flat, TreeBuilder.ResolveConfig(typeof(DefaultTreeDto)));
        Assert.Empty(roots);
    }

    [Fact]
    public void Build_多根_全部返回()
    {
        var flat = new List<DefaultTreeDto>
        {
            new() { Id = 1, Name = "Root1" },
            new() { Id = 2, Name = "Root2" },
            new() { Id = 3, Name = "Child1", ParentId = 1 },
        };

        var roots = TreeBuilder.Build(flat, TreeBuilder.ResolveConfig(typeof(DefaultTreeDto)));

        Assert.Equal(2, roots.Count);
        Assert.Single(roots[0].Children);
        Assert.Empty(roots[1].Children);
    }

    [Fact]
    public void Build_深层嵌套_四层挂载()
    {
        var flat = new List<DefaultTreeDto>
        {
            new() { Id = 1, Name = "L1" },
            new() { Id = 2, Name = "L2", ParentId = 1 },
            new() { Id = 3, Name = "L3", ParentId = 2 },
            new() { Id = 4, Name = "L4", ParentId = 3 },
        };

        var roots = TreeBuilder.Build(flat, TreeBuilder.ResolveConfig(typeof(DefaultTreeDto)));

        Assert.Single(roots);
        var node = roots[0];
        Assert.Equal("L1", node.Name);
        node = node.Children[0];
        Assert.Equal("L2", node.Name);
        node = node.Children[0];
        Assert.Equal("L3", node.Name);
        Assert.Equal("L4", node.Children[0].Name);
    }

    [Fact]
    public void Build_子节点乱序_按Id索引可解析()
    {
        // 子节点先于父节点（平铺顺序无关——先全量索引再挂载）
        var flat = new List<DefaultTreeDto>
        {
            new() { Id = 2, Name = "Child", ParentId = 1 },
            new() { Id = 1, Name = "Root" },
        };

        var roots = TreeBuilder.Build(flat, TreeBuilder.ResolveConfig(typeof(DefaultTreeDto)));

        Assert.Single(roots);
        Assert.Equal("Root", roots[0].Name);
        Assert.Single(roots[0].Children);
    }

    [Fact]
    public void Build_多自引用集合_精确名命中Children()
    {
        // 旧自动查找（FirstOrDefault）按 GetProperties 顺序可能拿 SubItems——非确定（P2-10）；
        // 新契约精确命中 Children，SubItems 保持空集合不被触碰
        var flat = new List<AmbiguousTreeDto>
        {
            new() { Id = 1 },
            new() { Id = 2, ParentId = 1 },
        };

        var roots = TreeBuilder.Build(flat, TreeBuilder.ResolveConfig(typeof(AmbiguousTreeDto)));

        Assert.Single(roots);
        Assert.Single(roots[0].Children);
        Assert.Empty(roots[0].SubItems);
        Assert.Equal(2, roots[0].Children[0].Id);
    }

    [Fact]
    public void Build_挂载接口类型_塞List实例()
    {
        // 锁定：Children 声明为 ICollection<TDto>惰性创建固定塞 List<TDto> 实例
        var flat = new List<InterfaceChildrenTreeDto>
        {
            new() { Id = 1 },
            new() { Id = 2, ParentId = 1 },
        };

        var roots = TreeBuilder.Build(flat, TreeBuilder.ResolveConfig(typeof(InterfaceChildrenTreeDto)));

        Assert.Single(roots);
        Assert.NotNull(roots[0].Children);
        Assert.IsType<List<InterfaceChildrenTreeDto>>(roots[0].Children);
        Assert.Single(roots[0].Children!);
    }

    // ===== 契约失败分支 =====

    [Fact]
    public void Build_缺失Children_抛异常报期望名与引导()
    {
        var flat = new List<FlatTreeDto> { new() { Id = 1 } };

        var ex = Assert.Throws<MapConfigurationException>(
            () => TreeBuilder.Build(flat, TreeBuilder.ResolveConfig(typeof(FlatTreeDto))));

        // 消息报期望名 + [MapTree] 引导 + 扁平场景提示（改写版三要素）
        Assert.Contains("Children", ex.Message);
        Assert.Contains("FlatTreeDto", ex.Message);
    }

    [Fact]
    public void Build_缺失Children_zh消息含引导与扁平提示()
    {
        using var _ = new CultureScope("zh-CN");
        var flat = new List<FlatTreeDto> { new() { Id = 1 } };

        var ex = Assert.Throws<MapConfigurationException>(
            () => TreeBuilder.Build(flat, TreeBuilder.ResolveConfig(typeof(FlatTreeDto))));

        Assert.Contains("Children", ex.Message);
        Assert.Contains("[MapTree(childrenProperty", ex.Message);
        Assert.Contains("ToList", ex.Message);
    }

    [Fact]
    public void Build_名命中类型不符_抛异常报实际类型()
    {
        var flat = new List<WrongTypeTreeDto> { new() { Id = 1 } };

        var ex = Assert.Throws<MapConfigurationException>(
            () => TreeBuilder.Build(flat, TreeBuilder.ResolveConfig(typeof(WrongTypeTreeDto))));

        // 失败因证据：报实际属性类型（System.String），且不误导为"缺失"
        Assert.Contains("Children", ex.Message);
        Assert.Contains("System.String", ex.Message);
    }

    [Fact]
    public void Build_名命中只读_抛异常()
    {
        var flat = new List<ReadOnlyTreeDto> { new() { Id = 1 } };

        var ex = Assert.Throws<MapConfigurationException>(
            () => TreeBuilder.Build(flat, TreeBuilder.ResolveConfig(typeof(ReadOnlyTreeDto))));

        Assert.Contains("Children", ex.Message);
    }

    [Fact]
    public void Build_两种失败因_消息互异()
    {
        // F2 验收锚点：名缺失 / 类型不符两分支消息可区分
        var flatWrongType = new List<WrongTypeTreeDto> { new() { Id = 1 } };
        var flatMissing = new List<FlatTreeDto> { new() { Id = 1 } };

        var exMissing = Assert.Throws<MapConfigurationException>(
            () => TreeBuilder.Build(flatMissing, TreeBuilder.ResolveConfig(typeof(FlatTreeDto))));
        var exWrongType = Assert.Throws<MapConfigurationException>(
            () => TreeBuilder.Build(flatWrongType, TreeBuilder.ResolveConfig(typeof(WrongTypeTreeDto))));

        Assert.NotEqual(exMissing.Message, exWrongType.Message);
        // 类型不符版含实际类型证据；名缺失版含扁平查询提示（两分支各自的区分特征）
        Assert.Contains("System.String", exWrongType.Message);
        Assert.DoesNotContain("System.String", exMissing.Message);
    }

    [Fact]
    public void Build_缺失IdParentId_抛TreeMissingIdParentId()
    {
        var flat = new List<MissingIdTreeDto> { new() { Name = "X" } };

        var ex = Assert.Throws<MapConfigurationException>(
            () => TreeBuilder.Build(flat, TreeBuilder.ResolveConfig(typeof(MissingIdTreeDto))));

        Assert.Contains("Id", ex.Message);
        Assert.Contains("ParentId", ex.Message);
    }

    // ===== ResolveConfig 解析 =====

    [Fact]
    public void ResolveConfig_无标注_返回默认契约()
    {
        var config = TreeBuilder.ResolveConfig(typeof(DefaultTreeDto));
        Assert.Equal(TreeConfig.Default, config);
        Assert.Equal("Id", config.IdProperty);
        Assert.Equal("ParentId", config.ParentIdProperty);
        Assert.Equal("Children", config.ChildrenProperty);
    }

    [Fact]
    public void ResolveConfig_MapTree三属性_全解析()
    {
        var config = TreeBuilder.ResolveConfig(typeof(CustomTreeDto));
        Assert.Equal("NodeId", config.IdProperty);
        Assert.Equal("ParentNodeId", config.ParentIdProperty);
        Assert.Equal("Nodes", config.ChildrenProperty);
    }

    [Fact]
    public void ResolveConfig_MapTree两参_Children取默认()
    {
        var config = TreeBuilder.ResolveConfig(typeof(LegacyTwoParamTreeDto));
        Assert.Equal("NodeId", config.IdProperty);
        Assert.Equal("ParentNodeId", config.ParentIdProperty);
        Assert.Equal("Children", config.ChildrenProperty);
    }
}
