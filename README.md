# mersoCore

[![Version](https://img.shields.io/badge/version-1.1.0-6c429c?style=for-the-badge)](https://mersocore.com)
[![Docs](https://img.shields.io/badge/docs-mersocore.com-6c429c?style=for-the-badge&logo=gitbook&logoColor=white)](https://mersocore.com/docs/)
[![.NET Standard](https://img.shields.io/badge/.NET_Standard-2.0-5c2d91?style=for-the-badge&logo=dotnet&logoColor=white)](https://mersocore.com)
[![License](https://img.shields.io/badge/license-MIT-84cc16?style=for-the-badge&logo=opensourceinitiative&logoColor=white)](https://github.com/mersolution/mersocore)

> **Merso Framework** (`mersolutionCore`) **v1.1.0** is a cross-platform **.NET ORM Framework** targeting .NET Standard 2.0 — it runs on .NET Framework 4.6.1+, .NET Core 2.0+ and .NET 5+.

Explore the full documentation at [mersocore.com](https://mersocore.com).

---

## v1.1.0

Current library and site release. Package version is `1.1.0` in `mersolutionCore.csproj`. Full API docs: [mersocore.com/docs](https://mersocore.com/docs/).

---

## Why mersolutionCore?

Modern .NET applications need a reliable, fast, and easy-to-use database layer. mersolutionCore gives you the balance between productivity and control:

* **.NET ORM Framework:** `Model<T>`, `DbContext`, `MerSet<T>`, relations, observers, and code-first tables.
* **Fluent Query Builder:** Chainable WHERE, JOIN, ORDER, GROUP, LIMIT, aggregates and pagination — no raw SQL required.
* **Cross-Platform:** One `.NET Standard 2.0` DLL for .NET Framework and modern .NET.
* **Multi-Database:** SQL Server, MySQL, MariaDB, PostgreSQL, SQLite via `IDbCommand`.
* **Minimal Setup:** Configure the connection, call `EnsureCreated()` — tables are created automatically.

---

## Core Features

* **ORM** — `Model<T>` with CRUD, relationships, soft deletes, timestamps and lifecycle events
* **Fluent Query Builder** — chainable WHERE, `OrWhere`, JOIN, ORDER, GROUP, LIMIT, aggregate and pagination
* **Multi-Database** — SQL Server, MySQL, MariaDB, PostgreSQL, SQLite via `IDbCommand`
* **DbContext & MerSet\<T\>** — typed entity sets, `EnsureCreated`, `EnsureDeleted`, `EnsureFresh`
* **Migrations** — code-first schema with `Schema`, `Migrator`, `MigrationRunner`
* **Caching** — `MersoCache` and `QueryCache`
* **Validation** — `MersoValidator` and attributes (`[Required]`, `[Email]`, …)
* **Transactions** — `MersoTransaction.Run()` / `TryRun()` with automatic rollback
* **Bulk Operations** — `BulkInsert`, `BulkUpdate`, `BulkDelete`, `BulkUpsert`
* **Raw Queries** — `RawQuery.Query<T>()`, `Scalar<T>()`, `Execute()`, `QueryTable()`
* **JSON Columns** — `JsonValue<T>`, `JsonDictionary`, `JsonList<T>`
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

---

## Quick Start

### 1. Configure the Database

```csharp
using mersolutionCore.Config;

DbConfig.ConfigureSqlServer(@".\SQLEXPRESS", "MyDatabase");
DbConfig.ConfigureMySQL("localhost", "mydb", "root", "password");
DbConfig.ConfigurePostgreSQL("localhost", "mydb", "postgres", "secret");
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

Grouped OR conditions use `WhereRaw` (AND binds tighter than OR):

```csharp
var staff = User.Query()
    .Where("IsActive", true)
    .WhereRaw("(Role = 'admin' OR Role = 'moderator')")
    .Get();
```

---

## Supported Databases

| Database | NuGet | Port | Cross-platform |
| :--- | :--- | :--- | :---: |
| **SQL Server** | `Microsoft.Data.SqlClient` | 1433 | ✅ |
| **MySQL** | `MySqlConnector` | 3306 | ✅ |
| **MariaDB** | `MySqlConnector` | 3306 | ✅ |
| **PostgreSQL** | `Npgsql` | 5432 | ✅ |
| **SQLite** | `Microsoft.Data.Sqlite` | — | ✅ |

---

## Compatibility

| Platform | Minimum |
| :--- | :--- |
| **.NET Standard** | 2.0 |
| **.NET Framework** | 4.6.1+ |
| **.NET Core** | 2.0+ |
| **.NET** | 5, 6, 7, 8, 9+ |

---

*mersolutionCore v1.1.0 — a .NET ORM Framework by [Mersolution Technology](https://mersolution.com)*
