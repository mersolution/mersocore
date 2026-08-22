using System;
using System.Collections.Generic;
using System.Data;
using MySqlConnector;
using mersolutionCore.Command.Abstractions;

namespace mersolutionCore.Command.MariaDB
{
    /// <summary>
    /// MariaDB database command implementation (uses MySqlConnector which supports MariaDB natively)
    /// </summary>
    public class MariaDbCommand : DbCommandBase
    {
        private readonly ConnectionConfig _config;
        private MySqlConnection _mariaConnection;
        private MySqlConnector.MySqlCommand _mariaCommand;
        private MySqlDataAdapter _mariaDataAdapter;
        private readonly Dictionary<string, object> _pendingParameters = new Dictionary<string, object>();

        public override DbProviderType ProviderType => DbProviderType.MariaDB;

        /// <summary>
        /// Create MariaDB command with connection config
        /// </summary>
        /// <param name="config">Connection configuration</param>
        public MariaDbCommand(ConnectionConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>
        /// Create MariaDB command with connection string
        /// </summary>
        /// <param name="connectionString">Connection string</param>
        public MariaDbCommand(string connectionString)
        {
            _config = new ConnectionConfig { ConnectionString = connectionString };
        }

        protected override void CreateConnection()
        {
            string connStr = BuildConnectionString();
            _mariaConnection = new MySqlConnection(connStr);
            _mariaConnection.Open();
            Connection = _mariaConnection;
        }

        protected override void CreateCommand()
        {
            _mariaCommand = new MySqlConnector.MySqlCommand();
            _mariaCommand.Connection = _mariaConnection;
            _mariaCommand.CommandTimeout = _config.Timeout;
            Command = _mariaCommand;
        }

        protected override void CreateDataAdapter()
        {
            _mariaDataAdapter = new MySqlDataAdapter(_mariaCommand);
            DataAdapter = _mariaDataAdapter;
        }

        private string BuildConnectionString()
        {
            if (!string.IsNullOrEmpty(_config.ConnectionString))
                return _config.ConnectionString;

            var builder = new MySqlConnectionStringBuilder
            {
                Server = _config.Server,
                Database = _config.Database,
                UserID = _config.Username,
                Password = _config.Password,
                ConnectionTimeout = (uint)_config.Timeout
            };

            if (_config.Port.HasValue)
                builder.Port = (uint)_config.Port.Value;

            return builder.ConnectionString;
        }

        /// <summary>
        /// Add parameter with MySQL type
        /// </summary>
        public void ParametersAdd(string name, object value, MySqlDbType mysqlType)
        {
            if (_mariaCommand == null) CreateCommand();
            _mariaCommand.Parameters.Add(name, mysqlType).Value = value ?? DBNull.Value;
        }

        /// <summary>
        /// Add parameter with MySQL type and size
        /// </summary>
        public void ParametersAdd(string name, object value, MySqlDbType mysqlType, int size)
        {
            if (_mariaCommand == null) CreateCommand();
            _mariaCommand.Parameters.Add(name, mysqlType, size).Value = value ?? DBNull.Value;
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
                _mariaCommand.Parameters.AddWithValue(param.Key, param.Value);
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
        /// Execute stored procedure
        /// </summary>
        public void RunExecuteStoredProcedure(string procedureName)
        {
            try
            {
                CreateConnection();
                CreateCommand();
                _mariaCommand.CommandText = procedureName;
                _mariaCommand.CommandType = CommandType.StoredProcedure;
                _mariaCommand.ExecuteNonQuery();
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

                _mariaCommand.CommandText = procedureName;
                _mariaCommand.CommandType = CommandType.StoredProcedure;

                DataTable dt = new DataTable();
                _mariaDataAdapter.Fill(dt);

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
            return RunToInt32Scaler($"SELECT IFNULL(MAX({columnName}), 0) FROM {tableName}");
        }

        /// <summary>
        /// Get last inserted auto increment ID
        /// </summary>
        public long GetLastInsertId()
        {
            return RunToInt64Scaler("SELECT LAST_INSERT_ID()");
        }

        /// <summary>
        /// Execute with transaction
        /// </summary>
        public void RunExecuteWithTransaction(string sql)
        {
            MySqlTransaction transaction = null;
            try
            {
                CreateConnection();
                CreateCommand();

                transaction = _mariaConnection.BeginTransaction();
                _mariaCommand.Transaction = transaction;
                _mariaCommand.CommandText = sql;
                _mariaCommand.ExecuteNonQuery();

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
        /// Execute SQL query and return DataSet using DataReader (memory-efficient)
        /// </summary>
        public override DataSet RunDataset(string sql)
        {
            var ds = new DataSet();
            ds.Tables.Add(RunDataTable(sql));
            return ds;
        }

        /// <summary>
        /// Execute SQL query with LIMIT (MariaDB specific, memory-safe)
        /// </summary>
        public DataTable RunDataTableLimit(string sql, int limitCount)
        {
            if (limitCount < 1) limitCount = 1000;
            return RunDataTable($"{sql} LIMIT {limitCount}");
        }

        /// <summary>
        /// Execute SQL query with pagination (MariaDB LIMIT OFFSET syntax)
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
        public void RunDataReaderStreaming(string sql, Action<MySqlDataReader> action)
        {
            try
            {
                CreateConnection();
                CreateCommand();
                _mariaCommand.CommandText = sql;

                using (var reader = _mariaCommand.ExecuteReader(CommandBehavior.SequentialAccess))
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
