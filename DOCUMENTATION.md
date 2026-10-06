# mersolutionCore ORM Documentation

**Version:** 2.1.0  
**Target Frameworks:** .NET Standard 2.0 (.NET Framework 4.6.1+, .NET Core 2.0+) and .NET 8 (.NET 8, 9, 10)  
**Author:** Merso Team  
**Last Updated:** October 2026 — see "New in 2.1.0", "New in 2.0.0" and "Upgrading from 1.x" in README.md

---

## 📋 Table of Contents

1. [Introduction](#introduction)
2. [Getting Started](#getting-started)
3. [Model Definition](#model-definition)
4. [CRUD Operations](#crud-operations)
5. [QueryBuilder](#querybuilder)
6. [Relationships](#relationships)
7. [Eager Loading](#eager-loading)
8. [Soft Delete](#soft-delete)
9. [Timestamps](#timestamps)
10. [MersoEvents](#mersoevents)
11. [Observers](#observers)
12. [Validation](#validation)
13. [Bulk Operations](#bulk-operations)
14. [Transactions](#transactions)
15. [Caching](#caching)
16. [Global Scopes](#global-scopes)
17. [Raw Queries](#raw-queries)
18. [Migrations](#migrations)
19. [Connection Pool](#connection-pool)
20. [JSON Columns](#json-columns)
21. [Async API](#async-api)
22. [Multiple Databases](#multiple-databases)
23. [Change Tracking & Concurrency](#change-tracking--concurrency)
24. [SQL Log](#sql-log)
25. [Composite Keys](#composite-keys)
26. [Hidden Fields & Mass Assignment](#hidden-fields--mass-assignment)
27. [Advanced Queries](#advanced-queries)
28. [More Relationships](#more-relationships)
29. [Schema Changes & Migration Batches](#schema-changes--migration-batches)
30. [Retry of Transient Errors](#retry-of-transient-errors)
31. [Distributed Cache](#distributed-cache)
32. [Dependency Injection](#dependency-injection)
33. [Nullable Annotations & Package](#nullable-annotations--package)
34. [DateOnly & TimeOnly](#dateonly--timeonly)
35. [Streaming & Chunks](#streaming--chunks)
36. [Conditional Queries & Row Locks](#conditional-queries--row-locks)
37. [Relation Queries & Aggregates](#relation-queries--aggregates)
38. [Simple & Cursor Pagination](#simple--cursor-pagination)
39. [Value Converters](#value-converters)
40. [Bulk Copy](#bulk-copy)
41. [Lambda Queries](#lambda-queries)
42. [Read Replicas](#read-replicas)
43. [OpenTelemetry](#opentelemetry)
44. [Scaffolding Models (mersocore tool)](#scaffolding-models-mersocore-tool)

---

## Introduction

**mersolutionCore** is a lightweight, feature-rich ORM (Object-Relational Mapping) library for .NET applications. It provides an intuitive API similar to Entity Framework and Laravel Eloquent, making database operations simple and efficient.

### Key Features

- ✅ Attribute-based model mapping
- ✅ Fluent QueryBuilder API
- ✅ Soft Delete support
- ✅ Automatic timestamps
- ✅ Model lifecycle events (MersoEvents)
- ✅ Relationship support (HasMany, BelongsTo, HasOne)
- ✅ Eager Loading
- ✅ Validation system
- ✅ Bulk operations
- ✅ Transaction support
- ✅ In-memory caching
- ✅ Global scopes
- ✅ Raw SQL queries
- ✅ Migration system
- ✅ Connection pooling
- ✅ JSON column support
- ✅ Async API with CancellationToken
- ✅ Several databases (named connections, per-tenant scope)
- ✅ Change tracking (only changed columns are saved) and optimistic concurrency
- ✅ Upsert (MERGE / ON CONFLICT / ON DUPLICATE KEY)
- ✅ SQL log with duration and errors
- ✅ Quoted identifiers (reserved words as column names)
- ✅ Streaming (`Cursor`), keyset paging, relation filters / counts, lambda conditions
- ✅ Value converters (enum names, JSON, encrypted columns), bulk copy, read replicas, OpenTelemetry
- ✅ `mersocore scaffold`: model classes from an existing database

---

## Getting Started

### Installation

```bash
dotnet add package mersolutionCore
```

### Configuration

```csharp
using mersolutionCore.Command.Abstractions;   // DbProviderType
using mersolutionCore.Config;
using mersolutionCore.ORM;

// Configure database connection
DbConfig.Configure("Server=localhost;Database=MyDb;Trusted_Connection=True;", DbProviderType.SqlServer);

// Create DbContext
public class AppDbContext : DbContext
{
    static AppDbContext()
    {
        DbConfig.Configure("your_connection_string", DbProviderType.SqlServer);
    }
}

// Initialize
var db = new AppDbContext();
db.EnsureCreated(); // Create tables if not exist
```

---

## Model Definition

### Basic Model

```csharp
using mersolutionCore.ORM;
using mersolutionCore.ORM.Entity;

[Table("Users")]
public class User : Model<User>
{
    [PrimaryKey(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("Name", Length = 100)]
    public string Name { get; set; }

    [Column("Email", Length = 255)]
    public string Email { get; set; }

    [Column("IsActive")]
    public bool IsActive { get; set; } = true;

    [CreatedAt]
    public DateTime? CreatedAt { get; set; }

    [UpdatedAt]
    public DateTime? UpdatedAt { get; set; }

    [SoftDelete]
    public DateTime? DeletedAt { get; set; }
}
```

### Available Attributes

| Attribute | Description |
|-----------|-------------|
| `[Table("name")]` | Specifies table name |
| `[PrimaryKey]` | Marks primary key column |
| `[Column("name")]` | Specifies column name and options |
| `[CreatedAt]` | Auto-set on create |
| `[UpdatedAt]` | Auto-set on update |
| `[SoftDelete]` | Enables soft delete |
| `[Ignore]` | Excludes property from mapping |
| `[Computed]` | Filled from query results (WithCount, SelectRaw alias, view column), never written |
| `[EnumAsString]` / `[JsonColumn]` / `[Encrypted]` / `[Converter(typeof(...))]` | Stored through a value converter |

---

## CRUD Operations

### Create

```csharp
// Method 1: New instance + Save
var user = new User
{
    Name = "John Doe",
    Email = "john@example.com"
};
user.Save();

// Method 2: Static Create
var user = User.Create(new User
{
    Name = "John Doe",
    Email = "john@example.com"
});
```

### Read

```csharp
// Find by ID (soft-deleted rows are excluded)
var user = User.Find(1);

// Find including soft-deleted rows
var any = User.FindWithTrashed(1);

// Find or throw exception
var user = User.FindOrFail(1);

// Find multiple
var users = User.FindMany(1, 2, 3);

// Get first record
var user = User.First();
var user = User.FirstOrFail();

// Get all records
var users = User.All();

// Check existence
bool exists = User.Exists();
bool exists = User.Exists(1);
```

### Update

```csharp
var user = User.Find(1);
user.Name = "Jane Doe";
user.Save();
```

### Delete

```csharp
var user = User.Find(1);
user.Delete();      // Soft delete (if enabled)
user.ForceDelete(); // Permanent delete
```

### Additional Methods

```csharp
// FirstOrCreate - Find or create
var user = User.Where("Email", "test@test.com").First()
    ?? User.Create(new User { Name = "Test", Email = "test@test.com" });

// Replicate - Clone model
var clone = user.Replicate();
clone.Save();

// Refresh - Reload from database
user.Refresh();

// Fresh - Get fresh instance
var fresh = user.Fresh();

// ToJson - Convert to JSON
string json = user.ToJson();

// ToDict - Convert to Dictionary
var dict = user.ToDict();

// Only - Get specific fields
var partial = user.Only("Name", "Email");

// Except - Exclude specific fields
var partial = user.Except("Password");

// Increment / Decrement
product.Increment("Stock", 10);
product.Decrement("Stock", 5);
```

---

## QueryBuilder

### Basic Queries

```csharp
// Where
var users = User.Query()
    .Where("IsActive", true)
    .Get();

// Where with operator
var users = User.Query()
    .Where("Age", ">", 18)
    .Get();

// Multiple conditions
var users = User.Query()
    .Where("IsActive", true)
    .Where("Age", ">=", 21)
    .Get();
```

### Advanced Where Clauses

```csharp
// WhereIn
var users = User.Query()
    .WhereIn("Id", new object[] { 1, 2, 3 })
    .Get();

// WhereNotIn
var users = User.Query()
    .WhereNotIn("Status", new object[] { "banned", "deleted" })
    .Get();

// WhereBetween
var products = Product.Query()
    .WhereBetween("Price", 100m, 500m)
    .Get();

// WhereNull / WhereNotNull
var users = User.Query()
    .WhereNull("DeletedAt")
    .Get();

// WhereLike
var users = User.Query()
    .WhereLike("Name", "John")
    .Get();

// OrWhere
var users = User.Query()
    .Where("Role", "admin")
    .OrWhere("Role", "moderator")
    .Get();

// Parenthesised groups: (Role = 'admin' OR Role = 'moderator') AND IsActive = 1
var staff = User.Query()
    .Where(q => q.Where("Role", "admin").OrWhere("Role", "moderator"))
    .Where("IsActive", true)
    .Get();

// OrWhereIn / OrWhereNotIn / OrWhereNull / OrWhereNotNull / OrWhereBetween / WhereNotBetween / WhereNot(group)
```

Subqueries, `WhereColumn`, `Union` and raw pieces with `?` bindings: see [Advanced Queries](#advanced-queries).

### Ordering

```csharp
// OrderBy
var users = User.Query()
    .OrderBy("Name")
    .Get();

// OrderByDesc
var users = User.Query()
    .OrderByDesc("CreatedAt")
    .Get();

// Latest (order by CreatedAt DESC)
var users = User.Query().Latest().Get();

// Oldest (order by CreatedAt ASC)
var users = User.Query().Oldest().Get();
```

### Pagination

```csharp
// Take / Skip
var users = User.Query()
    .Skip(10)
    .Take(5)
    .Get();

// Paginate
var result = User.Query().OrderBy("Id").Paginate(pageNumber: 1, pageSize: 10);
// result.Items - List of items
// result.TotalCount - Total records
// result.TotalPages - Total pages
// result.CurrentPage - Current page
// result.HasNextPage - Has more pages
// result.HasPreviousPage - Has previous page

// Chunk - Process in batches
User.Query().Chunk(100, users =>
{
    foreach (var user in users)
    {
        // Process each batch
    }
});
```

### Aggregates

```csharp
int count = User.Count();
decimal sum = Product.Sum("Price");
decimal avg = Product.Avg("Price");
object min = Product.Min("Price");
object max = Product.Max("Stock");
List<object> names = User.Pluck("Name");
```

### Joins

```csharp
var orders = Order.Query()
    .Join("Users", "Orders.UserId", "Users.Id")
    .Select("Orders.*", "Users.Name as UserName")
    .Get();

var orders = Order.Query()
    .LeftJoin("Users", "Orders.UserId", "Users.Id")
    .Get();
```

### Debug

```csharp
// Get generated SQL
string sql = User.Query()
    .Where("IsActive", true)
    .OrderByDesc("Id")
    .Take(10)
    .ToSql();
```

---

## Relationships

### Defining Relationships

```csharp
[Table("Users")]
public class User : Model<User>
{
    [PrimaryKey(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("Name")]
    public string Name { get; set; }

    // HasMany relationship
    [HasMany(typeof(Order), "UserId")]
    public List<Order> Orders { get; set; }

    // HasOne relationship
    [HasOne(typeof(Profile), "UserId")]
    public Profile Profile { get; set; }
}

[Table("Orders")]
public class Order : Model<Order>
{
    [PrimaryKey(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("UserId")]
    public int UserId { get; set; }

    // BelongsTo relationship
    [BelongsTo(typeof(User), "UserId")]
    public User User { get; set; }
}
```

### Using Relationships

The attributed properties are filled by eager loading (below). For on-demand loading add relation methods — the
`HasMany` / `HasOne` / `BelongsTo` helpers are `protected`, so they are called inside the model:

```csharp
public class User : Model<User>
{
    public HasMany<User, Order> OrderRelation() => HasMany<Order>("UserId");
    public HasOne<User, Profile> ProfileRelation() => HasOne<Profile>("UserId");
}

public class Order : Model<Order>
{
    public BelongsTo<Order, User> Owner() => BelongsTo<User>("UserId");
}

List<Order> orders = user.OrderRelation().GetRelated();
int count = user.OrderRelation().Count();
var big = user.OrderRelation().Query().Where("Total", ">", 100).Get();
Profile? profile = user.ProfileRelation().GetRelated();
User? owner = order.Owner().GetRelated();

// Without a method: the relation classes have public constructors
var same = new HasMany<User, Order>(user, "UserId").GetRelated();
```

---

## Eager Loading

Load relationships efficiently with a single query:

```csharp
// Load single relationship
var users = User.Query()
    .With("Orders")
    .Get();

// Load multiple relationships
var users = User.Query()
    .With("Orders")
    .With("Profile")
    .Get();

// Access loaded relationships
foreach (var user in users)
{
    Console.WriteLine($"{user.Name} has {user.Orders.Count} orders");
}
```

---

## Soft Delete

### Configuration

```csharp
[Table("Users")]
public class User : Model<User>
{
    // ... other properties

    [SoftDelete]
    public DateTime? DeletedAt { get; set; }
}
```

### Usage

```csharp
// Soft delete
user.Delete();

// Check if trashed
bool isTrashed = user.Trashed();

// Restore
user.Restore();

// Permanent delete
user.ForceDelete();

// Query including trashed
var allUsers = User.WithTrashed();

// Query only trashed
var trashedUsers = User.OnlyTrashed();

// Normal query (excludes trashed)
var activeUsers = User.All();
```

---

## Timestamps

### Configuration

```csharp
[Table("Users")]
public class User : Model<User>
{
    [CreatedAt]
    public DateTime? CreatedAt { get; set; }

    [UpdatedAt]
    public DateTime? UpdatedAt { get; set; }
}
```

Timestamps are automatically set:
- `CreatedAt` - Set when record is created
- `UpdatedAt` - Set when record is created or updated

---

## MersoEvents

Model lifecycle events with custom naming convention.

### Implementation

```csharp
[Table("Users")]
public class User : Model<User>, IMersoEvents
{
    // Properties...

    // Called before creating
    public bool OnMersoCreating()
    {
        Console.WriteLine("Creating user...");
        return true; // Return false to cancel
    }

    // Called after creating
    public void OnMersoCreated()
    {
        Console.WriteLine($"User created with ID: {Id}");
    }

    // Called before updating
    public bool OnMersoUpdating()
    {
        return true;
    }

    // Called after updating
    public void OnMersoUpdated()
    {
        Console.WriteLine("User updated");
    }

    // Called before deleting
    public bool OnMersoDeleting()
    {
        return true;
    }

    // Called after deleting
    public void OnMersoDeleted()
    {
        Console.WriteLine("User deleted");
    }

    // Called before saving (create or update)
    public bool OnMersoSaving()
    {
        return true;
    }

    // Called after saving
    public void OnMersoSaved()
    {
        Console.WriteLine("User saved");
    }

    // Called before restoring (soft delete)
    public bool OnMersoRestoring()
    {
        return true;
    }

    // Called after restoring
    public void OnMersoRestored()
    {
        Console.WriteLine("User restored");
    }
}
```

### Event Flow

**Create:** `OnMersoSaving` → `OnMersoCreating` → INSERT → `OnMersoCreated` → `OnMersoSaved`

**Update:** `OnMersoSaving` → `OnMersoUpdating` → UPDATE → `OnMersoUpdated` → `OnMersoSaved`

**Delete:** `OnMersoDeleting` → DELETE → `OnMersoDeleted`

**Restore:** `OnMersoRestoring` → UPDATE → `OnMersoRestored`

---

## Observers

External event handlers for models.

### Creating an Observer

```csharp
public class UserObserver : MersoObserver<User>
{
    public override bool Creating(User model)
    {
        Console.WriteLine($"Creating: {model.Name}");
        return true; // Return false to cancel
    }

    public override void Created(User model)
    {
        Console.WriteLine($"Created: {model.Id}");
        // Send welcome email, etc.
    }

    public override bool Updating(User model)
    {
        return true;
    }

    public override void Updated(User model)
    {
        // Log changes, etc.
    }

    public override bool Deleting(User model)
    {
        return true;
    }

    public override void Deleted(User model)
    {
        // Cleanup related data, etc.
    }
}
```

### Registering Observers

```csharp
// Register
var observer = new UserObserver();
ObserverManager.Register(observer);

// Unregister
ObserverManager.Unregister(observer);

// Clear all observers for a type
ObserverManager.Clear<User>();

// Clear all observers
ObserverManager.ClearAll();
```

---

## Validation

### Validation Attributes

```csharp
[Table("Users")]
public class User : Model<User>
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; }

    [Required]
    [Email]
    public string Email { get; set; }

    [MinLength(6)]
    public string Password { get; set; }

    [Range(0, 120)]
    public int Age { get; set; }

    [Phone]
    public string Phone { get; set; }

    [Url]
    public string Website { get; set; }

    [Positive]
    public decimal Balance { get; set; }

    [NonNegative]
    public int Stock { get; set; }

    [Pattern(@"^\d{5}$")]
    public string ZipCode { get; set; }
}
```

### Available Validators

| Attribute | Description |
|-----------|-------------|
| `[Required]` | Field is required |
| `[MaxLength(n)]` | Maximum string length |
| `[MinLength(n)]` | Minimum string length |
| `[Email]` | Valid email format |
| `[Phone]` | Valid phone format |
| `[Url]` | Valid URL format |
| `[Range(min, max)]` | Number range |
| `[Positive]` | Positive number |
| `[NonNegative]` | Non-negative number |
| `[Pattern(regex)]` | Regex pattern |

### Using Validation

```csharp
var user = new User { Name = "", Email = "invalid" };

// Validate and get result
var result = user.Validate();
if (!result.IsValid)
{
    foreach (var error in result.AllErrors())
    {
        Console.WriteLine(error);
    }
}

// Check if valid
bool isValid = user.IsValid();

// Validate or throw exception
try
{
    user.ValidateOrFail();
}
catch (ValidationException ex)
{
    Console.WriteLine(ex.Message);
    var errors = ex.ValidationResult.AllErrors();
}
```

---

## Bulk Operations

Efficient batch operations for large datasets.

```csharp
// Bulk Insert
var users = new List<User>();
for (int i = 0; i < 1000; i++)
{
    users.Add(new User { Name = $"User {i}", Email = $"user{i}@test.com" });
}
int inserted = BulkOperations.BulkInsert(users, batchSize: 100);

// Bulk Update
var usersToUpdate = User.All();
foreach (var user in usersToUpdate)
{
    user.IsActive = true;
}
int updated = BulkOperations.BulkUpdate(usersToUpdate);

// Bulk Delete (soft delete if enabled)
var idsToDelete = new List<object> { 1, 2, 3, 4, 5 };
int deleted = BulkOperations.BulkDelete<User>(idsToDelete);

// Bulk Force Delete (permanent)
int forceDeleted = BulkOperations.BulkForceDelete<User>(idsToDelete);
```

### Upsert

Insert new rows and update existing ones in one statement per batch.

```csharp
// Rows are matched on Code: existing ones get the new Name / Price, new ones are inserted
BulkOperations.Upsert(products, uniqueBy: new[] { "Code" });

// Only overwrite Price on existing rows
BulkOperations.Upsert(products, new[] { "Code" }, updateColumns: new[] { "Price" });

// Insert only, keep existing rows as they are
BulkOperations.Upsert(products, new[] { "Code" }, updateColumns: new string[0]);

// One model
Product.Upsert(product, "Code");
```

| Provider | SQL | Requirement |
|----------|-----|-------------|
| SQL Server | `MERGE ... WITH (HOLDLOCK)` | — |
| PostgreSQL, SQLite | `INSERT ... ON CONFLICT (Code) DO UPDATE` | unique index / constraint on exactly the `uniqueBy` columns |
| MySQL, MariaDB | `INSERT ... ON DUPLICATE KEY UPDATE` | a unique key (MySQL matches on any unique key of the table) |

The return value is the provider's affected-row count (MySQL counts an updated row as 2).

---

## Transactions

Ensure data integrity with transactions.

```csharp
// Simple transaction
MersoTransaction.Run(() =>
{
    var user = new User { Name = "John" };
    user.Save();

    var order = new Order { UserId = user.Id };
    order.Save();
});
// Auto-commit on success, auto-rollback on exception

// Transaction with return value
var result = MersoTransaction.Run(() =>
{
    var user = new User { Name = "John" };
    user.Save();
    return user.Id;
});

// Try transaction (returns bool)
bool success = MersoTransaction.TryRun(() =>
{
    // Operations that might fail
    var user = new User { Name = "John" };
    user.Save();
});

if (!success)
{
    Console.WriteLine("Transaction failed and was rolled back");
}

// Async
await MersoTransaction.RunAsync(async () =>
{
    var user = new User { Name = "John" };
    user.Save();
    await SomethingAsync();
});
```

Every model, `QueryBuilder`, `RawQuery` and `BulkOperations` call inside `Run` / `RunAsync` uses the same
connection and transaction, so an exception rolls back all of them. A nested `Run` joins the outer
transaction. Do not run database work in parallel (`Task.WhenAll`) inside one transaction — it shares a
single connection.

On a named connection (see [Multiple Databases](#multiple-databases)) use `MersoTransaction.Run("reports", () => ...)`.
Transactions on different databases nest: inside `Run("reports", ...)` a write to the default database still
belongs to the outer default-database transaction. Each database commits on its own (there is no two-phase commit).

---

## Caching

In-memory caching for improved performance.

### Basic Operations

```csharp
// Set cache
MersoCache.Set("key", "value", TimeSpan.FromMinutes(5));

// Get cache
var value = MersoCache.Get<string>("key");

// Check if exists
bool exists = MersoCache.Has("key");

// Remove from cache
MersoCache.Forget("key");

// Clear all cache
MersoCache.Flush();

// Clear by prefix
MersoCache.FlushByPrefix("user_");
```

### Remember Pattern

```csharp
// Get from cache or execute and cache
var users = MersoCache.Remember("all_users", TimeSpan.FromMinutes(10), () =>
{
    return User.All();
});

// Cache forever
var settings = MersoCache.RememberForever("app_settings", () =>
{
    return LoadSettings();
});
```

### QueryBuilder Integration

```csharp
// Cache query results
var users = User.Query()
    .Where("IsActive", true)
    .Remember(TimeSpan.FromMinutes(5));

// Cache forever
var admins = User.Query()
    .Where("Role", "admin")
    .RememberForever();
```

---

## Global Scopes

Automatic query filters applied to all queries.

### Definition

```csharp
[Table("Products")]
[GlobalScope("IsActive", true)]
[GlobalScope("DeletedAt", null, "IS")]
public class Product : Model<Product>
{
    // Properties...
}
```

The filters are added to every query of the model (`All()`, `Find()`, `Count()`, `Query()...`). Saving, deleting
and refreshing a single loaded record, relations and eager loading do not use them.

### Temporarily Disable

```csharp
// One query
var allProducts = Product.Query().WithoutGlobalScopes().Get();

// A block — only the current request / async flow, other requests keep their filters
using (GlobalScopeManager.WithoutGlobalScopes<Product>())
{
    var everything = Product.All(); // Includes inactive
}

// Manual control (global, every thread)
GlobalScopeManager.DisableScopes<Product>();
var all = Product.All();
GlobalScopeManager.EnableScopes<Product>();
```

---

## Raw Queries

Execute raw SQL when needed.

```csharp
// Select query returning models
var users = RawQuery.Query<User>(
    "SELECT * FROM Users WHERE Age > @age",
    new Dictionary<string, object> { { "age", 18 } }
);

// Select first
var user = RawQuery.QueryFirst<User>(
    "SELECT TOP 1 * FROM Users ORDER BY CreatedAt DESC"
);

// Scalar value
int count = RawQuery.Scalar<int>("SELECT COUNT(*) FROM Users");

// Execute (INSERT, UPDATE, DELETE)
RawQuery.Execute(
    "UPDATE Users SET IsActive = @active WHERE Id = @id",
    new Dictionary<string, object> { { "active", true }, { "id", 1 } }
);

// Get DataTable
var dt = RawQuery.QueryTable("SELECT * FROM Users");
```

---

## Migrations

Version-controlled database schema changes via `Schema` and `Migrator`.

### Creating a Migration

```csharp
using mersolutionCore.ORM.Migration;

public class CreatePostsTable : Migration
{
    public override string Version => "2026_04_15_120000_create_posts";

    public override void Up(Schema schema)
    {
        schema.CreateTable("Posts", table =>
        {
            table.Id();
            table.String("Title", 200).NotNull();
            table.Text("Content");
            table.Integer("AuthorId");
            table.Boolean("IsPublished");
            table.Timestamps();
            table.SoftDeletes();
            table.Foreign("AuthorId", "Users");
        });
    }

    public override void Down(Schema schema)
    {
        schema.DropTableIfExists("Posts");
    }
}
```

### Running Migrations

```csharp
using mersolutionCore.ORM.Migration;
using mersolutionCore.Config;

var runner = new Migrator(DbConfig.CreateConnection);
// or: new MigrationRunner() after a DbContext exists

runner.Add(new CreatePostsTable());
runner.Migrate();      // all pending migrations = one batch
runner.Rollback();     // undo the last batch (what the last Migrate() applied)
runner.Rollback(2);    // undo the last 2 migrations, across batches
runner.Reset();
runner.Refresh();
var status = runner.Status();   // Version, Applied, Batch
```

Each migration and its `__migrations` row run in one transaction, so a failing migration leaves nothing half-applied (MySQL / MariaDB commit DDL implicitly, so there it can). For statements a transaction does not allow (PostgreSQL `CREATE INDEX CONCURRENTLY`, SQL Server full-text indexes) override `public override bool WithinTransaction => false;`. Versions run in ordinal order; two classes with the same version are an error.

---

## Connection Pool

Efficient database connection management.

### Configuration

```csharp
// Configure pool settings
ConnectionPool.Configure(
    minSize: 5,          // Minimum connections
    maxSize: 100,        // Maximum connections
    timeoutSeconds: 30   // Connection timeout
);

// Initialize pool (optional - auto-initialized on first use)
ConnectionPool.Initialize(() => new SqlConnection(connectionString));

// Rent / return
using (var pooled = new PooledConnection(ConnectionPool.GetConnection(() => new SqlConnection(connectionString))))
{
    var cmd = pooled.Connection.CreateCommand();
    // ...
}
```

Each connection string has its own pool; `maxSize` applies per connection string. The ADO.NET providers already pool physical connections, so the models and `DbCommandBase` do not need this helper.

### Monitoring

```csharp
// Get pool status
var status = ConnectionPool.GetStatus();
Console.WriteLine($"Available: {status.AvailableConnections}");
Console.WriteLine($"Total: {status.TotalConnections}");
Console.WriteLine($"Min: {status.MinPoolSize}");
Console.WriteLine($"Max: {status.MaxPoolSize}");
```

### Cleanup

```csharp
// Clear all connections
ConnectionPool.Clear();
```

---

## JSON Columns

Store complex objects as JSON in database columns.

Since 2.1 `[JsonColumn]` converts the property itself: a list, dictionary or class is written as JSON
(System.Text.Json, camelCase) and read back into its type. A `string` property keeps its text as it is, so the
wrapper pattern below still works.

```csharp
[JsonColumn] public List<string> Tags { get; set; } = new List<string>();
[JsonColumn] public Address? ShippingAddress { get; set; }
```

### JSON Wrappers

```csharp
// JsonDictionary - Key-value storage
var metadata = new JsonDictionary();
metadata["theme"] = "dark";
metadata["language"] = "tr";
metadata["notifications"] = true;
string json = metadata.Json; // {"theme":"dark","language":"tr","notifications":true}

// JsonList - Array storage
var tags = new JsonList<string>();
tags.Add("featured");
tags.Add("popular");
string json = tags.Json; // ["featured","popular"]

// JsonValue - Any object
var settings = new JsonValue<UserSettings>(new UserSettings
{
    Theme = "dark",
    Language = "tr"
});
string json = settings.Json;
```

### Model Integration

```csharp
[Table("Products")]
public class Product : Model<Product>
{
    [PrimaryKey(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("Name")]
    public string Name { get; set; }

    // JSON column in database
    [Column("Metadata")]
    [JsonColumn]
    public string MetadataJson { get; set; }

    // Wrapper property (not mapped to database)
    [Ignore]
    public JsonDictionary Metadata
    {
        get
        {
            var dict = new JsonDictionary();
            dict.Json = MetadataJson;
            return dict;
        }
        set => MetadataJson = value.Json;
    }

    // JSON array column
    [Column("Tags")]
    [JsonColumn]
    public string TagsJson { get; set; }

    [Ignore]
    public JsonList<string> Tags
    {
        get
        {
            var list = new JsonList<string>();
            list.Json = TagsJson;
            return list;
        }
        set => TagsJson = value.Json;
    }
}
```

### Usage

```csharp
var product = new Product
{
    Name = "iPhone 15",
    Metadata = new JsonDictionary
    {
        ["color"] = "black",
        ["storage"] = "256GB"
    },
    Tags = new JsonList<string> { "electronics", "phone", "apple" }
};
product.Save();

// Read back
var loaded = Product.Find(product.Id);
Console.WriteLine(loaded.Metadata["color"]); // "black"
Console.WriteLine(loaded.Tags[0]); // "electronics"
```

---

## Async API

Every database call has an async counterpart that takes a `CancellationToken`.

```csharp
var user = await User.FindAsync(5, cancellationToken);
user.Name = "Yeni";
await user.SaveAsync();

var page = await User.Query()
    .Where("IsActive", true)
    .OrderBy("Id")
    .PaginateAsync(pageNumber: 1, pageSize: 20);

var total = await User.Query().Where("Age", ">", 18).CountAsync();
var withOrders = await User.Query().With("Orders").GetAsync();

await User.CreateAsync(new User { Name = "Ali" });
await user.DeleteAsync();

var rows = await RawQuery.QueryAsync<User>("SELECT * FROM Users WHERE Age > @age",
    new Dictionary<string, object> { { "age", 30 } });

await BulkOperations.UpsertAsync(products, new[] { "Code" });

await MersoTransaction.RunAsync(async () =>
{
    await order.SaveAsync();
    await stock.SaveAsync();
});
```

2.1 completes the list: every relation method (`GetRelatedAsync`, `CountAsync`, `CreateAsync`, `AssociateAsync`,
`AttachAsync`, `DetachAsync`, `SyncAsync`, `ToggleAsync`, `UpdateExistingPivotAsync` ...), `FirstOrCreateAsync`,
`FirstOrNewAsync`, `UpdateOrCreateAsync`, `Model.UpsertAsync`, `TruncateAsync`, static `SumAsync` / `AvgAsync` /
`MinAsync` / `MaxAsync`, `PluckAsync`, `ChunkAsync` / `ChunkByIdAsync` and `CursorAsync`.

---

## Multiple Databases

```csharp
// Default database
DbConfig.Configure(mainConnectionString, DbProviderType.SqlServer);
ModelBase.Configure(DbConfig.CreateConnection);

// Additional, named databases
DbConfig.AddConnection("reports", reportsConnectionString, DbProviderType.PostgreSQL);
DbConfig.AddConnection("archive", "Data Source=archive.db", DbProviderType.SQLite);
// or with your own factory: ModelBase.Configure("reports", () => new PostgreSqlCommand(cs));
```

| Need | How |
|------|-----|
| A model always lives in another database | `[Connection("reports")]` on the class |
| One query on another database | `Order.On("archive").Where("Year", 2020).Get()` — loaded models are saved back to `archive` |
| A whole block / request on another database (per tenant) | `using (MersoConnection.Use("tenant_42")) { ... }` or `MersoConnection.Use(() => new SqlServerCommand(tenantCs))` |
| Raw SQL | `RawQuery.Execute(sql, parameters, connectionName: "reports")` |
| Transaction | `MersoTransaction.Run("reports", () => ...)` |

Precedence: `On(name)` → `[Connection]` → `MersoConnection.Use(...)` → default.

---

## Change Tracking & Concurrency

A model loaded from the database remembers its values. `Save()` writes only the columns that changed
(nothing at all when nothing changed), so a column someone else updated in the meantime is not overwritten.

```csharp
var user = User.Find(5);
user.Age = 31;

user.IsDirty();          // true
user.IsDirty("Name");    // false
user.GetDirty();         // { "Age": 31 }
user.GetOriginal("Age"); // 30

user.Save();             // UPDATE Users SET Age = @c0, UpdatedAt = @c1 WHERE Id = @__k0
user.IsClean();          // true
```

A model created with `new` (never loaded) is saved with all columns, as before.

### Optimistic concurrency

```csharp
public class Invoice : Model<Invoice>
{
    public int Id { get; set; }
    public decimal Total { get; set; }

    [RowVersion]
    public int Version { get; set; }   // int or long, starts at 1
}

var a = Invoice.Find(1);   // Version 1
var b = Invoice.Find(1);   // Version 1

a.Total = 100; a.Save();   // ... WHERE Id = 1 AND Version = 1 → Version 2

b.Total = 90;
try { b.Save(); }          // ... AND Version = 1 → 0 rows
catch (DbConcurrencyException) { /* reload and ask the user */ }
```

`Delete()` and `BulkOperations.BulkUpdate` check the version too.

### Quoted identifiers

Table and column names are quoted for the provider (`[Order]`, `` `Order` ``, `"order"`), so reserved words work as
column names. PostgreSQL names are quoted in lower case — the same name an unquoted `CREATE TABLE` produced.
`ModelBase.QuoteIdentifiers = false` turns quoting off.

---

## SQL Log

```csharp
MersoLog.QueryExecuted += e =>
{
    if (!e.Succeeded)
        logger.LogError(e.Exception, "SQL failed: {Sql}", e.Sql);
    else if (e.Duration.TotalMilliseconds > 500)
        logger.LogWarning("Slow SQL ({Ms} ms): {Sql}", e.Duration.TotalMilliseconds, e.Sql);
};

// Parameter values are hidden (null) by default — they may contain passwords or personal data
MersoLog.IncludeParameterValues = true; // development only
```

Each entry has `Sql`, `Provider`, `StartedAtUtc`, `Duration`, `Parameters` (name / value) and `Exception`.

---

## Composite Keys

Put `[PrimaryKey]` on several properties. Composite keys are never auto-increment; `Order` sets the key order.

```csharp
[Table("OrderLines")]
public class OrderLine : Model<OrderLine>
{
    [PrimaryKey(AutoIncrement = false, Order = 1)] public long OrderId { get; set; }
    [PrimaryKey(AutoIncrement = false, Order = 2)] public string Sku { get; set; }
    public int Qty { get; set; }
}

OrderLine.Create(new OrderLine { OrderId = 1, Sku = "A", Qty = 2 });

var line = OrderLine.Find(1L, "A");                 // values in key order
line.Qty = 5;
line.Save();                                         // UPDATE ... WHERE OrderId = @__k0 AND Sku = @__k1
line.Delete();

OrderLine.FindMany(new object[] { 1L, "A" }, new object[] { 2L, "B" });
OrderLine.Destroy(1L, "A");                          // one key
OrderLine.Query().WhereKey(1L, "A").Exists();
```

`new OrderLine { OrderId = 1, Sku = "A" }.Save()` updates the row when the key already exists (no duplicate insert).
The table: `t.BigInteger("OrderId").NotNull(); t.String("Sku", 50).NotNull(); t.Primary("OrderId", "Sku");` —
`DbContext.EnsureCreated()` creates composite keys as well.

### Relations on a composite key

`HasOne`, `HasMany`, `BelongsTo`, their eager loading and `[Touches]` work with several key columns. Without
arguments the model's own key is used and the foreign key columns have the same names; otherwise list the columns
in key order:

```csharp
public class OrderLine : Model<OrderLine>                    // key (OrderId, Sku)
{
    public HasMany<OrderLine, LineTax> Taxes() => HasMany<LineTax>();               // LineTax.OrderId + LineTax.Sku
    [HasMany(typeof(LineTax))] public List<LineTax> TaxList { get; set; }            // eager: With("TaxList")
}

[Touches(typeof(OrderLine), "OrderId, Sku")]                 // saving a tax sets the line's UpdatedAt
public class LineTax : Model<LineTax>
{
    public int Id { get; set; }
    public long OrderId { get; set; }
    public string Sku { get; set; }

    public BelongsTo<LineTax, OrderLine> Line() => BelongsTo<OrderLine>();
    // other column names: BelongsTo<OrderLine>("LineOrderId, LineSku")
}
```

`BelongsToMany`, the `Through` relations and polymorphic relations still need a single-column key (a polymorphic
`{Name}Id` column holds one value); they say so with a `NotSupportedException`.

---

## Hidden Fields & Mass Assignment

```csharp
public class User : Model<User>
{
    public int Id { get; set; }
    [Fillable] public string Name { get; set; }
    [Fillable] public string Email { get; set; }
    [Hidden]   public string PasswordHash { get; set; }   // left out of ToDict / ToJson / MersoJson
    [Guarded]  public bool IsAdmin { get; set; }          // never set by Fill / Create(dictionary)
}

var user = User.Create(Request.Form.ToDictionary(...));   // only Name and Email are taken
user.ToJson();                                            // no PasswordHash
user.MakeVisible("PasswordHash").ToDict();                // shown for this instance
user.ForceFill(trustedValues);                            // ignores [Fillable] / [Guarded]
ModelBase.StrictMassAssignment = true;                    // throw MassAssignmentException instead of skipping
```

* Without any `[Fillable]` property every property except `[Guarded]` ones can be filled (1.x behaviour).
* `FirstOrCreate` / `FirstOrNew` / `UpdateOrCreate` always set the search values; the other values follow the rules.
* Relation `Create(...)` always sets the foreign key, even when it is `[Guarded]`.
* Models returned directly from ASP.NET Core: register the hidden-property rules once:

```csharp
builder.Services.ConfigureHttpJsonOptions(o => MersoJson.Configure(o.SerializerOptions));       // minimal APIs
builder.Services.AddControllers().AddJsonOptions(o => MersoJson.Configure(o.JsonSerializerOptions));
var json = JsonSerializer.Serialize(user, MersoJson.Options);
```

---

## Advanced Queries

```csharp
// Groups
User.Query().Where(q => q.Where("Age", ">", 40).OrWhere("Role", "admin")).Where("IsActive", true);
User.Query().WhereNot(q => q.Where("Status", "banned"));

// Compare two columns
User.Query().WhereColumn("UpdatedAt", ">", "CreatedAt");

// Subqueries (correlate with WhereColumn on table-qualified names)
User.Query().WhereExists(Order.Query().WhereColumn("Orders.UserId", "Users.Id"));
User.Query().WhereNotExists(Order.Query().WhereColumn("Orders.UserId", "Users.Id"));
User.Query().WhereIn("Id", Order.Query().Select("UserId").Where("Total", ">", 1000));

// UNION / UNION ALL of the same model: OrderBy / Take / Paginate apply to the combined rows
var page = User.Query().Where("Role", "admin")
    .Union(User.Query().Where("Age", ">", 60))
    .OrderBy("Name")
    .Paginate(1, 20);

// Raw pieces with ? placeholders (values are parameters; ? inside quotes is left alone)
var rows = User.Query()
    .Select("Id", "Name")
    .SelectRaw("Age + ? AS NextAge", 1)
    .WhereRaw("Age BETWEEN ? AND ?", 18, 65)
    .OrderByRaw("CASE WHEN Role = ? THEN 0 ELSE 1 END, Name", "admin")
    .Get();
int next = rows[0].GetAttribute<int>("NextAge");   // columns without a property

var stats = Order.Query().Select("UserId").SelectRaw("SUM(Total) AS Sum").GroupBy("UserId").HavingRaw("SUM(Total) > ?", 500).Get();
```

`WhereRaw("sql")` without values works as before. With PostgreSQL JSON operators that contain `?` pass no bindings.

---

## More Relationships

```csharp
public class Country : Model<Country>
{
    public int Id { get; set; }

    // Country → Users (users.CountryId) → Posts (posts.UserId)
    public HasManyThrough<Country, User, Post> Posts() => HasManyThrough<Post, User>("CountryId", "UserId");
    public HasOneThrough<Country, User, Post> LatestPost() => HasOneThrough<Post, User>("CountryId", "UserId");
}

// Polymorphic: one Comments table for posts and videos (CommentableType + CommentableId)
public class Post : Model<Post>
{
    public int Id { get; set; }
    [UpdatedAt] public DateTime? UpdatedAt { get; set; }

    public MorphMany<Post, Comment> Comments() => MorphMany<Comment>("Commentable");
    [MorphMany(typeof(Comment), "Commentable")] public List<Comment> CommentList { get; set; }   // eager: With("CommentList")
}

[Touches(typeof(Post), "PostId")]                    // saving / deleting a comment sets the post's UpdatedAt
public class Comment : Model<Comment>
{
    public int Id { get; set; }
    public int? PostId { get; set; }
    public string CommentableType { get; set; }
    public long? CommentableId { get; set; }
    public MorphTo<Comment> Commentable() => MorphTo("Commentable");
    [MorphTo("Commentable")] public ModelBase? Owner { get; set; }    // eager: With("Owner")
}

post.Comments().Create(new Dictionary<string, object?> { { "Body", "Nice" } });
ModelBase owner = comment.Commentable().GetRelated();          // Post or Video
var asPost = comment.Commentable().GetRelated<Post>();
var comments = Comment.Query().With("Owner").Get();            // one query per owner type, not one per comment
Comment.Query().With("Owner.CommentList").Get();               // nested: continues on each owner type
MorphMap.Register<Post>("post");                               // stored type name (default: class name)
post.Touch();                                                  // UpdatedAt = now

// Pivot columns
public BelongsToMany<User, Role> Roles() => BelongsToMany<Role>("RoleUser", "UserId", "RoleId");

user.Roles().Attach(roleId, new Dictionary<string, object?> { { "ExpiresAt", DateTime.UtcNow.AddDays(30) } });
var roles = user.Roles().WithPivot("ExpiresAt").GetRelated();
var expires = roles[0].GetPivot()["ExpiresAt"];
user.Roles().WherePivot("Active", true).GetRelated();
user.Roles().UpdateExistingPivot(roleId, new Dictionary<string, object?> { { "Active", false } });
user.Roles().Sync(new Dictionary<object, Dictionary<string, object?>> { { 1, new() { { "Active", true } } } });
```

---

## Schema Changes & Migration Batches

```csharp
public override void Up(Schema schema)
{
    // Change type / NULL / default (the column gets exactly this definition)
    schema.ChangeColumn("Users", t => t.String("Name", 200).NotNull());
    schema.ChangeColumn("Users", "Score", "DECIMAL(12,2)", nullable: true, defaultValue: "0");

    // Foreign keys (named fk_{table}_{column})
    schema.AddForeign("Orders", "UserId", "Users", "Id", onDelete: "CASCADE");
    schema.DropForeign("Orders", "UserId");          // looks the name up: also keys created without a name
    schema.DropForeignByName("Orders", "FK_Custom");
}
```

* SQL Server drops / re-creates the default constraint, MySQL / MariaDB use `MODIFY COLUMN`, PostgreSQL
  `ALTER COLUMN ... TYPE ... USING col::type`.
* SQLite cannot alter columns or constraints: the table is rebuilt (rows, indexes, unique constraints and foreign
  keys are kept). When other tables reference the rebuilt table and foreign keys are on, run the migration with
  `public override bool WithinTransaction => false;` (SQLite cannot switch foreign keys off inside a transaction).
* `Migrate()` stores a batch number; `Rollback()` undoes the last batch, `Rollback(n)` the last n migrations.
  A `__migrations` table from 1.x gets the `Batch` column automatically (old rows = batch 1).

---

## Retry of Transient Errors

Commands outside a transaction are retried automatically (default: 2 retries, 100 ms doubling, ±20 % jitter):

| Error | Retried |
|-------|---------|
| Connection could not be opened (refused, too many connections, server starting) | always |
| Deadlock, serialization failure, lock timeout, SQLite busy (statement rolled back) | always |
| Connection dropped while the command ran | only `SELECT` — a write may already be committed |

```csharp
MersoRetry.MaxRetries = 3;                      // 0 = off
MersoRetry.BaseDelay = TimeSpan.FromMilliseconds(200);
MersoRetry.IsTransient = ex => ex is MyGatewayException;   // extra rule
MersoRetry.Retrying += (ex, attempt, delay) => logger.LogWarning(ex, "Retry {Attempt}", attempt);

// Inside a transaction single statements are not retried; re-run the whole transaction instead:
MersoTransaction.Run(() => { order.Save(); stock.Decrement("Qty"); }, attempts: 3);
await MersoTransaction.RunAsync(async () => { ... }, attempts: 3);
```

The transaction callback must only touch the database (an e-mail sent inside it would be sent again). A connection
lost during `COMMIT` is not retried because the commit may have succeeded.

---

## Distributed Cache

The cache (`Cache`, `QCache`, `MersoCache`, `QueryBuilder.Remember`) lives in process memory by default. With
several servers use a shared store so they see the same entries and invalidations:

```csharp
// Any IDistributedCache (Redis, SQL Server, ...)
builder.Services.AddStackExchangeRedisCache(o => o.Configuration = "localhost:6379");
builder.Services.AddMersoCore(o => o.UsePostgreSql(cs).UseDistributedCache());

// Without DI
Cache.UseStore(new DistributedCacheStore(redisDistributedCache), keyPrefix: "shop:");
Cache.UseStore(new MyStore());      // or your own ICacheStore (ICacheCounterStore for atomic Increment)
Cache.UseMemory();                  // back to process memory
```

* Values are stored as JSON; models from `Remember()` come back as loaded models (`Save()` writes changes only).
* `ForgetByTag`, `QCache.ForgetTable("Users")`, `ForgetByPrefix` and `Flush()` work across servers (version keys).
* A shared store cannot list its keys, so `ForgetByPrefix("user:")` registers the prefix: the **first** call for
  a new prefix clears the whole cache once, later calls only drop that prefix. Use a few fixed prefixes; for
  per-record groups (`"user:42:"`) use tags. It returns -1 (the number of entries is not known).

---

## Dependency Injection

```csharp
builder.Services.AddMersoCore(o => o
    .UseSqlServer(builder.Configuration.GetConnectionString("Main"))     // UseMySql / UseMariaDb / UsePostgreSql / UseSqlite
    .AddConnection("reports", reportsCs, DbProviderType.PostgreSQL)
    .UseDistributedCache()                                               // needs a registered IDistributedCache
    .LogSql(LogLevel.Debug)                                              // ILogger category "mersolutionCore.Sql"
    .UseRetry(3));

builder.Services.AddMersoDbContext<AppDbContext>();                      // scoped

public class OrdersController(DbCommandBase db, AppDbContext context) { ... }   // DbCommandBase = default database
```

Databases and retry settings apply at once (models work before the host starts); IDistributedCache and ILogger are
wired when the host starts. Console apps without a host: `serviceProvider.UseMersoCore();`.

---

## Nullable Annotations & Package

* The package is annotated for nullable reference types: `Find`, `First`, `FindAsync`, `Fresh`, `GetRelated` of
  single relations, `RawQuery.QueryFirst` / `Scalar` return `T?`; dictionary values are `object?`.
* IntelliSense documentation (XML) is in the package; Source Link and a symbol package (`.snupkg`) let you step into
  the library while debugging.
* Release notes: [CHANGELOG.md](CHANGELOG.md).

---

## DateOnly & TimeOnly

`DateOnly` and `TimeOnly` (.NET 6+) are columns like `DateTime` / `TimeSpan` (before 2.1 they were skipped
silently). `EnsureCreated` creates `DATE` / `TIME` columns; the .NET Standard 2.0 build recognises the types by
name, so .NET 6 / 7 apps get them too.

```csharp
public class Holiday : Model<Holiday>
{
    public int Id { get; set; }
    public DateOnly Day { get; set; }
    public TimeOnly? OpensAt { get; set; }
}

var thisYear = Holiday.Query().Where("Day", ">=", new DateOnly(2026, 1, 1)).Get();
```

---

## Streaming & Chunks

Models are read straight from the data reader (no DataTable). For large results stream them one by one:

```csharp
foreach (var user in User.Query().Where("IsActive", true).Cursor())      // one row in memory at a time
    Export(user);

await foreach (var user in User.Query().OrderBy("Id").CursorAsync(cancellationToken))
    await ExportAsync(user);

await User.Query().ChunkAsync(500, async users => await SendAsync(users));
await User.Query().ChunkByIdAsync(500, async users => await SendAsync(users));   // safe while updating rows
```

`Cursor()` keeps the reader open while you iterate: there is no eager loading, and inside a transaction no other
query may run before the loop ends.

---

## Conditional Queries & Row Locks

```csharp
// Apply a part only when needed (When<TValue> skips null, "" and false)
var users = User.Query()
    .When(search, (q, s) => q.WhereLike("Name", $"%{s}%"))
    .When(onlyActive, q => q.Where("IsActive", true), q => q.OrderByDesc("CreatedAt"))
    .Unless(isAdmin, q => q.Where("TenantId", tenantId))
    .Get();

// Reusable parts ("scopes"): a method or an extension method
public static class UserScopes
{
    public static void Active(QueryBuilder<User> q) => q.Where("IsActive", true);
    public static QueryBuilder<User> Adults(this QueryBuilder<User> q) => q.Where("Age", ">=", 18);
}
User.Query().Apply(UserScopes.Active).Adults().Get();

// Row locks until the transaction ends
MersoTransaction.Run(() =>
{
    var stock = Stock.Query().Where("Sku", sku).LockForUpdate().First();   // FOR UPDATE / UPDLOCK
    stock!.Qty -= 1;
    stock.Save();
});
Order.Query().Where("UserId", 5).SharedLock().Get();                      // FOR SHARE / HOLDLOCK
```

SQLite has no row locks (its write transaction locks the file); the lock is left out there.

---

## Relation Queries & Aggregates

Filter and count by relations declared as `[HasMany]` / `[HasOne]` / `[BelongsTo]` / `[MorphMany]` / `[MorphOne]`
properties (soft-deleted related rows and the related model's global scopes are respected):

```csharp
User.Query().WhereHas("Orders").Get();                                          // at least one order
User.Query().WhereHas<Order>("Orders", q => q.Where("Total", ">", 100)).Get();
User.Query().WhereDoesntHave("Orders").Get();
User.Query().Has("Orders", ">=", 3).Get();
User.Query().WhereRelation("Orders", "Status", "paid").Get();
User.Query().WhereHas<Order>("Orders", q => q.WhereHas("Lines")).Get();         // nested
Category.Query().WhereHas("Children").Get();                                    // self relations work too

// Related counts / sums as extra columns
public class User : Model<User>
{
    [HasMany(typeof(Order), "UserId")] public List<Order> Orders { get; set; } = new List<Order>();
    [Computed] public int OrdersCount { get; set; }          // filled by queries, never written
}

var users = User.Query()
    .WithCount("Orders")                                     // → OrdersCount
    .WithCount<Order>("Orders", q => q.Where("Status", "paid"), "PaidOrders")
    .WithSum("Orders", "Total")                              // → OrdersSumTotal (WithAvg / WithMin / WithMax)
    .WithExists("Orders")                                    // → OrdersExists (1 / 0)
    .Get();
int paid = users[0].GetAttribute<int>("PaidOrders");        // columns without a property

// Eager loading with conditions / order (for a nested path: the last relation)
var withPaid = User.Query().With<Order>("Orders", q => q.Where("Status", "paid").OrderByDesc("Id")).Get();
```

---

## Simple & Cursor Pagination

```csharp
// No COUNT(*): reads one row more to know whether a next page exists
SimplePaginatedResult<User> page = User.Query().OrderBy("Id").SimplePaginate(pageNumber: 2, pageSize: 20);
// page.Items, page.HasNextPage, page.HasPreviousPage

// Keyset ("cursor") paging: continues after the last row instead of skipping rows — fast on large tables
var first = User.Query().OrderByDesc("CreatedAt").CursorPaginate(20);
var next  = User.Query().OrderByDesc("CreatedAt").CursorPaginate(20, first.NextCursor);
var back  = User.Query().OrderByDesc("CreatedAt").CursorPaginate(20, next.PreviousCursor);
```

The cursor is an opaque URL-safe string. Order columns get the primary key as a tie-breaker; they must not be NULL.
`OrderByRaw` / `InRandomOrder` cannot be used with `CursorPaginate`. Both have `...Async` versions.

---

## Value Converters

```csharp
public class Customer : Model<Customer>
{
    public int Id { get; set; }

    [EnumAsString] public Status Status { get; set; }               // "Active" instead of 1 (numbers still read)
    [JsonColumn]   public List<string> Tags { get; set; } = new();  // JSON text
    [Encrypted]    public string? TaxNumber { get; set; }           // AES-256 + HMAC-SHA256, random IV
    [Converter(typeof(MoneyConverter))] public Money Balance { get; set; }
}

// Once at startup (keep the key out of source control)
EncryptedStringConverter.Key = configuration["Merso:EncryptionKey"];

public class MoneyConverter : IValueConverter
{
    public object? ToDatabase(object? value) => ((Money?)value)?.Cents;
    public object? FromDatabase(object? value, Type propertyType) => new Money(Convert.ToInt64(value));
}
```

* `Where("Status", Status.Active)` / `WhereIn` convert the value the same way; change tracking compares the stored form.
* Encrypted columns cannot be searched with `Where` (every value has its own IV).
* `EnsureCreated` makes enum-name columns `VARCHAR(50)` and other converted columns text.

---

## Bulk Copy

```csharp
int rows = BulkOperations.BulkCopy(products);                 // or await BulkOperations.BulkCopyAsync(products)
```

| Database | Method |
|----------|--------|
| SQL Server | `SqlBulkCopy` |
| PostgreSQL | `COPY ... FROM STDIN` (CSV) |
| MySQL / MariaDB | `MySqlBulkCopy` — needs `AllowLoadLocalInfile=true` in the connection string and `local_infile` on the server |
| SQLite, or when the server refuses | batched `INSERT` (same result, slower) |

Timestamps, Guid keys, row versions and converters are applied; generated ids are **not** read back (save the
models one by one when you need them). `BulkCopy` joins a `MersoTransaction`.

---

## Lambda Queries

Type-safe conditions and columns; values (also captured variables) are always parameters:

```csharp
var minAge = 18;
var ids = new List<int> { 1, 2, 3 };

User.Query()
    .Where(u => u.Age >= minAge && u.Name.StartsWith("A"))
    .OrWhere(u => ids.Contains(u.Id))                                  // IN (...)
    .WhereNot(u => u.Email == null)
    .OrderBy(u => u.Name)
    .Select(u => u.Id, u => u.Name)
    .Get();

List<string> emails = User.Query().Where(u => u.IsActive).Pluck(u => u.Email);
var adults = User.Where(u => u.Age >= 18).Get();                      // static shortcut
```

Supported: `== != < > <= >=`, `&& || !`, null checks, bool properties, enums, string `StartsWith` / `EndsWith` /
`Contains` / `Equals` (LIKE with escaped wildcards), `string.IsNullOrEmpty`, `list.Contains(u.Prop)`.
Other expressions throw `NotSupportedException` with the part that could not be translated.

---

## Read Replicas

```csharp
DbConfig.Configure(primaryConnectionString, DbProviderType.PostgreSQL);
DbConfig.AddReadReplica(replica1ConnectionString);
DbConfig.AddReadReplica(replica2ConnectionString);
DbConfig.AddReadReplica(reportsReplicaConnectionString, "reports");    // replica of a named connection

// or with DI
builder.Services.AddMersoCore(o => o.UsePostgreSql(cs).AddReadReplica(replicaCs));

// Read your own writes inside one web request
app.Use(async (context, next) => { using (ReadReplicas.BeginScope()) await next(); });

var fresh = Order.Query().OnPrimary().Where("Id", id).First();         // this query always on the primary
```

* Reads of models, query builders, relations and eager loading go to the replicas (round robin).
* The primary is used for writes, everything inside a `MersoTransaction`, `LockForUpdate` / `SharedLock`,
  `Refresh` / `Fresh`, `FirstOrCreate` / `UpdateOrCreate`, `OnPrimary()` and `RawQuery`.
* Inside `ReadReplicas.BeginScope()` every read after the first write uses the primary, so a just-saved row is
  read back even when replication lags.

---

## OpenTelemetry

Every SQL command is an `Activity` of the source `mersolutionCore` with the database semantic convention tags
(`db.system.name`, `db.operation.name`, `db.query.text`, `db.namespace`, `server.address`, `error.type`):

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource(MersoTelemetry.SourceName).AddAspNetCoreInstrumentation().AddOtlpExporter());

MersoTelemetry.IncludeQueryText = false;    // leave the SQL text out (values are never in it)
```

Without a listener nothing is recorded and nothing costs time.

---

## Scaffolding Models (mersocore tool)

The `mersocore` .NET tool writes one model class per table of an existing database:

```bash
dotnet tool install -g mersolutionCore.Cli

mersocore scaffold --provider postgresql --connection "Host=localhost;Database=shop;Username=app;Password=..." --namespace Shop.Models
mersocore scaffold --provider sqlite --connection app.db --output Data/Models --strip-prefix tbl_
```

| Option | Meaning |
|--------|---------|
| `--provider` | `sqlserver`, `mysql`, `mariadb`, `postgresql` or `sqlite` |
| `--connection` | Connection string (SQLite: a file path is enough) |
| `--output <dir>` | Folder of the `.cs` files (default `Models`) |
| `--namespace <name>` | Namespace of the classes (default `Models`) |
| `--tables a,b` | Only these tables |
| `--strip-prefix <text>` | Remove a table prefix from class names (`tbl_users` → `User`) |
| `--date-only` | `DATE` / `TIME` columns as `DateOnly` / `TimeOnly` (default `DateTime` / `TimeSpan`) |
| `--no-nullable` | No nullable reference annotations (C# 7.3 / .NET Framework projects) |
| `--force` | Overwrite existing files |

What it generates: `[Table]`, `[Column]` (only when the name differs from the property), `[PrimaryKey]` (composite
keys with `Order`), lengths of text columns, `[CreatedAt]` / `[UpdatedAt]` / `[SoftDelete]` for `created_at` /
`updated_at` / `deleted_at`, and from the foreign keys `[BelongsTo]` on the child and `[HasMany]` on the parent
(self relations and composite foreign keys too):

```csharp
[Table("order_lines")]
public class OrderLine : Model<OrderLine>
{
    [PrimaryKey(AutoIncrement = false, Order = 1), Column("order_id")]
    public int OrderId { get; set; }

    [PrimaryKey(AutoIncrement = false, Order = 2), Column("line_no")]
    public int LineNo { get; set; }

    [Column(Length = 20)]
    public string Sku { get; set; } = string.Empty;

    [BelongsTo(typeof(Order), "OrderId")]
    public Order? Order { get; set; }
}
```

The classes are a starting point: rename properties, add validation, converters or methods freely (re-running
with `--force` overwrites them).

---

## Examples

Complete examples are on the documentation site:

| Page | Description |
|------|-------------|
| [CRUD Operations](https://mersocore.com/docs/?doc=example-crud) | Basic CRUD operations |
| [ORM Queries](https://mersocore.com/docs/?doc=example-orm) | Find methods, QueryBuilder, aggregates |
| [SQL Queries](https://mersocore.com/docs/?doc=example-sql) | Raw SQL and command classes |
| [Relations Examples](https://mersocore.com/docs/?doc=example-relations) | Relationships and eager loading |
| [Domain Models Example](https://mersocore.com/docs/?doc=example-erp) | A larger model set |

---

## License

MIT License - See LICENSE file for details.

---

## Support

For issues and feature requests, please contact the Merso Team.
