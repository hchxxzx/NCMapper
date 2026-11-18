using NCMapper.Tests.Models;
using System.Diagnostics;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// 深拷贝策略测试 — 表达式树深拷贝、循环引用检测、策略可替换性、性能对比
    /// </summary>
    [Collection("DeepCopyGlobalState")]
    public class DeepCopyStrategyTests
    {
        // ================================================================
        // 测试模型
        // ================================================================

        /// <summary>
        /// 含嵌套引用的 POCO — 测试表达式树递归深拷贝
        /// </summary>
        public class DeepCopyModel
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public DeepCopyNested? Nested { get; set; }
        }

        /// <summary>
        /// 嵌套对象 — 测试递归深拷贝是否创建独立副本
        /// </summary>
        public class DeepCopyNested
        {
            public string City { get; set; } = "";
            public int Code { get; set; }
        }

        /// <summary>
        /// 含循环引用的模型 — 测试循环引用检测（A.Self → A）
        /// </summary>
        public class CircularModel
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public CircularModel? Self { get; set; }
        }

        /// <summary>
        /// 含集合属性的模型 — 集合属性回退到 JSON 深拷贝
        /// </summary>
        public class CollectionModel
        {
            public int Id { get; set; }
            public List<string> Tags { get; set; } = new();
        }

        /// <summary>
        /// record 类型 — 无无参构造器，回退到 JSON 深拷贝
        /// </summary>
        public record RecordModel(int Id, string Name);

        /// <summary>
        /// 可空类型模型 — 测试 Nullable&lt;T&gt; 属性的深拷贝
        /// </summary>
        public class NullableModel
        {
            public int Id { get; set; }
            public int? OptionalValue { get; set; }
            public DateTime? OptionalDate { get; set; }
        }

        // ================================================================
        // 表达式树深拷贝正确性测试
        // ================================================================

        /// <summary>
        /// 表达式树深拷贝：简单 POCO 属性值一致但引用不同
        /// </summary>
        [Fact]
        public void ExpressionTree_SimplePOCO_ValuesMatch_ReferenceDifferent()
        {
            var original = new DeepCopyModel { Id = 1, Name = "Test", Nested = null };
            var copy = Mapper.DeepCopy(original)!;

            Assert.Equal(1, copy.Id);
            Assert.Equal("Test", copy.Name);
            Assert.NotSame(original, copy);
        }

        /// <summary>
        /// 表达式树深拷贝：嵌套对象属性值一致且引用不同（验证递归创建了独立副本）
        /// </summary>
        [Fact]
        public void ExpressionTree_NestedObject_DeepCopied()
        {
            var original = new DeepCopyModel
            {
                Id = 1,
                Name = "Parent",
                Nested = new DeepCopyNested { City = "Beijing", Code = 100000 }
            };
            var copy = Mapper.DeepCopy(original)!;

            Assert.Equal("Beijing", copy.Nested!.City);
            Assert.Equal(100000, copy.Nested.Code);
            // 关键：嵌套对象引用不同，修改拷贝不影响原对象
            Assert.NotSame(original.Nested, copy.Nested);

            copy.Nested.City = "Shanghai";
            Assert.Equal("Beijing", original.Nested.City); // 原对象不受影响
        }

        /// <summary>
        /// 表达式树深拷贝：null 嵌套属性保持 null
        /// </summary>
        [Fact]
        public void ExpressionTree_NullNested_StaysNull()
        {
            var original = new DeepCopyModel { Id = 1, Name = "Test", Nested = null };
            var copy = Mapper.DeepCopy(original)!;

            Assert.Null(copy.Nested);
        }

        /// <summary>
        /// 表达式树深拷贝：可空值类型正确复制
        /// </summary>
        [Fact]
        public void ExpressionTree_NullableValue_Copied()
        {
            var original = new NullableModel { Id = 1, OptionalValue = 42, OptionalDate = new DateTime(2024, 1, 1) };
            var copy = Mapper.DeepCopy(original)!;

            Assert.Equal(42, copy.OptionalValue);
            Assert.Equal(new DateTime(2024, 1, 1), copy.OptionalDate);
            Assert.NotSame(original, copy);
        }

        /// <summary>
        /// 表达式树深拷贝：null 可空值类型保持 null
        /// </summary>
        [Fact]
        public void ExpressionTree_NullNullable_StaysNull()
        {
            var original = new NullableModel { Id = 1, OptionalValue = null, OptionalDate = null };
            var copy = Mapper.DeepCopy(original)!;

            Assert.Null(copy.OptionalValue);
            Assert.Null(copy.OptionalDate);
        }

        /// <summary>
        /// 表达式树深拷贝：集合属性通过 JSON 回退正确深拷贝
        /// </summary>
        [Fact]
        public void ExpressionTree_CollectionProperty_FallsBackToJson()
        {
            var original = new CollectionModel { Id = 1, Tags = new List<string> { "a", "b", "c" } };
            var copy = Mapper.DeepCopy(original)!;

            Assert.Equal(3, copy.Tags.Count);
            Assert.Equal("a", copy.Tags[0]);
            Assert.Equal("b", copy.Tags[1]);
            Assert.Equal("c", copy.Tags[2]);
            // 集合引用不同
            Assert.NotSame(original.Tags, copy.Tags);
        }

        /// <summary>
        /// 表达式树深拷贝：record 类型通过 JSON 回退正确深拷贝
        /// </summary>
        [Fact]
        public void ExpressionTree_RecordType_FallsBackToJson()
        {
            var original = new RecordModel(1, "Test");
            var copy = Mapper.DeepCopy(original)!;

            Assert.Equal(1, copy.Id);
            Assert.Equal("Test", copy.Name);
            Assert.NotSame(original, copy);
        }

        // ================================================================
        // 循环引用检测测试
        // ================================================================

        /// <summary>
        /// 循环引用检测：自引用对象不导致栈溢出，返回有效深拷贝
        /// </summary>
        [Fact]
        public void CircularReference_SelfReference_NoStackOverflow()
        {
            var original = new CircularModel { Id = 1, Name = "Root" };
            original.Self = original; // 自引用循环

            // 必须使用 ExpressionTreeDeepCopyProvider（带循环引用检测）
            var savedProvider = DeepCopyProviderBase.Current;
            DeepCopyProviderBase.Current = new ExpressionTreeDeepCopyProvider();
            try
            {
                var copy = Mapper.DeepCopy(original)!;

                Assert.Equal(1, copy.Id);
                Assert.Equal("Root", copy.Name);
                // 循环引用应被检测：copy.Self 应指向 copy 自身（而非 original）
                Assert.NotNull(copy.Self);
                Assert.Same(copy, copy.Self);
                Assert.NotSame(original, copy.Self);
            }
            finally
            {
                DeepCopyProviderBase.Current = savedProvider;
            }
        }

        /// <summary>
        /// 循环引用检测：多级循环引用（A→B→A）不导致栈溢出
        /// </summary>
        [Fact]
        public void CircularReference_MultiLevel_NoStackOverflow()
        {
            var a = new CircularModel { Id = 1, Name = "A" };
            var b = new CircularModel { Id = 2, Name = "B" };
            a.Self = b;
            b.Self = a; // A→B→A 循环

            // 必须使用 ExpressionTreeDeepCopyProvider（带循环引用检测）
            // FastDeepCopyProvider 不做检测，会栈溢出导致进程崩溃
            var original = DeepCopyProviderBase.Current;
            DeepCopyProviderBase.Current = new ExpressionTreeDeepCopyProvider();
            try
            {
                var copyA = Mapper.DeepCopy(a)!;

                Assert.Equal(1, copyA.Id);
                Assert.NotNull(copyA.Self);
                Assert.Equal(2, copyA.Self.Id);
                Assert.Equal("B", copyA.Self.Name);
                // B 的 Self 应指向 copyA（循环引用被正确处理）
                Assert.Same(copyA, copyA.Self.Self);
            }
            finally
            {
                DeepCopyProviderBase.Current = original;
            }
        }

        // ================================================================
        // 策略可替换性测试
        // ================================================================

        /// <summary>
        /// JsonSerializerBase 可替换：自定义序列化器替换默认实现
        /// </summary>
        [Fact]
        public void JsonSerializerBase_Replaceable_CustomSerializer()
        {
            var originalSerializer = JsonSerializerBase.Current;
            try
            {
                var customSerializer = new CountingJsonSerializer();
                JsonSerializerBase.Current = customSerializer;

                var obj = new DeepCopyNested { City = "Test", Code = 123 };
                var json = JsonConverter.ObjectConvertToJson(obj);
                var deserialized = JsonConverter.JsonConvertToObject<DeepCopyNested>(json);

                Assert.True(customSerializer.SerializeCount > 0);
                Assert.True(customSerializer.DeserializeCount > 0);
                Assert.Equal("Test", deserialized!.City);
                Assert.Equal(123, deserialized.Code);
            }
            finally
            {
                JsonSerializerBase.Current = originalSerializer; // 恢复默认
            }
        }

        /// <summary>
        /// DeepCopyProviderBase 可替换：切换到 JSON 深拷贝策略
        /// </summary>
        [Fact]
        public void DeepCopyProvider_Replaceable_SwitchToJson()
        {
            var originalProvider = DeepCopyProviderBase.Current;
            try
            {
                DeepCopyProviderBase.Current = new JsonDeepCopyProviderWrapper();

                var original = new DeepCopyModel
                {
                    Id = 1,
                    Name = "Test",
                    Nested = new DeepCopyNested { City = "Beijing", Code = 100000 }
                };
                var copy = Mapper.DeepCopy(original)!;

                Assert.Equal(1, copy.Id);
                Assert.Equal("Test", copy.Name);
                Assert.Equal("Beijing", copy.Nested!.City);
                Assert.Equal(100000, copy.Nested.Code);
                Assert.NotSame(original, copy);
                Assert.NotSame(original.Nested, copy.Nested);
            }
            finally
            {
                DeepCopyProviderBase.Current = originalProvider; // 恢复默认
            }
        }

        /// <summary>
        /// 自定义深拷贝策略：继承 DeepCopyProviderBase 实现自定义逻辑
        /// </summary>
        [Fact]
        public void DeepCopyProvider_CustomImplementation()
        {
            var originalProvider = DeepCopyProviderBase.Current;
            try
            {
                DeepCopyProviderBase.Current = new TaggedDeepCopyProvider();
                var original = new DeepCopyModel { Id = 42, Name = "Custom" };
                var copy = Mapper.DeepCopy(original)!;

                Assert.Equal(42, copy.Id);
                Assert.Equal("Custom-DeepCopied", copy.Name); // 自定义策略修改了 Name
            }
            finally
            {
                DeepCopyProviderBase.Current = originalProvider;
            }
        }

        // ================================================================
        // 性能对比测试
        // ================================================================

        /// <summary>
        /// 性能对比：表达式树深拷贝 vs JSON 深拷贝（表达式树应明显更快）
        /// </summary>
        [Fact]
        public void Performance_ExpressionTree_vs_JSON()
        {
            var model = new DeepCopyModel
            {
                Id = 1,
                Name = "PerfTest",
                Nested = new DeepCopyNested { City = "Beijing", Code = 100000 }
            };

            // 预热
            Mapper.DeepCopy(model);

            const int iterations = 10_000;

            // 表达式树深拷贝
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                Mapper.DeepCopy(model);
            }
            sw.Stop();
            var expressionTreeMs = sw.ElapsedMilliseconds;

            // JSON 深拷贝
            var originalProvider = DeepCopyProviderBase.Current;
            try
            {
                DeepCopyProviderBase.Current = new JsonDeepCopyProviderWrapper();
                // 预热
                Mapper.DeepCopy(model);

                sw.Restart();
                for (int i = 0; i < iterations; i++)
                {
                    Mapper.DeepCopy(model);
                }
                sw.Stop();
                var jsonMs = sw.ElapsedMilliseconds;

                // 表达式树应至少比 JSON 快 2 倍（通常 5-10 倍）
                // 使用宽松断言避免 CI 环境波动
                Assert.True(expressionTreeMs < jsonMs,
                    $"表达式树深拷贝({expressionTreeMs}ms)应快于JSON深拷贝({jsonMs}ms)，迭代{iterations}次");
            }
            finally
            {
                DeepCopyProviderBase.Current = originalProvider;
            }
        }

        // ================================================================
        // 辅助类
        // ================================================================

        /// <summary>
        /// 计数 JSON 序列化器 — 用于验证 JsonSerializerBase 可替换性
        /// 内部委托给默认 System.Text.Json 实现，同时统计调用次数
        /// </summary>
        private class CountingJsonSerializer : JsonSerializerBase
        {
            public int SerializeCount { get; private set; }
            public int DeserializeCount { get; private set; }
            private static readonly System.Text.Json.JsonSerializerOptions _opts = new();

            public override string Serialize(object? obj)
            {
                SerializeCount++;
                if (obj == null) return "";
                return System.Text.Json.JsonSerializer.Serialize(obj, _opts);
            }

            public override object? Deserialize(string json, Type targetType)
            {
                DeserializeCount++;
                if (string.IsNullOrWhiteSpace(json)) return null;
                return System.Text.Json.JsonSerializer.Deserialize(json, targetType, _opts);
            }
        }

        /// <summary>
        /// JSON 深拷贝策略包装器 — 用 JSON 序列化实现深拷贝（不支持循环引用）
        /// </summary>
        private class JsonDeepCopyProviderWrapper : DeepCopyProviderBase
        {
            protected override T CreateInstance<T>(T source)
            {
                if (typeof(T) == typeof(string))
                    return source;
                var json = JsonSerializerBase.Current.Serialize(source);
                return (T?)JsonSerializerBase.Current.Deserialize(json, typeof(T))!;
            }

            protected override void FillProperties<T>(T source, T target)
            {
                // JSON 方案在 CreateInstance 中已完成所有工作
            }
        }

        /// <summary>
        /// 自定义深拷贝策略 — 在 Name 后追加 "-DeepCopied" 标记
        /// </summary>
        private class TaggedDeepCopyProvider : DeepCopyProviderBase
        {
            protected override T CreateInstance<T>(T source)
            {
                if (typeof(T) == typeof(string))
                    return source;
                var json = JsonSerializerBase.Current.Serialize(source);
                var result = (T?)JsonSerializerBase.Current.Deserialize(json, typeof(T));
                // 如果有 Name 属性，追加标记
                var nameProp = typeof(T).GetProperty("Name");
                if (nameProp != null && nameProp.CanWrite)
                {
                    var currentName = (string?)nameProp.GetValue(result);
                    nameProp.SetValue(result, currentName + "-DeepCopied");
                }
                return result!;
            }

            protected override void FillProperties<T>(T source, T target)
            {
                // 自定义方案在 CreateInstance 中已完成所有工作
            }
        }
    }
}
