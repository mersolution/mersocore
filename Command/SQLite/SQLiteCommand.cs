using System;
using System.Collections.Generic;
using System.Data;
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
        private SqliteConnection _sqliteConnection;
        private SqliteCommand _sqliteCommand;
        private readonly Dictionary<string, object> _pendingParameters = new Dictionary<string, object>();

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
                    ConnectionString = $"Data Source={databasePath}"
                };
            }
            else
            {
                _config = new ConnectionConfig { ConnectionString = databasePath };
            }
        }

        protected override void CreateConnection()
        {
            string connStr = BuildConnectionString();
            _sqliteConnection = new SqliteConnection(connStr);
            _sqliteConnection.Open();
            Connection = _sqliteConnection;
        }

        protected override void CreateCommand()
        {
            _sqliteCommand = _sqliteConnection.CreateCommand();
            Command = _sqliteCommand;
        }

        protected override void CreateDataAdapter()
        {
            // SQLite doesn't have a built-in DataAdapter in Microsoft.Data.Sqlite
            // We'll handle this manually in RunDataTable and RunDataset
        }

        private string BuildConnectionString()
        {
            if (!string.IsNullOrEmpty(_config.ConnectionString))
                return _config.ConnectionString;

            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = _config.Database
            };

            if (!string.IsNullOrEmpty(_config.Password))
                builder.Password = _config.Password;

            return builder.ConnectionString;
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
        /// Execute SQL query and return DataTable
        /// </summary>
        public override DataTable RunDataTable(string sql)
        {
            try
            {
                CreateConnection();
                CreateCommand();
                ApplyPendingParameters();

                _sqliteCommand.CommandText = sql;
                var dt = new DataTable();

                using (var reader = _sqliteCommand.ExecuteReader())
                {
                    dt.Load(reader);
                }

                return dt;
            }
            finally
            {
                CloseConnection();
            }
        }

        /// <summary>
        /// Execute SQL query and return DataSet
        /// </summary>
        public override DataSet RunDataset(string sql)
        {
            var ds = new DataSet();
            ds.Tables.Add(RunDataTable(sql));
            return ds;
        }

        /// <summary>
        /// Add parameter with SQLite type
        /// </summary>
        public void ParametersAdd(string name, object value, SqliteType sqliteType)
        {
            if (_sqliteCommand == null) CreateCommand();
            var param = _sqliteCommand.CreateParameter();
            param.ParameterName = name;
            param.SqliteType = sqliteType;
            param.Value = value ?? DBNull.Value;
            _sqliteCommand.Parameters.Add(param);
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
                var p = _sqliteCommand.CreateParameter();
                p.ParameterName = param.Key;
                p.Value = param.Value;
                _sqliteCommand.Parameters.Add(p);
            }
            _pendingParameters.Clear();
        }

        /// <summary>
        /// Add parameter (legacy - for compatibility)
        /// </summary>
        private void ParametersAddDirect(string name, object value)
        {
            if (_sqliteCommand == null)
            {
                CreateConnection();
                CreateCommand();
            }
            var param = _sqliteCommand.CreateParameter();
            param.ParameterName = name;
            param.Value = value ?? DBNull.Value;
            _sqliteCommand.Parameters.Add(param);
        }

        /// <summary>
        /// Get last inserted primary key value
        /// </summary>
        public override int PKLastKeyOrDefault(string tableName, string columnName)
        {
            return RunToInt32Scaler($"SELECT IFNULL(MAX({columnName}), 0) FROM {tableName}");
        }

        /// <summary>
        /// Get last inserted rowid
        /// </summary>
        public long GetLastInsertRowId()
        {
            return RunToInt64Scaler("SELECT last_insert_rowid()");
        }

        /// <summary>
        /// Execute with transaction
        /// </summary>
        public void RunExecuteWithTransaction(string sql)
        {
            SqliteTransaction transaction = null;
            try
            {
                CreateConnection();
                CreateCommand();

                transaction = _sqliteConnection.BeginTransaction();
                _sqliteCommand.Transaction = transaction;
                _sqliteCommand.CommandText = sql;
                _sqliteCommand.ExecuteNonQuery();

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
        /// Execute multiple SQL statements in a transaction
        /// </summary>
        public void RunExecuteBatch(params string[] sqlStatements)
        {
            SqliteTransaction transaction = null;
            try
            {
                CreateConnection();
                CreateCommand();

                transaction = _sqliteConnection.BeginTransaction();
                _sqliteCommand.Transaction = transaction;

                foreach (var sql in sqlStatements)
                {
                    _sqliteCommand.CommandText = sql;
                    _sqliteCommand.ExecuteNonQuery();
                }

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
        /// Execute SQL query with pagination (SQLite LIMIT OFFSET syntax)
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
        public void RunDataReaderStreaming(string sql, Action<SqliteDataReader> action)
        {
            try
            {
                CreateConnection();
                CreateCommand();
                _sqliteCommand.CommandText = sql;

                using (var reader = _sqliteCommand.ExecuteReader(CommandBehavior.SequentialAccess))
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
