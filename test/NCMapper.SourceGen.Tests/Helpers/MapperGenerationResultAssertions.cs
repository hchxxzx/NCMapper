using Xunit;

namespace NCMapper.SourceGen.Tests.Helpers;

/// <summary>
/// MapperGenerationResult 的流畅断言扩展 — 提供方法体匹配、方法存在性、方法数量等断言
/// </summary>
public class MapperGenerationResultAssertions
{
    /// <summary>
    /// 待断言的生成结果
    /// </summary>
    private readonly MapperGenerationResult _result;

    /// <summary>
    /// 从生成结果构建断言实例
    /// </summary>
    /// <param name="result">待断言的生成结果</param>
    public MapperGenerationResultAssertions(MapperGenerationResult result)
    {
        _result = result;
    }

    /// <summary>
    /// 断言指定方法存在
    /// </summary>
    /// <param name="methodName">方法名称</param>
    /// <returns>当前断言实例（支持链式调用）</returns>
    public MapperGenerationResultAssertions HaveMethod(string methodName)
    {
        Assert.True(_result.Methods.ContainsKey(methodName),
            $"方法 '{methodName}' 不存在。已生成的方法: [{string.Join(", ", _result.Methods.Keys)}]");
        return this;
    }

    /// <summary>
    /// 断言指定方法的方法体与预期文本匹配
    /// <para>预期文本会自动做换行统一和首尾 Trim 预处理</para>
    /// <para>若有重载方法，匹配第一个重载的方法体</para>
    /// </summary>
    /// <param name="methodName">方法名称</param>
    /// <param name="expectedBody">预期的方法体文本（raw string literal）</param>
    /// <returns>当前断言实例（支持链式调用）</returns>
    public MapperGenerationResultAssertions HaveMethodBody(string methodName, string expectedBody)
    {
        var method = _result.GetMethod(methodName);
        Assert.True(method != null,
            $"方法 '{methodName}' 不存在。已生成的方法: [{string.Join(", ", _result.Methods.Keys)}]");

        var expected = expectedBody.ReplaceLineEndings().Trim(' ', '\r', '\n');
        Assert.Equal(expected, method!.Body);
        return this;
    }

    /// <summary>
    /// 断言指定方法的方法体包含预期文本片段（部分匹配）
    /// <para>适用于方法体较长（如列表映射）的场景，验证关键片段而非精确匹配</para>
    /// <para>若有重载方法，匹配第一个重载的方法体</para>
    /// </summary>
    /// <param name="methodName">方法名称</param>
    /// <param name="fragment">预期包含的文本片段</param>
    /// <returns>当前断言实例（支持链式调用）</returns>
    public MapperGenerationResultAssertions HaveMethodBodyContaining(string methodName, string fragment)
    {
        var method = _result.GetMethod(methodName);
        Assert.True(method != null,
            $"方法 '{methodName}' 不存在。已生成的方法: [{string.Join(", ", _result.Methods.Keys)}]");

        var normalizedFragment = fragment.ReplaceLineEndings().Trim(' ', '\r', '\n');
        Assert.Contains(normalizedFragment, method!.Body);
        return this;
    }

    /// <summary>
    /// 断言生成的方法总数
    /// </summary>
    /// <param name="count">预期方法数量</param>
    /// <returns>当前断言实例（支持链式调用）</returns>
    public MapperGenerationResultAssertions HaveMethodCount(int count)
    {
        Assert.Equal(count, _result.Methods.Count);
        return this;
    }

    /// <summary>
    /// 断言方法列表精确匹配（数量和名称都匹配）
    /// </summary>
    /// <param name="methodNames">预期的方法名称列表</param>
    /// <returns>当前断言实例（支持链式调用）</returns>
    public MapperGenerationResultAssertions HaveOnlyMethods(params string[] methodNames)
    {
        Assert.Equal(methodNames.Length, _result.Methods.Count);
        foreach (var name in methodNames)
        {
            Assert.True(_result.Methods.ContainsKey(name),
                $"方法 '{name}' 不存在。已生成的方法: [{string.Join(", ", _result.Methods.Keys)}]");
        }
        return this;
    }
}

/// <summary>
/// MapperGenerationResult 的扩展方法入口 — 提供 Should() 流畅断言入口
/// </summary>
public static class MapperGenerationResultExtensions
{
    /// <summary>
    /// 获取断言对象，开始流畅断言链
    /// </summary>
    /// <param name="result">生成结果</param>
    /// <returns>断言实例</returns>
    public static MapperGenerationResultAssertions Should(this MapperGenerationResult result)
    {
        return new MapperGenerationResultAssertions(result);
    }
}
