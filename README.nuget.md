# mersolutionCore

Cross-platform .NET ORM (Merso Framework) for **SQL Server, MySQL, MariaDB, PostgreSQL and SQLite**.
Targets .NET Standard 2.0 (.NET Framework 4.6.1+, .NET Core 2.0+) and .NET 8+.

Documentation: [mersocore.com/docs](https://mersocore.com/docs/) · Source: [github.com/mersolution/mersocore](https://github.com/mersolution/mersocore) · Release notes: [CHANGELOG](https://github.com/mersolution/mersocore/blob/main/CHANGELOG.md)

## Quick start

```csharp
using mersolutionCore.Config;
using mersolutionCore.ORM;
using mersolutionCore.ORM.Entity;

DbConfig.ConfigurePostgreSQL("localhost", "shop", "postgres", "secret");
ModelBase.Configure(DbConfig.CreateConnection);

[Table("Users")]
public class User : Model<User>
{
    public int Id { get; set; }
    [Fillable] public string Name { get; set; } = "";
    [Hidden] public string PasswordHash { get; set; } = "";
    [CreatedAt] public DateTime? CreatedAt { get; set; }
    [UpdatedAt] public DateTime? UpdatedAt { get; set; }
    [SoftDelete] public DateTime? DeletedAt { get; set; }
}

var user = await User.CreateAsync(new User { Name = "Ayşe" });
var found = await User.FindAsync(user.Id);

var page = await User.Query()
    .Where(q => q.Where("Name", "LIKE", "A%").OrWhere("Name", "LIKE", "B%"))
    .OrderBy("Name")
    .PaginateAsync(1, 20);
```

## Highlights

- Active Record models with change tracking, soft delete, timestamps, events and observers
- Fluent queries: groups, lambdas (`Where(u => u.Age >= 18)`), subqueries (`WhereExists`, `WhereIn(column, subquery)`), `Union`, raw pieces with `?` bindings
- Relations: HasOne, HasMany, BelongsTo, BelongsToMany (pivot columns), HasManyThrough, polymorphic MorphMany / MorphTo, eager loading with conditions; `WhereHas`, `Has`, `WithCount` / `WithSum`
- Streaming (`Cursor()` / `CursorAsync()`), `SimplePaginate`, keyset `CursorPaginate`; DateOnly / TimeOnly
- Composite primary keys, optimistic concurrency (`[RowVersion]`), upsert, bulk operations and `BulkCopy` (SqlBulkCopy / COPY / MySqlBulkCopy)
- Value converters: `[EnumAsString]`, `[JsonColumn]`, `[Encrypted]`, custom `IValueConverter`
- Async API with `CancellationToken`; several databases (`[Connection]`, `Model.On("name")`, per-tenant scopes), read replicas
- Migrations with `ChangeColumn`, foreign keys and batches; automatic retry of transient errors
- Models from an existing database: `dotnet tool install -g mersolutionCore.Cli` → `mersocore scaffold`
- Memory or distributed cache (Redis / `IDistributedCache`), SQL log, OpenTelemetry traces, dependency injection:

```csharp
builder.Services.AddMersoCore(o => o
    .UsePostgreSql(builder.Configuration.GetConnectionString("Main")!)
    .UseDistributedCache()
    .LogSql());
```

MIT licensed.
