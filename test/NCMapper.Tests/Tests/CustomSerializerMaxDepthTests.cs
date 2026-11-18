using System.Text.Json;
using NCMapper;
using Xunit;

namespace NCMapper.Tests.Tests;

/// <summary>
/// 自定义序列化器极大 MaxDepth 绕过行为记录测试（C17 补测）
/// <para>报告 §五 C17 断言形态：行为记录 + 文档声明对齐（200 层 SUCCESS）。
/// 安全审计 A-001-F1：自定义序列化器（继承 JsonSerializerBase 替换 Current）显式极大 MaxDepth
/// 可绕过 JSON 深度保护（默认 64 层）；深度安全责任在调用方（文档声明），
/// 本测试记录该行为（极大 MaxDepth 下 200 层往返 SUCCESS），并对照默认 64 层保护在位。</para>
/// </summary>
public class CustomSerializerMaxDepthTests
{
    /// <summary>
    /// 深度 JSON 节点 — 自引用嵌套结构
    /// </summary>
    private class DeepJsonNode
    {
        /// <summary>
        /// 层级标识
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 子节点（嵌套链）
        /// </summary>
        public DeepJsonNode? Child { get; set; }
    }

    /// <summary>
    /// 循环构建深度链（无栈风险）
    /// </summary>
    /// <param name="depth">目标深度</param>
    /// <returns>根节点</returns>
    private static DeepJsonNode BuildDeepNode(int depth)
    {
        DeepJsonNode? current = null;
        for (int i = 0; i <= depth; i++)
        {
            current = new DeepJsonNode { Id = i, Child = current };
        }
        return current!;
    }

    /// <summary>
    /// 行为记录：极大 MaxDepth 自定义序列化器下 200 层 JSON 往返 SUCCESS
    /// <para>（A-001-F1 场景：绕过默认 64 层保护；文档声明对齐 = 自定义序列化器
    /// 深度安全责任在调用方，库不强制 MaxDepth 下限。）</para>
    /// </summary>
    [Fact]
    public void CustomSerializer_ExtremeMaxDepth_200Layers_RoundTripSucceeds()
    {
        var original = JsonSerializerBase.Current;
        try
        {
            // 自定义序列化器：显式极大 MaxDepth（绕过默认 64 保护）
            JsonSerializerBase.Current = new SystemTextJsonSerializer(new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                MaxDepth = int.MaxValue
            });

            var root = BuildDeepNode(200);

            var json = JsonSerializerBase.Current.Serialize(root);
            var restored = (DeepJsonNode)JsonSerializerBase.Current.Deserialize(json, typeof(DeepJsonNode))!;

            // 200 层 SUCCESS（行为记录：极大 MaxDepth 下深度保护被显式放宽）
            Assert.NotNull(restored);
            Assert.Equal(200, restored.Id);
            Assert.NotNull(restored.Child);
        }
        finally
        {
            JsonSerializerBase.Current = original;
        }
    }

    /// <summary>
    /// 对照：默认 MaxDepth=64 序列化器下 200 层 JSON 序列化被深度保护拦截
    /// <para>默认 SystemTextJsonSerializer（MaxDepth=64）下 200 层嵌套超过 64 → JsonException，
    /// 证明默认保护在位（C17 行为记录的对照基线）。</para>
    /// </summary>
    [Fact]
    public void DefaultSerializer_MaxDepth64_200Layers_Throws()
    {
        var original = JsonSerializerBase.Current;
        try
        {
            // 默认实现：MaxDepth=64
            JsonSerializerBase.Current = new SystemTextJsonSerializer();

            var root = BuildDeepNode(200);

            // 200 层嵌套超 64 层上限 → STJ 抛 JsonException
            Assert.Throws<JsonException>(() => JsonSerializerBase.Current.Serialize(root));
        }
        finally
        {
            JsonSerializerBase.Current = original;
        }
    }
}
