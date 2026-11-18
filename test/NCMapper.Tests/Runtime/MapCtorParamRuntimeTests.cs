using NCMapper;
using Xunit;

namespace NCMapper.Tests.Runtime;

/// <summary>
/// 矩阵行 16：MapCtorParam 运行时回退回归（B-4 裁决回归验证）。
/// <para>MapCtorParam 为 Fluent 生成路径专属能力（运行时回退路径零改动——任务书非目标②/B10）。
/// 本文件锁定：动态 Mapper.Map 带参构造对行为与现状一致——形参同名流入（既有语义）、
/// 形参无对应源属性落 default（运行时无绑定语义，不因生成侧能力引入分叉）。</para>
/// <para>fixture 均为普通类型（无 [MapperConfig]/[MapFrom]）——走反射动态路径，与生成注册
/// 隔离（依赖生成注册的测试隔离范式不适用）。</para>
/// </summary>
public class MapCtorParamRuntimeTests
{
    public class R16Src
    {
        public string NickName { get; set; } = "nk";
    }

    public class R16Dto
    {
        public string Nick { get; }
        public R16Dto(string nick) { Nick = nick; }
    }

    public class R16SameNameSrc
    {
        public string Nick { get; set; } = "direct";
    }

    /// <summary>动态带参构造对同名形参：源属性值按既有同名语义流入（运行时路径现状锁定）。</summary>
    [Fact]
    public void Dynamic_SameNameCtorParam_Flows_SourceValue()
    {
        var dst = Mapper.Map<R16SameNameSrc, R16Dto>(new R16SameNameSrc());
        Assert.NotNull(dst);
        Assert.Equal("direct", dst!.Nick);
    }

    /// <summary>形参无对应源属性：运行时无 MapCtorParam 绑定语义 → 落 default（B-4 裁决——
    /// 生成侧绑定能力不得改变运行时回退行为）。</summary>
    [Fact]
    public void Dynamic_UnmatchedCtorParam_Falls_Default()
    {
        var dst = Mapper.Map<R16Src, R16Dto>(new R16Src());
        Assert.NotNull(dst);
        Assert.Null(dst!.Nick); // string 形参未匹配 → default（MapperBase 既有语义）
    }

    /// <summary>MapList 双入口同语义（与 CtorArgCompatibilityTests 双入口口径一致）。</summary>
    [Fact]
    public void Dynamic_MapList_UnmatchedCtorParam_Falls_Default()
    {
        var list = Mapper.MapList<R16Src, R16Dto>(new[] { new R16Src(), new R16Src() });
        Assert.NotNull(list);
        Assert.Equal(2, list!.Count);
        Assert.All(list, d => Assert.Null(d.Nick));
    }
}
