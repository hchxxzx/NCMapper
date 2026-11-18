// d 反例 Convert **旧形态 (dest, converter) 双参**调用。
// 承载意义断代清扫后旧签名已无编译来源 ⇒ 三分派点的旧分支对可编译代码不可达
// ——G2 分派残留清扫的语义前提。本例锁定"旧形态不可编译"这一前提本身：
// 若旧重载被恢复，本例转零错误 → 反例门红（防"并存期分派判据"复活）。
using NCMapper;

namespace NCMapper.FluentNegativeCases.N9
{
    public class N9Entity { public string Tag { get; set; } = ""; }
    public class N9Dto { public string Tag { get; set; } = ""; }
    public static class N9Conv { public static string ToText(string v) => "t:" + v; }

    [MapperConfig]
    public static class N9Mapper
    {
        public static void Configure(FluentMappingConfig cfg)
            => cfg.For<N9Entity, N9Dto>()
                  .Member(d => d.Tag)
                  .Convert(d => d.Tag, N9Conv.ToText);   // N9：旧形态 (dest, converter) → 编译失败
    }
}
