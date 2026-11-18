// 反例 N18：漏写 For 类型对声明，直接在入口配置根上调用 BeforeMapping。
// 期望（实测回填）：CS1061——入口根仅承载 For，BeforeMapping 是对级配置器成员。
using NCMapper;

namespace NCMapper.FluentNegativeCases.N18
{
    public class N18Entity { public int Id { get; set; } }
    public class N18Dto { public int Id { get; set; } }

    [MapperConfig]
    public static class N18Mapper
    {
        public static void Configure(FluentMappingConfig cfg)
            => cfg.BeforeMapping(Guard);        // N18：入口根直接调 BeforeMapping → 无该成员

        public static void Guard(N18Entity src, N18Dto dst) { }
    }
}