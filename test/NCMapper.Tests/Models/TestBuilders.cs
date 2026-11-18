using NCMapper.Attributes;

namespace NCMapper.Tests.Models
{
    // ================================================================
    // T16: 测试数据工厂 — Builder 模式
    // ================================================================

    /// <summary>
    /// T16: FullTypeEntity 构建器 — 提供流式 API 构造测试数据，
    /// 默认值与 FullTypeEntity 一致，可通过 WithXxx 方法覆盖任意字段
    /// </summary>
    public class FullTypeEntityBuilder
    {
        private int _intProp = 42;
        private long _longProp = 9999999999L;
        private decimal _decimalProp = 123.45m;
        private float _floatProp = 3.14f;
        private double _doubleProp = 2.71828;
        private string _stringProp = "hello";
        private bool _boolProp = true;
        private DateTime _dateTimeProp = new(2024, 6, 1, 12, 30, 0);
        private Guid _guidProp = Guid.Parse("11111111-2222-3333-4444-555555555555");
        private OrderStatus _statusProp = OrderStatus.Shipped;
        private int? _nullableIntProp = 100;
        private int? _nullIntNull = null;

        public FullTypeEntityBuilder WithIntProp(int value) { _intProp = value; return this; }
        public FullTypeEntityBuilder WithLongProp(long value) { _longProp = value; return this; }
        public FullTypeEntityBuilder WithDecimalProp(decimal value) { _decimalProp = value; return this; }
        public FullTypeEntityBuilder WithFloatProp(float value) { _floatProp = value; return this; }
        public FullTypeEntityBuilder WithDoubleProp(double value) { _doubleProp = value; return this; }
        public FullTypeEntityBuilder WithStringProp(string value) { _stringProp = value; return this; }
        public FullTypeEntityBuilder WithBoolProp(bool value) { _boolProp = value; return this; }
        public FullTypeEntityBuilder WithDateTimeProp(DateTime value) { _dateTimeProp = value; return this; }
        public FullTypeEntityBuilder WithGuidProp(Guid value) { _guidProp = value; return this; }
        public FullTypeEntityBuilder WithStatusProp(OrderStatus value) { _statusProp = value; return this; }
        public FullTypeEntityBuilder WithNullableIntProp(int? value) { _nullableIntProp = value; return this; }
        public FullTypeEntityBuilder WithNullIntNull(int? value) { _nullIntNull = value; return this; }

        /// <summary>
        /// 构造 FullTypeEntity 实例
        /// </summary>
        public FullTypeEntity Build() => new()
        {
            IntProp = _intProp,
            LongProp = _longProp,
            DecimalProp = _decimalProp,
            FloatProp = _floatProp,
            DoubleProp = _doubleProp,
            StringProp = _stringProp,
            BoolProp = _boolProp,
            DateTimeProp = _dateTimeProp,
            GuidProp = _guidProp,
            StatusProp = _statusProp,
            NullableIntProp = _nullableIntProp,
            NullIntNull = _nullIntNull
        };

        /// <summary>
        /// 构造多个 FullTypeEntity（使用相同配置）
        /// </summary>
        public List<FullTypeEntity> BuildMany(int count) =>
            Enumerable.Range(0, count).Select(_ => Build()).ToList();
    }

    /// <summary>
    /// T16: CustomerEntity 构建器 — 支持嵌套 Address 和 Orders 的流式构造
    /// </summary>
    public class CustomerEntityBuilder
    {
        private int _id = 1;
        private string _name = "ZhangSan";
        private Address? _homeAddress = new() { City = "Beijing", Street = "ChangAn Street", ZipCode = 100000 };
        private List<OrderItem>? _orders = new()
        {
            new() { ProductName = "Widget", Quantity = 2, Price = 9.99m }
        };

        public CustomerEntityBuilder WithId(int value) { _id = value; return this; }
        public CustomerEntityBuilder WithName(string value) { _name = value; return this; }
        public CustomerEntityBuilder WithAddress(Address? value) { _homeAddress = value; return this; }
        public CustomerEntityBuilder WithAddress(string city, string street, int zipCode) =>
            WithAddress(new Address { City = city, Street = street, ZipCode = zipCode });
        public CustomerEntityBuilder WithOrders(List<OrderItem>? value) { _orders = value; return this; }
        public CustomerEntityBuilder WithoutAddress() { _homeAddress = null; return this; }
        public CustomerEntityBuilder WithoutOrders() { _orders = null; return this; }

        /// <summary>
        /// 构造 CustomerEntity 实例
        /// </summary>
        public CustomerEntity Build() => new()
        {
            Id = _id,
            Name = _name,
            HomeAddress = _homeAddress,
            Orders = _orders
        };
    }

    /// <summary>
    /// T16: JsonFieldEntity 构建器 — 支持自定义 JSON 字符串
    /// </summary>
    public class JsonFieldEntityBuilder
    {
        private int _id = 1;
        private string _configJson = "{\"Theme\":\"Dark\",\"FontSize\":14}";
        private string _tagsJson = "[\"csharp\",\"dotnet\",\"mapper\"]";
        private string _nestedObjectsJson = "[]";
        private string _metadataJson = "{}";
        private string _emptyJsonArray = "[]";

        public JsonFieldEntityBuilder WithId(int value) { _id = value; return this; }
        public JsonFieldEntityBuilder WithConfig(string theme, int fontSize)
        {
            _configJson = $"{{\"Theme\":\"{theme}\",\"FontSize\":{fontSize}}}";
            return this;
        }
        public JsonFieldEntityBuilder WithConfigJson(string json) { _configJson = json; return this; }
        public JsonFieldEntityBuilder WithTags(params string[] tags)
        {
            _tagsJson = "[" + string.Join(",", tags.Select(t => $"\"{t}\"")) + "]";
            return this;
        }
        public JsonFieldEntityBuilder WithTagsJson(string json) { _tagsJson = json; return this; }
        public JsonFieldEntityBuilder WithNestedObjectsJson(string json) { _nestedObjectsJson = json; return this; }
        public JsonFieldEntityBuilder WithMetadataJson(string json) { _metadataJson = json; return this; }
        public JsonFieldEntityBuilder WithEmptyJsonArray(string json) { _emptyJsonArray = json; return this; }

        /// <summary>
        /// 构造 JsonFieldEntity 实例
        /// </summary>
        public JsonFieldEntity Build() => new()
        {
            Id = _id,
            ConfigJson = _configJson,
            TagsJson = _tagsJson,
            NestedObjectsJson = _nestedObjectsJson,
            MetadataJson = _metadataJson,
            EmptyJsonArray = _emptyJsonArray
        };
    }

    /// <summary>
    /// T16: Address 构建器 — 嵌套对象快速构造
    /// </summary>
    public class AddressBuilder
    {
        private string _city = "Beijing";
        private string _street = "ChangAn Street";
        private int _zipCode = 100000;

        public AddressBuilder WithCity(string value) { _city = value; return this; }
        public AddressBuilder WithStreet(string value) { _street = value; return this; }
        public AddressBuilder WithZipCode(int value) { _zipCode = value; return this; }

        /// <summary>
        /// 构造 Address 实例
        /// </summary>
        public Address Build() => new()
        {
            City = _city,
            Street = _street,
            ZipCode = _zipCode
        };
    }
}
