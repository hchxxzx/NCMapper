using Xunit;
using NCMapper.Tests.Models;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// 统一入口测试 — 验证 Mapper.Map 自动选择源生成器路径
    /// 每个测试前清除注册（重新注册为 null），保证隔离
    /// </summary>
    public class UnifiedEntryTests : IDisposable
    {
        /// <summary>
        /// 构造函数 — 清除可能残留的生成器注册，保证每个测试的隔离性
        /// </summary>
        public UnifiedEntryTests()
        {
            // 清除可能残留的注册（泛型静态字段无法 Clear，只能重新注册为 null）
            Mapper.RegisterGeneratedCore<FullTypeEntity, SameTypeDto>(null!);
            Mapper.RegisterGenerated<FullTypeEntity, SameTypeDto>(null!);
            Mapper.RegisterGeneratedList<FullTypeEntity, SameTypeDto>(null!);
        }

        /// <summary>
        /// 清理 — 测试结束后重新注册为 null 模拟清除
        /// </summary>
        public void Dispose()
        {
            Mapper.RegisterGeneratedCore<FullTypeEntity, SameTypeDto>(null!);
            Mapper.RegisterGenerated<FullTypeEntity, SameTypeDto>(null!);
            Mapper.RegisterGeneratedList<FullTypeEntity, SameTypeDto>(null!);
        }

        /// <summary>
        /// Map 无 action 且已注册生成器时走生成器路径
        /// </summary>
        [Fact]
        public void Map_NoAction_Uses_Generated_When_Available()
        {
            Mapper.RegisterGenerated<FullTypeEntity, SameTypeDto>((src, _) =>
                new SameTypeDto { IntProp = src.IntProp * 100, StringProp = "GENERATED:" + src.StringProp });

            var entity = new FullTypeEntity { IntProp = 42, StringProp = "Test" };
            var dto = Mapper.Map<FullTypeEntity, SameTypeDto>(entity)!;

            Assert.Equal(4200, dto.IntProp);
            Assert.Equal("GENERATED:Test", dto.StringProp);
        }

        /// <summary>
        /// Map 带 action 回调时走生成器路径（生成器支持 action 参数）
        /// </summary>
        [Fact]
        public void Map_WithAction_Uses_Generated_When_Available()
        {
            Mapper.RegisterGenerated<FullTypeEntity, SameTypeDto>((src, action) =>
            {
                var dto = new SameTypeDto { IntProp = 999, StringProp = "GENERATED:" + src.StringProp };
                action?.Invoke(src, dto);
                return dto;
            });

            var entity = new FullTypeEntity { IntProp = 42, StringProp = "Test" };

            var dto = Mapper.Map<FullTypeEntity, SameTypeDto>(entity, (s, d) =>
            {
                d.IntProp = s.IntProp + 1;
            })!;

            Assert.Equal(43, dto.IntProp);
            Assert.Equal("GENERATED:Test", dto.StringProp);
        }

        /// <summary>
        /// 无生成器注册时回退到运行时表达式树路径
        /// </summary>
        [Fact]
        public void Map_NoGenerated_Falls_Back_To_Runtime()
        {
            var entity = new FullTypeEntity { IntProp = 55, StringProp = "Runtime" };
            var dto = Mapper.Map<FullTypeEntity, SameTypeDto>(entity)!;

            Assert.Equal(55, dto.IntProp);
            Assert.Equal("Runtime", dto.StringProp);
        }

        /// <summary>
        /// MapList 无 action 且已注册生成器时走生成器路径
        /// </summary>
        [Fact]
        public void MapList_NoAction_Uses_Generated_When_Available()
        {
            Mapper.RegisterGeneratedList<FullTypeEntity, SameTypeDto>(srcList =>
            {
                var result = new List<SameTypeDto>(srcList.Count);
                foreach (var src in srcList)
                {
                    if (src == null) continue;
                    result.Add(new SameTypeDto { IntProp = src.IntProp + 1000, StringProp = "GEN:" + src.StringProp });
                }
                return result;
            });

            var entities = new List<FullTypeEntity>
            {
                new() { IntProp = 1, StringProp = "A" },
                new() { IntProp = 2, StringProp = "B" }
            };

            var dtos = Mapper.MapList<FullTypeEntity, SameTypeDto>(entities)!;

            Assert.Equal(2, dtos.Count);
            Assert.Equal(1001, dtos[0].IntProp);
            Assert.Equal("GEN:A", dtos[0].StringProp);
            Assert.Equal(1002, dtos[1].IntProp);
        }

        /// <summary>
        /// MapList 带 action 回调时回退到运行时路径
        /// </summary>
        [Fact]
        public void MapList_WithAction_Falls_Back_To_Runtime()
        {
            Mapper.RegisterGeneratedList<FullTypeEntity, SameTypeDto>(srcList =>
            {
                return srcList.Select(s => new SameTypeDto { IntProp = 999 }).ToList();
            });

            var entities = new List<FullTypeEntity>
            {
                new() { IntProp = 10, StringProp = "X" }
            };

            var dtos = Mapper.MapList<FullTypeEntity, SameTypeDto>(entities, (s, d) =>
            {
                d.IntProp = s.IntProp + 5;
            })!;

            Assert.Equal(15, dtos[0].IntProp);
        }

        /// <summary>
        /// null 源直接返回 null，不走生成器
        /// </summary>
        [Fact]
        public void Map_Null_Source_Returns_Null()
        {
            Mapper.RegisterGenerated<FullTypeEntity, SameTypeDto>((src, _) => new SameTypeDto { IntProp = 1 });

            var dto = Mapper.Map<FullTypeEntity, SameTypeDto>(null!);
            Assert.Null(dto);
        }

        /// <summary>
        /// HasGenerated 在已注册时返回 true
        /// </summary>
        [Fact]
        public void HasGenerated_Returns_True_When_Registered()
        {
            Mapper.RegisterGenerated<FullTypeEntity, SameTypeDto>((src, _) => new SameTypeDto());
            Assert.True(Mapper.HasGenerated<FullTypeEntity, SameTypeDto>());
        }

        /// <summary>
        /// HasGenerated 在未注册时返回 false
        /// </summary>
        [Fact]
        public void HasGenerated_Returns_False_When_Not_Registered()
        {
            Assert.False(Mapper.HasGenerated<CaseMismatchDto, CaseMismatchDto>());
        }

        /// <summary>
        /// ClearGenerated 清除所有已注册的生成器
        /// </summary>
        [Fact]
        public void ClearGenerated_Removes_All_Registrations()
        {
            Mapper.RegisterGenerated<FullTypeEntity, SameTypeDto>((src, _) => new SameTypeDto());
            Assert.True(Mapper.HasGenerated<FullTypeEntity, SameTypeDto>());

            // 泛型静态字段无法运行时清除，重新注册为 null 模拟清除
            Mapper.RegisterGenerated<FullTypeEntity, SameTypeDto>(null!);
            Assert.False(Mapper.HasGenerated<FullTypeEntity, SameTypeDto>());
        }

        // ================================================================
        // RegisterGeneratedCore 测试
        // ================================================================

        /// <summary>
        /// RegisterGeneratedCore 注册后，Mapper.Map 使用注册的核心委托
        /// </summary>
        [Fact]
        public void RegisterGeneratedCore_Registered_Func_Used_By_Map()
        {
            Mapper.RegisterGeneratedCore<FullTypeEntity, SameTypeDto>(src =>
                new SameTypeDto { IntProp = src.IntProp * 10, StringProp = "CORE:" + src.StringProp });

            var entity = new FullTypeEntity { IntProp = 5, StringProp = "hello" };
            var dto = Mapper.Map<FullTypeEntity, SameTypeDto>(entity)!;

            Assert.Equal(50, dto.IntProp);
            Assert.Equal("CORE:hello", dto.StringProp);
        }

        /// <summary>
        /// RegisterGeneratedCore 传入 null 取消注册
        /// </summary>
        [Fact]
        public void RegisterGeneratedCore_Null_Deregisters()
        {
            Mapper.RegisterGeneratedCore<FullTypeEntity, SameTypeDto>(src =>
                new SameTypeDto { IntProp = 999 });
            Mapper.RegisterGeneratedCore<FullTypeEntity, SameTypeDto>(null!);

            var entity = new FullTypeEntity { IntProp = 42, StringProp = "runtime" };
            var dto = Mapper.Map<FullTypeEntity, SameTypeDto>(entity)!;

            // 回退到运行时路径
            Assert.Equal(42, dto.IntProp);
            Assert.Equal("runtime", dto.StringProp);
        }

        /// <summary>
        /// HasGenerated 在 RegisterGeneratedCore 注册后返回 true
        /// </summary>
        [Fact]
        public void HasGenerated_Returns_True_After_RegisterGeneratedCore()
        {
            Mapper.RegisterGeneratedCore<FullTypeEntity, SameTypeDto>(src =>
                new SameTypeDto { IntProp = 1 });

            Assert.True(Mapper.HasGenerated<FullTypeEntity, SameTypeDto>());
        }
    }
}
