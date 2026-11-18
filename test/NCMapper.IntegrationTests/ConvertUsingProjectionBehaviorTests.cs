using Xunit;
using FreeSql;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.FreeSql;

namespace NCMapper.IntegrationTests
{
    // ================================================================
    // 测试用 Converter（静态方法引用形态，T1 模板 M(TField)）：将 Phone 格式化为 "TEL:xxx"
    // ================================================================
    public static class PhonePrefixConverter
    {
        /// <summary>加 "TEL:" 前缀（null → 空串）</summary>
        public static string Convert(string? fieldValue)
            => fieldValue == null ? "" : $"TEL:{fieldValue}";
    }

    // ================================================================
    // 测试用 Entity + DTO（含 ConvertUsing 属性）
    // ================================================================
    public class ConvTestEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Phone { get; set; } = "";
    }

    [MapFrom(typeof(ConvTestEntity))]
    public class ConvTestDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";

        /// <summary>
        /// 使用 ConvertUsing 的属性 — 在 Projection 中会被跳过
        /// </summary>
        [Convert(typeof(PhonePrefixConverter), nameof(PhonePrefixConverter.Convert))]
        public string Phone { get; set; } = "";
    }

    /// <summary>
    /// 验证 [Convert] 属性在 Projection 路径（AsDto）中的实际行为
    /// 核心问题：BuildProjection 静默跳过 ConvertUsing 属性，DTO 中该字段为默认值
    /// </summary>
    public class ConvertUsingProjectionBehaviorTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly string _dbPath;

        public ConvertUsingProjectionBehaviorTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"ncmapper_conv_{Guid.NewGuid():N}.db");
            _fsql = new FreeSqlBuilder()
                .UseConnectionString(DataType.Sqlite, $"DataSource={_dbPath}")
                .UseAutoSyncStructure(true)
                .Build();

            _fsql.Insert(new List<ConvTestEntity>
            {
                new() { Id = 1, Name = "Alice", Phone = "13800000001" },
                new() { Id = 2, Name = "Bob", Phone = "13800000002" }
            }).ExecuteAffrows();
        }

        public void Dispose()
        {
            _fsql.Dispose();
            if (File.Exists(_dbPath))
            {
                try { File.Delete(_dbPath); } catch { }
            }
        }

        /// <summary>
        /// 对照组：内存映射路径 — ConvertUsing 正常工作
        /// </summary>
        [Fact]
        public void InMemory_Map_ConvertUsing_Works()
        {
            var entity = new ConvTestEntity { Id = 1, Name = "Alice", Phone = "13800000001" };
            var dto = Mapper.Map<ConvTestEntity, ConvTestDto>(entity)!;

            Assert.Equal(1, dto.Id);
            Assert.Equal("Alice", dto.Name);
            Assert.Equal("TEL:13800000001", dto.Phone);  // ConvertUsing 生效
        }

        /// <summary>
        /// FreeSql AsDto 路径 — ConvertUsing 属性通过自动降级正常工作
        /// 检测到 [Convert] 后自动降级为：SQL 查实体 → 内存 MapList
        /// </summary>
        [Fact]
        public void FreeSql_AsDto_ConvertUsing_AutoDowngrade_Works()
        {
            var dtos = _fsql.Select<ConvTestEntity>()
                .AsDto()
                .ToList<ConvTestDto>()
                .OrderBy(d => d.Id)
                .ToList();

            Assert.Equal(2, dtos.Count);
            Assert.Equal("Alice", dtos[0].Name);   // 普通属性正常
            Assert.Equal(1, dtos[0].Id);

            // 关键断言：ConvertUsing 属性通过降级路径正确执行
            Assert.Equal("TEL:13800000001", dtos[0].Phone);
            Assert.Equal("TEL:13800000002", dtos[1].Phone);
        }

        /// <summary>
        /// 场景 14：AsDto().First() 降级后 ConvertUsing 字段转换正确
        /// </summary>
        [Fact]
        public void FreeSql_AsDto_First_ConvertUsing_AppliesConverter()
        {
            var dto = _fsql.Select<ConvTestEntity>()
                .Where(e => e.Id == 1)
                .AsDto()
                .First<ConvTestDto>();

            Assert.NotNull(dto);
            Assert.Equal("Alice", dto!.Name);
            Assert.Equal("TEL:13800000001", dto.Phone); // 降级路径 ConvertUsing 生效
        }

        /// <summary>
        /// 场景 15：AsDto().ToPagedList() 降级后 ConvertUsing 字段转换正确（分页在 SQL 侧）
        /// </summary>
        [Fact]
        public void FreeSql_AsDto_ToPagedList_ConvertUsing_AppliesConverter()
        {
            var page = _fsql.Select<ConvTestEntity>()
                .OrderBy(e => e.Id)
                .AsDto()
                .ToPagedList<ConvTestDto>(page: 1, pageSize: 10);

            Assert.Equal(2, page.TotalCount);
            Assert.Equal(2, page.Items.Count);
            Assert.Equal("TEL:13800000001", page.Items[0].Phone); // 降级路径 ConvertUsing 生效
            Assert.Equal("TEL:13800000002", page.Items[1].Phone);
        }

        /// <summary>
        /// 场景 13：AsDto().ToPagedList() 非法页码/页长（&lt; 1）抛 ArgumentOutOfRangeException
        /// </summary>
        [Fact]
        public void FreeSql_AsDto_ToPagedList_InvalidPage_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                _fsql.Select<ConvTestEntity>()
                    .AsDto()
                    .ToPagedList<ConvTestDto>(page: 0, pageSize: 10));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                _fsql.Select<ConvTestEntity>()
                    .AsDto()
                    .ToPagedList<ConvTestDto>(page: 1, pageSize: 0));
        }

        /// <summary>
        /// 对比：FreeSql 先查询再内存映射 — ConvertUsing 正常
        /// </summary>
        [Fact]
        public void FreeSql_InMemory_Map_After_Query_Works()
        {
            var entities = _fsql.Select<ConvTestEntity>().ToList();
            var dtos = entities.Select(e => Mapper.Map<ConvTestEntity, ConvTestDto>(e)!).OrderBy(d => d.Id).ToList();

            Assert.Equal(2, dtos.Count);
            Assert.Equal("TEL:13800000001", dtos[0].Phone);  // ConvertUsing 生效
            Assert.Equal("TEL:13800000002", dtos[1].Phone);
        }
    }
}
