namespace NCMapper;
/// <summary>
/// 错误消息资源 — 支持 zh-Hans（中文，默认）和 en（英文）
/// <para>通过 <see cref="System.Globalization.CultureInfo.CurrentUICulture"/> 自动切换语言</para>
/// </summary>
public static class ErrorMessages
{
    /// <summary>
    /// 是否使用英文错误消息 — 每次访问时检查 CurrentUICulture，支持运行时切换语言
    /// </summary>
    private static bool UseEnglish => System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("en", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 无源属性场景的 scenario 哨兵值 — en 分支仅对它渲染 "(no-source scenario)" 标注；
    /// 其余非 null scenario（内部不变式违约等诊断上下文，中文描述）仅 zh 分支展示，en 不误标为 no-source
    /// </summary>
    internal const string NoSourceScenario = "（无源属性场景）";

    /// <summary>
    /// 映射递归深度超限
    /// </summary>
    /// <param name="maxDepth">最大递归深度</param>
    /// <param name="sourceType">源类型名</param>
    /// <param name="targetType">目标类型名</param>
    /// <param name="propertyName">触发递归的属性名（可选，顶层调用时无法获取）</param>
    /// <returns>格式化后的错误消息</returns>
    public static string RecursionDepthExceeded(int maxDepth, string sourceType, string targetType, string? propertyName = null)
    {
        if (UseEnglish)
        {
            return propertyName != null
                ? $"Mapping recursion depth exceeded {maxDepth}, possible circular reference. Property: {propertyName}, Source type: {sourceType}, Target type: {targetType}. Check for self-referencing cycles in the object graph."
                : $"Mapping recursion depth exceeded {maxDepth}, possible circular reference. Source type: {sourceType}, Target type: {targetType}. Check for self-referencing cycles in the object graph.";
        }
        return propertyName != null
            ? $"递归深度超过上限 {maxDepth}（属性: {propertyName}，源类型: {sourceType}，目标类型: {targetType}）。可能存在循环引用。请检查对象图中是否存在自引用环。"
            : $"递归深度超过 {maxDepth}，可能存在循环引用。源类型: {sourceType}, 目标类型: {targetType}。请检查对象图中是否存在自引用环。";
    }

    /// <summary>
    /// 类型未标注 [MapFrom]
    /// </summary>
    /// <param name="typeName">类型名</param>
    /// <param name="feature">使用的功能名</param>
    /// <returns>格式化后的错误消息</returns>
    public static string MapFromNotApplied(string typeName, string feature)
        => UseEnglish
            ? $"Type {typeName} is not annotated with [MapFrom], cannot use {feature}. Please add [MapFrom(typeof(PrimarySourceType))]."
            : $"类型 {typeName} 未标注 [MapFrom]，无法使用 {feature}。请先标注 [MapFrom(typeof(主源类型))]。";

    /// <summary>
    /// Compose 未找到主源类型匹配
    /// </summary>
    /// <param name="primarySourceName">主映射源类型名</param>
    /// <returns>格式化后的错误消息</returns>
    public static string ComposeSourceNotFound(string primarySourceName)
        => UseEnglish
            ? $"Compose<TTarget> could not find a source object matching the primary source type {primarySourceName}. The primary source type declared by [MapFrom(typeof({primarySourceName}))] must be passed as one of the arguments."
            : $"Compose<TTarget> 未能找到与主映射源类型 {primarySourceName} 匹配的源对象。" +
              $"[MapFrom(typeof({primarySourceName}))] 标注的主源类型必须作为参数之一传入。";

    /// <summary>
    /// 类型无公共构造函数
    /// </summary>
    /// <param name="typeName">类型名</param>
    /// <returns>格式化后的错误消息</returns>
    public static string NoPublicConstructor(string typeName)
        => UseEnglish
            ? $"Type {typeName} has no public constructor, cannot map."
            : $"类型 {typeName} 无公共构造函数，无法映射。";

    /// <summary>
    /// 类型无可映射属性（Projection）
    /// </summary>
    /// <param name="typeName">类型名</param>
    /// <returns>格式化后的错误消息</returns>
    public static string NoMappablePropertiesForProjection(string typeName)
        => UseEnglish
            ? $"Type {typeName} has no mappable properties, cannot build Projection."
            : $"类型 {typeName} 没有可映射的属性，无法构建 Projection。";

    /// <summary>
    /// Projection 源类型不匹配
    /// </summary>
    /// <param name="sourceName">实际传入的源类型名</param>
    /// <param name="declaredName">[MapFrom] 声明的源类型名</param>
    /// <returns>格式化后的错误消息</returns>
    public static string ProjectionSourceTypeMismatch(string sourceName, string declaredName)
        => UseEnglish
            ? $"Projection source type {sourceName} does not match the source type {declaredName} declared by [MapFrom]."
            : $"Projection 源类型 {sourceName} 与 [MapFrom] 声明的源类型 {declaredName} 不一致。";

    /// <summary>
    /// 类型无无参公共构造器（Projection MemberInit 场景）— 需要无参构造器以生成 new Tgt { ... } 格式
    /// </summary>
    /// <param name="typeName">目标类型名</param>
    /// <returns>错误消息字符串</returns>
    public static string ProjectionRequiresParameterlessConstructor(string typeName)
        => UseEnglish
            ? $"Type '{typeName}' has no parameterless public constructor, MapProjection requires MemberInit format (new Tgt {{ ... }} ). " +
              $"Please add a parameterless public constructor for '{typeName}', or use Mapper.Map for runtime mapping."
            : $"类型 '{typeName}' 没有无参公共构造器，MapProjection 需要 MemberInit 格式（new Tgt {{ ... }}）。" +
              $"请为 '{typeName}' 添加无参公共构造器，或改用 Mapper.Map 进行运行时映射。";

    /// <summary>
    /// 未知集合类型 — CollectionCopier 无对应转换方法
    /// </summary>
    /// <param name="typeName">集合类型名</param>
    /// <param name="methodName">转换方法名</param>
    /// <returns>格式化后的错误消息</returns>
    public static string UnknownCollectionType(string typeName, string methodName)
        => UseEnglish
            ? $"Unknown collection type: {typeName}, no matching conversion method '{methodName}' in CollectionCopier."
            : $"未知的对象：{typeName}，CollectionCopier 中无对应的转换方法 '{methodName}'。";

    /// <summary>
    /// 未知集合类型（无方法名）
    /// </summary>
    /// <param name="typeName">集合类型名</param>
    /// <returns>格式化后的错误消息</returns>
    public static string UnknownCollectionTypeNoMethod(string typeName)
        => UseEnglish
            ? $"Unknown collection type: {typeName}, no matching conversion method."
            : $"未知的对象：{typeName},无对应的转换方法";

    /// <summary>
    /// GetObjectByJsonExpression 要求 sourceType 为 string
    /// </summary>
    /// <param name="actualType">实际传入的类型名</param>
    /// <returns>格式化后的错误消息</returns>
    public static string JsonExpressionRequiresString(string actualType)
        => UseEnglish
            ? $"GetObjectByJsonExpression requires sourceType to be string, but got {actualType}. Check the IsFromJson path call conditions."
            : $"GetObjectByJsonExpression 要求 sourceType 为 string，实际为 {actualType}。请检查 IsFromJson 路径的调用条件。";

    /// <summary>
    /// 不支持的集合转换 — List/数组无法映射到 Dictionary —
    /// </summary>
    /// <param name="sourceTypeName">源类型名</param>
    /// <param name="targetTypeName">目标类型名</param>
    /// <returns>错误消息字符串</returns>
    public static string UnsupportedListToDictionaryConversion(string sourceTypeName, string targetTypeName)
        => UseEnglish
            ? $"Unsupported collection conversion: {sourceTypeName} → {targetTypeName}. List/array cannot be mapped to Dictionary. Check the DTO field type configuration."
            : $"不支持的集合转换：{sourceTypeName} → {targetTypeName}。List/数组无法映射到 Dictionary，请检查 DTO 字段类型配置。";

    /// <summary>
    /// ComposeProjection 主源类型不在泛型参数 T1/T2 中
    /// </summary>
    /// <param name="primaryName">主映射源类型名</param>
    /// <param name="t1Name">泛型参数 T1 类型名</param>
    /// <param name="t2Name">泛型参数 T2 类型名</param>
    /// <returns>错误消息字符串</returns>
    public static string ComposeProjectionSourceTypeMismatch(string primaryName, string t1Name, string t2Name)
        => UseEnglish
            ? $"ComposeProjection primary source type {primaryName} is not among the generic parameters T1({t1Name}) or T2({t2Name}). Ensure [MapFrom(typeof(T1))] or [MapFrom(typeof(T2))] matches one of the generic parameters."
            : $"ComposeProjection 主映射源类型 {primaryName} 不在泛型参数 T1({t1Name}) 或 T2({t2Name}) 之中。请确保 [MapFrom(typeof(T1))] 或 [MapFrom(typeof(T2))] 与泛型参数匹配。";

    /// <summary>
    /// ComposeProjection 主源类型不在泛型参数 T1/T2/T3 中（三表场景）
    /// </summary>
    /// <param name="primaryName">主映射源类型名</param>
    /// <param name="t1Name">泛型参数 T1 类型名</param>
    /// <param name="t2Name">泛型参数 T2 类型名</param>
    /// <param name="t3Name">泛型参数 T3 类型名</param>
    /// <returns>错误消息字符串</returns>
    public static string ComposeProjectionSourceTypeMismatch3(string primaryName, string t1Name, string t2Name, string t3Name)
        => UseEnglish
            ? $"ComposeProjection primary source type {primaryName} is not among T1({t1Name}), T2({t2Name}), or T3({t3Name}). Ensure [MapFrom] matches one of the generic parameters."
            : $"ComposeProjection 主映射源类型 {primaryName} 不在 T1({t1Name})、T2({t2Name}) 或 T3({t3Name}) 之中。请确保 [MapFrom] 与泛型参数匹配。";

    /// <summary>
    /// ToTreeList DTO 缺少自引用集合属性
    /// </summary>
    /// <param name="targetTypeName">DTO 类型名</param>
    /// <returns>格式化后的错误消息</returns>
    public static string TreeDtoMissingChildProperty(string targetTypeName)
        => UseEnglish
            ? $"Type {targetTypeName} has no self-referencing collection property (element type is {targetTypeName} itself), cannot use ToTreeList. Add a collection property like List<{targetTypeName}> mapped from the entity's tree navigation."
            : $"类型 {targetTypeName} 缺少自引用集合属性（元素类型为 {targetTypeName} 自身），无法使用 ToTreeList。请添加类似 List<{targetTypeName}> 的集合属性，映射自实体的树形导航属性。";

    /// <summary>
    /// FreeSql ToTreeList DTO 子集合属性名与实体自引用导航名不一致（Q8 同名校验）
    /// <para>FreeSql 树场景：DTO 子集合属性由 MapList 按名映射填充，须与实体自引用导航同名；
    /// 异名时子树静默丢失（仅返回根节点），同名校验将其转为显式异常。</para>
    /// </summary>
    /// <param name="sourceTypeName">实体类型名</param>
    /// <param name="targetTypeName">DTO 类型名</param>
    /// <param name="sourceNavName">实体自引用导航属性名（多个时逗号连接）</param>
    /// <param name="dtoChildrenName">DTO 自引用集合属性名（多个时逗号连接）</param>
    /// <returns>格式化后的错误消息（含迁移引导 + FreeSql 不读 [MapTree] 提示）</returns>
    public static string TreeDtoChildrenNameMismatch(string sourceTypeName, string targetTypeName, string sourceNavName, string dtoChildrenName)
        => UseEnglish
            ? $"FreeSql ToTreeList: self-referencing collection property name(s) [{dtoChildrenName}] of DTO type '{targetTypeName}' do not match self-referencing navigation property name(s) [{sourceNavName}] of entity '{sourceTypeName}'; child nodes would be silently lost (only root nodes returned). Rename the DTO collection property to match the entity navigation name, or rename the entity navigation property. Note: FreeSql does not read [MapTree]; a custom children property name has no effect on this path."
            : $"FreeSql ToTreeList：DTO 类型 '{targetTypeName}' 的自引用集合属性名 [{dtoChildrenName}] 与实体 '{sourceTypeName}' 的自引用导航属性名 [{sourceNavName}] 不一致，子节点将被静默丢弃（仅返回根节点）。请将 DTO 子集合属性重命名为与实体导航同名，或调整实体导航属性名。注意：FreeSql 不读取 [MapTree]，自定义子集合属性名在此路径无效。";

    /// <summary>
    /// JSON 反序列化失败（IsFromJson 路径，D7 异常包装用）
    /// <para>用于 JsonConverter 反序列化入口将 JsonException/NotSupportedException 包装为 MapException 时的消息，
    /// 保留 InnerException 诊断信息（JsonException 的 Path/LineNumber）。</para>
    /// </summary>
    /// <param name="targetTypeName">反序列化目标类型名</param>
    /// <returns>格式化后的错误消息</returns>
    public static string JsonDeserializeFailed(string targetTypeName)
        => UseEnglish
            ? $"Failed to deserialize JSON to type '{targetTypeName}'. The JSON is malformed, too deeply nested, or contains unsupported types. See InnerException for details."
            : $"JSON 反序列化失败，目标类型 '{targetTypeName}'。JSON 格式非法、嵌套过深或包含不受支持的类型。详情见 InnerException。";

    /// <summary>
    /// JSON 序列化失败（A3-09，D7 异常包装用——JsonConverter 序列化入口将
    /// JsonException/NotSupportedException 包装为 MapException 时的消息，保留 InnerException 诊断信息）
    /// </summary>
    /// <param name="sourceTypeName">序列化源类型名</param>
    /// <returns>格式化后的错误消息</returns>
    public static string JsonSerializeFailed(string sourceTypeName)
        => UseEnglish
            ? $"Failed to serialize type '{sourceTypeName}' to JSON. The type contains members not supported by the JSON serializer, or the serializer failed to produce valid JSON. See InnerException for details."
            : $"JSON 序列化失败，源类型 '{sourceTypeName}'。类型包含 JSON 序列化器不支持的成员，或序列化器未能产出合法 JSON。详情见 InnerException。";

    /// <summary>
    /// [Convert] 转换器表达式构建返回 null（内部错误）
    /// </summary>
    /// <param name="propertyName">属性名</param>
    /// <param name="scenario">场景描述片段（无源属性场景传 <see cref="NoSourceScenario"/>，en 分支据此渲染 no-source 标注；其余诊断上下文传中文描述仅 zh 展示；常规场景传 null）</param>
    /// <returns>格式化后的错误消息</returns>
    public static string ConverterExpressionNull(string propertyName, string? scenario = null)
        => UseEnglish
            ? $"[Convert] Property '{propertyName}'{(scenario == NoSourceScenario ? " (no-source scenario)" : "")}: converter expression building returned null. This is an unexpected internal error."
            : $"[Convert] 属性 '{propertyName}'{scenario ?? " "}的转换器表达式构建返回 null，这是不应发生的内部错误";

    // ==== 方法族（静态方法引用形态，与源生成器 NCMAP035/ / 同源文案；正反向共用，带方向上下文）====

    /// <summary>
    /// [Convert]/[ConvertBack] 锚定类型上不存在可访问的静态方法（与 NCMAP035 同源）
    /// </summary>
    /// <param name="propertyName">属性名</param>
    /// <param name="anchorTypeName">锚定类型名</param>
    /// <param name="methodName">方法名</param>
    /// <param name="isReverse">是否反向（[ConvertBack]，正反向共用消息族的方向上下文）</param>
    /// <param name="scenario">场景描述片段（无源属性场景传 <see cref="NoSourceScenario"/>；常规场景传 null）</param>
    /// <returns>格式化后的错误消息</returns>
    public static string ConverterMethodInaccessible(string propertyName, string anchorTypeName, string methodName, bool isReverse, string? scenario = null)
        => UseEnglish
            ? $"{(isReverse ? "[ConvertBack]" : "[Convert]")} Property '{propertyName}'{(scenario == NoSourceScenario ? " (no-source scenario)" : "")} ({(isReverse ? "reverse" : "forward")}): no accessible static method '{methodName}' on anchor type '{anchorTypeName}'. The method must be declared on the anchor type itself (inherited static methods are not considered), be static, and be public, internal (same assembly), or protected internal (same assembly). For the single-name form the anchor type is the type containing the annotated property; to target a method on another type, use the two-argument form [Convert(typeof(X), nameof(X.M))]."
            : $"{(isReverse ? "[ConvertBack]" : "[Convert]")} 属性 '{propertyName}'{scenario ?? " "}（{(isReverse ? "反向" : "正向")}）：锚定类型 '{anchorTypeName}' 上不存在可访问的静态方法 '{methodName}'。方法必须声明于锚定类型自身（不含基类继承）、为静态，且可访问性为 public / internal（同程序集）/ protected internal（同程序集）。单参形态的锚定类型即标注属性所属类型；若方法位于其他类型，请改用双参形态 [Convert(typeof(X), nameof(X.M))]";

    /// <summary>
    /// [Convert]/[ConvertBack] 静态方法多个重载同时命中签名模板（与 NCMAP036 同源）
    /// </summary>
    /// <param name="propertyName">属性名</param>
    /// <param name="anchorTypeName">锚定类型名</param>
    /// <param name="methodName">方法名</param>
    /// <param name="isReverse">是否反向</param>
    /// <param name="scenario">场景描述片段</param>
    /// <returns>格式化后的错误消息</returns>
    public static string ConverterMethodAmbiguous(string propertyName, string anchorTypeName, string methodName, bool isReverse, string? scenario = null)
        => UseEnglish
            ? $"{(isReverse ? "[ConvertBack]" : "[Convert]")} Property '{propertyName}'{(scenario == NoSourceScenario ? " (no-source scenario)" : "")} ({(isReverse ? "reverse" : "forward")}): multiple overloads of static method '{methodName}' on anchor type '{anchorTypeName}' match the signature templates. Rename the method or remove the conflicting overloads; a constructor form that selects an overload by signature is intentionally not provided."
            : $"{(isReverse ? "[ConvertBack]" : "[Convert]")} 属性 '{propertyName}'{scenario ?? " "}（{(isReverse ? "反向" : "正向")}）：锚定类型 '{anchorTypeName}' 的静态方法 '{methodName}' 存在多个重载同时命中签名模板。请重命名方法或移除冲突重载；不提供按签名选择重载的构造形态";

    /// <summary>
    /// [Convert]/[ConvertBack] 静态方法无任何重载匹配签名模板（与 NCMAP037 同源）
    /// </summary>
    /// <param name="propertyName">属性名</param>
    /// <param name="anchorTypeName">锚定类型名</param>
    /// <param name="methodName">方法名</param>
    /// <param name="targetTypeName">目标属性类型名（期望返回类型）</param>
    /// <param name="detail">排除/失配详情（开放泛型指引等）</param>
    /// <param name="isReverse">是否反向</param>
    /// <param name="scenario">场景描述片段</param>
    /// <returns>格式化后的错误消息</returns>
    public static string ConverterMethodSignatureMismatch(string propertyName, string anchorTypeName, string methodName, string targetTypeName, string detail, bool isReverse, string? scenario = null)
        => UseEnglish
            ? $"{(isReverse ? "[ConvertBack]" : "[Convert]")} Property '{propertyName}'{(scenario == NoSourceScenario ? " (no-source scenario)" : "")} ({(isReverse ? "reverse" : "forward")}): no overload of static method '{methodName}' on anchor type '{anchorTypeName}' matches the signature templates (expected return type '{targetTypeName}'). Details: {detail}."
            : $"{(isReverse ? "[ConvertBack]" : "[Convert]")} 属性 '{propertyName}'{scenario ?? " "}（{(isReverse ? "反向" : "正向")}）：锚定类型 '{anchorTypeName}' 的静态方法 '{methodName}' 无任何重载匹配签名模板（期望返回类型 '{targetTypeName}'）。详情：{detail}";

    /// <summary>
    /// [Mask] 脱敏类型未实现 IMask 接口
    /// </summary>
    /// <param name="propertyName">属性名</param>
    /// <param name="maskTypeName">脱敏类型名</param>
    /// <returns>格式化后的错误消息</returns>
    public static string MaskTypeNotImplementIMask(string propertyName, string maskTypeName)
        => UseEnglish
            ? $"[Mask] Property '{propertyName}': mask type '{maskTypeName}' does not implement IMask."
            : $"[Mask] 属性 '{propertyName}' 的脱敏类型 '{maskTypeName}' 未实现 IMask 接口";

    /// <summary>
    /// MaskProvider.Resolve(Type) 方法未找到（内部错误）
    /// <para>detail 为触发属性名，增强诊断定位；核心语义（方法未找到+内部错误）不变。</para>
    /// </summary>
    /// <param name="detail">触发属性名</param>
    /// <returns>格式化后的错误消息</returns>
    public static string MaskResolveMethodNotFound(string detail)
        => UseEnglish
            ? $"MaskProvider.Resolve(Type) method not found (property: {detail}). This is an unexpected internal error."
            : $"MaskProvider.Resolve(Type) 方法未找到（属性: {detail}），这是不应发生的内部错误";

    /// <summary>
    /// IMask.Mask 方法未找到（内部错误）
    /// <para>detail 为触发属性名，增强诊断定位；核心语义（方法未找到+内部错误）不变。</para>
    /// </summary>
    /// <param name="detail">触发属性名</param>
    /// <returns>格式化后的错误消息</returns>
    public static string MaskMaskMethodNotFound(string detail)
        => UseEnglish
            ? $"IMask.Mask method not found (property: {detail}). This is an unexpected internal error."
            : $"IMask.Mask 方法未找到（属性: {detail}），这是不应发生的内部错误";

    /// <summary>
    /// 无法创建 Mask 类型实例（MaskProvider.Resolve(Type) 路径）
    /// </summary>
    /// <param name="maskTypeFullName">脱敏类型全名</param>
    /// <returns>格式化后的错误消息</returns>
    public static string MaskCreateInstanceFailed(string maskTypeFullName)
        => UseEnglish
            ? $"Failed to create an instance of mask type '{maskTypeFullName}'."
            : $"无法创建 Mask 类型 '{maskTypeFullName}' 的实例";

    /// <summary>
    /// [MapProperty] 源字段名称为空（ArgumentException 路径）
    /// <para>fieldName 为触发参数名（nameof(fromField)），核心语义（源字段名不能为空）不变。</para>
    /// </summary>
    /// <param name="fieldName">触发参数名</param>
    /// <returns>格式化后的错误消息</returns>
    public static string MapPropertyFromFieldEmpty(string fieldName)
        => UseEnglish
            ? $"Source field name cannot be empty (parameter: {fieldName})."
            : $"源字段名称不能为空（参数: {fieldName}）";

    // ─────────────────────────────────────────────────────────────────────────
    // 双语完整性整改 — 新增 34 方法（原硬编码消息逐字迁移）
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// JSON 深拷贝失败
    /// </summary>
    /// <param name="typeName">目标类型名</param>
    /// <param name="innerMessage">内部异常消息</param>
    /// <returns>格式化后的错误消息</returns>
    public static string JsonDeepCopyFailed(string typeName, string innerMessage)
        => UseEnglish
            ? $"JSON deep copy failed: type {typeName} cannot be independently copied via JSON serialization: {innerMessage}"
            : $"JSON 深拷贝失败: 类型 {typeName} 无法通过 JSON 序列化创建独立副本: {innerMessage}";

    /// <summary>
    /// 深拷贝所有降级路径均失败
    /// </summary>
    /// <param name="typeName">目标类型名</param>
    /// <param name="formatterMessage">FormatterServices 路径异常消息</param>
    /// <param name="jsonMessage">JSON 路径异常消息</param>
    /// <returns>格式化后的错误消息</returns>
    public static string DeepCopyAllFallbacksFailed(string typeName, string formatterMessage, string jsonMessage)
        => UseEnglish
            ? $"Deep copy failed: no fallback path can create an independent copy of type {typeName}. FormatterServices: {formatterMessage}; JSON: {jsonMessage}"
            : $"深拷贝失败: 所有降级路径均无法为类型 {typeName} 创建独立副本。 FormatterServices: {formatterMessage}; JSON: {jsonMessage}";

    /// <summary>
    /// 构造器参数类型不兼容，无法深拷贝
    /// </summary>
    /// <param name="detail">不兼容参数详情</param>
    /// <returns>格式化后的错误消息</returns>
    public static string DeepCopyCtorIncompatible(string detail)
        => UseEnglish
            ? $"Constructor parameter types are incompatible, cannot deep copy: {detail}"
            : $"构造器参数类型不兼容，无法深拷贝：{detail}";

    /// <summary>
    /// 可空值类型对不存在转换运算符，无法条件转换
    /// 源 Nullable&lt;T&gt; 与目标类型之间既无隐式转换也无显式转换（DateTime?→long 等），
    /// 原 Expression.Convert 构建期裸抛 InvalidOperationException，统一为异常契约形态。
    /// </summary>
    /// <param name="sourceTypeName">源属性类型名（含 Nullable 形态）</param>
    /// <param name="targetTypeName">目标属性类型名</param>
    /// <returns>格式化后的错误消息</returns>
    public static string NullableConversionNotSupported(string sourceTypeName, string targetTypeName)
        => UseEnglish
            ? $"No coercion operator is defined between '{sourceTypeName}' and '{targetTypeName}': nullable-to-value conditional conversion is not possible. Use an explicitly convertible type pair or a custom converter ([Convert])"
            : $"“{sourceTypeName}”与“{targetTypeName}”之间未定义转换运算符，无法进行可空到值类型的条件转换。请改用可转换的类型对或自定义转换器（[Convert]）";

    /// <summary>
    /// 构造器参数类型不兼容，无法映射
    /// </summary>
    /// <param name="detail">不兼容参数详情</param>
    /// <returns>格式化后的错误消息</returns>
    public static string MapCtorIncompatible(string detail)
        => UseEnglish
            ? $"Constructor parameter types are incompatible, cannot map: {detail}"
            : $"构造器参数类型不兼容，无法映射：{detail}";

    /// <summary>
    /// 集合拷贝：类型无 IEnumerable&lt;T&gt; 构造器
    /// </summary>
    /// <param name="typeFullName">类型全名</param>
    /// <returns>格式化后的错误消息</returns>
    public static string CopyEnumerableNoIEnumerableCtor(string typeFullName)
        => UseEnglish
            ? $"CopyEnumerable: type {typeFullName} has no IEnumerable<T> constructor, cannot create an independent copy"
            : $"CopyEnumerable: 类型 {typeFullName} 无 IEnumerable<T> 构造器，无法创建独立副本";

    /// <summary>
    /// 集合拷贝：类型非双泛型参数字典
    /// </summary>
    /// <param name="typeFullName">类型全名</param>
    /// <returns>格式化后的错误消息</returns>
    public static string CopyDictionaryNotGeneric(string typeFullName)
        => UseEnglish
            ? $"CopyDictionaryByKeyValue: type {typeFullName} is not a two-generic-argument dictionary, cannot copy"
            : $"CopyDictionaryByKeyValue: 类型 {typeFullName} 非双泛型参数字典，无法拷贝";

    /// <summary>
    /// 反射：方法未找到
    /// </summary>
    /// <param name="typeFullName">类型全名</param>
    /// <param name="methodName">方法名</param>
    /// <returns>格式化后的错误消息</returns>
    public static string ReflectionMethodNotFound(string typeFullName, string methodName)
        => UseEnglish
            ? $"Method not found: {typeFullName}.{methodName}()"
            : $"方法未找到: {typeFullName}.{methodName}()";

    /// <summary>
    /// 反射：方法未找到（带参数列表）
    /// </summary>
    /// <param name="typeFullName">类型全名</param>
    /// <param name="methodName">方法名</param>
    /// <param name="paramList">参数类型列表</param>
    /// <returns>格式化后的错误消息</returns>
    public static string ReflectionMethodNotFoundWithParams(string typeFullName, string methodName, string paramList)
        => UseEnglish
            ? $"Method not found: {typeFullName}.{methodName}({paramList})"
            : $"方法未找到: {typeFullName}.{methodName}({paramList})";

    /// <summary>
    /// 反射：属性未找到
    /// </summary>
    /// <param name="typeFullName">类型全名</param>
    /// <param name="propertyName">属性名</param>
    /// <returns>格式化后的错误消息</returns>
    public static string ReflectionPropertyNotFound(string typeFullName, string propertyName)
        => UseEnglish
            ? $"Property not found: {typeFullName}.{propertyName}"
            : $"属性未找到: {typeFullName}.{propertyName}";

    /// <summary>
    /// 反射：构造器未找到
    /// </summary>
    /// <param name="typeFullName">类型全名</param>
    /// <param name="paramList">参数类型列表</param>
    /// <returns>格式化后的错误消息</returns>
    public static string ReflectionCtorNotFound(string typeFullName, string paramList)
        => UseEnglish
            ? $"Constructor not found: {typeFullName}({paramList})"
            : $"构造器未找到: {typeFullName}({paramList})";

    /// <summary>
    /// 反射：方法未找到（带绑定标志）
    /// </summary>
    /// <param name="typeFullName">类型全名</param>
    /// <param name="methodName">方法名</param>
    /// <param name="flags">BindingFlags 值</param>
    /// <returns>格式化后的错误消息</returns>
    public static string ReflectionMethodNotFoundWithFlags(string typeFullName, string methodName, string flags)
        => UseEnglish
            ? $"Method not found: {typeFullName}.{methodName} (BindingFlags={flags})"
            : $"方法未找到: {typeFullName}.{methodName} (BindingFlags={flags})";

    /// <summary>
    /// 枚举转换失败：可能为非法字符串或不兼容数字类型
    /// </summary>
    /// <param name="sourceName">源类型名</param>
    /// <param name="targetName">目标类型名</param>
    /// <returns>格式化后的错误消息</returns>
    public static string EnumConversionFailedInvalidString(string sourceName, string targetName)
        => UseEnglish
            ? $"Type conversion failed: {sourceName} → {targetName} (enum conversion failed: possibly an invalid string or incompatible numeric type)"
            : $"类型转换失败: {sourceName} → {targetName}（枚举转换失败，可能为非法字符串或不兼容数字类型）";

    /// <summary>
    /// 枚举值不匹配：目标枚举中不存在对应成员
    /// </summary>
    /// <param name="sourceName">源类型名</param>
    /// <param name="targetName">目标类型名</param>
    /// <returns>格式化后的错误消息</returns>
    public static string EnumConversionFailedNoMatch(string sourceName, string targetName)
        => UseEnglish
            ? $"Type conversion failed: {sourceName} → {targetName} (enum value mismatch: no corresponding member exists in the target enum)"
            : $"类型转换失败: {sourceName} → {targetName}（枚举值不匹配，目标枚举中不存在对应成员）";

    /// <summary>
    /// 类型转换失败：不支持 IConvertible/枚举/Guid/ToString 转换路径
    /// </summary>
    /// <param name="sourceName">源类型名</param>
    /// <param name="targetName">目标类型名</param>
    /// <returns>格式化后的错误消息</returns>
    public static string ConversionFailedUnsupported(string sourceName, string targetName)
        => UseEnglish
            ? $"Type conversion failed: {sourceName} → {targetName} (unsupported IConvertible/enum/Guid/ToString conversion path)"
            : $"类型转换失败: {sourceName} → {targetName}（不支持 IConvertible/枚举/Guid/ToString 转换路径）";

    /// <summary>
    /// 显式点路径段解析失败（正向映射）
    /// <para>按失败原因分类（DotPathFailureKind）逐字对齐原模板；NotNavigableValueType 分支原模板用"处中断"，其余用"处失败"。</para>
    /// </summary>
    /// <param name="path">完整点路径（[MapProperty] FromField 原文）</param>
    /// <param name="segment">失败段原文</param>
    /// <param name="kind">失败原因分类</param>
    /// <param name="targetProperty">目标属性名</param>
    /// <param name="detailTypeName">详情类型名（PropertyNotFound/NotNavigableValueType/NotIndexableType 分支传入）</param>
    /// <returns>格式化后的错误消息</returns>
    public static string DotPathSegmentFailed(string path, string segment, DotPathFailureKind kind, string targetProperty, string? detailTypeName = null)
    {
        return kind switch
        {
            DotPathFailureKind.InvalidIndexSyntax
                => UseEnglish
                    ? $"[NCMapper] Explicit dot path '{path}' failed at segment '{segment}' (invalid index syntax); target property {targetProperty} cannot be mapped"
                    : $"[NCMapper] 显式点路径 '{path}' 在段 '{segment}' 处失败（索引语法非法），目标属性 {targetProperty} 无法映射",
            DotPathFailureKind.IndexNotNonNegative
                => UseEnglish
                    ? $"[NCMapper] Explicit dot path '{path}' failed at segment '{segment}' (index must be a non-negative integer); target property {targetProperty} cannot be mapped"
                    : $"[NCMapper] 显式点路径 '{path}' 在段 '{segment}' 处失败（索引须为非负整数），目标属性 {targetProperty} 无法映射",
            DotPathFailureKind.PropertyNotFound
                => UseEnglish
                    ? $"[NCMapper] Explicit dot path '{path}' failed at segment '{segment}' (property not found in type {detailTypeName}); target property {targetProperty} cannot be mapped"
                    : $"[NCMapper] 显式点路径 '{path}' 在段 '{segment}' 处失败（类型 {detailTypeName} 中未找到属性），目标属性 {targetProperty} 无法映射",
            DotPathFailureKind.NotNavigableValueType
                => UseEnglish
                    ? $"[NCMapper] Explicit dot path '{path}' aborted at segment '{segment}' (type {detailTypeName} is a value type/string/non-collection, cannot be navigated by index); target property {targetProperty} cannot be mapped"
                    : $"[NCMapper] 显式点路径 '{path}' 在段 '{segment}' 处中断（类型 {detailTypeName} 为值型/string/非集合，不可索引导航），目标属性 {targetProperty} 无法映射",
            DotPathFailureKind.NotIndexableType
                => UseEnglish
                    ? $"[NCMapper] Explicit dot path '{path}' failed at segment '{segment}' (type {detailTypeName} is not a collection/string, cannot be index-accessed); target property {targetProperty} cannot be mapped"
                    : $"[NCMapper] 显式点路径 '{path}' 在段 '{segment}' 处失败（类型 {detailTypeName} 非集合/string，不可索引访问），目标属性 {targetProperty} 无法映射",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind,
                UseEnglish ? "Unknown dot path failure kind." : "未知的点路径失败分类。")
        };
    }

    /// <summary>
    /// 反向写回路径段解析失败
    /// <para>NotNavigable 分支原模板用"处中断"，PropertyNotFound 分支用"处失败"。</para>
    /// </summary>
    /// <param name="path">完整写回路径（展示形式）</param>
    /// <param name="segment">失败段原文</param>
    /// <param name="kind">失败原因分类（仅 ReverseWriteBack* 两个成员合法）</param>
    /// <param name="sourceProperty">源属性名</param>
    /// <param name="detailTypeName">详情类型名</param>
    /// <returns>格式化后的错误消息</returns>
    public static string ReverseWriteBackPathFailed(string path, string segment, DotPathFailureKind kind, string sourceProperty, string? detailTypeName = null)
    {
        return kind switch
        {
            DotPathFailureKind.ReverseWriteBackSegmentPropertyNotFound
                => UseEnglish
                    ? $"[NCMapper] Reverse write-back path '{path}' failed at segment '{segment}' (property not found in type {detailTypeName}); source property {sourceProperty} cannot be written back"
                    : $"[NCMapper] 反向写回路径 '{path}' 在段 '{segment}' 处失败（类型 {detailTypeName} 中未找到属性），源属性 {sourceProperty} 无法写回",
            DotPathFailureKind.ReverseWriteBackSegmentNotNavigable
                => UseEnglish
                    ? $"[NCMapper] Reverse write-back path '{path}' aborted at segment '{segment}' (type {detailTypeName} is a value type/string, cannot be navigated); source property {sourceProperty} cannot be written back"
                    : $"[NCMapper] 反向写回路径 '{path}' 在段 '{segment}' 处中断（类型 {detailTypeName} 为值型/string，不可导航），源属性 {sourceProperty} 无法写回",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind,
                UseEnglish ? "Unknown reverse write-back failure kind." : "未知的反向写回失败分类。")
        };
    }

    /// <summary>
    /// 全局映射配置 NamingStrategy 取值越界
    /// </summary>
    /// <param name="value">实际取值</param>
    /// <param name="legalRange">合法范围说明（如"0-3，对应 Default/CaseSensitive/SnakeCase/UpperSnakeCase"）</param>
    /// <returns>格式化后的错误消息</returns>
    public static string NamingStrategyOutOfRange(int value, string legalRange)
        => UseEnglish
            ? $"Global mapping configuration NamingStrategy value out of range: {value} (legal range: {legalRange})."
            : $"全局映射配置 NamingStrategy 取值越界：{value}（合法范围 {legalRange}）。";

    /// <summary>
    /// 全局映射配置冲突：程序集在配置冻结后注册了不同内容
    /// </summary>
    /// <param name="assemblyName">冲突程序集名</param>
    /// <param name="frozenSources">已冻结配置来源</param>
    /// <returns>格式化后的错误消息</returns>
    public static string GlobalConfigConflict(string assemblyName, string frozenSources)
        => UseEnglish
            ? $"Global mapping configuration conflict: assembly '{assemblyName}' registered content that differs from the frozen configuration after it was frozen. Frozen configuration source(s): {frozenSources}. Align [assembly: MapDefaults] or MSBuild attributes across all assemblies — the process-level singleton requires all assembly configurations to be explicitly identical."
            : $"全局映射配置冲突：程序集 '{assemblyName}' 在配置冻结后注册了与已冻结配置不同的内容。已冻结配置来源：{frozenSources}。请对齐各程序集的 [assembly: MapDefaults] 或 MSBuild 属性，进程级单实例要求所有程序集配置显式一致。";

    /// <summary>
    /// 全局映射配置冲突：多个程序集注册了内容不同的全局映射配置
    /// </summary>
    /// <param name="assemblyNames">注册了冲突全局配置的程序集名清单（去重后逗号分隔拼接）</param>
    /// <returns>格式化后的错误消息</returns>
    public static string GlobalConfigMultiAssemblyConflict(string assemblyNames)
        => UseEnglish
            ? $"Global mapping configuration conflict: the following assemblies registered different global mapping configurations [{assemblyNames}]. The process-level singleton requires all assemblies' global configuration to be explicitly aligned. Unify [assembly: MapDefaults] (or the NCMapperXxx MSBuild properties) across assemblies."
            : $"全局映射配置冲突：以下程序集注册了不同的全局映射配置 [{assemblyNames}]。进程级单实例要求所有程序集的全局配置显式对齐；请统一各程序集的 [assembly: MapDefaults] 或 MSBuild 属性（NCMapperXxx）。";

    /// <summary>
    /// 映射验证失败（Error 级别问题汇总）
    /// </summary>
    /// <param name="errorCount">Error 级别问题数</param>
    /// <param name="summary">问题摘要</param>
    /// <returns>格式化后的错误消息</returns>
    public static string MappingValidationFailed(int errorCount, string summary)
        => UseEnglish
            ? $"Mapping validation failed, {errorCount} Error-level issue(s) found: {summary}"
            : $"映射验证失败，发现 {errorCount} 个 Error 级别问题：{summary}";

    /// <summary>
    /// DeepCopy&lt;TSource, TTarget&gt; 不支持同类拷贝
    /// </summary>
    /// <param name="typeName">类型名</param>
    /// <returns>格式化后的错误消息</returns>
    public static string DeepCopySameTypeNotSupported(string typeName)
        => UseEnglish
            ? $"DeepCopy<TSource, TTarget> does not support same-type copying ({typeName}); use DeepCopy<T>(source) instead."
            : $"DeepCopy<TSource, TTarget> 不支持同类拷贝（{typeName}），请使用 DeepCopy<T>(source)。";

    /// <summary>
    /// 嵌套 DTO 上的特性不可翻译为 SQL 投影表达式
    /// </summary>
    /// <param name="targetType">嵌套目标类型名</param>
    /// <param name="property">属性名</param>
    /// <param name="attrName">特性名（不含 Attribute 后缀与方括号）</param>
    /// <returns>格式化后的错误消息</returns>
    public static string ProjectionAttributeNotSqlTranslatable(string targetType, string property, string attrName)
        => UseEnglish
            ? $"Nested type {targetType} property {property} is annotated with [{attrName}], which cannot be translated into a SQL projection expression. Use the AsDto() automatic degradation path, or remove the [{attrName}] attribute from the nested DTO."
            : $"嵌套类型 {targetType} 属性 {property} 标注了 [{attrName}]，该特性不可翻译为 SQL 投影表达式。请使用 AsDto() 自动降级路径，或移除嵌套 DTO 上的 [{attrName}] 特性。";

    /// <summary>
    /// 页码必须 >= 1
    /// </summary>
    /// <returns>格式化后的错误消息</returns>
    public static string PageNumberMustBePositive()
        => UseEnglish
            ? "Page number must be >= 1"
            : "页码必须 >= 1";

    /// <summary>
    /// 每页条数必须 >= 1
    /// </summary>
    /// <returns>格式化后的错误消息</returns>
    public static string PageSizeMustBePositive()
        => UseEnglish
            ? "Page size must be >= 1"
            : "每页条数必须 >= 1";

    /// <summary>
    /// 每批条数必须 >= 1
    /// </summary>
    /// <returns>格式化后的错误消息</returns>
    public static string BatchSizeMustBePositive()
        => UseEnglish
            ? "Batch size must be >= 1"
            : "每批条数必须 >= 1";

    /// <summary>
    /// Dapper GridReader.Read&lt;T&gt;(bool) 重载未找到
    /// </summary>
    /// <param name="dapperVersion">当前 Dapper 版本</param>
    /// <returns>格式化后的错误消息</returns>
    public static string DapperReadOverloadNotFound(string dapperVersion)
        => UseEnglish
            ? $"Dapper GridReader.Read<T>(bool) overload not found (current Dapper version {dapperVersion}). Adjust the Dapper version or contact NCMapper maintainers."
            : $"Dapper GridReader.Read<T>(bool) 重载未找到（当前 Dapper 版本 {dapperVersion}），请调整 Dapper 版本或联系 NCMapper 维护者。";

    /// <summary>
    /// Mapper.MapList&lt;T1,T2&gt; 静态重载未找到
    /// </summary>
    /// <param name="ncMapperVersion">当前 NCMapper 版本</param>
    /// <returns>格式化后的错误消息</returns>
    public static string MapListOverloadNotFound(string ncMapperVersion)
        => UseEnglish
            ? $"Mapper.MapList<T1,T2> static overload not found (NCMapper version {ncMapperVersion}). The runtime component may be incomplete."
            : $"Mapper.MapList<T1,T2> 静态重载未找到（NCMapper 版本 {ncMapperVersion}），运行时组件可能不完整。";

    /// <summary>
    /// DTO 未标注 [MapFrom] 特性（Dapper AsDto 路径）
    /// </summary>
    /// <param name="dtoTypeFullName">DTO 类型全名</param>
    /// <returns>格式化后的错误消息</returns>
    public static string DtoMissingMapFrom(string dtoTypeFullName)
        => UseEnglish
            ? $"DTO type '{dtoTypeFullName}' is not annotated with [MapFrom]. The Dapper AsDto path requires the DTO to be annotated with [MapFrom(typeof(SourceEntity))] to infer the source entity type."
            : $"DTO 类型 '{dtoTypeFullName}' 未标注 [MapFrom] 特性，Dapper AsDto 路径要求 DTO 标注 [MapFrom(typeof(SourceEntity))] 以推断源实体类型。";

    /// <summary>
    /// DTO 未找到指定名称的自引用集合属性（树形结构契约， 改写）
    /// <para>树契约：Children 属性精确名查找（默认 "Children"，[MapTree] 可自定义），名缺失抛本消息；
    /// 名命中但类型不符/只读走带 actualPropertyTypeName 的重载 —— 消息区分两种失败因。</para>
    /// </summary>
    /// <param name="typeFullName">DTO 类型全名</param>
    /// <param name="typeName">DTO 类型名（用于 List&lt;T&gt; Children 示例）</param>
    /// <param name="childrenPropertyName">期望的子集合属性名（[MapTree] 解析或默认 "Children"）</param>
    /// <returns>格式化后的错误消息（报期望名 + [MapTree] 引导 + 扁平场景 ToList/Query 提示）</returns>
    public static string TreeMissingChildrenCollectionProperty(string typeFullName, string typeName, string childrenPropertyName)
        => UseEnglish
            ? $"DTO type '{typeFullName}' has no self-referencing collection property named '{childrenPropertyName}' (expected e.g. List<{typeName}> {childrenPropertyName}); cannot build the tree structure. Annotate [MapTree(childrenProperty: \"...\")] to customize the children property name, or use ToList()/Query() for flat results."
            : $"DTO 类型 '{typeFullName}' 未找到名为 '{childrenPropertyName}' 的自引用集合属性（期望如 List<{typeName}> {childrenPropertyName}），无法构建树形结构。可标注 [MapTree(childrenProperty: \"...\")] 自定义子集合属性名；若无需树形结构，请改用 ToList()/Query() 平铺查询。";

    /// <summary>
    /// DTO 的子集合属性名命中但类型不符/只读（树形结构契约， 新增）
    /// <para>与名缺失版统一抛 MapConfigurationException，消息区分两种失败因。</para>
    /// </summary>
    /// <param name="typeFullName">DTO 类型全名</param>
    /// <param name="typeName">DTO 类型名（用于 List&lt;T&gt; Children 示例）</param>
    /// <param name="childrenPropertyName">子集合属性名（名命中）</param>
    /// <param name="actualPropertyTypeName">实际属性类型全名（失败因证据）</param>
    /// <param name="isReadOnly">true = 属性只读；false = 属性非自引用集合</param>
    /// <returns>格式化后的错误消息（报实际类型 + 期望签名 + [MapTree] 引导）</returns>
    public static string TreeChildrenPropertyInvalid(string typeFullName, string typeName, string childrenPropertyName, string actualPropertyTypeName, bool isReadOnly = false)
        => UseEnglish
            ? isReadOnly
                ? $"DTO type '{typeFullName}': property '{childrenPropertyName}' is read-only; the tree structure requires a writable self-referencing collection (expected e.g. List<{typeName}> {childrenPropertyName}). Annotate [MapTree(childrenProperty: \"...\")] to customize the children property name."
                : $"DTO type '{typeFullName}': property '{childrenPropertyName}' has type '{actualPropertyTypeName}' which is not a self-referencing collection of '{typeName}' (expected e.g. List<{typeName}> {childrenPropertyName}); cannot build the tree structure. Annotate [MapTree(childrenProperty: \"...\")] to customize the children property name."
            : isReadOnly
                ? $"DTO 类型 '{typeFullName}'：属性 '{childrenPropertyName}' 为只读，树形结构要求可写的自引用集合（期望如 List<{typeName}> {childrenPropertyName}）。可标注 [MapTree(childrenProperty: \"...\")] 自定义子集合属性名。"
                : $"DTO 类型 '{typeFullName}'：属性 '{childrenPropertyName}' 的类型为 '{actualPropertyTypeName}'，不是 '{typeName}' 的自引用集合（期望如 List<{typeName}> {childrenPropertyName}），无法构建树形结构。可标注 [MapTree(childrenProperty: \"...\")] 自定义子集合属性名。";

    /// <summary>
    /// DTO 缺少 Id/ParentId 属性（树形结构）
    /// </summary>
    /// <param name="typeFullName">DTO 类型全名</param>
    /// <param name="idPropName">Id 属性名</param>
    /// <param name="parentIdPropName">ParentId 属性名</param>
    /// <returns>格式化后的错误消息</returns>
    public static string TreeMissingIdParentId(string typeFullName, string idPropName, string parentIdPropName)
        => UseEnglish
            ? $"DTO type '{typeFullName}' is missing the '{idPropName}' or '{parentIdPropName}' property; cannot build the tree structure."
            : $"DTO 类型 '{typeFullName}' 缺少 '{idPropName}' 或 '{parentIdPropName}' 属性，无法构建树形结构。";

    /// <summary>
    /// 序列不包含任何元素（Single 要求恰好 1 个，英文原文保留）
    /// </summary>
    /// <param name="targetTypeName">目标类型名</param>
    /// <returns>格式化后的错误消息</returns>
    public static string SequenceNoElements(string targetTypeName)
        => UseEnglish
            ? $"Sequence contains no elements for {targetTypeName} (Single)"
            : $"序列 {targetTypeName} 中不存在任何元素（Single）";

    /// <summary>
    /// 序列包含多个元素（Single 要求恰好 1 个，英文原文保留）
    /// </summary>
    /// <param name="targetTypeName">目标类型名</param>
    /// <returns>格式化后的错误消息</returns>
    public static string SequenceMultipleElements(string targetTypeName)
        => UseEnglish
            ? $"Sequence contains more than one element for {targetTypeName} (Single)"
            : $"序列 {targetTypeName} 中包含多个元素（Single）";

    /// <summary>
    /// GroupBy 后不可使用指定特性（EFCore Guard）
    /// </summary>
    /// <param name="featureName">特性名（如 AsDto）</param>
    /// <returns>格式化后的错误消息</returns>
    public static string GroupByNotSupported(string featureName)
        => UseEnglish
            ? $"Cannot use {featureName} after GroupBy; use an alternative instead (see the compile error for details)"
            : $"GroupBy 后不可使用 {featureName}，请使用替代方案（详见编译错误信息）";

    /// <summary>
    /// AsDto 不支持链式 GroupBy（FreeSql Guard）
    /// <para>句法结构与 EFCore GroupByNotSupported 不同，单模板无法逐字覆盖，独立方法。</para>
    /// </summary>
    /// <param name="featureName">特性名（如 AsDto）</param>
    /// <returns>格式化后的错误消息</returns>
    public static string ChainedGroupByNotSupported(string featureName)
        => UseEnglish
            ? $"{featureName} does not support chained GroupBy; first call .AsDto().ToList() to materialize the data into memory, then use LINQ GroupBy (see the compile error for details)"
            : $"{featureName} 不支持链式 GroupBy，请先 .AsDto().ToList() 取数据到内存再用 LINQ GroupBy（详见编译错误信息）";

    /// <summary>
    /// 已进入 IEnumerable 域（EFCore Guard）
    /// </summary>
    /// <param name="methodName">替代方法名（如 Mapper.MapList）</param>
    /// <returns>格式化后的错误消息</returns>
    public static string IEnumerableDomainReached(string methodName)
        => UseEnglish
            ? $"The query has already entered the IEnumerable domain; use {methodName} instead (see the compile error for details)"
            : $"已进入 IEnumerable 域，请使用 {methodName}（详见编译错误信息）";

    /// <summary>
    /// 委托工厂注册中心未找到类型对的委托实现（兜底触发后仍未命中，W2-1 ④ 策略化深化）
    /// <para>internal：仅由 MapperDelegateRegistry.Resolve 消费； 定案 PublicApi 基线无变更，
    /// 故不随 ErrorMessages 既有 public 消息方法形态（避免公共 API 面扩张）。</para>
    /// </summary>
    /// <param name="sourceTypeName">源类型名</param>
    /// <param name="targetTypeName">目标类型名</param>
    /// <returns>格式化后的错误消息</returns>
    internal static string MapperDelegateNotRegistered(string sourceTypeName, string targetTypeName)
        => UseEnglish
            ? $"No MapperBase delegate is registered for the type pair ({sourceTypeName} -> {targetTypeName}) after the bootstrap fallback. This typically means the static constructor of MapperBase<{sourceTypeName}, {targetTypeName}> failed to complete registration."
            : $"委托工厂注册中心未找到类型对（{sourceTypeName} -> {targetTypeName}）的委托实现（兜底触发后仍未命中）。这通常表示 MapperBase<{sourceTypeName}, {targetTypeName}> 的静态构造函数未能完成注册。";

    /// <summary>
    /// 集合元素混合类型对不可映射（源元素为引用类型 × 目标元素为值类型， 修复）
    /// <para>internal：与 <see cref="MapperDelegateNotRegistered"/> 同口径（PublicApi 基线零变更，不扩张公共 API 面）；
    /// 不复用 MapperDelegateNotRegistered——其"静态构造未能完成注册"语义对类型系统注定不可注册的混合对有误导性。</para>
    /// </summary>
    /// <param name="sourceElemName">源元素类型名</param>
    /// <param name="targetElemName">目标元素类型名</param>
    /// <returns>格式化后的错误消息</returns>
    internal static string MixedElementTypePairNotMappable(string sourceElemName, string targetElemName)
        => UseEnglish
            ? $"Cannot map collection elements from '{sourceElemName}' to '{targetElemName}': the target element type is a value type and no mapping delegate can be instantiated (MapperBase requires TTarget : class)."
            : $"集合元素类型对（{sourceElemName} -> {targetElemName}）不可映射：目标元素为值类型，无法实例化映射委托（MapperBase 约束 TTarget : class）。";

    /// <summary>
    /// 注册表承载的脱敏类型名未能解析为类型（四段式解析全 miss 与负缓存命中统一抛点）
    /// <para>internal：与 <see cref="MapperDelegateNotRegistered"/> 同口径（PublicApi 基线零变更）。
    /// 文案不写"含程序集限定的全名"类引导——承载值由发射器按 Type.FullName 显示名写出，
    /// 用户不可控，该指引不可达。</para>
    /// </summary>
    /// <param name="maskTypeName">脱敏类型显示名</param>
    /// <param name="targetPropertyName">目标属性名</param>
    /// <returns>格式化后的错误消息</returns>
    internal static string MaskTypeNameUnresolvable(string maskTypeName, string targetPropertyName)
        => UseEnglish
            ? $"The mask type name '{maskTypeName}' carried by the registry detail could not be resolved to a type (property: {targetPropertyName}). Make sure the assembly containing the type is referenced and loaded when the mapping delegate is built."
            : $"注册表承载的脱敏类型名 '{maskTypeName}' 未能解析为类型（属性: {targetPropertyName}）——请确认该类型所在程序集已被引用且在映射委托构建时已加载。";

    /// <summary>
    /// [MapFormat] 格式串为空/空白（特性构造器即时报错）
    /// <para>internal：与 <see cref="MapperDelegateNotRegistered"/> 同口径（不扩张公共 API 面）。</para>
    /// </summary>
    /// <returns>格式化后的错误消息</returns>
    internal static string MapFormatEmpty()
        => UseEnglish
            ? "The format string of [MapFormat] must not be null or whitespace."
            : "[MapFormat] 的格式串不能为空或空白。";

    /// <summary>
    /// ConvertBack 反向写回目标解析失败时的出口指引（NCMAP_F18 导航文案，追加在消息末尾）
    /// <para>给出可执行形态：两侧属性名不同 → lambda 形态，写回目标由 lambda 体解析。
    /// 生成端未消费本方法（源生成器运行在宿主编译的程序集身份里，双语分支不可用；
    /// F18 的 Args 就地拼同口径文案）——此处与生成端文案同口径留存，勿分叉。</para>
    /// </summary>
    /// <param name="entityTypeDisplay">实体（反向目标）类型显示名</param>
    /// <param name="anchorName">锚点（DTO 侧）属性名</param>
    /// <returns>出口指引片段（以 "；" 起头，可直接拼在消息末尾）</returns>
    internal static string ConvertBackReverseTargetHint(string entityTypeDisplay, string anchorName)
        => UseEnglish
            ? $"; if the property on '{entityTypeDisplay}' has a different name, use the lambda form .ConvertBack<TEntityProp>((dto) => dto.YourProp) — the write-back target is then taken from the lambda body instead of being looked up by name"
            : $"；若 '{entityTypeDisplay}' 上该属性另有名字，请改用 lambda 形态 .ConvertBack<TEntityProp>((dto) => dto.你的属性)，写回目标将由 lambda 体解析（T3 两参形态用 (dto, 字段值) => …，写回点即正向来源字段），而不再按名查找";

    /// <summary>
    /// [MapFormat] 反向映射拦截（NCMAP046 运行时镜像， 守门表 / 组合矩阵）
    /// <para>internal：与 <see cref="MapperDelegateNotRegistered"/> 同口径（不扩张公共 API 面）；
    /// 文本与源生成器 NCMAP046 消息模板同口径（两条出路指引）。</para>
    /// </summary>
    /// <param name="propertyName">属性名</param>
    /// <returns>格式化后的错误消息</returns>
    internal static string MapFormatReverseSkipped(string propertyName)
        => UseEnglish
            ? $"[MapFormat] property '{propertyName}' is skipped in reverse mapping (format output cannot be reverse-parsed; the entity property keeps its value). Declare [ConvertBack] if round-tripping is needed."
            : $"[MapFormat] 属性 '{propertyName}' 反向不赋值（格式化输出不可反解析，实体属性保持原值）；该属性需要往返请声明 [ConvertBack]";

    // ─────────────────────────────────────────────────────────────────────────
    // 验证器族消息— MapValidator 验证消息
    // 接入双语机制。zh 分支与迁移前 MapValidator 内联字符串逐字一致（含全角/ASCII 标点混用现状，
    // 禁止顺手统一——zh 防回归基线由 ValidatorMessagesLocalizationTests 锚定）。
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 验证报表行降级标记（MapValidationResult.ToString 消费，追加于行尾）
    /// </summary>
    /// <returns>格式化后的消息（非降级行由调用方拼空串，与迁移前行为一致）</returns>
    internal static string ValidatorProjectionDowngradeMark()
        => UseEnglish ? " ⤵ ORM projection downgraded" : " ⤵ ORM投影降级";

    /// <summary>
    /// 验证报表行主体（MapValidationResult.ToString 消费；调用方另拼图标前缀与 Message/降级标记后缀）
    /// </summary>
    /// <param name="sourceType">源类型名</param>
    /// <param name="targetType">目标类型名</param>
    /// <param name="mapped">已映射属性数</param>
    /// <param name="total">目标属性总数</param>
    /// <returns>格式化后的消息</returns>
    internal static string ValidatorReportLine(string sourceType, string targetType, int mapped, int total)
        => UseEnglish
            ? $"{sourceType} → {targetType}: {mapped}/{total} properties mapped"
            : $"{sourceType} → {targetType}: {mapped}/{total} 属性已映射";

    /// <summary>
    /// 程序集扫描异常 Trace 诊断（MapValidator.ScanAssembly 消费，写 MapTracer）
    /// </summary>
    /// <param name="assemblyName">程序集名</param>
    /// <param name="error">异常消息</param>
    /// <returns>格式化后的消息</returns>
    internal static string ValidatorAssemblyScanFailure(string assemblyName, string error)
        => UseEnglish
            ? $"[NCMapper] Validation exception: failed to load types from assembly {assemblyName}: {error}"
            : $"[NCMapper] 验证异常: 程序集 {assemblyName} 加载类型失败: {error}";

    /// <summary>
    /// Fluent 声明对验证行注记（MapValidator.Validate Fluent 补扫产出）
    /// </summary>
    /// <returns>格式化后的消息</returns>
    internal static string ValidatorFluentPairNote()
        => UseEnglish
            ? "(Fluent declaration pair — property completeness is reported by Fluent channel diagnostics; this row reports the downgrade dimension only)"
            : "（Fluent 声明对——属性完整性由 Fluent 通道诊断承载，本行仅报降级维度）";

    /// <summary>
    /// 未映射明细项（MapValidator.ValidateMapping 消费）
    /// </summary>
    /// <param name="targetProp">目标属性名</param>
    /// <param name="missingSourceField">缺失的源字段名</param>
    /// <returns>格式化后的消息</returns>
    internal static string ValidatorUnmappedItem(string targetProp, string missingSourceField)
        => UseEnglish
            ? $"{targetProp} (source field {missingSourceField} not found)"
            : $"{targetProp} (源字段 {missingSourceField} 不存在)";

    /// <summary>
    /// 类型不兼容明细项（MapValidator.ValidateMapping 消费）
    /// </summary>
    /// <param name="targetProp">目标属性名</param>
    /// <param name="sourceTypeName">源属性类型名</param>
    /// <param name="targetTypeName">目标属性类型名</param>
    /// <returns>格式化后的消息</returns>
    internal static string ValidatorTypeMismatchItem(string targetProp, string sourceTypeName, string targetTypeName)
        => UseEnglish
            ? $"{targetProp}: {sourceTypeName} → {targetTypeName} incompatible"
            : $"{targetProp}: {sourceTypeName} → {targetTypeName} 不兼容";

    /// <summary>
    /// 类型不兼容汇总前缀（MapValidator.ValidateMapping 消费）。
    /// <para><b>前导标点约定</b>：Message 自带前导标点（zh 全角"，"、en ASCII ", "），供
    /// MapValidationResult.ToString 报表行直接拼接；剥离契约见 Mapper.ValidateOrThrow
    /// （TrimStart 字符集 {'，', ',', ' '}——两文化前导标点均可剥离）。</para>
    /// </summary>
    /// <param name="count">不兼容属性数</param>
    /// <param name="details">明细串（逗号连接）</param>
    /// <returns>格式化后的消息</returns>
    internal static string ValidatorTypeMismatchSummary(int count, string details)
        => UseEnglish
            ? $", {count} incompatible type(s): {details}"
            : $"，{count} 个类型不兼容：{details}";

    /// <summary>
    /// 未映射汇总前缀（MapValidator.ValidateMapping 消费）。前导标点约定同 <see cref="ValidatorTypeMismatchSummary"/>。
    /// </summary>
    /// <param name="count">未映射属性数</param>
    /// <param name="details">明细串（逗号连接）</param>
    /// <returns>格式化后的消息</returns>
    internal static string ValidatorUnmappedSummary(int count, string details)
        => UseEnglish
            ? $", {count} unmapped: {details}"
            : $"，{count} 个未映射：{details}";

    /// <summary>
    /// PrintReport 报表头（MapValidator.PrintReport 消费）
    /// </summary>
    /// <returns>格式化后的消息</returns>
    internal static string ValidatorReportTitle()
        => UseEnglish
            ? "========== NCMapper Mapping Validation Report =========="
            : "========== NCMapper 映射验证报告 ==========";

    /// <summary>
    /// PrintReport 总计行（MapValidator.PrintReport 消费）
    /// </summary>
    /// <param name="total">映射对总数</param>
    /// <param name="ok">OK 级数量</param>
    /// <param name="warn">Warn 级数量</param>
    /// <param name="error">Error 级数量</param>
    /// <param name="downgraded">投影降级对数</param>
    /// <returns>格式化后的消息（图标两种文化均保留——现状即含，非文案）</returns>
    internal static string ValidatorReportTotals(int total, int ok, int warn, int error, int downgraded)
        => UseEnglish
            ? $"Total {total} mapping pair(s): ✅ {ok} OK / ⚠️ {warn} warning(s) / ❌ {error} error(s) / ⤵ downgraded {downgraded} pair(s)"
            : $"总计 {total} 个映射对：✅ {ok} OK / ⚠️ {warn} 警告 / ❌ {error} 错误 / ⤵ 降级 {downgraded} 对";

    /// <summary>
    /// PrintReport 降级原因行（MapValidator.PrintReport 消费）。
    /// <para>reason 正文来自 DegradationDetectorBase 判定层（本期缩界保留中文——en 文化下该行
    /// 前缀英文 + 正文中文混排， 形态②）。</para>
    /// </summary>
    /// <param name="reason">降级原因正文（判定层原样透传）</param>
    /// <returns>格式化后的消息</returns>
    internal static string ValidatorDowngradeReasonLine(string reason)
        => UseEnglish
            ? $"    ⤵ Downgrade reason: {reason}"
            : $"    ⤵ 降级原因: {reason}";
}

/// <summary>
/// 显式点路径 / 反向写回路径的失败原因分类
/// <para>正向 5 成员供 <see cref="ErrorMessages.DotPathSegmentFailed"/>；反向 2 成员供 <see cref="ErrorMessages.ReverseWriteBackPathFailed"/>。</para>
/// </summary>
public enum DotPathFailureKind
{
    /// <summary>索引语法非法（正向点路径段）</summary>
    InvalidIndexSyntax,

    /// <summary>索引须为非负整数（正向点路径段）</summary>
    IndexNotNonNegative,

    /// <summary>类型中未找到属性（正向点路径段）</summary>
    PropertyNotFound,

    /// <summary>值型/string/非集合，不可索引导航（正向点路径段，原模板"处中断"分支）</summary>
    NotNavigableValueType,

    /// <summary>非集合/string，不可索引访问（正向点路径末段）</summary>
    NotIndexableType,

    /// <summary>反向写回段：类型中未找到属性</summary>
    ReverseWriteBackSegmentPropertyNotFound,

    /// <summary>反向写回段：值型/string 不可导航（原模板"处中断"分支）</summary>
    ReverseWriteBackSegmentNotNavigable
}
