using System.Linq;
using FsCheck.Xunit;
using NCMapper;
using Xunit;

namespace NCMapper.FuzzTests;

public class FcEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Kept { get; set; } = "";
    public string ReverseOnly { get; set; } = "";
    public string ForwardOnly { get; set; } = "";
    public int Num { get; set; }
    public int Code { get; set; }
    public int Score { get; set; }
    public List<int> Nums { get; set; } = new();
}

public class FcDto
{
    public int Id { get; set; }
    public string Alias { get; set; } = "";
    // 忽略面初值取非空哨兵：链式声明被移除时该属性会被自动匹配改名注入，值变化可区分
    public string Kept { get; set; } = "KEPT-INITIAL";
    public string ReverseOnly { get; set; } = "REVERSE-INITIAL";
    public string ForwardOnly { get; set; } = "FORWARD-INITIAL";
    public string Code { get; set; } = "";
    // 谓词假分支哨兵：条件守卫缺席时会被源值覆盖，值变化可区分
    public int Score { get; set; } = -99;
    public int Doubled { get; set; }
    public List<int> Values { get; set; } = new();
}

public static class FcConverters
{
    public static int Twice(int value) => value * 2;

    public static int ParseFormatted(string value) => int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
}

[MapperConfig]
internal class FcMapper
{
    public static void Configure(FluentMappingConfig cfg)
        => cfg.For<FcEntity, FcDto>()
              .Member(d => d.Alias).From(s => s.Name)
              .Member(d => d.Kept).Ignore()
              .Member(d => d.ReverseOnly).Ignore(NCMapper.Attributes.Direction.ToDto)
              .Member(d => d.ForwardOnly).Ignore(NCMapper.Attributes.Direction.ToEntity)
              .Member(d => d.Code).From(s => s.Num).Format("D3").ConvertBack<int>(FcConverters.ParseFormatted)
              .Member(d => d.Score).MapWhen(v => v > 0)
              .Member(d => d.Doubled).From(s => s.Num).Convert<int>(FcConverters.Twice)
              .Member(d => d.Values).From(s => s.Nums);
}

/// <summary>
/// Fluent 链式组合属性测试（FsCheck 100 随机输入 + shrinking）。
/// <para>瞄准点 = <b>组合与形态</b>（显式来源 × 忽略共生 / 忽略方向性 / 格式化产出 / 谓词守卫 /
/// 自定义转换 / 集合异名绑定）——与单点值一致、MapList、null 守卫、Update 写回四类既有属性正交互补。</para>
/// <para>每条均为可区分断言：目标侧初值取非空哨兵，声明面被移除或语义反向时断言必红
/// （非恒真/空转——见反向验证）。</para>
/// </summary>
public class FluentChainProperties
{
    /// <summary>显式来源绑定与忽略声明在同对内共生：绑定属性随源值，忽略属性恒保持目标侧初值。</summary>
    [Property]
    public bool FluentChain_ExplicitFrom_CoexistsWith_Ignore(int id, string name, string kept)
    {
        var dto = Mapper.Map<FcEntity, FcDto>(new FcEntity { Id = id, Name = name, Kept = kept })!;
        return dto.Alias == name && dto.Kept == "KEPT-INITIAL";
    }

    /// <summary>忽略方向性：正向忽略形态恒保持初值；反向忽略形态在正向照常取源值。</summary>
    [Property]
    public bool FluentChain_IgnoreDirection_ForwardSemantics(string reverseOnly, string forwardOnly)
    {
        var dto = Mapper.Map<FcEntity, FcDto>(
            new FcEntity { ReverseOnly = reverseOnly, ForwardOnly = forwardOnly })!;
        return dto.ReverseOnly == "REVERSE-INITIAL" && dto.ForwardOnly == forwardOnly;
    }

    /// <summary>格式化产出恒等于按格式串对源值的直接产出（先算期望再比对，文化口径同链式声明）。</summary>
    [Property]
    public bool FluentChain_Format_MatchesDirectToString(int rawNum)
    {
        var num = rawNum % 1000;
        var dto = Mapper.Map<FcEntity, FcDto>(new FcEntity { Num = num })!;
        return dto.Code == num.ToString("D3", null);
    }

    /// <summary>谓词守卫：源值满足谓词时赋值、不满足时保持目标侧初值（两分支均覆盖）。</summary>
    [Property]
    public bool FluentChain_MapWhen_GuardBothBranches(int score)
    {
        var dto = Mapper.Map<FcEntity, FcDto>(new FcEntity { Score = score })!;
        return score > 0 ? dto.Score == score : dto.Score == -99;
    }

    /// <summary>自定义转换产出恒等于转换函数的直接结果（方法组形态；期望值独立算出，不复用转换器本体）。</summary>
    [Property]
    public bool FluentChain_Convert_MatchesConverterOutput(int num)
    {
        var dto = Mapper.Map<FcEntity, FcDto>(new FcEntity { Num = num })!;
        return dto.Doubled == num * 2;
    }

    /// <summary>集合异名绑定：数量一致 + 逐项一致（随机长度含 0/1/多）。</summary>
    [Property]
    public bool FluentChain_Collection_DifferentName_ElementInvariant(int[] nums)
    {
        var list = nums.ToList();
        var dto = Mapper.Map<FcEntity, FcDto>(new FcEntity { Nums = list })!;
        return dto.Values.Count == list.Count && dto.Values.SequenceEqual(list);
    }
}