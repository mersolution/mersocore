using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using MySqlConnector;
using mersolutionCore.Command.Abstractions;

namespace mersolutionCore.Command.MySQL
{
    /// <summary>
    /// MySQL database command implementation
    /// </summary>
    public class MySqlCommand : DbCommandBase
    {
        private readonly ConnectionConfig _config;
        private long _lastInsertId;

        public override DbProviderType ProviderType => DbProviderType.MySQL;

        /// <summary>
        /// Create MySQL command with connection config
        /// </summary>
        /// <param name="config">Connection configuration</param>
        public MySqlCommand(ConnectionConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>
        /// Create MySQL command with connection string
        /// </summary>
        /// <param name="connectionString">Connection string</param>
        public MySqlCommand(string connectionString)
        {
            _config = new ConnectionConfig { ConnectionString = connectionString };
        }

        private MySqlConnector.MySqlCommand MyCmd => (MySqlConnector.MySqlCommand)Command!;

        protected override int CommandTimeout => _config.Timeout;

        protected override DbConnection NewConnection() => new MySqlConnection(GetConnectionString());

        protected override void CreateDataAdapter()
        {
            DataAdapter = new MySqlDataAdapter(MyCmd);
        }

        protected override string GetConnectionString()
        {
            if (!string.IsNullOrEmpty(_config.ConnectionString))
                return _config.ConnectionString!;

            var builder = new MySqlConnectionStringBuilder
            {
                Server = _config.Server,
                Database = _config.Database,
                UserID = _config.Username,
                Password = _config.Password,
                ConnectionTimeout = (uint)Math.Max(0, _config.Timeout)
            };

            if (_config.Port.HasValue)
                builder.Port = (uint)_config.Port.Value;

            var connStr = builder.ConnectionString;
            if (!string.IsNullOrEmpty(_config.AdditionalParameters))
                connStr += ";" + _config.AdditionalParameters!.TrimStart(';');

            return connStr;
        }

        protected override string BuildPagedSql(string sql, int offset, int pageSize)
        {
            return $"{sql} LIMIT {pageSize} OFFSET {offset}";
        }

        protected override object? ExecuteInsertCommand(DbCommand command, string primaryKeyColumn)
        {
            command.ExecuteNonQuery();
            return ((MySqlConnector.MySqlCommand)command).LastInsertedId;
        }

        protected override async Task<object?> ExecuteInsertCommandAsync(DbCommand command, string primaryKeyColumn, CancellationToken cancellationToken)
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return ((MySqlConnector.MySqlCommand)command).LastInsertedId;
        }

        /// <summary>
        /// MySqlBulkCopy (LOAD DATA LOCAL INFILE) when the connection string has <c>AllowLoadLocalInfile=true</c> and
        /// the server allows local_infile; null otherwise (BulkCopy then uses batched INSERTs)
        /// </summary>
        public override int? TryBulkCopy(string quotedTable, System.Collections.Generic.IReadOnlyList<string> columns, System.Collections.Generic.IReadOnlyList<object?[]> rows)
        {
            if (!AllowsLocalInfile())
                return null;

            var data = BulkTable(columns, rows);
            try
            {
                BeginBulk($"LOAD DATA LOCAL INFILE INTO {quotedTable} ({rows.Count} rows)");
                return NewBulkCopy(quotedTable, columns).WriteToServer(data).RowsInserted;
            }
            catch (Exception ex) when (LogFailure(ex))
            {
                throw;
            }
            catch (MySqlException ex) when (IsLocalInfileRefused(ex))
            {
                return null;
            }
            finally
            {
                CloseConnection();
            }
        }

        /// <summary>
        /// MySqlBulkCopy (async)
        /// </summary>
        public override async Task<int?> TryBulkCopyAsync(string quotedTable, System.Collections.Generic.IReadOnlyList<string> columns,
            System.Collections.Generic.IReadOnlyList<object?[]> rows, CancellationToken cancellationToken = default)
        {
            if (!AllowsLocalInfile())
                return null;

            var data = BulkTable(columns, rows);
            try
            {
                await BeginBulkAsync($"LOAD DATA LOCAL INFILE INTO {quotedTable} ({rows.Count} rows)", cancellationToken).ConfigureAwait(false);
                var result = await NewBulkCopy(quotedTable, columns).WriteToServerAsync(data, cancellationToken).ConfigureAwait(false);
                return result.RowsInserted;
            }
            catch (Exception ex) when (LogFailure(ex))
            {
                throw;
            }
            catch (MySqlException ex) when (IsLocalInfileRefused(ex))
            {
                return null;
            }
            finally
            {
                CloseConnection();
            }
        }

        private bool AllowsLocalInfile()
        {
            try
            {
                return new MySqlConnectionStringBuilder(GetConnectionString()).AllowLoadLocalInfile;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        // local_infile off on the server (3948 / 1148) or refused by the client
        private static bool IsLocalInfileRefused(MySqlException ex)
        {
            return ex.Number == 3948 || ex.Number == 1148 || ex.Number == 2068
                   || ex.Message.IndexOf("local", StringComparison.OrdinalIgnoreCase) >= 0 && ex.Message.IndexOf("infile", StringComparison.OrdinalIgnoreCase) >= 0
                   || ex.Message.IndexOf("Loading local data is disabled", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private MySqlBulkCopy NewBulkCopy(string quotedTable, System.Collections.Generic.IReadOnlyList<string> columns)
        {
            var bulk = new MySqlBulkCopy((MySqlConnection)Connection!, (MySqlTransaction?)ActiveTransaction)
            {
                DestinationTableName = quotedTable,
                BulkCopyTimeout = CommandTimeout
            };
            // MySqlBulkCopy quotes the destination column names itself
            for (int i = 0; i < columns.Count; i++)
                bulk.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(i, columns[i]));
            return bulk;
        }

        protected override void OnCommandExecuted(DbCommand command, string sql)
        {
            var head = sql?.TrimStart() ?? string.Empty;
            if (head.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase) || head.StartsWith("REPLACE", StringComparison.OrdinalIgnoreCase))
                _lastInsertId = ((MySqlConnector.MySqlCommand)command).LastInsertedId;
        }

        /// <summary>
        /// Add parameter with MySQL type
        /// </summary>
        public void ParametersAdd(string name, object? value, MySqlDbType mysqlType)
        {
            AddPendingParameter(name, cmd => ((MySqlConnector.MySqlCommand)cmd).Parameters.Add(name, mysqlType).Value = NormalizeParameterValue(value));
        }

        /// <summary>
        /// Add parameter with MySQL type and size
        /// </summary>
        public void ParametersAdd(string name, object? value, MySqlDbType mysqlType, int size)
        {
            AddPendingParameter(name, cmd => ((MySqlConnector.MySqlCommand)cmd).Parameters.Add(name, mysqlType, size).Value = NormalizeParameterValue(value));
        }

        /// <summary>
        /// Execute stored procedure
        /// </summary>
        public void RunExecuteStoredProcedure(string procedureName)
        {
            try
            {
                PrepareCommand(procedureName);
                Command!.CommandType = CommandType.StoredProcedure;
                Command!.ExecuteNonQuery();
            }
            finally
            {
                CloseConnection();
            }
        }

        /// <summary>
        /// Execute stored procedure and return DataTable
        /// </summary>
        public DataTable RunDataTableStoredProcedure(string procedureName)
        {
            try
            {
                PrepareCommand(procedureName);
                Command!.CommandType = CommandType.StoredProcedure;
                CreateDataAdapter();

                var dt = new DataTable();
                DataAdapter!.Fill(dt);
                return dt;
            }
            finally
            {
                CloseConnection();
            }
        }

        /// <summary>
        /// Get the highest primary key value (MAX). For a just inserted row use <see cref="DbCommandBase.RunInsertGetId"/>.
        /// </summary>
        public override int PKLastKeyOrDefault(string tableName, string columnName)
        {
            return RunToInt32Scaler($"SELECT IFNULL(MAX({columnName}), 0) FROM {tableName}");
        }

        /// <summary>
        /// AUTO_INCREMENT id generated by the last INSERT run through this instance
        /// (LAST_INSERT_ID() is per connection, so it is captured right after the statement)
        /// </summary>
        public long GetLastInsertId()
        {
            return _lastInsertId;
        }

        /// <summary>
        /// Execute SQL query with LIMIT (MySQL specific, memory-safe)
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
        public void RunDataReaderStreaming(string sql, Action<MySqlDataReader> action)
        {
            try
            {
                PrepareCommand(sql);

                using (var reader = MyCmd.ExecuteReader())
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
