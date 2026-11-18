// 反例 N16：子类型对登记只写一个类型实参。
// 期望（实测回填）：CS0305——MapSubclass 需 2 个类型实参（子类源 + 子类目标）。
using NCMapper;

namespace NCMapper.FluentNegativeCases.N16
{
    public class N16Animal { public string Name { get; set; } = ""; }
    public class N16Dog : N16Animal { public string Breed { get; set; } = ""; }
    public class N16AnimalDto { public string Name { get; set; } = ""; }
    public class N16DogDto : N16AnimalDto { public string Breed { get; set; } = ""; }

    [MapperConfig]
    public static class N16Mapper
    {
        public static void Configure(FluentMappingConfig cfg)
            => cfg.For<N16Animal, N16AnimalDto>()
                  .MapSubclass<N16Dog>();       // N16：缺目标类型实参 → 泛型元数不符
    }
}