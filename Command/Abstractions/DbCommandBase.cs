using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;

namespace mersolutionCore.Command.Abstractions
{
    /// <summary>
    /// Abstract base class for all database command implementations
    /// </summary>
    public abstract partial class DbCommandBase : IDbCommand, IDisposable
    {
        protected DbConnection Connection;
        protected DbCommand Command;
        protected DbDataAdapter DataAdapter;
        protected bool _disposed = false;

        /// <summary>
        /// Database provider type
        /// </summary>
        public abstract DbProviderType ProviderType { get; }

        /// <summary>
        /// Create and open database connection
        /// </summary>
        protected abstract void CreateConnection();

        /// <summary>
        /// Create command object
        /// </summary>
        protected abstract void CreateCommand();

        /// <summary>
        /// Create data adapter
        /// </summary>
        protected abstract void CreateDataAdapter();

        /// <summary>
        /// Execute SQL query (INSERT, UPDATE, DELETE)
        /// </summary>
        public virtual void RunExecute(string sql)
        {
            try
            {
                CreateConnection();
                CreateCommand();
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
        public virtual DataTable RunDataTable(string sql)
        {
            try
            {
                CreateConnection();
                CreateCommand();
                CreateDataAdapter();

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
        /// Execute SQL query and return DataSet
        /// </summary>
        public virtual DataSet RunDataset(string sql)
        {
            try
            {
                CreateConnection();
                CreateCommand();
                CreateDataAdapter();

                Command.CommandText = sql;
                DataSet ds = new DataSet();
                DataAdapter.Fill(ds);

                return ds;
            }
            finally
            {
                CloseConnection();
            }
        }

        /// <summary>
        /// Execute SQL query and return string scalar value
        /// </summary>
        public virtual string RunToStringScaler(string sql)
        {
            try
            {
                CreateConnection();
                CreateCommand();
                Command.CommandText = sql;

                var result = Command.ExecuteScalar();
                return result?.ToString() ?? string.Empty;
            }
            finally
            {
                CloseConnection();
            }
        }

        /// <summary>
        /// Execute SQL query and return Int32 scalar value
        /// </summary>
        public virtual int RunToInt32Scaler(string sql)
        {
            try
            {
                CreateConnection();
                CreateCommand();
                Command.CommandText = sql;

                var result = Command.ExecuteScalar();
                return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
            }
            finally
            {
                CloseConnection();
            }
        }

        /// <summary>
        /// Execute SQL query and return Int64 scalar value
        /// </summary>
        public virtual long RunToInt64Scaler(string sql)
        {
            try
            {
                CreateConnection();
                CreateCommand();
                Command.CommandText = sql;

                var result = Command.ExecuteScalar();
                return result != null && result != DBNull.Value ? Convert.ToInt64(result) : 0;
            }
            finally
            {
                CloseConnection();
            }
        }

        /// <summary>
        /// Execute SQL query and return decimal scalar value
        /// </summary>
        public virtual decimal RunToDecimalScaler(string sql)
        {
            try
            {
                CreateConnection();
                CreateCommand();
                Command.CommandText = sql;

                var result = Command.ExecuteScalar();
                return result != null && result != DBNull.Value ? Convert.ToDecimal(result) : 0;
            }
            finally
            {
                CloseConnection();
            }
        }

        /// <summary>
        /// Execute SQL query and return object scalar value
        /// </summary>
        public virtual object RunToObjectScaler(string sql)
        {
            try
            {
                CreateConnection();
                CreateCommand();
                Command.CommandText = sql;

                var result = Command.ExecuteScalar();
                return result == DBNull.Value ? null : result;
            }
            finally
            {
                CloseConnection();
            }
        }

        /// <summary>
        /// Add parameter to command
        /// </summary>
        public abstract void ParametersAdd(string name, object value);

        /// <summary>
        /// Clear all parameters
        /// </summary>
        public virtual void ParametersClear()
        {
            Command?.Parameters.Clear();
        }

        /// <summary>
        /// Check if record exists
        /// </summary>
        public virtual DataTable FindOrDefault(string columnName, object value, string tableName)
        {
            string valueStr = value is string ? $"'{value}'" : value.ToString();
            int count = RunToInt32Scaler($"SELECT COUNT(*) FROM {tableName} WHERE {columnName} = {valueStr}");

            if (count == 0)
                return null;

            return RunDataTable($"SELECT * FROM {tableName} WHERE {columnName} = {valueStr}");
        }

        /// <summary>
        /// Get last inserted primary key value
        /// </summary>
        public abstract int PKLastKeyOrDefault(string tableName, string columnName);

        /// <summary>
        /// Close database connection
        /// </summary>
        protected virtual void CloseConnection()
        {
            if (Connection != null && Connection.State == ConnectionState.Open)
            {
                Connection.Close();
            }
        }

        #region Transaction Support

        private DbTransaction _transaction;

        /// <summary>
        /// Begin a database transaction
        /// </summary>
        public virtual void BeginTransaction()
        {
            CreateConnection();
            CreateCommand();
            _transaction = Connection.BeginTransaction();
            Command.Transaction = _transaction;
        }

        /// <summary>
        /// Commit the current transaction
        /// </summary>
        public virtual void CommitTransaction()
        {
            _transaction?.Commit();
            _transaction = null;
        }

        /// <summary>
        /// Rollback the current transaction
        /// </summary>
        public virtual void RollbackTransaction()
        {
            _transaction?.Rollback();
            _transaction = null;
        }

        #endregion

        /// <summary>
        /// Data table search by integer column
        /// </summary>
        public DataTable RunIDataTableIntSearch(DataTable dataTable, string columnName, int searchValue)
        {
            var rows = dataTable.AsEnumerable()
                .Where(row => row.Field<int>(columnName) == searchValue);

            return rows.Any() ? rows.CopyToDataTable() : new DataTable();
        }

        /// <summary>
        /// Get first data row from DataTable
        /// </summary>
        public DataRow FirstDataRow(DataTable dataTable)
        {
            return dataTable?.Rows.Count > 0 ? dataTable.Rows[0] : null;
        }

        /// <summary>
        /// Get last data row from DataTable
        /// </summary>
        public DataRow LastDataRow(DataTable dataTable)
        {
            return dataTable?.Rows.Count > 0 ? dataTable.Rows[dataTable.Rows.Count - 1] : null;
        }

        /// <summary>
        /// Get all rows as IEnumerable
        /// </summary>
        public IEnumerable<DataRow> RunIDataRowList(DataTable dataTable)
        {
            return dataTable.AsEnumerable();
        }

        /// <summary>
        /// Filter DataTable with DataView
        /// </summary>
        public DataView DataTableFilter(DataTable dataTable, string columnName, object searchValue)
        {
            if (dataTable == null || string.IsNullOrEmpty(searchValue?.ToString()))
                return null;

            dataTable.DefaultView.Sort = $"{columnName} DESC";
            dataTable.DefaultView.RowFilter = $"{columnName} LIKE '{searchValue}'";

            return dataTable.DefaultView;
        }

        /// <summary>
        /// Execute SQL query and return DataTable with pagination (memory-safe)
        /// </summary>
        public virtual DataTable RunDataTablePaged(string sql, int pageNumber, int pageSize)
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 100;

            int offset = (pageNumber - 1) * pageSize;
            string pagedSql = $"{sql} OFFSET {offset} ROWS FETCH NEXT {pageSize} ROWS ONLY";

            return RunDataTable(pagedSql);
        }

        /// <summary>
        /// Execute SQL query and stream results row by row (memory-efficient)
        /// </summary>
        public virtual void RunDataReader(string sql, Action<IDataReader> action)
        {
            try
            {
                CreateConnection();
                CreateCommand();
                Command.CommandText = sql;

                using (var reader = Command.ExecuteReader(CommandBehavior.SequentialAccess))
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

        /// <summary>
        /// Execute SQL query and yield results as IEnumerable (lazy loading)
        /// </summary>
        public virtual IEnumerable<Dictionary<string, object>> RunDataReaderEnumerable(string sql)
        {
            CreateConnection();
            CreateCommand();
            Command.CommandText = sql;

            using (var reader = Command.ExecuteReader(CommandBehavior.CloseConnection))
            {
                var columns = new string[reader.FieldCount];
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    columns[i] = reader.GetName(i);
                }

                while (reader.Read())
                {
                    var row = new Dictionary<string, object>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        row[columns[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    }
                    yield return row;
                }
            }
        }

        /// <summary>
        /// Maximum rows allowed for RunDataTable before warning (default: 100000)
        /// Set to 0 to disable limit
        /// </summary>
        public int MaxRowsWarningThreshold { get; set; } = 100000;

        /// <summary>
        /// Execute SQL query and return ALL data (use with caution for large datasets)
        /// For very large tables, consider using RunDataTablePaged or RunDataReader instead
        /// </summary>
        public virtual DataTable RunDataTableAll(string sql)
        {
            try
            {
                CreateConnection();
                CreateCommand();
                Command.CommandText = sql;

                var dt = new DataTable();
                using (var reader = Command.ExecuteReader(CommandBehavior.SequentialAccess))
                {
                    // Add columns first
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        dt.Columns.Add(reader.GetName(i), reader.GetFieldType(i));
                    }

                    // Read all rows
                    while (reader.Read())
                    {
                        var row = dt.NewRow();
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            row[i] = reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i);
                        }
                        dt.Rows.Add(row);
                    }
                }

                return dt;
            }
            finally
            {
                CloseConnection();
            }
        }

        /// <summary>
        /// Execute SQL query with row limit to prevent memory issues
        /// </summary>
        public virtual DataTable RunDataTableLimited(string sql, int maxRows)
        {
            if (maxRows < 1) maxRows = 1000;

            try
            {
                CreateConnection();
                CreateCommand();
                Command.CommandText = sql;

                var dt = new DataTable();
                using (var reader = Command.ExecuteReader(CommandBehavior.SequentialAccess))
                {
                    // Add columns
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        dt.Columns.Add(reader.GetName(i), reader.GetFieldType(i));
                    }

                    // Add rows with limit
                    int rowCount = 0;
                    while (reader.Read() && rowCount < maxRows)
                    {
                        var row = dt.NewRow();
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            row[i] = reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i);
                        }
                        dt.Rows.Add(row);
                        rowCount++;
                    }
                }

                return dt;
            }
            finally
            {
                CloseConnection();
            }
        }

        /// <summary>
        /// Execute SQL query and process in batches (memory-efficient for very large datasets)
        /// </summary>
        public virtual void RunDataReaderBatched(string sql, int batchSize, Action<DataTable> batchAction)
        {
            if (batchSize < 1) batchSize = 1000;

            try
            {
                CreateConnection();
                CreateCommand();
                Command.CommandText = sql;

                using (var reader = Command.ExecuteReader(CommandBehavior.SequentialAccess))
                {
                    DataTable batchTable = null;
                    int rowCount = 0;

                    while (reader.Read())
                    {
                        // Create new batch table if needed
                        if (batchTable == null)
                        {
                            batchTable = new DataTable();
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                batchTable.Columns.Add(reader.GetName(i), reader.GetFieldType(i));
                            }
                        }

                        // Add row to batch
                        var row = batchTable.NewRow();
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            row[i] = reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i);
                        }
                        batchTable.Rows.Add(row);
                        rowCount++;

                        // Process batch when full
                        if (rowCount >= batchSize)
                        {
                            batchAction(batchTable);
                            batchTable.Clear();
                            rowCount = 0;
                        }
                    }

                    // Process remaining rows
                    if (batchTable != null && batchTable.Rows.Count > 0)
                    {
                        batchAction(batchTable);
                    }
                }
            }
            finally
            {
                CloseConnection();
            }
        }

        /// <summary>
        /// Dispose resources
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    Command?.Dispose();
                    Connection?.Dispose();
                }
                _disposed = true;
            }
        }
    }
}
