using System;
using mersolutionCore.Command.Abstractions;

namespace mersolutionCore.ORM.Migration
{
    /// <summary>
    /// Alias for <see cref="Migrator"/>. Prefer <see cref="Migrator"/> in new code.
    /// </summary>
    public class MigrationRunner : Migrator
    {
        /// <summary>
        /// Uses the connection factory registered by DbContext / ModelBase.
        /// </summary>
        public MigrationRunner()
            : base(RequireConnectionFactory())
        {
        }

        public MigrationRunner(Func<DbCommandBase> connectionFactory, string migrationsTable = "__migrations")
            : base(connectionFactory, migrationsTable)
        {
        }

        /// <summary>
        /// Apply all pending migrations (same as <see cref="Migrator.Migrate"/>).
        /// </summary>
        public MigrationResult Run() => Migrate();

        private static Func<DbCommandBase> RequireConnectionFactory()
        {
            if (ModelBase.ConnectionFactory == null)
                throw new InvalidOperationException("Database not configured. Create a DbContext first, or pass a connection factory to MigrationRunner.");

            return ModelBase.ConnectionFactory;
        }
    }
}
