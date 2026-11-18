using NCMapper.Attributes;
using Xunit;

namespace NCMapper.Tests;

/// <summary>
/// 集合索引路径 × MapValidator 验证端一致性（审计补充）
/// <para>验证端拆段支持索引段（[n]）：校验 OK ⇔ 映射成功（与 H23 决策同口径）</para>
/// </summary>
[Collection("GlobalConfigState")]
public class CollectionIndexValidatorTests : IDisposable
{
    public CollectionIndexValidatorTests()
    {
        Mapper.ClearCache();
    }

    public void Dispose()
    {
        Mapper.ClearCache();
    }

    // ==================== 模型 ====================

    public class VPost
    {
        public string Title { get; set; } = "";
    }

    public class VBlog
    {
        public List<VPost> Posts { get; set; } = new();
        public List<string> Tags { get; set; } = new();
    }

    /// <summary>索引路径（元素类型匹配目标）</summary>
    [MapFrom(typeof(VBlog))]
    public class VBlogDto
    {
        [MapProperty(typeof(VBlog), "Posts[0].Title")]
        public string? FirstPostTitle { get; set; }

        [MapProperty(typeof(VBlog), "Tags[0]")]
        public string? FirstTag { get; set; }
    }

    /// <summary>索引路径（元素类型不匹配目标）→ 类型不兼容</summary>
    [MapFrom(typeof(VBlog))]
    public class VBlogDtoMismatch
    {
        [MapProperty(typeof(VBlog), "Posts[0]")]
        public string? FirstPost { get; set; }
    }

    /// <summary>非集合段索引 → 未映射（与运行时抛 MapConfigurationException 分叉可接受？不——验证端返回未映射，映射端抛异常）</summary>
    [MapFrom(typeof(VBlog))]
    public class VBlogDtoInvalid
    {
        [MapProperty(typeof(VBlog), "Tags[x]")]
        public string? FirstTag { get; set; }
    }

    // ==================== 测试 ====================

    /// <summary>验证 OK：索引路径可解析 + 元素类型兼容（拆段支持 [n]）</summary>
    [Fact]
    public void Validator_CollectionIndexPath_ResolvesOk()
    {
        var result = MapValidator.ValidateMapping(typeof(VBlog), typeof(VBlogDto));

        Assert.Equal(MapValidationLevel.OK, result.Level);
        Assert.Equal(2, result.MappedCount);
    }

    /// <summary>验证与映射一致：校验 OK ⇔ 运行时映射成功且值正确</summary>
    [Fact]
    public void Validator_CollectionIndexPath_Implies_MappingSucceeds()
    {
        var result = MapValidator.ValidateMapping(typeof(VBlog), typeof(VBlogDto));
        Assert.Equal(MapValidationLevel.OK, result.Level);

        var dto = Mapper.Map<VBlog, VBlogDto>(new VBlog
        {
            Posts = { new VPost { Title = "Post A" } },
            Tags = { "Tag1" },
        });

        Assert.Equal("Post A", dto!.FirstPostTitle);
        Assert.Equal("Tag1", dto.FirstTag);
    }

    /// <summary>元素类型不兼容 → 验证 Error（与映射侧跳过一致）</summary>
    [Fact]
    public void Validator_CollectionIndex_ElementTypeMismatch_ReportsError()
    {
        // 验证消息双语化后本断言锁定 zh 形态，固定 zh-CN 文化
        //（B2 特例登记：断言文本零改动，仅消除新引入的文化依赖；先例 ErrorMessagesLocalizationTests 模式）
        var originalCurrent = System.Globalization.CultureInfo.CurrentCulture;
        var originalUi = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("zh-CN");
            System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("zh-CN");

            var result = MapValidator.ValidateMapping(typeof(VBlog), typeof(VBlogDtoMismatch));

            Assert.Equal(MapValidationLevel.Error, result.Level);
            Assert.Contains("不兼容", string.Join(" ", result.TypeMismatchProperties));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = originalCurrent;
            System.Globalization.CultureInfo.CurrentUICulture = originalUi;
        }
    }

    /// <summary>非法索引语法 → 未映射（验证端拆段失败）</summary>
    [Fact]
    public void Validator_CollectionIndex_InvalidSyntax_ReportsUnmapped()
    {
        var result = MapValidator.ValidateMapping(typeof(VBlog), typeof(VBlogDtoInvalid));

        Assert.Equal(MapValidationLevel.Warn, result.Level);
        Assert.Contains(result.UnmappedProperties, p => p.Contains("FirstTag"));
    }
}
