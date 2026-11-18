using Xunit;
using NCMapper.Tests.Models;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// 异常路径测试 — 补充覆盖无公共构造器、源类型不匹配等异常场景
    /// </summary>
    [Collection("DeepCopyGlobalState")]
    public class ExceptionPathTests
    {
        /// <summary>
        /// 无 [MapFrom] 标注的类型调用 Compose 抛异常
        /// </summary>
        [Fact]
        public void Compose_Without_MapFrom_Throws_InvalidOperation()
        {
            var source = new NoMapFromProjClass();
            Assert.ThrowsAny<InvalidOperationException>(() =>
                Mapper.Compose<NoMapFromProjClass>(source));
        }

        /// <summary>
        /// Compose 主源缺失时抛异常
        /// </summary>
        [Fact]
        public void Compose_Primary_Source_Missing_Throws_InvalidOperation()
        {
            var customer = new DictSourceB { NameB = "C1", ValueB = 10 };
            Assert.ThrowsAny<InvalidOperationException>(() =>
                Mapper.Compose<DictTargetDto>(customer));
        }

        /// <summary>
        /// MaxRecursionDepth 设置超过上界时被限制
        /// </summary>
        [Fact]
        public void MaxRecursionDepth_Upper_Bound_Enforced()
        {
            var original = Mapper.MaxRecursionDepth;
            try
            {
                // 设置超过上界 → 应被限制为上界值
                Mapper.MaxRecursionDepth = int.MaxValue;
                Assert.Equal(Mapper.MaxRecursionDepthUpperBound, Mapper.MaxRecursionDepth);

                // 设置小于 1 → 应被限制为 1
                Mapper.MaxRecursionDepth = 0;
                Assert.Equal(1, Mapper.MaxRecursionDepth);

                // 设置正常值 → 应原样保存
                Mapper.MaxRecursionDepth = 128;
                Assert.Equal(128, Mapper.MaxRecursionDepth);
            }
            finally
            {
                Mapper.MaxRecursionDepth = original;
            }
        }

        /// <summary>
        /// MaxRecursionDepth 设置为 1 时浅层映射正常工作
        /// </summary>
        [Fact]
        public void MaxRecursionDepth_One_Shallow_Map_OK()
        {
            var original = Mapper.MaxRecursionDepth;
            try
            {
                Mapper.MaxRecursionDepth = 1;
                var entity = new FullTypeEntity { IntProp = 42, StringProp = "Test" };
                var dto = Mapper.Map<FullTypeEntity, SameTypeDto>(entity)!;
                Assert.Equal(42, dto.IntProp);
                Assert.Equal("Test", dto.StringProp);
            }
            finally
            {
                Mapper.MaxRecursionDepth = original;
            }
        }

        /// <summary>
        /// Compose 空数组返回 null
        /// </summary>
        [Fact]
        public void Compose_Empty_Sources_Returns_Null()
        {
            var result = Mapper.Compose<DictTargetDto>();
            Assert.Null(result);
        }

        /// <summary>
        /// Compose 全 null 源 — 主源缺失抛异常
        /// </summary>
        [Fact]
        public void Compose_All_Null_Sources_Throws_Primary_Missing()
        {
            // DictTargetDto 有 [MapFrom] 标注，有源生成器注册
            // 全 null sources → 主源找不到 → 抛异常
            Assert.ThrowsAny<InvalidOperationException>(() =>
                Mapper.Compose<DictTargetDto>((object?)null!, (object?)null!));
        }

        // ================================================================
        // T06: 接口/抽象类/struct 作为映射目标的负面测试
        // ================================================================

        /// <summary>
        /// 接口作为映射目标 — 接口无法实例化，运行时引擎应抛出异常
        /// <para>ITargetInterface 满足 class 泛型约束（接口是引用类型），但 Expression.New 无法创建接口实例</para>
        /// </summary>
        [Fact]
        public void Map_Interface_As_Target_Throws()
        {
            var entity = new FullTypeEntity { IntProp = 42, StringProp = "test" };

            // 接口无法实例化 → 运行时引擎构建表达式树时 Expression.New 失败
            Assert.ThrowsAny<Exception>(() =>
                Mapper.Map<FullTypeEntity, ITargetInterface>(entity));
        }

        /// <summary>
        /// 抽象类作为映射目标 — 抽象类无法实例化，运行时引擎应抛出异常
        /// <para>AbstractTargetBase 满足 class 泛型约束，但 new AbstractTargetBase() 在运行时不可用</para>
        /// </summary>
        [Fact]
        public void Map_AbstractClass_As_Target_Throws()
        {
            var entity = new FullTypeEntity { IntProp = 42, StringProp = "test" };

            // 抽象类无法实例化 → 运行时引擎构建表达式树时失败
            Assert.ThrowsAny<Exception>(() =>
                Mapper.Map<FullTypeEntity, AbstractTargetBase>(entity));
        }

        /// <summary>
        /// struct 类型被 Mapper.Map 的 class 泛型约束排除 — 编译时即可阻止
        /// <para>Mapper.Map&lt;TSource, TTarget&gt; 声明 where TTarget : class，struct 不满足此约束</para>
        /// <para>以下代码无法通过编译（注释保留以示设计意图）：</para>
        /// <code>
        /// // Mapper.Map&lt;FullTypeEntity, StructTarget&gt;(entity);  // CS0452: 值类型不满足 class 约束
        /// </code>
        /// </summary>
        [Fact]
        public void Struct_Target_Excluded_By_Class_Constraint()
        {
            // 验证 Mapper.Map<TSource, TTarget> 的 TTarget 有 class 约束
            // struct 类型 StructTarget 不满足此约束，编译时被排除
            var mapMethod = typeof(Mapper).GetMethod("Map",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                null,
                new[] { typeof(object), typeof(System.Action<,>) },
                null);

            // 使用泛型方法定义验证 class 约束
            var methods = typeof(Mapper).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            var mapGenericMethods = methods.Where(m => m.Name == "Map" && m.IsGenericMethod && m.GetGenericArguments().Length == 2).ToList();

            // 至少有一个 Map<TSource, TTarget> 泛型方法
            Assert.NotEmpty(mapGenericMethods);

            // 验证 TTarget 参数有 class 约束
            var mapMethod2Generic = mapGenericMethods.First();
            var genericArgs = mapMethod2Generic.GetGenericArguments();
            var targetParam = genericArgs[1]; // TTarget
            var constraints = targetParam.GenericParameterAttributes;

            Assert.True(
                (constraints & System.Reflection.GenericParameterAttributes.ReferenceTypeConstraint) != 0,
                "TTarget 应有 class (ReferenceTypeConstraint) 泛型约束，阻止 struct 作为映射目标");
        }

        /// <summary>
        /// MapValidator.ValidateMapping 对接口目标类型不崩溃
        /// <para>接口有属性但无法实例化，验证器应能正常分析其属性列表并返回结果</para>
        /// </summary>
        [Fact]
        public void ValidateMapping_Interface_Target_Does_Not_Throw()
        {
            // 验证器只做静态属性分析，不实例化目标类型
            var result = MapValidator.ValidateMapping(typeof(FullTypeEntity), typeof(ITargetInterface));

            Assert.NotNull(result);
            Assert.True(result.TotalCount > 0, "接口有可写属性，TotalCount 应 > 0");
            Assert.Equal(typeof(ITargetInterface), result.TargetType);
        }

        /// <summary>
        /// MapValidator.ValidateMapping 对抽象类目标类型不崩溃
        /// <para>抽象类有属性但无法实例化，验证器应能正常分析其属性列表并返回结果</para>
        /// </summary>
        [Fact]
        public void ValidateMapping_AbstractClass_Target_Does_Not_Throw()
        {
            var result = MapValidator.ValidateMapping(typeof(FullTypeEntity), typeof(AbstractTargetBase));

            Assert.NotNull(result);
            Assert.True(result.TotalCount > 0, "抽象类有可写属性，TotalCount 应 > 0");
            Assert.Equal(typeof(AbstractTargetBase), result.TargetType);
        }
    }

    /// <summary>无 [MapFrom] 标注的测试类 — 用于验证 Compose 异常路径</summary>
    public class NoMapFromProjClass
    {
        /// <summary>ID</summary>
        public int Id { get; set; }
        /// <summary>名称</summary>
        public string? Name { get; set; }
    }
}
