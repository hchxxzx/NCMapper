// 反例 N17：钩子委托实参元数不符（三参 lambda 对双参 Action）。
// 期望（实测回填）：CS1593——AfterMapping 期望 Action<TSource,TTarget>，不接受三参委托。
using NCMapper;

namespace NCMapper.FluentNegativeCases.N17
{
    public class N17Entity { public int Id { get; set; } }
    public class N17Dto { public int Id { get; set; } }

    [MapperConfig]
    public static class N17Mapper
    {
        public static void Configure(FluentMappingConfig cfg)
            => cfg.For<N17Entity, N17Dto>()
                  .AfterMapping((src, dst, extra) => { });   // N17：三参 lambda → 委托签名不匹配
    }
}