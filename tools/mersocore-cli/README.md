# mersocore

Command line tool for [mersolutionCore](https://www.nuget.org/packages/mersolutionCore): writes one model class per
table of an existing **SQL Server, MySQL, MariaDB, PostgreSQL or SQLite** database.

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
| `--date-only` | `DATE` / `TIME` columns as `DateOnly` / `TimeOnly` |
| `--no-nullable` | No nullable reference annotations (C# 7.3 / .NET Framework projects) |
| `--force` | Overwrite existing files |

The classes get `[Table]`, `[Column]`, `[PrimaryKey]` (composite keys too), text lengths, `[CreatedAt]` /
`[UpdatedAt]` / `[SoftDelete]`, and `[BelongsTo]` / `[HasMany]` navigation properties from the foreign keys.

Documentation: [mersocore.com/docs](https://mersocore.com/docs/) · MIT licensed.
