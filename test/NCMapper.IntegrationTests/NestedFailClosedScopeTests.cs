// 复现审查者 finding #1 的真实形态：反射直调 BuildCompose，单跳嵌套源不在 join 参数集
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NCMapper.Attributes;
using Xunit;

namespace NCMapper.IntegrationTests
{
    public class F1Customer { public int Id { get; set; } public string? Name { get; set; } }
    public class F1Unrelated { public int Id { get; set; } public string? Tag { get; set; } }

    public class F1Order
    {
        public int Id { get; set; }
        public int CustomerId { get; set; }
        /// <summary>导航：单跳指向 F1Customer（该类型不进 join 参数集）。</summary>
        public F1Customer? Customer { get; set; }
    }

    /// <summary>★ 必须带 [MapFrom] —— 否则不进嵌套投影分支，测不到改指逻辑。</summary>
    [MapFrom(typeof(F1Customer))]
    public class F1CustomerDto { public int Id { get; set; } public string? Name { get; set; } }

    /// <summary>单层嵌套 DTO：Customer 的源 F1Customer 不在 (F1Order, F1Unrelated) 参数集内。</summary>
    [MapFrom(typeof(F1Order))]
    public class F1OrderDto
    {
        public int Id { get; set; }
        /// <summary>显式声明映射源 → 会进 BuildNestedProjection，触发改指逻辑。</summary>
        [MapProperty(typeof(F1Order), nameof(F1Order.Customer))]
        public F1CustomerDto? Customer { get; set; }
    }

    /// <summary>
    /// ★ 防「修复过度」专项（复审 finding #1）：单跳嵌套、源未 join 的形态**不得** fail-closed。
    /// <para>背景：FreeSql 对单跳导航自建 JOIN 完全支持，这类查询在收窄前能正常返回业务值；
    /// 若 fail-closed 不按嵌套跳数收窄，会把「原来能用」的代码变成抛异常——
    /// 而仓库降级网（<c>FreeSqlDegradationDetector</c>）并不把「源未 join」列为降级项，
    /// 用户会既拿不到投影、也拿不到降级，只剩异常。</para>
    /// <para>为什么用反射直调：需要精确构造「源类型不在 join 参数集」的形态，
    /// 走公开查询 API 会被其它校验先行拦下，测不到 <c>RedirectNestedSource</c> 的分支。</para>
    /// </summary>
    public class NestedFailClosedScopeTests
    {
        [Fact]
        public void SingleHop_SourceNotJoined_MustNotFailClosed()
        {
            var asm = Assembly.Load("NCMapper.FreeSql");
            var t = asm.GetType("NCMapper.FreeSql.FreeSqlProjectionBuilder");
            var m = t?.GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                .FirstOrDefault(x => x.Name == "BuildCompose" && x.GetGenericArguments().Length == 3);
            var gm = m!.MakeGenericMethod(typeof(F1Order), typeof(F1Unrelated), typeof(F1OrderDto));

            // 不抛异常即通过（单跳导航交给 provider 自建 JOIN，属可用形态）
            var expr = Record.Exception(() => gm.Invoke(null, null));
            Assert.Null(expr);

            var built = gm.Invoke(null, null);
            Assert.NotNull(built);
            Assert.Contains("Customer", built.ToString());
        }
    }
}


