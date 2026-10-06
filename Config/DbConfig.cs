using System;
using System.IO;
using mersolutionCore.Command.Abstractions;
using mersolutionCore.Command.SqlServer;
using mersolutionCore.Command.MySQL;
using mersolutionCore.Command.PostgreSQL;
using mersolutionCore.Command.SQLite;
using mersolutionCore.Command.MariaDB;

namespace mersolutionCore.Config
{
    /// <summary>
    /// Database configuration and factory
    /// </summary>
    public static class DbConfig
    {
        private static ConnectionConfig? _config;
        private static DbProviderType _providerType = DbProviderType.SqlServer;

        /// <summary>
        /// Configure database connection
        /// </summary>
        public static void Configure(ConnectionConfig config, DbProviderType providerType = DbProviderType.SqlServer)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _providerType = providerType;
        }

        /// <summary>
        /// Configure with connection string
        /// </summary>
        public static void Configure(string connectionString, DbProviderType providerType = DbProviderType.SqlServer)
        {
            _config = new ConnectionConfig { ConnectionString = connectionString };
            _providerType = providerType;
        }

        /// <summary>
        /// Configure SQL Server with Windows Authentication
        /// </summary>
        public static void ConfigureSqlServer(string server, string database)
        {
            _config = new ConnectionConfig
            {
                Server = server,
                Database = database,
                IntegratedSecurity = true
            };
            _providerType = DbProviderType.SqlServer;
        }

        /// <summary>
        /// Configure SQL Server with SQL Authentication
        /// </summary>
        public static void ConfigureSqlServer(string server, string database, string username, string password)
        {
            _config = new ConnectionConfig
            {
                Server = server,
                Database = database,
                Username = username,
                Password = password,
                IntegratedSecurity = false
            };
            _providerType = DbProviderType.SqlServer;
        }

        /// <summary>
        /// Configure MySQL
        /// </summary>
        public static void ConfigureMySQL(string server, string database, string username, string password, int port = 3306)
        {
            _config = new ConnectionConfig
            {
                Server = server,
                Database = database,
                Username = username,
                Password = password,
                Port = port
            };
            _providerType = DbProviderType.MySQL;
        }

        /// <summary>
        /// Configure PostgreSQL
        /// </summary>
        public static void ConfigurePostgreSQL(string server, string database, string username, string password, int port = 5432)
        {
            _config = new ConnectionConfig
            {
                Server = server,
                Database = database,
                Username = username,
                Password = password,
                Port = port
            };
            _providerType = DbProviderType.PostgreSQL;
        }

        /// <summary>
        /// Configure MariaDB
        /// </summary>
        public static void ConfigureMariaDB(string server, string database, string username, string password, int port = 3306)
        {
            _config = new ConnectionConfig
            {
                Server = server,
                Database = database,
                Username = username,
                Password = password,
                Port = port
            };
            _providerType = DbProviderType.MariaDB;
        }

        /// <summary>
        /// Configure SQLite
        /// </summary>
        public static void ConfigureSQLite(string databasePath)
        {
            _config = new ConnectionConfig
            {
                // Builder quotes paths with ';' or spaces correctly
                ConnectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = databasePath }.ConnectionString
            };
            _providerType = DbProviderType.SQLite;
        }

        /// <summary>
        /// Create database command instance
        /// </summary>
        public static DbCommandBase CreateConnection()
        {
            if (_config == null)
                throw new InvalidOperationException("Database not configured. Call DbConfig.Configure() first.");

            return Create(_config, _providerType);
        }

        /// <summary>
        /// Register an additional named database. Models use it with <c>[Connection("name")]</c>,
        /// queries with <c>Model.On("name")</c>, a code block with <c>MersoConnection.Use("name")</c>.
        /// </summary>
        public static void AddConnection(string name, string connectionString, DbProviderType providerType)
        {
            AddConnection(name, new ConnectionConfig { ConnectionString = connectionString }, providerType);
        }

        /// <summary>
        /// Register an additional named database (see <see cref="AddConnection(string, string, DbProviderType)"/>)
        /// </summary>
        public static void AddConnection(string name, ConnectionConfig config, DbProviderType providerType)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
            if (config == null) throw new ArgumentNullException(nameof(config));

            _named[name] = new NamedConnection(config, providerType);
            mersolutionCore.ORM.ModelBase.Configure(name, () => CreateConnection(name));
        }

        /// <summary>
        /// Add a read replica of the default connection (or of a named one): SELECTs go to the replicas (round
        /// robin), writes and transactions to the primary — see <see cref="mersolutionCore.ORM.ReadReplicas"/>.
        /// The replica uses the provider of its connection.
        /// </summary>
        public static void AddReadReplica(string connectionString, string? connectionName = null)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentNullException(nameof(connectionString));

            DbProviderType provider;
            if (string.IsNullOrEmpty(connectionName))
            {
                if (_config == null)
                    throw new InvalidOperationException("Configure the default database before adding its read replica.");
                provider = _providerType;
            }
            else if (_named.TryGetValue(connectionName!, out var named))
            {
                provider = named.ProviderType;
            }
            else
            {
                throw new InvalidOperationException($"Connection '{connectionName}' is not configured. Call DbConfig.AddConnection(\"{connectionName}\", ...) first.");
            }

            var config = new ConnectionConfig { ConnectionString = connectionString };
            mersolutionCore.ORM.ReadReplicas.Add(() => Create(config, provider), connectionName);
        }

        /// <summary>
        /// Create a command for a connection registered with <see cref="AddConnection(string, string, DbProviderType)"/>
        /// </summary>
        public static DbCommandBase CreateConnection(string name)
        {
            if (string.IsNullOrEmpty(name))
                return CreateConnection();

            if (!_named.TryGetValue(name, out var named))
                throw new InvalidOperationException($"Connection '{name}' is not configured. Call DbConfig.AddConnection(\"{name}\", ...) first.");

            return Create(named.Config, named.ProviderType);
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, NamedConnection> _named =
            new System.Collections.Concurrent.ConcurrentDictionary<string, NamedConnection>(StringComparer.OrdinalIgnoreCase);

        private sealed class NamedConnection
        {
            public NamedConnection(ConnectionConfig config, DbProviderType providerType)
            {
                Config = config;
                ProviderType = providerType;
            }

            public ConnectionConfig Config { get; }
            public DbProviderType ProviderType { get; }
        }

        private static DbCommandBase Create(ConnectionConfig config, DbProviderType providerType)
        {
            switch (providerType)
            {
                case DbProviderType.SqlServer:
                    return new SqlServerCommand(config);
                case DbProviderType.MySQL:
                    return new MySqlCommand(config);
                case DbProviderType.PostgreSQL:
                    return new PostgreSqlCommand(config);
                case DbProviderType.SQLite:
                    return new SQLiteCommand(config);
                case DbProviderType.MariaDB:
                    return new MariaDbCommand(config);
                default:
                    throw new NotSupportedException($"Provider {providerType} is not supported.");
            }
        }

        /// <summary>
        /// Get current provider type
        /// </summary>
        public static DbProviderType ProviderType => _providerType;

        /// <summary>
        /// Get current config
        /// </summary>
        public static ConnectionConfig? Config => _config;
    }
}
