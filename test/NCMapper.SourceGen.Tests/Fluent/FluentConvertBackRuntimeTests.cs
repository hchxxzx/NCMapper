using NCMapper;
using NCMapper.Generated;
using Xunit;

namespace NCMapper.SourceGen.Tests.Fluent;

/// <summary>
/// 运行时矩阵：ConvertBack 执行端（生成代码真实调用——独立验证）。
/// <para>本文件随测试工程编译即被源生成器处理（测试工程引用 SourceGen 为 Analyzer），
/// 生成的 GeneratedMapper 方法编译进测试程序集供运行时断言（FluentSmokePoCTests 同款布局——
/// 镜像特性侧 ConvertBackUsingRuntimeTests）。</para>
/// <para>覆盖：T0/T2/T3 形态实参（整 DTO 传入）、T1Guarded null 短路（CallCount 拦截）、
/// ReverseOnly 条目反向方法生成且调用、写回链（auto-flatten 正向 + X2 反向）。</para>
/// </summary>

// ── Pair 1：T1 匹配对（红测批次既有）─────────────────────────────
/// <summary>运行时模型源（T1 匹配对——自动匹配命中 + ConvertBack 单值）</summary>
public class FcbRunEntity
{
    /// <summary>标签（反向经 FcbRunConv.Convert）</summary>
    public string Tag { get; set; } = "";
}

/// <summary>运行时模型目标（T1 匹配对）</summary>
public class FcbRunDto
{
    /// <summary>标签</summary>
    public string Tag { get; set; } = "";
}

/// <summary>运行时转换器（T1 模板 M(TField)）</summary>
public static class FcbRunConv
{
    /// <summary>带前缀回写（运行时断言载体）</summary>
    public static string Convert(string fieldValue) => "back:" + fieldValue;
}

/// <summary>
/// Fluent 通道入口（运行时）：ConvertBack T1 单值——正向自动匹配（string→string 直赋），
/// 反向消费转换器（镜像特性侧 CbuR1 运行时契约）
/// </summary>
[MapperConfig]
public class FcbRunMapper
{
    /// <summary>Fluent 链式声明</summary>
    public static void Configure(FluentMappingConfig cfg)
    {
        cfg.For<FcbRunEntity, FcbRunDto>().Member(d => d.Tag).ConvertBack(FcbRunConv.Convert);
    }
}

// ── Pair 2：T0 常量 ────────────────────────────────────────────
/// <summary>运行时模型源（T0 常量形态）</summary>
public class FcbRun0Entity
{
    /// <summary>来源（反向写常量）</summary>
    public string Source { get; set; } = "";
}

/// <summary>运行时模型目标（T0 常量形态）</summary>
public class FcbRun0Dto
{
    /// <summary>来源</summary>
    public string Source { get; set; } = "";
}

/// <summary>运行时转换器（T0 模板 M()）</summary>
public static class FcbRun0Conv
{
    /// <summary>常量产物</summary>
    public const string Fixed = "fixed-value";

    /// <summary>无参常量</summary>
    public static string Stamp() => Fixed;
}

/// <summary>Fluent 通道入口（运行时）：T0 常量</summary>
[MapperConfig]
public class FcbRun0Mapper
{
    /// <summary>Fluent 链式声明</summary>
    public static void Configure(FluentMappingConfig cfg)
    {
        cfg.For<FcbRun0Entity, FcbRun0Dto>().Member(d => d.Source).ConvertBack<string>(FcbRun0Conv.Stamp);
    }
}

// ── Pair 3：T2 整源 ────────────────────────────────────────────
/// <summary>运行时模型源（T2 整源形态）</summary>
public class FcbRun2Entity
{
    /// <summary>摘要（反向由整 DTO 组合生成）</summary>
    public string Summary { get; set; } = "";
}

/// <summary>运行时模型目标（T2 整源形态）</summary>
public class FcbRun2Dto
{
    /// <summary>摘要</summary>
    public string Summary { get; set; } = "";

    /// <summary>姓名（整源组合素材）</summary>
    public string Name { get; set; } = "";
}

/// <summary>运行时转换器（T2 模板 M(TSource)——实参为整个 DTO 对象）</summary>
public static class FcbRun2Conv
{
    /// <summary>整源组合（读 DTO 多字段——证明整对象实参传入）</summary>
    public static string Build(FcbRun2Dto dto) => "sum:" + dto.Name + ":" + dto.Summary;
}

/// <summary>Fluent 通道入口（运行时）：T2 整源</summary>
[MapperConfig]
public class FcbRun2Mapper
{
    /// <summary>Fluent 链式声明</summary>
    public static void Configure(FluentMappingConfig cfg)
    {
        cfg.For<FcbRun2Entity, FcbRun2Dto>().Member(d => d.Summary).ConvertBack(FcbRun2Conv.Build);
    }
}

// ── Pair 4：T3 组合+字段值 ─────────────────────────────────────
/// <summary>运行时模型源（T3 组合形态）</summary>
public class FcbRun3Entity
{
    /// <summary>标签（反向由整 DTO + 字段值组合生成）</summary>
    public string Tag { get; set; } = "";
}

/// <summary>运行时模型目标（T3 组合形态）</summary>
public class FcbRun3Dto
{
    /// <summary>标签</summary>
    public string Tag { get; set; } = "";

    /// <summary>姓名（组合素材）</summary>
    public string Name { get; set; } = "";
}

/// <summary>运行时转换器（T3 模板 M(TSource, TField)）</summary>
public static class FcbRun3Conv
{
    /// <summary>组合（双实参拼接——证明整对象 + 字段值同时传入）</summary>
    public static string Build(FcbRun3Dto dto, string tag) => "T3:" + dto.Name + ":" + tag;
}

/// <summary>Fluent 通道入口（运行时）：T3 组合+字段值</summary>
[MapperConfig]
public class FcbRun3Mapper
{
    /// <summary>Fluent 链式声明</summary>
    public static void Configure(FluentMappingConfig cfg)
    {
        cfg.For<FcbRun3Entity, FcbRun3Dto>().Member(d => d.Tag).ConvertBack(FcbRun3Conv.Build);
    }
}

// ── Pair 5：Nullable 直传形态（守卫的 Fluent 等价表达——D-1 差异登记）─────────
/// <summary>运行时模型源（Nullable 直传形态）</summary>
public class FcbRunGEntity
{
    /// <summary>计数（反向经 Nullable 直传转换器）</summary>
    public int Count { get; set; }
}

/// <summary>运行时模型目标（Nullable 直传形态——可空锚点）</summary>
public class FcbRunGDto
{
    /// <summary>计数（可空锚点）</summary>
    public int? Count { get; set; }
}

/// <summary>运行时转换器（Nullable 直传——入参 int?，null 自行处理）</summary>
public static class FcbRunGConv
{
    /// <summary>调用计数（直传断言载体）</summary>
    public static int CallCount;

    /// <summary>Nullable 直传（null → 0；守卫变体经方法组 API 不可达——D-1 差异登记）</summary>
    public static int Convert(int? fieldValue)
    {
        CallCount++;
        return (fieldValue ?? -1) + 1;
    }
}

/// <summary>Fluent 通道入口（运行时）：Nullable 直传形态（平直 T1）</summary>
[MapperConfig]
public class FcbRunGMapper
{
    /// <summary>Fluent 链式声明</summary>
    public static void Configure(FluentMappingConfig cfg)
    {
        cfg.For<FcbRunGEntity, FcbRunGDto>().Member(d => d.Count).ConvertBack(FcbRunGConv.Convert);
    }
}

// ── Pair 6：ReverseOnly（NoAutoInclude + 无正向声明）──────────────────
/// <summary>运行时模型源（ReverseOnly）</summary>
public class FcbRunRoEntity
{
    /// <summary>Id（正向显式声明）</summary>
    public int Id { get; set; }

    /// <summary>审计注记（反向无源——仅 ConvertBack 声明）</summary>
    public string AuditNote { get; set; } = "";
}

/// <summary>运行时模型目标（ReverseOnly）</summary>
public class FcbRunRoDto
{
    /// <summary>Id</summary>
    public int Id { get; set; }

    /// <summary>审计注记</summary>
    public string AuditNote { get; set; } = "";
}

/// <summary>运行时转换器（ReverseOnly——T2 整源）</summary>
public static class FcbRunRoConv
{
    /// <summary>整源组合（读 DTO Id——证明 ReverseOnly 条目反向方法生成且调用）</summary>
    public static string Build(FcbRunRoDto dto) => "ro:" + dto.Id;
}

/// <summary>Fluent 通道入口（运行时）：ReverseOnly（NoAutoInclude）</summary>
[MapperConfig]
public class FcbRunRoMapper
{
    /// <summary>Fluent 链式声明</summary>
    public static void Configure(FluentMappingConfig cfg)
    {
        cfg.For<FcbRunRoEntity, FcbRunRoDto>()
           .NoAutoMatch()
           .Member(d => d.Id).From(s => s.Id)
           .Member(d => d.AuditNote).ConvertBack(FcbRunRoConv.Build);
    }
}

// ── Pair 7：写回链（auto-flatten 正向 + X2 反向）────────────────────
/// <summary>运行时模型地址（写回链中间节点）</summary>
public class FcbRunWAddress
{
    /// <summary>城市（写回链叶）</summary>
    public string City { get; set; } = "";
}

/// <summary>运行时模型源（写回链）</summary>
public class FcbRunWEntity
{
    /// <summary>地址（正向扁平化源）</summary>
    public FcbRunWAddress Address { get; set; } = new();
}

/// <summary>运行时模型目标（写回链——扁平 AddressCity）</summary>
public class FcbRunWDto
{
    /// <summary>城市（扁平属性——auto-flatten 命名约定：路径段拼接）</summary>
    public string AddressCity { get; set; } = "";
}

/// <summary>运行时转换器（写回链 T1）</summary>
public static class FcbRunWConv
{
    /// <summary>带前缀回写（写回链消费断言载体）</summary>
    public static string Convert(string fieldValue) => "city:" + fieldValue;
}

/// <summary>Fluent 通道入口（运行时）：写回链（正向 auto-flatten，反向 X2 写回）</summary>
[MapperConfig]
public class FcbRunWMapper
{
    /// <summary>Fluent 链式声明</summary>
    public static void Configure(FluentMappingConfig cfg)
    {
        cfg.For<FcbRunWEntity, FcbRunWDto>().Member(d => d.AddressCity).ConvertBack(FcbRunWConv.Convert);
    }
}

/// <summary>ConvertBack 运行时定向测试（生成代码真实调用——执行端独立验证）</summary>
public class FluentConvertBackRuntimeTests
{
    /// <summary>
    /// T1 匹配对执行端：反向生成方法真实调用转换器（"back:hello"——纯赋值时为 "hello"）。
    /// </summary>
    [Fact]
    public void T1_MatchedPair_ReverseCallsConverter()
    {
        var entity = GeneratedMapper.Map_FcbRunDto_To_FcbRunEntity(new FcbRunDto { Tag = "hello" })!;
        Assert.Equal("back:hello", entity.Tag);
    }

    /// <summary>
    /// T1 匹配对执行端：正向不受影响（同类型直赋——"正向不消费"契约）。
    /// </summary>
    [Fact]
    public void T1_MatchedPair_ForwardUnaffected()
    {
        var dto = GeneratedMapper.Map_FcbRunEntity_To_FcbRunDto(new FcbRunEntity { Tag = "raw" })!;
        Assert.Equal("raw", dto.Tag);
    }

    /// <summary>T0 常量执行端：反向写入常量（M() 无实参）。</summary>
    [Fact]
    public void T0_MatchedPair_ReverseWritesConstant()
    {
        var entity = GeneratedMapper.Map_FcbRun0Dto_To_FcbRun0Entity(new FcbRun0Dto { Source = "whatever" })!;
        Assert.Equal(FcbRun0Conv.Fixed, entity.Source);
    }

    /// <summary>T2 整源执行端：反向整 DTO 对象传入（读 Name+Summary 双字段——证明整对象实参）。</summary>
    [Fact]
    public void T2_MatchedPair_ReversePassesWholeDto()
    {
        var entity = GeneratedMapper.Map_FcbRun2Dto_To_FcbRun2Entity(
            new FcbRun2Dto { Summary = "s", Name = "Bob" })!;
        Assert.Equal("sum:Bob:s", entity.Summary);
    }

    /// <summary>T3 组合执行端：反向整 DTO + 字段值双实参传入。</summary>
    [Fact]
    public void T3_MatchedPair_ReversePassesDtoAndField()
    {
        var entity = GeneratedMapper.Map_FcbRun3Dto_To_FcbRun3Entity(
            new FcbRun3Dto { Tag = "t", Name = "Alice" })!;
        Assert.Equal("T3:Alice:t", entity.Tag);
    }

    /// <summary>
    /// Nullable 直传执行端 ①：非 null 计数——转换器消费直传实参。
    /// </summary>
    [Fact]
    public void NullableParam_T1_NonNull_ConvertsValue()
    {
        FcbRunGConv.CallCount = 0;
        var entity = GeneratedMapper.Map_FcbRunGDto_To_FcbRunGEntity(new FcbRunGDto { Count = 41 })!;
        Assert.Equal(42, entity.Count);
        Assert.Equal(1, FcbRunGConv.CallCount);
    }

    /// <summary>
    /// Nullable 直传执行端 ②：null 直传——转换器被调用（CallCount=1）并自行处理 null
    ///（守卫变体经方法组 API 结构性不可达——D-1 差异登记：null 短路语义由转换器自行承接）。
    /// </summary>
    [Fact]
    public void NullableParam_T1_Null_PassesThroughToConverter()
    {
        FcbRunGConv.CallCount = 0;
        var entity = GeneratedMapper.Map_FcbRunGDto_To_FcbRunGEntity(new FcbRunGDto { Count = null })!;
        Assert.Equal(0, entity.Count); // (null ?? -1) + 1
        Assert.Equal(1, FcbRunGConv.CallCount); // 直传形态：转换器被调用（非守卫短路）
    }

    /// <summary>
    /// ReverseOnly 执行端：NoAutoInclude 场景——AuditNote 无正向声明，反向方法仍生成且调用转换器
    ///（正向仅显式声明 Id——对级语义）。
    /// </summary>
    [Fact]
    public void ReverseOnly_NoAutoInclude_ReverseMethodGeneratedAndCalled()
    {
        // 正向：仅 Id（AuditNote 不映射——NoAutoInclude + 无显式声明）
        var dto = GeneratedMapper.Map_FcbRunRoEntity_To_FcbRunRoDto(new FcbRunRoEntity { Id = 7, AuditNote = "note" })!;
        Assert.Equal(7, dto.Id);
        Assert.Equal("", dto.AuditNote);

        // 反向：ReverseOnly 条目——整 DTO 传入转换器
        var entity = GeneratedMapper.Map_FcbRunRoDto_To_FcbRunRoEntity(new FcbRunRoDto { Id = 7 })!;
        Assert.Equal("ro:7", entity.AuditNote);
    }

    /// <summary>
    /// 写回链执行端：正向 auto-flatten（Address.City → AddressCity），反向经 X2 写回链
    ///（AddressCity → Address.City，经转换器）。
    /// </summary>
    [Fact]
    public void WriteBackChain_ReverseWritesNestedPathViaConverter()
    {
        // 正向：扁平化（AddressCity ← Address.City）
        var dto = GeneratedMapper.Map_FcbRunWEntity_To_FcbRunWDto(new FcbRunWEntity { Address = new FcbRunWAddress { City = "SH" } })!;
        Assert.Equal("SH", dto.AddressCity);

        // 反向：写回链（dst.Address.City = Convert(src.AddressCity)）
        var entity = GeneratedMapper.Map_FcbRunWDto_To_FcbRunWEntity(new FcbRunWDto { AddressCity = "SH" })!;
        Assert.Equal("city:SH", entity.Address.City);
    }
}
