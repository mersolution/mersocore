using System;
using System.Collections.Generic;
using System.Data;

namespace mersolutionCore.Command.Abstractions
{
    /// <summary>
    /// Common database command interface for all database providers
    /// </summary>
    public interface IDbCommand
    {
        /// <summary>
        /// Execute SQL query (INSERT, UPDATE, DELETE)
        /// </summary>
        /// <param name="sql">SQL query to execute</param>
        void RunExecute(string sql);

        /// <summary>
        /// Execute SQL query and return DataTable
        /// </summary>
        /// <param name="sql">SQL query to execute</param>
        /// <returns>DataTable with results</returns>
        DataTable RunDataTable(string sql);

        /// <summary>
        /// Execute SQL query and return DataSet
        /// </summary>
        /// <param name="sql">SQL query to execute</param>
        /// <returns>DataSet with results</returns>
        DataSet RunDataset(string sql);

        /// <summary>
        /// Execute SQL query and return string scalar value
        /// </summary>
        /// <param name="sql">SQL query to execute</param>
        /// <returns>String result</returns>
        string RunToStringScaler(string sql);

        /// <summary>
        /// Execute SQL query and return Int32 scalar value
        /// </summary>
        /// <param name="sql">SQL query to execute</param>
        /// <returns>Int32 result</returns>
        int RunToInt32Scaler(string sql);

        /// <summary>
        /// Execute SQL query and return Int64 scalar value
        /// </summary>
        /// <param name="sql">SQL query to execute</param>
        /// <returns>Int64 result</returns>
        long RunToInt64Scaler(string sql);

        /// <summary>
        /// Execute SQL query and return decimal scalar value
        /// </summary>
        /// <param name="sql">SQL query to execute</param>
        /// <returns>Decimal result</returns>
        decimal RunToDecimalScaler(string sql);

        /// <summary>
        /// Add parameter to command
        /// </summary>
        /// <param name="name">Parameter name</param>
        /// <param name="value">Parameter value</param>
        void ParametersAdd(string name, object value);

        /// <summary>
        /// Clear all parameters
        /// </summary>
        void ParametersClear();

        /// <summary>
        /// Check if record exists
        /// </summary>
        /// <param name="columnName">Column name to search</param>
        /// <param name="value">Value to search</param>
        /// <param name="tableName">Table name</param>
        /// <returns>DataTable if found, null if not</returns>
        DataTable FindOrDefault(string columnName, object value, string tableName);

        /// <summary>
        /// Get last inserted primary key value
        /// </summary>
        /// <param name="tableName">Table name</param>
        /// <param name="columnName">Primary key column name</param>
        /// <returns>Last primary key value</returns>
        int PKLastKeyOrDefault(string tableName, string columnName);

        /// <summary>
        /// Execute SQL query and return ALL data (use with caution for large datasets)
        /// </summary>
        /// <param name="sql">SQL query</param>
        /// <returns>DataTable with all results</returns>
        DataTable RunDataTableAll(string sql);

        /// <summary>
        /// Execute SQL query and return DataTable with pagination (memory-safe)
        /// </summary>
        /// <param name="sql">SQL query</param>
        /// <param name="pageNumber">Page number (1-based)</param>
        /// <param name="pageSize">Number of records per page</param>
        /// <returns>DataTable with paginated results</returns>
        DataTable RunDataTablePaged(string sql, int pageNumber, int pageSize);

        /// <summary>
        /// Execute SQL query and stream results row by row (memory-efficient for large datasets)
        /// </summary>
        /// <param name="sql">SQL query</param>
        /// <param name="action">Action to perform on each row</param>
        void RunDataReader(string sql, Action<IDataReader> action);

        /// <summary>
        /// Execute SQL query and yield results as IEnumerable (lazy loading)
        /// </summary>
        /// <param name="sql">SQL query</param>
        /// <returns>IEnumerable of dictionary representing each row</returns>
        IEnumerable<Dictionary<string, object>> RunDataReaderEnumerable(string sql);

        /// <summary>
        /// Execute SQL query with batch size limit to prevent memory issues
        /// </summary>
        /// <param name="sql">SQL query</param>
        /// <param name="maxRows">Maximum number of rows to return</param>
        /// <returns>DataTable with limited results</returns>
        DataTable RunDataTableLimited(string sql, int maxRows);
    }
}
