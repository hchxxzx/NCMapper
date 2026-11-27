// ============================================================================
// POC 回归共享基座（TB-20261127-01 §6A —— A~Y 25 组、120 例 POC 现象的永久回归载体）
// ----------------------------------------------------------------------------
// 本文件承载「9 个 FreeSqlComposePocRegression.*.cs 回归文件」共用的两件基础设施：
//   ① PocFixture      —— 独立 SQLite（Guid 命名，与其它 Fixture 隔离）＋ §6A 模型族的种子数据；
//   ② PocModels 命名空间 —— POC 原始模型（Site/Dept/Company/Customer/Order/Store/Region 族），
//      字段与关联形状逐字对齐 POC 实测报告（docs/05-记录与沉淀/20261126-002-POC实测报告/）。
//
// ★ 为何必须自带模型而不能复用 FreeSqlTestFixture / FreeSqlNullFilterFixture：
//   1) POC 的判定依赖**具体业务值**（IT / HR-DELETED / CUST-1 / 区A / 区B / ACME），
//      既有 fixture 的数据形状不同（Topic/Category），换了数据就断言不到 POC 原值；
//   2) Y 组（空壳语义）必须用**嵌套对象形态**的 DTO 才能观察到
//      「Dept != null 但 Dept.Id == 0 / Dept.Name == ""」——既有 NullFilterTests 的 DTO 是
//      扁平字段 `string? CategoryName`，orphan 行实测就是 `<null>`，**观察不到空壳**
//      （captain 2026-11-27 专门提示过的方向陷阱）；
//   3) 每个回归文件按 §6A.1 承载不同组，共用同一份数据才能保证跨组读数可对账
//      （C 组行数、G 组降级行数、I 组约束 B 必须落在同一基线 4 行上）。
//
// 数据基线（POC 原样，见 poc-report.txt 的 C0「原生 join 真值 = 4 行」）：
//   Company : 1 = ACME
//   Dept    : 10 = IT（正常）/ 11 = HR-DELETED（IsDeleted=true）/ 12 = OPS（CompanyId=null）
//   Site    : 1 → 10（正常）/ 2 → null（无部门）/ 3 → 999（孤儿）/ 4 → 11（指向软删部门）
//   故 Site LEFT JOIN Dept ON Site.DeptId = Dept.Id 恒为 4 行——这就是全部行数断言的「真值」。
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using FreeSql;
using NCMapper;
using NCMapper.Attributes;

namespace NCMapper.IntegrationTests.PocModels
{
    // ================================================================
    // 实体（形状逐字对齐 POC：Site→Dept→Company 三级链）
    // ================================================================

    /// <summary>公司实体（POC Company）— 三表/双层嵌套的第二跳</summary>
    public class PocCompany
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>公司名（ACME）</summary>
        public string? Name { get; set; }
    }

    /// <summary>部门实体（POC Dept）— 含软删标记与非主键外键，用于缺陷①③的关联语义验证</summary>
    public class PocDept
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>部门名（IT / HR-DELETED / OPS）</summary>
        public string? Name { get; set; }
        /// <summary>软删标记 — 用户 join 上的 `!b.IsDeleted` 过滤据此生效</summary>
        public bool IsDeleted { get; set; }
        /// <summary>所属公司外键（可为 null）</summary>
        public int? CompanyId { get; set; }

        /// <summary>公司导航（多对一）— 双层嵌套的内层来源</summary>
        [global::FreeSql.DataAnnotations.Navigate(nameof(CompanyId))]
        public PocCompany? Company { get; set; }
    }

    /// <summary>站点实体（POC Site）— Compose 的主源（T1）</summary>
    public class PocSite
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>站点名（S-normal / S-null / S-orphan / S-deleted-dept）</summary>
        public string? Name { get; set; }
        /// <summary>部门外键（可为 null；999 为孤儿）</summary>
        public int? DeptId { get; set; }

        /// <summary>部门导航（多对一）— 缺陷③的「自建第二条 JOIN」正是由它触发</summary>
        [global::FreeSql.DataAnnotations.Navigate(nameof(DeptId))]
        public PocDept? Dept { get; set; }
    }

    /// <summary>客户实体（POC Customer）— B2/H1「T1 无导航」场景的 T2</summary>
    public class PocCustomer
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>客户名（CUST-1）</summary>
        public string? Name { get; set; }
    }

    /// <summary>
    /// 订单实体（POC Order）— ★ 刻意**不声明** [Navigate]：
    /// 这是 B2/H1 的场景本体（T1 没有导航属性，但用户自己 join 了 T2）。
    /// </summary>
    public class PocOrder
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>订单号（ORD-1）</summary>
        public string? OrderNo { get; set; }
        /// <summary>客户外键 — 无 [Navigate]，故内存映射取不到导航</summary>
        public int CustomerId { get; set; }
    }

    /// <summary>区域实体（POC Region）— B3/H2「非主键 join」的 T2</summary>
    public class PocRegion
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>业务码（A / B）— 用户 join 用 Code 而非主键</summary>
        public string? Code { get; set; }
        /// <summary>区域名（区A / 区B）</summary>
        public string? Label { get; set; }
    }

    /// <summary>
    /// 门店实体（POC Store）— 业务码指向「区B」，而外键 RegionId 指向「区A」。
    /// <para>这个错位是 B3/H2 的判定核心：按外键导航取值得到「区A」（错），
    /// 按用户 join 的业务码取值得到「区B」（对）。</para>
    /// </summary>
    public class PocStore
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>门店名（ST1）</summary>
        public string? Name { get; set; }
        /// <summary>业务码（B）— 用户 join 用它</summary>
        public string? RegionCode { get; set; }
        /// <summary>区域外键（1 → 区A）— 与业务码故意不一致</summary>
        public int? RegionId { get; set; }

        /// <summary>区域导航（按外键）— 缺陷③会拿它自建 JOIN，从而取错行</summary>
        [global::FreeSql.DataAnnotations.Navigate(nameof(RegionId))]
        public PocRegion? Region { get; set; }
    }

    // ================================================================
    // DTO —— 特性通道（[MapFrom]），含嵌套引用属性
    // ================================================================

    /// <summary>公司 DTO（POC CompanyDto）</summary>
    [MapFrom(typeof(PocCompany))]
    public class PocCompanyDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>公司名</summary>
        public string? Name { get; set; }
    }

    /// <summary>部门 DTO（POC DeptDto）— 嵌套层；字段为「值类型 Id + 引用类型 Name」，
    /// 这个组合正是 Y 组观察空壳的判据（空壳时 Id=0、Name=""）。</summary>
    [MapFrom(typeof(PocDept))]
    public class PocDeptDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>部门名</summary>
        public string? Name { get; set; }
    }

    /// <summary>含公司嵌套的部门 DTO（POC DeptNestedCompanyDto）— H4 双层嵌套的内层</summary>
    [MapFrom(typeof(PocDept))]
    public class PocDeptNestedCompanyDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>部门名</summary>
        public string? Name { get; set; }
        /// <summary>公司 — 第二层嵌套</summary>
        public PocCompanyDto? Company { get; set; }
    }

    /// <summary>站点 + 嵌套部门 DTO（POC SiteNestedDto）— A/B/C/D/F/G/H/I/J/K/S/T/V/W/X/Y 组的主目标</summary>
    [MapFrom(typeof(PocSite))]
    public class PocSiteNestedDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>站点名</summary>
        public string? Name { get; set; }
        /// <summary>部门 — 嵌套投影的目标属性</summary>
        public PocDeptDto? Dept { get; set; }
    }

    /// <summary>站点 + 双层嵌套 DTO（POC SiteDoubleNestedDto）— A2/A7/D3/H4 用</summary>
    [MapFrom(typeof(PocSite))]
    public class PocSiteDoubleNestedDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>站点名</summary>
        public string? Name { get; set; }
        /// <summary>部门（含公司，共两层嵌套）</summary>
        public PocDeptNestedCompanyDto? Dept { get; set; }
    }

    /// <summary>
    /// 站点 + 单层嵌套 DTO（T 组：缺陷④「单表出嵌套 / Compose 不出」的载体）。
    /// <para>与 <see cref="PocSiteNestedDto"/> 同形状但类型不同，用于隔离注册
    /// （避免与其它组的快照按 (Source,Target) 合并而互相干扰）。</para>
    /// </summary>
    [MapFrom(typeof(PocSite))]
    public class PocCompanyNestedDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>站点名</summary>
        public string? Name { get; set; }
        /// <summary>部门 — 嵌套投影的目标属性</summary>
        public PocDeptDto? Dept { get; set; }
    }

    /// <summary>扁平站点 DTO（POC SiteFlatDto）— A8 无嵌套对照组</summary>
    [MapFrom(typeof(PocSite))]
    public class PocSiteFlatDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>站点名</summary>
        public string? Name { get; set; }
    }

    /// <summary>订单 + 嵌套客户 DTO（POC OrderDto）— B2/H1：T1 无 [Navigate]，值必须来自用户 join 的 T2</summary>
    [MapFrom(typeof(PocOrder))]
    public class PocOrderDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>订单号</summary>
        public string? OrderNo { get; set; }
        /// <summary>客户 — T1 无导航，只能靠 t2 参数取值（修复后应为 CUST-1）</summary>
        public PocCustomerDto? Customer { get; set; }
    }

    /// <summary>客户 DTO（POC CustomerDto）</summary>
    [MapFrom(typeof(PocCustomer))]
    public class PocCustomerDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>客户名</summary>
        public string? Name { get; set; }
    }

    /// <summary>门店 + 嵌套区域 DTO（POC StoreDto）— B3/H2：非主键 join，应取「区B」</summary>
    [MapFrom(typeof(PocStore))]
    public class PocStoreDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>门店名</summary>
        public string? Name { get; set; }
        /// <summary>区域 — 必须来自用户 join 的业务码（区B），不得来自外键导航（区A）</summary>
        public PocRegionDto? Region { get; set; }
    }

    /// <summary>区域 DTO（POC RegionDto）</summary>
    [MapFrom(typeof(PocRegion))]
    public class PocRegionDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>区域名（区A / 区B）</summary>
        public string? Label { get; set; }
    }

    // ================================================================
    // DTO —— Fluent 通道（A3/K/L 组：无 [MapFrom] 锚，靠运行时注册）
    // ================================================================

    /// <summary>Fluent 部门的 DTO（POC FluentDeptDto）</summary>
    public class PocFluentDeptDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>部门名</summary>
        public string? Name { get; set; }
    }

    /// <summary>Fluent 站点 + 嵌套部门 DTO（POC FluentSiteNestedDto）— L/N/S/T 组成因对照</summary>
    public class PocFluentSiteNestedDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>站点名</summary>
        public string? Name { get; set; }
        /// <summary>部门 — Fluent 未显式声明时该属性不进 Properties</summary>
        public PocFluentDeptDto? Dept { get; set; }
    }

    // ---- L/N/S「未声明」/「已声明」两套类型对（隔离用） ----
    //
    // ★ 为何要两套独立类型：RuntimeSnapshotStore.Register 对同一 (Source,Target) 对做
    //   「幂等合并、明细优先」，先注册「已声明 Dept」再注册「只声明标量」时，合并结果仍保留
    //   Dept 条目 → 后一个用例测到的其实是前一形态。用**不同类型对**即可彻底隔离，
    //   无需 Clear() 清空整个注册表（Clear 会破坏其它用例依赖的生成产物条目）。

    /// <summary>Fluent「未声明嵌套」形态的部门 DTO — 不注册为映射对</summary>
    public class PocFluentDeptUndeclaredDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>部门名</summary>
        public string? Name { get; set; }
    }

    /// <summary>Fluent「未声明嵌套」形态的站点 DTO — 主对只声明标量，嵌套对不注册</summary>
    public class PocFluentSiteUndeclaredDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>站点名</summary>
        public string? Name { get; set; }
        /// <summary>部门 — 嵌套对未注册，故该属性不进表达式</summary>
        public PocFluentDeptUndeclaredDto? Dept { get; set; }
    }

    /// <summary>Fluent「已声明嵌套」形态的部门 DTO — 注册为映射对</summary>
    public class PocFluentDeptDeclaredDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>部门名</summary>
        public string? Name { get; set; }
    }

    /// <summary>Fluent「已声明嵌套」形态的站点 DTO — 主对的 Properties/AllTargetSlots 含 Dept</summary>
    public class PocFluentSiteDeclaredDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>站点名</summary>
        public string? Name { get; set; }
        /// <summary>部门 — 嵌套对已注册且主对显式声明，故进表达式（含 IIF）</summary>
        public PocFluentDeptDeclaredDto? Dept { get; set; }
    }

    // ================================================================
    // DTO —— Convert 触发降级（A3/D5/I/K 组：约束 A 与约束 B 的载体）
    // ================================================================

    /// <summary>转换器（POC Conv）— 空串兜底，无副作用</summary>
    public static class PocConv
    {
        /// <summary>null → 空串</summary>
        /// <param name="v">原值</param>
        /// <returns>非 null 字符串</returns>
        public static string Do(string? v) => v ?? "";
    }

    /// <summary>
    /// 站点 + 嵌套 + Convert DTO（POC SiteConvNestedDto）—
    /// <c>Name</c> 上的 <see cref="ConvertAttribute"/> 使降级网判定为「需要降级」，
    /// 于是查询走**内存映射降级路径**（缺陷①的现场，也是约束 B 丢行的现场）。
    /// </summary>
    [MapFrom(typeof(PocSite))]
    public class PocSiteConvNestedDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>站点名 — Convert 触发降级</summary>
        [Convert(typeof(PocConv), nameof(PocConv.Do))]
        public string? Name { get; set; }
        /// <summary>部门 — 降级路径下是否仍取得到，正是缺陷①的判定</summary>
        public PocDeptDto? Dept { get; set; }
    }

    /// <summary>
    /// Fail-closed 场景专用 DTO（H5）：Dept 在 AllTargetSlots 但不在 Properties，
    /// 用于构造「嵌套已声明、但用户没 join 该源类型」的配置错。
    /// </summary>
    [MapFrom(typeof(PocSite))]
    public class PocSiteFailClosedDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>站点名</summary>
        public string? Name { get; set; }
        /// <summary>部门 — 语义上应来自 join 的 PocDept，但查询未 join</summary>
        public PocDeptDto? Dept { get; set; }
    }
}

namespace NCMapper.IntegrationTests
{
    using global::NCMapper.IntegrationTests.PocModels;

    /// <summary>
    /// POC 回归共享 Fixture —— 独立 SQLite（Guid 命名）＋ POC 原样种子数据。
    /// <para>行数真值：Site 4 条，LEFT JOIN Dept 恒 4 行（C0 基线）；其中 1 条命中 IT、
    /// 2 条无匹配（null / 孤儿）、1 条命中软删部门 HR-DELETED。</para>
    /// <para>用 <c>IClassFixture&lt;PocFixture&gt;</c> 获取共享实例（同一测试类内复用）。</para>
    /// </summary>
    public class PocFixture : IDisposable
    {
        /// <summary>FreeSql 实例（SQLite 临时文件）</summary>
        public IFreeSql Fsql { get; }

        private readonly string _dbPath;

        /// <summary>构造 — 建库 + 建表 + 植入 POC 种子数据</summary>
        public PocFixture()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"ncmapper_pocreg_{Guid.NewGuid():N}.db");

            Fsql = new FreeSqlBuilder()
                .UseConnectionString(DataType.Sqlite, $"DataSource={_dbPath}")
                .UseAutoSyncStructure(true)
                .Build();

            Seed();
        }

        /// <summary>释放 FreeSql 并删除临时库文件</summary>
        public void Dispose()
        {
            Fsql.Dispose();
            if (File.Exists(_dbPath))
            {
                try { File.Delete(_dbPath); } catch { }
            }
        }

        /// <summary>
        /// POC 种子数据（与 poc-report.txt 的 Init() 逐字对齐）。
        /// <para>Dept 10 正常 / 11 软删 / 12 无公司；Site 1 正常 / 2 无部门 / 3 孤儿 / 4 指向软删部门。</para>
        /// </summary>
        private void Seed()
        {
            Fsql.Insert(new PocCompany { Id = 1, Name = "ACME" }).ExecuteAffrows();

            Fsql.Insert(new PocDept { Id = 10, Name = "IT", IsDeleted = false, CompanyId = 1 }).ExecuteAffrows();
            Fsql.Insert(new PocDept { Id = 11, Name = "HR-DELETED", IsDeleted = true, CompanyId = 1 }).ExecuteAffrows();
            Fsql.Insert(new PocDept { Id = 12, Name = "OPS", IsDeleted = false, CompanyId = null }).ExecuteAffrows();

            Fsql.Insert(new PocSite { Id = 1, Name = "S-normal", DeptId = 10 }).ExecuteAffrows();
            Fsql.Insert(new PocSite { Id = 2, Name = "S-null", DeptId = null }).ExecuteAffrows();
            Fsql.Insert(new PocSite { Id = 3, Name = "S-orphan", DeptId = 999 }).ExecuteAffrows();
            Fsql.Insert(new PocSite { Id = 4, Name = "S-deleted-dept", DeptId = 11 }).ExecuteAffrows();

            Fsql.Insert(new PocCustomer { Id = 1, Name = "CUST-1" }).ExecuteAffrows();
            Fsql.Insert(new PocOrder { Id = 1, OrderNo = "ORD-1", CustomerId = 1 }).ExecuteAffrows();

            // 业务码 B → 区B；外键 RegionId=1 → 区A（故意错位，B3/H2 的判定核心）
            Fsql.Insert(new PocRegion { Id = 1, Code = "A", Label = "区A" }).ExecuteAffrows();
            Fsql.Insert(new PocRegion { Id = 2, Code = "B", Label = "区B" }).ExecuteAffrows();
            Fsql.Insert(new PocStore { Id = 1, Name = "ST1", RegionCode = "B", RegionId = 1 }).ExecuteAffrows();
        }
    }
}



