# Changelog

All notable changes to **mersolutionCore** (Merso Framework). Versions follow [Semantic Versioning](https://semver.org/).

## [2.1.0] - 2026-10-06

### Added

- **DateOnly / TimeOnly** properties are columns (`DATE` / `TIME`); the .NET Standard 2.0 build recognises them by name for .NET 6 / 7 apps.
- **Reader mapping**: models are filled straight from the data reader with compiled property setters (no DataTable copy); new `DbCommandBase.RunReader` / `RunReaderAsync` / `StreamReader` / `StreamReaderAsync`.
- **Streaming**: `QueryBuilder.Cursor()` / `CursorAsync()` and `Model.Cursor()` / `CursorAsync()` read one row at a time; `ChunkAsync`, `ChunkByIdAsync`.
- **Async completed**: every relation method (`GetRelatedAsync`, `CountAsync`, `CreateAsync`, `AssociateAsync`, `DissociateAsync`, `AttachAsync`, `DetachAsync`, `SyncAsync`, `ToggleAsync`, `ContainsAsync`, `UpdateExistingPivotAsync` ...), `FirstOrCreateAsync`, `FirstOrNewAsync`, `UpdateOrCreateAsync`, `Model.UpsertAsync`, `TruncateAsync`, static `SumAsync` / `AvgAsync` / `MinAsync` / `MaxAsync`, `PluckAsync`, `BulkForceDeleteAsync`.
- **Conditional queries**: `When(condition, ...)`, `When(value, (q, v) => ...)`, `Unless`, `Apply(scope)`.
- **Row locks**: `LockForUpdate()` (FOR UPDATE / UPDLOCK), `SharedLock()` (FOR SHARE / LOCK IN SHARE MODE / HOLDLOCK).
- **Relation filters**: `WhereHas`, `OrWhereHas`, `WhereDoesntHave`, `OrWhereDoesntHave`, `Has(relation, op, count)`, `OrHas`, `DoesntHave`, `WhereRelation`, `OrWhereRelation` (with constraints, nested, self relations).
- **Relation aggregates**: `WithCount`, `WithSum`, `WithAvg`, `WithMin`, `WithMax`, `WithExists`; `[Computed]` properties are filled from query results and never written.
- **Constrained eager loading**: `With<TRelated>("Orders", q => q.Where(...).OrderBy(...))`.
- **Paging**: `SimplePaginate` (no COUNT) and keyset `CursorPaginate` with `NextCursor` / `PreviousCursor` (+ async).
- **Value converters**: `IValueConverter` + `[Converter(typeof(...))]`, `[EnumAsString]`, `[JsonColumn]` (now converts lists / classes), `[Encrypted]` (AES-256-CBC + HMAC-SHA256, `EncryptedStringConverter.Key`).
- **Bulk copy**: `BulkOperations.BulkCopy` / `BulkCopyAsync` with SqlBulkCopy, PostgreSQL `COPY` and MySqlBulkCopy (`AllowLoadLocalInfile=true`); batched INSERTs otherwise.
- **Lambda queries**: `Where` / `OrWhere` / `WhereNot` with `Expression<Func<T, bool>>`, `OrderBy` / `OrderByDesc` / `Select` / `Pluck` with property lambdas, static `Model.Where(lambda)`.
- **Read replicas**: `DbConfig.AddReadReplica`, `MersoCoreOptions.AddReadReplica`, `ReadReplicas.BeginScope()` (read your own writes), `QueryBuilder.OnPrimary()`.
- **OpenTelemetry**: ActivitySource `mersolutionCore` (`MersoTelemetry.SourceName`) with database semantic convention tags; `MersoTelemetry.IncludeQueryText`.
- **`mersocore` .NET tool** (package `mersolutionCore.Cli`): `mersocore scaffold` writes model classes from an existing database (keys, timestamps, soft delete, relations from foreign keys).
- **Repository**: release workflow (tag → NuGet + GitHub release), Dependabot, issue / pull request templates, SECURITY.md, CONTRIBUTING.md, `tests/docker-compose.yml`.

### Changed

- Model reads go through `RunReader` / `RunReaderAsync` / `StreamReader` instead of `RunDataTable`. A custom `DbCommandBase` subclass that overrides `RunDataTable` to intercept model reads must override these as well.
- `[JsonColumn]` is a real converter: a non-string property is stored as JSON (a `string` property keeps its text as before).
- Eager loading key lists use parameters named `@k0`, `@k1`, ... (visible in the SQL log).
- `Select()` without arguments is ambiguous between the string and the lambda overload (compile error CS0121) — pass columns or remove the call.

### Fixed

- `DateOnly` / `TimeOnly` properties were silently skipped (not read, not written, no column created). They are columns now — such a property without a matching column needs `[Ignore]`.

## [2.0.0] - 2026-10-06

2.0 is a major release: several behaviours change (see *Upgrading from 1.x*) and the package gains a .NET 8 build.

### Added

- **Async API** for models, QueryBuilder, RawQuery, bulk operations, eager loading and transactions (`FindAsync`, `SaveAsync`, `GetAsync`, `PaginateAsync`, `UpsertAsync`, `RunAsync`, ...), all with `CancellationToken`.
- **Multiple databases**: `DbConfig.AddConnection(name, ...)`, `[Connection("name")]`, `Model.On("name")` (loaded models save back there), `MersoConnection.Use(...)` for a block / tenant; transactions on different databases nest.
- **Quoted identifiers** per provider (`[Order]`, `` `Order` ``, `"order"`), so reserved words work as column names.
- **Change tracking**: `Save()` writes only changed columns; `IsDirty`, `GetDirty`, `GetOriginal`, `SyncOriginal`.
- **Optimistic concurrency**: `[RowVersion]` + `DbConcurrencyException`.
- **Upsert**: `BulkOperations.Upsert` / `Model.Upsert` (MERGE, ON CONFLICT, ON DUPLICATE KEY UPDATE).
- **SQL log**: `MersoLog.QueryExecuted` (SQL, duration, error; parameter values only on request).
- **Composite primary keys**: several `[PrimaryKey]` properties (`Order` sets key order); `Find(a, b)`, `WhereKey(a, b)`, `FindMany`, `Destroy`, Save / Delete / Refresh, bulk update / delete and upsert; `HasOne` / `HasMany` / `BelongsTo` (+ eager loading) and `[Touches]` on several key columns (`"OrderId, Sku"`, default = the model's own key).
- **Hidden fields and mass assignment**: `[Hidden]` (left out of `ToDict` / `ToJson` / `MersoJson`), `MakeVisible` / `MakeHidden`, `[Fillable]` allow-list, `[Guarded]`, `ForceFill`, `ModelBase.StrictMassAssignment` + `MassAssignmentException`; `MersoJson.Configure(options)` for System.Text.Json / ASP.NET Core.
- **QueryBuilder**: parenthesised groups `Where(q => ...)`, `OrWhere(q => ...)`, `WhereNot(q => ...)`; `OrWhereIn`, `OrWhereNotIn`, `OrWhereNull`, `OrWhereNotNull`, `OrWhereBetween`, `WhereNotBetween`, `OrWhereLike`; `WhereColumn` / `OrWhereColumn`; `WhereExists` / `WhereNotExists` / `OrWhereExists`; `WhereIn` / `WhereNotIn` / `OrWhereIn` with a subquery; `Union` / `UnionAll` (order, limit and paging apply to the combined rows); `SelectRaw`, `OrderByRaw`, `HavingRaw`, `WhereRaw` / `OrWhereRaw` with `?` bindings; `GetAttribute(name)` for result columns without a property.
- **Relationships**: `HasManyThrough`, `HasOneThrough`; polymorphic `MorphMany`, `MorphOne`, `MorphTo` (+ `[MorphMany]` / `[MorphOne]` / `[MorphTo]` eager loading — one query per owner type, nested paths continue per type — `MorphMap` / `[MorphName]`); pivot columns with `WithPivot`, `WherePivot`, `GetPivot()`, `UpdateExistingPivot`, `Attach(ids, data)`, `Sync` with pivot data; `[Touches]` and `model.Touch()`.
- **Migrations**: `Schema.ChangeColumn` (type / NULL / default; SQLite rebuilds the table), `AddForeign`, `DropForeign` (finds the constraint name, also for unnamed keys), `DropForeignByName`; foreign keys from `TableBuilder.Foreign` are named `fk_{table}_{column}`; migration **batches**: `Rollback()` undoes the last `Migrate()`, `Rollback(n)` the last n migrations, `MigrationInfo.Batch`.
- **Retry of transient errors** (`MersoRetry`): deadlocks, serialization failures, lock timeouts, refused / dropped connections, too many connections — SQL Server, MySQL / MariaDB, PostgreSQL, SQLite; reads are retried after a dropped connection, writes only when the server rolled them back; `MersoTransaction.Run(action, attempts)` re-runs a whole transaction.
- **Distributed cache**: `ICacheStore` / `ICacheCounterStore`, `DistributedCacheStore` over `IDistributedCache` (Redis, SQL Server ...), `Cache.UseStore(...)` / `UseMemory()`; tags, prefixes and flush work across servers (the first `ForgetByPrefix` of a new prefix clears the cache once).
- **Dependency injection**: `services.AddMersoCore(o => o.UsePostgreSql(cs).AddConnection(...).UseDistributedCache().LogSql().UseRetry(3))`, `AddMersoDbContext<T>()`, `IServiceProvider.UseMersoCore()`; SQL log to `ILogger`.
- **Package**: `netstandard2.0` + `net8.0` targets, nullable reference annotations, XML documentation in the package, Source Link + symbol package, icon, README, MIT license, tests (`tests/mersolutionCore.Tests`) and GitHub Actions CI on SQL Server, MySQL, MariaDB, PostgreSQL and SQLite.

### Fixed

- **PostgreSQL on a Turkish (tr-TR) system**: columns whose name contains an upper-case `I` (`Id`, `IsActive`, `UserId` ...) were never filled, because DataTable compares names with the current culture and PostgreSQL returns them in lower case; `ChunkById` looped forever. Column matching is now culture-independent.
- `ChunkById` stops with an error instead of looping when the cursor column does not advance.
- `[GlobalScope]` filters are applied to every query (they were never applied).
- Transactions really are transactions: `MersoTransaction.Run` shares one connection and transaction with every model, QueryBuilder, RawQuery and bulk call inside it.
- Queued parameters reach scalar, reader, async and stored-procedure calls on every provider; typed / output parameters are kept.
- New ids come from the inserting connection (`SCOPE_IDENTITY` / `RETURNING` / `LAST_INSERT_ID` / `last_insert_rowid`), not `MAX(id)`; `long` and Guid keys work.
- QueryBuilder generates provider SQL for `First`, `Take`, `Skip`, `Paginate`, `Chunk`, `InRandomOrder`, `WhereDate` on MySQL, MariaDB, PostgreSQL and SQLite; `OrWhere` no longer leaks soft-deleted rows; counts ignore `ORDER BY`; `Update` / `Delete` / `Increment` return affected rows.
- Mapping of enum, Guid, DateTimeOffset, TimeSpan and SQLite text values; culture-independent numbers; SQLite `DECIMAL` no longer truncated.
- Observers fire and can cancel; `Save` / `Delete` / `Restore` / `ForceDelete` return `bool`.
- `Find()` excludes soft-deleted rows (`FindWithTrashed()`); relations and eager loading filter them.
- Eager loading matches int / long keys; nested `With("Orders.Items")`.
- Query cache keys include parameter values.
- Migrations: indexes are created, SQL Server text is `NVARCHAR`, each migration runs in a transaction, duplicate versions are reported.
- Connection pool: one pool per connection string; max size holds; double returns are ignored.
- Security: JWT signature / algorithm / issuer / audience checks; `Crypto.EncryptSecure` / `DecryptSecure`; unbiased random codes.
- Validation attributes read numbers culture-independently (tr-TR read "12.5" as 125).

### Upgrading from 1.x

- `Save()` of a loaded model updates only changed columns.
- Generated SQL quotes names; PostgreSQL names are quoted in lower case. Tables created with quoted mixed-case names need `ModelBase.QuoteIdentifiers = false`.
- `[GlobalScope]` filters now apply (also to `All()`, `Find()`, `Count()`).
- `Find()` no longer returns soft-deleted rows; `Where()` accepts only `=, !=, <>, <, >, <=, >=, LIKE, NOT LIKE, ILIKE`.
- `Migrator.Rollback()` without an argument now undoes the last **batch** (in 1.x: the last migration) — use `Rollback(1)` for one migration.
- Transient errors are retried by default (2 retries) — set `MersoRetry.MaxRetries = 0` to turn it off.
- `Fill()` / `Create(dictionary)` skip `[Guarded]` properties (and everything not `[Fillable]` once a model has `[Fillable]` properties); models without these attributes behave as before.
- Nullable annotations: `Find`, `First`, `FindAsync`, `Fresh`, single-relation `GetRelated`, `RawQuery.QueryFirst` / `Scalar` return `T?`; dictionary parameters are `Dictionary<string, object?>` (projects with nullable enabled may see new warnings).
- `RawQuery` methods have an optional `connectionName` parameter; custom `DbCommandBase` subclasses implement `NewConnection()`. Recompile callers.
- New foreign keys are named `fk_{table}_{column}`; `DropForeign(table, column)` also finds unnamed 1.x keys.

## [1.1.0]

Previous release (single `netstandard2.0` target).
