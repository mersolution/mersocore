using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace mersolutionCore.Command.Abstractions
{
    /// <summary>
    /// Async extension methods for DbCommandBase
    /// </summary>
    public abstract partial class DbCommandBase : IDbCommandAsync
    {
        /// <summary>
        /// Create and open database connection async
        /// </summary>
        protected virtual async Task CreateConnectionAsync(CancellationToken cancellationToken = default)
        {
            CreateConnection();
            if (Connection.State != ConnectionState.Open)
            {
                await ((DbConnection)Connection).OpenAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Execute SQL query async (INSERT, UPDATE, DELETE)
        /// </summary>
        public virtual async Task RunExecuteAsync(string sql, CancellationToken cancellationToken = default)
        {
            try
            {
                await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
                CreateCommand();
                Command.CommandText = sql;
                await Command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                CloseConnection();
            }
        }

        /// <summary>
        /// Execute SQL query and return DataTable async
        /// </summary>
        public virtual async Task<DataTable> RunDataTableAsync(string sql, CancellationToken cancellationToken = default)
        {
            try
            {
                await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
                CreateCommand();
                Command.CommandText = sql;

                var dt = new DataTable();
                using (var reader = await Command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken).ConfigureAwait(false))
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
        /// Execute SQL query and return DataSet async
        /// </summary>
        public virtual async Task<DataSet> RunDatasetAsync(string sql, CancellationToken cancellationToken = default)
        {
            var ds = new DataSet();
            ds.Tables.Add(await RunDataTableAsync(sql, cancellationToken).ConfigureAwait(false));
            return ds;
        }

        /// <summary>
        /// Execute SQL query and return string scalar value async
        /// </summary>
        public virtual async Task<string> RunToStringScalerAsync(string sql, CancellationToken cancellationToken = default)
        {
            try
            {
                await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
                CreateCommand();
                Command.CommandText = sql;

                var result = await Command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                return result?.ToString() ?? string.Empty;
            }
            finally
            {
                CloseConnection();
            }
        }

        /// <summary>
        /// Execute SQL query and return Int32 scalar value async
        /// </summary>
        public virtual async Task<int> RunToInt32ScalerAsync(string sql, CancellationToken cancellationToken = default)
        {
            try
            {
                await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
                CreateCommand();
                Command.CommandText = sql;

                var result = await Command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
            }
            finally
            {
                CloseConnection();
            }
        }

        /// <summary>
        /// Execute SQL query and return Int64 scalar value async
        /// </summary>
        public virtual async Task<long> RunToInt64ScalerAsync(string sql, CancellationToken cancellationToken = default)
        {
            try
            {
                await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
                CreateCommand();
                Command.CommandText = sql;

                var result = await Command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                return result != null && result != DBNull.Value ? Convert.ToInt64(result) : 0;
            }
            finally
            {
                CloseConnection();
            }
        }

        /// <summary>
        /// Execute SQL query and return decimal scalar value async
        /// </summary>
        public virtual async Task<decimal> RunToDecimalScalerAsync(string sql, CancellationToken cancellationToken = default)
        {
            try
            {
                await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
                CreateCommand();
                Command.CommandText = sql;

                var result = await Command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                return result != null && result != DBNull.Value ? Convert.ToDecimal(result) : 0;
            }
            finally
            {
                CloseConnection();
            }
        }

        /// <summary>
        /// Execute SQL query and return DataTable with pagination async
        /// </summary>
        public virtual async Task<DataTable> RunDataTablePagedAsync(string sql, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 100;

            int offset = (pageNumber - 1) * pageSize;
            string pagedSql = $"{sql} OFFSET {offset} ROWS FETCH NEXT {pageSize} ROWS ONLY";

            return await RunDataTableAsync(pagedSql, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Execute SQL query with row limit async
        /// </summary>
        public virtual async Task<DataTable> RunDataTableLimitedAsync(string sql, int maxRows, CancellationToken cancellationToken = default)
        {
            if (maxRows < 1) maxRows = 1000;

            try
            {
                await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
                CreateCommand();
                Command.CommandText = sql;

                var dt = new DataTable();
                using (var reader = await Command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken).ConfigureAwait(false))
                {
                    // Add columns
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        dt.Columns.Add(reader.GetName(i), reader.GetFieldType(i));
                    }

                    // Add rows with limit
                    int rowCount = 0;
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false) && rowCount < maxRows)
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
        /// Execute SQL query and yield results as async enumerable
        /// </summary>
        public virtual async IAsyncEnumerable<Dictionary<string, object>> RunDataReaderAsyncEnumerable(
            string sql, 
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
            CreateCommand();
            Command.CommandText = sql;

            using (var reader = await Command.ExecuteReaderAsync(CommandBehavior.CloseConnection, cancellationToken).ConfigureAwait(false))
            {
                var columns = new string[reader.FieldCount];
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    columns[i] = reader.GetName(i);
                }

                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
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
        /// Check if record exists async
        /// </summary>
        public virtual async Task<DataTable> FindOrDefaultAsync(string columnName, object value, string tableName, CancellationToken cancellationToken = default)
        {
            string valueStr = value is string ? $"'{value}'" : value.ToString();
            int count = await RunToInt32ScalerAsync($"SELECT COUNT(*) FROM {tableName} WHERE {columnName} = {valueStr}", cancellationToken).ConfigureAwait(false);

            if (count == 0)
                return null;

            return await RunDataTableAsync($"SELECT * FROM {tableName} WHERE {columnName} = {valueStr}", cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Get last inserted primary key value async
        /// </summary>
        public virtual async Task<int> PKLastKeyOrDefaultAsync(string tableName, string columnName, CancellationToken cancellationToken = default)
        {
            return await RunToInt32ScalerAsync($"SELECT COALESCE(MAX({columnName}), 0) FROM {tableName}", cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Execute SQL query and process in batches async
        /// </summary>
        public virtual async Task RunDataReaderBatchedAsync(string sql, int batchSize, Func<DataTable, Task> batchAction, CancellationToken cancellationToken = default)
        {
            if (batchSize < 1) batchSize = 1000;

            try
            {
                await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
                CreateCommand();
                Command.CommandText = sql;

                using (var reader = await Command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken).ConfigureAwait(false))
                {
                    DataTable batchTable = null;
                    int rowCount = 0;

                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        if (batchTable == null)
                        {
                            batchTable = new DataTable();
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                batchTable.Columns.Add(reader.GetName(i), reader.GetFieldType(i));
                            }
                        }

                        var row = batchTable.NewRow();
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            row[i] = reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i);
                        }
                        batchTable.Rows.Add(row);
                        rowCount++;

                        if (rowCount >= batchSize)
                        {
                            await batchAction(batchTable).ConfigureAwait(false);
                            batchTable.Clear();
                            rowCount = 0;
                        }
                    }

                    if (batchTable != null && batchTable.Rows.Count > 0)
                    {
                        await batchAction(batchTable).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                CloseConnection();
            }
        }

        /// <summary>
        /// Execute with transaction async
        /// </summary>
        public virtual async Task RunExecuteWithTransactionAsync(string sql, CancellationToken cancellationToken = default)
        {
            DbTransaction transaction = null;
            try
            {
                await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
                CreateCommand();

                transaction = ((DbConnection)Connection).BeginTransaction();
                Command.Transaction = transaction;
                Command.CommandText = sql;
                await Command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

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
        /// Execute multiple SQL statements in a transaction async
        /// </summary>
        public virtual async Task RunExecuteBatchAsync(IEnumerable<string> sqlStatements, CancellationToken cancellationToken = default)
        {
            DbTransaction transaction = null;
            try
            {
                await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
                CreateCommand();

                transaction = ((DbConnection)Connection).BeginTransaction();
                Command.Transaction = transaction;

                foreach (var sql in sqlStatements)
                {
                    Command.CommandText = sql;
                    await Command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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
    }
}
