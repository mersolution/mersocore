# mersolutionCore

[![Docs](https://img.shields.io/badge/docs-mersocore.com-6c429c?style=for-the-badge&logo=gitbook&logoColor=white)](https://mersocore.com/docs/)
[![.NET Standard](https://img.shields.io/badge/.NET_Standard-2.0-5c2d91?style=for-the-badge&logo=dotnet&logoColor=white)](https://mersocore.com)
[![License](https://img.shields.io/badge/license-MIT-84cc16?style=for-the-badge&logo=opensourceinitiative&logoColor=white)](https://github.com/mersolution/mersocore)

> **mersolutionCore** is a lightweight, cross-platform ORM and fluent query builder built specifically for .NET Standard — works seamlessly on both .NET Framework and modern .NET.

Explore the full documentation at [mersocore.com](https://mersocore.com).

---

## 🚀 Why mersolutionCore?

Modern .NET applications need a reliable, fast, and easy-to-use database layer. mersolutionCore gives you the perfect balance between productivity and control:

* **Lightweight Architecture:** No heavy dependencies — built directly on ADO.NET for maximum speed.
* **Fluent Query Builder:** Chainable methods for clean, readable queries without raw SQL.
* **Model-Based ORM:** Object-oriented database management with full CRUD, relationships, and lifecycle hooks.
* **Cross-Platform:** A single `.NET Standard 2.0` library that runs everywhere .NET runs.
* **Zero Config:** Just set your connection string and call `EnsureCreated()` — tables are created automatically.

---

## ✨ Core Features

* **ORM** — `Model<T>` with CRUD, relationships, soft deletes, timestamps and lifecycle events
* **Fluent Query Builder** — chainable WHERE, JOIN, ORDER, GROUP, LIMIT, aggregate and pagination
* **Multi-Database** — SQL Server, MySQL, MariaDB, PostgreSQL, SQLite via a unified `IDbCommand` interface
* **DbContext & MerSet\<T\>** — typed entity sets, `EnsureCreated`, `EnsureDeleted`, `EnsureFresh`
* **Migrations** — code-first schema management with `Schema`, `Migrator`, `MigrationRunner`
* **Caching** — in-memory key/value (`MersoCache`) and query-level cache (`QueryCache`)
* **Validation** — fluent and attribute-based validation (`MersoValidator`, `[Required]`, `[Email]`, …)
* **Transactions** — `MersoTransaction.Run()` / `TryRun()` with automatic rollback on failure
* **Bulk Operations** — high-performance `BulkInsert`, `BulkUpdate`, `BulkDelete`, `BulkUpsert`
* **Raw Queries** — `RawQuery.Query<T>()`, `Scalar<T>()`, `Execute()`, `QueryTable()`
* **JSON Columns** — `JsonValue<T>`, `JsonDictionary`, `JsonList<T>` column wrappers
* **Observers & Events** — `MersoObserver<T>` and `IMersoEvents` model lifecycle hooks
* **Global Scopes** — automatic query filters via `[GlobalScope]` attribute
* **Connection Pool** — built-in ADO.NET connection pooling
* **Cryptography** — AES/TripleDES encryption, SHA/MD5/HMAC hashing (`Crypto`, `Security`)
* **Utilities** — `TextHelper`, `StringExtensions`, `HttpClientHelper`, `JwtHelper`

---

## 📖 Quick Links

| Resource | Link |
| :--- | :--- |
| **Official Website** | [mersocore.com](https://mersocore.com) |
| **Full Documentation** | [mersocore.com/docs](https://mersocore.com/docs/) |
| **Getting Started** | [Installation Guide](https://mersocore.com/docs/) |
| **GitHub** | [mersolution/mersocore](https://github.com/mersolution/mersocore) |

---

## 🏁 Getting Started

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
using mersolutionCore.ORM;

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

### 4. CRUD Operations

```csharp
// Create
var user = new User { Name = "Alice", Email = "alice@example.com" };
user.Save();

// Read
var found  = User.Find(user.Id);
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

### 5. Fluent Query Builder

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
// page.Items  |  page.TotalCount  |  page.TotalPages  |  page.HasNextPage

// Aggregates
int  count = User.Count();
bool any   = User.Exists();
var  names = User.Pluck("Name");   // List<object>

// Debug — inspect generated SQL
string sql = User.Query().Where("IsActive", true).Take(5).ToSql();
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
├── Command/        # IDbCommand, DbFactory — SQL Server / MySQL / MariaDB / PostgreSQL / SQLite drivers
├── Config/         # DbConfig — connection factory and provider configuration
├── ORM/            # ModelBase, QueryBuilder, DbContext, MerSet<T>
│                   #   Migrations, Relations, BulkOperations, Transactions
│                   #   RawQuery, Observers, Events, GlobalScope, JsonColumns
│                   #   ConnectionPool, SoftDeletes
├── Cache/          # MemoryCache, QueryCache
├── Http/           # HttpClientHelper, JwtHelper
├── Library/        # Crypto, Security, TextHelper, StringExtensions
└── Validation/     # MersoValidator, ValidationResult, ValidationAttributes
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
<tr style="background-color:#f9f9f9;"><td style="border:1px solid #ddd;padding:8px;"><strong>Fluent Query Builder</strong></td><td style="border:1px solid #ddd;padding:8px;">Clean chainable query API — no raw SQL needed</td></tr>
<tr><td style="border:1px solid #ddd;padding:8px;"><strong>Relations</strong></td><td style="border:1px solid #ddd;padding:8px;">HasOne, HasMany, BelongsTo, BelongsToMany with eager loading</td></tr>
<tr style="background-color:#f9f9f9;"><td style="border:1px solid #ddd;padding:8px;"><strong>Observers & Events</strong></td><td style="border:1px solid #ddd;padding:8px;">Model lifecycle hooks — Creating, Created, Updating, Deleting, …</td></tr>
<tr><td style="border:1px solid #ddd;padding:8px;"><strong>Bulk Operations</strong></td><td style="border:1px solid #ddd;padding:8px;">Efficient BulkInsert, BulkUpdate, BulkDelete, BulkUpsert</td></tr>
<tr style="background-color:#f9f9f9;"><td style="border:1px solid #ddd;padding:8px;"><strong>JSON Columns</strong></td><td style="border:1px solid #ddd;padding:8px;">Map JSON/text columns to C# objects with JsonValue&lt;T&gt;</td></tr>
<tr><td style="border:1px solid #ddd;padding:8px;"><strong>Validation Attributes</strong></td><td style="border:1px solid #ddd;padding:8px;">[Required], [Email], [Range], [MinLength], [Url], [Phone], …</td></tr>
<tr style="background-color:#f9f9f9;"><td style="border:1px solid #ddd;padding:8px;"><strong>Connection Pool</strong></td><td style="border:1px solid #ddd;padding:8px;">Built-in ADO.NET connection pooling for high-throughput apps</td></tr>
<tr><td style="border:1px solid #ddd;padding:8px;"><strong>Query Cache</strong></td><td style="border:1px solid #ddd;padding:8px;">Cache results with MersoCache.Remember() or .Remember() extension</td></tr>
<tr style="background-color:#f9f9f9;"><td style="border:1px solid #ddd;padding:8px;"><strong>Soft Deletes</strong></td><td style="border:1px solid #ddd;padding:8px;">[SoftDelete] attribute — Delete(), Restore(), WithTrashed(), OnlyTrashed()</td></tr>
<tr><td style="border:1px solid #ddd;padding:8px;"><strong>Transactions</strong></td><td style="border:1px solid #ddd;padding:8px;">MersoTransaction.Run() / TryRun() with automatic rollback</td></tr>
<tr style="background-color:#f9f9f9;"><td style="border:1px solid #ddd;padding:8px;"><strong>Global Scopes</strong></td><td style="border:1px solid #ddd;padding:8px;">Automatic WHERE filters applied to every query via [GlobalScope]</td></tr>
<tr><td style="border:1px solid #ddd;padding:8px;"><strong>Cryptography</strong></td><td style="border:1px solid #ddd;padding:8px;">AES/TripleDES encryption, SHA-256/MD5/HMAC hashing</td></tr>
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
<tr style="background-color:#f9f9f9;"><td style="border:1px solid #ddd;padding:8px;"><strong>.NET</strong></td><td style="border:1px solid #ddd;padding:8px;">5, 6, 7, 8, 9+</td></tr>
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
<tr><td style="border:1px solid #ddd;padding:8px;"><strong>Database Config</strong></td><td style="border:1px solid #ddd;padding:8px;">Configure your connection string and provider</td></tr>
<tr style="background-color:#f9f9f9;"><td style="border:1px solid #ddd;padding:8px;"><strong>ORM Model</strong></td><td style="border:1px solid #ddd;padding:8px;">Define models with attributes and relationships</td></tr>
<tr><td style="border:1px solid #ddd;padding:8px;"><strong>Query Builder</strong></td><td style="border:1px solid #ddd;padding:8px;">Build complex, chainable queries</td></tr>
<tr style="background-color:#f9f9f9;"><td style="border:1px solid #ddd;padding:8px;"><strong>Relations</strong></td><td style="border:1px solid #ddd;padding:8px;">Define and query model relationships</td></tr>
<tr><td style="border:1px solid #ddd;padding:8px;"><strong>Migrations</strong></td><td style="border:1px solid #ddd;padding:8px;">Manage database schema with code-first migrations</td></tr>
<tr style="background-color:#f9f9f9;"><td style="border:1px solid #ddd;padding:8px;"><strong>Caching</strong></td><td style="border:1px solid #ddd;padding:8px;">Cache queries and results for better performance</td></tr>
<tr><td style="border:1px solid #ddd;padding:8px;"><strong>Validation</strong></td><td style="border:1px solid #ddd;padding:8px;">Validate models with attributes or fluent rules</td></tr>
<tr style="background-color:#f9f9f9;"><td style="border:1px solid #ddd;padding:8px;"><strong>CRUD Examples</strong></td><td style="border:1px solid #ddd;padding:8px;">Complete working CRUD examples</td></tr>
</tbody>
</table>

---

*mersolutionCore — a lightweight and fast .NET ORM and query builder by [Mersolution Technology](https://mersolution.com)*
