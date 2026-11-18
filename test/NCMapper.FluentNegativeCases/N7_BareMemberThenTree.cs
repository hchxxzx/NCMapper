// 反例裸 Member 直接接对级方法（Tree）。
// 期望（并存期/终态一致）：CS1061——MemberHead 不继承对级（:681-684 分层规则）：
// 未完成声明/配置的字段段不可接对级方法。
using NCMapper;

namespace NCMapper.FluentNegativeCases.N7
{
    public class N7Entity { public string Name { get; set; } = ""; }
    public class N7Dto { public string Name { get; set; } = ""; }

    [MapperConfig]
    public static class N7Mapper
    {
        public static void Configure(FluentMappingConfig cfg)
            => cfg.For<N7Entity, N7Dto>()
                  .Member(d => d.Name)
                  .Tree();                      // N7：裸 Member 接 Tree → CS1061
    }
}
