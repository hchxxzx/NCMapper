namespace NCMapper.SourceGen;

/// <summary>
/// 特性与接口全名常量— 消除全项目散落的魔法字符串（原 39 处），
/// 拼写错误有编译期保护；特性改名只需改此处。
/// </summary>
internal static class AttributeNames
{
    /// <summary>
    /// [MapFrom] 特性全名
    /// </summary>
    public const string MapFrom = "NCMapper.Attributes.MapFromAttribute";

    /// <summary>
    /// [MapProperty] 特性全名
    /// </summary>
    public const string MapProperty = "NCMapper.Attributes.MapPropertyAttribute";

    /// <summary>
    /// [MapIgnore] 特性全名
    /// </summary>
    public const string MapIgnore = "NCMapper.Attributes.MapIgnoreAttribute";

    /// <summary>
    /// [Mask] 特性全名
    /// </summary>
    public const string Mask = "NCMapper.Attributes.MaskAttribute";

    /// <summary>
    /// [Convert] 特性全名
    /// </summary>
    public const string Convert = "NCMapper.Attributes.ConvertAttribute";

    /// <summary>
    /// [ConvertBack] 特性全名
    /// </summary>
    public const string ConvertBack = "NCMapper.Attributes.ConvertBackAttribute";

    /// <summary>
    /// [MapNoAutoInclude] 特性全名
    /// </summary>
    public const string MapNoAutoInclude = "NCMapper.Attributes.MapNoAutoIncludeAttribute";

    /// <summary>
    /// [MapDefaults] 特性全名
    /// </summary>
    public const string MapDefaults = "NCMapper.Attributes.MapDefaultsAttribute";

    /// <summary>
    /// [NCMapperOptions] 特性全名
    /// </summary>
    public const string NCMapperOptions = "NCMapper.Attributes.NCMapperOptionsAttribute";

    /// <summary>
    /// [MapperConfig] 特性全名（Fluent 通道入口锚点——A1.5 方案：
    /// 类级特性 + 类内 static void M(FluentMappingConfig cfg) 方法名任意）
    /// </summary>
    public const string MapperConfig = "NCMapper.MapperConfigAttribute";

    /// <summary>
    /// [BeforeMapping] 特性全名（特性侧前置生命周期钩子
    /// DTO 类型级、与 [MapFrom] 同居；双 ctor 解析镜像 Convert）
    /// </summary>
    public const string BeforeMapping = "NCMapper.Attributes.BeforeMappingAttribute";

    /// <summary>
    /// [AfterMapping] 特性全名（特性侧后置生命周期钩子
    /// 此前仅 Fluent 通道 .AfterMapping 终端可达）
    /// </summary>
    public const string AfterMapping = "NCMapper.Attributes.AfterMappingAttribute";

    /// <summary>
    /// [MapWhen] 特性全名（特性侧条件守卫
    /// 属性级谓词声明，与 Fluent .MapWhen 终端填充同一 pm 级承载字段）
    /// </summary>
    public const string MapWhen = "NCMapper.Attributes.MapWhenAttribute";

    /// <summary>
    /// [MapSubclass] 特性全名（特性侧多态子类型对登记
    /// 标注在承载 [MapFrom] 的基类 DTO 上，与 Fluent .MapSubclass 终端同承载
    /// SubclassPairs；O-1 方案 1 形态，Inherited=false）
    /// </summary>
    public const string MapSubclass = "NCMapper.Attributes.MapSubclassAttribute";

    /// <summary>
    /// [MapTree] 特性全名（类级树构建定义：定义非映射对，不纳入 NCMAP019
    /// 十项家族管制：类级映射配置特性同样须映射声明锚，置顶 P3"规则不能有例外"）
    /// </summary>
    public const string MapTree = "NCMapper.Attributes.MapTreeAttribute";

    /// <summary>
    /// [MapFormat] 特性全名（属性级格式化——接入面 a；
    /// 消费点：NCMAP019 家族清单 / NCMAP022 矛盾谓词 / OverrideIgnore 采集面 / 提取链）
    /// </summary>
    public const string MapFormat = "NCMapper.Attributes.MapFormatAttribute";

    /// <summary>
    /// [MapNoAutoMatch] 特性全名（特性侧对级白名单开关—— 转类；
    /// 消费点：提取链类级读取点（MappingExtractor.ExtractMappingCore 第四读取点）/ NCMAP019 家族清单
    /// （IsMappingAttributeClass）/ 运行时反射腿（RuntimeSnapshotStore.IsNoAutoMatch 纯反射腿））
    /// </summary>
    public const string MapNoAutoMatch = "NCMapper.Attributes.MapNoAutoMatchAttribute";
}

/// <summary>
/// 特性符号显示名缓存— INamedTypeSymbol → ToDisplayString()
/// 一次物化复用（特性判定站点原先每次调用全量物化显示串，多属性 × 多类型输入下重复物化为主导成本）。
/// <para>生命周期合规（跨 compilation 缓存禁止）：键 = INamedTypeSymbol 实例——Roslyn 符号
/// 身份与 compilation 绑定（同 compilation 内同类型恒同实例，跨 compilation 必为新实例），
/// ConditionalWeakTable 条目随符号 GC 回收 → 缓存天然不跨 compilation 存活，无需手动清理。</para>
/// <para>语义等价：同一 compilation 内同一符号的 ToDisplayString() 恒定（显示格式无参默认），
/// 缓存值 ≡ 现场计算；null 透传（symbol == null → null，与站点 `?.ToDisplayString()` 逐位一致）；
/// CWT 并发首访 factory 幂等（结果一致），线程安全。</para>
/// </summary>
internal static class AttrSymbolCache
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Microsoft.CodeAnalysis.INamedTypeSymbol, string> _displayNames
        = new System.Runtime.CompilerServices.ConditionalWeakTable<Microsoft.CodeAnalysis.INamedTypeSymbol, string>();

    /// <summary>
    /// 特性类符号显示名（缓存化 ToDisplayString；null 透传）
    /// </summary>
    /// <param name="symbol">特性类符号（可为 null）</param>
    /// <returns>显示名（symbol 为 null 时返回 null）</returns>
    public static string? DisplayName(Microsoft.CodeAnalysis.INamedTypeSymbol? symbol)
        => symbol == null ? null : _displayNames.GetValue(symbol, static s => s.ToDisplayString());
}
