// ============================================================================
// Red_B02 — 占位实例泄漏：环回边引用空壳
//
// 缺陷（006 第 2 项）：DeepCopyProviderBase.DeepCopy<T>(T,ctx)（135-150）占位实例
// 泄漏 —— record 无参构造缺失类型走占位路径后，环上引用最终指向 GetUninitializedObject
// 产生的空壳占位（实测环回边对象 Name/Next 均为 null），而非最终拷贝实例。
//
// 修复后期望（断言口径，006）：
//   构造完成后将 result 的属性回拷到 placeholder（保持环引用一致），始终返回 placeholder。
//   双向环 a↔b：copy.Next.Next == copy（环回边引用一致）且 Name 完整。
//
// 当前基线（e59f1c9）落红形态（留档）：copy.Next.Next 为空壳占位实例（≠ copy，
// 其 Name/Next 均为 null）→ 引用一致断言与 Name 完整断言失败 = 预期落红。
// ============================================================================

using Xunit;

namespace NCMapper.Tests.Tests;

public class Red_B02_PlaceholderLeak
{
    [Fact]
    public void 双向环深拷贝_环回边引用一致且属性完整()
    {
        var a = new B02Node("a", null!);
        var b = new B02Node("b", a); // b.Next = a（构造时）
        a.Next = b;                  // 双向环闭合：a↔b

        // 修复后期望：占位回拷后环引用一致、Name 完整；
        // 当前形态（留档）：copy.Next.Next 为空壳占位（Name=null、Next=null）→ 落红
        var copy = Mapper.DeepCopy(a)!;

        Assert.Equal("a", copy.Name);
        Assert.Equal("b", copy.Next!.Name);
        Assert.Same(copy, copy.Next!.Next);       // 环回边引用一致（当前指向空壳占位 → 落红）
        Assert.Equal("a", copy.Next!.Next!.Name); // 环上 Name 完整（当前为 null → 落红）
    }

    /// <summary>
    /// 仅带参构造器的引用类型（无无参公共构造器 → 深拷贝走 ProviderBase :135-150 占位路径）；
    /// 构造器参数 name/next 与属性按名匹配（TryCompileRecordCreator 递归 DeepCopy 构参），
    /// Next 保留 setter 以便构造后闭合双向环。
    /// </summary>
    private sealed class B02Node(string name, B02Node? next)
    {
        public string Name { get; } = name;
        public B02Node? Next { get; set; } = next;
    }
}
