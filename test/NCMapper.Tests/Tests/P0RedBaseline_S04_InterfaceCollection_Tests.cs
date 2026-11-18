// ============================================================================
// Red_S04 — 接口集合深拷贝退化走 JsonSerializerBase 回退（P1 正确性，
//
// 缺陷（006 第 7 项 / 深拷贝·Json 回退路径）：深拷贝接口集合（IList<T>）
// 实测 6.1× 退化 —— CollectionCopier.CopyEnumerable（492-497 / :526-528）对接口类型
// 集合（无 IEnumerable<T> 构造器）回退 JsonSerializerBase 序列化中转。
//
// 修复后期望（断言口径，006）：
//   接口集合直接构造 List<T> 返回（结果类型收窄为深拷贝通行语义），不走
//   JsonSerializerBase 回退 —— 非公共 setter 属性（深拷贝 CompileFiller 按 CanWrite
//   绑定，internal setter 可写）不丢失。
//
// 当前基线（e59f1c9）落红形态（留档实测）：IList<T> 深拷贝走 CopyEnumerable 的 JSON 回退
// （526-528），System.Text.Json 反序列化无法绑定非公共 setter 属性 → copy 元素的
// Name 丢失（回落属性默认值 ""）→ 断言失败 = 预期落红。（JSON 回退的行为证据：STJ
// 仅绑定公共 setter，internal set 属性反序列化被跳过，而正确的深拷贝路径对此无损。）
// ============================================================================

using Xunit;

namespace NCMapper.Tests.Tests;

public class Red_S04_InterfaceCollectionFallback
{
    [Fact]
    public void IList接口集合深拷贝_不走Json回退_属性无丢失()
    {
        var src = new List<S04Item> { new() { Name = "keep-me" } };
        IList<S04Item> ifaceSrc = src;

        // 修复后期望：接口集合走构造 List<T> 的深拷贝通行语义（不走 JsonSerializerBase 回退），
        // 元素属性经 CompileFiller（CanRead && CanWrite，含 internal setter）完整填充；
        // 当前形态（留档实测）：CopyEnumerable JSON 回退 → STJ 反序列化无法绑定 internal setter
        // 属性 → copy[0].Name 丢失（回落属性默认值 ""）→ 落红
        var copy = Mapper.DeepCopy(ifaceSrc)!;

        Assert.NotNull(copy);
        Assert.Single(copy);
        Assert.Equal("keep-me", copy[0].Name); // 当前 ""（JSON 回退丢属性）→ 落红
    }

    /// <summary>
    /// 含 internal setter 属性的元素类型：深拷贝路径（CompileFiller 按 CanWrite 绑定）可完整拷贝，
    /// 而 JSON 回退路径（System.Text.Json 仅绑定公共 setter）反序列化时该属性被跳过 —— 用作
    /// 「走/不走 JsonSerializerBase 回退」的行为区分器。
    /// </summary>
    private sealed class S04Item
    {
        public string Name { get; internal set; } = "";
    }
}
