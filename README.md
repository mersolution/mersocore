# mersolutionCore

[![Version](https://img.shields.io/badge/version-2.1.0-6c429c?style=for-the-badge)](https://mersocore.com)
[![Docs](https://img.shields.io/badge/docs-mersocore.com-6c429c?style=for-the-badge&logo=gitbook&logoColor=white)](https://mersocore.com/docs/)
[![.NET](https://img.shields.io/badge/.NET_Standard_2.0_|_.NET_8-5c2d91?style=for-the-badge&logo=dotnet&logoColor=white)](https://mersocore.com)
[![License](https://img.shields.io/badge/license-MIT-84cc16?style=for-the-badge&logo=opensourceinitiative&logoColor=white)](https://github.com/mersolution/mersocore)

> **Merso Framework** (`mersolutionCore`) **v2.1.0** is a cross-platform **.NET ORM Framework** for .NET Standard 2.0 (.NET Framework 4.6.1+, .NET Core 2.0+) and .NET 8+ (.NET 8, 9, 10 get the current SQL Server / PostgreSQL drivers).

Explore the full documentation at [mersocore.com](https://mersocore.com).

---

## v2.1.0

Current library release. Package version is `2.1.0` in `mersolutionCore.csproj`. Full API docs: [mersocore.com/docs](https://mersocore.com/docs/) and [DOCUMENTATION.md](DOCUMENTATION.md); release notes: [CHANGELOG.md](CHANGELOG.md).

### New in 2.1.0

* **Faster reads** — models are filled straight from the data reader with compiled setters (no DataTable in between).
* **Streaming** — `foreach (var u in User.Query().Cursor())` / `await foreach (... CursorAsync())`: one row in memory at a time; `ChunkAsync` / `ChunkByIdAsync`.
* **DateOnly / TimeOnly** columns (they were skipped silently).
* **Relation filters** — `WhereHas("Orders")`, `WhereHas<Order>("Orders", q => q.Where("Total", ">", 100))`, `WhereDoesntHave`, `Has("Orders", ">=", 3)`, `WhereRelation("Orders", "Status", "paid")`.
* **Relation aggregates** — `WithCount("Orders")`, `WithSum` / `WithAvg` / `WithMin` / `WithMax` / `WithExists`; `[Computed]` properties receive them; constrained eager loading `With<Order>("Orders", q => q.Where("Status", "paid"))`.
* **Paging without COUNT** — `SimplePaginate(page, size)`; keyset paging `CursorPaginate(size, cursor)` with `NextCursor` / `PreviousCursor`.
* **Value converters** — `[EnumAsString]`, `[JsonColumn]` (lists / classes as JSON), `[Encrypted]` (AES-256 + HMAC), your own `[Converter(typeof(...))]`.
* **Bulk copy** — `BulkOperations.BulkCopy(models)`: `SqlBulkCopy`, PostgreSQL `COPY`, `MySqlBulkCopy`; batched INSERTs where not available.
* **Lambda queries** — `Where(u => u.Age >= 18 && u.Name.StartsWith("A"))`, `OrderBy(u => u.Name)`, `Select(u => u.Id, u => u.Name)`, `Pluck(u => u.Email)`.
* **Query helpers** — `When` / `Unless` / `Apply` (reusable scopes), `LockForUpdate()` / `SharedLock()`.
* **Read replicas** — `DbConfig.AddReadReplica(cs)` / `o.AddReadReplica(cs)`: reads go to replicas, writes and transactions to the primary; `ReadReplicas.BeginScope()` reads your own writes; `OnPrimary()`.
* **OpenTelemetry** — every command is an `Activity` of the source `mersolutionCore` (`AddSource(MersoTelemetry.SourceName)`).
* **Async completed** — all relation methods, `FirstOrCreateAsync`, `UpdateOrCreateAsync`, `FirstOrNewAsync`, `UpsertAsync`, `TruncateAsync`, static `SumAsync` / `AvgAsync` / `MinAsync` / `MaxAsync`.
* **`mersocore` tool** — `dotnet tool install -g mersolutionCore.Cli`, then `mersocore scaffold --provider postgresql --connection "..."` writes model classes (keys, timestamps, soft delete, relations from foreign keys) from an existing database.
* **Repository** — release workflow (tag → NuGet + GitHub release), Dependabot, issue / PR templates, SECURITY.md, CONTRIBUTING.md, `tests/docker-compose.yml` for the five test databases.

### Upgrading from 2.0

* Models are read with `RunReader` / `RunReaderAsync` / `StreamReader`: a custom `DbCommandBase` subclass that overrides `RunDataTable` to intercept model reads must override these as well (nothing to do otherwise).
* `[JsonColumn]` now converts: a list / class / dictionary property is stored as JSON (a `string` property is unchanged).
* `Select()` with no arguments is ambiguous between the string and the lambda overload — pass columns or remove the call.
* `DateOnly` / `TimeOnly` properties are now columns (2.0 skipped them): such a property without a matching column needs `[Ignore]`.

### New in 2.0.0

* **Async everywhere** — `FindAsync`, `AllAsync`, `CreateAsync`, `SaveAsync`, `DeleteAsync`, `RefreshAsync`, `RestoreAsync`; QueryBuilder `GetAsync`, `FirstAsync`, `CountAsync`, `ExistsAsync`, `SumAsync`, `PaginateAsync`, `UpdateAsync`, `DeleteAsync` …; `RawQuery.QueryAsync` / `ExecuteAsync` / `ScalarAsync`; `BulkInsertAsync`, `UpsertAsync`; eager loading `With(...).GetAsync()`. All take a `CancellationToken`.
* **Several databases** — `DbConfig.AddConnection("reports", connectionString, DbProviderType.PostgreSQL)`, then `[Connection("reports")]` on a model, `Model.On("reports")` for one query (loaded models save back there) or `using (MersoConnection.Use("tenant_42")) { ... }` for a block (per tenant). `MersoTransaction.Run("reports", ...)`; transactions on different databases nest.
* **Quoted identifiers** — table and column names are quoted per provider (`[Order]`, `` `Order` ``, `"order"`), so reserved words such as `Order`, `Group`, `Key` work as column names. Expressions (`COUNT(*)`, `DATE(x)`) are left as written.
* **Change tracking** — `Save()` on a loaded model writes only the changed columns (and nothing when nothing changed); `IsDirty()`, `IsDirty("Name")`, `GetDirty()`, `GetOriginal("Name")`, `SyncOriginal()`.
* **Optimistic concurrency** — `[RowVersion] public int Version { get; set; }`: `Save()` / `Delete()` only touch the row when it still has the loaded version, otherwise `DbConcurrencyException`.
* **Upsert** — `BulkOperations.Upsert(models, uniqueBy: new[] { "Code" })` / `Model.Upsert(model, "Code")`: SQL Server `MERGE`, PostgreSQL / SQLite `ON CONFLICT`, MySQL / MariaDB `ON DUPLICATE KEY UPDATE`.
* **SQL log** — `MersoLog.QueryExecuted += e => logger.LogDebug("{Sql} {Ms} ms", e.Sql, e.Duration.TotalMilliseconds);` for every command (with error, without parameter values unless `MersoLog.IncludeParameterValues = true`).
* **Global scopes work** — `[GlobalScope("IsActive", true)]` is now applied to every query of the model (it was never applied); `Query().WithoutGlobalScopes()`; `GlobalScopeManager.WithoutGlobalScopes<T>()` only affects the current request / async flow.
* **Composite keys** — several `[PrimaryKey]` properties; `Find(orderId, sku)`, `WhereKey(a, b)`, Save / Delete / FindMany / Destroy / upsert by the whole key; HasOne / HasMany / BelongsTo (+ eager loading) and `[Touches]` on several columns.
* **Hidden fields & mass assignment** — `[Hidden]` (out of `ToDict` / `ToJson`, `MersoJson` for ASP.NET Core), `MakeVisible`; `[Fillable]` / `[Guarded]`, `ForceFill`, `ModelBase.StrictMassAssignment`.
* **Query builder** — groups `Where(q => q.Where(...).OrWhere(...))`, `WhereNot`; `OrWhereIn` / `OrWhereNull` / `OrWhereBetween` …; `WhereColumn`; `WhereExists` / `WhereIn(column, subquery)`; `Union` / `UnionAll`; `SelectRaw`, `OrderByRaw`, `HavingRaw`, `WhereRaw` with `?` bindings; `model.GetAttribute("Total")` for computed columns.
* **Relations** — `HasManyThrough`, `HasOneThrough`, polymorphic `MorphMany` / `MorphOne` / `MorphTo` (+ eager `[MorphMany]` / `[MorphOne]` / `[MorphTo]`), pivot columns (`WithPivot`, `WherePivot`, `UpdateExistingPivot`, `Sync` with data), `[Touches]` / `Touch()`.
* **Migrations** — `ChangeColumn`, `AddForeign`, `DropForeign` (finds unnamed keys too; SQLite rebuilds the table safely), batches: `Rollback()` undoes the last `Migrate()`.
* **Retry** — deadlocks, serialization failures, refused / dropped connections are retried automatically (`MersoRetry`); `MersoTransaction.Run(action, attempts: 3)` re-runs a whole transaction.
* **Distributed cache** — `Cache.UseStore(new DistributedCacheStore(redis))` or `AddMersoCore(o => o.UseDistributedCache())`: every server shares entries, tags, prefixes and flushes.
* **Dependency injection** — `builder.Services.AddMersoCore(o => o.UsePostgreSql(cs).AddConnection(...).UseDistributedCache().LogSql())`, `AddMersoDbContext<AppDbContext>()`; SQL log to `ILogger`.
* **Package** — targets `netstandard2.0` and `net8.0`; nullable annotations; XML docs (IntelliSense), Source Link and symbols in the package; icon, README, [CHANGELOG](CHANGELOG.md), LICENSE; tests in `tests/` (`dotnet test tests/mersolutionCore.Tests`, set `MERSO_TEST_SQLSERVER` / `MERSO_TEST_MYSQL` / `MERSO_TEST_MARIADB` / `MERSO_TEST_POSTGRES` to include those databases); GitHub Actions CI on all five databases.

### Upgrading from 1.x

* `Save()` of a loaded model updates only changed columns — code that changed a column in the database behind the model's back no longer gets it overwritten.
* Generated SQL quotes names. On PostgreSQL the quoted name is lower case, exactly what an unquoted `CREATE TABLE` produced; tables created with quoted mixed-case names need `ModelBase.QuoteIdentifiers = false`.
* `[GlobalScope]` filters are now applied (also to `All()`, `Find()`, `Count()`).
* `Find()` no longer returns soft-deleted rows (`FindWithTrashed()`); `QueryBuilder.Where()` accepts only `=, !=, <>, <, >, <=, >=, LIKE, NOT LIKE, ILIKE` (use `WhereRaw` for anything else); JWT tokens without the configured issuer / audience are rejected; custom subclasses of `DbCommandBase` implement `NewConnection()` instead of `CreateConnection()`.
* `RawQuery` methods have an optional `connectionName` parameter (recompile callers).
* `Migrator.Rollback()` undoes the last batch (all migrations of the last `Migrate()`); `Rollback(1)` undoes one migration as before.
* Transient errors are retried by default (`MersoRetry.MaxRetries = 0` turns it off).
* `Fill()` / `Create(dictionary)` skip `[Guarded]` properties, and everything that is not `[Fillable]` once a model has `[Fillable]` properties (models without these attributes are unchanged).
* Nullable annotations: `Find` / `First` / `FindAsync` / `Fresh` return `T?`, dictionary values are `object?` — nullable-enabled projects may see new warnings.

### Fixed in 2.0.0

* **PostgreSQL on Turkish Windows / tr-TR** — properties whose column name contains an upper-case `I` (`Id`, `IsActive`, `UserId` …) were never filled, because PostgreSQL returns lower-case names and the DataTable name lookup uses the current culture (`i` ≠ `I` in Turkish); `ChunkById` looped forever. Column matching is now culture-independent.
* **Transactions really are transactions** — `MersoTransaction.Run()` now shares one connection + transaction with every model, `QueryBuilder`, `RawQuery` and bulk call inside it (rollback used to undo nothing). Nested `Run` joins the outer one; new `RunAsync()`. `DbCommandBase.BeginTransaction()` keeps its connection for the following calls.
* **Parameters everywhere** — queued parameters now reach scalar (`RunToInt32Scaler` …), reader, async and stored-procedure calls on every provider; typed / output parameters (`SqlDbType`, `MySqlDbType`, `NpgsqlDbType`, `SqliteType`) are no longer lost.
* **Correct new ids** — `Save()` reads the id of the inserted row on the same connection (`SCOPE_IDENTITY` / `RETURNING` / `LAST_INSERT_ID` / `last_insert_rowid`) instead of `MAX(id)`; `long` and `Guid` keys work (Guid keys are generated, application keys insert-or-update). `MySqlCommand.GetLastInsertId()` / `SQLiteCommand.GetLastInsertRowId()` return the real value.
* **QueryBuilder on MySQL, MariaDB, PostgreSQL, SQLite** — `First`, `Take`, `Skip`, `Paginate`, `Chunk`, `InRandomOrder`, `WhereDate` generate provider SQL (they always produced SQL Server `TOP` / `OFFSET … FETCH`).
* **QueryBuilder correctness** — `OrWhere` no longer returns soft-deleted rows; `Count()` / aggregates don't change the builder and ignore `ORDER BY` (Paginate + OrderBy works); grouped / distinct counts; `Update` / `Delete` / `Increment` return affected rows; `Increment` works; `Having` is parameterized; operators and order direction are validated; empty `WhereIn` is valid SQL; `Where(col, null)` → `IS NULL`.
* **Mapping** — enum, Guid, DateTimeOffset, TimeSpan and SQLite text values load correctly (they were silently left empty); numbers are parsed culture-independently; SQLite `DECIMAL` values are no longer truncated (12.5 → 12).
* **Observers fire** — `ObserverManager.Register(...)` observers now run on save / delete / restore and can cancel. `Save()`, `Delete()`, `Restore()`, `ForceDelete()` return `bool`.
* **Soft delete** — `Find()` excludes soft-deleted rows like `All()` / `FindMany()` (new `FindWithTrashed()`); `HasOne` / `BelongsTo` and eager loading filter them too.
* **Eager loading** — keys match across int/long (SQLite never matched); foreign key / related type inferred when omitted; nested `With("Orders.Items")`.
* **Query cache** — `Remember()` keys include parameter values (`Where("Id", 1)` and `Where("Id", 2)` shared one entry); `QueryCache` keys keep letter case.
* **Migrations** — `Index()` / `Unique()` indexes are created; SQL Server text columns are `NVARCHAR`; `Double` is valid on SQL Server; `HasTable` / `HasColumn` check the current schema / database; auto migrations map `long`, enums, `double`, `byte[]`, Guid / string primary keys. `Migrator` runs each migration and its `__migrations` row in one transaction (`WithinTransaction => false` to opt out), on one connection, in ordinal version order; duplicate versions are reported.
* **Connection pool** — one pool per connection string (two databases no longer swap connections); the max size holds under load, waiting callers get freed slots, double returns and `Clear()` keep the counters right.
* **Security** — JWT: constant-time signature check, `alg` check, configured issuer / audience are required, standard JSON (`Roles`, `GetClaimArray`). `Crypto`: non-ASCII keys, exact `Decrypt(byte[])`, new `EncryptSecure` / `DecryptSecure` (random IV, PBKDF2, HMAC). `Security.GenerateRandomCode` without modulo bias.
* **Validation** — regex rules with `,` `:` `|`, `nullable`, number vs. length for `min` / `max` / `between`, decimal parameters, IBAN with dashes, TC Kimlik check digit, real JSON check. Attributes: `[Range]` / `[Positive]` / `[NonNegative]` read "12.5" as 12.5 under any culture (tr-TR gave 125), `[Phone]` counts 10–15 digits, `[MinLength]` / `[MaxLength]` also count collections, `[Pattern]` has a regex timeout.
* **Thread safety** — metadata, observer, scope and cache registries are safe for concurrent requests.
* **Packages** — Microsoft.Data.Sqlite 10.0.12 (fixes the high-severity SQLitePCLRaw advisory), MySqlConnector 2.6.2; .NET Standard 2.0: Microsoft.Data.SqlClient 5.2.3, Npgsql 8.0.9 (newest versions that still ship a real .NET Standard 2.0 build); .NET 8+: Microsoft.Data.SqlClient 7.1.1, Npgsql 10.0.3.

---

## Why mersolutionCore?

Modern .NET applications need a reliable, fast, and easy-to-use database layer. mersolutionCore gives you the balance between productivity and control:

* **.NET ORM Framework:** `Model<T>`, `DbContext`, `MerSet<T>`, relations, observers, and code-first tables.
* **Fluent Query Builder:** Chainable WHERE, JOIN, ORDER, GROUP, LIMIT, aggregates and pagination — no raw SQL required.
* **Cross-Platform:** A `.NET Standard 2.0` build for .NET Framework / .NET Core and a `.NET 8` build for .NET 8, 9, 10 in one package.
* **Multi-Database:** SQL Server, MySQL, MariaDB, PostgreSQL, SQLite via `IDbCommand`.
* **Minimal Setup:** Configure the connection, call `EnsureCreated()` — tables are created automatically.

---

## Core Features

* **ORM** — `Model<T>` with CRUD, relationships, soft deletes, timestamps and lifecycle events
* **Fluent Query Builder** — chainable WHERE, `OrWhere`, JOIN, ORDER, GROUP, LIMIT, aggregate and pagination; lambda conditions, relation filters / counts, keyset paging, streaming
* **Multi-Database** — SQL Server, MySQL, MariaDB, PostgreSQL, SQLite via `IDbCommand`; read replicas
* **DbContext & MerSet\<T\>** — typed entity sets, `EnsureCreated`, `EnsureDeleted`, `EnsureFresh`
* **Migrations** — code-first schema with `Schema`, `Migrator`, `MigrationRunner`
* **Caching** — `MersoCache` and `QueryCache`, in memory or on a shared store (Redis / `IDistributedCache`)
* **Dependency Injection** — `services.AddMersoCore(...)`, `AddMersoDbContext<T>()`, SQL log to `ILogger`
* **Resilience** — automatic retry of transient errors, optimistic concurrency, change tracking
* **Validation** — `MersoValidator` and attributes (`[Required]`, `[Email]`, …)
* **Transactions** — `MersoTransaction.Run()` / `RunAsync()` / `TryRun()` with automatic rollback
* **Bulk Operations** — `BulkInsert`, `BulkCopy` (provider bulk APIs), `BulkUpdate`, `BulkDelete`, `BulkForceDelete`, `Upsert`
* **Raw Queries** — `RawQuery.Query<T>()`, `Scalar<T>()`, `Execute()`, `QueryTable()`
* **Value Converters** — `[EnumAsString]`, `[JsonColumn]`, `[Encrypted]`, custom `IValueConverter`; `JsonValue<T>`, `JsonDictionary`, `JsonList<T>`
* **Observability** — SQL log (`MersoLog`, `ILogger`) and OpenTelemetry traces
* **Scaffolding** — the `mersocore` .NET tool writes models from an existing database
* **Observers & Events** — `MersoObserver<T>` and `IMersoEvents`
* **Global Scopes** — `[GlobalScope]`
* **Connection Pool** — ADO.NET pooling; optional `ConnectionPool` helper
* **Cryptography** — `Crypto`, `Security`
* **Utilities** — `TextHelper`, `StringExtensions`, `HttpClientHelper`, `JwtHelper`

---

## Quick Links

| Resource | Link |
| :--- | :--- |
| **Official Website** | [mersocore.com](https://mersocore.com) |
| **Full Documentation** | [mersocore.com/docs](https://mersocore.com/docs/) |
| **Getting Started** | [Installation Guide](https://mersocore.com/docs/?doc=getting-started) |
| **GitHub** | [mersolution/mersocore](https://github.com/mersolution/mersocore) |

---

## Getting Started

### Installation

```bash
dotnet add package mersolutionCore
```

Or add a project reference:

```xml
<ProjectReference Include="..\mersolutionCore\mersolutionCore.csproj" />
```

Have an existing database? Generate the model classes with the `mersocore` tool:

```bash
dotnet tool install -g mersolutionCore.Cli
mersocore scaffold --provider postgresql --connection "Host=localhost;Database=shop;Username=app;Password=..." --namespace Shop.Models
```

---

## Quick Start

### 1. Configure the Database

```csharp
using mersolutionCore.Config;

// SQL Server
DbConfig.ConfigureSqlServer(@".\SQLEXPRESS", "MyDatabase");

// MySQL / MariaDB
DbConfig.ConfigureMySQL("localhost", "mydb", "root", "password");

// PostgreSQL
DbConfig.ConfigurePostgreSQL("localhost", "mydb", "postgres", "secret");

// SQLite
DbConfig.ConfigureSQLite("./app.db");
```

### 2. Define a Model

```csharp
using mersolutionCore.ORM;
using mersolutionCore.ORM.Entity;

[Table("Users")]
public class User : Model<User>
{
    [PrimaryKey(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("Name", Length = 100)]
    public string Name { get; set; } = "";

    [Column("Email", Length = 255)]
    public string Email { get; set; } = "";

    [Column("IsActive")]
    public bool IsActive { get; set; } = true;

    [CreatedAt]  public DateTime? CreatedAt { get; set; }
    [UpdatedAt]  public DateTime? UpdatedAt { get; set; }
    [SoftDelete] public DateTime? DeletedAt { get; set; }
}
```

### 3. Create a DbContext

```csharp
using mersolutionCore.Config;
using mersolutionCore.ORM;

public class AppDbContext : DbContext
{
    static AppDbContext() => DbConfig.ConfigureSqlServer(@".\SQLEXPRESS", "MyDatabase");

    public MerSet<User>    Users    { get; set; } = null!;
    public MerSet<Product> Products { get; set; } = null!;

    public AppDbContext() : base(DbConfig.CreateConnection) { }
}

var db = new AppDbContext();
db.EnsureCreated();
```

### 4. CRUD

```csharp
var user = new User { Name = "Alice", Email = "alice@example.com" };
user.Save();

var found  = User.Find(user.Id);
var orFail = User.FindOrFail(99);

found.Name = "Bob";
found.Save();

found.Delete();
found.Restore();
found.ForceDelete();
```

### 5. Fluent Query Builder

```csharp
var results = User
    .Where("IsActive", true)
    .Where("CreatedAt", ">", DateTime.Today.AddDays(-30))
    .OrderByDesc("CreatedAt")
    .Take(20)
    .Select("Id", "Name", "Email")
    .Get();

var page = User.Query().Where("IsActive", true).Paginate(pageNumber: 1, pageSize: 10);
// page.Items  |  page.TotalCount  |  page.TotalPages  |  page.HasNextPage

int  count = User.Count();
bool any   = User.Exists();
var  names = User.Pluck("Name");

string sql = User.Query().Where("IsActive", true).Take(5).ToSql();
```

Grouped OR conditions are a parenthesised group:

```csharp
var staff = User.Query()
    .Where("IsActive", true)
    .Where(q => q.Where("Role", "admin").OrWhere("Role", "moderator"))
    .Get();
```

### 6. New in 2.1 at a glance

```csharp
// Type-safe conditions, relation filters and counts
// (User has [HasMany(typeof(Order), "UserId")] public List<Order> Orders { get; set; })
var buyers = User.Query()
    .Where(u => u.IsActive && u.Email.EndsWith("@example.com"))
    .WhereHas<Order>("Orders", q => q.Where("Total", ">", 100))
    .WithCount("Orders")                                   // → GetAttribute<int>("OrdersCount") or a [Computed] property
    .OrderBy(u => u.Name)
    .Get();

// Keyset paging and streaming for large tables
var first = User.Query().OrderByDesc("CreatedAt").CursorPaginate(20);
var next  = User.Query().OrderByDesc("CreatedAt").CursorPaginate(20, first.NextCursor);

await foreach (var u in User.Query().CursorAsync())
    Console.WriteLine(u.Name);

// Provider bulk API (SqlBulkCopy, PostgreSQL COPY, MySqlBulkCopy)
BulkOperations.BulkCopy(importedUsers);
```

---

## Supported Databases

<table style="width:100%; border-collapse: collapse;">
<thead>
<tr style="background-color: #5c2d91; color: white;">
<th style="border: 1px solid #ddd; padding: 10px; text-align: left;">Database</th>
<th style="border: 1px solid #ddd; padding: 10px; text-align: left;">NuGet Package</th>
<th style="border: 1px solid #ddd; padding: 10px; text-align: left;">Default Port</th>
<th style="border: 1px solid #ddd; padding: 10px; text-align: center;">Cross-Platform</th>
</tr>
</thead>
<tbody>
<tr><td style="border:1px solid #ddd;padding:8px;"><strong>SQL Server</strong></td><td style="border:1px solid #ddd;padding:8px;"><code>Microsoft.Data.SqlClient</code></td><td style="border:1px solid #ddd;padding:8px;">1433</td><td style="border:1px solid #ddd;padding:8px;text-align:center;">✅</td></tr>
<tr style="background-color:#f9f9f9;"><td style="border:1px solid #ddd;padding:8px;"><strong>MySQL</strong></td><td style="border:1px solid #ddd;padding:8px;"><code>MySqlConnector</code></td><td style="border:1px solid #ddd;padding:8px;">3306</td><td style="border:1px solid #ddd;padding:8px;text-align:center;">✅</td></tr>
<tr><td style="border:1px solid #ddd;padding:8px;"><strong>MariaDB</strong></td><td style="border:1px solid #ddd;padding:8px;"><code>MySqlConnector</code></td><td style="border:1px solid #ddd;padding:8px;">3306</td><td style="border:1px solid #ddd;padding:8px;text-align:center;">✅</td></tr>
<tr style="background-color:#f9f9f9;"><td style="border:1px solid #ddd;padding:8px;"><strong>PostgreSQL</strong></td><td style="border:1px solid #ddd;padding:8px;"><code>Npgsql</code></td><td style="border:1px solid #ddd;padding:8px;">5432</td><td style="border:1px solid #ddd;padding:8px;text-align:center;">✅</td></tr>
<tr><td style="border:1px solid #ddd;padding:8px;"><strong>SQLite</strong></td><td style="border:1px solid #ddd;padding:8px;"><code>Microsoft.Data.Sqlite</code></td><td style="border:1px solid #ddd;padding:8px;">—</td><td style="border:1px solid #ddd;padding:8px;text-align:center;">✅</td></tr>
</tbody>
</table>

---

## Project Structure

```
mersolutionCore/
├── Command/        # IDbCommand, DbFactory — SQL Server / MySQL / MariaDB / PostgreSQL / SQLite
├── Config/         # DbConfig, MersoLog, MersoRetry, MersoTelemetry, AddMersoCore (DI)
├── ORM/            # ModelBase, QueryBuilder, DbContext, MerSet<T>
│                   #   Migration, Relationships, RelationQuery, BulkOperations, Transactions
│                   #   RawQuery, Observers, Events, GlobalScope, JsonColumns, ValueConverters
│                   #   ReadReplicas, ConnectionPool, Validation
├── Cache/          # MemoryCache, QueryCache, DistributedCache
├── Http/           # HttpClientHelper, JwtHelper
├── Library/        # Crypto, Security, TextHelper, StringExtensions
├── tools/          # mersocore-cli: the `mersocore scaffold` .NET tool (package mersolutionCore.Cli)
└── tests/          # mersolutionCore.Tests (xUnit): dotnet test tests/mersolutionCore.Tests
```

---

## Key Features

<table style="width:100%; border-collapse: collapse;">
<thead>
<tr style="background-color: #5c2d91; color: white;">
<th style="border: 1px solid #ddd; padding: 10px; text-align: left;">Feature</th>
<th style="border: 1px solid #ddd; padding: 10px; text-align: left;">Description</th>
</tr>
</thead>
<tbody>
<tr><td style="border:1px solid #ddd;padding:8px;"><strong>Model-First Migrations</strong></td><td style="border:1px solid #ddd;padding:8px;">Auto-create tables from model attribute definitions</td></tr>
<tr style="background-color:#f9f9f9;"><td style="border:1px solid #ddd;padding:8px;"><strong>Fluent Query Builder</strong></td><td style="border:1px solid #ddd;padding:8px;">Chainable query API — groups <code>Where(q =&gt; ...)</code>, lambdas <code>Where(u =&gt; u.Age &gt;= 18)</code>, WhereHas / WithCount, subqueries, UNION, raw pieces with <code>?</code> bindings, cursor paging, streaming</td></tr>
<tr><td style="border:1px solid #ddd;padding:8px;"><strong>Relations</strong></td><td style="border:1px solid #ddd;padding:8px;">HasOne, HasMany, BelongsTo, BelongsToMany (pivot columns), HasManyThrough, polymorphic MorphMany / MorphTo, eager loading with conditions, WhereHas / Has / WithCount / WithSum</td></tr>
<tr style="background-color:#f9f9f9;"><td style="border:1px solid #ddd;padding:8px;"><strong>Observers &amp; Events</strong></td><td style="border:1px solid #ddd;padding:8px;">Creating, Created, Updating, Deleting, …</td></tr>
<tr><td style="border:1px solid #ddd;padding:8px;"><strong>Bulk Operations</strong></td><td style="border:1px solid #ddd;padding:8px;">BulkInsert, BulkCopy (SqlBulkCopy / COPY / MySqlBulkCopy), BulkUpdate, BulkDelete, BulkForceDelete, Upsert</td></tr>
<tr style="background-color:#f9f9f9;"><td style="border:1px solid #ddd;padding:8px;"><strong>Value Converters</strong></td><td style="border:1px solid #ddd;padding:8px;">[EnumAsString], [JsonColumn], [Encrypted], custom IValueConverter; JsonValue&lt;T&gt;, JsonDictionary, JsonList&lt;T&gt;</td></tr>
<tr><td style="border:1px solid #ddd;padding:8px;"><strong>Validation Attributes</strong></td><td style="border:1px solid #ddd;padding:8px;">[Required], [Email], [Range], [MinLength], [Url], [Phone], …</td></tr>
<tr style="background-color:#f9f9f9;"><td style="border:1px solid #ddd;padding:8px;"><strong>Connection Pool</strong></td><td style="border:1px solid #ddd;padding:8px;">ADO.NET pooling; optional <code>ConnectionPool</code> helper</td></tr>
<tr><td style="border:1px solid #ddd;padding:8px;"><strong>Query Cache</strong></td><td style="border:1px solid #ddd;padding:8px;">MersoCache.Remember() / query cache</td></tr>
<tr style="background-color:#f9f9f9;"><td style="border:1px solid #ddd;padding:8px;"><strong>Soft Deletes</strong></td><td style="border:1px solid #ddd;padding:8px;">[SoftDelete] — Delete(), Restore(), WithTrashed(), OnlyTrashed()</td></tr>
<tr><td style="border:1px solid #ddd;padding:8px;"><strong>Transactions</strong></td><td style="border:1px solid #ddd;padding:8px;">MersoTransaction.Run() / TryRun() with automatic rollback</td></tr>
<tr style="background-color:#f9f9f9;"><td style="border:1px solid #ddd;padding:8px;"><strong>Global Scopes</strong></td><td style="border:1px solid #ddd;padding:8px;">[GlobalScope] automatic WHERE filters</td></tr>
<tr><td style="border:1px solid #ddd;padding:8px;"><strong>Cryptography</strong></td><td style="border:1px solid #ddd;padding:8px;">AES/TripleDES, SHA-256/MD5/HMAC</td></tr>
</tbody>
</table>

---

## Compatibility

<table style="width:100%; border-collapse: collapse;">
<thead>
<tr style="background-color: #5c2d91; color: white;">
<th style="border: 1px solid #ddd; padding: 10px; text-align: left;">Platform</th>
<th style="border: 1px solid #ddd; padding: 10px; text-align: left;">Minimum Version</th>
</tr>
</thead>
<tbody>
<tr><td style="border:1px solid #ddd;padding:8px;"><strong>.NET Standard</strong></td><td style="border:1px solid #ddd;padding:8px;">2.0</td></tr>
<tr style="background-color:#f9f9f9;"><td style="border:1px solid #ddd;padding:8px;"><strong>.NET Framework</strong></td><td style="border:1px solid #ddd;padding:8px;">4.6.1+</td></tr>
<tr><td style="border:1px solid #ddd;padding:8px;"><strong>.NET Core</strong></td><td style="border:1px solid #ddd;padding:8px;">2.0+</td></tr>
<tr style="background-color:#f9f9f9;"><td style="border:1px solid #ddd;padding:8px;"><strong>.NET</strong></td><td style="border:1px solid #ddd;padding:8px;">5, 6, 7 (.NET Standard build); 8, 9, 10+ (.NET 8 build)</td></tr>
</tbody>
</table>

---

## Next Steps

<table style="width:100%; border-collapse: collapse;">
<thead>
<tr style="background-color: #5c2d91; color: white;">
<th style="border: 1px solid #ddd; padding: 10px; text-align: left;">Documentation</th>
<th style="border: 1px solid #ddd; padding: 10px; text-align: left;">Description</th>
</tr>
</thead>
<tbody>
<tr><td style="border:1px solid #ddd;padding:8px;"><a href="https://mersocore.com/docs/?doc=whats-new"><strong>What's New in 2.1</strong></a></td><td style="border:1px solid #ddd;padding:8px;">2.1 and 2.0 features, upgrading</td></tr>
<tr style="background-color:#f9f9f9;"><td style="border:1px solid #ddd;padding:8px;"><a href="https://mersocore.com/docs/?doc=database-config"><strong>Database Config</strong></a></td><td style="border:1px solid #ddd;padding:8px;">Connection string and provider</td></tr>
<tr><td style="border:1px solid #ddd;padding:8px;"><a href="https://mersocore.com/docs/?doc=orm-model"><strong>ORM Model</strong></a></td><td style="border:1px solid #ddd;padding:8px;">Models, attributes, relationships</td></tr>
<tr style="background-color:#f9f9f9;"><td style="border:1px solid #ddd;padding:8px;"><a href="https://mersocore.com/docs/?doc=query-builder"><strong>Query Builder</strong></a></td><td style="border:1px solid #ddd;padding:8px;">Chainable queries</td></tr>
<tr><td style="border:1px solid #ddd;padding:8px;"><a href="https://mersocore.com/docs/?doc=relations"><strong>Relations</strong></a></td><td style="border:1px solid #ddd;padding:8px;">HasOne, HasMany, BelongsTo, BelongsToMany</td></tr>
<tr style="background-color:#f9f9f9;"><td style="border:1px solid #ddd;padding:8px;"><a href="https://mersocore.com/docs/?doc=qb-relation-queries"><strong>Relation Filters &amp; Counts</strong></a></td><td style="border:1px solid #ddd;padding:8px;">WhereHas, Has, WithCount, WithSum</td></tr>
<tr><td style="border:1px solid #ddd;padding:8px;"><a href="https://mersocore.com/docs/?doc=qb-lambda"><strong>Lambda Queries</strong></a></td><td style="border:1px solid #ddd;padding:8px;">Where(u =&gt; ...), OrderBy, Select, Pluck</td></tr>
<tr style="background-color:#f9f9f9;"><td style="border:1px solid #ddd;padding:8px;"><a href="https://mersocore.com/docs/?doc=dbcontext"><strong>DbContext</strong></a></td><td style="border:1px solid #ddd;padding:8px;">DbContext and MerSet&lt;T&gt;</td></tr>
<tr><td style="border:1px solid #ddd;padding:8px;"><a href="https://mersocore.com/docs/?doc=schema-migrations"><strong>Migrations</strong></a></td><td style="border:1px solid #ddd;padding:8px;">Code-first schema</td></tr>
<tr style="background-color:#f9f9f9;"><td style="border:1px solid #ddd;padding:8px;"><a href="https://mersocore.com/docs/?doc=scaffold"><strong>Scaffolding</strong></a></td><td style="border:1px solid #ddd;padding:8px;">Models from an existing database</td></tr>
<tr><td style="border:1px solid #ddd;padding:8px;"><a href="https://mersocore.com/docs/?doc=example-crud"><strong>CRUD Examples</strong></a></td><td style="border:1px solid #ddd;padding:8px;">End-to-end CRUD</td></tr>
</tbody>
</table>

---

*mersolutionCore v2.1.0 — a .NET ORM Framework by [Mersolution Technology](https://mersolution.com)*
