using NCMapper.Tests.Models;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// 多级嵌套映射测试 — 四级深度 Company → Department → Team → Member
    /// 验证 MapperBase 在每一层递归调用自身，逐层映射子对象
    /// </summary>
    public class MultiLevelNestedTests
    {
        /// <summary>
        /// 构建 Company(1个) → Departments(2个) → 每个部门 Teams(2个) → 每个团队 Members(2个)
        /// 共 4 层嵌套，8 个叶子节点。验证每一层的名称和数量是否正确。
        /// </summary>
        [Fact]
        public void Four_Level_Nesting_All_Fields_Mapped()
        {
            var company = new Company
            {
                Name = "Acme Corp",
                Departments = new List<Department>
                {
                    new Department
                    {
                        Name = "Engineering",
                        Teams = new List<Team>
                        {
                            new Team
                            {
                                Name = "Frontend",
                                Members = new List<Member>
                                {
                                    new Member { Name = "Alice", Age = 28, IsActive = true },
                                    new Member { Name = "Bob", Age = 32, IsActive = true }
                                }
                            },
                            new Team
                            {
                                Name = "Backend",
                                Members = new List<Member>
                                {
                                    new Member { Name = "Charlie", Age = 35, IsActive = true },
                                    new Member { Name = "Diana", Age = 29, IsActive = false }
                                }
                            }
                        }
                    },
                    new Department
                    {
                        Name = "Marketing",
                        Teams = new List<Team>
                        {
                            new Team
                            {
                                Name = "SEO",
                                Members = new List<Member>
                                {
                                    new Member { Name = "Eve", Age = 26, IsActive = true }
                                }
                            }
                        }
                    }
                }
            };

            var dto = Mapper.Map<Company, CompanyDto>(company)!;

            // L1: 公司
            Assert.Equal("Acme Corp", dto.Name);
            Assert.NotNull(dto.Departments);
            Assert.Equal(2, dto.Departments!.Count);

            // L2: Engineering 部门
            var eng = dto.Departments[0];
            Assert.Equal("Engineering", eng.Name);
            Assert.NotNull(eng.Teams);
            Assert.Equal(2, eng.Teams!.Count);

            // L3: Frontend 团队
            var fe = eng.Teams[0];
            Assert.Equal("Frontend", fe.Name);
            Assert.NotNull(fe.Members);
            Assert.Equal(2, fe.Members!.Count);

            // L4: Frontend 成员
            Assert.Equal("Alice", fe.Members[0].Name);
            Assert.Equal(28, fe.Members[0].Age);
            Assert.True(fe.Members[0].IsActive);

            Assert.Equal("Bob", fe.Members[1].Name);
            Assert.Equal(32, fe.Members[1].Age);

            // L3: Backend 团队
            var be = eng.Teams[1];
            Assert.Equal("Backend", be.Name);
            Assert.Equal(2, be.Members!.Count);
            Assert.Equal("Charlie", be.Members[0].Name);
            Assert.Equal(35, be.Members[0].Age);
            Assert.Equal("Diana", be.Members[1].Name);
            Assert.False(be.Members[1].IsActive);

            // L2: Marketing 部门
            var mkt = dto.Departments[1];
            Assert.Equal("Marketing", mkt.Name);
            Assert.Single(mkt.Teams!);

            // L4: SEO 成员
            Assert.Equal("Eve", mkt.Teams![0].Members![0].Name);
        }

        /// <summary>
        /// 任意层级子对象为 null 时，对应 DTO 属性为 null，不抛异常
        /// </summary>
        [Fact]
        public void Null_At_MidLevel_Propagates()
        {
            var company = new Company
            {
                Name = "Solo Corp",
                Departments = new List<Department>
                {
                    new Department { Name = "EmptyDept", Teams = null }  // Teams 为 null
                }
            };

            var dto = Mapper.Map<Company, CompanyDto>(company)!;

            Assert.Equal("Solo Corp", dto.Name);
            Assert.Single(dto.Departments!);
            Assert.Equal("EmptyDept", dto.Departments![0].Name);
            // 中间层级为 null，不会抛异常
            Assert.Null(dto.Departments[0].Teams);
        }
    }
}
