// ============================================================================
// Red_B04 — 非泛型集合派生类深拷贝返回空壳
//
// 缺陷（006 第 4 项）：DeepCopyCompiler（TryGetCollectionCopyMethod :338
// 「!type.IsGenericType → return false」等）非泛型集合派生类深拷贝返回空壳，
// 后续 Add 抛 NRE（实测 Count=0）。
//
// 机理（执行期核实）：OrderList : List<string> 自身非泛型类型（IsGenericType=false）→
// CompileCreator 对集合类型返回 null → TryGetCollectionCopyMethod 不命中 →
// CreateInstanceCore 兜底走 GetUninitializedObject（82-85）→ 跳过 List 内部数组初始化
// → Count==0，Add 抛 NullReferenceException。
//
// 修复后期望（断言口径，006）：
//   非泛型但实现 IEnumerable<T> 的派生集合补回退分支：拷贝 Count==2 且后续 Add 不抛 NRE。
//
// 当前基线（e59f1c9）落红形态（留档）：copy.Count==0（空壳）且 copy.Add(...) 抛
// NullReferenceException → 断言失败 = 预期落红。
// ============================================================================

using Xunit;

namespace NCMapper.Tests.Tests;

public class Red_B04_NonGenericCollectionDerivative
{
    [Fact]
    public void 非泛型集合派生类深拷贝_元素数一致且后续Add不抛NRE()
    {
        var src = new B04OrderList { "first", "second" };

        // 修复后期望：拷贝 Count==2 且后续 Add 不抛 NRE；
        // 当前形态（留档）：GetUninitializedObject 空壳（Count==0），Add 抛 NRE → 落红
        var copy = Mapper.DeepCopy(src)!;

        Assert.Equal(2, copy.Count);
        Assert.Equal("first", copy[0]);
        Assert.Equal("second", copy[1]);

        copy.Add("third");            // 当前形态：NRE（空壳内部数组未初始化）→ 落红
        Assert.Equal(3, copy.Count);  // 修复后期望：Add 成功后计数递增
    }

    /// <summary>非泛型集合派生类（006 的 OrderList 形态：继承泛型基类、自身非泛型）。</summary>
    private sealed class B04OrderList : List<string>;
}
