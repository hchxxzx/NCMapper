// 集成测试：EFCoreDtoSelector.Single/SingleAsync 降级腿（[Mask] 触发）唯一记录消息口径契约
// 三路自证（每格缺一即视为夹具绕开缺陷路径，经验库 #201/#202）：
//   ① 检测面 —— EfCoreDegradationDetector.NeedsDegradation 命中且 reason 含 "Mask"
//   ② 执行面 —— Trace 含降级标记（同步 "[EFCore AsDto] Single 降级" / 异步 "[EFCore AsDto] SingleAsync降级"）
//   ③ 脱敏实证 —— 恰 1 条时 Email 已被 MaskPhone 脱敏（真走内存映射腿的决定性证据）
using Microsoft.EntityFrameworkCore;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.EFCore;
using NCMapper.IntegrationTests.Models;
using Xunit;

namespace NCMapper.IntegrationTests
{
    /// <summary>
    /// 降级腿夹具：目标属性标注 [Mask]（IProjectionPolicy.RequiresDowngrade=true）→
    /// AsDto 终端方法走内存映射腿而非 SQL 投影腿。
    /// <para>类级 [MapFrom] 特性锚必备——属性级映射配置族标注在无特性锚类型上不参与降级判定
    /// （DegradationDetectorBase.IsAttributeFamilyWithoutAnchor），缺锚则本夹具绕开缺陷路径。</para>
    /// </summary>
    [MapFrom(typeof(UserEntity))]
    public class UserMaskedDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";

        /// <summary>脱敏列 — 降级腿自证锚点</summary>
        [Mask(typeof(MaskPhone))]
        public string Email { get; set; } = "";
    }

    /// <summary>
    /// EFCore Single/SingleAsync 降级腿契约测试 — 0 条 / ≥2 条 × 同步与异步四格。
    /// </summary>
    public class EFCoreSingleDowngradeContractTests : IDisposable
    {
        private const string SourceEmail = "alice@test.com";

        private readonly TestDbContext _db;

        public EFCoreSingleDowngradeContractTests()
        {
            var connStr = $"DataSource=file:downgrade{Guid.NewGuid():N}?mode=memory&cache=shared";
            _db = new TestDbContext(connStr);
            _db.SeedData();
        }

        public void Dispose() => _db.Dispose();

        /// <summary>Trace 捕获 — 照 C10ActionLabelTraceTests 先例（GloballyEnabled + GlobalOutput）</summary>
        private static (StringWriter Sw, Action Restore) CaptureTrace()
        {
            var sw = new StringWriter();
            var oldEnabled = MapTracer.GloballyEnabled;
            var oldOutput = MapTracer.GlobalOutput;
            MapTracer.GloballyEnabled = true;
            MapTracer.GlobalOutput = sw;
            return (sw, () => { MapTracer.GloballyEnabled = oldEnabled; MapTracer.GlobalOutput = oldOutput; });
        }

        /// <summary>① 检测面自证：夹具类型对必须命中降级判定</summary>
        private static void AssertDegradationDetected()
        {
            Assert.True(
                EfCoreDegradationDetector.NeedsDegradation(typeof(UserEntity), typeof(UserMaskedDto), out var reason),
                "夹具未命中降级判定——用例不覆盖降级腿（#201/#202 绕开缺陷路径）");
            Assert.Contains("Mask", reason);
        }

        [Fact]
        public void Single_降级腿_零条_抛SequenceNoElements()
        {
            AssertDegradationDetected();

            var (sw, restore) = CaptureTrace();
            try
            {
                // ③ 脱敏实证：恰 1 条 → 内存映射腿，Email 已脱敏（投影腿无法承载 [Mask]）
                var single = _db.Users.Where(u => u.Id == 1).AsDto().Single<UserMaskedDto>();
                Assert.NotNull(single);
                Assert.NotEqual(SourceEmail, single!.Email);

                var ex = Assert.Throws<InvalidOperationException>(
                    () => _db.Users.Where(u => u.Id < 0).AsDto().Single<UserMaskedDto>());

                // ② 执行面 Trace（同步降级腿标记，前缀与「降级」间有空格）
                Assert.Contains("[EFCore AsDto] Single 降级", sw.ToString());
                Assert.Equal(ErrorMessages.SequenceNoElements(nameof(UserMaskedDto)), ex.Message);
            }
            finally
            {
                restore();
            }
        }

        [Fact]
        public void Single_降级腿_多条_抛SequenceMultipleElements()
        {
            AssertDegradationDetected();

            var (sw, restore) = CaptureTrace();
            try
            {
                var single = _db.Users.Where(u => u.Id == 1).AsDto().Single<UserMaskedDto>();
                Assert.NotNull(single);
                Assert.NotEqual(SourceEmail, single!.Email);

                var ex = Assert.Throws<InvalidOperationException>(
                    () => _db.Users.AsDto().Single<UserMaskedDto>());

                Assert.Contains("[EFCore AsDto] Single 降级", sw.ToString());
                Assert.Equal(ErrorMessages.SequenceMultipleElements(nameof(UserMaskedDto)), ex.Message);
            }
            finally
            {
                restore();
            }
        }

        [Fact]
        public async Task SingleAsync_降级腿_零条_抛SequenceNoElements()
        {
            AssertDegradationDetected();

            var (sw, restore) = CaptureTrace();
            try
            {
                var single = await _db.Users.Where(u => u.Id == 1).AsDto()
                    .SingleAsync<UserMaskedDto>(cancellationToken: TestContext.Current.CancellationToken);
                Assert.NotNull(single);
                Assert.NotEqual(SourceEmail, single!.Email);

                var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => _db.Users.Where(u => u.Id < 0).AsDto()
                        .SingleAsync<UserMaskedDto>(cancellationToken: TestContext.Current.CancellationToken));

                // ② 执行面 Trace（异步降级腿标记，DtoSelectorSkeleton 文案无空格）
                Assert.Contains("[EFCore AsDto] SingleAsync降级", sw.ToString());
                Assert.Equal(ErrorMessages.SequenceNoElements(nameof(UserMaskedDto)), ex.Message);
            }
            finally
            {
                restore();
            }
        }

        [Fact]
        public async Task SingleAsync_降级腿_多条_抛SequenceMultipleElements()
        {
            AssertDegradationDetected();

            var (sw, restore) = CaptureTrace();
            try
            {
                var single = await _db.Users.Where(u => u.Id == 1).AsDto()
                    .SingleAsync<UserMaskedDto>(cancellationToken: TestContext.Current.CancellationToken);
                Assert.NotNull(single);
                Assert.NotEqual(SourceEmail, single!.Email);

                var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => _db.Users.AsDto()
                        .SingleAsync<UserMaskedDto>(cancellationToken: TestContext.Current.CancellationToken));

                Assert.Contains("[EFCore AsDto] SingleAsync降级", sw.ToString());
                Assert.Equal(ErrorMessages.SequenceMultipleElements(nameof(UserMaskedDto)), ex.Message);
            }
            finally
            {
                restore();
            }
        }
    }
}
