// ============================================================================
// Red_B03 — 构参外 set 属性静默丢失
//
// 缺陷（006 第 3 项）：DeepCopyCompiler.CompileFiller（396-398，无参构造缺失即返回
// null 不编译填充器）+ DeepCopyProviderBase（149，record 分支不调 FillProperties）——
// 无参构造缺失类型的构参外可写属性静默丢失（实测 copy.Other=null）。
//
// 修复后期望（断言口径，006）：
//   base record 分支在 CreateInstance 返回后对 result 补一次「构参外 set 属性」填充
//   —— MutNode 构参外可写属性 copy.Other 与源同值。
//
// 当前基线（e59f1c9）落红形态（留档）：copy.Other == null（构参外属性静默丢失）→ 落红。
// ============================================================================

using Xunit;

namespace NCMapper.Tests.Tests;

public class Red_B03_CtorExternalProperty
{
    [Fact]
    public void Record构参外可写属性_深拷贝_与源同值()
    {
        var src = new B03MutNode("n") { Other = "extra" };

        // 修复后期望：构参外 set 属性补填，copy.Other == "extra"；
        // 当前形态（留档）：copy.Other == null（CompileFiller :396-398 对无参构造缺失类型
        // 返回 null，record 分支不执行属性填充）→ 落红
        var copy = Mapper.DeepCopy(src)!;

        Assert.Equal("n", copy.Name);          // 构参内属性：现状已正确（对照锚）
        Assert.Equal("extra", copy.Other);     // 构参外属性：当前 null → 落红
    }

    /// <summary>主构参 Name + 构参外可写属性 Other 的 record（006 的 MutNode 形态）。</summary>
    private sealed record B03MutNode(string Name)
    {
        public string? Other { get; set; }
    }
}
