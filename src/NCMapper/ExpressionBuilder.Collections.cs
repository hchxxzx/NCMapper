using NCMapper.Attributes;
using NCMapper.Internal;
using NCMapper.Shared;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using static System.Linq.Expressions.Expression;

namespace NCMapper;

internal static partial class ExpressionBuilder
{
    /// <summary>
    /// 对象类型转换（嵌套对象映射）
    /// 优化：优先尝试内联赋值（零委托调用），失败则回退到 MapperBase.Map 委托调用
    /// 安全限制：内联深度上限 MaxInlineDepth 层，超过则回退到委托调用（防止编译期表达式树无限展开）
    /// <para>同类型对自引用结构（如树形 DTO）不无限内联，超限层回退委托调用，由运行时递归深度检查保护</para>
    /// </summary>
    /// <param name="sourceProperty">源对象属性表达式</param>
    /// <param name="sourceType">源对象类型</param>
    /// <param name="targetType">目标对象类型</param>
    /// <returns>条件表达式：sourceProperty 非空时映射，否则返回 null</returns>
    [RequiresDynamicCode("GetClassExpression uses MakeGenericType and reflection in the runtime fallback path. Not supported in AOT.")]
    [RequiresUnreferencedCode("GetClassExpression scans type members and attributes via reflection. Some members may be trimmed.")]
    internal static Expression GetClassExpression(Expression sourceProperty, Type sourceType, Type targetType)
    {
        //条件p.Item!=null
        var testItem = NotEqual(sourceProperty, Constant(null, sourceType));

        // 优化：尝试内联 — 直接在当前表达式树中嵌入 new + 赋值列表，消除 Map/MapFunc/MapAction 三层委托
        // 安全限制：只在前 MaxInlineDepth 层内联，超过则回退到委托调用（运行时 Map 方法有 B12 循环引用防护）
        // 注意：s_inlineDepth 必须在 TryGetInlineMapBody 之前检查，因为 TryGetInlineMapBody 会递归调用 BuildMapBody→GetClassExpression
        if (s_inlineDepth < Mapper.MaxInlineDepth)
        {
            s_inlineDepth++;
            List<Expression>? inlineBody = null;
            ParameterExpression? innerTgtVar = null;
            Expression? newExpr = null;
            try
            {
                inlineBody = TryGetInlineMapBody(sourceType, targetType, sourceProperty, out innerTgtVar);
                if (inlineBody != null && innerTgtVar != null)
                {
                    newExpr = TryBuildNewExpr(targetType, sourceType, sourceProperty);
                }
            }
            catch (Exception ex) when (ex is not MapException)
            {
                // 内联映射构建失败 → 回退到非内联路径
                // MapException（配置错误）不捕获，向上传播给用户
                if (MapTracer.GloballyEnabled) MapTracer.Write($"[NCMapper] 内联映射构建失败: {sourceType.Name}→{targetType.Name}: {ex.GetType().Name}: {ex.Message}");
                inlineBody = null;
            }
            finally
            {
                s_inlineDepth--;
            }

            if (inlineBody != null && innerTgtVar != null && newExpr != null)
            {
                var block = Block(
                    targetType,
                    new[] { innerTgtVar },
                    Assign(innerTgtVar, newExpr),
                    Block(inlineBody),
                    innerTgtVar
                );
                return Condition(testItem, block, Constant(null, targetType));
            }
        }

        // W2-1 ④（P1-SHOULD-1 消除）：Fallback 路径 — 委托工厂按实际类型对（嵌套属性类型对）
        // Resolve 后构造 Map(src, null) 调用表达式（null 守卫 testItem 保留原处——MapFallback 仅构造调用，
        // Map 入口为双计数器递归深度检查，超限抛 MapException——语义随 MapperBase.Map 委托承载）
        var mapperDelegate = MapperDelegateRegistry.Resolve(sourceType, targetType);
        var iftrue = mapperDelegate.MapFallback(sourceProperty);
        return Condition(testItem, iftrue, Constant(null, targetType));
    }

    /// <summary>
    /// 将对象转换为JSON文本（目标属性为string类型时使用）
    /// </summary>
    /// <param name="sourceProperty">源对象属性表达式</param>
    /// <param name="sourceType">源对象类型（非string的对象）</param>
    /// <param name="targetType">目标类型（string类型）</param>
    /// <returns>条件表达式：sourceProperty 非空时序列化为JSON，否则返回 null</returns>
    [RequiresUnreferencedCode("GetJsonByObjectExpression scans type members and attributes via reflection. Some members may be trimmed.")]
    internal static Expression GetJsonByObjectExpression(Expression sourceProperty, Type sourceType, Type targetType)
    {
        // 删除值类型源死分支——唯一调用方
        // JsonStrategy 的 srcIsObject 前置判定（!IsValueType && !IsString）已排除该形态，
        // 值类型装箱序列化分支不可达；helper 契约对齐为"引用类型源序列化"。
        //呼叫指定类的指定方法
        var method = typeof(JsonConverter).GetMethod("ObjectConvertToJson");

        //条件p.Item!=null
        var testItem = NotEqual(sourceProperty, Constant(null, sourceType));

        var iftrue = Call(null, method!, sourceProperty);

        //如果testItem符合条件，则执行iftrue代表的方法；否则，返回目标默认值
        var conditionItem = Condition(testItem, iftrue, Default(targetType));

        return conditionItem;
    }

    /// <summary>
    /// 将JSON字符串转换为对象（源属性为string类型时使用）
    /// </summary>
    /// <param name="sourceProperty">源对象属性表达式（JSON文本）</param>
    /// <param name="sourceType">源类型，必须为 string 类型（明确前置条件）</param>
    /// <param name="targetType">目标类型，反序列化后的对象T</param>
    /// <returns>条件表达式：sourceProperty 非空时反序列化为对象，否则返回 null</returns>
    [RequiresDynamicCode("GetObjectByJsonExpression uses MakeGenericMethod and reflection in the runtime fallback path. Not supported in AOT.")]
    [RequiresUnreferencedCode("GetObjectByJsonExpression scans type members and attributes via reflection. Some members may be trimmed.")]
    internal static Expression GetObjectByJsonExpression(Expression sourceProperty, Type sourceType, Type targetType)
    {
        // 前置条件：调用方保证 sourceType == typeof(string)，否则 GetMethod 查找会失败
        // B9如果 sourceType 不是 string，抛出明确异常而非 NullReferenceException
        if (sourceType != typeof(string))
        {
            throw new ArgumentException(
                ErrorMessages.JsonExpressionRequiresString(sourceType.Name));
        }

        //条件p.Item!=null
        var testItem = NotEqual(sourceProperty, Constant(null, sourceType));

        //呼叫指定类的指定方法
        var method = ReflectionHelper.GetRequiredMethod(typeof(JsonConverter), "JsonConvertToObject", new Type[] { sourceType }).MakeGenericMethod(targetType);
        var iftrue = Call(null, method, sourceProperty);

        //如果testItem符合条件，则执行iftrue代表的方法；否则，返回目标默认值
        // 使用 Default 而非 Constant(null)：值类型（int/DateTime/Guid）不允许 null 常量
        var conditionItem = Condition(testItem, iftrue, Default(targetType));

        return conditionItem;
    }

    /// <summary>
    /// 构建列表元素转换表达式（含空元素守卫；运行时回退路径，AOT 下不可用）
    /// </summary>
    /// <param name="sourceProperty">源集合元素表达式</param>
    /// <param name="sourceType">源元素类型</param>
    /// <param name="targetType">目标元素类型</param>
    /// <returns>列表元素转换表达式</returns>
    [RequiresDynamicCode("ConvertListExpression uses MakeGenericMethod and reflection in the runtime fallback path. Not supported in AOT.")]
    [RequiresUnreferencedCode("ConvertListExpression scans type members and attributes via reflection. Some members may be trimmed.")]
    internal static Expression ConvertListExpression(Expression sourceProperty, Type sourceType, Type targetType)
    {
        //条件p.Item!=null
        var testItem = NotEqual(sourceProperty, Constant(null, sourceType));

        Type[] makeTypes;
        string methodName;

        // 按集合类型分发到独立方法，降低圈复杂度
        if (sourceType.IsArray)
        {
            methodName = "CopyArray";
            makeTypes = new[] { sourceType };
        }
        else if (CollectionTypeDetector.IsGenericDictionary(sourceType))
        {
            (methodName, makeTypes) = ResolveDictionaryCopyMethod(sourceType);
        }
        else if (IsConcurrentCollection(sourceType, out var concurrentMethod))
        {
            methodName = concurrentMethod;
            makeTypes = sourceType.GetGenericArguments();
        }
        else if (IsReadOnlyCollection(sourceType, out var roMethod))
        {
            methodName = roMethod;
            makeTypes = sourceType.GetGenericArguments();
        }
        else if (sourceType.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
              && !CollectionTypeDetector.IsGenericDictionary(sourceType))
        {
            (methodName, makeTypes) = ResolveSingleValueCollectionMethod(sourceType);
        }
        else
        {
            // 其他未知集合 → 通用 IEnumerable<T> 构造器拷贝兜底（无构造器则浅拷贝返回原引用）
            methodName = "CopyEnumerable";
            makeTypes = new[] { sourceType };
        }

        MethodInfo? method = typeof(CollectionCopier).GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
        if (method == null)
            throw new NotSupportedException(ErrorMessages.UnknownCollectionType(sourceType.Name, methodName));

        method = method.MakeGenericMethod(makeTypes);

        // 方向 I：CopyArray/CopyList 为浅拷贝无 ctx 参数；其余集合拷贝方法需要 ctx 做循环引用检测
        Expression iftrue;
        if (methodName is "CopyArray" or "CopyList")
        {
            iftrue = Call(null, method, sourceProperty);
        }
        else
        {
            iftrue = Call(null, method, sourceProperty, Call(null, s_getOrCreateDeepCopyCtxMethod));
        }

        //如果testItem符合条件，则执行iftrue代表的方法；否则，返回目标默认值
        var conditionItem = Condition(testItem, iftrue, Constant(null, targetType));

        return conditionItem;

    }

    /// <summary>
    /// 解析字典类型集合的拷贝方法 — 从 ConvertListExpression 拆分
    /// <para>Dictionary/ConcurrentDictionary 走专用拷贝，其他走 CopyDictionaryByKeyValue</para>
    /// </summary>
    /// <param name="sourceType">源集合类型</param>
    /// <returns>方法名和泛型参数数组的元组</returns>
    private static (string MethodName, Type[] MakeTypes) ResolveDictionaryCopyMethod(Type sourceType)
    {
        var genericDef = sourceType.IsGenericType ? sourceType.GetGenericTypeDefinition() : null;
        // 已知高频类型走专用深拷贝（含复杂类型元素的 JSON 深拷贝）
        if (genericDef == typeof(Dictionary<,>))
        {
            return ("CopyDictionary", sourceType.GetGenericArguments());
        }
        if (genericDef == typeof(System.Collections.Concurrent.ConcurrentDictionary<,>))
        {
            return ("CopyConcurrentDictionary", sourceType.GetGenericArguments());
        }
        // SortedDictionary/SortedList/ReadOnlyDictionary 等 → 通用 IDictionary<K,V> 构造器拷贝
        return ("CopyDictionaryByKeyValue", new[] { sourceType });
    }

    /// <summary>
    /// 判断是否为 ConcurrentQueue/ConcurrentStack 并返回对应拷贝方法名 — 从 ConvertListExpression 拆分
    /// </summary>
    /// <param name="sourceType">源集合类型</param>
    /// <param name="methodName">输出的方法名（CopyConcurrentQueue / CopyConcurrentStack）</param>
    /// <returns>true 表示是 Concurrent 集合；false 表示不是</returns>
    private static bool IsConcurrentCollection(Type sourceType, out string methodName)
    {
        if (sourceType.IsGenericType && sourceType.GetGenericTypeDefinition() == typeof(System.Collections.Concurrent.ConcurrentQueue<>))
        {
            methodName = "CopyConcurrentQueue";
            return true;
        }
        if (sourceType.IsGenericType && sourceType.GetGenericTypeDefinition() == typeof(System.Collections.Concurrent.ConcurrentStack<>))
        {
            methodName = "CopyConcurrentStack";
            return true;
        }
        methodName = "";
        return false;
    }

    /// <summary>
    /// 判断是否为 ReadOnlyCollection 并返回对应拷贝方法名 — 从 ConvertListExpression 拆分
    /// </summary>
    /// <param name="sourceType">源集合类型</param>
    /// <param name="methodName">输出的方法名（CopyReadOnlyCollection）</param>
    /// <returns>true 表示是 ReadOnlyCollection；false 表示不是</returns>
    private static bool IsReadOnlyCollection(Type sourceType, out string methodName)
    {
        if (sourceType.IsGenericType && sourceType.GetGenericTypeDefinition() == typeof(System.Collections.ObjectModel.ReadOnlyCollection<>))
        {
            methodName = "CopyReadOnlyCollection";
            return true;
        }
        methodName = "";
        return false;
    }

    /// <summary>
    /// 解析单值泛型集合的拷贝方法 — 从 ConvertListExpression 拆分
    /// <para>List/HashSet/Queue/Stack 走专用拷贝，其他走 CopyEnumerable</para>
    /// </summary>
    /// <param name="sourceType">源集合类型</param>
    /// <returns>方法名和泛型参数数组的元组</returns>
    private static (string MethodName, Type[] MakeTypes) ResolveSingleValueCollectionMethod(Type sourceType)
    {
        var genericDef = sourceType.IsGenericType ? sourceType.GetGenericTypeDefinition() : null;
        if (genericDef == typeof(List<>))
        {
            return ("CopyList", sourceType.GetGenericArguments());
        }
        if (genericDef == typeof(HashSet<>))
        {
            return ("CopyHashSet", sourceType.GetGenericArguments());
        }
        if (genericDef == typeof(Queue<>))
        {
            return ("CopyQueue", sourceType.GetGenericArguments());
        }
        if (genericDef == typeof(Stack<>))
        {
            return ("CopyStack", sourceType.GetGenericArguments());
        }
        // SortedSet/LinkedList/ObservableCollection/ConcurrentBag 等 → 通用 IEnumerable<T> 构造器拷贝
        return ("CopyEnumerable", new[] { sourceType });
    }

    /// <summary>
    /// 跨类型集合转换（数组/列表内部元素为class时，调用Mapper本身进行递归映射）
    /// </summary>
    /// <param name="sourceProperty">源集合属性表达式</param>
    /// <param name="sourceType">源集合类型</param>
    /// <param name="targetType">目标集合类型</param>
    /// <returns>条件表达式：sourceProperty 非空时递归映射列表元素，否则返回 null</returns>
    [RequiresDynamicCode("GetListExpression uses MakeGenericType and reflection in the runtime fallback path. Not supported in AOT.")]
    [RequiresUnreferencedCode("GetListExpression scans type members and attributes via reflection. Some members may be trimmed.")]
    internal static Expression GetListExpression(Expression sourceProperty, Type sourceType, Type targetType)
    {
        //条件p.Item!=null
        var testItem = NotEqual(sourceProperty, Constant(null, sourceType));

        // B3 修复：List/数组 → Dictionary 类型不兼容，生成运行时抛异常的表达式
        // 不能在编译时 throw（会导致静态构造失败 → TypeInitializationException），
        // 也不能继续走 MakeGenericType（Dictionary 首泛型参数可能不满足 class,new() 约束 → ArgumentException）
        if (typeof(IDictionary).IsAssignableFrom(targetType) && !typeof(IDictionary).IsAssignableFrom(sourceType))
        {
            // 使用 ErrorMessages 统一错误消息
            var msg = ErrorMessages.UnsupportedListToDictionaryConversion(sourceType.Name, targetType.Name);
            var exCtor = typeof(NotSupportedException).GetConstructor(new[] { typeof(string) })!;
            var throwExpr = Throw(New(exCtor, Constant(msg)), targetType);
            return Condition(testItem, throwExpr, Constant(null, targetType));
        }

        // W2-1 ④（P1-SHOULD-1 消除）：集合递归 — 委托工厂按实际类型对（集合元素类型对）
        // Resolve 后构造 MapList(source, null) 调用表达式（sourceArg/targetArg 推导保留现状；
        // 生成的表达式树在运行时直接调用 MapperBase.MapList 委托，无反射开销——语义不变）
        // ：非泛型集合派生类
        // （class OrderList : List<string>）自身 GetGenericArguments 为空——原 [0] 抛
        // IndexOutOfRange，构建层捕获后属性绑定被静默跳过（实测 dto.Items=null）。改经
        // 接口链提取元素类型（NavigationResolver.GetCollectionElementType，与深拷贝侧
        // 同口径）；仅实现非泛型 IEnumerable 的形态返回 null，由下游调用方兜底（行为不变）
        var sourceArg = sourceType.IsArray ? sourceType.GetElementType()
            : sourceType.IsGenericType ? sourceType.GetGenericArguments()[0]
            : NavigationResolver.GetCollectionElementType(sourceType);
        // 目标侧补非泛型第三档（对齐 sourceArg 口径）
        // 非泛型集合/字典目标（ArrayList、非泛型派生类等）原 GetGenericArguments()[0] 构建期
        // IndexOutOfRange 裸抛；接口链可提取时正常分派，不可提取时友好异常。
        var targetArg = targetType.IsArray ? targetType.GetElementType()
            : targetType.IsGenericType ? targetType.GetGenericArguments()[0]
            : NavigationResolver.GetCollectionElementType(targetType);
        if (sourceArg == null || targetArg == null)
        {
            // 元素类型不可提取（仅实现非泛型 IEnumerable 的形态）——友好异常替代 IndexOutOfRange 裸抛
            // 展示类型取非空侧：sourceArg 可能为 null（sourceArg==null 同样进入本分支），须兜底 targetType
            var displayType = targetArg ?? sourceArg ?? targetType;
            throw new MapConfigurationException(
                ErrorMessages.UnknownCollectionTypeNoMethod(displayType.FullName!));
        }
        // 元素对分类前置判定——按 targetArg.IsValueType 严格二分（无"不可判定"兜底档）。
        // 档1：targetArg 引用类型（类/接口/string/数组元素）→ 现状 Resolve（保留 bootstrap 语义；覆盖接口元素对
        //      与值类型源×class 目标合法注册对——TSource 无约束，单约束下仍可注册）；
        // 档2c：targetArg 值类型且 sourceArg 引用类型（混合对，如 string→int）——MapperBase<,> 单约束
        //      （where TTarget : class，MapperBase.cs:17）下类型系统注定不可注册，构建期直接抛双语专用异常
        //      （不复用 MapperDelegateNotRegistered，其"静态构造未能完成注册"语义误导）；
        // 档2a/2b：双方值类型 → 逐元素转换管线（隐式可转换强转 / 不可隐式转换 ConvertValue，见管线内注释）
        Expression mapperExecMap;
        if (!targetArg!.IsValueType)
        {
            var elementDelegate = MapperDelegateRegistry.Resolve(sourceArg!, targetArg);
            mapperExecMap = elementDelegate.MapList(sourceProperty);
        }
        else if (!sourceArg!.IsValueType)
        {
            // 混合对：明确失败优于隐式猜测（不尝试 string→int 等 ConvertValue 语义转换——未注册类型对无映射语义）
            throw new MapConfigurationException(
                ErrorMessages.MixedElementTypePairNotMappable(sourceArg.Name, targetArg.Name));
        }
        else
        {
            mapperExecMap = BuildValueTypeElementPipeline(sourceProperty, sourceArg, targetArg);
        }

        Expression iftrue;
        if (targetType == mapperExecMap.Type)
        {
            iftrue = mapperExecMap;
        }
        else if (targetType.IsArray)//数组类型调用ToArray()方法
        {
            iftrue = Call(typeof(Enumerable), nameof(Enumerable.ToArray), new[] { mapperExecMap.Type.GenericTypeArguments[0] }, mapperExecMap);
        }
        else if (typeof(IDictionary).IsAssignableFrom(targetType))
        {
            // B3 修复：List/数组 → Dictionary 类型不兼容，抛异常而非静默置 null 导致数据丢失
            // 使用 ErrorMessages 统一错误消息
            var msg = ErrorMessages.UnsupportedListToDictionaryConversion(sourceType.Name, targetType.Name);
            var exCtor = typeof(NotSupportedException).GetConstructor(new[] { typeof(string) })!;
            iftrue = Throw(New(exCtor, Constant(msg)), targetType);
        }
        else
        {
            // 目标容器三档分派（判定顺序：引用转换档必须先于构造器档，
            // 接口/基类兼容目标（IList<T>/IEnumerable<T> 等 List<T> 可实现）无 IEnumerable<T> 构造器，
            // 误入构造器档即接口目标回归）
            if (targetType.IsAssignableFrom(mapperExecMap.Type))
            {
                // 档1：引用转换可达（接口/基类兼容目标）→ 维持现状 Convert（防回归）
                iftrue = Convert(mapperExecMap, targetType);
            }
            else
            {
                // 档2：IEnumerable<T> 单参构造器（HashSet/Queue/Stack/SortedSet/ObservableCollection 等）——
                // 构造器解析仅构建期反射，运行时表达式零反射（GetListExpression 已有 Requires* 方法级标注）
                var ctor = targetType.GetConstructor(new[] { typeof(IEnumerable<>).MakeGenericType(targetArg!) });
                if (ctor != null)
                {
                    Expression ctorArg = mapperExecMap;
                    if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(Stack<>))
                    {
                        // Stack<T> 构造参数包 Reverse（Pop 语义对齐：枚举序栈顶→栈底，构造器 Push 后新栈顶=枚举末尾，
                        // 直接构造会反转——与生成侧 CodeGenerator.Collections 的 SimpleName=="Stack" 换向发射同构；
                        // ConcurrentStack<> 维持现状不包 Reverse（与生成侧 SimpleName=="Stack" 精确匹配对齐））
                        ctorArg = Call(typeof(Enumerable), nameof(Enumerable.Reverse), new[] { targetArg! }, mapperExecMap);
                    }
                    iftrue = New(ctor, ctorArg);
                }
                else
                {
                    // 档3：引用转换与构造器均不可达（无构造器自定义集合）→ 双语 NotSupportedException
                    //（替换裸 Convert 对具体容器目标的构建期 InvalidOperationException 崩溃）
                    var msg = ErrorMessages.UnknownCollectionTypeNoMethod(targetType.Name);
                    var exCtor = typeof(NotSupportedException).GetConstructor(new[] { typeof(string) })!;
                    iftrue = Throw(New(exCtor, Constant(msg)), targetType);
                }
            }
        }

        var conditionItem = Condition(testItem, iftrue, Constant(null, targetType));

        return conditionItem;
    }

    /// <summary>
    /// 值类型元素对逐元素转换管线
    /// 双方均为值类型的集合元素对（如 List&lt;int&gt;→List&lt;long&gt; 属性级），
    /// 按可隐式转换性分流：命中隐式转换表 → 逐元素强转（Expression.Convert，零反射）；
    /// 不可隐式转换（如 long→int 显式转换）→ 逐元素 TypeConvertHelper.ConvertValue
    /// （ChangeType 支持显式转换、溢出吞后抛 MapException，与生成侧 F-04 路径②同构）。
    /// </summary>
    /// <param name="sourceProperty">源集合属性表达式</param>
    /// <param name="sourceArg">源元素类型（值类型）</param>
    /// <param name="targetArg">目标元素类型（值类型）</param>
    /// <returns>new List&lt;targetArg&gt;(Enumerable.Select(source, e => 转换(e))) 表达式——
    /// 产出类型契约 List&lt;targetArg&gt; 与档1 MapList 委托产出一致，下游数组 ToArray 与容器适配分支依赖此契约</returns>
    [RequiresDynamicCode("BuildValueTypeElementPipeline uses MakeGenericType and generic Expression.Call (Select/ConvertValue). Not supported in AOT.")]
    [RequiresUnreferencedCode("BuildValueTypeElementPipeline calls TypeConvertHelper.ConvertValue which resolves types at runtime. Some members may be trimmed.")]
    private static Expression BuildValueTypeElementPipeline(Expression sourceProperty, Type sourceArg, Type targetArg)
    {
        var elemParam = Parameter(sourceArg, "e");
        Expression elemBody = TypeConverter.IsImplicitConvertible(sourceArg, targetArg)
            ? Convert(elemParam, targetArg)
            : Call(typeof(TypeConvertHelper), nameof(TypeConvertHelper.ConvertValue),
                new[] { sourceArg, targetArg }, elemParam);
        var selectCall = Call(typeof(Enumerable), nameof(Enumerable.Select),
            new[] { sourceArg, targetArg }, sourceProperty, Lambda(elemBody, elemParam));
        var listCtor = typeof(List<>).MakeGenericType(targetArg)
            .GetConstructor(new[] { typeof(IEnumerable<>).MakeGenericType(targetArg) })!;
        return New(listCtor, selectCall);
    }

    /// <summary>
    /// 字典类型的转换（针对双值集合且元素类型不同的，通过 ConvertDictionary 递归映射）
    /// </summary>
    /// <param name="sourceProperty">源字典属性表达式</param>
    /// <param name="sourceType">源字典类型</param>
    /// <param name="targetType">目标字典类型</param>
    /// <returns>条件表达式：sourceProperty 非空时调用 ConvertDictionary，否则返回 null</returns>
    [RequiresDynamicCode("GetDictionaryExpression uses MakeGenericMethod and reflection in the runtime fallback path. Not supported in AOT.")]
    [RequiresUnreferencedCode("GetDictionaryExpression scans type members and attributes via reflection. Some members may be trimmed.")]
    internal static Expression GetDictionaryExpression(Expression sourceProperty, Type sourceType, Type targetType)
    {
        //条件p.Item!=null
        var testItem = NotEqual(sourceProperty, Constant(null, sourceType));
        var targetTypes = targetType.GetGenericArguments();

        // 删除不可达防御分支（接口判断已保证目标为 Dictionary<K,V>；原分支为 Speculative Generality）

        // 获取原始 IDictionary 实现，确保任何实现了 IDictionary 的类型都能处理
        var dictInterface = sourceType.GetInterface("IDictionary");
        // 接口字典声明源（IDictionary<K,V>/IReadOnlyDictionary<K,V>
        // 自身）不实现非泛型 IDictionary，GetInterface 返回 null —— 原分支直接抛 NotSupportedException；
        // 收敛口径后 IsDictionary(接口)=true 分派到本方法，改经 object 中转双步转换到非泛型 IDictionary
        //（接口声明的运行时实例必为实现非泛型 IDictionary 的具体字典类），走字典转换路径而非构建期失败
        Expression castedSource;
        if (dictInterface != null)
        {
            castedSource = Convert(sourceProperty, dictInterface);
        }
        else if (CollectionTypeDetector.IsDictionary(sourceType))
        {
            castedSource = Convert(Convert(sourceProperty, typeof(object)), typeof(System.Collections.IDictionary));
        }
        else
        {
            throw new NotSupportedException(ErrorMessages.UnknownCollectionTypeNoMethod(sourceType.Name));
        }

        // 统一走 ConvertDictionary 方法（接收 IDictionary 参数）
        var method = ReflectionHelper.GetRequiredMethod(typeof(CollectionCopier), "ConvertDictionary")
            .MakeGenericMethod(targetTypes[0], targetTypes[1]);

        var iftrue = Call(null, method!, castedSource);

        //如果testItem符合条件，则执行iftrue代表的方法；否则，返回目标默认值
        var conditionItem = Condition(testItem, iftrue, Constant(null, targetType));

        return conditionItem;
    }
}
