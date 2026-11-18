using NCMapper;
using Xunit;

namespace NCMapper.Tests.Tests;

/// <summary>
/// B2 TryCompileRecordCreator 无条件 Convert 修复测试
/// <para>覆盖报告-20260818-010 B2 验收点：①同类型自拷贝中"属性与构造参数类型不一致"形态
/// （class Weird { string Age; Weird(int age) }）DeepCopy 抛 <see cref="MapConfigurationException"/>
/// （而非构建期 InvalidOperationException）；②异常消息含属性/参数类型；③合法 record 深拷贝正常；
/// ④连续调用异常语义稳定（修复后不再"每次重编译重抛"）。</para>
/// <para>修复机制：DeepCopyCompiler.TryCompileRecordCreator 兼容性预检（IsCtorArgConvertible），
/// 不兼容构造器跳过，全部不可用时抛 MapConfigurationException（DeepCopyCompiler.cs:213-215 修复）。</para>
/// </summary>
public class RecordDeepCopyCtorTests
{
    /// <summary>
    /// B2 复现类型 — Age 属性为 string，构造器参数 age 为 int（按名匹配，类型不兼容）
    /// <para>无无参构造器（定义带参构造后编译器不再生成默认构造）→ 走 TryCompileRecordCreator 路径。</para>
    /// </summary>
    public class Weird
    {
        /// <summary>
        /// 年龄属性（string，与构造参数 int 不兼容）
        /// </summary>
        public string Age { get; set; } = "";

        /// <summary>
        /// 带参构造器 — age 为 int（按名匹配到 Age 属性，类型不兼容）
        /// </summary>
        /// <param name="age">年龄（int）</param>
        public Weird(int age)
        {
            Age = age.ToString();
        }
    }

    /// <summary>
    /// 合法 record — 主构造参数与属性同类型，深拷贝应正常
    /// </summary>
    /// <param name="Name">名称</param>
    public record DeepCopyOkPerson(string Name);

    /// <summary>
    /// 验收点①：Weird 形态 DeepCopy 抛 MapConfigurationException（非 InvalidOperationException）
    /// </summary>
    [Fact]
    public void RecordDeepCopyCtor_Incompatible_Throws_MapConfigurationException()
    {
        var source = new Weird(1);

        var ex = Assert.Throws<MapConfigurationException>(() => Mapper.DeepCopy(source));
        Assert.NotNull(ex);
    }

    /// <summary>
    /// 验收点②：异常消息含属性/参数类型上下文（Weird.Age(string) → Weird..ctor(int) 形态）
    /// </summary>
    [Fact]
    public void RecordDeepCopyCtor_Message_ContainsTypeContext()
    {
        var source = new Weird(1);

        var ex = Assert.Throws<MapConfigurationException>(() => Mapper.DeepCopy(source));

        // 消息含类型名、属性名与构造器签名上下文
        Assert.Contains(nameof(Weird), ex.Message);
        Assert.Contains("Age", ex.Message);
        // 类型上下文：源属性 string → 参数 int（.NET 类型名 String/Int32）
        Assert.Contains(typeof(string).Name, ex.Message);
        Assert.Contains(typeof(int).Name, ex.Message);
    }

    /// <summary>
    /// 验收点③：合法 record（同名同类型参数）深拷贝正常（回归防线）
    /// </summary>
    [Fact]
    public void RecordDeepCopyCtor_Compatible_DeepCopySucceeds()
    {
        var source = new DeepCopyOkPerson("Test");

        var copy = Mapper.DeepCopy(source);
        Assert.NotNull(copy);
        Assert.NotSame(source, copy);
        Assert.Equal("Test", copy!.Name);
    }

    /// <summary>
    /// 验收点④：连续调用异常语义稳定（修复后不出现"每次重编译重抛"）
    /// <para>catch 断言：连续 2 次调用均抛同类型异常（报告 B2 验收点②口径）。</para>
    /// </summary>
    [Fact]
    public void RecordDeepCopyCtor_RepeatedCalls_StableException()
    {
        var source = new Weird(1);

        Assert.Throws<MapConfigurationException>(() => Mapper.DeepCopy(source));
        var ex2 = Assert.Throws<MapConfigurationException>(() => Mapper.DeepCopy(source));
        Assert.Contains(nameof(Weird), ex2.Message);
    }
}
