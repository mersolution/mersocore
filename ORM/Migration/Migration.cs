using System;

namespace mersolutionCore.ORM.Migration
{
    /// <summary>
    /// Base class for database migrations
    /// </summary>
    public abstract class Migration
    {
        /// <summary>
        /// Migration version/timestamp (e.g., "2026_01_27_120000")
        /// </summary>
        public abstract string Version { get; }

        /// <summary>
        /// Migration description
        /// </summary>
        public virtual string Description => GetType().Name;

        /// <summary>
        /// Run Up/Down and the __migrations record in one transaction (default true).
        /// Return false for statements a transaction does not allow, e.g. PostgreSQL
        /// CREATE INDEX CONCURRENTLY or SQL Server full-text indexes. MySQL/MariaDB commit
        /// DDL implicitly, so there a failed migration can still leave partial changes.
        /// </summary>
        public virtual bool WithinTransaction => true;

        /// <summary>
        /// Run the migration (create tables, add columns, etc.)
        /// </summary>
        public abstract void Up(Schema schema);

        /// <summary>
        /// Reverse the migration (drop tables, remove columns, etc.)
        /// </summary>
        public abstract void Down(Schema schema);
    }
}
