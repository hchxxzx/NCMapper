// 反例 N14：漏写 For 类型对声明，直接在入口配置根上调用 NoAutoMatch。
// 期望（实测回填）：CS1061——入口根仅承载 For，NoAutoMatch 是对级配置器成员。
using NCMapper;

namespace NCMapper.FluentNegativeCases.N14
{
    [MapperConfig]
    public static class N14Mapper
    {
        public static void Configure(FluentMappingConfig cfg)
            => cfg.NoAutoMatch();               // N14：入口根直接调 NoAutoMatch → 无该成员
    }
}