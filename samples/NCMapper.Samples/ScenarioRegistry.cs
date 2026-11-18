namespace NCMapper.Samples;

/// <summary>
/// 场景注册表 —— 域→场景二级静态注册。
/// 编译期固定，不使用反射扫描（AOT 友好，出错即编译错）。
/// 新增场景：在对应域目录新建/扩展场景类后在此登记一行，入口代码零改动。
/// 非法场景号判定 = Find 未命中（含区间内未注册号），禁止区间上下界比较。
/// </summary>
public sealed record Scenario(int Id, string Name, Action Run);

public sealed record SampleDomain(string Name, IReadOnlyList<Scenario> Scenarios);

public static class ScenarioRegistry
{
    public static readonly IReadOnlyList<SampleDomain> Domains =
    [
        new("01-Fluent",
        [
            new(101, "Fluent Basics", Fluent.FluentBasicsScenarios.Run),
            new(102, "Fluent Convert", Fluent.FluentConvertScenarios.Run),
            new(103, "Fluent FromSource", Fluent.FluentFromSourceScenarios.Run),
            new(104, "Fluent Hooks", Fluent.FluentHooksScenarios.Run),
            new(105, "MapWhen", Fluent.MapWhenScenarios.Run),
            new(106, "MapCtorParam", Fluent.CtorParamScenarios.Run),
            new(107, "MapSubclass", Fluent.SubclassScenarios.Run),
            new(108, "Inline Lambda Transcription", Fluent.InlineLambdaScenarios.Run),
            new(109, "Compose Runtime Metadata (RuntimeComposeMapping)", Fluent.RuntimeComposeScenarios.Run),
        ]),
        new("02-Declarative",
        [
            new(201, "Basic Mapping (Attribute)", Declarative.BasicScenarios.Run),
            new(202, "MapFormat (Attribute & Fluent)", MapFormat.MapFormatScenarios.Run),
        ]),
        new("03-Compose",
        [
            new(301, "Multi-Source Compose", Compose.ComposeScenarios.Run),
        ]),
        new("04-DeepCopy",
        [
            new(401, "Deep Copy", DeepCopy.DeepCopyScenarios.Run),
        ]),
        new("05-Orm",
        [
            new(501, "ORM Projection (FreeSql/EFCore)", Orm.ProjectionScenarios.Run),
            new(502, "Dapper Integration", Orm.DapperScenarios.Run),
            new(503, "FreeSql Multi-Table & Advanced", Orm.FreeSqlMultiTableScenarios.Run),
        ]),
        new("06-Infrastructure",
        [
            new(601, "Pipeline", Infrastructure.PipelineScenarios.Run),
            new(602, "Validation & Diagnostics", Infrastructure.ValidationScenarios.Run),
            new(603, "DI Integration", Infrastructure.DIScenarios.Run),
            new(604, "Performance", Infrastructure.PerformanceScenarios.Run),
            new(605, "Data Masking", Infrastructure.MaskScenarios.Run),
            new(606, "Global Config", Infrastructure.GlobalConfigScenarios.Run),
            new(607, "Mapping Trace", Infrastructure.TraceScenarios.Run),
        ]),
    ];

    /// <summary>按场景号查找；未命中返回 null（调用方据此置退出码 2）。</summary>
    public static Scenario? Find(int id)
    {
        foreach (var domain in Domains)
            foreach (var scenario in domain.Scenarios)
                if (scenario.Id == id)
                    return scenario;
        return null;
    }
}
