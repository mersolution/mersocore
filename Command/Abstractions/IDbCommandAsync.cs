using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace mersolutionCore.Command.Abstractions
{
    /// <summary>
    /// Async database command interface for all database providers
    /// </summary>
    public interface IDbCommandAsync
    {
        /// <summary>
        /// Execute SQL query async (INSERT, UPDATE, DELETE)
        /// </summary>
        Task RunExecuteAsync(string sql, CancellationToken cancellationToken = default);

        /// <summary>
        /// Execute SQL query and return DataTable async
        /// </summary>
        Task<DataTable> RunDataTableAsync(string sql, CancellationToken cancellationToken = default);

        /// <summary>
        /// Execute SQL query and return DataSet async
        /// </summary>
        Task<DataSet> RunDatasetAsync(string sql, CancellationToken cancellationToken = default);

        /// <summary>
        /// Execute SQL query and return string scalar value async
        /// </summary>
        Task<string> RunToStringScalerAsync(string sql, CancellationToken cancellationToken = default);

        /// <summary>
        /// Execute SQL query and return Int32 scalar value async
        /// </summary>
        Task<int> RunToInt32ScalerAsync(string sql, CancellationToken cancellationToken = default);

        /// <summary>
        /// Execute SQL query and return Int64 scalar value async
        /// </summary>
        Task<long> RunToInt64ScalerAsync(string sql, CancellationToken cancellationToken = default);

        /// <summary>
        /// Execute SQL query and return decimal scalar value async
        /// </summary>
        Task<decimal> RunToDecimalScalerAsync(string sql, CancellationToken cancellationToken = default);

        /// <summary>
        /// Execute SQL query and return DataTable with pagination async
        /// </summary>
        Task<DataTable> RunDataTablePagedAsync(string sql, int pageNumber, int pageSize, CancellationToken cancellationToken = default);

        /// <summary>
        /// Execute SQL query with row limit async
        /// </summary>
        Task<DataTable> RunDataTableLimitedAsync(string sql, int maxRows, CancellationToken cancellationToken = default);

        /// <summary>
        /// Execute SQL query and yield results as async enumerable
        /// </summary>
        IAsyncEnumerable<Dictionary<string, object>> RunDataReaderAsyncEnumerable(string sql, CancellationToken cancellationToken = default);

        /// <summary>
        /// Check if record exists async
        /// </summary>
        Task<DataTable> FindOrDefaultAsync(string columnName, object value, string tableName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Get last inserted primary key value async
        /// </summary>
        Task<int> PKLastKeyOrDefaultAsync(string tableName, string columnName, CancellationToken cancellationToken = default);
    }
}
