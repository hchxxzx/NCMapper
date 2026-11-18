using NCMapper.SourceGen.Tests.Models;
using NCMapper.Generated;
using Xunit;

namespace NCMapper.SourceGen.Tests
{
    /// <summary>
    /// 扁平化路径段数专项测试 — 验证源生成器对不同深度嵌套路径的扁平化支持
    /// <para>P09: 4 段路径正确映射，P10: 5 段路径正确映射，P11: 6 段回退运行时</para>
    /// </summary>
    public class FlattenDepthTests
    {
        /// <summary>
        /// P09: 4 段路径 Level1.Level2.Level3.Level4.Value 正确映射
        /// <para>扁平化属性名 Level2Level3Level4Value（4 个部分）由源生成器自动拆解匹配</para>
        /// </summary>
        [Fact]
        public void Flatten_4_Segments_Maps_Correctly()
        {
            var source = new SgFlattenLevel1
            {
                Level2 = new SgFlattenLevel2
                {
                    Level3 = new SgFlattenLevel3
                    {
                        Level4 = new SgFlattenLevel4 { Value = 42 }
                    }
                }
            };

            var dto = GeneratedMapper.Map_SgFlattenLevel1_To_SgFlatten4SegmentsDto(source)!;

            Assert.Equal(42, dto.Level2Level3Level4Value);
        }

        /// <summary>
        /// P09 补充：4 段路径中间节点为 null 时安全返回默认值（?. 安全访问）
        /// </summary>
        [Fact]
        public void Flatten_4_Segments_Null_MidLevel_Returns_Default()
        {
            var source = new SgFlattenLevel1 { Level2 = null };

            var dto = GeneratedMapper.Map_SgFlattenLevel1_To_SgFlatten4SegmentsDto(source)!;

            Assert.Equal(0, dto.Level2Level3Level4Value);
        }

        /// <summary>
        /// P10: 5 段路径 Level1.Level2.Level3.Level4.Level5.Value 正确映射
        /// <para>扁平化属性名 Level2Level3Level4Level5Value（5 个部分）由源生成器自动拆解匹配</para>
        /// </summary>
        [Fact]
        public void Flatten_5_Segments_Maps_Correctly()
        {
            var source = new SgFlattenLevel1
            {
                Level2 = new SgFlattenLevel2
                {
                    Level3 = new SgFlattenLevel3
                    {
                        Level4 = new SgFlattenLevel4
                        {
                            Level5 = new SgFlattenLevel5 { Value = 99 }
                        }
                    }
                }
            };

            var dto = GeneratedMapper.Map_SgFlattenLevel1_To_SgFlatten5SegmentsDto(source)!;

            Assert.Equal(99, dto.Level2Level3Level4Level5Value);
        }

        /// <summary>
        /// P10 补充：5 段路径中间节点为 null 时安全返回默认值
        /// </summary>
        [Fact]
        public void Flatten_5_Segments_Null_MidLevel_Returns_Default()
        {
            var source = new SgFlattenLevel1
            {
                Level2 = new SgFlattenLevel2 { Level3 = null }
            };

            var dto = GeneratedMapper.Map_SgFlattenLevel1_To_SgFlatten5SegmentsDto(source)!;

            Assert.Equal(0, dto.Level2Level3Level4Level5Value);
        }

        /// <summary>
        /// P11: 超过 5 段（6 段）路径回退到运行时 — SG 无法匹配，属性保持默认值
        /// <para>6 段路径名 Level2Level3Level4Level5ValueExtra 超过 SG 扁平化限制（5 段），
        /// 运行时路径不支持自动扁平化，属性不被映射</para>
        /// </summary>
        [Fact]
        public void Flatten_6_Segments_Falls_Back_To_Runtime_Unmapped()
        {
            var source = new SgFlattenLevel1
            {
                Level2 = new SgFlattenLevel2
                {
                    Level3 = new SgFlattenLevel3
                    {
                        Level4 = new SgFlattenLevel4
                        {
                            Level5 = new SgFlattenLevel5 { Value = 77 }
                        }
                    }
                }
            };

            var dto = Mapper.Map<SgFlattenLevel1, SgFlatten6SegmentsDto>(source)!;

            // SG 不支持 6 段扁平化，运行时也不做扁平化 → 属性保持默认值
            Assert.Equal(-999, dto.Level2Level3Level4Level5ValueExtra);
        }
    }
}
