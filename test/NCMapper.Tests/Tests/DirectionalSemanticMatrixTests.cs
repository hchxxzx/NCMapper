// 行 3——方向语义矩阵（四格 × 特性/Fluent
//   双通道 = 8 用例）。实体承载侧对称性为盲区重点（格 3/格 4——For 声明 T = 实体侧）。
//   判定式：isToDto = 目标类型映射声明锚（[MapFrom] ∨ 注册表命中，载体相对语义）；
//  [Convert] 消费 ⇔ isToDto=true；[ConvertBack] 消费 ⇔ isToDto=false（Q3 矩阵定案）。
//   Fluent 通道 = For<S,T>() 声明（fluentTargets 豁免锚 + 注册表锚），属性级转换消费经
//   特性 [Convert]/[ConvertBack] 承载（A-10/A-09 双通道——Fluent 终端 .Member(dest).Convert()/.ConvertBack()
//   与特性填充同一快照字段，本矩阵锁定特性侧消费方向）。
//   先验锚：每格含消费断言 + 对角不消费断言（标记前缀 = 转换器唯一产物途径）。
using NCMapper;
using NCMapper.Attributes;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// 方向语义矩阵锁定——四格 × 双通道（承接）
    /// </summary>
    public class DirectionalSemanticMatrixTests
    {
        // 共享转换器（标记前缀 = 转换器调用的唯一产物途径，断言不含前缀 = 未消费）
        public static class DsmConverters
        {
            public static string Wrap(int v) => "#" + v;
        }

        public static class DsmBackConverters
        {
            public static string Mark(string v) => "B:" + v;
        }

        // ================= 特性通道（4 格）=================

        // ── 格 1：DTO 承载 + Entity→Dto（isToDto=true）：[Convert] 消费 / [ConvertBack] 不消费 ──

        public class DsmA1Entity { public int Id { get; set; } public string Note { get; set; } = "N1"; }

        [MapFrom(typeof(DsmA1Entity))]
        public class DsmA1View
        {
            [Convert(typeof(DsmConverters), nameof(DsmConverters.Wrap))]
            public string Id { get; set; } = "";

            [ConvertBack(typeof(DsmBackConverters), nameof(DsmBackConverters.Mark))]
            public string Note { get; set; } = "";
        }

        [Fact]
        public void Grid1_DtoCarrier_EntityToDto_ConvertConsumed_ConvertBackNot()
        {
            var dto = Mapper.Map<DsmA1Entity, DsmA1View>(new DsmA1Entity { Id = 5, Note = "N1" })!;
            Assert.Equal("#5", dto.Id);      // [Convert] 消费（isToDto=true）
            Assert.Equal("N1", dto.Note);    // [ConvertBack] 不消费（直传无标记）
        }

        // ── 格 2：DTO 承载 + Dto→Entity（isToDto=false）：[ConvertBack] 消费 / [Convert] 不消费 ──

        public class DsmA2Entity { public string Note { get; set; } = ""; }

        [MapFrom(typeof(DsmA2Entity))]
        public class DsmA2View
        {
            [Convert(typeof(DsmConverters), nameof(DsmConverters.Wrap))]
            public string Extra { get; set; } = "";

            [ConvertBack(typeof(DsmBackConverters), nameof(DsmBackConverters.Mark))]
            public string Note { get; set; } = "";
        }

        [Fact]
        public void Grid2_DtoCarrier_DtoToEntity_ConvertBackConsumed_ConvertNot()
        {
            var entity = Mapper.Map<DsmA2View, DsmA2Entity>(new DsmA2View { Extra = "E", Note = "x" })!;
            Assert.Equal("B:x", entity.Note);  // [ConvertBack] 消费（isToDto=false）
            // [Convert] 不消费：Extra 无源同名属性、转换器不调用（无 "#" 注入）
            Assert.DoesNotContain("#", entity.Note);
        }

        // ── 格 3：Entity 承载 + Dto→Entity（isToDto=true）：[Convert] 消费（实体承载对称性）──

        [MapFrom(typeof(DsmA3View))]
        public class DsmA3Entity
        {
            [Convert(typeof(DsmConverters), nameof(DsmConverters.Wrap))]
            public string Id { get; set; } = "";
        }

        public class DsmA3View { public int Id { get; set; } }

        [Fact]
        public void Grid3_EntityCarrier_DtoToEntity_ConvertConsumed()
        {
            // [MapFrom] 标在实体侧（承载）→ Map<View, Entity> 的 target 有锚 → isToDto=true
            var entity = Mapper.Map<DsmA3View, DsmA3Entity>(new DsmA3View { Id = 5 })!;
            Assert.Equal("#5", entity.Id);   // 实体承载侧 [Convert] 对称消费
        }

        // ── 格 4：Entity 承载 + Entity→Dto（isToDto=false）：[ConvertBack] 消费（实体承载对称性）──

        [MapFrom(typeof(DsmA4View))]
        public class DsmA4Entity
        {
            [ConvertBack(typeof(DsmBackConverters), nameof(DsmBackConverters.Mark))]
            public string Id { get; set; } = "";
        }

        public class DsmA4View { public string Id { get; set; } = ""; }

        [Fact]
        public void Grid4_EntityCarrier_EntityToDto_ConvertBackConsumed()
        {
            // Map<Entity, View>：target=View 无锚 → isToDto=false → 实体承载侧 [ConvertBack] 消费
            var view = Mapper.Map<DsmA4Entity, DsmA4View>(new DsmA4Entity { Id = "raw" })!;
            Assert.Equal("B:raw", view.Id);
        }

        // ================= Fluent 通道（4 格——终端形态）=================
            // Fluent 声明对的属性级转换以通道内终端（.Member(dest).Convert()/.ConvertBack()）表达；
        // 混合形态（For 对 + 属性侧 [Convert] 特性）不消费——见 FluentGrid5 混合形态锁定。

            // ── 格 1：For&lt;E,V&gt;（V 承载）+ Member(dest).Convert 终端 → Entity→Dto 消费 ──

        public class DsmF1Entity { public int Id { get; set; } }
        public class DsmF1View { public string Id { get; set; } = ""; }

        [MapperConfig]
        public class DsmCfg1
        {
            public static void Configure(FluentMappingConfig cfg)
                => cfg.For<DsmF1Entity, DsmF1View>()
                      .Member(d => d.Id).From(s => s.Id)
                      .Member(d => d.Id).Convert(s => "#" + s.Id);
        }

        [Fact]
        public void FluentGrid1_ConvertTerminal_EntityToDto_Consumed()
        {
            var dto = Mapper.Map<DsmF1Entity, DsmF1View>(new DsmF1Entity { Id = 5 })!;
            Assert.Equal("#5", dto.Id);
        }

            // ── 格 2：For&lt;E,V&gt;（V 承载）+ Member(dest).ConvertBack 终端 → Dto→Entity 消费 ──

        public class DsmF2Entity { public string Note { get; set; } = ""; }
        public class DsmF2View { public string Note { get; set; } = ""; }

        [MapperConfig]
        public class DsmCfg2
        {
            public static void Configure(FluentMappingConfig cfg)
                => cfg.For<DsmF2Entity, DsmF2View>()
                      .Member(d => d.Note).From(s => s.Note)
                      .Member(d => d.Note).ConvertBack(s => "B:" + s.Note);
        }

        [Fact]
        public void FluentGrid2_ConvertBackTerminal_DtoToEntity_Consumed()
        {
            var entity = Mapper.Map<DsmF2View, DsmF2Entity>(new DsmF2View { Note = "x" })!;
            Assert.Equal("B:x", entity.Note);
        }

            // ── 格 3：For&lt;V,E&gt;（实体承载）+ Member(dest).Convert 终端 → Dto→Entity 消费（对称性盲区重点）──

        public class DsmF3View { public int Id { get; set; } }
        public class DsmF3Entity { public string Id { get; set; } = ""; }

        [MapperConfig]
        public class DsmCfg3
        {
            public static void Configure(FluentMappingConfig cfg)
                => cfg.For<DsmF3View, DsmF3Entity>()
                      .Member(d => d.Id).From(s => s.Id)
                      .Member(d => d.Id).Convert(s => "#" + s.Id);
        }

        [Fact]
        public void FluentGrid3_EntityCarrier_DtoToEntity_ConvertConsumed()
        {
                // For<V,E>：T=实体承载 → Map<V,E> isToDto=true → Member(dest).Convert 终端消费
            var entity = Mapper.Map<DsmF3View, DsmF3Entity>(new DsmF3View { Id = 5 })!;
            Assert.Equal("#5", entity.Id);
        }

            // ── 格 4：For&lt;V,E&gt;（实体承载）+ Member(dest).ConvertBack 终端 → Entity→Dto 消费（对称性盲区重点）──

        public class DsmF4View { public string Id { get; set; } = ""; }
        public class DsmF4Entity { public string Id { get; set; } = ""; }

        [MapperConfig]
        public class DsmCfg4
        {
            public static void Configure(FluentMappingConfig cfg)
                => cfg.For<DsmF4View, DsmF4Entity>()
                      .Member(d => d.Id).From(s => s.Id)
                      .Member(d => d.Id).ConvertBack(s => "B:" + s.Id);
        }

        [Fact]
        public void FluentGrid4_EntityCarrier_EntityToDto_ConvertBackConsumed()
        {
                // Map<E,V>（isToDto=false，相对 For 声明的反向）→ Member(dest).ConvertBack 终端消费
            var view = Mapper.Map<DsmF4Entity, DsmF4View>(new DsmF4Entity { Id = "raw" })!;
            Assert.Equal("B:raw", view.Id);
        }

        // ── 混合形态锁定（执行期实测新增——登记）：For 对目标属性上的 [Convert] 特性不消费 ──
        //（Fluent 声明对的配置面 = 通道内终端；特性侧 [Convert] 仅服务 [MapFrom] 通道。
        //  For 目标经 fluentTargets 豁免不报 019；属性值走类型直转 int→string。）

        public class DsmF5Entity { public int Id { get; set; } }

        public class DsmF5View
        {
            [Convert(typeof(DsmConverters), nameof(DsmConverters.Wrap))]
            public string Id { get; set; } = "";
        }

        [MapperConfig]
        public class DsmCfg5
        {
            public static void Configure(FluentMappingConfig cfg)
                => cfg.For<DsmF5Entity, DsmF5View>().Member(d => d.Id).From(s => s.Id);
        }

        [Fact]
        public void FluentGrid5_MixedForm_AttributeConvertOnForTarget_NotConsumed()
        {
            // 混合形态行为锁定：特性不消费（无 "#" 前缀），属性按类型直转
            var dto = Mapper.Map<DsmF5Entity, DsmF5View>(new DsmF5Entity { Id = 5 })!;
            Assert.Equal("5", dto.Id);
        }
    }
}
