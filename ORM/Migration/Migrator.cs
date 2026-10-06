using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using mersolutionCore.Command.Abstractions;

namespace mersolutionCore.ORM.Migration
{
    /// <summary>
    /// Migration runner - manages database migrations
    /// </summary>
    public class Migrator
    {
        private static readonly Regex TableNamePattern = new Regex(@"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)?$");

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

            if (string.IsNullOrEmpty(migrationsTable) || !TableNamePattern.IsMatch(migrationsTable))
                throw new ArgumentException("Migrations table must be a plain identifier (letters, digits, '_', optional schema prefix).", nameof(migrationsTable));

            _migrationsTable = migrationsTable;
        }

        /// <summary>
        /// Register a migration
        /// </summary>
        public Migrator Add(Migration migration)
        {
            Register(migration);
            return this;
        }

        /// <summary>
        /// Register multiple migrations
        /// </summary>
        public Migrator Add(params Migration[] migrations)
        {
            foreach (var migration in migrations)
                Register(migration);
            return this;
        }

        /// <summary>
        /// Auto-discover migrations from assembly
        /// </summary>
        public Migrator Discover(Assembly assembly)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).Select(t => t!).ToArray();
            }

            var migrationTypes = types
                .Where(t => typeof(Migration).IsAssignableFrom(t) && !t.IsAbstract && !t.ContainsGenericParameters
                            && t.GetConstructor(Type.EmptyTypes) != null)
                .ToList();

            foreach (var type in migrationTypes)
            {
                Register((Migration)Activator.CreateInstance(type)!);
            }

            return this;
        }

        /// <summary>
        /// Run all pending migrations. They form one batch: <see cref="Rollback()"/> undoes the last batch.
        /// </summary>
        public MigrationResult Migrate()
        {
            var result = new MigrationResult();

            using (var db = _connectionFactory())
            {
                EnsureMigrationsTable(db);

                var appliedRows = GetAppliedMigrations(db);
                var applied = new HashSet<string>(appliedRows.Select(a => a.Version), StringComparer.Ordinal);
                var batch = appliedRows.Count == 0 ? 1 : appliedRows.Max(a => a.Batch) + 1;
                var pending = _migrations
                    .Where(m => !applied.Contains(m.Version))
                    .OrderBy(m => m.Version, StringComparer.Ordinal)
                    .ToList();

                foreach (var migration in pending)
                {
                    try
                    {
                        RunStep(db, migration, () =>
                        {
                            migration.Up(new Schema(db));
                            RecordMigration(db, migration.Version, migration.Description ?? "", batch);
                        });

                        result.Applied.Add(migration.Version);
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"{migration.Version}: {ex.Message}");
                        break;
                    }
                }

                result.Success = result.Errors.Count == 0;
            }

            return result;
        }

        /// <summary>
        /// Roll back the last batch (every migration the last Migrate() call applied)
        /// </summary>
        public MigrationResult Rollback()
        {
            using (var db = _connectionFactory())
            {
                EnsureMigrationsTable(db);

                var applied = GetAppliedMigrations(db);
                if (applied.Count == 0)
                    return new MigrationResult();

                var lastBatch = applied.Max(a => a.Batch);
                var toRollback = Newest(applied.Where(a => a.Batch == lastBatch)).ToList();

                return RollbackVersions(db, toRollback, skipUnknown: false);
            }
        }

        /// <summary>
        /// Roll back the last <paramref name="steps"/> migrations, newest first (across batches)
        /// </summary>
        public MigrationResult Rollback(int steps)
        {
            using (var db = _connectionFactory())
            {
                EnsureMigrationsTable(db);

                var toRollback = Newest(GetAppliedMigrations(db))
                    .Take(Math.Max(0, steps))
                    .ToList();

                return RollbackVersions(db, toRollback, skipUnknown: false);
            }
        }

        /// <summary>
        /// Rollback all migrations
        /// </summary>
        public MigrationResult Reset()
        {
            using (var db = _connectionFactory())
            {
                EnsureMigrationsTable(db);

                var toRollback = Newest(GetAppliedMigrations(db)).ToList();

                return RollbackVersions(db, toRollback, skipUnknown: true);
            }
        }

        // Newest batch first, inside a batch the highest version first
        private static IEnumerable<string> Newest(IEnumerable<AppliedMigration> applied)
        {
            return applied
                .OrderByDescending(a => a.Batch)
                .ThenByDescending(a => a.Version, StringComparer.Ordinal)
                .Select(a => a.Version);
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

                var applied = GetAppliedMigrations(db)
                    .GroupBy(a => a.Version, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.First().Batch, StringComparer.Ordinal);

                foreach (var migration in _migrations.OrderBy(m => m.Version, StringComparer.Ordinal))
                {
                    var isApplied = applied.TryGetValue(migration.Version, out var batch);
                    status.Migrations.Add(new MigrationInfo
                    {
                        Version = migration.Version,
                        Description = migration.Description,
                        Applied = isApplied,
                        Batch = isApplied ? batch : (int?)null
                    });
                }

                status.PendingCount = status.Migrations.Count(m => !m.Applied);
                status.AppliedCount = status.Migrations.Count(m => m.Applied);
            }

            return status;
        }

        #region Private Methods

        private void Register(Migration migration)
        {
            if (migration == null) throw new ArgumentNullException(nameof(migration));
            if (string.IsNullOrWhiteSpace(migration.Version))
                throw new ArgumentException($"{migration.GetType().Name} has no Version.", nameof(migration));

            var existing = _migrations.FirstOrDefault(m => string.Equals(m.Version, migration.Version, StringComparison.Ordinal));
            if (existing != null)
            {
                // Add() + Discover() of the same class: keep one
                if (existing.GetType() == migration.GetType())
                    return;

                throw new InvalidOperationException(
                    $"Duplicate migration version '{migration.Version}': {existing.GetType().Name} and {migration.GetType().Name}.");
            }

            _migrations.Add(migration);
        }

        private MigrationResult RollbackVersions(DbCommandBase db, List<string> versions, bool skipUnknown)
        {
            var result = new MigrationResult();

            foreach (var version in versions)
            {
                var migration = _migrations.FirstOrDefault(m => string.Equals(m.Version, version, StringComparison.Ordinal));
                if (migration == null)
                {
                    if (!skipUnknown)
                        result.Errors.Add($"Migration {version} not found in registered migrations");
                    continue;
                }

                try
                {
                    RunStep(db, migration, () =>
                    {
                        migration.Down(new Schema(db));
                        RemoveMigration(db, version);
                    });

                    result.RolledBack.Add(version);
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{version}: {ex.Message}");
                    break;
                }
            }

            result.Success = result.Errors.Count == 0;
            return result;
        }

        // Schema changes and the __migrations row succeed or fail together
        private static void RunStep(DbCommandBase db, Migration migration, Action step)
        {
            if (!migration.WithinTransaction)
            {
                step();
                return;
            }

            db.BeginTransaction();
            try
            {
                step();
                db.CommitTransaction();
            }
            catch
            {
                try { db.RollbackTransaction(); } catch { }
                throw;
            }
        }

        private void EnsureMigrationsTable(DbCommandBase db)
        {
            string sql;
            switch (db.ProviderType)
            {
                case DbProviderType.SqlServer:
                    sql = $@"
                        IF OBJECT_ID(N'{_migrationsTable}', N'U') IS NULL
                        CREATE TABLE {_migrationsTable} (
                            Id INT IDENTITY(1,1) PRIMARY KEY,
                            Version VARCHAR(100) NOT NULL,
                            Description VARCHAR(255),
                            Batch INT NOT NULL DEFAULT 1,
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
                            Batch INT NOT NULL DEFAULT 1,
                            AppliedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
                        )";
                    break;

                case DbProviderType.PostgreSQL:
                    sql = $@"
                        CREATE TABLE IF NOT EXISTS {_migrationsTable} (
                            Id SERIAL PRIMARY KEY,
                            Version VARCHAR(100) NOT NULL,
                            Description VARCHAR(255),
                            Batch INT NOT NULL DEFAULT 1,
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
                            Batch INTEGER NOT NULL DEFAULT 1,
                            AppliedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
                        )";
                    break;
            }

            db.RunExecute(sql);

            // Tables from 1.x have no Batch column: every earlier migration counts as batch 1
            if (!new Schema(db).HasColumn(_migrationsTable, "Batch"))
                db.RunExecute($"ALTER TABLE {_migrationsTable} ADD Batch INT NOT NULL DEFAULT 1");
        }

        private List<AppliedMigration> GetAppliedMigrations(DbCommandBase db)
        {
            var dt = db.RunDataTable($"SELECT Version, Batch FROM {_migrationsTable}");
            return dt.AsEnumerable()
                .Select(r => new AppliedMigration(
                    Convert.ToString(r[0], CultureInfo.InvariantCulture) ?? string.Empty,
                    r[1] == DBNull.Value ? 1 : Convert.ToInt32(r[1], CultureInfo.InvariantCulture)))
                .ToList();
        }

        private void RecordMigration(DbCommandBase db, string version, string description, int batch)
        {
            db.ParametersAdd("@version", version);
            db.ParametersAdd("@description", description);
            db.ParametersAdd("@batch", batch);
            db.RunExecute($"INSERT INTO {_migrationsTable} (Version, Description, Batch) VALUES (@version, @description, @batch)");
        }

        private sealed class AppliedMigration
        {
            public AppliedMigration(string version, int batch)
            {
                Version = version;
                Batch = batch;
            }

            public string Version { get; }
            public int Batch { get; }
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
        public string? Version { get; set; }
        public string? Description { get; set; }
        public bool Applied { get; set; }

        /// <summary>
        /// Batch number of an applied migration (null when pending)
        /// </summary>
        public int? Batch { get; set; }
    }
}
