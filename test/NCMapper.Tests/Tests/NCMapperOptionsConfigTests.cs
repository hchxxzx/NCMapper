using Xunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// NCMapperOptions 配置测试 — DeepCopyStrategy 枚举应用、AddNCMapper(IConfiguration) 重载绑定
    /// </summary>
    [Collection("DeepCopyGlobalState")]
    public class NCMapperOptionsConfigTests
    {
        /// <summary>
        /// 保存全局状态快照，供测试后还原，避免污染其他测试
        /// </summary>
        private readonly struct GlobalStateSnapshot
        {
            public DeepCopyProviderBase Provider { get; init; }
            public bool ValidationEnabled { get; init; }
            public bool TraceEnabled { get; init; }
            public int MaxRecursionDepth { get; init; }
            public int MaxInlineDepth { get; init; }

            public static GlobalStateSnapshot Capture() => new()
            {
                Provider = DeepCopyProviderBase.Current,
                ValidationEnabled = Mapper.ValidationEnabled,
                TraceEnabled = Mapper.TraceEnabled,
                MaxRecursionDepth = Mapper.MaxRecursionDepth,
                MaxInlineDepth = Mapper.MaxInlineDepth,
            };

            public void Restore()
            {
                DeepCopyProviderBase.Current = Provider;
                Mapper.ValidationEnabled = ValidationEnabled;
                Mapper.TraceEnabled = TraceEnabled;
                Mapper.MaxRecursionDepth = MaxRecursionDepth;
                Mapper.MaxInlineDepth = MaxInlineDepth;
            }
        }

        /// <summary>
        /// DeepCopyStrategy.Fast 应用后 DeepCopyProviderBase.Current 为 FastDeepCopyProvider
        /// </summary>
        [Fact]
        public void Apply_DeepCopyStrategy_Fast_SetsFastProvider()
        {
            var snapshot = GlobalStateSnapshot.Capture();
            try
            {
                var opts = new NCMapperOptions { DeepCopyStrategy = DeepCopyStrategy.Fast };
                opts.Apply();

                Assert.IsType<FastDeepCopyProvider>(DeepCopyProviderBase.Current);
            }
            finally
            {
                snapshot.Restore();
            }
        }

        /// <summary>
        /// DeepCopyStrategy.ExpressionTree 应用后 DeepCopyProviderBase.Current 为 ExpressionTreeDeepCopyProvider
        /// </summary>
        [Fact]
        public void Apply_DeepCopyStrategy_ExpressionTree_SetsExpressionTreeProvider()
        {
            var snapshot = GlobalStateSnapshot.Capture();
            try
            {
                var opts = new NCMapperOptions { DeepCopyStrategy = DeepCopyStrategy.ExpressionTree };
                opts.Apply();

                Assert.IsType<ExpressionTreeDeepCopyProvider>(DeepCopyProviderBase.Current);
            }
            finally
            {
                snapshot.Restore();
            }
        }

        /// <summary>
        /// DeepCopyStrategy.Json 应用后 DeepCopyProviderBase.Current 为 JsonDeepCopyProvider
        /// </summary>
        [Fact]
        public void Apply_DeepCopyStrategy_Json_SetsJsonProvider()
        {
            var snapshot = GlobalStateSnapshot.Capture();
            try
            {
                var opts = new NCMapperOptions { DeepCopyStrategy = DeepCopyStrategy.Json };
                opts.Apply();

                Assert.IsType<JsonDeepCopyProvider>(DeepCopyProviderBase.Current);
            }
            finally
            {
                snapshot.Restore();
            }
        }

        /// <summary>
        /// NCMapperOptions.DeepCopyStrategy 默认值为 Fast
        /// </summary>
        [Fact]
        public void DeepCopyStrategy_Default_IsFast()
        {
            var opts = new NCMapperOptions();
            Assert.Equal(DeepCopyStrategy.Fast, opts.DeepCopyStrategy);
        }

        /// <summary>
        /// AddNCMapper(IConfiguration) 从内存配置绑定标量项与 DeepCopyStrategy 并应用
        /// </summary>
        [Fact]
        public void AddNCMapper_IConfiguration_BindsAndAppliesOptions()
        {
            var snapshot = GlobalStateSnapshot.Capture();
            try
            {
                var config = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ValidationEnabled"] = "false",
                        ["TraceEnabled"] = "true",
                        ["MaxRecursionDepth"] = "128",
                        ["DeepCopyStrategy"] = "ExpressionTree",
                    })
                    .Build();

                var services = new ServiceCollection();
                services.AddNCMapper(config);

                Assert.False(Mapper.ValidationEnabled);
                Assert.True(Mapper.TraceEnabled);
                Assert.Equal(128, Mapper.MaxRecursionDepth);
                Assert.IsType<ExpressionTreeDeepCopyProvider>(DeepCopyProviderBase.Current);
            }
            finally
            {
                snapshot.Restore();
            }
        }

        /// <summary>
        /// AddNCMapper(IConfiguration) 同样幂等注册 IMapper 为 Singleton
        /// </summary>
        [Fact]
        public void AddNCMapper_IConfiguration_Registers_IMapper_As_Singleton()
        {
            var snapshot = GlobalStateSnapshot.Capture();
            try
            {
                var config = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>())
                    .Build();

                var services = new ServiceCollection();
                services.AddNCMapper(config);
                var provider = services.BuildServiceProvider();

                var mapper1 = provider.GetService<IMapper>();
                var mapper2 = provider.GetService<IMapper>();

                Assert.NotNull(mapper1);
                Assert.Same(mapper1, mapper2);
            }
            finally
            {
                snapshot.Restore();
            }
        }

    }
}
