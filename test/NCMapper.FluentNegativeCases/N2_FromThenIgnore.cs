// 反例 From 之后写 Ignore。
// 期望（并存期/终态一致）：CS1501——MemberConfig 经继承面可见的对级旧字段级
// Ignore<TProp>(dest[, Direction]) 均需 ≥1 实参，零参调用无适用重载（编译期强制
// "Ignore 为头声明、From 定源后不可再独唱忽略"）。
using NCMapper;

namespace NCMapper.FluentNegativeCases.N2
{
    public class N2Entity { public string Name { get; set; } = ""; }
    public class N2Dto { public string Name { get; set; } = ""; }

    [MapperConfig]
    public static class N2Mapper
    {
        public static void Configure(FluentMappingConfig cfg)
            => cfg.For<N2Entity, N2Dto>()
                  .Member(d => d.Name).From(s => s.Name)
                  .Ignore();                    // N2：From 后写 Ignore → CS1501
    }
}
