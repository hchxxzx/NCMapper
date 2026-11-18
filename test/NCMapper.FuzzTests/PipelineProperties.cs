using FsCheck.Xunit;
using NCMapper.FuzzTests.Models;

namespace NCMapper.FuzzTests;

/// <summary>
/// MapPipeline 管道属性测试 — 验证 PostMap 执行顺序和上下文传递
/// <para>执行顺序：映射核心 → PostMap（按注册逆序）</para>
/// <para>每个 [Property] 默认运行 100 次随机输入，失败时自动 shrinking 到最小复现</para>
/// <para>注意：管道是全局状态，每个测试必须 try/finally 清理</para>
/// </summary>
public class PipelineProperties
{
    // ================================================================
    // PostMap — 映射后执行，可修改目标对象
    // ================================================================

    /// <summary>
    /// PostMap 修改目标对象 — 映射后 PostMap 回调覆盖 DTO 字段
    /// </summary>
    [Property]
    public bool Pipeline_PostMap_Modifies_Target(int id, string name)
    {
        Mapper.Pipeline.Clear();
        try
        {
            Mapper.Pipeline.AddPostMap<FuzzUser, FuzzUserDto>((src, tgt) =>
            {
                tgt.Name = "POST_MAP_INJECTED";
            });

            var user = new FuzzUser { Id = id, Name = name };
            var dto = Mapper.Map<FuzzUser, FuzzUserDto>(user)!;
            return dto.Id == id && dto.Name == "POST_MAP_INJECTED";
        }
        finally
        {
            Mapper.Pipeline.Clear();
        }
    }

    // ================================================================
    // PostMap 逆序执行 — 洋葱模型
    // ================================================================

    /// <summary>
    /// PostMap 按注册逆序执行 — 先注册的回调后执行（覆盖后注册的值）
    /// </summary>
    [Property]
    public bool Pipeline_PostMap_Reverse_Order(int id, string name)
    {
        Mapper.Pipeline.Clear();
        try
        {
            Mapper.Pipeline.AddPostMap<FuzzUser, FuzzUserDto>((src, tgt) =>
            {
                tgt.Name = "FIRST";
            });
            Mapper.Pipeline.AddPostMap<FuzzUser, FuzzUserDto>((src, tgt) =>
            {
                tgt.Name = "SECOND";
            });

            var user = new FuzzUser { Id = id, Name = name };
            var dto = Mapper.Map<FuzzUser, FuzzUserDto>(user)!;
            // 逆序执行：SECOND 先执行，FIRST 后执行，最终 Name="FIRST"
            return dto.Name == "FIRST";
        }
        finally
        {
            Mapper.Pipeline.Clear();
        }
    }

    // ================================================================
    // Clear — 禁用管道
    // ================================================================

    /// <summary>
    /// Clear 后管道不再执行 — 注册回调后 Clear，映射结果不受回调影响
    /// </summary>
    [Property]
    public bool Pipeline_Clear_Disables(int id, string name)
    {
        Mapper.Pipeline.Clear();
        Mapper.Pipeline.AddPostMap<FuzzUser, FuzzUserDto>((src, tgt) =>
        {
            tgt.Name = "SHOULD_NOT_APPEAR";
        });
        Mapper.Pipeline.Clear(); // 禁用

        var user = new FuzzUser { Id = id, Name = name };
        var dto = Mapper.Map<FuzzUser, FuzzUserDto>(user)!;
        return dto.Id == id && dto.Name == name; // 不受 PostMap 影响
    }
}
