using System;
using System.Collections.Generic;
using System.Globalization;
using mersolutionCore.Command.Abstractions;

namespace mersolutionCore.Config
{
    /// <summary>
    /// SQL log: every command run through the library (models, QueryBuilder, RawQuery, DbCommandBase)
    /// raises <see cref="QueryExecuted"/> with its SQL, duration and error.
    /// <code>
    /// MersoLog.QueryExecuted += e => logger.LogDebug("{Sql} ({Ms} ms)", e.Sql, e.Duration.TotalMilliseconds);
    /// </code>
    /// </summary>
    public static class MersoLog
    {
        /// <summary>
        /// Raised after each command (also when it failed — see <see cref="QueryLogEntry.Exception"/>).
        /// Exceptions thrown by a handler are ignored so logging never breaks a query.
        /// </summary>
        public static event Action<QueryLogEntry>? QueryExecuted;

        /// <summary>
        /// Put parameter values into the log (default false: only names — values may hold passwords or personal data)
        /// </summary>
        public static bool IncludeParameterValues { get; set; }

        internal static bool IsEnabled => QueryExecuted != null;

        internal static void Publish(QueryLogEntry entry)
        {
            var handler = QueryExecuted;
            if (handler == null)
                return;

            try
            {
                handler(entry);
            }
            catch
            {
                // a failing log handler must not fail the query
            }
        }
    }

    /// <summary>
    /// One executed SQL command
    /// </summary>
    public sealed class QueryLogEntry
    {
        public string Sql { get; internal set; } = string.Empty;
        public DbProviderType Provider { get; internal set; }
        public DateTime StartedAtUtc { get; internal set; }
        public TimeSpan Duration { get; internal set; }

        /// <summary>
        /// Parameters in order; <see cref="QueryLogParameter.Value"/> is null unless <see cref="MersoLog.IncludeParameterValues"/> is set
        /// </summary>
        public IReadOnlyList<QueryLogParameter> Parameters { get; internal set; } = new QueryLogParameter[0];

        /// <summary>
        /// Error of a failed command (null when it succeeded)
        /// </summary>
        public Exception? Exception { get; internal set; }

        public bool Succeeded => Exception == null;

        public override string ToString()
        {
            var ms = Duration.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture);
            return Succeeded ? $"[{ms} ms] {Sql}" : $"[{ms} ms, failed: {Exception?.Message}] {Sql}";
        }
    }

    /// <summary>
    /// Logged command parameter
    /// </summary>
    public sealed class QueryLogParameter
    {
        public string Name { get; internal set; } = string.Empty;
        public object? Value { get; internal set; }
    }
}
