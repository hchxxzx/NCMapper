using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Dapper;
using NCMapper;

namespace NCMapper.Dapper;

// DapperDtoSelector.Execution.cs — 委托编译关切（分家，partial 拆分）
// 内容：CompileMapListDelegate（映射委托编译，服务 MapListCached 列表映射缓存）
// T-4 双轨归一（决策 A）：CompileQueryDelegate / CompileQueryAsyncDelegate /
// CompileQuerySingleDelegate(×2) / CompileQuerySingleAsyncDelegate 五个查询委托编译方法已删除
// ——字符串版入口
// 改为泛型类内直调 Dapper（编译期实例化），s_mapListCache 映射委托保留。
// static 字段（s_mapListCache + s_mapListMethod + 反射缓存 2 个）留守门面主文件（B-1 字段留守定案）。

public sealed partial class DapperDtoSelector<TSource> where TSource : class
{
    [RequiresDynamicCode("CompileMapListDelegate compiles the MapList delegate via MakeGenericMethod/MakeGenericType and Expression.Compile. Not supported in AOT.")]
    [RequiresUnreferencedCode("CompileMapListDelegate resolves the generic MapList method via MakeGenericMethod. Some members may be trimmed.")]
    private static Func<IEnumerable<TSource>, IEnumerable<object>> CompileMapListDelegate<TDto>() where TDto : class
    {
        var method = s_mapListMethod.MakeGenericMethod(typeof(TSource), typeof(TDto));
        var entitiesP = System.Linq.Expressions.Expression.Parameter(typeof(IEnumerable<TSource>), "e");
        var nullAction = System.Linq.Expressions.Expression.Constant(null,
            typeof(Action<,>).MakeGenericType(typeof(TSource), typeof(TDto)));
        var call = System.Linq.Expressions.Expression.Call(null, method, entitiesP, nullAction);
        var castResult = System.Linq.Expressions.Expression.Convert(call, typeof(IEnumerable<object>));
        return System.Linq.Expressions.Expression
            .Lambda<Func<IEnumerable<TSource>, IEnumerable<object>>>(castResult, entitiesP).Compile();
    }
}
