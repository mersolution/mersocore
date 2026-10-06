using System;
using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using mersolutionCore.Command.Abstractions;

namespace mersolutionCore.Command.SqlServer
{
    /// <summary>
    /// SQL Server database command implementation
    /// </summary>
    public class SqlServerCommand : DbCommandBase
    {
        private readonly ConnectionConfig _config;

        public override DbProviderType ProviderType => DbProviderType.SqlServer;

        /// <summary>
        /// Create SQL Server command with connection config
        /// </summary>
        /// <param name="config">Connection configuration</param>
        public SqlServerCommand(ConnectionConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>
        /// Create SQL Server command with connection string
        /// </summary>
        /// <param name="connectionString">Connection string</param>
        public SqlServerCommand(string connectionString)
        {
            _config = new ConnectionConfig { ConnectionString = connectionString };
        }

        private SqlCommand SqlCmd => (SqlCommand)Command!;

        protected override int CommandTimeout => _config.Timeout;

        protected override DbConnection NewConnection() => new SqlConnection(GetConnectionString());

        protected override void CreateDataAdapter()
        {
            DataAdapter = new SqlDataAdapter(SqlCmd);
        }

        protected override string GetConnectionString()
        {
            if (!string.IsNullOrEmpty(_config.ConnectionString))
                return _config.ConnectionString!;

            var builder = new SqlConnectionStringBuilder
            {
                DataSource = _config.Port.HasValue ? $"{_config.Server},{_config.Port.Value}" : _config.Server,
                InitialCatalog = _config.Database,
                ConnectTimeout = _config.Timeout
            };

            if (_config.IntegratedSecurity)
            {
                builder.IntegratedSecurity = true;
            }
            else
            {
                builder.UserID = _config.Username;
                builder.Password = _config.Password;
            }

            builder.TrustServerCertificate = true;

            var connStr = builder.ConnectionString;
            if (!string.IsNullOrEmpty(_config.AdditionalParameters))
                connStr += ";" + _config.AdditionalParameters!.TrimStart(';');

            return connStr;
        }

        protected override string InsertWithIdentity(string insertSql, string primaryKeyColumn)
        {
            return insertSql + "; SELECT CAST(SCOPE_IDENTITY() AS BIGINT);";
        }

        /// <summary>
        /// SqlBulkCopy (columns mapped by name, joins the active transaction)
        /// </summary>
        public override int? TryBulkCopy(string quotedTable, System.Collections.Generic.IReadOnlyList<string> columns, System.Collections.Generic.IReadOnlyList<object?[]> rows)
        {
            var data = BulkTable(columns, rows);
            try
            {
                BeginBulk($"INSERT BULK {quotedTable} ({rows.Count} rows)");
                using (var bulk = NewBulkCopy(quotedTable, columns))
                {
                    bulk.WriteToServer(data);
                }
                return rows.Count;
            }
            catch (Exception ex) when (LogFailure(ex))
            {
                throw;
            }
            finally
            {
                CloseConnection();
            }
        }

        /// <summary>
        /// SqlBulkCopy (async)
        /// </summary>
        public override async System.Threading.Tasks.Task<int?> TryBulkCopyAsync(string quotedTable, System.Collections.Generic.IReadOnlyList<string> columns,
            System.Collections.Generic.IReadOnlyList<object?[]> rows, System.Threading.CancellationToken cancellationToken = default)
        {
            var data = BulkTable(columns, rows);
            try
            {
                await BeginBulkAsync($"INSERT BULK {quotedTable} ({rows.Count} rows)", cancellationToken).ConfigureAwait(false);
                using (var bulk = NewBulkCopy(quotedTable, columns))
                {
                    await bulk.WriteToServerAsync(data, cancellationToken).ConfigureAwait(false);
                }
                return rows.Count;
            }
            catch (Exception ex) when (LogFailure(ex))
            {
                throw;
            }
            finally
            {
                CloseConnection();
            }
        }

        private SqlBulkCopy NewBulkCopy(string quotedTable, System.Collections.Generic.IReadOnlyList<string> columns)
        {
            var bulk = new SqlBulkCopy((SqlConnection)Connection!, SqlBulkCopyOptions.Default, (SqlTransaction?)ActiveTransaction)
            {
                DestinationTableName = quotedTable,
                BatchSize = 5000,
                BulkCopyTimeout = CommandTimeout
            };
            foreach (var column in columns)
                bulk.ColumnMappings.Add(column, column);
            return bulk;
        }

        /// <summary>
        /// Add parameter with SQL type
        /// </summary>
        public void ParametersAdd(string name, object? value, SqlDbType sqlType)
        {
            AddPendingParameter(name, cmd => ((SqlCommand)cmd).Parameters.Add(name, sqlType).Value = NormalizeParameterValue(value));
        }

        /// <summary>
        /// Add parameter with SQL type and size
        /// </summary>
        public void ParametersAdd(string name, object? value, SqlDbType sqlType, int size)
        {
            AddPendingParameter(name, cmd => ((SqlCommand)cmd).Parameters.Add(name, sqlType, size).Value = NormalizeParameterValue(value));
        }

        /// <summary>
        /// Add output parameter (read it with <see cref="ParameterReturnOutput"/> after the call)
        /// </summary>
        public void ParametersOutputAdd(string name, SqlDbType sqlType)
        {
            AddPendingParameter(name, cmd => ((SqlCommand)cmd).Parameters.Add(name, sqlType).Direction = ParameterDirection.Output);
        }

        /// <summary>
        /// Add output parameter with size (needed for NVARCHAR/VARCHAR outputs)
        /// </summary>
        public void ParametersOutputAdd(string name, SqlDbType sqlType, int size)
        {
            AddPendingParameter(name, cmd => ((SqlCommand)cmd).Parameters.Add(name, sqlType, size).Direction = ParameterDirection.Output);
        }

        /// <summary>
        /// Get output parameter value of the last executed command
        /// </summary>
        public object? ParameterReturnOutput(string name)
        {
            if (Command == null || !Command.Parameters.Contains(name))
                return null;

            var value = Command!.Parameters[name].Value;
            return value == DBNull.Value ? null : value;
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
            return RunToInt32Scaler($"SELECT ISNULL(MAX({columnName}), 0) FROM {tableName} WITH (NOLOCK)");
        }

        /// <summary>
        /// Return the rows where <paramref name="columnName"/> equals <paramref name="value"/>, or null (dirty read, NOLOCK)
        /// </summary>
        public override DataTable? FindOrDefault(string columnName, object? value, string tableName)
        {
            ParametersAdd("@__value", value);
            var dt = RunDataTable($"SELECT * FROM {tableName} WITH (NOLOCK) WHERE {columnName} = @__value");
            return dt.Rows.Count == 0 ? null : dt;
        }

        /// <summary>
        /// Execute SQL query with TOP limit (SQL Server specific, memory-safe)
        /// </summary>
        public DataTable RunDataTableTop(string sql, int topCount)
        {
            if (topCount < 1) topCount = 1000;

            // Insert TOP after SELECT [DISTINCT]
            string limitedSql = sql.Trim();
            if (limitedSql.StartsWith("SELECT DISTINCT ", StringComparison.OrdinalIgnoreCase))
            {
                limitedSql = "SELECT DISTINCT TOP " + topCount + " " + limitedSql.Substring(16);
            }
            else if (limitedSql.StartsWith("SELECT ", StringComparison.OrdinalIgnoreCase))
            {
                limitedSql = "SELECT TOP " + topCount + " " + limitedSql.Substring(7);
            }

            return RunDataTable(limitedSql);
        }

        /// <summary>
        /// Get total record count for pagination (<paramref name="whereClause"/> is raw SQL — never pass user input)
        /// </summary>
        public int GetTotalCount(string tableName, string? whereClause = null)
        {
            string sql = $"SELECT COUNT(*) FROM {tableName} WITH (NOLOCK)";
            if (!string.IsNullOrEmpty(whereClause))
            {
                sql += $" WHERE {whereClause}";
            }
            return RunToInt32Scaler(sql);
        }

        /// <summary>
        /// Execute SQL query with cursor-based streaming (for very large datasets)
        /// </summary>
        public void RunDataReaderStreaming(string sql, Action<SqlDataReader> action)
        {
            try
            {
                PrepareCommand(sql);

                using (var reader = SqlCmd.ExecuteReader())
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
