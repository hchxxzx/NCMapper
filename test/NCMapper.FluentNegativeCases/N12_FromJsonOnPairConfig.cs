// 反例 N12：漏写 Member 锚点，直接在对级配置器上调用 FromJson。
// 期望（实测回填）：CS1061——FromJson 仅限字段段（MemberHead / MemberConfig），对级配置器无此成员。
using NCMapper;

namespace NCMapper.FluentNegativeCases.N12
{
    public class N12Entity { public string Name { get; set; } = ""; }
    public class N12Dto { public string Name { get; set; } = ""; }

    [MapperConfig]
    public static class N12Mapper
    {
        public static void Configure(FluentMappingConfig cfg)
            => cfg.For<N12Entity, N12Dto>()
                  .FromJson();                  // N12：对级配置器直接调 FromJson → 无该成员
    }
}