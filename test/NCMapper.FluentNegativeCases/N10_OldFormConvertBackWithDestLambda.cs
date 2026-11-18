// d 反例 ConvertBack **旧形态 (dest, converter) 双参**调用。
// 与 N9 同族（另一分派点 ProcessChainConvertBack）：旧签名断代后不可编译 ⇒ 旧分支死代码。
using NCMapper;

namespace NCMapper.FluentNegativeCases.N10
{
    public class N10Entity { public string Tag { get; set; } = ""; }
    public class N10Dto { public string Tag { get; set; } = ""; }
    public static class N10Conv { public static string ToText(string v) => "b:" + v; }

    [MapperConfig]
    public static class N10Mapper
    {
        public static void Configure(FluentMappingConfig cfg)
            => cfg.For<N10Entity, N10Dto>()
                  .Member(d => d.Tag)
                  .ConvertBack(d => d.Tag, N10Conv.ToText);   // N10：旧形态 (dest, converter) → 编译失败
    }
}
