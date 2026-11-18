using Microsoft.EntityFrameworkCore;
using NCMapper.Dapper;
using NCMapper.EFCore;
using NCMapper.FreeSql;
using NCMapper.Samples.Helpers;
using NCMapper.Samples.Models.Dtos;
using NCMapper.Samples.Models.Entities;

namespace NCMapper.Samples.Orm;

/// <summary>
/// 场景 501 — ORM 投影（FreeSql + EFCore 双 ORM）：单表/分页/多表/导航/索引路径/Fx 双态。
/// </summary>
public static class ProjectionScenarios
{
    public static void Run()
    {
    // ===== FreeSql 单表 =====
    using var fsql = DbSeedData.CreateInMemoryFreeSql();
    DbSeedData.SeedUsers(fsql);

    var fsDtos = FreeSqlScenarios.SingleTableAsDto(fsql);
    OutputHelper.Dump("501-freesql-single-table", "WHERE Age > 20", fsDtos);
    // 期望值：DbSeedData.SeedUsers 种子契约——3 行全过 Age>20，首行 Alice/25（单表投影列→属性映射语义）
    if (fsDtos.Count != 3 || fsDtos[0].Name != "Alice" || fsDtos[0].Age != 25)
        OutputHelper.Error($"501-freesql-single-table: WHERE Age > 20 should map 3 rows, first Alice/25 (actual: {fsDtos.Count} rows)");

    var fsPaged = FreeSqlScenarios.SingleTablePaged(fsql);
    OutputHelper.Dump("501-freesql-paged", "Page 1, Size 2", fsPaged);
    // 期望值：ToPagedList 移交断言——PagedList<TDto> 契约（种子 3 行按页大小 2 切分）
    if (fsPaged.Page != 1 || fsPaged.PageSize != 2 || fsPaged.TotalCount != 3 || fsPaged.Items.Count != 2)
        OutputHelper.Error($"501-freesql-paged: page=1 size=2 over 3 rows should yield Page=1/PageSize=2/TotalCount=3/Items=2 (actual: {fsPaged.Page}/{fsPaged.PageSize}/{fsPaged.TotalCount}/{fsPaged.Items.Count})");

    var fsChunk = FreeSqlScenarios.ToChunkDto(fsql);
    OutputHelper.Dump("501-freesql-chunk", $"Total: {fsChunk.Count}", fsChunk);
    // 期望值：分批守恒语义——种子 3 行全数进入分块回调
    if (fsChunk.Count != 3)
        OutputHelper.Error($"501-freesql-chunk: batched reads should conserve all 3 rows (actual: {fsChunk.Count})");

    // ===== FreeSql ToDictionary 字典终端 =====
    var fsDict = fsql.Select<UserEntity>().AsDto().ToDictionary<int, UserDto>(u => u.Id);
    OutputHelper.Dump("501-freesql-dictionary", $"Keys: {string.Join(",", fsDict.Keys.OrderBy(k => k))}", fsDict);
    // 补演示（期望值来源：DbSeedData.SeedUsers 种子契约 Id=1 Alice/2 Bob/3 Charlie +
    // 原生 ToDictionary 取实体字典→逐 value Map 的既有语义）
    if (fsDict.Count != 3 || !fsDict.ContainsKey(2) || fsDict[2].Name != "Bob")
        OutputHelper.Error($"501-freesql-dictionary: keys should be {{1,2,3}} with dict[2].Name=Bob (actual: {fsDict.Count} entries)");

    var fsDictAsync = fsql.Select<UserEntity>().AsDto()
        .ToDictionaryAsync<int, UserDto>(u => u.Id).GetAwaiter().GetResult();
    // 补演示（期望值来源：异步字典终端与同步形态同语义 + 种子 3 行）
    if (fsDictAsync.Count != 3 || fsDictAsync[3].Name != "Charlie")
        OutputHelper.Error($"501-freesql-dictionary: async dict should carry 3 entries with [3].Name=Charlie (actual: {fsDictAsync.Count})");

    // ===== FreeSql 多表 Compose =====
    var fsql2 = DbSeedData.CreateInMemoryFreeSql();
    WikiSeedData.SeedWikiData(fsql2);
    var multiTwo = FreeSqlScenarios.MultiTableTwoWay(fsql2);
    OutputHelper.Dump("501-freesql-multi-2table", $"Count: {multiTwo.Count}", multiTwo);
    // 补断言（期望值来源：WikiSeedData.SeedWikiData 种子——O1/O2→C1 张三/北京、O3→C2 李四/上海；
    // NeedsDegradationCompose=false 走投影路径，每单皆有客户
    // 无 null 行故投影/降级两态行数同=3；行序无保证，按 OrderNo 定位）
    if (multiTwo.Count != 3
        || multiTwo.Single(o => o.OrderNo == "ORD-001").CustomerName != "张三"
        || multiTwo.Single(o => o.OrderNo == "ORD-003").City != "上海")
        OutputHelper.Error($"501-freesql-multi-2table: two-source compose should yield 3 rows with ORD-001=张三, ORD-003 City=上海 (actual: {multiTwo.Count})");

    var multiThree = FreeSqlScenarios.MultiTableThreeWay(fsql2);
    OutputHelper.Dump("501-freesql-multi-3table", $"Count: {multiThree.Count}", multiThree);
    // 补断言（期望值来源：明细种子 O1→笔记本+鼠标 2 行、O2→键盘 1 行、O3→无；投影路径 LeftJoin
    // 保留 null 行（O3 行 Product=null）→ 2+1+1=4；WikiOrderFullDto 全标量+顶层跨表 MapProperty 无降级因子。
    // 若执行期路径翻降级（FilterAndMap2 滤 null 行→3 行）本断言应红暴露问题，不得改期望值迁就实跑（GF11））
    if (multiThree.Count != 4
        || multiThree.Count(r => r.OrderNo == "ORD-001") != 2
        || !multiThree.Any(r => r.OrderNo == "ORD-002" && r.Product == "键盘" && r.Quantity == 1))
        OutputHelper.Error($"501-freesql-multi-3table: three-source compose should yield 4 rows (ORD-001 twice, ORD-002 键盘 qty1) (actual: {multiThree.Count})");

    // ===== FreeSql Include 导航 =====
    var fsql3 = DbSeedData.CreateInMemoryFreeSql();
    WikiSeedData.SeedCategoryData(fsql3);
    var includeResult = FreeSqlScenarios.IncludeNavigation(fsql3);
    OutputHelper.Dump("501-freesql-include", $"Count: {includeResult.Count}", includeResult);

    // ===== FreeSql WithSql =====
    var withSqlResult = FreeSqlScenarios.WithSqlAsDto(fsql);
    OutputHelper.Dump("501-freesql-withsql", $"Count: {withSqlResult.Count}", withSqlResult);

    // ===== EF Core =====
    using var efConnection = DbSeedData.CreateInMemoryConnection();
    var efOpts = new DbContextOptionsBuilder<InMemoryDbContext>()
        .UseSqlite(efConnection)
        .Options;
    using var db = new InMemoryDbContext(efOpts);
    db.Database.EnsureCreated();
    db.Users.AddRange(
        new UserEntity { Id = 1, Name = "Alice", Age = 25, Email = "alice@example.com" },
        new UserEntity { Id = 2, Name = "Bob", Age = 30, Email = "bob@example.com" },
        new UserEntity { Id = 3, Name = "Charlie", Age = 35, Email = "charlie@example.com" }
    );
    db.SaveChanges();

    var efDtos = EFCoreScenarios.SingleTableAsDto(db);
    OutputHelper.Dump("501-efcore-single-table", "WHERE Age > 20", efDtos);
    // 期望值：EFCore 种子契约——3 行全过 Age>20（列裁剪投影语义）
    if (efDtos.Count != 3 || efDtos[0].Name != "Alice")
        OutputHelper.Error($"501-efcore-single-table: WHERE Age > 20 should map 3 rows, first Alice (actual: {efDtos.Count} rows)");

    var efChunk = EFCoreScenarios.ToChunkDto(db);
    OutputHelper.Dump("501-efcore-chunk", $"Total: {efChunk.Count}", efChunk);
    // 期望值：ToChunkDto 移交断言——分批守恒语义 + EFCore 种子 3 行
    if (efChunk.Count != 3)
        OutputHelper.Error($"501-efcore-chunk: batched reads should conserve all 3 rows (actual: {efChunk.Count})");

    var efLookup = EFCoreScenarios.ToLookup(db);
    OutputHelper.Dump("501-efcore-lookup", $"Groups: {efLookup.Count}", "");
    // 期望值：ToLookup 移交断言——keySelector Age/10 → 组 2(1 行)+组 3(2 行)
    if (efLookup.Count != 2 || efLookup[2].Count() != 1 || efLookup[3].Count() != 2)
        OutputHelper.Error($"501-efcore-lookup: Age/10 should yield group 2 (1 row) and group 3 (2 rows) (actual: {efLookup.Count} groups)");

    // 期望值：ToDictionary 移交断言——EFCoreScenarios 的字典终端为异步形态 ToDictionaryAsync；
    // keySelector u => u.Id 契约 + 种子键 1/2/3
    var efDict = EFCoreScenarios.ToDictionaryAsync(db).GetAwaiter().GetResult();
    OutputHelper.Dump("501-efcore-dictionary", $"Keys: {string.Join(",", efDict.Keys)}", efDict);
    if (efDict.Count != 3 || !efDict.ContainsKey(3) || efDict[3].Name != "Charlie")
        OutputHelper.Error($"501-efcore-dictionary: keys should be {{1,2,3}} with dict[3].Name=Charlie (actual: {efDict.Count} entries)");

    // ===== EFCore ToPagedList 分页终端 =====
    var efPaged = db.Set<UserEntity>().OrderBy(u => u.Id).AsDto().ToPagedList<UserDto>(1, 2);
    OutputHelper.Dump("501-efcore-paged", "Page 1, Size 2", efPaged);
    // 补演示（期望值来源：EF 分页终端 Count+Skip/Take 均 SQL 侧 +
    // PagedList 语义 TotalPages=⌈3/2⌉=2、HasNext=Page<TotalPages + EFCore 种子 3 行按 Id 序）
    if (efPaged.TotalCount != 3 || efPaged.Items.Count != 2 || efPaged.Page != 1
        || efPaged.TotalPages != 2 || !efPaged.HasNext || efPaged.Items[0].Name != "Alice")
        OutputHelper.Error($"501-efcore-paged: page=1 size=2 over 3 rows should yield Total=3/Items=2/TotalPages=2/HasNext/first Alice (actual: {efPaged.TotalCount}/{efPaged.Items.Count}/{efPaged.TotalPages})");

    var efPaged2 = db.Set<UserEntity>().OrderBy(u => u.Id).AsDto()
        .ToPagedListAsync<UserDto>(2, 2).GetAwaiter().GetResult();
    OutputHelper.Dump("501-efcore-paged-async", "Page 2, Size 2 (async)", efPaged2);
    // 补演示（期望值来源：Skip(2) 余 1 行 Charlie + PagedList 语义 HasPrev=Page>1、HasNext=Page<TotalPages）
    if (efPaged2.Items.Count != 1 || efPaged2.Items[0].Name != "Charlie" || !efPaged2.HasPrev || efPaged2.HasNext)
        OutputHelper.Error($"501-efcore-paged-async: page=2 should yield 1 item Charlie with HasPrev and no HasNext (actual: {efPaged2.Items.Count})");

    // ===== SqlDialectResolver 按连接解析（落 501）=====
    var resolvedDialect = SqlDialectResolver.Resolve(efConnection);
    var resolvedPageSql = resolvedDialect.PageSql("SELECT * FROM Users", "@offset", "@pageSize");
    OutputHelper.Dump("501-dapper-resolve-dialect", "Resolve(SqliteConnection) -> SqliteDialect", resolvedPageSql);
    // 补演示（期望值来源：SqlDialects 类型名 switch——"SqliteConnection"→SqliteDialect，
    // 未识别才回退 MySqlDialect（:313）——类型断言非恒真；SqliteDialect=LIMIT/OFFSET 方言，与 502-dapper-dialect 同源）
    if (resolvedDialect is not SqliteDialect || !resolvedPageSql.Contains("LIMIT") || !resolvedPageSql.Contains("OFFSET"))
        OutputHelper.Error($"501-dapper-resolve-dialect: SqliteConnection should resolve to SqliteDialect with LIMIT/OFFSET page SQL (actual: {resolvedDialect.GetType().Name})");

        // ===== EF Core 集合索引路径投影=====
    db.Blogs.AddRange(
        new Blog { Id = 1, Title = "Blog-A", Posts = { new Post { Id = 1, BlogId = 1, Title = "Post-1" }, new Post { Id = 2, BlogId = 1, Title = "Post-2" } } },
        new Blog { Id = 2, Title = "Blog-B", Posts = { } }
    );
    db.SaveChanges();

    var (indexDtos, indexSql) = EFCoreScenarios.MapIndexPathProjection(db);
    OutputHelper.Dump("501-index-path-projection", "Posts[0].Title → LIMIT 1 OFFSET 0 (unordered single row)", indexDtos);
    OutputHelper.Dump("501-index-path-sql", "Equivalent query SQL (LIMIT 1 OFFSET 0)", indexSql);

    // ===== Runtime×ORM：Fluent DTO + AsDto 双态=====
    db.FxWikis.AddRange(
        new FxWikiEntity { Id = 1, Title = "NCMapper-Fluent", Body = "投影列裁剪演示正文" },
        new FxWikiEntity { Id = 2, Title = "Runtime Snapshot", Body = "注册表承载演示正文" });
    db.FxMasks.AddRange(new FxMaskEntity { Id = 1, Phone = "13800002222", Title = "Mask-Degrade" });
    db.SaveChanges();

    // ① 纯可翻译声明 → 投影路径可用（EFCore/FreeSql 双 ORM）
    var fxEf = FxScenarios.ECoreFluentProjection(db);
    OutputHelper.Dump("501-fx-efcore-projection", "Fluent DTO (purely translatable) AsDto", fxEf);
    if (fxEf.Count != 2 || fxEf[0].Title != "NCMapper-Fluent")
        OutputHelper.Error("501-fx-efcore-projection: Fluent DTO projection result mismatch");

    // ② Mask 声明 → 一律强制降级（内存映射 + 脱敏不落明文——安全断言）
    var fxEfMask = FxScenarios.ECoreFluentMaskDowngrade(db);
    OutputHelper.Dump("501-fx-efcore-mask-degrade", "Fluent DTO (with Mask) forced downgrade", fxEfMask);
    if (fxEfMask.Count != 1 || fxEfMask[0].Phone.Contains("13800002222") || !fxEfMask[0].Phone.Contains("****"))
        OutputHelper.Error("501-fx-efcore-mask-degrade: mask downgrade assertion failed (no plaintext output)");

    using var fsqlFx = DbSeedData.CreateInMemoryFreeSql();
    fsqlFx.CodeFirst.SyncStructure<FxWikiEntity>();
    fsqlFx.CodeFirst.SyncStructure<FxMaskEntity>();
    fsqlFx.Insert(new FxWikiEntity { Id = 1, Title = "FS-Fluent", Body = "正文" }).ExecuteAffrows();
    fsqlFx.Insert(new FxMaskEntity { Id = 1, Phone = "13900003333", Title = "FS-Mask" }).ExecuteAffrows();

    var fxFs = FxScenarios.FreeSqlFluentProjection(fsqlFx);
    OutputHelper.Dump("501-fx-freesql-projection", "Fluent DTO (purely translatable) AsDto", fxFs);
    if (fxFs.Count < 1 || fxFs[0].Title != "FS-Fluent")
        OutputHelper.Error("501-fx-freesql-projection: FreeSql Fluent DTO projection result mismatch");

    var fxFsMask = FxScenarios.FreeSqlFluentMaskDowngrade(fsqlFx);
    OutputHelper.Dump("501-fx-freesql-mask-degrade", "Fluent DTO (with Mask) forced downgrade", fxFsMask);
    if (fxFsMask.Count < 1 || fxFsMask[0].Phone.Contains("13900003333") || !fxFsMask[0].Phone.Contains("****"))
        OutputHelper.Error("501-fx-freesql-mask-degrade: mask downgrade assertion failed (no plaintext output)");

    // ===== EFCore AsTracking 显式跟踪双态 =====
    // 新 context 对照：原 db 已 SaveChanges 本地实例入轨，会污染「默认无跟踪」态；
    // 投影路径不物化实体跟踪不可观测，故用 Mask 强制降级对（FxModels.cs:80-81）承载双态
    using var trackDb = new InMemoryDbContext(efOpts);
    var maskNoTrack = trackDb.Set<FxMaskEntity>().AsDto().ToList<FxMaskDto>();
    var entriesNoTrack = trackDb.ChangeTracker.Entries<FxMaskEntity>().Count();
    trackDb.ChangeTracker.Clear();
    var maskTracked = trackDb.Set<FxMaskEntity>().AsDto().AsTracking().ToList<FxMaskDto>();
    var entriesTracked = trackDb.ChangeTracker.Entries<FxMaskEntity>().Count();
    OutputHelper.Dump("501-efcore-tracking", $"NoTrack entries: {entriesNoTrack}, AsTracking entries: {entriesTracked}", maskTracked);
    // 补演示（期望值来源：NoTrackQuery 默认追加 AsNoTracking 的既有约定、
    // AsTracking :109-113 置 _tracking 跳过之；降级路径 ToList :248 物化实体；FxMask 种子 1 行）
    if (entriesNoTrack != 0 || entriesTracked != 1)
        OutputHelper.Error($"501-efcore-tracking: default degrade should track 0 entities, AsTracking should track 1 (actual: {entriesNoTrack}/{entriesTracked})");
    if (maskTracked.Count != 1 || maskTracked[0].Phone.Contains("13800002222") || !maskTracked[0].Phone.Contains("****"))
        OutputHelper.Error("501-efcore-tracking: tracking must not alter mask degrade output (no plaintext)");

        // ===== EFCore ToTreeList + Fluent 树查询=====
    db.Categories.AddRange(
        new CategoryEntity { NodeId = 1, ParentNodeId = null, Name = "Root" },
        new CategoryEntity { NodeId = 2, ParentNodeId = 1, Name = "Child-A" },
        new CategoryEntity { NodeId = 3, ParentNodeId = 1, Name = "Child-B" },
        new CategoryEntity { NodeId = 4, ParentNodeId = 2, Name = "Grandchild" });
    db.FxTrees.AddRange(
        new FxTreeEntity { Id = 1, Name = "Fluent-Root", ParentId = null },
        new FxTreeEntity { Id = 2, Name = "Fluent-Child", ParentId = 1 });
    db.SaveChanges();

    var efTree = EFCoreScenarios.ToTreeListFlat(db);
    OutputHelper.Dump("501-efcore-treelist", "[MapTree] attribute channel ToTreeList (Root with 2 children)", efTree);
    if (efTree.Count != 1 || efTree[0].Children.Count != 2 || efTree[0].Children[0].Children.Count != 1)
        OutputHelper.Error("501-efcore-treelist: tree assembly structure mismatch (expected Root→2 children→1 grandchild)");

    var efFluentTree = EFCoreScenarios.ToTreeListFluent(db);
    OutputHelper.Dump("501-efcore-fluent-tree", "Fluent Tree() registry-backed ToTreeList", efFluentTree);
    if (efFluentTree.Count != 1 || efFluentTree[0].Children.Count != 1)
        OutputHelper.Error("501-efcore-fluent-tree: Fluent tree assembly structure mismatch (expected Root→1 child)");
    }
}
