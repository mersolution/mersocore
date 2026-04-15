using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace mersolutionCore.ORM.Migrations
{
    /// <summary>
    /// Migration Runner - Migration'ları çalıştır
    /// </summary>
    public class MigrationRunner
    {
        private readonly string _migrationsTable = "__MersoMigrations";

        /// <summary>
        /// Migration tablosunu oluştur
        /// </summary>
        public void EnsureMigrationsTable()
        {
            using (var db = ModelBase.ConnectionFactory())
            {
                var sql = $@"
                    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = '{_migrationsTable}')
                    BEGIN
                        CREATE TABLE {_migrationsTable} (
                            Id INT IDENTITY(1,1) PRIMARY KEY,
                            Migration NVARCHAR(255) NOT NULL,
                            Batch INT NOT NULL,
                            ExecutedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE()
                        )
                    END";
                db.RunExecute(sql);
            }
        }

        /// <summary>
        /// Tüm migration'ları çalıştır
        /// </summary>
        public void Migrate(Assembly assembly = null)
        {
            EnsureMigrationsTable();

            var migrations = GetPendingMigrations(assembly);
            if (migrations.Count == 0)
            {
                Console.WriteLine("Çalıştırılacak migration yok.");
                return;
            }

            var batch = GetLastBatch() + 1;

            foreach (var migration in migrations)
            {
                Console.WriteLine($"Migrating: {migration.Name}");
                
                try
                {
                    migration.Up();
                    RecordMigration(migration.Name, batch);
                    Console.WriteLine($"Migrated: {migration.Name}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Migration hatası ({migration.Name}): {ex.Message}");
                    throw;
                }
            }
        }

        /// <summary>
        /// Son batch'i geri al
        /// </summary>
        public void Rollback(Assembly assembly = null)
        {
            EnsureMigrationsTable();

            var lastBatch = GetLastBatch();
            if (lastBatch == 0)
            {
                Console.WriteLine("Geri alınacak migration yok.");
                return;
            }

            var migrations = GetMigrationsInBatch(lastBatch, assembly);

            foreach (var migration in migrations.OrderByDescending(m => m.Name))
            {
                Console.WriteLine($"Rolling back: {migration.Name}");
                
                try
                {
                    migration.Down();
                    RemoveMigration(migration.Name);
                    Console.WriteLine($"Rolled back: {migration.Name}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Rollback hatası ({migration.Name}): {ex.Message}");
                    throw;
                }
            }
        }

        /// <summary>
        /// Tüm migration'ları geri al
        /// </summary>
        public void Reset(Assembly assembly = null)
        {
            EnsureMigrationsTable();

            while (GetLastBatch() > 0)
            {
                Rollback(assembly);
            }
        }

        /// <summary>
        /// Reset + Migrate
        /// </summary>
        public void Refresh(Assembly assembly = null)
        {
            Reset(assembly);
            Migrate(assembly);
        }

        /// <summary>
        /// Migration durumunu göster
        /// </summary>
        public void Status(Assembly assembly = null)
        {
            EnsureMigrationsTable();

            var ran = GetRanMigrations();
            var all = GetAllMigrations(assembly);

            Console.WriteLine("\nMigration Durumu:");
            Console.WriteLine("─────────────────────────────────────");

            foreach (var migration in all)
            {
                var status = ran.Contains(migration.Name) ? "✓ Ran" : "✗ Pending";
                Console.WriteLine($"  {status} - {migration.Name}");
            }

            Console.WriteLine("─────────────────────────────────────\n");
        }

        private List<Migration> GetPendingMigrations(Assembly assembly)
        {
            var ran = GetRanMigrations();
            return GetAllMigrations(assembly)
                .Where(m => !ran.Contains(m.Name))
                .OrderBy(m => m.Name)
                .ToList();
        }

        private List<Migration> GetAllMigrations(Assembly assembly)
        {
            assembly = assembly ?? Assembly.GetCallingAssembly();

            return assembly.GetTypes()
                .Where(t => t.IsSubclassOf(typeof(Migration)) && !t.IsAbstract)
                .Select(t => (Migration)Activator.CreateInstance(t))
                .OrderBy(m => m.Name)
                .ToList();
        }

        private List<Migration> GetMigrationsInBatch(int batch, Assembly assembly)
        {
            var names = new List<string>();
            using (var db = ModelBase.ConnectionFactory())
            {
                db.ParametersAdd("@batch", batch);
                var dt = db.RunDataTable($"SELECT Migration FROM {_migrationsTable} WHERE Batch = @batch");
                foreach (System.Data.DataRow row in dt.Rows)
                {
                    names.Add(row["Migration"].ToString());
                }
            }

            return GetAllMigrations(assembly)
                .Where(m => names.Contains(m.Name))
                .ToList();
        }

        private HashSet<string> GetRanMigrations()
        {
            var set = new HashSet<string>();
            using (var db = ModelBase.ConnectionFactory())
            {
                var dt = db.RunDataTable($"SELECT Migration FROM {_migrationsTable}");
                foreach (System.Data.DataRow row in dt.Rows)
                {
                    set.Add(row["Migration"].ToString());
                }
            }
            return set;
        }

        private int GetLastBatch()
        {
            using (var db = ModelBase.ConnectionFactory())
            {
                var result = db.RunToObjectScaler($"SELECT ISNULL(MAX(Batch), 0) FROM {_migrationsTable}");
                return Convert.ToInt32(result);
            }
        }

        private void RecordMigration(string name, int batch)
        {
            using (var db = ModelBase.ConnectionFactory())
            {
                db.ParametersAdd("@name", name);
                db.ParametersAdd("@batch", batch);
                db.RunExecute($"INSERT INTO {_migrationsTable} (Migration, Batch) VALUES (@name, @batch)");
            }
        }

        private void RemoveMigration(string name)
        {
            using (var db = ModelBase.ConnectionFactory())
            {
                db.ParametersAdd("@name", name);
                db.RunExecute($"DELETE FROM {_migrationsTable} WHERE Migration = @name");
            }
        }
    }
}
