using System;
using mersolutionCore.Command.Abstractions;
using mersolutionCore.Command.SqlServer;
using mersolutionCore.Command.MySQL;
using mersolutionCore.Command.SQLite;
using mersolutionCore.Command.PostgreSQL;
using mersolutionCore.Command.MariaDB;

namespace mersolutionCore.Command
{
    /// <summary>
    /// Factory for creating database command instances
    /// </summary>
    public static class DbFactory
    {
        /// <summary>
        /// Create database command based on provider type
        /// </summary>
        /// <param name="providerType">Database provider type</param>
        /// <param name="config">Connection configuration</param>
        /// <returns>Database command instance</returns>
        public static IDbCommand Create(DbProviderType providerType, ConnectionConfig config)
        {
            switch (providerType)
            {
                case DbProviderType.SqlServer:
                    return new SqlServerCommand(config);

                case DbProviderType.MySQL:
                    return new MySqlCommand(config);

                case DbProviderType.SQLite:
                    return new SQLiteCommand(config);

                case DbProviderType.PostgreSQL:
                    return new PostgreSqlCommand(config);

                case DbProviderType.MariaDB:
                    return new MariaDbCommand(config);

                default:
                    throw new ArgumentException($"Unsupported provider type: {providerType}");
            }
        }

        /// <summary>
        /// Create database command based on provider type with connection string
        /// </summary>
        /// <param name="providerType">Database provider type</param>
        /// <param name="connectionString">Connection string</param>
        /// <returns>Database command instance</returns>
        public static IDbCommand Create(DbProviderType providerType, string connectionString)
        {
            switch (providerType)
            {
                case DbProviderType.SqlServer:
                    return new SqlServerCommand(connectionString);

                case DbProviderType.MySQL:
                    return new MySqlCommand(connectionString);

                case DbProviderType.SQLite:
                    return new SQLiteCommand(connectionString);

                case DbProviderType.PostgreSQL:
                    return new PostgreSqlCommand(connectionString);

                case DbProviderType.MariaDB:
                    return new MariaDbCommand(connectionString);

                default:
                    throw new ArgumentException($"Unsupported provider type: {providerType}");
            }
        }

        /// <summary>
        /// Create SQL Server command
        /// </summary>
        public static SqlServerCommand CreateSqlServer(ConnectionConfig config)
        {
            return new SqlServerCommand(config);
        }

        /// <summary>
        /// Create SQL Server command with connection string
        /// </summary>
        public static SqlServerCommand CreateSqlServer(string connectionString)
        {
            return new SqlServerCommand(connectionString);
        }

        /// <summary>
        /// Create MySQL command
        /// </summary>
        public static MySqlCommand CreateMySql(ConnectionConfig config)
        {
            return new MySqlCommand(config);
        }

        /// <summary>
        /// Create MySQL command with connection string
        /// </summary>
        public static MySqlCommand CreateMySql(string connectionString)
        {
            return new MySqlCommand(connectionString);
        }

        /// <summary>
        /// Create SQLite command
        /// </summary>
        public static SQLiteCommand CreateSQLite(ConnectionConfig config)
        {
            return new SQLiteCommand(config);
        }

        /// <summary>
        /// Create SQLite command with database file path
        /// </summary>
        public static SQLiteCommand CreateSQLite(string databasePath)
        {
            return new SQLiteCommand(databasePath, true);
        }

        /// <summary>
        /// Create PostgreSQL command
        /// </summary>
        public static PostgreSqlCommand CreatePostgreSql(ConnectionConfig config)
        {
            return new PostgreSqlCommand(config);
        }

        /// <summary>
        /// Create PostgreSQL command with connection string
        /// </summary>
        public static PostgreSqlCommand CreatePostgreSql(string connectionString)
        {
            return new PostgreSqlCommand(connectionString);
        }

        /// <summary>
        /// Create MariaDB command
        /// </summary>
        public static MariaDbCommand CreateMariaDb(ConnectionConfig config)
        {
            return new MariaDbCommand(config);
        }

        /// <summary>
        /// Create MariaDB command with connection string
        /// </summary>
        public static MariaDbCommand CreateMariaDb(string connectionString)
        {
            return new MariaDbCommand(connectionString);
        }
    }
}
