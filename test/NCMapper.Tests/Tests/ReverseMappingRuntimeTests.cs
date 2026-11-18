using NCMapper;
using NCMapper.Attributes;
using NCMapper.Generated;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// B1 反向匹配运行时测试模型（非规范：MapFrom 标 Entity）
    /// <para>U18 双引擎一致性 + U3/U12 运行时值类型行为断言</para>
    /// </summary>
    public class R18Config
    {
        /// <summary>
        /// 主题（JSON 反序列化目标属性）
        /// </summary>
        public string Theme { get; set; } = "";
    }

    /// <summary>
    /// B1 场景 DTO — Config 属性标注 IsFromJson，声明源为 Entity.ConfigJson
    /// （场景规范化：锚迁至承载侧 DTO——原"MapFrom 标 Entity"非规范
    /// 形态属 019 拦截域；IsFromJson 双引擎语义与锚位置无关，断言保持）
    /// </summary>
    [MapFrom(typeof(R18Entity))]
    public class R18Dto
    {
        /// <summary>
        /// 唯一标识
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 配置对象（从 Entity.ConfigJson JSON 字符串反序列化，B1 触发场景）
        /// </summary>
        [MapProperty(typeof(R18Entity), nameof(R18Entity.ConfigJson), IsFromJson = true)]
        public R18Config Config { get; set; } = new();
    }

    /// <summary>
    /// B1 场景实体（类级 [MapFrom] 随场景规范化移除——锚已迁 R18Dto）
    /// </summary>
    public class R18Entity
    {
        /// <summary>
        /// 唯一标识
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 配置 JSON 字符串（B1 反向匹配目标属性）
        /// </summary>
        public string ConfigJson { get; set; } = "";
    }

    /// <summary>
    /// B1 反向匹配运行时双引擎一致性测试（U18 + U3/U12 运行时值类型）
    /// </summary>
    public class ReverseMappingRuntimeTests : IDisposable
    {
        /// <summary>
        /// 取消 R18 类型对的自动注册，使 Mapper.Map 走运行时路径（测运行时引擎）
        /// </summary>
        public ReverseMappingRuntimeTests()
        {
            Mapper.RegisterGenerated<R18Entity, R18Dto>(null!);
            Mapper.RegisterGeneratedCore<R18Entity, R18Dto>(null!);
            Mapper.RegisterGenerated<R18Dto, R18Entity>(null!);
            Mapper.RegisterGeneratedCore<R18Dto, R18Entity>(null!);
            Mapper.RegisterGenerated<R18ValEntity, R18ValDto>(null!);
            Mapper.RegisterGeneratedCore<R18ValEntity, R18ValDto>(null!);
        }

        /// <summary>
        /// 恢复 R18 类型对的自动注册（防止污染其他测试）
        /// </summary>
        public void Dispose()
        {
            Mapper.RegisterGeneratedCore<R18Entity, R18Dto>(GeneratedMapper.Map_R18Entity_To_R18Dto);
            Mapper.RegisterGenerated<R18Entity, R18Dto>(GeneratedMapper.Map_R18Entity_To_R18Dto_WithAction);
            Mapper.RegisterGeneratedCore<R18Dto, R18Entity>(GeneratedMapper.Map_R18Dto_To_R18Entity);
            Mapper.RegisterGenerated<R18Dto, R18Entity>(GeneratedMapper.Map_R18Dto_To_R18Entity_WithAction);
            Mapper.RegisterGeneratedCore<R18ValEntity, R18ValDto>(GeneratedMapper.Map_R18ValEntity_To_R18ValDto);
            Mapper.RegisterGenerated<R18ValEntity, R18ValDto>(GeneratedMapper.Map_R18ValEntity_To_R18ValDto_WithAction);
        }

        /// <summary>
        /// U18：Entity→Dto 双引擎一致性 — 运行时与生成侧反序列化结果一致
        /// </summary>
        [Fact]
        public void U18_EntityToDto_DualEngine_Consistent()
        {
            var entity = new R18Entity { Id = 7, ConfigJson = "{\"Theme\":\"Dark\"}" };

            // 运行时路径（构造函数已取消自动注册）
            var runtime = Mapper.Map<R18Entity, R18Dto>(entity)!;
            Assert.NotNull(runtime.Config);
            Assert.Equal("Dark", runtime.Config.Theme);

            // 生成侧路径（真实生成代码，直接调用）
            var gen = GeneratedMapper.Map_R18Entity_To_R18Dto(entity)!;
            Assert.NotNull(gen.Config);
            Assert.Equal("Dark", gen.Config.Theme);

            // 双引擎结果一致
            Assert.Equal(runtime.Id, gen.Id);
            Assert.Equal(runtime.Config.Theme, gen.Config.Theme);
        }

        /// <summary>
        /// U18：Dto→Entity 方向 — 双引擎均序列化写回（X2-B 落地，hch 原则 1/2）
        /// <para> W4S-3：运行时 targetIsDefine 分支补反向匹配 + 对侧 IsFromJson fallback，
        /// 消除 Dto→Entity 方向双引擎分叉（原 P2-1 选 A 差异登记 X2 已升级为 X2-B 修复）。
        /// 运行时行为从"跳过（ConfigJson='')"变为"序列化写回（ConfigJson=JSON）"，与生成侧一致。</para>
        /// </summary>
        [Fact]
        public void U18_DtoToEntity_DualEngine_BothSerialize()
        {
            var dto = new R18Dto { Id = 7, Config = new R18Config { Theme = "Light" } };

            // 生成侧：对象→string 序列化（B1 反向匹配建立的正向映射）
            var gen = GeneratedMapper.Map_R18Dto_To_R18Entity(dto)!;
            Assert.Contains("Light", gen.ConfigJson);

            // 运行时：X2-B 后 Dto→Entity 消费对侧 IsFromJson → 序列化写回（与生成侧一致，原则 2 达成）
            var runtime = Mapper.Map<R18Dto, R18Entity>(dto)!;
            Assert.Contains("Light", runtime.ConfigJson);
        }

        /// <summary>
        /// U3/U12 运行时侧：值类型参与 IsFromJson — 运行时跳过属性（与生成侧 return 行为一致）
        /// <para>非规范场景：Entity.Val(int) ← Dto.Val(int)，Dto.Val 标注 IsFromJson → 运行时 BuildJsonAssignment
        /// 值类型组合返回 null 跳过；生成侧改动点 3 return 跳过。双引擎一致。</para>
        /// </summary>
        [Fact]
        public void U12_Runtime_ValueTypeIsFromJson_Skipped()
        {
            var entity = new R18ValEntity { Id = 1, Val = 42 };
            var dto = Mapper.Map<R18ValEntity, R18ValDto>(entity)!;
            Assert.Equal(1, dto.Id);
            Assert.Equal(0, dto.Val); // 值类型 IsFromJson 被跳过，保持默认值（未赋值）
        }
    }

    /// <summary>
    /// U12 值类型场景实体（T5：类级 [MapFrom] 随场景规范化移除）
    /// </summary>
    public class R18ValEntity
    {
        /// <summary>
        /// 唯一标识
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 值类型字段（IsFromJson 不支持值类型，应跳过）
        /// </summary>
        public int Val { get; set; }
    }

    /// <summary>
    /// U12 值类型场景 DTO — Val 标注 IsFromJson（值类型，应跳过映射；T5 补锚同 R18Dto）
    /// </summary>
    [MapFrom(typeof(R18ValEntity))]
    public class R18ValDto
    {
        /// <summary>
        /// 唯一标识
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 值类型属性（IsFromJson 值类型参与 → 运行时与生成侧均跳过）
        /// </summary>
        [MapProperty(typeof(R18ValEntity), nameof(R18ValEntity.Val), IsFromJson = true)]
        public int Val { get; set; }
    }
}

/// <summary>
/// X1 运行时测试模型 — 对侧（源侧）带 [Convert]，锚点无标注（X1-1c 触发场景）
/// </summary>
public class X1RtOppositeEntity
{
    /// <summary>
    /// 标识字段
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 源侧带  的属性（对侧标注，锚点 DTO 无标注）
    /// </summary>
    public string Phone { get; set; } = "";
}

/// <summary>
/// X1 运行时锚点优先模型 — 锚点（DTO）带 [Convert]（①a 优先级）
/// </summary>
public class X1RtAnchorEntity
{
    /// <summary>
    /// 标识字段
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 普通属性（源侧无标注）
    /// </summary>
    public string Phone { get; set; } = "";
}

/// <summary>
/// X1 运行时目标 DTO — 锚点带 [Convert]（①a 场景）
/// </summary>
public class X1RtAnchorDto
{
    /// <summary>
    /// 标识字段
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 锚点带 （显式契约优先）
    /// </summary>
    public string Phone { get; set; } = "";
}

/// <summary>
/// X1 运行时目标 DTO（对侧消费场景）— 锚点无标注
/// </summary>
public class X1RtOppositeDto
{
    /// <summary>
    /// 标识字段
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 锚点无标注属性
    /// </summary>
    public string Phone { get; set; } = "";
}

/// <summary>
/// X1 前缀转换器（静态方法形态）— 在源值前加前缀，验证转换器被消费
/// </summary>
public static class X1RtPrefixConverter
{
    /// <summary>
    /// 在源值前加 "X1-"（T1 单值模板）
    /// </summary>
    /// <param name="fieldValue">源属性值</param>
    /// <returns>加前缀后的字符串</returns>
    public static string Convert(string fieldValue) => "X1-" + fieldValue;
}

/// <summary>
/// X1 运行时测试 — A-4：反向（!isToDto）一律不消费 [Convert]
/// （含对侧/锚点标注），降级类型转换路径；原 X1-1c/①a 消费语义由 [ConvertBack]承载
/// </summary>
public class X1RuntimeTests
{
    /// <summary>
    /// X1-1c 场景（锚点无标注 + 源侧带 [Convert]）：A-4——
    /// 反向不再消费对侧转换器，按类型转换路径直写（原"前缀转换器生效"语义失效）
    /// </summary>
    [Fact]
    public void X1_Runtime_Reverse_OppositeConvertUsing_NotConsumed()
    {
        var entity = new X1RtOppositeEntity { Id = 7, Phone = "13812345678" };
        var dto = Mapper.Map<X1RtOppositeEntity, X1RtOppositeDto>(entity)!;

        // 反向不消费对侧 [Convert]（无 X1- 前缀）
        Assert.Equal("13812345678", dto.Phone);
    }

    /// <summary>
    /// ①a 场景（锚点带 [Convert]）：A-4——
    /// 反向不再消费锚点转换器，按类型转换路径直写（原"锚点优先"语义失效）
    /// </summary>
    [Fact]
    public void X1_Runtime_Reverse_AnchorConvertUsing_NotConsumed()
    {
        var entity = new X1RtAnchorEntity { Id = 7, Phone = "13900000000" };
        var dto = Mapper.Map<X1RtAnchorEntity, X1RtAnchorDto>(entity)!;

        // 反向不消费锚点 [Convert]（无 X1- 前缀）
        Assert.Equal("13900000000", dto.Phone);
    }
}
