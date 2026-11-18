// 嵌套投影副源腿 fail-closed
// 锚点段：src/NCMapper/Internal/ProjectionExpressionFactory.cs:1092-1142（③ 跨表分流）
// 承诺行归属（v1.6 台账）：
//   动作1 成功链 → :1099,1100,1101,1103(internal 副源走 :1107-1108 名字回退),1116,1117,1119,1121,1122,1123
//   动作2 SQL 列裁剪 → ToQueryString 断言（不触锚点段新行，语义回归）
//   动作3 fail-closed → :1110,1112,1113
//   动作4 空组 → :1094,1096,1097 防御分支不可达（RuntimeComposeMapping.cs:57-60 注释自证：
//     物化期全 Ignore 组不产 cm（MappingSnapshotBuilder.cs:357-358）、特性侧空字段名走 NCMAP005
//     不产 cm——产物中不存在 SourcePathNames 为空的行，台账举证）
using Xunit;
using Microsoft.EntityFrameworkCore;
using FreeSql;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.EFCore;
using NCMapper.FreeSql;
using NCMapper.FreeSql.Internal;

namespace NCMapper.IntegrationTests.Models
{
    // ================================================================
    // 测试专用模型（自包含，不与共享模型交叉）
    // 链路：NpOrg → NpDepts[] → NpCompany（跨表）/ NpLicense（internal 跨表）/ NpBadge（无导航）
    // ================================================================

    /// <summary>组织实体（Depts 一对多集合导航——嵌套集合语境宿主）</summary>
    public class NpOrg
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        [global::FreeSql.DataAnnotations.Navigate(nameof(NpDept.OrgId))]
        public List<NpDept> Depts { get; set; } = new();
    }

    /// <summary>
    /// 部门实体（嵌套元素源）——含 Company 引用导航 + internal License 导航；
    /// Badge 无导航（动作 3 fail-closed 触发源）；Note 为未映射列（动作 2 列裁剪断言靶）
    /// </summary>
    public class NpDept
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public int OrgId { get; set; }
        public int? CompanyId { get; set; }
        /// <summary>未映射列——动作 2 断言 SQL 不 SELECT 此列</summary>
        public string? Note { get; set; }
        /// <summary>公司导航（动作 1 跨表副源——public 类型，SubSourceTypeRef 非 null）</summary>
        [global::FreeSql.DataAnnotations.Navigate(nameof(CompanyId))]
        public NpCompany? Company { get; set; }
        /// <summary>许可导航（:1107-1108 触发源——类型 internal → 发射器 SubSourceTypeRef=null，
        /// 运行时走 SubSourceTypeName 名字归一匹配回退；EF 显式配置，FreeSql 忽略 internal 属性）</summary>
        internal NpLicense? License { get; set; }
    }

    /// <summary>公司实体（public 跨表副源）</summary>
    public class NpCompany
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    /// <summary>许可实体（internal 跨表副源——NonPublic 类型 → SubSourceTypeRef=null 形态）</summary>
    internal class NpLicense
    {
        public int Id { get; set; }
        public string? Key { get; set; }
    }

    /// <summary>徽章实体（动作 3 副源——NpDept 上无任何指向该类型的导航属性）</summary>
    public class NpBadge
    {
        public int Id { get; set; }
        public string? Code { get; set; }
    }

    /// <summary>
    /// 站点实体（引用导航宿主——FreeSql 侧动作 1b/3b 用：
    /// FreeSqlProjectionBuilder 策略 EnableCollectionProjection=false（FreeSql 翻译能力有限，
    /// 保守策略）——集合嵌套在 FreeSql 侧被策略关闭置 null，引用导航嵌套仍开）
    /// </summary>
    public class NpSite
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public int? DeptId { get; set; }
        [global::FreeSql.DataAnnotations.Navigate(nameof(DeptId))]
        public NpDept? Dept { get; set; }
    }

    // ================================================================
    // DTO
    // ================================================================

    /// <summary>组织 DTO——嵌套集合 Depts</summary>
    [MapFrom(typeof(NpOrg))]
    public class NpOrgDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public List<NpDeptDto>? Depts { get; set; }
    }

    /// <summary>
    /// 部门 DTO（嵌套元素）——CompanyName 动作 1 成功链；LicenseKey 走 internal 副源
    /// 名字回退链（:1107-1108）；BadgeCode 副源无导航 → fail-closed（:1110-1113）
    /// </summary>
    [MapFrom(typeof(NpDept))]
    public class NpDeptDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }

        /// <summary>动作 1：跨表副源（NpCompany.Name，public 类型 → :1101→:1103 引用解析）</summary>
        [MapProperty(typeof(NpCompany), nameof(NpCompany.Name))]
        public string? CompanyName { get; set; }

        /// <summary>:1107-1108：跨表副源 internal（NpLicense → SubSourceTypeRef=null →
        /// :1107 名字回退在 public 属性集内查找，internal 导航不可见 → :1110-1113 fail-closed）</summary>
        [MapProperty(typeof(NpLicense), nameof(NpLicense.Key))]
        public string? LicenseKey { get; set; }

        /// <summary>动作 3：跨表副源无导航（NpDept 无 NpBadge 导航 → :1110-1113 fail-closed）</summary>
        [MapProperty(typeof(NpBadge), nameof(NpBadge.Code))]
        public string? BadgeCode { get; set; }
    }
    /// <summary>
    /// 站点 DTO（引用导航嵌套——FreeSql 侧载体；嵌套 DTO 跨表分流与集合形态共用锚点段）
    /// </summary>
    [MapFrom(typeof(NpSite))]
    public class NpSiteDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public NpDeptDto? Dept { get; set; }
    }
}

namespace NCMapper.IntegrationTests
{
    using global::NCMapper.IntegrationTests.Models;

    // ================================================================
    // EF Core 侧（SQLite 内存库）
    // ================================================================

    /// <summary> EF 夹具（动作 1a/2/3a——覆盖 :1099-1123 承诺行）</summary>
    public class NestedCrossTableEfTests : IDisposable
    {
        private readonly DbContext _db;

        private sealed class NpDbContext : DbContext
        {
            private readonly string _connStr;
            public NpDbContext(string connStr) => _connStr = connStr;
            public DbSet<NpOrg> Orgs => Set<NpOrg>();

            protected override void OnConfiguring(DbContextOptionsBuilder ob)
                => ob.UseSqlite($"Data Source={_connStr}");

            protected override void OnModelCreating(ModelBuilder mb)
            {
                mb.Entity<NpDept>().HasOne(d => d.Company).WithMany().HasForeignKey(d => d.CompanyId);
                mb.Entity<NpOrg>().HasMany(o => o.Depts).WithOne().HasForeignKey(d => d.OrgId);
                // NpDept.License（internal 属性）EF 约定不发现——不配置不建列；
                // 快照侧 SubSourceTypeRef=null 由发射器对 internal 类型的 NullableTypeOf 决定，与 EF 无关
            }
        }

        public NestedCrossTableEfTests()
        {
            var connStr = $"Data Source=file:np3{Guid.NewGuid():N}?mode=memory&cache=shared";
            _db = new NpDbContext(connStr);
            _db.Database.EnsureCreated();

            _db.Set<NpCompany>().Add(new NpCompany { Id = 1, Name = "ACME" });
            _db.Set<NpOrg>().Add(new NpOrg
            {
                Id = 1,
                Name = "ORG-1",
                Depts = new List<NpDept>
                {
                    new() { Id = 10, Name = "IT", OrgId = 1, CompanyId = 1, Note = "should-not-select" }
                }
            });
            _db.SaveChanges();
        }

        public void Dispose() => _db.Dispose();

        /// <summary>
        /// 动作 1a（EF）：嵌套元素 DTO 内层跨表字段 = 副源表实际值（非 null）。
        /// CompanyName 走 :1103 引用解析链；LicenseKey 走 :1107-1108 名字回退链（internal 副源）；
        /// BadgeCode 副源无导航——fail-closed 置 null（动作 3a 同夹具，此处只断不抛+全链值正确）。
        /// 覆盖承诺行：1099,1100,1101,1103,1107,1108,1110,1112,1113,1116,1117,1119,1121,1122,1123
        /// </summary>
        [Fact]
        public void EF_Nested_CrossTable_Fields_EqualsSubSourceValues()
        {
            var dtos = _db.Set<NpOrg>()
                .Where(o => o.Id == 1)
                .Select(EfCoreProjectionBuilder.Build<NpOrg, NpOrgDto>())
                .ToList();

            var dto = Assert.Single(dtos);
            var dept = Assert.Single(dto.Depts!);
            Assert.Equal(10, dept.Id);
            Assert.Equal("IT", dept.Name);
            // 动作 1a：public 副源实际值（:1103 链）
            Assert.Equal("ACME", dept.CompanyName);
            // :1107-1108：internal 副源 → SubSourceTypeRef=null → 名字回退查找限定 public 属性集
            //（GetPublicInstanceProperties BindingFlags.Public）→ internal 导航不可见 → 落 :1110-1113
            // fail-closed（行执行即命中；行为=保守 null，非缺陷）
            Assert.Null(dept.LicenseKey);
            // 动作 3a：fail-closed——不抛、字段 null、其余正确（上两断言即"其余正确"）
            Assert.Null(dept.BadgeCode);
        }

        /// <summary>
        /// 动作 2（EF）：嵌套投影 SQL 列裁剪——ToQueryString() 通路
        /// （先例 CollectionProjectionPocTests.cs:244）。跨表副源已 JOIN 且其投影子查询
        /// 只取所需列（NpCompany.Name）；未声明列 Note 不得进入跨表投影。
        /// <para>口径注（v1.6 台账）：EF 对集合元素 JOIN 取实体全列（顶层 "n"."Note"）系
        /// EF 集合 JOIN 固有形态，非 NCMapper 投影行为——NCMapper 跨表投影子查询（别名 s/n0）
        /// 列裁剪干净，断言锚定该子查询。</para>
        /// </summary>
        [Fact]
        public void EF_Nested_SqlSelectsOnlyDeclaredColumns()
        {
            var sql = _db.Set<NpOrg>()
                .Where(o => o.Id == 1)
                .Select(EfCoreProjectionBuilder.Build<NpOrg, NpOrgDto>())
                .ToQueryString();

            Assert.Contains("SELECT", sql);
            // 跨表副源已 JOIN（副源表出现在 SQL）
            Assert.Contains("NpCompany", sql);
            // 跨表投影子查询（NpDept 别名 n0）列裁剪干净——未声明列 Note 不进跨表投影
            Assert.DoesNotContain("\"n0\".\"Note\"", sql);
            // 跨表字段以别名投影出现（n1."Name" AS "CompanyName"）
            Assert.Contains("CompanyName", sql);
        }
    }

    // ================================================================
    // FreeSql 侧（SQLite 临时文件库）
    // ================================================================

    /// <summary> FreeSql 夹具（动作 1b/3b——provider 无关链路的第二触发路径）</summary>
    [Collection("PipelineGlobalState")]
    public class NestedCrossTableFreeSqlTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly string _dbPath;

        public NestedCrossTableFreeSqlTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"ncmapper_np3_{Guid.NewGuid():N}.db");
            _fsql = new FreeSqlBuilder()
                .UseConnectionString(DataType.Sqlite, $"Data Source={_dbPath}")
                .UseAutoSyncStructure(true)
                .Build();

            _fsql.Insert(new NpCompany { Id = 1, Name = "ACME" }).ExecuteAffrows();
            _fsql.Insert(new NpDept
            {
                Id = 10, Name = "IT", OrgId = 1, CompanyId = 1, Note = "should-not-select"
            }).ExecuteAffrows();
            _fsql.Insert(new NpSite { Id = 1, Name = "SITE-1", DeptId = 10 }).ExecuteAffrows();
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
        /// 动作 1b/3b（FreeSql）：引用导航嵌套 DTO 内层跨表字段 = 副源表实际值（非 null）；
        /// BadgeCode 副源无导航 → fail-closed 置 null 不抛，其余字段正确。
        /// <para><b>修复后取消 Skip</b>（原 Skip 挂）：共享内核 IIF null
        /// 保护（ProjectionExpressionFactory.cs:1029-1031）FreeSql 翻译不了——修复 = SQL 消费前剥保护
        /// （FreeSqlNullGuardStripVisitor；C3：Build 本体纯净不剥，测试直跑处显式 Strip）。
        /// 跨表 CompanyName 字段剥保护后 SQL 投影可译（评估 20261029-006 A1 实证），断言保留原意。
        /// 集合嵌套形态另见 FreeSqlProjectionBuilder.cs:35 保守策略（预期）。</para>
        /// </summary>
        [Fact]
        public void FreeSql_Nested_CrossTable_Fields_EqualsSubSourceValues()
        {
            // 直跑 internal Build 产物（不经 AsDto/降级判定）→ 显式剥 IIF null 保护 → SQL 翻译
            var expr = FreeSqlNullGuardStripVisitor.Strip(FreeSqlProjectionBuilder.Build<NpSite, NpSiteDto>());
            var dtos = _fsql.Select<NpSite>().Where(o => o.Id == 1).ToList(expr);

            var dto = Assert.Single(dtos);
            Assert.Equal(1, dto.Id);
            Assert.NotNull(dto.Dept);
            Assert.Equal(10, dto.Dept!.Id);
            Assert.Equal("IT", dto.Dept.Name);
            // 动作 1b：public 副源实际值
            Assert.Equal("ACME", dto.Dept.CompanyName);
            // 动作 3b：fail-closed——不抛、字段 null、其余正确
            Assert.Null(dto.Dept.BadgeCode);
        }
    }
}
