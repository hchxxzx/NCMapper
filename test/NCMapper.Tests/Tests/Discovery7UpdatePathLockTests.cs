// 发现#7 锁定用例（释放登记承接——#1 呈报）
//   发现#7 = Map(src,target) 更新路径对承载侧 [MapIgnore(ToEntity)] 的消费缺口
//   （001 samples 606 反向写回实证，提交 5338a22/845b77e）。
//
//  **执行期实测定性反转（2026-09-26，Linux net10.0 Release 实测——#5 登记）**：
// 终态（a4f2c69 基线 + 本档改动）上复测：
//   - 生成端更新路径（有锚对，命中 GeneratedCache.UpdateAction）：承载侧 [MapIgnore(ToEntity)]
//     **正确消费**（反向写回保持原值）——606 原始红不复现；
//   - 生成端反向新建：**正确消费**（跳过赋值，新建对象保持默认值）；
//   - 运行时回退（无锚对）：不消费 = "无锚不消费"目标态语义（非缺口，与 D3 收口④同语义）。
//  定性：发现#7 系中间态现象，发现#1 修复（assignmentplanner 三特性
//  承载侧锚门补齐 + 直调 fixture 补锚）时连带闭合；001 修复记录与 606 退役时点差导致
//  登记为遗留。本文件按实测反转后**锁定终态正确行为**，#1 呈报"归档不推翻 + 001 审计留痕"。
using NCMapper;
using NCMapper.Attributes;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// 发现#7 锁定：承载侧 [MapIgnore(ToEntity)] 反向消费的终态行为（生成端消费正确 ∧ 无锚不消费目标态）
    /// </summary>
    public class Discovery7UpdatePathLockTests
    {
        public class D7Entity
        {
            public int Id { get; set; }
            public string AuditStamp { get; set; } = "";
        }

        [MapFrom(typeof(D7Entity))]
        public class D7Dto
        {
            public int Id { get; set; }

            /// <summary>承载侧声明：仅反向写回忽略（A-11 合法形态——001 T5 后唯一合法侧别）。</summary>
            [MapIgnore(Direction.ToEntity)]
            public string AuditStamp { get; set; } = "";
        }

        /// <summary>
        /// 正向对照：[MapIgnore(ToEntity)] 不拦正向（Entity→Dto）——AuditStamp 照常映射。
        /// </summary>
        [Fact]
        public void Forward_ToEntityIgnore_DoesNotBlock()
        {
            var src = new D7Entity { Id = 1, AuditStamp = "STAMP-2026" };
            var dto = Mapper.Map<D7Entity, D7Dto>(src)!;
            Assert.Equal("STAMP-2026", dto.AuditStamp);
        }

        /// <summary>
        /// 发现#7 现场锁定（终态正确行为）：Map(dto, existingEntity) 更新路径——承载侧
        /// [MapIgnore(ToEntity)] 被正确消费，反向写回跳过 AuditStamp（原值保持）。
        /// <para>606 原始红（覆盖）在 001 终态不复现——实测取证见文件头与 #5。</para>
        /// </summary>
        [Fact]
        public void ReverseUpdatePath_CarrierSideToEntityIgnore_Consumed_KeepOriginal()
        {
            var dto = new D7Dto { Id = 1, AuditStamp = "STAMP-2026" };
            var existing = new D7Entity { Id = 1, AuditStamp = "KEEP-ORIG" };
            Mapper.Map(dto, existing);
            Assert.Equal("KEEP-ORIG", existing.AuditStamp);
        }

        /// <summary>
        /// 反向新建锁定：Map&lt;D7Dto, D7Entity&gt;(dto)——承载侧 [MapIgnore(ToEntity)] 消费，
        /// 新建对象 AuditStamp 保持默认（跳过赋值）。
        /// </summary>
        [Fact]
        public void ReverseCreatePath_CarrierSideToEntityIgnore_Consumed_SkipAssignment()
        {
            var dto = new D7Dto { Id = 2, AuditStamp = "STAMP-2026" };
            var created = Mapper.Map<D7Dto, D7Entity>(dto)!;
            Assert.Equal("", created.AuditStamp);
        }

        /// <summary>
        /// 运行时回退对照（无锚对）：无锚类型上的 [MapIgnore(ToEntity)] 不消费 = "无锚不消费"
        /// 目标态语义（标注视而不见、属性照常覆盖）——非缺口，与 D3 收口④同语义锁定。
        /// </summary>
        [Fact]
        public void RuntimeFallbackPath_NoAnchor_IgnoreNotConsumed_ByDesign()
        {
            var dto = new D7RtDto { Id = 3, AuditStamp = "RT-STAMP" };
            var existing = new D7RtEntity { Id = 3, AuditStamp = "RT-KEEP" };
            Mapper.Map(dto, existing);
            Assert.Equal("RT-STAMP", existing.AuditStamp);
        }

        public class D7RtEntity
        {
            public int Id { get; set; }
            public string AuditStamp { get; set; } = "";
        }

        public class D7RtDto
        {
            public int Id { get; set; }

            [MapIgnore(Direction.ToEntity)]
            public string AuditStamp { get; set; } = "";
        }
    }
}
