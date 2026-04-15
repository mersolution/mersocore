using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using mersolutionCore.Command.Abstractions;

namespace mersolutionCore.ORM.Migration
{
    /// <summary>
    /// Migration runner - manages database migrations
    /// </summary>
    public class Migrator
    {
        private readonly Func<DbCommandBase> _connectionFactory;
        private readonly string _migrationsTable;
        private readonly List<Migration> _migrations = new List<Migration>();

        /// <summary>
        /// Create migrator with connection factory
        /// </summary>
        /// <param name="connectionFactory">Database connection factory</param>
        /// <param name="migrationsTable">Table name for tracking migrations (default: __migrations)</param>
        public Migrator(Func<DbCommandBase> connectionFactory, string migrationsTable = "__migrations")
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _migrationsTable = migrationsTable;
        }

        /// <summary>
        /// Register a migration
        /// </summary>
        public Migrator Add(Migration migration)
        {
            _migrations.Add(migration);
            return this;
        }

        /// <summary>
        /// Register multiple migrations
        /// </summary>
        public Migrator Add(params Migration[] migrations)
        {
            _migrations.AddRange(migrations);
            return this;
        }

        /// <summary>
        /// Auto-discover migrations from assembly
        /// </summary>
        public Migrator Discover(Assembly assembly)
        {
            var migrationTypes = assembly.GetTypes()
                .Where(t => typeof(Migration).IsAssignableFrom(t) && !t.IsAbstract)
                .ToList();

            foreach (var type in migrationTypes)
            {
                var migration = (Migration)Activator.CreateInstance(type);
                _migrations.Add(migration);
            }

            return this;
        }

        /// <summary>
        /// Run all pending migrations
        /// </summary>
        public MigrationResult Migrate()
        {
            var result = new MigrationResult();

            using (var db = _connectionFactory())
            {
                EnsureMigrationsTable(db);

                var applied = GetAppliedMigrations(db);
                var pending = _migrations
                    .Where(m => !applied.Contains(m.Version))
                    .OrderBy(m => m.Version)
                    .ToList();

                foreach (var migration in pending)
                {
                    try
                    {
                        // Use fresh connection for each migration
                        using (var migrationDb = _connectionFactory())
                        {
                            var schema = new Schema(migrationDb);
                            migration.Up(schema);
                        }

                        // Use fresh connection for recording
                        using (var recordDb = _connectionFactory())
                        {
                            recordDb.ParametersAdd("@version", migration.Version);
                            recordDb.ParametersAdd("@description", migration.Description ?? "");
                            recordDb.RunExecute($"INSERT INTO {_migrationsTable} (Version, Description) VALUES (@version, @description)");
                        }
                        
                        result.Applied.Add(migration.Version);
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"{migration.Version}: {ex.Message}");
                        result.Success = false;
                        break;
                    }
                }

                result.Success = result.Errors.Count == 0;
            }

            return result;
        }

        /// <summary>
        /// Rollback last migration
        /// </summary>
        public MigrationResult Rollback(int steps = 1)
        {
            var result = new MigrationResult();

            using (var db = _connectionFactory())
            {
                EnsureMigrationsTable(db);

                var applied = GetAppliedMigrations(db);
                var toRollback = applied
                    .OrderByDescending(v => v)
                    .Take(steps)
                    .ToList();

                foreach (var version in toRollback)
                {
                    var migration = _migrations.FirstOrDefault(m => m.Version == version);
                    if (migration == null)
                    {
                        result.Errors.Add($"Migration {version} not found in registered migrations");
                        continue;
                    }

                    try
                    {
                        var schema = new Schema(db);
                        migration.Down(schema);

                        RemoveMigration(db, version);
                        result.RolledBack.Add(version);
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"{version}: {ex.Message}");
                        result.Success = false;
                        break;
                    }
                }

                result.Success = result.Errors.Count == 0;
            }

            return result;
        }

        /// <summary>
        /// Rollback all migrations
        /// </summary>
        public MigrationResult Reset()
        {
            var result = new MigrationResult();

            using (var db = _connectionFactory())
            {
                EnsureMigrationsTable(db);

                var applied = GetAppliedMigrations(db);
                var toRollback = applied.OrderByDescending(v => v).ToList();

                foreach (var version in toRollback)
                {
                    var migration = _migrations.FirstOrDefault(m => m.Version == version);
                    if (migration == null) continue;

                    try
                    {
                        var schema = new Schema(db);
                        migration.Down(schema);

                        RemoveMigration(db, version);
                        result.RolledBack.Add(version);
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"{version}: {ex.Message}");
                        result.Success = false;
                        break;
                    }
                }

                result.Success = result.Errors.Count == 0;
            }

            return result;
        }

        /// <summary>
        /// Reset and re-run all migrations
        /// </summary>
        public MigrationResult Refresh()
        {
            var resetResult = Reset();
            if (!resetResult.Success)
                return resetResult;

            return Migrate();
        }

        /// <summary>
        /// Get migration status
        /// </summary>
        public MigrationStatus Status()
        {
            var status = new MigrationStatus();

            using (var db = _connectionFactory())
            {
                EnsureMigrationsTable(db);

                var applied = GetAppliedMigrations(db);

                foreach (var migration in _migrations.OrderBy(m => m.Version))
                {
                    status.Migrations.Add(new MigrationInfo
                    {
                        Version = migration.Version,
                        Description = migration.Description,
                        Applied = applied.Contains(migration.Version)
                    });
                }

                status.PendingCount = status.Migrations.Count(m => !m.Applied);
                status.AppliedCount = status.Migrations.Count(m => m.Applied);
            }

            return status;
        }

        #region Private Methods

        private void EnsureMigrationsTable(DbCommandBase db)
        {
            string sql;
            switch (db.ProviderType)
            {
                case DbProviderType.SqlServer:
                    sql = $@"
                        IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='{_migrationsTable}' AND xtype='U')
                        CREATE TABLE {_migrationsTable} (
                            Id INT IDENTITY(1,1) PRIMARY KEY,
                            Version VARCHAR(100) NOT NULL,
                            Description VARCHAR(255),
                            AppliedAt DATETIME NOT NULL DEFAULT GETDATE()
                        )";
                    break;

                case DbProviderType.MySQL:
                case DbProviderType.MariaDB:
                    sql = $@"
                        CREATE TABLE IF NOT EXISTS {_migrationsTable} (
                            Id INT AUTO_INCREMENT PRIMARY KEY,
                            Version VARCHAR(100) NOT NULL,
                            Description VARCHAR(255),
                            AppliedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
                        )";
                    break;

                case DbProviderType.PostgreSQL:
                    sql = $@"
                        CREATE TABLE IF NOT EXISTS {_migrationsTable} (
                            Id SERIAL PRIMARY KEY,
                            Version VARCHAR(100) NOT NULL,
                            Description VARCHAR(255),
                            AppliedAt TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
                        )";
                    break;

                case DbProviderType.SQLite:
                default:
                    sql = $@"
                        CREATE TABLE IF NOT EXISTS {_migrationsTable} (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            Version TEXT NOT NULL,
                            Description TEXT,
                            AppliedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
                        )";
                    break;
            }

            db.RunExecute(sql);
        }

        private List<string> GetAppliedMigrations(DbCommandBase db)
        {
            var dt = db.RunDataTable($"SELECT Version FROM {_migrationsTable} ORDER BY Version");
            return dt.AsEnumerable().Select(r => r["Version"].ToString()).ToList();
        }

        private void RecordMigration(DbCommandBase db, string version, string description)
        {
            db.ParametersAdd("@version", version);
            db.ParametersAdd("@description", description);
            db.RunExecute($"INSERT INTO {_migrationsTable} (Version, Description) VALUES (@version, @description)");
        }

        private void RemoveMigration(DbCommandBase db, string version)
        {
            db.ParametersAdd("@version", version);
            db.RunExecute($"DELETE FROM {_migrationsTable} WHERE Version = @version");
        }

        #endregion
    }

    /// <summary>
    /// Migration execution result
    /// </summary>
    public class MigrationResult
    {
        public bool Success { get; set; } = true;
        public List<string> Applied { get; } = new List<string>();
        public List<string> RolledBack { get; } = new List<string>();
        public List<string> Errors { get; } = new List<string>();
    }

    /// <summary>
    /// Migration status
    /// </summary>
    public class MigrationStatus
    {
        public List<MigrationInfo> Migrations { get; } = new List<MigrationInfo>();
        public int PendingCount { get; set; }
        public int AppliedCount { get; set; }
    }

    /// <summary>
    /// Single migration info
    /// </summary>
    public class MigrationInfo
    {
        public string Version { get; set; }
        public string Description { get; set; }
        public bool Applied { get; set; }
    }
}
