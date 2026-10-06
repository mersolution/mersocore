using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using mersolutionCore.Command.Abstractions;

namespace mersolutionCore.ORM.Migration
{
    /// <summary>
    /// Schema builder for database migrations
    /// </summary>
    public class Schema
    {
        private readonly DbCommandBase _db;
        private readonly DbProviderType _providerType;

        public Schema(DbCommandBase db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _providerType = db.ProviderType;
        }

        #region Table Operations

        /// <summary>
        /// Create a new table
        /// </summary>
        public void CreateTable(string tableName, Action<TableBuilder> columns)
        {
            var builder = new TableBuilder(tableName, _providerType);
            columns(builder);
            var sql = builder.Build();
            _db.RunExecute(sql);

            // Index() / Unique() definitions are separate statements
            foreach (var indexSql in builder.GetIndexStatements())
            {
                _db.RunExecute(indexSql);
            }
        }

        /// <summary>
        /// Drop a table if exists
        /// </summary>
        public void DropTableIfExists(string tableName)
        {
            string sql;
            switch (_providerType)
            {
                case DbProviderType.SqlServer:
                    sql = $"IF OBJECT_ID(N'{tableName.Replace("'", "''")}', 'U') IS NOT NULL DROP TABLE {T(tableName)}";
                    break;
                case DbProviderType.MySQL:
                case DbProviderType.MariaDB:
                case DbProviderType.SQLite:
                case DbProviderType.PostgreSQL:
                default:
                    sql = $"DROP TABLE IF EXISTS {T(tableName)}";
                    break;
            }
            _db.RunExecute(sql);
        }

        /// <summary>
        /// Drop a table
        /// </summary>
        public void DropTable(string tableName)
        {
            _db.RunExecute($"DROP TABLE {T(tableName)}");
        }

        /// <summary>
        /// Rename a table
        /// </summary>
        public void RenameTable(string oldName, string newName)
        {
            string sql;
            switch (_providerType)
            {
                case DbProviderType.SqlServer:
                    sql = $"EXEC sp_rename '{oldName.Replace("'", "''")}', '{newName.Replace("'", "''")}'";
                    break;
                case DbProviderType.MySQL:
                case DbProviderType.MariaDB:
                    sql = $"RENAME TABLE {T(oldName)} TO {T(newName)}";
                    break;
                case DbProviderType.PostgreSQL:
                case DbProviderType.SQLite:
                default:
                    sql = $"ALTER TABLE {T(oldName)} RENAME TO {C(newName)}";
                    break;
            }
            _db.RunExecute(sql);
        }

        // Quoted table / column names for this provider
        private string T(string tableName) => mersolutionCore.ORM.SqlDialect.Table(_providerType, tableName);
        private string C(string columnName) => mersolutionCore.ORM.SqlDialect.Column(_providerType, columnName);
        private string Catalog(string name) => mersolutionCore.ORM.SqlDialect.CatalogName(_providerType, name);

        /// <summary>
        /// Check if table exists
        /// </summary>
        public bool HasTable(string tableName)
        {
            SplitName(tableName, out var schema, out var name);
            _db.ParametersAdd("@table", Catalog(name));

            string sql;
            switch (_providerType)
            {
                case DbProviderType.SqlServer:
                    sql = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @table AND TABLE_SCHEMA = " + SchemaExpression(schema);
                    break;
                case DbProviderType.MySQL:
                case DbProviderType.MariaDB:
                    sql = "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = @table AND table_schema = " + SchemaExpression(schema);
                    break;
                case DbProviderType.PostgreSQL:
                    sql = "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = @table AND table_schema = " + SchemaExpression(schema);
                    break;
                case DbProviderType.SQLite:
                default:
                    sql = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name = @table";
                    break;
            }
            return _db.RunToInt32Scaler(sql) > 0;
        }

        // "dbo.Users" → schema "dbo", name "Users"
        private static void SplitName(string qualified, out string? schema, out string name)
        {
            var dot = qualified.LastIndexOf('.');
            schema = dot > 0 ? qualified.Substring(0, dot) : null;
            name = dot > 0 ? qualified.Substring(dot + 1) : qualified;
        }

        // Explicit schema → parameter; otherwise the connection's current schema / database
        private string SchemaExpression(string? schema)
        {
            if (!string.IsNullOrEmpty(schema))
            {
                _db.ParametersAdd("@schema", Catalog(schema!));
                return "@schema";
            }

            switch (_providerType)
            {
                case DbProviderType.SqlServer:
                    return "SCHEMA_NAME()";
                case DbProviderType.PostgreSQL:
                    return "current_schema()";
                default:
                    return "DATABASE()";
            }
        }

        #endregion

        #region Column Operations

        /// <summary>
        /// Add a column to existing table
        /// </summary>
        public void AddColumn(string tableName, string columnName, string columnType, bool nullable = true, string? defaultValue = null)
        {
            var sb = new StringBuilder();
            sb.Append($"ALTER TABLE {T(tableName)} ADD {C(columnName)} {columnType}");

            if (!nullable)
                sb.Append(" NOT NULL");

            if (defaultValue != null)
                sb.Append($" DEFAULT {defaultValue}");

            _db.RunExecute(sb.ToString());
        }

        /// <summary>
        /// Drop a column from table
        /// </summary>
        public void DropColumn(string tableName, string columnName)
        {
            // SQLite supports DROP COLUMN since 3.35 (bundled with Microsoft.Data.Sqlite)
            _db.RunExecute($"ALTER TABLE {T(tableName)} DROP COLUMN {C(columnName)}");
        }

        /// <summary>
        /// Rename a column
        /// </summary>
        public void RenameColumn(string tableName, string oldName, string newName)
        {
            string sql;
            switch (_providerType)
            {
                case DbProviderType.SqlServer:
                    sql = $"EXEC sp_rename '{tableName.Replace("'", "''")}.{oldName.Replace("'", "''")}', '{newName.Replace("'", "''")}', 'COLUMN'";
                    break;
                case DbProviderType.MySQL:
                case DbProviderType.MariaDB:
                case DbProviderType.PostgreSQL:
                case DbProviderType.SQLite:
                default:
                    // MySQL 8.0+, MariaDB 10.5.2+, PostgreSQL, SQLite 3.25+
                    sql = $"ALTER TABLE {T(tableName)} RENAME COLUMN {C(oldName)} TO {C(newName)}";
                    break;
            }
            _db.RunExecute(sql);
        }

        /// <summary>
        /// Change a column's type, NULL / NOT NULL and default (the column gets exactly this definition;
        /// <paramref name="defaultValue"/> null removes an existing default). The type is raw SQL for this provider.
        /// SQLite cannot alter columns: the table is rebuilt with its data, indexes and foreign keys.
        /// </summary>
        public void ChangeColumn(string tableName, string columnName, string columnType, bool nullable = true, string? defaultValue = null)
        {
            if (string.IsNullOrWhiteSpace(columnType))
                throw new ArgumentNullException(nameof(columnType));

            var table = T(tableName);
            var column = C(columnName);

            switch (_providerType)
            {
                case DbProviderType.SqlServer:
                {
                    // Defaults are named constraints: drop the old one, alter, add the new one
                    var literalTable = tableName.Replace("'", "''");
                    var literalColumn = columnName.Replace("'", "''");
                    _db.RunExecute(
                        "DECLARE @df sysname, @sql nvarchar(max); " +
                        "SELECT @df = dc.name FROM sys.default_constraints dc JOIN sys.columns c ON c.default_object_id = dc.object_id " +
                        $"WHERE dc.parent_object_id = OBJECT_ID(N'{literalTable}') AND c.name = N'{literalColumn}'; " +
                        $"IF @df IS NOT NULL BEGIN SET @sql = N'ALTER TABLE {table.Replace("'", "''")} DROP CONSTRAINT ' + QUOTENAME(@df); EXEC sp_executesql @sql; END");

                    _db.RunExecute($"ALTER TABLE {table} ALTER COLUMN {column} {columnType} {(nullable ? "NULL" : "NOT NULL")}");

                    if (defaultValue != null)
                    {
                        var dfName = C(ConstraintName("df", tableName, columnName));
                        _db.RunExecute($"ALTER TABLE {table} ADD CONSTRAINT {dfName} DEFAULT {defaultValue} FOR {column}");
                    }
                    break;
                }

                case DbProviderType.MySQL:
                case DbProviderType.MariaDB:
                    _db.RunExecute($"ALTER TABLE {table} MODIFY COLUMN {column} {columnType} {(nullable ? "NULL" : "NOT NULL")}" +
                                   (defaultValue != null ? $" DEFAULT {defaultValue}" : string.Empty));
                    break;

                case DbProviderType.PostgreSQL:
                    _db.RunExecute($"ALTER TABLE {table} " +
                                   $"ALTER COLUMN {column} TYPE {columnType} USING {column}::{columnType}, " +
                                   $"ALTER COLUMN {column} {(nullable ? "DROP NOT NULL" : "SET NOT NULL")}, " +
                                   $"ALTER COLUMN {column} {(defaultValue != null ? "SET DEFAULT " + defaultValue : "DROP DEFAULT")}");
                    break;

                default:
                    RebuildSqliteTable(tableName, (columns, foreignKeys) =>
                    {
                        var target = columns.FirstOrDefault(c => c.Name.Equals(columnName, StringComparison.OrdinalIgnoreCase))
                                     ?? throw new InvalidOperationException($"Column {columnName} not found in {tableName}.");
                        target.Type = columnType;
                        target.NotNull = !nullable;
                        target.Default = defaultValue;
                    });
                    break;
            }
        }

        /// <summary>
        /// Change columns defined with the table builder types, e.g.
        /// <c>schema.ChangeColumn("Users", t =&gt; t.String("Name", 200).NotNull())</c>
        /// </summary>
        public void ChangeColumn(string tableName, Action<TableBuilder> columns)
        {
            if (columns == null) throw new ArgumentNullException(nameof(columns));

            var builder = new TableBuilder(tableName, _providerType);
            columns(builder);
            foreach (var column in builder.Columns)
                ChangeColumn(tableName, column.Name, column.Type, column.Nullable, column.DefaultValue);
        }

        #endregion

        #region Foreign Keys

        /// <summary>
        /// Add a foreign key constraint to an existing table (named fk_{table}_{column}).
        /// SQLite: the table is rebuilt.
        /// </summary>
        public void AddForeign(string tableName, string column, string referencesTable, string referencesColumn = "Id", string onDelete = "CASCADE")
        {
            var action = ForeignAction(onDelete);

            if (_providerType == DbProviderType.SQLite)
            {
                RebuildSqliteTable(tableName, (columns, foreignKeys) =>
                    foreignKeys.Add(new SqliteForeignKey(new List<string> { column }, referencesTable, new List<string?> { referencesColumn }, action, "NO ACTION")));
                return;
            }

            var name = C(ConstraintName("fk", tableName, column));
            _db.RunExecute($"ALTER TABLE {T(tableName)} ADD CONSTRAINT {name} FOREIGN KEY ({C(column)}) REFERENCES {T(referencesTable)}({C(referencesColumn)}) ON DELETE {action}");
        }

        /// <summary>
        /// Drop the foreign key(s) on a column. The constraint name is looked up in the catalog, so keys
        /// created without a name (before 2.0) are found as well. SQLite: the table is rebuilt without it.
        /// </summary>
        /// <returns>Number of dropped constraints</returns>
        public int DropForeign(string tableName, string column)
        {
            if (_providerType == DbProviderType.SQLite)
            {
                int removed = 0;
                RebuildSqliteTable(tableName, (columns, foreignKeys) =>
                {
                    removed = foreignKeys.RemoveAll(fk => fk.From.Any(f => f.Equals(column, StringComparison.OrdinalIgnoreCase)));
                });
                if (removed == 0)
                    throw new InvalidOperationException($"No foreign key on {tableName}.{column}.");
                return removed;
            }

            var names = ForeignKeyNames(tableName, column);
            if (names.Count == 0)
                throw new InvalidOperationException($"No foreign key on {tableName}.{column}.");

            foreach (var name in names)
                DropForeignByName(tableName, name);
            return names.Count;
        }

        /// <summary>
        /// Drop a foreign key constraint by its exact name (not supported for SQLite: use DropForeign(table, column))
        /// </summary>
        public void DropForeignByName(string tableName, string constraintName)
        {
            switch (_providerType)
            {
                case DbProviderType.MySQL:
                case DbProviderType.MariaDB:
                    _db.RunExecute($"ALTER TABLE {T(tableName)} DROP FOREIGN KEY {Exact(constraintName)}");
                    break;
                case DbProviderType.SQLite:
                    throw new NotSupportedException("SQLite foreign keys have no usable names: use DropForeign(table, column).");
                default:
                    _db.RunExecute($"ALTER TABLE {T(tableName)} DROP CONSTRAINT {Exact(constraintName)}");
                    break;
            }
        }

        // Catalog names of the foreign keys whose columns include `column`
        private List<string> ForeignKeyNames(string tableName, string column)
        {
            SplitName(tableName, out var schema, out var name);
            string sql;
            switch (_providerType)
            {
                case DbProviderType.SqlServer:
                    _db.ParametersAdd("@table", tableName);
                    _db.ParametersAdd("@column", column);
                    sql = "SELECT fk.name FROM sys.foreign_keys fk " +
                          "JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id " +
                          "JOIN sys.columns c ON c.object_id = fkc.parent_object_id AND c.column_id = fkc.parent_column_id " +
                          "WHERE fk.parent_object_id = OBJECT_ID(@table) AND c.name = @column";
                    break;
                case DbProviderType.MySQL:
                case DbProviderType.MariaDB:
                    _db.ParametersAdd("@table", name);
                    _db.ParametersAdd("@column", column);
                    sql = "SELECT CONSTRAINT_NAME FROM information_schema.KEY_COLUMN_USAGE " +
                          "WHERE TABLE_NAME = @table AND COLUMN_NAME = @column AND REFERENCED_TABLE_NAME IS NOT NULL AND TABLE_SCHEMA = " + SchemaExpression(schema);
                    break;
                default: // PostgreSQL
                    _db.ParametersAdd("@table", Catalog(name));
                    _db.ParametersAdd("@column", Catalog(column));
                    sql = "SELECT tc.constraint_name FROM information_schema.table_constraints tc " +
                          "JOIN information_schema.key_column_usage kcu ON kcu.constraint_name = tc.constraint_name " +
                          "AND kcu.table_schema = tc.table_schema AND kcu.table_name = tc.table_name " +
                          "WHERE tc.constraint_type = 'FOREIGN KEY' AND tc.table_name = @table AND kcu.column_name = @column AND tc.table_schema = " + SchemaExpression(schema);
                    break;
            }

            return _db.RunDataTable(sql).AsEnumerable()
                .Select(r => Convert.ToString(r[0], CultureInfo.InvariantCulture) ?? string.Empty)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        // Catalog name quoted as is (no PostgreSQL lower-casing)
        private string Exact(string name)
        {
            switch (_providerType)
            {
                case DbProviderType.SqlServer:
                    return "[" + name.Replace("]", "]]") + "]";
                case DbProviderType.MySQL:
                case DbProviderType.MariaDB:
                    return "`" + name.Replace("`", "``") + "`";
                default:
                    return "\"" + name.Replace("\"", "\"\"") + "\"";
            }
        }

        private static string ForeignAction(string action)
        {
            var normalized = (action ?? "NO ACTION").Trim().ToUpperInvariant();
            switch (normalized)
            {
                case "CASCADE":
                case "SET NULL":
                case "SET DEFAULT":
                case "RESTRICT":
                case "NO ACTION":
                    return normalized;
                default:
                    throw new ArgumentException($"Unknown foreign key action '{action}'.", nameof(action));
            }
        }

        /// <summary>
        /// Constraint name "{prefix}_{table}_{column}", shortened with a hash to stay within 60 characters
        /// (PostgreSQL 63, MySQL 64)
        /// </summary>
        internal static string ConstraintName(string prefix, string tableName, string column)
        {
            var name = $"{prefix}_{tableName.Replace('.', '_')}_{column}";
            if (name.Length <= 60)
                return name;

            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(name));
                var suffix = BitConverter.ToString(hash, 0, 4).Replace("-", "").ToLowerInvariant();
                return name.Substring(0, 51) + "_" + suffix;
            }
        }

        #endregion

        #region SQLite table rebuild

        /// <summary>
        /// SQLite has no ALTER COLUMN / ADD or DROP CONSTRAINT: create the new table, copy the rows, drop the old
        /// one, rename, recreate indexes (sqlite.org/lang_altertable.html, "other kinds of table schema changes").
        /// Foreign keys are switched off meanwhile on a dedicated connection; inside a transaction that is not
        /// possible, so there a table that other tables reference cannot be rebuilt.
        /// </summary>
        private void RebuildSqliteTable(string tableName, Action<List<SqliteColumn>, List<SqliteForeignKey>> change)
        {
            if (_db.InTransaction)
            {
                RebuildSqliteTable(tableName, change, sql => _db.RunDataTable(sql), sql => _db.RunExecute(sql), insideTransaction: true);
                return;
            }

            _db.WithOwnConnection(connection =>
            {
                Func<string, DataTable> query = sql =>
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = sql;
                        using (var reader = cmd.ExecuteReader())
                        {
                            var dt = new DataTable();
                            DbCommandBase.AddReaderColumns(dt, reader, true);
                            while (reader.Read())
                                dt.Rows.Add(DbCommandBase.ReadRow(dt, reader));
                            return dt;
                        }
                    }
                };
                Action<string> exec = sql =>
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = sql;
                        cmd.ExecuteNonQuery();
                    }
                };

                RebuildSqliteTable(tableName, change, query, exec, insideTransaction: false);
            });
        }

        private static void RebuildSqliteTable(string tableName, Action<List<SqliteColumn>, List<SqliteForeignKey>> change,
            Func<string, DataTable> query, Action<string> exec, bool insideTransaction)
        {
            string Lit(string s) => "'" + s.Replace("'", "''") + "'";
            string Q(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
            string Str(object value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;

            var createSql = query($"SELECT sql FROM sqlite_master WHERE type = 'table' AND name = {Lit(tableName)}");
            if (createSql.Rows.Count == 0)
                throw new InvalidOperationException($"Table {tableName} does not exist.");
            var autoIncrement = (Convert.ToString(createSql.Rows[0][0], CultureInfo.InvariantCulture) ?? string.Empty)
                .IndexOf("AUTOINCREMENT", StringComparison.OrdinalIgnoreCase) >= 0;

            var columns = query($"SELECT name, type, \"notnull\", dflt_value, pk FROM pragma_table_info({Lit(tableName)}) ORDER BY cid").AsEnumerable()
                .Select(r => new SqliteColumn
                {
                    Name = Str(r[0]),
                    Type = Str(r[1]),
                    NotNull = Convert.ToInt64(r[2], CultureInfo.InvariantCulture) != 0,
                    Default = r[3] == DBNull.Value ? null : Convert.ToString(r[3], CultureInfo.InvariantCulture),
                    PkOrder = Convert.ToInt32(r[4], CultureInfo.InvariantCulture)
                }).ToList();
            var originalNames = new HashSet<string>(columns.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);

            var foreignKeys = query($"SELECT id, seq, \"table\", \"from\", \"to\", on_update, on_delete FROM pragma_foreign_key_list({Lit(tableName)}) ORDER BY id, seq").AsEnumerable()
                .GroupBy(r => Convert.ToInt64(r[0], CultureInfo.InvariantCulture))
                .Select(g => new SqliteForeignKey(
                    g.Select(r => Str(r[3])).ToList(),
                    Str(g.First()[2]),
                    g.Select(r => r[4] == DBNull.Value ? null : Str(r[4])).ToList(),
                    Str(g.First()[6]),
                    Str(g.First()[5])))
                .ToList();

            // UNIQUE constraints declared in CREATE TABLE live in automatic indexes (no SQL to replay)
            var uniques = new List<List<string>>();
            foreach (DataRow index in query($"SELECT name, origin FROM pragma_index_list({Lit(tableName)})").Rows)
            {
                if (!string.Equals(Convert.ToString(index[1], CultureInfo.InvariantCulture), "u", StringComparison.Ordinal))
                    continue;
                var indexName = Str(index[0]);
                uniques.Add(query($"SELECT name FROM pragma_index_info({Lit(indexName)}) ORDER BY seqno").AsEnumerable()
                    .Select(r => Str(r[0])).ToList());
            }

            var indexSql = query($"SELECT sql FROM sqlite_master WHERE type = 'index' AND tbl_name = {Lit(tableName)} AND sql IS NOT NULL").AsEnumerable()
                .Select(r => Str(r[0])).ToList();

            change(columns, foreignKeys);

            // New CREATE TABLE
            var keyColumns = columns.Where(c => c.PkOrder > 0).OrderBy(c => c.PkOrder).ToList();
            var rowIdKey = keyColumns.Count == 1 && keyColumns[0].Type.Equals("INTEGER", StringComparison.OrdinalIgnoreCase);
            var defs = new List<string>();
            foreach (var c in columns)
            {
                var def = $"{Q(c.Name)} {c.Type}".TrimEnd();
                if (rowIdKey && c.PkOrder > 0)
                    def += autoIncrement ? " PRIMARY KEY AUTOINCREMENT" : " PRIMARY KEY";
                if (c.NotNull)
                    def += " NOT NULL";
                if (c.Default != null)
                    def += " DEFAULT " + c.Default;
                defs.Add(def);
            }
            if (keyColumns.Count > 0 && !rowIdKey)
                defs.Add($"PRIMARY KEY ({string.Join(", ", keyColumns.Select(c => Q(c.Name)))})");
            foreach (var unique in uniques)
                defs.Add($"UNIQUE ({string.Join(", ", unique.Select(Q))})");
            foreach (var fk in foreignKeys)
            {
                var to = fk.To.All(t => t == null) ? string.Empty : $"({string.Join(", ", fk.To.Select(t => Q(t ?? string.Empty)))})";
                defs.Add($"FOREIGN KEY ({string.Join(", ", fk.From.Select(Q))}) REFERENCES {Q(fk.Table)}{to} ON DELETE {fk.OnDelete} ON UPDATE {fk.OnUpdate}");
            }

            var temp = "__mc_rebuild_" + tableName.Replace('.', '_');
            var copied = columns.Where(c => originalNames.Contains(c.Name)).Select(c => Q(c.Name)).ToList();

            bool fkWasOn = Convert.ToInt64(query("PRAGMA foreign_keys").Rows[0][0], CultureInfo.InvariantCulture) != 0;
            if (fkWasOn && insideTransaction)
            {
                var referenced = query("SELECT name FROM sqlite_master WHERE type = 'table'").AsEnumerable()
                    .Select(r => Str(r[0]))
                    .Where(t => !t.Equals(tableName, StringComparison.OrdinalIgnoreCase))
                    .Any(t => query($"SELECT 1 FROM pragma_foreign_key_list({Lit(t)}) WHERE \"table\" = {Lit(tableName)} COLLATE NOCASE").Rows.Count > 0);
                if (referenced)
                    throw new NotSupportedException(
                        $"SQLite: {tableName} is referenced by foreign keys of other tables, and foreign keys cannot be switched off inside a transaction. " +
                        "Override WithinTransaction => false in this migration so the table can be rebuilt safely.");
            }

            if (!insideTransaction)
            {
                if (fkWasOn) exec("PRAGMA foreign_keys = OFF");
                exec("BEGIN");
            }

            try
            {
                exec($"DROP TABLE IF EXISTS {Q(temp)}");
                exec($"CREATE TABLE {Q(temp)} ({string.Join(", ", defs)})");
                if (copied.Count > 0)
                    exec($"INSERT INTO {Q(temp)} ({string.Join(", ", copied)}) SELECT {string.Join(", ", copied)} FROM {Q(tableName)}");
                exec($"DROP TABLE {Q(tableName)}");
                exec($"ALTER TABLE {Q(temp)} RENAME TO {Q(tableName)}");
                foreach (var sql in indexSql)
                    exec(sql);

                if (!insideTransaction && fkWasOn && query($"SELECT 1 FROM pragma_foreign_key_check({Lit(tableName)})").Rows.Count > 0)
                    throw new InvalidOperationException($"SQLite: rows of {tableName} break the new foreign key; nothing was changed.");

                if (!insideTransaction)
                    exec("COMMIT");
            }
            catch
            {
                if (!insideTransaction)
                {
                    try { exec("ROLLBACK"); } catch { }
                }
                throw;
            }
            finally
            {
                if (!insideTransaction && fkWasOn)
                    exec("PRAGMA foreign_keys = ON");
            }
        }

        private sealed class SqliteColumn
        {
            public string Name { get; set; } = string.Empty;
            public string Type { get; set; } = string.Empty;
            public bool NotNull { get; set; }
            public string? Default { get; set; }
            public int PkOrder { get; set; }
        }

        private sealed class SqliteForeignKey
        {
            public SqliteForeignKey(List<string> from, string table, List<string?> to, string onDelete, string onUpdate)
            {
                From = from;
                Table = table;
                To = to;
                OnDelete = string.IsNullOrEmpty(onDelete) ? "NO ACTION" : onDelete;
                OnUpdate = string.IsNullOrEmpty(onUpdate) ? "NO ACTION" : onUpdate;
            }

            public List<string> From { get; }
            public string Table { get; }
            public List<string?> To { get; }
            public string OnDelete { get; }
            public string OnUpdate { get; }
        }

        #endregion

        #region Column Info

        /// <summary>
        /// Check if column exists
        /// </summary>
        public bool HasColumn(string tableName, string columnName)
        {
            SplitName(tableName, out var schema, out var name);
            _db.ParametersAdd("@table", Catalog(name));
            _db.ParametersAdd("@column", Catalog(columnName));

            string sql;
            switch (_providerType)
            {
                case DbProviderType.SqlServer:
                    sql = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @table AND COLUMN_NAME = @column AND TABLE_SCHEMA = " + SchemaExpression(schema);
                    break;
                case DbProviderType.MySQL:
                case DbProviderType.MariaDB:
                case DbProviderType.PostgreSQL:
                    sql = "SELECT COUNT(*) FROM information_schema.columns WHERE table_name = @table AND column_name = @column AND table_schema = " + SchemaExpression(schema);
                    break;
                case DbProviderType.SQLite:
                default:
                    sql = "SELECT COUNT(*) FROM pragma_table_info(@table) WHERE name = @column";
                    break;
            }
            return _db.RunToInt32Scaler(sql) > 0;
        }

        #endregion

        #region Index Operations

        /// <summary>
        /// Create an index
        /// </summary>
        public void CreateIndex(string tableName, string indexName, params string[] columns)
        {
            var sql = $"CREATE INDEX {C(indexName)} ON {T(tableName)} ({string.Join(", ", columns.Select(C))})";
            _db.RunExecute(sql);
        }

        /// <summary>
        /// Create a unique index
        /// </summary>
        public void CreateUniqueIndex(string tableName, string indexName, params string[] columns)
        {
            var sql = $"CREATE UNIQUE INDEX {C(indexName)} ON {T(tableName)} ({string.Join(", ", columns.Select(C))})";
            _db.RunExecute(sql);
        }

        /// <summary>
        /// Drop an index
        /// </summary>
        public void DropIndex(string tableName, string indexName)
        {
            string sql;
            switch (_providerType)
            {
                case DbProviderType.SqlServer:
                case DbProviderType.MySQL:
                case DbProviderType.MariaDB:
                    sql = $"DROP INDEX {C(indexName)} ON {T(tableName)}";
                    break;
                case DbProviderType.PostgreSQL:
                case DbProviderType.SQLite:
                default:
                    sql = $"DROP INDEX {C(indexName)}";
                    break;
            }
            _db.RunExecute(sql);
        }

        #endregion

        #region Raw SQL

        /// <summary>
        /// Execute raw SQL
        /// </summary>
        public void Raw(string sql)
        {
            _db.RunExecute(sql);
        }

        #endregion
    }

    /// <summary>
    /// Table builder for creating tables with fluent API
    /// </summary>
    public class TableBuilder
    {
        private readonly string _tableName;
        private readonly DbProviderType _providerType;
        private readonly List<ColumnDefinition> _columns = new List<ColumnDefinition>();
        private readonly List<string> _primaryKeys = new List<string>();
        private readonly List<string> _indexes = new List<string>();
        private readonly List<string> _foreignKeys = new List<string>();

        public TableBuilder(string tableName, DbProviderType providerType)
        {
            _tableName = tableName;
            _providerType = providerType;
        }

        #region Column Types

        /// <summary>
        /// Auto-incrementing primary key
        /// </summary>
        public TableBuilder Id(string name = "Id")
        {
            switch (_providerType)
            {
                case DbProviderType.SqlServer:
                    _columns.Add(new ColumnDefinition(name, "INT IDENTITY(1,1)", false));
                    break;
                case DbProviderType.MySQL:
                case DbProviderType.MariaDB:
                    _columns.Add(new ColumnDefinition(name, "INT AUTO_INCREMENT", false));
                    break;
                case DbProviderType.PostgreSQL:
                    _columns.Add(new ColumnDefinition(name, "SERIAL", false));
                    break;
                case DbProviderType.SQLite:
                default:
                    _columns.Add(new ColumnDefinition(name, "INTEGER", false) { IsPrimaryKey = true });
                    break;
            }
            _primaryKeys.Add(name);
            return this;
        }

        /// <summary>
        /// Big integer auto-incrementing primary key
        /// </summary>
        public TableBuilder BigId(string name = "Id")
        {
            switch (_providerType)
            {
                case DbProviderType.SqlServer:
                    _columns.Add(new ColumnDefinition(name, "BIGINT IDENTITY(1,1)", false));
                    break;
                case DbProviderType.MySQL:
                case DbProviderType.MariaDB:
                    _columns.Add(new ColumnDefinition(name, "BIGINT AUTO_INCREMENT", false));
                    break;
                case DbProviderType.PostgreSQL:
                    _columns.Add(new ColumnDefinition(name, "BIGSERIAL", false));
                    break;
                case DbProviderType.SQLite:
                default:
                    _columns.Add(new ColumnDefinition(name, "INTEGER", false) { IsPrimaryKey = true });
                    break;
            }
            _primaryKeys.Add(name);
            return this;
        }

        /// <summary>
        /// UUID/GUID primary key
        /// </summary>
        public TableBuilder Uuid(string name = "Id")
        {
            switch (_providerType)
            {
                case DbProviderType.SqlServer:
                    _columns.Add(new ColumnDefinition(name, "UNIQUEIDENTIFIER", false) { DefaultValue = "NEWID()" });
                    break;
                case DbProviderType.PostgreSQL:
                    _columns.Add(new ColumnDefinition(name, "UUID", false) { DefaultValue = "gen_random_uuid()" });
                    break;
                case DbProviderType.MySQL:
                case DbProviderType.MariaDB:
                case DbProviderType.SQLite:
                default:
                    _columns.Add(new ColumnDefinition(name, "VARCHAR(36)", false));
                    break;
            }
            _primaryKeys.Add(name);
            return this;
        }

        /// <summary>
        /// String column
        /// </summary>
        public ColumnBuilder String(string name, int length = 255)
        {
            // SQL Server: NVARCHAR keeps Unicode (ğ, ş, İ ...) regardless of the database collation
            string type = _providerType == DbProviderType.SqlServer
                ? (length > 4000 ? "NVARCHAR(MAX)" : $"NVARCHAR({length})")
                : $"VARCHAR({length})";
            var col = new ColumnDefinition(name, type, true);
            _columns.Add(col);
            return new ColumnBuilder(col, this);
        }

        /// <summary>
        /// Text column (long text)
        /// </summary>
        public ColumnBuilder Text(string name)
        {
            string type = _providerType == DbProviderType.SqlServer ? "NVARCHAR(MAX)" : "TEXT";
            var col = new ColumnDefinition(name, type, true);
            _columns.Add(col);
            return new ColumnBuilder(col, this);
        }

        /// <summary>
        /// Integer column
        /// </summary>
        public ColumnBuilder Integer(string name)
        {
            var col = new ColumnDefinition(name, "INT", true);
            _columns.Add(col);
            return new ColumnBuilder(col, this);
        }

        /// <summary>
        /// Big integer column
        /// </summary>
        public ColumnBuilder BigInteger(string name)
        {
            var col = new ColumnDefinition(name, "BIGINT", true);
            _columns.Add(col);
            return new ColumnBuilder(col, this);
        }

        /// <summary>
        /// Small integer column
        /// </summary>
        public ColumnBuilder SmallInteger(string name)
        {
            var col = new ColumnDefinition(name, "SMALLINT", true);
            _columns.Add(col);
            return new ColumnBuilder(col, this);
        }

        /// <summary>
        /// Tiny integer column
        /// </summary>
        public ColumnBuilder TinyInteger(string name)
        {
            string type = (_providerType == DbProviderType.PostgreSQL) ? "SMALLINT" : "TINYINT";
            var col = new ColumnDefinition(name, type, true);
            _columns.Add(col);
            return new ColumnBuilder(col, this);
        }

        /// <summary>
        /// Decimal column
        /// </summary>
        public ColumnBuilder Decimal(string name, int precision = 10, int scale = 2)
        {
            var col = new ColumnDefinition(name, $"DECIMAL({precision},{scale})", true);
            _columns.Add(col);
            return new ColumnBuilder(col, this);
        }

        /// <summary>
        /// Float column
        /// </summary>
        public ColumnBuilder Float(string name)
        {
            var col = new ColumnDefinition(name, "FLOAT", true);
            _columns.Add(col);
            return new ColumnBuilder(col, this);
        }

        /// <summary>
        /// Double column
        /// </summary>
        public ColumnBuilder Double(string name)
        {
            string type;
            switch (_providerType)
            {
                case DbProviderType.PostgreSQL:
                    type = "DOUBLE PRECISION";
                    break;
                case DbProviderType.SqlServer:
                    type = "FLOAT"; // FLOAT(53) = double precision; SQL Server has no DOUBLE
                    break;
                case DbProviderType.SQLite:
                    type = "REAL";
                    break;
                default:
                    type = "DOUBLE";
                    break;
            }
            var col = new ColumnDefinition(name, type, true);
            _columns.Add(col);
            return new ColumnBuilder(col, this);
        }

        /// <summary>
        /// Boolean column
        /// </summary>
        public ColumnBuilder Boolean(string name)
        {
            string type = (_providerType == DbProviderType.SqlServer) ? "BIT" : "BOOLEAN";
            var col = new ColumnDefinition(name, type, true);
            _columns.Add(col);
            return new ColumnBuilder(col, this);
        }

        /// <summary>
        /// Date column
        /// </summary>
        public ColumnBuilder Date(string name)
        {
            var col = new ColumnDefinition(name, "DATE", true);
            _columns.Add(col);
            return new ColumnBuilder(col, this);
        }

        /// <summary>
        /// DateTime column
        /// </summary>
        public ColumnBuilder DateTime(string name)
        {
            string type = (_providerType == DbProviderType.PostgreSQL) ? "TIMESTAMP" : "DATETIME";
            var col = new ColumnDefinition(name, type, true);
            _columns.Add(col);
            return new ColumnBuilder(col, this);
        }

        /// <summary>
        /// Time column
        /// </summary>
        public ColumnBuilder Time(string name)
        {
            var col = new ColumnDefinition(name, "TIME", true);
            _columns.Add(col);
            return new ColumnBuilder(col, this);
        }

        /// <summary>
        /// Timestamp column
        /// </summary>
        public ColumnBuilder Timestamp(string name)
        {
            string type = (_providerType == DbProviderType.SqlServer) ? "DATETIME2" : "TIMESTAMP";
            var col = new ColumnDefinition(name, type, true);
            _columns.Add(col);
            return new ColumnBuilder(col, this);
        }

        /// <summary>
        /// Binary/Blob column
        /// </summary>
        public ColumnBuilder Binary(string name, int length = 0)
        {
            string type;
            switch (_providerType)
            {
                case DbProviderType.SqlServer:
                    type = length > 0 ? $"VARBINARY({length})" : "VARBINARY(MAX)";
                    break;
                case DbProviderType.MySQL:
                case DbProviderType.MariaDB:
                    type = length > 0 ? $"VARBINARY({length})" : "BLOB";
                    break;
                case DbProviderType.PostgreSQL:
                    type = "BYTEA";
                    break;
                default:
                    type = "BLOB";
                    break;
            }
            var col = new ColumnDefinition(name, type, true);
            _columns.Add(col);
            return new ColumnBuilder(col, this);
        }

        /// <summary>
        /// JSON column
        /// </summary>
        public ColumnBuilder Json(string name)
        {
            string type;
            switch (_providerType)
            {
                case DbProviderType.SqlServer:
                    type = "NVARCHAR(MAX)";
                    break;
                case DbProviderType.MySQL:
                case DbProviderType.MariaDB:
                case DbProviderType.PostgreSQL:
                    type = "JSON";
                    break;
                default:
                    type = "TEXT";
                    break;
            }
            var col = new ColumnDefinition(name, type, true);
            _columns.Add(col);
            return new ColumnBuilder(col, this);
        }

        /// <summary>
        /// Add created_at and updated_at columns
        /// </summary>
        public TableBuilder Timestamps()
        {
            DateTime("CreatedAt").Nullable();
            DateTime("UpdatedAt").Nullable();
            return this;
        }

        /// <summary>
        /// Add soft delete column (deleted_at)
        /// </summary>
        public TableBuilder SoftDeletes(string name = "DeletedAt")
        {
            DateTime(name).Nullable();
            return this;
        }

        /// <summary>
        /// Foreign key column
        /// </summary>
        public ColumnBuilder ForeignId(string name)
        {
            var col = new ColumnDefinition(name, "INT", true);
            _columns.Add(col);
            return new ColumnBuilder(col, this);
        }

        /// <summary>
        /// Declare the primary key for columns created with String/Integer/... (Id/BigId/Uuid add it automatically)
        /// </summary>
        public TableBuilder Primary(params string[] columns)
        {
            foreach (var column in columns)
            {
                if (!_primaryKeys.Contains(column))
                    _primaryKeys.Add(column);

                var def = _columns.FirstOrDefault(c => c.Name == column);
                if (def != null)
                    def.Nullable = false;
            }
            return this;
        }

        /// <summary>
        /// Add foreign key constraint (named fk_{table}_{column}; Schema.DropForeign(table, column) removes it)
        /// </summary>
        public TableBuilder Foreign(string column, string referencesTable, string referencesColumn = "Id", string onDelete = "CASCADE")
        {
            var name = C(Schema.ConstraintName("fk", _tableName, column));
            _foreignKeys.Add($"CONSTRAINT {name} FOREIGN KEY ({C(column)}) REFERENCES {T(referencesTable)}({C(referencesColumn)}) ON DELETE {onDelete}");
            return this;
        }

        /// <summary>
        /// Columns defined so far (Schema.ChangeColumn uses them)
        /// </summary>
        internal IReadOnlyList<ColumnDefinition> Columns => _columns;

        /// <summary>
        /// Add index
        /// </summary>
        public TableBuilder Index(params string[] columns)
        {
            var indexName = $"idx_{_tableName.Replace('.', '_')}_{string.Join("_", columns)}";
            _indexes.Add($"CREATE INDEX {C(indexName)} ON {T(_tableName)} ({string.Join(", ", columns.Select(C))})");
            return this;
        }

        /// <summary>
        /// Add unique constraint
        /// </summary>
        public TableBuilder Unique(params string[] columns)
        {
            var indexName = $"uq_{_tableName.Replace('.', '_')}_{string.Join("_", columns)}";
            _indexes.Add($"CREATE UNIQUE INDEX {C(indexName)} ON {T(_tableName)} ({string.Join(", ", columns.Select(C))})");
            return this;
        }

        private string T(string tableName) => mersolutionCore.ORM.SqlDialect.Table(_providerType, tableName);
        private string C(string columnName) => mersolutionCore.ORM.SqlDialect.Column(_providerType, columnName);

        #endregion

        /// <summary>
        /// Build the CREATE TABLE SQL
        /// </summary>
        public string Build()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"CREATE TABLE {T(_tableName)} (");

            var columnDefs = new List<string>();

            foreach (var col in _columns)
            {
                columnDefs.Add(col.Build(_providerType));
            }

            // Add primary key constraint (if not SQLite with INTEGER PRIMARY KEY)
            if (_primaryKeys.Count > 0 && !(_providerType == DbProviderType.SQLite && _columns.Any(c => c.IsPrimaryKey)))
            {
                columnDefs.Add($"PRIMARY KEY ({string.Join(", ", _primaryKeys.Select(C))})");
            }

            // Add foreign keys
            columnDefs.AddRange(_foreignKeys);

            sb.AppendLine(string.Join(",\n", columnDefs));
            sb.Append(")");

            return sb.ToString();
        }

        /// <summary>
        /// Get index creation statements
        /// </summary>
        public List<string> GetIndexStatements()
        {
            return _indexes;
        }
    }

    /// <summary>
    /// Column definition
    /// </summary>
    public class ColumnDefinition
    {
        public string Name { get; }
        public string Type { get; }
        public bool Nullable { get; set; }
        public string? DefaultValue { get; set; }
        public bool IsPrimaryKey { get; set; }
        public bool IsUnique { get; set; }

        public ColumnDefinition(string name, string type, bool nullable)
        {
            Name = name;
            Type = type;
            Nullable = nullable;
        }

        public string Build(DbProviderType providerType)
        {
            var sb = new StringBuilder();
            sb.Append($"{mersolutionCore.ORM.SqlDialect.Column(providerType, Name)} {Type}");

            if (IsPrimaryKey && providerType == DbProviderType.SQLite)
            {
                sb.Append(" PRIMARY KEY");
            }

            if (!Nullable)
                sb.Append(" NOT NULL");

            if (DefaultValue != null)
                sb.Append($" DEFAULT {DefaultValue}");

            if (IsUnique)
                sb.Append(" UNIQUE");

            return sb.ToString();
        }
    }

    /// <summary>
    /// Fluent column builder
    /// </summary>
    public class ColumnBuilder
    {
        private readonly ColumnDefinition _column;
        private readonly TableBuilder _tableBuilder;

        public ColumnBuilder(ColumnDefinition column, TableBuilder tableBuilder)
        {
            _column = column;
            _tableBuilder = tableBuilder;
        }

        public ColumnBuilder Nullable()
        {
            _column.Nullable = true;
            return this;
        }

        public ColumnBuilder NotNull()
        {
            _column.Nullable = false;
            return this;
        }

        public ColumnBuilder Default(string value)
        {
            _column.DefaultValue = value;
            return this;
        }

        public ColumnBuilder Unique()
        {
            _column.IsUnique = true;
            return this;
        }

        // Allow chaining back to TableBuilder
        public ColumnBuilder String(string name, int length = 255) => _tableBuilder.String(name, length);
        public ColumnBuilder Text(string name) => _tableBuilder.Text(name);
        public ColumnBuilder Integer(string name) => _tableBuilder.Integer(name);
        public ColumnBuilder BigInteger(string name) => _tableBuilder.BigInteger(name);
        public ColumnBuilder Decimal(string name, int precision = 10, int scale = 2) => _tableBuilder.Decimal(name, precision, scale);
        public ColumnBuilder Boolean(string name) => _tableBuilder.Boolean(name);
        public ColumnBuilder DateTime(string name) => _tableBuilder.DateTime(name);
        public ColumnBuilder Date(string name) => _tableBuilder.Date(name);
        public ColumnBuilder ForeignId(string name) => _tableBuilder.ForeignId(name);
        public TableBuilder Id(string name = "Id") => _tableBuilder.Id(name);
        public TableBuilder Timestamps() => _tableBuilder.Timestamps();
        public TableBuilder SoftDeletes(string name = "DeletedAt") => _tableBuilder.SoftDeletes(name);
        public TableBuilder Foreign(string column, string referencesTable, string referencesColumn = "Id", string onDelete = "CASCADE") 
            => _tableBuilder.Foreign(column, referencesTable, referencesColumn, onDelete);
        public TableBuilder Index(params string[] columns) => _tableBuilder.Index(columns);
        public TableBuilder Unique(params string[] columns) => _tableBuilder.Unique(columns);
    }
}
