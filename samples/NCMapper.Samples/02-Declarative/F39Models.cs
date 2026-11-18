using NCMapper.Attributes;
namespace NCMapper.Samples.Declarative;
// ================================================================
// F3/F9 演示模型
// F3：反向映射默认不消费 [Convert]（NCMAP033 Info 提示）——反向按类型转换路径写入
// F9：string→enum 空串/可空语义（含 InvariantCulture 教训同构锁定形态）
// ================================================================
/// <summary>F3 演示等级枚举（None=0 为默认值）。</summary>
public enum F3Level
{
    /// <summary>默认值</summary>
    None = 0,
    /// <summary>低</summary>
    Low = 1,
    /// <summary>高</summary>
    High = 2,
}
/// <summary>F3 演示实体（正向转换源）。</summary>
public class F3Entity
{
    /// <summary>主键</summary>
    public int Id { get; set; }

    /// <summary>等级（正向经 ConvertUsing 转换输出非成员名文本）</summary>
    public F3Level Level { get; set; } = F3Level.High;
}
/// <summary>F3 演示 DTO（正向转换锚点）。</summary>
[MapFrom(typeof(F3Entity))]
public class F3Dto
{
    /// <summary>主键</summary>
    public int Id { get; set; }

    /// <summary>等级文本（同名锚点 Level——正向 [Convert] 消费；反向不消费，内建 string→enum 解析非成员名失败）
    /// [MapFrom] 建立正向方向（isToDto=true，运行时/生成双侧同判）——缺省时方向不定，[Convert] 不消费（样例断言自引入起即红的根因）</summary>
    [Convert(typeof(F3Conv), nameof(F3Conv.ToText))]
    public string Level { get; set; } = "";
}
/// <summary>F3 正向转换器（输出非 enum 成员名文本——反向内建解析必然失败，证明未消费）。</summary>
public static class F3Conv
{
    /// <summary>调用计数——反向不消费证明：反向映射前后计数不增长（正向恰好 +1）。</summary>
    public static int CallCount;

    /// <summary>正向转换：输出带装饰的文本（非 F3Level 成员名）。</summary>
    public static string ToText(F3Level level)
    {
        CallCount++;
        return $"LEVEL_{level}!";
    }
}
/// <summary>F9 演示等级枚举。</summary>
public enum F9Level
{
    /// <summary>默认值（null 反向落点）</summary>
    None = 0,
    /// <summary>低</summary>
    Low = 1,
    /// <summary>高</summary>
    High = 2,
}
/// <summary>F9 演示 DTO（string 侧）。</summary>
public class F9Dto
{
    /// <summary>等级文本（非空 string——空串反向 → 抛 MapException，不落 default）</summary>
    public string Level { get; set; } = "";
    /// <summary>档位文本（可空 string——null 反向 → 可空枚举保持 null）</summary>
    public string? Grade { get; set; }
}
/// <summary>F9 演示实体（enum 侧）。</summary>
public class F9Entity
{
    /// <summary>等级（非空 enum）</summary>
    public F9Level Level { get; set; } = F9Level.None;
    /// <summary>档位（可空枚举）</summary>
    public F9Level? Grade { get; set; }
}
