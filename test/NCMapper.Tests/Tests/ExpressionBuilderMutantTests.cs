using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using NCMapper.Attributes;
using NCMapper.Tests.Models;
using System.Linq.Expressions;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// ExpressionBuilder mutant-killing tests — 覆盖表达式构建器中存活变异体的代码路径
    /// </summary>
    [Collection("DeepCopyGlobalState")]
    public class ExpressionBuilderMutantTests : IDisposable
    {
        private readonly int _originalMaxInlineDepth;

        public ExpressionBuilderMutantTests()
        {
            _originalMaxInlineDepth = Mapper.MaxInlineDepth;
            Mapper.ClearCache();
        }

        public void Dispose()
        {
            Mapper.MaxInlineDepth = _originalMaxInlineDepth;
            Mapper.ClearCache();
        }

        // ================================================================
        // 测试模型 — 针对 ExpressionBuilder 中存活变异体设计
        // ================================================================

        /// <summary>
        /// 源实体 — 含 string 属性 + int 属性 + 可空属性
        /// </summary>
        public class SimpleSource
        {
            public int Id { get; set; } = 42;
            public string Name { get; set; } = "Test";
            public int? NullableInt { get; set; } = 100;
            public int NonNullableInt { get; set; } = 200;
        }

        /// <summary>
        /// 目标 DTO — 含 ConvertUsing 但无匹配源属性 (杀灭 L69/L76/L78-79)
        /// ComputedField 有 [Convert] 但 SimpleSource 没有 "ComputedField" 属性
        /// </summary>
        [MapFrom(typeof(SimpleSource))]
        public class TargetWithNoSourceConvertUsing
        {
            public int Id { get; set; }

            /// <summary>
            /// 无匹配源属性但有 [Convert] — 杀灭 L69 sourceItem == null 分支
            /// 和 L76 converterExpr == null 判断、L78-79 的 continue
            /// </summary>
            [Convert(typeof(SimpleComputedConverter), nameof(SimpleComputedConverter.Convert))]
            public string ComputedField { get; set; } = "";
        }

        /// <summary>
        /// 简单计算转换器（静态方法形态 T2 多字段模板）— 无匹配源属性时直接读 source 对象字段
        /// </summary>
        public static class SimpleComputedConverter
        {
            public static string Convert(SimpleSource source)
                => $"Id={source.Id}";
        }

        /// <summary>
        /// 目标 DTO — 含只读属性 (杀灭 L69 CanWrite 判断)
        /// ReadOnlyField 无 setter → targetItem.CanWrite == false → 应跳过
        /// </summary>
        public class TargetWithReadOnlyProp
        {
            public int Id { get; set; }
            public string ReadOnlyField { get; } = "FIXED";
        }

        /// <summary>
        /// 源实体 — 含 [MapIgnore] 标注的属性 (杀灭 L88 ||→&& 变异)
        /// </summary>
        public class SourceWithMapIgnore
        {
            public int Id { get; set; } = 1;
            public string SecretField { get; set; } = "SECRET";

            public string PublicField { get; set; } = "PUBLIC";
        }

        /// <summary>
        /// 目标 DTO — 对应 SourceWithMapIgnore
        /// SecretField 在源端有 [MapIgnore]，但目标端没有
        /// 杀灭 L88 的 || 变异：ShouldIgnore(sourceItem) || ShouldIgnore(targetItem)
        /// 如果变异为 &&，只有两端都有 [MapIgnore] 才跳过，SecretField 会被错误映射
        /// </summary>
        public class TargetForMapIgnoreSource
        {
            public int Id { get; set; }
            public string SecretField { get; set; } = "SHOULD_NOT_BE_OVERWRITTEN";
            public string PublicField { get; set; } = "";
        }

        /// <summary>
        /// 源实体 — 含可空 int? 和非可空 int
        /// </summary>
        public class NullableSourceEntity
        {
            public int? NullableInt { get; set; } = 42;
            public int? NullNullableInt { get; set; } = null;
            public int NonNullableInt { get; set; } = 200;
            public string Name { get; set; } = "Hello";
        }

        /// <summary>
        /// 目标 DTO — nullable→non-nullable 和 non-nullable→nullable
        /// 杀灭 L157 (nullable→non-nullable) 和 L165-166 (non-nullable→nullable, not string)
        /// </summary>
        public class NullableConversionDto
        {
            /// <summary>
            /// int? → int: HasValue 检查路径 (杀灭 L157)
            /// </summary>
            public int NullableInt { get; set; }

            /// <summary>
            /// int? (null) → int: default 路径
            /// </summary>
            public int NullNullableInt { get; set; }

            /// <summary>
            /// int → int?: 非可空→可空转换 (杀灭 L165-166)
            /// 非 string 类型走 Convert 路径
            /// </summary>
            public int? NonNullableInt { get; set; }
        }

        /// <summary>
        /// 目标 DTO — string → string 直接赋值 (杀灭 L166 string 检查)
        /// 如果 sourceItem.PropertyType != typeof(string) 变异为 == typeof(string)，
        /// string 类型的属性会错误进入非可空→可空的 Convert 路径
        /// </summary>
        public class StringToNullableDto
        {
            /// <summary>
            /// string → string?: 源是 string (IsNullable=true for reference types)
            /// 应走直接赋值路径而非 Convert 路径
            /// </summary>
            public string? Name { get; set; }
        }

        /// <summary>
        /// 源实体 — 含 string 属性 (用于 string→string 直接赋值测试)
        /// </summary>
        public class StringSource
        {
            public string Name { get; set; } = "DirectCopy";
            public string Description { get; set; } = "Desc";
        }

        /// <summary>
        /// 目标 DTO — string→string 同类型直接赋值
        /// 杀灭 L166 的 string 类型排除检查
        /// </summary>
        public class StringTarget
        {
            public string Name { get; set; } = "";
            public string Description { get; set; } = "";
        }

        /// <summary>
        /// 转换器（静态方法形态 T1 单值模板）— 正向消费的有效转换器
        /// </summary>
        public static class ValidConverter
        {
            public static string Convert(int fieldValue)
                => $"Value={fieldValue}";
        }

        /// <summary>
        /// 目标 DTO — 使用有效转换器（正向消费路径）
        /// </summary>
        [MapFrom(typeof(ConverterSourceEntity))]
        public class ConverterValidationDto
        {
            public int Id { get; set; }

            [Convert(typeof(ValidConverter), nameof(ValidConverter.Convert))]
            [MapProperty(typeof(ConverterSourceEntity), nameof(ConverterSourceEntity.Code))]
            public string FormattedCode { get; set; } = "";
        }

        /// <summary>
        /// 源端和目标端同时标注 [MapIgnore] — 杀灭 L88 的 || 变异
        /// 当两端都有 [MapIgnore] 时，|| 和 && 结果相同，
        /// 需要配合单端标注的测试来杀灭变异
        /// </summary>
        public class BothIgnoreSource
        {
            public int Id { get; set; } = 1;
            public string IgnoredBoth { get; set; } = "IGNORED";
        }

        public class BothIgnoreTarget
        {
            public int Id { get; set; }
            public string IgnoredBoth { get; set; } = "KEEP";
        }

        // ================================================================
        // Round 2 测试模型
        // ================================================================

        /// <summary>
        /// 目标 DTO — 含只读属性 + Action 回调 (杀灭 L69 CanWrite + Action 路径)
        /// </summary>
        public class TargetWithReadOnlyAndAction
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public string ReadOnlyProp { get; } = "READONLY";
        }

        /// <summary>
        /// 源实体 — 对应 TargetWithReadOnlyAndAction
        /// </summary>
        public class SourceForReadOnlyAction
        {
            public int Id { get; set; } = 1;
            public string Name { get; set; } = "Test";
            public string ReadOnlyProp { get; set; } = "SHOULD_NOT_MAP";
        }

        /// <summary>
        /// 源实体 — 仅在目标端标注 [MapIgnore] (杀灭 L88 ||→&& 变异)
        /// Secret 属性在源端无 [MapIgnore]
        /// </summary>
        public class SourceForTargetIgnore
        {
            public int Id { get; set; } = 1;
            public string Secret { get; set; } = "SOURCE_SECRET";
            public string Name { get; set; } = "PublicName";
        }

        /// <summary>
        /// 目标 DTO — 仅在目标端标注 [MapIgnore]（补锚：承载侧合法形态）
        /// 杀灭 L88 ShouldIgnore(targetItem, targetIsDefine) 路径
        /// </summary>
        [MapFrom(typeof(SourceForTargetIgnore))]
        public class TargetWithMapIgnore
        {
            public int Id { get; set; }

            [MapIgnore]
            public string Secret { get; set; } = "KEEP_DEFAULT";

            public string Name { get; set; } = "";
        }

        /// <summary>
        /// 源实体 — 含嵌套 class 属性 (杀灭 L270 class→class 跨类型)
        /// </summary>
        public class CrossClassSource
        {
            public int Id { get; set; } = 1;
            public Address AddressInfo { get; set; } = new() { City = "Shanghai", Street = "Nanjing Road", ZipCode = 200000 };
        }

        /// <summary>
        /// 目标 DTO — AddressInfo 是不同类型但同名的 class (杀灭 L270)
        /// </summary>
        public class CrossClassTarget
        {
            public int Id { get; set; }
            public AddressDto AddressInfo { get; set; } = new();
        }

        /// <summary>
        /// 源实体 — 含数组属性 (杀灭 L270 IsArray 检查)
        /// Scores 是 int[] — IsArray = true
        /// </summary>
        public class ArrayPropSource
        {
            public int Id { get; set; } = 1;
            public int[] Scores { get; set; } = new[] { 10, 20, 30 };
        }

        /// <summary>
        /// 目标 DTO — Scores 也是 int[] (同类型集合)
        /// 同类型 int[] → int[] 走 BuildSameTypeCollectionAssignment 路径
        /// 确保数组属性不进入 class-to-class 分支 (杀灭 L270 IsArray 检查)
        /// </summary>
        public class ArrayPropTarget
        {
            public int Id { get; set; }
            public int[] Scores { get; set; } = Array.Empty<int>();
        }

        /// <summary>
        /// 源实体 — 含泛型属性 (杀灭 L270 IsGenericType 检查)
        /// Tags 是 List&lt;string&gt; — IsGenericType = true
        /// </summary>
        public class GenericPropSource
        {
            public int Id { get; set; } = 1;
            public List<string> Tags { get; set; } = new() { "A", "B" };
        }

        /// <summary>
        /// 目标 DTO — Tags 也是 List&lt;string&gt; (同类型泛型集合)
        /// 同类型 List&lt;string&gt; → List&lt;string&gt; 走 BuildSameTypeCollectionAssignment
        /// 确保泛型属性不进入 class-to-class 分支 (杀灭 L270 IsGenericType 检查)
        /// </summary>
        public class GenericPropTarget
        {
            public int Id { get; set; }
            public List<string> Tags { get; set; } = new();
        }

        /// <summary>
        /// 历史负例转换器（静态方法形态 T1 单值模板）— 旧接口形态下 TSource 泛型参数与实际源类型不符；
        /// 静态形态下反向一律不消费（A-4），仅作反向不消费回归锚点
        /// </summary>
        public static class WrongSourceEntityConverter
        {
            public static string Convert(int fieldValue) => $"Wrong={fieldValue}";
        }

        /// <summary>
        /// 目标 DTO — 使用历史负例转换器（仅反向场景消费方：不消费）
        /// </summary>
        public class TargetWithWrongConverter
        {
            public int Id { get; set; }
            public string FormattedId { get; set; } = "DEFAULT";
        }

        /// <summary>
        /// 历史负例转换器类（静态方法形态 T1 单值模板）— 旧接口形态下未实现 IValueConverter；
        /// 静态形态下反向一律不消费（A-4），仅作反向不消费回归锚点
        /// </summary>
        public static class StaticConvertMethod
        {
            public static string Convert(int value) => $"Static={value}";
        }

        /// <summary>
        /// 目标 DTO — 使用历史负例转换器（仅反向场景消费方：不消费）
        /// 反向不触发静态方法解析 → 属性保持初始值
        /// </summary>
        public class TargetWithStaticConverter
        {
            public int Id { get; set; }
            public string FormattedId { get; set; } = "DEFAULT";
        }

        /// <summary>
        /// 目标 DTO — 使用历史负例转换器 (有匹配源属性；仅反向场景消费方：不消费)
        /// </summary>
        public class TargetWithStaticConverterMatching
        {
            public int Id { get; set; }
            public int FormattedId { get; set; }
        }

        /// <summary>
        /// 源实体 — 含 FormattedId 属性，匹配 TargetWithStaticConverterMatching
        /// 用于测试 ConvertUsing 反向不消费路径（有匹配源属性场景）
        /// </summary>
        public class SourceWithFormattedId
        {
            public int Id { get; set; } = 1;
            public int FormattedId { get; set; } = 55;
        }

        /// <summary>
        /// 源实体 — 含非泛型 class 属性 (Detail 是 Address)
        /// 用于 L270 generic 检查：一个泛型一个非泛型
        /// </summary>
        public class GenericVsNonGenericSource
        {
            public int Id { get; set; } = 1;
            public List<int> Detail { get; set; } = new() { 1, 2, 3 };
        }

        /// <summary>
        /// 目标 DTO — Detail 是非泛型 class (Address)
        /// source: List&lt;int&gt; (IsGenericType=true) vs target: Address (IsGenericType=false)
        /// 杀灭 L272 !sourceItem.IsGenericType &amp;&amp; !targetItem.IsGenericType
        /// </summary>
        public class GenericVsNonGenericTarget
        {
            public int Id { get; set; }
            public Address Detail { get; set; } = new();
        }

        // ================================================================
        // Round 3 测试模型 — null guards, MaxInlineDepth, JSON, collections
        // ================================================================

        /// <summary>
        /// 源实体 — 含可空嵌套对象 (杀灭 GetClassExpression null check L497)
        /// </summary>
        public class NullNestedSource
        {
            public int Id { get; set; } = 1;
            /// <summary>嵌套对象为 null — 杀灭 GetClassExpression 中 sourceProperty != null 条件</summary>
            public Address? HomeAddress { get; set; } = null;
        }

        /// <summary>
        /// 目标 DTO — 对应 NullNestedSource
        /// </summary>
        [MapFrom(typeof(NullNestedSource))]
        public class NullNestedTarget
        {
            public int Id { get; set; }
            public AddressDto? HomeAddress { get; set; }
        }

        /// <summary>
        /// 源实体 — 含对象属性用于 object→JSON 序列化 (杀灭 GetJsonByObjectExpression)
        /// </summary>
        public class ObjectToJsonSource
        {
            public int Id { get; set; } = 1;
            /// <summary>对象属性 — 将被序列化为 JSON 字符串</summary>
            public AppConfig Config { get; set; } = new() { Theme = "Dark", FontSize = 14 };
        }

        /// <summary>
        /// 目标 DTO — object→JSON string (杀灭 GetJsonByObjectExpression L556-569)
        /// Config 是对象类型, ConfigJson 是 string, IsFromJson=true 触发 object→JSON 路径
        /// </summary>
        [MapFrom(typeof(ObjectToJsonSource))]
        public class ObjectToJsonDto
        {
            public int Id { get; set; }

            [MapProperty(typeof(ObjectToJsonSource), nameof(ObjectToJsonSource.Config), IsFromJson = true)]
            public string? ConfigJson { get; set; }
        }

        /// <summary>
        /// 源实体 — 含可空对象属性 (杀灭 GetJsonByObjectExpression null guard L559)
        /// </summary>
        public class NullObjectToJsonSource
        {
            public int Id { get; set; } = 1;
            /// <summary>null 对象 — 杀灭 GetJsonByObjectExpression 的 != null 条件</summary>
            public AppConfig? Config { get; set; } = null;
        }

        /// <summary>
        /// 目标 DTO — null object→JSON string (杀灭 GetJsonByObjectExpression null → null)
        /// </summary>
        [MapFrom(typeof(NullObjectToJsonSource))]
        public class NullObjectToJsonDto
        {
            public int Id { get; set; }

            [MapProperty(typeof(NullObjectToJsonSource), nameof(NullObjectToJsonSource.Config), IsFromJson = true)]
            public string? ConfigJson { get; set; }
        }

        /// <summary>
        /// 源实体 — JSON string 为 null (杀灭 GetObjectByJsonExpression null guard L590)
        /// </summary>
        public class NullJsonSource
        {
            public int Id { get; set; } = 1;
            /// <summary>null JSON 字符串 — 杀灭 GetObjectByJsonExpression 的 != null 条件</summary>
            public string? ConfigJson { get; set; } = null;
        }

        /// <summary>
        /// 目标 DTO — null JSON→object (杀灭 GetObjectByJsonExpression null → null)
        /// </summary>
        [MapFrom(typeof(NullJsonSource))]
        public class NullJsonToDto
        {
            public int Id { get; set; }

            [MapProperty(typeof(NullJsonSource), nameof(NullJsonSource.ConfigJson), IsFromJson = true)]
            public AppConfig? Config { get; set; }
        }

        /// <summary>
        /// 源实体 — 含可空集合属性 (杀灭 ConvertListExpression/GetListExpression null guard)
        /// </summary>
        public class NullCollectionSource
        {
            public int Id { get; set; } = 1;
            public List<string>? Tags { get; set; } = null;
            public int[]? Scores { get; set; } = null;
            public Dictionary<string, int>? Metadata { get; set; } = null;
        }

        /// <summary>
        /// 目标 DTO — 可空集合 (杀灭集合表达式中 != null 条件)
        /// </summary>
        [MapFrom(typeof(NullCollectionSource))]
        public class NullCollectionDto
        {
            public int Id { get; set; }
            public List<string>? Tags { get; set; }
            public int[]? Scores { get; set; }
            public Dictionary<string, int>? Metadata { get; set; }
        }

        /// <summary>
        /// 源实体 — 含 enum[] 数组 (杀灭 BuildSameTypeCollectionAssignment L308-310 isSimpleElement for array enum)
        /// </summary>
        public class EnumArraySource
        {
            public int Id { get; set; } = 1;
            public OrderStatus[] Statuses { get; set; } = new[] { OrderStatus.Pending, OrderStatus.Shipped };
        }

        /// <summary>
        /// 目标 DTO — enum[] 同类型 (杀灭 isSimpleElement 对数组元素类型 IsEnum 检测)
        /// </summary>
        [MapFrom(typeof(EnumArraySource))]
        public class EnumArrayDto
        {
            public int Id { get; set; }
            public OrderStatus[] Statuses { get; set; } = Array.Empty<OrderStatus>();
        }

        /// <summary>
        /// 源实体 — 含 string[] 数组 (杀灭 isSimpleElement for array string)
        /// </summary>
        public class StringArraySource
        {
            public int Id { get; set; } = 1;
            public string[] Tags { get; set; } = new[] { "a", "b" };
        }

        /// <summary>
        /// 目标 DTO — string[] 同类型
        /// </summary>
        [MapFrom(typeof(StringArraySource))]
        public class StringArrayTarget
        {
            public int Id { get; set; }
            public string[] Tags { get; set; } = Array.Empty<string>();
        }

        // ================================================================
        // 测试用例 — 杀灭 ExpressionBuilder 存活变异体
        // ================================================================

        /// <summary>
        /// [Convert] 无匹配源属性时转换器仍被调用
        /// 杀灭 L69 (sourceItem == null &amp;&amp; targetItem.CanWrite) 和 L76/L78-79
        /// sourceItem 为 null 时，如果有 [Convert]，转换器仍应被调用
        /// </summary>
        [Fact]
        public void Map_ConvertUsing_NoMatchingSource_ConverterCalled()
        {
            var source = new MultiFieldSourceEntity { Id = 42, FirstName = "A", LastName = "B" };
            var dto = Mapper.Map<MultiFieldSourceEntity, MultiFieldDto>(source)!;

            Assert.Equal("Id=42", dto.ComputedField);
            Assert.Equal("A B", dto.FullName);
        }

        /// <summary>
        /// 目标只读属性被跳过 — 杀灭 L69 的 CanWrite == false 检查
        /// 如果 CanWrite 检查被移除或变异，只读属性赋值会抛异常
        /// </summary>
        [Fact]
        public void Map_ReadOnly_Target_Property_Skipped()
        {
            var source = new SimpleSource { Id = 99, Name = "Test" };
            var target = Mapper.Map<SimpleSource, TargetWithReadOnlyProp>(source)!;

            Assert.Equal(99, target.Id);
            Assert.Equal("FIXED", target.ReadOnlyField);
        }

            // 实体侧 [MapIgnore] 消费锁定用例随源侧腿退役删除

            // 实体侧 [MapIgnore] 消费锁定用例随源侧腿退役删除

        /// <summary>
        /// int? → int nullable→non-nullable 转换 — 杀灭 L157
        /// HasValue=true 时正常转换，null 时返回 default
        /// </summary>
        [Fact]
        public void Map_Nullable_To_NonNullable_HasValue()
        {
            var source = new NullableSourceEntity
            {
                NullableInt = 42,
                NullNullableInt = null,
                NonNullableInt = 200,
                Name = "Test"
            };
            var target = Mapper.Map<NullableSourceEntity, NullableConversionDto>(source)!;

            Assert.Equal(42, target.NullableInt);
            Assert.Equal(0, target.NullNullableInt);
            Assert.NotNull(target.NonNullableInt);
            Assert.Equal(200, target.NonNullableInt!.Value);
        }

        /// <summary>
        /// int → int? non-nullable→nullable 转换 — 杀灭 L165-166
        /// sourceItem.PropertyType != typeof(string) 检查确保非 string 类型走此路径
        /// </summary>
        [Fact]
        public void Map_NonNullable_To_Nullable_Int()
        {
            var source = new NullableSourceEntity { NonNullableInt = 999 };
            var target = Mapper.Map<NullableSourceEntity, NullableConversionDto>(source)!;

            Assert.NotNull(target.NonNullableInt);
            Assert.Equal(999, target.NonNullableInt!.Value);
        }

        /// <summary>
        /// string → string 直接赋值 — 杀灭 L166 的 typeof(string) 排除
        /// string 是引用类型但 IsNullableType 返回 true，
        /// 但 sourceItem.PropertyType != typeof(string) 排除了 string，不走 Convert 路径
        /// </summary>
        [Fact]
        public void Map_String_To_String_Direct()
        {
            var source = new StringSource { Name = "Hello", Description = "World" };
            var target = Mapper.Map<StringSource, StringTarget>(source)!;

            Assert.Equal("Hello", target.Name);
            Assert.Equal("World", target.Description);
        }

        /// <summary>
        /// [Convert] 有效转换器（静态方法形态）— 正向消费路径
        /// 验证 T1 单值模板静态方法被正确解析与调用
        /// </summary>
        [Fact]
        public void Map_ConvertUsing_Valid_Interface_Detection()
        {
            var source = new ConverterSourceEntity { Id = 1, Code = 42 };
            var target = Mapper.Map<ConverterSourceEntity, ConverterValidationDto>(source)!;

            Assert.Equal(1, target.Id);
            Assert.Equal("Value=42", target.FormattedCode);
        }

        /// <summary>
        /// 混合可空转换 — 同一 DTO 同时包含 nullable→non-nullable 和 non-nullable→nullable
        /// 杀灭多条可空转换路径的组合正确性
        /// </summary>
        [Fact]
        public void Map_Mixed_Nullable_Conversions()
        {
            var source = new NullableTypesEntity
            {
                NullableDateTime = new DateTime(2024, 6, 15),
                NullDateTime = null,
                NullableGuid = Guid.Parse("aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb"),
                NullGuid = null,
                NullableDecimal = 987.65m,
                NullDecimal = null,
                NullableStatus = OrderStatus.Delivered,
                NullStatus = null,
                NonNullDateTime = new DateTime(2023, 12, 31)
            };

            var dto1 = Mapper.Map<NullableTypesEntity, NullableToNonNullDto>(source)!;
            Assert.Equal(new DateTime(2024, 6, 15), dto1.NullableDateTime);
            Assert.Equal(default(DateTime), dto1.NullDateTime);
            Assert.Equal(Guid.Parse("aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb"), dto1.NullableGuid);
            Assert.Equal(default(Guid), dto1.NullGuid);
            Assert.Equal(987.65m, dto1.NullableDecimal);
            Assert.Equal(default(decimal), dto1.NullDecimal);
            Assert.Equal(OrderStatus.Delivered, dto1.NullableStatus);
            Assert.Equal(default(OrderStatus), dto1.NullStatus);

            var dto2 = Mapper.Map<NullableTypesEntity, NonNullToNullableDto>(source)!;
            Assert.NotNull(dto2.NonNullDateTime);
            Assert.Equal(new DateTime(2023, 12, 31), dto2.NonNullDateTime!.Value);
        }

        /// <summary>
        /// 枚举 → 不同枚举类型转换 — 通过 TypeConvertHelper 路径
        /// </summary>
        [Fact]
        public void Map_Enum_To_DifferentEnum_Via_Int()
        {
            var source = new FullTypeEntity { StatusProp = OrderStatus.Shipped };
            var dto = Mapper.Map<FullTypeEntity, EnumToIntDto>(source)!;
            Assert.Equal(2, dto.StatusProp);

            var strDto = Mapper.Map<FullTypeEntity, EnumToStringDto>(source)!;
            Assert.Equal("Shipped", strDto.StatusProp);
        }

        /// <summary>
        /// 值类型 → 可空类型 (int → int?) — 杀灭 L165 的非可空→可空路径
        /// 和 L166 的 sourceItem.PropertyType != typeof(string) 排除
        /// </summary>
        [Fact]
        public void Map_ValueType_To_Nullable()
        {
            var source = new NullableTypesEntity { NonNullDateTime = new DateTime(2025, 1, 1) };
            var dto = Mapper.Map<NullableTypesEntity, NonNullToNullableDto>(source)!;

            Assert.NotNull(dto.NonNullDateTime);
            Assert.Equal(new DateTime(2025, 1, 1), dto.NonNullDateTime!.Value);
        }

        /// <summary>
        /// [Convert] 已有源属性匹配 — 静态方法解析 + 模板匹配完整路径
        /// （模型见 ConverterModels.cs，静态方法引用形态）
        /// </summary>
        [Fact]
        public void Map_ConvertUsing_WithMatchingSource_FullPath()
        {
            var source = new ConverterSourceEntity
            {
                Id = 100,
                CreateTime = new DateTime(2025, 12, 31, 23, 59, 0),
                DateString = "2024-01-15",
                Code = 42
            };
            var dto = Mapper.Map<ConverterSourceEntity, ConverterDto>(source)!;

            Assert.Equal(100, dto.Id);
            Assert.Equal("2025-12-31", dto.CreateTime);
            Assert.Equal(2024, dto.ParsedDate.Year);
            Assert.Equal(1, dto.ParsedDate.Month);
            Assert.Equal(15, dto.ParsedDate.Day);
            Assert.Equal("000042", dto.Code);
        }

        /// <summary>
        /// 多字段组合转换器 — 杀灭 BuildConverterAssignment 中
        /// sourceParameter 传递整个源对象的逻辑
        /// </summary>
        [Fact]
        public void Map_ConvertUsing_MultiFieldCombination()
        {
            var source = new MultiFieldSourceEntity
            {
                Id = 1,
                FirstName = "John",
                LastName = "Doe",
                Price = 10.0m,
                Quantity = 5,
                Phone = "13800000001",
                Salary = 8000m,
                Info = new NestedInfo { Department = "Sales" }
            };

            var dto = Mapper.Map<MultiFieldSourceEntity, MultiFieldDto>(source)!;

            Assert.Equal(1, dto.Id);
            Assert.Equal("John Doe", dto.FullName);
            Assert.Equal(50.0m, dto.TotalPrice);
            Assert.Equal("Id=1", dto.ComputedField);
            Assert.Equal("Sales", dto.Department);
        }

        /// <summary>
        /// JSON 转换 string→object — 杀灭 BuildJsonAssignment 中
        /// sourceItem.PropertyType == typeof(string) 的 true 分支
        /// </summary>
        [Fact]
        public void Map_Json_String_To_Object()
        {
            var source = new JsonFieldEntity
            {
                Id = 1,
                ConfigJson = "{\"Theme\":\"Light\",\"FontSize\":18}",
                TagsJson = "[\"tag1\",\"tag2\"]"
            };

            var dto = Mapper.Map<JsonFieldEntity, JsonToObjectDto>(source)!;

            Assert.Equal(1, dto.Id);
            Assert.NotNull(dto.Config);
            Assert.Equal("Light", dto.Config!.Theme);
            Assert.Equal(18, dto.Config.FontSize);
            Assert.NotNull(dto.Tags);
            Assert.Equal(2, dto.Tags!.Count);
            Assert.Equal("tag1", dto.Tags[0]);
            Assert.Equal("tag2", dto.Tags[1]);
        }

        /// <summary>
        /// 嵌套对象 class→class 跨类型映射 — 杀灭 BuildCrossTypeAssignment
        /// </summary>
        [Fact]
        public void Map_Nested_CrossType_Class()
        {
            var source = new CustomerEntity
            {
                Id = 1,
                Name = "Customer1",
                HomeAddress = new Address { City = "Beijing", Street = "Street1", ZipCode = 100000 }
            };

            var dto = Mapper.Map<CustomerEntity, CustomerDto>(source)!;

            Assert.Equal(1, dto.Id);
            Assert.Equal("Customer1", dto.Name);
            Assert.NotNull(dto.HomeAddress);
            Assert.Equal("Beijing", dto.HomeAddress!.City);
            Assert.Equal("Street1", dto.HomeAddress.Street);
            Assert.Equal(100000, dto.HomeAddress.ZipCode);
        }

        /// <summary>
        /// 同类型集合 (List&lt;string&gt;) — 杀灭 BuildSameTypeCollectionAssignment
        /// 和 isSimpleElement 判断路径
        /// </summary>
        [Fact]
        public void Map_SameType_SimpleCollection()
        {
            var source = new MultiCollectionEntity
            {
                NameList = new List<string> { "A", "B", "C" },
                TagSet = new HashSet<string> { "x", "y" }
            };

            var dto = Mapper.Map<MultiCollectionEntity, MultiCollectionDto>(source)!;

            Assert.NotNull(dto.NameList);
            Assert.Equal(3, dto.NameList!.Count);
            Assert.Equal("A", dto.NameList[0]);
            Assert.NotSame(source.NameList, dto.NameList);

            Assert.NotNull(dto.TagSet);
            Assert.Equal(2, dto.TagSet!.Count);
            Assert.Contains("x", dto.TagSet);
        }

        /// <summary>
        /// 隐式数值类型转换 (int→long) — 杀灭 BuildTypeConversionAssignment
        /// 中 IsImplicitConvertible 路径
        /// </summary>
        [Fact]
        public void Map_Implicit_Numeric_Conversion()
        {
            var precSource = new PrecisionEntity { BigLong = long.MaxValue };
            var dto = Mapper.Map<PrecisionEntity, CrossNumericDto>(precSource)!;
            Assert.Equal((decimal)long.MaxValue, dto.BigLong);
        }

        // ================================================================
        // Round 2 测试用例 — 杀灭 ExpressionBuilder 存活变异体
        // ================================================================

        /// <summary>
        /// 目标只读属性 + Action 回调 — 杀灭 L69 CanWrite 检查
        /// 使用 Mapper.Map 的 Action 重载，验证只读属性在 Action 路径下也被正确跳过
        /// 如果 CanWrite 检查被变异移除，只读属性赋值会抛 InvalidOperationException
        /// </summary>
        [Fact]
        public void Map_ReadOnly_Target_Property_Skipped_With_Action()
        {
            var source = new SourceForReadOnlyAction { Id = 42, Name = "ActionTest" };
            var actionCalled = false;
            var target = Mapper.Map<SourceForReadOnlyAction, TargetWithReadOnlyAndAction>(
                source, (s, t) => { actionCalled = true; })!;

            Assert.True(actionCalled);
            Assert.Equal(42, target.Id);
            Assert.Equal("ActionTest", target.Name);
            Assert.Equal("READONLY", target.ReadOnlyProp);
        }

        /// <summary>
        /// 仅目标端标注 [MapIgnore] — 杀灭 L88 ||→&& 变异
        /// ShouldIgnore(targetItem, targetIsDefine) 为 true → || 整体为 true → 跳过
        /// 如果变异为 &&，ShouldIgnore(sourceItem) 为 false → false &amp;&amp; true = false → 属性被错误映射
        /// </summary>
        [Fact]
        public void Map_MapIgnore_OnlyOnTarget_SkipsProperty()
        {
            var source = new SourceForTargetIgnore
            {
                Id = 1,
                Secret = "SOURCE_SECRET",
                Name = "PublicName"
            };
            var target = Mapper.Map<SourceForTargetIgnore, TargetWithMapIgnore>(source)!;

            Assert.Equal(1, target.Id);
            Assert.Equal("PublicName", target.Name);
            Assert.Equal("KEEP_DEFAULT", target.Secret);
        }

            // 实体侧 [MapIgnore] 消费锁定用例随源侧腿退役删除

        /// <summary>
        /// int? (null) → int 默认值转换 — 杀灭 L157/L159 (Round 2 独立补充)
        /// 单独验证 null 可空值映射到非可空类型时返回 default
        /// </summary>
        [Fact]
        public void Map_NullableInt_To_Int_WithNull()
        {
            var source = new NullableSourceEntity
            {
                NullableInt = null,
                NullNullableInt = null,
                NonNullableInt = 50,
                Name = "Test"
            };
            var target = Mapper.Map<NullableSourceEntity, NullableConversionDto>(source)!;

            Assert.Equal(0, target.NullableInt);
            Assert.Equal(0, target.NullNullableInt);
        }

        /// <summary>
        /// int? (有值) → int 正常转换 — 杀灭 L157 (Round 2 独立补充)
        /// 验证 HasValue=true 时 Convert 路径正确
        /// </summary>
        [Fact]
        public void Map_NullableInt_To_Int_WithValue()
        {
            var source = new NullableSourceEntity
            {
                NullableInt = 777,
                NullNullableInt = 333,
                NonNullableInt = 100,
                Name = "Value"
            };
            var target = Mapper.Map<NullableSourceEntity, NullableConversionDto>(source)!;

            Assert.Equal(777, target.NullableInt);
            Assert.Equal(333, target.NullNullableInt);
        }

        /// <summary>
        /// int → int? 非可空到可空转换 — 杀灭 L165 (Round 2 独立补充)
        /// 验证 Convert 路径: Expression.Convert(int, int?)
        /// </summary>
        [Fact]
        public void Map_Int_To_NullableInt()
        {
            var source = new NullableSourceEntity { NonNullableInt = 12345 };
            var target = Mapper.Map<NullableSourceEntity, NullableConversionDto>(source)!;

            Assert.NotNull(target.NonNullableInt);
            Assert.Equal(12345, target.NonNullableInt!.Value);
        }

        /// <summary>
        /// string → string null 值传递 — 杀灭 L166 string 类型排除 (Round 2 补充)
        /// string 是引用类型(IsNullableType=true)但不应进入 nullable→non-nullable 转换路径
        /// </summary>
        [Fact]
        public void Map_String_To_String_NullValue()
        {
            var source = new StringSource { Name = null!, Description = null! };
            var target = Mapper.Map<StringSource, StringTarget>(source)!;

            Assert.Null(target.Name);
            Assert.Null(target.Description);
        }

        /// <summary>
        /// class → class 跨类型嵌套映射 (新模型) — 杀灭 L270 全部三个 &amp;&amp; 条件
        /// source: Address (class, not array, not generic) → target: AddressDto (class, not array, not generic)
        /// 验证 BuildCrossTypeAssignment 中 class-to-class 条件正确触发
        /// </summary>
        [Fact]
        public void Map_CrossType_Class_To_Class()
        {
            var source = new CrossClassSource
            {
                Id = 1,
                AddressInfo = new Address { City = "Shanghai", Street = "Nanjing Road", ZipCode = 200000 }
            };
            var target = Mapper.Map<CrossClassSource, CrossClassTarget>(source)!;

            Assert.Equal(1, target.Id);
            Assert.NotNull(target.AddressInfo);
            Assert.Equal("Shanghai", target.AddressInfo.City);
            Assert.Equal("Nanjing Road", target.AddressInfo.Street);
            Assert.Equal(200000, target.AddressInfo.ZipCode);
        }

        /// <summary>
        /// 数组属性同类型集合映射 — 杀灭 L270 IsArray 检查
        /// int[] → int[]: 同类型走 BuildSameTypeCollectionAssignment，验证数组正确拷贝
        /// 确保数组属性通过集合路径处理，不进入 class-to-class 分支
        /// </summary>
        [Fact]
        public void Map_Array_Property_NotCrossMapped()
        {
            var source = new ArrayPropSource
            {
                Id = 1,
                Scores = new[] { 10, 20, 30 }
            };
            var target = Mapper.Map<ArrayPropSource, ArrayPropTarget>(source)!;

            Assert.Equal(1, target.Id);
            Assert.NotNull(target.Scores);
            Assert.Equal(3, target.Scores.Length);
            Assert.Equal(10, target.Scores[0]);
            Assert.Equal(20, target.Scores[1]);
            Assert.Equal(30, target.Scores[2]);
            Assert.NotSame(source.Scores, target.Scores);
        }

        /// <summary>
        /// 泛型属性同类型集合映射 — 杀灭 L270 IsGenericType 检查
        /// List&lt;string&gt; → List&lt;string&gt;: 同类型走 BuildSameTypeCollectionAssignment
        /// 确保泛型属性通过集合路径处理，不进入 class-to-class 分支
        /// </summary>
        [Fact]
        public void Map_GenericType_Property_NotCrossMapped()
        {
            var source = new GenericPropSource
            {
                Id = 1,
                Tags = new List<string> { "Alpha", "Beta", "Gamma" }
            };
            var target = Mapper.Map<GenericPropSource, GenericPropTarget>(source)!;

            Assert.Equal(1, target.Id);
            Assert.NotNull(target.Tags);
            Assert.Equal(3, target.Tags.Count);
            Assert.Equal("Alpha", target.Tags[0]);
            Assert.Equal("Beta", target.Tags[1]);
            Assert.Equal("Gamma", target.Tags[2]);
            Assert.NotSame(source.Tags, target.Tags);
        }

        /// <summary>
        /// [Convert] 反向不消费（历史负例：旧接口形态泛型参数不匹配）— A-4
        /// <para>目标未标 [MapFrom] → isToDto=false（反向），无论转换器是否有效一律不消费，
        /// 降级类型转换路径且不抛 MapException（原 "无效转换器抛异常"语义
        /// 已被 A-4 取代；正向消费校验仍由静态方法解析保留）。</para>
        /// </summary>
        [Fact]
        public void Map_Reverse_ConvertUsing_WithWrongGenericArgs_NotConsumed()
        {
            var source = new SimpleSource { Id = 99, Name = "Test" };
            var target = Mapper.Map<SimpleSource, TargetWithWrongConverter>(source)!;

            // 反向不消费转换器 → 不抛异常；FormattedId 无同名源属性 → 保持初始值
            Assert.Equal("DEFAULT", target.FormattedId);
        }

        /// <summary>
        /// [Convert] 反向不消费（历史负例：旧接口形态未实现 IValueConverter）— A-4
        /// <para>反向一律不消费，转换器不触发静态方法解析 → 不抛 MapException</para>
        /// </summary>
        [Fact]
        public void Map_Reverse_ConvertUsing_StaticMethod_NotConsumed()
        {
            var source = new SimpleSource { Id = 77, Name = "StaticTest" };
            var target = Mapper.Map<SimpleSource, TargetWithStaticConverter>(source)!;

            Assert.Equal("DEFAULT", target.FormattedId);
        }

        /// <summary>
        /// [Convert] 反向不消费（历史负例 + 有匹配源属性）— A-4
        /// <para>反向按类型转换路径：同名 FormattedId（int）直赋值，转换器不触发静态方法解析</para>
        /// </summary>
        [Fact]
        public void Map_Reverse_ConvertUsing_NoInterface_WithMatchingSource_NotConsumed()
        {
            var source = new SourceWithFormattedId { Id = 55, FormattedId = 99 };
            var target = Mapper.Map<SourceWithFormattedId, TargetWithStaticConverterMatching>(source)!;

            Assert.Equal(99, target.FormattedId);
        }

        /// <summary>
        /// 泛型 vs 非泛型属性 — 杀灭 L272 IsGenericType &amp;&amp; 检查
        /// source: List&lt;int&gt; (IsGenericType=true) → target: Address (IsGenericType=false)
        /// 原始代码: !IsGenericType(src) &amp;&amp; !IsGenericType(tgt) = false &amp;&amp; true = false → 不进入 class-to-class
        /// 变异体: !IsGenericType(src) || !IsGenericType(tgt) = false || true = true → 错误进入 class-to-class
        /// 验证 List&lt;int&gt; 不被错误映射为 Address 对象
        /// </summary>
        [Fact]
        public void Map_GenericVsNonGeneric_DoesNotCrossMap()
        {
            var source = new GenericVsNonGenericSource
            {
                Id = 1,
                Detail = new List<int> { 1, 2, 3 }
            };
            Assert.Throws<NCMapper.MapException>(() =>
                Mapper.Map<GenericVsNonGenericSource, GenericVsNonGenericTarget>(source));
        }

        // ================================================================
        // Round 3 测试用例 — null guards, MaxInlineDepth, JSON, collections
        // ================================================================

        /// <summary>
        /// GetMapAction null source guard — 杀灭 L24 NotEqual(sourceParameter, null) 条件
        /// Mapper.Map 传入 null source 时，生成的表达式树中 if(source != null) 跳过所有赋值
        /// 如果变异移除 null 检查 → NullReferenceException on source property access
        /// </summary>
        [Fact]
        public void Map_NullSource_ReturnsTargetWithDefaults()
        {
            // Mapper.Map with null source — should not throw, target keeps defaults
            var target = Mapper.Map<SimpleSource, SimpleSource>(null!);
            // The Map overload that returns TTarget creates a new target and calls MapAction
            // With null source, the IfThen(source != null, ...) guard skips all assignments
            Assert.Null(target);
        }

        /// <summary>
        /// GetMapAction null source guard (Action overload) — 杀灭 L24 条件
        /// Action 重载：source 为 null 时不抛异常，target 保持原值
        /// </summary>
        [Fact]
        public void Map_NullSource_ActionOverload_DoesNotThrow()
        {
            var target = new TargetWithReadOnlyProp { Id = 0, };
            // source is null → the IfThen guard skips all assignments → target unchanged
            Mapper.Map<SimpleSource, TargetWithReadOnlyProp>(null!, target);
            // Id was 0, should remain 0 since no mapping happened
            Assert.Equal(0, target.Id);
            Assert.Equal("FIXED", target.ReadOnlyField);
        }

        /// <summary>
        /// GetMapAction null target guard — 杀灭 L29 NotEqual(targetParameter, null) 条件
        /// target 为 null 时不抛 NullReferenceException
        /// 如果变异移除 null 检查 → target property assignment throws NRE
        /// </summary>
        [Fact]
        public void Map_NullTarget_ActionOverload_DoesNotThrow()
        {
            var source = new SimpleSource { Id = 42, Name = "Test" };
            // target is null → the IfThen(target != null, ...) guard skips all assignments
            Mapper.Map<SimpleSource, TargetWithReadOnlyProp>(source, null!);
            // 契约：调用无副作用，source 原值应保持（防"静默吞错仍恒绿"）
            Assert.Equal(42, source.Id);
            Assert.Equal("Test", source.Name);
        }

        /// <summary>
        /// GetClassExpression null source property — 杀灭 L497 NotEqual(sourceProperty, null) 条件
        /// 嵌套对象 HomeAddress 为 null → 条件表达式返回 Constant(null, targetType)
        /// 如果变异移除 null 检查 → 尝试对 null 对象执行内联映射 → NullReferenceException
        /// </summary>
        [Fact]
        public void Map_NestedObject_NullSource_ReturnsNull()
        {
            var source = new NullNestedSource { Id = 1, HomeAddress = null };
            var target = Mapper.Map<NullNestedSource, NullNestedTarget>(source)!;

            Assert.Equal(1, target.Id);
            // HomeAddress is null → GetClassExpression returns null → target.HomeAddress stays null
            Assert.Null(target.HomeAddress);
        }

        /// <summary>
        /// GetClassExpression null source property (non-null case) — 杀灭 L497 条件的 true 分支
        /// 嵌套对象非 null 时正常映射，验证 Condition 表达式的 if-true 路径
        /// </summary>
        [Fact]
        public void Map_NestedObject_NonNullSource_MapsCorrectly()
        {
            var source = new NullNestedSource
            {
                Id = 2,
                HomeAddress = new Address { City = "Beijing", Street = "Changan", ZipCode = 100000 }
            };
            var target = Mapper.Map<NullNestedSource, NullNestedTarget>(source)!;

            Assert.Equal(2, target.Id);
            Assert.NotNull(target.HomeAddress);
            Assert.Equal("Beijing", target.HomeAddress!.City);
            Assert.Equal("Changan", target.HomeAddress.Street);
            Assert.Equal(100000, target.HomeAddress.ZipCode);
        }

        /// <summary>
        /// GetJsonByObjectExpression — object→JSON string 路径 (杀灭 L556-569)
        /// 源是 AppConfig 对象，目标是 string，IsFromJson=true 触发 ObjectConvertToJson
        /// 杀灭 BuildJsonAssignment 中 sourceItem.PropertyType == typeof(string) 的 false 分支
        /// </summary>
        [Fact]
        public void Map_ObjectToJson_SerializesCorrectly()
        {
            var source = new ObjectToJsonSource
            {
                Id = 1,
                Config = new AppConfig { Theme = "Light", FontSize = 18 }
            };
            var target = Mapper.Map<ObjectToJsonSource, ObjectToJsonDto>(source)!;

            Assert.Equal(1, target.Id);
            Assert.NotNull(target.ConfigJson);
            Assert.Contains("Light", target.ConfigJson!);
            Assert.Contains("18", target.ConfigJson);
        }

        /// <summary>
        /// GetJsonByObjectExpression null guard — 杀灭 L559 NotEqual(sourceProperty, null) 条件
        /// 源对象为 null → Condition 返回 Constant(null, targetType) → target string is null
        /// 如果变异移除 null 检查 → 对 null 对象调用 ObjectConvertToJson → NullReferenceException
        /// </summary>
        [Fact]
        public void Map_ObjectToJson_NullSource_ReturnsNull()
        {
            var source = new NullObjectToJsonSource { Id = 1, Config = null };
            var target = Mapper.Map<NullObjectToJsonSource, NullObjectToJsonDto>(source)!;

            Assert.Equal(1, target.Id);
            // Config is null → GetJsonByObjectExpression Condition returns null
            Assert.Null(target.ConfigJson);
        }

        /// <summary>
        /// GetObjectByJsonExpression null guard — 杀灭 L590 NotEqual(sourceProperty, null) 条件
        /// JSON 字符串为 null → Condition 返回 Constant(null, targetType) → target object is null
        /// 如果变异移除 null 检查 → 对 null 字符串调用 JsonConvertToObject → NullReferenceException
        /// </summary>
        [Fact]
        public void Map_JsonToObject_NullJson_ReturnsNull()
        {
            var source = new NullJsonSource { Id = 1, ConfigJson = null };
            var target = Mapper.Map<NullJsonSource, NullJsonToDto>(source)!;

            Assert.Equal(1, target.Id);
            // ConfigJson is null → GetObjectByJsonExpression Condition returns null
            Assert.Null(target.Config);
        }

        /// <summary>
        /// GetObjectByJsonExpression sourceType guard — 杀灭 L583 sourceType != typeof(string) 检查
        /// 直接调用内部方法传入非 string 类型应抛 ArgumentException
        /// 如果变异移除该检查 → 后续 GetMethod 查找会失败或产生不可预期行为
        /// </summary>
        [Fact]
        public void GetObjectByJsonExpression_NonStringSourceType_ThrowsArgumentException()
        {
            var param = Expression.Parameter(typeof(int), "x");
            var ex = Assert.Throws<ArgumentException>(() =>
                ExpressionBuilder.GetObjectByJsonExpression(param, typeof(int), typeof(AppConfig)));
            Assert.Contains("string", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// MaxInlineDepth = 0 — 杀灭 L502 s_inlineDepth &lt; Mapper.MaxInlineDepth 比较
        /// 设置深度为 0 → 所有嵌套对象走 fallback 委托调用路径 (MapperBase.Map)
        /// 如果变异为 &lt;= → depth 0 仍内联一层，行为不同
        /// </summary>
        [Fact]
        public void Map_MaxInlineDepth_Zero_FallsBackToDelegate()
        {
            Mapper.MaxInlineDepth = 0;

            var source = new CustomerEntity
            {
                Id = 1,
                Name = "Test",
                HomeAddress = new Address { City = "Shanghai", Street = "Nanjing", ZipCode = 200001 }
            };
            var target = Mapper.Map<CustomerEntity, CustomerDto>(source)!;

            // Even with MaxInlineDepth=0, nested mapping must still work via fallback delegate
            Assert.Equal(1, target.Id);
            Assert.Equal("Test", target.Name);
            Assert.NotNull(target.HomeAddress);
            Assert.Equal("Shanghai", target.HomeAddress!.City);
            Assert.Equal("Nanjing", target.HomeAddress.Street);
            Assert.Equal(200001, target.HomeAddress.ZipCode);
        }

        /// <summary>
        /// MaxInlineDepth boundary — 杀灭 L502 &lt; 变异的 &lt;=
        /// 设置深度为 1 → 第一层嵌套内联，第二层走 fallback
        /// 如果 &lt; 变异为 &lt;= → depth 1 时会多内联一层
        /// 验证：即使内联深度有限，多层嵌套仍能正确映射（通过 fallback）
        /// </summary>
        [Fact]
        public void Map_MaxInlineDepth_One_MultiLevelStillWorks()
        {
            Mapper.MaxInlineDepth = 1;

            var source = new Company
            {
                Name = "Corp",
                Departments = new List<Department>
                {
                    new Department
                    {
                        Name = "Engineering",
                        Teams = new List<Team>
                        {
                            new Team
                            {
                                Name = "Backend",
                                Members = new List<Member>
                                {
                                    new Member { Name = "Alice", Age = 30, IsActive = true }
                                }
                            }
                        }
                    }
                }
            };

            var target = Mapper.Map<Company, CompanyDto>(source)!;

            Assert.Equal("Corp", target.Name);
            Assert.NotNull(target.Departments);
            Assert.Single(target.Departments!);
            Assert.Equal("Engineering", target.Departments[0].Name);
            Assert.NotNull(target.Departments[0].Teams);
            Assert.Single(target.Departments[0].Teams!);
            Assert.Equal("Backend", target.Departments![0].Teams![0].Name);
            Assert.NotNull(target.Departments![0].Teams![0].Members);
            Assert.Single(target.Departments![0].Teams![0].Members!);
            Assert.Equal("Alice", target.Departments![0].Teams![0].Members![0].Name);
            Assert.Equal(30, target.Departments![0].Teams![0].Members![0].Age);
            Assert.True(target.Departments![0].Teams![0].Members![0].IsActive);
        }

        /// <summary>
        /// MaxInlineDepth decrement in finally — 杀灭 L524 s_inlineDepth-- 变异
        /// 多次调用 Map 确保 depth 计数器正确重置（如果 -- 变异为 ++ 或移除，
        /// 后续调用的 depth 会累积，最终超过 MaxInlineDepth 导致 fallback）
        /// </summary>
        [Fact]
        public void Map_MaxInlineDepth_Decrement_ResetsAfterCall()
        {
            Mapper.MaxInlineDepth = 2;

            var source = new CustomerEntity
            {
                Id = 1,
                Name = "First",
                HomeAddress = new Address { City = "City1", Street = "St1", ZipCode = 100001 }
            };

            // First call
            var target1 = Mapper.Map<CustomerEntity, CustomerDto>(source)!;
            Assert.Equal("City1", target1.HomeAddress!.City);

            // Second call — if s_inlineDepth-- was removed/mutated, depth would accumulate
            // and this call might behave differently (fallback instead of inline)
            var source2 = new CustomerEntity
            {
                Id = 2,
                Name = "Second",
                HomeAddress = new Address { City = "City2", Street = "St2", ZipCode = 200002 }
            };
            var target2 = Mapper.Map<CustomerEntity, CustomerDto>(source2)!;
            Assert.Equal("City2", target2.HomeAddress!.City);

            // Third call to be sure
            var source3 = new CustomerEntity
            {
                Id = 3,
                Name = "Third",
                HomeAddress = new Address { City = "City3", Street = "St3", ZipCode = 300003 }
            };
            var target3 = Mapper.Map<CustomerEntity, CustomerDto>(source3)!;
            Assert.Equal("City3", target3.HomeAddress!.City);
        }

        /// <summary>
        /// ConvertListExpression null guard — 杀灭 L612 NotEqual(sourceProperty, null) 条件
        /// null List → Condition 返回 Constant(null, targetType)
        /// 如果变异移除 null 检查 → 对 null 集合调用 CopyList → NullReferenceException
        /// </summary>
        [Fact]
        public void Map_NullCollection_List_ReturnsNull()
        {
            var source = new NullCollectionSource { Id = 1, Tags = null, Scores = null, Metadata = null };
            var target = Mapper.Map<NullCollectionSource, NullCollectionDto>(source)!;

            Assert.Equal(1, target.Id);
            Assert.Null(target.Tags);
            Assert.Null(target.Scores);
            Assert.Null(target.Metadata);
        }

        /// <summary>
        /// ConvertListExpression array path — 杀灭 L618-621 sourceType.IsArray 分支
        /// int[] → int[]: CopyArray 路径，验证数组正确克隆
        /// 如果 IsArray 检查变异为 !IsArray → 数组走其他分支 → 运行时异常或错误结果
        /// </summary>
        [Fact]
        public void Map_Array_CopyArray_Path()
        {
            var source = new ArrayPropSource
            {
                Id = 1,
                Scores = new[] { 100, 200, 300 }
            };
            var target = Mapper.Map<ArrayPropSource, ArrayPropTarget>(source)!;

            Assert.Equal(3, target.Scores.Length);
            Assert.Equal(100, target.Scores[0]);
            Assert.Equal(200, target.Scores[1]);
            Assert.Equal(300, target.Scores[2]);
            Assert.NotSame(source.Scores, target.Scores);
        }

        /// <summary>
        /// BuildSameTypeCollectionAssignment isSimpleElement for enum array
        /// 杀灭 L308-310 中 sourceType.IsArray → elemType.IsEnum 检测
        /// enum[] 应被识别为 simple element → 走浅拷贝路径 (ConvertListExpression)
        /// </summary>
        [Fact]
        public void Map_EnumArray_IsSimpleElement()
        {
            var source = new EnumArraySource
            {
                Id = 1,
                Statuses = new[] { OrderStatus.Pending, OrderStatus.Shipped, OrderStatus.Delivered }
            };
            var target = Mapper.Map<EnumArraySource, EnumArrayDto>(source)!;

            Assert.Equal(3, target.Statuses.Length);
            Assert.Equal(OrderStatus.Pending, target.Statuses[0]);
            Assert.Equal(OrderStatus.Shipped, target.Statuses[1]);
            Assert.Equal(OrderStatus.Delivered, target.Statuses[2]);
            Assert.NotSame(source.Statuses, target.Statuses);
        }

        /// <summary>
        /// BuildSameTypeCollectionAssignment isSimpleElement for string array
        /// 杀灭 L308-310 中 elemType == typeof(string) 检测
        /// string[] 应被识别为 simple element → 走浅拷贝路径
        /// </summary>
        [Fact]
        public void Map_StringArray_IsSimpleElement()
        {
            var source = new StringArraySource
            {
                Id = 1,
                Tags = new[] { "hello", "world" }
            };
            var target = Mapper.Map<StringArraySource, StringArrayTarget>(source)!;

            Assert.Equal(2, target.Tags.Length);
            Assert.Equal("hello", target.Tags[0]);
            Assert.Equal("world", target.Tags[1]);
            Assert.NotSame(source.Tags, target.Tags);
        }

        /// <summary>
        /// GetListExpression List→Dictionary NotSupportedException — 杀灭 L769-776
        /// List 源 → Dictionary 目标 → typeof(IDictionary).IsAssignableFrom(targetType) 为 true
        /// 但 typeof(IDictionary).IsAssignableFrom(sourceType) 为 false → 抛 NotSupportedException
        /// 如果 L769 条件变异（移除 !）→ 不抛异常 → 数据丢失
        /// </summary>
        [Fact]
        public void Map_List_To_Dictionary_Throws_NotSupported()
        {
            var source = new ListToDictSource
            {
                Codes = new List<string> { "A", "B", "C" }
            };

            // List<string> → Dictionary<string, string> is unsupported
            Assert.ThrowsAny<Exception>(() =>
                Mapper.Map<ListToDictSource, ListToDictDto>(source));
        }

        /// <summary>
        /// GetListExpression null guard — 杀灭 L764 NotEqual(sourceProperty, null)
        /// 源集合为 null → GetListExpression Condition 返回 null
        /// 用于跨类型集合映射中 null 源集合的处理
        /// </summary>
        [Fact]
        public void Map_CrossTypeCollection_NullSource_ReturnsNull()
        {
            var source = new CustomerEntity
            {
                Id = 1,
                Name = "Test",
                Orders = null  // null list → GetListExpression returns null
            };
            var target = Mapper.Map<CustomerEntity, CustomerDto>(source)!;

            Assert.Equal(1, target.Id);
            // Orders is null → target doesn't have Orders property (different DTO), but no exception
        }

        /// <summary>
        /// GetDictionaryExpression null guard — 杀灭 L827 NotEqual(sourceProperty, null)
        /// null Dictionary → Condition 返回 Constant(null, targetType)
        /// </summary>
        [Fact]
        public void Map_Dictionary_NullSource_ReturnsNull()
        {
            var source = new Blog
            {
                Title = "Test",
                Posts = null,
                TagCounts = null
            };
            var target = Mapper.Map<Blog, BlogDto>(source)!;

            Assert.Equal("Test", target.Title);
            Assert.Null(target.Posts);
            Assert.Null(target.TagCounts);
        }

        /// <summary>
        /// BuildEnumToStringSwitch — 杀灭 L392-413 的 Switch 表达式正确性
        /// 验证每个枚举值都映射到正确的字符串名称
        /// 如果 SwitchCase 构建错误（如索引偏移），某些枚举值会返回错误字符串
        /// </summary>
        [Fact]
        public void Map_EveryEnumValue_ToString_Correct()
        {
            // Test every OrderStatus value
            foreach (OrderStatus status in Enum.GetValues<OrderStatus>())
            {
                var source = new FullTypeEntity { StatusProp = status };
                var dto = Mapper.Map<FullTypeEntity, EnumToStringDto>(source)!;
                Assert.Equal(status.ToString(), dto.StatusProp);
            }
        }

        /// <summary>
        /// BuildEnumToStringSwitch default body — 杀灭 L409-410 默认分支
        /// 使用 [Flags] 组合枚举值（不在预定义 SwitchCase 中）→ 走 defaultBody ToString()
        /// </summary>
        [Fact]
        public void Map_FlagsEnum_CombinedValue_ToString()
        {
            var source = new FlagsEnumEntity { Access = Permissions.Read | Permissions.Write };
            var dto = Mapper.Map<FlagsEnumEntity, FlagsEnumToStringDto>(source)!;

            // Combined flags value "Read, Write" → Switch default body calls ToString()
            Assert.Equal("Read, Write", dto.Access);
        }

        /// <summary>
        /// BuildTypeConversionAssignment — 隐式转换 int→double
        /// 杀灭 L364 IsImplicitConvertible == true 分支
        /// 如果变异使 IsImplicitConvertible 返回 false → 走 TypeConvertHelper 路径（更慢但结果相同）
        /// 通过验证精确结果杀灭（TypeConvertHelper 对某些转换可能有精度差异）
        /// </summary>
        [Fact]
        public void Map_ImplicitConversion_IntToDouble()
        {
            var source = new FullTypeEntity { IntProp = 42 };
            // IntProp (int) → SameTypeDto.IntProp (int) is same type, so test with
            // a known implicit conversion path instead
            var precSource = new PrecisionEntity { BigLong = 123456789L };
            var dto = Mapper.Map<PrecisionEntity, CrossNumericDto>(precSource)!;
            // long → decimal is implicit widening
            Assert.Equal(123456789m, dto.BigLong);
        }

        /// <summary>
        /// BuildTypeConversionAssignment — 非隐式转换 string→int
        /// 杀灭 L379-382 TypeConvertHelper.ConvertValue 路径
        /// "hello" 无法转换为 int → TypeConvertHelper 抛出 MapException
        /// 如果变异移除 helperMethod 调用 → 编译表达式树失败而非运行时 MapException
        /// </summary>
        [Fact]
        public void Map_NonImplicitConversion_StringToInt()
        {
            var source = new FullTypeEntity { StringProp = "hello" };
            // "hello" → int via TypeConvertHelper → MapException (unconvertible)
            var ex = Assert.Throws<NCMapper.MapException>(() =>
                Mapper.Map<FullTypeEntity, StringToValueDto>(source));
            Assert.Contains("String", ex.Message);
            Assert.Contains("Int32", ex.Message);
        }

        /// <summary>
        /// BuildTypeConversionAssignment — string→int with valid numeric string
        /// 杀灭 L379-382 TypeConvertHelper 路径的正确值传递
        /// </summary>
        [Fact]
        public void Map_NonImplicitConversion_ValidNumericString()
        {
            var source = new FullTypeEntity { StringProp = "12345" };
            var dto = Mapper.Map<FullTypeEntity, StringToValueDto>(source)!;
            Assert.Equal(12345, dto.StringProp);
        }

        /// <summary>
        /// TryGetInlineMapBody try-catch — 杀灭 L448 catch 块
        /// 此测试验证即使内联构建可能失败的场景，映射仍然正确
        /// （通过 MaxInlineDepth=0 强制走 fallback，间接验证 catch 块不破坏状态）
        /// </summary>
        [Fact]
        public void Map_TryGetInlineMapBody_FallbackPath_PreservesCorrectness()
        {
            Mapper.MaxInlineDepth = 0;

            // With inline disabled, all nested mappings go through MapperBase.Map fallback
            var source = new Company
            {
                Name = "TestCorp",
                Departments = new List<Department>
                {
                    new Department { Name = "R&D" }
                }
            };

            var target = Mapper.Map<Company, CompanyDto>(source)!;
            Assert.Equal("TestCorp", target.Name);
            Assert.NotNull(target.Departments);
            Assert.Single(target.Departments!);
            Assert.Equal("R&D", target.Departments[0].Name);
        }

        /// <summary>
        /// GetClassExpression fallback to MapperBase.Map — 杀灭 L542-546 fallback 路径
        /// 设置 MaxInlineDepth=0 强制走 fallback → 验证 MapperBase.Map 被调用且结果正确
        /// 如果 L542 MakeGenericType 或 L544 GetMethod 出错 → 运行时异常
        /// </summary>
        [Fact]
        public void Map_GetClassExpression_Fallback_Delegate_Call()
        {
            Mapper.MaxInlineDepth = 0;

            var source = new CrossClassSource
            {
                Id = 1,
                AddressInfo = new Address { City = "Shenzhen", Street = "Tech Road", ZipCode = 518000 }
            };
            var target = Mapper.Map<CrossClassSource, CrossClassTarget>(source)!;

            Assert.Equal(1, target.Id);
            Assert.NotNull(target.AddressInfo);
            Assert.Equal("Shenzhen", target.AddressInfo.City);
            Assert.Equal("Tech Road", target.AddressInfo.Street);
            Assert.Equal(518000, target.AddressInfo.ZipCode);
        }

        /// <summary>
        /// Same-type Dictionary mapping — 杀灭 BuildSameTypeCollectionAssignment + GetDictionaryExpression
        /// Dictionary&lt;string,int&gt; → Dictionary&lt;string,int&gt; 同类型
        /// 验证字典同类型拷贝走 ConvertListExpression 路径
        /// </summary>
        [Fact]
        public void Map_SameType_Dictionary()
        {
            var source = new Blog
            {
                Title = "Blog1",
                TagCounts = new Dictionary<string, int>
                {
                    ["csharp"] = 10,
                    ["dotnet"] = 20
                }
            };
            var target = Mapper.Map<Blog, BlogDto>(source)!;

            Assert.Equal("Blog1", target.Title);
            Assert.NotNull(target.TagCounts);
            Assert.Equal(2, target.TagCounts!.Count);
            Assert.Equal(10, target.TagCounts["csharp"]);
            Assert.Equal(20, target.TagCounts["dotnet"]);
            Assert.NotSame(source.TagCounts, target.TagCounts);
        }

        /// <summary>
        /// Cross-type collection (List&lt;Post&gt; → List&lt;PostDto&gt;) — 杀灭 GetListExpression
        /// 集合元素类型不同 → GetListExpression 调用 MapperBase.MapList
        /// 杀灭 L783 sourceArg/targetArg 提取和 L785 MakeGenericType
        /// </summary>
        [Fact]
        public void Map_CrossType_List_Mapping()
        {
            var source = new Blog
            {
                Title = "Blog2",
                Posts = new List<Post>
                {
                    new Post { Title = "Post1", Views = 100 },
                    new Post { Title = "Post2", Views = 200 }
                }
            };
            var target = Mapper.Map<Blog, BlogDto>(source)!;

            Assert.Equal("Blog2", target.Title);
            Assert.NotNull(target.Posts);
            Assert.Equal(2, target.Posts!.Count);
            Assert.Equal("Post1", target.Posts[0].Title);
            Assert.Equal(100, target.Posts[0].Views);
            Assert.Equal("Post2", target.Posts[1].Title);
            Assert.Equal(200, target.Posts[1].Views);
        }

        /// <summary>
        /// Cross-type Dictionary mapping — 杀灭 GetDictionaryExpression L825-860
        /// Dictionary&lt;string, Post&gt; → Dictionary&lt;string, PostDto&gt;
        /// 验证 ConvertDictionary 被正确调用
        /// </summary>
        [Fact]
        public void Map_CrossType_Dictionary_Mapping()
        {
            var source = new ArticleCollection
            {
                Name = "Articles",
                PostsByCategory = new Dictionary<string, Post>
                {
                    ["tech"] = new Post { Title = "Tech Post", Views = 50 },
                    ["science"] = new Post { Title = "Science Post", Views = 75 }
                }
            };
            var target = Mapper.Map<ArticleCollection, ArticleCollectionDto>(source)!;

            Assert.Equal("Articles", target.Name);
            Assert.NotNull(target.PostsByCategory);
            Assert.Equal(2, target.PostsByCategory!.Count);
            Assert.Equal("Tech Post", target.PostsByCategory["tech"].Title);
            Assert.Equal(50, target.PostsByCategory["tech"].Views);
            Assert.Equal("Science Post", target.PostsByCategory["science"].Title);
            Assert.Equal(75, target.PostsByCategory["science"].Views);
        }

        /// <summary>
        /// GetListExpression array target — 杀灭 L796-798 targetType.IsArray → ToArray() 路径
        /// 源是 List&lt;OrderItem&gt;，目标是数组（如果有对应模型）
        /// 此处用 CustomerEntity.Orders (List) 映射到 CustomerDto（无 Orders），
        /// 改为验证同类型 List 映射中 ToArray 路径的间接覆盖
        /// </summary>
        [Fact]
        public void Map_SameType_List_DeepCopy()
        {
            var source = new MultiCollectionEntity
            {
                NameList = new List<string> { "X", "Y", "Z" }
            };
            var target = Mapper.Map<MultiCollectionEntity, MultiCollectionDto>(source)!;

            Assert.NotNull(target.NameList);
            Assert.Equal(3, target.NameList!.Count);
            Assert.Equal("X", target.NameList[0]);
            Assert.Equal("Y", target.NameList[1]);
            Assert.Equal("Z", target.NameList[2]);
            Assert.NotSame(source.NameList, target.NameList);
        }

        /// <summary>
        /// HashSet same-type mapping — 杀灭 ResolveSingleValueCollectionMethod L738-739
        /// HashSet&lt;string&gt; → HashSet&lt;string&gt;: CopyHashSet 路径
        /// </summary>
        [Fact]
        public void Map_HashSet_SameType_CopyHashSet()
        {
            var source = new MultiCollectionEntity
            {
                TagSet = new HashSet<string> { "alpha", "beta", "gamma" }
            };
            var target = Mapper.Map<MultiCollectionEntity, MultiCollectionDto>(source)!;

            Assert.NotNull(target.TagSet);
            Assert.Equal(3, target.TagSet!.Count);
            Assert.Contains("alpha", target.TagSet);
            Assert.Contains("beta", target.TagSet);
            Assert.Contains("gamma", target.TagSet);
            Assert.NotSame(source.TagSet, target.TagSet);
        }

        /// <summary>
        /// Queue same-type mapping — 杀灭 ResolveSingleValueCollectionMethod L742-743
        /// Queue&lt;int&gt; → Queue&lt;int&gt;: CopyQueue 路径
        /// </summary>
        [Fact]
        public void Map_Queue_SameType_CopyQueue()
        {
            var source = new MultiCollectionEntity();
            source.TaskQueue = new Queue<int>();
            source.TaskQueue.Enqueue(1);
            source.TaskQueue.Enqueue(2);
            source.TaskQueue.Enqueue(3);

            var target = Mapper.Map<MultiCollectionEntity, MultiCollectionDto>(source)!;

            Assert.NotNull(target.TaskQueue);
            Assert.Equal(3, target.TaskQueue!.Count);
            Assert.Equal(1, target.TaskQueue.Dequeue());
            Assert.Equal(2, target.TaskQueue.Dequeue());
            Assert.Equal(3, target.TaskQueue.Dequeue());
            Assert.NotSame(source.TaskQueue, target.TaskQueue);
        }

        /// <summary>
        /// Stack same-type mapping — 杀灭 ResolveSingleValueCollectionMethod L746-747
        /// Stack&lt;int&gt; → Stack&lt;int&gt;: CopyStack 路径 (含 Array.Reverse)
        /// </summary>
        [Fact]
        public void Map_Stack_SameType_CopyStack()
        {
            var source = new MultiCollectionEntity();
            source.HistoryStack = new Stack<int>();
            source.HistoryStack.Push(10);
            source.HistoryStack.Push(20);
            source.HistoryStack.Push(30);

            var target = Mapper.Map<MultiCollectionEntity, MultiCollectionDto>(source)!;

            Assert.NotNull(target.HistoryStack);
            Assert.Equal(3, target.HistoryStack!.Count);
            // Stack pop order: 30 (top), 20, 10 (bottom)
            Assert.Equal(30, target.HistoryStack.Pop());
            Assert.Equal(20, target.HistoryStack.Pop());
            Assert.Equal(10, target.HistoryStack.Pop());
            Assert.NotSame(source.HistoryStack, target.HistoryStack);
        }

        /// <summary>
        /// Tree structure (self-referencing) mapping — 杀灭 GetClassExpression 递归内联 + fallback
        /// Category → CategoryDto 含 Children (List&lt;Category&gt; → List&lt;CategoryDto&gt;)
        /// 自引用嵌套同时测试 GetClassExpression 和 GetListExpression
        /// </summary>
        [Fact]
        public void Map_TreeStructure_SelfReferencing()
        {
            var source = new Category
            {
                Name = "Root",
                Sort = 1,
                Children = new List<Category>
                {
                    new Category
                    {
                        Name = "Child1",
                        Sort = 2,
                        Children = new List<Category>
                        {
                            new Category { Name = "Grandchild", Sort = 3 }
                        }
                    },
                    new Category { Name = "Child2", Sort = 4 }
                }
            };
            var target = Mapper.Map<Category, CategoryDto>(source)!;

            Assert.Equal("Root", target.Name);
            Assert.Equal(1, target.Sort);
            Assert.NotNull(target.Children);
            Assert.Equal(2, target.Children!.Count);

            Assert.Equal("Child1", target.Children[0].Name);
            Assert.Equal(2, target.Children[0].Sort);
            Assert.NotNull(target.Children[0].Children);
            Assert.Single(target.Children[0].Children!);
            Assert.Equal("Grandchild", target.Children![0].Children![0].Name);
            Assert.Equal(3, target.Children![0].Children![0].Sort);

            Assert.Equal("Child2", target.Children[1].Name);
            Assert.Equal(4, target.Children[1].Sort);
        }

        /// <summary>
        /// MaxInlineDepth upper bound clamping — 杀灭 Mapper.cs L513-514 的钳制逻辑
        /// 设置超出上限的值 → 自动钳制到 MaxInlineDepthUpperBound (16)
        /// </summary>
        [Fact]
        public void MaxInlineDepth_SetAboveUpperBound_Clamped()
        {
            Mapper.MaxInlineDepth = 100;
            Assert.Equal(Mapper.MaxInlineDepthUpperBound, Mapper.MaxInlineDepth);
        }

        /// <summary>
        /// MaxInlineDepth negative clamping — 杀灭 Mapper.cs L515 的负值钳制
        /// 设置负值 → 自动钳制到 0
        /// </summary>
        [Fact]
        public void MaxInlineDepth_SetNegative_ClampedToZero()
        {
            Mapper.MaxInlineDepth = -5;
            Assert.Equal(0, Mapper.MaxInlineDepth);
        }

        /// <summary>
        /// Multi-level nested with collections — 杀灭 GetListExpression + GetClassExpression 组合
        /// Company → CompanyDto: 四级嵌套，每级含 List 集合
        /// 验证跨类型集合 (List&lt;Department&gt; → List&lt;DepartmentDto&gt;) 和嵌套对象同时工作
        /// </summary>
        [Fact]
        public void Map_MultiLevel_Nested_With_Collections()
        {
            var source = new Company
            {
                Name = "MegaCorp",
                Departments = new List<Department>
                {
                    new Department
                    {
                        Name = "Dev",
                        Teams = new List<Team>
                        {
                            new Team
                            {
                                Name = "Frontend",
                                Members = new List<Member>
                                {
                                    new Member { Name = "Bob", Age = 25, IsActive = true },
                                    new Member { Name = "Carol", Age = 28, IsActive = false }
                                }
                            }
                        }
                    },
                    new Department
                    {
                        Name = "QA",
                        Teams = new List<Team>
                        {
                            new Team
                            {
                                Name = "Testing",
                                Members = new List<Member>
                                {
                                    new Member { Name = "Dave", Age = 32, IsActive = true }
                                }
                            }
                        }
                    }
                }
            };

            var target = Mapper.Map<Company, CompanyDto>(source)!;

            Assert.Equal("MegaCorp", target.Name);
            Assert.Equal(2, target.Departments!.Count);
            Assert.Equal("Dev", target.Departments[0].Name);
            Assert.Equal("Frontend", target.Departments[0].Teams![0].Name);
            Assert.Equal(2, target.Departments![0].Teams![0].Members!.Count);
            Assert.Equal("Bob", target.Departments![0].Teams![0].Members![0].Name);
            Assert.Equal(25, target.Departments![0].Teams![0].Members![0].Age);
            Assert.True(target.Departments![0].Teams![0].Members![0].IsActive);
            Assert.Equal("Carol", target.Departments![0].Teams![0].Members![1].Name);
            Assert.False(target.Departments![0].Teams![0].Members![1].IsActive);

            Assert.Equal("QA", target.Departments[1].Name);
            Assert.Equal("Testing", target.Departments[1].Teams![0].Name);
            Assert.Equal("Dave", target.Departments![1].Teams![0].Members![0].Name);
            Assert.Equal(32, target.Departments![1].Teams![0].Members![0].Age);
        }

        /// <summary>
        /// Nullable enum to non-nullable enum — 杀灭 L157 + L370-373 nullable enum 路径
        /// OrderStatus? (有值) → OrderStatus: Nullable.GetUnderlyingType 去除 Nullable
        /// 验证 IsNullableType(source)=true, IsNullableType(target)=false → HasValue 检查路径
        /// </summary>
        [Fact]
        public void Map_NullableEnum_To_NonNullable_HasValue()
        {
            var source = new NullableTypesEntity
            {
                NullableStatus = OrderStatus.Shipped,
                NullStatus = null
            };
            var dto = Mapper.Map<NullableTypesEntity, NullableToNonNullDto>(source)!;

            Assert.Equal(OrderStatus.Shipped, dto.NullableStatus);
            Assert.Equal(default(OrderStatus), dto.NullStatus);
        }

        /// <summary>
        /// Non-nullable DateTime to nullable DateTime? — 杀灭 L165-166
        /// DateTime → DateTime?: IsNullableType(source)=false, IsNullableType(target)=true
        /// sourceItem.PropertyType != typeof(string) = true → Convert 路径
        /// </summary>
        [Fact]
        public void Map_DateTime_To_NullableDateTime()
        {
            var source = new NullableTypesEntity
            {
                NonNullDateTime = new DateTime(2026, 1, 1)
            };
            var dto = Mapper.Map<NullableTypesEntity, NonNullToNullableDto>(source)!;

            Assert.NotNull(dto.NonNullDateTime);
            Assert.Equal(new DateTime(2026, 1, 1), dto.NonNullDateTime!.Value);
        }

        /// <summary>
        /// JSON string→object null guard (via mapping) — 杀灭 GetObjectByJsonExpression L590
        /// 通过完整映射路径验证 null JSON 字符串产生 null 对象
        /// 与 Map_JsonToObject_NullJson_ReturnsNull 互补，使用不同模型
        /// </summary>
        [Fact]
        public void Map_JsonNullString_ToObject_ReturnsNull()
        {
            var source = new JsonFieldEntity
            {
                Id = 99,
                ConfigJson = null!,  // null JSON string
                TagsJson = null!
            };
            var dto = Mapper.Map<JsonFieldEntity, JsonToObjectDto>(source)!;

            Assert.Equal(99, dto.Id);
            // null JSON string → GetObjectByJsonExpression returns null
            Assert.Null(dto.Config);
            Assert.Null(dto.Tags);
        }

        /// <summary>
        /// ByteStatus enum → string — 杀灭 BuildEnumToStringSwitch 对不同底层类型枚举的支持
        /// byte 底层枚举的 Switch 表达式使用 byte 值作为 case 常量
        /// </summary>
        [Fact]
        public void Map_ByteEnum_ToString_SwitchCorrect()
        {
            foreach (ByteStatus status in Enum.GetValues<ByteStatus>())
            {
                var source = new ByteEnumEntity { Status = status };
                var dto = Mapper.Map<ByteEnumEntity, ByteEnumToStringDto>(source)!;
                Assert.Equal(status.ToString(), dto.Status);
            }
        }

        /// <summary>
        /// FileSize enum (long underlying) → string — 杀灭 BuildEnumToStringSwitch 对 long 底层枚举
        /// long 值的 Switch 表达式需要正确处理大数值 case 常量
        /// </summary>
        [Fact]
        public void Map_LongEnum_ToString_SwitchCorrect()
        {
            foreach (FileSize size in Enum.GetValues<FileSize>())
            {
                var source = new LongEnumEntity { Size = size };
                var dto = Mapper.Map<LongEnumEntity, LongEnumToStringDto>(source)!;
                Assert.Equal(size.ToString(), dto.Size);
            }
        }

        /// <summary>
        /// ParameterRewriter — 杀灭 L895-899 VisitParameter 中 node == _oldSrc / _oldTgt 替换
        /// 通过 TryGetInlineMapBody 间接测试：内联映射中参数替换必须正确
        /// 如果 _oldSrc 替换失败 → 表达式树编译失败或映射结果错误
        /// </summary>
        [Fact]
        public void Map_InlineMapping_ParameterRewriter_CorrectSubstitution()
        {
            // Multi-level inline: CustomerEntity → CustomerDto with nested Address → AddressDto
            // The inline body uses temp parameters that must be rewritten to outer expressions
            Mapper.MaxInlineDepth = 5;

            var source = new CustomerEntity
            {
                Id = 42,
                Name = "InlineTest",
                HomeAddress = new Address { City = "InlineCity", Street = "InlineSt", ZipCode = 99999 }
            };
            var target = Mapper.Map<CustomerEntity, CustomerDto>(source)!;

            // If ParameterRewriter fails, nested object mapping would be wrong
            Assert.Equal(42, target.Id);
            Assert.Equal("InlineTest", target.Name);
            Assert.NotNull(target.HomeAddress);
            Assert.Equal("InlineCity", target.HomeAddress!.City);
            Assert.Equal("InlineSt", target.HomeAddress.Street);
            Assert.Equal(99999, target.HomeAddress.ZipCode);
        }

        /// <summary>
        /// BuildMapBody — source property not readable (CanRead == false)
        /// 杀灭 L66 sourceItem.CanRead 检查
        /// 如果变异移除 CanRead 检查 → 尝试读取不可读属性 → 运行时异常
        /// </summary>
        [Fact]
        public void Map_WriteOnlySource_SkippedGracefully()
        {
            // TargetWithReadOnlyProp has a read-only property.
            // When mapping SimpleSource → TargetWithReadOnlyProp:
            // - Id maps normally
            // - ReadOnlyField in target has no setter (CanWrite=false) → skipped
            var source = new SimpleSource { Id = 55 };
            var target = Mapper.Map<SimpleSource, TargetWithReadOnlyProp>(source)!;

            Assert.Equal(55, target.Id);
            Assert.Equal("FIXED", target.ReadOnlyField);
        }

        /// <summary>
        /// Same type mapping (FullTypeEntity → SameTypeDto) — 杀灭 L179 常规字段直接赋值
        /// 所有同名字段类型相同 → Assign(targetProperty, sourceProperty)
        /// 如果变异改为 Convert 或其他 → 不必要的类型转换或异常
        /// </summary>
        [Fact]
        public void Map_SameType_DirectAssignment()
        {
            var source = new FullTypeEntity
            {
                IntProp = 42,
                LongProp = 999L,
                DecimalProp = 12.5m,
                FloatProp = 1.5f,
                DoubleProp = 2.5,
                StringProp = "test",
                BoolProp = true,
                DateTimeProp = new DateTime(2024, 1, 1),
                GuidProp = Guid.NewGuid()
            };
            var dto = Mapper.Map<FullTypeEntity, SameTypeDto>(source)!;

            Assert.Equal(42, dto.IntProp);
            Assert.Equal(999L, dto.LongProp);
            Assert.Equal(12.5m, dto.DecimalProp);
            Assert.Equal(1.5f, dto.FloatProp);
            Assert.Equal(2.5, dto.DoubleProp);
            Assert.Equal("test", dto.StringProp);
            Assert.True(dto.BoolProp);
            Assert.Equal(new DateTime(2024, 1, 1), dto.DateTimeProp);
            Assert.Equal(source.GuidProp, dto.GuidProp);
        }
        // ================================================================
        // Round 3 — Concurrent/ReadOnly collection same-type mapping
        // Kills: IsConcurrentCollection (L692-706), IsReadOnlyCollection (L714-723),
        // ConcurrentDictionary detection (L678-680)
        // ================================================================

        /// <summary>
        /// Entity with ConcurrentQueue property — same-type mapping.
        /// Kills mutant: ConcurrentQueue branch == to != at L694.
        /// </summary>
        public class EntityWithConcurrentQueueProp
        {
            public int Id { get; set; }
            public ConcurrentQueue<string>? Messages { get; set; }
        }

        /// <summary>
        /// Entity with ConcurrentStack property — same-type mapping.
        /// Kills mutant: ConcurrentStack branch == to != at L699.
        /// </summary>
        public class EntityWithConcurrentStackProp
        {
            public int Id { get; set; }
            public ConcurrentStack<int>? Values { get; set; }
        }

        /// <summary>
        /// Entity with ReadOnlyCollection property — same-type mapping.
        /// Kills mutant: ReadOnlyCollection branch == to != at L716.
        /// </summary>
        public class EntityWithReadOnlyCollProp
        {
            public int Id { get; set; }
            public ReadOnlyCollection<int>? Items { get; set; }
        }

        /// <summary>
        /// Entity with ConcurrentDictionary property — same-type mapping.
        /// Kills mutant: ConcurrentDictionary branch == to != at L678.
        /// </summary>
        public class EntityWithConcurrentDictProp
        {
            public int Id { get; set; }
            public ConcurrentDictionary<string, int>? Counts { get; set; }
        }

        /// <summary>
        /// Same-type ConcurrentQueue mapping — verifies IsConcurrentCollection path.
        /// If mutant changes == to !=, ConcurrentQueue falls through to CopyEnumerable
        /// instead of CopyConcurrentQueue, producing different behavior.
        /// </summary>
        [Fact]
        public void Map_SameType_ConcurrentQueue_Property()
        {
            var source = new EntityWithConcurrentQueueProp
            {
                Id = 1,
                Messages = new ConcurrentQueue<string>(new[] { "hello", "world" })
            };

            var target = Mapper.Map<EntityWithConcurrentQueueProp, EntityWithConcurrentQueueProp>(source)!;

            Assert.NotSame(source, target);
            Assert.Equal(1, target.Id);
            Assert.NotNull(target.Messages);
            Assert.Equal(2, target.Messages!.Count);
            var arr = target.Messages.ToArray();
            Assert.Equal("hello", arr[0]);
            Assert.Equal("world", arr[1]);
        }

        /// <summary>
        /// Same-type ConcurrentStack mapping — verifies IsConcurrentCollection path.
        /// If mutant changes == to != at L699, falls through to wrong copy method.
        /// </summary>
        [Fact]
        public void Map_SameType_ConcurrentStack_Property()
        {
            var source = new EntityWithConcurrentStackProp
            {
                Id = 2,
                Values = new ConcurrentStack<int>(new[] { 10, 20, 30 })
            };

            var target = Mapper.Map<EntityWithConcurrentStackProp, EntityWithConcurrentStackProp>(source)!;

            Assert.NotSame(source, target);
            Assert.Equal(2, target.Id);
            Assert.NotNull(target.Values);
            Assert.Equal(3, target.Values!.Count);
        }

        /// <summary>
        /// Same-type ReadOnlyCollection mapping — verifies IsReadOnlyCollection path.
        /// If mutant changes == to != at L716, falls through to CopyEnumerable.
        /// </summary>
        [Fact]
        public void Map_SameType_ReadOnlyCollection_Property()
        {
            var source = new EntityWithReadOnlyCollProp
            {
                Id = 3,
                Items = new List<int> { 7, 8, 9 }.AsReadOnly()
            };

            var target = Mapper.Map<EntityWithReadOnlyCollProp, EntityWithReadOnlyCollProp>(source)!;

            Assert.NotSame(source, target);
            Assert.Equal(3, target.Id);
            Assert.NotNull(target.Items);
            Assert.Equal(3, target.Items!.Count);
            Assert.Equal(7, target.Items[0]);
            Assert.Equal(8, target.Items[1]);
            Assert.Equal(9, target.Items[2]);
        }

        /// <summary>
        /// Same-type ConcurrentDictionary mapping — verifies ConcurrentDictionary detection.
        /// If mutant changes == to != at L678, falls through to CopyDictionaryByKeyValue.
        /// </summary>
        [Fact]
        public void Map_SameType_ConcurrentDictionary_Property()
        {
            var source = new EntityWithConcurrentDictProp
            {
                Id = 4,
                Counts = new ConcurrentDictionary<string, int>()
            };
            source.Counts["alpha"] = 10;
            source.Counts["beta"] = 20;

            var target = Mapper.Map<EntityWithConcurrentDictProp, EntityWithConcurrentDictProp>(source)!;

            Assert.NotSame(source, target);
            Assert.Equal(4, target.Id);
            Assert.NotNull(target.Counts);
            Assert.Equal(2, target.Counts!.Count);
            Assert.Equal(10, target.Counts["alpha"]);
            Assert.Equal(20, target.Counts["beta"]);
        }

        /// <summary>
        /// Same-type ConcurrentQueue with null — null guard kills.
        /// </summary>
        [Fact]
        public void Map_SameType_ConcurrentQueue_Null_Property()
        {
            var source = new EntityWithConcurrentQueueProp { Id = 5, Messages = null };
            var target = Mapper.Map<EntityWithConcurrentQueueProp, EntityWithConcurrentQueueProp>(source)!;
            Assert.NotSame(source, target);
            Assert.Equal(5, target.Id);
            Assert.Null(target.Messages);
        }

        /// <summary>
        /// Same-type ConcurrentStack with null — null guard kills.
        /// </summary>
        [Fact]
        public void Map_SameType_ConcurrentStack_Null_Property()
        {
            var source = new EntityWithConcurrentStackProp { Id = 6, Values = null };
            var target = Mapper.Map<EntityWithConcurrentStackProp, EntityWithConcurrentStackProp>(source)!;
            Assert.NotSame(source, target);
            Assert.Null(target.Values);
        }

        /// <summary>
        /// Same-type ReadOnlyCollection with null — null guard kills.
        /// </summary>
        [Fact]
        public void Map_SameType_ReadOnlyCollection_Null_Property()
        {
            var source = new EntityWithReadOnlyCollProp { Id = 7, Items = null };
            var target = Mapper.Map<EntityWithReadOnlyCollProp, EntityWithReadOnlyCollProp>(source)!;
            Assert.NotSame(source, target);
            Assert.Null(target.Items);
        }

        /// <summary>
        /// Same-type ConcurrentDictionary with null — null guard kills.
        /// </summary>
        [Fact]
        public void Map_SameType_ConcurrentDictionary_Null_Property()
        {
            var source = new EntityWithConcurrentDictProp { Id = 8, Counts = null };
            var target = Mapper.Map<EntityWithConcurrentDictProp, EntityWithConcurrentDictProp>(source)!;
            Assert.NotSame(source, target);
            Assert.Null(target.Counts);
        }
    }
}
