using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using mersolutionCore.Command.Abstractions;

namespace mersolutionCore.Cli
{
    internal sealed class TableInfo
    {
        public string Schema = string.Empty;
        public string Name = string.Empty;
        public List<ColumnInfo> Columns = new List<ColumnInfo>();
        public List<ForeignKeyInfo> ForeignKeys = new List<ForeignKeyInfo>();
        public string Key => (Schema + "." + Name).ToLowerInvariant();
    }

    internal sealed class ColumnInfo
    {
        public string Name = string.Empty;
        public string DataType = string.Empty;      // information_schema data type (lower case)
        public string FullType = string.Empty;      // tinyint(1), varchar(50), DECIMAL(18,2) ...
        public bool Nullable;
        public int? Length;
        public int Ordinal;
        public int KeyOrder;                        // > 0: position in the primary key
        public bool Identity;
    }

    internal sealed class ForeignKeyInfo
    {
        public string Name = string.Empty;
        public List<string> Columns = new List<string>();
        public string RefSchema = string.Empty;
        public string RefTable = string.Empty;
        public List<string> RefColumns = new List<string>();
        public string RefKey => (RefSchema + "." + RefTable).ToLowerInvariant();
    }

    /// <summary>
    /// Reads tables, columns, primary keys and foreign keys from the catalog of each database
    /// </summary>
    internal static class SchemaReader
    {
        public static List<TableInfo> Read(Func<DbCommandBase> connect, DbProviderType provider)
        {
            return provider == DbProviderType.SQLite ? ReadSqlite(connect) : ReadCatalog(connect, provider);
        }

        private static List<TableInfo> ReadCatalog(Func<DbCommandBase> connect, DbProviderType provider)
        {
            var q = Queries(provider);
            var tables = new Dictionary<string, TableInfo>();

            foreach (DataRow row in Rows(connect, q.Tables))
            {
                var t = new TableInfo { Schema = Text(row, 0), Name = Text(row, 1) };
                tables[t.Key] = t;
            }

            foreach (DataRow row in Rows(connect, q.Columns))
            {
                if (!tables.TryGetValue((Text(row, 0) + "." + Text(row, 1)).ToLowerInvariant(), out var t))
                    continue;

                t.Columns.Add(new ColumnInfo
                {
                    Name = Text(row, 2),
                    DataType = Text(row, 3).ToLowerInvariant(),
                    Nullable = string.Equals(Text(row, 4), "YES", StringComparison.OrdinalIgnoreCase),
                    Length = Int(row, 5),
                    Ordinal = Int(row, 6) ?? 0,
                    Identity = (Int(row, 7) ?? 0) == 1,
                    FullType = row.Table.Columns.Count > 8 ? Text(row, 8).ToLowerInvariant() : Text(row, 3).ToLowerInvariant()
                });
            }

            foreach (DataRow row in Rows(connect, q.PrimaryKeys))
            {
                if (!tables.TryGetValue((Text(row, 0) + "." + Text(row, 1)).ToLowerInvariant(), out var t))
                    continue;
                var column = t.Columns.FirstOrDefault(c => c.Name == Text(row, 2));
                if (column != null)
                    column.KeyOrder = Int(row, 3) ?? 1;
            }

            // constraint, schema, table, column, ref schema, ref table, ref column, ordinal
            var fkRows = Rows(connect, q.ForeignKeys).Cast<DataRow>()
                .Select(r => new
                {
                    Constraint = Text(r, 0), Key = (Text(r, 1) + "." + Text(r, 2)).ToLowerInvariant(), Column = Text(r, 3),
                    RefSchema = Text(r, 4), RefTable = Text(r, 5), RefColumn = Text(r, 6), Ordinal = Int(r, 7) ?? 0
                })
                .OrderBy(r => r.Key).ThenBy(r => r.Constraint).ThenBy(r => r.Ordinal);

            foreach (var group in fkRows.GroupBy(r => new { r.Key, r.Constraint }))
            {
                if (!tables.TryGetValue(group.Key.Key, out var t))
                    continue;
                var first = group.First();
                t.ForeignKeys.Add(new ForeignKeyInfo
                {
                    Name = group.Key.Constraint,
                    Columns = group.Select(r => r.Column).ToList(),
                    RefSchema = first.RefSchema,
                    RefTable = first.RefTable,
                    RefColumns = group.Select(r => r.RefColumn).ToList()
                });
            }

            foreach (var t in tables.Values)
                t.Columns.Sort((a, b) => a.Ordinal.CompareTo(b.Ordinal));
            return tables.Values.OrderBy(t => t.Schema).ThenBy(t => t.Name).ToList();
        }

        private static List<TableInfo> ReadSqlite(Func<DbCommandBase> connect)
        {
            var result = new List<TableInfo>();
            foreach (DataRow tableRow in Rows(connect, "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name"))
            {
                var t = new TableInfo { Name = Text(tableRow, 0) };

                using (var db = connect())
                {
                    db.ParametersAdd("@t", t.Name);
                    foreach (DataRow row in db.RunDataTable("SELECT name, type, \"notnull\", pk, cid FROM pragma_table_info(@t)").Rows)
                    {
                        var declared = Text(row, 1);
                        var paren = declared.IndexOf('(');
                        t.Columns.Add(new ColumnInfo
                        {
                            Name = Text(row, 0),
                            DataType = (paren > 0 ? declared.Substring(0, paren) : declared).Trim().ToLowerInvariant(),
                            FullType = declared.ToLowerInvariant(),
                            Nullable = (Int(row, 2) ?? 0) == 0,
                            Length = paren > 0 ? FirstNumber(declared.Substring(paren)) : null,
                            KeyOrder = Int(row, 3) ?? 0,
                            Ordinal = Int(row, 4) ?? 0
                        });
                    }
                }

                // INTEGER PRIMARY KEY is the rowid: generated by SQLite
                var keys = t.Columns.Where(c => c.KeyOrder > 0).ToList();
                if (keys.Count == 1 && keys[0].DataType == "integer")
                    keys[0].Identity = true;
                foreach (var key in keys)
                    key.Nullable = false;

                using (var db = connect())
                {
                    db.ParametersAdd("@t", t.Name);
                    var fks = db.RunDataTable("SELECT id, seq, \"table\", \"from\", \"to\" FROM pragma_foreign_key_list(@t) ORDER BY id, seq").Rows.Cast<DataRow>();
                    foreach (var group in fks.GroupBy(r => Int(r, 0)))
                    {
                        var first = group.First();
                        var refTable = Text(first, 2);
                        t.ForeignKeys.Add(new ForeignKeyInfo
                        {
                            Name = "fk_" + t.Name + "_" + group.Key,
                            Columns = group.Select(r => Text(r, 3)).ToList(),
                            RefTable = refTable,
                            // "to" is empty when the foreign key points to the primary key
                            RefColumns = group.Select(r => Text(r, 4)).ToList()
                        });
                    }
                }

                result.Add(t);
            }

            // Fill the referenced primary key where SQLite left "to" empty
            foreach (var fk in result.SelectMany(t => t.ForeignKeys))
            {
                if (fk.RefColumns.Any(string.IsNullOrEmpty))
                {
                    var owner = result.FirstOrDefault(t => t.Name.Equals(fk.RefTable, StringComparison.OrdinalIgnoreCase));
                    if (owner != null)
                        fk.RefColumns = owner.Columns.Where(c => c.KeyOrder > 0).OrderBy(c => c.KeyOrder).Select(c => c.Name).ToList();
                }
            }
            return result;
        }

        private sealed class CatalogQueries
        {
            public string Tables = string.Empty, Columns = string.Empty, PrimaryKeys = string.Empty, ForeignKeys = string.Empty;
        }

        // Every query returns its columns in a fixed order (read by position)
        private static CatalogQueries Queries(DbProviderType provider)
        {
            switch (provider)
            {
                case DbProviderType.SqlServer:
                    return new CatalogQueries
                    {
                        Tables = "SELECT TABLE_SCHEMA, TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'",
                        Columns = "SELECT TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME, DATA_TYPE, IS_NULLABLE, CHARACTER_MAXIMUM_LENGTH, ORDINAL_POSITION, " +
                                  "COLUMNPROPERTY(OBJECT_ID(QUOTENAME(TABLE_SCHEMA) + '.' + QUOTENAME(TABLE_NAME)), COLUMN_NAME, 'IsIdentity') " +
                                  "FROM INFORMATION_SCHEMA.COLUMNS",
                        PrimaryKeys = "SELECT k.TABLE_SCHEMA, k.TABLE_NAME, k.COLUMN_NAME, k.ORDINAL_POSITION FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS t " +
                                      "JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE k ON k.CONSTRAINT_NAME = t.CONSTRAINT_NAME AND k.TABLE_SCHEMA = t.TABLE_SCHEMA AND k.TABLE_NAME = t.TABLE_NAME " +
                                      "WHERE t.CONSTRAINT_TYPE = 'PRIMARY KEY'",
                        ForeignKeys = "SELECT fk.name, SCHEMA_NAME(tp.schema_id), tp.name, cp.name, SCHEMA_NAME(tr.schema_id), tr.name, cr.name, fkc.constraint_column_id " +
                                      "FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id " +
                                      "JOIN sys.tables tp ON tp.object_id = fkc.parent_object_id JOIN sys.columns cp ON cp.object_id = fkc.parent_object_id AND cp.column_id = fkc.parent_column_id " +
                                      "JOIN sys.tables tr ON tr.object_id = fkc.referenced_object_id JOIN sys.columns cr ON cr.object_id = fkc.referenced_object_id AND cr.column_id = fkc.referenced_column_id"
                    };

                case DbProviderType.MySQL:
                case DbProviderType.MariaDB:
                    return new CatalogQueries
                    {
                        Tables = "SELECT TABLE_SCHEMA, TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'BASE TABLE'",
                        Columns = "SELECT TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME, DATA_TYPE, IS_NULLABLE, CHARACTER_MAXIMUM_LENGTH, ORDINAL_POSITION, " +
                                  "CASE WHEN EXTRA LIKE '%auto_increment%' THEN 1 ELSE 0 END, COLUMN_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE()",
                        PrimaryKeys = "SELECT TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME, ORDINAL_POSITION FROM information_schema.KEY_COLUMN_USAGE " +
                                      "WHERE TABLE_SCHEMA = DATABASE() AND CONSTRAINT_NAME = 'PRIMARY'",
                        ForeignKeys = "SELECT CONSTRAINT_NAME, TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME, REFERENCED_TABLE_SCHEMA, REFERENCED_TABLE_NAME, REFERENCED_COLUMN_NAME, ORDINAL_POSITION " +
                                      "FROM information_schema.KEY_COLUMN_USAGE WHERE TABLE_SCHEMA = DATABASE() AND REFERENCED_TABLE_NAME IS NOT NULL"
                    };

                case DbProviderType.PostgreSQL:
                    return new CatalogQueries
                    {
                        Tables = "SELECT table_schema, table_name FROM information_schema.tables WHERE table_type = 'BASE TABLE' AND table_schema NOT IN ('pg_catalog', 'information_schema')",
                        Columns = "SELECT table_schema, table_name, column_name, data_type, is_nullable, character_maximum_length, ordinal_position, " +
                                  "CASE WHEN is_identity = 'YES' OR column_default LIKE 'nextval(%' THEN 1 ELSE 0 END, udt_name " +
                                  "FROM information_schema.columns WHERE table_schema NOT IN ('pg_catalog', 'information_schema')",
                        PrimaryKeys = "SELECT kcu.table_schema, kcu.table_name, kcu.column_name, kcu.ordinal_position FROM information_schema.table_constraints tc " +
                                      "JOIN information_schema.key_column_usage kcu ON kcu.constraint_name = tc.constraint_name AND kcu.table_schema = tc.table_schema AND kcu.table_name = tc.table_name " +
                                      "WHERE tc.constraint_type = 'PRIMARY KEY'",
                        ForeignKeys = "SELECT con.conname, ns.nspname, cl.relname, att.attname, rns.nspname, rcl.relname, ratt.attname, k.ord " +
                                      "FROM pg_constraint con JOIN pg_class cl ON cl.oid = con.conrelid JOIN pg_namespace ns ON ns.oid = cl.relnamespace " +
                                      "JOIN pg_class rcl ON rcl.oid = con.confrelid JOIN pg_namespace rns ON rns.oid = rcl.relnamespace " +
                                      "CROSS JOIN LATERAL unnest(con.conkey, con.confkey) WITH ORDINALITY AS k(attnum, refnum, ord) " +
                                      "JOIN pg_attribute att ON att.attrelid = con.conrelid AND att.attnum = k.attnum " +
                                      "JOIN pg_attribute ratt ON ratt.attrelid = con.confrelid AND ratt.attnum = k.refnum " +
                                      "WHERE con.contype = 'f'"
                    };

                default:
                    throw new NotSupportedException($"Provider {provider} is not supported.");
            }
        }

        private static DataRowCollection Rows(Func<DbCommandBase> connect, string sql)
        {
            using (var db = connect())
                return db.RunDataTable(sql).Rows;
        }

        private static string Text(DataRow row, int index)
        {
            var value = row[index];
            return value == DBNull.Value ? string.Empty : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static int? Int(DataRow row, int index)
        {
            var value = row[index];
            if (value == DBNull.Value)
                return null;
            try
            {
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch (OverflowException)
            {
                return int.MaxValue;
            }
        }

        private static int? FirstNumber(string text)
        {
            var digits = new string(text.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray());
            return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : (int?)null;
        }
    }
}
