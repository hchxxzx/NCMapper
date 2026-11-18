// 反例 Ignore 之后写 Mask。
// 期望（并存期/终态一致）：CS0305——AfterIgnore（Ignore 独唱终态）经继承面可见的对级旧字段级
// Mask<TMask, TProp>(dest[, oi]) 需两个类型参数；单泛型实参调用触发泛型参数数量不符
// （编译期强制"忽略之后不得跟任何映射/变换操作"）。
using NCMapper;

namespace NCMapper.FluentNegativeCases.N3
{
    public class N3Entity { public string Phone { get; set; } = ""; }
    public class N3Dto { public string Phone { get; set; } = ""; }

    [MapperConfig]
    public static class N3Mapper
    {
        public static void Configure(FluentMappingConfig cfg)
            => cfg.For<N3Entity, N3Dto>()
                  .Member(d => d.Phone).Ignore()
                  .Mask<MaskPhone>();           // N3：Ignore 后写 Mask → CS0305
    }
}
