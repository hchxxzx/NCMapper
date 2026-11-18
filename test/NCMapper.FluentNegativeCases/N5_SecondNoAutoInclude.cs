// 反例写第二个 NoAutoInclude（下）。
// 并存期期望：**零错误**（本例为 7/8 拦截的唯一例外——AfterIgnore.NoAutoInclude() 写完即回
// 对级 FluentTypeConfig，第二个 NoAutoInclude 解析到并存期仍在位的对级旧名
// FluentTypeConfig.NoAutoInclude()，语义混合——src FluentMappingConfig.cs AfterIgnore 类型注释
// 1205-1207 并存期行为差异登记；终态 = 删除对级旧名 + 卸 new 后本例被 CS1061 拦截
// 脚本 --mode final 翻转断言）。
using NCMapper;

namespace NCMapper.FluentNegativeCases.N5
{
    public class N5Entity { public string Name { get; set; } = ""; }
    public class N5Dto { public string Name { get; set; } = ""; }

    [MapperConfig]
    public static class N5Mapper
    {
        public static void Configure(FluentMappingConfig cfg)
            => cfg.For<N5Entity, N5Dto>()
                  .Member(d => d.Name).Ignore().NoAutoInclude()
                  .NoAutoInclude();             // N5：第二个 NoAutoInclude（并存期零错误 / 终态 CS1061）
    }
}
