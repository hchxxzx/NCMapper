using System;
using System.Globalization;
using NCMapper;
using NCMapper.Generated;
using Xunit;

namespace NCMapper.SourceGen.Tests.Fluent;

/// <summary>
/// 运行时矩阵：Convert 执行端（生成代码真实调用——独立验证；
/// 更名 ConvertUsing→Convert）。
/// <para>本文件随测试工程编译即被源生成器处理（测试工程引用 SourceGen 为 Analyzer），
/// 生成的 GeneratedMapper 方法编译进测试程序集供运行时断言（FluentConvertBackRuntimeTests 同款布局）。</para>
/// <para>覆盖：T0/T1/T2/T3 四形态正向实参（整源传入）、T1Guarded null 短路（CallCount 拦截）、
/// 显式 Map 附着源、扁平化源、Convert × ConvertBack 共存双向调用。</para>
/// <para><b>Batch-3 执行期发现（本文件首次真实编译时实证）</b>：T1/T3 重载
/// （<c>Func&lt;TSrcProp, TTargetProp&gt;</c> / <c>Func&lt;TSource, TSrcProp, TTargetProp&gt;</c>）
/// 方法组直用形态 CS1503——C# 类型推断不从方法组推断委托输入位泛参（TSrcProp 无推断位）；
/// T2 经新增类级固定入参重载 <c>Func&lt;TSource, TTargetProp&gt;</c> 自然可用（M(TSource) 无歧义绑定）。
/// 故本文件：T1/T3/Guarded/扁平化 用显式类型参（<c>Convert&lt;int, string&gt;(...)</c>），
/// T0/T2 自然形态；Map 附着源沿用同型源（string→string——历史沿承，Map 双泛型后跨型亦可用）。</para>
/// </summary>

// ── Pair 1：T1 字段值 ────────────────────────────────────────────
/// <summary>运行时模型源（T1 字段值形态）</summary>
public class FcuRt1Entity
{
    /// <summary>年龄（正向经 FcuRt1Conv.ToBand 分段）</summary>
    public int Age { get; set; }
}

/// <summary>运行时模型目标（T1 字段值形态）</summary>
public class FcuRt1Dto
{
    /// <summary>年龄段</summary>
    public string Age { get; set; } = "";
}

/// <summary>运行时转换器（T1 模板 M(TField)）</summary>
public static class FcuRt1Conv
{
    /// <summary>年龄分段（运行时断言载体）</summary>
    public static string ToBand(int fieldValue) => fieldValue < 18 ? "minor" : "adult";
}

/// <summary>Fluent 通道入口（运行时）：T1 字段值——同名自动匹配（显式类型参——方法组推断指引）</summary>
[MapperConfig]
public class FcuRt1Mapper
{
    /// <summary>Fluent 链式声明</summary>
    public static void Configure(FluentMappingConfig cfg)
    {
        cfg.For<FcuRt1Entity, FcuRt1Dto>().Member(d => d.Age).Convert<int>(FcuRt1Conv.ToBand);
    }
}

// ── Pair 2：T0 常量 ────────────────────────────────────────────
/// <summary>运行时模型源（T0 常量形态——源侧同名属性满足 F24 可达门，转换器不消费其值）</summary>
public class FcuRt0Entity
{
    /// <summary>戳记（源侧同名可达门——常量写入不读取）</summary>
    public string Stamp { get; set; } = "";
}

/// <summary>运行时模型目标（T0 常量形态）</summary>
public class FcuRt0Dto
{
    /// <summary>戳记（常量写入）</summary>
    public string Stamp { get; set; } = "";
}

/// <summary>运行时转换器（T0 模板 M()）</summary>
public static class FcuRt0Conv
{
    /// <summary>常量产物</summary>
    public const string Fixed = "fixed-rt";

    /// <summary>无参常量</summary>
    public static string Stamp() => Fixed;
}

/// <summary>Fluent 通道入口（运行时）：T0 常量</summary>
[MapperConfig]
public class FcuRt0Mapper
{
    /// <summary>Fluent 链式声明</summary>
    public static void Configure(FluentMappingConfig cfg)
    {
        cfg.For<FcuRt0Entity, FcuRt0Dto>().Member(d => d.Stamp).Convert(FcuRt0Conv.Stamp);
    }
}

// ── Pair 3：T2 整源 ────────────────────────────────────────────
/// <summary>运行时模型源（T2 整源形态——源侧锚点同名属性满足 F24 可达门，转换器消费整源对象）</summary>
public class FcuRt2Entity
{
    /// <summary>城市（整源组合素材）</summary>
    public string City { get; set; } = "";

    /// <summary>街道（整源组合素材）</summary>
    public string Street { get; set; } = "";

    /// <summary>完整地址（源侧同名可达门——整源转换不读取单值）</summary>
    public string FullAddress { get; set; } = "";
}

/// <summary>运行时模型目标（T2 整源形态）</summary>
public class FcuRt2Dto
{
    /// <summary>完整地址（正向由整源组合生成）</summary>
    public string FullAddress { get; set; } = "";
}

/// <summary>运行时转换器（T2 模板 M(TSource)——实参为整个源对象）</summary>
public static class FcuRt2Conv
{
    /// <summary>整源组合（读源多字段——证明整对象实参传入）</summary>
    public static string Build(FcuRt2Entity entity) => entity.City + ", " + entity.Street;
}

/// <summary>Fluent 通道入口（运行时）：T2 整源</summary>
[MapperConfig]
public class FcuRt2Mapper
{
    /// <summary>Fluent 链式声明</summary>
    public static void Configure(FluentMappingConfig cfg)
    {
        cfg.For<FcuRt2Entity, FcuRt2Dto>().Member(d => d.FullAddress).Convert(FcuRt2Conv.Build);
    }
}

// ── Pair 4：T3 组合+字段值 ─────────────────────────────────────
/// <summary>运行时模型源（T3 组合形态）</summary>
public class FcuRt3Entity
{
    /// <summary>区域（组合素材）</summary>
    public string Locale { get; set; } = "";

    /// <summary>编号（正向由整源 + 字段值组合生成）</summary>
    public int Label { get; set; }
}

/// <summary>运行时模型目标（T3 组合形态）</summary>
public class FcuRt3Dto
{
    /// <summary>标签</summary>
    public string Label { get; set; } = "";
}

/// <summary>运行时转换器（T3 模板 M(TSource, TField)）</summary>
public static class FcuRt3Conv
{
    /// <summary>组合（双实参拼接——证明整对象 + 字段值同时传入）</summary>
    public static string Build(FcuRt3Entity entity, int fieldValue) => "v" + fieldValue + "@" + entity.Locale;
}

/// <summary>Fluent 通道入口（运行时）：T3 组合+字段值（显式类型参——方法组推断指引）</summary>
[MapperConfig]
public class FcuRt3Mapper
{
    /// <summary>Fluent 链式声明</summary>
    public static void Configure(FluentMappingConfig cfg)
    {
        cfg.For<FcuRt3Entity, FcuRt3Dto>().Member(d => d.Label).Convert<int>(FcuRt3Conv.Build);
    }
}

// ── Pair 5：T1Guarded Nullable 守卫（null 短路——CallCount 拦截）─────────
/// <summary>运行时模型源（Nullable 守卫形态）</summary>
public class FcuRtGEntity
{
    /// <summary>计数（可空源——null 时转换器不调用）</summary>
    public int? Count { get; set; }
}

/// <summary>运行时模型目标（Nullable 守卫形态）</summary>
public class FcuRtGDto
{
    /// <summary>计数（null 源保持 default）</summary>
    public int Count { get; set; }
}

/// <summary>运行时转换器（T1 模板 + 守卫变体——入参 int 非 Nullable）</summary>
public static class FcuRtGConv
{
    /// <summary>调用计数（守卫短路断言载体）</summary>
    public static int CallCount;

    /// <summary>自增（守卫变体：null 源不进入本方法）</summary>
    public static int Plus(int fieldValue)
    {
        CallCount++;
        return fieldValue + 1;
    }
}

/// <summary>Fluent 通道入口（运行时）：T1Guarded Nullable 守卫（显式类型参——方法组推断指引）</summary>
[MapperConfig]
public class FcuRtGMapper
{
    /// <summary>Fluent 链式声明</summary>
    public static void Configure(FluentMappingConfig cfg)
    {
        cfg.For<FcuRtGEntity, FcuRtGDto>().Member(d => d.Count).Convert<int>(FcuRtGConv.Plus);
    }
}

// ── Pair 6：显式 Map 附着源 ────────────────────────────────────
/// <summary>运行时模型源（附着路径形态）</summary>
public class FcuRtEEntity
{
    /// <summary>姓（显式源——锚点名 FullName 在源侧不存在，附着的唯一源输入）</summary>
    public string LastName { get; set; } = "";
}

/// <summary>运行时模型目标（附着路径形态）</summary>
public class FcuRtEDto
{
    /// <summary>全名（显式 Map 目标锚点）</summary>
    public string FullName { get; set; } = "";
}

/// <summary>运行时转换器（附着路径 T1——string→string）</summary>
public static class FcuRtEConv
{
    /// <summary>大写化（附着源消费断言载体）</summary>
    public static string Upper(string fieldValue) => fieldValue.ToUpperInvariant();
}

/// <summary>Fluent 通道入口（运行时）：显式 Map 附着（转换器三元组附着显式源——同名解析外的字段）</summary>
[MapperConfig]
public class FcuRtEMapper
{
    /// <summary>Fluent 链式声明（Map&lt;TProp&gt; 同类型约束：显式源路径须与锚点同型——string→string）</summary>
    public static void Configure(FluentMappingConfig cfg)
    {
        cfg.For<FcuRtEEntity, FcuRtEDto>()
           .Member(d => d.FullName).From(s => s.LastName)
           .Member(d => d.FullName).Convert<string>(FcuRtEConv.Upper);
    }
}

// ── Pair 7：扁平化源 ────────────────────────────────────────────
/// <summary>运行时模型地址（扁平化中间节点）</summary>
public class FcuRtFAddress
{
    /// <summary>城市（扁平化叶）</summary>
    public string City { get; set; } = "";
}

/// <summary>运行时模型源（扁平化形态）</summary>
public class FcuRtFEntity
{
    /// <summary>地址（正向扁平化源）</summary>
    public FcuRtFAddress Address { get; set; } = new();
}

/// <summary>运行时模型目标（扁平化形态）</summary>
public class FcuRtFDto
{
    /// <summary>城市（扁平属性——转换器消费叶值）</summary>
    public string AddressCity { get; set; } = "";
}

/// <summary>运行时转换器（扁平化叶 T1）</summary>
public static class FcuRtFConv
{
    /// <summary>大写化（叶值消费断言载体）</summary>
    public static string Upper(string fieldValue) => fieldValue.ToUpperInvariant();
}

/// <summary>Fluent 通道入口（运行时）：扁平化源（TryFindFlattenedPath 命中嵌套叶；显式类型参）</summary>
[MapperConfig]
public class FcuRtFMapper
{
    /// <summary>Fluent 链式声明</summary>
    public static void Configure(FluentMappingConfig cfg)
    {
        cfg.For<FcuRtFEntity, FcuRtFDto>().Member(d => d.AddressCity).Convert<string>(FcuRtFConv.Upper);
    }
}

// ── Pair 8：Convert × ConvertBack 共存（双向真实调用）─────────
/// <summary>运行时模型源（共存形态）</summary>
public class FcuRtCBEntity
{
    /// <summary>创建时间（正向 ToText / 反向 Parse）</summary>
    public DateTime Created { get; set; }
}

/// <summary>运行时模型目标（共存形态）</summary>
public class FcuRtCBDto
{
    /// <summary>创建时间（字符串形态）</summary>
    public string Created { get; set; } = "";
}

/// <summary>运行时转换器（共存——正向 T1 + 反向 T1 双方法）</summary>
public static class FcuRtCBConv
{
    /// <summary>正向格式化</summary>
    public static string ToText(DateTime v) => v.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>反向解析</summary>
    public static DateTime Parse(string v) => DateTime.Parse(v, CultureInfo.InvariantCulture);
}

/// <summary>Fluent 通道入口（运行时）：Convert + ConvertBack 同锚点共存（正向显式类型参；反向 T1 自然形态）</summary>
[MapperConfig]
public class FcuRtCBMapper
{
    /// <summary>Fluent 链式声明</summary>
    public static void Configure(FluentMappingConfig cfg)
    {
        cfg.For<FcuRtCBEntity, FcuRtCBDto>()
           .Member(d => d.Created).Convert<DateTime>(FcuRtCBConv.ToText)
           .Member(d => d.Created).ConvertBack(FcuRtCBConv.Parse);
    }
}

/// <summary>Convert 运行时定向测试（生成代码真实调用——执行端独立验证）</summary>
public class FluentConvertRuntimeTests
{
    /// <summary>T1 字段值执行端：正向生成方法真实调用转换器（20 → "adult"）。</summary>
    [Fact]
    public void T1_SameNameSource_ForwardCallsConverter()
    {
        var dto = GeneratedMapper.Map_FcuRt1Entity_To_FcuRt1Dto(new FcuRt1Entity { Age = 20 })!;
        Assert.Equal("adult", dto.Age);
    }

    /// <summary>T0 常量执行端：正向写入常量（M() 无实参——源侧同名属性值被忽略）。</summary>
    [Fact]
    public void T0_Constant_ForwardWritesConstant()
    {
        var dto = GeneratedMapper.Map_FcuRt0Entity_To_FcuRt0Dto(new FcuRt0Entity { Stamp = "ignored" })!;
        Assert.Equal(FcuRt0Conv.Fixed, dto.Stamp);
    }

    /// <summary>T2 整源执行端：正向整源对象传入（读 City+Street 双字段——证明整对象实参）。</summary>
    [Fact]
    public void T2_WholeSource_ForwardPassesWholeEntity()
    {
        var dto = GeneratedMapper.Map_FcuRt2Entity_To_FcuRt2Dto(
            new FcuRt2Entity { City = "SH", Street = "Nanjing Rd" })!;
        Assert.Equal("SH, Nanjing Rd", dto.FullAddress);
    }

    /// <summary>T3 组合执行端：正向整源 + 字段值双实参传入。</summary>
    [Fact]
    public void T3_Combined_ForwardPassesEntityAndField()
    {
        var dto = GeneratedMapper.Map_FcuRt3Entity_To_FcuRt3Dto(
            new FcuRt3Entity { Locale = "zh", Label = 7 })!;
        Assert.Equal("v7@zh", dto.Label);
    }

    /// <summary>
    /// T1Guarded 执行端 ①：非 null 源——转换器消费解包值（CallCount=1）。
    /// </summary>
    [Fact]
    public void T1Guarded_NonNull_ConvertsUnwrappedValue()
    {
        FcuRtGConv.CallCount = 0;
        var dto = GeneratedMapper.Map_FcuRtGEntity_To_FcuRtGDto(new FcuRtGEntity { Count = 41 })!;
        Assert.Equal(42, dto.Count);
        Assert.Equal(1, FcuRtGConv.CallCount);
    }

    /// <summary>
    /// T1Guarded 执行端 ②：null 源——守卫三元短路（转换器不被调用、目标保持 default）。
    /// </summary>
    [Fact]
    public void T1Guarded_Null_ShortCircuitsConverter()
    {
        FcuRtGConv.CallCount = 0;
        var dto = GeneratedMapper.Map_FcuRtGEntity_To_FcuRtGDto(new FcuRtGEntity { Count = null })!;
        Assert.Equal(0, dto.Count); // default（null 不调用转换器）
        Assert.Equal(0, FcuRtGConv.CallCount); // 守卫短路——零调用
    }

    /// <summary>显式 Map 附着执行端：转换器消费显式源路径（LastName="smith" → "SMITH"——锚点名源侧不存在，唯附着可达）。</summary>
    [Fact]
    public void ExplicitMapAttached_ForwardConsumesExplicitSource()
    {
        var dto = GeneratedMapper.Map_FcuRtEEntity_To_FcuRtEDto(new FcuRtEEntity { LastName = "smith" })!;
        Assert.Equal("SMITH", dto.FullName);
    }

    /// <summary>扁平化源执行端：转换器消费嵌套叶值（Address.City → AddressCity）。</summary>
    [Fact]
    public void FlattenedSource_ForwardConsumesLeafValue()
    {
        var dto = GeneratedMapper.Map_FcuRtFEntity_To_FcuRtFDto(
            new FcuRtFEntity { Address = new FcuRtFAddress { City = "sh" } })!;
        Assert.Equal("SH", dto.AddressCity);
    }

    /// <summary>共存执行端：正向 Convert 与反向 ConvertBack 各自真实调用。</summary>
    [Fact]
    public void Convert_And_ConvertBack_BothEmitAtRuntime()
    {
        var created = new DateTime(2026, 9, 3);
        // 正向：ToText 格式化
        var dto = GeneratedMapper.Map_FcuRtCBEntity_To_FcuRtCBDto(new FcuRtCBEntity { Created = created })!;
        Assert.Equal("2026-09-03", dto.Created);
        // 反向：Parse 解析（round-trip）
        var entity = GeneratedMapper.Map_FcuRtCBDto_To_FcuRtCBEntity(new FcuRtCBDto { Created = "2026-09-03" })!;
        Assert.Equal(created, entity.Created);
    }
}
