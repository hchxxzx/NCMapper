// ============================================================================
// T64 / B5-5 Batch-5 — 转换语义与判据防御红测
//（A1-08 枚举转换值域 / A2-05 树构建告警 / A2-06 嵌套深度硬编码 / A2-07 NCMapper 前缀误匹配）
//
// 裁决（轻裁决）：Q4=①空串→枚举改抛 MapException（对齐 :117-130 显式抛口径）+ ToObject
// 补 IsDefined 校验（Flags 组合值白名单豁免）；Q5=告警 + 保持现有挂载语义不变。
//
// 缺陷承载（审计 20260917-001 -B5-5）：
//   A1-08  TypeConvertHelper.cs:111 空串静默 default / :117/:123 Enum.ToObject 不查值域
//（99 → 未定义值静默产出）；
//   A2-05  TreeBuilder.cs:50 重复 Id 覆盖无 Trace、:57-74 自环/互指静默消失；
//   A2-06  DegradationDetectorBase.cs:273 硬编码 depth>8（抽象属性 :25 EFCore=8/FreeSql=3）；
//   A2-07  DegradationDetectorBase.cs:219/:240/:348 StartsWith("NCMapper") 把 NCMapperExtend
//          等同形命名空间误判为未声明 NCMapper 特性 → 误触发降级。
// ============================================================================

using System.IO;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.FreeSql;
using NCMapper.Internal;
using Xunit;

namespace NCMapper.Tests.Tests;

[Collection("GlobalConfigState")]
public class T64_T005_ConversionAndDetectorTests
{
    // ============================== 承载类型 ==============================

    private enum T5Mode { A = 1, B = 2 }

    [Flags]
    private enum T5Flags { R = 1, G = 2, B = 4 }

    private class T5NodeDto
    {
        public int Id { get; set; }
        public int? ParentId { get; set; }
        public List<T5NodeDto> Children { get; set; } = new();
    }

    // A2-06：6 级嵌套链（[MapFrom] 反射回退注册主源，嵌套扫描方可下降）——
    // 触发源置于 L6：其属性扫描发生在 depth=4 调用内（FreeSql 3 截止拦截 / EFCore 8 放行）
    private class T5A6Src { public T5L2Src? A { get; set; } }
    private class T5L2Src { public T5L3Src? A { get; set; } }
    private class T5L3Src { public T5L4Src? A { get; set; } }
    private class T5L4Src { public T5L5Src? A { get; set; } }
    private class T5L5Src { public T5L6Src? A { get; set; } }
    private class T5L6Src { public string? Phone { get; set; } }

    private class T5L1Dto { public T5L2Dto? A { get; set; } }

    [MapFrom(typeof(T5L2Src))]
    private class T5L2Dto { public T5L3Dto? A { get; set; } }

    [MapFrom(typeof(T5L3Src))]
    private class T5L3Dto { public T5L4Dto? A { get; set; } }

    [MapFrom(typeof(T5L4Src))]
    private class T5L4Dto { public T5L5Dto? A { get; set; } }

    [MapFrom(typeof(T5L5Src))]
    private class T5L5Dto { public T5L6Dto? A { get; set; } }

    [MapFrom(typeof(T5L6Src))]
    private class T5L6Dto
    {
        [Mask(typeof(MaskPhone))]
        public string? Phone { get; set; }   // 深层触发源：仅 depth ≤ 上限时被扫描
    }

    // A2-07：同形命名空间自定义特性（StartsWith("NCMapper") 误命中，"NCMapper." 不命中）
    private class T5A7Src { public string? Name { get; set; } }

    [MapFrom(typeof(T5A7Src))]
    private class T5A7Dto
    {
        [NCMapperExtend.ExtendStyle]
        public string? Name { get; set; }
    }

    // ============================== A1-08 ==============================

    /// <summary>
    /// A1-08 红测（Q4=改抛）：空串 → 枚举——现状 :111 静默 return default（红：不抛）；
    /// 修复后落入 Enum.Parse 抛 ArgumentException → 既有 catch 统一转 MapException
    ///（117-130 显式抛口径对齐）。
    /// </summary>
    [Fact]
    public void A1_08_空串转枚举_不再静默default改抛MapException()
        => Assert.Throws<MapException>(() => TypeConvertHelper.ConvertValue<string, T5Mode>(""));

    /// <summary>
    /// A1-08 红测：未定义数值 → 枚举——现状 Enum.ToObject 不查值域（99 静默产出未定义值，
    /// 红：不抛）；修复后补 IsDefined 校验（Flags 组合值白名单豁免）抛 MapException。
    /// </summary>
    [Fact]
    public void A1_08_未定义数值转枚举_抛MapException()
    {
        Assert.Throws<MapException>(() => TypeConvertHelper.ConvertValue<int, T5Mode>(99));
        // 浮点路径同口径（120-123 先转 long 再 ToObject）
        Assert.Throws<MapException>(() => TypeConvertHelper.ConvertValue<double, T5Mode>(99d));
    }

    /// <summary>
    /// A1-08 行为锁定：Flags 组合值白名单放行（3 = R|G 未逐一定义但合法）+ 已定义单值放行
    ///——防值域校验过抛。
    /// </summary>
    [Fact]
    public void A1_08_Flags组合值与已定义值_白名单放行()
    {
        Assert.Equal((T5Flags)3, TypeConvertHelper.ConvertValue<int, T5Flags>(3));
        Assert.Equal(T5Mode.A, TypeConvertHelper.ConvertValue<int, T5Mode>(1));
        Assert.Equal(T5Mode.B, TypeConvertHelper.ConvertValue<string, T5Mode>("b"));
    }

    // ============================== A2-05 ==============================

    /// <summary>
    /// A2-05 红测（Q5=告警+挂载语义不变）：重复 Id 覆盖——现状 :50 静默覆盖（红：无 Trace）；
    /// 修复后 MapTracer 结构化告警可观测。挂载语义锁定：后项覆盖前项索引（既有行为）。
    /// </summary>
    [Fact]
    public void A2_05_重复Id覆盖_告警可观测且挂载语义不变()
    {
        var sw = new StringWriter();
        var oldEnabled = MapTracer.GloballyEnabled;
        var oldOutput = MapTracer.GlobalOutput;
        MapTracer.GloballyEnabled = true;
        MapTracer.GlobalOutput = sw;
        try
        {
            var flat = new List<T5NodeDto>
            {
                new() { Id = 1, ParentId = null },
                new() { Id = 2, ParentId = 1 },
                new() { Id = 1, ParentId = null },   // 重复 Id（后者覆盖索引）
                new() { Id = 3, ParentId = 1 },
            };

            var roots = TreeBuilder.Build(flat, TreeConfig.Default);

            // 挂载语义不变：Id=1 两个实例均不出现在 roots（后者的索引形态），Id=1 的子项挂到后注册的 1 号
            Assert.Equal(2, roots.Count);
            // 告警可观测（现状红：静默覆盖零留痕）
            Assert.Contains("重复", sw.ToString());
            Assert.Contains("1", sw.ToString());
        }
        finally
        {
            MapTracer.GloballyEnabled = oldEnabled;
            MapTracer.GlobalOutput = oldOutput;
        }
    }

    /// <summary>
    /// A2-05 红测（Q5）：自环（Id==ParentId）与互指（A↔B）——现状静默消失于 roots（红：无
    /// Trace）；修复后显式告警。挂载语义保持：自环节点自挂载、互指对偶互挂（均不出 roots）。
    /// </summary>
    [Fact]
    public void A2_05_自环与互指_告警可观测且挂载语义不变()
    {
        var sw = new StringWriter();
        var oldEnabled = MapTracer.GloballyEnabled;
        var oldOutput = MapTracer.GlobalOutput;
        MapTracer.GloballyEnabled = true;
        MapTracer.GlobalOutput = sw;
        try
        {
            var flat = new List<T5NodeDto>
            {
                new() { Id = 1, ParentId = 1 },   // 自环
                new() { Id = 2, ParentId = 3 },   // 互指 A
                new() { Id = 3, ParentId = 2 },   // 互指 B
            };

            var roots = TreeBuilder.Build(flat, TreeConfig.Default);

            // 挂载语义不变：自环/互指均不出 roots（静默消失形态保持——Q5 防下游破坏）
            Assert.Empty(roots);
            // 告警可观测（现状红）
            Assert.Contains("自环", sw.ToString());
            Assert.Contains("互指", sw.ToString());
        }
        finally
        {
            MapTracer.GloballyEnabled = oldEnabled;
            MapTracer.GlobalOutput = oldOutput;
        }
    }

    // ============================== A2-06 ==============================

    /// <summary>
    /// A2-06 行为锁定（红测不可构造——登记）：基座 NeedsNestedDegradation 截止口径由
    /// 硬编码 8 改对齐抽象上限（owner?.MaxNestingDepth，EFCore=8 等值/FreeSql=3 收紧）——但
    /// FreeSql 自有 GetMaxNestingDepth 对注册深层链（&gt;3）先行触发降级（RunExtraChecks
    /// 先于嵌套扫描），基座截断经 ORM 通道不可分离观测（b25v2 门禁实测：reason 为"嵌套投影
    /// 深度 5 超过 FreeSql 上限 3"）。故本条为一致性加固 + 行为锁定测试：FreeSql 深层链仍
    /// 降级（上限 3 口径报告）+ EFCore（上限 8）仍扫描到 L6 的 [Mask]。
    /// </summary>
    [Fact]
    public void A2_06_嵌套深度上限_深层链降级行为锁定()
    {
        // FreeSql：6 级注册链（>3）→ 降级，reason 按自身上限 3 报告（A2-06 修复不得破坏该口径）
        Assert.True(FreeSqlDegradationDetector.NeedsDegradation(
            typeof(T5A6Src), typeof(T5L1Dto), out var reasonFs),
            "FreeSql 深层嵌套链应降级");
        Assert.Contains("3", reasonFs);

        // EFCore 对照：上限 8 → L6（depth=4 扫描）的 [Mask] 仍被检出 → 降级（行为锁定）
        Assert.True(NCMapper.EFCore.EfCoreDegradationDetector.NeedsDegradation(
            typeof(T5A6Src), typeof(T5L1Dto), out _),
            "EFCore 对照：MaxNestingDepth=8 应扫描到 L6（行为锁定）");
    }

    // ============================== A2-07 ==============================

    /// <summary>
    /// A2-07 红测：NCMapperExtend 同形命名空间特性不再误触发"未声明的 NCMapper 特性"降级
    ///——现状 StartsWith("NCMapper") 误命中（红：误降级）；修复后精确前缀 "NCMapper."。
    /// </summary>
    [Fact]
    public void A2_07_NCMapperExtend命名空间特性_不误触发降级()
        => Assert.False(FreeSqlDegradationDetector.NeedsDegradation(
            typeof(T5A7Src), typeof(T5A7Dto), out var reason),
            $"NCMapperExtend 命名空间特性不应触发降级（实际触发：{reason}）");
}
