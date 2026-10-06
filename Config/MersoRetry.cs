using System;
using System.Data.Common;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace mersolutionCore.Config
{
    /// <summary>
    /// Automatic retry of transient database errors (deadlock, serialization failure, lock timeout,
    /// connection refused / dropped, too many connections). Commands outside a transaction are retried:
    /// <list type="bullet">
    /// <item>when the connection could not be opened — nothing was sent yet;</item>
    /// <item>after a deadlock / serialization failure / lock timeout — the server rolled the statement back;</item>
    /// <item>after a dropped connection only for SELECT — a write may already have been committed.</item>
    /// </list>
    /// Inside a transaction single statements are never retried (the transaction is already broken); use
    /// <c>MersoTransaction.Run(action, attempts)</c> to re-run the whole transaction.
    /// </summary>
    public static class MersoRetry
    {
        private static int _maxRetries = 2;

        /// <summary>
        /// Retries after the first failure (default 2 → up to 3 attempts; 0 turns retrying off)
        /// </summary>
        public static int MaxRetries
        {
            get => _maxRetries;
            set => _maxRetries = Math.Max(0, value);
        }

        /// <summary>
        /// Wait before the first retry; doubled for each further retry, with ±20 % jitter (default 100 ms)
        /// </summary>
        public static TimeSpan BaseDelay { get; set; } = TimeSpan.FromMilliseconds(100);

        /// <summary>
        /// Upper limit of a single wait (default 2 s)
        /// </summary>
        public static TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Extra rule: return true for errors of your own that are safe to retry like a deadlock
        /// </summary>
        public static Func<Exception, bool>? IsTransient { get; set; }

        /// <summary>
        /// Raised before each retry (error, attempt number starting at 1, wait)
        /// </summary>
        public static event Action<Exception, int, TimeSpan>? Retrying;

        [ThreadStatic] private static Random? _random;

        /// <summary>
        /// Kind of a transient error
        /// </summary>
        internal enum TransientKind
        {
            None,
            /// <summary>Statement rolled back by the server (deadlock, serialization failure, lock timeout, busy)</summary>
            RolledBack,
            /// <summary>Connection refused, dropped or timed out (a write may or may not have happened)</summary>
            Connection
        }

        internal static TransientKind Classify(Exception error)
        {
            for (var e = error; e != null; e = e.InnerException)
            {
                var kind = ClassifyOne(e);
                if (kind != TransientKind.None)
                    return kind;
            }

            var custom = IsTransient;
            if (custom != null)
            {
                try
                {
                    if (custom(error))
                        return TransientKind.RolledBack;
                }
                catch
                {
                    // a failing rule means "not transient"
                }
            }
            return TransientKind.None;
        }

        private static TransientKind ClassifyOne(Exception e)
        {
            switch (e)
            {
                case Microsoft.Data.SqlClient.SqlException sql:
                    foreach (Microsoft.Data.SqlClient.SqlError err in sql.Errors)
                    {
                        var kind = SqlServerKind(err.Number);
                        if (kind != TransientKind.None)
                            return kind;
                    }
                    return SqlServerKind(sql.Number);

                case MySqlConnector.MySqlException my:
                    return MySqlKind(my.Number);

                case Npgsql.PostgresException pg:
                    return PostgresKind(pg.SqlState);

                case Npgsql.NpgsqlException npg:
                    return npg.IsTransient ? TransientKind.Connection : TransientKind.None;

                case Microsoft.Data.Sqlite.SqliteException lite:
                    // SQLITE_BUSY / SQLITE_LOCKED: the statement did not run
                    return lite.SqliteErrorCode == 5 || lite.SqliteErrorCode == 6 ? TransientKind.RolledBack : TransientKind.None;

                case TimeoutException _:
                case SocketException _:
                case IOException _:
                    return TransientKind.Connection;

                default:
                    return TransientKind.None;
            }
        }

        private static TransientKind SqlServerKind(int number)
        {
            switch (number)
            {
                case 1205:  // deadlock victim
                case 1222:  // lock request timeout
                case 41301: // in-memory OLTP dependency / conflicts
                case 41302:
                case 41305:
                case 41325:
                    return TransientKind.RolledBack;

                case -2:    // command timeout
                case 20:    // instance does not support encryption / transport issue
                case 64:    // connection was dropped
                case 121:   // semaphore timeout
                case 233:   // no process on the other end of the pipe
                case 4060:  // database unavailable
                case 4221:  // read-only replica not ready
                case 10053: // transport-level error
                case 10054: // connection reset
                case 10060: // connection timeout
                case 10928: // resource limit (Azure)
                case 10929:
                case 40197: // service error (Azure)
                case 40501: // service busy (Azure)
                case 40613: // database not available (Azure)
                case 49918:
                case 49919:
                case 49920:
                    return TransientKind.Connection;

                default:
                    return TransientKind.None;
            }
        }

        private static TransientKind MySqlKind(int number)
        {
            switch (number)
            {
                case 1213: // deadlock
                case 1205: // lock wait timeout (statement rolled back)
                    return TransientKind.RolledBack;

                case 1040: // too many connections
                case 1042: // unable to connect to host
                case 1053: // server shutdown in progress
                case 1158: // network read / write errors
                case 1159:
                case 1160:
                case 1161:
                case 1927: // connection killed (MariaDB)
                case 2002: // can't connect
                case 2003:
                case 2006: // server has gone away
                case 2013: // lost connection during query
                case 2055:
                case 4031: // client disconnected for inactivity (MySQL 8)
                    return TransientKind.Connection;

                default:
                    return TransientKind.None;
            }
        }

        private static TransientKind PostgresKind(string sqlState)
        {
            switch (sqlState)
            {
                case "40001": // serialization_failure
                case "40P01": // deadlock_detected
                case "55P03": // lock_not_available
                    return TransientKind.RolledBack;

                case "53300": // too_many_connections
                case "57P01": // admin_shutdown
                case "57P02": // crash_shutdown
                case "57P03": // cannot_connect_now
                case "08000": // connection_exception
                case "08001":
                case "08003":
                case "08004":
                case "08006":
                case "08007":
                case "08P01":
                    return TransientKind.Connection;

                default:
                    return TransientKind.None;
            }
        }

        /// <summary>
        /// Decide whether a failed command is retried
        /// </summary>
        /// <param name="error">The failure</param>
        /// <param name="attempt">Retries done so far (0 after the first failure)</param>
        /// <param name="sent">The command reached the server (the connection was open)</param>
        /// <param name="readOnly">The command only reads</param>
        internal static bool ShouldRetry(Exception error, int attempt, bool sent, bool readOnly)
        {
            if (attempt >= _maxRetries || error is OperationCanceledException)
                return false;

            switch (Classify(error))
            {
                case TransientKind.RolledBack:
                    return true;
                case TransientKind.Connection:
                    return !sent || readOnly;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Wait before retry number <paramref name="attempt"/> (0-based) and raise <see cref="Retrying"/>
        /// </summary>
        internal static TimeSpan NextDelay(Exception error, int attempt)
        {
            var random = _random ?? (_random = new Random(Guid.NewGuid().GetHashCode()));
            var factor = Math.Pow(2, attempt) * (0.8 + random.NextDouble() * 0.4);
            var ms = Math.Min(MaxDelay.TotalMilliseconds, Math.Max(0, BaseDelay.TotalMilliseconds) * factor);
            var delay = TimeSpan.FromMilliseconds(ms);

            var handler = Retrying;
            if (handler != null)
            {
                try
                {
                    handler(error, attempt + 1, delay);
                }
                catch
                {
                    // a failing handler must not stop the retry
                }
            }
            return delay;
        }

        internal static void Wait(Exception error, int attempt)
        {
            var delay = NextDelay(error, attempt);
            if (delay > TimeSpan.Zero)
                Thread.Sleep(delay);
        }

        internal static Task WaitAsync(Exception error, int attempt, CancellationToken cancellationToken)
        {
            var delay = NextDelay(error, attempt);
            return delay > TimeSpan.Zero ? Task.Delay(delay, cancellationToken) : Task.CompletedTask;
        }

        /// <summary>
        /// True for SQL that only reads (SELECT / WITH ... SELECT / SHOW / PRAGMA)
        /// </summary>
        internal static bool IsReadOnlySql(string sql)
        {
            if (string.IsNullOrEmpty(sql))
                return false;

            var text = sql.TrimStart(' ', '\t', '\r', '\n', '(');
            return StartsWithWord(text, "SELECT") || StartsWithWord(text, "SHOW") || StartsWithWord(text, "PRAGMA")
                   || (StartsWithWord(text, "WITH") && text.IndexOf("INSERT", StringComparison.OrdinalIgnoreCase) < 0
                       && text.IndexOf("UPDATE", StringComparison.OrdinalIgnoreCase) < 0
                       && text.IndexOf("DELETE", StringComparison.OrdinalIgnoreCase) < 0);
        }

        private static bool StartsWithWord(string text, string word)
        {
            return text.Length >= word.Length
                   && string.Compare(text, 0, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) == 0
                   && (text.Length == word.Length || !char.IsLetterOrDigit(text[word.Length]));
        }

        /// <summary>
        /// A failed transaction can be re-run from the start (MersoTransaction.Run with attempts): any transient
        /// error before COMMIT (the server rolls the transaction back); during COMMIT only a rolled-back one —
        /// a dropped connection there may have committed already.
        /// </summary>
        internal static bool CanRerunTransaction(Exception error, bool duringCommit)
        {
            if (error is OperationCanceledException)
                return false;

            var kind = Classify(error);
            return kind == TransientKind.RolledBack || (kind == TransientKind.Connection && !duringCommit);
        }
    }
}
