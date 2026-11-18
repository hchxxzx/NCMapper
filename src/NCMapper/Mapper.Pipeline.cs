namespace NCMapper;

/// <summary>
/// Mapper 管道扩展 — 提供全局映射管道访问点
/// </summary>
public partial class Mapper
{
    /// <summary>
    /// 全局映射管道实例 — 用于注册 PostMap 回调
    /// <para>用法：Mapper.Pipeline.AddPostMap&lt;UserEntity, UserDto&gt;(...)</para>
    /// </summary>
    [System.Diagnostics.CodeAnalysis.Experimental("NCMapper001")]
    public static readonly MapPipeline Pipeline = new();
}
