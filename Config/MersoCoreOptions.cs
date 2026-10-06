using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using mersolutionCore.Cache;
using mersolutionCore.Command.Abstractions;
using mersolutionCore.ORM;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace mersolutionCore.Config
{
    /// <summary>
    /// Settings for <c>services.AddMersoCore(options =&gt; ...)</c>
    /// </summary>
    public sealed class MersoCoreOptions
    {
        private readonly List<Action> _steps = new List<Action>();

        internal bool DistributedCacheFromServices { get; private set; }
        internal string CacheKeyPrefix { get; private set; } = "mc:";
        internal bool SqlLogEnabled { get; private set; }
        internal LogLevel SqlLogLevel { get; private set; } = LogLevel.Debug;

        /// <summary>Default database: SQL Server</summary>
        public MersoCoreOptions UseSqlServer(string connectionString) => UseConnection(connectionString, DbProviderType.SqlServer);

        /// <summary>Default database: MySQL</summary>
        public MersoCoreOptions UseMySql(string connectionString) => UseConnection(connectionString, DbProviderType.MySQL);

        /// <summary>Default database: MariaDB</summary>
        public MersoCoreOptions UseMariaDb(string connectionString) => UseConnection(connectionString, DbProviderType.MariaDB);

        /// <summary>Default database: PostgreSQL</summary>
        public MersoCoreOptions UsePostgreSql(string connectionString) => UseConnection(connectionString, DbProviderType.PostgreSQL);

        /// <summary>Default database: SQLite file</summary>
        public MersoCoreOptions UseSqlite(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath)) throw new ArgumentNullException(nameof(databasePath));

            _steps.Add(() =>
            {
                DbConfig.ConfigureSQLite(databasePath);
                ModelBase.Configure(DbConfig.CreateConnection);
            });
            return this;
        }

        /// <summary>Default database with an explicit provider</summary>
        public MersoCoreOptions UseConnection(string connectionString, DbProviderType provider)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentNullException(nameof(connectionString));

            _steps.Add(() =>
            {
                DbConfig.Configure(connectionString, provider);
                ModelBase.Configure(DbConfig.CreateConnection);
            });
            return this;
        }

        /// <summary>Extra named database ([Connection("name")], Model.On("name"), MersoConnection.Use("name"))</summary>
        public MersoCoreOptions AddConnection(string name, string connectionString, DbProviderType provider)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
            if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentNullException(nameof(connectionString));

            _steps.Add(() => DbConfig.AddConnection(name, connectionString, provider));
            return this;
        }

        /// <summary>
        /// Read replica of the default connection (or of a named one): SELECTs go there, writes and transactions to
        /// the primary — see <see cref="ReadReplicas"/>. Call it after the connection it belongs to.
        /// </summary>
        public MersoCoreOptions AddReadReplica(string connectionString, string? connectionName = null)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentNullException(nameof(connectionString));

            _steps.Add(() => DbConfig.AddReadReplica(connectionString, connectionName));
            return this;
        }

        /// <summary>
        /// Keep Cache / QCache / MersoCache / query cache entries in the registered <see cref="IDistributedCache"/>
        /// (AddStackExchangeRedisCache, AddDistributedSqlServerCache ...) so every server shares them
        /// </summary>
        public MersoCoreOptions UseDistributedCache(string keyPrefix = "mc:")
        {
            DistributedCacheFromServices = true;
            CacheKeyPrefix = keyPrefix ?? string.Empty;
            return this;
        }

        /// <summary>
        /// Keep the cache in your own <see cref="ICacheStore"/>
        /// </summary>
        public MersoCoreOptions UseCacheStore(ICacheStore store, string keyPrefix = "mc:")
        {
            if (store == null) throw new ArgumentNullException(nameof(store));

            DistributedCacheFromServices = false;
            _steps.Add(() => Cache.Cache.UseStore(store, keyPrefix));
            return this;
        }

        /// <summary>
        /// Write every SQL command to ILogger (category "mersolutionCore.Sql"); failed commands are logged as warnings
        /// </summary>
        /// <param name="level">Level of successful commands</param>
        /// <param name="includeParameterValues">Also log parameter values (may contain personal data)</param>
        public MersoCoreOptions LogSql(LogLevel level = LogLevel.Debug, bool includeParameterValues = false)
        {
            SqlLogEnabled = true;
            SqlLogLevel = level;
            _steps.Add(() => MersoLog.IncludeParameterValues = includeParameterValues);
            return this;
        }

        /// <summary>
        /// Retries of transient errors (default 2; 0 = off) — see <see cref="MersoRetry"/>
        /// </summary>
        public MersoCoreOptions UseRetry(int maxRetries, TimeSpan? baseDelay = null)
        {
            _steps.Add(() =>
            {
                MersoRetry.MaxRetries = maxRetries;
                if (baseDelay.HasValue)
                    MersoRetry.BaseDelay = baseDelay.Value;
            });
            return this;
        }

        /// <summary>
        /// Settings that need no services (databases, retry, own cache store): applied by AddMersoCore at once,
        /// so models work before the host starts (migrations in Program.cs)
        /// </summary>
        internal void ApplyStatic()
        {
            foreach (var step in _steps)
                step();
        }
    }

    /// <summary>
    /// Wires the settings that need the service provider: IDistributedCache and ILogger
    /// </summary>
    internal static class MersoCoreRuntime
    {
        private static readonly object Gate = new object();
        private static Action<QueryLogEntry>? _sqlHandler;
        private static Action<Exception, int, TimeSpan>? _retryHandler;

        internal static void Start(MersoCoreOptions options, IServiceProvider services)
        {
            lock (Gate)
            {
                if (options.DistributedCacheFromServices)
                {
                    var distributed = services.GetService<IDistributedCache>()
                        ?? throw new InvalidOperationException("UseDistributedCache(): register an IDistributedCache first (e.g. services.AddStackExchangeRedisCache(...)).");
                    Cache.Cache.UseStore(new DistributedCacheStore(distributed), options.CacheKeyPrefix);
                }

                var loggerFactory = services.GetService<ILoggerFactory>();
                if (loggerFactory == null)
                    return;

                // Re-wiring (tests, several hosts): replace the previous handlers instead of logging twice
                if (_sqlHandler != null)
                    MersoLog.QueryExecuted -= _sqlHandler;
                if (_retryHandler != null)
                    MersoRetry.Retrying -= _retryHandler;
                _sqlHandler = null;

                var retryLogger = loggerFactory.CreateLogger("mersolutionCore.Retry");
                _retryHandler = (error, attempt, delay) =>
                    retryLogger.LogInformation(error, "Transient database error, retry {Attempt} in {Delay} ms", attempt, (int)delay.TotalMilliseconds);
                MersoRetry.Retrying += _retryHandler;

                if (!options.SqlLogEnabled)
                    return;

                var sqlLogger = loggerFactory.CreateLogger("mersolutionCore.Sql");
                var level = options.SqlLogLevel;
                _sqlHandler = entry =>
                {
                    var ms = entry.Duration.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture);
                    var parameters = string.Join(", ", entry.Parameters.Select(p => MersoLog.IncludeParameterValues ? $"{p.Name}={p.Value ?? "NULL"}" : p.Name));
                    if (entry.Succeeded)
                    {
                        if (sqlLogger.IsEnabled(level))
                            sqlLogger.Log(level, "SQL ({Ms} ms) {Sql} [{Parameters}]", ms, entry.Sql, parameters);
                    }
                    else
                    {
                        sqlLogger.LogWarning(entry.Exception, "SQL failed ({Ms} ms) {Sql} [{Parameters}]", ms, entry.Sql, parameters);
                    }
                };
                MersoLog.QueryExecuted += _sqlHandler;
            }
        }
    }
}
