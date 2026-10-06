using System;
using System.Data;
using System.Data.Common;
using System.Linq;
using Npgsql;
using mersolutionCore.Command.Abstractions;

namespace mersolutionCore.Command.PostgreSQL
{
    /// <summary>
    /// PostgreSQL database command implementation
    /// </summary>
    public class PostgreSqlCommand : DbCommandBase
    {
        private readonly ConnectionConfig _config;

        public override DbProviderType ProviderType => DbProviderType.PostgreSQL;

        /// <summary>
        /// Create PostgreSQL command with connection config
        /// </summary>
        /// <param name="config">Connection configuration</param>
        public PostgreSqlCommand(ConnectionConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>
        /// Create PostgreSQL command with connection string
        /// </summary>
        /// <param name="connectionString">Connection string</param>
        public PostgreSqlCommand(string connectionString)
        {
            _config = new ConnectionConfig { ConnectionString = connectionString };
        }

        private NpgsqlCommand NpgCmd => (NpgsqlCommand)Command!;

        protected override int CommandTimeout => _config.Timeout;

        protected override DbConnection NewConnection() => new NpgsqlConnection(GetConnectionString());

        protected override void CreateDataAdapter()
        {
            DataAdapter = new NpgsqlDataAdapter(NpgCmd);
        }

        protected override string GetConnectionString()
        {
            if (!string.IsNullOrEmpty(_config.ConnectionString))
                return _config.ConnectionString!;

            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = _config.Server,
                Database = _config.Database,
                Username = _config.Username,
                Password = _config.Password,
                Timeout = _config.Timeout
            };

            if (_config.Port.HasValue)
                builder.Port = _config.Port.Value;

            var connStr = builder.ConnectionString;
            if (!string.IsNullOrEmpty(_config.AdditionalParameters))
                connStr += ";" + _config.AdditionalParameters!.TrimStart(';');

            return connStr;
        }

        protected override string InsertWithIdentity(string insertSql, string primaryKeyColumn)
        {
            return $"{insertSql} RETURNING {primaryKeyColumn}";
        }

        /// <summary>
        /// Add parameter with NpgsqlDbType
        /// </summary>
        public void ParametersAdd(string name, object? value, NpgsqlTypes.NpgsqlDbType npgsqlType)
        {
            AddPendingParameter(name, cmd => ((NpgsqlCommand)cmd).Parameters.Add(name, npgsqlType).Value = NormalizeParameterValue(value));
        }

        /// <summary>
        /// Add parameter with NpgsqlDbType and size
        /// </summary>
        public void ParametersAdd(string name, object? value, NpgsqlTypes.NpgsqlDbType npgsqlType, int size)
        {
            AddPendingParameter(name, cmd =>
            {
                var param = new NpgsqlParameter(name, npgsqlType, size) { Value = NormalizeParameterValue(value) };
                ((NpgsqlCommand)cmd).Parameters.Add(param);
            });
        }

        /// <summary>
        /// Build "fn(@a, @b)" from the queued parameters unless the caller already wrote the argument list
        /// </summary>
        private string FunctionCall(string functionName)
        {
            if (functionName.Contains("("))
                return functionName;

            return $"{functionName}({string.Join(", ", PendingParameterNames)})";
        }

        /// <summary>
        /// Execute stored function (SELECT fn(...)). Arguments are the queued parameters in insertion order.
        /// </summary>
        public void RunExecuteFunction(string functionName)
        {
            try
            {
                PrepareCommand($"SELECT {FunctionCall(functionName)}");
                Command!.ExecuteNonQuery();
            }
            finally
            {
                CloseConnection();
            }
        }

        /// <summary>
        /// Execute stored function and return its result set as DataTable (SELECT * FROM fn(...))
        /// </summary>
        public DataTable RunDataTableFunction(string functionName)
        {
            return RunDataTable($"SELECT * FROM {FunctionCall(functionName)}");
        }

        /// <summary>
        /// Execute a stored PROCEDURE (CALL proc(...))
        /// </summary>
        public void RunExecuteProcedure(string procedureName)
        {
            try
            {
                PrepareCommand($"CALL {FunctionCall(procedureName)}");
                Command!.ExecuteNonQuery();
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
            return RunToInt32Scaler($"SELECT COALESCE(MAX({columnName}), 0) FROM {tableName}");
        }

        /// <summary>
        /// Execute INSERT ... RETURNING column and return the value
        /// </summary>
        public int RunExecuteReturning(string sql, string columnName)
        {
            return (int)RunInsertGetId(sql, columnName);
        }

        /// <summary>
        /// COPY ... FROM STDIN (FORMAT csv): PostgreSQL parses every value into its column type, so int / bigint,
        /// numeric, dates and enums need no exact binary types. Joins the active transaction.
        /// </summary>
        public override int? TryBulkCopy(string quotedTable, System.Collections.Generic.IReadOnlyList<string> columns, System.Collections.Generic.IReadOnlyList<object?[]> rows)
        {
            try
            {
                BeginBulk(CopySql(quotedTable, columns));
                using (var writer = ((NpgsqlConnection)Connection!).BeginTextImport(CopySql(quotedTable, columns)))
                {
                    foreach (var row in rows)
                        writer.Write(CsvLine(row));
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
        /// COPY ... FROM STDIN (FORMAT csv) (async)
        /// </summary>
        public override async System.Threading.Tasks.Task<int?> TryBulkCopyAsync(string quotedTable, System.Collections.Generic.IReadOnlyList<string> columns,
            System.Collections.Generic.IReadOnlyList<object?[]> rows, System.Threading.CancellationToken cancellationToken = default)
        {
            try
            {
                await BeginBulkAsync(CopySql(quotedTable, columns), cancellationToken).ConfigureAwait(false);
                using (var writer = await ((NpgsqlConnection)Connection!).BeginTextImportAsync(CopySql(quotedTable, columns), cancellationToken).ConfigureAwait(false))
                {
                    foreach (var row in rows)
                        await writer.WriteAsync(CsvLine(row)).ConfigureAwait(false);
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

        private static string CopySql(string quotedTable, System.Collections.Generic.IReadOnlyList<string> columns)
        {
            var list = string.Join(", ", columns.Select(c => mersolutionCore.ORM.SqlDialect.Column(DbProviderType.PostgreSQL, c)));
            return $"COPY {quotedTable} ({list}) FROM STDIN (FORMAT csv)";
        }

        // One CSV line: NULL = empty field, every value quoted ("" inside), PostgreSQL text formats
        private static string CsvLine(object?[] row)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < row.Length; i++)
            {
                if (i > 0) sb.Append(',');
                var text = CsvText(BulkValue(row[i]));
                if (text != null)
                    sb.Append('"').Append(text.Replace("\"", "\"\"")).Append('"');
            }
            return sb.Append('\n').ToString();
        }

        private static string? CsvText(object value)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            switch (value)
            {
                case DBNull _:
                    return null;
                case bool b:
                    return b ? "t" : "f";
                case DateTime dt:
                    return dt.ToString("yyyy-MM-dd HH:mm:ss.ffffff", inv) + (dt.Kind == DateTimeKind.Utc ? "+00" : string.Empty);
                case DateTimeOffset dto:
                    return dto.ToString("yyyy-MM-dd HH:mm:ss.ffffffzzz", inv);
                case TimeSpan ts:
                    return ts.ToString("c", inv);
                case byte[] bytes:
                    return "\\x" + BitConverter.ToString(bytes).Replace("-", string.Empty);
                case double d:
                    return d.ToString("R", inv);
                case float f:
                    return f.ToString("R", inv);
            }

            var type = value.GetType();
            if (type.FullName == "System.DateOnly")
                return ((IFormattable)value).ToString("yyyy-MM-dd", inv);
            if (type.FullName == "System.TimeOnly")
                return ((IFormattable)value).ToString("HH:mm:ss.fffffff", inv);
            return value is IFormattable formattable ? formattable.ToString(null, inv) : value.ToString();
        }

        /// <summary>
        /// Execute binary COPY for bulk insert (DataTable columns must be in table column order and match the
        /// column types exactly). <c>BulkOperations.BulkCopy</c> uses the text COPY instead.
        /// </summary>
        public void BulkInsert(string tableName, DataTable dataTable)
        {
            try
            {
                OpenConnection();
                var connection = (NpgsqlConnection)Connection!;

                using (var writer = connection.BeginBinaryImport($"COPY {tableName} FROM STDIN (FORMAT BINARY)"))
                {
                    foreach (DataRow row in dataTable.Rows)
                    {
                        writer.StartRow();
                        foreach (var item in row.ItemArray)
                        {
                            if (item == null || item == DBNull.Value)
                                writer.WriteNull();
                            else
                                writer.Write(item);
                        }
                    }
                    writer.Complete();
                }
            }
            finally
            {
                CloseConnection();
            }
        }

        /// <summary>
        /// Execute SQL query with LIMIT (PostgreSQL specific, memory-safe)
        /// </summary>
        public DataTable RunDataTableLimit(string sql, int limitCount)
        {
            if (limitCount < 1) limitCount = 1000;
            return RunDataTable($"{sql} LIMIT {limitCount}");
        }

        protected override string BuildPagedSql(string sql, int offset, int pageSize)
        {
            return $"{sql} LIMIT {pageSize} OFFSET {offset}";
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
        public void RunDataReaderStreaming(string sql, Action<NpgsqlDataReader> action)
        {
            try
            {
                PrepareCommand(sql);

                using (var reader = NpgCmd.ExecuteReader())
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
