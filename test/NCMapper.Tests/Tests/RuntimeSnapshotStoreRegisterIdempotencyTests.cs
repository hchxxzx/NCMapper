using NCMapper;
using NCMapper.Runtime;
using Xunit;

namespace NCMapper.Tests.Tests;

/// <summary>
/// <see cref="RuntimeSnapshotStore.Register(RuntimeMapperMetadata)"/> 同内容重复注册幂等性测试
/// （甲案红测）。
/// <para>缺陷背景：<c>MergeMetadata</c> 此前恒 <c>return new RuntimeMapperMetadata{...}</c>，
/// 致调用方 <c>if (ReferenceEquals(merged, existing)) return;</c> 恒为 false（死分支），
/// 同内容重复注册也会走 <c>ReplaceEntry</c> ＋ <c>Interlocked.Increment(ref s_contentVersion)</c>，
/// 造成 ContentVersion 无意义递增、快照版本语义失真（下游按 ContentVersion 失配重算全部被触发）。</para>
/// <para>修复口径（甲案）：<c>MergeMetadata</c> 在合并结果与首参语义等价时返回首参本身，
/// 使 ReferenceEquals 短路成立，同内容重复注册不递增版本、不重建 s_all。</para>
/// </summary>
[Collection("GlobalConfigState")]
public class RuntimeSnapshotStoreRegisterIdempotencyTests
{
    /// <summary>被测源类型</summary>
    private class IdemSrc
    {
        /// <summary>源标识字段</summary>
        public int Id { get; set; }
    }

    /// <summary>被测目标类型</summary>
    private class IdemTgt
    {
        /// <summary>目标标识字段</summary>
        public int Id { get; set; }
    }

    /// <summary>
    /// 构造最小合法元数据（字段全部取默认值，仅类型与 FQN 必填）。
    /// </summary>
    private static RuntimeMapperMetadata MakeMeta() => new()
    {
        SourceType = typeof(IdemSrc),
        TargetType = typeof(IdemTgt),
        SourceTypeFqn = typeof(IdemSrc).FullName!,
        TargetTypeFqn = typeof(IdemTgt).FullName!,
        Channel = RuntimeMappingChannel.MapFrom,
    };

    /// <summary>
    /// 同内容重复注册 **不递增** ContentVersion（甲案核心行为锁）。
    /// <para>红测口径：修复前该断言必失败（每次 Register 都递增）；修复后通过。</para>
    /// </summary>
    [Fact]
    public void Register_SameContentTwice_DoesNotBumpContentVersion()
    {
        RuntimeSnapshotStore.Clear();

        RuntimeSnapshotStore.Register(MakeMeta());
        var afterFirst = RuntimeSnapshotStore.ContentVersion;

        RuntimeSnapshotStore.Register(MakeMeta());
        var afterSecond = RuntimeSnapshotStore.ContentVersion;

        Assert.Equal(afterFirst, afterSecond);
    }

    /// <summary>
    /// 同内容重复注册 **不新增条目**（幂等合并，终态仍一条）。
    /// </summary>
    [Fact]
    public void Register_SameContentTwice_KeepsSingleEntry()
    {
        RuntimeSnapshotStore.Clear();

        RuntimeSnapshotStore.Register(MakeMeta());
        RuntimeSnapshotStore.Register(MakeMeta());

        var found = RuntimeSnapshotStore.FindByPair(typeof(IdemSrc), typeof(IdemTgt));
        Assert.NotNull(found);
        Assert.Single(RuntimeSnapshotStore.FindByTarget(typeof(IdemTgt)));
    }

    /// <summary>
    /// 内容 **有实质差异** 时仍必须递增 ContentVersion（防修复过度：把真实变更也短路掉）。
    /// <para>这是修复的反向断言——若实现把"任何重复注册"都短路（不管内容是否相同），本用例必红。</para>
    /// </summary>
    [Fact]
    public void Register_DifferentContent_BumpsContentVersion()
    {
        RuntimeSnapshotStore.Clear();

        RuntimeSnapshotStore.Register(MakeMeta());
        var afterFirst = RuntimeSnapshotStore.ContentVersion;

        // 仅改一个布尔字段（NoAutoInclude false→true），属实质差异
        var changed = new RuntimeMapperMetadata
        {
            SourceType = typeof(IdemSrc),
            TargetType = typeof(IdemTgt),
            SourceTypeFqn = typeof(IdemSrc).FullName!,
            TargetTypeFqn = typeof(IdemTgt).FullName!,
            Channel = RuntimeMappingChannel.MapFrom,
            NoAutoInclude = true,
        };
        RuntimeSnapshotStore.Register(changed);
        var afterSecond = RuntimeSnapshotStore.ContentVersion;

        Assert.True(afterSecond > afterFirst,
            $"实质差异注册必须递增 ContentVersion（实际 first={afterFirst} second={afterSecond}）");
    }

    /// <summary>
    /// 合并后的终态字段必须反映两侧（防修复引入"短路导致字段丢失"）。
    /// </summary>
    [Fact]
    public void Register_MergedContent_RetainsBothSides()
    {
        RuntimeSnapshotStore.Clear();

        RuntimeSnapshotStore.Register(MakeMeta());
        RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
        {
            SourceType = typeof(IdemSrc),
            TargetType = typeof(IdemTgt),
            SourceTypeFqn = typeof(IdemSrc).FullName!,
            TargetTypeFqn = typeof(IdemTgt).FullName!,
            Channel = RuntimeMappingChannel.MapFrom,
            NoAutoInclude = true,
        });

        var found = RuntimeSnapshotStore.FindByPair(typeof(IdemSrc), typeof(IdemTgt));
        Assert.NotNull(found);
        Assert.True(found!.NoAutoInclude, "合并后应保留 b 侧的 NoAutoInclude=true");
    }
}
