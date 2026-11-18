using FreeSql;
using Microsoft.EntityFrameworkCore;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.EFCore;
using NCMapper.FreeSql;
using NCMapper.IntegrationTests.Models;
using Xunit;

namespace NCMapper.IntegrationTests;

/// <summary>
/// 投影忽略清单 × 扁平化/点路径 叶节点源侧判定
/// <para>背景：全局忽略清单对扁平化/点路径命中项的源侧判定，投影表达式引擎漏判（命中后直接 return，
/// 不过 ShouldIgnoreSource），四引擎行为不一致且为静默数据流出。本类锁定修复后行为：
/// ① 叶节点命中忽略清单 → 该属性不映射（与源生成器/运行时一致）；
/// ② 中间导航节点命中清单 → 不判定（判定粒度=叶节点，与源生成器值模型管线一致）。</para>
/// <para>隔离范式与 ProjectionGlobalConfigConsistencyTests 一致：
/// [Collection("PipelineGlobalState")] + 构造器/Dispose 双 Mapper.ClearCache()
/// （含投影缓存清理链；进程级串行由 AssemblyInfo DisableTestParallelization 保证）。</para>
/// <para>用例复用 Blog 类型对（BlogEntity/BlogFlattenedDto/BlogDotPathDto，G7 fixture）；
/// 禁止在无隔离机制的 EFCoreProjectionTests/FreeSqlProjectionTests 中直接追加配置类用例。</para>
/// </summary>
[Collection("PipelineGlobalState")]
public class ProjectionIgnoreFlattenTests : IDisposable
{
    /// <summary>
    /// 测试前重置全局配置冻结状态（测试隔离）
    /// </summary>
    public ProjectionIgnoreFlattenTests() { Mapper.ClearCache(); GlobalMapConfig.ResetForTests(); } // 迁移

    /// <summary>
    /// 测试后重置冻结状态 — 防止配置冻结残留污染其他测试类
    /// </summary>
    public void Dispose()
    {
        Mapper.ClearCache();        GlobalMapConfig.ResetForTests(); // 迁移

    }

    /// <summary>
    /// 用例 1a（忽略×命名约定，FreeSql）：IgnoreNames=["Name","Author"] → BlogFlattenedDto 投影
    /// <para>叶节点 Name 命中清单 → AuthorName 不映射（null）；
    /// 中间节点 Author 命中清单但不判定（粒度=叶节点）→ AuthorEmail 正常映射；
    /// 一项配置同时锁定"叶节点判定 + 中间节点不判定"粒度。</para>
    /// </summary>
    [Fact]
    public void FreeSql_Flattened_GlobalIgnore_LeafSkipped_IntermediateNotJudged()
    {
        Mapper.RegisterGeneratedConfig(
            new GeneratedMapConfig { IgnoreNames = ["Name", "Author"] },
            "ProjIgnoreFlatten");

        var dbPath = Path.Combine(Path.GetTempPath(), $"ncmapper_projignore_{Guid.NewGuid():N}.db");
        try
        {
            using var fsql = new FreeSqlBuilder()
                .UseConnectionString(DataType.Sqlite, $"DataSource={dbPath}")
                .UseAutoSyncStructure(true)
                .Build();
            fsql.Insert(new AuthorEntity { Id = 1, Name = "Alice Writer", Email = "alice@blog.com" }).ExecuteAffrows();
            fsql.Insert(new BlogEntity { Id = 1, Title = "NCMapper 入门指南", AuthorId = 1 }).ExecuteAffrows();

            var expr = FreeSqlProjectionBuilder.Build<BlogEntity, BlogFlattenedDto>();
            var dto = fsql.Select<BlogEntity>().Where(b => b.Id == 1).ToList(expr).First();

            // 叶节点 Name 命中全局忽略清单 → 不映射（fail-closed 留空，与源生成器/运行时一致）
            Assert.Null(dto.AuthorName);
            // 中间节点 Author 命中清单但不判定（粒度=叶节点）→ AuthorEmail 正常映射
            Assert.Equal("alice@blog.com", dto.AuthorEmail);
            // 未忽略属性正常映射（断言仅锁定叶/中间粒度两属性，不约束其他属性）
            Assert.Equal("NCMapper 入门指南", dto.Title);
        }
        finally
        {
            if (File.Exists(dbPath)) { try { File.Delete(dbPath); } catch { } }
        }
    }

    /// <summary>
    /// 用例 1b（忽略×命名约定，EFCore）：同配置 → BlogFlattenedDto 投影
    /// </summary>
    [Fact]
    public void EFCore_Flattened_GlobalIgnore_LeafSkipped_IntermediateNotJudged()
    {
        Mapper.RegisterGeneratedConfig(
            new GeneratedMapConfig { IgnoreNames = ["Name", "Author"] },
            "ProjIgnoreFlatten");

        var connStr = $"DataSource=file:projignore{Guid.NewGuid():N}?mode=memory&cache=shared";
        using var db = new TestDbContext(connStr);
        db.SeedData();

        var dto = db.Blogs
            .Where(b => b.Id == 1)
            .Select(EfCoreProjectionBuilder.Build<BlogEntity, BlogFlattenedDto>())
            .First();

        Assert.Null(dto.AuthorName);
        Assert.Equal("alice@blog.com", dto.AuthorEmail);
        Assert.Equal("NCMapper 入门指南", dto.Title);
    }

    /// <summary>
    /// 用例 2a（忽略×点路径，FreeSql）：同配置 + BlogDotPathDto
    /// （[MapProperty(typeof(BlogEntity), "Author.Name")]）→ 叶节点 Name 命中 → AuthorName 不映射（null）
    /// </summary>
    [Fact]
    public void FreeSql_DotPath_GlobalIgnore_LeafSkipped()
    {
        Mapper.RegisterGeneratedConfig(
            new GeneratedMapConfig { IgnoreNames = ["Name", "Author"] },
            "ProjIgnoreFlatten");

        var dbPath = Path.Combine(Path.GetTempPath(), $"ncmapper_projignore_{Guid.NewGuid():N}.db");
        try
        {
            using var fsql = new FreeSqlBuilder()
                .UseConnectionString(DataType.Sqlite, $"DataSource={dbPath}")
                .UseAutoSyncStructure(true)
                .Build();
            fsql.Insert(new AuthorEntity { Id = 1, Name = "Alice Writer", Email = "alice@blog.com" }).ExecuteAffrows();
            fsql.Insert(new BlogEntity { Id = 1, Title = "NCMapper 入门指南", AuthorId = 1 }).ExecuteAffrows();

            var expr = FreeSqlProjectionBuilder.Build<BlogEntity, BlogDotPathDto>();
            var dto = fsql.Select<BlogEntity>().Where(b => b.Id == 1).ToList(expr).First();

            Assert.Null(dto.AuthorName);
            Assert.Equal(1, dto.Id);
        }
        finally
        {
            if (File.Exists(dbPath)) { try { File.Delete(dbPath); } catch { } }
        }
    }

    /// <summary>
    /// 用例 2b（忽略×点路径，EFCore）：同配置 + BlogDotPathDto → AuthorName 不映射（null）
    /// </summary>
    [Fact]
    public void EFCore_DotPath_GlobalIgnore_LeafSkipped()
    {
        Mapper.RegisterGeneratedConfig(
            new GeneratedMapConfig { IgnoreNames = ["Name", "Author"] },
            "ProjIgnoreFlatten");

        var connStr = $"DataSource=file:projignore{Guid.NewGuid():N}?mode=memory&cache=shared";
        using var db = new TestDbContext(connStr);
        db.SeedData();

        var dto = db.Blogs
            .Where(b => b.Id == 1)
            .Select(EfCoreProjectionBuilder.Build<BlogEntity, BlogDotPathDto>())
            .First();

        Assert.Null(dto.AuthorName);
        Assert.Equal(1, dto.Id);
    }
}
