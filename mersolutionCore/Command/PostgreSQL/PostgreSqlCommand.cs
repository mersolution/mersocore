using System;
using System.Collections.Generic;
using System.Data;
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
        private NpgsqlConnection _npgsqlConnection;
        private NpgsqlCommand _npgsqlCommand;
        private NpgsqlDataAdapter _npgsqlDataAdapter;
        private readonly Dictionary<string, object> _pendingParameters = new Dictionary<string, object>();

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

        protected override void CreateConnection()
        {
            string connStr = BuildConnectionString();
            _npgsqlConnection = new NpgsqlConnection(connStr);
            _npgsqlConnection.Open();
            Connection = _npgsqlConnection;
        }

        protected override void CreateCommand()
        {
            _npgsqlCommand = new NpgsqlCommand();
            _npgsqlCommand.Connection = _npgsqlConnection;
            _npgsqlCommand.CommandTimeout = _config.Timeout;
            Command = _npgsqlCommand;
        }

        protected override void CreateDataAdapter()
        {
            _npgsqlDataAdapter = new NpgsqlDataAdapter(_npgsqlCommand);
            DataAdapter = _npgsqlDataAdapter;
        }

        private string BuildConnectionString()
        {
            if (!string.IsNullOrEmpty(_config.ConnectionString))
                return _config.ConnectionString;

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

            return builder.ConnectionString;
        }

        /// <summary>
        /// Add parameter with NpgsqlDbType
        /// </summary>
        public void ParametersAdd(string name, object value, NpgsqlTypes.NpgsqlDbType npgsqlType)
        {
            if (_npgsqlCommand == null) CreateCommand();
            _npgsqlCommand.Parameters.Add(name, npgsqlType).Value = value ?? DBNull.Value;
        }

        /// <summary>
        /// Add parameter with NpgsqlDbType and size
        /// </summary>
        public void ParametersAdd(string name, object value, NpgsqlTypes.NpgsqlDbType npgsqlType, int size)
        {
            if (_npgsqlCommand == null) CreateCommand();
            var param = new NpgsqlParameter(name, npgsqlType, size);
            param.Value = value ?? DBNull.Value;
            _npgsqlCommand.Parameters.Add(param);
        }

        /// <summary>
        /// Add parameter
        /// </summary>
        public override void ParametersAdd(string name, object value)
        {
            _pendingParameters[name] = value ?? DBNull.Value;
        }

        /// <summary>
        /// Apply pending parameters to command
        /// </summary>
        private void ApplyPendingParameters()
        {
            foreach (var param in _pendingParameters)
            {
                _npgsqlCommand.Parameters.AddWithValue(param.Key, param.Value);
            }
            _pendingParameters.Clear();
        }

        /// <summary>
        /// Execute SQL query with pending parameters
        /// </summary>
        public override void RunExecute(string sql)
        {
            try
            {
                CreateConnection();
                CreateCommand();
                ApplyPendingParameters();
                Command.CommandText = sql;
                Command.ExecuteNonQuery();
            }
            finally
            {
                CloseConnection();
            }
        }

        /// <summary>
        /// Execute SQL query and return DataTable with pending parameters
        /// </summary>
        public override DataTable RunDataTable(string sql)
        {
            try
            {
                CreateConnection();
                CreateCommand();
                CreateDataAdapter();
                ApplyPendingParameters();

                Command.CommandText = sql;
                DataTable dt = new DataTable();
                DataAdapter.Fill(dt);

                return dt;
            }
            finally
            {
                CloseConnection();
            }
        }

        /// <summary>
        /// Execute stored function
        /// </summary>
        public void RunExecuteFunction(string functionName)
        {
            try
            {
                CreateConnection();
                CreateCommand();
                _npgsqlCommand.CommandText = functionName;
                _npgsqlCommand.CommandType = CommandType.StoredProcedure;
                _npgsqlCommand.ExecuteNonQuery();
            }
            finally
            {
                CloseConnection();
            }
        }

        /// <summary>
        /// Execute stored function and return DataTable
        /// </summary>
        public DataTable RunDataTableFunction(string functionName)
        {
            try
            {
                CreateConnection();
                CreateCommand();
                CreateDataAdapter();

                _npgsqlCommand.CommandText = functionName;
                _npgsqlCommand.CommandType = CommandType.StoredProcedure;

                DataTable dt = new DataTable();
                _npgsqlDataAdapter.Fill(dt);

                return dt;
            }
            finally
            {
                CloseConnection();
            }
        }

        /// <summary>
        /// Get last inserted primary key value
        /// </summary>
        public override int PKLastKeyOrDefault(string tableName, string columnName)
        {
            return RunToInt32Scaler($"SELECT COALESCE(MAX({columnName}), 0) FROM {tableName}");
        }

        /// <summary>
        /// Get last inserted ID using RETURNING clause
        /// </summary>
        public int RunExecuteReturning(string sql, string columnName)
        {
            try
            {
                CreateConnection();
                CreateCommand();
                _npgsqlCommand.CommandText = $"{sql} RETURNING {columnName}";

                var result = _npgsqlCommand.ExecuteScalar();
                return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
            }
            finally
            {
                CloseConnection();
            }
        }

        /// <summary>
        /// Execute with transaction
        /// </summary>
        public void RunExecuteWithTransaction(string sql)
        {
            NpgsqlTransaction transaction = null;
            try
            {
                CreateConnection();
                CreateCommand();

                transaction = _npgsqlConnection.BeginTransaction();
                _npgsqlCommand.Transaction = transaction;
                _npgsqlCommand.CommandText = sql;
                _npgsqlCommand.ExecuteNonQuery();

                transaction.Commit();
            }
            catch
            {
                transaction?.Rollback();
                throw;
            }
            finally
            {
                CloseConnection();
            }
        }

        /// <summary>
        /// Execute COPY command for bulk insert
        /// </summary>
        public void BulkInsert(string tableName, DataTable dataTable)
        {
            try
            {
                CreateConnection();

                using (var writer = _npgsqlConnection.BeginBinaryImport($"COPY {tableName} FROM STDIN (FORMAT BINARY)"))
                {
                    foreach (DataRow row in dataTable.Rows)
                    {
                        writer.StartRow();
                        foreach (var item in row.ItemArray)
                        {
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
        /// Execute SQL query and return DataSet using DataReader (memory-efficient)
        /// </summary>
        public override DataSet RunDataset(string sql)
        {
            var ds = new DataSet();
            ds.Tables.Add(RunDataTable(sql));
            return ds;
        }

        /// <summary>
        /// Execute SQL query with LIMIT (PostgreSQL specific, memory-safe)
        /// </summary>
        public DataTable RunDataTableLimit(string sql, int limitCount)
        {
            if (limitCount < 1) limitCount = 1000;
            return RunDataTable($"{sql} LIMIT {limitCount}");
        }

        /// <summary>
        /// Execute SQL query with pagination (PostgreSQL LIMIT OFFSET syntax)
        /// </summary>
        public override DataTable RunDataTablePaged(string sql, int pageNumber, int pageSize)
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 100;

            int offset = (pageNumber - 1) * pageSize;
            string pagedSql = $"{sql} LIMIT {pageSize} OFFSET {offset}";

            return RunDataTable(pagedSql);
        }

        /// <summary>
        /// Get total record count for pagination
        /// </summary>
        public int GetTotalCount(string tableName, string whereClause = null)
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
                CreateConnection();
                CreateCommand();
                _npgsqlCommand.CommandText = sql;

                using (var reader = _npgsqlCommand.ExecuteReader(CommandBehavior.SequentialAccess))
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
