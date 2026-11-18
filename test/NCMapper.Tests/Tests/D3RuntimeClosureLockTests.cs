// 行 4——D3 运行时收口锁定全量
//   覆盖点（D9/D10 裁决后口径）：
//   ① 无锚双 false 退化按名匹配；② 互指对（双 true）运行时同步收敛纯按名；
//   ③ [MapProperty] 异名无锚不消费（TryGetMapPropertyInfo 单点门）；④ [MapIgnore] 无锚不消费；
//   ⑤ [Convert] 无锚不消费；⑥ [ConvertBack] 无锚不消费（承载侧单查）；⑦ [MapWhen] ②腿无锚不消费；
//   ⑧ 投影守卫两分支（[Mask]/[Convert]）拒绝成对（EfCoreProjectionBuilder 直调——不触发降级检测的
//   漏网形态由 :804-812 守卫兜底）；⑨ [MapTree] 类级无锚照常消费（D9 锚门回滚——TreeBuilder:151-153
//   直读形态锁定）；⑩ OverrideIgnore 豁免无锚不生效（HasOverrideIgnore 类级锚门）。
//   先验锚纪律（#67）：每个"无锚不消费"用例配同构有锚孪生类型断言消费生效，排除恒绿。
//   隔离：全局缓存/注册面 → [Collection("GlobalConfigState")]（同 GlobalIgnoreRuntimeTests 形态）。
using NCMapper;
using NCMapper.Attributes;
using NCMapper.EFCore;
using NCMapper.Internal;
using Xunit;

namespace NCMapper.Tests.Tests
{
    [Collection("GlobalConfigState")]
    public class D3RuntimeClosureLockTests : IDisposable
    {
        public D3RuntimeClosureLockTests()
        {
            Mapper.ClearCache();
            GlobalMapConfig.ResetForTests();
        }

        public void Dispose()
        {
            Mapper.ClearCache();
            GlobalMapConfig.ResetForTests();
        }

        // ===== ① 无锚双 false 退化按名 =====

        public class D3PlainSource { public int Id { get; set; } public string Name { get; set; } = ""; }
        public class D3PlainTarget { public int Id { get; set; } public string Name { get; set; } = ""; }

        /// <summary>① 双侧无锚（双 false）→ 纯按名匹配照常工作（退化不毁基本映射）。</summary>
        [Fact]
        public void NoAnchorPair_ByNames_MapsNormally()
        {
            var dst = Mapper.Map<D3PlainSource, D3PlainTarget>(new D3PlainSource { Id = 7, Name = "plain" });
            Assert.Equal(7, dst!.Id);
            Assert.Equal("plain", dst.Name);
        }

        // ===== ② 互指对（双 true）运行时收敛——迁至 SourceGen.Tests/DualEngineConsistencyLockTests =====
        //（025 Error 编译诊断阻断测试工程构建，fixture 须经合成编译承载——迁移登记）

        // ===== ③ MapProperty 异名：无锚不消费 / 有锚消费（先验锚）=====

        public class D3MpEntity { public int Code { get; set; } }

        [MapFrom(typeof(D3MpEntity))]
        public class D3MpAliasView
        {
            [MapProperty(typeof(D3MpEntity), "Code")]
            public int No { get; set; }
        }

        public class D3MpNoAnchorView
        {
            [MapProperty(typeof(D3MpEntity), "Code")]
            public int No { get; set; }
        }

        /// <summary>③ 无锚 [MapProperty] 异名不消费（门化）→ No 保持默认；有锚孪生异名生效。</summary>
        [Fact]
        public void MapProperty_Alias_NoAnchor_NotConsumed_WithAnchorConsumed()
        {
            var src = new D3MpEntity { Code = 42 };
            var anchored = Mapper.Map<D3MpEntity, D3MpAliasView>(src);
            Assert.Equal(42, anchored!.No);          // 先验锚：有锚异名映射生效
            var bare = Mapper.Map<D3MpEntity, D3MpNoAnchorView>(src);
            Assert.Equal(0, bare!.No);               // 无锚：标注不消费、按名匹配失败（Code≠No）
        }

        // ===== ④ MapIgnore：无锚不消费 / 有锚消费 =====

        public class D3MiEntity { public int Id { get; set; } public string Secret { get; set; } = ""; }

        [MapFrom(typeof(D3MiEntity))]
        public class D3MiAnchoredView
        {
            public int Id { get; set; }

            [MapIgnore]
            public string Secret { get; set; } = "KEEP";
        }

        public class D3MiNoAnchorView
        {
            public int Id { get; set; }

            [MapIgnore]
            public string Secret { get; set; } = "KEEP";
        }

        /// <summary>④ 无锚 [MapIgnore] 不消费（标注视同无标注）→ 属性照常复制；有锚孪生忽略生效。</summary>
        [Fact]
        public void MapIgnore_NoAnchor_NotConsumed_WithAnchorConsumed()
        {
            var src = new D3MiEntity { Id = 1, Secret = "leak-me" };
            var anchored = Mapper.Map<D3MiEntity, D3MiAnchoredView>(src);
            Assert.Equal("KEEP", anchored!.Secret);  // 先验锚：有锚忽略生效（初始值保持）
            var bare = Mapper.Map<D3MiEntity, D3MiNoAnchorView>(src);
            Assert.Equal("leak-me", bare!.Secret);   // 无锚：不忽略，照常复制
        }

        // ===== ⑤ Convert：无锚不消费 / 有锚消费 =====

        public class D3CvEntity { public int Id { get; set; } }

        public static class D3Converters
        {
            public static string Wrap(int v) => "#" + v;
        }

        [MapFrom(typeof(D3CvEntity))]
        public class D3CvAnchoredView
        {
            [Convert(typeof(D3Converters), nameof(D3Converters.Wrap))]
            public string Id { get; set; } = "";
        }

        public class D3CvNoAnchorView
        {
            [Convert(typeof(D3Converters), nameof(D3Converters.Wrap))]
            public string Id { get; set; } = "";
        }

        /// <summary>⑤ 无锚 [Convert] 不消费（转换器不调用）→ 值不含转换标记；有锚孪生转换生效。</summary>
        [Fact]
        public void Convert_NoAnchor_NotConsumed_WithAnchorConsumed()
        {
            var src = new D3CvEntity { Id = 5 };
            var anchored = Mapper.Map<D3CvEntity, D3CvAnchoredView>(src);
            Assert.Equal("#5", anchored!.Id);        // 先验锚：有锚转换器生效
            var bare = Mapper.Map<D3CvEntity, D3CvNoAnchorView>(src);
            Assert.DoesNotContain("#", bare!.Id);    // 无锚：转换器未调用
        }

        // ===== ⑥ ConvertBack：无锚不消费 / 有锚消费（更新路径承载侧单查）=====

        public class D3CbEntity { public string Tag { get; set; } = ""; }

        public static class D3BackConverters
        {
            public static string Mark(string v) => "B:" + v;
        }

        [MapFrom(typeof(D3CbEntity))]
        public class D3CbAnchoredView
        {
            [ConvertBack(typeof(D3BackConverters), nameof(D3BackConverters.Mark))]
            public string Tag { get; set; } = "";
        }

        public class D3CbNoAnchorView
        {
            [ConvertBack(typeof(D3BackConverters), nameof(D3BackConverters.Mark))]
            public string Tag { get; set; } = "";
        }

        /// <summary>⑥ 无锚 [ConvertBack] 反向写回不消费（转换器不调用）；有锚孪生反向转换生效。</summary>
        [Fact]
        public void ConvertBack_NoAnchor_NotConsumed_WithAnchorConsumed()
        {
            var viewA = new D3CbAnchoredView { Tag = "x" };
            var entityA = new D3CbEntity { Tag = "orig" };
            Mapper.Map(viewA, entityA);
            Assert.Equal("B:x", entityA.Tag);        // 先验锚：有锚反向转换生效

            var viewB = new D3CbNoAnchorView { Tag = "x" };
            var entityB = new D3CbEntity { Tag = "orig" };
            Mapper.Map(viewB, entityB);
            Assert.Equal("x", entityB.Tag);          // 无锚：转换器未调用，值直传
        }

        // ===== ⑦ MapWhen：无锚守卫不消费 / 有锚守卫生效 =====

        public class D3MwEntity { public int Id { get; set; } public string Note { get; set; } = ""; }

        [MapFrom(typeof(D3MwEntity))]
        public class D3MwAnchoredView
        {
            public int Id { get; set; }

            [MapWhen(nameof(D3MwAnchoredView.HasContent))]
            public string Note { get; set; } = "INIT";

            internal static bool HasContent(string v) => !string.IsNullOrEmpty(v);
        }

        public class D3MwNoAnchorView
        {
            public int Id { get; set; }

            [MapWhen(nameof(D3MwNoAnchorView.HasContent))]
            public string Note { get; set; } = "INIT";

            internal static bool HasContent(string v) => !string.IsNullOrEmpty(v);
        }

        /// <summary>
        /// ⑦ 无锚 [MapWhen] 谓词不消费 → 条件假仍赋值（空源值覆盖初始值）；
        /// 有锚孪生守卫生效（条件假跳过赋值保持初始值）。
        /// </summary>
        [Fact]
        public void MapWhen_NoAnchor_NotConsumed_WithAnchorConsumed()
        {
            var src = new D3MwEntity { Id = 1, Note = "" };   // 谓词求值 false
            var anchored = Mapper.Map<D3MwEntity, D3MwAnchoredView>(src);
            Assert.Equal("INIT", anchored!.Note);    // 先验锚：有锚守卫生效（跳过赋值）
            var bare = Mapper.Map<D3MwEntity, D3MwNoAnchorView>(src);
            Assert.Equal("", bare!.Note);            // 无锚：守卫不消费，空值照常覆盖
        }

        // ===== ⑧ 投影守卫两分支拒绝成对（[Mask]/[Convert]—— EfCoreProjectionBuilder 直调）=====
        // 触发形态 = 嵌套投影绑定（ProjectionExpressionFactory.BuildNestedProjectionBindings :804-812
        // 守卫所在方法）——嵌套子 DTO 属性带 [Mask]/[Convert] 时构建抛 MapConfigurationException
        //（拒绝非消费；顶层场景由降级检测先行分流，嵌套漏网形态由本守卫兜底）。

        public class D3GdChildEntity { public string Phone { get; set; } = ""; }
        public class D3GdChildEntity2 { public int Id { get; set; } }

        public static class D3GdConverters
        {
            public static string Masked(int v) => v.ToString();
        }

        [MapFrom(typeof(D3GdChildEntity))]
        public class D3GdMaskChild
        {
            public int Id { get; set; }

            [Mask(typeof(MaskPhone))]
            public string Phone { get; set; } = "";
        }

        [MapFrom(typeof(D3GdChildEntity2))]
        public class D3GdConvertChild
        {
            [Convert(typeof(D3GdConverters), nameof(D3GdConverters.Masked))]
            public string Id { get; set; } = "";
        }

        [MapFrom(typeof(D3GdEntity))]
        public class D3GdMaskParent
        {
            public int Id { get; set; }
            public D3GdMaskChild Child { get; set; } = new();
        }

        public class D3GdEntity { public int Id { get; set; } public D3GdChildEntity Child { get; set; } = new(); }
        public class D3GdEntity2 { public int Id { get; set; } public D3GdChildEntity2 Child { get; set; } = new(); }

        [MapFrom(typeof(D3GdEntity2))]
        public class D3GdConvertParent
        {
            public int Id { get; set; }
            public D3GdConvertChild Child { get; set; } = new();
        }

        /// <summary>
        /// ⑧ 投影守卫两分支拒绝成对（v1.3 补——[Mask]/[Convert] 同守卫理由同源）：
        /// 嵌套子 DTO 属性带 [Mask] 或 [Convert] 任一 → 投影表达式构建抛 MapConfigurationException。
        /// </summary>
        [Fact]
        public void ProjectionGuard_Mask_And_Convert_Rejection_Pair()
        {
            Assert.Throws<MapConfigurationException>(
                () => EfCoreProjectionBuilder.Build<D3GdEntity, D3GdMaskParent>());
            Assert.Throws<MapConfigurationException>(
                () => EfCoreProjectionBuilder.Build<D3GdEntity2, D3GdConvertParent>());
        }

        // ===== ⑨ [MapTree] 类级无锚照常消费（D9 锚门回滚形态锁定）=====

        /// <summary>无锚（无 [MapFrom]）类级 [MapTree] 自定义契约——树构建照常消费（D9：类级树配置非映射对治理域）。</summary>
        [MapTree("NodeId", "ParentNodeId", "Nodes")]
        public sealed class D3TreeNoAnchorDto
        {
            public int NodeId { get; set; }
            public int ParentNodeId { get; set; }
            public string Name { get; set; } = "";
            public List<D3TreeNoAnchorDto> Nodes { get; set; } = new();
        }

        public sealed class D3TreeRow
        {
            public int NodeId { get; set; }
            public int ParentNodeId { get; set; }
            public string Name { get; set; } = "";
        }

        /// <summary>
        /// ⑨ 无锚 [MapTree] 类级树配置照常生效（D9 改判——锁定"树构建照常消费"而非"不消费"）：
        /// EFCore ToTreeList 通道按自定义契约（NodeId/ParentNodeId/Nodes）构建成功。
        /// 武装（同 RuntimeHotPathProbeTests T001 形态）：无 [MapFrom] 目标的 project 通道拒绝，
        /// 经 EFCoreProjectionAsNoTrackingDefault=false + PostMap 强制降级进入内存映射 + 树构建。
        /// </summary>
        [Fact]
        public void MapTree_ClassLevel_NoAnchor_StillConsumed_AfterD9()
        {
            NCMapperEFCoreExtensions.EFCoreProjectionAsNoTrackingDefault = false;
            Mapper.Pipeline.AddPostMap<D3TreeRow, D3TreeNoAnchorDto>((s, t) => { });
            try
            {
                var rows = new List<D3TreeRow>
                {
                    new() { NodeId = 1, Name = "root" },
                    new() { NodeId = 2, ParentNodeId = 1, Name = "leaf" },
                };
                var tree = rows.AsQueryable().AsDto().ToTreeList<D3TreeNoAnchorDto>()!;
                var root = Assert.Single(tree);
                Assert.Equal("root", root.Name);
                var leaf = Assert.Single(root.Nodes);
                Assert.Equal("leaf", leaf.Name);
            }
            finally
            {
                NCMapperEFCoreExtensions.EFCoreProjectionAsNoTrackingDefault = true;
                Mapper.Pipeline.Clear();
            }
        }

        // ===== ⑩ OverrideIgnore 豁免：无锚不生效 / 有锚豁免 =====

        private class D3OiEntity
        {
            public int Id { get; set; }
            public string Password { get; set; } = "";
        }

        [MapFrom(typeof(D3OiEntity))]
        private class D3OiAnchoredView
        {
            public int Id { get; set; }

            [MapProperty(typeof(D3OiEntity), "Password", OverrideIgnore = true)]
            public string Password { get; set; } = "";
        }

        private class D3OiNoAnchorView
        {
            public int Id { get; set; }

            [MapProperty(typeof(D3OiEntity), "Password", OverrideIgnore = true)]
            public string Password { get; set; } = "";
        }

        /// <summary>
        /// ⑩ 全局忽略清单命中 + OverrideIgnore=true：无锚类豁免不生效（仍忽略——HasOverrideIgnore
        /// 类级锚门）；有锚孪生豁免生效（映射恢复）。
        /// </summary>
        [Fact]
        public void OverrideIgnore_NoAnchor_NoExempt_WithAnchorExempt()
        {
            Mapper.RegisterGeneratedConfig(new GeneratedMapConfig
            {
                IgnoreNames = ["Password"],
            }, "D3OiTest");

            var src = new D3OiEntity { Id = 1, Password = "pwd" };

            var bare = Mapper.Map<D3OiEntity, D3OiNoAnchorView>(src);
            Assert.Equal("", bare!.Password);        // 无锚：豁免不生效，全局忽略仍命中

            var anchored = Mapper.Map<D3OiEntity, D3OiAnchoredView>(src);
            Assert.Equal("pwd", anchored!.Password); // 先验锚：有锚豁免生效
        }
    }
}
