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
        private static ConnectionConfig _config;
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
                ConnectionString = $"Data Source={databasePath}"
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

            switch (_providerType)
            {
                case DbProviderType.SqlServer:
                    return new SqlServerCommand(_config);
                case DbProviderType.MySQL:
                    return new MySqlCommand(_config);
                case DbProviderType.PostgreSQL:
                    return new PostgreSqlCommand(_config);
                case DbProviderType.SQLite:
                    return new SQLiteCommand(_config);
                case DbProviderType.MariaDB:
                    return new MariaDbCommand(_config);
                default:
                    throw new NotSupportedException($"Provider {_providerType} is not supported.");
            }
        }

        /// <summary>
        /// Get current provider type
        /// </summary>
        public static DbProviderType ProviderType => _providerType;

        /// <summary>
        /// Get current config
        /// </summary>
        public static ConnectionConfig Config => _config;
    }
}
