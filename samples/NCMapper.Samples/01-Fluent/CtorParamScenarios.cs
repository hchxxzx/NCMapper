using NCMapper;
using NCMapper.Samples.Helpers;

namespace NCMapper.Samples.Fluent;

/// <summary>
/// 场景 106 — MapCtorParam 显式构造参数绑定（how-to/fluent/terminals.md「MapCtorParam」节，Fluent-only）：
/// 形态 1 源 lambda / 形态 2 源属性名字符串 / record 经既有 .Map / MapList 元素级。
/// </summary>
public static class CtorParamScenarios
{
    /// <summary>
    /// 形态 1（源 lambda）：不可变目标（形参无属性锚点）直喂构造器。
    /// </summary>
    /// <param name="src">源人员实体</param>
    /// <returns>构造器绑定的不可变目标</returns>
    public static CpPersonView Form1Lambda(CpPersonEntity src)
        => Mapper.Map<CpPersonEntity, CpPersonView>(src)!;

    /// <summary>
    /// 形态 2（源属性名字符串）：生成期符号解析。
    /// </summary>
    /// <param name="src">源订单实体</param>
    /// <returns>构造器绑定的不可变目标</returns>
    public static CpOrderView Form2String(CpOrderEntity src)
        => Mapper.Map<CpOrderEntity, CpOrderView>(src)!;

    /// <summary>
    /// record 场景（边界澄清）：既有 .Map 即可喂构造器（匹配键 = 目标属性名）。
    /// </summary>
    /// <param name="src">源人员实体</param>
    /// <returns>位置参数构造的 record DTO</returns>
    public static CpRecordDto RecordViaMap(CpPersonEntity src)
        => Mapper.Map<CpPersonEntity, CpRecordDto>(src)!;

    /// <summary>
    /// MapList 元素级：内联构造器路径同样消费绑定（双点之一）。
    /// </summary>
    /// <param name="srcs">源人员实体列表</param>
    /// <returns>构造器绑定的不可变目标列表</returns>
    public static List<CpPersonView> ListLevel(System.Collections.Generic.IEnumerable<CpPersonEntity> srcs)
        => Mapper.MapList<CpPersonEntity, CpPersonView>(srcs)!;

    /// <summary>
    /// 场景调度 — 顺序执行全部子场景并输出。
    /// </summary>
    public static void Run()
    {
        // 1. 形态 1（源 lambda）：形参无属性锚点 → 显式绑定直喂
        var person = new CpPersonEntity { FullName = "Zhang San", Age = 30 };
        var view = Form1Lambda(person);
        OutputHelper.Dump("106-ctor-param-lambda", person, view);
        if (view.Name != "Zhang San" || view.Age != 30)
            OutputHelper.Error($"106-ctor-param-lambda: ctor param binding failed (Name={view.Name}, Age={view.Age})");

        // 2. 形态 2（源属性名字符串）
        var order = new CpOrderEntity { Id = 7, OrderNo = "ORD-7" };
        var orderView = Form2String(order);
        OutputHelper.Dump("106-ctor-param-string", order, orderView);
        if (orderView.OrderNo != "ORD-7" || orderView.Id != 7)
            OutputHelper.Error($"106-ctor-param-string: ctor param binding failed (OrderNo={orderView.OrderNo}, Id={orderView.Id})");

        // 3. record 场景：既有 .Map 即可（位置参数 = 同名 init 属性，匹配键 = 目标属性名）
        var person2 = new CpPersonEntity { FullName = "Li Si", Age = 28 };
        var rec = RecordViaMap(person2);
        OutputHelper.Dump("106-record-via-map", person2, rec);
        if (rec.Name != "Li Si" || rec.Age != 28)
            OutputHelper.Error($"106-record-via-map: .Map into record ctor failed (Name={rec.Name}, Age={rec.Age})");

        // 4. MapList 元素级（内联构造器路径消费绑定）
        var people = new[]
        {
            new CpPersonEntity { FullName = "A-One", Age = 21 },
            new CpPersonEntity { FullName = "B-Two", Age = 22 },
        };
        var views = ListLevel(people);
        OutputHelper.Dump("106-ctor-param-list", $"Count: {people.Length}", views);
        if (views.Count != 2 || views[0].Name != "A-One" || views[1].Name != "B-Two" || views[1].Age != 22)
            OutputHelper.Error($"106-ctor-param-list: element-level binding failed ([0]={views[0].Name}, [1]={views[1].Name}/{views[1].Age})");
    }
}
