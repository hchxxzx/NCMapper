// 反例 N13：Ignore 终态之后再补 MapTree。
// 期望（实测回填）：CS1061——Ignore 返回的 AfterIgnore 段不承载 MapTree（MapTree 仅限字段头段与配置段）。
using NCMapper;

namespace NCMapper.FluentNegativeCases.N13
{
    public class N13Entity { public string Name { get; set; } = ""; }
    public class N13Dto { public string Name { get; set; } = ""; }

    [MapperConfig]
    public static class N13Mapper
    {
        public static void Configure(FluentMappingConfig cfg)
            => cfg.For<N13Entity, N13Dto>()
                  .Member(d => d.Name)
                  .Ignore()                     // 进入 AfterIgnore 终态
                  .MapTree();                   // N13：终态后补 MapTree → 无该成员
    }
}