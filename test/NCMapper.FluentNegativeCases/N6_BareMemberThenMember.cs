// 反例裸 Member 直接续下一个 Member。
// 期望（并存期/终态一致）：CS1061——MemberHead 刻意不继承对级（src FluentMappingConfig.cs
// MemberHead 类型注释 :681-684 分层规则）：未完成头声明/配置的字段段不可续链。
using NCMapper;

namespace NCMapper.FluentNegativeCases.N6
{
    public class N6Entity { public string Name { get; set; } = ""; public string Note { get; set; } = ""; }
    public class N6Dto { public string Name { get; set; } = ""; public string Note { get; set; } = ""; }

    [MapperConfig]
    public static class N6Mapper
    {
        public static void Configure(FluentMappingConfig cfg)
            => cfg.For<N6Entity, N6Dto>()
                  .Member(d => d.Name)
                  .Member(d => d.Note);         // N6：裸 Member 续 Member → CS1061
    }
}
