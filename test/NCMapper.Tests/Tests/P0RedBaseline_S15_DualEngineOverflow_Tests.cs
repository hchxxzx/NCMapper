// ============================================================================
// Red_S15 — 双引擎溢出行为分叉（P1 正确性）
//
// 缺陷（006 第 7 项 / 审计 20260827-005）：IsImplicitConvertible 不识别
// Nullable，Block 渲染（ConvertStrategy.cs:82→:97-99 ConvertValue 装箱）与 MemberInit
// 渲染（MapperBase.cs:617-620「值→可空」Convert 快路径分支，枚举对走 :621-623）分叉 ——
// 溢出行为分叉（审计承载：Convert(long,int)=-1 环绕 vs ConvertValue 抛 MapException ——
// 同一映射对双引擎结果不同）。
//
// 双引擎触发方式（执行期核实）：
//   - MemberInit 路径：类型对全部属性为简单映射（MapperBase.TryBuildMemberInit 快速路径）；
//   - Block 路径：同属性形态 + 追加一个集合属性（AssignmentKind.Collection → shouldFallback）
//     令整条映射回退 Block，枚举对经 TypeConvertHelper.ConvertValue 渲染。
// 溢出承载形态（执行期探明）：隐式转换表（TypeCompatibilityCore.ImplicitConversions）内
// 拓宽转换不溢出，long→int 等窄化不在表内（两引擎均 fallback，无分叉）；分叉缺陷的
// 可落红承载形态为「枚举 → 底层窄数值」（621-623 枚举分支保留 MemberInit 的
// unchecked Expression.Convert）—— 枚举底层值溢出目标域时静默环绕。
//
// 修复后期望（断言口径，006 R6 已定口径「统一为 ConvertValue
// 语义=显式异常」）：同一映射对 MemberInit 路径 vs Block 路径溢出行为一致 —— 均抛
// MapException（枚举底层值溢出目标域不再静默环绕）。
//
// 当前基线（e59f1c9）落红形态（留档实测）：两条路径对枚举底层值溢出均静默不抛
//（MemberInit：Expression.Convert enum→byte unchecked 环绕 255；Block：ConvertValue
// 枚举路径 Convert.ChangeType 亦未抛 OverflowException）→ Assert.Throws 断言失败 =
// 预期落红。修复后统一为「显式抛 MapException」口径（006 R6）。
// ============================================================================

using Xunit;

namespace NCMapper.Tests.Tests;

public class Red_S15_DualEngineOverflow
{
    [Fact]
    public void 枚举溢出窄数值_MemberInit与Block路径_行为一致均抛MapException()
    {
        // MemberInit 路径（类型对仅含简单映射属性 → TryBuildMemberInit 快速路径，
        // 枚举对经 :621-623 分支生成 Expression.Convert）：
        // 修复后期望：溢出抛 MapException（统一为 ConvertValue 语义，006 R6 口径）；
        // 当前形态（留档实测）：unchecked 环绕静默得错值 255（不抛）→ 落红
        Assert.Throws<MapException>(() =>
            Mapper.Map<S15MiSrc, S15MiDto>(new S15MiSrc { Value = S15Enum.Big }));

        // Block 路径（追加集合属性触发整条映射 shouldFallback → ConvertValue 渲染）：
        // 修复后期望：同样抛 MapException（双引擎行为统一的目标语义）；
        // 当前形态（留档实测）：枚举路径 Convert.ChangeType 亦静默不抛 → 落红
        Assert.Throws<MapException>(() =>
            Mapper.Map<S15BlkSrc, S15BlkDto>(new S15BlkSrc { Value = S15Enum.Big }));
    }

    // ---------- 模型类型（单文件自含） ----------

    /// <summary>底层值为 int.MaxValue 的枚举（底层值溢出 byte 目标域）。</summary>
    public enum S15Enum
    {
        Big = int.MaxValue,
    }

    /// <summary>MemberInit 路径类型对：仅简单映射属性（枚举→byte）。</summary>
    public sealed class S15MiSrc
    {
        public S15Enum Value { get; set; }
    }

    public sealed class S15MiDto
    {
        public byte Value { get; set; }
    }

    /// <summary>Block 路径类型对：同属性形态 + 集合属性（触发 shouldFallback → Block 引擎）。</summary>
    public sealed class S15BlkSrc
    {
        public S15Enum Value { get; set; }
        public List<int> Tags { get; set; } = new();
    }

    public sealed class S15BlkDto
    {
        public byte Value { get; set; }
        public List<int> Tags { get; set; } = new();
    }
}
