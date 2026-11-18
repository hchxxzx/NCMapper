// 反例 N15：构造器形参绑定漏写第二个实参（源属性）。
// 期望（实测回填）：CS1501——两个 MapCtorParam 重载均需 2 实参（源 lambda 或源属性名），无单参重载。
using NCMapper;

namespace NCMapper.FluentNegativeCases.N15
{
    public class N15Entity { public int Id { get; set; } }

    [MapperConfig]
    public static class N15Mapper
    {
        public static void Configure(FluentMappingConfig cfg)
            => cfg.For<N15Entity, N15Dto>()
                  .MapCtorParam("id");          // N15：缺源实参 → 无匹配重载
    }

    public class N15Dto
    {
        public int Id { get; }
        public N15Dto(int id) { Id = id; }
    }
}