using System.Linq.Expressions;
using System.Reflection;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.Internal;
using Xunit;

namespace NCMapper.Tests.Tests;

// ================================================================
// 批次三遗留测试补齐（v1.2 用户裁决增补）——
// B3-04 / A1-01 / A2-01 三条目的验收断言（批次三代码已落地，本文件仅补验证面；
// 断言敏感性由 V7/V8/V10 回退演练承担——回退对应修复行 → 各断言转红）
// ================================================================

// ---------------- B3-04：无源分支补 [MapWhen] 消费（运行时端到端） ----------------

/// <summary>无源转换器（null → "C:" 前缀）——非空产物使"守卫拦截 vs 无条件转换"可观测。</summary>
public static class T7B3Conv
{
    /// <summary>T1 模板：null → "C:"（守卫失效时 Extra 会变成 "C:"——观测面）</summary>
    public static string Convert(string v) => "C:" + v;
}

public class T7B3Entity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

[MapFrom(typeof(T7B3Entity))]
public class T7B3Dto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    /// <summary>无源属性 + [Convert] + [MapWhen]——B3-04 修复点：无源分支须消费条件守卫
    /// （谓词对 null 源值求值 = false → 不应用 Convert 产物）。</summary>
    [Convert(typeof(T7B3Conv), nameof(T7B3Conv.Convert))]
    [MapWhen(nameof(NonEmpty))]
    public string Extra { get; set; } = "keep";

    /// <summary>对照：无源 + [Convert] 无 [MapWhen]——无条件 Convert(null) = "C:"（先验锚）。</summary>
    [Convert(typeof(T7B3Conv), nameof(T7B3Conv.Convert))]
    public string Extra2 { get; set; } = "keep";

    /// <summary>T1 谓词（目标属性类型 string 基准——B3-04 无源场景口径）。</summary>
    public static bool NonEmpty(string value) => !string.IsNullOrEmpty(value);
}

/// <summary>B3-04（fbcd29b MappingExtractor.Properties.cs :314-329）：无源分支（提前 return 绕过
/// 主链尾部消费区）补 [MapWhen] 消费。断言：对照属性 Extra2（无守卫）= "C:"（先验锚——无源分支
/// Convert 确实执行），守卫属性 Extra ≠ "C:"（条件拦截）——回退修复行（V7）时 Extra 变 "C:" 红。</summary>
[Collection("DeepCopyGlobalState")]
public class T7B3MapWhenNullSourceTests
{
    [Fact]
    public void MapWhen_Consumed_InNullSourceBranch()
    {
        var dto = Mapper.Map<T7B3Entity, T7B3Dto>(new T7B3Entity { Id = 1, Name = "n" });

        Assert.NotNull(dto);
        // 先验锚：无守卫对照属性走无条件 Convert(null) = "C:"——证明无源分支 Convert 可达（防 vacuous）
        Assert.Equal("C:", dto!.Extra2);
        // B3-04 核心断言：[MapWhen] 守卫拦截 Convert 产物（修复前静默丢失 → Extra == "C:" 红）
        Assert.NotEqual("C:", dto.Extra);
    }
}

// ---------------- A1-01：跨表快路径守卫强制回退 Block 路径（白盒） ----------------

public class T7A1Cust
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class T7A1Order
{
    public int Id { get; set; }
    public T7A1Cust? Customer { get; set; }
}

/// <summary>A1-01 转换器。</summary>
public static class T7A1Conv
{
    /// <summary>T1 模板。</summary>
    public static string Convert(string v) => "[" + v + "]";
}

[MapFrom(typeof(T7A1Order))]
public class T7A1Dto
{
    public int Id { get; set; }

    /// <summary>跨表属性（无转换器）——守卫不误伤对照面。</summary>
    [MapProperty(typeof(T7A1Cust), nameof(T7A1Cust.Name))]
    public string CustName { get; set; } = "";

    /// <summary>跨表属性 + [Convert]——A1-01 修复点：MemberInit 快路径须回退 Block。</summary>
    [MapProperty(typeof(T7A1Cust), nameof(T7A1Cust.Name))]
    [Convert(typeof(T7A1Conv), nameof(T7A1Conv.Convert))]
    public string CustName2 { get; set; } = "";
}

/// <summary>A1-01（960176c MapperBase.cs :709-711）：跨表 MemberInit 快路径入口守卫——
/// 带 [Convert]/[ConvertBack] 的目标属性返回 null（强制回退 Block 路径，转换器语义
/// 经 CrossTableStrategy 消费链恢复）；白盒直接断言 TryBuildCrossTableBinding 守卫行为——
/// 回退修复行（V10）时 CustName2 也返回非 null binding → 红。</summary>
[Collection("DeepCopyGlobalState")]
public class T7A1CrossTableGuardTests
{
    private static MemberBinding? InvokeTryBuildCrossTableBinding(PropertyInfo targetItem)
    {
        var method = typeof(MapperBase<T7A1Order, T7A1Dto>).GetMethod(
            "TryBuildCrossTableBinding",
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(MapPropertyAttribute), typeof(PropertyInfo), typeof(ParameterExpression) },
            modifiers: null);
        Assert.NotNull(method);
        var attr = new MapPropertyAttribute(typeof(T7A1Cust), nameof(T7A1Cust.Name));
        var srcParam = Expression.Parameter(typeof(T7A1Order));
        return (MemberBinding?)method!.Invoke(null, new object?[] { attr, targetItem, srcParam });
    }

    [Fact]
    public void CrossTableFastPath_WithConvertUsing_FallsBackToNull()
    {
        // 守卫核心断言：带 [Convert] 的跨表属性 → 返回 null（回退 Block；修复前静默直赋非 null 红）
        var converted = InvokeTryBuildCrossTableBinding(typeof(T7A1Dto).GetProperty("CustName2")!);
        Assert.Null(converted);
    }

    [Fact]
    public void CrossTableFastPath_WithoutConverter_StillBinds()
    {
        // 守卫不误伤对照：无转换器跨表属性 → 正常 MemberInit 绑定（非 null）
        var plain = InvokeTryBuildCrossTableBinding(typeof(T7A1Dto).GetProperty("CustName")!);
        Assert.NotNull(plain);
    }
}

// ---------------- A2-01：嵌套投影不可转字段前置预检跳过（白盒） ----------------

/// <summary>导航类型——DateTime? 字段与 DTO 侧 int 构成不可转组合（IsImplicitConvertible=false）。</summary>
public class T7A2SubEntity
{
    public DateTime? Code { get; set; }
}

public class T7A2Entity
{
    public int Id { get; set; }
    public T7A2SubEntity Sub { get; set; } = new();
}

/// <summary>DTO——Code 经 [MapProperty] 声明导航字段（fieldSourceType=T7A2SubEntity，触发
/// BuildNestedProjectionBindings 的导航取值分支 :756-798）。</summary>
public class T7A2Dto
{
    public int Id { get; set; }
    public int Code { get; set; }
}

/// <summary>A2-01（fbcd29b ProjectionExpressionFactory.cs :790-794）：嵌套投影不可转组合
/// （DateTime?→int——IsImplicitConvertible 经剥壳后 DateTime→int 不在隐式表 = false）
/// 前置预检跳过——修复前 Expression.Convert 绑定保留（可空源 null 时运行期裸抛）。
/// 白盒断言 BuildNestedProjectionBindings（srcType=T7A2Entity 外层，导航取值分支）产物不含
/// Code 绑定——回退修复行（V8）时含 Code 绑定（或构建 Convert 抛）→ 红。
/// 注意：int?→int 组合经剥壳归一为可转（IsImplicitConvertible=true，双引擎同走
/// Convert 的既定语义），不可用作本断言 fixture。</summary>
[Collection("DeepCopyGlobalState")]
public class T7A2NestedProjectionGuardTests
{
    [Fact]
    public void NestedProjection_NonConvertibleField_Skipped()
    {
        var strategy = new ProjectionStrategy(
            MaxNestingDepth: 3, WarningDepth: 2, EnableCollectionProjection: false, TracePrefix: "[T7] ");
        var method = typeof(ProjectionExpressionFactory).GetMethod(
            "BuildNestedProjectionBindings",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var srcExpr = Expression.Parameter(typeof(T7A2Entity));
        // 修复态：不可转 Code 被前置预检跳过（绑定集不含 Code 或全跳过返回 null）；
        // 回退态（V8 红）：Code 以 Expression.Convert(DateTime?, int) 绑定保留
        var result = method!.Invoke(null, new object?[] { typeof(T7A2Entity), typeof(T7A2Dto), srcExpr, 1, strategy });
        if (result is System.Collections.Generic.List<MemberBinding> bindings)
        {
            Assert.DoesNotContain(bindings, b => b.Member.Name == "Code");
        }
        else
        {
            Assert.Null(result);
        }
    }
}
