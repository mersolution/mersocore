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
            Connection = NewConnection();
            await Connection!.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Async counterpart of <see cref="PrepareCommand"/>
        /// </summary>
        protected async Task PrepareCommandAsync(string sql, CancellationToken cancellationToken)
        {
            if (_transaction == null)
            {
                var ambient = AmbientTransaction.Find(this);
                if (ambient != null)
                {
                    _ambient = ambient;
                    Connection = ambient.Connection;
                }
                else
                {
                    _ambient = null;
                    await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            _sent = true;
            CreateCommand();
            Command!.CommandText = sql;
            Command!.Transaction = ActiveTransaction;
            ApplyPendingParameters();
            BeginLog(Command!);
        }

        /// <summary>
        /// Async provider hook: execute the (rewritten) INSERT and return the identity value
        /// </summary>
        protected virtual Task<object?> ExecuteInsertCommandAsync(DbCommand command, string primaryKeyColumn, CancellationToken cancellationToken)
        {
            return command.ExecuteScalarAsync(cancellationToken);
        }

        /// <summary>
        /// Execute SQL query async (INSERT, UPDATE, DELETE)
        /// </summary>
        public virtual async Task RunExecuteAsync(string sql, CancellationToken cancellationToken = default)
        {
            await RunNonQueryAsync(sql, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Execute SQL query async and return the number of affected rows
        /// </summary>
        public virtual Task<int> RunNonQueryAsync(string sql, CancellationToken cancellationToken = default)
        {
            mersolutionCore.ORM.ReadReplicas.MarkWrite();
            return WithRetryAsync(sql, true, async () =>
            {
                try
                {
                    await PrepareCommandAsync(sql, cancellationToken).ConfigureAwait(false);
                    var affected = await Command!.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    OnCommandExecuted(Command!, sql);
                    return affected;
                }
                catch (Exception ex) when (LogFailure(ex))
                {
                    throw;
                }
                finally
                {
                    CloseConnection();
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Execute SQL query and return DataTable async
        /// </summary>
        public virtual Task<DataTable> RunDataTableAsync(string sql, CancellationToken cancellationToken = default)
        {
            return WithRetryAsync(sql, false, async () =>
            {
                try
                {
                    await PrepareCommandAsync(sql, cancellationToken).ConfigureAwait(false);

                    var dt = new DataTable();
                    using (var reader = await Command!.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken).ConfigureAwait(false))
                    {
                        AddReaderColumns(dt, reader, LoadColumnsAsObject);
                        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                        {
                            dt.Rows.Add(ReadRow(dt, reader));
                        }
                    }

                    return dt;
                }
                catch (Exception ex) when (LogFailure(ex))
                {
                    throw;
                }
                finally
                {
                    CloseConnection();
                }
            }, cancellationToken);
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

        private Task<object?> ExecuteScalarCoreAsync(string sql, CancellationToken cancellationToken)
        {
            return WithRetryAsync(sql, false, async () =>
            {
                try
                {
                    await PrepareCommandAsync(sql, cancellationToken).ConfigureAwait(false);
                    return (object?)await Command!.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (LogFailure(ex))
                {
                    throw;
                }
                finally
                {
                    CloseConnection();
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Execute SQL query and return string scalar value async
        /// </summary>
        public virtual async Task<string> RunToStringScalerAsync(string sql, CancellationToken cancellationToken = default)
        {
            var result = await ExecuteScalarCoreAsync(sql, cancellationToken).ConfigureAwait(false);
            return result == null || result == DBNull.Value ? string.Empty : result.ToString() ?? string.Empty;
        }

        /// <summary>
        /// Execute SQL query and return Int32 scalar value async
        /// </summary>
        public virtual async Task<int> RunToInt32ScalerAsync(string sql, CancellationToken cancellationToken = default)
        {
            var result = await ExecuteScalarCoreAsync(sql, cancellationToken).ConfigureAwait(false);
            return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
        }

        /// <summary>
        /// Execute SQL query and return Int64 scalar value async
        /// </summary>
        public virtual async Task<long> RunToInt64ScalerAsync(string sql, CancellationToken cancellationToken = default)
        {
            var result = await ExecuteScalarCoreAsync(sql, cancellationToken).ConfigureAwait(false);
            return result != null && result != DBNull.Value ? Convert.ToInt64(result) : 0;
        }

        /// <summary>
        /// Execute SQL query and return decimal scalar value async
        /// </summary>
        public virtual async Task<decimal> RunToDecimalScalerAsync(string sql, CancellationToken cancellationToken = default)
        {
            var result = await ExecuteScalarCoreAsync(sql, cancellationToken).ConfigureAwait(false);
            return result != null && result != DBNull.Value ? Convert.ToDecimal(result) : 0;
        }

        /// <summary>
        /// Execute SQL query and return object scalar value async
        /// </summary>
        public virtual async Task<object?> RunToObjectScalerAsync(string sql, CancellationToken cancellationToken = default)
        {
            var result = await ExecuteScalarCoreAsync(sql, cancellationToken).ConfigureAwait(false);
            return result == DBNull.Value ? null : result;
        }

        /// <summary>
        /// Execute an INSERT async and return the generated identity value of the new row
        /// </summary>
        public virtual Task<long> RunInsertGetIdAsync(string insertSql, string primaryKeyColumn, CancellationToken cancellationToken = default)
        {
            mersolutionCore.ORM.ReadReplicas.MarkWrite();
            return WithRetryAsync(insertSql, true, async () =>
            {
                try
                {
                    await PrepareCommandAsync(InsertWithIdentity(insertSql, primaryKeyColumn), cancellationToken).ConfigureAwait(false);
                    var result = await ExecuteInsertCommandAsync(Command!, primaryKeyColumn, cancellationToken).ConfigureAwait(false);
                    OnCommandExecuted(Command!, insertSql);
                    return result == null || result == DBNull.Value ? 0 : Convert.ToInt64(result);
                }
                catch (Exception ex) when (LogFailure(ex))
                {
                    throw;
                }
                finally
                {
                    CloseConnection();
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Execute SQL query and return DataTable with pagination async
        /// </summary>
        public virtual Task<DataTable> RunDataTablePagedAsync(string sql, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 100;

            int offset = (pageNumber - 1) * pageSize;
            return RunDataTableAsync(BuildPagedSql(sql, offset, pageSize), cancellationToken);
        }

        /// <summary>
        /// Provider specific paging clause (OFFSET/FETCH here, LIMIT/OFFSET for MySQL, MariaDB, SQLite)
        /// </summary>
        protected virtual string BuildPagedSql(string sql, int offset, int pageSize)
        {
            return $"{sql} OFFSET {offset} ROWS FETCH NEXT {pageSize} ROWS ONLY";
        }

        /// <summary>
        /// Execute SQL query with row limit async
        /// </summary>
        public virtual Task<DataTable> RunDataTableLimitedAsync(string sql, int maxRows, CancellationToken cancellationToken = default)
        {
            if (maxRows < 1) maxRows = 1000;

            return WithRetryAsync(sql, false, async () =>
            {
                try
                {
                    await PrepareCommandAsync(sql, cancellationToken).ConfigureAwait(false);

                    var dt = new DataTable();
                    using (var reader = await Command!.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken).ConfigureAwait(false))
                    {
                        AddReaderColumns(dt, reader, LoadColumnsAsObject);

                        int rowCount = 0;
                        while (rowCount < maxRows && await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                        {
                            dt.Rows.Add(ReadRow(dt, reader));
                            rowCount++;
                        }
                    }

                    return dt;
                }
                catch (Exception ex) when (LogFailure(ex))
                {
                    throw;
                }
                finally
                {
                    CloseConnection();
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Execute SQL query and yield results as async enumerable
        /// </summary>
        public virtual async IAsyncEnumerable<Dictionary<string, object?>> RunDataReaderAsyncEnumerable(
            string sql,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            try
            {
                await PrepareCommandAsync(sql, cancellationToken).ConfigureAwait(false);

                using (var reader = await Command!.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    var columns = new string[reader.FieldCount];
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        columns[i] = reader.GetName(i);
                    }

                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        var row = new Dictionary<string, object?>();
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            row[columns[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                        }
                        yield return row;
                    }
                }
            }
            finally
            {
                CloseConnection();
            }
        }

        /// <summary>
        /// Return the rows where <paramref name="columnName"/> equals <paramref name="value"/> async, or null when there is none
        /// </summary>
        public virtual async Task<DataTable?> FindOrDefaultAsync(string columnName, object? value, string tableName, CancellationToken cancellationToken = default)
        {
            ParametersAdd("@__value", value);
            var dt = await RunDataTableAsync($"SELECT * FROM {tableName} WHERE {columnName} = @__value", cancellationToken).ConfigureAwait(false);
            return dt.Rows.Count == 0 ? null : dt;
        }

        /// <summary>
        /// Get the highest primary key value async (MAX)
        /// </summary>
        public virtual Task<int> PKLastKeyOrDefaultAsync(string tableName, string columnName, CancellationToken cancellationToken = default)
        {
            return RunToInt32ScalerAsync($"SELECT COALESCE(MAX({columnName}), 0) FROM {tableName}", cancellationToken);
        }

        /// <summary>
        /// Execute SQL query and process in batches async
        /// </summary>
        public virtual async Task RunDataReaderBatchedAsync(string sql, int batchSize, Func<DataTable, Task> batchAction, CancellationToken cancellationToken = default)
        {
            if (batchSize < 1) batchSize = 1000;

            try
            {
                await PrepareCommandAsync(sql, cancellationToken).ConfigureAwait(false);

                using (var reader = await Command!.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken).ConfigureAwait(false))
                {
                    var batchTable = new DataTable();
                    AddReaderColumns(batchTable, reader, LoadColumnsAsObject);

                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        batchTable.Rows.Add(ReadRow(batchTable, reader));

                        if (batchTable.Rows.Count >= batchSize)
                        {
                            await batchAction(batchTable).ConfigureAwait(false);
                            batchTable.Clear();
                        }
                    }

                    if (batchTable.Rows.Count > 0)
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
        /// Execute with transaction async (joins an already active transaction)
        /// </summary>
        public virtual Task RunExecuteWithTransactionAsync(string sql, CancellationToken cancellationToken = default)
        {
            return RunExecuteBatchAsync(new[] { sql }, cancellationToken);
        }

        /// <summary>
        /// Execute multiple SQL statements in a transaction async (joins an already active transaction)
        /// </summary>
        public virtual async Task RunExecuteBatchAsync(IEnumerable<string> sqlStatements, CancellationToken cancellationToken = default)
        {
            mersolutionCore.ORM.ReadReplicas.MarkWrite();
            if (sqlStatements == null)
                return;

            if (KeepConnectionOpen || AmbientTransaction.Find(this) != null)
            {
                foreach (var sql in sqlStatements)
                    await RunNonQueryAsync(sql, cancellationToken).ConfigureAwait(false);
                return;
            }

            DbTransaction? transaction = null;
            try
            {
                await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
                transaction = Connection!.BeginTransaction();

                foreach (var sql in sqlStatements)
                {
                    CreateCommand();
                    Command!.Transaction = transaction;
                    Command!.CommandText = sql;
                    ApplyPendingParameters();
                    BeginLog(Command!);
                    await Command!.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    EndLog();
                    OnCommandExecuted(Command!, sql);
                }

                transaction.Commit();
            }
            catch (Exception ex) when (LogFailure(ex))
            {
                throw;
            }
            catch
            {
                TryRollback(transaction);
                throw;
            }
            finally
            {
                transaction?.Dispose();
                CloseConnection();
            }
        }
    }
}
