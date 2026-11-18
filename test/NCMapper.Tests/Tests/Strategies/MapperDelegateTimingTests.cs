using System.Linq.Expressions;
using NCMapper;
using NCMapper.Internal;
using Xunit;

namespace NCMapper.Tests.Tests.Strategies
{
    /// <summary>
    /// Q5 前置 PoC — MapperBase 委托工厂注册时序 4 项验证（W2-1 ④ 策略化深化）。
    /// <para> Q5 / 委托时序测试矩阵（v1.3 计数断言）：
    /// ① 自举路径——顶层类型对首次 Map 前注册已生效（断言 RegisterCount 增量）；
    /// ② 嵌套内联——从未触及的嵌套属性类型对 Resolve 经 RunClassConstructor 兜底命中（断言 BootstrapCount 触发）；
    /// ③ 集合元素——未注册元素类型对 Resolve 兜底命中；
    /// ④ 兜底重试——注册中心清空后 Resolve 触发 .cctor 注册 + 类型初始化仅一次的幂等性（计数关系断言）。</para>
    /// <para>计数器语义（MapperDelegateRegistry）：RegisterCount 每次 Register +1（全进程噪声——
    /// 并行测试类的映射亦触发 cctor 注册，仅可作 ≥ 增量断言）；ResolveHitCount 字典命中 +1；
    /// BootstrapCount 兜底执行 +1（阶段生产代码无 Resolve 调用点，仅本测试类触达——
    /// 本类内测试串行执行，BootstrapCount 增量为确定性值）。</para>
    /// <para>注：阶段 3 处反射回指点尚未接入 Resolve（改造），故 ②③ 以
    /// MapperDelegateRegistry.Resolve 直调验证兜底机制（P3 前提：RunClassConstructor 兜底可覆盖
    /// 嵌套/集合元素类型对懒加载场景）；测试④的 Clear() 在接入后需复核测试间影响。</para>
    /// </summary>
    public class MapperDelegateTimingTests
    {
        // —— 专用模型类型：各测试独占类型对，保证本测试进程内从未被触及（MapperBase cctor 未跑、注册中心未命中）——

        /// <summary>测试① 专用：顶层映射类型对</summary>
        public class TopLevelSrc1 { public int Id { get; set; } public string Name { get; set; } = ""; }
        /// <summary>测试① 专用：顶层映射类型对</summary>
        public class TopLevelDto1 { public int Id { get; set; } public string Name { get; set; } = ""; }

        /// <summary>测试② 专用：模拟嵌套属性类型对</summary>
        public class NestedSrc2 { public int Value { get; set; } }
        /// <summary>测试② 专用：模拟嵌套属性类型对</summary>
        public class NestedDto2 { public int Value { get; set; } }

        /// <summary>测试③ 专用：模拟集合元素类型对</summary>
        public class ElemSrc3 { public int Value { get; set; } }
        /// <summary>测试③ 专用：模拟集合元素类型对</summary>
        public class ElemDto3 { public int Value { get; set; } }

        /// <summary>测试④ 专用：幂等性验证类型对</summary>
        public class IdempotentSrc4 { public int Value { get; set; } }
        /// <summary>测试④ 专用：幂等性验证类型对</summary>
        public class IdempotentDto4 { public int Value { get; set; } }

        /// <summary>
        /// 测试①：顶层类型对首次 Map 前注册已生效——Mapper.Map 触发 MapperBase&lt;S,T&gt;
        /// 显式 .cctor（Register 前置首行），断言 RegisterCount 增量。
        /// </summary>
        [Fact]
        public void Q5_1_TopLevelPair_RegisteredOnFirstMap()
        {
            var src = new TopLevelSrc1 { Id = 42, Name = "q5" };
            long before = MapperDelegateRegistry.RegisterCount;

            // 首次 Map：MapperBase<TopLevelSrc1, TopLevelDto1> cctor 运行（先于 Map 方法体执行）
            var result = Mapper.Map<TopLevelSrc1, TopLevelDto1>(src);

            Assert.NotNull(result);
            Assert.Equal(42, result!.Id);
            Assert.Equal("q5", result.Name);
            // RegisterCount 增量 ≥ 1（并行测试类的映射可能带来额外增量，≥ 断言对噪声鲁棒）
            Assert.True(MapperDelegateRegistry.RegisterCount > before,
                $"RegisterCount 未随首次 Map 增加（before={before}, after={MapperDelegateRegistry.RegisterCount}）——.cctor 注册时序违约");
        }

        /// <summary>
        /// 测试②：从未触及的嵌套属性类型对 Resolve 经 RunClassConstructor 兜底命中
        /// （类型对 cctor 从未运行——编译期不触达，兜底为唯一注册触发路径），断言 BootstrapCount 触发。
        /// </summary>
        [Fact]
        public void Q5_2_NestedPair_BootstrapFallbackHit()
        {
            long beforeBootstrap = MapperDelegateRegistry.BootstrapCount;

            // 直调 Resolve 模拟 GetClassExpression Fallback 路径（接入点）按实际类型对查询
            var impl = MapperDelegateRegistry.Resolve(typeof(NestedSrc2), typeof(NestedDto2));

            Assert.NotNull(impl);
            Assert.True(MapperDelegateRegistry.BootstrapCount > beforeBootstrap,
                $"BootstrapCount 未触发（before={beforeBootstrap}, after={MapperDelegateRegistry.BootstrapCount}）——兜底路径未执行");

            // 兜底命中的委托可用性：MapFallback 产出 MapperBase<S,T>.Map(src, null) 调用表达式
            var srcParam = Expression.Parameter(typeof(NestedSrc2), "src");
            var fallback = impl.MapFallback(srcParam);
            // IsAssignableFrom：.NET 对 ≤2 参调用返回内部子类 MethodCallExpression2（非精确类型）
            var call = Assert.IsAssignableFrom<MethodCallExpression>(fallback);
            Assert.Equal("Map", call.Method.Name);
            Assert.Equal(typeof(MapperBase<NestedSrc2, NestedDto2>), call.Method.DeclaringType);
        }

        /// <summary>
        /// 测试③：未注册集合元素类型对 Resolve 兜底命中（GetListExpression 集合递归路径的元素类型对），
        /// 断言 BootstrapCount 触发 + MapList 委托可用。
        /// </summary>
        [Fact]
        public void Q5_3_CollectionElementPair_BootstrapFallbackHit()
        {
            long beforeBootstrap = MapperDelegateRegistry.BootstrapCount;

            var impl = MapperDelegateRegistry.Resolve(typeof(ElemSrc3), typeof(ElemDto3));

            Assert.NotNull(impl);
            Assert.True(MapperDelegateRegistry.BootstrapCount > beforeBootstrap,
                $"BootstrapCount 未触发（before={beforeBootstrap}, after={MapperDelegateRegistry.BootstrapCount}）——兜底路径未执行");

            // 兜底命中的委托可用性：MapList 产出 MapperBase<S,T>.MapList(source, null) 调用表达式
            var srcParam = Expression.Parameter(typeof(List<ElemSrc3>), "src");
            var mapListCall = impl.MapList(srcParam);
            // IsAssignableFrom：.NET 对 ≤2 参调用返回内部子类 MethodCallExpression2（非精确类型）
            var call = Assert.IsAssignableFrom<MethodCallExpression>(mapListCall);
            Assert.Equal("MapList", call.Method.Name);
            Assert.Equal(typeof(MapperBase<ElemSrc3, ElemDto3>), call.Method.DeclaringType);
        }

        /// <summary>
        /// 测试④：注册中心清空后 Resolve 触发 .cctor 注册（未初始化类型对）+
        /// 类型初始化仅一次的幂等性（已初始化类型对清空后 Resolve 不重注册、兜底未命中显式失败）+
        /// 计数关系断言（BootstrapCount 增量 == Resolve 调用数；RegisterCount 增量仅来自首次 cctor）。
        /// </summary>
        [Fact]
        public void Q5_4_ClearThenResolve_BootstrapIdempotency()
        {
            long beforeBootstrap = MapperDelegateRegistry.BootstrapCount;
            long beforeRegister = MapperDelegateRegistry.RegisterCount;

            // 阶段 A：清空后 Resolve 未初始化类型对 → RunClassConstructor 触发 cctor → 注册 → 命中
            MapperDelegateRegistry.Clear();
            var impl = MapperDelegateRegistry.Resolve(typeof(IdempotentSrc4), typeof(IdempotentDto4));
            Assert.NotNull(impl);

            // 阶段 B：再次清空后 Resolve 同一类型对 → cctor 已运行（类型初始化仅一次，不重注册）
            // → 兜底重试未命中 → 显式抛 MapConfigurationException（幂等性边界：Clear 为破坏性测试操作）
            MapperDelegateRegistry.Clear();
            var ex = Assert.Throws<MapConfigurationException>(
                () => MapperDelegateRegistry.Resolve(typeof(IdempotentSrc4), typeof(IdempotentDto4)));
            Assert.Contains("IdempotentSrc4", ex.Message);

            // 阶段 C：手动 Register 恢复（生产不依赖——仅验证注册中心可修复 + 留下可用状态）
            MapperDelegateRegistry.Register<IdempotentSrc4, IdempotentDto4>(
                new MapperBaseDelegateImpl<IdempotentSrc4, IdempotentDto4>());
            Assert.NotNull(MapperDelegateRegistry.Resolve(typeof(IdempotentSrc4), typeof(IdempotentDto4)));

            // 计数关系断言：本测试 2 次 Resolve 字典未命中（阶段 A/B 各 1 次兜底）+ 阶段 C 命中不兜底
            // （阶段 C 的 Resolve 走字典命中，BootstrapCount 不增）——增量 == 2 为确定性值
            // （BootstrapCount 仅本测试类触达，类内串行无噪声）
            Assert.Equal(2, MapperDelegateRegistry.BootstrapCount - beforeBootstrap);
            // RegisterCount：本测试确定性贡献 1（阶段 A 的 cctor 注册；阶段 B 不重注册）+ 阶段 C 手动 1 = 2；
            // 并行测试类可能带来额外增量，故断言 ≥ 2（精确上界由阶段 B 抛异常间接证明——未发生第 2 次 cctor 注册）
            Assert.True(MapperDelegateRegistry.RegisterCount - beforeRegister >= 2,
                $"RegisterCount 增量 < 2（before={beforeRegister}, after={MapperDelegateRegistry.RegisterCount}）");
        }
    }
}
