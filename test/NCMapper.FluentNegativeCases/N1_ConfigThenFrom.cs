// 反例配置之后再补 From。
// 期望（并存期/终态一致）：CS1061——From 头声明仅限 MemberHead 段且唯一；
// MemberConfig（配置段返回类型）不承载 From，对级 FluentTypeConfig 亦无 From 签名。
using NCMapper;

namespace NCMapper.FluentNegativeCases.N1
{
    public class N1Entity { public string Name { get; set; } = ""; }
    public class N1Dto { public string Name { get; set; } = ""; }

    [MapperConfig]
    public static class N1Mapper
    {
        public static void Configure(FluentMappingConfig cfg)
            => cfg.For<N1Entity, N1Dto>()
                  .Member(d => d.Name).From(s => s.Name)
                  .MapWhen(v => v.Length > 0)   // 进入配置段（合法）
                  .From(s => s.Name);           // N1：配置段后补 From → CS1061
    }
}
