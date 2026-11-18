using NCMapper;
using NCMapper.Attributes;
using NCMapper.Generated;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// 验收 1 运行时列实体 — 直接属性反向消费 [ConvertBack]
    /// </summary>
    public class CbuR1Entity
    {
        /// <summary>
        /// 标签（反向目标属性，与 DTO 同类型 string）
        /// </summary>
        public string Tag { get; set; } = "";
    }

    /// <summary>
    /// 验收 1 反向转换器（T1 模板 M(TField)）— 输出带 "back:" 前缀
    /// </summary>
    public static class CbuR1BackConverter
    {
        /// <summary>
        /// 将 DTO 标签值转换为实体标签值（前缀标识转换器被消费）
        /// </summary>
        /// <param name="fieldValue">DTO 标签值</param>
        /// <returns>带 "back:" 前缀的标签值</returns>
        public static string Convert(string fieldValue) => "back:" + fieldValue;
    }

    /// <summary>
    /// 验收 1 运行时列 DTO — Tag 标 [ConvertBack]
    /// </summary>
    [MapFrom(typeof(CbuR1Entity))]
    public class CbuR1Dto
    {
        /// <summary>
        /// 标签（反向经转换器写回实体）
        /// </summary>
        [ConvertBack(typeof(CbuR1BackConverter), nameof(CbuR1BackConverter.Convert))]
        public string Tag { get; set; } = "";
    }

    /// <summary>
    /// 验收 2 写回路径实体 — Address 导航属性链
    /// </summary>
    public class CbuR2Entity
    {
        /// <summary>
        /// 地址（写回链中间节点）
        /// </summary>
        public CbuR2Address Address { get; set; } = new();
    }

    /// <summary>
    /// 验收 2 写回链叶类型 — City 属性
    /// </summary>
    public class CbuR2Address
    {
        /// <summary>
        /// 城市（反向写回叶）
        /// </summary>
        public string City { get; set; } = "";
    }

    /// <summary>
    /// 验收 2 反向转换器（T1 模板 M(TField)）
    /// </summary>
    public static class CbuR2BackConverter
    {
        /// <summary>
        /// 将 DTO 城市值转换为实体城市值（前缀标识转换器被消费）
        /// </summary>
        /// <param name="fieldValue">DTO 城市值</param>
        /// <returns>带 "city:" 前缀的城市值</returns>
        public static string Convert(string fieldValue) => "city:" + fieldValue;
    }

    /// <summary>
    /// 验收 2 运行时列 DTO — City 标 [MapProperty] 写回链 + [ConvertBack]
    /// </summary>
    [MapFrom(typeof(CbuR2Entity))]
    public class CbuR2Dto
    {
        /// <summary>
        /// 城市（反向经扁平化写回链 Address.City，源 null 走清空分支）
        /// </summary>
        [MapProperty(typeof(CbuR2Entity), "Address.City")]
        [ConvertBack(typeof(CbuR2BackConverter), nameof(CbuR2BackConverter.Convert))]
        public string? City { get; set; }
    }

    /// <summary>
    /// 验收 3（B-1）实体 — 仅标 [ConvertBack] 的快捷路径回退场景
    /// </summary>
    public class CbuR3Entity
    {
        /// <summary>
        /// 标签（反向目标属性）
        /// </summary>
        public string Tag { get; set; } = "";
    }

    /// <summary>
    /// 验收 3（B-1）反向转换器（T1 模板）— 静态调用计数用于断言快捷路径回退
    /// </summary>
    public static class CbuR3BackConverter
    {
        /// <summary>
        /// 转换器被调用次数（静态计数，验证 MemberInit 快捷路径已回退到转换器消费）
        /// </summary>
        public static int CallCount;

        /// <summary>
        /// 将 DTO 标签值转换为实体标签值（调用计数 +1）
        /// </summary>
        /// <param name="fieldValue">DTO 标签值</param>
        /// <returns>带 "fast:" 前缀的标签值</returns>
        public static string Convert(string fieldValue)
        {
            CallCount++;
            return "fast:" + fieldValue;
        }
    }

    /// <summary>
    /// 验收 3（B-1）运行时列 DTO — 仅标 [ConvertBack]，同类型属性
    /// </summary>
    [MapFrom(typeof(CbuR3Entity))]
    public class CbuR3Dto
    {
        /// <summary>
        /// 标签（仅标 [ConvertBack]：若快捷路径未回退将静默同类型直赋，转换器不消费）
        /// </summary>
        [ConvertBack(typeof(CbuR3BackConverter), nameof(CbuR3BackConverter.Convert))]
        public string Tag { get; set; } = "";
    }

    /// <summary>
    /// 验收 5 运行时列实体 — 同属性双特性场景
    /// </summary>
    public class CbuR4Entity
    {
        /// <summary>
        /// 标签（正向源属性）
        /// </summary>
        public string Tag { get; set; } = "";
    }

    /// <summary>
    /// 验收 5 正向转换器（T1 模板 M(TField)）
    /// </summary>
    public static class CbuR4FwdConverter
    {
        /// <summary>
        /// 将实体标签值转换为 DTO 标签值（前缀标识正向转换器被消费）
        /// </summary>
        /// <param name="fieldValue">实体标签值</param>
        /// <returns>带 "fwd:" 前缀的标签值</returns>
        public static string Convert(string fieldValue) => "fwd:" + fieldValue;
    }

    /// <summary>
    /// 验收 5 反向转换器（T1 模板 M(TField)）
    /// </summary>
    public static class CbuR4BackConverter
    {
        /// <summary>
        /// 将 DTO 标签值转换为实体标签值（前缀标识反向转换器被消费）
        /// </summary>
        /// <param name="fieldValue">DTO 标签值</param>
        /// <returns>带 "back:" 前缀的标签值</returns>
        public static string Convert(string fieldValue) => "back:" + fieldValue;
    }

    /// <summary>
    /// 验收 5 运行时列 DTO — 同属性 [Convert] + [ConvertBack] 并列
    /// </summary>
    [MapFrom(typeof(CbuR4Entity))]
    public class CbuR4Dto
    {
        /// <summary>
        /// 标签（正向经 CbuR4FwdConverter、反向经 CbuR4BackConverter，各自生效互不干扰）
        /// </summary>
        [Convert(typeof(CbuR4FwdConverter), nameof(CbuR4FwdConverter.Convert))]
        [ConvertBack(typeof(CbuR4BackConverter), nameof(CbuR4BackConverter.Convert))]
        public string Tag { get; set; } = "";
    }

    /// <summary>
    /// [ConvertBack] 运行时定向测试 — 四路径反向消费值行为 + B-1 快捷路径回退
    /// <para>覆盖验收 1/2/3/5 运行时列。构造/Dispose 取消并恢复自动注册
    /// （模式），使 Mapper.Map 走运行时路径；生成侧真实代码直接调用做双引擎差分。</para>
    /// </summary>
    public class ConvertBackUsingRuntimeTests : IDisposable
    {
        /// <summary>
        /// 取消 CbuR1~R4 四组类型对的自动注册，使 Mapper.Map 走运行时路径
        /// </summary>
        public ConvertBackUsingRuntimeTests()
        {
            Mapper.RegisterGenerated<CbuR1Dto, CbuR1Entity>(null!);
            Mapper.RegisterGeneratedCore<CbuR1Dto, CbuR1Entity>(null!);
            Mapper.RegisterGenerated<CbuR2Dto, CbuR2Entity>(null!);
            Mapper.RegisterGeneratedCore<CbuR2Dto, CbuR2Entity>(null!);
            Mapper.RegisterGenerated<CbuR3Dto, CbuR3Entity>(null!);
            Mapper.RegisterGeneratedCore<CbuR3Dto, CbuR3Entity>(null!);
            Mapper.RegisterGenerated<CbuR4Dto, CbuR4Entity>(null!);
            Mapper.RegisterGeneratedCore<CbuR4Dto, CbuR4Entity>(null!);
            CbuR3BackConverter.CallCount = 0;
        }

        /// <summary>
        /// 恢复四组类型对的自动注册（防止污染其他测试）
        /// </summary>
        public void Dispose()
        {
            Mapper.RegisterGeneratedCore<CbuR1Dto, CbuR1Entity>(GeneratedMapper.Map_CbuR1Dto_To_CbuR1Entity);
            Mapper.RegisterGenerated<CbuR1Dto, CbuR1Entity>(GeneratedMapper.Map_CbuR1Dto_To_CbuR1Entity_WithAction);
            Mapper.RegisterGeneratedCore<CbuR2Dto, CbuR2Entity>(GeneratedMapper.Map_CbuR2Dto_To_CbuR2Entity);
            Mapper.RegisterGenerated<CbuR2Dto, CbuR2Entity>(GeneratedMapper.Map_CbuR2Dto_To_CbuR2Entity_WithAction);
            Mapper.RegisterGeneratedCore<CbuR3Dto, CbuR3Entity>(GeneratedMapper.Map_CbuR3Dto_To_CbuR3Entity);
            Mapper.RegisterGenerated<CbuR3Dto, CbuR3Entity>(GeneratedMapper.Map_CbuR3Dto_To_CbuR3Entity_WithAction);
            Mapper.RegisterGeneratedCore<CbuR4Dto, CbuR4Entity>(GeneratedMapper.Map_CbuR4Dto_To_CbuR4Entity);
            Mapper.RegisterGenerated<CbuR4Dto, CbuR4Entity>(GeneratedMapper.Map_CbuR4Dto_To_CbuR4Entity_WithAction);
        }

        /// <summary>
        /// 验收 1（运行时列）：直接属性反向消费 — 运行时与生成侧值一致
        /// </summary>
        [Fact]
        public void DirectProperty_ReverseConsumes_RuntimeAndGeneratedConsistent()
        {
            var dto = new CbuR1Dto { Tag = "hello" };

            var runtime = Mapper.Map<CbuR1Dto, CbuR1Entity>(dto)!;
            Assert.Equal("back:hello", runtime.Tag);

            var generated = GeneratedMapper.Map_CbuR1Dto_To_CbuR1Entity(dto)!;
            Assert.Equal(runtime.Tag, generated.Tag);
        }

        /// <summary>
        /// 验收 2（运行时列）：写回路径反向消费 + N-1 — 源非 null 消费转换器，源 null 清空且不调用
        /// </summary>
        [Fact]
        public void WriteBackPath_ReverseConsumes_And_NullSourceClearsWithoutConvert()
        {
            // 源非 null：写回链消费转换器
            var dto = new CbuR2Dto { City = "SH" };
            var runtime = Mapper.Map<CbuR2Dto, CbuR2Entity>(dto)!;
            Assert.Equal("city:SH", runtime.Address.City);

            var generated = GeneratedMapper.Map_CbuR2Dto_To_CbuR2Entity(dto)!;
            Assert.Equal(runtime.Address.City, generated.Address.City);

            // 源 null：清空分支（实体侧 City=null），不调转换器（无前缀产物）
            var dtoNull = new CbuR2Dto { City = null };
            var cleared = Mapper.Map<CbuR2Dto, CbuR2Entity>(dtoNull)!;
            Assert.Null(cleared.Address.City);
        }

        /// <summary>
        /// 验收 3（B-1）：仅标 [ConvertBack] 同类型属性 × 运行时反向 — 快捷路径回退消费
        /// <para>若 MemberInit 快捷路径未回退（B-1 未落实），string→string 同类型将静默直赋，
        /// 转换器不被调用、Tag 无 "fast:" 前缀 —— 本用例以 CallCount 与值双重拦截。</para>
        /// </summary>
        [Fact]
        public void SingleConvertBackUsing_RuntimeReverse_FallsBackToConverter()
        {
            var dto = new CbuR3Dto { Tag = "hello" };

            var entity = Mapper.Map<CbuR3Dto, CbuR3Entity>(dto)!;

            Assert.Equal(1, CbuR3BackConverter.CallCount);
            Assert.Equal("fast:hello", entity.Tag);
        }

        /// <summary>
        /// 验收 5（运行时列）：同属性双特性 — 正向消费正向转换器、反向消费反向转换器，互不干扰
        /// </summary>
        [Fact]
        public void DualAttribute_RuntimeForwardAndReverse_UseRespectiveConverters()
        {
            var entity = new CbuR4Entity { Tag = "raw" };
            var runtimeFwd = Mapper.Map<CbuR4Entity, CbuR4Dto>(entity)!;
            Assert.Equal("fwd:raw", runtimeFwd.Tag);
            var genFwd = GeneratedMapper.Map_CbuR4Entity_To_CbuR4Dto(entity)!;
            Assert.Equal(runtimeFwd.Tag, genFwd.Tag);

            var dto = new CbuR4Dto { Tag = "raw" };
            var runtimeBack = Mapper.Map<CbuR4Dto, CbuR4Entity>(dto)!;
            Assert.Equal("back:raw", runtimeBack.Tag);
            var genBack = GeneratedMapper.Map_CbuR4Dto_To_CbuR4Entity(dto)!;
            Assert.Equal(runtimeBack.Tag, genBack.Tag);
        }
    }
}

// ================================================================
// 红测先行 — 反向无源消费（实体侧标注 + DTO 无同名源属性）
// ================================================================

/// <summary>
/// 形态①实体 — 一对多拆分场景（FirstName/LastName 均标 [ConvertBack]，DTO 仅 FullName）
/// </summary>
public class CbuNs1Entity
{
    /// <summary>
    /// 标识（匹配对锚点属性：保证正向/反向方法当前即生成，生成侧断言可编译）
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 名（反向目标属性，无同名 DTO 源属性——经拆分转换器写回）
    /// </summary>
    public string FirstName { get; set; } = "";

    /// <summary>
    /// 姓（反向目标属性，无同名 DTO 源属性）
    /// </summary>
    public string LastName { get; set; } = "";
}

/// <summary>
/// 形态①反向转换器（T2 模板 M(TSource)）— 接收整个 DTO 做一对多拆分
/// </summary>
public static class CbuNs1Splitter
{
    /// <summary>
    /// 拆名（TSource 实参 = 整个 DTO 对象）
    /// </summary>
    /// <param name="dto">反向映射源 DTO</param>
    /// <returns>带 "First:" 前缀的名</returns>
    public static string ToFirstName(CbuNs1Dto dto) => "First:" + dto.FullName;

    /// <summary>
    /// 拆姓（TSource 实参 = 整个 DTO 对象）
    /// </summary>
    /// <param name="dto">反向映射源 DTO</param>
    /// <returns>带 "Last:" 前缀的姓</returns>
    public static string ToLastName(CbuNs1Dto dto) => "Last:" + dto.FullName;
}

/// <summary>
/// 形态①DTO — 仅 FullName（无 FirstName/LastName 同名源属性）
/// </summary>
[MapFrom(typeof(CbuNs1Entity))]
public class CbuNs1Dto
{
    /// <summary>
    /// 标识（匹配对锚点）
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 全名（反向供拆分转换器消费）
    /// </summary>
    public string FullName { get; set; } = "";
}

/// <summary>
/// 形态②实体 — 四模板（P0/P1/P2/P3 分别标 T0/T1/T2/T3 形态转换器）
/// </summary>
public class CbuNs2Entity
{
    /// <summary>
    /// 标识（匹配对锚点属性）
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// T0 模板目标（M() 无参常量）
    /// </summary>
    public string P0 { get; set; } = "";

    /// <summary>
    /// T1 模板目标（M(default) 单参默认值）
    /// </summary>
    public string P1 { get; set; } = "";

    /// <summary>
    /// T2 模板目标（M(dto) 收整个 DTO）
    /// </summary>
    public string P2 { get; set; } = "";

    /// <summary>
    /// T3 模板目标（M(dto, default) 组合默认值）
    /// </summary>
    public string P3 { get; set; } = "";
}

/// <summary>
/// 形态②反向转换器 — 四模板齐备（双参构造锚定为默认形态）
/// </summary>
public static class CbuNs2Conv
{
    /// <summary>
    /// T0：无参常量
    /// </summary>
    public static string M0() => "T0";

    /// <summary>
    /// T1：单参（无源 → default(int)=0）
    /// </summary>
    /// <param name="v">字段值（无源场景为默认值）</param>
    public static string M1(int v) => "T1:" + v;

    /// <summary>
    /// T2：收整个 DTO
    /// </summary>
    /// <param name="dto">反向映射源 DTO</param>
    public static string M2(CbuNs2Dto dto) => "T2:" + dto.FullName;

    /// <summary>
    /// T3：DTO + 默认值组合
    /// </summary>
    /// <param name="dto">反向映射源 DTO</param>
    /// <param name="v">字段值（无源场景为默认值）</param>
    public static string M3(CbuNs2Dto dto, int v) => "T3:" + dto.FullName + ":" + v;
}

/// <summary>
/// 形态②DTO — 仅 Id/FullName（无 P0~P3 同名源属性）
/// </summary>
[MapFrom(typeof(CbuNs2Entity))]
public class CbuNs2Dto
{
    /// <summary>
    /// 标识（匹配对锚点）
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 全名（反向供 T2/T3 模板消费）
    /// </summary>
    public string FullName { get; set; } = "";
}

/// <summary>
/// 形态②锚定实体 — 单参构造形态（锚定=实体类型自身，方法声明于实体类）
/// </summary>
public class CbuNs2AEntity
{
    /// <summary>
    /// 标识（匹配对锚点属性）
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 备注（单参构造标注：方法解析域收紧为本类型自身）
    /// </summary>
    public string Note { get; set; } = "";

    /// <summary>
    /// 单参锚定方法（声明于实体类型自身，T2 模板收整个 DTO）
    /// </summary>
    /// <param name="dto">反向映射源 DTO</param>
    public static string SelfBack(CbuNs2ADto dto) => "self:" + dto.FullName;
}

/// <summary>
/// 形态②锚定DTO — 仅 Id/FullName
/// </summary>
[MapFrom(typeof(CbuNs2AEntity))]
public class CbuNs2ADto
{
    /// <summary>
    /// 标识（匹配对锚点）
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 全名（反向供单参锚定转换器消费）
    /// </summary>
    public string FullName { get; set; } = "";
}

/// <summary>
/// 形态③实体 — 匹配对（Tag 与 DTO 同名），仅实体侧标 [ConvertBack]
/// </summary>
public class CbuNs3Entity
{
    /// <summary>
    /// 标识（匹配对锚点属性）
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 标签（仅实体侧标注：运行时经 PlanCore 对侧回退消费、生成端当前不消费——-2 既有分叉）
    /// </summary>
    public string Tag { get; set; } = "";
}

/// <summary>
/// 形态③反向转换器（T1 模板 M(TField)）
/// </summary>
public static class CbuNs3Conv
{
    /// <summary>
    /// 转换（T1 模板，匹配对场景消费 DTO Tag 字段值）
    /// </summary>
    /// <param name="fieldValue">DTO 标签值</param>
    public static string Convert(string fieldValue) => "back:" + fieldValue;
}

/// <summary>
/// 形态③DTO — Tag 同名无标注（实体侧标注的消费验证）
/// </summary>
[MapFrom(typeof(CbuNs3Entity))]
public class CbuNs3Dto
{
    /// <summary>
    /// 标识（匹配对锚点）
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 标签（同名属性，无任何标注）
    /// </summary>
    public string Tag { get; set; } = "";
}

/// <summary>
/// 双侧同标实体 — 匹配对，实体与 DTO 侧各标不同 [ConvertBack]（DTO 优先序锁定）
/// </summary>
public class CbuNs3BEntity
{
    /// <summary>
    /// 标识（匹配对锚点属性）
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 标签（实体侧标注——双侧同标时不被消费，DTO 侧优先）
    /// </summary>
    public string Tag { get; set; } = "";
}

/// <summary>
/// 双侧同标实体侧转换器（T1 模板 M(TField)）
/// </summary>
public static class CbuNs3BEntityConv
{
    /// <summary>
    /// 转换（T1 模板——双侧同标时不被消费，消费断言锁定 DTO 优先序）
    /// </summary>
    /// <param name="fieldValue">DTO 标签值</param>
    public static string Convert(string fieldValue) => "entity:" + fieldValue;
}

/// <summary>
/// 双侧同标 DTO 侧转换器（T1 模板 M(TField)，优先消费）
/// </summary>
public static class CbuNs3BDtoConv
{
    /// <summary>
    /// 转换（T1 模板——双侧同标时优先消费，与运行时 sourceItem ?? targetItem 查询序一致）
    /// </summary>
    /// <param name="fieldValue">DTO 标签值</param>
    public static string Convert(string fieldValue) => "dto:" + fieldValue;
}

/// <summary>
/// 双侧同标DTO — Tag 同名标注（双侧同标 DTO 优先序锁定）
/// </summary>
[MapFrom(typeof(CbuNs3BEntity))]
public class CbuNs3BDto
{
    /// <summary>
    /// 标识（匹配对锚点）
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 标签（DTO 侧标注——规范位置优先消费）
    /// </summary>
    [ConvertBack(typeof(CbuNs3BDtoConv), nameof(CbuNs3BDtoConv.Convert))]
    public string Tag { get; set; } = "";
}

/// <summary>
/// 红测先行 — 反向无源消费目标语义（现状必红）
/// <para>用例①一对多拆分 / ②四模板 × 单参/双参锚定 / ③匹配对实体侧标注（现状红法 =
/// 运行时过 / 生成侧不过，即 -2 既有分叉的锁定证据）。构造/Dispose 取消并恢复自动注册
/// （模式），使 Mapper.Map 走运行时路径；生成侧真实代码直接调用做双引擎差分。</para>
/// <para>实现落位（运行时分支 / 生成端发射 / 匹配对对齐）后本类全绿。</para>
/// </summary>
public class ConvertBackNullSourceRuntimeTests : IDisposable
{
    /// <summary>
    /// 取消 CbuNs1/Ns2/Ns2A/Ns3/Ns3B 五组类型对的自动注册，使 Mapper.Map 走运行时路径
    /// </summary>
    public ConvertBackNullSourceRuntimeTests()
    {
        Mapper.RegisterGenerated<CbuNs1Dto, CbuNs1Entity>(null!);
        Mapper.RegisterGeneratedCore<CbuNs1Dto, CbuNs1Entity>(null!);
        Mapper.RegisterGenerated<CbuNs2Dto, CbuNs2Entity>(null!);
        Mapper.RegisterGeneratedCore<CbuNs2Dto, CbuNs2Entity>(null!);
        Mapper.RegisterGenerated<CbuNs2ADto, CbuNs2AEntity>(null!);
        Mapper.RegisterGeneratedCore<CbuNs2ADto, CbuNs2AEntity>(null!);
        Mapper.RegisterGenerated<CbuNs3Dto, CbuNs3Entity>(null!);
        Mapper.RegisterGeneratedCore<CbuNs3Dto, CbuNs3Entity>(null!);
        Mapper.RegisterGenerated<CbuNs3BDto, CbuNs3BEntity>(null!);
        Mapper.RegisterGeneratedCore<CbuNs3BDto, CbuNs3BEntity>(null!);
    }

    /// <summary>
    /// 恢复五组类型对的自动注册（防止污染其他测试）
    /// </summary>
    public void Dispose()
    {
        Mapper.RegisterGeneratedCore<CbuNs1Dto, CbuNs1Entity>(GeneratedMapper.Map_CbuNs1Dto_To_CbuNs1Entity);
        Mapper.RegisterGenerated<CbuNs1Dto, CbuNs1Entity>(GeneratedMapper.Map_CbuNs1Dto_To_CbuNs1Entity_WithAction);
        Mapper.RegisterGeneratedCore<CbuNs2Dto, CbuNs2Entity>(GeneratedMapper.Map_CbuNs2Dto_To_CbuNs2Entity);
        Mapper.RegisterGenerated<CbuNs2Dto, CbuNs2Entity>(GeneratedMapper.Map_CbuNs2Dto_To_CbuNs2Entity_WithAction);
        Mapper.RegisterGeneratedCore<CbuNs2ADto, CbuNs2AEntity>(GeneratedMapper.Map_CbuNs2ADto_To_CbuNs2AEntity);
        Mapper.RegisterGenerated<CbuNs2ADto, CbuNs2AEntity>(GeneratedMapper.Map_CbuNs2ADto_To_CbuNs2AEntity_WithAction);
        Mapper.RegisterGeneratedCore<CbuNs3Dto, CbuNs3Entity>(GeneratedMapper.Map_CbuNs3Dto_To_CbuNs3Entity);
        Mapper.RegisterGenerated<CbuNs3Dto, CbuNs3Entity>(GeneratedMapper.Map_CbuNs3Dto_To_CbuNs3Entity_WithAction);
        Mapper.RegisterGeneratedCore<CbuNs3BDto, CbuNs3BEntity>(GeneratedMapper.Map_CbuNs3BDto_To_CbuNs3BEntity);
        Mapper.RegisterGenerated<CbuNs3BDto, CbuNs3BEntity>(GeneratedMapper.Map_CbuNs3BDto_To_CbuNs3BEntity_WithAction);
    }

    // 本用例随 S 侧锚定形态退役删除（④——
    // 实体侧标注消费出清，A-14 族已知退出；承载侧合法形态由其余用例锁定）

    // 本用例随 S 侧锚定形态退役删除（④——
    // 实体侧标注消费出清，A-14 族已知退出；承载侧合法形态由其余用例锁定）

    // 本用例随 S 侧锚定形态退役删除（④——
    // 实体侧标注消费出清，A-14 族已知退出；承载侧合法形态由其余用例锁定）

    // 本用例随 S 侧锚定形态退役删除（④——
    // 实体侧标注消费出清，A-14 族已知退出；承载侧合法形态由其余用例锁定）

    /// <summary>
    /// 验收 #2（运行时列）：双侧同标 DTO 侧优先 — 实体/DTO 各标不同转换器，
    /// 运行时（sourceItem ?? targetItem 查询序）与生成侧均消费 DTO 侧转换器
    /// （锁定优先序——防实体侧兜底翻转查询序；前后均应绿）
    /// </summary>
    [Fact]
    public void MatchedPair_BothSidesAnnotated_DtoSidePriority_BothEngines()
    {
        var dto = new CbuNs3BDto { Id = 4, Tag = "hello" };

        var runtime = Mapper.Map<CbuNs3BDto, CbuNs3BEntity>(dto)!;
        Assert.Equal("dto:hello", runtime.Tag);

        var generated = GeneratedMapper.Map_CbuNs3BDto_To_CbuNs3BEntity(dto)!;
        Assert.Equal("dto:hello", generated.Tag);
        Assert.Equal(runtime.Tag, generated.Tag);
    }
}

// ================================================================
// 验收 #5 — 写回链覆盖排除（评审 B-1 运行时侧）
// ================================================================

/// <summary>
/// 验收 #5 实体 — 中间段标 [ConvertBack] + DTO 扁平属性（推导写回链场景）
/// </summary>
public class CbuNs4Entity
{
    /// <summary>
    /// 标识（匹配对锚点属性）
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 地址（反向写回链中间段：DTO.AddressCity 推导折叠 → dst.Address.City；
    /// 同时标 ——按覆盖排除应静默让位写回链，不被新分支消费）
    /// </summary>
    public CbuNs4Address Address { get; set; } = new();
}

/// <summary>
/// 验收 #5 嵌套地址类型（写回链叶段容器）
/// </summary>
public class CbuNs4Address
{
    /// <summary>
    /// 城市（写回链叶段目标）
    /// </summary>
    public string City { get; set; } = "";
}

/// <summary>
/// 验收 #5 反向转换器 — 若被新分支误消费则整对象覆盖写回产物（毒丸标记 + 调用计数）
/// </summary>
public static class CbuNs4Conv
{
    /// <summary>
    /// 调用计数（0 = 覆盖排除生效，转换器不被消费；测试类构造中归零）
    /// </summary>
    public static int CallCount;

    /// <summary>
    /// 整对象毒丸（T2 模板收整个 DTO；返回 City="POISON" 的新实例）
    /// </summary>
    /// <param name="dto">反向映射源 DTO</param>
    /// <returns>毒丸地址对象</returns>
    public static CbuNs4Address MakeAddress(CbuNs4Dto dto)
    {
        CallCount++;
        return new CbuNs4Address { City = "POISON" };
    }
}

/// <summary>
/// 验收 #5 DTO — AddressCity 扁平属性（推导写回链来源）
/// </summary>
[MapFrom(typeof(CbuNs4Entity))]
public class CbuNs4Dto
{
    /// <summary>
    /// 标识（匹配对锚点）
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 地址城市（推导扁平化 → 实体 Address.City 写回链）
    /// </summary>
    public string AddressCity { get; set; } = "";
}

/// <summary>
/// 验收 #5 — 写回链覆盖排除（评审 B-1 运行时侧）：实体中间段标 [ConvertBack] 时
/// 新分支静默让位写回链（写回链优先），断言转换器不被消费（CallCount==0）、写回产物不被
/// 整对象覆盖（City=源值而非 "POISON"）。运行时/生成端双引擎同断言（生成端
/// 落地前即绿——写回链为既有能力、覆盖排除为对称语义锁定）。
/// <para>变异锚点（F6）：移除覆盖排除后本测试必红——发射序在写回链之后时转换器整对象覆盖
/// 写回产物（City="POISON"、CallCount&gt;0）。</para>
/// </summary>
public class ConvertBackWriteBackCoverageTests : IDisposable
{
    /// <summary>
    /// 取消 CbuNs4 类型对自动注册（走运行时路径）+ 转换器调用计数归零
    /// </summary>
    public ConvertBackWriteBackCoverageTests()
    {
        Mapper.RegisterGenerated<CbuNs4Dto, CbuNs4Entity>(null!);
        Mapper.RegisterGeneratedCore<CbuNs4Dto, CbuNs4Entity>(null!);
        CbuNs4Conv.CallCount = 0;
    }

    /// <summary>
    /// 恢复 CbuNs4 类型对自动注册（防止污染其他测试）
    /// </summary>
    public void Dispose()
    {
        Mapper.RegisterGeneratedCore<CbuNs4Dto, CbuNs4Entity>(GeneratedMapper.Map_CbuNs4Dto_To_CbuNs4Entity);
        Mapper.RegisterGenerated<CbuNs4Dto, CbuNs4Entity>(GeneratedMapper.Map_CbuNs4Dto_To_CbuNs4Entity_WithAction);
    }

    /// <summary>
    /// 覆盖排除：实体中间段 [ConvertBack] 让位写回链 — City 为写回产物、转换器零调用
    /// </summary>
    [Fact]
    public void CoveredIntermediateSegment_WriteBackWins_ConverterNotConsumed()
    {
        var dto = new CbuNs4Dto { Id = 9, AddressCity = "Beijing" };

        var runtime = Mapper.Map<CbuNs4Dto, CbuNs4Entity>(dto)!;
        Assert.Equal(9, runtime.Id);
        Assert.Equal("Beijing", runtime.Address.City);
        Assert.Equal(0, CbuNs4Conv.CallCount);

        var generated = GeneratedMapper.Map_CbuNs4Dto_To_CbuNs4Entity(dto)!;
        Assert.Equal("Beijing", generated.Address.City);
        Assert.Equal(0, CbuNs4Conv.CallCount);
    }
}

/// <summary>
/// setter 门实体 — get-only 形态（Tag 无 setter）
/// </summary>
public class CbuR5Entity
{
    /// <summary>
    /// 标识（匹配对锚点属性）
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 标签（get-only：运行时 CanWrite=false 门拦截、生成端扫描门跳过 + NCMAP013——双引擎一致不赋值）
    /// </summary>
    public string Tag { get; } = "";
}

/// <summary>
/// setter 门转换器（T1 模板 M(TField)）— get-only 场景不应被消费
/// </summary>
public static class CbuR5Conv
{
    /// <summary>
    /// 转换（T1 模板——get-only 门生效时不被调用）
    /// </summary>
    /// <param name="fieldValue">字段值</param>
    public static string Make(string fieldValue) => "r5:" + fieldValue;
}

/// <summary>
/// setter 门DTO — 仅 Id（Tag 无同名源属性）
/// </summary>
[MapFrom(typeof(CbuR5Entity))]
public class CbuR5Dto
{
    /// <summary>
    /// 标识（匹配对锚点）
    /// </summary>
    public int Id { get; set; }
}

/// <summary>
/// setter 门实体 — init-only 形态（Tag 仅 init setter + 参数化构造器）
/// </summary>
public class CbuR6Entity
{
    /// <summary>
    /// 标识（匹配对锚点属性）
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 标签（init-only：v1.1/评审 B-2——运行时 CanWrite=true 经新分支消费（表达式树对 init-only 赋值合法）、
    /// 生成端经构造器参数匹配（H-04）作为构造实参消费——双引擎一致）
    /// </summary>
    public string Tag { get; init; } = "";

    /// <summary>
    /// 参数化构造器（同名参数触发生成端 H-04 构造器参数匹配路径）
    /// </summary>
    /// <param name="id">标识</param>
    /// <param name="tag">标签</param>
    public CbuR6Entity(int id, string tag)
    {
        Id = id;
        Tag = tag;
    }
}

/// <summary>
/// setter 门转换器（T1 模板 M(TField)）— init-only 消费验证
/// </summary>
public static class CbuR6Conv
{
    /// <summary>
    /// 转换（T1 模板，无源形态收 default(TField)=null → "init:"）
    /// </summary>
    /// <param name="fieldValue">字段值（无源场景为 null）</param>
    public static string Make(string fieldValue) => "init:" + fieldValue;
}

/// <summary>
/// setter 门DTO — 仅 Id（Tag 无同名源属性）
/// </summary>
[MapFrom(typeof(CbuR6Entity))]
public class CbuR6Dto
{
    /// <summary>
    /// 标识（匹配对锚点）
    /// </summary>
    public int Id { get; set; }
}

/// <summary>
/// [MapIgnore] 联动实体 — Tag 双标注（[MapIgnore(Both)] + [ConvertBack]）
/// </summary>
public class CbuR7Entity
{
    /// <summary>
    /// 标识（匹配对锚点属性）
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 标签（运行时目标属性枚举过滤排除、生成端扫描门 ShouldIgnoreInDirection 排除——
    /// 双引擎一致静默跳过，转换器不消费）
    /// </summary>
    public string Tag { get; set; } = "";
}

/// <summary>
/// [MapIgnore] 联动转换器（T1 模板 M(TField)）— 被忽略时不应被消费
/// </summary>
public static class CbuR7Conv
{
    /// <summary>
    /// 转换（T1 模板——[MapIgnore] 生效时不被调用）
    /// </summary>
    /// <param name="fieldValue">字段值</param>
    public static string Make(string fieldValue) => "r7:" + fieldValue;
}

/// <summary>
/// [MapIgnore] 联动DTO — 仅 Id（Tag 无同名源属性）
/// </summary>
[MapFrom(typeof(CbuR7Entity))]
public class CbuR7Dto
{
    /// <summary>
    /// 标识（匹配对锚点）
    /// </summary>
    public int Id { get; set; }
}

/// <summary>
/// setter 门运行时列 — get-only 跳过 / init-only 消费 / [MapIgnore] 抑制，
/// 三形态运行时与生成侧双引擎一致断言（构造/Dispose 取消并恢复自动注册，模式）。
/// </summary>
public class ConvertBackSetterGateRuntimeTests : IDisposable
{
    /// <summary>
    /// 取消 CbuR5/R6/R7 三组类型对的自动注册，使 Mapper.Map 走运行时路径
    /// </summary>
    public ConvertBackSetterGateRuntimeTests()
    {
        Mapper.RegisterGenerated<CbuR5Dto, CbuR5Entity>(null!);
        Mapper.RegisterGeneratedCore<CbuR5Dto, CbuR5Entity>(null!);
        Mapper.RegisterGenerated<CbuR6Dto, CbuR6Entity>(null!);
        Mapper.RegisterGeneratedCore<CbuR6Dto, CbuR6Entity>(null!);
        Mapper.RegisterGenerated<CbuR7Dto, CbuR7Entity>(null!);
        Mapper.RegisterGeneratedCore<CbuR7Dto, CbuR7Entity>(null!);
    }

    /// <summary>
    /// 恢复三组类型对的自动注册（防止污染其他测试）
    /// </summary>
    public void Dispose()
    {
        Mapper.RegisterGeneratedCore<CbuR5Dto, CbuR5Entity>(GeneratedMapper.Map_CbuR5Dto_To_CbuR5Entity);
        Mapper.RegisterGenerated<CbuR5Dto, CbuR5Entity>(GeneratedMapper.Map_CbuR5Dto_To_CbuR5Entity_WithAction);
        Mapper.RegisterGeneratedCore<CbuR6Dto, CbuR6Entity>(GeneratedMapper.Map_CbuR6Dto_To_CbuR6Entity);
        Mapper.RegisterGenerated<CbuR6Dto, CbuR6Entity>(GeneratedMapper.Map_CbuR6Dto_To_CbuR6Entity_WithAction);
        Mapper.RegisterGeneratedCore<CbuR7Dto, CbuR7Entity>(GeneratedMapper.Map_CbuR7Dto_To_CbuR7Entity);
        Mapper.RegisterGenerated<CbuR7Dto, CbuR7Entity>(GeneratedMapper.Map_CbuR7Dto_To_CbuR7Entity_WithAction);
    }

    /// <summary>
    /// setter 门（get-only）：双引擎一致跳过 — Tag 保持默认值""、无异常
    /// （运行时 CanWrite=false 门拦截；生成端扫描门跳过 + NCMAP013——生成列见
    /// ConvertBackUsingGenerationTests.SetterGate_GetOnly_SkipsWithNCMAP013）
    /// </summary>
    [Fact]
    public void SetterGate_GetOnly_BothEngines_SkipWithoutCrash()
    {
        var dto = new CbuR5Dto { Id = 5 };

        var runtime = Mapper.Map<CbuR5Dto, CbuR5Entity>(dto)!;
        Assert.Equal(5, runtime.Id);
        Assert.Equal("", runtime.Tag);

        var generated = GeneratedMapper.Map_CbuR5Dto_To_CbuR5Entity(dto)!;
        Assert.Equal("", generated.Tag);
        Assert.Equal(runtime.Tag, generated.Tag);
    }

    // 本用例随 S 侧锚定形态退役删除（④——
    // 实体侧标注消费出清，A-14 族已知退出；承载侧合法形态由其余用例锁定）

    /// <summary>
    /// [MapIgnore(Both)] 联动：双引擎一致静默跳过 — Tag 保持默认值""、转换器不消费
    /// （运行时目标属性枚举过滤排除；生成端扫描门 ShouldIgnoreInDirection 排除）
    /// </summary>
    [Fact]
    public void MapIgnore_OnEntity_BothEngines_Skip()
    {
        var dto = new CbuR7Dto { Id = 7 };

        var runtime = Mapper.Map<CbuR7Dto, CbuR7Entity>(dto)!;
        Assert.Equal(7, runtime.Id);
        Assert.Equal("", runtime.Tag);

        var generated = GeneratedMapper.Map_CbuR7Dto_To_CbuR7Entity(dto)!;
        Assert.Equal("", generated.Tag);
        Assert.Equal(runtime.Tag, generated.Tag);
    }
}

// ================================================================
// 红测先行 — 写回链实体叶标注（X2 分叉本体）
// ================================================================

/// <summary>
/// 写回链实体叶标注实体 — Address 导航链，叶属性 City 标 [ConvertBack]（非规范位置）
/// </summary>
public class CbuWLEntity
{
    /// <summary>
    /// 地址（写回链中间节点）
    /// </summary>
    public CbuWLAddress Address { get; set; } = new();
}

/// <summary>
/// 写回链叶类型 — City 标 [ConvertBack]（实体叶属性非规范位置，运行时叶兜底消费）
/// </summary>
public class CbuWLAddress
{
    /// <summary>
    /// 城市（反向写回叶——标注 ，DTO 侧不标）
    /// </summary>
    public string City { get; set; } = "";
}

/// <summary>
/// 写回链叶标注转换器（T1 模板 M(TField)）— 输出 "wleaf:" 前缀（与类型转换直写可区分）
/// </summary>
public static class CbuWLConv
{
    /// <summary>
    /// 将 DTO 城市值转换为实体城市值（前缀标识转换器被消费）
    /// </summary>
    /// <param name="fieldValue">DTO 城市值</param>
    /// <returns>带 "wleaf:" 前缀的城市值</returns>
    public static string Convert(string fieldValue) => "wleaf:" + fieldValue;
}

/// <summary>
/// 写回链叶标注 DTO — City 仅标 [MapProperty] 写回链（不标 [ConvertBack]）
/// </summary>
[MapFrom(typeof(CbuWLEntity))]
public class CbuWLDto
{
    /// <summary>
    /// 城市（反向经扁平化写回链 Address.City，叶侧转换器兜底消费）
    /// </summary>
    [MapProperty(typeof(CbuWLEntity), "Address.City")]
    public string? City { get; set; }
}

/// <summary>
/// 矩阵实体 — 叶标注四模板（P0=T0 常量 / P2=T2 整 DTO / P3=T3 DTO+字段；T1 见 CbuWL）
/// </summary>
public class CbuWLMEntity
{
    /// <summary>
    /// 地址（写回链中间节点）
    /// </summary>
    public CbuWLMAddress Address { get; set; } = new();
}

/// <summary>
/// 矩阵叶类型 — P0/P2/P3 各标不同模板的 [ConvertBack]
/// </summary>
public class CbuWLMAddress
{
    /// <summary>
    /// T0 常量模板叶
    /// </summary>
    public string P0 { get; set; } = "";

    /// <summary>
    /// T2 整 DTO 模板叶
    /// </summary>
    public string P2 { get; set; } = "";

    /// <summary>
    /// T3 DTO+字段模板叶
    /// </summary>
    public string P3 { get; set; } = "";
}

/// <summary>
/// 矩阵转换器 — T0/T2/T3 三模板（T1 见 CbuWLConv）
/// </summary>
public static class CbuWLMConv
{
    /// <summary>
    /// T0：无参常量
    /// </summary>
    public static string M0() => "T0";

    /// <summary>
    /// T2：收整个 DTO
    /// </summary>
    /// <param name="dto">反向映射源 DTO</param>
    public static string M2(CbuWLMDto dto) => "T2:" + dto.P2;

    /// <summary>
    /// T3：DTO + 字段值组合
    /// </summary>
    /// <param name="dto">反向映射源 DTO</param>
    /// <param name="v">字段值</param>
    public static string M3(CbuWLMDto dto, string v) => "T3:" + v;
}

/// <summary>
/// 矩阵 DTO — P0/P2/P3 扁平属性各标 [MapProperty] 写回链
/// </summary>
[MapFrom(typeof(CbuWLMEntity))]
public class CbuWLMDto
{
    /// <summary>
    /// P0（反向经写回链 Address.P0，T0 模板）
    /// </summary>
    [MapProperty(typeof(CbuWLMEntity), "Address.P0")]
    public string P0 { get; set; } = "";

    /// <summary>
    /// P2（反向经写回链 Address.P2，T2 模板）
    /// </summary>
    [MapProperty(typeof(CbuWLMEntity), "Address.P2")]
    public string P2 { get; set; } = "";

    /// <summary>
    /// P3（反向经写回链 Address.P3，T3 模板）
    /// </summary>
    [MapProperty(typeof(CbuWLMEntity), "Address.P3")]
    public string P3 { get; set; } = "";
}

/// <summary>
/// 矩阵实体 — 叶标注单参锚定（方法声明于叶类型自身）
/// </summary>
public class CbuWLSEntity
{
    /// <summary>
    /// 地址（写回链中间节点）
    /// </summary>
    public CbuWLSAddress Address { get; set; } = new();
}

/// <summary>
/// 矩阵叶类型 — City 标单参 [ConvertBack]（锚定=叶属性所属类型自身）
/// </summary>
public class CbuWLSAddress
{
    /// <summary>
    /// 城市（单参锚定：方法解析域收紧为本类型自身，镜像运行时 targetItem.DeclaringType）
    /// </summary>
    public string City { get; set; } = "";

    /// <summary>
    /// 单参锚定方法（声明于叶类型自身，T1 模板）
    /// </summary>
    /// <param name="fieldValue">字段值</param>
    public static string MakeCity(string fieldValue) => "self:" + fieldValue;
}

/// <summary>
/// 矩阵 DTO — City 仅标 [MapProperty] 写回链（单参锚定场景）
/// </summary>
[MapFrom(typeof(CbuWLSEntity))]
public class CbuWLSDto
{
    /// <summary>
    /// 城市（反向经写回链 Address.City，叶侧单参锚定转换器）
    /// </summary>
    [MapProperty(typeof(CbuWLSEntity), "Address.City")]
    public string City { get; set; } = "";
}

// ================================================================
// 说明 — 警告态（叶标注签名不匹配）运行时列不可落测：
// (1) NCMAP037 为 Error 级且 pragma 不抑制 Error 级生成器诊断（NCMAP012 Warning 级 pragma
//     先例仅在 Warning 级成立）——带 [MapFrom] 的警告态模型在本项目不可编译；
// (2) 无 [MapFrom] 形态下运行时写回守卫（ExpressionBuilder.BuildReversePathWriteBacks
//     开头：sourceType 须标 [MapFrom] 且 FromSource == targetType）不触发写回
//     ——Assert.Throws<MapException> 实证未抛（写回未生效而非未抛）；
// (3) 运行时真实行为为 MapException 抛出（代码实读：ResolveStaticMethod 失败即抛，
//     ExpressionBuilder 仅两处 catch 且均 when (ex is not MapException)；任务书⑥
//     「跳过整条写回」表述与实行为不符——1008 null 检查为防御性死代码）。
// 该行为仅在「仅引用核心库、不引用 SourceGen 的运行时纯消费项目」可达，本仓库测试布局
// 不可覆盖——已登记。生成端警告态行为由生成列 in-memory 用例锁定：
// ConvertBackUsingGenerationTests.WriteBackLeaf_EntitySideAnnotation_NotValidated_GeneratesDirectWrite。
// ================================================================

/// <summary>
/// 定向测试（运行时列守住性 + 一致性列）— 写回链实体叶标注
/// <para>运行时引擎对实体叶属性 [ConvertBack] 兜底消费已落地（ExpressionBuilder
/// BuildReversePathWriteBackFromPath 的 sourceProp ?? leaf 查询序）——运行时列现状即绿（守住性）；
/// 生成端不消费（物化门多段路径传 null + 写回分支仅查 DTO 侧）——一致性列现状必红
/// （X2 分叉本体证据，物化门扩展 + ReversePlanner 兜底分支后转绿）。
/// 生成列见 ConvertBackUsingGenerationTests.WriteBackLeaf_EntitySideAnnotation_NotConsumed。</para>
/// </summary>
public class ConvertBackWriteLeafRuntimeTests : IDisposable
{
    /// <summary>
    /// 取消 CbuWL 类型对的自动注册，使 Mapper.Map 走运行时路径
    /// </summary>
    public ConvertBackWriteLeafRuntimeTests()
    {
        Mapper.RegisterGenerated<CbuWLDto, CbuWLEntity>(null!);
        Mapper.RegisterGeneratedCore<CbuWLDto, CbuWLEntity>(null!);
        Mapper.RegisterGenerated<CbuWLMDto, CbuWLMEntity>(null!);
        Mapper.RegisterGeneratedCore<CbuWLMDto, CbuWLMEntity>(null!);
        Mapper.RegisterGenerated<CbuWLSDto, CbuWLSEntity>(null!);
        Mapper.RegisterGeneratedCore<CbuWLSDto, CbuWLSEntity>(null!);
    }

    /// <summary>
    /// 恢复 CbuWL 类型对的自动注册（防止污染其他测试）
    /// </summary>
    public void Dispose()
    {
        Mapper.RegisterGeneratedCore<CbuWLDto, CbuWLEntity>(GeneratedMapper.Map_CbuWLDto_To_CbuWLEntity);
        Mapper.RegisterGenerated<CbuWLDto, CbuWLEntity>(GeneratedMapper.Map_CbuWLDto_To_CbuWLEntity_WithAction);
        Mapper.RegisterGeneratedCore<CbuWLMDto, CbuWLMEntity>(GeneratedMapper.Map_CbuWLMDto_To_CbuWLMEntity);
        Mapper.RegisterGenerated<CbuWLMDto, CbuWLMEntity>(GeneratedMapper.Map_CbuWLMDto_To_CbuWLMEntity_WithAction);
        Mapper.RegisterGeneratedCore<CbuWLSDto, CbuWLSEntity>(GeneratedMapper.Map_CbuWLSDto_To_CbuWLSEntity);
        Mapper.RegisterGenerated<CbuWLSDto, CbuWLSEntity>(GeneratedMapper.Map_CbuWLSDto_To_CbuWLSEntity_WithAction);
    }

    // 本用例随 S 侧锚定形态退役删除（④——
    // 实体侧标注消费出清，A-14 族已知退出；承载侧合法形态由其余用例锁定）

    // 本用例随 S 侧锚定形态退役删除（④——
    // 实体侧标注消费出清，A-14 族已知退出；承载侧合法形态由其余用例锁定）

    /// <summary>
    /// 专项行②（运行时列）：叶标注 + 源可空 — 源 null → 清叶不调转换器（N-1 语义双引擎一致）
    /// </summary>
    [Fact]
    public void WriteBackLeaf_NullSource_ClearsLeaf_BothEngines()
    {
        var dto = new CbuWLDto { City = null };

        var runtime = Mapper.Map<CbuWLDto, CbuWLEntity>(dto)!;
        Assert.Null(runtime.Address.City);

        var generated = GeneratedMapper.Map_CbuWLDto_To_CbuWLEntity(dto)!;
        Assert.Null(generated.Address.City);
        Assert.Equal(runtime.Address.City, generated.Address.City);
    }

    // 本用例随 S 侧锚定形态退役删除（④——
    // 实体侧标注消费出清，A-14 族已知退出；承载侧合法形态由其余用例锁定）

    // 本用例随 S 侧锚定形态退役删除（④——
    // 实体侧标注消费出清，A-14 族已知退出；承载侧合法形态由其余用例锁定）

}

// ================================================================
// 忽略域行为锁定（F-D1 强形式证伪，双引擎一致断言）
// ================================================================

/// <summary>
/// 用例①实体 — 叶侧 [MapIgnore(Direction.ToEntity)] + [ConvertBack]（双引擎均跳过写回）
/// </summary>
public class CbuWMI1Entity
{
    /// <summary>
    /// 地址（写回链中间节点）
    /// </summary>
    public CbuWMI1Address Address { get; set; } = new();
}

/// <summary>
/// 用例①叶类型 — City 标 [MapIgnore(ToEntity)] + [ConvertBack]
/// </summary>
public class CbuWMI1Address
{
    /// <summary>
    /// 城市（反向 ToEntity 方向命中忽略——双引擎跳过写回，叶保持默认值）
    /// </summary>
    public string City { get; set; } = "";
}

/// <summary>
/// 用例②实体 — DTO 侧 [MapIgnore(Direction.ToEntity)]、叶标 [ConvertBack]（双引擎均跳过；
/// Id 锚点保证反向方法生成——全属性被忽略时反向方法整体不生成属另一独立边界）
/// </summary>
public class CbuWMI2Entity
{
    /// <summary>
    /// 标识（匹配对锚点属性：保证反向方法当前即生成，生成侧断言可编译）
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 地址（写回链中间节点）
    /// </summary>
    public CbuWMI2Address Address { get; set; } = new();
}

/// <summary>
/// 用例②叶类型 — City 标 [ConvertBack]（DTO 侧忽略命中时转换器不消费）
/// </summary>
public class CbuWMI2Address
{
    /// <summary>
    /// 城市（反向写回叶——DTO 侧  命中，双引擎跳过）
    /// </summary>
    public string City { get; set; } = "";
}

/// <summary>
/// 用例③实体 — 叶侧 [MapIgnore(Direction.ToDto)]（方向不命中，写回照常）
/// </summary>
public class CbuWMI3Entity
{
    /// <summary>
    /// 地址（写回链中间节点）
    /// </summary>
    public CbuWMI3Address Address { get; set; } = new();
}

/// <summary>
/// 用例③叶类型 — City 标 [MapIgnore(ToDto)] + [ConvertBack]（反向 ToEntity 方向不命中）
/// </summary>
public class CbuWMI3Address
{
    /// <summary>
    /// 城市（ToDto 方向忽略不影响反向写回——叶标注转换器照常消费）
    /// </summary>
    public string City { get; set; } = "";
}

/// <summary>
/// 忽略域联动转换器（T1 模板）— 三模型族共用，输出 "wmi:" 前缀
/// </summary>
public static class CbuWMConv
{
    /// <summary>
    /// 将 DTO 城市值转换为实体城市值（前缀标识转换器被消费）
    /// </summary>
    /// <param name="fieldValue">DTO 城市值</param>
    /// <returns>带 "wmi:" 前缀的城市值</returns>
    public static string Convert(string fieldValue) => "wmi:" + fieldValue;
}

/// <summary>
/// 用例② DTO — City 标 [MapProperty] 写回链 + [MapIgnore(ToEntity)]（Id 锚点）
/// </summary>
[MapFrom(typeof(CbuWMI2Entity))]
public class CbuWMI2Dto
{
    /// <summary>
    /// 标识（匹配对锚点属性）
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 城市（DTO 侧 ToEntity 方向命中忽略——双引擎跳过写回）
    /// </summary>
    [MapProperty(typeof(CbuWMI2Entity), "Address.City")]
    [MapIgnore(Direction.ToEntity)]
    public string City { get; set; } = "";
}

/// <summary>
/// 用例① DTO — City 仅标 [MapProperty] 写回链（忽略在叶侧）
/// </summary>
[MapFrom(typeof(CbuWMI1Entity))]
public class CbuWMI1Dto
{
    /// <summary>
    /// 城市（反向经写回链 Address.City，叶侧忽略命中时跳过）
    /// </summary>
    [MapProperty(typeof(CbuWMI1Entity), "Address.City")]
    public string City { get; set; } = "";
}

/// <summary>
/// 用例③ DTO — City 仅标 [MapProperty] 写回链（叶侧 ToDto 方向不命中反向）
/// </summary>
[MapFrom(typeof(CbuWMI3Entity))]
public class CbuWMI3Dto
{
    /// <summary>
    /// 城市（反向经写回链 Address.City，叶侧 ToDto 忽略不影响反向）
    /// </summary>
    [MapProperty(typeof(CbuWMI3Entity), "Address.City")]
    public string City { get; set; } = "";
}

/// <summary>
/// 定向测试（运行时列）— 写回链 [MapIgnore] 三方向双引擎锁定
/// <para>F-D1 强形式证伪落地：叶侧 [MapIgnore(ToEntity)] 生成端非无视——发射期
/// ShouldSkipForDirectionPair 双侧判定拦截，与运行时写回双侧检查（ExpressionBuilder
/// BuildReversePathWriteBackFromPath :990-991）最终行为一致；本组用例防未来重构
///（如发射期过滤前移/删除）静默回归。生成列见 ConvertBackUsingGenerationTests
/// WriteBackLeaf_MapIgnoreToEntityOnLeaf_SkipsWriteBack / WriteBackLeaf_MapIgnoreToEntityOnDto_SkipsWriteBack /
/// WriteBackLeaf_MapIgnoreToDtoOnLeaf_WriteBackNotSkipped 三用例。</para>
/// </summary>
public class ConvertBackWriteLeafIgnoreRuntimeTests : IDisposable
{
    /// <summary>
    /// 取消 CbuWMI1/2/3 三组类型对的自动注册，使 Mapper.Map 走运行时路径
    /// </summary>
    public ConvertBackWriteLeafIgnoreRuntimeTests()
    {
        Mapper.RegisterGenerated<CbuWMI1Dto, CbuWMI1Entity>(null!);
        Mapper.RegisterGeneratedCore<CbuWMI1Dto, CbuWMI1Entity>(null!);
        Mapper.RegisterGenerated<CbuWMI2Dto, CbuWMI2Entity>(null!);
        Mapper.RegisterGeneratedCore<CbuWMI2Dto, CbuWMI2Entity>(null!);
        Mapper.RegisterGenerated<CbuWMI3Dto, CbuWMI3Entity>(null!);
        Mapper.RegisterGeneratedCore<CbuWMI3Dto, CbuWMI3Entity>(null!);
    }

    /// <summary>
    /// 恢复三组类型对的自动注册（防止污染其他测试）
    /// </summary>
    public void Dispose()
    {
        Mapper.RegisterGeneratedCore<CbuWMI1Dto, CbuWMI1Entity>(GeneratedMapper.Map_CbuWMI1Dto_To_CbuWMI1Entity);
        Mapper.RegisterGenerated<CbuWMI1Dto, CbuWMI1Entity>(GeneratedMapper.Map_CbuWMI1Dto_To_CbuWMI1Entity_WithAction);
        Mapper.RegisterGeneratedCore<CbuWMI2Dto, CbuWMI2Entity>(GeneratedMapper.Map_CbuWMI2Dto_To_CbuWMI2Entity);
        Mapper.RegisterGenerated<CbuWMI2Dto, CbuWMI2Entity>(GeneratedMapper.Map_CbuWMI2Dto_To_CbuWMI2Entity_WithAction);
        Mapper.RegisterGeneratedCore<CbuWMI3Dto, CbuWMI3Entity>(GeneratedMapper.Map_CbuWMI3Dto_To_CbuWMI3Entity);
        Mapper.RegisterGenerated<CbuWMI3Dto, CbuWMI3Entity>(GeneratedMapper.Map_CbuWMI3Dto_To_CbuWMI3Entity_WithAction);
    }

    // 本用例随 S 侧锚定形态退役删除（④——
    // 实体侧标注消费出清，A-14 族已知退出；承载侧合法形态由其余用例锁定）

    /// <summary>
    /// 用例②（运行时列）：DTO 侧 [MapIgnore(ToEntity)] — 双引擎均跳过写回（Id 匹配对保留）
    /// </summary>
    [Fact]
    public void MapIgnoreToEntityOnDto_BothEngines_SkipWriteBack()
    {
        var dto = new CbuWMI2Dto { Id = 2, City = "SH" };

        var runtime = Mapper.Map<CbuWMI2Dto, CbuWMI2Entity>(dto)!;
        Assert.Equal(2, runtime.Id);
        Assert.Equal("", runtime.Address.City);

        var generated = GeneratedMapper.Map_CbuWMI2Dto_To_CbuWMI2Entity(dto)!;
        Assert.Equal("", generated.Address.City);
        Assert.Equal(runtime.Address.City, generated.Address.City);
    }

    // 本用例随 S 侧锚定形态退役删除（④——
    // 实体侧标注消费出清，A-14 族已知退出；承载侧合法形态由其余用例锁定）

}
