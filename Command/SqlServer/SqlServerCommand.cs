using System;
using System.Collections.Generic;
using System.Data;
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
        private SqlConnection _sqlConnection;
        private SqlCommand _sqlCommand;
        private SqlDataAdapter _sqlDataAdapter;
        private readonly Dictionary<string, object> _pendingParameters = new Dictionary<string, object>();

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

        protected override void CreateConnection()
        {
            string connStr = BuildConnectionString();
            _sqlConnection = new SqlConnection(connStr);
            _sqlConnection.Open();
            Connection = _sqlConnection;
        }

        protected override void CreateCommand()
        {
            _sqlCommand = new SqlCommand();
            _sqlCommand.Connection = _sqlConnection;
            _sqlCommand.CommandTimeout = _config.Timeout;
            Command = _sqlCommand;
        }

        protected override void CreateDataAdapter()
        {
            _sqlDataAdapter = new SqlDataAdapter(_sqlCommand);
            DataAdapter = _sqlDataAdapter;
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

        private string BuildConnectionString()
        {
            if (!string.IsNullOrEmpty(_config.ConnectionString))
                return _config.ConnectionString;

            var builder = new SqlConnectionStringBuilder
            {
                DataSource = _config.Server,
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

            return builder.ConnectionString;
        }

        /// <summary>
        /// Add parameter with SQL type
        /// </summary>
        public void ParametersAdd(string name, object value, SqlDbType sqlType)
        {
            if (_sqlCommand == null) CreateCommand();
            _sqlCommand.Parameters.Add(name, sqlType).Value = value ?? DBNull.Value;
        }

        /// <summary>
        /// Add parameter with SQL type and size
        /// </summary>
        public void ParametersAdd(string name, object value, SqlDbType sqlType, int size)
        {
            if (_sqlCommand == null) CreateCommand();
            _sqlCommand.Parameters.Add(name, sqlType, size).Value = value ?? DBNull.Value;
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
                _sqlCommand.Parameters.AddWithValue(param.Key, param.Value);
            }
            _pendingParameters.Clear();
        }

        /// <summary>
        /// Add output parameter
        /// </summary>
        public void ParametersOutputAdd(string name, SqlDbType sqlType)
        {
            if (_sqlCommand == null) CreateCommand();
            _sqlCommand.Parameters.Add(name, sqlType).Direction = ParameterDirection.Output;
        }

        /// <summary>
        /// Get output parameter value
        /// </summary>
        public object ParameterReturnOutput(string name)
        {
            return _sqlCommand.Parameters[name].Value;
        }

        /// <summary>
        /// Execute stored procedure
        /// </summary>
        public void RunExecuteStoredProcedure(string procedureName)
        {
            try
            {
                CreateConnection();
                CreateCommand();
                _sqlCommand.CommandText = procedureName;
                _sqlCommand.CommandType = CommandType.StoredProcedure;
                _sqlCommand.ExecuteNonQuery();
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
                CreateConnection();
                CreateCommand();
                CreateDataAdapter();

                _sqlCommand.CommandText = procedureName;
                _sqlCommand.CommandType = CommandType.StoredProcedure;

                DataTable dt = new DataTable();
                _sqlDataAdapter.Fill(dt);

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
            return RunToInt32Scaler($"SELECT ISNULL(MAX({columnName}), 0) FROM {tableName} WITH (NOLOCK)");
        }

        /// <summary>
        /// Execute with transaction
        /// </summary>
        public void RunExecuteWithTransaction(string sql)
        {
            SqlTransaction transaction = null;
            try
            {
                CreateConnection();
                CreateCommand();

                transaction = _sqlConnection.BeginTransaction();
                _sqlCommand.Transaction = transaction;
                _sqlCommand.CommandText = sql;
                _sqlCommand.ExecuteNonQuery();

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
        /// Check if record exists (optimized for SQL Server)
        /// </summary>
        public override DataTable FindOrDefault(string columnName, object value, string tableName)
        {
            string valueStr = value is string ? $"'{value}'" : value.ToString();
            int count = RunToInt32Scaler($"SELECT ISNULL(COUNT(*), 0) FROM {tableName} WITH (NOLOCK) WHERE {columnName} = {valueStr}");

            if (count == 0)
                return null;

            return RunDataTable($"SELECT * FROM {tableName} WITH (NOLOCK) WHERE {columnName} = {valueStr}");
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
        /// Execute SQL query with TOP limit (SQL Server specific, memory-safe)
        /// </summary>
        public DataTable RunDataTableTop(string sql, int topCount)
        {
            if (topCount < 1) topCount = 1000;

            // Insert TOP after SELECT
            string limitedSql = sql.Trim();
            if (limitedSql.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                limitedSql = "SELECT TOP " + topCount + " " + limitedSql.Substring(6);
            }

            return RunDataTable(limitedSql);
        }

        /// <summary>
        /// Execute SQL query with pagination (SQL Server 2012+ syntax)
        /// Requires ORDER BY clause in the original SQL
        /// </summary>
        public override DataTable RunDataTablePaged(string sql, int pageNumber, int pageSize)
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 100;

            int offset = (pageNumber - 1) * pageSize;
            
            // SQL Server 2012+ OFFSET FETCH syntax
            string pagedSql = $"{sql} OFFSET {offset} ROWS FETCH NEXT {pageSize} ROWS ONLY";

            return RunDataTable(pagedSql);
        }

        /// <summary>
        /// Get total record count for pagination
        /// </summary>
        public int GetTotalCount(string tableName, string whereClause = null)
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
                CreateConnection();
                CreateCommand();
                _sqlCommand.CommandText = sql;

                using (var reader = _sqlCommand.ExecuteReader(CommandBehavior.SequentialAccess))
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
