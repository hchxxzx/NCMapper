using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Dapper;
using NCMapper;

namespace NCMapper.Dapper;

// DapperDtoSelector.MultiResult.cs — 多结果集关切（分家，partial 拆分）
// 内容：QueryMultiple（2/3 结果集）+ ReadByType/MapListByType 反射读集
// static 字段（s_readByTypeCache/s_mapListByTypeCache）与 pragma 块留守门面主文件 DapperDtoSelector.cs（B-1 字段留守定案）

public sealed partial class DapperDtoSelector<TSource> where TSource : class
{
    /// <summary>
    /// 多结果集查询 — 一次 SQL 返回多个结果集，各自独立映射为 DTO 元组
    /// <para>主结果集（TDto1）使用 Selector 的 TSource 泛型参数读取和映射</para>
    /// <para>次结果集（TDto2/TDto3）通过 TDto 上的 [MapFrom] 推断源类型</para>
    /// <para>各结果集独立映射，不涉及 Mapper.Compose</para>
    /// </summary>
    /// <typeparam name="TDto1">主结果集 DTO 类型</typeparam>
    /// <typeparam name="TDto2">第二结果集 DTO 类型</typeparam>
    /// <param name="sql">SQL 语句（含多条 SELECT）</param>
    /// <param name="param">参数</param>
    /// <param name="transaction">事务</param>
    /// <param name="commandTimeout">命令超时</param>
    /// <param name="commandType">命令类型</param>
    /// <returns>元组 (主结果集列表, 第二结果集列表)</returns>
    [RequiresDynamicCode("QueryMultiple uses Expression.Compile() for the mapping delegate. Not supported in AOT. [MapFrom] makes mapping AOT-safe but delegate compilation still requires dynamic code.")]
    [RequiresUnreferencedCode("QueryMultiple scans attributes via reflection.")]
    public (List<TDto1> Items1, List<TDto2> Items2) QueryMultiple<TDto1, TDto2>(
        string sql, object? param = null,
        IDbTransaction? transaction = null, int? commandTimeout = null,
        CommandType? commandType = null)
        where TDto1 : class where TDto2 : class
    {
        using var grid = _connection.QueryMultiple(sql, param, transaction, commandTimeout, commandType);

        // 主结果集：使用 Selector 的 TSource
        var entities1 = grid.Read<TSource>().ToList();
        var items1 = MapListCached<TDto1>(entities1).Cast<TDto1>().ToList();

        // 次结果集：从 [MapFrom] 推断源类型
        var sourceType2 = ResolveSourceTypeFromMapFrom(typeof(TDto2));
        var entities2 = ReadByType(sourceType2, grid);
        var items2 = MapListByType(sourceType2, typeof(TDto2), entities2).Cast<TDto2>().ToList();

        return (items1, items2);
    }

    /// <summary>
    /// 三结果集查询
    /// </summary>
    /// <typeparam name="TDto1">主结果集 DTO 类型</typeparam>
    /// <typeparam name="TDto2">第二结果集 DTO 类型</typeparam>
    /// <typeparam name="TDto3">第三结果集 DTO 类型</typeparam>
    /// <param name="sql">SQL 语句（含多条 SELECT）</param>
    /// <param name="param">参数</param>
    /// <param name="transaction">事务</param>
    /// <param name="commandTimeout">命令超时</param>
    /// <param name="commandType">命令类型</param>
    /// <returns>元组 (主结果集, 第二结果集, 第三结果集)</returns>
    [RequiresDynamicCode("QueryMultiple uses Expression.Compile() for the mapping delegate. Not supported in AOT. [MapFrom] makes mapping AOT-safe but delegate compilation still requires dynamic code.")]
    [RequiresUnreferencedCode("QueryMultiple scans attributes via reflection.")]
    public (List<TDto1> Items1, List<TDto2> Items2, List<TDto3> Items3) QueryMultiple<TDto1, TDto2, TDto3>(
        string sql, object? param = null,
        IDbTransaction? transaction = null, int? commandTimeout = null,
        CommandType? commandType = null)
        where TDto1 : class where TDto2 : class where TDto3 : class
    {
        using var grid = _connection.QueryMultiple(sql, param, transaction, commandTimeout, commandType);

        var entities1 = grid.Read<TSource>().ToList();
        var items1 = MapListCached<TDto1>(entities1).Cast<TDto1>().ToList();

        var sourceType2 = ResolveSourceTypeFromMapFrom(typeof(TDto2));
        var entities2 = ReadByType(sourceType2, grid);
        var items2 = MapListByType(sourceType2, typeof(TDto2), entities2).Cast<TDto2>().ToList();

        var sourceType3 = ResolveSourceTypeFromMapFrom(typeof(TDto3));
        var entities3 = ReadByType(sourceType3, grid);
        var items3 = MapListByType(sourceType3, typeof(TDto3), entities3).Cast<TDto3>().ToList();

        return (items1, items2, items3);
    }

    /// <summary>
    /// 通过反射读取非泛型结果集。
    /// <para>Dapper 2.x 的 GridReader.Read&lt;T&gt; 无无参重载，仅有 Read&lt;T&gt;(bool buffered)，
    /// 故用 GetMethods + First 精确匹配单泛型参数 + 单 bool 参数的重载，Invoke 时传 buffered=true。</para>
    /// </summary>
    /// <param name="sourceType">源实体类型。</param>
    /// <param name="grid">Dapper GridReader。</param>
    /// <returns>读取到的实体集合。</returns>
    [RequiresDynamicCode("ReadByType resolves the Dapper generic read method via MakeGenericMethod. Not supported in AOT.")]
    [RequiresUnreferencedCode("ReadByType scans type members and attributes via reflection. Some members may be trimmed.")]
    private static System.Collections.IEnumerable ReadByType(Type sourceType, SqlMapper.GridReader grid)
    {
        var readMethod = s_readByTypeCache.GetOrAdd(sourceType, static t =>
        {
            var method = typeof(SqlMapper.GridReader)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == nameof(SqlMapper.GridReader.Read)
                         && m.IsGenericMethod
                         && m.GetGenericArguments().Length == 1
                         && m.GetParameters() is { Length: 1 } pars
                         && pars[0].ParameterType == typeof(bool))
                ?? throw new InvalidOperationException(
                    ErrorMessages.DapperReadOverloadNotFound(typeof(SqlMapper).Assembly.GetName().Version?.ToString() ?? ""));
            return method.MakeGenericMethod(t);
        });
        return (System.Collections.IEnumerable)readMethod.Invoke(grid, new object[] { true })!;
    }

    /// <summary>
    /// 通过反射映射非泛型实体列表 — 调用 Mapper.MapList(Type,Type,IEnumerable)。
    /// <para>Mapper.MapList&lt;TSource,TTarget&gt; 的 postMap 参数为 Action&lt;TSource,TTarget&gt;?（可空，默认 null），
    /// 直接传 null 即可，无需 Activator.CreateInstance 创建委托实例（委托类型无无参构造函数）。</para>
    /// </summary>
    /// <param name="sourceType">源实体类型。</param>
    /// <param name="dtoType">目标 DTO 类型。</param>
    /// <param name="entities">实体集合。</param>
    /// <returns>映射后的 DTO 集合。</returns>
    [RequiresDynamicCode("MapListByType resolves the generic MapList method via MakeGenericMethod. Not supported in AOT.")]
    [RequiresUnreferencedCode("MapListByType scans type members and attributes via reflection. Some members may be trimmed.")]
    private static IEnumerable<object> MapListByType(Type sourceType, Type dtoType, System.Collections.IEnumerable entities)
    {
        var genericMethod = s_mapListByTypeCache.GetOrAdd((sourceType, dtoType), static key =>
        {
            var method = typeof(Mapper).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == nameof(Mapper.MapList) && m.GetGenericArguments().Length == 2)
                ?? throw new InvalidOperationException(
                    ErrorMessages.MapListOverloadNotFound(typeof(Mapper).Assembly.GetName().Version?.ToString() ?? ""));
            return method.MakeGenericMethod(key.Item1, key.Item2);
        });
        return (IEnumerable<object>)genericMethod.Invoke(null, new object?[] { entities, null })!;
    }
}
