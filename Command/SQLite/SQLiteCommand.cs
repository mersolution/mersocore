using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using mersolutionCore.Command.Abstractions;

namespace mersolutionCore.Command.SQLite
{
    /// <summary>
    /// SQLite database command implementation
    /// </summary>
    public class SQLiteCommand : DbCommandBase
    {
        private readonly ConnectionConfig _config;
        private long _lastInsertRowId;

        public override DbProviderType ProviderType => DbProviderType.SQLite;

        /// <summary>
        /// Create SQLite command with connection config
        /// </summary>
        /// <param name="config">Connection configuration</param>
        public SQLiteCommand(ConnectionConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>
        /// Create SQLite command with connection string
        /// </summary>
        /// <param name="connectionString">Connection string</param>
        public SQLiteCommand(string connectionString)
        {
            _config = new ConnectionConfig { ConnectionString = connectionString };
        }

        /// <summary>
        /// Create SQLite command with database file path
        /// </summary>
        /// <param name="databasePath">Path to SQLite database file</param>
        /// <param name="isFilePath">Set to true to indicate this is a file path</param>
        public SQLiteCommand(string databasePath, bool isFilePath)
        {
            if (isFilePath)
            {
                _config = new ConnectionConfig
                {
                    ConnectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ConnectionString
                };
            }
            else
            {
                _config = new ConnectionConfig { ConnectionString = databasePath };
            }
        }

        protected override int CommandTimeout => _config.Timeout;

        protected override DbConnection NewConnection() => new SqliteConnection(GetConnectionString());

        // Microsoft.Data.Sqlite has no DataAdapter: the base class loads results through a reader.
        // Columns are typed per value (SQLite is dynamically typed; DECIMAL may hold REAL/TEXT).
        protected override bool LoadColumnsAsObject => true;

        protected override string GetConnectionString()
        {
            if (!string.IsNullOrEmpty(_config.ConnectionString))
                return _config.ConnectionString!;

            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = _config.Database
            };

            if (!string.IsNullOrEmpty(_config.Password))
                builder.Password = _config.Password;

            return builder.ConnectionString;
        }

        protected override string BuildPagedSql(string sql, int offset, int pageSize)
        {
            return $"{sql} LIMIT {pageSize} OFFSET {offset}";
        }

        protected override object? ExecuteInsertCommand(DbCommand command, string primaryKeyColumn)
        {
            command.ExecuteNonQuery();
            return ReadLastInsertRowId(command);
        }

        protected override async Task<object?> ExecuteInsertCommandAsync(DbCommand command, string primaryKeyColumn, CancellationToken cancellationToken)
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return ReadLastInsertRowId(command);
        }

        protected override void OnCommandExecuted(DbCommand command, string sql)
        {
            var head = sql?.TrimStart() ?? string.Empty;
            if (head.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase) || head.StartsWith("REPLACE", StringComparison.OrdinalIgnoreCase))
                _lastInsertRowId = ReadLastInsertRowId(command);
        }

        // last_insert_rowid() is per connection: ask the same connection, inside the same transaction
        private static long ReadLastInsertRowId(DbCommand executed)
        {
            using (var cmd = executed.Connection!.CreateCommand())
            {
                cmd.Transaction = executed.Transaction;
                cmd.CommandText = "SELECT last_insert_rowid()";
                return Convert.ToInt64(cmd.ExecuteScalar());
            }
        }

        /// <summary>
        /// Add parameter with SQLite type
        /// </summary>
        public void ParametersAdd(string name, object? value, SqliteType sqliteType)
        {
            AddPendingParameter(name, cmd =>
            {
                var param = ((SqliteCommand)cmd).CreateParameter();
                param.ParameterName = name;
                param.SqliteType = sqliteType;
                param.Value = NormalizeParameterValue(value);
                cmd.Parameters.Add(param);
            });
        }

        /// <summary>
        /// Get the highest primary key value (MAX). For a just inserted row use <see cref="DbCommandBase.RunInsertGetId"/>.
        /// </summary>
        public override int PKLastKeyOrDefault(string tableName, string columnName)
        {
            return RunToInt32Scaler($"SELECT IFNULL(MAX({columnName}), 0) FROM {tableName}");
        }

        /// <summary>
        /// Rowid generated by the last INSERT run through this instance
        /// (last_insert_rowid() is per connection, so it is captured right after the statement)
        /// </summary>
        public long GetLastInsertRowId()
        {
            return _lastInsertRowId;
        }

        /// <summary>
        /// Vacuum database (optimize and shrink)
        /// </summary>
        public void Vacuum()
        {
            RunExecute("VACUUM");
        }

        /// <summary>
        /// Execute SQL query with LIMIT (SQLite specific, memory-safe)
        /// </summary>
        public DataTable RunDataTableLimit(string sql, int limitCount)
        {
            if (limitCount < 1) limitCount = 1000;
            return RunDataTable($"{sql} LIMIT {limitCount}");
        }

        /// <summary>
        /// Get total record count for pagination (<paramref name="whereClause"/> is raw SQL — never pass user input)
        /// </summary>
        public int GetTotalCount(string tableName, string? whereClause = null)
        {
            string sql = $"SELECT COUNT(*) FROM {tableName}";
            if (!string.IsNullOrEmpty(whereClause))
            {
                sql += $" WHERE {whereClause}";
            }
            return RunToInt32Scaler(sql);
        }

        /// <summary>
        /// Execute SQL query with cursor-based streaming (for very large datasets)
        /// </summary>
        public void RunDataReaderStreaming(string sql, Action<SqliteDataReader> action)
        {
            try
            {
                PrepareCommand(sql);

                using (var reader = ((SqliteCommand)Command!).ExecuteReader())
                {
                    while (reader.Read())
                    {
                        action(reader);
                    }
                }
            }
            finally
            {
                CloseConnection();
            }
        }
    }
}
