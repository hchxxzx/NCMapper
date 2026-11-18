// ============================================================================
// P1Fix_T003 — 批次 3，MapPipeline 状态收敛 +
// PostMap 基类链回退 + AddNCMapper 幂等语义验收用例。
//
// （审计 20260827-005）：static s_isEnabled/s_version 与实例回调字典混杂——
// 任意实例 Clear() 拉低全局标志致单例回调静默失效；任意实例注册/清空互增全局
// 版本号致降级缓存反复失效。修复（评审 002 裁决"实例字段 + 入口收敛"非破坏
// 方案——Q8 排查 new MapPipeline( 仅测试使用，src/samples/ORM 扩展零命中）：
// 标志/版本实例化，公共入口经 Mapper.Pipeline 单例，IsPipelineEnabled 静态门面
// 转发单例（SourceGen 生成侧零改动）。
//
// ExecutePostMapNonGeneric 沿源类型基类链回退查找——派生源注册在基类的
// 回调可触发（原运行时精确匹配静默不执行，与 Generic 入口口径分叉）。
//
// AddNCMapper 幂等跳过注册时同步跳过 Apply——两阶段注册后全局配置保持。
// ============================================================================

using Microsoft.Extensions.DependencyInjection;
using NCMapper.Attributes;
using Xunit;

namespace NCMapper.Tests.Tests;

public class P1Fix_T003_PipelineIdempotency
{

    /// <summary>
    /// A1：双实例 A.Clear() 后单例 PostMap 回调仍执行——
    /// 原 static 标志被 A.Clear() 拉低致 Mapper.Map 跳过管道（回调静默失效）；
    /// 实例化后 A 的状态与单例隔离。
    /// </summary>
    [Fact]
    public void S24_双实例Clear_单例回调仍执行()
    {
        Mapper.Pipeline.Clear();
        try
        {
            var called = false;
            Mapper.Pipeline.AddPostMap<T3PEntity, T3PDto>((_, _) => called = true);

            var other = new MapPipeline(); // 第二实例
            other.Clear(); // 原：拉低全局 s_isEnabled → 单例回调失效

            var dto = Mapper.Map<T3PEntity, T3PDto>(new T3PEntity { Id = 1 });
            Assert.True(called); // 修复后：单例回调不受其他实例影响
        }
        finally
        {
            Mapper.Pipeline.Clear();
        }
    }

    /// <summary>
    /// A2：多实例互增 version 不影响单例——其他实例注册/清空不递增单例版本号
    /// （降级缓存键稳定），单例自身操作才递增。
    /// </summary>
    [Fact]
    public void S24_多实例互增version_单例版本号不变()
    {
        Mapper.Pipeline.Clear();
        var before = Mapper.Pipeline.Version;
        try
        {
            var other = new MapPipeline();
            other.AddPostMap<T3PEntity, T3PDto>((_, _) => { }); // 原：互增全局 s_version
            var mid = Mapper.Pipeline.Version;
            Assert.Equal(before, mid); // 实例隔离：other 注册不递增单例版本

            other.Clear(); // 原：互增全局 s_version（降级缓存失效）
            Assert.Equal(mid, Mapper.Pipeline.Version);

            Mapper.Pipeline.AddPostMap<T3PEntity, T3PDto>((_, _) => { }); // 单例自身操作才递增
            Assert.Equal(mid + 1, Mapper.Pipeline.Version);
        }
        finally
        {
            Mapper.Pipeline.Clear();
        }
    }


    /// <summary>
    /// A3：Compose + 派生源类型 PostMap 回调触发——注册在基类（T3Base, Dto）的回调
    /// 对派生源实例（T3Derived，主源类型）经 NonGeneric 入口（Compose）执行
    ///（原运行时精确匹配静默不触发）。
    /// </summary>
    [Fact]
    public void S25_Compose派生源_基类注册回调触发()
    {
        Mapper.Pipeline.Clear();
        try
        {
            var called = false;
            Mapper.Pipeline.AddPostMap<T3Base, T3CDto>((src, _) =>
            {
                called = true;
                Assert.IsType<T3Derived>(src); // 派生实例送达
            });

            // Compose（NonGeneric 入口）：主源 [MapFrom(typeof(T3Derived))] 精确匹配 +
            // 回调注册在基类对（T3Base, T3CDto）——基类链回退后触发
            var dto = Mapper.Compose<T3CDto>(new T3Derived { V = 7 });

            Assert.NotNull(dto);
            Assert.True(called); // 基类链回退后回调触发（原 false）
        }
        finally
        {
            Mapper.Pipeline.Clear();
        }
    }

    /// <summary>
    /// 反向锚点：精确类型注册仍触发（既有行为不变）。
    /// </summary>
    [Fact]
    public void S25_精确类型注册_回调触发不变()
    {
        Mapper.Pipeline.Clear();
        try
        {
            var called = false;
            Mapper.Pipeline.AddPostMap<T3PEntity, T3PDto>((_, _) => called = true);
            Mapper.Map<T3PEntity, T3PDto>(new T3PEntity { Id = 1 });
            Assert.True(called);
        }
        finally
        {
            Mapper.Pipeline.Clear();
        }
    }


    /// <summary>
    /// A4：两阶段注册后 Mapper.TraceEnabled 保持 true——
    /// 第一阶段（IMapper 注册 + TraceEnabled=true Apply）为权威；第二阶段幂等跳过
    /// 注册 + Apply（原：跳过注册但仍 Apply，configure 默认值回抹 false）。
    /// </summary>
    [Fact]
    public void S28_两阶段注册_TraceEnabled保持()
    {
        Mapper.TraceEnabled = false;
        var services = new ServiceCollection();
        try
        {
            services.AddNCMapper(o => o.TraceEnabled = true);
            Assert.True(Mapper.TraceEnabled);

            services.AddNCMapper(o => { /* 第二阶段：configure 未显式设置（默认 false） */ });
            Assert.True(Mapper.TraceEnabled); // 原：被第二阶段 Apply 回抹 false
        }
        finally
        {
            Mapper.TraceEnabled = false;
        }
    }

    /// <summary>
    /// 第一阶段（未注册）配置正常生效；重复调用幂等（单 IMapper 注册）。
    /// </summary>
    [Fact]
    public void S28_首阶段生效_重复注册幂等()
    {
        Mapper.TraceEnabled = false;
        var services = new ServiceCollection();
        try
        {
            services.AddNCMapper(o => o.TraceEnabled = true);
            services.AddNCMapper(); // 无参重载（不 Apply，无影响）
            services.AddNCMapper(o => o.TraceEnabled = true);

            Assert.Single(services, s => s.ServiceType == typeof(IMapper)); // 单注册
            Assert.True(Mapper.TraceEnabled);
        }
        finally
        {
            Mapper.TraceEnabled = false;
        }
    }

    // ============================== 模型 ==============================

    public sealed class T3PEntity
    {
        public int Id { get; set; }
    }

    public sealed class T3PDto
    {
        public int Id { get; set; }
    }

    public class T3Base
    {
        public int V { get; set; }
    }

    public sealed class T3Derived : T3Base
    {
    }

    [MapFrom(typeof(T3Derived))]
    public sealed class T3CDto
    {
        public int V { get; set; }
    }
}