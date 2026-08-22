namespace mersolutionCore.Command.Abstractions
{
    /// <summary>
    /// Database connection configuration
    /// </summary>
    public class ConnectionConfig
    {
        /// <summary>
        /// Server/Host address
        /// </summary>
        public string Server { get; set; }

        /// <summary>
        /// Database name
        /// </summary>
        public string Database { get; set; }

        /// <summary>
        /// Username for authentication
        /// </summary>
        public string Username { get; set; }

        /// <summary>
        /// Password for authentication
        /// </summary>
        public string Password { get; set; }

        /// <summary>
        /// Port number (optional, uses default if not specified)
        /// </summary>
        public int? Port { get; set; }

        /// <summary>
        /// Connection timeout in seconds
        /// </summary>
        public int Timeout { get; set; } = 30;

        /// <summary>
        /// Use integrated security (Windows Authentication for SQL Server)
        /// </summary>
        public bool IntegratedSecurity { get; set; } = false;

        /// <summary>
        /// Additional connection string parameters
        /// </summary>
        public string AdditionalParameters { get; set; }

        /// <summary>
        /// Full connection string (if provided, overrides other properties)
        /// </summary>
        public string ConnectionString { get; set; }
    }
}
