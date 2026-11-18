using NCMapper;
using NCMapper.Attributes;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// 无公共构造器类型映射测试 — 验证只有 private/internal 构造器的类型映射时抛出 MapConfigurationException
    /// </summary>
    [Collection("DeepCopyGlobalState")]
    public class NoPublicConstructorTests
    {
        /// <summary>
        /// 源实体 — 正常公共构造器，提供 Name 和 Age 属性
        /// </summary>
        public class NoPublicCtorSource
        {
            /// <summary>
            /// 名称
            /// </summary>
            public string Name { get; set; } = "";

            /// <summary>
            /// 年龄
            /// </summary>
            public int Age { get; set; }
        }

        /// <summary>
        /// 只有 private 构造器的 DTO — 标注 [MapFrom]，映射时应抛出 MapConfigurationException
        /// </summary>
        [MapFrom(typeof(NoPublicCtorSource))]
        public class PrivateCtorDto
        {
            /// <summary>
            /// 名称
            /// </summary>
            public string Name { get; set; } = "";

            /// <summary>
            /// 年龄
            /// </summary>
            public int Age { get; set; }

            private PrivateCtorDto() { }
        }

        /// <summary>
        /// 只有 internal 构造器的 DTO — 标注 [MapFrom]，映射时应抛出 MapConfigurationException
        /// </summary>
        [MapFrom(typeof(NoPublicCtorSource))]
        public class InternalCtorDto
        {
            /// <summary>
            /// 名称
            /// </summary>
            public string Name { get; set; } = "";

            /// <summary>
            /// 年龄
            /// </summary>
            public int Age { get; set; }

            internal InternalCtorDto() { }
        }

        /// <summary>
        /// 只有 private 构造器的类型通过 [MapFrom] 映射时抛出 MapConfigurationException
        /// </summary>
        [Fact]
        public void Private_Constructor_Throws_MapConfigurationException()
        {
            var source = new NoPublicCtorSource { Name = "Test", Age = 18 };

            Assert.ThrowsAny<MapConfigurationException>(() =>
                Mapper.Map<NoPublicCtorSource, PrivateCtorDto>(source));
        }

        /// <summary>
        /// 只有 internal 构造器的类型通过 [MapFrom] 映射时抛出 MapConfigurationException
        /// </summary>
        [Fact]
        public void Internal_Constructor_Throws_MapConfigurationException()
        {
            var source = new NoPublicCtorSource { Name = "Test", Age = 18 };

            Assert.ThrowsAny<MapConfigurationException>(() =>
                Mapper.Map<NoPublicCtorSource, InternalCtorDto>(source));
        }
    }
}
