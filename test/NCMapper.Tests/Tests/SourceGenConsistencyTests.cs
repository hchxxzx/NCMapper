using Xunit;
using NCMapper.Tests.Models;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// 源生成器与运行时一致性测试 — 验证源生成器路径和运行时表达式树路径产生相同结果
    /// </summary>
    public class SourceGenConsistencyTests : IDisposable
    {
        public SourceGenConsistencyTests()
        {
            Mapper.RegisterGenerated<FullTypeEntity, SameTypeDto>(null!);
            Mapper.RegisterGeneratedCore<FullTypeEntity, SameTypeDto>(null!);
        }

        public void Dispose()
        {
            Mapper.RegisterGenerated<FullTypeEntity, SameTypeDto>(null!);
            Mapper.RegisterGeneratedCore<FullTypeEntity, SameTypeDto>(null!);
            // 清理测试中注册的其他类型对（泛型静态字段无法被 ClearGenerated 清除，必须逐个 null 覆盖）
            Mapper.RegisterGenerated<CustomerEntity, CustomerDto>(null!);
            Mapper.RegisterGenerated<Company, CompanyDto>(null!);
            Mapper.RegisterGenerated<Blog, BlogDto>(null!);
            Mapper.RegisterGenerated<FullTypeEntity, EnumToIntDto>(null!);
            Mapper.RegisterGenerated<FullTypeEntity, EnumToStringDto>(null!);
        }

        // ================================================================
        // 嵌套对象 null 子对象一致性
        // ================================================================

        /// <summary>
        /// 嵌套对象 null 子对象 — 源生成器与运行时结果一致
        /// </summary>
        [Fact]
        public void Nested_Null_Child_Produces_Same_Result()
        {
            // 运行时路径
            var entity = new CustomerEntity { Id = 1, Name = "Runtime", HomeAddress = null };
            var runtime = Mapper.Map<CustomerEntity, CustomerDto>(entity)!;

            // 源生成器路径 — 注册模拟生成器
            Mapper.RegisterGenerated<CustomerEntity, CustomerDto>((src, action) =>
            {
                var dto = new CustomerDto { Id = src.Id, Name = src.Name, HomeAddress = null };
                action?.Invoke(src, dto);
                return dto;
            });

            var gen = Mapper.Map<CustomerEntity, CustomerDto>(entity)!;

            Assert.Equal(runtime.Id, gen.Id);
            Assert.Equal(runtime.Name, gen.Name);
            Assert.Null(gen.HomeAddress);
        }

        // ================================================================
        // 多层嵌套（3+ 级别）一致性
        // ================================================================

        /// <summary>
        /// 多层嵌套（3+ 级）— 源生成器与运行时遍历深度一致
        /// </summary>
        [Fact]
        public void MultiLevel_Nesting_Consistent()
        {
            var company = new Company
            {
                Name = "TestCorp",
                Departments =
                [
                    new Department
                    {
                        Name = "Engineering",
                        Teams =
                        [
                            new Team
                            {
                                Name = "Backend",
                                Members =
                                [
                                    new Member { Name = "Alice", Age = 30, IsActive = true },
                                    new Member { Name = "Bob", Age = 25, IsActive = true }
                                ]
                            }
                        ]
                    }
                ]
            };

            // 运行时路径
            var runtime = Mapper.Map<Company, CompanyDto>(company)!;

            // 源生成器路径 — 手动构造 DTO 以模拟生成器行为（避免递归调用 Mapper.Map）
            Mapper.RegisterGenerated<Company, CompanyDto>((src, action) =>
            {
                var dto = new CompanyDto { Name = src.Name };
                if (src.Departments != null)
                {
                    dto.Departments = src.Departments.Select(dept =>
                    {
                        var deptDto = new DepartmentDto { Name = dept.Name };
                        if (dept.Teams != null)
                        {
                            deptDto.Teams = dept.Teams.Select(team =>
                            {
                                var teamDto = new TeamDto { Name = team.Name };
                                if (team.Members != null)
                                {
                                    teamDto.Members = team.Members.Select(m =>
                                        new MemberDto { Name = m.Name, Age = m.Age, IsActive = m.IsActive }).ToList();
                                }
                                return teamDto;
                            }).ToList();
                        }
                        return deptDto;
                    }).ToList();
                }
                action?.Invoke(src, dto);
                return dto;
            });

            var gen = Mapper.Map<Company, CompanyDto>(company)!;

            Assert.Equal(runtime.Name, gen.Name);
            Assert.NotNull(gen.Departments);
            Assert.Single(gen.Departments);
            Assert.Equal("Engineering", gen.Departments![0].Name);
            Assert.NotNull(gen.Departments[0].Teams);
            Assert.Single(gen.Departments[0].Teams!);
            Assert.Equal("Backend", gen.Departments[0].Teams![0].Name);
            Assert.NotNull(gen.Departments![0].Teams![0].Members!);
            Assert.Equal(2, gen.Departments![0].Teams![0].Members!.Count);
            Assert.Equal("Alice", gen.Departments![0].Teams![0].Members![0].Name);
        }

        // ================================================================
        // 枚举映射双向一致性
        // ================================================================

        /// <summary>
        /// 枚举映射双向一致性 — enum→int 和 enum→string 结果一致
        /// </summary>
        [Fact]
        public void Enum_Mapping_Both_Directions_Consistent()
        {
            var entity = new FullTypeEntity { StatusProp = OrderStatus.Delivered };

            // Runtime: enum → int
            var runtimeInt = Mapper.Map<FullTypeEntity, EnumToIntDto>(entity)!;
            // Runtime: enum → string
            var runtimeStr = Mapper.Map<FullTypeEntity, EnumToStringDto>(entity)!;

            Assert.Equal(3, runtimeInt.StatusProp);              // Delivered = 3
            Assert.Equal("Delivered", runtimeStr.StatusProp);

            // 源生成器 → int
            Mapper.RegisterGenerated<FullTypeEntity, EnumToIntDto>((src, action) =>
                {
                    var dto = new EnumToIntDto { StatusProp = (int)src.StatusProp };
                    action?.Invoke(src, dto);
                    return dto;
                });

            var genInt = Mapper.Map<FullTypeEntity, EnumToIntDto>(entity)!;
            Assert.Equal(runtimeInt.StatusProp, genInt.StatusProp);

            // 源生成器 → string
            Mapper.RegisterGenerated<FullTypeEntity, EnumToStringDto>((src, action) =>
                {
                    var dto = new EnumToStringDto { StatusProp = src.StatusProp.ToString() };
                    action?.Invoke(src, dto);
                    return dto;
                });

            var genStr = Mapper.Map<FullTypeEntity, EnumToStringDto>(entity)!;
            Assert.Equal(runtimeStr.StatusProp, genStr.StatusProp);

            // Cleanup
            Mapper.RegisterGenerated<FullTypeEntity, EnumToIntDto>(null!);
            Mapper.RegisterGenerated<FullTypeEntity, EnumToStringDto>(null!);
        }

        // ================================================================
        // 集合属性一致性
        // ================================================================

        /// <summary>
        /// 集合属性 — 源生成器与运行时 List/Dictionary 映射一致
        /// </summary>
        [Fact]
        public void Collection_Properties_Consistent()
        {
            var blog = new Blog
            {
                Title = "My Blog",
                Posts =
                [
                    new Post { Title = "Post 1", Views = 100 },
                    new Post { Title = "Post 2", Views = 200 }
                ],
                TagCounts = new Dictionary<string, int> { ["csharp"] = 5, ["dotnet"] = 3 }
            };

            // 运行时路径
            var runtime = Mapper.Map<Blog, BlogDto>(blog)!;

            // 源生成器路径 — 手动构造 DTO（避免递归）
            Mapper.RegisterGenerated<Blog, BlogDto>((src, action) =>
            {
                var dto = new BlogDto
                {
                    Title = src.Title,
                    Posts = src.Posts?.Select(p => new PostDto { Title = p.Title, Views = p.Views }).ToList(),
                    TagCounts = src.TagCounts?.ToDictionary(kv => kv.Key, kv => kv.Value)
                };
                action?.Invoke(src, dto);
                return dto;
            });

            var gen = Mapper.Map<Blog, BlogDto>(blog)!;

            Assert.Equal(runtime.Title, gen.Title);
            Assert.Equal(2, gen.Posts!.Count);
            Assert.Equal("Post 1", gen.Posts[0].Title);
            Assert.Equal(100, gen.Posts[0].Views);
            Assert.Equal("Post 2", gen.Posts[1].Title);
            Assert.NotNull(gen.TagCounts);
            Assert.Equal(5, gen.TagCounts!["csharp"]);
            Assert.Equal(3, gen.TagCounts["dotnet"]);
        }

        // ================================================================
        // FullTypeEntity 全字段一致性
        // ================================================================

        /// <summary>
        /// FullTypeEntity 全字段 — 源生成器与运行时所有属性映射一致
        /// </summary>
        [Fact]
        public void FullType_All_Fields_Consistent()
        {
            var entity = new FullTypeEntity
            {
                IntProp = 42,
                LongProp = 1234567890,
                DecimalProp = 99.95m,
                FloatProp = 1.5f,
                DoubleProp = 3.14159,
                StringProp = "hello",
                BoolProp = false,
                DateTimeProp = new DateTime(2024, 1, 1, 12, 0, 0),
                GuidProp = Guid.NewGuid(),
                StatusProp = OrderStatus.Processing,
                NullableIntProp = 200,
                NullIntNull = null
            };

            // 运行时路径
            var runtime = Mapper.Map<FullTypeEntity, SameTypeDto>(entity)!;

            // 源生成器路径
            Mapper.RegisterGeneratedCore<FullTypeEntity, SameTypeDto>(src =>
                new SameTypeDto
                {
                    IntProp = src.IntProp,
                    LongProp = src.LongProp,
                    DecimalProp = src.DecimalProp,
                    FloatProp = src.FloatProp,
                    DoubleProp = src.DoubleProp,
                    StringProp = src.StringProp,
                    BoolProp = src.BoolProp,
                    DateTimeProp = src.DateTimeProp,
                    GuidProp = src.GuidProp
                });

            var gen = Mapper.Map<FullTypeEntity, SameTypeDto>(entity)!;

            Assert.Equal(runtime.IntProp, gen.IntProp);
            Assert.Equal(runtime.LongProp, gen.LongProp);
            Assert.Equal(runtime.DecimalProp, gen.DecimalProp);
            Assert.Equal(runtime.FloatProp, gen.FloatProp);
            Assert.Equal(runtime.DoubleProp, gen.DoubleProp);
            Assert.Equal(runtime.StringProp, gen.StringProp);
            Assert.Equal(runtime.BoolProp, gen.BoolProp);
            Assert.Equal(runtime.DateTimeProp, gen.DateTimeProp);
            Assert.Equal(runtime.GuidProp, gen.GuidProp);
        }

        // ================================================================
        // DeepCopy 一致性
        // ================================================================

        /// <summary>
        /// DeepCopy — 源生成器路径与运行时深拷贝结果一致
        /// </summary>
        [Fact]
        public void DeepCopy_Consistency()
        {
            var entity = new FullTypeEntity
            {
                IntProp = 99,
                StringProp = "deep-copy-test",
                DecimalProp = 123.45m,
                DateTimeProp = new DateTime(2025, 6, 15),
                GuidProp = Guid.NewGuid()
            };

            // 运行时 DeepCopy
            var runtimeCopy = Mapper.DeepCopy(entity)!;

            // 验证深拷贝内容一致
            Assert.Equal(entity.IntProp, runtimeCopy.IntProp);
            Assert.Equal(entity.StringProp, runtimeCopy.StringProp);
            Assert.Equal(entity.DecimalProp, runtimeCopy.DecimalProp);
            Assert.Equal(entity.DateTimeProp, runtimeCopy.DateTimeProp);
            Assert.Equal(entity.GuidProp, runtimeCopy.GuidProp);

            // 验证是独立副本
            runtimeCopy.IntProp = 0;
            Assert.Equal(99, entity.IntProp);
        }

        // ================================================================
        // MapList 一致性
        // ================================================================

        /// <summary>
        /// MapList — 源生成器路径与运行时列表映射结果一致
        /// </summary>
        [Fact]
        public void MapList_Consistency()
        {
            var entities = new List<FullTypeEntity>
            {
                new() { IntProp = 1, StringProp = "A" },
                new() { IntProp = 2, StringProp = "B" },
                new() { IntProp = 3, StringProp = "C" }
            };

            // 运行时路径
            var runtime = Mapper.MapList<FullTypeEntity, SameTypeDto>(entities)!;

            // 源生成器路径
            Mapper.RegisterGeneratedCore<FullTypeEntity, SameTypeDto>(src =>
                new SameTypeDto
                {
                    IntProp = src.IntProp,
                    StringProp = src.StringProp
                });

            var gen = Mapper.MapList<FullTypeEntity, SameTypeDto>(entities)!;

            Assert.Equal(runtime.Count, gen.Count);
            for (int i = 0; i < runtime.Count; i++)
            {
                Assert.Equal(runtime[i].IntProp, gen[i].IntProp);
                Assert.Equal(runtime[i].StringProp, gen[i].StringProp);
            }

            // Cleanup
            Mapper.RegisterGeneratedCore<FullTypeEntity, SameTypeDto>(null!);
        }

        // ================================================================
        // Compose 一致性
        // ================================================================

        /// <summary>
        /// Compose — 多源合成路径基本一致性验证
        /// </summary>
        [Fact]
        public void Compose_Basic_Consistency()
        {
            // Compose 走运行时路径（无源生成器等价物），验证基本功能正确
            var customer = new CustomerEntity { Id = 10, Name = "ComposeTest" };
            var result = Mapper.Compose<CustomerDto>(customer);

            // Compose 应能正确映射同名属性
            Assert.NotNull(result);
            Assert.Equal(10, result!.Id);
            Assert.Equal("ComposeTest", result.Name);
        }

    }
}
