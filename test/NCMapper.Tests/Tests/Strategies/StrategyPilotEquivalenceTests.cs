using System.Linq.Expressions;
using System.Reflection;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.Internal;
using NCMapper.Shared;
using Xunit;
using static System.Linq.Expressions.Expression;

namespace NCMapper.Tests.Tests.Strategies
{
    /// <summary>
    /// 试点策略类快照测（W2-1 ④ 策略化深化）。
    /// <para><b>形态演进</b>时为迁移期 RenderPlan 对照形态（策略产出 vs RenderPlan 产出
    /// ToString 等价断言，双全绿互证迁移正确性）；RenderPlan 删除后转为<b>策略产出快照测</b>
    /// （设计：快照 = 迁移期捕获的 ToString 串，时点转换、扩展至 12 策略
    /// 全矩阵）。</para>
    /// <para>Q1 断言成本结论（记录）：可控——每用例约 10 行样板（mock BuildSession +
    /// 策略调用 + 快照断言），无需手构造期望表达式树。</para>
    /// <para>三试点覆盖三种实现形态（方案书 v1.3）：DirectStrategy（无迁移）/ ConvertStrategy
    /// （方法体整体迁入）/ MaskStrategy（共享 helper internal 化消费）。</para>
    /// </summary>
    public class StrategyPilotEquivalenceTests
    {
        // ================ 测试模型（各用例独占，避免特性查询缓存/类型对交叉污染） ================

        /// <summary>Direct 用例模型</summary>
        public class DirectSrc { public int Id { get; set; } public string Name { get; set; } = ""; }
        /// <summary>Direct 用例模型</summary>
        public class DirectDto { public int Id { get; set; } public string Name { get; set; } = ""; }

        /// <summary>Convert 可空条件用例模型（int? → int）</summary>
        public class ConvertNullableSrc { public int? Value { get; set; } }
        /// <summary>Convert 可空条件用例模型</summary>
        public class ConvertNullableDto { public int Value { get; set; } }

        /// <summary>Convert 隐式转换用例模型（int → long）</summary>
        public class ConvertImplicitSrc { public int Value { get; set; } }
        /// <summary>Convert 隐式转换用例模型</summary>
        public class ConvertImplicitDto { public long Value { get; set; } }

        /// <summary>枚举→string 用例枚举</summary>
        public enum Color { Red, Green, Blue }

        /// <summary>Convert 枚举→string 用例模型</summary>
        public class ConvertEnumSrc { public Color Color { get; set; } }
        /// <summary>Convert 枚举→string 用例模型</summary>
        public class ConvertEnumDto { public string? Color { get; set; } }

        /// <summary>Mask 用例源模型</summary>
        public class MaskSrc { public string? Phone { get; set; } }

        /// <summary>Mask 用例目标模型（[Mask] 标注）</summary>
        [MapFrom(typeof(MaskSrc))] // T5 复核：Plan 直调输入的承载侧合法形态
        public class MaskDto
        {
            [Mask(typeof(TestMask))]
            public string? Phone { get; set; }
        }

        /// <summary>测试用 IMask 实现（固定替换为 ***，验证表达式构建而非脱敏算法）</summary>
        public class TestMask : IMask
        {
            public string Mask(string? value) => "***";
        }

        // ================ mock 会话构造 ================

        private static BuildSession BuildSession<TSrc, TTgt>(
            ParameterExpression srcParam, ParameterExpression tgtParam)
            => new()
            {
                SourceType = typeof(TSrc),
                TargetType = typeof(TTgt),
                SourceParameter = srcParam,
                TargetParameter = tgtParam,
                SourceProperties = new List<PropertyInfo>(),
                TargetProperties = new List<PropertyInfo>(),
                SourceIsDefine = false,
                TargetIsDefine = true,
                PathResolver = new TypePathResolver(),
            };

        // ================ DirectStrategy（无迁移形态） ================

        /// <summary>
        /// Direct：等类型属性直接赋值——快照（迁移期与 RenderPlan :395-397 分支产出 ToString 等价互证）。
        /// </summary>
        [Fact]
        public void DirectStrategy_MatchesSnapshot()
        {
            var srcParam = Parameter(typeof(DirectSrc), "src");
            var tgtParam = Parameter(typeof(DirectDto), "tgt");
            var srcProp = typeof(DirectSrc).GetProperty(nameof(DirectSrc.Id))!;
            var tgtProp = typeof(DirectDto).GetProperty(nameof(DirectDto.Id))!;

            var actual = new DirectStrategy().BuildExpression(
                BuildSession<DirectSrc, DirectDto>(srcParam, tgtParam), srcProp, tgtProp, null,
                new AssignmentPlan(AssignmentKind.Direct));

            Assert.NotNull(actual);
            Assert.Equal("(tgt.Id = src.Id)", actual!.ToString());
        }

        // ================ ConvertStrategy（方法体迁入形态） ================

        /// <summary>
        /// Convert 分支一：IsNullableConditional=true 可空条件转换——快照（迁移期与 RenderPlan
        /// :403 分支产出 ToString 等价互证）。
        /// </summary>
        [Fact]
        public void ConvertStrategy_NullableConditional_MatchesSnapshot()
        {
            var srcParam = Parameter(typeof(ConvertNullableSrc), "src");
            var tgtParam = Parameter(typeof(ConvertNullableDto), "tgt");
            var srcProp = typeof(ConvertNullableSrc).GetProperty(nameof(ConvertNullableSrc.Value))!;
            var tgtProp = typeof(ConvertNullableDto).GetProperty(nameof(ConvertNullableDto.Value))!;

            var actual = new ConvertStrategy().BuildExpression(
                BuildSession<ConvertNullableSrc, ConvertNullableDto>(srcParam, tgtParam), srcProp, tgtProp, null,
                new AssignmentPlan(AssignmentKind.Convert, IsNullableConditional: true));

            Assert.NotNull(actual);
            Assert.Equal("(tgt.Value = IIF((src.Value.HasValue == True), Convert(src.Value, Int32), default(Int32)))",
                actual!.ToString());
        }

        /// <summary>
        /// Convert 分支二：隐式基元转换 int→long——快照（迁移期与 RenderPlan :404 分支产出等价互证）。
        /// </summary>
        [Fact]
        public void ConvertStrategy_ImplicitConvert_MatchesSnapshot()
        {
            var srcParam = Parameter(typeof(ConvertImplicitSrc), "src");
            var tgtParam = Parameter(typeof(ConvertImplicitDto), "tgt");
            var srcProp = typeof(ConvertImplicitSrc).GetProperty(nameof(ConvertImplicitSrc.Value))!;
            var tgtProp = typeof(ConvertImplicitDto).GetProperty(nameof(ConvertImplicitDto.Value))!;

            var actual = new ConvertStrategy().BuildExpression(
                BuildSession<ConvertImplicitSrc, ConvertImplicitDto>(srcParam, tgtParam), srcProp, tgtProp, null,
                new AssignmentPlan(AssignmentKind.Convert));

            Assert.NotNull(actual);
            Assert.Equal("(tgt.Value = Convert(src.Value, Int64))", actual!.ToString());
        }

        /// <summary>
        /// Convert 分支三：枚举→string Switch——快照（迁移期与 RenderPlan :404 分支产出等价互证）。
        /// <para>注：SwitchExpression.ToString() 省略 case 明细（"switch (…) { ... }"）——快照为
        /// 粗粒度锚点；case 结构由 2863 行为测试（枚举映射值正确性）覆盖，测试网扩展结构断言。</para>
        /// </summary>
        [Fact]
        public void ConvertStrategy_EnumToString_MatchesSnapshot()
        {
            var srcParam = Parameter(typeof(ConvertEnumSrc), "src");
            var tgtParam = Parameter(typeof(ConvertEnumDto), "tgt");
            var srcProp = typeof(ConvertEnumSrc).GetProperty(nameof(ConvertEnumSrc.Color))!;
            var tgtProp = typeof(ConvertEnumDto).GetProperty(nameof(ConvertEnumDto.Color))!;

            var actual = new ConvertStrategy().BuildExpression(
                BuildSession<ConvertEnumSrc, ConvertEnumDto>(srcParam, tgtParam), srcProp, tgtProp, null,
                new AssignmentPlan(AssignmentKind.Convert));

            Assert.NotNull(actual);
            Assert.Equal("(tgt.Color = switch (src.Color) { ... })", actual!.ToString());
        }

        // ================ MaskStrategy（共享 helper internal 化消费形态） ================

        /// <summary>
        /// Mask：string 目标属性经 [Mask] 脱敏——快照（迁移期与 RenderPlan :449-458 分支产出
        /// ToString 等价互证；四参消费 ExpressionBuilder.BuildMaskAssignment + 缓存特性查询）。
        /// </summary>
        [Fact]
        public void MaskStrategy_MatchesSnapshot()
        {
            var srcParam = Parameter(typeof(MaskSrc), "src");
            var tgtParam = Parameter(typeof(MaskDto), "tgt");
            var srcProp = typeof(MaskSrc).GetProperty(nameof(MaskSrc.Phone))!;
            var tgtProp = typeof(MaskDto).GetProperty(nameof(MaskDto.Phone))!;

            var actual = new MaskStrategy().BuildExpression(
                BuildSession<MaskSrc, MaskDto>(srcParam, tgtParam), srcProp, tgtProp, null,
                new AssignmentPlan(AssignmentKind.Mask));

            Assert.NotNull(actual);
            Assert.Equal("(tgt.Phone = Resolve(NCMapper.Tests.Tests.Strategies.StrategyPilotEquivalenceTests+TestMask).Mask(src.Phone))",
                actual!.ToString());
        }

        /// <summary>
        /// Mask 边界：Plan 产 Mask Kind 但属性未标 [Mask]（内部不变式违约）——
        /// 策略抛 MapException（异常路径覆盖）。
        /// </summary>
        [Fact]
        public void MaskStrategy_MaskKindWithoutAttribute_Throws()
        {
            var srcParam = Parameter(typeof(DirectSrc), "src");
            var tgtParam = Parameter(typeof(DirectDto), "tgt");
            var srcProp = typeof(DirectSrc).GetProperty(nameof(DirectSrc.Name))!;
            var tgtProp = typeof(DirectDto).GetProperty(nameof(DirectDto.Name))!;

            var ex = Assert.Throws<MapException>(() => new MaskStrategy().BuildExpression(
                BuildSession<DirectSrc, DirectDto>(srcParam, tgtParam), srcProp, tgtProp, null,
                new AssignmentPlan(AssignmentKind.Mask)));
            // 异常经 ErrorMessages.ConverterExpressionNull 生成，消息指向目标属性名（zh/en 均含 'Name'）。
            // 断言验证"Mask 不变式违约"路径针对 Name 属性抛出；不依赖语言特定的 "Mask" 字样（en 分支不拼接
            // 中文 scenario），避免对运行环境语言的隐式依赖（en_US 下消息为 [Convert] Property 'Name'...）。
            Assert.Contains(nameof(DirectDto.Name), ex.Message);
        }

        // ================ 分发器接入核验 ================

        /// <summary>
        /// 策略数组完整性断言（评审 002-I-4）：12 Kind 全槽位命中对应策略类——
        /// 防 AssignmentKind 枚举扩展后数组硬编码越界静默返回 null（试点期该测试断言
        /// 未接入 Kind 返回 null，全量就位后升级为完整性形态）。
        /// </summary>
        [Fact]
        public void Resolver_AllTwelveStrategies_Wired()
        {
            Assert.IsType<DirectStrategy>(AssignmentStrategyResolver.Resolve(new AssignmentPlan(AssignmentKind.Direct)));
            Assert.IsType<ConvertStrategy>(AssignmentStrategyResolver.Resolve(new AssignmentPlan(AssignmentKind.Convert)));
            Assert.IsType<CollectionStrategy>(AssignmentStrategyResolver.Resolve(new AssignmentPlan(AssignmentKind.Collection)));
            Assert.IsType<NestedStrategy>(AssignmentStrategyResolver.Resolve(new AssignmentPlan(AssignmentKind.Nested)));
            Assert.IsType<IgnoreStrategy>(AssignmentStrategyResolver.Resolve(new AssignmentPlan(AssignmentKind.Ignore)));
            Assert.IsType<SkipStrategy>(AssignmentStrategyResolver.Resolve(new AssignmentPlan(AssignmentKind.Skip)));
            Assert.IsType<JsonStrategy>(AssignmentStrategyResolver.Resolve(new AssignmentPlan(AssignmentKind.Json)));
            Assert.IsType<CrossTableStrategy>(AssignmentStrategyResolver.Resolve(new AssignmentPlan(AssignmentKind.CrossTable)));
            Assert.IsType<ConvertUsingStrategy>(AssignmentStrategyResolver.Resolve(new AssignmentPlan(AssignmentKind.ConvertUsing)));
            Assert.IsType<MaskStrategy>(AssignmentStrategyResolver.Resolve(new AssignmentPlan(AssignmentKind.Mask)));
            Assert.IsType<ConvertUsingThenMaskStrategy>(AssignmentStrategyResolver.Resolve(new AssignmentPlan(AssignmentKind.ConvertUsingThenMask)));
            Assert.IsType<ConvertBackUsingStrategy>(AssignmentStrategyResolver.Resolve(new AssignmentPlan(AssignmentKind.ConvertBackUsing)));
            // Format 族两槽位（枚举扩值 + 策略注册同批——防护锚点语义不变，
            // 槽位数随 AssignmentKind 扩展同步 12→14；漏注册即本断言红）
            Assert.IsType<FormatStrategy>(AssignmentStrategyResolver.Resolve(new AssignmentPlan(AssignmentKind.Format)));
            Assert.IsType<FormatThenMaskStrategy>(AssignmentStrategyResolver.Resolve(new AssignmentPlan(AssignmentKind.FormatThenMask)));

            // 完整性：Kind 枚举值数 == 策略数组长度（14）——枚举扩展越界静默返回 null 的防护锚点
            Assert.Equal(14, Enum.GetValues<AssignmentKind>().Length);
        }

        /// <summary>
        /// 防御壳行为：IgnoreStrategy/SkipStrategy.BuildExpression 恒返回 null（主循环提前
        /// continue 不进入分发，本断言锁定防御壳契约——Plan/分发器不一致时兜底跳过不抛错）。
        /// </summary>
        [Fact]
        public void IgnoreAndSkipStrategies_DefensiveShell_ReturnNull()
        {
            var srcParam = Parameter(typeof(DirectSrc), "src");
            var tgtParam = Parameter(typeof(DirectDto), "tgt");
            var srcProp = typeof(DirectSrc).GetProperty(nameof(DirectSrc.Id))!;
            var tgtProp = typeof(DirectDto).GetProperty(nameof(DirectDto.Id))!;

            Assert.Null(new IgnoreStrategy().BuildExpression(
                BuildSession<DirectSrc, DirectDto>(srcParam, tgtParam), srcProp, tgtProp, null,
                new AssignmentPlan(AssignmentKind.Ignore)));
            Assert.Null(new SkipStrategy().BuildExpression(
                BuildSession<DirectSrc, DirectDto>(srcParam, tgtParam), srcProp, tgtProp, null,
                new AssignmentPlan(AssignmentKind.Skip)));
        }
    }
}
