using NCMapper.Tests.Models;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// 树形结构（自引用）映射测试 — Category → CategoryDto，
    /// Children 是同类型 List，Mapper 递归映射每个层级
    /// </summary>
    public class TreeStructureTests
    {
        /// <summary>
        /// 构建三层分类树：
        ///   技术(1)
        ///     ├─ 后端(1.1)
        ///     │   ├─ .NET(1.1.1)
        ///     │   └─ Java(1.1.2)
        ///     └─ 前端(1.2)
        ///         └─ React(1.2.1)
        /// 验证树形结构的名称和深度正确。
        /// </summary>
        [Fact]
        public void Three_Level_Tree_Mapped()
        {
            var category = new Category
            {
                Name = "技术",
                Sort = 1,
                Children = new List<Category>
                {
                    new Category
                    {
                        Name = "后端",
                        Sort = 2,
                        Children = new List<Category>
                        {
                            new Category { Name = ".NET", Sort = 3, Children = null },
                            new Category { Name = "Java", Sort = 4, Children = null }
                        }
                    },
                    new Category
                    {
                        Name = "前端",
                        Sort = 5,
                        Children = new List<Category>
                        {
                            new Category { Name = "React", Sort = 6, Children = null }
                        }
                    }
                }
            };

            var dto = Mapper.Map<Category, CategoryDto>(category)!;

            // 根节点
            Assert.Equal("技术", dto.Name);
            Assert.Equal(1, dto.Sort);
            Assert.NotNull(dto.Children);
            Assert.Equal(2, dto.Children!.Count);

            // 二级：后端
            var backend = dto.Children[0];
            Assert.Equal("后端", backend.Name);
            Assert.Equal(2, backend.Sort);
            Assert.Equal(2, backend.Children!.Count);

            // 三级：.NET 和 Java
            Assert.Equal(".NET", backend.Children[0].Name);
            Assert.Equal(3, backend.Children[0].Sort);
            Assert.Null(backend.Children[0].Children); // 叶子节点

            Assert.Equal("Java", backend.Children[1].Name);

            // 二级：前端
            var frontend = dto.Children[1];
            Assert.Equal("前端", frontend.Name);
            Assert.Single(frontend.Children!);

            // 三级：React
            Assert.Equal("React", frontend.Children![0].Name);
        }

        /// <summary>
        /// 叶子节点（Children = null）正确映射
        /// </summary>
        [Fact]
        public void Leaf_Node_NoChildren()
        {
            var leaf = new Category { Name = "叶子", Sort = 99, Children = null };
            var dto = Mapper.Map<Category, CategoryDto>(leaf)!;

            Assert.Equal("叶子", dto.Name);
            Assert.Null(dto.Children);
        }
    }
}
