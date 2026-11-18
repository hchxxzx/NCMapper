using Microsoft.CodeAnalysis;
using NCMapper.SourceGen.Tests.Models;
using NCMapper.SourceGen.Tests.Helpers;
using NCMapper.Generated;
using Xunit;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// CS8628 回归测试 — 可空集合属性触发 new T?() 非法语法
/// <para>2026-07-18 修复 CodeGenerator.DeepCopy.cs 中 4 处 new {fullTypeStr}() 生成问题：</para>
/// <list type="number">
/// <item>Queue 分支 new {fullTypeStr}() — GenerateDeepCopyCollectionProperty</item>
/// <item>Stack 分支 new {fullTypeStr}() — GenerateDeepCopyCollectionProperty</item>
/// <item>SortedSet 分支 new {fullTypeStr}() — GenerateDeepCopyCollectionProperty</item>
/// <item>new {fullTypeStr}({listVarName}) — GetCollectionCreationExpression</item>
/// </list>
/// <para>触发条件：[MapFrom] 的 Source/Target 含可空引用类型集合属性（如 Queue&lt;int&gt;?）时，
/// propType.ToDisplayString() 返回 "Queue&lt;int&gt;?"，拼接出 new Queue&lt;int&gt;?() —
/// C# 不允许 new 表达式后跟可空注解 ?，编译失败（CS8628 / 语法错误）</para>
/// <para>修复：GetNonNullableTypeName 用 WithNullableAnnotation(NotAnnotated) 去掉外层注解，
/// 不递归泛型参数（Queue&lt;string?&gt;? → Queue&lt;string?&gt;，元素注解保留）</para>
/// <para>本测试直接调用 GeneratedMapper.DeepCopy_SgNullableCollectionEntity —
/// 若生成代码含语法错误则方法不存在，编译失败</para>
/// </summary>
public class NullableCollectionCs8628RegressionTests
{
    // ================================================================
    // 行为测试：DeepCopy 正确性 — 覆盖 4 个集合分支
    // ================================================================

    /// <summary>
    /// DeepCopy 保留所有可空集合的内容和顺序 — 覆盖 Queue/Stack/SortedSet/LinkedList 四个分支
    /// <para>Queue 保持 FIFO 顺序；Stack 保持 LIFO 顺序；SortedSet 保持排序；LinkedList 保持插入顺序</para>
    /// <para>修复前：源生成器生成 new Queue&lt;int&gt;?() 等非法语法，编译失败，此测试无法存在</para>
    /// <para>修复后：生成 new Queue&lt;int&gt;()，深拷贝独立副本</para>
    /// </summary>
    [Fact]
    public void DeepCopy_Preserves_All_Collections_Content_And_Order()
    {
        var original = new SgNullableCollectionEntity
        {
            Id = 42,
            TaskQueue = new Queue<int>(new[] { 10, 20, 30 }),
            HistoryStack = new Stack<int>(new[] { 1, 2, 3 }),
            SortedTags = new SortedSet<int> { 5, 1, 3, 2, 4 },
            LinkedItems = new LinkedList<int>(new[] { 100, 200, 300 })
        };

        var copy = GeneratedMapper.DeepCopy_SgNullableCollectionEntity(original, new DeepCopyContext())!;

        Assert.NotSame(original, copy);
        Assert.Equal(42, copy.Id);

        // Queue — FIFO 顺序保持
        Assert.NotNull(copy.TaskQueue);
        Assert.NotSame(original.TaskQueue, copy.TaskQueue);
        Assert.Equal(new[] { 10, 20, 30 }, copy.TaskQueue!.ToArray());

        // Stack — LIFO 顺序保持（new Stack({1,2,3}) 栈顶为 3，ToArray 返回 [3,2,1]）
        Assert.NotNull(copy.HistoryStack);
        Assert.NotSame(original.HistoryStack, copy.HistoryStack);
        Assert.Equal(original.HistoryStack!.ToArray(), copy.HistoryStack!.ToArray());

        // SortedSet — 排序保持
        Assert.NotNull(copy.SortedTags);
        Assert.NotSame(original.SortedTags, copy.SortedTags);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, copy.SortedTags!.ToArray());

        // LinkedList — 插入顺序保持
        Assert.NotNull(copy.LinkedItems);
        Assert.NotSame(original.LinkedItems, copy.LinkedItems);
        Assert.Equal(new[] { 100, 200, 300 }, copy.LinkedItems!);
    }

    /// <summary>
    /// DeepCopy 全 null 集合属性 — 验证 null 守卫正确，null 集合保持 null
    /// <para>生成的代码用 if (src.Prop != null) 守卫，null 时跳过赋值，dst.Prop 保持默认 null</para>
    /// </summary>
    [Fact]
    public void DeepCopy_Null_Collections_Remain_Null()
    {
        var original = new SgNullableCollectionEntity
        {
            Id = 99,
            TaskQueue = null,
            HistoryStack = null,
            SortedTags = null,
            LinkedItems = null
        };

        var copy = GeneratedMapper.DeepCopy_SgNullableCollectionEntity(original, new DeepCopyContext())!;

        Assert.NotSame(original, copy);
        Assert.Equal(99, copy.Id);
        Assert.Null(copy.TaskQueue);
        Assert.Null(copy.HistoryStack);
        Assert.Null(copy.SortedTags);
        Assert.Null(copy.LinkedItems);
    }

    /// <summary>
    /// DeepCopy 是真正的深拷贝 — 修改副本集合不影响原件
    /// <para>验证 Queue/Stack/SortedSet/LinkedList 副本与原件完全独立</para>
    /// </summary>
    [Fact]
    public void DeepCopy_Modified_Copy_Does_Not_Affect_Original()
    {
        var original = new SgNullableCollectionEntity
        {
            Id = 1,
            TaskQueue = new Queue<int>(new[] { 10, 20 }),
            HistoryStack = new Stack<int>(new[] { 1, 2 }),
            SortedTags = new SortedSet<int> { 1, 2, 3 },
            LinkedItems = new LinkedList<int>(new[] { 100, 200 })
        };

        var copy = GeneratedMapper.DeepCopy_SgNullableCollectionEntity(original, new DeepCopyContext())!;

        // 修改副本
        copy.TaskQueue!.Enqueue(999);
        copy.HistoryStack!.Push(999);
        copy.SortedTags!.Add(999);
        copy.LinkedItems!.AddLast(999);

        // 原件不受影响
        Assert.Equal(2, original.TaskQueue!.Count);
        Assert.Equal(2, original.HistoryStack!.Count);
        Assert.Equal(3, original.SortedTags!.Count);
        Assert.Equal(2, original.LinkedItems!.Count);
    }

    /// <summary>
    /// DeepCopy 源为 null 返回 null — 顶层 null 守卫
    /// </summary>
    [Fact]
    public void DeepCopy_Null_Source_Returns_Null()
    {
        var copy = GeneratedMapper.DeepCopy_SgNullableCollectionEntity(null!, new DeepCopyContext());
        Assert.Null(copy);
    }

    // ================================================================
    // 生成代码语法断言 — 验证不含 new T?() 非法语法
    // ================================================================

    /// <summary>
    /// 生成的 DeepCopy 代码不含 new T?() 非法语法 — CS8628 回归防线
    /// <para>修复前：生成 "new System.Collections.Generic.Queue&lt;int&gt;?()" 等非法语法</para>
    /// <para>修复后：生成 "new System.Collections.Generic.Queue&lt;int&gt;()"（无 ? 注解）</para>
    /// <para>此测试用 TestHelper 单独运行源生成器，检查生成代码字符串，
    /// 不依赖项目实际编译（更精准定位语法问题）</para>
    /// </summary>
    [Fact]
    public void GeneratedCode_No_NewNullable_Syntax()
    {
        var source = """
            namespace NCMapper.SourceGen.Tests.Models
            {
                public class SgCheckEntity
                {
                    public int Id { get; set; } = 1;
                    public System.Collections.Generic.Queue<int>? TaskQueue { get; set; }
                    public System.Collections.Generic.Stack<int>? HistoryStack { get; set; }
                    public System.Collections.Generic.SortedSet<int>? SortedTags { get; set; }
                    public System.Collections.Generic.LinkedList<int>? LinkedItems { get; set; }
                }

                [NCMapper.Attributes.MapFrom(typeof(SgCheckEntity))]
                public class SgCheckDto
                {
                    public int Id { get; set; }
                }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 源生成器不应报告任何 Error 级别诊断（Info 级别的 NCMAP007 未映射属性提示可忽略）
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.NotEmpty(result.GeneratedCode);

        // 核心：生成代码中不应出现 new T?() 非法语法
        Assert.DoesNotContain("new System.Collections.Generic.Queue<int>?(", result.GeneratedCode);
        Assert.DoesNotContain("new System.Collections.Generic.Stack<int>?(", result.GeneratedCode);
        Assert.DoesNotContain("new System.Collections.Generic.SortedSet<int>?(", result.GeneratedCode);
        Assert.DoesNotContain("new System.Collections.Generic.LinkedList<int>?(", result.GeneratedCode);

        // 正向验证：生成代码中应包含正确的 new T() 语法（无 ?）
        Assert.Contains("new System.Collections.Generic.Queue<int>()", result.GeneratedCode);
        Assert.Contains("new System.Collections.Generic.Stack<int>()", result.GeneratedCode);
        Assert.Contains("new System.Collections.Generic.SortedSet<int>()", result.GeneratedCode);
        Assert.Contains("new System.Collections.Generic.LinkedList<int>(_tmpList)", result.GeneratedCode);
    }
}
