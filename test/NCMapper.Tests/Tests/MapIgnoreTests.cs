using NCMapper.Tests.Models;
using NCMapper.Attributes;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// [MapIgnore] 忽略字段测试 — 标记了 [MapIgnore] 的属性按方向控制是否参与映射
    /// </summary>
    public class MapIgnoreTests
    {
        /// <summary>
        /// [MapIgnore] 双向忽略 — 属性在映射时被跳过，保持原始值不变
        /// </summary>
        [Fact]
        public void MapIgnore_Both_Field_Not_Overwritten()
        {
            var entity = new FullTypeEntity { IntProp = 99, StringProp = "test" };
            var dto = Mapper.Map<FullTypeEntity, MapIgnoreDto>(entity)!;

            // 未标记 [MapIgnore]，正常映射
            Assert.Equal(99, dto.IntProp);
            Assert.Equal("test", dto.StringProp);

            // [MapIgnore] 双向忽略 — 保持 DTO 中的原始值
            Assert.Equal("ORIGINAL_VALUE", dto.IgnoredField);
        }

        /// <summary>
        /// [MapIgnore(Direction.ToDto)] — Entity→DTO 时忽略，DTO→Entity 时映射
        /// </summary>
        [Fact]
        public void MapIgnore_ToDto_Skips_EntityToDto()
        {
            var entity = new MapIgnoreDirectionEntity
            {
                Id = 1,
                PasswordHash = "secret_hash",
                Name = "User"
            };

            var dto = Mapper.Map<MapIgnoreDirectionEntity, MapIgnoreDirectionDto>(entity)!;

            Assert.Equal(1, dto.Id);
            Assert.Equal("User", dto.Name);
            // ToDto 方向忽略 — PasswordHash 不映射
            Assert.Null(dto.PasswordHash);
        }

        /// <summary>
        /// [MapIgnore(Direction.ToDto)] — DTO→Entity 时仍映射（反向不受影响）
        /// </summary>
        [Fact]
        public void MapIgnore_ToDto_Allows_DtoToEntity()
        {
            var dto = new MapIgnoreDirectionDto
            {
                Id = 2,
                PasswordHash = "new_hash",
                Name = "UpdatedUser"
            };

            var entity = Mapper.Map<MapIgnoreDirectionDto, MapIgnoreDirectionEntity>(dto)!;

            Assert.Equal(2, entity.Id);
            Assert.Equal("UpdatedUser", entity.Name);
            // ToDto 方向忽略不影响 ToEntity — PasswordHash 正常映射
            Assert.Equal("new_hash", entity.PasswordHash);
        }

        /// <summary>
        /// [MapIgnore(Direction.ToEntity)] — DTO→Entity 时忽略，Entity→DTO 时映射
        /// </summary>
        [Fact]
        public void MapIgnore_ToEntity_Skips_DtoToEntity()
        {
            var dto = new MapIgnoreDirectionDto
            {
                Id = 3,
                PasswordHash = "hash",
                Name = "Test",
                CreatedAt = new DateTime(2099, 1, 1)  // 尝试修改 CreatedAt
            };

            var entity = Mapper.Map<MapIgnoreDirectionDto, MapIgnoreDirectionEntity>(dto)!;

            Assert.Equal(3, entity.Id);
            Assert.Equal("Test", entity.Name);
            // ToEntity 方向忽略 — CreatedAt 不映射，保持默认值
            Assert.NotEqual(new DateTime(2099, 1, 1), entity.CreatedAt);
        }

        /// <summary>
        /// [MapIgnore(Direction.ToEntity)] — Entity→DTO 时仍映射（正向不受影响）
        /// </summary>
        [Fact]
        public void MapIgnore_ToEntity_Allows_EntityToDto()
        {
            var entity = new MapIgnoreDirectionEntity
            {
                Id = 4,
                PasswordHash = "hash",
                Name = "Show",
                CreatedAt = new DateTime(2024, 6, 1)
            };

            var dto = Mapper.Map<MapIgnoreDirectionEntity, MapIgnoreDirectionDto>(entity)!;

            Assert.Equal(4, dto.Id);
            Assert.Equal("Show", dto.Name);
            // ToEntity 方向忽略不影响 ToDto — CreatedAt 正常映射
            Assert.Equal(new DateTime(2024, 6, 1), dto.CreatedAt);
        }

                // ================================================================
        // 运行时路径 Direction 测试 — 清除 SG 缓存强制走 MapperBase
        // ================================================================

        /// <summary>
        /// 运行时路径：[MapIgnore(Direction.ToDto)] 正向（Entity→DTO）跳过
        /// <para>清除 GeneratedCache 强制走 MapperBase 运行时表达式树路径</para>
        /// </summary>
        [Fact]
        public void MapIgnore_Runtime_ToDto_Skips_EntityToDto()
        {
            // 清除 SG 缓存，强制走运行时路径
            Mapper.RegisterGeneratedCore<RuntimeDirectionEntity, RuntimeDirectionDto>(null);
            Mapper.RegisterGenerated<RuntimeDirectionEntity, RuntimeDirectionDto>(null);

            var entity = new RuntimeDirectionEntity
            {
                Id = 1,
                PasswordHash = "secret_hash",
                Name = "User",
                CreatedAt = new DateTime(2024, 1, 1)
            };

            var dto = Mapper.Map<RuntimeDirectionEntity, RuntimeDirectionDto>(entity)!;

            Assert.Equal(1, dto.Id);
            Assert.Equal("User", dto.Name);
            // ToDto 方向忽略 — PasswordHash 不映射
            Assert.Null(dto.PasswordHash);
            // ToEntity 方向不忽略 — CreatedAt 正常映射
            Assert.Equal(new DateTime(2024, 1, 1), dto.CreatedAt);
        }

        /// <summary>
        /// 运行时路径：[MapIgnore(Direction.ToDto)] 反向（DTO→Entity）不跳过
        /// </summary>
        [Fact]
        public void MapIgnore_Runtime_ToDto_Allows_DtoToEntity()
        {
            Mapper.RegisterGeneratedCore<RuntimeDirectionDto, RuntimeDirectionEntity>(null);
            Mapper.RegisterGenerated<RuntimeDirectionDto, RuntimeDirectionEntity>(null);

            var dto = new RuntimeDirectionDto
            {
                Id = 2,
                PasswordHash = "new_hash",
                Name = "UpdatedUser",
                CreatedAt = new DateTime(2099, 12, 31)
            };

            var entity = Mapper.Map<RuntimeDirectionDto, RuntimeDirectionEntity>(dto)!;

            Assert.Equal(2, entity.Id);
            Assert.Equal("UpdatedUser", entity.Name);
            // ToDto 方向忽略不影响 ToEntity — PasswordHash 正常映射
            Assert.Equal("new_hash", entity.PasswordHash);
            // CreatedAt 标注 [MapIgnore(Direction.ToEntity)]，DTO→Entity 时跳过
            Assert.NotEqual(new DateTime(2099, 12, 31), entity.CreatedAt);
        }

        /// <summary>
        /// 运行时路径：[MapIgnore(Direction.ToEntity)] 反向（DTO→Entity）跳过
        /// </summary>
        [Fact]
        public void MapIgnore_Runtime_ToEntity_Skips_DtoToEntity()
        {
            Mapper.RegisterGeneratedCore<RuntimeDirectionDto, RuntimeDirectionEntity>(null);
            Mapper.RegisterGenerated<RuntimeDirectionDto, RuntimeDirectionEntity>(null);

            var dto = new RuntimeDirectionDto
            {
                Id = 3,
                PasswordHash = "hash",
                Name = "Test",
                CreatedAt = new DateTime(2099, 1, 1)
            };

            var entity = Mapper.Map<RuntimeDirectionDto, RuntimeDirectionEntity>(dto)!;

            Assert.Equal(3, entity.Id);
            Assert.Equal("Test", entity.Name);
            // ToEntity 方向忽略 — CreatedAt 不映射
            Assert.NotEqual(new DateTime(2099, 1, 1), entity.CreatedAt);
            // ToDto 方向不忽略 — PasswordHash 正常映射
            Assert.Equal("hash", entity.PasswordHash);
        }

        /// <summary>
        /// 运行时路径：[MapIgnore(Direction.ToEntity)] 正向（Entity→DTO）不跳过
        /// </summary>
        [Fact]
        public void MapIgnore_Runtime_ToEntity_Allows_EntityToDto()
        {
            Mapper.RegisterGeneratedCore<RuntimeDirectionEntity, RuntimeDirectionDto>(null);
            Mapper.RegisterGenerated<RuntimeDirectionEntity, RuntimeDirectionDto>(null);

            var entity = new RuntimeDirectionEntity
            {
                Id = 4,
                PasswordHash = "hash",
                Name = "Show",
                CreatedAt = new DateTime(2024, 6, 1)
            };

            var dto = Mapper.Map<RuntimeDirectionEntity, RuntimeDirectionDto>(entity)!;

            Assert.Equal(4, dto.Id);
            Assert.Equal("Show", dto.Name);
            // ToEntity 方向忽略不影响 ToDto — CreatedAt 正常映射
            Assert.Equal(new DateTime(2024, 6, 1), dto.CreatedAt);
            // ToDto 方向忽略 — PasswordHash 不映射
            Assert.Null(dto.PasswordHash);
        }
    }

    /// <summary>
    /// 方向忽略测试 — Entity
    /// </summary>
    public class MapIgnoreDirectionEntity
    {
        /// <summary>
        /// 实体唯一标识符
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// 用户密码哈希值
        /// </summary>
        public string PasswordHash { get; set; } = "";
        /// <summary>
        /// 用户名称
        /// </summary>
        public string Name { get; set; } = "";
        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// 方向忽略测试 — DTO
    /// </summary>
    [MapFrom(typeof(MapIgnoreDirectionEntity))]
    public class MapIgnoreDirectionDto
    {
        /// <summary>
        /// 实体唯一标识符
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Entity→DTO 时忽略（不返回密码哈希给前端）
        /// </summary>
        [MapIgnore(Direction.ToDto)]
        public string? PasswordHash { get; set; }

        /// <summary>
        /// 用户名称
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// DTO→Entity 时忽略（不允许从前端修改创建时间）
        /// </summary>
        [MapIgnore(Direction.ToEntity)]
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// 运行时路径方向忽略测试 — Entity
    /// </summary>
    public class RuntimeDirectionEntity
    {
        /// <summary>
        /// 实体唯一标识符
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// 用户密码哈希值
        /// </summary>
        public string PasswordHash { get; set; } = "";
        /// <summary>
        /// 用户名称
        /// </summary>
        public string Name { get; set; } = "";
        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// 运行时路径方向忽略测试 — DTO
    /// <para>标注 [MapFrom] 以让运行时引擎正确判断映射方向，
    /// 测试中清除 GeneratedCache 强制走 MapperBase 路径</para>
    /// </summary>
    [MapFrom(typeof(RuntimeDirectionEntity))]
    public class RuntimeDirectionDto
    {
        /// <summary>
        /// 实体唯一标识符
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Entity→DTO 时忽略（不返回密码哈希给前端）
        /// </summary>
        [MapIgnore(Direction.ToDto)]
        public string? PasswordHash { get; set; }

        /// <summary>
        /// 用户名称
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// DTO→Entity 时忽略（不允许从前端修改创建时间）
        /// </summary>
        [MapIgnore(Direction.ToEntity)]
        public DateTime CreatedAt { get; set; }
    }

    // ================================================================
    // SourceMapIgnoreTests 类随源侧腿退役移除（生成端源侧 [MapIgnore] 消费退役）
}