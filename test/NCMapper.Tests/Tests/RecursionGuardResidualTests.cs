using NCMapper;
using Xunit;

namespace NCMapper.Tests.Tests;

/// <summary>
/// 双计数器残留回归测试（C18 补测 + 修订）
/// <para>报告 §五 C18 断言形态：同类型对残留处置 + 跨类型对隔离 SUCCESS。
/// MapperBase 双计数器（s_depthIn/s_depthOut，MapperBase.cs:134-148）为泛型实例
/// [ThreadStatic]：B3 修复前异常后差值残留（in+1 但 out 不+1），可被同类型对操纵放大
/// （安全审计 A-002-F2，累积性故障）；修复后 ExitRecursion 移入 finally，
/// 异常也配对、差值不残留（修订断言——原"放大触发"用例编码缺陷行为）。
/// 跨类型对因泛型隔离始终不受影响。</para>
/// <para>本类修改全局 <see cref="Mapper.MaxRecursionDepth"/>，须与 StressTests 等
/// 改全局状态的测试串行（DeepCopyGlobalState 集合），避免并行竞态（验收发现）。</para>
/// </summary>
[Collection("DeepCopyGlobalState")]
public class RecursionGuardResidualTests
{
    /// <summary>
    /// 被测源类型 A — 含非法转换属性（制造异常残留）与自引用链
    /// </summary>
    private class AmpSrc
    {
        /// <summary>
        /// 标识字段
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 类型不匹配字段（"x" 非整数 → 转换异常）
        /// </summary>
        public string Bad { get; set; } = "";

        /// <summary>
        /// 自引用子节点
        /// </summary>
        public AmpSrc? Child { get; set; }
    }

    /// <summary>
    /// 被测目标类型 A — Bad 为 int（与源 string 不匹配）
    /// </summary>
    private class AmpTgt
    {
        /// <summary>
        /// 标识字段
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 目标 int 字段
        /// </summary>
        public int Bad { get; set; }

        /// <summary>
        /// 子节点
        /// </summary>
        public AmpTgt? Child { get; set; }
    }

    /// <summary>
    /// 隔离对照类型 B（独立泛型 ThreadStatic 计数器）
    /// </summary>
    private class BmpSrc
    {
        /// <summary>
        /// 标识字段
        /// </summary>
        public int Id { get; set; }
    }

    /// <summary>
    /// 隔离对照目标类型 B
    /// </summary>
    private class BmpTgt
    {
        /// <summary>
        /// 标识字段
        /// </summary>
        public int Id { get; set; }
    }

    /// <summary>
    /// 同类型对残留不再放大（修订）— B3 修复后异常也经 finally 配对
    /// <para>MaxRecursionDepth=5；连续以非法值（Bad 转换失败）对类型对 A 调用 Map：
    /// 每次异常 in/out 均配对（ExitRecursion 已移入 finally），差值不累积；
    /// 守卫不应提前触发（单次调用真实深度仅 1，远小于上限 5）。
    /// <b>修订说明</b>：原用例断言"残留放大触发守卫"编码了 B3 缺陷行为（异常后差值永久 +1，
    /// 累积性故障——报告-20260818-010 B3），修复后该行为消除；改为
    /// 验证"不放大 + 正常映射不受误伤"（MapperBase.cs:173-175/205-207 修复）。</para>
    /// </summary>
    [Fact]
    public void RecursionGuard_SameTypePair_NoResidualAmplification_MapSucceeds()
    {
        int original = Mapper.MaxRecursionDepth;
        Mapper.MaxRecursionDepth = 5;
        try
        {
            // B3 修复后：深度 1 的异常调用连续执行，差值不累积，守卫不提前触发
            MapException? guardException = null;
            for (int i = 0; i < Mapper.MaxRecursionDepth + 5; i++)
            {
                var ex = Record.Exception(() =>
                    Mapper.Map<AmpSrc, AmpTgt>(new AmpSrc { Id = 1, Bad = "not-an-int" }));
                Assert.NotNull(ex);

                // 守卫异常（消息含递归深度语义）与转换异常（FormatException 等）区分
                if (ex is MapException me &&
                    (me.Message.Contains("递归", StringComparison.Ordinal) ||
                     me.Message.Contains("recursion", StringComparison.OrdinalIgnoreCase)))
                {
                    guardException = me;
                    break;
                }
            }

            // 不放大实证：残留已消除（异常也配对），守卫不提前触发
            Assert.Null(guardException);

            // 正常映射不受误伤：B 类型对（独立计数器）浅映射 SUCCESS
            var dto = Mapper.Map<BmpSrc, BmpTgt>(new BmpSrc { Id = 9 });
            Assert.NotNull(dto);
            Assert.Equal(9, dto!.Id);
        }
        finally
        {
            Mapper.MaxRecursionDepth = original;
        }
    }

    /// <summary>
    /// 跨类型对隔离 — 类型对 A 残留不影响类型对 B（泛型 ThreadStatic 独立）
    /// <para>A 残留 10 之后，B（独立计数器）浅映射 SUCCESS（差值 0），
    /// 证明残留被隔离在各自类型对的 [ThreadStatic] 槽内。</para>
    /// </summary>
    [Fact]
    public void RecursionGuard_CrossTypePair_Isolated_Succeeds()
    {
        int original = Mapper.MaxRecursionDepth;
        Mapper.MaxRecursionDepth = 5;
        try
        {
            // 先污染类型对 A（残留 MaxRecursionDepth 次，见放大测试同逻辑）
            for (int i = 0; i < Mapper.MaxRecursionDepth; i++)
            {
                Assert.NotNull(Record.Exception(() =>
                    Mapper.Map<AmpSrc, AmpTgt>(new AmpSrc { Id = 1, Bad = "bad" })));
            }

            // 类型对 B：独立计数器 → 正常 SUCCESS（无残留污染）
            var dto = Mapper.Map<BmpSrc, BmpTgt>(new BmpSrc { Id = 9 });
            Assert.NotNull(dto);
            Assert.Equal(9, dto!.Id);
        }
        finally
        {
            Mapper.MaxRecursionDepth = original;
        }
    }
}
