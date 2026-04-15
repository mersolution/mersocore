using System;
using System.Collections.Generic;

namespace mersolutionCore.ORM.Migrations
{
    /// <summary>
    /// Migration base class
    /// </summary>
    public abstract class Migration
    {
        /// <summary>
        /// Migration adı
        /// </summary>
        public virtual string Name => GetType().Name;

        /// <summary>
        /// Migration versiyonu (timestamp)
        /// </summary>
        public virtual long Version => 0;

        /// <summary>
        /// Migration'ı uygula
        /// </summary>
        public abstract void Up();

        /// <summary>
        /// Migration'ı geri al
        /// </summary>
        public abstract void Down();

        /// <summary>
        /// SQL çalıştır
        /// </summary>
        protected void Execute(string sql)
        {
            using (var db = ModelBase.ConnectionFactory())
            {
                db.RunExecute(sql);
            }
        }

        /// <summary>
        /// Tablo oluştur
        /// </summary>
        protected TableBuilder CreateTable(string tableName)
        {
            return new TableBuilder(tableName, TableOperation.Create);
        }

        /// <summary>
        /// Tablo güncelle
        /// </summary>
        protected TableBuilder AlterTable(string tableName)
        {
            return new TableBuilder(tableName, TableOperation.Alter);
        }

        /// <summary>
        /// Tablo sil
        /// </summary>
        protected void DropTable(string tableName)
        {
            Execute($"DROP TABLE IF EXISTS {tableName}");
        }

        /// <summary>
        /// Tablo var mı kontrol et
        /// </summary>
        protected bool TableExists(string tableName)
        {
            using (var db = ModelBase.ConnectionFactory())
            {
                var sql = $"SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = '{tableName}'";
                return db.RunToInt32Scaler(sql) > 0;
            }
        }
    }

    public enum TableOperation
    {
        Create,
        Alter
    }

    /// <summary>
    /// Tablo oluşturucu
    /// </summary>
    public class TableBuilder
    {
        private readonly string _tableName;
        private readonly TableOperation _operation;
        private readonly List<string> _columns = new List<string>();
        private readonly List<string> _alterStatements = new List<string>();

        public TableBuilder(string tableName, TableOperation operation)
        {
            _tableName = tableName;
            _operation = operation;
        }

        public TableBuilder Id(string name = "Id")
        {
            _columns.Add($"{name} INT IDENTITY(1,1) PRIMARY KEY");
            return this;
        }

        public TableBuilder String(string name, int length = 255, bool nullable = true)
        {
            var nullStr = nullable ? "NULL" : "NOT NULL";
            if (_operation == TableOperation.Create)
                _columns.Add($"{name} NVARCHAR({length}) {nullStr}");
            else
                _alterStatements.Add($"ALTER TABLE {_tableName} ADD {name} NVARCHAR({length}) {nullStr}");
            return this;
        }

        public TableBuilder Int(string name, bool nullable = true)
        {
            var nullStr = nullable ? "NULL" : "NOT NULL";
            if (_operation == TableOperation.Create)
                _columns.Add($"{name} INT {nullStr}");
            else
                _alterStatements.Add($"ALTER TABLE {_tableName} ADD {name} INT {nullStr}");
            return this;
        }

        public TableBuilder BigInt(string name, bool nullable = true)
        {
            var nullStr = nullable ? "NULL" : "NOT NULL";
            if (_operation == TableOperation.Create)
                _columns.Add($"{name} BIGINT {nullStr}");
            else
                _alterStatements.Add($"ALTER TABLE {_tableName} ADD {name} BIGINT {nullStr}");
            return this;
        }

        public TableBuilder Decimal(string name, int precision = 18, int scale = 2, bool nullable = true)
        {
            var nullStr = nullable ? "NULL" : "NOT NULL";
            if (_operation == TableOperation.Create)
                _columns.Add($"{name} DECIMAL({precision},{scale}) {nullStr}");
            else
                _alterStatements.Add($"ALTER TABLE {_tableName} ADD {name} DECIMAL({precision},{scale}) {nullStr}");
            return this;
        }

        public TableBuilder Bool(string name, bool defaultValue = false)
        {
            var defaultStr = defaultValue ? "1" : "0";
            if (_operation == TableOperation.Create)
                _columns.Add($"{name} BIT NOT NULL DEFAULT {defaultStr}");
            else
                _alterStatements.Add($"ALTER TABLE {_tableName} ADD {name} BIT NOT NULL DEFAULT {defaultStr}");
            return this;
        }

        public TableBuilder DateTime(string name, bool nullable = true)
        {
            var nullStr = nullable ? "NULL" : "NOT NULL";
            if (_operation == TableOperation.Create)
                _columns.Add($"{name} DATETIME2 {nullStr}");
            else
                _alterStatements.Add($"ALTER TABLE {_tableName} ADD {name} DATETIME2 {nullStr}");
            return this;
        }

        public TableBuilder Text(string name, bool nullable = true)
        {
            var nullStr = nullable ? "NULL" : "NOT NULL";
            if (_operation == TableOperation.Create)
                _columns.Add($"{name} NVARCHAR(MAX) {nullStr}");
            else
                _alterStatements.Add($"ALTER TABLE {_tableName} ADD {name} NVARCHAR(MAX) {nullStr}");
            return this;
        }

        public TableBuilder Timestamps()
        {
            _columns.Add("CreatedAt DATETIME2 NULL");
            _columns.Add("UpdatedAt DATETIME2 NULL");
            return this;
        }

        public TableBuilder SoftDeletes()
        {
            _columns.Add("DeletedAt DATETIME2 NULL");
            return this;
        }

        public TableBuilder ForeignKey(string column, string referencesTable, string referencesColumn = "Id")
        {
            _columns.Add($"CONSTRAINT FK_{_tableName}_{column} FOREIGN KEY ({column}) REFERENCES {referencesTable}({referencesColumn})");
            return this;
        }

        public TableBuilder Index(string column)
        {
            _alterStatements.Add($"CREATE INDEX IX_{_tableName}_{column} ON {_tableName}({column})");
            return this;
        }

        public TableBuilder DropColumn(string name)
        {
            _alterStatements.Add($"ALTER TABLE {_tableName} DROP COLUMN {name}");
            return this;
        }

        public void Build()
        {
            using (var db = ModelBase.ConnectionFactory())
            {
                if (_operation == TableOperation.Create)
                {
                    var sql = $"CREATE TABLE {_tableName} ({string.Join(", ", _columns)})";
                    db.RunExecute(sql);
                }

                foreach (var stmt in _alterStatements)
                {
                    db.RunExecute(stmt);
                }
            }
        }
    }
}
