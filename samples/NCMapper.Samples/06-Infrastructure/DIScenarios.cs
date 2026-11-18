using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NCMapper.Samples.Models.Dtos;
using NCMapper.Samples.Models.Entities;
using NCMapper.Samples.Helpers;

namespace NCMapper.Samples.Infrastructure;

/// <summary>
/// DI 集成场景集 — AddNCMapper 注册、IMapper 注入、NCMapperOptions 配置。
/// </summary>
public static class DIScenarios
{
    /// <summary>
    /// 通过 DI 注册 NCMapper（默认配置），解析 IMapper。
    /// </summary>
    /// <returns>配置好的 IMapper 实例</returns>
    #region DI_ResolveMapper
    public static IMapper ResolveMapper()
    {
        var services = new ServiceCollection();
        services.AddNCMapper();
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IMapper>();
    }
    #endregion

    /// <summary>
    /// 通过 DI 注册 NCMapper（自定义 NCMapperOptions）。
    /// </summary>
    /// <param name="configure">Options 配置委托</param>
    /// <returns>配置好的 IMapper 实例</returns>
    #region DI_ResolveMapperWithOptions
    public static IMapper ResolveMapperWithOptions(Action<NCMapperOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddNCMapper(configure);
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IMapper>();
    }
    #endregion

    /// <summary>
    /// 从 IConfiguration 配置节绑定注册 — AddNCMapper(IConfiguration) 重载（appsettings 场景，巡览不挂 Run）。
    /// </summary>
    /// <param name="services">目标容器</param>
    /// <param name="configuration">应用配置根（含 "NCMapper" 节）</param>
    /// <returns>注册完成的容器（链式返回）</returns>
    #region DI_ConfigSectionBind
    public static IServiceCollection ConfigSectionBind(IServiceCollection services, IConfiguration configuration)
        => services.AddNCMapper(configuration.GetSection("NCMapper"));
    #endregion

    /// <summary>
    /// 使用 DI 解析的 IMapper 执行映射。
    /// </summary>
    /// <param name="mapper">DI 解析的 IMapper</param>
    /// <param name="src">源实体</param>
    /// <returns>映射后的 DTO</returns>
    public static UserDto MapWithDi(IMapper mapper, UserEntity src)
        => mapper.Map<UserEntity, UserDto>(src)!;

        // ===== 场景注册入口=====
    public static void Run()
    {
    var mapper = DIScenarios.ResolveMapper();
    var src = new UserEntity { Id = 1, Name = "DI-User", Age = 25, Email = "di@test.com" };
    var dto = DIScenarios.MapWithDi(mapper, src);
    OutputHelper.Dump("603-di-mapping", src, dto);
    // 期望值：DI 解析的 IMapper 注册为单例——字段映射照常落位
    if (dto.Name != "DI-User" || dto.Email != "di@test.com")
        OutputHelper.Error($"603-di-mapping: DI-resolved IMapper should map fields (actual: {dto.Name}/{dto.Email})");
    var mapperAgain = DIScenarios.ResolveMapper();
    if (!ReferenceEquals(mapper, mapperAgain))
    {
        // 跨 provider 解析不同实例属正常（各自容器）；单例契约 = 同一 provider 内两次解析同实例（di.md:36）
        var singletonServices = new ServiceCollection();
        singletonServices.AddNCMapper();
        var singletonProvider = singletonServices.BuildServiceProvider();
        var s1 = singletonProvider.GetRequiredService<IMapper>();
        var s2 = singletonProvider.GetRequiredService<IMapper>();
        if (!ReferenceEquals(s1, s2))
            OutputHelper.Error("603-di-mapping: AddNCMapper should register IMapper as singleton within one provider (di.md:36)");
    }
    // 期望值：IMapper 接口与静态 Mapper 类行为完全一致（契约语义）
    var staticDto = Mapper.Map<UserEntity, UserDto>(src)!;
    if (staticDto.Name != dto.Name || staticDto.Email != dto.Email || staticDto.Age != dto.Age)
        OutputHelper.Error($"603-di-mapping: IMapper and static Mapper should produce identical results (DI: {dto.Name}/{dto.Email}/{dto.Age})");
    // 期望值：AddNCMapper(configure) 配置重载——Options 委托被接受且解析可用
    var optionsMapper = DIScenarios.ResolveMapperWithOptions(o => o.ValidationEnabled = true);
    var optionsDto = DIScenarios.MapWithDi(optionsMapper, src);
    if (optionsDto.Name != "DI-User")
        OutputHelper.Error($"603-di-mapping: AddNCMapper(options) resolved mapper should map (actual: {optionsDto.Name})");
    }

}
