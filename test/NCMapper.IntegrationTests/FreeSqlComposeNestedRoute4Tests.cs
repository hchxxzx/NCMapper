// ============================================================================
// AC-8 验收用例 —— 不可译形态必须 fail-closed（不得静默返回 null 或常量空壳）
// ----------------------------------------------------------------------------
// 任务书 V1 卡验收第 7 条：
//   dotnet test ... --filter-class "*FreeSqlComposeNestedRoute4*"
//   → 看到 UnresolvableNestedType_MustFailClosed 通过
//
// ★ 硬性约束（任务书 §5 第 4 条）：嵌套源参数匹配不到时必须 fail-closed（抛异常），
//   不得静默返回 null 或常量空壳。
//
// ★ 为什么这条比「功能正确」更重要：fail-open 的错误形态是**用户看到正常返回的数据**，
//   而数据其实是错的（C3 回退验证 2 实证：把 BuildCompose 整体放进 guard，
//   配置错误会被兜成降级，用户看到「正常返回了数据」而不是报错 —— 这比崩溃更危险）。
//
// ★ 两类 fail-closed 场景（对应 ComposeNestedSourceScope 的两级 API）：
//   ① 源类型不在用户 join 源参数集 → MapConfigurationException（Strict）；
//   ② 多源同类型（同一类型在参数集中出现多次）→ 无法唯一确定 → MapConfigurationException。
//   两条都必须给出**可定位的原因**（点名缺失/重复的源类型 + 处置指引）。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using FreeSql;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.FreeSql;
using NCMapper.Runtime;
using Xunit;

namespace NCMapper.IntegrationTests
{
    using global::NCMapper.IntegrationTests.PocModels;

    /// <summary>
    /// AC-8：不可译的嵌套形态必须 fail-closed，且异常可定位。
    /// <para>类名匹配 <c>--filter-class "*FreeSqlComposeNestedRoute4*"</c>。</para>
    /// </summary>
    public class FreeSqlComposeNestedRoute4Tests : IClassFixture<PocFixture>
    {
        private readonly IFreeSql _fsql;

        /// <summary>构造 — 取 POC 共享 Fixture</summary>
        /// <param name="fixture">POC 共享 Fixture</param>
        public FreeSqlComposeNestedRoute4Tests(PocFixture fixture) => _fsql = fixture.Fsql;

        /// <summary>
        /// AC-8 主用例：嵌套源类型不在用户 join 源参数集 → 必须抛 <see cref="MapConfigurationException"/>，
        /// 不得静默返回 null 或常量空壳。
        /// <para>场景：嵌套 DTO <c>PocRoute4DeptDto</c> 已在注册表（主源 <c>PocDept</c>），
        /// 但查询只 join 了 <c>PocCustomer</c> —— 即「声明了该嵌套、用户却没 join 对应源」的配置错。</para>
        /// <para>★ 断言三件事：① 抛的是 MapConfigurationException（不是 NRE/翻译异常）；
        /// ② 异常消息点名缺失的源类型；③ 消息给出处置指引（把该类型加入 join 或改指已 join 的类型）。</para>
        /// </summary>
        [Fact]
        public void UnresolvableNestedType_MustFailClosed()
        {
            RegisterRoute4Snapshots();

            // ① 必须经 AsDto() 走 Compose 门面（直接 ToList<T>() 是 FreeSql 自己的映射，不进 NCMapper）
            var ex = Assert.Throws<MapConfigurationException>(() =>
                _fsql.Select<PocSite, PocCustomer>()
                    .LeftJoin((a, b) => b.Id == 1)
                    .AsDto()
                    .ToList<PocRoute4SiteDto>());

            // ② 异常可定位：点名缺失的源类型
            Assert.Contains(nameof(PocDept), ex.Message);
            Assert.Contains("不在本次查询的用户 join 源参数集", ex.Message);

            // ③ 给出处置指引（不是「出错了」这种无信息量的消息）
            //    实际消息给出两条路径：把该源类型加入 join，或「在映射声明中改指已 join 的源类型」
            Assert.Contains("LeftJoin", ex.Message);
            Assert.Contains("改指", ex.Message);
            Assert.Contains("映射声明", ex.Message);

            // ④ 不得 fail-open：同样的调用不得「成功返回带空壳/ null 的数据」
            var failOpenProbe = Record.Exception(() =>
                _fsql.Select<PocSite, PocCustomer>()
                    .LeftJoin((a, b) => b.Id == 1)
                    .AsDto()
                    .ToList<PocRoute4SiteDto>());
            Assert.NotNull(failOpenProbe);                       // 确实抛了
            Assert.IsType<MapConfigurationException>(failOpenProbe); // 且是配置异常（不是被兜成降级）
        }

        /// <summary>
        /// AC-8 补充 1：<b>多源同类型</b> → 无法唯一确定应取哪个 join 参数 → fail-closed。
        /// <para>场景：三表查询的 T2 与 T3 都是 <c>PocDept</c>，嵌套 DTO 主源也是 <c>PocDept</c>。</para>
        /// <para>★ 这条对应 <c>ComposeNestedSourceScope.FailClosedAmbiguous</c>：绝不猜是哪一个 join，
        /// 因为猜错的后果是「取了另一个 join 的行」——静默错数据。</para>
        /// </summary>
        [Fact]
        public void AmbiguousSameType_MustFailClosed()
        {
            RegisterRoute4Snapshots();

            var ex = Assert.Throws<MapConfigurationException>(() =>
                _fsql.Select<PocSite, PocDept, PocDept>()
                    .LeftJoin((a, b, c) => a.DeptId == b.Id)
                    .LeftJoin((a, b, c) => b.Id == c.Id)
                    .AsDto()
                    .ToList<PocRoute4SiteDto>());

            Assert.Contains(nameof(PocDept), ex.Message);
            Assert.Contains("出现 2 次", ex.Message);
            Assert.Contains("无法唯一确定", ex.Message);
        }

        /// <summary>
        /// AC-8 补充 2（§11.3）：guard 捕获范围必须被锁住 —— **配置错误不得被兜成降级**。
        /// <para>C3 回退验证 2 实证：若把 <c>BuildCompose</c> 整体放进 guard，构建期的配置错误
        /// （如主源类型不在泛型参数中）会被兜成降级路径，用户看到「正常返回了数据」而不是报错。</para>
        /// <para>本用例构造「主源不匹配」的配置错，断言抛 <see cref="InvalidOperationException"/>
        /// （配置错误原样冒），而不是被吞成 MapConfigurationException 或降级结果。</para>
        /// <para>★ 关键：这里断言的是 <c>InvalidOperationException</c> 且**不是** <c>MapConfigurationException</c>
        /// —— 两种异常类型分属「配置校验」与「降级兜底」，混了就是捕获范围错了。</para>
        /// </summary>
        [Fact]
        public void ConfigError_MustNotBeSwallowedByGuard()
        {
            // 目标 DTO 的主源是 PocSite，但查询的泛型参数里没有 PocSite（全是 PocDept）
            var ex = Assert.Throws<InvalidOperationException>(() =>
                _fsql.Select<PocDept, PocCustomer>()
                    .LeftJoin((a, b) => b.Id == 1)
                    .AsDto()
                    .ToList<PocRoute4SiteDto>());

            // 主源不匹配的专用消息（ErrorMessages.ComposeProjectionSourceTypeMismatch）
            Assert.Contains("主映射源类型", ex.Message);
            Assert.Contains(nameof(PocSite), ex.Message);

            // ★ 不得被兜成降级语义的 MapConfigurationException
            Assert.IsNotType<MapConfigurationException>(ex);
        }

        /// <summary>
        /// AC-8 补充 3：fail-closed 只对「声明了却不可解析」生效，**不得误伤**合法形态。
        /// <para>反向验证：把 PocDept 真的 join 进来后，同一条查询必须成功 ——
        /// 否则 fail-closed 就变成了「凡嵌套都报错」，把正确用法也堵死了。</para>
        /// </summary>
        [Fact]
        public void ResolvableNestedType_MustSucceed()
        {
            RegisterRoute4Snapshots();

            var rows = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .OrderBy((a, b) => a.Id)
                .AsDto().ToList<PocRoute4SiteDto>();

            Assert.Equal(4, rows.Count);
            Assert.NotNull(rows[0].Dept);
            Assert.Equal("IT", rows[0].Dept!.Name);
        }

        /// <summary>
        /// AC-8 补充 4（§11.3 的另一个边界）：guard 只包 FreeSql 执行，不包构建期。
        /// <para>验证方式：翻译期失败（IIF 未剥）会被 guard 接住并走降级（不冒异常），
        /// 而构建期失败（配置错）会原样冒 —— 两者行为必须不同。</para>
        /// <para>本用例锁「翻译期失败不冒到调用方」这一侧。</para>
        /// </summary>
        [Fact]
        public void TranslationFailure_HandledByGuard()
        {
            // 未剥除的原始产物直接执行 → 翻译期失败 → guard 接住 → 走降级 → 不冒异常
            var raw = FreeSqlProjectionBuilder.BuildCompose<PocSite, PocDept, PocSiteNestedDto>();
            Assert.Contains("IIF", raw.ToString());

            // 直接调 FreeSql 会炸（证明翻译期确实失败）
            Assert.ThrowsAny<Exception>(() =>
                _fsql.Select<PocSite, PocDept>()
                    .LeftJoin((a, b) => a.DeptId == b.Id)
                    .ToList(raw));

            // 产品路径（guard 内）不炸且行数正确 —— 说明 guard 接住了
            var rows = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .OrderBy((a, b) => a.Id)
                .AsDto().ToList<PocSiteNestedDto>();
            Assert.Equal(4, rows.Count);
            Assert.Equal("IT", rows[0].Dept?.Name);
        }

        // ================================================================
        // 基础设施
        // ================================================================

        /// <summary>
        /// 登记 route4 场景的快照：<c>PocDept→PocRoute4DeptDto</c> 已注册（嵌套被声明），
        /// 主对 <c>PocSite→PocRoute4SiteDto</c> 的 AllTargetSlots 含 Dept 但 Properties 不含
        /// —— 即 §6A 的「属性级 miss」形态（C2 递归入口的触发条件）。
        /// </summary>
        private static void RegisterRoute4Snapshots()
        {
            RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
            {
                SourceType = typeof(PocDept),
                TargetType = typeof(PocRoute4DeptDto),
                SourceTypeFqn = typeof(PocDept).FullName!,
                TargetTypeFqn = typeof(PocRoute4DeptDto).FullName!,
                Channel = RuntimeMappingChannel.MapFrom,
                Properties = new[] { Pm("Id", "Int32"), Pm("Name", "String") },
                AllTargetSlots = new[] { Slot("Id", "Int32"), Slot("Name", "String") },
            });

            RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
            {
                SourceType = typeof(PocSite),
                TargetType = typeof(PocRoute4SiteDto),
                SourceTypeFqn = typeof(PocSite).FullName!,
                TargetTypeFqn = typeof(PocRoute4SiteDto).FullName!,
                Channel = RuntimeMappingChannel.MapFrom,
                Properties = new[] { Pm("Id", "Int32"), Pm("Name", "String") },
                AllTargetSlots = new[]
                {
                    Slot("Id", "Int32"), Slot("Name", "String"), Slot("Dept", nameof(PocRoute4DeptDto)),
                },
            });
        }

        private static RuntimeTargetPropertySlot Slot(string name, string typeName)
            => new() { Name = name, TypeName = typeName };

        private static RuntimePropertyMapping Pm(string name, string typeName) => new()
        {
            Target = Slot(name, typeName),
            SourceName = name,
            SourcePathNames = new[] { name },
        };

        /// <summary>route4 场景的嵌套部门 DTO</summary>
        public class PocRoute4DeptDto
        {
            /// <summary>主键</summary>
            public int Id { get; set; }
            /// <summary>部门名</summary>
            public string? Name { get; set; }
        }

        /// <summary>route4 场景的目标 DTO（嵌套 Dept 在 AllTargetSlots 但不在 Properties）</summary>
        [MapFrom(typeof(PocSite))]
        public class PocRoute4SiteDto
        {
            /// <summary>主键</summary>
            public int Id { get; set; }
            /// <summary>站点名</summary>
            public string? Name { get; set; }
            /// <summary>部门 — 语义上应来自 join 的 PocDept</summary>
            public PocRoute4DeptDto? Dept { get; set; }
        }
    }
}
