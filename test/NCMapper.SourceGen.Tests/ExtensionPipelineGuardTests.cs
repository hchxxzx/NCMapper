#pragma warning disable NCMapper001 // Pipeline API（Experimental）
using Xunit;
using NCMapper.Generated;
using NCMapper.SourceGen.Tests.Models;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// 串行集合 — 这些测试类共享静态 <see cref="NCMapper.Mapper.Pipeline"/>，
/// 并行执行会因 PostMap 回调 queue/cache 竞态产生 Heisenbug，必须串行
/// </summary>
[CollectionDefinition("PipelineSerial", DisableParallelization = true)]
public class PipelineSerialCollection { }

/// <summary>
/// 扩展方法 Pipeline 守护行为测试 — 验证 src.MapToXxx() 路径在管道启用时正确触发 PostMap
/// </summary>
[Collection("PipelineSerial")]
public class ExtensionPipelineGuardTests : IDisposable
{
    public void Dispose()
    {
        Mapper.Pipeline.Clear();
    }

    /// <summary>
    /// 单对象扩展方法 — Pipeline 启用时 PostMap 触发并可修改 DTO
    /// </summary>
    [Fact]
    public void Extension_MapTo_PostMap_ModifiesDto()
    {
        Mapper.Pipeline.AddPostMap<SgFlatEntity, SgFlatDto>((src, dto) =>
        {
            dto.Name = "PipelineModified";
        });

        var entity = new SgFlatEntity { Name = "Original" };
        var dto = entity.MapToSgFlatDto();

        Assert.NotNull(dto);
        Assert.Equal("PipelineModified", dto!.Name);
    }

    /// <summary>
    /// 单对象扩展方法 — Pipeline 启用时 PostMap 再 Set/Get 上下文数据携带
    /// </summary>
    [Fact]
    public void Extension_MapTo_PostMap_ContextFlow()
    {
        string? captured = null;

        Mapper.Pipeline
            .AddPostMap<SgFlatEntity, SgFlatDto>((src, dto) =>
            {
                captured = "FromPostMap";
            });

        var entity = new SgFlatEntity();
        entity.MapToSgFlatDto();

        Assert.Equal("FromPostMap", captured);
    }

    /// <summary>
    /// 单对象扩展方法（带 Action）— Pipeline 启用时 PostMap 在 Action 之后触发
    /// </summary>
    [Fact]
    public void Extension_MapTo_WithAction_PostMap_AfterAction()
    {
        var order = new List<string>();

        Mapper.Pipeline.AddPostMap<SgFlatEntity, SgFlatDto>((src, dto) =>
        {
            order.Add("PostMap");
        });

        var entity = new SgFlatEntity();
        entity.MapToSgFlatDto((src, dto) => order.Add("Action"));

        Assert.Equal(new[] { "Action", "PostMap" }, order);
    }

    /// <summary>
    /// 更新扩展方法 — Pipeline 启用时 PostMap 触发
    /// </summary>
    [Fact]
    public void Extension_Update_PostMap_Triggered()
    {
        bool postMapCalled = false;

        Mapper.Pipeline.AddPostMap<SgFlatEntity, SgFlatDto>((src, dto) =>
        {
            postMapCalled = true;
        });

        var entity = new SgFlatEntity { Name = "Updated" };
        var dto = new SgFlatDto();
        entity.MapToSgFlatDto(dto);

        Assert.True(postMapCalled);
        Assert.Equal("Updated", dto.Name);
    }

    /// <summary>
    /// 列表扩展方法 — Pipeline 启用时逐元素触发 PostMap
    /// </summary>
    [Fact]
    public void Extension_MapToList_PostMap_PerElement()
    {
        int postMapCount = 0;

        Mapper.Pipeline.AddPostMap<SgFlatEntity, SgFlatDto>((src, dto) =>
        {
            postMapCount++;
        });

        var entities = new List<SgFlatEntity> { new(), new(), new() };
        var dtos = entities.MapToSgFlatDtoList();

        Assert.NotNull(dtos);
        Assert.Equal(3, dtos!.Count);
        Assert.Equal(3, postMapCount);
    }

    /// <summary>
    /// Pipeline 未启用时扩展方法正常映射（守卫快速路径）
    /// </summary>
    [Fact]
    public void Extension_NoPipeline_NormalMapping()
    {
        // 不注册任何 Pipeline 回退 — s_isEnabled = false
        var entity = new SgFlatEntity { Id = 42, Name = "Hello", Age = 25 };
        var dto = entity.MapToSgFlatDto();

        Assert.NotNull(dto);
        Assert.Equal(42, dto!.Id);
        Assert.Equal("Hello", dto.Name);
        Assert.Equal(25, dto.Age);
    }

    /// <summary>
    /// Pipeline 类型过滤 — PostMap 只对匹配类型对生效，扩展方法路径同样遵守
    /// </summary>
    [Fact]
    public void Extension_TypeFilter_OnlyMatchingTypes()
    {
        bool called = false;

        // 注册一个不匹配的类型对
        Mapper.Pipeline.AddPostMap<SgConverterEntity, SgConverterDto>((src, dto) =>
        {
            called = true;
        });

        var entity = new SgFlatEntity();
        entity.MapToSgFlatDto();

        Assert.False(called);
    }
}
