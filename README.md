# mersolutionCore

Cross-platform .NET ORM, query builder and utility library targeting **.NET Standard 2.0** — works on .NET Framework 4.6.1+ and .NET Core 2.0 / .NET 5–9+.

---

## Features

- **ORM** — `Model<T>` / `BaseModel<T>` with CRUD, relationships, soft deletes, timestamps and lifecycle events
- **Fluent Query Builder** — chainable WHERE, JOIN, ORDER, GROUP, LIMIT, aggregate and pagination API
- **Multi-Database** — SQL Server, MySQL, MariaDB, PostgreSQL, SQLite via a unified `IDbCommand` interface
- **DbContext** — `EnsureCreated`, `EnsureDeleted`, `EnsureFresh`, `MerSet<T>` typed entity sets
- **Migrations** — code-first schema management with `Schema`, `Migrator`, `MigrationRunner`
- **Caching** — in-memory key/value cache (`MersoCache`) and query-level cache (`QueryCache`)
- **Validation** — fluent and attribute-based validation (`MersoValidator`, `[Required]`, `[Email]`, …)
- **Transactions** — `MersoTransaction.Run()` / `TryRun()` with automatic rollback
- **Bulk Operations** — high-performance `BulkInsert`, `BulkUpdate`, `BulkDelete`, `BulkUpsert`
- **Raw Queries** — `RawQuery.Query<T>()`, `Scalar<T>()`, `Execute()`, `QueryTable()`
- **JSON Columns** — `JsonValue<T>`, `JsonDictionary`, `JsonList<T>` column wrappers
- **Observers & Events** — `MersoObserver<T>`, `IMersoEvents` model lifecycle hooks
- **Global Scopes** — automatic query filters via `[GlobalScope]` attribute
- **Connection Pool** — built-in ADO.NET connection pooling
- **Cryptography** — AES/TripleDES encryption, SHA/MD5/HMAC hashing (`Crypto`, `Security`)
- **Utilities** — `TextHelper`, `StringExtensions`, `HttpClientHelper`, `JwtHelper`

---

## Installation

```bash
dotnet add package mersolutionCore
```

Or add a project reference directly:

```xml
<ProjectReference Include="..\mersolutionCore\mersolutionCore.csproj" />
```

---

## Quick Start

### 1 — Configure the database

```csharp
using mersolutionCore.Config;

// SQL Server (Windows auth)
DbConfig.ConfigureSqlServer(@".\SQLEXPRESS", "MyDatabase");

// MySQL / MariaDB
DbConfig.ConfigureMySQL("localhost", "mydb", "root", "password");

// PostgreSQL
DbConfig.ConfigurePostgreSQL("localhost", "mydb", "postgres", "secret");

// SQLite
DbConfig.ConfigureSQLite("./app.db");
```

### 2 — Define a model

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

### 3 — Create a DbContext

```csharp
using mersolutionCore.ORM;
using mersolutionCore.Config;

public class AppDbContext : DbContext
{
    static AppDbContext() => DbConfig.ConfigureSqlServer(@".\SQLEXPRESS", "MyDatabase");

    public MerSet<User>    Users    { get; set; } = null!;
    public MerSet<Product> Products { get; set; } = null!;

    public AppDbContext() : base(DbConfig.CreateConnection) { }
}

// On startup — creates all tables (idempotent)
var db = new AppDbContext();
db.EnsureCreated();
```

### 4 — CRUD

```csharp
// Insert
var user = new User { Name = "Alice", Email = "alice@example.com" };
user.Save();

// Find
var found = User.Find(user.Id);
var orFail = User.FindOrFail(99);   // throws ModelNotFoundException

// Update
found.Name = "Bob";
found.Save();

// Soft delete / restore
found.Delete();
found.Restore();

// Hard delete
found.ForceDelete();
```

### 5 — Fluent Query Builder

```csharp
var results = User
    .Where("IsActive", true)
    .Where("CreatedAt", ">", DateTime.Today.AddDays(-30))
    .OrderByDesc("CreatedAt")
    .Take(20)
    .Select("Id", "Name", "Email")
    .Get();

// Pagination
var page = User.Query().Where("IsActive", true).Paginate(pageNumber: 1, pageSize: 10);
// page.Items, page.TotalCount, page.TotalPages, page.HasNextPage

// Aggregates
int  count = User.Count();
bool any   = User.Exists();
var  names = User.Pluck("Name");   // List<object>

// Debug
string sql = User.Query().Where("IsActive", true).Take(5).ToSql();
```

### 6 — Relationships

```csharp
[Table("Orders")]
public class Order : Model<Order>
{
    [PrimaryKey(AutoIncrement = true)] public int Id { get; set; }
    [Column("UserId")]                 public int UserId { get; set; }

    [BelongsTo(typeof(User), "UserId")]
    public User? Owner { get; set; }
}

// Eager load
var orders = Order.Query().With("Owner").Get();
```

### 7 — Transactions

```csharp
MersoTransaction.Run(() =>
{
    var user  = new User { Name = "TX User", Email = "tx@example.com" };
    user.Save();
    var order = new Order { UserId = user.Id, Total = 500m };
    order.Save();
});

bool ok = MersoTransaction.TryRun(() => { /* … */ });
```

### 8 — Cache

```csharp
MersoCache.Set("key", value, TimeSpan.FromMinutes(10));
var val = MersoCache.Get<string>("key");

// Remember pattern
var users = MersoCache.Remember("all_users", TimeSpan.FromMinutes(5), () => User.All());

// Query-level cache
var active = User.Query().Where("IsActive", true).Remember(TimeSpan.FromMinutes(5));
```

### 9 — Low-level DbFactory

```csharp
using mersolutionCore.Command;

using var db = DbFactory.CreateSqlServer(config);
var data  = db.RunDataTable("SELECT * FROM Users");
var count = db.RunToInt32Scaler("SELECT COUNT(*) FROM Users");

db.ParametersAdd("@name", "Alice");
db.RunExecute("INSERT INTO Users (Name) VALUES (@name)");
```

---

## Supported Databases

| Database   | NuGet Package              | Cross-Platform |
|------------|----------------------------|:--------------:|
| SQL Server | `Microsoft.Data.SqlClient` | ✅ |
| MySQL      | `MySqlConnector`           | ✅ |
| MariaDB    | `MySqlConnector`           | ✅ |
| PostgreSQL | `Npgsql`                   | ✅ |
| SQLite     | `Microsoft.Data.Sqlite`    | ✅ |

---

## Project Structure

```
mersolutionCore/
├── Command/        # DB drivers — IDbCommand, DbFactory, SqlServer/MySQL/MariaDB/PostgreSQL/SQLite
├── Config/         # DbConfig — connection factory and provider configuration
├── ORM/            # ModelBase, QueryBuilder, DbContext, MerSet<T>, Migrations, Relations,
│                   #   BulkOperations, Transactions, RawQuery, Observers, Events, GlobalScope,
│                   #   JsonColumns, ConnectionPool, SoftDeletes, Validation attributes
├── Cache/          # MemoryCache, QueryCache
├── Http/           # HttpClientHelper, JwtHelper
├── Library/        # Crypto, Security, TextHelper, StringExtensions
└── Validation/     # MersoValidator, ValidationResult, ValidationAttributes
```

---

## Compatibility

| Platform       | Minimum Version |
|----------------|-----------------|
| .NET Standard  | 2.0             |
| .NET Framework | 4.6.1+          |
| .NET Core      | 2.0+            |
| .NET           | 5, 6, 7, 8, 9+  |

---

## License

MIT License — © Mersolution Technology
