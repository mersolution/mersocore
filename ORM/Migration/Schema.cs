using System;
using System.Collections.Generic;
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
                    sql = $"IF OBJECT_ID('{tableName}', 'U') IS NOT NULL DROP TABLE {tableName}";
                    break;
                case DbProviderType.MySQL:
                case DbProviderType.MariaDB:
                case DbProviderType.SQLite:
                case DbProviderType.PostgreSQL:
                default:
                    sql = $"DROP TABLE IF EXISTS {tableName}";
                    break;
            }
            _db.RunExecute(sql);
        }

        /// <summary>
        /// Drop a table
        /// </summary>
        public void DropTable(string tableName)
        {
            _db.RunExecute($"DROP TABLE {tableName}");
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
                    sql = $"EXEC sp_rename '{oldName}', '{newName}'";
                    break;
                case DbProviderType.MySQL:
                case DbProviderType.MariaDB:
                    sql = $"RENAME TABLE {oldName} TO {newName}";
                    break;
                case DbProviderType.PostgreSQL:
                case DbProviderType.SQLite:
                default:
                    sql = $"ALTER TABLE {oldName} RENAME TO {newName}";
                    break;
            }
            _db.RunExecute(sql);
        }

        /// <summary>
        /// Check if table exists
        /// </summary>
        public bool HasTable(string tableName)
        {
            string sql;
            switch (_providerType)
            {
                case DbProviderType.SqlServer:
                    sql = $"SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = '{tableName}'";
                    break;
                case DbProviderType.MySQL:
                case DbProviderType.MariaDB:
                    sql = $"SELECT COUNT(*) FROM information_schema.tables WHERE table_name = '{tableName}'";
                    break;
                case DbProviderType.PostgreSQL:
                    sql = $"SELECT COUNT(*) FROM information_schema.tables WHERE table_name = '{tableName}'";
                    break;
                case DbProviderType.SQLite:
                default:
                    sql = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{tableName}'";
                    break;
            }
            return _db.RunToInt32Scaler(sql) > 0;
        }

        #endregion

        #region Column Operations

        /// <summary>
        /// Add a column to existing table
        /// </summary>
        public void AddColumn(string tableName, string columnName, string columnType, bool nullable = true, string defaultValue = null)
        {
            var sb = new StringBuilder();
            sb.Append($"ALTER TABLE {tableName} ADD {columnName} {columnType}");

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
            string sql;
            switch (_providerType)
            {
                case DbProviderType.SQLite:
                    throw new NotSupportedException("SQLite does not support dropping columns directly");
                default:
                    sql = $"ALTER TABLE {tableName} DROP COLUMN {columnName}";
                    break;
            }
            _db.RunExecute(sql);
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
                    sql = $"EXEC sp_rename '{tableName}.{oldName}', '{newName}', 'COLUMN'";
                    break;
                case DbProviderType.MySQL:
                case DbProviderType.MariaDB:
                    throw new NotSupportedException("MySQL/MariaDB rename column requires column type - use ModifyColumn instead");
                case DbProviderType.PostgreSQL:
                case DbProviderType.SQLite:
                default:
                    sql = $"ALTER TABLE {tableName} RENAME COLUMN {oldName} TO {newName}";
                    break;
            }
            _db.RunExecute(sql);
        }

        /// <summary>
        /// Check if column exists
        /// </summary>
        public bool HasColumn(string tableName, string columnName)
        {
            string sql;
            switch (_providerType)
            {
                case DbProviderType.SqlServer:
                    sql = $"SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{tableName}' AND COLUMN_NAME = '{columnName}'";
                    break;
                case DbProviderType.MySQL:
                case DbProviderType.MariaDB:
                case DbProviderType.PostgreSQL:
                    sql = $"SELECT COUNT(*) FROM information_schema.columns WHERE table_name = '{tableName}' AND column_name = '{columnName}'";
                    break;
                case DbProviderType.SQLite:
                default:
                    sql = $"SELECT COUNT(*) FROM pragma_table_info('{tableName}') WHERE name = '{columnName}'";
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
            var sql = $"CREATE INDEX {indexName} ON {tableName} ({string.Join(", ", columns)})";
            _db.RunExecute(sql);
        }

        /// <summary>
        /// Create a unique index
        /// </summary>
        public void CreateUniqueIndex(string tableName, string indexName, params string[] columns)
        {
            var sql = $"CREATE UNIQUE INDEX {indexName} ON {tableName} ({string.Join(", ", columns)})";
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
                    sql = $"DROP INDEX {indexName} ON {tableName}";
                    break;
                case DbProviderType.MySQL:
                case DbProviderType.MariaDB:
                    sql = $"DROP INDEX {indexName} ON {tableName}";
                    break;
                case DbProviderType.PostgreSQL:
                case DbProviderType.SQLite:
                default:
                    sql = $"DROP INDEX {indexName}";
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
            var col = new ColumnDefinition(name, $"VARCHAR({length})", true);
            _columns.Add(col);
            return new ColumnBuilder(col, this);
        }

        /// <summary>
        /// Text column (long text)
        /// </summary>
        public ColumnBuilder Text(string name)
        {
            var col = new ColumnDefinition(name, "TEXT", true);
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
            string type = (_providerType == DbProviderType.PostgreSQL) ? "DOUBLE PRECISION" : "DOUBLE";
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
        /// Add foreign key constraint
        /// </summary>
        public TableBuilder Foreign(string column, string referencesTable, string referencesColumn = "Id", string onDelete = "CASCADE")
        {
            _foreignKeys.Add($"FOREIGN KEY ({column}) REFERENCES {referencesTable}({referencesColumn}) ON DELETE {onDelete}");
            return this;
        }

        /// <summary>
        /// Add index
        /// </summary>
        public TableBuilder Index(params string[] columns)
        {
            var indexName = $"idx_{_tableName}_{string.Join("_", columns)}";
            _indexes.Add($"CREATE INDEX {indexName} ON {_tableName} ({string.Join(", ", columns)})");
            return this;
        }

        /// <summary>
        /// Add unique constraint
        /// </summary>
        public TableBuilder Unique(params string[] columns)
        {
            var indexName = $"uq_{_tableName}_{string.Join("_", columns)}";
            _indexes.Add($"CREATE UNIQUE INDEX {indexName} ON {_tableName} ({string.Join(", ", columns)})");
            return this;
        }

        #endregion

        /// <summary>
        /// Build the CREATE TABLE SQL
        /// </summary>
        public string Build()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"CREATE TABLE {_tableName} (");

            var columnDefs = new List<string>();

            foreach (var col in _columns)
            {
                columnDefs.Add(col.Build(_providerType));
            }

            // Add primary key constraint (if not SQLite with INTEGER PRIMARY KEY)
            if (_primaryKeys.Count > 0 && !(_providerType == DbProviderType.SQLite && _columns.Any(c => c.IsPrimaryKey)))
            {
                columnDefs.Add($"PRIMARY KEY ({string.Join(", ", _primaryKeys)})");
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
        public string DefaultValue { get; set; }
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
            sb.Append($"{Name} {Type}");

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
