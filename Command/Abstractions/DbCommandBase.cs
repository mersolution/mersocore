using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using mersolutionCore.Config;

namespace mersolutionCore.Command.Abstractions
{
    /// <summary>
    /// Abstract base class for all database command implementations
    /// </summary>
    /// <remarks>
    /// Every Run* method opens a connection, applies the parameters queued with
    /// <see cref="ParametersAdd(string, object)"/>, executes and closes the connection again —
    /// unless a transaction is active (own <see cref="BeginTransaction"/> or an ambient
    /// <c>MersoTransaction.Run</c>); then the transaction's connection is reused and kept open.
    /// </remarks>
    public abstract partial class DbCommandBase : IDbCommand, IDisposable
    {
        protected DbConnection? Connection;
        protected DbCommand? Command;
        protected DbDataAdapter? DataAdapter;
        protected bool _disposed = false;

        // Ordered so that positional SQL (e.g. PostgreSQL function calls) sees parameters in insertion order.
        private readonly List<string> _pendingOrder = new List<string>();
        private readonly Dictionary<string, Action<DbCommand>> _pendingParameters = new Dictionary<string, Action<DbCommand>>(StringComparer.Ordinal);

        private DbTransaction? _transaction;      // own transaction (BeginTransaction)
        private AmbientTransaction? _ambient;      // borrowed from MersoTransaction.Run
        private bool _joinedAmbient;              // BeginTransaction called while an ambient transaction was active

        /// <summary>
        /// Database provider type
        /// </summary>
        public abstract DbProviderType ProviderType { get; }

        /// <summary>
        /// Create a new, not yet opened provider connection
        /// </summary>
        protected abstract DbConnection NewConnection();

        /// <summary>
        /// Connection string of this command (used to decide whether an ambient transaction can be joined)
        /// </summary>
        protected virtual string? GetConnectionString() => null;

        /// <summary>
        /// Command timeout in seconds
        /// </summary>
        protected virtual int CommandTimeout => 30;

        internal string? ConnectionStringForMatching => GetConnectionString();

        /// <summary>
        /// True while statements of this command run inside a transaction (own BeginTransaction or MersoTransaction)
        /// </summary>
        internal bool InTransaction => _transaction != null || _joinedAmbient || AmbientTransaction.Find(this) != null;

        /// <summary>
        /// Run raw ADO.NET work on a fresh, dedicated connection (schema work that needs connection-level
        /// settings, e.g. SQLite PRAGMA foreign_keys, which cannot change inside a transaction)
        /// </summary>
        internal void WithOwnConnection(Action<DbConnection> work)
        {
            using (var connection = NewConnection())
            {
                connection.Open();
                work(connection);
            }
        }

        /// <summary>
        /// Create and open database connection
        /// </summary>
        protected virtual void CreateConnection()
        {
            Connection = NewConnection();
            Connection!.Open();
        }

        /// <summary>
        /// Create command object on the current connection
        /// </summary>
        protected virtual void CreateCommand()
        {
            Command = Connection!.CreateCommand();
            Command!.CommandTimeout = CommandTimeout;
        }

        /// <summary>
        /// Create data adapter for the current command (null when the provider has none, e.g. SQLite)
        /// </summary>
        protected virtual void CreateDataAdapter()
        {
            DataAdapter = null;
        }

        #region Connection / command lifecycle

        /// <summary>
        /// True while a transaction owns the connection (it must not be closed after each statement)
        /// </summary>
        protected bool KeepConnectionOpen => _transaction != null || _ambient != null;

        /// <summary>
        /// Transaction the next command has to run in (own or ambient)
        /// </summary>
        protected DbTransaction? ActiveTransaction => _transaction ?? _ambient?.Transaction;

        /// <summary>
        /// Open the connection, or reuse the transaction connection
        /// </summary>
        protected void OpenConnection()
        {
            if (_transaction != null)
                return;

            var ambient = AmbientTransaction.Find(this);
            if (ambient != null)
            {
                _ambient = ambient;
                Connection = ambient.Connection;
                return;
            }

            _ambient = null;
            CreateConnection();
        }

        /// <summary>
        /// Open connection, create the command, attach transaction and queued parameters
        /// </summary>
        protected void PrepareCommand(string sql)
        {
            OpenConnection();
            _sent = true;
            CreateCommand();
            Command!.CommandText = sql;
            Command!.Transaction = ActiveTransaction;
            ApplyPendingParameters();
            BeginLog(Command!);
        }

        /// <summary>
        /// Close database connection (no-op while a transaction is active)
        /// </summary>
        protected virtual void CloseConnection()
        {
            EndLog();

            if (KeepConnectionOpen)
                return;

            if (Connection != null)
            {
                if (Connection!.State != ConnectionState.Closed)
                    Connection!.Close();
                Connection!.Dispose();
                Connection = null;
            }
        }

        /// <summary>
        /// Hook called after a non-query statement ran (providers capture e.g. LAST_INSERT_ID here)
        /// </summary>
        protected virtual void OnCommandExecuted(DbCommand command, string sql)
        {
        }

        #endregion

        #region SQL log (MersoLog)

        private bool _logPending;
        private string? _logSql;
        private QueryLogParameter[]? _logParameters;
        private DateTime _logStartedUtc;
        private long _logStartTicks;
        private Exception? _logError;

        /// <summary>
        /// Start timing a prepared command for <see cref="MersoLog"/> (no-op without subscribers)
        /// </summary>
        protected void BeginLog(DbCommand command)
        {
            _logPending = false;

            // OpenTelemetry (MersoTelemetry): one Activity per command while a listener is attached
            MersoTelemetry.Stop(_activity, null);
            _activity = command == null ? null : MersoTelemetry.Start(command, ProviderType);
            _activityError = null;

            if (!MersoLog.IsEnabled || command == null)
                return;

            var withValues = MersoLog.IncludeParameterValues;
            _logParameters = command.Parameters.Cast<DbParameter>()
                .Select(p => new QueryLogParameter
                {
                    Name = p.ParameterName,
                    Value = withValues ? (p.Value == DBNull.Value ? null : p.Value) : null
                })
                .ToArray();
            _logSql = command.CommandText;
            _logError = null;
            _logStartedUtc = DateTime.UtcNow;
            _logStartTicks = Stopwatch.GetTimestamp();
            _logPending = true;
        }

        /// <summary>
        /// Exception filter: remember the error for the log entry, never catches (always false)
        /// </summary>
        protected bool LogFailure(Exception error)
        {
            if (_logPending)
                _logError = error;
            if (_activity != null)
                _activityError = error;
            return false;
        }

        private Activity? _activity;
        private Exception? _activityError;

        /// <summary>
        /// Publish the pending log entry (called from <see cref="CloseConnection"/>)
        /// </summary>
        protected void EndLog()
        {
            if (_activity != null)
            {
                MersoTelemetry.Stop(_activity, _activityError);
                _activity = null;
                _activityError = null;
            }

            if (!_logPending)
                return;

            _logPending = false;
            var elapsed = Stopwatch.GetTimestamp() - _logStartTicks;
            MersoLog.Publish(new QueryLogEntry
            {
                Sql = _logSql ?? string.Empty,
                Provider = ProviderType,
                StartedAtUtc = _logStartedUtc,
                Duration = TimeSpan.FromTicks((long)(elapsed * ((double)TimeSpan.TicksPerSecond / Stopwatch.Frequency))),
                Parameters = _logParameters ?? new QueryLogParameter[0],
                Exception = _logError
            });
            _logParameters = null;
            _logError = null;
        }

        #endregion

        #region Retry (MersoRetry)

        // The current attempt got an open connection (so the statement may have reached the server)
        private bool _sent;

        /// <summary>
        /// Run one statement with <see cref="MersoRetry"/> (never inside a transaction)
        /// </summary>
        private T WithRetry<T>(string sql, bool writes, Func<T> run)
        {
            if (MersoRetry.MaxRetries == 0 || InTransaction)
                return run();

            var readOnly = !writes && MersoRetry.IsReadOnlySql(sql);
            var saved = SavePendingParameters();
            for (int attempt = 0; ; attempt++)
            {
                _sent = false;
                try
                {
                    return run();
                }
                catch (Exception ex) when (MersoRetry.ShouldRetry(ex, attempt, _sent, readOnly))
                {
                    RestorePendingParameters(saved);
                    MersoRetry.Wait(ex, attempt);
                }
            }
        }

        /// <summary>
        /// Async counterpart of <see cref="WithRetry{T}"/>
        /// </summary>
        private async System.Threading.Tasks.Task<T> WithRetryAsync<T>(string sql, bool writes, Func<System.Threading.Tasks.Task<T>> run,
            System.Threading.CancellationToken cancellationToken)
        {
            if (MersoRetry.MaxRetries == 0 || InTransaction)
                return await run().ConfigureAwait(false);

            var readOnly = !writes && MersoRetry.IsReadOnlySql(sql);
            var saved = SavePendingParameters();
            for (int attempt = 0; ; attempt++)
            {
                _sent = false;
                Exception failure;
                try
                {
                    return await run().ConfigureAwait(false);
                }
                catch (Exception ex) when (MersoRetry.ShouldRetry(ex, attempt, _sent, readOnly))
                {
                    failure = ex;
                }

                RestorePendingParameters(saved);
                await MersoRetry.WaitAsync(failure, attempt, cancellationToken).ConfigureAwait(false);
            }
        }

        private List<KeyValuePair<string, Action<DbCommand>>> SavePendingParameters()
        {
            return _pendingOrder.Select(n => new KeyValuePair<string, Action<DbCommand>>(n, _pendingParameters[n])).ToList();
        }

        private void RestorePendingParameters(List<KeyValuePair<string, Action<DbCommand>>> saved)
        {
            _pendingParameters.Clear();
            _pendingOrder.Clear();
            foreach (var parameter in saved)
                AddPendingParameter(parameter.Key, parameter.Value);
        }

        #endregion

        #region Parameters

        /// <summary>
        /// Add parameter to the next command
        /// </summary>
        public virtual void ParametersAdd(string name, object? value)
        {
            var normalized = NormalizeParameterValue(value);
            AddPendingParameter(name, cmd =>
            {
                var p = cmd.CreateParameter();
                p.ParameterName = name;
                p.Value = normalized;
                cmd.Parameters.Add(p);
            });
        }

        /// <summary>
        /// Queue a provider specific parameter (typed, output, ...) for the next command
        /// </summary>
        protected void AddPendingParameter(string name, Action<DbCommand> apply)
        {
            if (!_pendingParameters.ContainsKey(name))
                _pendingOrder.Add(name);
            _pendingParameters[name] = apply;
        }

        /// <summary>
        /// Names of the queued parameters, in insertion order
        /// </summary>
        protected IReadOnlyList<string> PendingParameterNames => _pendingOrder;

        /// <summary>
        /// Clear all parameters
        /// </summary>
        public virtual void ParametersClear()
        {
            _pendingParameters.Clear();
            _pendingOrder.Clear();
            Command?.Parameters.Clear();
        }

        private void ApplyPendingParameters()
        {
            foreach (var name in _pendingOrder)
            {
                _pendingParameters[name](Command!);
            }
            _pendingParameters.Clear();
            _pendingOrder.Clear();
        }

        /// <summary>
        /// Map CLR values that some providers cannot bind (enums, null)
        /// </summary>
        protected static object NormalizeParameterValue(object? value)
        {
            if (value == null)
                return DBNull.Value;

            var type = value.GetType();
            if (type.IsEnum)
                return Convert.ChangeType(value, Enum.GetUnderlyingType(type));

            if (mersolutionCore.ORM.DateTypes.IsDateOrTimeOnly(type))
                return mersolutionCore.ORM.DateTypes.ToParameterValue(value);

            return value;
        }

        #endregion

        #region Execute

        /// <summary>
        /// Execute SQL query (INSERT, UPDATE, DELETE)
        /// </summary>
        public virtual void RunExecute(string sql)
        {
            RunNonQuery(sql);
        }

        /// <summary>
        /// Execute SQL query (INSERT, UPDATE, DELETE) and return the number of affected rows
        /// </summary>
        public virtual int RunNonQuery(string sql)
        {
            mersolutionCore.ORM.ReadReplicas.MarkWrite();
            return WithRetry(sql, true, () =>
            {
                try
                {
                    PrepareCommand(sql);
                    var affected = Command!.ExecuteNonQuery();
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
            });
        }

        /// <summary>
        /// Execute an INSERT and return the generated identity value of the new row
        /// (same connection: SCOPE_IDENTITY / RETURNING / LAST_INSERT_ID / last_insert_rowid)
        /// </summary>
        public virtual long RunInsertGetId(string insertSql, string primaryKeyColumn)
        {
            mersolutionCore.ORM.ReadReplicas.MarkWrite();
            return WithRetry(insertSql, true, () =>
            {
                try
                {
                    PrepareCommand(InsertWithIdentity(insertSql, primaryKeyColumn));
                    var result = ExecuteInsertCommand(Command!, primaryKeyColumn);
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
            });
        }

        /// <summary>
        /// Provider hook: rewrite an INSERT so it also returns the identity value
        /// </summary>
        protected virtual string InsertWithIdentity(string insertSql, string primaryKeyColumn) => insertSql;

        /// <summary>
        /// Provider hook: execute the (rewritten) INSERT and return the identity value
        /// </summary>
        protected virtual object? ExecuteInsertCommand(DbCommand command, string primaryKeyColumn) => command.ExecuteScalar();

        /// <summary>
        /// Execute SQL query and return DataTable
        /// </summary>
        public virtual DataTable RunDataTable(string sql)
        {
            return WithRetry(sql, false, () =>
            {
                try
                {
                    PrepareCommand(sql);
                    CreateDataAdapter();

                    var dt = new DataTable();
                    if (DataAdapter != null)
                    {
                        DataAdapter.Fill(dt);
                    }
                    else
                    {
                        using (var reader = Command!.ExecuteReader())
                        {
                            FillFromReader(dt, reader);
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
            });
        }

        /// <summary>
        /// True when column types must not be taken from the reader schema. SQLite reports the declared
        /// type (DECIMAL → Int64) while a row may hold REAL/TEXT; DataTable.Load then truncated 12.5 to 12.
        /// </summary>
        protected virtual bool LoadColumnsAsObject => false;

        private void FillFromReader(DataTable dt, IDataReader reader)
        {
            AddReaderColumns(dt, reader, LoadColumnsAsObject);
            while (reader.Read())
            {
                dt.Rows.Add(ReadRow(dt, reader));
            }
        }

        /// <summary>
        /// Execute SQL query and return DataSet (all result sets when the provider has a data adapter)
        /// </summary>
        public virtual DataSet RunDataset(string sql)
        {
            return WithRetry(sql, false, () =>
            {
                try
                {
                    PrepareCommand(sql);
                    CreateDataAdapter();

                    var ds = new DataSet();
                    if (DataAdapter != null)
                    {
                        DataAdapter.Fill(ds);
                    }
                    else
                    {
                        var dt = new DataTable();
                        using (var reader = Command!.ExecuteReader())
                        {
                            FillFromReader(dt, reader);
                        }
                        ds.Tables.Add(dt);
                    }

                    return ds;
                }
                catch (Exception ex) when (LogFailure(ex))
                {
                    throw;
                }
                finally
                {
                    CloseConnection();
                }
            });
        }

        private object? ExecuteScalarCore(string sql)
        {
            return WithRetry(sql, false, () =>
            {
                try
                {
                    PrepareCommand(sql);
                    return Command!.ExecuteScalar();
                }
                catch (Exception ex) when (LogFailure(ex))
                {
                    throw;
                }
                finally
                {
                    CloseConnection();
                }
            });
        }

        /// <summary>
        /// Execute SQL query and return string scalar value
        /// </summary>
        public virtual string RunToStringScaler(string sql)
        {
            var result = ExecuteScalarCore(sql);
            return result == null || result == DBNull.Value ? string.Empty : result.ToString() ?? string.Empty;
        }

        /// <summary>
        /// Execute SQL query and return Int32 scalar value
        /// </summary>
        public virtual int RunToInt32Scaler(string sql)
        {
            var result = ExecuteScalarCore(sql);
            return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
        }

        /// <summary>
        /// Execute SQL query and return Int64 scalar value
        /// </summary>
        public virtual long RunToInt64Scaler(string sql)
        {
            var result = ExecuteScalarCore(sql);
            return result != null && result != DBNull.Value ? Convert.ToInt64(result) : 0;
        }

        /// <summary>
        /// Execute SQL query and return decimal scalar value
        /// </summary>
        public virtual decimal RunToDecimalScaler(string sql)
        {
            var result = ExecuteScalarCore(sql);
            return result != null && result != DBNull.Value ? Convert.ToDecimal(result) : 0;
        }

        /// <summary>
        /// Execute SQL query and return object scalar value
        /// </summary>
        public virtual object? RunToObjectScaler(string sql)
        {
            var result = ExecuteScalarCore(sql);
            return result == DBNull.Value ? null : result;
        }

        /// <summary>
        /// Execute a statement inside its own transaction (joins an already active transaction)
        /// </summary>
        public virtual void RunExecuteWithTransaction(string sql)
        {
            RunExecuteBatch(sql);
        }

        /// <summary>
        /// Execute multiple SQL statements in one transaction (joins an already active transaction)
        /// </summary>
        public virtual void RunExecuteBatch(params string[] sqlStatements)
        {
            if (sqlStatements == null || sqlStatements.Length == 0)
                return;
            mersolutionCore.ORM.ReadReplicas.MarkWrite();

            if (KeepConnectionOpen || AmbientTransaction.Find(this) != null)
            {
                foreach (var sql in sqlStatements)
                    RunNonQuery(sql);
                return;
            }

            DbTransaction? transaction = null;
            try
            {
                CreateConnection();
                transaction = Connection!.BeginTransaction();

                foreach (var sql in sqlStatements)
                {
                    CreateCommand();
                    Command!.Transaction = transaction;
                    Command!.CommandText = sql;
                    ApplyPendingParameters();
                    BeginLog(Command!);
                    Command!.ExecuteNonQuery();
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

        #endregion

        /// <summary>
        /// Return the rows where <paramref name="columnName"/> equals <paramref name="value"/>, or null when there is none
        /// </summary>
        public virtual DataTable? FindOrDefault(string columnName, object? value, string tableName)
        {
            ParametersAdd("@__value", value);
            var dt = RunDataTable($"SELECT * FROM {tableName} WHERE {columnName} = @__value");
            return dt.Rows.Count == 0 ? null : dt;
        }

        /// <summary>
        /// Get the highest primary key value of a table (MAX). For the id of a row you just inserted use <see cref="RunInsertGetId"/>.
        /// </summary>
        public abstract int PKLastKeyOrDefault(string tableName, string columnName);

        #region Transaction Support

        /// <summary>
        /// Begin a database transaction. Following Run* calls on this instance use it until
        /// <see cref="CommitTransaction"/> / <see cref="RollbackTransaction"/>.
        /// Inside <c>MersoTransaction.Run</c> the call joins the outer transaction.
        /// </summary>
        public virtual void BeginTransaction()
        {
            if (_transaction != null || _joinedAmbient)
                throw new InvalidOperationException("A transaction is already active on this command.");

            var ambient = AmbientTransaction.Find(this);
            if (ambient != null)
            {
                _ambient = ambient;
                Connection = ambient.Connection;
                _joinedAmbient = true;
                return;
            }

            _ambient = null;
            CreateConnection();
            _transaction = Connection!.BeginTransaction();
        }

        /// <summary>
        /// Commit the current transaction
        /// </summary>
        public virtual void CommitTransaction()
        {
            if (_joinedAmbient)
            {
                _joinedAmbient = false;
                return;
            }

            if (_transaction == null)
                return;

            try
            {
                _transaction.Commit();
            }
            finally
            {
                EndTransaction();
            }
        }

        /// <summary>
        /// Rollback the current transaction
        /// </summary>
        public virtual void RollbackTransaction()
        {
            if (_joinedAmbient)
            {
                _joinedAmbient = false;
                return;
            }

            if (_transaction == null)
                return;

            try
            {
                _transaction.Rollback();
            }
            finally
            {
                EndTransaction();
            }
        }

        private void EndTransaction()
        {
            _transaction?.Dispose();
            _transaction = null;
            CloseConnection();
        }

        /// <summary>
        /// Share this command's open transaction with every command created in the current flow
        /// </summary>
        internal AmbientTransaction CreateAmbientTransaction()
        {
            if (_transaction == null)
                throw new InvalidOperationException("BeginTransaction must be called first.");

            return new AmbientTransaction(this, Connection!, _transaction, GetConnectionString(), AmbientTransaction.Current);
        }

        private static void TryRollback(DbTransaction? transaction)
        {
            try
            {
                transaction?.Rollback();
            }
            catch
            {
                // Keep the original exception; a broken connection already rolled back.
            }
        }

        #endregion

        /// <summary>
        /// Data table search by integer column
        /// </summary>
        public DataTable RunIDataTableIntSearch(DataTable dataTable, string columnName, int searchValue)
        {
            var rows = dataTable.AsEnumerable()
                .Where(row => row[columnName] != DBNull.Value && Convert.ToInt64(row[columnName]) == searchValue);

            return rows.Any() ? rows.CopyToDataTable() : new DataTable();
        }

        /// <summary>
        /// Get first data row from DataTable
        /// </summary>
        public DataRow? FirstDataRow(DataTable? dataTable)
        {
            return dataTable?.Rows.Count > 0 ? dataTable.Rows[0] : null;
        }

        /// <summary>
        /// Get last data row from DataTable
        /// </summary>
        public DataRow? LastDataRow(DataTable? dataTable)
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
        public DataView? DataTableFilter(DataTable? dataTable, string columnName, object? searchValue)
        {
            var text = searchValue?.ToString();
            if (dataTable == null || text == null || text.Length == 0)
                return null;

            var escaped = text.Replace("'", "''");
            dataTable.DefaultView.Sort = $"[{columnName}] DESC";
            dataTable.DefaultView.RowFilter = $"[{columnName}] LIKE '{escaped}'";

            return dataTable.DefaultView;
        }

        /// <summary>
        /// Execute SQL query and return DataTable with pagination (memory-safe).
        /// Syntax comes from <see cref="BuildPagedSql"/>: OFFSET/FETCH (SQL Server 2012+ — needs ORDER BY, PostgreSQL)
        /// or LIMIT/OFFSET (MySQL, MariaDB, SQLite).
        /// </summary>
        public virtual DataTable RunDataTablePaged(string sql, int pageNumber, int pageSize)
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 100;

            int offset = (pageNumber - 1) * pageSize;
            return RunDataTable(BuildPagedSql(sql, offset, pageSize));
        }

        /// <summary>
        /// Execute SQL query and stream results row by row (memory-efficient)
        /// </summary>
        public virtual void RunDataReader(string sql, Action<IDataReader> action)
        {
            try
            {
                PrepareCommand(sql);

                using (var reader = Command!.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        action(reader);
                    }
                }
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
        /// Execute SQL query and yield results as IEnumerable (lazy loading)
        /// </summary>
        public virtual IEnumerable<Dictionary<string, object?>> RunDataReaderEnumerable(string sql)
        {
            try
            {
                PrepareCommand(sql);

                using (var reader = Command!.ExecuteReader())
                {
                    var columns = new string[reader.FieldCount];
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        columns[i] = reader.GetName(i);
                    }

                    while (reader.Read())
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
            return RunDataTableLimitedCore(sql, int.MaxValue);
        }

        /// <summary>
        /// Execute SQL query with row limit to prevent memory issues
        /// </summary>
        public virtual DataTable RunDataTableLimited(string sql, int maxRows)
        {
            if (maxRows < 1) maxRows = 1000;
            return RunDataTableLimitedCore(sql, maxRows);
        }

        private DataTable RunDataTableLimitedCore(string sql, int maxRows)
        {
            return WithRetry(sql, false, () =>
            {
                try
                {
                    PrepareCommand(sql);

                    var dt = new DataTable();
                    using (var reader = Command!.ExecuteReader(CommandBehavior.SequentialAccess))
                    {
                        AddReaderColumns(dt, reader, LoadColumnsAsObject);

                        int rowCount = 0;
                        while (rowCount < maxRows && reader.Read())
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
            });
        }

        /// <summary>
        /// Execute SQL query and process in batches (memory-efficient for very large datasets)
        /// </summary>
        public virtual void RunDataReaderBatched(string sql, int batchSize, Action<DataTable> batchAction)
        {
            if (batchSize < 1) batchSize = 1000;

            try
            {
                PrepareCommand(sql);

                using (var reader = Command!.ExecuteReader(CommandBehavior.SequentialAccess))
                {
                    var batchTable = new DataTable();
                    AddReaderColumns(batchTable, reader, LoadColumnsAsObject);

                    while (reader.Read())
                    {
                        batchTable.Rows.Add(ReadRow(batchTable, reader));

                        if (batchTable.Rows.Count >= batchSize)
                        {
                            batchAction(batchTable);
                            batchTable.Clear();
                        }
                    }

                    if (batchTable.Rows.Count > 0)
                    {
                        batchAction(batchTable);
                    }
                }
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

        internal static void AddReaderColumns(DataTable dt, IDataReader reader, bool asObject)
        {
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var name = reader.GetName(i);
                // Duplicate names (e.g. joins without aliases) would make DataTable throw
                var unique = name;
                for (int n = 1; dt.Columns.Contains(unique); n++)
                    unique = name + n;
                dt.Columns.Add(unique, asObject ? typeof(object) : reader.GetFieldType(i));
            }
        }

        internal static DataRow ReadRow(DataTable dt, IDataReader reader)
        {
            var row = dt.NewRow();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                row[i] = reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i);
            }
            return row;
        }

        /// <summary>
        /// Dispose resources (an uncommitted own transaction is rolled back)
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
                    if (_transaction != null)
                    {
                        TryRollback(_transaction);
                        _transaction.Dispose();
                        _transaction = null;
                    }

                    _joinedAmbient = false;

                    // A borrowed ambient connection belongs to the MersoTransaction owner
                    if (_ambient == null)
                    {
                        Command?.Dispose();
                        Connection?.Dispose();
                    }
                    _ambient = null;
                }
                _disposed = true;
            }
        }
    }
}
