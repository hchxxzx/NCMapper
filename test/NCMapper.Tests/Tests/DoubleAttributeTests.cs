using NCMapper.Attributes;
using Xunit;

namespace NCMapper.Tests.Tests;

/// <summary>
/// 双标（[Convert] + [Mask] 共存）四象限守护测试 — W4S-3a（P1-6/P1-8/P2-5/P3-5）
/// <para>固化 P1-6 裁决（有序执行：先转换再脱敏）后的双引擎一致行为：
/// 生成侧 CodeGenerator.Mapping.cs L538-543 组合表达式 vs 运行时 ExpressionBuilder.cs L134-156 组合表达式。
/// 核心断言（P3-5）：脱敏对象必须为转换结果（dst.Prop == Mask(Convert(src))），而非原始源值。</para>
/// <para>路径说明：DoubleAttrDtoGen 带 [MapFrom] → 走源生成器（生成侧）；DoubleAttrDtoRt 无 [MapFrom] → 走运行时表达式树。</para>
/// </summary>
public class DoubleAttributeTests
{
    // ===== 生成侧（SourceGen）双标四象限 =====

    /// <summary>
    /// 生成侧双标：先转换再脱敏 — 脱敏对象为转换结果（P3-5 核心断言）
    /// <para>源值 "PREFIX-13812345678" → 转换器提取数字段 → "13812345678" → MaskPhone 脱敏 → "138****5678"。
    /// 若实现错误（Mask 输入为原始源值），断言失败。</para>
    /// </summary>
    [Fact]
    public void DoubleAttr_SourceGen_Both_ConvertThenMask()
    {
        var entity = new DoubleAttrEntity { Phone = "PREFIX-13812345678" };
        var dto = Mapper.Map<DoubleAttrEntity, DoubleAttrDtoGen>(entity)!;

        // 转换结果（提取数字段）再脱敏：13812345678 → 138****5678
        Assert.Equal("138****5678", dto.Phone);
    }

    /// <summary>
    /// 生成侧单标（仅 ConvertUsing）：转换结果原样输出（不受双标改动影响）
    /// </summary>
    [Fact]
    public void DoubleAttr_SourceGen_ConvertOnly()
    {
        var entity = new DoubleAttrEntity { Phone = "PREFIX-13900000000" };
        var dto = Mapper.Map<DoubleAttrEntity, DoubleAttrDtoGenConvertOnly>(entity)!;

        Assert.Equal("13900000000", dto.Phone);
    }

    /// <summary>
    /// 生成侧单标（仅 Mask）：Mask 输入为原始源值（现状行为不变）
    /// </summary>
    [Fact]
    public void DoubleAttr_SourceGen_MaskOnly()
    {
        var entity = new DoubleAttrEntity { Phone = "13711112222" };
        var dto = Mapper.Map<DoubleAttrEntity, DoubleAttrDtoGenMaskOnly>(entity)!;

        Assert.Equal("137****2222", dto.Phone);
    }

    // ===== 运行时（ExpressionBuilder）双标四象限 =====

    // 发现#6：双 false 象限"反向不消费 [Convert]"行为随 T5 锚门语义退役
    //（该象限 = 无锚运行时专属现状，019 拦截域；承载侧等价行为由 SourceGen 系用例锁定）

    // 发现#6：双 false 象限"反向不消费 [Convert]"行为随 T5 锚门语义退役
    //（该象限 = 无锚运行时专属现状，019 拦截域；承载侧等价行为由 SourceGen 系用例锁定）

    /// <summary>
    /// 运行时单标（仅 Mask）：Mask 输入为原始源值（现状行为不变）
    /// </summary>
    [Fact]
    public void DoubleAttr_Runtime_MaskOnly()
    {
        var entity = new DoubleAttrEntity { Phone = "13711112222" };
        var dto = Mapper.Map<DoubleAttrEntity, DoubleAttrDtoRtMaskOnly>(entity)!;

        Assert.Equal("137****2222", dto.Phone);
    }

    /// <summary>
    /// 双标类型转换器（静态方法形态 T1 单值模板）— 从源值中提取数字段（模拟"规范化"转换器）
    /// <para>输入 "PREFIX-13812345678" → 输出 "13812345678"；若输入无数字段，输出空串。</para>
    /// </summary>
    public static class DoubleAttrPhoneConverter
    {
        /// <summary>
        /// 提取源值中的连续数字段
        /// </summary>
        /// <param name="fieldValue">源属性值（可能带前缀）</param>
        /// <returns>提取后的数字字符串</returns>
        public static string Convert(string fieldValue)
        {
            var digits = new string(fieldValue?.Where(char.IsDigit).ToArray() ?? Array.Empty<char>());
            return digits;
        }
    }

    /// <summary>
    /// 双标测试源实体（无 [MapFrom] 标注，双 false 象限）
    /// </summary>
    public class DoubleAttrEntity
    {
        /// <summary>
        /// 手机号字段（带前缀，模拟非规范化源数据）
        /// </summary>
        public string Phone { get; set; } = "";
    }

    /// <summary>
    /// 双标生成侧 DTO — 规范位置（[MapFrom] 标 DTO）
    /// </summary>
    [MapFrom(typeof(DoubleAttrEntity))]
    public class DoubleAttrDtoGen
    {
        /// <summary>
        /// 双标属性（生成侧）：先转换再脱敏
        /// </summary>
        [Convert(typeof(DoubleAttrPhoneConverter), nameof(DoubleAttrPhoneConverter.Convert))]
        [Mask(typeof(MaskPhone))]
        public string Phone { get; set; } = "";
    }

    /// <summary>
    /// 双标运行时 DTO — 双 false 象限（无 [MapFrom]，走运行时路径）
    /// </summary>
    [MapFrom(typeof(DoubleAttrEntity))] // 复核：承载侧合法形态（原"双 false 象限"前提随 T5 锚门语义退役）
    public class DoubleAttrDtoRt
    {
        /// <summary>
        /// 双标属性（运行时）：先转换再脱敏
        /// </summary>
        [Convert(typeof(DoubleAttrPhoneConverter), nameof(DoubleAttrPhoneConverter.Convert))]
        [Mask(typeof(MaskPhone))]
        public string Phone { get; set; } = "";
    }

    /// <summary>
    /// 生成侧单标 DTO（仅 [Convert]）— 验证双标改动不影响单标路径
    /// </summary>
    [MapFrom(typeof(DoubleAttrEntity))]
    public class DoubleAttrDtoGenConvertOnly
    {
        /// <summary>
        /// 仅转换属性
        /// </summary>
        [Convert(typeof(DoubleAttrPhoneConverter), nameof(DoubleAttrPhoneConverter.Convert))]
        public string Phone { get; set; } = "";
    }

    /// <summary>
    /// 生成侧单标 DTO（仅 [Mask]）— 验证双标改动不影响单标路径
    /// </summary>
    [MapFrom(typeof(DoubleAttrEntity))]
    public class DoubleAttrDtoGenMaskOnly
    {
        /// <summary>
        /// 仅脱敏属性
        /// </summary>
        [Mask(typeof(MaskPhone))]
        public string Phone { get; set; } = "";
    }

    /// <summary>
    /// 运行时单标 DTO（仅 [Convert]）— 双 false 象限
    /// </summary>
    [MapFrom(typeof(DoubleAttrEntity))] // T5 复核：Plan 直调输入的承载侧合法形态
    public class DoubleAttrDtoRtConvertOnly
    {
        /// <summary>
        /// 仅转换属性
        /// </summary>
        [Convert(typeof(DoubleAttrPhoneConverter), nameof(DoubleAttrPhoneConverter.Convert))]
        public string Phone { get; set; } = "";
    }

    /// <summary>
    /// 运行时单标 DTO（仅 [Mask]）— 双 false 象限
    /// </summary>
    [MapFrom(typeof(DoubleAttrEntity))] // T5 复核：Plan 直调输入的承载侧合法形态
    public class DoubleAttrDtoRtMaskOnly
    {
        /// <summary>
        /// 仅脱敏属性
        /// </summary>
        [Mask(typeof(MaskPhone))]
        public string Phone { get; set; } = "";
    }
}
