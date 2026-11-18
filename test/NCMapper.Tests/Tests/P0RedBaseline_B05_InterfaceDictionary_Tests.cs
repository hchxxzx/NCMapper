// ============================================================================
// Red_B05 — CollectionTypeDetector 接口自身漏判
//
// 缺陷（006 第 5 项）：CollectionTypeDetector（IsGenericDictionary :21-22 /
// IsNonGenericDictionary :33）GetInterfaces() 不含自身 —— 接口字典属性误判
// （实测 IsDictionary(IDictionary<string,object>)=False；K/V 简单场景静默 JSON 劣化、
// 复杂场景 NotSupportedException/MemberAccessException）；与 TypeCompatAdapter.
// IsDictionaryShape（115-118）双口径分叉。
//
// 修复后期望（断言口径，006 / 联合矩阵）：
//   IsGenericDictionary 增加自身判定（type.IsGenericType && GetGenericTypeDefinition()
//   == typeof(IDictionary<,>)）；IsNonGenericDictionary 同理 —— 接口字典属性走字典
//   拷贝/映射路径而非 JSON 回退，Items 内容完整，环值深拷贝成功且环保持。
//
// 当前基线（e59f1c9）落红形态（留档）：
//   ① IsGenericDictionary(IDictionary<string,object>) == False（接口自身漏判）→ 落红；
//  ② 接口字典源同型映射：复杂 K/V 场景构建/运行失败（006 示例留档：
//     NotSupportedException「不支持的集合转换」）→ 落红；
//   ③ 接口字典属性深拷贝（环值，平移 VerifyReview V1c 形态）：实测抛 MemberAccessException
//     （DeepCopyProviderBase :138 GetUninitializedObject(IDictionary 接口) —— "Cannot create
//  an instance of an interface"）——006「复杂场景 NotSupportedException/
//     MemberAccessException」承载的 MemberAccessException 形态 → 落红。
// 断言形态平移自审计实测程序 VerifyReview V1/V1b/V1c（artifacts/tmp_review_20260827），
// 当前形态以本任务书执行期落红实测为准（trust but verify，经验库 #22）。
// ============================================================================

using Xunit;

namespace NCMapper.Tests.Tests;

public class Red_B05_InterfaceDictionaryDetection
{
    // ---------- ① 判定路径：接口自身漏判（V1 平移） ----------
    [Fact]
    public void IDictionary接口自身_字典判定_应为字典()
    {
        // 修复后期望：IsGenericDictionary/IsDictionary 对 IDictionary<string,object> 自身为 true；
        // 当前形态（留档）：GetInterfaces() 不含自身 → 判定为 False → 落红
        Assert.True(CollectionTypeDetector.IsGenericDictionary(typeof(IDictionary<string, object>)));
        Assert.True(CollectionTypeDetector.IsDictionary(typeof(IDictionary<string, object>)));
    }

    [Fact]
    public void IReadOnlyDictionary接口自身_字典判定_应为字典()
    {
        // 修复后期望：IReadOnlyDictionary<string,object> 同样识别为字典形态
        // （收敛口径统一转发 TypeCompatAdapter.IsDictionaryShape，006）；
        // 当前形态（留档）：判定为 False → 落红
        Assert.True(CollectionTypeDetector.IsDictionary(typeof(IReadOnlyDictionary<string, object>)));
    }

    // ---------- ② 映射端到端：接口声明源 → 具体声明目标（V1b 平移，006 示例断言）----------
    [Fact]
    public void 接口字典属性_同型映射_走字典路径_Items完整()
    {
        var node = new B05Node { Name = "n1" };
        var src = new B05IfSrc { Items = new Dictionary<string, B05Node> { ["a"] = node } };

        // 修复后期望：接口字典源走字典拷贝路径（非 JSON 回退），映射成功、Items 内容完整；
        // 当前形态（留档实测）：NotSupportedException「不支持的集合转换：IDictionary`2 →
        // Dictionary`2」（006 示例留档同形态）→ 落红
        var dto = Mapper.Map<B05IfSrc, B05Dto>(src)!;

        Assert.NotNull(dto.Items);
        Assert.Single(dto.Items!);
        Assert.Equal("n1", dto.Items!["a"].Name);
    }

    // ---------- ③ 深拷贝端到端：接口字典属性 + 环值（V1c 平移） ----------
    [Fact]
    public void 接口字典属性_深拷贝环值_不走Json回退_成功且环保持()
    {
        var a = new B05CycNode { Name = "a" };
        var b = new B05CycNode { Name = "b" };
        a.Next = b;
        b.Next = a; // 循环引用值
        var src = new B05DeepIface { Items = new Dictionary<string, B05CycNode> { ["a"] = a } };

        // 修复后期望：接口字典走字典拷贝路径（非 JSON 回退），环值深拷贝成功且环回边引用一致；
        // 当前形态（留档实测）：DeepCopyProviderBase :138 GetUninitializedObject(IDictionary 接口)
        // 抛 MemberAccessException「Cannot create an instance of an interface」→ 落红
        var copy = Mapper.DeepCopy(src)!;

        Assert.NotNull(copy.Items);
        Assert.Single(copy.Items!);
        Assert.Equal("a", copy.Items!["a"].Name);
        Assert.Same(copy.Items!["a"], copy.Items!["a"].Next!.Next); // 环保持（字典值深拷贝语义）
    }

    // ---------- 模型类型（VerifyReview V1 族形态平移，单文件自含） ----------

    private sealed class B05Node
    {
        public string Name { get; set; } = "";
    }

    private sealed class B05IfSrc
    {
        public IDictionary<string, B05Node> Items { get; set; } = new Dictionary<string, B05Node>();
    }

    private sealed class B05Dto
    {
        public Dictionary<string, B05Node>? Items { get; set; }
    }

    private sealed class B05CycNode
    {
        public string Name { get; set; } = "";
        public B05CycNode? Next { get; set; }
    }

    private sealed class B05DeepIface
    {
        public IDictionary<string, B05CycNode> Items { get; set; } = new Dictionary<string, B05CycNode>();
    }
}
