using NCMapper;
using Xunit;

namespace NCMapper.Tests.Tests;

/// <summary>
/// B1 BuildConstructorArgs 无条件 Convert 修复测试
/// <para>覆盖报告-20260818-010 B1 验收点：①record 构造器参数类型不兼容（string→int）时
/// <see cref="Mapper.Map{TSource,TTarget}"/> 抛 <see cref="MapConfigurationException"/>（而非构建期
/// InvalidOperationException）；②<see cref="Mapper.MapList{TSource,TTarget}"/> 双入口同语义；
/// ③异常消息含源属性类型与参数类型上下文；④合法 record（同名同类型）映射正常；
/// ⑤连续调用异常语义稳定（修复后不再"每次重编译重抛"）。</para>
/// <para>修复机制：MapperBase.BuildConstructorArgs 兼容性预检（IsCtorArgConvertible），
/// 不兼容构造器跳过，全部不可用时抛 MapConfigurationException（MapperBase.cs:673-676 修复）。</para>
/// </summary>
public class CtorArgCompatibilityTests
{
    /// <summary>
    /// 源类型 — Name/Age/Color 均为 string（Age 与目标 record 的 int 参数不兼容；
    /// Color 用于 string→enum 不兼容验证）
    /// </summary>
    public class CtorArgSrc
    {
        /// <summary>
        /// 名称（与目标同类型）
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// 年龄（string，与目标 int 参数不兼容）
        /// </summary>
        public string Age { get; set; } = "";

        /// <summary>
        /// 颜色（string，与目标枚举参数不兼容——审计 A1）
        /// </summary>
        public string Color { get; set; } = "";
    }

    /// <summary>
    /// 目标 record — Age 构造参数为 int（与源 string 属性不兼容，触发 B1）
    /// </summary>
    /// <param name="Name">名称</param>
    /// <param name="Age">年龄（int）</param>
    public record CtorArgPerson(string Name, int Age);

    /// <summary>
    /// 合法目标 record — 参数与源属性同类型，映射应正常
    /// </summary>
    /// <param name="Name">名称</param>
    public record CtorArgOkPerson(string Name);

    /// <summary>
    /// 颜色枚举 — 用于 string→enum 不兼容验证（审计 A1 精确化判据）
    /// </summary>
    public enum CtorArgColor
    {
        /// <summary>
        /// 红
        /// </summary>
        Red,

        /// <summary>
        /// 绿
        /// </summary>
        Green,
    }

    /// <summary>
    /// 枚举参数目标 record — Color 构造参数为枚举（源属性为 string，无转换操作符）
    /// </summary>
    /// <param name="Name">名称</param>
    /// <param name="Color">颜色（枚举）</param>
    public record CtorArgEnumPerson(string Name, CtorArgColor Color);

    /// <summary>
    /// 验收点①：Map 入口 record 参数类型不兼容抛 MapConfigurationException（非 InvalidOperationException）
    /// </summary>
    [Fact]
    public void CtorArgIncompatible_Map_Throws_MapConfigurationException()
    {
        var source = new CtorArgSrc { Name = "Test", Age = "25" };

        var ex = Assert.Throws<MapConfigurationException>(
            () => Mapper.Map<CtorArgSrc, CtorArgPerson>(source));
        Assert.NotNull(ex);
    }

    /// <summary>
    /// 审计 A1 验收：string→enum 无转换操作符，必须抛 MapConfigurationException
    /// <para>修复前 IsCtorArgConvertible 的 <c>IsEnum 或</c> 判据将 string→enum 误判为可转换
    /// （Convert 构建期崩）；修复后判不兼容抛配置异常（MapperBase.cs enum 分支精确化）。</para>
    /// </summary>
    [Fact]
    public void CtorArgIncompatible_StringToEnum_Throws_MapConfigurationException()
    {
        var source = new CtorArgSrc { Name = "Test", Age = "25", Color = "Red" };

        var ex = Assert.Throws<MapConfigurationException>(
            () => Mapper.Map<CtorArgSrc, CtorArgEnumPerson>(source));
        // 消息含类型上下文（enum 参数名 Color）
        Assert.Contains("Color", ex.Message);
    }

    /// <summary>
    /// 验收点②：MapList 入口同形态同样抛 MapConfigurationException（双入口共享修复）
    /// </summary>
    [Fact]
    public void CtorArgIncompatible_MapList_Throws_MapConfigurationException()
    {
        var sources = new List<CtorArgSrc>
        {
            new() { Name = "A", Age = "1" },
            new() { Name = "B", Age = "2" },
        };

        Assert.Throws<MapConfigurationException>(
            () => Mapper.MapList<CtorArgSrc, CtorArgPerson>(sources));
    }

    /// <summary>
    /// 验收点③：异常消息含源属性类型与参数类型上下文（Src.Age(string) → Person..ctor(int) 形态）
    /// </summary>
    [Fact]
    public void CtorArgIncompatible_Message_ContainsTypeContext()
    {
        var source = new CtorArgSrc { Name = "Test", Age = "25" };

        var ex = Assert.Throws<MapConfigurationException>(
            () => Mapper.Map<CtorArgSrc, CtorArgPerson>(source));

        // 消息含源类型.属性(源属性类型) 与 目标类型..ctor(参数类型)
        Assert.Contains(nameof(CtorArgSrc), ex.Message);
        Assert.Contains(nameof(CtorArgPerson), ex.Message);
        Assert.Contains("Age", ex.Message);
        // 类型上下文：源属性 string → 参数 int（.NET 类型名 String/Int32）
        Assert.Contains(typeof(string).Name, ex.Message);
        Assert.Contains(typeof(int).Name, ex.Message);
    }

    /// <summary>
    /// 验收点④：合法 record（同名同类型参数）映射正常（回归防线）
    /// </summary>
    [Fact]
    public void CtorArgCompatible_SameTypeParam_MapsNormally()
    {
        var source = new CtorArgSrc { Name = "Test", Age = "25" };

        var result = Mapper.Map<CtorArgSrc, CtorArgOkPerson>(source);
        Assert.NotNull(result);
        Assert.Equal("Test", result!.Name);
    }

    /// <summary>
    /// 验收点⑤：连续调用异常语义稳定（修复后不出现"每次重编译重抛"）
    /// <para>catch 断言：连续 2 次调用均抛同类型异常（异常类型稳定即可，
    /// 不依赖 Lazy 计数——报告 B1 验收点③口径）。</para>
    /// </summary>
    [Fact]
    public void CtorArgIncompatible_RepeatedCalls_StableException()
    {
        var source = new CtorArgSrc { Name = "Test", Age = "25" };

        // 第一次调用抛 MapConfigurationException
        Assert.Throws<MapConfigurationException>(() => Mapper.Map<CtorArgSrc, CtorArgPerson>(source));
        // 第二次调用异常语义稳定（仍抛 MapConfigurationException，而非其他异常）
        var ex2 = Assert.Throws<MapConfigurationException>(() => Mapper.Map<CtorArgSrc, CtorArgPerson>(source));
        Assert.Contains(nameof(CtorArgPerson), ex2.Message);
    }
}
