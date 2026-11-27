// TB-20261127-01 卡 C1（路线四）—— Compose 通道「用户 join 源参数」作用域（共享内核侧）
//
// 问题：Compose 投影的嵌套 DTO 源解析走 NavigationResolver.ResolveNavigationProperty
//       （按 [Navigate] 外键重新推导关联），完全绕过用户写的 join 参数 t2/t3，于是
//       FreeSql 另建第二条 LEFT JOIN —— 用户 join 上的软删过滤 / 非主键条件对它不生效
//       （缺陷③：软删泄漏／T1 无导航丢数据／非主键 join 串行）。
//
// 修法（路线四）：让嵌套源改指「用户 join 的那个源参数」。参数集合
//       （Type[] sourceTypes / ParameterExpression[] parameters）只在
//       ComposeProjectionKernel.BuildComposeCoreN 的作用域内建出来，共享内核的
//       四处 ResolveNavigationProperty 调用点拿不到它 —— 故本章以「环境作用域」
//       承接 Compose 侧的一次性下传（kernel 内仅一行 Push，见卡交付说明）。
//
// 两级 API（区分「配置错」与「合法回退」）：
//   · Soft  ：0 命中返回 null（调用方决定回退导航/保持原样）；多命中仍 fail-closed（绝不猜）。
//   · Strict：0 命中即 fail-closed 抛异常 —— 用于「元数据已声明该源类型、但用户没 join」这类
//             配置错，必须抛异常，不得静默返回 null 或常量空壳。
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Threading;

namespace NCMapper.Internal
{
    /// <summary>
    /// Compose 通道用户 join 源参数作用域 —— 构建期环境态（AsyncLocal，嵌套/并发安全）。
    /// <para>仅在 <c>ComposeProjectionKernel.BuildComposeCoreN</c> 的 bindings 构建期间存在；
    /// 单表 <c>AsDto</c>／EF Core／Dapper 三条既有路径不 Push，故行为逐字不变（C1 回归红线）。</para>
    /// <para>作用域内容 = 本次查询的源类型数组与其参数表达式（t1/t2/t3…），顺序即用户
    /// <c>Select&lt;T1,T2[,T3]&gt;</c> 的泛型参数顺序。</para>
    /// </summary>
    internal sealed class ComposeNestedSourceScope : IDisposable
    {
        /// <summary>
        /// 当前作用域（AsyncLocal：构建是同步的，但保证异步调用链上不漏不掉；
        /// 嵌套 Push 形成链，Dispose 逐层还原父作用域）。
        /// </summary>
        private static readonly AsyncLocal<ComposeNestedSourceScope?> s_current = new();

        private readonly ComposeNestedSourceScope? _parent;
        private readonly Type[] _sourceTypes;
        private readonly ParameterExpression[] _parameters;
        private bool _disposed;
        /// <summary>
        /// 空作用域（屏蔽标记）：为 true 时本作用域不可用，
        /// 使外层 Compose 作用域对本段构建立即失效（单表 <c>Build</c> 的单源语义隔离）。
        /// </summary>
        private readonly bool _suppressed;

        private ComposeNestedSourceScope(
            ComposeNestedSourceScope? parent, Type[] sourceTypes, ParameterExpression[] parameters,
            bool suppressed = false)
        {
            _parent = parent;
            _sourceTypes = sourceTypes;
            _parameters = parameters;
            _suppressed = suppressed;
        }

        /// <summary>
        /// 当前是否处于可用的 Compose 构建作用域内（单表/EFCore/Dapper 恒 false）。
        /// </summary>
        internal static bool IsActive => s_current.Value is { IsUsable: true };

        /// <summary>
        /// 本作用域是否可用于「改指 join 参数」：未释放、且非空作用域（屏蔽标记）。
        /// </summary>
        private bool IsUsable => !_disposed && !_suppressed;

        /// <summary>
        /// 压入作用域 —— 由 <c>ComposeProjectionKernel.BuildComposeCoreN</c> 在 bindings 循环前调用一次，
        /// <c>using</c> 释放即出栈。
        /// </summary>
        /// <param name="sourceTypes">本次 Compose 查询的源类型数组（顺序 = 泛型参数顺序）</param>
        /// <param name="parameters">与 <paramref name="sourceTypes"/> 一一对应的参数表达式（t1/t2/t3…）</param>
        internal static ComposeNestedSourceScope Push(Type[] sourceTypes, ParameterExpression[] parameters)
        {
            ArgumentNullException.ThrowIfNull(sourceTypes);
            ArgumentNullException.ThrowIfNull(parameters);
            if (sourceTypes.Length != parameters.Length)
                throw new ArgumentException("sourceTypes 与 parameters 长度必须一致。", nameof(parameters));

            var scope = new ComposeNestedSourceScope(s_current.Value, sourceTypes, parameters);
            s_current.Value = scope;
            return scope;
        }

        /// <summary>
        /// 压入一个「空作用域」—— 显式屏蔽外层 Compose 作用域。
        /// <para>用于单表 <c>Build</c>（单源语义）等绝不能继承外层 Compose join 参数的场景：
        /// 空作用域 <see cref="IsActive"/> 恒 false、解析全部返回 null，行为等同「不在作用域内」。</para>
        /// </summary>
        internal static ComposeNestedSourceScope PushEmpty()
        {
            var scope = new ComposeNestedSourceScope(
                s_current.Value, Array.Empty<Type>(), Array.Empty<ParameterExpression>(), suppressed: true);
            s_current.Value = scope;
            return scope;
        }

        /// <summary>
        /// 出栈还原父作用域（幂等：重复 Dispose 无副作用）。
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            // 仅当自己是栈顶时还原（乱序 Dispose 时不破坏上层作用域）
            if (ReferenceEquals(s_current.Value, this))
                s_current.Value = _parent;
        }

        // ------------------------------------------------------------ 查找

        /// <summary>
        /// 在作用域内按「源类型」查找唯一匹配的用户 join 源参数。
        /// <para>0 命中 → false；多命中（多源同类型，无法唯一确定是哪一个 join）→ fail-closed 抛异常。</para>
        /// </summary>
        private bool TryFindByType(Type desiredSourceType, out ParameterExpression? parameter)
        {
            parameter = null;
            ParameterExpression? found = null;
            var hits = 0;
            for (var i = 0; i < _parameters.Length; i++)
            {
                if (_parameters[i].Type != desiredSourceType) continue;
                hits++;
                found ??= _parameters[i];
            }

            if (hits == 1)
            {
                parameter = found;
                return true;
            }
            if (hits > 1)
                throw FailClosedAmbiguous(desiredSourceType.Name, hits);

            return false;
        }

        /// <summary>
        /// 在作用域内按「编译期类型名」查找唯一匹配的用户 join 源参数（对齐 Compose 明细
        /// <c>SubSourceTypeRef</c> 缺失时的按名解析口径：符号显示串与运行时 FullName 归一后比较）。
        /// </summary>
        private bool TryFindByName(string compileTimeTypeName, out ParameterExpression? parameter)
        {
            parameter = null;
            ParameterExpression? found = null;
            var hits = 0;
            for (var i = 0; i < _parameters.Length; i++)
            {
                if (!NamesMatchRuntimeNormalized(_parameters[i].Type.FullName, compileTimeTypeName)) continue;
                hits++;
                found ??= _parameters[i];
            }

            if (hits == 1)
            {
                parameter = found;
                return true;
            }
            if (hits > 1)
                throw FailClosedAmbiguous(compileTimeTypeName, hits);

            return false;
        }

        /// <summary>
        /// 运行时归一全名匹配（与 Compose 明细按名解析同口径；嵌套类型 '+' 与 '.' 归一）。
        /// </summary>
        private static bool NamesMatchRuntimeNormalized(string? runtimeFullName, string? compileTimeDisplayName)
        {
            if (runtimeFullName is null || compileTimeDisplayName is null) return false;
            if (string.Equals(runtimeFullName, compileTimeDisplayName, StringComparison.Ordinal)) return true;
            return string.Equals(
                runtimeFullName.Replace('+', '.'),
                compileTimeDisplayName.Replace('+', '.'),
                StringComparison.Ordinal);
        }

        /// <summary>
        /// 【Soft／按类型】解析用户 join 源参数：不在作用域或 0 命中 → null；多命中 → fail-closed。
        /// <para>用于「嵌套 DTO 主源 == 当前容器源类型」这类合法回退场景：调用方拿到 null 后
        /// 继续走既有导航推断，不得视为配置错。</para>
        /// </summary>
        /// <param name="desiredSourceType">期望的源侧类型</param>
        /// <returns>命中的参数表达式；未命中为 null</returns>
        internal static Expression? TryFindJoinedSource(Type desiredSourceType)
        {
            var scope = s_current.Value;
            if (scope is not { IsUsable: true }) return null;
            return scope.TryFindByType(desiredSourceType, out var parameter) ? parameter : null;
        }

        /// <summary>
        /// 【Strict／按类型】解析用户 join 源参数：不在作用域 → null（调用方保持既有导航语义）；
        /// 作用域内 0 命中或多命中 → <b>fail-closed 抛异常</b>（不得静默 null/空壳）。
        /// <para>用于「元数据已声明该源类型」的跨表声明面与嵌套 DTO 主源面：声明了却没 join，
        /// 属配置错，必须让调用方看到可定位的异常而不是静默空值。</para>
        /// </summary>
        /// <param name="desiredSourceType">期望的源侧类型</param>
        /// <param name="targetName">目标属性/目标 DTO 名（Trace 与异常消息用）</param>
        /// <param name="tracePrefix">Trace 前缀</param>
        /// <returns>命中的参数表达式；不在作用域内为 null</returns>
        internal static Expression? RequireJoinedSource(
            Type desiredSourceType, string? targetName, string tracePrefix)
        {
            var scope = s_current.Value;
            if (scope is not { IsUsable: true }) return null;

            if (scope.TryFindByType(desiredSourceType, out var parameter))
                return parameter;

            throw scope.FailClosedMissing(desiredSourceType.Name, targetName, tracePrefix);
        }

        /// <summary>
        /// 【Strict／按名】语义同 <see cref="RequireJoinedSource"/>，按编译期类型名解析。
        /// </summary>
        /// <param name="compileTimeTypeName">编译期发射的源类型全名（符号显示串）</param>
        /// <param name="targetName">目标属性/目标 DTO 名（Trace 与异常消息用）</param>
        /// <param name="tracePrefix">Trace 前缀</param>
        /// <returns>命中的参数表达式；不在作用域内为 null</returns>
        internal static Expression? RequireJoinedSourceByName(
            string compileTimeTypeName, string? targetName, string tracePrefix)
        {
            var scope = s_current.Value;
            if (scope is not { IsUsable: true }) return null;

            if (scope.TryFindByName(compileTimeTypeName, out var parameter))
                return parameter;

            throw scope.FailClosedMissing(compileTimeTypeName, targetName, tracePrefix);
        }

        /// <summary>
        /// 嵌套投影入口的源改指 —— 作用域内把「按导航链取到的源表达式」换成用户 join 的源参数。
        /// <para>必要性：注册表自动匹配出的嵌套条目走 <c>t1.Nav</c> 属性访问，不经
        /// <c>ResolveNavigationProperty</c>；若不在嵌套投影入口改指，ORM 仍会自建第二条 JOIN。</para>
        /// <para><b>fail-closed 的触发条件（复审后收窄）</b>：仅当源类型不在用户 join 参数集、
        /// <b>且</b><paramref name="nestingDepth"/> &gt; 1（该次投影已不是顶层一跳，而是二跳及更深）
        /// 时抛 <see cref="MapConfigurationException"/>。</para>
        /// <para>收窄理由（均有实测支撑）：FreeSql 对**单跳**导航自建 JOIN 完全支持——单跳嵌套、
        /// 源未 join 的查询在收窄前能正常返回业务值；而**多跳**（嵌套里再嵌套）时 provider 会在
        /// <c>t2.Company</c> 这类导航链上无限递归直至 <c>StackOverflowException</c>
        /// （进程级崩溃、catch 不住，实测 Repeated 27 times: ExpressionLambdaToSql ↔ ExpressionBinary）。
        /// 故只在真正会崩的形态下 fail-closed，单跳保持旧 Soft 回退，不改动既有可用形态。</para>
        /// <para>★ 阈值取 1 的实测依据：顶层属性以 <c>ConvertProjectionType(depth:0)</c> 进入，
        /// 单跳 <c>BuildNestedProjection(depth:1)</c>；双层的最内层为 <c>depth:3</c>。
        /// 因此 <c>&gt; 1</c> 恰好区分「单跳（可自建 JOIN）」与「多跳（会栈溢出）」。</para>
        /// </summary>
        /// <param name="nestedSourceType">嵌套 DTO 的源类型（= 嵌套投影的 <c>srcType</c>）</param>
        /// <param name="sourceExpr">原源表达式（导航链或参数）</param>
        /// <param name="targetName">目标属性/目标 DTO 名（异常消息用）</param>
        /// <param name="tracePrefix">Trace 前缀</param>
        /// <param name="nestingDepth">
        /// 当前嵌套投影深度（顶层属性单跳为 1，其内层递增）。仅 &gt; 1 时对「源不在参数集」
        /// fail-closed；单跳（1）保持旧 Soft 回退，不改变既有可用形态。
        /// </param>
        /// <returns>作用域内的用户 join 源参数；未命中且单跳或作用域外时原样返回</returns>
        internal static Expression RedirectNestedSource(
            Type nestedSourceType, Expression sourceExpr, string? targetName = null,
            string tracePrefix = "", int nestingDepth = 1)
        {
            var scope = s_current.Value;
            if (scope is not { IsUsable: true }) return sourceExpr;

            // 源表达式本身已是本作用域的 join 参数 → 无需改指
            if (sourceExpr is ParameterExpression && scope.IsOwnParameter(sourceExpr))
                return sourceExpr;

            if (scope.TryFindByType(nestedSourceType, out var parameter))
                return parameter!;

            // 单跳嵌套：FreeSql 支持自建 JOIN（实测可返回业务值），保持旧 Soft 回退
            if (nestingDepth <= 1) return sourceExpr;

            // 多跳嵌套 + 源不在参数集：交给 provider 会无限递归直到栈溢出 → fail-closed
            throw scope.FailClosedMissing(nestedSourceType.Name, targetName, tracePrefix);
        }

        /// <summary>
        /// 判断表达式是否为本作用域的参数之一（防止把已改指结果再次替换/误判）。
        /// </summary>
        private bool IsOwnParameter(Expression expr)
        {
            for (var i = 0; i < _parameters.Length; i++)
            {
                if (ReferenceEquals(_parameters[i], expr)) return true;
            }
            return false;
        }

        // ------------------------------------------------------------ fail-closed

        /// <summary>
        /// 源类型不在源参数集 → fail-closed 硬约束：
        /// 匹配不到必须抛异常，不得静默返回 null 或常量空壳。
        /// </summary>
        [DoesNotReturn]
        internal Exception FailClosedMissing(string desiredSourceTypeName, string? targetName, string tracePrefix)
        {
            var typeList = string.Join(", ", _sourceTypes.Select(t => t.Name));
            var target = targetName ?? "(未知)";
            var message =
                $"NCMapper Compose 嵌套源参数解析失败：{target} 需要的源类型 " +
                $"{desiredSourceTypeName} 不在本次查询的用户 join 源参数集（{typeList}）中。" +
                "路线四要求嵌套 DTO 的取值来自用户 join 的那个源参数，不再按 [Navigate] 自建 JOIN；" +
                "请把该源类型加入 Select 泛型参数并显式 LeftJoin/InnerJoin，或在映射声明中改指已 join 的源类型。";
            MapTracer.Write($"{tracePrefix} 嵌套源参数解析失败（fail-closed）：{target} 的源类型 " +
                $"{desiredSourceTypeName} 不在用户 join 源参数集（{typeList}）中");
            throw new MapConfigurationException(message);
        }

        /// <summary>
        /// 多源同类型（同一类型在源参数集中出现多次）→ fail-closed（不猜是哪一个 join）。
        /// </summary>
        [DoesNotReturn]
        private Exception FailClosedAmbiguous(string desiredSourceTypeName, int hits)
        {
            var typeList = string.Join(", ", _sourceTypes.Select(t => t.Name));
            var message =
                $"NCMapper Compose 嵌套源参数解析失败：源类型 {desiredSourceTypeName} 在本次查询的用户 join " +
                $"源参数集中出现 {hits} 次（{typeList}），无法唯一确定嵌套 DTO 应取哪一个源参数。" +
                "请避免同一映射对的多个源参数为同一类型，或改用 [MapProperty(FromSource=...)] 明确指定。";
            MapTracer.Write(message);
            throw new MapConfigurationException(message);
        }
    }
}


