# mersolutionCore ORM Documentation

**Version:** 1.0.0  
**Target Framework:** .NET Standard 2.0 / .NET Framework 4.8.1+  
**Author:** Merso Team  
**Last Updated:** January 2026

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

---

## Getting Started

### Installation

Add reference to `mersolutionCore` in your project.

### Configuration

```csharp
using mersolutionCore.ORM;

// Configure database connection
DbConfig.ConfigureSqlServer("Server=localhost;Database=MyDb;Trusted_Connection=True;");

// Create DbContext
public class AppDbContext : DbContext
{
    static AppDbContext()
    {
        DbConfig.ConfigureSqlServer("your_connection_string");
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
// Find by ID
var user = User.Find(1);

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
```

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
var result = User.Query().Paginate(page: 1, perPage: 10);
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

```csharp
// HasMany
var orders = user.HasMany<User, Order>("UserId");

// BelongsTo
var owner = order.BelongsTo<Order, User>("UserId");

// HasOne
var profile = user.HasOne<User, Profile>("UserId");
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
```

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

### Temporarily Disable

```csharp
// Disable for a block
using (GlobalScopeManager.WithoutGlobalScopes<Product>())
{
    var allProducts = Product.All(); // Includes inactive
}

// Manual control
GlobalScopeManager.DisableScopes<Product>();
var allProducts = Product.All();
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

Version-controlled database schema changes.

### Creating a Migration

```csharp
public class CreatePostsTable : Migration
{
    public override void Up()
    {
        CreateTable("Posts")
            .Id()                                    // INT IDENTITY PRIMARY KEY
            .String("Title", 200, nullable: false)   // NVARCHAR(200) NOT NULL
            .Text("Content")                         // NVARCHAR(MAX)
            .Int("AuthorId")                         // INT
            .Bool("IsPublished", defaultValue: false)// BIT DEFAULT 0
            .Decimal("Price", 18, 2)                 // DECIMAL(18,2)
            .DateTime("PublishedAt")                 // DATETIME2
            .Timestamps()                            // CreatedAt, UpdatedAt
            .SoftDeletes()                           // DeletedAt
            .ForeignKey("AuthorId", "Users")         // FK constraint
            .Index("Title")                          // Index
            .Build();
    }

    public override void Down()
    {
        DropTable("Posts");
    }
}

public class AddPhoneToUsers : Migration
{
    public override void Up()
    {
        AlterTable("Users")
            .String("Phone", 20)
            .Build();
    }

    public override void Down()
    {
        AlterTable("Users")
            .DropColumn("Phone")
            .Build();
    }
}
```

### Running Migrations

```csharp
var runner = new MigrationRunner();

// Run all pending migrations
runner.Migrate();

// Rollback last batch
runner.Rollback();

// Rollback all migrations
runner.Reset();

// Reset and re-run all
runner.Refresh();

// Show migration status
runner.Status();
```

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
```

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

## Examples

All examples are available in the `testCore/Examples` folder:

| File | Description |
|------|-------------|
| `BasicCRUD.cs` | Basic CRUD operations |
| `FindMethodsExample.cs` | Find methods |
| `ModelMethodsExample.cs` | Model instance methods |
| `AggregateExample.cs` | Aggregate functions |
| `QueryBuilderExamples.cs` | QueryBuilder usage |
| `SoftDeleteExample.cs` | Soft delete operations |
| `IncrementDecrementExample.cs` | Increment/Decrement |
| `MersoEventsExample.cs` | MersoEvents usage |
| `BulkOperationsExample.cs` | Bulk operations |
| `TransactionExample.cs` | Transaction usage |
| `ValidationExample.cs` | Validation system |
| `CacheExample.cs` | Caching |
| `GlobalScopeExample.cs` | Global scopes |
| `RelationshipsExample.cs` | Relationships |
| `RawQueryExample.cs` | Raw SQL queries |
| `MigrationExample.cs` | Migrations |
| `ObserverExample.cs` | Observers |
| `ConnectionPoolExample.cs` | Connection pooling |
| `JsonColumnExample.cs` | JSON columns |

Run all examples:

```csharp
using testCore.Examples;

ExampleRunner.RunAll();
```

---

## License

MIT License - See LICENSE file for details.

---

## Support

For issues and feature requests, please contact the Merso Team.
