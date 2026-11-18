using System.Data;
using System.Data.Common;
using Dapper;
using Microsoft.Data.Sqlite;
using NCMapper.Attributes;
using NCMapper.Dapper;
using Xunit;

namespace NCMapper.IntegrationTests;

// 红测先证（Dapper 四条）
//   C-02 转义状态机不完备：注释（--/块注释）、双引号、中括号、backtick 内文本不设防；
//         尾分号剩余面 = 无 ORDER BY 分支（StripOrderBy 原样返回）+ PageSql 直拼 + LimitOffset/Oracle 族
//         （既有 CountSql_TrailingSemicolon_Stripped 仅覆盖"有 ORDER BY+尾分号"形态——分号随 ORDER BY 一起被切）；
//   C-03 未包装未知连接恒不告警（!ReferenceEquals(current, connection) 对该场景恒 false）；
//   C-05 ToChunkDtoAsync 伪流式（QuerySourceAsync CommandDefinition 默认 buffered → 全量物化后 foreach）；
//   C-15 免红测登记（修复属防御性单次枚举物化——Dapper buffered:true 下源已物化，两次枚举无可从公开面
//         构造的确定性红测形态；替代验证 = 顺序一一对应锁定断言（ToDictionary_UniqueKey_Works 既有）+ 重复键
//         ArgumentException 锁定（ToDictionary_DuplicateKey_Throws 既有））。
// 修复前形态：标注【现状红】的用例必红；修复 commit 后全部转绿；回退任一字态 → 复红。

[Collection("DapperGlobalState")]
public class T64Batch6RedTests
{
    // ==================================================================
    // C-02：词法状态机（字符串级，零数据库门槛）
    // ==================================================================

    private readonly SqlServerDialect _mssql = new();
    private readonly MySqlDialect _mysql = new();

    /// <summary>【现状红】行注释位于真实 ORDER BY 之后：注释内 "ORDER BY fake" 被误判为剥离点。</summary>
    [Fact]
    public void C02_CountSql_尾随行注释内OrderBy_不参与匹配()
    {
        var countSql = _mssql.CountSql("SELECT * FROM T ORDER BY Id -- ORDER BY fake");
        Assert.DoesNotContain("ORDER BY", countSql);
    }

    /// <summary>【现状红】块注释位于真实 ORDER BY 之后：注释内 ORDER BY 文本被误匹配。</summary>
    [Fact]
    public void C02_CountSql_尾随块注释内OrderBy_不参与匹配()
    {
        var countSql = _mssql.CountSql("SELECT * FROM T ORDER BY Id /* ORDER BY fake */");
        Assert.DoesNotContain("ORDER BY", countSql);
    }

    /// <summary>【现状红】块注释内 '(' 污染括号深度 → 真实 ORDER BY 被跳过（depth≠0），ORDER BY 残留进子查询。</summary>
    [Fact]
    public void C02_CountSql_尾随块注释内左括号_不污染深度计数()
    {
        var countSql = _mssql.CountSql("SELECT * FROM T ORDER BY Id /* ( */");
        Assert.DoesNotContain("ORDER BY", countSql);
    }

    /// <summary>【现状红】ORDER BY 双引号标识符（PG/标准 SQL）：内层 "ORDER BY" 被误判为剥离点。</summary>
    [Fact]
    public void C02_CountSql_双引号标识符内OrderBy_不参与匹配()
    {
        var countSql = _mssql.CountSql("""SELECT * FROM T ORDER BY "ORDER BY" DESC""");
        Assert.DoesNotContain("ORDER BY", countSql);
    }

    /// <summary>【现状红】ORDER BY 中括号标识符（SQL Server）：[ORDER BY] 内层文本被误判。</summary>
    [Fact]
    public void C02_CountSql_中括号标识符内OrderBy_不参与匹配()
    {
        var countSql = _mssql.CountSql("SELECT * FROM T ORDER BY [ORDER BY] DESC");
        Assert.DoesNotContain("ORDER BY", countSql);
    }

    /// <summary>【现状红】ORDER BY backtick 标识符内文本不参与剥离点匹配。
    /// 载体用 SqlServerDialect.CountSql（词法状态机通用）：LimitOffset 族 CountSql 不做 ORDER BY 剥离（设计如此），
    /// 断言"DoesNotContain ORDER BY"仅对含 StripOrderBy 的 CountSql 有意义——绿验轮修正载体错配（原 _mysql 恒假红）。</summary>
    [Fact]
    public void C02_CountSql_Backtick标识符内OrderBy_不参与匹配()
    {
        var countSql = _mssql.CountSql("SELECT * FROM T ORDER BY `ORDER BY` DESC");
        Assert.DoesNotContain("ORDER BY", countSql);
    }

    /// <summary>【现状红】无 ORDER BY + 尾分号：StripOrderBy 原样返回 → 包裹出 SELECT COUNT(*) FROM (...;) 非法 SQL。</summary>
    [Fact]
    public void C02_CountSql_无OrderBy尾分号_剥离()
    {
        var countSql = _mssql.CountSql("SELECT * FROM T;");
        Assert.DoesNotContain(";", countSql);
    }

    /// <summary>【现状红】同形态覆盖 LimitOffset 族（MySql/PG/Sqlite 共用模板）。</summary>
    [Fact]
    public void C02_CountSql_LimitOffset族_无OrderBy尾分号_剥离()
    {
        var countSql = _mysql.CountSql("SELECT * FROM T;");
        Assert.DoesNotContain(";", countSql);
    }

    /// <summary>【现状红】尾注释 + 分号：`SELECT * FROM T /* c */;` 消毒后无分号。</summary>
    [Fact]
    public void C02_CountSql_尾随块注释加尾分号_剥离()
    {
        var countSql = _mssql.CountSql("SELECT * FROM T /* c */;");
        Assert.DoesNotContain(";", countSql);
    }

    /// <summary>【现状红】PageSql 直拼失效：有 ORDER BY + 尾分号 → "...; OFFSET ..." 非法 SQL。</summary>
    [Fact]
    public void C02_PageSql_有OrderBy尾分号_前置消毒()
    {
        var pageSql = _mssql.PageSql("SELECT * FROM T ORDER BY Id;", "@p0", "@p1");
        Assert.DoesNotContain(";", pageSql);
    }

    /// <summary>【现状红】PageSql 无 ORDER BY + 尾分号（补 ORDER BY 语义在分号之后拼接）。</summary>
    [Fact]
    public void C02_PageSql_无OrderBy尾分号_前置消毒()
    {
        var pageSql = _mssql.PageSql("SELECT * FROM T;", "@p0", "@p1");
        Assert.DoesNotContain(";", pageSql);
    }

    /// <summary>【现状红】LimitOffset 族 PageSql 同形态。</summary>
    [Fact]
    public void C02_PageSql_LimitOffset族_尾分号_前置消毒()
    {
        var pageSql = _mysql.PageSql("SELECT * FROM T;", "@p0", "@p1");
        Assert.DoesNotContain(";", pageSql);
    }

    /// <summary>回归锁定：字面量内 "(ORDER BY)"（已修复面）不随状态机扩展回归。</summary>
    [Fact]
    public void C02_回归_单引号字面量内OrderBy_仍正确()
    {
        var countSql = _mssql.CountSql("SELECT * FROM T WHERE Name='(ORDER BY)' ORDER BY Id");
        Assert.Contains("'(ORDER BY)'", countSql);
        Assert.DoesNotContain("ORDER BY Id", countSql);
    }

    // ==================================================================
    // M-01 / M-02：TailTrim 尾行注释与 Db2 消毒次序（20260926-001 五轴审查修复）
    // ==================================================================

    /// <summary>M-01：尾行注释收尾——全方言 CountSql 包裹后括号不得被注释吞掉（原产出 `(... -- c) AS _count_tbl`）。</summary>
    [Fact]
    public void M01_CountSql_尾行注释_剥离()
    {
        Assert.DoesNotContain("--", _mssql.CountSql("SELECT * FROM T -- c"));
        Assert.DoesNotContain("--", _mysql.CountSql("SELECT * FROM T -- c"));
    }

    /// <summary>M-01：尾行注释剥离后 ORDER BY 仍照常剥离（注释剥离 → StripOrderBy 次序不被破坏）。</summary>
    [Fact]
    public void M01_CountSql_尾行注释加OrderBy_注释与OrderBy均剥离()
    {
        var countSql = _mssql.CountSql("SELECT * FROM T ORDER BY Id -- fake");
        Assert.DoesNotContain("--", countSql);
        Assert.DoesNotContain("ORDER BY", countSql);
    }

    /// <summary>M-01：字面量/标识符内的 "--" 属内容不剥离（防误切主体）。</summary>
    [Fact]
    public void M01_TrimStatementTail_字面量内双横线_不剥离()
    {
        var countSql = _mssql.CountSql("SELECT * FROM T WHERE Name = '--'");
        Assert.Contains("'--'", countSql);
    }

    /// <summary>M-02：Db2 PageSql 先消毒再判 ORDER BY——原实现尾分号被夹在 "ORDER BY (SELECT NULL)" 之前剥不到。</summary>
    [Fact]
    public void M02_Db2PageSql_尾分号_前置消毒()
    {
        var pageSql = new Db2Dialect().PageSql("SELECT * FROM T;", "@p0", "@p1");
        Assert.DoesNotContain(";", pageSql);
        Assert.Contains("ORDER BY (SELECT NULL)", pageSql);
    }

    /// <summary>M-02：Db2 已有 ORDER BY + 尾分号——消毒后不重复补 ORDER BY 且无残留分号。</summary>
    [Fact]
    public void M02_Db2PageSql_有OrderBy尾分号_前置消毒()
    {
        var pageSql = new Db2Dialect().PageSql("SELECT * FROM T ORDER BY Id;", "@p0", "@p1");
        Assert.DoesNotContain(";", pageSql);
        Assert.DoesNotContain("ORDER BY (SELECT NULL)", pageSql);
    }

    // ==================================================================
    // C-03：两分支并存告警（Trace 捕获）
    // ==================================================================

    private static (System.IO.StringWriter Sw, Action Restore) CaptureTrace()
    {
        var sw = new System.IO.StringWriter();
        var oldEnabled = MapTracer.GloballyEnabled;
        var oldOutput = MapTracer.GlobalOutput;
        MapTracer.GloballyEnabled = true;
        MapTracer.GlobalOutput = sw;
        return (sw, () => { MapTracer.GloballyEnabled = oldEnabled; MapTracer.GlobalOutput = oldOutput; });
    }

    /// <summary>【现状红】未包装未知连接（无 InnerConnection/WrappedConnection 属性）→ 现状恒不告警。</summary>
    [Fact]
    public void C03_未包装未知连接_发出一次性告警()
    {
        var (sw, restore) = CaptureTrace();
        try
        {
            _ = SqlDialectResolver.Resolve(new FakePlainConnection());
            Assert.Contains("SqlDialectResolver", sw.ToString());
        }
        finally { restore(); }
    }

    /// <summary>行为锁定（现状绿）：包装连接解包后仍未知 → 现状已告警，防回归。</summary>
    [Fact]
    public void C03_包装后未知连接_告警_行为锁定()
    {
        var (sw, restore) = CaptureTrace();
        try
        {
            _ = SqlDialectResolver.Resolve(new FakeWrappedUnknownConnection());
            Assert.Contains("SqlDialectResolver", sw.ToString());
        }
        finally { restore(); }
    }

    private sealed class FakePlainConnection : IDbConnection
    {
        public string ConnectionString { get; set; } = "";
        public int ConnectionTimeout => 0;
        public string Database => "";
        public ConnectionState State => ConnectionState.Closed;
        public IDbTransaction BeginTransaction() => throw new NotSupportedException();
        public IDbTransaction BeginTransaction(IsolationLevel il) => throw new NotSupportedException();
        public void ChangeDatabase(string databaseName) { }
        public IDbCommand CreateCommand() => throw new NotSupportedException();
        public void Close() { }
        public void Dispose() { }
        public void Open() { }
    }

    private sealed class FakeWrappedUnknownConnection : IDbConnection
    {
        public string ConnectionString { get; set; } = "";
        public int ConnectionTimeout => 0;
        public string Database => "";
        public ConnectionState State => ConnectionState.Closed;
        public IDbTransaction BeginTransaction() => throw new NotSupportedException();
        public IDbTransaction BeginTransaction(IsolationLevel il) => throw new NotSupportedException();
        public void ChangeDatabase(string databaseName) { }
        public IDbCommand CreateCommand() => throw new NotSupportedException();
        public void Close() { }
        public void Dispose() { }
        public void Open() { }
        public IDbConnection InnerConnection => new FakePlainConnection();
    }

    // ==================================================================
    // C-05：ToChunkDtoAsync 真流式（reader 消费时机计数）
    // ==================================================================

    [Fact]
    public async Task C05_异步分块_回调时reader未全量物化()
    {
        using var inner = new SqliteConnection("Data Source=:memory:");
        inner.Open();
        inner.Execute("CREATE TABLE T (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL)");
        inner.Execute("INSERT INTO T (Id, Name) VALUES " + string.Join(",", Enumerable.Range(1, 10).Select(i => $"({i}, 'n{i}')")));

        var counting = new ReaderCountingConnection(inner);

        // 同步对照锁定（现状已真流式：buffered:false 直传）——第一轮回调时仅消费当前批 3 行
        var syncReads = -1;
        counting.AsDto<T64B6Row>().ToChunkDto<T64B6RowDto>("SELECT Id, Name FROM T", null, 3,
            batch => { if (syncReads < 0) syncReads = counting.ReadCount; });
        Assert.Equal(3, syncReads);

        // 【现状红】异步：全量物化后 foreach → 第一轮回调时异步段增量 Read == 10（修复后应为当前批 3）
        var batchesSeen = new List<int>();
        var asyncBaseline = counting.ReadCount; // 异步段增量口径（同步基线已消费的行不计入）
        var readsAtFirstCallback = -1;
        await counting.AsDto<T64B6Row>().ToChunkDtoAsync<T64B6RowDto>(
            "SELECT Id, Name FROM T", null, 3,
            batch =>
            {
                batchesSeen.Add(batch.Count);
                if (readsAtFirstCallback < 0) readsAtFirstCallback = counting.ReadCount - asyncBaseline;
                return Task.CompletedTask;
            });

        Assert.Equal(new[] { 3, 3, 3, 1 }, batchesSeen);
        Assert.True(readsAtFirstCallback <= 3,
            $"回调时 reader 应仅消费当前批（<=3），现状 {readsAtFirstCallback}（全量物化=10）");
    }
}

internal sealed class T64B6Row { public long Id { get; set; } public string Name { get; set; } = ""; }

[MapFrom(typeof(T64B6Row))]
internal sealed class T64B6RowDto { public long Id { get; set; } public string Name { get; set; } = ""; }

/// <summary>包装 SqliteConnection：对底层 reader 的 Read/ReadAsync 计数（C-05 消费时机观测面）。</summary>
internal sealed class ReaderCountingConnection : DbConnection
{
    private readonly DbConnection _inner;
    public int ReadCount;

    public ReaderCountingConnection(DbConnection inner) => _inner = inner;
    public override string ConnectionString { get => _inner.ConnectionString; set => _inner.ConnectionString = value; }
    public override string Database => _inner.Database;
    public override string DataSource => _inner.DataSource;
    public override string ServerVersion => _inner.ServerVersion;
    public override ConnectionState State => _inner.State;
    public override void ChangeDatabase(string databaseName) => _inner.ChangeDatabase(databaseName);
    public override void Open() => _inner.Open();
    public override void Close() => _inner.Close();
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => _inner.BeginTransaction(isolationLevel);
    protected override DbCommand CreateDbCommand()
    {
        var cmd = _inner.CreateCommand();
        return new T64B6CountingCommand(cmd, this);
    }
}

internal sealed class T64B6CountingCommand : DbCommand
{
    private readonly DbCommand _inner;
    private readonly ReaderCountingConnection _owner;
    public T64B6CountingCommand(DbCommand inner, ReaderCountingConnection owner) { _inner = inner; _owner = owner; }

    public override string CommandText { get => _inner.CommandText; set => _inner.CommandText = value; }
    public override int CommandTimeout { get => _inner.CommandTimeout; set => _inner.CommandTimeout = value; }
    public override CommandType CommandType { get => _inner.CommandType; set => _inner.CommandType = value; }
    public override bool DesignTimeVisible { get => _inner.DesignTimeVisible; set => _inner.DesignTimeVisible = value; }
    public override UpdateRowSource UpdatedRowSource { get => _inner.UpdatedRowSource; set => _inner.UpdatedRowSource = value; }
    protected override DbConnection DbConnection { get => _inner.Connection; set => _inner.Connection = (DbConnection)value!; }
    protected override DbParameterCollection DbParameterCollection => _inner.Parameters;
    protected override DbTransaction DbTransaction { get => _inner.Transaction; set => _inner.Transaction = value; }
    public override void Cancel() => _inner.Cancel();
    public override int ExecuteNonQuery() => _inner.ExecuteNonQuery();
    public override object? ExecuteScalar() => _inner.ExecuteScalar();
    public override void Prepare() => _inner.Prepare();
    protected override DbParameter CreateDbParameter() => _inner.CreateParameter();
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        var rdr = _inner.ExecuteReader(behavior);
        return new T64B6CountingReader(rdr, _owner);
    }
    protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
    {
        var rdr = await _inner.ExecuteReaderAsync(behavior, cancellationToken).ConfigureAwait(false);
        return new T64B6CountingReader(rdr, _owner);
    }
}

internal sealed class T64B6CountingReader : DbDataReader
{
    private readonly DbDataReader _inner;
    private readonly ReaderCountingConnection _owner;
    public T64B6CountingReader(DbDataReader inner, ReaderCountingConnection owner) { _inner = inner; _owner = owner; }

    private void Bump() => System.Threading.Interlocked.Increment(ref _owner.ReadCount);

    public override bool Read()
    {
        var r = _inner.Read();
        if (r) Bump();
        return r;
    }
    public override async Task<bool> ReadAsync(CancellationToken cancellationToken)
    {
        var r = await _inner.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (r) Bump();
        return r;
    }

    public override int Depth => _inner.Depth;
    public override int FieldCount => _inner.FieldCount;
    public override bool HasRows => _inner.HasRows;
    public override bool IsClosed => _inner.IsClosed;
    public override int RecordsAffected => _inner.RecordsAffected;
    public override object this[int ordinal] => _inner[ordinal];
    public override object this[string name] => _inner[name];
    public override bool GetBoolean(int ordinal) => _inner.GetBoolean(ordinal);
    public override byte GetByte(int ordinal) => _inner.GetByte(ordinal);
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => _inner.GetBytes(ordinal, dataOffset, buffer, bufferOffset, length);
    public override char GetChar(int ordinal) => _inner.GetChar(ordinal);
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => _inner.GetChars(ordinal, dataOffset, buffer, bufferOffset, length);
    public override string GetDataTypeName(int ordinal) => _inner.GetDataTypeName(ordinal);
    public override DateTime GetDateTime(int ordinal) => _inner.GetDateTime(ordinal);
    public override decimal GetDecimal(int ordinal) => _inner.GetDecimal(ordinal);
    public override double GetDouble(int ordinal) => _inner.GetDouble(ordinal);
    public override System.Collections.IEnumerator GetEnumerator() => _inner.GetEnumerator();
    public override System.Type GetFieldType(int ordinal) => _inner.GetFieldType(ordinal);
    public override float GetFloat(int ordinal) => _inner.GetFloat(ordinal);
    public override Guid GetGuid(int ordinal) => _inner.GetGuid(ordinal);
    public override short GetInt16(int ordinal) => _inner.GetInt16(ordinal);
    public override int GetInt32(int ordinal) => _inner.GetInt32(ordinal);
    public override long GetInt64(int ordinal) => _inner.GetInt64(ordinal);
    public override string GetName(int ordinal) => _inner.GetName(ordinal);
    public override int GetOrdinal(string name) => _inner.GetOrdinal(name);
    public override string GetString(int ordinal) => _inner.GetString(ordinal);
    public override object GetValue(int ordinal) => _inner.GetValue(ordinal);
    public override int GetValues(object[] values) => _inner.GetValues(values);
    public override bool IsDBNull(int ordinal) => _inner.IsDBNull(ordinal);
    public override bool NextResult() => _inner.NextResult();
}
