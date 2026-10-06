using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace mersolutionCore.Command.Abstractions
{
    /// <summary>
    /// Native bulk copy (SqlBulkCopy, PostgreSQL COPY, MySqlBulkCopy). Providers without one return null and
    /// <c>BulkOperations.BulkCopy</c> falls back to batched multi-row INSERTs.
    /// </summary>
    public abstract partial class DbCommandBase
    {
        /// <summary>
        /// Copy <paramref name="rows"/> into <paramref name="quotedTable"/> with the provider's bulk API; null when the
        /// provider has none. Joins the active (MersoTransaction) transaction.
        /// </summary>
        /// <param name="quotedTable">Table name, quoted for the provider</param>
        /// <param name="columns">Column names (not quoted), in the order of the row values</param>
        /// <param name="rows">Values per row (already converted for the database)</param>
        public virtual int? TryBulkCopy(string quotedTable, IReadOnlyList<string> columns, IReadOnlyList<object?[]> rows)
        {
            return null;
        }

        /// <summary>
        /// Async counterpart of <see cref="TryBulkCopy"/> (default: the synchronous copy)
        /// </summary>
        public virtual Task<int?> TryBulkCopyAsync(string quotedTable, IReadOnlyList<string> columns, IReadOnlyList<object?[]> rows,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(TryBulkCopy(quotedTable, columns, rows));
        }

        /// <summary>
        /// A value as the driver should get it (enums as numbers, DateOnly / TimeOnly on older drivers, null → DBNull)
        /// </summary>
        protected static object BulkValue(object? value)
        {
            return NormalizeParameterValue(value);
        }

        /// <summary>
        /// Rows as a DataTable with object columns named like the table columns (SqlBulkCopy / MySqlBulkCopy source)
        /// </summary>
        protected static DataTable BulkTable(IReadOnlyList<string> columns, IReadOnlyList<object?[]> rows)
        {
            var table = new DataTable();
            foreach (var column in columns)
                table.Columns.Add(column, typeof(object));

            foreach (var row in rows)
            {
                var values = new object[row.Length];
                for (int i = 0; i < row.Length; i++)
                    values[i] = BulkValue(row[i]);
                table.Rows.Add(values);
            }
            return table;
        }

        /// <summary>
        /// Open the connection (or join the transaction) and start the SQL log entry for a bulk copy; close with
        /// <see cref="CloseConnection"/>
        /// </summary>
        protected void BeginBulk(string description)
        {
            mersolutionCore.ORM.ReadReplicas.MarkWrite();
            PrepareCommand(description);
        }

        /// <summary>
        /// Async counterpart of <see cref="BeginBulk"/>
        /// </summary>
        protected Task BeginBulkAsync(string description, CancellationToken cancellationToken)
        {
            mersolutionCore.ORM.ReadReplicas.MarkWrite();
            return PrepareCommandAsync(description, cancellationToken);
        }
    }
}
