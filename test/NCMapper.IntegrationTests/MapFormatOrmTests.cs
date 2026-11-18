using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.EFCore;
using NCMapper.FreeSql;
using Xunit;
using FreeSql;

namespace NCMapper.IntegrationTests
{
    /// <summary>
    /// [MapFormat] ORM 三通道矩阵——EFCore/FreeSql AsDto 降级二步
    /// （降级判定读 pm.FormatString → ClassifyUntranslatableDeclaration 分类 "Format"——P4 前提
    /// 双 ORM 共享 DegradationDetectorBase 自动生效）、Dapper 恒走"先查实体再内存映射"
    /// （路径 9 零改动——格式化在内存映射步执行，行为与纯内存一致）。
    /// </summary>
    public class MapFormatOrmTests : IDisposable
    {
        private readonly MFOmcDbContext _db;
        private readonly IFreeSql _fsql;
        private readonly string _dbPath;

        public MapFormatOrmTests()
        {
            var connStr = $"DataSource=file:mfomc{Guid.NewGuid():N}?mode=memory&cache=shared";
            _db = new MFOmcDbContext(connStr);
            _db.Database.EnsureCreated();
            _db.Orders.Add(new MFOOrder
            {
                Id = 1,
                CreatedAt = new DateTime(2026, 7, 20, 10, 0, 0),
                Amount = 99.5m,
            });
            _db.SaveChanges();

            _dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ncmapper_mfomc_{Guid.NewGuid():N}.db");
            _fsql = new FreeSqlBuilder()
                .UseConnectionString(DataType.Sqlite, $"DataSource={_dbPath}")
                .UseAutoSyncStructure(true)
                .Build();
            _fsql.Insert(new MFOOrder
            {
                Id = 1,
                CreatedAt = new DateTime(2026, 7, 20, 10, 0, 0),
                Amount = 99.5m,
            }).ExecuteAffrows();
        }

        public void Dispose()
        {
            _db.Dispose();
            _fsql.Dispose();
            try { System.IO.File.Delete(_dbPath); } catch { /* temp cleanup best-effort */ }
        }

        /// <summary>文化固定（格式化断言随当前文化——测试主机文化漂移防护硬约束）</summary>
        private static IDisposable UseInvariant() => new CultureScope(CultureInfo.InvariantCulture);

        private sealed class CultureScope(CultureInfo original) : IDisposable
        {
            public void Dispose() => CultureInfo.CurrentCulture = original;
        }

        // ================================================================
        // 模型
        // ================================================================

        public class MFOOrder
        {
            public int Id { get; set; }
            public DateTime CreatedAt { get; set; }
            public decimal Amount { get; set; }
        }

        [MapFrom(typeof(MFOOrder))]
        public class MFOOrderDto
        {
            public int Id { get; set; }
            // [ConvertBack] 配套（NCMAP046 编译期反向拦截为强制语义——ORM 测试工程同为真实编译，
            // 全部 [MapFormat] 属性挂合法组合规避；反向行为非本组用例意图）
            [MapFormat("yyyy-MM-dd")]
            [ConvertBack(typeof(MFOParsers), nameof(MFOParsers.ParseDateTime))]
            public string CreatedAt { get; set; } = "";
            [MapFormat("N2")]
            [ConvertBack(typeof(MFOParsers), nameof(MFOParsers.ParseDecimal))]
            public string Amount { get; set; } = "";
        }

        public static class MFOParsers
        {
            public static DateTime ParseDateTime(string value) => DateTime.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
            public static decimal ParseDecimal(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        public class MFOmcDbContext : DbContext
        {
            private readonly string _connStr;
            public MFOmcDbContext(string connStr) => _connStr = connStr;
            public DbSet<MFOOrder> Orders => Set<MFOOrder>();
            protected override void OnConfiguring(DbContextOptionsBuilder options)
                => options.UseSqlite(_connStr);
        }

        // ================================================================
        // EFCore：AsDto 降级二步（带 [MapFormat] 的 DTO 必走降级——RequiresDowngrade=true
        // + pm.FormatString 分类 "Format"），格式化在内存映射步执行，输出与文化口径一致
        // ================================================================

        [Fact]
        public void EFCore_AsDto_WithMapFormat_DegradesAndFormats()
        {
            using (UseInvariant())
            {
                var list = _db.Orders.AsDto().ToList<MFOOrderDto>();
                var dto = Assert.Single(list);
                Assert.Equal("2026-07-20", dto.CreatedAt);
                Assert.Equal("99.50", dto.Amount);
            }
        }

        // ================================================================
        // FreeSql：AsDto 降级二步（与 EFCore 共享 DegradationDetectorBase 判定面——
        // Format 分类由基类属性循环读 pm.FormatString，两 ORM 扩展工程零改动自动生效）
        // ================================================================

        [Fact]
        public void FreeSql_AsDto_WithMapFormat_DegradesAndFormats()
        {
            using (UseInvariant())
            {
                var list = _fsql.Select<MFOOrder>().AsDto().ToList<MFOOrderDto>();
                var dto = Assert.Single(list);
                Assert.Equal("2026-07-20", dto.CreatedAt);
                Assert.Equal("99.50", dto.Amount);
            }
        }

        // ================================================================
        // Dapper 通道：恒走"先查实体再 Mapper.MapList"（路径 9 零改动登记）
        // 格式化在内存映射步执行，与纯内存映射一致（Mapper.MapList 直测等效断言）
        // ================================================================

        [Fact]
        public void InMemory_MapList_WithMapFormat_MatchesDapperSemantics()
        {
            using (UseInvariant())
            {
                var entities = new List<MFOOrder>
                {
                    new() { Id = 1, CreatedAt = new DateTime(2026, 7, 20, 10, 0, 0), Amount = 99.5m },
                };
                var list = Mapper.MapList<MFOOrder, MFOOrderDto>(entities);
                var dto = Assert.Single(list);
                Assert.Equal("2026-07-20", dto.CreatedAt);
                Assert.Equal("99.50", dto.Amount);
            }
        }
    }
}
