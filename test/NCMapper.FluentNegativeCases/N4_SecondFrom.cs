// 反例写第二个来源（重复 From）。
// 期望（并存期/终态一致）：CS1061——头声明唯一：From 返回 MemberConfig（配置段），
// 其上（含对级继承面）无第二个 From 签名。
using NCMapper;

namespace NCMapper.FluentNegativeCases.N4
{
    public class N4Entity { public string Name { get; set; } = ""; public string Title { get; set; } = ""; }
    public class N4Dto { public string Name { get; set; } = ""; }

    [MapperConfig]
    public static class N4Mapper
    {
        public static void Configure(FluentMappingConfig cfg)
            => cfg.For<N4Entity, N4Dto>()
                  .Member(d => d.Name).From(s => s.Name)
                  .From(s => s.Title);          // N4：第二个 From → CS1061
    }
}
