// ============================================================================
// P1Fix_T002 — 批次 2，双引擎统一 + 配置回退
// 验收用例（A1/A2 双引擎行为一致；溢出口径由 P0RedBaseline_S15 红测承载，
// 配置保持由 P0RedBaseline_S23 红测承载——P0RedBaseline_* 系列已退役，
// 红测形态记录见册）。
//
// 缺陷（006 审计 20260827-005）：IsImplicitConvertible 不剥壳 Nullable →
// int?→long? 等可空对判 false → Block 渲染落 ConvertValue 装箱慢路径（ConvertStrategy
// :97-99），MemberInit 走"值→可空"Convert 快路径（MapperBase :636-638）→ 双引擎分叉。
//
// 修复后（剥壳归一）：双侧引擎同走 Expression.Convert（A1 无 ConvertValue 装箱由代码
// 路径收敛保证——IsImplicitConvertible(剥壳) true 时 ConvertStrategy :82 / MapperBase
// :634 均生成 Convert 而非 ConvertValue 调用）；行为断言双引擎值语义一致。
// ============================================================================

using Xunit;

namespace NCMapper.Tests.Tests;

public class P1Fix_T002_DualEngineConfig
{
    // ============================== A1/A2 ==============================

    /// <summary>
    /// A1：int?→long?（可空对可空对）双引擎（MemberInit vs Block）行为一致——
    /// 剥壳归一后双引擎均走 Expression.Convert（HasValue 拓宽 / null 回落默认）。
    /// </summary>
    [Fact]
    public void S15_A1_IntNullable转LongNullable_双引擎行为一致()
    {
        // MemberInit 路径（仅简单属性 → TryBuildMemberInit 快路径）
        var mi1 = Mapper.Map<T2MiSrc, T2MiDto>(new T2MiSrc { Value = 5 });
        Assert.Equal(5L, mi1!.Value);
        var mi2 = Mapper.Map<T2MiSrc, T2MiDto>(new T2MiSrc { Value = null });
        Assert.Null(mi2!.Value); // 可空对可空：null 语义保持 null

        // Block 路径（集合属性先声明触发 shouldFallback → 整条回退 Block 渲染）
        var blk1 = Mapper.Map<T2BlkSrc, T2BlkDto>(new T2BlkSrc { Value = 5 });
        Assert.Equal(5L, blk1!.Value);
        var blk2 = Mapper.Map<T2BlkSrc, T2BlkDto>(new T2BlkSrc { Value = null });
        Assert.Null(blk2!.Value); // 双引擎 null 语义一致
    }

    /// <summary>
    /// A2：int→long?（值→可空）双引擎行为一致——剥壳后隐式表命中
    ///（int→long），Block 侧原 ConvertValue 装箱路径收敛为 Convert 直转。
    /// </summary>
    [Fact]
    public void S15_A2_Int转LongNullable_双引擎行为一致()
    {
        var mi = Mapper.Map<T2ValSrc, T2ValDto>(new T2ValSrc { Value = 7 });
        Assert.Equal(7L, mi!.Value);
        Assert.Equal(0L, Mapper.Map<T2ValSrc, T2ValDto>(new T2ValSrc { Value = 0 })!.Value);

        var blk = Mapper.Map<T2ValBlkSrc, T2ValBlkDto>(new T2ValBlkSrc { Value = 7 });
        Assert.Equal(7L, blk!.Value);
    }

    /// <summary>
    /// A2 补充：int?→long? 隐式对与溢出口径的边界——long?→int?（窄化，不在隐式表）
    /// 双引擎一致（Block 侧 ConvertValue 运行时转换，MemberInit 侧 shouldFallback 同走
    /// Block——窄化对 MemberInit 无快路径，两引擎同路径同语义）。
    /// </summary>
    [Fact]
    public void S15_A2_LongNullable转IntNullable_窄化对双引擎一致()
    {
        var mi = Mapper.Map<T2NarrowSrc, T2NarrowDto>(new T2NarrowSrc { Value = 42L });
        Assert.Equal(42, mi!.Value);

        var blk = Mapper.Map<T2NarrowBlkSrc, T2NarrowBlkDto>(new T2NarrowBlkSrc { Value = 42L });
        Assert.Equal(42, blk!.Value);
    }

    // ============================== 模型（public：IsNotPublic 嵌套类型绑定跳过，见诊断）==============================

    public sealed class T2MiSrc
    {
        public int? Value { get; set; }
    }

    public sealed class T2MiDto
    {
        public long? Value { get; set; }
    }

    public sealed class T2BlkSrc
    {
        public List<int> Tags { get; set; } = new();
        public int? Value { get; set; }
    }

    public sealed class T2BlkDto
    {
        public List<int> Tags { get; set; } = new();
        public long? Value { get; set; }
    }

    public sealed class T2ValSrc
    {
        public int Value { get; set; }
    }

    public sealed class T2ValDto
    {
        public long? Value { get; set; }
    }

    public sealed class T2ValBlkSrc
    {
        public List<int> Tags { get; set; } = new();
        public int Value { get; set; }
    }

    public sealed class T2ValBlkDto
    {
        public List<int> Tags { get; set; } = new();
        public long? Value { get; set; }
    }

    public sealed class T2NarrowSrc
    {
        public long? Value { get; set; }
    }

    public sealed class T2NarrowDto
    {
        public int? Value { get; set; }
    }

    public sealed class T2NarrowBlkSrc
    {
        public List<int> Tags { get; set; } = new();
        public long? Value { get; set; }
    }

    public sealed class T2NarrowBlkDto
    {
        public List<int> Tags { get; set; } = new();
        public int? Value { get; set; }
    }
}