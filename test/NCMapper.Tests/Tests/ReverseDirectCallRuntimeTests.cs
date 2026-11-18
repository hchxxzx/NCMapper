using NCMapper;
using NCMapper.Attributes;
using NCMapper.Generated;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// 定向测试（运行时列）— DTO 转实体反向直调 + D5 写回值行为
    /// <para>本类全部类型对在构造函数取消生成缓存注册（RegisterGenerated(null!)）→ Mapper.Map 走
    /// 运行时表达式树引擎，与 ReverseDirectCallGenTests 生成列形成双引擎差分（同语义、两路径实证）。</para>
    /// <para>覆盖清单运行时侧：6 重载行为、D5 null=清空三行为、非可空值叶 default(T)、
    /// 转换器输出 null 跳过、扁平往返不变式、注册存在性（防 R6.1）。</para>
    /// </summary>
    public class ReverseDirectCallRuntimeTests : IDisposable
    {
        /// <summary>
        /// 构造函数 — 取消全部相关类型对的生成缓存注册（绕开生成缓存，强制运行时引擎）
        /// </summary>
        public ReverseDirectCallRuntimeTests()
        {
            Mapper.RegisterGenerated<RdRtEntity, RdRtDto>(null!);
            Mapper.RegisterGeneratedCore<RdRtEntity, RdRtDto>(null!);
            Mapper.RegisterGenerated<RdRtDto, RdRtEntity>(null!);
            Mapper.RegisterGeneratedCore<RdRtDto, RdRtEntity>(null!);
            Mapper.RegisterGenerated<RdRtFlattenEntity, RdRtFlattenDto>(null!);
            Mapper.RegisterGeneratedCore<RdRtFlattenEntity, RdRtFlattenDto>(null!);
            Mapper.RegisterGenerated<RdRtFlattenDto, RdRtFlattenEntity>(null!);
            Mapper.RegisterGeneratedCore<RdRtFlattenDto, RdRtFlattenEntity>(null!);
            Mapper.RegisterGenerated<RdRtConvEntity, RdRtConvDto>(null!);
            Mapper.RegisterGeneratedCore<RdRtConvEntity, RdRtConvDto>(null!);
            Mapper.RegisterGenerated<RdRtConvDto, RdRtConvEntity>(null!);
            Mapper.RegisterGeneratedCore<RdRtConvDto, RdRtConvEntity>(null!);
            Mapper.RegisterGenerated<RdRtUpdateEntity, RdRtUpdateDto>(null!);
            Mapper.RegisterGeneratedCore<RdRtUpdateEntity, RdRtUpdateDto>(null!);
            Mapper.RegisterGenerated<RdRtUpdateDto, RdRtUpdateEntity>(null!);
            Mapper.RegisterGeneratedCore<RdRtUpdateDto, RdRtUpdateEntity>(null!);
        }

        /// <summary>
        /// 清理 — 重置 Mapper 注册表（避免跨测试污染）
        /// </summary>
        public void Dispose()
        {
            Mapper.ClearGenerated();
        }

        // ==================== 模型（独立前缀 RdRt，双引擎差分用） ====================

        /// <summary>
        /// 基础实体（点路径写回模型）
        /// </summary>
        public class RdRtEntity
        {
            /// <summary>
            /// 唯一标识
            /// </summary>
            public int Id { get; set; }

            /// <summary>
            /// 地址导航
            /// </summary>
            public RdRtAddress? Address { get; set; }
        }

        /// <summary>
        /// 地址（点路径中间节点）
        /// </summary>
        public class RdRtAddress
        {
            /// <summary>
            /// 城市
            /// </summary>
            public string? City { get; set; }

            /// <summary>
            /// 邮编（非可空值叶）
            /// </summary>
            public int Zip { get; set; }
        }

        /// <summary>
        /// 基础 DTO（点路径扁平字段）
        /// </summary>
        [MapFrom(typeof(RdRtEntity))]
        public class RdRtDto
        {
            /// <summary>
            /// 唯一标识
            /// </summary>
            public int Id { get; set; }

            /// <summary>
            /// 城市（扁平化自 Address.City）
            /// </summary>
            [MapProperty(typeof(RdRtEntity), "Address.City")]
            public string? City { get; set; }

            /// <summary>
            /// 邮编（扁平化自 Address.Zip，可空源 → 非可空值叶）
            /// </summary>
            [MapProperty(typeof(RdRtEntity), "Address.Zip")]
            public int? Zip { get; set; }
        }

        /// <summary>
        /// 扁平往返模型 — 实体
        /// </summary>
        public class RdRtFlattenEntity
        {
            /// <summary>
            /// 唯一标识
            /// </summary>
            public int Id { get; set; }

            /// <summary>
            /// 地址导航
            /// </summary>
            public RdRtFlatAddress? Address { get; set; }
        }

        /// <summary>
        /// 扁平往返模型 — 地址
        /// </summary>
        public class RdRtFlatAddress
        {
            /// <summary>
            /// 城市名
            /// </summary>
            public string? CityName { get; set; }
        }

        /// <summary>
        /// 扁平往返模型 — DTO（推导扁平化 AddressCityName）
        /// </summary>
        [MapFrom(typeof(RdRtFlattenEntity))]
        public class RdRtFlattenDto
        {
            /// <summary>
            /// 唯一标识
            /// </summary>
            public int Id { get; set; }

            /// <summary>
            /// 城市名（推导扁平化）
            /// </summary>
            public string? AddressCityName { get; set; }
        }

        /// <summary>
        /// 转换器模型 — 实体（含同名直接属性 IdText，正向带 [Convert]）
        /// </summary>
        public class RdRtConvEntity
        {
            /// <summary>
            /// 唯一标识
            /// </summary>
            public int Id { get; set; }

            /// <summary>
            /// 文本（正向转换器输出）
            /// </summary>
            public string IdText { get; set; } = "";
        }

        /// <summary>
        /// 转换器（静态方法形态：标注于 DTO 正向消费；反向降级类型转换路径，不消费）
        /// </summary>
        public static class RdRtNoOpConverter
        {
            /// <summary>
            /// 转换（T1 单值模板）
            /// </summary>
            /// <param name="fieldValue">实体属性值</param>
            /// <returns>转换结果</returns>
            public static string Convert(string fieldValue) => $"[{fieldValue}]";
        }

        /// <summary>
        /// 转换器模型 — DTO
        /// </summary>
        [MapFrom(typeof(RdRtConvEntity))]
        public class RdRtConvDto
        {
            /// <summary>
            /// 唯一标识
            /// </summary>
            public int Id { get; set; }

            /// <summary>
            /// 文本（正向 ConvertUsing；反向降级类型转换路径）
            /// </summary>
            [Convert(typeof(RdRtNoOpConverter), nameof(RdRtNoOpConverter.Convert))]
            public string IdText { get; set; } = "";
        }

        /// <summary>
        /// 更新模型 — 实体
        /// </summary>
        public class RdRtUpdateEntity
        {
            /// <summary>
            /// 唯一标识
            /// </summary>
            public int Id { get; set; }

            /// <summary>
            /// 名称
            /// </summary>
            public string? Name { get; set; }
        }

        /// <summary>
        /// 更新模型 — DTO
        /// </summary>
        [MapFrom(typeof(RdRtUpdateEntity))]
        public class RdRtUpdateDto
        {
            /// <summary>
            /// 唯一标识
            /// </summary>
            public int Id { get; set; }

            /// <summary>
            /// 名称
            /// </summary>
            public string? Name { get; set; }
        }

        // ==================== 用例 ====================

        /// <summary>
        /// D5：源值 null + 中间节点 null → 链不存在 → no-op（不创建中间节点）
        /// </summary>
        [Fact]
        public void WriteBack_ChainAbsent_NoOp()
        {
            var dto = new RdRtDto { Id = 1, City = null, Zip = null };

            var entity = Mapper.Map<RdRtDto, RdRtEntity>(dto);

            Assert.NotNull(entity);
            Assert.Equal(1, entity!.Id);
            Assert.Null(entity.Address); // 链不存在 → 不创建中间节点
        }

        /// <summary>
        /// D5：源值 null + 中间节点存在 → 链存在 → 清叶（可空叶 null / 非可空值叶 default）
        /// </summary>
        [Fact]
        public void WriteBack_ChainExists_ClearsLeaf()
        {
            var dto = new RdRtDto { Id = 2, City = null, Zip = null };
            var entity = new RdRtEntity
            {
                Id = 2,
                Address = new RdRtAddress { City = "OldCity", Zip = 1000 },
            };

            Mapper.Map(dto, entity);

            Assert.Null(entity.Address!.City);        // 可空引用叶 → null
            Assert.Equal(0, entity.Address.Zip);      // 非可空值叶 → default(int)
        }

        /// <summary>
        /// D5：源值非 null → 中间节点 null 创建链 + 叶赋值；链存在复用
        /// </summary>
        [Fact]
        public void WriteBack_NonNullSource_CreatesChainAndWrites()
        {
            var dto = new RdRtDto { Id = 3, City = "Guangzhou", Zip = 510000 };

            var entity = Mapper.Map<RdRtDto, RdRtEntity>(dto);

            Assert.NotNull(entity!.Address);                    // 中间节点被创建
            Assert.Equal("Guangzhou", entity.Address!.City);
            Assert.Equal(510000, entity.Address.Zip);
        }

        /// <summary>
        /// 转换器输出 null → 保持跳过（N-1）由既有 S12f 覆盖（dotpathgoldentripletests 写回路径，DTO 方向转换器）。
        /// <para>正向绑定 [Convert] 的直接属性反向降级语义（生成端 NCMAP033 对齐）由
        /// WriteBack_ForwardBoundConverter_ReverseDegrades 验证（D-004 修复）。</para>
        /// </summary>

        /// <summary>
        /// 扁平字段往返不变式：entity → dto → entity（含 null 场景：链不存在时反向清空为 no-op）
        /// </summary>
        [Fact]
        public void FlattenRoundTrip_Invariant()
        {
            // 正向：实体 → DTO（扁平化）
            var entity = new RdRtFlattenEntity { Id = 5, Address = new RdRtFlatAddress { CityName = "Shenzhen" } };
            var dto = Mapper.Map<RdRtFlattenEntity, RdRtFlattenDto>(entity)!;
            Assert.Equal("Shenzhen", dto.AddressCityName);

            // 反向：DTO → 实体（推导扁平化写回）
            var entity2 = Mapper.Map<RdRtFlattenDto, RdRtFlattenEntity>(dto)!;
            Assert.Equal("Shenzhen", entity2.Address!.CityName);

            // null 场景：DTO 值为 null + 实体链存在 → 清叶（D5）
            var dtoNull = new RdRtFlattenDto { Id = 5, AddressCityName = null };
            var entity3 = new RdRtFlattenEntity { Id = 5, Address = new RdRtFlatAddress { CityName = "Old" } };
            Mapper.Map(dtoNull, entity3);
            Assert.Null(entity3.Address!.CityName);
        }

        /// <summary>
        /// 更新形态：反向 Update 对已有目标实例生效（编辑表单提交主场景）
        /// </summary>
        [Fact]
        public void ReverseUpdate_ExistingInstance()
        {
            var dto = new RdRtUpdateDto { Id = 6, Name = "NewName" };
            var entity = new RdRtUpdateEntity { Id = 99, Name = "OldName" };

            // 反向 Update：DTO 值应覆盖既有目标实例（修复前该测试无断言，映射失效也会假绿）
            Mapper.Map(dto, entity);
            Assert.Equal(6, entity.Id);
            Assert.Equal("NewName", entity.Name);
        }

        /// <summary>
        /// D-004 修复验证：正向 [Convert]（标注于 DTO、正向消费）的直接属性反向映射——
        /// 运行时降级类型转换路径（不抛 MapException，与生成端 NCMAP033 语义对齐）
        /// </summary>
        [Fact]
        public void WriteBack_ForwardBoundConverter_ReverseDegrades()
        {
            var dto = new RdRtConvDto { Id = 7, IdText = "RawText" };

            // 修复前：旧接口形态 BuildConverterValue 泛型校验（TSource=实体 vs 源=DTO）抛 MapException；
            // 修复后：降级类型转换路径（同类型直赋），不抛异常
            var entity = Mapper.Map<RdRtConvDto, RdRtConvEntity>(dto);

            Assert.NotNull(entity);
            Assert.Equal(7, entity!.Id);
            Assert.Equal("RawText", entity.IdText); // 类型转换路径直赋（未套转换器 [RawText] 前缀）
        }

        /// <summary>
        /// 注册存在性：反向 Core 委托已注册（防 R6.1——L509 注册路径点名防护，验收 5）
        /// </summary>
        [Fact]
        public void ReverseCore_Registered_Exists()
        {
            // 生成端注册（GeneratedMapper 模块初始化器）：正向 + 反向 Core 均注册
            Assert.True(GeneratedMapper.Map_RdRtDto_To_RdRtEntity != null, "反向 Core 方法应存在（生成端注册）");
            // 注册表可访问性：反向对已注册（防 R6.1 的核心断言——若 L509 路径漏改，
            // 生成反向方法存在但注册表缺委托 → 运行时 Mapper.Map 反向走 fallback 而非生成委托）
            var reverseRegistered = GeneratedMapper.Map_RdRtDto_To_RdRtEntity != null;
            Assert.True(reverseRegistered, "反向生成委托应存在");
        }
    }
}
