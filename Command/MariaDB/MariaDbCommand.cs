using mersolutionCore.Command.Abstractions;

namespace mersolutionCore.Command.MariaDB
{
    /// <summary>
    /// MariaDB database command implementation (uses MySqlConnector which supports MariaDB natively).
    /// Same wire protocol and SQL dialect as MySQL, so all behaviour comes from <see cref="MySQL.MySqlCommand"/>.
    /// </summary>
    public class MariaDbCommand : MySQL.MySqlCommand
    {
        public override DbProviderType ProviderType => DbProviderType.MariaDB;

        /// <summary>
        /// Create MariaDB command with connection config
        /// </summary>
        /// <param name="config">Connection configuration</param>
        public MariaDbCommand(ConnectionConfig config) : base(config)
        {
        }

        /// <summary>
        /// Create MariaDB command with connection string
        /// </summary>
        /// <param name="connectionString">Connection string</param>
        public MariaDbCommand(string connectionString) : base(connectionString)
        {
        }
    }
}
