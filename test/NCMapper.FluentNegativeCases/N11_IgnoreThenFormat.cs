// 反例 N11：Ignore 终态之后再补 Format。
// 期望（实测回填）：CS1061——Ignore 返回的 AfterIgnore 段不承载 Format（Format 仅限字段头段与配置段）。
using NCMapper;

namespace NCMapper.FluentNegativeCases.N11
{
    public class N11Entity { public string Name { get; set; } = ""; }
    public class N11Dto { public string Name { get; set; } = ""; }

    [MapperConfig]
    public static class N11Mapper
    {
        public static void Configure(FluentMappingConfig cfg)
            => cfg.For<N11Entity, N11Dto>()
                  .Member(d => d.Name)
                  .Ignore()                     // 进入 AfterIgnore 终态
                  .Format("yyyy-MM-dd");        // N11：终态后补 Format → 无该成员
    }
}