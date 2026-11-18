using NCMapper;
using NCMapper.Attributes;
using NCMapper.Runtime;
using System;
using System.Globalization;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// c) 锚语义锁定族——**Fluent 对不消费属性级标注**（"不消费"为断言方向，
    /// 防回归主锁；行 1 + 双引擎纪律）。
    /// <para>三态承载（007 探针四组转正形态）：① <b>空明细</b> Fluent 注册（<c>Properties = 空</c>，
    /// 探针 A/C 组）；② <b>满载明细</b> Fluent 注册（探针 D 组——"特性压过明细"缺陷的原现场）；
    /// ③ <b>无锚</b> 不注册（探针 B 组基线）。对照态 ④ <b>特性对</b>（<c>[MapFrom]</c>）照旧消费（零变化）。</para>
    /// <para>标注族覆盖：MapIgnore / MapProperty / Convert / ConvertBack / Mask / MapFormat 六族在本文件
    /// （MapNoAutoInclude 见 <c>TreeConfigConsumptionTests</c> 导航承载、MapWhen 见降级对照——八族齐）；
    /// 双向 = ToDto（<c>Map&lt;E,D&gt;</c>）与 ToEntity（<c>Map&lt;D,E&gt;</c>，ConvertBack 族主检向）。
    /// 007 探针（<c>Assert.True(true)</c> + 结果文件）自此**不再作为证据源**（d 步转正收口）。</para>
    /// <para>文化定桩：<c>Stamp</c> 断言期望侧显式 <c>CultureInfo.InvariantCulture</c>——
    /// 未标注属性转换恒 InvariantCulture 是库的既定设计，期望侧
    /// 禁止用环境文化渲染（<c>ToString()</c> 无参），否则 zh-CN 等非 invariant 环境必红（WSL/CI 无 LANG
    /// 恰为 invariant 掩盖过此敏感性）。</para>
    /// </summary>
    public class FluentAttributeNonConsumptionTests
    {
        // ═══════════════════════════ fixture ═══════════════════════════

        public class FacEntity
        {
            public int Id { get; set; }
            public string Phone { get; set; } = "";
            public string Tag { get; set; } = "";
            public string Alias { get; set; } = "";
            public string AliasTarget { get; set; } = "";
            public string Secret { get; set; } = "";
            public DateTime Stamp { get; set; }
        }

        /// <summary>Fluent 对目标（**无 [MapFrom]**）：六族标注齐备——本册口径下全部不消费。</summary>
        public class FacDto
        {
            public int Id { get; set; }
            [MapIgnore]
            public string Phone { get; set; } = "";
            [Convert(typeof(FacConv), nameof(FacConv.Convert))]
            public string Tag { get; set; } = "";
            [MapProperty(typeof(FacEntity), nameof(FacEntity.Alias))]
            public string AliasTarget { get; set; } = "";
            [Mask(typeof(FacMask))]
            public string Secret { get; set; } = "";
            [MapFormat("yyyy")]
            public string Stamp { get; set; } = "";
        }

        /// <summary>反向承载（ToEntity 主检向）：[ConvertBack] 在 Fluent 对上不消费。</summary>
        public class FacBackDto
        {
            public int Id { get; set; }
            [ConvertBack(typeof(FacBackConv), nameof(FacBackConv.ConvertBack))]
            public string Tag { get; set; } = "";
        }

        /// <summary>特性对对照（有 [MapFrom]）：同形态标注照旧消费（零变化断言）。</summary>
        [MapFrom(typeof(FacEntity))]
        public class FacAttrDto
        {
            public int Id { get; set; }
            [MapIgnore]
            public string Phone { get; set; } = "";
            [Convert(typeof(FacConv), nameof(FacConv.Convert))]
            public string Tag { get; set; } = "";
            [MapProperty(typeof(FacEntity), nameof(FacEntity.Alias))]
            public string AliasTarget { get; set; } = "";
            [Mask(typeof(FacMask))]
            public string Secret { get; set; } = "";
            [MapFormat("yyyy")]
            public string Stamp { get; set; } = "";
        }

        public static class FacConv { public static string Convert(string value) => "c:" + value; }
        public static class FacBackConv { public static string ConvertBack(string value) => "b:" + value; }
        public class FacMask : IMask { public string Mask(string? value) => (value ?? "") + "***"; }

        private static FacEntity NewEntity() => new()
        {
            Id = 7,
            Phone = "13800000000",
            Tag = "raw-tag",
            Alias = "alias-src",
            AliasTarget = "detail-src",
            Secret = "1234567890",
            Stamp = new DateTime(2026, 10, 10),
        };

        // ═══════════════════════ 注册辅助（三态承载）═══════════════════════

        private static RuntimeMapperMetadata Meta(Type src, Type tgt, RuntimePropertyMapping[] properties)
            => new()
            {
                SourceType = src,
                TargetType = tgt,
                SourceTypeFqn = src.FullName!,
                TargetTypeFqn = tgt.FullName!,
                Channel = RuntimeMappingChannel.Fluent,
                IsFluentForward = true,
                Properties = properties,
                // 满载明细态须同时承载目标槽位全集（007 探针 MetaWithDetail 同口径——缺此则明细不参与
                // 赋值面构建，实测 2026-10-10）
                AllTargetSlots = properties.Length == 0
                    ? Array.Empty<RuntimeTargetPropertySlot>()
                    : Array.ConvertAll(properties, p => p.Target),
            };

        private static RuntimePropertyMapping Detail(string targetName, string targetTypeName, string sourceName)
            => new()
            {
                Target = new RuntimeTargetPropertySlot { Name = targetName, TypeName = targetTypeName },
                SourceName = sourceName,
                SourcePathNames = new[] { sourceName },
            };

        /// <summary>满载明细：明细显式指定源（与标注指向不同名）——明细驱动结果可辨识、标注驱动结果可区分。</summary>
        private static RuntimePropertyMapping[] FullDetail() => new[]
        {
            Detail(nameof(FacDto.Id), "int", nameof(FacEntity.Id)),
            Detail(nameof(FacDto.Phone), "string", nameof(FacEntity.Phone)),   // 明细映射 vs [MapIgnore]
            Detail(nameof(FacDto.Tag), "string", nameof(FacEntity.Tag)),       // 明细裸值 vs [Convert]
            Detail(nameof(FacDto.AliasTarget), "string", nameof(FacEntity.AliasTarget)), // 明细同名取 AliasTarget vs [MapProperty] 指 Alias
            Detail(nameof(FacDto.Secret), "string", nameof(FacEntity.Secret)), // 明细裸值 vs [Mask]
            Detail(nameof(FacDto.Stamp), "string", nameof(FacEntity.Stamp)),   // 明细默认渲染 vs [MapFormat("yyyy")]
        };

        // ═══════════════════ 态①：空明细 Fluent 注册——六族不消费 ═══════════════════

        /// <summary>态①·MapIgnore：Fluent 对（空明细）+ [MapIgnore] → 按名赋值（不消费）。</summary>
        [Fact]
        public void EmptyDetail_FluentPair_MapIgnore_NotConsumed()
        {
            using var _ = new SnapshotScope();
            RuntimeSnapshotStore.Register(Meta(typeof(FacEntity), typeof(FacDto), Array.Empty<RuntimePropertyMapping>()));
            Mapper.ClearCache();

            var dto = Mapper.Map<FacEntity, FacDto>(NewEntity())!;
            Assert.Equal("13800000000", dto.Phone);
        }

        /// <summary>态①·Convert：Fluent 对（空明细）+ [Convert] → 裸值按名赋值（不消费）。</summary>
        [Fact]
        public void EmptyDetail_FluentPair_Convert_NotConsumed()
        {
            using var _ = new SnapshotScope();
            RuntimeSnapshotStore.Register(Meta(typeof(FacEntity), typeof(FacDto), Array.Empty<RuntimePropertyMapping>()));
            Mapper.ClearCache();

            var dto = Mapper.Map<FacEntity, FacDto>(NewEntity())!;
            Assert.Equal("raw-tag", dto.Tag);
        }

        /// <summary>态①·MapProperty：Fluent 对（空明细）+ [MapProperty] 指 Alias → 按名匹配取同名 AliasTarget（不消费）。</summary>
        [Fact]
        public void EmptyDetail_FluentPair_MapProperty_NotConsumed()
        {
            using var _ = new SnapshotScope();
            RuntimeSnapshotStore.Register(Meta(typeof(FacEntity), typeof(FacDto), Array.Empty<RuntimePropertyMapping>()));
            Mapper.ClearCache();

            var dto = Mapper.Map<FacEntity, FacDto>(NewEntity())!;
            Assert.Equal("detail-src", dto.AliasTarget);   // 按名同名匹配
            Assert.NotEqual("alias-src", dto.AliasTarget); // 标注指向的 Alias 未被取用
        }

        /// <summary>态①·Mask：Fluent 对（空明细）+ [Mask] → 裸值（不消费）。</summary>
        [Fact]
        public void EmptyDetail_FluentPair_Mask_NotConsumed()
        {
            using var _ = new SnapshotScope();
            RuntimeSnapshotStore.Register(Meta(typeof(FacEntity), typeof(FacDto), Array.Empty<RuntimePropertyMapping>()));
            Mapper.ClearCache();

            var dto = Mapper.Map<FacEntity, FacDto>(NewEntity())!;
            Assert.Equal("1234567890", dto.Secret);
        }

        /// <summary>态①·MapFormat：Fluent 对（空明细）+ [MapFormat("yyyy")] → 默认渲染（不消费）。</summary>
        [Fact]
        public void EmptyDetail_FluentPair_MapFormat_NotConsumed()
        {
            using var _ = new SnapshotScope();
            RuntimeSnapshotStore.Register(Meta(typeof(FacEntity), typeof(FacDto), Array.Empty<RuntimePropertyMapping>()));
            Mapper.ClearCache();

            var e = NewEntity();
            var dto = Mapper.Map<FacEntity, FacDto>(e)!;
            Assert.Equal(e.Stamp.ToString(CultureInfo.InvariantCulture), dto.Stamp);
            Assert.NotEqual("2026", dto.Stamp);
        }

        /// <summary>态①·ConvertBack（双向 ToEntity 主检向）：Fluent 对 + [ConvertBack] → 裸值写回（不消费）。</summary>
        [Fact]
        public void EmptyDetail_FluentPair_ConvertBack_NotConsumed_ReverseDirection()
        {
            using var _ = new SnapshotScope();
            RuntimeSnapshotStore.Register(Meta(typeof(FacBackDto), typeof(FacEntity), Array.Empty<RuntimePropertyMapping>()));
            Mapper.ClearCache();

            var back = Mapper.Map<FacBackDto, FacEntity>(new FacBackDto { Id = 1, Tag = "front-tag" })!;
            Assert.Equal("front-tag", back.Tag);
        }

        // ═══════════════════ 态②：满载明细 Fluent 注册——明细压过标注 ═══════════════════

        /// <summary>态②·MapIgnore：满载明细映射 Phone → 值在位（"特性压过明细"缺陷消除——007 D 组原现场）。</summary>
        [Fact]
        public void FullDetail_FluentPair_MapIgnore_DetailWins()
        {
            using var _ = new SnapshotScope();
            RuntimeSnapshotStore.Register(Meta(typeof(FacEntity), typeof(FacDto), FullDetail()));
            Mapper.ClearCache();

            var dto = Mapper.Map<FacEntity, FacDto>(NewEntity())!;
            Assert.Equal("13800000000", dto.Phone);
        }

        /// <summary>态②·Convert：满载明细裸映射 Tag → 明细值（不经标注转换器）。</summary>
        [Fact]
        public void FullDetail_FluentPair_Convert_DetailWins()
        {
            using var _ = new SnapshotScope();
            RuntimeSnapshotStore.Register(Meta(typeof(FacEntity), typeof(FacDto), FullDetail()));
            Mapper.ClearCache();

            var dto = Mapper.Map<FacEntity, FacDto>(NewEntity())!;
            Assert.Equal("raw-tag", dto.Tag);
        }

        /// <summary>态②·MapProperty：明细同名取 AliasTarget、标注指 Alias → 明细胜（= AliasTarget 值，非 Alias 值）。</summary>
        [Fact]
        public void FullDetail_FluentPair_MapProperty_DetailWins()
        {
            using var _ = new SnapshotScope();
            RuntimeSnapshotStore.Register(Meta(typeof(FacEntity), typeof(FacDto), FullDetail()));
            Mapper.ClearCache();

            var dto = Mapper.Map<FacEntity, FacDto>(NewEntity())!;
            Assert.Equal("detail-src", dto.AliasTarget);
        }

        /// <summary>态②·Mask：满载明细裸映射 Secret → 裸值（不脱敏）。</summary>
        [Fact]
        public void FullDetail_FluentPair_Mask_DetailWins()
        {
            using var _ = new SnapshotScope();
            RuntimeSnapshotStore.Register(Meta(typeof(FacEntity), typeof(FacDto), FullDetail()));
            Mapper.ClearCache();

            var dto = Mapper.Map<FacEntity, FacDto>(NewEntity())!;
            Assert.Equal("1234567890", dto.Secret);
        }

        /// <summary>态②·MapFormat：满载明细默认渲染 → 非 "yyyy" 格式化值。</summary>
        [Fact]
        public void FullDetail_FluentPair_MapFormat_DetailWins()
        {
            using var _ = new SnapshotScope();
            RuntimeSnapshotStore.Register(Meta(typeof(FacEntity), typeof(FacDto), FullDetail()));
            Mapper.ClearCache();

            var e = NewEntity();
            var dto = Mapper.Map<FacEntity, FacDto>(e)!;
            Assert.Equal(e.Stamp.ToString(CultureInfo.InvariantCulture), dto.Stamp);
            Assert.NotEqual("2026", dto.Stamp);
        }

        // ═══════════════════ 态③：无锚对照（不注册）——不消费不变 ═══════════════════

        /// <summary>态③·无锚（B 组基线）：六族标注一律不消费（T5 既有口径零变化）。</summary>
        [Fact]
        public void NoAnchor_MapIgnoreAndFriends_NotConsumed()
        {
            using var _ = new SnapshotScope();

            var e = NewEntity();
            var dto = Mapper.Map<FacEntity, FacDto>(e)!;
            Assert.Equal("13800000000", dto.Phone);
            Assert.Equal("raw-tag", dto.Tag);
            Assert.Equal("detail-src", dto.AliasTarget);
            Assert.NotEqual("alias-src", dto.AliasTarget);
            Assert.Equal("1234567890", dto.Secret);
            Assert.Equal(e.Stamp.ToString(CultureInfo.InvariantCulture), dto.Stamp);
        }

        // ═══════════════════ 态④：特性对对照——照旧消费（零变化）═══════════════════

        /// <summary>态④·特性对（[MapFrom]）：六族标注照旧消费——MapIgnore 剔除、Convert 生效、
        /// MapProperty 异名命中、Mask 脱敏、MapFormat 格式化。</summary>
        [Fact]
        public void AttributeAnchored_Pair_AllFamilies_StillConsumed()
        {
            using var _ = new SnapshotScope();

            var e = NewEntity();
            var dto = Mapper.Map<FacEntity, FacAttrDto>(e)!;
            Assert.Equal("", dto.Phone);                       // [MapIgnore] 消费
            Assert.Equal("c:raw-tag", dto.Tag);                // [Convert] 消费
            Assert.Equal("alias-src", dto.AliasTarget);        // [MapProperty] 消费
            Assert.Equal("1234567890***", dto.Secret);         // [Mask] 消费
            Assert.Equal("2026", dto.Stamp);                   // [MapFormat] 消费
        }

        // ═══════════════════ 混合形态：[MapFrom] + Fluent 注册并存 ═══════════════════

        /// <summary>混合形态（第 4 行 = 读）：[MapFrom] + Fluent 注册并存 → 标注照旧消费；
        /// 并断言 <c>Mapper.ClearCache()</c> 后判定路径不变（缓存面覆盖——评审 002）。</summary>
        [Fact]
        public void MixedForm_MapFromPlusFluentRegistration_Consumes_AfterClearCache()
        {
            using var _ = new SnapshotScope();
            RuntimeSnapshotStore.Register(Meta(typeof(FacEntity), typeof(FacAttrDto), Array.Empty<RuntimePropertyMapping>()));
            Mapper.ClearCache();

            var first = Mapper.Map<FacEntity, FacAttrDto>(NewEntity())!;
            Assert.Equal("", first.Phone);
            Assert.Equal("c:raw-tag", first.Tag);

            // 缓存清理后重判：特性锚经反射腿负/正缓存重建，判定路径与结果一致
            Mapper.ClearCache();
            var second = Mapper.Map<FacEntity, FacAttrDto>(NewEntity())!;
            Assert.Equal("", second.Phone);
            Assert.Equal("c:raw-tag", second.Tag);
        }

        /// <summary>混合形态·Fluent 侧（无 [MapFrom]）经二次 ClearCache 后仍不消费（谓词缓存重建稳定）。</summary>
        [Fact]
        public void FluentPair_AfterClearCache_StillNotConsumed()
        {
            using var _ = new SnapshotScope();
            RuntimeSnapshotStore.Register(Meta(typeof(FacEntity), typeof(FacDto), Array.Empty<RuntimePropertyMapping>()));
            Mapper.ClearCache();

            var first = Mapper.Map<FacEntity, FacDto>(NewEntity())!;
            Assert.Equal("13800000000", first.Phone);
            Assert.Equal("raw-tag", first.Tag);

            Mapper.ClearCache();
            var second = Mapper.Map<FacEntity, FacDto>(NewEntity())!;
            Assert.Equal("13800000000", second.Phone);
            Assert.Equal("raw-tag", second.Tag);
        }

        // ═══════════════════ 反向验证锚（NF1 防假绿）═══════════════════

        // 反向验证锚 = 态④ `AttributeAnchored_Pair_AllFamilies_StillConsumed`（特性对六族照旧消费）：
        // ① 若准入被整体放开（缺陷回归），态①/②/③ 六族"不消费"断言集体红；
        // ② 若准入被过度收紧（把特性对也关掉），态④ 先红——两侧互为判别，不存在 vacuous pass 空间。
        // 明细承载侧（HasConverter/HasMask 运行时回退不参与赋值面构建——实测 2026-10-10）不入本族断言面，
        // 明细驱动能力由态② FullDetail 五例（明细取异名源）锁定。

        /// <summary>快照隔离作用域：进出各清一次（串行执行前提 = AssemblyInfo 禁并行）。</summary>
        private sealed class SnapshotScope : IDisposable
        {
            public SnapshotScope()
            {
                RuntimeSnapshotStore.Clear();
                Mapper.ClearCache();
            }

            public void Dispose()
            {
                RuntimeSnapshotStore.Clear();
                Mapper.ClearCache();
            }
        }
    }
}
