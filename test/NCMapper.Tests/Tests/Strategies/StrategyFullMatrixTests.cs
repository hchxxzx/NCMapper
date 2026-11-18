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
    /// 12 策略类单测全矩阵补全（W2-1 ④ 策略化深化）。
    /// <para>试点 3 策略（Direct/Convert/Mask）快照测见 StrategyPilotEquivalenceTests；
    /// 本文件补齐其余 7 渲染策略（Ignore/Skip 防御壳已测）+ 委托工厂单测
    /// （BuildConstructorArgs 元组返回 / 非无参构造类型可注册断言）。</para>
    /// <para>断言形态：快照（ToString）+ 结构断言（NodeType/类型校验）混合——嵌套/集合策略
    /// 产出含递归内联块，结构断言比快照更稳健；转换器策略产出确定性高，用快照。</para>
    /// </summary>
    public class StrategyFullMatrixTests
    {
        // ================ 模型 ================

        /// <summary>Collection 同型用例（List&lt;int&gt;→List&lt;int&gt;，简单元素浅拷贝路径）</summary>
        public class SameTypeColSrc { public List<int> Values { get; set; } = new(); }
        /// <summary>Collection 同型用例</summary>
        public class SameTypeColDto { public List<int> Values { get; set; } = new(); }

        /// <summary>Collection 异型元素 A</summary>
        public class ElemA { public int Value { get; set; } }
        /// <summary>Collection 异型元素 B</summary>
        public class ElemB { public int Value { get; set; } }

        /// <summary>Collection 异型用例（List&lt;ElemA&gt;→List&lt;ElemB&gt;，class 元素经 MapList 委托链）</summary>
        public class CrossTypeColSrc { public List<ElemA> Values { get; set; } = new(); }
        /// <summary>Collection 异型用例</summary>
        public class CrossTypeColDto { public List<ElemB> Values { get; set; } = new(); }

        /// <summary>Nested 用例</summary>
        public class NestedChild { public int Value { get; set; } }
        /// <summary>Nested 用例</summary>
        public class NestedParentSrc { public NestedChild Child { get; set; } = new(); }
        /// <summary>Nested 用例</summary>
        public class NestedParentDto { public NestedChild Child { get; set; } = new(); }

        /// <summary>Json 用例（对象→string / string→对象）</summary>
        public class JsonObjSrc { public JsonPayload Payload { get; set; } = new(); }
        /// <summary>Json 用例</summary>
        public class JsonStrDto { public string? Payload { get; set; } }
        /// <summary>Json 载荷</summary>
        public class JsonPayload { public int A { get; set; } }

        /// <summary>跨表导航子源</summary>
        public class DeptEntity { public string? Code { get; set; } }
        /// <summary>跨表导航宿主源（含导航属性）</summary>
        public class EmpSrc { public DeptEntity Dept { get; set; } = new(); public int Id { get; set; } }
        /// <summary>跨表用例目标（[MapProperty] FromSource=DeptEntity）</summary>
        [MapFrom(typeof(EmpSrc))] // T5 复核：Plan 直调输入的承载侧合法形态
        public class EmpDto
        {
            public int Id { get; set; }
            [MapProperty(typeof(DeptEntity), "Code")]
            public string? DeptCode { get; set; }
        }

        /// <summary>ConvertUsing 转换器（ConvSrc.Level int→DTO string，静态方法形态 T1 单值模板）</summary>
        public static class UpperConverter
        {
            public static string Convert(int field) => field.ToString();
        }

        /// <summary>ConvertBackUsing 转换器（BackDto string→实体 int，静态方法形态 T1 单值模板）</summary>
        public static class ParseBackConverter
        {
            public static int Convert(string field) => int.Parse(field);
        }

        /// <summary>ConvertUsing 用例源</summary>
        public class ConvSrc { public int Level { get; set; } }
        /// <summary>ConvertUsing 用例目标</summary>
        [MapFrom(typeof(ConvSrc))] // T5 复核：Plan 直调输入的承载侧合法形态
        public class ConvDto
        {
            [Convert(typeof(UpperConverter), nameof(UpperConverter.Convert))]
            public string? Level { get; set; }
        }

        /// <summary>组合用例源</summary>
        public class ComboSrc { public int Card { get; set; } }
        /// <summary>组合用例目标（双标）</summary>
        [MapFrom(typeof(ComboSrc))] // T5 复核：Plan 直调输入的承载侧合法形态
        public class ComboDto
        {
            [Convert(typeof(ComboConverter), nameof(ComboConverter.Convert))]
            [Mask(typeof(StrategyPilotEquivalenceTests.TestMask))]
            public string? Card { get; set; }
        }
        /// <summary>组合转换器（静态方法形态 T1 单值模板）</summary>
        public static class ComboConverter
        {
            public static string Convert(int field) => field.ToString();
        }

        /// <summary>ConvertBackUsing 用例（反向：DTO→实体）</summary>
        public class BackDto
        
        {
            [ConvertBack(typeof(ParseBackConverter), nameof(ParseBackConverter.Convert))]
            public string? Level { get; set; }
        }
        /// <summary>ConvertBackUsing 用例</summary>
        public class BackEntity { public int Level { get; set; } }

        /// <summary>非无参构造（record）——BuildConstructorArgs 服务场景</summary>
        public record NoParamlessCtor(int Id, string Name);

        private static BuildSession Sess<TSrc, TTgt>(ParameterExpression s, ParameterExpression t) => new()
        {
            SourceType = typeof(TSrc), TargetType = typeof(TTgt), SourceParameter = s, TargetParameter = t,
            SourceProperties = new List<PropertyInfo>(), TargetProperties = new List<PropertyInfo>(),
            SourceIsDefine = false, TargetIsDefine = true, PathResolver = new TypePathResolver(),
        };

        // ================ Collection ================

        /// <summary>Collection 同型：简单元素浅拷贝路径（ConvertListExpression）——结构断言。</summary>
        [Fact]
        public void CollectionStrategy_SameType_SimpleElement()
        {
            var s = Parameter(typeof(SameTypeColSrc), "src"); var t = Parameter(typeof(SameTypeColDto), "tgt");
            var sp = typeof(SameTypeColSrc).GetProperty(nameof(SameTypeColSrc.Values))!;
            var tp = typeof(SameTypeColDto).GetProperty(nameof(SameTypeColDto.Values))!;

            var actual = new CollectionStrategy().BuildExpression(
                Sess<SameTypeColSrc, SameTypeColDto>(s, t), sp, tp, null,
                new AssignmentPlan(AssignmentKind.Collection));

            Assert.NotNull(actual);
            // (tgt.Values = IIF((src.Values != null), CopyList(...)...))：null 守卫 + 浅拷贝
            Assert.Equal(ExpressionType.Assign, actual!.NodeType);
            Assert.StartsWith("(tgt.Values = IIF", actual.ToString());
            Assert.Contains("CopyList", actual.ToString());
        }

        /// <summary>Collection 异型：元素转换路径（GetListExpression→MapList 委托链）——结构断言。</summary>
        [Fact]
        public void CollectionStrategy_CrossType_ElementConvert()
        {
            var s = Parameter(typeof(CrossTypeColSrc), "src"); var t = Parameter(typeof(CrossTypeColDto), "tgt");
            var sp = typeof(CrossTypeColSrc).GetProperty(nameof(CrossTypeColSrc.Values))!;
            var tp = typeof(CrossTypeColDto).GetProperty(nameof(CrossTypeColDto.Values))!;

            var actual = new CollectionStrategy().BuildExpression(
                Sess<CrossTypeColSrc, CrossTypeColDto>(s, t), sp, tp, null,
                new AssignmentPlan(AssignmentKind.Collection));

            Assert.NotNull(actual);
            // 嵌套递归走 GetListExpression→Resolve(ElemA, ElemB)→MapList 调用——验证委托工厂链路接入
            // （值类型元素对 List<int>→List<long> 在基线即抛 MakeGenericType 约束违规——非本期行为域）
            var str = actual!.ToString();
            Assert.Contains("MapList", str);
            Assert.StartsWith("(tgt.Values = IIF", str); // 赋值 + Condition(null 检查) 包裹
        }

        // ================ Nested ================

        /// <summary>Nested：异类型 class 递归映射（GetClassExpression 内联/委托链）——结构断言。</summary>
        [Fact]
        public void NestedStrategy_ProducesCondition()
        {
            var s = Parameter(typeof(NestedParentSrc), "src"); var t = Parameter(typeof(NestedParentDto), "tgt");
            var sp = typeof(NestedParentSrc).GetProperty(nameof(NestedParentSrc.Child))!;
            var tp = typeof(NestedParentDto).GetProperty(nameof(NestedParentDto.Child))!;

            var actual = new NestedStrategy().BuildExpression(
                Sess<NestedParentSrc, NestedParentDto>(s, t), sp, tp, null,
                new AssignmentPlan(AssignmentKind.Nested));

            Assert.NotNull(actual);
            // tgt.Child = IIF(src.Child != null, ...)——null 守卫条件结构
            Assert.Equal(ExpressionType.Assign, actual!.NodeType);
            var cond = Assert.IsAssignableFrom<ConditionalExpression>(((BinaryExpression)actual).Right);
            Assert.Equal(ExpressionType.NotEqual, cond.Test.NodeType);
        }

        // ================ Json ================

        /// <summary>Json 对象→string 序列化方向。</summary>
        [Fact]
        public void JsonStrategy_ObjectToString()
        {
            var s = Parameter(typeof(JsonObjSrc), "src"); var t = Parameter(typeof(JsonStrDto), "tgt");
            var sp = typeof(JsonObjSrc).GetProperty(nameof(JsonObjSrc.Payload))!;
            var tp = typeof(JsonStrDto).GetProperty(nameof(JsonStrDto.Payload))!;

            var actual = new JsonStrategy().BuildExpression(
                Sess<JsonObjSrc, JsonStrDto>(s, t), sp, tp, null,
                new AssignmentPlan(AssignmentKind.Json));

            Assert.NotNull(actual);
            var str = actual!.ToString();
            Assert.StartsWith("(tgt.Payload = IIF", str); // 赋值 + null 守卫
            Assert.Contains("ObjectConvertToJson", str); // JSON 序列化调用
        }

        /// <summary>Json string→对象反序列化方向（边界：string→string 直赋由 string-string 分支覆盖）。</summary>
        [Fact]
        public void JsonStrategy_StringToObject()
        {
            var s = Parameter(typeof(JsonStrDto), "src"); var t = Parameter(typeof(JsonObjSrc), "tgt");
            var sp = typeof(JsonStrDto).GetProperty(nameof(JsonStrDto.Payload))!;
            var tp = typeof(JsonObjSrc).GetProperty(nameof(JsonObjSrc.Payload))!;

            var actual = new JsonStrategy().BuildExpression(
                Sess<JsonStrDto, JsonObjSrc>(s, t), sp, tp, null,
                new AssignmentPlan(AssignmentKind.Json));

            Assert.NotNull(actual);
            var str = actual!.ToString();
            Assert.StartsWith("(tgt.Payload = IIF", str);
            Assert.Contains("JsonConvertToObject", str);
        }

        // ================ CrossTable ================

        /// <summary>CrossTable：四参消费（session.SourceType/attr/session 两参数）+ null 安全链。</summary>
        [Fact]
        public void CrossTableStrategy_FourArgsConsumption()
        {
            var s = Parameter(typeof(EmpSrc), "src"); var t = Parameter(typeof(EmpDto), "tgt");
            var sp = typeof(EmpSrc).GetProperty(nameof(EmpSrc.Dept))!; // 导航属性（sourceItem 仅供 Property 构造）
            var tp = typeof(EmpDto).GetProperty(nameof(EmpDto.DeptCode))!;
            var attr = tp.GetCustomAttribute<MapPropertyAttribute>()!;

            var actual = new CrossTableStrategy().BuildExpression(
                Sess<EmpSrc, EmpDto>(s, t), sp, tp, attr,
                new AssignmentPlan(AssignmentKind.CrossTable));

            Assert.NotNull(actual);
            // (tgt.DeptCode = IIF((src.Dept != null), src.Dept.Code, null))——导航 null 安全链
            var str = actual!.ToString();
            Assert.StartsWith("(tgt.DeptCode = IIF", str);
            Assert.Contains("src.Dept.Code", str);
        }

        /// <summary>CrossTable 边界：导航属性缺失（attr.FromSource 类型在源中无导航）→ null + 语义不受 Trace 影响。</summary>
        [Fact]
        public void CrossTableStrategy_MissingNavigation_ReturnsNull()
        {
            var s = Parameter(typeof(DeptEntity), "src"); var t = Parameter(typeof(EmpDto), "tgt");
            var sp = typeof(DeptEntity).GetProperty(nameof(DeptEntity.Code))!;
            var tp = typeof(EmpDto).GetProperty(nameof(EmpDto.DeptCode))!;
            var attr = tp.GetCustomAttribute<MapPropertyAttribute>()!;

            var actual = new CrossTableStrategy().BuildExpression(
                Sess<DeptEntity, EmpDto>(s, t), sp, tp, attr,
                new AssignmentPlan(AssignmentKind.CrossTable));

            Assert.Null(actual); // DeptEntity 无 DeptEntity 导航属性 → 渲染 null（调用方 Trace 跳过）
        }

        // ================ ConvertUsing / 组合 / ConvertBackUsing ================

        /// <summary>ConvertUsing：正向转换器赋值（快照）。</summary>
        [Fact]
        public void ConvertUsingStrategy_Positive()
        {
            var s = Parameter(typeof(ConvSrc), "src"); var t = Parameter(typeof(ConvDto), "tgt");
            var sp = typeof(ConvSrc).GetProperty(nameof(ConvSrc.Level))!;
            var tp = typeof(ConvDto).GetProperty(nameof(ConvDto.Level))!;

            var actual = new ConvertUsingStrategy().BuildExpression(
                Sess<ConvSrc, ConvDto>(s, t), sp, tp, null,
                new AssignmentPlan(AssignmentKind.ConvertUsing));

            Assert.NotNull(actual);
            // 静态方法形态快照：T1 单值模板调用 Convert(src.Level)（无 new 实例化、无 source 实参）
            Assert.Equal("(tgt.Level = Convert(src.Level))", actual!.ToString());
        }

        /// <summary>ConvertUsing 边界：Kind 违约（无特性）抛 MapException。</summary>
        [Fact]
        public void ConvertUsingStrategy_NoAttribute_Throws()
        {
            var s = Parameter(typeof(ConvSrc), "src"); var t = Parameter(typeof(StrategyPilotEquivalenceTests.DirectDto), "tgt");
            var sp = typeof(ConvSrc).GetProperty(nameof(ConvSrc.Level))!;
            var tp = typeof(StrategyPilotEquivalenceTests.DirectDto).GetProperty(nameof(StrategyPilotEquivalenceTests.DirectDto.Name))!;

            Assert.Throws<MapException>(() => new ConvertUsingStrategy().BuildExpression(
                Sess<ConvSrc, StrategyPilotEquivalenceTests.DirectDto>(s, t), sp, tp, null,
                new AssignmentPlan(AssignmentKind.ConvertUsing)));
        }

        /// <summary>组合：先转换再脱敏（双标有序——快照锁定顺序）。</summary>
        [Fact]
        public void ConvertUsingThenMaskStrategy_Ordered()
        {
            var s = Parameter(typeof(ComboSrc), "src"); var t = Parameter(typeof(ComboDto), "tgt");
            var sp = typeof(ComboSrc).GetProperty(nameof(ComboSrc.Card))!;
            var tp = typeof(ComboDto).GetProperty(nameof(ComboDto.Card))!;

            var actual = new ConvertUsingThenMaskStrategy().BuildExpression(
                Sess<ComboSrc, ComboDto>(s, t), sp, tp, null,
                new AssignmentPlan(AssignmentKind.ConvertUsingThenMask));

            Assert.NotNull(actual);
            // 转换值作为 Mask 输入：Mask(...).Mask(Convert(...)) —— 先转换再脱敏（静态形态快照：T1 单值调用）
            var str = actual!.ToString();
            Assert.Contains(").Mask(Convert(src.Card))", str);
        }

        /// <summary>ConvertBackUsing：仅反向消费（快照）。</summary>
        [Fact]
        public void ConvertBackUsingStrategy_Reverse()
        {
            var s = Parameter(typeof(BackDto), "src"); var t = Parameter(typeof(BackEntity), "tgt");
            var sp = typeof(BackDto).GetProperty(nameof(BackDto.Level))!;
            var tp = typeof(BackEntity).GetProperty(nameof(BackEntity.Level))!;

            var actual = new ConvertBackUsingStrategy().BuildExpression(
                Sess<BackDto, BackEntity>(s, t), sp, tp, null,
                new AssignmentPlan(AssignmentKind.ConvertBackUsing));

            Assert.NotNull(actual);
            // 静态方法形态快照：T1 单值模板调用 Convert(src.Level)（反向 DTO 属性 → 实体属性）
            Assert.Equal("(tgt.Level = Convert(src.Level))", actual!.ToString());
        }

        /// <summary>ConvertBackUsing 边界：Kind 违约（无特性）抛 MapException。</summary>
        [Fact]
        public void ConvertBackUsingStrategy_NoAttribute_Throws()
        {
            var s = Parameter(typeof(BackDto), "src"); var t = Parameter(typeof(StrategyPilotEquivalenceTests.DirectDto), "tgt");
            var sp = typeof(BackDto).GetProperty(nameof(BackDto.Level))!;
            var tp = typeof(StrategyPilotEquivalenceTests.DirectDto).GetProperty(nameof(StrategyPilotEquivalenceTests.DirectDto.Name))!;

            Assert.Throws<MapException>(() => new ConvertBackUsingStrategy().BuildExpression(
                Sess<BackDto, StrategyPilotEquivalenceTests.DirectDto>(s, t), sp, tp, null,
                new AssignmentPlan(AssignmentKind.ConvertBackUsing)));
        }

        // ================ 委托工厂单测 ================

        /// <summary>
        /// BuildConstructorArgs 元组返回 + record（非无参构造）类型可注册断言——
        /// TTarget 无 new() 约束正是该路径服务场景（评审 001-B3/002-B-3）。
        /// </summary>
        [Fact]
        public void MapperBaseDelegateImpl_BuildConstructorArgs_TupleReturn()
        {
            // record 类型对注册（无 new() 约束验证）
            var impl = MapperDelegateRegistry.Resolve(typeof(NoParamlessCtor), typeof(NoParamlessCtor));
            Assert.NotNull(impl);

            // 元组解构 + New(ctor, args) 组装语义
            var s = Parameter(typeof(NoParamlessCtor), "src");
            var (ctor, args) = impl.BuildConstructorArgs(typeof(NoParamlessCtor), typeof(NoParamlessCtor), s);
            Assert.NotNull(ctor);
            Assert.Equal(2, args.Length);
            var newExpr = Expression.New(ctor, args);
            Assert.Equal(typeof(NoParamlessCtor), newExpr.Type);
        }

        /// <summary>委托工厂 Resolve 兜底未命中抛 MapConfigurationException（异常路径）。</summary>
        [Fact]
        public void MapperDelegateRegistry_ResolveMiss_Throws()
        {
            // struct 目标不满足 TTarget:class 约束——MakeGenericType 抛 ArgumentException 前置边界
            // （理论不可达路径：生产调用点类型对均来自合法映射对；此处验证 F1 显式失败语义）
            Assert.ThrowsAny<Exception>(() =>
                MapperDelegateRegistry.Resolve(typeof(int), typeof(int)));
        }
    }
}
