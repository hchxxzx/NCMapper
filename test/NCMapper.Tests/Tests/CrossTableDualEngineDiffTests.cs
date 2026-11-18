using NCMapper;
using NCMapper.Attributes;
using NCMapper.Generated;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// 跨表双端差分测试模型 — 客户导航链（Address.City.Name 三级）
    /// </summary>
    public class CtDCustomer
    {
        /// <summary>
        /// 地址（导航链第一段）
        /// </summary>
        public CtDAddress Address { get; set; } = new();
    }

    /// <summary>
    /// 跨表双端差分测试模型 — 地址（导航链第二段）
    /// </summary>
    public class CtDAddress
    {
        /// <summary>
        /// 城市（导航链第三段）
        /// </summary>
        public CtDCity City { get; set; } = new();
    }

    /// <summary>
    /// 跨表双端差分测试模型 — 城市（导航链叶）
    /// </summary>
    public class CtDCity
    {
        /// <summary>
        /// 城市名（跨表转换器输入 = 末段访问值）
        /// </summary>
        public string Name { get; set; } = "";
    }

    /// <summary>
    /// 跨表双端差分测试模型 — 订单实体（主源）
    /// </summary>
    public class CtDOrderEntity
    {
        /// <summary>
        /// 客户（子源导航）
        /// </summary>
        public CtDCustomer Customer { get; set; } = new();
    }

    /// <summary>
    /// 跨表转换器（静态方法形态 T1 单值模板）— 旧接口形态 TSource=子源 CtDCustomer（B-3/B-4 契约）；
    /// 方法体不消费 source → T1 仅收末段字段值
    /// </summary>
    public static class CtDConverter
    {
        /// <summary>
        /// 转换器被调用次数（静态计数，差分断言用）
        /// </summary>
        public static int CallCount;

        /// <summary>
        /// 将子源末段城市名转换为目标值（前缀标识消费）
        /// </summary>
        /// <param name="fieldValue">末段访问 Address.City.Name 值</param>
        /// <returns>带 "CONV:" 前缀的城市名</returns>
        public static string Convert(string fieldValue)
        {
            CallCount++;
            return "CONV:" + fieldValue;
        }
    }

    /// <summary>
    /// 跨表双端差分测试 DTO — 跨表字段 [Convert]（静态方法形态，转换输入=子源末段值）
    /// </summary>
    [MapFrom(typeof(CtDOrderEntity))]
    public class CtDOrderDto
    {
        /// <summary>
        /// 城市名（跨表字段：主源 OrderEntity 无同名属性，经导航 Customer 匹配）
        /// </summary>
        [MapProperty(typeof(CtDCustomer), "Address.City.Name")]
        [Convert(typeof(CtDConverter), nameof(CtDConverter.Convert))]
        public string CityName { get; set; } = "";
    }

    /// <summary>
    /// 跨表 [Convert] 双端差分测试（模式，矩阵末行）
    /// <para>覆盖任务书验收 1（测试矩阵全绿）中跨表差分项 + 验收 3（G3 双端差分一致）：
    /// 生成端 vs 运行时对同一输入的值结果一致；导航 null 场景双端一致（跳过不调用，Q2 验证）。
    /// 已建运行时独立用例（private 模型，无生成侧对应物），本类用 public 模型
    /// 补差分（RegisterGenerated(null!) 绕缓存值比对）。</para>
    /// </summary>
    public class CrossTableDualEngineDiffTests : IDisposable
    {
        /// <summary>
        /// 取消 CtD 类型对自动注册，使 Mapper.Map 走运行时路径（绕缓存）
        /// </summary>
        public CrossTableDualEngineDiffTests()
        {
            Mapper.RegisterGenerated<CtDOrderEntity, CtDOrderDto>(null!);
            Mapper.RegisterGeneratedCore<CtDOrderEntity, CtDOrderDto>(null!);
        }

        /// <summary>
        /// 恢复 CtD 类型对自动注册（防污染其他测试）
        /// </summary>
        public void Dispose()
        {
            Mapper.RegisterGeneratedCore<CtDOrderEntity, CtDOrderDto>(GeneratedMapper.Map_CtDOrderEntity_To_CtDOrderDto);
            Mapper.RegisterGenerated<CtDOrderEntity, CtDOrderDto>(GeneratedMapper.Map_CtDOrderEntity_To_CtDOrderDto_WithAction);
        }

        /// <summary>
        /// G3 差分（跨表消费）：生成端 Compose Update 方法（source=子源）与运行时 Map 主路径
        /// （source=子源导航）值一致（矩阵 L286/L291 双端对照，B-3/B-4；
        /// T1 单值模板下转换输入均为子源末段值）
        /// </summary>
        [Fact]
        public void CrossTableConvertUsing_RuntimeAndGenerated_ValueConsistent()
        {
            CtDConverter.CallCount = 0;
            var order = new CtDOrderEntity
            {
                Customer = new CtDCustomer { Address = new CtDAddress { City = new CtDCity { Name = "Shenzhen" } } }
            };

            // 运行时路径（构造函数已取消自动注册）：Map 主路径跨表分支 BuildCrossTableAssignment 消费
            var runtime = Mapper.Map<CtDOrderEntity, CtDOrderDto>(order)!;
            Assert.Equal("CONV:Shenzhen", runtime.CityName);

            // 生成侧跨表消费路径 = Compose Update 方法（就地更新 DTO，source=子源对象）
            var genDto = new CtDOrderDto();
            GeneratedMapper.Update_CtDCustomer_To_CtDOrderDto(order.Customer, genDto);
            Assert.Equal("CONV:Shenzhen", genDto.CityName);

            // 双端一致（同字段同转换器同跨表语义，T1 单值输入=末段值）
            Assert.Equal(runtime.CityName, genDto.CityName);
        }

        /// <summary>
        /// G3 差分（导航 null）：Customer=null → 双端均不调用转换器（Q2 验证）
        /// <para>入口差异（设计内）：运行时 Map 跨表 null 安全链短路赋 default(string)=null；
        /// 生成端 Update 子源判空早退不写（DTO 保持初始 ""）。核心语义一致——均不触发转换器调用。
        /// （导航 null 裁决：导航链任一中间节点 null → 跳过转换器调用、叶不写）</para>
        /// </summary>
        [Fact]
        public void CrossTableConvertUsing_NavigationNull_DualEngineSkipNoCall()
        {
            CtDConverter.CallCount = 0;
            var order = new CtDOrderEntity { Customer = null! };

            // 运行时：null 安全链短路 → default(string)=null，不调用转换器
            var runtime = Mapper.Map<CtDOrderEntity, CtDOrderDto>(order)!;
            Assert.Null(runtime.CityName);

            // 生成端 Update：子源判空早退 → 不写，DTO 保持初始 ""
            var genDto = new CtDOrderDto();
            GeneratedMapper.Update_CtDCustomer_To_CtDOrderDto(order.Customer!, genDto);
            Assert.Equal("", genDto.CityName);

            // 核心语义一致：双端均不触发转换器调用
            Assert.Equal(0, CtDConverter.CallCount);
        }
    }
}
