using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using mersolutionCore.Cache;
using mersolutionCore.Command.Abstractions;
using mersolutionCore.Config;
using mersolutionCore.Http;
using mersolutionCore.Library;
using mersolutionCore.ORM;
using mersolutionCore.ORM.Entity;
using mersolutionCore.ORM.Migration;
using mersolutionCore.ORM.Relationships;
using mersolutionCore.ORM.Validation;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

public enum UserStatus { Pending = 0, Active = 1, Banned = 2 }

[Table("mc_users")]
public class TUser : Model<TUser>
{
    [PrimaryKey] public int Id { get; set; }
    [Column(Length = 100)] public string Name { get; set; }
    public string Email { get; set; }
    public int Age { get; set; }
    public bool IsActive { get; set; }
    public UserStatus Status { get; set; }
    public decimal Balance { get; set; }
    public DateTime? BirthDate { get; set; }
    [CreatedAt] public DateTime? CreatedAt { get; set; }
    [UpdatedAt] public DateTime? UpdatedAt { get; set; }
    [SoftDelete] public DateTime? DeletedAt { get; set; }

    [HasMany(typeof(TOrder), "UserId")] public List<TOrder> Orders { get; set; }

    public int OrderCount() => HasMany<TOrder>("UserId").Count();

    public BelongsToMany<TUser, TRole> Roles() => BelongsToMany<TRole>("mc_role_user", "UserId", "RoleId");
}

[Table("mc_orders")]
public class TOrder : Model<TOrder>
{
    [PrimaryKey] public long Id { get; set; }
    public int UserId { get; set; }
    public decimal Total { get; set; }

    [BelongsTo(typeof(TUser), "UserId")] public TUser User { get; set; }
    [HasMany(typeof(TItem), "OrderId")] public List<TItem> Items { get; set; }
}

[Table("mc_items")]
public class TItem : Model<TItem>
{
    [PrimaryKey] public int Id { get; set; }
    public long OrderId { get; set; }
    public string Sku { get; set; }
}

[Table("mc_docs")]
public class TDoc : Model<TDoc>
{
    [PrimaryKey(AutoIncrement = false)] public Guid Id { get; set; }
    public string Title { get; set; }
}

[Table("mc_reserved")]
public class TReserved : Model<TReserved>
{
    [PrimaryKey] public int Id { get; set; }
    public string Order { get; set; }
    public int Group { get; set; }
    public string Key { get; set; }
}

[Table("mc_versioned")]
public class TVersioned : Model<TVersioned>
{
    [PrimaryKey] public int Id { get; set; }
    public string Title { get; set; }
    public string Note { get; set; }
    [RowVersion] public int Version { get; set; }
}

[Table("mc_products")]
[GlobalScope("IsActive", true)]
public class TProduct : Model<TProduct>
{
    [PrimaryKey] public int Id { get; set; }
    public string Code { get; set; }
    public string Name { get; set; }
    public decimal Price { get; set; }
    public bool IsActive { get; set; }
}

[Connection("second")]
[Table("mc_logs")]
public class TLog : Model<TLog>
{
    [PrimaryKey] public int Id { get; set; }
    public string Message { get; set; }
}

// Composite primary key
[Table("mc_order_lines")]
public class TOrderLine : Model<TOrderLine>
{
    [PrimaryKey(AutoIncrement = false, Order = 1)] public long OrderId { get; set; }
    [PrimaryKey(AutoIncrement = false, Order = 2)] public string Sku { get; set; }
    public int Qty { get; set; }
}

// Country → Authors → Posts (HasManyThrough); posts and videos share comments (polymorphic)
[Table("mc_countries")]
public class TCountry : Model<TCountry>
{
    [PrimaryKey] public int Id { get; set; }
    public string Name { get; set; }

    public HasManyThrough<TCountry, TAuthor, TPost> Posts() => HasManyThrough<TPost, TAuthor>("CountryId", "AuthorId");
    public HasOneThrough<TCountry, TAuthor, TPost> FirstPost() => HasOneThrough<TPost, TAuthor>("CountryId", "AuthorId");
}

[Table("mc_authors")]
public class TAuthor : Model<TAuthor>
{
    [PrimaryKey] public int Id { get; set; }
    public int CountryId { get; set; }
    public string Name { get; set; }
}

[Table("mc_posts")]
public class TPost : Model<TPost>
{
    [PrimaryKey] public int Id { get; set; }
    public int AuthorId { get; set; }
    public string Title { get; set; }
    [UpdatedAt] public DateTime? UpdatedAt { get; set; }

    [MorphMany(typeof(TComment), "Commentable")] public List<TComment> CommentList { get; set; }

    public MorphMany<TPost, TComment> Comments() => MorphMany<TComment>("Commentable");
}

[Table("mc_videos")]
public class TVideo : Model<TVideo>
{
    [PrimaryKey] public int Id { get; set; }
    public string Title { get; set; }

    public MorphMany<TVideo, TComment> Comments() => MorphMany<TComment>("Commentable");
}

[Table("mc_comments")]
[Touches(typeof(TPost), "PostId")]
public class TComment : Model<TComment>
{
    [PrimaryKey] public int Id { get; set; }
    public int? PostId { get; set; }
    public string CommentableType { get; set; }
    public long? CommentableId { get; set; }
    public string Body { get; set; }

    [MorphTo("Commentable")] public ModelBase Owner { get; set; }

    public MorphTo<TComment> Commentable() => MorphTo("Commentable");
}

// Self relation (2.1 WhereHas / nested eager constraints)
[Table("mc_categories")]
public class TCategory : Model<TCategory>
{
    [PrimaryKey] public int Id { get; set; }
    public int? ParentId { get; set; }
    public string Name { get; set; }

    [HasMany(typeof(TCategory), "ParentId")] public List<TCategory> Children { get; set; }
    [BelongsTo(typeof(TCategory), "ParentId")] public TCategory Parent { get; set; }
}

// Same table as TUser with a [Computed] column (2.1 WithCount)
[Table("mc_users")]
public class TUserStat : Model<TUserStat>
{
    [PrimaryKey] public int Id { get; set; }
    public string Name { get; set; }
    [SoftDelete] public DateTime? DeletedAt { get; set; }

    [HasMany(typeof(TOrder), "UserId")] public List<TOrder> Orders { get; set; }
    [Computed] public int OrdersCount { get; set; }
}

// Value converters (2.1)
public enum TLevel { Low = 1, High = 2 }

[Table("mc_profiles")]
public class TProfile : Model<TProfile>
{
    [PrimaryKey] public int Id { get; set; }
    [EnumAsString] public TLevel Level { get; set; }
    [JsonColumn] public List<string> Tags { get; set; } = new List<string>();
    [Encrypted] public string Secret { get; set; }
}

// Read replica test table (2.1)
[Table("mc_rr")]
public class TReplicaRow : Model<TReplicaRow>
{
    [PrimaryKey] public int Id { get; set; }
    public string Name { get; set; }
}

// DateOnly / TimeOnly columns (2.1)
[Table("mc_schedules")]
public class TSchedule : Model<TSchedule>
{
    [PrimaryKey] public int Id { get; set; }
    public DateOnly Day { get; set; }
    public TimeOnly? StartsAt { get; set; }
    public string Title { get; set; }
}

// Relations on a composite key: a line (OrderId, Pos) has taxes; a tax belongs to its line and touches it
[Table("mc_lines")]
public class TLine : Model<TLine>
{
    [PrimaryKey(AutoIncrement = false, Order = 1)] public int OrderId { get; set; }
    [PrimaryKey(AutoIncrement = false, Order = 2)] public int Pos { get; set; }
    public string Product { get; set; }
    [UpdatedAt] public DateTime? UpdatedAt { get; set; }

    [HasMany(typeof(TLineTax))] public List<TLineTax> TaxList { get; set; }

    public HasMany<TLine, TLineTax> Taxes() => HasMany<TLineTax>();
    public HasOne<TLine, TLineTax> FirstTax() => HasOne<TLineTax>();
}

[Table("mc_line_taxes")]
[Touches(typeof(TLine), "OrderId, Pos")]
public class TLineTax : Model<TLineTax>
{
    [PrimaryKey] public int Id { get; set; }
    public int OrderId { get; set; }
    public int Pos { get; set; }
    public string Code { get; set; }

    [BelongsTo(typeof(TLine))] public TLine Line { get; set; }

    public BelongsTo<TLineTax, TLine> LineOf() => BelongsTo<TLine>();
}

[Table("mc_roles")]
public class TRole : Model<TRole>
{
    [PrimaryKey] public int Id { get; set; }
    public string Name { get; set; }
}

// Hidden / guarded / fillable (no table needed)
public class TAccount : Model<TAccount>
{
    [PrimaryKey] public int Id { get; set; }
    public string Name { get; set; }
    [Hidden] public string PasswordHash { get; set; }
    [Guarded] public bool IsAdmin { get; set; }
}

public class TFillable : Model<TFillable>
{
    [PrimaryKey] public int Id { get; set; }
    [Fillable] public string Name { get; set; }
    public string Role { get; set; }
}

// Shared cache store for the distributed cache tests (stands in for Redis)
public class TestCacheStore : ICacheStore
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> _data = new System.Collections.Concurrent.ConcurrentDictionary<string, byte[]>();
    public int Count => _data.Count;
    public byte[] Get(string key) => _data.TryGetValue(key, out var v) ? v : null;
    public void Set(string key, byte[] value, TimeSpan ttl) => _data[key] = value;
    public void Remove(string key) => _data.TryRemove(key, out _);
}

public class TestDistributedCache : IDistributedCache
{
    public readonly TestCacheStore Store = new TestCacheStore();
    public byte[] Get(string key) => Store.Get(key);
    public Task<byte[]> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Store.Get(key));
    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => Store.Set(key, value, TimeSpan.Zero);
    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) { Set(key, value, options); return Task.CompletedTask; }
    public void Refresh(string key) { }
    public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
    public void Remove(string key) => Store.Remove(key);
    public Task RemoveAsync(string key, CancellationToken token = default) { Store.Remove(key); return Task.CompletedTask; }
}

// Collects log lines (AddMersoCore(...).LogSql() test)
public class ListLoggerProvider : ILoggerProvider
{
    private readonly List<string> _lines;
    public ListLoggerProvider(List<string> lines) { _lines = lines; }
    public ILogger CreateLogger(string categoryName) => new ListLogger(_lines, categoryName);
    public void Dispose() { }

    private class ListLogger : ILogger
    {
        private readonly List<string> _lines;
        private readonly string _category;
        public ListLogger(List<string> lines, string category) { _lines = lines; _category = category; }
        public IDisposable BeginScope<TState>(TState state) => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            lock (_lines) _lines.Add(_category + ": " + formatter(state, exception));
        }
    }
}

// Records SQL instead of running it (transactions still work on an in-memory SQLite connection)
public class FakeCommand : DbCommandBase
{
    public static readonly List<string> Sql = new List<string>();
    private readonly DbProviderType _provider;
    public FakeCommand(DbProviderType provider) { _provider = provider; }
    public override DbProviderType ProviderType => _provider;
    protected override System.Data.Common.DbConnection NewConnection() => new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
    public override int PKLastKeyOrDefault(string tableName, string columnName) => 0;
    public override int RunNonQuery(string sql) { Sql.Add(sql); ParametersClear(); return 1; }
    public override long RunInsertGetId(string insertSql, string primaryKeyColumn) { Sql.Add(insertSql); ParametersClear(); return 1; }
    public override System.Data.DataTable RunDataTable(string sql) { Sql.Add(sql); ParametersClear(); return new System.Data.DataTable(); }
    public override T RunReader<T>(string sql, Func<System.Data.Common.DbDataReader, T> read) { Sql.Add(sql); ParametersClear(); return read(new System.Data.DataTable().CreateDataReader()); }
    public override Task<T> RunReaderAsync<T>(string sql, Func<System.Data.Common.DbDataReader, Task<T>> read, CancellationToken cancellationToken = default) { Sql.Add(sql); ParametersClear(); return read(new System.Data.DataTable().CreateDataReader()); }
    public override object RunToObjectScaler(string sql) { Sql.Add(sql); ParametersClear(); return 0; }
}

public class TestContext : DbContext
{
    public TestContext(Func<DbCommandBase> f) : base(f) { }
    public MerSet<TUser> Users { get; set; }
    public MerSet<TOrder> Orders { get; set; }
    public MerSet<TItem> Items { get; set; }
    public MerSet<TDoc> Docs { get; set; }
    public MerSet<TReserved> Reserved { get; set; }
    public MerSet<TVersioned> Versioned { get; set; }
    public MerSet<TProduct> Products { get; set; }
}

public class CountingObserver : MersoObserver<TUser>
{
    public int CreatingCount, CreatedCount, SavedCount;
    public bool CancelNames;
    public override bool Creating(TUser m) { CreatingCount++; return !(CancelNames && m.Name == "BLOCKED"); }
    public override void Created(TUser m) => CreatedCount++;
    public override void Saved(TUser m) => SavedCount++;
}

/// <summary>
/// Checks shared by the xUnit tests (MersoCoreTests). Each Run* method returns the failed checks.
/// Databases other than SQLite run when their admin connection string is set:
/// MERSO_TEST_SQLSERVER, MERSO_TEST_MYSQL, MERSO_TEST_MARIADB, MERSO_TEST_POSTGRES (a test database "mersocore_test" is created and dropped).
/// </summary>
public static class Suite
{
    public const string SqlServerEnv = "MERSO_TEST_SQLSERVER";
    public const string MySqlEnv = "MERSO_TEST_MYSQL";
    public const string MariaDbEnv = "MERSO_TEST_MARIADB";
    public const string PostgresEnv = "MERSO_TEST_POSTGRES";

    static readonly List<string> _failures = new List<string>();

    // MERSO_TEST_TRACE=1 prints every check before it runs (finds a check that hangs)
    static readonly bool TraceChecks = Environment.GetEnvironmentVariable("MERSO_TEST_TRACE") == "1";

    static void Check(string name, Func<bool> test)
    {
        if (TraceChecks)
            Console.WriteLine("[check] " + name);

        try
        {
            if (!test())
                _failures.Add(name);
        }
        catch (Exception ex)
        {
            _failures.Add(name + " -> " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    static List<string> Run(Action body)
    {
        _failures.Clear();
        // tr-TR on purpose: decimal comma, dotless i — the library must not depend on the culture
        CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("tr-TR");
        CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
        body();
        return new List<string>(_failures);
    }

    public static List<string> RunLibrary() => Run(LibraryTests);

    public static List<string> RunDialects() => Run(DialectTests);

    public static List<string> RunDatabase(string provider) => Run(() =>
    {
        switch (provider)
        {
            case "SQLite":
                var sqlitePath = Path.Combine(Path.GetTempPath(), "mc_test_" + Guid.NewGuid().ToString("N") + ".db");
                RunDb(() => DbConfig.ConfigureSQLite(sqlitePath), () =>
                {
                    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                    File.Delete(sqlitePath);
                });
                break;

            case "MySQL":
            case "MariaDB":
                var admin = Environment.GetEnvironmentVariable(provider == "MySQL" ? MySqlEnv : MariaDbEnv);
                RunDb(() =>
                {
                    using (var c = Retry(() => new MySqlConnector.MySqlConnection(admin)))
                    {
                        var v = new MySqlConnector.MySqlCommand("SELECT VERSION()", c).ExecuteScalar()?.ToString();
                        new MySqlConnector.MySqlCommand("DROP DATABASE IF EXISTS mersocore_test; CREATE DATABASE mersocore_test CHARACTER SET utf8mb4", c).ExecuteNonQuery();
                        var type = v != null && v.IndexOf("MariaDB", StringComparison.OrdinalIgnoreCase) >= 0 ? DbProviderType.MariaDB : DbProviderType.MySQL;
                        DbConfig.Configure(admin + ";Database=mersocore_test", type);
                    }
                }, () =>
                {
                    MySqlConnector.MySqlConnection.ClearAllPools();
                    using (var c = Retry(() => new MySqlConnector.MySqlConnection(admin)))
                        new MySqlConnector.MySqlCommand("DROP DATABASE IF EXISTS mersocore_test", c).ExecuteNonQuery();
                });
                break;

            case "PostgreSQL":
                var pgAdmin = Environment.GetEnvironmentVariable(PostgresEnv);
                RunDb(() =>
                {
                    using (var c = Retry(() => new Npgsql.NpgsqlConnection(pgAdmin + ";Database=postgres")))
                    {
                        // FORCE: a killed earlier run can leave sessions open on the test database
                        new Npgsql.NpgsqlCommand("DROP DATABASE IF EXISTS mersocore_test WITH (FORCE)", c).ExecuteNonQuery();
                        new Npgsql.NpgsqlCommand("CREATE DATABASE mersocore_test", c).ExecuteNonQuery();
                    }
                    DbConfig.Configure(pgAdmin + ";Database=mersocore_test", DbProviderType.PostgreSQL);
                }, () =>
                {
                    Npgsql.NpgsqlConnection.ClearAllPools();
                    using (var c = Retry(() => new Npgsql.NpgsqlConnection(pgAdmin + ";Database=postgres")))
                        new Npgsql.NpgsqlCommand("DROP DATABASE IF EXISTS mersocore_test WITH (FORCE)", c).ExecuteNonQuery();
                });
                break;

            case "SqlServer":
                var sqlAdmin = Environment.GetEnvironmentVariable(SqlServerEnv);
                const string drop = "IF DB_ID('mersocore_test') IS NOT NULL BEGIN ALTER DATABASE mersocore_test SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE mersocore_test; END";
                RunDb(() =>
                {
                    using (var c = Retry(() => new Microsoft.Data.SqlClient.SqlConnection(sqlAdmin)))
                    {
                        new Microsoft.Data.SqlClient.SqlCommand(drop, c).ExecuteNonQuery();
                        new Microsoft.Data.SqlClient.SqlCommand("CREATE DATABASE mersocore_test", c).ExecuteNonQuery();
                    }
                    DbConfig.Configure(sqlAdmin + ";Database=mersocore_test", DbProviderType.SqlServer);
                }, () =>
                {
                    Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
                    using (var c = Retry(() => new Microsoft.Data.SqlClient.SqlConnection(sqlAdmin)))
                        new Microsoft.Data.SqlClient.SqlCommand(drop, c).ExecuteNonQuery();
                });
                break;

            default:
                _failures.Add("Unknown provider " + provider);
                break;
        }
    });

    // Containers in CI may still be starting: retry the first connection for up to a minute
    static T Retry<T>(Func<T> create) where T : System.Data.Common.DbConnection
    {
        for (int attempt = 1; ; attempt++)
        {
            var connection = create();
            try
            {
                connection.Open();
                return connection;
            }
            catch when (attempt < 30)
            {
                connection.Dispose();
                Thread.Sleep(2000);
            }
        }
    }

    static void RunDb(Action setup, Action cleanup)
    {
        try
        {
            setup();
        }
        catch (Exception ex)
        {
            _failures.Add("setup -> " + ex.GetType().Name + ": " + ex.Message);
            return;
        }

        try
        {
            DbTests();
        }
        finally
        {
            ObserverManager.ClearAll();
            try { cleanup(); } catch (Exception ex) { Console.WriteLine("cleanup: " + ex.Message); }
        }
    }

    static void DbTests()
    {
        var ctx = new TestContext(DbConfig.CreateConnection);
        Check("EnsureCreated", () => { ctx.EnsureCreated(); return true; });

        TUser ali = null, cem = null, deniz = null;
        Check("Insert returns distinct ids", () =>
        {
            ali = TUser.Create(new TUser { Name = "Ali Şahin", Email = "ali@x.com", Age = 30, IsActive = true, Status = UserStatus.Active, Balance = 12.5m, BirthDate = new DateTime(1990, 5, 1) });
            cem = TUser.Create(new TUser { Name = "Cem", Email = "cem@x.com", Age = 25, IsActive = false, Status = UserStatus.Banned, Balance = 3m });
            deniz = TUser.Create(new TUser { Name = "Deniz", Email = "deniz@x.com", Age = 40, IsActive = true, Status = UserStatus.Pending, Balance = 0m });
            return ali.Id > 0 && cem.Id > ali.Id && deniz.Id > cem.Id;
        });

        Check("Round trip enum / decimal / bool / unicode / date", () =>
        {
            var u = TUser.Find(ali.Id);
            return u.Status == UserStatus.Active && u.Balance == 12.5m && u.IsActive && u.Name == "Ali Şahin"
                && u.BirthDate == new DateTime(1990, 5, 1) && u.CreatedAt.HasValue;
        });

        Check("Delete is soft; Find excludes trashed; FindWithTrashed keeps it", () =>
        {
            var deleted = cem.Delete();
            return deleted && cem.Trashed() && TUser.Find(cem.Id) == null && TUser.FindWithTrashed(cem.Id) != null && TUser.Count() == 2;
        });

        Check("First() works on this provider", () => TUser.Query().OrderBy("Id").First()?.Id == ali.Id);
        Check("Take/Skip with OrderBy", () =>
        {
            var r = TUser.Query().OrderBy("Id").Skip(1).Take(1).Get();
            return r.Count == 1 && r[0].Id == deniz.Id;
        });
        Check("Skip without OrderBy", () => TUser.Query().Skip(1).Get().Count == 1);
        Check("Paginate with OrderBy (count ignores ORDER BY)", () =>
        {
            var p = TUser.Query().OrderByDesc("Age").Paginate(1, 1);
            return p.TotalCount == 2 && p.TotalPages == 2 && p.Items.Count == 1 && p.Items[0].Id == deniz.Id && p.HasNextPage;
        });
        Check("OrWhere does not leak soft-deleted rows", () =>
        {
            var r = TUser.Query().Where("Name", "Ali Şahin").OrWhere("Name", "Cem").Get();
            return r.Count == 1 && r[0].Id == ali.Id;
        });
        Check("Count() does not break later Get()", () =>
        {
            var q = TUser.Query().Where("IsActive", true);
            var n = q.Count();
            var list = q.Get();
            return n == 2 && list.Count == 2 && list.All(u => u.Name != null);
        });
        Check("Where(col, null) becomes IS NULL", () => TUser.Query().Where("BirthDate", null).Count() == 1);
        Check("QueryBuilder.Increment / affected rows", () =>
        {
            var affected = TUser.Query().Where("Id", ali.Id).Increment("Age", 5);
            return affected == 1 && TUser.Find(ali.Id).Age == 35;
        });
        Check("QueryBuilder.Update returns affected rows", () =>
            TUser.Query().Where("IsActive", true).Update(new Dictionary<string, object> { { "Balance", 1m } }) == 2);
        Check("WhereIn empty / WhereNotIn empty", () =>
            TUser.Query().WhereIn("Id", new object[0]).Count() == 0 && TUser.Query().WhereNotIn("Id", new object[0]).Count() == 2);
        Check("WhereDate (today, CreatedAt)", () => TUser.Query().WhereDate("CreatedAt", DateTime.UtcNow).Count() == 2);
        Check("InRandomOrder", () => TUser.Query().InRandomOrder().First() != null);
        Check("GroupBy/Having + Count via subquery", () =>
            TUser.Query().Select("IsActive", "COUNT(*) AS c").GroupBy("IsActive").Having("COUNT(*)", ">", 0).Count() == 1);
        Check("Invalid operator rejected", () =>
        {
            try { TUser.Query().Where("Id", "; DROP TABLE x --", 1); return false; } catch (ArgumentException) { return true; }
        });
        Check("Model.Increment()", () => { var u = TUser.Find(deniz.Id); u.Increment("Age", 2); return u.Age == 42; });
        Check("Restore()", () =>
        {
            var c = TUser.FindWithTrashed(cem.Id);
            var ok = c.Restore() && TUser.Find(cem.Id) != null;
            c.Delete();
            return ok;
        });

        // Transactions
        Check("MersoTransaction rollback undoes all statements", () =>
        {
            var before = TUser.Count();
            try
            {
                MersoTransaction.Run(() =>
                {
                    TUser.Create(new TUser { Name = "Tx1", Email = "t1@x.com" });
                    TUser.Query().Where("Id", ali.Id).Update(new Dictionary<string, object> { { "Age", 99 } });
                    throw new InvalidOperationException("boom");
                });
            }
            catch (InvalidOperationException) { }
            return TUser.Count() == before && TUser.Find(ali.Id).Age == 35;
        });
        Check("MersoTransaction commit + nested Run joins", () =>
        {
            var before = TUser.Count();
            MersoTransaction.Run(() =>
            {
                TUser.Create(new TUser { Name = "Tx2", Email = "t2@x.com" });
                MersoTransaction.Run(() => TUser.Create(new TUser { Name = "Tx3", Email = "t3@x.com" }));
            });
            return TUser.Count() == before + 2;
        });
        Check("MersoTransaction.RunAsync rollback", () =>
        {
            var before = TUser.Count();
            try
            {
                MersoTransaction.RunAsync(async () =>
                {
                    await Task.Yield();
                    TUser.Create(new TUser { Name = "TxA", Email = "ta@x.com" });
                    throw new InvalidOperationException("async boom");
                }).GetAwaiter().GetResult();
            }
            catch (InvalidOperationException) { }
            return TUser.Count() == before;
        });
        Check("DbCommandBase.BeginTransaction + Rollback", () =>
        {
            var before = TUser.Count();
            using (var db = DbConfig.CreateConnection())
            {
                db.BeginTransaction();
                db.ParametersAdd("@n", "Manual");
                db.ParametersAdd("@e", "m@x.com");
                db.ParametersAdd("@t", true);
                db.RunExecute("INSERT INTO mc_users (Name, Email, Age, IsActive, Status, Balance) VALUES (@n, @e, 1, @t, 0, 0)");
                var inside = db.RunToInt32Scaler("SELECT COUNT(*) FROM mc_users WHERE DeletedAt IS NULL");
                db.RollbackTransaction();
                return inside == before + 1 && TUser.Count() == before;
            }
        });

        // Observers
        var observer = new CountingObserver { CancelNames = true };
        ObserverManager.Register(observer);
        Check("Observers fire and can cancel", () =>
        {
            var blocked = new TUser { Name = "BLOCKED", Email = "b@x.com" };
            var saved = blocked.Save();
            var ok = TUser.Create(new TUser { Name = "Obs", Email = "o@x.com" });
            return !saved && blocked.Id == 0 && observer.CreatingCount == 2 && observer.CreatedCount == 1 && observer.SavedCount == 1 && ok.Id > 0;
        });
        ObserverManager.ClearAll();

        // Relations / eager loading
        TOrder o1 = null, o2 = null;
        Check("long PK insert id", () =>
        {
            o1 = TOrder.Create(new TOrder { UserId = ali.Id, Total = 10m });
            o2 = TOrder.Create(new TOrder { UserId = ali.Id, Total = 20m });
            TOrder.Create(new TOrder { UserId = deniz.Id, Total = 5m });
            TItem.Create(new TItem { OrderId = o1.Id, Sku = "A" });
            TItem.Create(new TItem { OrderId = o1.Id, Sku = "B" });
            return o1.Id > 0 && o2.Id == o1.Id + 1;
        });
        Check("HasMany relation count", () => TUser.Find(ali.Id).OrderCount() == 2);
        Check("Eager HasMany", () =>
        {
            var users = TUser.Query().OrderBy("Id").With("Orders").Get();
            var a = users.First(u => u.Id == ali.Id);
            var d = users.First(u => u.Id == deniz.Id);
            return a.Orders.Count == 2 && d.Orders.Count == 1;
        });
        Check("Eager nested Orders.Items", () =>
        {
            var a = TUser.Query().Where("Id", ali.Id).With("Orders.Items").First();
            return a.Orders.First(o => o.Id == o1.Id).Items.Count == 2 && a.Orders.First(o => o.Id == o2.Id).Items.Count == 0;
        });
        Check("Eager BelongsTo", () =>
        {
            var orders = TOrder.Query().With("User").Get();
            return orders.Count == 3 && orders.All(o => o.User != null && o.User.Id == o.UserId);
        });

        // Guid keys
        Check("Guid PK generated, found, updated (no duplicate)", () =>
        {
            var d = new TDoc { Title = "İlk" };
            d.Save();
            var id = d.Id;
            d.Title = "İkinci";
            d.Save();
            var found = TDoc.Find(id);
            return id != Guid.Empty && found != null && found.Title == "İkinci" && TDoc.Count() == 1;
        });

        // Cache key includes values
        Check("Query cache keeps different parameter values apart", () =>
        {
            MersoCache.Flush();
            var a = TUser.Query().Where("Id", ali.Id).Remember(TimeSpan.FromMinutes(1));
            var b = TUser.Query().Where("Id", deniz.Id).Remember(TimeSpan.FromMinutes(1));
            return a.Single().Id == ali.Id && b.Single().Id == deniz.Id;
        });

        // Raw queries
        Check("RawQuery.Scalar<int?>, Execute affected rows, params", () =>
        {
            var n = RawQuery.Scalar<int?>("SELECT COUNT(*) FROM mc_users WHERE Age > @age", new Dictionary<string, object> { { "age", 20 } });
            var none = RawQuery.Scalar<int?>("SELECT MAX(Age) FROM mc_users WHERE Id < 0");
            var affected = RawQuery.Execute("UPDATE mc_users SET Balance = Balance WHERE IsActive = @a", new Dictionary<string, object> { { "a", true } });
            return n >= 2 && none == null && affected >= 2;
        });

        // Bulk
        Check("BulkInsert 800 rows + BulkDelete affected", () =>
        {
            var items = Enumerable.Range(0, 800).Select(i => new TItem { OrderId = o2.Id, Sku = "S" + i }).ToList();
            var inserted = BulkOperations.BulkInsert(items, 500);
            var ids = TItem.Query().Where("OrderId", o2.Id).Pluck("Id");
            var deleted = BulkOperations.BulkDelete<TItem>(ids);
            return inserted == 800 && ids.Count == 800 && deleted == 800;
        });

        Check("ChunkById visits every row once", () =>
        {
            int seen = 0;
            TUser.Query().WithTrashed().ChunkById(2, chunk => seen += chunk.Count);
            return seen == TUser.Query().WithTrashed().Count();
        });

        Check("Concurrent inserts get their own ids", () =>
        {
            var created = new System.Collections.Concurrent.ConcurrentBag<TItem>();
            Parallel.For(0, 12, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i =>
            {
                created.Add(TItem.Create(new TItem { OrderId = 999, Sku = "P" + i }));
            });
            return created.Select(c => c.Id).Distinct().Count() == 12
                && created.All(c => TItem.Find(c.Id)?.Sku == c.Sku);
        });

        Check("Schema HasTable / HasColumn", () =>
        {
            using (var db = DbConfig.CreateConnection())
            {
                var s = new Schema(db);
                return s.HasTable("mc_users") && !s.HasTable("mc_nope") && s.HasColumn("mc_users", "Email") && !s.HasColumn("mc_users", "Nope");
            }
        });

        Check("ToSql uses provider syntax", () =>
        {
            var sql = TUser.Query().OrderBy("Id").Take(5).ToSql();
            return DbConfig.ProviderType == DbProviderType.SqlServer ? sql.Contains("TOP 5") : sql.Contains("LIMIT 5");
        });

        Check("Async API applies parameters", () =>
        {
            using (var db = DbConfig.CreateConnection())
            {
                db.ParametersAdd("@id", ali.Id);
                var n = db.RunToInt32ScalerAsync("SELECT COUNT(*) FROM mc_users WHERE Id = @id").GetAwaiter().GetResult();
                db.ParametersAdd("@id", ali.Id);
                var dt = db.RunDataTablePagedAsync("SELECT * FROM mc_users WHERE Id >= @id ORDER BY Id", 1, 1).GetAwaiter().GetResult();
                return n == 1 && dt.Rows.Count == 1;
            }
        });

        Check("Migrator: failed migration rolled back, rollback, duplicates", () =>
        {
            var m = new Migrator(DbConfig.CreateConnection, "mc_migrations");
            m.Add(new MigA(), new MigBFails());
            m.Add(new MigA());                       // same class twice → kept once
            var dupRejected = false;
            try { m.Add(new MigADuplicate()); } catch (InvalidOperationException) { dupRejected = true; }

            var r = m.Migrate();
            bool hasA, hasB;
            using (var db = DbConfig.CreateConnection())
            {
                var s = new Schema(db);
                hasA = s.HasTable("mc_mig_a");
                hasB = s.HasTable("mc_mig_b");
            }
            var ddlTransactional = DbConfig.ProviderType != DbProviderType.MySQL && DbConfig.ProviderType != DbProviderType.MariaDB;
            var status = m.Status();
            var back = m.Rollback();
            bool aAfter;
            using (var db = DbConfig.CreateConnection()) aAfter = new Schema(db).HasTable("mc_mig_a");

            return dupRejected && !r.Success && r.Applied.SequenceEqual(new[] { "2026_01_01_a" }) && hasA
                && (!ddlTransactional || !hasB) && status.AppliedCount == 1 && status.PendingCount == 1
                && back.Success && back.RolledBack.SequenceEqual(new[] { "2026_01_01_a" }) && !aAfter;
        });

        // 2.0: quoting, async, dirty tracking, row version, upsert, global scopes, SQL log, named connections
        Check("Reserved-word columns (Order, Group, Key)", () =>
        {
            var r = TReserved.Create(new TReserved { Order = "o1", Group = 3, Key = "k" });
            var found = TReserved.Query().Where("Order", "o1").OrderBy("Group").First();
            found.Key = "k2";
            found.Save();
            return r.Id > 0 && found.Id == r.Id && TReserved.Find(r.Id).Key == "k2"
                && TReserved.Query().Select("Group").GroupBy("Group").Count() == 1 && TReserved.Sum("Group") == 3m;
        });

        Check("Async API (Create/Find/Save/Count/Paginate/Get/Delete)", () => Task.Run(async () =>
        {
            var u = await TUser.CreateAsync(new TUser { Name = "Async", Email = "as@x.com", Age = 7 });
            var f = await TUser.FindAsync(u.Id);
            f.Age = 8;
            await f.SaveAsync();
            var n = await TUser.Query().Where("Name", "Async").CountAsync();
            var page = await TUser.Query().OrderBy("Id").PaginateAsync(1, 2);
            var list = await TUser.Query().Where("Age", 8).GetAsync();
            var eager = await TUser.Query().Where("Id", ali.Id).With("Orders").GetAsync();
            var deleted = await f.DeleteAsync();
            return f.Id == u.Id && n == 1 && page.Items.Count == 2 && list.Any(x => x.Id == u.Id)
                && eager.Single().Orders.Count == 2 && deleted && await TUser.FindAsync(u.Id) == null;
        }).GetAwaiter().GetResult());

        Check("Save writes only changed columns; another user's change survives", () =>
        {
            var a = TUser.Find(ali.Id);
            var b = TUser.Find(ali.Id);
            a.Age = 50;
            var dirtyKeys = a.GetDirty().Keys.ToList();
            var sqls = new List<string>();
            Action<QueryLogEntry> h = e => sqls.Add(e.Sql);
            MersoLog.QueryExecuted += h;
            try
            {
                a.Save();
                a.Save(); // clean → no SQL
            }
            finally { MersoLog.QueryExecuted -= h; }

            b.Email = "ali2@x.com";
            b.Save();
            var fresh = TUser.Find(ali.Id);
            return dirtyKeys.SequenceEqual(new[] { "Age" }) && a.IsClean() && sqls.Count == 1 && !sqls[0].Contains("Email")
                && fresh.Age == 50 && fresh.Email == "ali2@x.com" && (string)b.GetOriginal("Email") == "ali2@x.com";
        });

        Check("RowVersion: stale save and delete throw DbConcurrencyException", () =>
        {
            var v = TVersioned.Create(new TVersioned { Title = "t" });
            var a = TVersioned.Find(v.Id);
            var b = TVersioned.Find(v.Id);
            a.Title = "a";
            a.Save();
            bool saveThrew = false, deleteThrew = false;
            b.Note = "b";
            try { b.Save(); } catch (DbConcurrencyException) { saveThrew = true; }
            try { b.Delete(); } catch (DbConcurrencyException) { deleteThrew = true; }
            var fresh = TVersioned.Find(v.Id);
            return v.Version == 1 && a.Version == 2 && saveThrew && deleteThrew
                && fresh.Title == "a" && fresh.Note == null && fresh.Version == 2 && a.Delete() && TVersioned.Find(v.Id) == null;
        });

        Check("Upsert inserts new rows and updates existing ones", () =>
        {
            using (var db = DbConfig.CreateConnection())
                new Schema(db).CreateUniqueIndex("mc_products", "uq_mc_products_code", "Code");

            var first = BulkOperations.Upsert(new[]
            {
                new TProduct { Code = "P1", Name = "Bir", Price = 10m, IsActive = true },
                new TProduct { Code = "P2", Name = "İki", Price = 20m, IsActive = true }
            }, new[] { "Code" });
            var second = BulkOperations.Upsert(new[]
            {
                new TProduct { Code = "P2", Name = "İki yeni", Price = 25m, IsActive = true },
                new TProduct { Code = "P3", Name = "Üç", Price = 30m, IsActive = true }
            }, new[] { "Code" });
            var all = TProduct.Query().OrderBy("Code").Get();
            return first == 2 && second >= 2 && all.Count == 3 && all[1].Name == "İki yeni" && all[1].Price == 25m && all[2].Code == "P3";
        });

        Check("[GlobalScope] filters every query; WithoutGlobalScopes shows all", () =>
        {
            TProduct.Create(new TProduct { Code = "P9", Name = "Pasif", Price = 1m, IsActive = false });
            var visible = TProduct.Count();
            var all = TProduct.Query().WithoutGlobalScopes().Count();
            int inBlock;
            using (GlobalScopeManager.WithoutGlobalScopes<TProduct>())
                inBlock = TProduct.All().Count;
            return visible == 3 && all == 4 && inBlock == 4 && TProduct.Count() == 3 && TProduct.Query().Where("Code", "P9").First() == null;
        });

        Check("MersoLog: SQL, duration, failure, parameter values hidden", () =>
        {
            var entries = new List<QueryLogEntry>();
            Action<QueryLogEntry> h = e => entries.Add(e);
            MersoLog.QueryExecuted += h;
            try
            {
                TUser.Query().Where("Id", ali.Id).First();
                try { RawQuery.Execute("UPDATE mc_no_such_table SET x = 1"); } catch { }
            }
            finally { MersoLog.QueryExecuted -= h; }

            return entries.Count == 2 && entries[0].Succeeded && entries[0].Sql.Contains("mc_users")
                && entries[0].Parameters.Count >= 1 && entries[0].Parameters.All(p => p.Value == null) && entries[0].Duration >= TimeSpan.Zero
                && !entries[1].Succeeded && entries[1].Exception != null;
        });

        // 2.0 (second part): composite keys, query builder, relations, schema changes, batches, retry, distributed cache
        Check("Create tables for the relation / composite key tests", () =>
        {
            using (var db = DbConfig.CreateConnection())
            {
                var s = new Schema(db);
                s.CreateTable("mc_order_lines", t => { t.BigInteger("OrderId").NotNull(); t.String("Sku", 50).NotNull(); t.Integer("Qty"); t.Primary("OrderId", "Sku"); });
                s.CreateTable("mc_countries", t => { t.Id(); t.String("Name", 50); });
                s.CreateTable("mc_authors", t => { t.Id(); t.Integer("CountryId"); t.String("Name", 50); });
                s.CreateTable("mc_posts", t => { t.Id(); t.Integer("AuthorId"); t.String("Title", 100); t.DateTime("UpdatedAt").Nullable(); });
                s.CreateTable("mc_videos", t => { t.Id(); t.String("Title", 100); });
                s.CreateTable("mc_comments", t => { t.Id(); t.Integer("PostId").Nullable(); t.String("CommentableType", 50).Nullable(); t.BigInteger("CommentableId").Nullable(); t.String("Body", 200); });
                s.CreateTable("mc_roles", t => { t.Id(); t.String("Name", 50); });
                s.CreateTable("mc_role_user", t => { t.Integer("UserId").NotNull(); t.Integer("RoleId").NotNull(); t.String("Level", 20).Nullable(); t.Primary("UserId", "RoleId"); });
                s.CreateTable("mc_lines", t => { t.Integer("OrderId").NotNull(); t.Integer("Pos").NotNull(); t.String("Product", 50); t.DateTime("UpdatedAt").Nullable(); t.Primary("OrderId", "Pos"); });
                s.CreateTable("mc_line_taxes", t => { t.Id(); t.Integer("OrderId"); t.Integer("Pos"); t.String("Code", 10); });
                s.CreateTable("mc_schedules", t => { t.Id(); t.Date("Day").NotNull(); t.Time("StartsAt").Nullable(); t.String("Title", 50); });
                s.CreateTable("mc_categories", t => { t.Id(); t.Integer("ParentId").Nullable(); t.String("Name", 50); });
                s.CreateTable("mc_profiles", t => { t.Id(); t.String("Level", 20); t.Text("Tags").Nullable(); t.Text("Secret").Nullable(); });
            }
            return true;
        });

        Check("Composite key: insert, Find(a, b), update, save of an existing key, FindMany, Delete, Destroy", () =>
        {
            TOrderLine.Create(new TOrderLine { OrderId = 1, Sku = "A", Qty = 1 });
            TOrderLine.Create(new TOrderLine { OrderId = 1, Sku = "B", Qty = 2 });
            TOrderLine.Create(new TOrderLine { OrderId = 2, Sku = "A", Qty = 3 });

            var found = TOrderLine.Find(1L, "B");
            found.Qty = 20;
            found.Save();
            new TOrderLine { OrderId = 1, Sku = "A", Qty = 9 }.Save();   // not loaded, key exists → update, no duplicate

            var many = TOrderLine.FindMany(new object[] { 1L, "A" }, new object[] { 2L, "A" });
            var counts = TOrderLine.Count();
            var deleted = TOrderLine.Find(2L, "A").Delete();
            var destroyed = TOrderLine.Destroy(1L, "B");
            var left = TOrderLine.All();

            return found != null && counts == 3 && many.Count == 2 && deleted && destroyed == 1
                && left.Count == 1 && left[0].Sku == "A" && left[0].Qty == 9 && TOrderLine.Find(new object[] { 1L, "A" }) != null;
        });

        Check("Where groups, OrWhereIn / OrWhereNull, WhereNot, WhereColumn, raw bindings", () =>
        {
            var users = TUser.All();
            var grouped = TUser.Query().Where(q => q.Where("Age", ">", 40).OrWhere("Name", "Cem")).Where("IsActive", true).Count();
            var groupedExpected = users.Count(u => (u.Age > 40 || u.Name == "Cem") && u.IsActive);
            var orIn = TUser.Query().Where("Name", "Nobody").OrWhereIn("Id", new object[] { ali.Id }).OrWhereNull("BirthDate").Count();
            var orInExpected = users.Count(u => u.Id == ali.Id || u.BirthDate == null);
            var not = TUser.Query().WhereNot(q => q.Where("Id", ali.Id)).Count();
            var columns = TUser.Query().WhereColumn("CreatedAt", "<=", "UpdatedAt").Count();
            var raw = TUser.Query().WhereRaw("Age > ? AND Name <> ?", 0, "x").Count();
            return grouped == groupedExpected && orIn == orInExpected && not == users.Count - 1
                && columns == users.Count && raw == users.Count(u => u.Age > 0);
        });

        Check("WhereExists / WhereNotExists / WhereIn subquery, Union / UnionAll, SelectRaw, OrderByRaw", () =>
        {
            var withOrders = TUser.Query().WhereExists(TOrder.Query().WhereColumn("mc_orders.UserId", "mc_users.Id")).Count();
            var withoutOrders = TUser.Query().WhereNotExists(TOrder.Query().WhereColumn("mc_orders.UserId", "mc_users.Id")).Count();
            var inSub = TUser.Query().WhereIn("Id", TOrder.Query().Select("UserId")).OrderBy("Id").Get();

            var union = TUser.Query().Where("Id", ali.Id).Union(TUser.Query().Where("Id", deniz.Id)).OrderBy("Id").Get();
            var unionAll = TUser.Query().Where("Id", ali.Id).UnionAll(TUser.Query().Where("Id", ali.Id)).Count();
            var unionPage = TUser.Query().Where("Id", ali.Id).Union(TUser.Query().Where("Id", deniz.Id)).OrderByDesc("Id").Paginate(1, 1);

            var total = TUser.Query().SelectRaw("COUNT(*) AS Total").First().GetAttribute<long>("Total");
            var older = TUser.Query().Select("Id", "Age").SelectRaw("Age + ? AS Older", 1).Where("Id", ali.Id).First();
            var firstByRaw = TUser.Query().OrderByRaw("CASE WHEN Id = ? THEN 0 ELSE 1 END, Id", deniz.Id).First();

            return withOrders == 2 && withoutOrders == TUser.Count() - 2 && inSub.Select(u => u.Id).SequenceEqual(new[] { ali.Id, deniz.Id })
                && union.Count == 2 && union[0].Id == ali.Id && union[1].Id == deniz.Id && unionAll == 2
                && unionPage.TotalCount == 2 && unionPage.Items.Single().Id == deniz.Id
                && total == TUser.Count() && older.GetAttribute<int>("Older") == older.Age + 1 && firstByRaw.Id == deniz.Id;
        });

        TPost p1 = null;
        Check("HasManyThrough / HasOneThrough", () =>
        {
            var tr = TCountry.Create(new TCountry { Name = "TR" });
            var de = TCountry.Create(new TCountry { Name = "DE" });
            var a1 = TAuthor.Create(new TAuthor { CountryId = tr.Id, Name = "A1" });
            var a2 = TAuthor.Create(new TAuthor { CountryId = tr.Id, Name = "A2" });
            var a3 = TAuthor.Create(new TAuthor { CountryId = de.Id, Name = "A3" });
            p1 = TPost.Create(new TPost { AuthorId = a1.Id, Title = "P1" });
            TPost.Create(new TPost { AuthorId = a2.Id, Title = "P2" });
            TPost.Create(new TPost { AuthorId = a3.Id, Title = "P3" });

            var trPosts = tr.Posts().GetRelated();
            return trPosts.Count == 2 && trPosts.All(p => p.Title != "P3") && tr.Posts().Count() == 2 && de.FirstPost().GetRelated()?.Title == "P3";
        });

        Check("Polymorphic: MorphMany, Create, MorphTo, eager [MorphMany]", () =>
        {
            var video = TVideo.Create(new TVideo { Title = "V1" });
            p1.Comments().Create(new Dictionary<string, object> { { "Body", "post comment 1" } });
            p1.Comments().Create(new Dictionary<string, object> { { "Body", "post comment 2" } });
            var vc = video.Comments().Create(new Dictionary<string, object> { { "Body", "video comment" } });

            var owner = vc.Commentable().GetRelated();
            var postOwner = TComment.Query().Where("Body", "post comment 1").First().Commentable().GetRelated<TPost>();
            var eager = TPost.Query().Where("Id", p1.Id).With("CommentList").First();
            return p1.Comments().Count() == 2 && video.Comments().GetRelated().Single().Body == "video comment"
                && vc.CommentableType == "TVideo" && owner is TVideo v && v.Id == video.Id && postOwner?.Id == p1.Id
                && eager.CommentList.Count == 2 && eager.CommentList.All(c => c.CommentableType == "TPost");
        });

        Check("Touch: a saved comment updates its post's UpdatedAt; model.Touch()", () =>
        {
            var old = DateTime.UtcNow.AddDays(-3);
            TPost.Query().Where("Id", p1.Id).Update(new Dictionary<string, object> { { "UpdatedAt", old } });
            TComment.Create(new TComment { PostId = p1.Id, Body = "touching" });
            var touched = TPost.Find(p1.Id).UpdatedAt;

            TPost.Query().Where("Id", p1.Id).Update(new Dictionary<string, object> { { "UpdatedAt", old } });
            var ok = TPost.Find(p1.Id).Touch();
            var touched2 = TPost.Find(p1.Id).UpdatedAt;
            return touched > old.AddDays(1) && ok && touched2 > old.AddDays(1);
        });

        Check("Eager [MorphTo]: owners of several types (one query per type), nested With(\"Owner.CommentList\")", () =>
        {
            var comments = TComment.Query().OrderBy("Id").With("Owner").Get();
            var typed = comments.Where(c => !string.IsNullOrEmpty(c.CommentableType)).ToList();
            var nested = TComment.Query().Where("CommentableType", "TPost").With("Owner.CommentList").Get();
            return typed.Count == 3 && typed.All(c => c.Owner != null && MorphMap.NameOf(c.Owner.GetType()) == c.CommentableType)
                && comments.Where(c => string.IsNullOrEmpty(c.CommentableType)).All(c => c.Owner == null)
                && typed.Count(c => c.Owner is TPost) == 2 && typed.Single(c => c.Owner is TVideo).Body == "video comment"
                && nested.Count == 2 && nested.All(c => ((TPost)c.Owner).CommentList.Count == 2);
        });

        Check("Composite key relations: HasMany / HasOne / BelongsTo, eager both ways, Associate, two-column [Touches]", () =>
        {
            var l1 = TLine.Create(new TLine { OrderId = 7, Pos = 1, Product = "a" });
            var l2 = TLine.Create(new TLine { OrderId = 7, Pos = 2, Product = "b" });
            var l3 = TLine.Create(new TLine { OrderId = 8, Pos = 1, Product = "c" });
            l1.Taxes().Create(new Dictionary<string, object> { { "Code", "VAT" } });
            l1.Taxes().Create(new Dictionary<string, object> { { "Code", "ECO" } });
            l3.Taxes().Create(new Dictionary<string, object> { { "Code", "VAT" } });

            var counts = l1.Taxes().Count() == 2 && l2.Taxes().Count() == 0 && l3.Taxes().GetRelated().Single().Code == "VAT"
                         && l3.FirstTax().GetRelated()?.Code == "VAT" && l1.Taxes().Query().Where("Code", "VAT").Count() == 1;

            var tax = TLineTax.Query().Where("Code", "ECO").First();
            var owner = tax.LineOf().GetRelated();
            var belongs = owner != null && owner.OrderId == 7 && owner.Pos == 1;

            var eager = TLine.Query().Where("OrderId", 7).OrderBy("Pos").With("TaxList").Get();
            var eagerOk = eager.Count == 2 && eager[0].TaxList.Count == 2 && eager[1].TaxList.Count == 0;
            var back = TLineTax.Query().OrderBy("Id").With("Line").Get();
            var backOk = back.Count == 3 && back.All(t => t.Line != null && t.Line.OrderId == t.OrderId && t.Line.Pos == t.Pos);

            // Moving the tax to line 2 touches the old and the new line, not line (8, 1)
            var old = DateTime.UtcNow.AddDays(-3);
            TLine.Query().Where("OrderId", ">", 0).Update(new Dictionary<string, object> { { "UpdatedAt", old } });
            tax.LineOf().Associate(l2);
            var touchedOld = TLine.Find(7, 1).UpdatedAt;
            var touchedNew = TLine.Find(7, 2).UpdatedAt;
            var untouched = TLine.Find(8, 1).UpdatedAt;
            var moved = l2.Taxes().Count() == 1 && l1.Taxes().Count() == 1;
            var deleted = l1.Taxes().DeleteAll() == 1 && l1.Taxes().Count() == 0;

            return counts && belongs && eagerOk && backOk && moved && deleted
                && touchedOld > old.AddDays(1) && touchedNew > old.AddDays(1) && untouched < old.AddDays(1);
        });

        Check("BelongsToMany pivot columns: Attach data, WithPivot, WherePivot, UpdateExistingPivot, Sync with data", () =>
        {
            var admin = TRole.Create(new TRole { Name = "admin" });
            var editor = TRole.Create(new TRole { Name = "editor" });
            var u = TUser.Find(ali.Id);
            u.Roles().Attach(admin.Id, new Dictionary<string, object> { { "Level", "high" } });
            u.Roles().Attach(editor.Id, new Dictionary<string, object> { { "Level", "low" } });

            var roles = u.Roles().WithPivot("Level").GetRelated();
            var high = u.Roles().WherePivot("Level", "high").GetRelated();
            var changed = u.Roles().UpdateExistingPivot(editor.Id, new Dictionary<string, object> { { "Level", "mid" } });
            var editorLevel = u.Roles().WithPivot("Level").GetRelated().Single(r => r.Id == editor.Id).GetPivot()["Level"];
            u.Roles().Sync(new Dictionary<object, Dictionary<string, object>> { { admin.Id, new Dictionary<string, object> { { "Level", "top" } } } });
            var after = u.Roles().WithPivot("Level").GetRelated();

            return roles.Count == 2 && (string)roles.Single(r => r.Id == admin.Id).GetPivot()["Level"] == "high"
                && high.Single().Id == admin.Id && changed == 1 && (string)editorLevel == "mid"
                && after.Count == 1 && (string)after[0].GetPivot()["Level"] == "top";
        });

        Check("2.1 DateOnly / TimeOnly: save, read back, null, Where with DateOnly, Find", () =>
        {
            var a = TSchedule.Create(new TSchedule { Day = new DateOnly(2026, 10, 6), StartsAt = new TimeOnly(9, 30), Title = "a" });
            TSchedule.Create(new TSchedule { Day = new DateOnly(2026, 12, 31), StartsAt = null, Title = "b" });
            var back = TSchedule.Find(a.Id);
            var byDay = TSchedule.Query().Where("Day", new DateOnly(2026, 12, 31)).First();
            var later = TSchedule.Query().Where("Day", ">", new DateOnly(2026, 11, 1)).Count();
            back.StartsAt = new TimeOnly(18, 15, 30);
            back.Save();
            var changed = TSchedule.Find(a.Id).StartsAt;
            return back.Day == new DateOnly(2026, 10, 6) && byDay?.Title == "b" && byDay.StartsAt == null
                && later == 1 && changed == new TimeOnly(18, 15, 30);
        });

        Check("2.1 async relations: GetRelatedAsync, CountAsync, CreateAsync, BelongsTo / MorphTo async, AttachAsync, SyncAsync, ContainsAsync", () =>
        {
            var line = TLine.Find(7, 2);
            var before = line.Taxes().CountAsync().GetAwaiter().GetResult();
            var created = line.Taxes().CreateAsync(new Dictionary<string, object> { { "Code", "ASY" } }).GetAwaiter().GetResult();
            var taxes = line.Taxes().GetRelatedAsync().GetAwaiter().GetResult();
            var owner = created.LineOf().GetRelatedAsync().GetAwaiter().GetResult();

            var u = TUser.Find(ali.Id);
            var editor = TRole.Query().Where("Name", "editor").First();
            u.Roles().AttachAsync(editor.Id).GetAwaiter().GetResult();
            var attached = u.Roles().ContainsAsync(editor.Id).GetAwaiter().GetResult();
            u.Roles().SyncAsync(new object[] { editor.Id }).GetAwaiter().GetResult();
            var synced = u.Roles().GetRelatedAsync().GetAwaiter().GetResult();

            var comment = TComment.Query().Where("Body", "video comment").First();
            var video = comment.Commentable().GetRelatedAsync<TVideo>().GetAwaiter().GetResult();

            return taxes.Count == before + 1 && taxes.Any(t => t.Code == "ASY") && owner?.Pos == 2
                && attached && synced.Count == 1 && synced[0].Id == editor.Id && video != null;
        });

        Check("2.1 Cursor / CursorAsync, ChunkAsync / ChunkByIdAsync, FirstOrCreateAsync / UpdateOrCreateAsync, async aggregates", () =>
        {
            var expected = TUser.Count();
            var streamed = TUser.Query().OrderBy("Id").Cursor().Select(u => u.Id).ToList();
            var firstOnly = TUser.Cursor().Take(1).Count();   // leaving the loop early closes the reader

            var asyncIds = new List<int>();
            var e = TUser.Query().OrderBy("Id").CursorAsync().GetAsyncEnumerator();
            try { while (e.MoveNextAsync().AsTask().GetAwaiter().GetResult()) asyncIds.Add(e.Current.Id); }
            finally { e.DisposeAsync().AsTask().GetAwaiter().GetResult(); }

            int chunks = 0, rows = 0, byId = 0;
            TUser.Query().ChunkAsync(2, list => { chunks++; rows += list.Count; return Task.CompletedTask; }).GetAwaiter().GetResult();
            TUser.Query().ChunkByIdAsync(2, list => { byId += list.Count; return Task.CompletedTask; }).GetAwaiter().GetResult();

            var search = new Dictionary<string, object> { { "Name", "async-role" } };
            var created = TRole.FirstOrCreateAsync(search).GetAwaiter().GetResult();
            var again = TRole.FirstOrCreateAsync(search).GetAwaiter().GetResult();
            var updated = TRole.UpdateOrCreateAsync(search, new Dictionary<string, object> { { "Name", "async-role" } }).GetAwaiter().GetResult();

            var maxId = TUser.MaxAsync("Id").GetAwaiter().GetResult();
            var sumAge = TUser.SumAsync("Age").GetAwaiter().GetResult();
            return streamed.Count == expected && streamed.SequenceEqual(streamed.OrderBy(x => x)) && firstOnly == 1
                && asyncIds.SequenceEqual(streamed) && rows == expected && chunks == (expected + 1) / 2 && byId == expected
                && created.Id > 0 && again.Id == created.Id && updated.Id == created.Id
                && Convert.ToInt32(maxId) == streamed.Max() && sumAge == TUser.All().Sum(u => u.Age) && TUser.Count() == expected;
        });

        Check("2.1 WhereHas / WhereDoesntHave / Has(count) / WhereRelation / OrWhereHas, BelongsTo, morph, self relation, nested", () =>
        {
            var users = TUser.All();
            var orders = TOrder.All();
            bool HasOrders(TUser u) => orders.Any(o => o.UserId == u.Id);

            var withOrders = TUser.Query().WhereHas("Orders").Count();
            var without = TUser.Query().WhereDoesntHave("Orders").Count();
            var big = TUser.Query().WhereHas<TOrder>("Orders", q => q.Where("Total", ">", 100)).Count();
            var expectedBig = users.Count(u => orders.Any(o => o.UserId == u.Id && o.Total > 100));
            var relation = TUser.Query().WhereRelation("Orders", "Total", ">", 100).Count();
            var twoPlus = TUser.Query().Has("Orders", ">=", 2).Count();
            var none = TUser.Query().Has("Orders", "=", 0).Count();
            var owners = TOrder.Query().WhereHas<TUser>("User", q => q.Where("Name", ali.Name)).Count();
            var orHas = TUser.Query().Where("Id", -1).OrWhereHas("Orders").Count();

            var root = TCategory.Create(new TCategory { Name = "root" });
            var child = TCategory.Create(new TCategory { Name = "child", ParentId = root.Id });
            TCategory.Create(new TCategory { Name = "grandchild", ParentId = child.Id });
            TCategory.Create(new TCategory { Name = "lonely" });
            var parents = TCategory.Query().WhereHas("Children").Count();
            var grandparents = TCategory.Query().WhereHas<TCategory>("Children", q => q.WhereHas("Children")).Count();
            var withParent = TCategory.Query().WhereHas("Parent").Count();

            var commented = TPost.Query().WhereHas("CommentList").Count();
            var expectedCommented = TPost.All().Count(p => TComment.Query().Where("CommentableType", "TPost").Where("CommentableId", p.Id).Count() > 0);

            return withOrders == users.Count(HasOrders) && without == users.Count(u => !HasOrders(u)) && big == expectedBig && relation == expectedBig
                && twoPlus == users.Count(u => orders.Count(o => o.UserId == u.Id) >= 2) && none == without
                && owners == orders.Count(o => o.UserId == ali.Id) && orHas == withOrders
                && parents == 2 && grandparents == 1 && withParent == 2 && commented == expectedCommented && commented > 0;
        });

        Check("2.1 WithCount / WithSum / WithExists, conditions + alias, [Computed] property (read, never written)", () =>
        {
            var orders = TOrder.All();
            var list = TUser.Query().OrderBy("Id").WithCount("Orders").WithSum("Orders", "Total").WithExists("Orders").Get();
            var counts = list.All(u =>
            {
                var mine = orders.Where(o => o.UserId == u.Id).ToList();
                return u.GetAttribute<int>("OrdersCount") == mine.Count
                    && (mine.Count == 0 ? u.GetAttribute("OrdersSumTotal") == null : u.GetAttribute<decimal>("OrdersSumTotal") == mine.Sum(o => o.Total))
                    && u.GetAttribute<int>("OrdersExists") == (mine.Count > 0 ? 1 : 0);
            });

            var named = TUser.Query().WithCount<TOrder>("Orders", q => q.Where("Total", ">", 100), "BigOrders").WithCount("Orders as AllOrders").Get();
            var namedOk = named.All(u => u.GetAttribute<int>("BigOrders") == orders.Count(o => o.UserId == u.Id && o.Total > 100)
                                        && u.GetAttribute<int>("AllOrders") == orders.Count(o => o.UserId == u.Id));

            var stats = TUserStat.Query().WithCount("Orders").OrderBy("Id").Get();
            var stat = stats.First(s => s.Id == ali.Id);
            var oldName = stat.Name;
            stat.Name = oldName + "!";
            var saved = stat.Save();                       // OrdersCount is not a column: never written
            var reloaded = TUserStat.Find(ali.Id);
            reloaded.Name = oldName;
            reloaded.Save();

            return counts && namedOk && list.Count == TUser.Count()
                && stat.OrdersCount == orders.Count(o => o.UserId == ali.Id) && stat.ToDict().ContainsKey("OrdersCount")
                && saved && reloaded.OrdersCount == 0;
        });

        Check("2.1 With<T>(relation, conditions / order), nested path constraint, wrong related type rejected", () =>
        {
            var orders = TOrder.All();
            var users = TUser.Query().OrderBy("Id").With<TOrder>("Orders", q => q.Where("Total", ">", 100).OrderByDesc("Total").OrderBy("Id")).Get();
            var filtered = users.All(u => u.Orders.Select(o => o.Id).SequenceEqual(
                orders.Where(o => o.UserId == u.Id && o.Total > 100).OrderByDescending(o => o.Total).ThenBy(o => o.Id).Select(o => o.Id)));

            var roots = TCategory.Query().Where("Name", "root").With<TCategory>("Children.Children", q => q.Where("Name", "grandchild")).Get();
            var nested = roots.Single().Children.Single().Children.Single().Name == "grandchild";

            var rejected = false;
            try { TUser.Query().With<TRole>("Orders", q => { }); } catch (ArgumentException) { rejected = true; }

            var asyncLoaded = TUser.Query().With<TOrder>("Orders", q => q.Where("Total", ">", 100)).GetAsync().GetAwaiter().GetResult();
            return filtered && nested && rejected && asyncLoaded.Sum(u => u.Orders.Count) == orders.Count(o => o.Total > 100 && users.Any(u => u.Id == o.UserId));
        });

        Check("2.1 SimplePaginate (no count), CursorPaginate (keyset: DESC + id tie-breaker, forward to the end, one page back)", () =>
        {
            var all = TUser.Query().OrderBy("Id").Get();
            var first = TUser.Query().OrderBy("Id").SimplePaginate(1, 2);
            var lastPage = TUser.Query().OrderBy("Id").SimplePaginate((all.Count + 1) / 2, 2);

            var ordered = all.OrderByDescending(u => u.Age).ThenBy(u => u.Id).Select(u => u.Id).ToList();
            var seen = new List<int>();
            string cursor = null;
            CursorPaginatedResult<TUser> page;
            int pages = 0;
            do
            {
                page = TUser.Query().OrderByDesc("Age").CursorPaginate(2, cursor);
                seen.AddRange(page.Items.Select(u => u.Id));
                cursor = page.NextCursor;
                pages++;
            } while (cursor != null && pages < 50);

            var lastStart = (pages - 1) * 2;
            var back = page.PreviousCursor == null ? null : TUser.Query().OrderByDesc("Age").CursorPaginate(2, page.PreviousCursor);
            var backOk = pages == 1 ? back == null
                : back.Items.Select(u => u.Id).SequenceEqual(ordered.Skip(lastStart - 2).Take(2)) && back.HasNextPage && back.HasPreviousPage == (lastStart - 2 > 0);

            var rejected = false;
            try { TUser.Query().CursorPaginate(2, "not-a-cursor"); } catch (ArgumentException) { rejected = true; }

            return first.Items.Select(u => u.Id).SequenceEqual(all.Take(2).Select(u => u.Id)) && first.HasNextPage == (all.Count > 2) && !first.HasPreviousPage
                && !lastPage.HasNextPage && seen.SequenceEqual(ordered) && backOk && rejected;
        });

        Check("2.1 converters: [EnumAsString], [JsonColumn] list, [Encrypted]; Where / WhereIn convert, in-place list change saved", () =>
        {
            EncryptedStringConverter.Key = "test-key-1";
            try
            {
                var p = TProfile.Create(new TProfile { Level = TLevel.High, Tags = new List<string> { "a", "b" }, Secret = "s3cret" });
                TProfile.Create(new TProfile { Level = TLevel.Low, Secret = "x" });

                var raw = RawQuery.QueryTable("SELECT Level, Tags, Secret FROM mc_profiles WHERE Id = @id", new Dictionary<string, object> { { "id", p.Id } }).Rows[0];
                var stored = (string)raw[0] == "High" && raw[1].ToString() == "[\"a\",\"b\"]" && raw[2].ToString() != "s3cret" && raw[2].ToString().Length > 20;

                var back = TProfile.Find(p.Id);
                var read = back.Level == TLevel.High && back.Tags.SequenceEqual(new[] { "a", "b" }) && back.Secret == "s3cret";
                var clean = !back.IsDirty();                // encrypted values get a new IV per conversion

                var byLevel = TProfile.Query().Where("Level", TLevel.High).Count();
                var inLevels = TProfile.Query().WhereIn("Level", new object[] { TLevel.High, TLevel.Low }).Count();

                back.Tags.Add("c");                         // changed in place: still seen as a change
                var dirty = back.IsDirty("Tags") && !back.IsDirty("Secret") && !back.IsDirty("Level");
                back.Save();
                var saved = TProfile.Find(p.Id).Tags.Count;

                var again = TProfile.Find(p.Id);
                again.Secret = "new";
                var secretDirty = again.IsDirty("Secret");
                again.Save();
                var secretSaved = TProfile.Find(p.Id).Secret == "new";

                return stored && read && clean && byLevel == 1 && inLevels == 2 && dirty && saved == 3 && secretDirty && secretSaved;
            }
            finally
            {
                EncryptedStringConverter.Key = null;
            }
        });

        Check("2.1 BulkCopy / BulkCopyAsync: provider bulk API or INSERT fallback; quotes, commas, NULL, DateOnly, converters, rollback", () =>
        {
            EncryptedStringConverter.Key = "test-key-1";
            var log = new List<QueryLogEntry>();
            Action<QueryLogEntry> collect = e => { lock (log) log.Add(e); };
            MersoLog.QueryExecuted += collect;
            try
            {
                var before = TSchedule.Count();
                var schedules = Enumerable.Range(1, 300).Select(i => new TSchedule
                {
                    Day = new DateOnly(2027, 1, 1).AddDays(i),
                    StartsAt = i % 2 == 0 ? new TimeOnly(8, i % 60) : (TimeOnly?)null,
                    Title = "bulk \"" + i + "\", ok"
                }).ToList();
                var copied = BulkOperations.BulkCopy(schedules);

                var profiles = Enumerable.Range(1, 20).Select(i => new TProfile { Level = i % 2 == 0 ? TLevel.High : TLevel.Low, Tags = new List<string> { "t" + i }, Secret = "s" + i }).ToList();
                var copiedAsync = BulkOperations.BulkCopyAsync(profiles).GetAwaiter().GetResult();

                var sample = TSchedule.Query().Where("Title", "bulk \"8\", ok").First();
                var odd = TSchedule.Query().Where("Title", "bulk \"7\", ok").First();
                var profile = TProfile.Query().Where("Level", TLevel.High).OrderByDesc("Id").First();

                var rolledBack = false;
                try
                {
                    MersoTransaction.Run(() =>
                    {
                        BulkOperations.BulkCopy(new[] { new TSchedule { Day = new DateOnly(2030, 1, 1), Title = "rollback" } });
                        throw new InvalidOperationException("stop");
                    });
                }
                catch (InvalidOperationException)
                {
                    rolledBack = TSchedule.Query().Where("Title", "rollback").Count() == 0;
                }

                // MySQL / MariaDB: MySqlBulkCopy needs AllowLoadLocalInfile=true (and local_infile on the server)
                var provider = DbConfig.ProviderType;
                if (provider == DbProviderType.MySQL || provider == DbProviderType.MariaDB)
                {
                    var cs = Environment.GetEnvironmentVariable(provider == DbProviderType.MySQL ? MySqlEnv : MariaDbEnv) + ";Database=mersocore_test;AllowLoadLocalInfile=true";
                    using (MersoConnection.Use(() => provider == DbProviderType.MySQL ? new mersolutionCore.Command.MySQL.MySqlCommand(cs) : new mersolutionCore.Command.MariaDB.MariaDbCommand(cs)))
                        copied += BulkOperations.BulkCopy(new[] { new TSchedule { Day = new DateOnly(2031, 1, 1), Title = "infile" } });
                }

                var native = log.Any(e => e.Succeeded && (e.Sql.StartsWith("INSERT BULK") || e.Sql.StartsWith("COPY ") || e.Sql.StartsWith("LOAD DATA")));
                Console.WriteLine($"[info] {provider} BulkCopy: {(native ? "native bulk API" : "INSERT fallback")}");

                var expectedCopied = 300 + (provider == DbProviderType.MySQL || provider == DbProviderType.MariaDB ? 1 : 0);
                var nativeRequired = provider == DbProviderType.SqlServer || provider == DbProviderType.PostgreSQL;
                return copied == expectedCopied && TSchedule.Count() == before + expectedCopied && copiedAsync == 20
                    && sample?.Day == new DateOnly(2027, 1, 9) && sample.StartsAt == new TimeOnly(8, 8) && odd.StartsAt == null
                    && profile.Tags.Count == 1 && profile.Secret.StartsWith("s") && rolledBack && (native || !nativeRequired);
            }
            finally
            {
                MersoLog.QueryExecuted -= collect;
                EncryptedStringConverter.Key = null;
            }
        });

        Check("2.1 lambda Where / OrWhere / WhereNot / OrderBy / Pluck / Select: captured values, IN, NULL, enums, LIKE escaping", () =>
        {
            var users = TUser.All();
            var min = 30;
            var older = TUser.Query().Where(u => u.Age > min).Count() == users.Count(u => u.Age > min);
            var or = TUser.Query().Where(u => u.Age < 0).OrWhere(u => u.Id == ali.Id).Count() == 1;

            var prefix = ali.Name.Substring(0, 2);
            var starts = TUser.Where(u => u.Name.StartsWith(prefix)).Count();
            var startsOk = starts == users.Count(u => u.Name.StartsWith(prefix, StringComparison.Ordinal))
                           || starts == users.Count(u => u.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

            var ids = users.Take(2).Select(u => u.Id).ToList();
            var inList = TUser.Where(u => ids.Contains(u.Id)).Count() == 2;
            var inactive = TUser.Query().WhereNot(u => u.IsActive).Count() == users.Count(u => !u.IsActive);
            var negated = TUser.Where(u => !u.IsActive && u.Age >= 0).Count() == users.Count(u => !u.IsActive);
            var nulls = TUser.Where(u => u.BirthDate == null).Count() == users.Count(u => u.BirthDate == null);
            var empty = TUser.Where(u => string.IsNullOrEmpty(u.Email)).Count() == users.Count(u => string.IsNullOrEmpty(u.Email));
            var active = TUser.Where(u => u.Status == UserStatus.Active).Count() == users.Count(u => u.Status == UserStatus.Active);
            var columns = TUser.Where(u => u.CreatedAt <= u.UpdatedAt).Count() == users.Count(u => u.CreatedAt <= u.UpdatedAt);

            var ordered = TUser.Query().OrderByDesc(u => u.Age).OrderBy(u => u.Id).Pluck(u => u.Id)
                .SequenceEqual(users.OrderByDescending(u => u.Age).ThenBy(u => u.Id).Select(u => u.Id));
            var selected = TUser.Query().Select(u => u.Id, u => u.Name).Where(u => u.Id == ali.Id).First();

            TCategory.Create(new TCategory { Name = "50%_off" });
            TCategory.Create(new TCategory { Name = "50xyoff" });
            var escaped = TCategory.Where(c => c.Name.Contains("%_")).Count() == 1 && TCategory.Where(c => c.Name.StartsWith("50")).Count() == 2;

            var rejected = false;
            try { TUser.Where(u => u.Name.ToUpper() == "X").Count(); } catch (NotSupportedException) { rejected = true; }

            return older && or && startsOk && inList && inactive && negated && nulls && empty && active && columns && ordered
                && selected.Name == ali.Name && selected.Age == 0 && escaped && rejected;
        });

        Check("2.1 LockForUpdate / SharedLock run inside a transaction", () =>
        {
            var locked = MersoTransaction.Run(() => TUser.Query().Where("Id", ali.Id).LockForUpdate().First());
            var shared = MersoTransaction.Run(() => TUser.Query().Where("Id", ali.Id).SharedLock().Get());
            var page = MersoTransaction.Run(() => TUser.Query().OrderBy("Id").LockForUpdate().Paginate(1, 2));
            return locked?.Id == ali.Id && shared.Count == 1 && page.Items.Count > 0;
        });

        Check("Schema: ChangeColumn, AddForeign, DropForeign (named and unnamed), data kept", () =>
        {
            using (var db = DbConfig.CreateConnection())
            {
                var s = new Schema(db);
                s.CreateTable("mc_parents", t => { t.Id(); t.String("Name", 20); });
                s.CreateTable("mc_children", t => { t.Id(); t.Integer("ParentId").Nullable(); t.String("Code", 10); });
                RawQuery.Execute("INSERT INTO mc_parents (Name) VALUES ('p')");
                var pid = RawQuery.Scalar<long>("SELECT MAX(Id) FROM mc_parents");
                Action<long, string> child = (parent, code) =>
                    RawQuery.Execute("INSERT INTO mc_children (ParentId, Code) VALUES (@p, @c)", new Dictionary<string, object> { { "p", parent }, { "c", code } });

                child(pid, "short");
                s.ChangeColumn("mc_children", t => t.String("Code", 50).NotNull());
                child(pid, new string('x', 30));   // longer than the old VARCHAR(10)

                s.AddForeign("mc_children", "ParentId", "mc_parents");
                var enforced = DbConfig.ProviderType != DbProviderType.SQLite
                               || RawQuery.Scalar<long>("PRAGMA foreign_keys") == 1;
                var rejected = false;
                try { child(999999, "bad"); } catch { rejected = true; }

                var dropped = s.DropForeign("mc_children", "ParentId");
                child(999999, "free");

                s.Raw("CREATE TABLE mc_legacy (Id INT PRIMARY KEY, ParentId INT, FOREIGN KEY (ParentId) REFERENCES mc_parents(Id))");
                var droppedLegacy = s.DropForeign("mc_legacy", "ParentId");

                var codes = RawQuery.QueryTable("SELECT Code FROM mc_children ORDER BY Id").Rows.Count;
                s.DropTableIfExists("mc_legacy");
                s.DropTableIfExists("mc_children");
                s.DropTableIfExists("mc_parents");

                return (!enforced || rejected) && dropped == 1 && droppedLegacy == 1 && codes == 3;
            }
        });

        Check("Migrator batches: Rollback() undoes the last Migrate() only, Rollback(n), 1.x table gets Batch", () =>
        {
            var m = new Migrator(DbConfig.CreateConnection, "mc_batches");
            m.Add(new MigC());
            var r1 = m.Migrate();
            m.Add(new MigD(), new MigE());
            var r2 = m.Migrate();
            var status = m.Status();
            var back = m.Rollback();
            bool c, d, e;
            using (var db = DbConfig.CreateConnection())
            {
                var s = new Schema(db);
                c = s.HasTable("mc_mig_c"); d = s.HasTable("mc_mig_d"); e = s.HasTable("mc_mig_e");
            }
            var back2 = m.Rollback(1);
            bool cAfter;
            using (var db = DbConfig.CreateConnection()) cAfter = new Schema(db).HasTable("mc_mig_c");

            // A migrations table from 1.x (no Batch column)
            using (var db = DbConfig.CreateConnection())
                new Schema(db).CreateTable("mc_old_migrations", t => { t.Id(); t.String("Version", 100).NotNull(); t.String("Description", 255); t.DateTime("AppliedAt").Nullable(); });
            RawQuery.Execute("INSERT INTO mc_old_migrations (Version, Description) VALUES ('2026_02_01_c', 'old')");
            var old = new Migrator(DbConfig.CreateConnection, "mc_old_migrations").Add(new MigC()).Status();

            return r1.Success && r2.Success && status.Migrations.Single(x => x.Version == "2026_02_01_c").Batch == 1
                && status.Migrations.Single(x => x.Version == "2026_02_02_d").Batch == 2
                && back.RolledBack.SequenceEqual(new[] { "2026_02_03_e", "2026_02_02_d" }) && c && !d && !e
                && back2.RolledBack.SequenceEqual(new[] { "2026_02_01_c" }) && !cAfter
                && old.AppliedCount == 1 && old.Migrations.Single().Batch == 1;
        });

        Check("Retry: transient error retried with the same parameters, not inside a transaction; transaction re-run", () =>
        {
            var oldRule = MersoRetry.IsTransient;
            var oldDelay = MersoRetry.BaseDelay;
            int retries = 0;
            Action<Exception, int, TimeSpan> onRetry = (error, attempt, delay) =>
            {
                retries++;
                if (retries == 1)   // first retry: create the missing table, so the next attempt works
                    using (var db = DbConfig.CreateConnection()) new Schema(db).CreateTable("mc_retry", t => { t.Id(); t.Integer("N"); });
            };
            MersoRetry.IsTransient = ex => ex.Message.IndexOf("mc_retry", StringComparison.OrdinalIgnoreCase) >= 0;
            MersoRetry.BaseDelay = TimeSpan.Zero;
            MersoRetry.Retrying += onRetry;
            try
            {
                var n = RawQuery.Scalar<long>("SELECT COUNT(*) FROM mc_retry WHERE N > @n", new Dictionary<string, object> { { "n", 0 } });
                var afterFirst = retries;

                var threwInTx = false;
                try { MersoTransaction.Run(() => RawQuery.Scalar<long>("SELECT COUNT(*) FROM mc_retry_missing")); }
                catch { threwInTx = true; }
                var notRetriedInTx = retries == afterFirst;

                int runs = 0;
                MersoTransaction.Run(() =>
                {
                    runs++;
                    RawQuery.Execute("INSERT INTO mc_retry (N) VALUES (1)");
                    if (runs == 1)
                        throw new InvalidOperationException("mc_retry: simulated deadlock");
                }, 3);
                var rows = RawQuery.Scalar<long>("SELECT COUNT(*) FROM mc_retry");

                return n == 0 && afterFirst == 1 && threwInTx && notRetriedInTx && runs == 2 && rows == 1;
            }
            finally
            {
                MersoRetry.Retrying -= onRetry;
                MersoRetry.IsTransient = oldRule;
                MersoRetry.BaseDelay = oldDelay;
                using (var db = DbConfig.CreateConnection()) new Schema(db).DropTableIfExists("mc_retry");
            }
        });

        Check("Query cache on a distributed store: shared JSON entries come back as loaded models", () =>
        {
            Cache.UseStore(new TestCacheStore());
            try
            {
                var first = TUser.Query().Where("Id", ali.Id).Remember(TimeSpan.FromMinutes(1));
                var second = TUser.Query().Where("Id", ali.Id).Remember(TimeSpan.FromMinutes(1));
                QCache.ForgetTable("mc_users");
                var third = TUser.Query().Where("Id", ali.Id).Remember(TimeSpan.FromMinutes(1));
                return first.Single().Id == ali.Id && !ReferenceEquals(first[0], second[0]) && second[0].Name == first[0].Name
                    && second[0].IsClean() && third.Single().Id == ali.Id;
            }
            finally
            {
                Cache.UseMemory();
            }
        });

        var secondPath = Path.Combine(Path.GetTempPath(), "mc_second_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            Check("Named connections: [Connection], On(name), MersoConnection.Use, save back", () =>
            {
                DbConfig.AddConnection("second", "Data Source=" + secondPath, DbProviderType.SQLite);
                using (var db = DbConfig.CreateConnection("second"))
                {
                    var s = new Schema(db);
                    s.CreateTable("mc_logs", t => { t.Id(); t.String("Message", 200); });
                    s.CreateTable("mc_users", t =>
                    {
                        t.Id(); t.String("Name", 100); t.String("Email"); t.Integer("Age"); t.Boolean("IsActive");
                        t.Integer("Status"); t.Decimal("Balance"); t.DateTime("BirthDate"); t.Timestamps(); t.SoftDeletes();
                    });
                }

                var log = TLog.Create(new TLog { Message = "ikinci db" });
                var logs = RawQuery.Scalar<long>("SELECT COUNT(*) FROM mc_logs", null, "second");

                int scopedCount;
                using (MersoConnection.Use("second"))
                {
                    TUser.Create(new TUser { Name = "Scoped", Email = "s@x.com" });
                    scopedCount = TUser.Count();
                }
                var onDefault = TUser.Query().Where("Name", "Scoped").Count();

                var loaded = TUser.On("second").Where("Name", "Scoped").First();
                loaded.Age = 77;
                loaded.Save();
                var reread = TUser.On("second").Where("Name", "Scoped").First();

                return log.Id > 0 && logs == 1 && scopedCount == 1 && onDefault == 0 && reread.Age == 77;
            });

            Check("Transactions on two databases nest correctly", () =>
            {
                try
                {
                    MersoTransaction.Run(() =>
                    {
                        TUser.Create(new TUser { Name = "TxOuter", Email = "o@x.com" });
                        MersoTransaction.Run("second", () => { TLog.Create(new TLog { Message = "inner" }); });
                        throw new InvalidOperationException("outer fails");
                    });
                }
                catch (InvalidOperationException) { }

                try
                {
                    MersoTransaction.Run(() =>
                    {
                        // default-DB write inside the second DB's transaction still joins the outer one
                        MersoTransaction.Run("second", () => { TUser.Create(new TUser { Name = "TxJoin", Email = "j@x.com" }); });
                        throw new InvalidOperationException("outer fails");
                    });
                }
                catch (InvalidOperationException) { }

                return TUser.Query().Where("Name", "TxOuter").Count() == 0
                    && RawQuery.Scalar<long>("SELECT COUNT(*) FROM mc_logs WHERE Message = 'inner'", null, "second") == 1
                    && TUser.Query().Where("Name", "TxJoin").Count() == 0;
            });
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(secondPath); } catch { }
        }
    }

    public class MigA : Migration
    {
        public override string Version => "2026_01_01_a";
        public override void Up(Schema s) => s.CreateTable("mc_mig_a", t => { t.Id(); t.String("Name", 50); });
        public override void Down(Schema s) => s.DropTableIfExists("mc_mig_a");
    }

    public class MigADuplicate : Migration
    {
        public override string Version => "2026_01_01_a";
        public override void Up(Schema s) { }
        public override void Down(Schema s) { }
    }

    public class MigBFails : Migration
    {
        public override string Version => "2026_01_02_b";
        public override void Up(Schema s)
        {
            s.CreateTable("mc_mig_b", t => { t.Id(); });
            throw new InvalidOperationException("boom");
        }
        public override void Down(Schema s) => s.DropTableIfExists("mc_mig_b");
    }

    public class MigC : Migration
    {
        public override string Version => "2026_02_01_c";
        public override void Up(Schema s) => s.CreateTable("mc_mig_c", t => { t.Id(); });
        public override void Down(Schema s) => s.DropTableIfExists("mc_mig_c");
    }

    public class MigD : Migration
    {
        public override string Version => "2026_02_02_d";
        public override void Up(Schema s) => s.CreateTable("mc_mig_d", t => { t.Id(); });
        public override void Down(Schema s) => s.DropTableIfExists("mc_mig_d");
    }

    public class MigE : Migration
    {
        public override string Version => "2026_02_03_e";
        public override void Up(Schema s) => s.CreateTable("mc_mig_e", t => { t.Id(); });
        public override void Down(Schema s) => s.DropTableIfExists("mc_mig_e");
    }

    static void DialectTests()
    {
        ModelBase.Configure(DbConfig.CreateConnection);

        DbConfig.Configure("Server=none", DbProviderType.SqlServer);
        Check("SqlServer TOP without offset", () => TUser.Query().Take(5).ToSql() == "SELECT TOP 5 * FROM [mc_users] WHERE [DeletedAt] IS NULL");
        Check("SqlServer DISTINCT TOP", () => TUser.Query().Distinct().Select("Name").Take(3).ToSql().StartsWith("SELECT DISTINCT TOP 3 [Name]"));
        Check("SqlServer OFFSET without ORDER BY", () => TUser.Query().Skip(10).Take(5).ToSql().EndsWith("ORDER BY (SELECT NULL) OFFSET 10 ROWS FETCH NEXT 5 ROWS ONLY"));
        Check("SqlServer random", () => TUser.Query().InRandomOrder().ToSql().EndsWith("ORDER BY NEWID()"));
        Check("SqlServer OrWhere grouped", () => TUser.Query().Where("A", 1).OrWhere("B", 2).ToSql().Contains("WHERE [DeletedAt] IS NULL AND ([A] = @p0 OR [B] = @p1)"));
        Check("SqlServer reserved words quoted, expressions kept", () =>
            TReserved.Query().Select("Order AS o", "COUNT(*) AS c", "t.*").Where("Order", "x").GroupBy("Order, Group").OrderBy("Group").ToSql()
            == "SELECT [Order] AS [o], COUNT(*) AS c, [t].* FROM [mc_reserved] WHERE [Order] = @p0 GROUP BY [Order], [Group] ORDER BY [Group] ASC");

        Check("SqlServer where group / WhereNot / OrWhereIn / OrWhereNull", () =>
            TUser.Query().Where(q => q.Where("A", 1).OrWhere("B", 2)).Where("C", 3).ToSql()
                == "SELECT * FROM [mc_users] WHERE [DeletedAt] IS NULL AND (([A] = @p0 OR [B] = @p1) AND [C] = @p2)"
            && TUser.Query().WhereNot(q => q.Where("A", 1)).OrWhereIn("Id", new object[] { 1, 2 }).OrWhereNull("B").ToSql()
                == "SELECT * FROM [mc_users] WHERE [DeletedAt] IS NULL AND (NOT ([A] = @p0) OR [Id] IN (@p1, @p2) OR [B] IS NULL)"
            && TUser.Query().Where(q => { }).Where("A", 1).ToSql() == "SELECT * FROM [mc_users] WHERE [DeletedAt] IS NULL AND ([A] = @p0)");
        Check("SqlServer exists / in subquery / column compare", () =>
            TUser.Query().WhereExists(TOrder.Query().WhereColumn("mc_orders.UserId", "mc_users.Id")).ToSql()
                == "SELECT * FROM [mc_users] WHERE [DeletedAt] IS NULL AND (EXISTS (SELECT 1 FROM [mc_orders] WHERE [mc_orders].[UserId] = [mc_users].[Id]))"
            && TUser.Query().Where("A", 1).WhereIn("Id", TOrder.Query().Select("UserId").Where("Total", ">", 5)).ToSql()
                == "SELECT * FROM [mc_users] WHERE [DeletedAt] IS NULL AND ([A] = @p0 AND [Id] IN (SELECT [UserId] FROM [mc_orders] WHERE [Total] > @p1))"
            && TUser.Query().WhereColumn("UpdatedAt", ">", "CreatedAt").ToSql()
                == "SELECT * FROM [mc_users] WHERE [DeletedAt] IS NULL AND ([UpdatedAt] > [CreatedAt])");
        Check("SqlServer union keeps TOP / ORDER BY on the combined rows", () =>
            TUser.Query().Where("Id", 1).Union(TUser.Query().Where("Id", 2)).OrderBy("Id").Take(5).ToSql()
                == "SELECT TOP 5 * FROM (SELECT * FROM [mc_users] WHERE [DeletedAt] IS NULL AND ([Id] = @p0) UNION SELECT * FROM [mc_users] WHERE [DeletedAt] IS NULL AND ([Id] = @p1)) mc_union ORDER BY [Id] ASC");
        Check("Raw pieces with ? bindings (quoted ? untouched), count mismatch rejected", () =>
        {
            var sql = TUser.Query().SelectRaw("Age + ? AS Older", 1).WhereRaw("Name = '?' AND Age > ?", 5).OrderByRaw("FIELD(Id, ?, ?)", 3, 4).ToSql();
            var rejected = false;
            try { TUser.Query().WhereRaw("Age > ? AND Age < ?", 1).ToSql(); } catch (ArgumentException) { rejected = true; }
            return sql == "SELECT Age + @p0 AS Older FROM [mc_users] WHERE [DeletedAt] IS NULL AND ((Name = '?' AND Age > @p1)) ORDER BY FIELD(Id, @p2, @p3)"
                && rejected;
        });

        DbConfig.Configure("Server=none", DbProviderType.MySQL);
        Check("MySQL LIMIT/OFFSET", () => TUser.Query().OrderBy("Id").Skip(10).Take(5).ToSql().EndsWith("ORDER BY `Id` ASC LIMIT 5 OFFSET 10"));
        Check("MySQL offset only", () => TUser.Query().Skip(10).ToSql().EndsWith("LIMIT 18446744073709551615 OFFSET 10"));
        Check("MySQL random / date", () => TUser.Query().InRandomOrder().ToSql().EndsWith("ORDER BY RAND()")
            && TUser.Query().WhereDate("CreatedAt", DateTime.Today).ToSql().Contains("DATE(`CreatedAt`) = DATE(@p0)"));
        Check("Global scope in SQL", () => TProduct.Query().ToSql() == "SELECT * FROM `mc_products` WHERE `IsActive` = @p0"
            && TProduct.Query().WithoutGlobalScopes().ToSql() == "SELECT * FROM `mc_products`");

        DbConfig.Configure("Host=none", DbProviderType.PostgreSQL);
        Check("PostgreSQL quotes lower case (same as unquoted CREATE TABLE)", () =>
            TUser.Query().Where("Name", "x").ToSql() == "SELECT * FROM \"mc_users\" WHERE \"deletedat\" IS NULL AND (\"name\" = @p0)");

        DbConfig.Configure("Host=none", DbProviderType.PostgreSQL);
        Check("PostgreSQL offset only / random", () => TUser.Query().Skip(10).ToSql().EndsWith(" OFFSET 10") && !TUser.Query().Skip(10).ToSql().Contains("LIMIT")
            && TUser.Query().InRandomOrder().Take(1).ToSql().EndsWith("ORDER BY RANDOM() LIMIT 1"));

        DbConfig.Configure("Data Source=none.db", DbProviderType.SQLite);
        Check("SQLite offset only", () => TUser.Query().Skip(10).ToSql().EndsWith("LIMIT -1 OFFSET 10"));

        Check("2.1 LockForUpdate / SharedLock SQL per provider (SQLite: no lock), When / Unless / Apply", () =>
        {
            var sqlite = TUser.Query().Where("Id", 1).LockForUpdate().ToSql();
            DbConfig.Configure("Server=none", DbProviderType.SqlServer);
            var mssql = TUser.Query().Where("Id", 1).LockForUpdate().ToSql();
            var mssqlShared = TUser.Query().SharedLock().ToSql();
            DbConfig.Configure("Server=none", DbProviderType.MySQL);
            var mysql = TUser.Query().Where("Id", 1).LockForUpdate().Take(1).ToSql();
            var mysqlShared = TUser.Query().SharedLock().ToSql();
            DbConfig.Configure("Host=none", DbProviderType.PostgreSQL);
            var pg = TUser.Query().LockForUpdate().ToSql();
            var pgShared = TUser.Query().SharedLock().ToSql();

            DbConfig.Configure("Server=none", DbProviderType.SqlServer);
            var whereHas = TUser.Query().WhereHas("Orders").ToSql();
            var withCount = TUser.Query().WithCount("Orders").ToSql();
            var min = 18;
            var names = new List<string> { "Ali", "Cem" };
            var lambda = TUser.Query().Where(u => u.Age >= min && (u.Name.StartsWith("A") || names.Contains(u.Name)) && !u.IsActive && u.BirthDate != null)
                .OrderBy(u => u.Name).ToSql();
            DbConfig.Configure("Host=none", DbProviderType.PostgreSQL);

            string search = null;
            var noFilter = TUser.Query().When(search, (q, s) => q.Where("Name", s)).ToSql();
            var filter = TUser.Query().When("Ali", (q, s) => q.Where("Name", s)).ToSql();
            var otherwise = TUser.Query().When(false, q => q.Where("Age", 1), q => q.Where("Age", 2)).Unless(true, q => q.Where("X", 1)).ToSql();
            var applied = TUser.Query().Apply(q => q.Where("IsActive", true)).ToSql();
            DbConfig.Configure("Data Source=none.db", DbProviderType.SQLite);

            return !sqlite.Contains("FOR") && !sqlite.Contains("LOCK")
                && mssql.StartsWith("SELECT * FROM [mc_users] WITH (ROWLOCK, UPDLOCK, HOLDLOCK) WHERE") && mssqlShared.Contains("WITH (ROWLOCK, HOLDLOCK)")
                && mysql.EndsWith("LIMIT 1 FOR UPDATE") && mysqlShared.EndsWith(" LOCK IN SHARE MODE")
                && pg.EndsWith(" FOR UPDATE") && pgShared.EndsWith(" FOR SHARE")
                && !noFilter.Contains("\"name\" =") && filter.Contains("\"name\" = @p0")
                && otherwise.Contains("\"age\" = @p0") && !otherwise.Contains("\"x\"") && applied.Contains("\"isactive\" = @p0")
                && whereHas.Contains("EXISTS (SELECT 1 FROM [mc_orders] mc_r1 WHERE mc_r1.[UserId] = [mc_users].[Id])")
                && withCount.StartsWith("SELECT *, (SELECT COUNT(*) FROM [mc_orders] mc_r1 WHERE mc_r1.[UserId] = [mc_users].[Id]) AS [OrdersCount] FROM [mc_users]")
                && lambda.Contains("(([Age] >= @p0 AND ([Name] LIKE @p1 ESCAPE '!' OR [Name] IN (@p2, @p3))) AND [IsActive] = @p4) AND [BirthDate] IS NOT NULL")
                && lambda.EndsWith("ORDER BY [Name] ASC");
        });

        FakeDialectTests();
    }

    // Generated write SQL per provider, recorded by FakeCommand (no server needed)
    static List<string> Capture(DbProviderType provider, Action action)
    {
        FakeCommand.Sql.Clear();
        using (MersoConnection.Use(() => new FakeCommand(provider)))
            action();
        return new List<string>(FakeCommand.Sql);
    }

    static TProduct[] Products() => new[]
    {
        new TProduct { Code = "P1", Name = "A", Price = 1m, IsActive = true },
        new TProduct { Code = "P2", Name = "B", Price = 2m, IsActive = true }
    };

    static void FakeDialectTests()
    {
        Check("Upsert SQL Server = MERGE", () =>
        {
            var sql = Capture(DbProviderType.SqlServer, () => BulkOperations.Upsert(Products(), new[] { "Code" })).Single();
            return sql.StartsWith("MERGE INTO [mc_products] WITH (HOLDLOCK) AS target USING (VALUES (@p0, @p1, @p2, @p3), (@p4, @p5, @p6, @p7)) AS source ([Code], [Name], [Price], [IsActive]) ON target.[Code] = source.[Code]")
                && sql.Contains("WHEN MATCHED THEN UPDATE SET target.[Name] = source.[Name], target.[Price] = source.[Price], target.[IsActive] = source.[IsActive]")
                && sql.EndsWith("WHEN NOT MATCHED THEN INSERT ([Code], [Name], [Price], [IsActive]) VALUES (source.[Code], source.[Name], source.[Price], source.[IsActive]);");
        });
        Check("Upsert MySQL / MariaDB = ON DUPLICATE KEY UPDATE", () =>
        {
            var sql = Capture(DbProviderType.MariaDB, () => BulkOperations.Upsert(Products(), new[] { "Code" }, new[] { "Price" })).Single();
            return sql == "INSERT INTO `mc_products` (`Code`, `Name`, `Price`, `IsActive`) VALUES (@p0, @p1, @p2, @p3), (@p4, @p5, @p6, @p7) ON DUPLICATE KEY UPDATE `Price` = VALUES(`Price`)";
        });
        Check("Upsert PostgreSQL = ON CONFLICT, insert-only = DO NOTHING", () =>
        {
            var sql = Capture(DbProviderType.PostgreSQL, () => BulkOperations.Upsert(Products(), new[] { "Code" })).Single();
            var ignore = Capture(DbProviderType.PostgreSQL, () => BulkOperations.Upsert(Products(), new[] { "Code" }, new string[0])).Single();
            return sql.EndsWith("ON CONFLICT (\"code\") DO UPDATE SET \"name\" = excluded.\"name\", \"price\" = excluded.\"price\", \"isactive\" = excluded.\"isactive\"")
                && ignore.EndsWith("ON CONFLICT (\"code\") DO NOTHING");
        });
        Check("Upsert rejects auto-increment key", () =>
        {
            try { Capture(DbProviderType.SQLite, () => BulkOperations.Upsert(Products(), new[] { "Id" })); return false; }
            catch (ArgumentException) { return true; }
        });
        Check("Dirty save writes only changed columns", () =>
        {
            var u = new TUser { Id = 5, Name = "x", Email = "e" };
            u.SyncOriginal();
            u.Age = 3;
            var sql = Capture(DbProviderType.SqlServer, () => u.Save()).Single();
            var again = Capture(DbProviderType.SqlServer, () => u.Save());
            return sql == "UPDATE [mc_users] SET [Age] = @c0, [UpdatedAt] = @c1 WHERE [Id] = @__k0" && again.Count == 0 && u.IsClean();
        });
        Check("RowVersion update SQL (PostgreSQL)", () =>
        {
            var v = new TVersioned { Id = 1, Title = "t", Version = 3 };
            v.SyncOriginal();
            v.Title = "u";
            var sql = Capture(DbProviderType.PostgreSQL, () => v.Save()).Single();
            return sql == "UPDATE \"mc_versioned\" SET \"title\" = @c0, \"version\" = \"version\" + 1 WHERE \"id\" = @__k0 AND \"version\" = @__ver" && v.Version == 4;
        });
        Check("Composite key update / delete SQL", () =>
        {
            var line = new TOrderLine { OrderId = 7, Sku = "X", Qty = 1 };
            line.SyncOriginal();
            line.Qty = 2;
            var update = Capture(DbProviderType.SqlServer, () => line.Save()).Single();
            var delete = Capture(DbProviderType.SqlServer, () => line.Delete()).Single();
            return update == "UPDATE [mc_order_lines] SET [Qty] = @c0 WHERE [OrderId] = @__k0 AND [Sku] = @__k1"
                && delete == "DELETE FROM [mc_order_lines] WHERE [OrderId] = @__k0 AND [Sku] = @__k1";
        });
        Check("Composite key relation SQL (HasMany two columns, BelongsTo owner key, one column unchanged)", () =>
        {
            var line = new TLine { OrderId = 7, Pos = 1 };
            var tax = new TLineTax { OrderId = 7, Pos = 1 };
            var many = Capture(DbProviderType.SqlServer, () => line.Taxes().GetRelated()).Single();
            var owner = Capture(DbProviderType.SqlServer, () => tax.LineOf().GetRelated()).Single();
            var single = Capture(DbProviderType.SqlServer, () => new HasMany<TUser, TOrder>(new TUser { Id = 3 }, "UserId").GetRelated()).Single();
            return many == "SELECT * FROM [mc_line_taxes] WHERE [OrderId] = @fk0 AND [Pos] = @fk1"
                && owner == "SELECT * FROM [mc_lines] WHERE [OrderId] = @pk0 AND [Pos] = @pk1"
                && single == "SELECT * FROM [mc_orders] WHERE [UserId] = @fk";
        });
        Check("Schema SQL: ChangeColumn (SQL Server / MySQL / PostgreSQL), AddForeign", () =>
        {
            FakeCommand.Sql.Clear();
            new Schema(new FakeCommand(DbProviderType.SqlServer)).ChangeColumn("mc_users", "Name", "NVARCHAR(200)", false, "N''");
            var ss = new List<string>(FakeCommand.Sql);
            FakeCommand.Sql.Clear();
            new Schema(new FakeCommand(DbProviderType.MySQL)).ChangeColumn("mc_users", "Name", "VARCHAR(200)", false);
            new Schema(new FakeCommand(DbProviderType.PostgreSQL)).ChangeColumn("mc_users", "Name", "VARCHAR(200)", true);
            new Schema(new FakeCommand(DbProviderType.SqlServer)).AddForeign("mc_orders", "UserId", "mc_users");
            var rest = new List<string>(FakeCommand.Sql);
            return ss.Count == 3 && ss[0].Contains("sys.default_constraints") && ss[0].Contains("QUOTENAME(@df)")
                && ss[1] == "ALTER TABLE [mc_users] ALTER COLUMN [Name] NVARCHAR(200) NOT NULL"
                && ss[2] == "ALTER TABLE [mc_users] ADD CONSTRAINT [df_mc_users_Name] DEFAULT N'' FOR [Name]"
                && rest[0] == "ALTER TABLE `mc_users` MODIFY COLUMN `Name` VARCHAR(200) NOT NULL"
                && rest[1] == "ALTER TABLE \"mc_users\" ALTER COLUMN \"name\" TYPE VARCHAR(200) USING \"name\"::VARCHAR(200), ALTER COLUMN \"name\" DROP NOT NULL, ALTER COLUMN \"name\" DROP DEFAULT"
                && rest[2] == "ALTER TABLE [mc_orders] ADD CONSTRAINT [fk_mc_orders_UserId] FOREIGN KEY ([UserId]) REFERENCES [mc_users]([Id]) ON DELETE CASCADE";
        });
        Check("New model insert quotes columns", () =>
        {
            var sql = Capture(DbProviderType.MySQL, () => new TReserved { Order = "o", Group = 1, Key = "k" }.Save()).Single();
            return sql == "INSERT INTO `mc_reserved` (`Order`, `Group`, `Key`) VALUES (@c0, @c1, @c2)";
        });
    }

    static void LibraryTests()
    {
        Check("Validator regex with comma + nullable + string min", () =>
        {
            var v = new Validator(new Dictionary<string, object> { { "code", "AB12" }, { "nick", "" }, { "user", "123" }, { "age", 17 } })
                .Validate(new Dictionary<string, string>
                {
                    { "code", @"required|regex:^[A-Z]{2,3}\d{1,2}$" },
                    { "nick", "nullable|min:3" },
                    { "user", "alpha_num|min:3" },
                    { "age", "integer|min:18" },
                });
            return !v.HasError("code") && !v.HasError("nick") && !v.HasError("user") && v.HasError("age");
        });
        Check("Validator IBAN with dashes, TC kimlik, decimal min, json, ip", () =>
        {
            var v = new Validator(new Dictionary<string, object>
            {
                { "iban", "TR33-0006-1005-1978-6457-8413-26" }, { "tc", "19090909018" }, { "price", "1,5" },
                { "j", "{\"a\":1}" }, { "bad", "{oops}" }, { "ip", "1" }
            }).Validate(new Dictionary<string, string>
            {
                { "iban", "iban" }, { "tc", "tc_kimlik" }, { "price", "numeric|min:0.5" }, { "j", "json" }, { "bad", "json" }, { "ip", "ip" }
            });
            return !v.HasError("iban") && !v.HasError("tc") && !v.HasError("price") && !v.HasError("j") && v.HasError("bad") && v.HasError("ip");
        });
        Check("MersoValidator uses runtime type", () =>
        {
            object m = new VModel { Name = null };
            return !MersoValidator.Validate(m).IsValid;
        });

        Check("JWT round trip, roles, issuer required, tamper", () =>
        {
            var jwt = new JwtHelper("0123456789abcdef0123456789abcdef", "merso", "api");
            var token = jwt.GenerateToken(new Dictionary<string, object> { { "sub", "42" }, { "score", 1.5 }, { "roles", new[] { "admin", "user" } } });
            var ok = jwt.ValidateToken(token);
            var noIssuer = new JwtHelper("0123456789abcdef0123456789abcdef").GenerateToken("1", "x");
            var rejected = jwt.ValidateToken(noIssuer);
            var parts = token.Split('.');
            var tampered = parts[0] + "." + parts[1] + "." + (parts[2][0] == 'A' ? "B" : "A") + parts[2].Substring(1);
            return ok.IsValid && ok.UserId == "42" && ok.Roles.SequenceEqual(new[] { "admin", "user" }) && ok.GetClaim("score") == "1.5"
                && !rejected.IsValid && !jwt.ValidateToken(tampered).IsValid && jwt.RefreshToken(token) != null;
        });

        Check("Crypto legacy key derivation unchanged for ASCII keys", () =>
        {
            foreach (var provider in new[] { Crypto.CryptoProvider.Aes, Crypto.CryptoProvider.TripleDES })
            {
                using (var c = new Crypto(provider))
                using (SymmetricAlgorithm alg = provider == Crypto.CryptoProvider.Aes ? (SymmetricAlgorithm)Aes.Create() : TripleDES.Create())
                {
                    for (int len = 1; len <= 40; len++)
                    {
                        var key = new string('k', len - 1) + "Z";
                        var oldKey = OldGetLegalKey(alg, key);
                        if (oldKey == null) continue; // old code threw → nothing to stay compatible with
                        if (!oldKey.SequenceEqual(c.GetLegalKey(key))) return false;
                    }
                }
            }
            return true;
        });
        Check("Crypto encrypt/decrypt + Turkish long key + bytes exact", () =>
        {
            using (var c = new Crypto())
            {
                var enc = c.Encrypt("merhaba dünya", "anahtar");
                var longTr = c.Encrypt("x", "çok-uzun-türkçe-anahtar-ğüşİöç-1234567890");
                var bytes = c.Encrypt(new byte[] { 1, 2, 3 }, "k");
                var back = c.Decrypt(bytes, "k");
                return c.Decrypt(enc, "anahtar") == "merhaba dünya" && c.Decrypt(longTr, "çok-uzun-türkçe-anahtar-ğüşİöç-1234567890") == "x"
                    && back.SequenceEqual(new byte[] { 1, 2, 3 });
            }
        });
        Check("EncryptSecure round trip, random IV, tamper detection", () =>
        {
            var a = Crypto.EncryptSecure("gizli", "parola");
            var b = Crypto.EncryptSecure("gizli", "parola");
            var raw = Convert.FromBase64String(a);
            raw[20] ^= 1;
            return a != b && Crypto.DecryptSecure(a, "parola") == "gizli" && Crypto.DecryptSecure(a, "yanlis") == null
                && Crypto.DecryptSecure(Convert.ToBase64String(raw), "parola") == null;
        });
        Check("Security.GenerateRandomCode", () =>
        {
            var code = Security.GenerateRandomCode(64);
            return code.Length == 64 && code.All(ch => "ABCDEFGHJKLMNPQRSTUVXWYZ123456789".IndexOf(ch) >= 0);
        });
        Check("TextHelper.Truncate small max / StripHtml keeps text between scripts", () =>
            TextHelper.Truncate("abcdef", 2) == "ab" && TextHelper.StripHtml("<script>a</script>KEEP<script>b</script>") == "KEEP");
        Check("MemoryCache.Increment is atomic", () =>
        {
            Cache.Forget("ctr");
            Parallel.For(0, 1000, _ => Cache.Increment("ctr"));
            return Cache.Get<long>("ctr") == 1000;
        });
        Check("QueryCache key is case sensitive for literals", () =>
            QueryCache.Instance.GenerateKey("SELECT * FROM t WHERE n = 'Ali'") != QueryCache.Instance.GenerateKey("SELECT * FROM t WHERE n = 'ali'"));
        Check("JsonList collection initializer + JsonDictionary on empty column", () =>
        {
            var list = new JsonList<string> { "a", "b" };
            var dict = new JsonDictionary();
            dict.Json = null;
            return list.Count == 2 && list.Json == "[\"a\",\"b\"]" && dict["missing"] == null;
        });

        Check("Validation attributes under tr-TR (Range 12.5, Phone digits, collection length)", () =>
        {
            var errors = MersoValidator.Validate(new AModel()).Errors;
            return errors.Count == 2 && errors.ContainsKey("Phone") && errors.ContainsKey("Tags");
        });

        Check("[Hidden] / MakeVisible / MersoJson, [Guarded] / [Fillable] / ForceFill / StrictMassAssignment", () =>
        {
            var acc = new TAccount().Fill(new Dictionary<string, object> { { "Name", "Ali" }, { "PasswordHash", "h" }, { "IsAdmin", true } });
            var dict = acc.ToDict();
            var json = System.Text.Json.JsonSerializer.Serialize(acc, MersoJson.Options);
            acc.MakeVisible("PasswordHash");
            var visible = acc.ToDict().ContainsKey("PasswordHash") && System.Text.Json.JsonSerializer.Serialize(acc, MersoJson.Options).Contains("PasswordHash");

            var filled = new TFillable().Fill(new Dictionary<string, object> { { "Name", "n" }, { "Role", "admin" } });
            var forced = new TFillable().ForceFill(new Dictionary<string, object> { { "Role", "admin" } });
            var threw = false;
            ModelBase.StrictMassAssignment = true;
            try { new TAccount().Fill(new Dictionary<string, object> { { "IsAdmin", true } }); }
            catch (MassAssignmentException) { threw = true; }
            finally { ModelBase.StrictMassAssignment = false; }

            return acc.Name == "Ali" && acc.PasswordHash == "h" && !acc.IsAdmin
                && !dict.ContainsKey("PasswordHash") && dict.ContainsKey("Name") && !json.Contains("PasswordHash") && json.Contains("Ali")
                && visible && filled.Name == "n" && filled.Role == null && forced.Role == "admin" && threw;
        });

        Check("Distributed cache store: entries shared by servers, tags, flush, typed values", () =>
        {
            var store = new TestCacheStore();
            Cache.UseStore(store, "t:");
            try
            {
                Cache.Set("a", 42, TimeSpan.FromMinutes(1), new[] { "nums" });
                Cache.Set("b", new List<string> { "x" }, TimeSpan.FromMinutes(1));
                var otherServer = new DistributedCache(store, "t:");
                var shared = otherServer.Get<int>("a") == 42 && otherServer.Get<List<string>>("b")[0] == "x";

                Cache.ForgetByTag("nums");
                var tagWorked = !otherServer.Has("a") && otherServer.Has("b");
                otherServer.Flush();
                var flushWorked = !Cache.Has("b");

                // ForgetByPrefix: the first call registers the prefix and clears everything once, later calls only the prefix
                Cache.Set("user:1", "u1", TimeSpan.FromMinutes(1));
                Cache.Set("order:1", "o1", TimeSpan.FromMinutes(1));
                Cache.ForgetByPrefix("user:");
                var firstClearsAll = !otherServer.Has("user:1") && !otherServer.Has("order:1");
                Cache.Set("user:1", "u1", TimeSpan.FromMinutes(1));
                Cache.Set("order:1", "o1", TimeSpan.FromMinutes(1));
                otherServer.ForgetByPrefix("user:");
                var onlyPrefix = !Cache.Has("user:1") && Cache.Get<string>("order:1") == "o1";
                Cache.Set("user:2", "u2", TimeSpan.FromMinutes(1));
                var newEntryKept = otherServer.Get<string>("user:2") == "u2";
                otherServer.Flush();
                Cache.Set("user:3", "u3", TimeSpan.FromMinutes(1));
                Cache.Set("order:3", "o3", TimeSpan.FromMinutes(1));
                Cache.ForgetByPrefix("user:");
                var registeredAfterFlush = !Cache.Has("user:3") && Cache.Has("order:3");

                var remembered = Cache.Remember("r", TimeSpan.FromMinutes(1), () => "v") == "v" && otherServer.Get<string>("r") == "v";
                return shared && tagWorked && flushWorked && firstClearsAll && onlyPrefix && newEntryKept && registeredAfterFlush
                    && remembered && Cache.IsDistributed;
            }
            finally
            {
                Cache.UseMemory();
            }
        });

        Check("DI: AddMersoCore sets the database and named connection, wires IDistributedCache and ILogger", () =>
        {
            var main = Path.Combine(Path.GetTempPath(), "mc_di_" + Guid.NewGuid().ToString("N") + ".db");
            var other = Path.Combine(Path.GetTempPath(), "mc_di2_" + Guid.NewGuid().ToString("N") + ".db");
            var lines = new List<string>();
            var distributed = new TestDistributedCache();
            var oldRetries = MersoRetry.MaxRetries;
            try
            {
                var services = new ServiceCollection();
                services.AddLogging(b => b.AddProvider(new ListLoggerProvider(lines)).SetMinimumLevel(LogLevel.Debug));
                services.AddSingleton<IDistributedCache>(distributed);
                services.AddMersoCore(o => o
                    .UseSqlite(main)
                    .AddConnection("di_other", "Data Source=" + other, DbProviderType.SQLite)
                    .UseDistributedCache("di:")
                    .UseRetry(1)
                    .LogSql());
                services.AddMersoDbContext<TestContext>();

                using (var provider = services.BuildServiceProvider())
                {
                    provider.UseMersoCore();
                    using (var db = provider.GetRequiredService<DbCommandBase>())
                        db.RunExecute("CREATE TABLE di_t (Id INTEGER)");
                    using (var db = DbConfig.CreateConnection("di_other"))
                        db.RunExecute("CREATE TABLE di_other_t (Id INTEGER)");
                    Cache.Set("k", 1);

                    var logged = lines.Any(l => l.StartsWith("mersolutionCore.Sql") && l.Contains("CREATE TABLE di_t"));
                    return logged && Cache.IsDistributed && distributed.Store.Count > 0 && MersoRetry.MaxRetries == 1
                        && DbConfig.ProviderType == DbProviderType.SQLite;
                }
            }
            finally
            {
                // Unhook the logger / cache again: options without LogSql and UseDistributedCache
                var reset = new ServiceCollection();
                reset.AddLogging();
                reset.AddMersoCore(o => o.UseRetry(oldRetries));
                using (var p = reset.BuildServiceProvider()) p.UseMersoCore();
                Cache.UseMemory();
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                try { File.Delete(main); File.Delete(other); } catch { }
            }
        });

        Check("2.1 OpenTelemetry: ActivitySource 'mersolutionCore', db.* tags, client kind, error status on failure", () =>
        {
            var path = Path.Combine(Path.GetTempPath(), "mc_otel_" + Guid.NewGuid().ToString("N") + ".db");
            var activities = new List<System.Diagnostics.Activity>();
            var listener = new System.Diagnostics.ActivityListener
            {
                ShouldListenTo = s => s.Name == MersoTelemetry.SourceName,
                Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) => System.Diagnostics.ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = a => { lock (activities) activities.Add(a); }
            };
            System.Diagnostics.ActivitySource.AddActivityListener(listener);
            try
            {
                DbConfig.ConfigureSQLite(path);
                ModelBase.Configure(DbConfig.CreateConnection);
                RawQuery.Execute("CREATE TABLE mc_otel (Id INTEGER)");
                RawQuery.Scalar<long>("SELECT COUNT(*) FROM mc_otel");
                try { RawQuery.Execute("DELETE FROM mc_missing_table"); } catch (Exception) { }
            }
            finally
            {
                listener.Dispose();
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                try { File.Delete(path); } catch { }
            }

            var select = activities.FirstOrDefault(a => (a.GetTagItem("db.query.text") as string ?? "").Contains("FROM mc_otel"));
            var failed = activities.FirstOrDefault(a => a.Status == System.Diagnostics.ActivityStatusCode.Error);
            return select != null && (string)select.GetTagItem("db.operation.name") == "SELECT" && (string)select.GetTagItem("db.system.name") == "sqlite"
                && select.Kind == System.Diagnostics.ActivityKind.Client && select.OperationName == "SELECT"
                && failed != null && failed.GetTagItem("error.type") != null && (string)failed.GetTagItem("db.operation.name") == "DELETE";
        });

        Check("2.1 read replicas: reads → replica, writes / transactions / OnPrimary / Fresh → primary, sticky scope after a write", () =>
        {
            var primary = Path.Combine(Path.GetTempPath(), "mc_rr_p_" + Guid.NewGuid().ToString("N") + ".db");
            var replica = Path.Combine(Path.GetTempPath(), "mc_rr_r_" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                DbConfig.ConfigureSQLite(primary);
                ModelBase.Configure(DbConfig.CreateConnection);
                RawQuery.Execute("CREATE TABLE mc_rr (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT)");
                RawQuery.Execute("INSERT INTO mc_rr (Name) VALUES ('on primary')");
                using (var r = new mersolutionCore.Command.SQLite.SQLiteCommand("Data Source=" + replica))
                {
                    r.RunExecute("CREATE TABLE mc_rr (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT)");
                    r.RunExecute("INSERT INTO mc_rr (Name) VALUES ('on replica')");
                }
                DbConfig.AddReadReplica("Data Source=" + replica);

                var read = TReplicaRow.All().Single().Name;                       // replica
                var created = TReplicaRow.Create(new TReplicaRow { Name = "new" }); // primary
                var primaryRows = RawQuery.Scalar<long>("SELECT COUNT(*) FROM mc_rr");
                var stillReplica = TReplicaRow.Count();
                var onPrimary = TReplicaRow.Query().OnPrimary().Count();
                var inTransaction = MersoTransaction.Run(() => TReplicaRow.Count());
                var fresh = created.Fresh()?.Name;

                int beforeWrite, afterWrite;
                using (ReadReplicas.BeginScope())
                {
                    beforeWrite = TReplicaRow.Count();
                    TReplicaRow.Create(new TReplicaRow { Name = "in scope" });
                    afterWrite = TReplicaRow.CountAsync().GetAwaiter().GetResult();
                }
                var afterScope = TReplicaRow.Count();

                return read == "on replica" && primaryRows == 2 && stillReplica == 1 && onPrimary == 2 && inTransaction == 2
                    && fresh == "new" && beforeWrite == 1 && afterWrite == 3 && afterScope == 1;
            }
            finally
            {
                ReadReplicas.Clear();
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                try { File.Delete(primary); File.Delete(replica); } catch { }
            }
        });

        ConnectionPoolTests();
    }

    public class AModel
    {
        [Range(0, 100)] public string Score { get; set; } = "12.5";   // tr-TR Convert.ToDouble → 125
        [Positive] public string Price { get; set; } = "0,5";
        [NonNegative] public decimal Stock { get; set; } = 0m;
        [Phone] public string Phone { get; set; } = "----------";       // 10 chars, no digits
        [Phone] public string Mobile { get; set; } = "+90 (555) 123 45 67";
        [MinLength(2)] public List<int> Tags { get; set; } = new List<int> { 1 };
        [Pattern(@"^\d{5}$")] public string Zip { get; set; } = "34000";
    }

    static void ConnectionPoolTests()
    {
        var dbA = Path.Combine(Path.GetTempPath(), "mc_pool_a_" + Guid.NewGuid().ToString("N") + ".db");
        var dbB = Path.Combine(Path.GetTempPath(), "mc_pool_b_" + Guid.NewGuid().ToString("N") + ".db");
        Func<System.Data.Common.DbConnection> fa = () => new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + dbA);
        Func<System.Data.Common.DbConnection> fb = () => new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + dbB);

        try
        {
            Check("ConnectionPool: separate pools per connection string", () =>
            {
                ConnectionPool.Clear();
                ConnectionPool.Configure(0, 10, 1);
                var a = ConnectionPool.GetConnection(fa);
                ConnectionPool.ReturnConnection(a);
                var b = ConnectionPool.GetConnection(fb);
                var ok = !ReferenceEquals(a, b) && b.ConnectionString.Contains(dbB);
                ConnectionPool.ReturnConnection(b);
                var again = ConnectionPool.GetConnection(fa);
                ok &= ReferenceEquals(a, again);
                ConnectionPool.ReturnConnection(again);
                return ok;
            });

            Check("ConnectionPool: max size, waiting caller gets a freed slot", () =>
            {
                ConnectionPool.Clear();
                ConnectionPool.Configure(0, 1, 0);
                var c1 = ConnectionPool.GetConnection(fa);
                var timedOut = false;
                try { ConnectionPool.GetConnection(fa); } catch (TimeoutException) { timedOut = true; }

                ConnectionPool.Configure(0, 1, 5);
                var waiter = Task.Run(() => ConnectionPool.GetConnection(fa));
                Thread.Sleep(100);
                c1.Close();
                ConnectionPool.ReturnConnection(c1);          // closed → destroyed, slot freed
                var c2 = waiter.Wait(3000) ? waiter.Result : null;
                var ok = timedOut && c2 != null && c2.State == System.Data.ConnectionState.Open;
                ConnectionPool.ReturnConnection(c2);
                return ok;
            });

            Check("ConnectionPool: double return / Clear keep counters", () =>
            {
                ConnectionPool.Clear();
                ConnectionPool.Configure(0, 10, 1);
                var before = ConnectionPool.GetStatus();
                var c = ConnectionPool.GetConnection(fa);
                ConnectionPool.ReturnConnection(c);
                ConnectionPool.ReturnConnection(c);
                var afterDouble = ConnectionPool.GetStatus();
                var rented = ConnectionPool.GetConnection(fa);
                ConnectionPool.Clear();
                ConnectionPool.ReturnConnection(rented);    // older generation → closed
                var end = ConnectionPool.GetStatus();
                return afterDouble.AvailableConnections == before.AvailableConnections + 1
                    && end.TotalConnections == 0 && end.AvailableConnections == 0 && rented.State == System.Data.ConnectionState.Closed;
            });
        }
        finally
        {
            ConnectionPool.Clear();
            ConnectionPool.Configure();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(dbA); File.Delete(dbB); } catch { }
        }
    }

    public class VModel { [Required] public string Name { get; set; } }

    // Copy of the 1.1.0 GetLegalKey (string based) to prove ASCII keys produce identical bytes
    static byte[] OldGetLegalKey(SymmetricAlgorithm alg, string key)
    {
        try
        {
            if (alg.LegalKeySizes.Length > 0)
            {
                int keySize = key.Length * 8;
                int minSize = alg.LegalKeySizes[0].MinSize;
                int maxSize = alg.LegalKeySizes[0].MaxSize;
                int skipSize = alg.LegalKeySizes[0].SkipSize;

                if (keySize > maxSize)
                    key = key.Substring(0, maxSize / 8);
                else if (keySize < maxSize)
                {
                    int validSize = (keySize <= minSize) ? minSize : (keySize - keySize % skipSize) + skipSize;
                    if (keySize < validSize)
                        key = key.PadRight((validSize / 8) - (Encoding.UTF8.GetByteCount(key) - key.Length), '*');
                }
            }
            return Encoding.UTF8.GetBytes(key);
        }
        catch { return null; }
    }
}
