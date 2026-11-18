using System;
using System.Collections.Generic;
using System.Linq;
using NCMapper.Shared;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// 计划器一致性测试（测试 4）：
    /// ① AssignmentPlanner 决策表（12 规则执行定稿，含 M7 规则 2 前置）；
    /// ② 计划器驱动后的运行时行为等价（标准路径 BuildAssignmentForProperty + 快速路径 TryBuildSingleBinding），
    /// 值级断言覆盖 Direct/Convert/Collection/Nested 及可空条件转换；Json/跨表行为由既有
    /// JsonMappingTests/跨表测试全量回归兜底（改造前后全绿即等价）。
    /// </summary>
    [Collection("DeepCopyGlobalState")]
    public class AssignmentPlannerTests
    {
        // ---------- 决策表 ----------

        public class NestedA { public int X { get; set; } }
        public class NestedB { public int X { get; set; } }
        public class OtherSource { }

        public static TheoryData<Type, Type, bool, bool, bool, Type?, string> DecisionMatrix => new()
        {
            // 等类型 → Direct
            { typeof(int), typeof(int), false, false, false, null, "Direct" },
            { typeof(string), typeof(string), false, false, false, null, "Direct" },
            { typeof(NestedA), typeof(NestedA), false, false, false, null, "Direct" },
            // 类型不同 → Convert（D4 执行定稿：无条件，含无转换路径组合）
            { typeof(int), typeof(long), false, false, false, null, "Convert" },
            { typeof(string), typeof(int), false, false, false, null, "Convert" },
            { typeof(int), typeof(DateTime), false, false, false, null, "Convert" },
            // 可空（规则 7/8）
            { typeof(int?), typeof(int), false, false, false, null, "Convert" },   // IsNullableConditional=true
            { typeof(int), typeof(int?), false, false, false, null, "Convert" },
            { typeof(int?), typeof(long?), false, false, false, null, "Convert" },
            // 嵌套 → Nested（规则 3）
            { typeof(NestedA), typeof(NestedB), false, false, false, null, "Nested" },
            // 集合 → Collection（规则 4，同型/异型合并）
            { typeof(List<int>), typeof(List<int>), false, false, false, null, "Collection" },
            { typeof(List<int>), typeof(List<long>), false, false, false, null, "Collection" },
            { typeof(int[]), typeof(List<int>), false, false, false, null, "Collection" },
            // Json（规则 5）
            { typeof(string), typeof(NestedB), false, false, true, null, "Json" },
            // 跨表（规则 6）
            { typeof(string), typeof(string), false, false, false, typeof(OtherSource), "CrossTable" },
            // 全局忽略（规则 1）
            { typeof(int), typeof(int), false, false, false, null, "Ignore" },
        };

        [Theory]
        [MemberData(nameof(DecisionMatrix))]
        public void Planner_Decision_Table(Type source, Type target, bool hasConvertUsing, bool hasMask,
            bool isFromJson, Type? crossTableFromSource, string expectedKind)
        {
            var plan = AssignmentPlanner.Plan(
                source, target,
                hasConvertUsing: hasConvertUsing, hasMask: hasMask,
                isToDto: false, globallyIgnored: expectedKind == "Ignore",
                isFromJson: isFromJson, crossTableFromSource: crossTableFromSource);

            Assert.Equal(expectedKind, plan.Kind.ToString());
            if (expectedKind == "Convert" && source == typeof(int?) && target == typeof(int))
                Assert.True(plan.IsNullableConditional);
        }

        /// <summary>M7 关键场景：同类型 + [Convert] → Convert（规则 2 必须前置，禁止命中 Direct）。</summary>
        [Fact]
        public void Planner_ConvertUsing_Precedes_Direct()
        {
            var plan = AssignmentPlanner.Plan(
                typeof(string), typeof(string),
                hasConvertUsing: true, hasMask: false,
                isToDto: false, globallyIgnored: false,
                isFromJson: false, crossTableFromSource: null);
            Assert.Equal(AssignmentKind.Convert, plan.Kind);

            var maskPlan = AssignmentPlanner.Plan(
                typeof(string), typeof(string),
                hasConvertUsing: false, hasMask: true,
                isToDto: false, globallyIgnored: false,
                isFromJson: false, crossTableFromSource: null);
            Assert.Equal(AssignmentKind.Convert, maskPlan.Kind);
        }

        /// <summary>Json 优先于跨表/转换（规则 5 在规则 6 之前，:310-313 现状）。</summary>
        [Fact]
        public void Planner_Json_Precedes_CrossTable()
        {
            var plan = AssignmentPlanner.Plan(
                typeof(string), typeof(NestedB),
                hasConvertUsing: false, hasMask: false,
                isToDto: false, globallyIgnored: false,
                isFromJson: true, crossTableFromSource: typeof(OtherSource));
            Assert.Equal(AssignmentKind.Json, plan.Kind);
        }

        // ---------- 行为等价（计划器驱动后运行时值断言） ----------

        public class SimpleSource { public int Id { get; set; } public string Name { get; set; } = ""; }
        public class SimpleTarget { public int Id { get; set; } public string Name { get; set; } = ""; }

        public class NullableSource { public int? Count { get; set; } public int? NullCount { get; set; } }
        public class NullableTarget { public int Count { get; set; } public int? NullCount { get; set; } }

        public class NestedSource2 { public NestedA A { get; set; } = new(); }
        public class NestedTarget2 { public NestedB A { get; set; } = new(); }

        public class ListSource { public List<int> Items { get; set; } = new(); public NestedA[] Arr { get; set; } = Array.Empty<NestedA>(); }
        public class ListTarget { public List<int> Items { get; set; } = new(); public List<NestedB> Arr { get; set; } = new(); }

        [Fact]
        public void Runtime_Direct_Values_Correct()
        {
            var mapped = Mapper.Map<SimpleSource, SimpleTarget>(new SimpleSource { Id = 3, Name = "n" });
            Assert.NotNull(mapped);
            Assert.Equal(3, mapped!.Id);
            Assert.Equal("n", mapped.Name);
        }

        [Fact]
        public void Runtime_NullableConditional_Values_Correct()
        {
            var mapped = Mapper.Map<NullableSource, NullableTarget>(new NullableSource { Count = 5, NullCount = null });
            Assert.NotNull(mapped);
            Assert.Equal(5, mapped!.Count);      // 可空→非可空条件转换（规则 7 + IsNullableConditional）
            Assert.Null(mapped.NullCount);       // 非可空→可空（规则 8）
        }

        [Fact]
        public void Runtime_Nested_Values_Correct()
        {
            var mapped = Mapper.Map<NestedSource2, NestedTarget2>(new NestedSource2 { A = new NestedA { X = 9 } });
            Assert.NotNull(mapped);
            Assert.Equal(9, mapped!.A.X);
        }

        [Fact]
        public void Runtime_Collection_Values_Correct()
        {
            var mapped = Mapper.Map<ListSource, ListTarget>(new ListSource
            {
                Items = new List<int> { 1, 2 },
                Arr = new[] { new NestedA { X = 3 }, new NestedA { X = 4 } },
            });
            Assert.NotNull(mapped);
            Assert.Equal(new List<int> { 1, 2 }, mapped!.Items);   // 同型集合（规则 4 → BuildSameTypeCollectionAssignment）
            Assert.Equal(2, mapped.Arr.Count);                     // 异型集合（规则 4 → BuildCrossTypeAssignment 集合分支）
            Assert.Equal(3, mapped.Arr[0].X);
            Assert.Equal(4, mapped.Arr[1].X);
        }

        [Fact]
        public void Runtime_Convert_Values_Correct()
        {
            var mapped = Mapper.Map<ConvertSource, ConvertTarget>(new ConvertSource { Value = "123" });
            Assert.NotNull(mapped);
            Assert.Equal(123, mapped!.Value);
        }

        public class ConvertSource { public string Value { get; set; } = ""; }
        public class ConvertTarget { public int Value { get; set; } }

        // ---------- A2-02：动态类型直算不入缓存守卫 ----------

        /// <summary>
        /// 反射读 s_planCache 计数（private static——白盒断言面；ConcurrentDictionary 实现
        /// IDictionary，经非泛型接口取 Count 避免泛型实例化耦合）。
        /// </summary>
        private static System.Collections.IDictionary GetPlanCache() =>
            (System.Collections.IDictionary)(typeof(AssignmentPlanner)
                .GetField("s_planCache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                ?? throw new InvalidOperationException("s_planCache field missing"))
                .GetValue(null)!;

        /// <summary>动态程序集类型（Reflection.Emit；Assembly.IsDynamic=true）——A2-02 缓存键恒新
        /// 零命中场景的判定载体（PropertyMatcher.IsDynamicType 判据 :264 腿 1）。</summary>
        private static System.Reflection.PropertyInfo CreateDynamicProperty()
        {
            var asm = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
                new System.Reflection.AssemblyName("A202DynAsm"), System.Reflection.Emit.AssemblyBuilderAccess.Run);
            var mod = asm.DefineDynamicModule("A202DynMod");
            var tb = mod.DefineType("A202DynType", System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.Class);
            var mb = tb.DefineMethod("get_V", System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.SpecialName,
                typeof(int), Type.EmptyTypes);
            var il = mb.GetILGenerator();
            il.Emit(System.Reflection.Emit.OpCodes.Ldc_I4_7);
            il.Emit(System.Reflection.Emit.OpCodes.Ret);
            var pb = tb.DefineProperty("V", System.Reflection.PropertyAttributes.None, typeof(int), Type.EmptyTypes);
            pb.SetGetMethod(mb);
            var dynType = tb.CreateType()!;
            return dynType.GetProperty("V")!;
        }

        /// <summary>A2-02 主断言：动态类型两次 Plan → s_planCache.Count 不变（缺陷态守卫缺失：
        /// 每次构造新 PlanCacheKey 强键入缓存 → +2 红）；sourceItem == null 场景不 NRE 且不入缓存；
        /// 静态类型既有路径缓存命中行为不变（两次同对 Plan 仅 +1 条目）。</summary>
        [Fact]
        public void Plan_DynamicTypes_BypassCache_NullSourceSafe()
        {
            var cache = GetPlanCache();
            var dynProp = CreateDynamicProperty();
            var staticProp = typeof(NestedA).GetProperty("X")!;

            int before = cache.Count;
            // 动态对两次 Plan（缺陷态：+2 红）
            AssignmentPlanner.Plan(dynProp, dynProp, isToDto: true, attr: null, crossTableFromSource: null);
            AssignmentPlanner.Plan(dynProp, dynProp, isToDto: false, attr: null, crossTableFromSource: null);
            Assert.Equal(before, cache.Count);

            // null 源属性（无源属性场景）不 NRE、不入缓存
            AssignmentPlanner.Plan(null, staticProp, isToDto: true, attr: null, crossTableFromSource: null);
            Assert.Equal(before, cache.Count);

            // 静态类型既有路径：同对 Plan 两次仅 +1 条目（GetOrAdd 命中，回归防线）
            AssignmentPlanner.Plan(staticProp, staticProp, isToDto: true, attr: null, crossTableFromSource: null);
            int afterFirst = cache.Count;
            AssignmentPlanner.Plan(staticProp, staticProp, isToDto: true, attr: null, crossTableFromSource: null);
            Assert.Equal(afterFirst, cache.Count);
            Assert.Equal(before + 1, afterFirst);
        }
    }
}
