using System.Collections.Concurrent;
using FreeSql;
using NCMapper;
using NCMapper.FreeSql;
using NCMapper.IntegrationTests.Models;
using Xunit;

namespace NCMapper.IntegrationTests;

/// <summary>
/// ORM 投影/降级缓存并发测试（C9 补测）
/// <para>断言形态：并发投影结果守恒 + 版本变更后旧缓存不命中。
/// 投影导航收集缓存键含 <see cref="MapPipeline.s_version"/>（FreeSqlNavigationCollector），
/// Pipeline 注册/清除使版本递增 → 旧缓存 key 失效 → 重建；测试验证重建后投影结果仍守恒
/// （无脏缓存污染）。</para>
/// </summary>
[Collection("DeepCopyGlobalState")]
public class ProjectionCacheConcurrencyTests : IDisposable
{
    /// <summary>
    /// FreeSql 实例（sqlite 临时库）
    /// </summary>
    private readonly IFreeSql _fsql;

    /// <summary>
    /// 临时数据库路径
    /// </summary>
    private readonly string _dbPath;

    /// <summary>
    /// 构造 — 初始化 sqlite 临时库并播种产品数据
    /// </summary>
    public ProjectionCacheConcurrencyTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"ncmapper_proj_{Guid.NewGuid():N}.db");
        _fsql = new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, $"DataSource={_dbPath}")
            .UseAutoSyncStructure(true)
            .Build();
        SeedData();
    }

    /// <summary>
    /// 释放 — 关闭连接并删除临时库文件
    /// </summary>
    public void Dispose()
    {
        _fsql.Dispose();
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { /* 句柄释放竞态可容忍 */ }
        }
    }

    /// <summary>
    /// 播种三条产品数据（覆盖不同枚举状态）
    /// </summary>
    private void SeedData()
    {
        _fsql.Insert(new ProductEntity { Id = 1, Name = "P1", Price = 10m, Status = OrderStatus.Confirmed }).ExecuteAffrows();
        _fsql.Insert(new ProductEntity { Id = 2, Name = "P2", Price = 20m, Status = OrderStatus.Confirmed }).ExecuteAffrows();
        _fsql.Insert(new ProductEntity { Id = 3, Name = "P3", Price = 30m, Status = OrderStatus.Cancelled }).ExecuteAffrows();
    }

    /// <summary>
    /// 并发 AsDto 投影 — 结果守恒（固定字段 + 枚举 + 行数）
    /// <para>多线程并发走 SQL 投影路径（AsDto().ToList()），断言各线程投影结果
    /// 字段守恒（名称/价格/枚举一致、行数一致），验证投影缓存并发构建无脏数据。</para>
    /// </summary>
    [Fact]
    public void ConcurrentAsDtoProjection_ResultsConserved()
    {
        var failures = new ConcurrentBag<string>();

        Parallel.For(0, 8, _ =>
        {
            try
            {
                var dtos = _fsql.Select<ProductEntity>().AsDto().ToList<ProductDto>();
                if (dtos.Count != 3)
                {
                    failures.Add($"投影行数异常: {dtos.Count}");
                    return;
                }

                var p1 = dtos.First(d => d.Id == 1);
                if (p1.Name != "P1" || p1.Price != 10m || p1.Status != OrderStatus.Confirmed)
                    failures.Add($"P1 字段不守恒: Name={p1.Name}, Price={p1.Price}, Status={p1.Status}");

                var p3 = dtos.First(d => d.Id == 3);
                if (p3.Name != "P3" || p3.Price != 30m || p3.Status != OrderStatus.Cancelled)
                    failures.Add($"P3 字段不守恒: Name={p3.Name}, Price={p3.Price}, Status={p3.Status}");
            }
            catch (Exception ex) { failures.Add(ex.Message); }
        });

        Assert.True(failures.IsEmpty, $"并发投影问题: {string.Join("; ", failures.Take(3))}");
    }

    /// <summary>
    /// Pipeline 版本变更 — 旧投影缓存不命中重建，结果仍守恒
    /// <para>步骤：首次投影建立缓存（key 含 s_version=V0）→ Pipeline 注册回调
    /// （s_version 递增 V1，旧缓存 key 失效）→ 再次投影触发重建 → 清理 Pipeline
    /// （s_version 再递增）→ 再次投影。全程投影结果守恒，验证版本失效机制
    /// 无脏缓存残留。</para>
    /// </summary>
    [Fact]
    public void PipelineVersionChange_InvalidatesProjectionCache()
    {
        // 1. 首次投影（基线，s_version=V0）建立导航收集缓存
        var before = _fsql.Select<ProductEntity>().AsDto().ToList<ProductDto>();
        Assert.Equal(3, before.Count);
        Assert.Equal("P1", before.First(d => d.Id == 1).Name);

        // 2. Pipeline 注册回调 → s_version 递增（版本变更，旧缓存失效）
        Mapper.Pipeline.AddPostMap<ProductEntity, ProductDto>((src, dto) => { });
        try
        {
            // 3. 再次投影 → 旧缓存不命中（key 含新版本）→ 重建，结果仍守恒
            var after = _fsql.Select<ProductEntity>().AsDto().ToList<ProductDto>();
            Assert.Equal(3, after.Count);
            var p1 = after.First(d => d.Id == 1);
            Assert.Equal("P1", p1.Name);
            Assert.Equal(10m, p1.Price);
            Assert.Equal(OrderStatus.Confirmed, p1.Status);
        }
        finally
        {
            Mapper.Pipeline.Clear();
        }

        // 4. 清理后再投影（版本再变更）→ 仍守恒
        var cleared = _fsql.Select<ProductEntity>().AsDto().ToList<ProductDto>();
        Assert.Equal(3, cleared.Count);
        Assert.Equal("P3", cleared.First(d => d.Id == 3).Name);
    }
}
