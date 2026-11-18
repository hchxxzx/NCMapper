using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics.CodeAnalysis;
namespace NCMapper;

/// <summary>
/// NCMapper 依赖注入扩展方法
/// </summary>
public static class NCMapperServiceExtensions
{
    /// <summary>
    /// 注册 NCMapper IMapper 到 DI 容器（Singleton 生命周期），使用默认配置
    /// 用法：services.AddNCMapper();
    /// </summary>
    /// <param name="services">IServiceCollection</param>
    /// <returns>IServiceCollection（链式调用）</returns>
    public static IServiceCollection AddNCMapper(this IServiceCollection services)
    {
        // 幂等注册 — 已注册时跳过，避免重复注册导致多个 IMapper 实例
        if (!services.Any(s => s.ServiceType == typeof(IMapper)))
            services.AddSingleton<IMapper, NCMapperMapper>();
        return services;
    }

    /// <summary>
    /// 注册 NCMapper IMapper 到 DI 容器，并通过回调配置选项
    /// 用法：services.AddNCMapper(opts => { opts.ValidationEnabled = false; });
    /// <para>从 appsettings.json 读取时，先用 IConfiguration 绑定到 NCMapperOptions 再传入：</para>
    /// <para><c>var opts = new NCMapperOptions(); config.GetSection("NCMapper").Bind(opts);</c></para>
    /// <para><c>services.AddNCMapper(o => { o.ValidationEnabled = opts.ValidationEnabled; });</c></para>
    /// </summary>
    /// <param name="services">IServiceCollection</param>
    /// <param name="configure">配置回调</param>
    /// <returns>IServiceCollection（链式调用）</returns>
    [RequiresDynamicCode("AddNCMapper builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    public static IServiceCollection AddNCMapper(this IServiceCollection services, Action<NCMapperOptions>? configure)
    {
        // 幂等跳过注册时同步跳过 Apply——原第二阶段
        //（IMapper 已注册）仍执行 Apply，configure 未显式设置的选项用默认值回抹全局状态
        //（两阶段注册时第一阶段的显式设置被默认值覆盖，Mapper.TraceEnabled true→false）；
        // 注册与配置应用同为幂等语义（第一阶段为权威），行为变更见 CHANGELOG。
        if (services.Any(s => s.ServiceType == typeof(IMapper)))
            return services;

        var options = new NCMapperOptions();
        configure?.Invoke(options);
        options.Apply();

        // 幂等注册 — 已注册时跳过，避免重复注册导致多个 IMapper 实例
        services.AddSingleton<IMapper, NCMapperMapper>();
        return services;
    }

    /// <summary>
    /// 注册 NCMapper IMapper 到 DI 容器，并从 <see cref="IConfiguration"/> 节绑定选项
    /// 用法：services.AddNCMapper(configuration.GetSection("NCMapper"));
    /// <para>可绑定键：ValidationEnabled / TraceEnabled / MaxRecursionDepth / MaxInlineDepth / DeepCopyStrategy</para>
    /// <para>注意：<see cref="NCMapperOptions.TraceOutput"/>（TextWriter）无法从 JSON 绑定，
    /// 如需自定义请改用 <see cref="AddNCMapper(IServiceCollection, Action{NCMapperOptions})"/> 重载在代码中赋值</para>
    /// </summary>
    /// <param name="services">IServiceCollection</param>
    /// <param name="configuration">配置节（如 configuration.GetSection("NCMapper")）</param>
    /// <returns>IServiceCollection（链式调用）</returns>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("configuration.Bind uses reflection to set NCMapperOptions members. Some members may be trimmed. Use the AddNCMapper(IServiceCollection, Action<NCMapperOptions>) overload for an AOT-safe alternative.")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("configuration.Bind may require dynamic code to construct bound types. Not supported in AOT. Use the AddNCMapper(IServiceCollection, Action<NCMapperOptions>) overload for an AOT-safe alternative.")]
    public static IServiceCollection AddNCMapper(this IServiceCollection services, IConfiguration configuration)
    {
        // 幂等跳过注册时同步跳过 Apply；Bind 天然仅对节内
        // 显式存在的键生效（MS ConfigurationBinder 语义），未出现键保持 options 默认值——
        // 两阶段场景第二阶段整体跳过后不再有默认值回抹路径。
        if (services.Any(s => s.ServiceType == typeof(IMapper)))
            return services;

        var options = new NCMapperOptions();
        configuration.Bind(options);
        options.Apply();

        // 幂等注册 — 已注册时跳过，避免重复注册导致多个 IMapper 实例
        services.AddSingleton<IMapper, NCMapperMapper>();
        return services;
    }
}
