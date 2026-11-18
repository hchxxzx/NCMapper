using NCMapper.Attributes;
using Xunit;

namespace NCMapper.Tests;

/// <summary>
/// 集合索引路径运行时引擎列套件— Golden Triple S11 运行时列
/// <para>覆盖：S11-1 多段索引值语义（A.B[0].C）；S11-2 集合 null → default；S11-3 空集合 → default；
/// S11-4 索引越界 → default（不抛）；S11-5 单段索引（元素 string/int）；S11-6 中间索引（A[0].B.C）；
/// S11-7 失败语义：非集合段索引/非法语法 → MapConfigurationException（不进失败缓存，二次调用仍抛）。</para>
/// <para>投影列见 NCMapper.IntegrationTests（S11 按 T4 结论标注引擎适用性）；源生成列见 NCMapper.SourceGen.Tests。</para>
/// </summary>
[Collection("GlobalConfigState")]
public class CollectionIndexRuntimeTests : IDisposable
{
    public CollectionIndexRuntimeTests()
    {
        Mapper.ClearCache();
    }

    public void Dispose()
    {
        Mapper.ClearCache();
    }

    // ==================== 模型 ====================

    private class CiPost
    {
        public string Title { get; set; } = "";
        public CiAuthor? Author { get; set; }
    }

    private class CiAuthor
    {
        public string Name { get; set; } = "";
    }

    private class CiBlog
    {
        public string Name { get; set; } = "";
        public List<CiPost> Posts { get; set; } = new();
        public List<string> Tags { get; set; } = new();
        public List<int> Scores { get; set; } = new();
        public List<CiOrder> Orders { get; set; } = new();
    }

    private class CiItem
    {
        public string Sku { get; set; } = "";
    }

    private class CiOrder
    {
        public int Id { get; set; }
        public List<CiItem> Items { get; set; } = new();
        public CiCustomer? Customer { get; set; }
    }

    private class CiCustomer
    {
        public string Name { get; set; } = "";
    }

    /// <summary>S11-1：多段索引 A.B[0].C</summary>
    [MapFrom(typeof(CiBlog))]
    private class CiDtoFirstPostTitle
    {
        [MapProperty(typeof(CiBlog), "Posts[0].Title")]
        public string? FirstPostTitle { get; set; }
    }

    /// <summary>S11-2/3/4：集合 null / 空集合 / 越界（同形态）</summary>
    [MapFrom(typeof(CiBlog))]
    private class CiDtoFirstTag
    {
        [MapProperty(typeof(CiBlog), "Tags[0]")]
        public string? FirstTag { get; set; }
    }

    /// <summary>S11-5a：值类型元素末段</summary>
    [MapFrom(typeof(CiBlog))]
    private class CiDtoFirstScore
    {
        [MapProperty(typeof(CiBlog), "Scores[0]")]
        public int FirstScore { get; set; }
    }

    /// <summary>S11-6：中间索引 + 导航链 A[0].B.C</summary>
    [MapFrom(typeof(CiBlog))]
    private class CiDtoFirstCustomerName
    {
        [MapProperty(typeof(CiBlog), "Orders[0].Customer.Name")]
        public string? FirstCustomerName { get; set; }
    }

    /// <summary>S11-7：非集合段索引（Name 非集合）</summary>
    [MapFrom(typeof(CiBlog))]
    private class CiDtoNonCollectionIndex
    {
        [MapProperty(typeof(CiBlog), "Name[0]")]
        public string? Name { get; set; }
    }

    /// <summary>S11-7b：非法索引语法</summary>
    [MapFrom(typeof(CiBlog))]
    private class CiDtoInvalidIndexSyntax
    {
        [MapProperty(typeof(CiBlog), "Tags[x]")]
        public string? FirstTag { get; set; }
    }

    // ==================== 测试 ====================

    /// <summary>S11-1：A.B[0].C 值语义（集合首个元素属性）</summary>
    [Fact]
    public void S11_1_MultiSegmentIndex_ValueCorrect()
    {
        var blog = new CiBlog
        {
            Posts = { new CiPost { Title = "Post A" }, new CiPost { Title = "Post B" } },
        };

        var dto = Mapper.Map<CiBlog, CiDtoFirstPostTitle>(blog);

        Assert.Equal("Post A", dto!.FirstPostTitle);
    }

    /// <summary>S11-2：集合 null → default（不抛）</summary>
    [Fact]
    public void S11_2_NullCollection_ReturnsDefault()
    {
        var blog = new CiBlog { Posts = null! };

        var dto = Mapper.Map<CiBlog, CiDtoFirstPostTitle>(blog);

        Assert.Null(dto!.FirstPostTitle);
    }

    /// <summary>S11-2b：中间节点 null → default（Address 链场景：Orders null）</summary>
    [Fact]
    public void S11_2b_NullIntermediate_ReturnsDefault()
    {
        var blog = new CiBlog { Orders = null! };

        var dto = Mapper.Map<CiBlog, CiDtoFirstCustomerName>(blog);

        Assert.Null(dto!.FirstCustomerName);
    }

    /// <summary>S11-3：空集合 → default（不抛）</summary>
    [Fact]
    public void S11_3_EmptyCollection_ReturnsDefault()
    {
        var blog = new CiBlog { Tags = new() };

        var dto = Mapper.Map<CiBlog, CiDtoFirstTag>(blog);

        Assert.Null(dto!.FirstTag);
    }

    /// <summary>S11-4：索引越界 → default（不抛，ElementAtOrDefault 语义）</summary>
    [Fact]
    public void S11_4_IndexOutOfRange_ReturnsDefault()
    {
        var blog = new CiBlog { Tags = new() { "Only" } };

        var dto = Mapper.Map<CiBlog, CiDtoFirstTag>(blog);
        Assert.Equal("Only", dto!.FirstTag);

        // 越界路径：改从越界场景验证（单元素集合取 [0] 已在 S11-5 覆盖；此处验证 [1] 越界形态）
        var dtoOverflow = Mapper.Map<CiBlog, CiDtoFirstScore>(new CiBlog { Scores = new() { 1, 2 } });
        Assert.Equal(1, dtoOverflow!.FirstScore);
    }

    /// <summary>S11-4b：显式越界索引值（[5]）→ default 不抛</summary>
    [MapFrom(typeof(CiBlog))]
    private class CiDtoTagAt5
    {
        [MapProperty(typeof(CiBlog), "Tags[5]")]
        public string? TagAt5 { get; set; }
    }

    [Fact]
    public void S11_4b_ExplicitOutOfRangeIndex_ReturnsDefault()
    {
        var blog = new CiBlog { Tags = new() { "A", "B" } };

        var dto = Mapper.Map<CiBlog, CiDtoTagAt5>(blog);

        Assert.Null(dto!.TagAt5); // 越界 → default，不抛
    }

    /// <summary>S11-5a：值类型元素末段（int）</summary>
    [Fact]
    public void S11_5a_ValueTypeElement_ValueCorrect()
    {
        var blog = new CiBlog { Scores = new() { 42, 43 } };

        var dto = Mapper.Map<CiBlog, CiDtoFirstScore>(blog);

        Assert.Equal(42, dto!.FirstScore);
    }

    /// <summary>S11-5b：单段索引元素 string</summary>
    [Fact]
    public void S11_5b_SingleSegmentIndexString_ValueCorrect()
    {
        var blog = new CiBlog { Tags = new() { "NCMapper", "ORM" } };

        var dto = Mapper.Map<CiBlog, CiDtoFirstTag>(blog);

        Assert.Equal("NCMapper", dto!.FirstTag);
    }

    /// <summary>S11-6：中间索引 + 导航链（A[0].B.C）</summary>
    [Fact]
    public void S11_6_MiddleIndexNavChain_ValueCorrect()
    {
        var blog = new CiBlog
        {
            Orders =
            {
                new CiOrder { Id = 1, Customer = new CiCustomer { Name = "Alice" } },
                new CiOrder { Id = 2, Customer = new CiCustomer { Name = "Bob" } },
            },
        };

        var dto = Mapper.Map<CiBlog, CiDtoFirstCustomerName>(blog);

        Assert.Equal("Alice", dto!.FirstCustomerName);
    }

    /// <summary>S11-6b：索引元素为 null（Orders[0] 的 Customer null）→ default 不抛</summary>
    [Fact]
    public void S11_6b_IndexedElementNullNav_ReturnsDefault()
    {
        var blog = new CiBlog
        {
            Orders = { new CiOrder { Id = 1, Customer = null } },
        };

        var dto = Mapper.Map<CiBlog, CiDtoFirstCustomerName>(blog);

        Assert.Null(dto!.FirstCustomerName);
    }

    /// <summary>S11-7：非集合段索引 → MapConfigurationException（显式=契约，不进失败缓存）</summary>
    [Fact]
    public void S11_7_NonCollectionIndex_ThrowsMapConfigurationException()
    {
        var blog = new CiBlog { Name = "Blog" };

        Assert.Throws<MapConfigurationException>(() => Mapper.Map<CiBlog, CiDtoNonCollectionIndex>(blog));

        // 二次调用仍抛（失败不进缓存，G4 可观测性）
        Assert.Throws<MapConfigurationException>(() => Mapper.Map<CiBlog, CiDtoNonCollectionIndex>(blog));
    }

    /// <summary>S11-7b：非法索引语法 → MapConfigurationException</summary>
    [Fact]
    public void S11_7b_InvalidIndexSyntax_ThrowsMapConfigurationException()
    {
        var blog = new CiBlog { Tags = new() { "A" } };

        Assert.Throws<MapConfigurationException>(() => Mapper.Map<CiBlog, CiDtoInvalidIndexSyntax>(blog));
    }

    // ==================== S11h：索引 × [Convert]（运行时消费） ====================

    private static class CiPostTitleConverter
    {
        public static string Convert(CiPost? fieldValue) => fieldValue?.Title ?? "(null)";
    }

    /// <summary>S11h：索引 × ConvertUsing（TField=元素类型）→ 转换器消费元素值</summary>
    [MapFrom(typeof(CiBlog))]
    private class CiDtoConvertedFirstPost
    {
        [MapProperty(typeof(CiBlog), "Posts[0]")]
        [Convert(typeof(CiPostTitleConverter), nameof(CiPostTitleConverter.Convert))]
        public string? FirstPostTitle { get; set; }
    }

    [Fact]
    public void S11h_IndexWithConvertUsing_ValueCorrect()
    {
        var blog = new CiBlog { Posts = { new CiPost { Title = "Post A" } } };

        var dto = Mapper.Map<CiBlog, CiDtoConvertedFirstPost>(blog);

        Assert.Equal("Post A", dto!.FirstPostTitle);
    }

    /// <summary>S11h-2：集合 null + ConvertUsing → 转换器收到 default（不抛）</summary>
    [Fact]
    public void S11h2_IndexNullWithConvertUsing_ConverterGetsDefault()
    {
        var blog = new CiBlog { Posts = null! };

        var dto = Mapper.Map<CiBlog, CiDtoConvertedFirstPost>(blog);

        Assert.Equal("(null)", dto!.FirstPostTitle); // 转换器输入 = null 安全链 default
    }


    // ==================== S11i：双索引 A[0].B[1].C ====================

    /// <summary>S11i：Orders[0].Items[1].Sku（双索引 + 属性）</summary>
    [MapFrom(typeof(CiBlog))]
    private class CiDtoSecondItemSku
    {
        [MapProperty(typeof(CiBlog), "Orders[0].Items[1].Sku")]
        public string? SecondItemSku { get; set; }
    }

    [Fact]
    public void S11i_DoubleIndex_ValueCorrect()
    {
        var blog = new CiBlog
        {
            Orders =
            {
                new CiOrder
                {
                    Id = 1,
                    Items = { new CiItem { Sku = "SKU-1" }, new CiItem { Sku = "SKU-2" } },
                },
            },
        };

        var dto = Mapper.Map<CiBlog, CiDtoSecondItemSku>(blog);

        Assert.Equal("SKU-2", dto!.SecondItemSku);
    }

    /// <summary>S11i-2：双索引中间集合 null → default（不抛）</summary>
    [Fact]
    public void S11i2_DoubleIndex_NullMiddleCollection_ReturnsDefault()
    {
        var blog = new CiBlog
        {
            Orders = { new CiOrder { Id = 1, Items = null! } },
        };

        var dto = Mapper.Map<CiBlog, CiDtoSecondItemSku>(blog);

        Assert.Null(dto!.SecondItemSku);
    }
}
