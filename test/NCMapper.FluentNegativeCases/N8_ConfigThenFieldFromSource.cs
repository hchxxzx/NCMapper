// 反例配置之后用字段版 FromSource 速记。
// 期望（并存期/终态一致）：CS1061——FromSource 速记（单 lambda 选源字段）仅限 MemberHead 段；
// MemberConfig 经继承面可见的对级 FromSource<TSubSource> 两重载为「分组块 Action<FluentTypeConfig<…>>」
// 与「双 lambda (dest, src)」形态：单 lambda 实参对块版解析时 lambda 体 s.Name 在
// FluentTypeConfig 接收者上不存在成员 → CS1061（"同名不同物"防呆——Spike 结论 6/7）。
using NCMapper;

namespace NCMapper.FluentNegativeCases.N8
{
    public class N8Entity { public string Name { get; set; } = ""; }
    public class N8Sub { public string Alias { get; set; } = ""; }
    public class N8Dto { public string Name { get; set; } = ""; public string Alias { get; set; } = ""; }

    [MapperConfig]
    public static class N8Mapper
    {
        public static void Configure(FluentMappingConfig cfg)
            => cfg.For<N8Entity, N8Dto>()
                  .Member(d => d.Name).From(s => s.Name)
                  .FromSource<N8Sub>(s => s.Alias); // N8：配置段后用字段版速记 → CS1061
    }
}
