using NCMapper.Tests.Models;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// 集合属性映射测试 — 实体包含 List&lt;T&gt; 和 Dictionary&lt;K,V&gt; 属性时，
    /// MapperBase 识别集合类型差异并调用 GetListExpression / ConvertListExpression 映射
    /// </summary>
    public class CollectionPropertyTests
    {
        /// <summary>
        /// Blog.Posts (List&lt;Post&gt;) → BlogDto.Posts (List&lt;PostDto&gt;)
        /// MapperBase 检测到 List`1 且元素类型不同（Post vs PostDto），
        /// 调用 GetListExpression 对每个元素递归映射
        /// </summary>
        [Fact]
        public void List_Of_NestedObjects_Mapped()
        {
            var blog = new Blog
            {
                Title = "Tech Blog",
                Posts = new List<Post>
                {
                    new Post { Title = ".NET 10 发布", Views = 15000 },
                    new Post { Title = "C# 13 新特性", Views = 8200 },
                    new Post { Title = "ASP.NET Core 性能优化", Views = 34000 }
                }
            };

            var dto = Mapper.Map<Blog, BlogDto>(blog)!;

            // 顶层字段
            Assert.Equal("Tech Blog", dto.Title);

            // 集合属性
            Assert.NotNull(dto.Posts);
            Assert.Equal(3, dto.Posts!.Count);

            // 集合中的每个元素都正确映射
            Assert.Equal(".NET 10 发布", dto.Posts[0].Title);
            Assert.Equal(15000, dto.Posts[0].Views);

            Assert.Equal("C# 13 新特性", dto.Posts[1].Title);
            Assert.Equal(8200, dto.Posts[1].Views);

            Assert.Equal("ASP.NET Core 性能优化", dto.Posts[2].Title);
            Assert.Equal(34000, dto.Posts[2].Views);
        }

        /// <summary>
        /// Blog.TagCounts (Dictionary&lt;string,int&gt;) → BlogDto.TagCounts (Dictionary&lt;string,int&gt;)
        /// MapperBase 检测到 Dictionary`2 且值类型相同（int），
        /// 调用 ConvertListExpression → CopyDictionary 浅复制
        /// </summary>
        [Fact]
        public void Dictionary_SameValueType_Copied()
        {
            var blog = new Blog
            {
                Title = "Dev Blog",
                TagCounts = new Dictionary<string, int>
                {
                    ["csharp"] = 42,
                    ["dotnet"] = 35,
                    ["aspnet"] = 28
                }
            };

            var dto = Mapper.Map<Blog, BlogDto>(blog)!;

            Assert.NotNull(dto.TagCounts);
            Assert.Equal(3, dto.TagCounts!.Count);
            Assert.Equal(42, dto.TagCounts["csharp"]);
            Assert.Equal(35, dto.TagCounts["dotnet"]);
            Assert.Equal(28, dto.TagCounts["aspnet"]);
        }

        /// <summary>
        /// 集合属性为 null 时，DTO 对应属性也为 null
        /// </summary>
        [Fact]
        public void Null_Collection_Returns_Null()
        {
            var blog = new Blog { Title = "Empty Blog", Posts = null, TagCounts = null };
            var dto = Mapper.Map<Blog, BlogDto>(blog)!;

            Assert.Equal("Empty Blog", dto.Title);
            Assert.Null(dto.Posts);
            Assert.Null(dto.TagCounts);
        }

        /// <summary>
        /// 空集合 → 空集合（有实例但无元素）
        /// </summary>
        [Fact]
        public void Empty_Collection_Returns_Empty()
        {
            var blog = new Blog
            {
                Title = "Starter Blog",
                Posts = new List<Post>(),
                TagCounts = new Dictionary<string, int>()
            };

            var dto = Mapper.Map<Blog, BlogDto>(blog)!;

            Assert.NotNull(dto.Posts);
            Assert.Empty(dto.Posts!);
            Assert.NotNull(dto.TagCounts);
            Assert.Empty(dto.TagCounts!);
        }

        /// <summary>
        /// 字典值类型跨越映射（核心 Bug 修复验证）：
        /// Dictionary&lt;string, Post&gt; → Dictionary&lt;string, PostDto&gt;
        /// 旧代码中 ConvertDictionary 的 "is Tvalue" 检查会丢弃所有数据，
        /// 修复后 MapperBase&lt;Post, PostDto&gt;.Map() 递归映射每个 Post → PostDto
        /// </summary>
        [Fact]
        public void Dictionary_CrossType_Values_Mapped()
        {
            var collection = new ArticleCollection
            {
                Name = "Weekly Digest",
                PostsByCategory = new Dictionary<string, Post>
                {
                    ["tech"] = new Post { Title = "AI Breakthroughs", Views = 5000 },
                    ["life"] = new Post { Title = "Morning Routine", Views = 1200 },
                    ["code"] = new Post { Title = "Clean Architecture", Views = 8900 }
                }
            };

            var dto = Mapper.Map<ArticleCollection, ArticleCollectionDto>(collection)!;

            Assert.Equal("Weekly Digest", dto.Name);
            Assert.NotNull(dto.PostsByCategory);
            Assert.Equal(3, dto.PostsByCategory!.Count);

            // 验证值已正确映射（Post → PostDto）
            Assert.Equal("AI Breakthroughs", dto.PostsByCategory["tech"].Title);
            Assert.Equal(5000, dto.PostsByCategory["tech"].Views);

            Assert.Equal("Morning Routine", dto.PostsByCategory["life"].Title);
            Assert.Equal(1200, dto.PostsByCategory["life"].Views);

            Assert.Equal("Clean Architecture", dto.PostsByCategory["code"].Title);
            Assert.Equal(8900, dto.PostsByCategory["code"].Views);
        }

        /// <summary>
        /// 字典 key 类型变化（string → int）：TypeConvertHelper 处理 key 转换
        /// </summary>
        [Fact]
        public void Dictionary_KeyType_Conversion()
        {
            // 直接调用 CollectionCopier.ConvertDictionary 测试 key 类型转换
            var source = new Dictionary<string, int>
            {
                ["100"] = 42,
                ["200"] = 99
            };

            var result = CollectionCopier.ConvertDictionary<int, int>(source);

            Assert.Equal(2, result.Count);
            Assert.Equal(42, result[100]);
            Assert.Equal(99, result[200]);
        }

        /// <summary>
        /// Dictionary 属性运行时回退测试 — 验证 Mapper.Map 路径能正确处理 Dictionary 属性
        /// <para>即使源生成器未覆盖，运行时 CollectionCopier.CopyDictionary 也能完成映射</para>
        /// </summary>
        [Fact]
        public void Dictionary_Property_Runtime_Fallback_Works()
        {
            // 使用 ArticleCollection 验证跨类型 Dictionary 值映射的运行时路径
            var collection = new ArticleCollection
            {
                Name = "Runtime Fallback",
                PostsByCategory = new Dictionary<string, Post>
                {
                    ["x"] = new Post { Title = "TitleX", Views = 100 },
                    ["y"] = new Post { Title = "TitleY", Views = 200 }
                }
            };

            var dto = Mapper.Map<ArticleCollection, ArticleCollectionDto>(collection)!;

            Assert.Equal("Runtime Fallback", dto.Name);
            Assert.NotNull(dto.PostsByCategory);
            Assert.Equal(2, dto.PostsByCategory!.Count);
            Assert.Equal("TitleX", dto.PostsByCategory["x"].Title);
            Assert.Equal(100, dto.PostsByCategory["x"].Views);
            Assert.Equal("TitleY", dto.PostsByCategory["y"].Title);
            Assert.Equal(200, dto.PostsByCategory["y"].Views);
        }

        /// <summary>
        /// Dictionary 属性运行时回退 — null 字典不抛异常
        /// </summary>
        [Fact]
        public void Dictionary_Property_Runtime_Null_No_Exception()
        {
            var collection = new ArticleCollection
            {
                Name = "Null Dict",
                PostsByCategory = null
            };

            var dto = Mapper.Map<ArticleCollection, ArticleCollectionDto>(collection)!;

            Assert.Equal("Null Dict", dto.Name);
            Assert.Null(dto.PostsByCategory);
        }

        /// <summary>
        /// Dictionary 属性运行时回退 — 空字典生成空字典
        /// </summary>
        [Fact]
        public void Dictionary_Property_Runtime_Empty_Returns_Empty()
        {
            var collection = new ArticleCollection
            {
                Name = "Empty Dict",
                PostsByCategory = new Dictionary<string, Post>()
            };

            var dto = Mapper.Map<ArticleCollection, ArticleCollectionDto>(collection)!;

            Assert.Equal("Empty Dict", dto.Name);
            Assert.NotNull(dto.PostsByCategory);
            Assert.Empty(dto.PostsByCategory!);
        }
    }
}
