using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using mersolutionCore.Command.Abstractions;

namespace mersolutionCore.ORM
{
    /// <summary>
    /// Bulk Operations - Toplu veritabanı işlemleri
    /// </summary>
    public static class BulkOperations
    {
        // SQL Server allows 2100 parameters per command; stay below for every provider
        private const int MaxParametersPerCommand = 2000;

        // SQL Server allows at most 1000 rows in one VALUES list
        private const int MaxRowsPerInsert = 1000;

        #region Insert

        /// <summary>
        /// Toplu kayıt ekleme (batch boyutu parametre limitine göre otomatik küçültülür)
        /// </summary>
        public static int BulkInsert<T>(IEnumerable<T> models, int batchSize = 100) where T : Model<T>, new()
        {
            return DbRun.Sync(BulkInsertCore(models, batchSize, false, default));
        }

        /// <summary>
        /// Toplu kayıt ekleme (async)
        /// </summary>
        public static Task<int> BulkInsertAsync<T>(IEnumerable<T> models, int batchSize = 100, CancellationToken cancellationToken = default) where T : Model<T>, new()
        {
            return BulkInsertCore(models, batchSize, true, cancellationToken);
        }

        private static async Task<int> BulkInsertCore<T>(IEnumerable<T> models, int batchSize, bool useAsync, CancellationToken ct) where T : Model<T>, new()
        {
            var modelList = models?.ToList() ?? new List<T>();
            if (modelList.Count == 0) return 0;

            var metadata = Model<T>.GetMetadata<T>();
            var insertProps = InsertProperties(metadata);
            if (insertProps.Count == 0) return 0;

            var effectiveBatch = EffectiveBatch(batchSize, insertProps.Count);
            int totalInserted = 0;

            // All batches commit together (joins an outer MersoTransaction)
            await MersoTransaction.RunCore(metadata.ConnectionName, async () =>
            {
                for (int i = 0; i < modelList.Count; i += effectiveBatch)
                {
                    var batch = modelList.Skip(i).Take(effectiveBatch).ToList();
                    PrepareForInsert(batch, metadata);

                    using (var db = ModelBase.CreateCommand(metadata.ConnectionName))
                    {
                        var p = db.ProviderType;
                        var sql = new StringBuilder();
                        sql.Append($"INSERT INTO {SqlDialect.Table(p, metadata.TableName)} ({ColumnList(p, insertProps)}) VALUES ");
                        sql.Append(ValuesList(db, batch, insertProps));
                        totalInserted += await DbRun.NonQuery(db, sql.ToString(), useAsync, ct).ConfigureAwait(false);
                    }
                }
                return totalInserted;
            }).ConfigureAwait(false);

            foreach (var model in modelList)
                model.SyncOriginal();

            return totalInserted;
        }

        /// <summary>
        /// Fastest insert of many rows with the provider's bulk API: SQL Server SqlBulkCopy, PostgreSQL COPY,
        /// MySQL / MariaDB MySqlBulkCopy (needs <c>AllowLoadLocalInfile=true</c> and local_infile on the server).
        /// Falls back to batched INSERTs (SQLite, or when the bulk API is not available). Timestamps, Guid keys,
        /// row versions and converters are applied; generated ids are not read back. Joins a MersoTransaction.
        /// </summary>
        public static int BulkCopy<T>(IEnumerable<T> models) where T : Model<T>, new()
        {
            return DbRun.Sync(BulkCopyCore(models, false, default));
        }

        /// <summary>
        /// Fastest insert of many rows with the provider's bulk API (async)
        /// </summary>
        public static Task<int> BulkCopyAsync<T>(IEnumerable<T> models, CancellationToken cancellationToken = default) where T : Model<T>, new()
        {
            return BulkCopyCore(models, true, cancellationToken);
        }

        private static async Task<int> BulkCopyCore<T>(IEnumerable<T> models, bool useAsync, CancellationToken ct) where T : Model<T>, new()
        {
            var modelList = models?.ToList() ?? new List<T>();
            if (modelList.Count == 0) return 0;

            var metadata = Model<T>.GetMetadata<T>();
            var insertProps = InsertProperties(metadata);
            if (insertProps.Count == 0) return 0;

            PrepareForInsert(modelList, metadata);
            var columns = insertProps.Select(p => p.ColumnName).ToList();
            var rows = modelList.Select(m => insertProps.Select(p => p.GetDbValue(m)).ToArray()).ToList();

            int? copied;
            using (var db = ModelBase.CreateCommand(metadata.ConnectionName))
            {
                var table = SqlDialect.Table(db.ProviderType, metadata.TableName);
                copied = useAsync
                    ? await db.TryBulkCopyAsync(table, columns, rows, ct).ConfigureAwait(false)
                    : db.TryBulkCopy(table, columns, rows);
            }

            if (copied == null)
                return await BulkInsertCore(modelList, MaxRowsPerInsert, useAsync, ct).ConfigureAwait(false);

            foreach (var model in modelList)
                model.SyncOriginal();
            return copied.Value;
        }

        #endregion

        #region Upsert

        /// <summary>
        /// Insert new rows and update existing ones in one statement per batch:
        /// SQL Server MERGE, PostgreSQL / SQLite INSERT ... ON CONFLICT, MySQL / MariaDB INSERT ... ON DUPLICATE KEY UPDATE.
        /// </summary>
        /// <param name="models">Rows to write</param>
        /// <param name="uniqueBy">Columns (or property names) that identify an existing row. PostgreSQL / SQLite need a
        /// unique index on exactly these columns; MySQL / MariaDB match on any unique key of the table.
        /// Default: an application-assigned primary key.</param>
        /// <param name="updateColumns">Columns to overwrite on an existing row (default: every column except the
        /// key columns, the primary key and the created-at timestamp; none = insert only)</param>
        /// <param name="batchSize">Rows per statement (reduced automatically to the parameter limit)</param>
        /// <returns>Affected rows as reported by the provider (MySQL counts an updated row as 2)</returns>
        public static int Upsert<T>(IEnumerable<T> models, string[]? uniqueBy, string[]? updateColumns = null, int batchSize = 100) where T : Model<T>, new()
        {
            return DbRun.Sync(UpsertCore(models, uniqueBy, updateColumns, batchSize, false, default));
        }

        /// <summary>
        /// Upsert (async) — see <see cref="Upsert{T}"/>
        /// </summary>
        public static Task<int> UpsertAsync<T>(IEnumerable<T> models, string[]? uniqueBy, string[]? updateColumns = null, int batchSize = 100, CancellationToken cancellationToken = default) where T : Model<T>, new()
        {
            return UpsertCore(models, uniqueBy, updateColumns, batchSize, true, cancellationToken);
        }

        private static async Task<int> UpsertCore<T>(IEnumerable<T> models, string[]? uniqueBy, string[]? updateColumns, int batchSize, bool useAsync, CancellationToken ct) where T : Model<T>, new()
        {
            var modelList = models?.ToList() ?? new List<T>();
            if (modelList.Count == 0) return 0;

            var metadata = Model<T>.GetMetadata<T>();
            var insertProps = InsertProperties(metadata);
            var keyProps = ResolveKeyProperties(metadata, uniqueBy);

            foreach (var key in keyProps)
            {
                if (!insertProps.Contains(key))
                    throw new ArgumentException($"Upsert key '{key.ColumnName}' is an auto-increment primary key; use a unique column instead.", nameof(uniqueBy));
            }

            var version = metadata.VersionProperty;
            var created = metadata.CreatedAtProperty;
            List<PropertyMetadata> updateProps;
            if (updateColumns != null)
            {
                updateProps = updateColumns.Select(c => ModelBase.FindProperty(metadata, c)
                    ?? throw new ArgumentException($"Unknown column '{c}' on {typeof(T).Name}.", nameof(updateColumns))).ToList();
            }
            else
            {
                updateProps = insertProps.Where(p => !keyProps.Contains(p) && !p.IsPrimaryKey && p != version
                                                     && (created == null || p.PropertyInfo != created)).ToList();
            }

            var effectiveBatch = EffectiveBatch(batchSize, insertProps.Count);
            int affected = 0;

            await MersoTransaction.RunCore(metadata.ConnectionName, async () =>
            {
                for (int i = 0; i < modelList.Count; i += effectiveBatch)
                {
                    var batch = modelList.Skip(i).Take(effectiveBatch).ToList();
                    PrepareForInsert(batch, metadata);

                    using (var db = ModelBase.CreateCommand(metadata.ConnectionName))
                    {
                        var sql = BuildUpsert(db, metadata, batch, insertProps, keyProps, updateProps, version);
                        affected += await DbRun.NonQuery(db, sql, useAsync, ct).ConfigureAwait(false);
                    }
                }
                return affected;
            }).ConfigureAwait(false);

            return affected;
        }

        private static List<PropertyMetadata> ResolveKeyProperties(ModelMetadata metadata, string[]? uniqueBy)
        {
            if (uniqueBy != null && uniqueBy.Length > 0)
            {
                return uniqueBy.Select(c => ModelBase.FindProperty(metadata, c)
                    ?? throw new ArgumentException($"Unknown column '{c}' on {metadata.ModelType.Name}.", nameof(uniqueBy))).ToList();
            }

            var keys = metadata.KeyProperties;
            if (keys.Count == 0 || keys.Any(k => k.IsAutoIncrement))
                throw new ArgumentException("Upsert needs uniqueBy columns (or an application-assigned primary key).", nameof(uniqueBy));

            return keys.ToList();
        }

        private static string BuildUpsert<T>(DbCommandBase db, ModelMetadata metadata, List<T> batch,
            List<PropertyMetadata> insertProps, List<PropertyMetadata> keyProps, List<PropertyMetadata> updateProps, PropertyMetadata? version)
        {
            var p = db.ProviderType;
            var table = SqlDialect.Table(p, metadata.TableName);
            var columns = ColumnList(p, insertProps);
            var values = ValuesList(db, batch, insertProps);
            string Q(PropertyMetadata prop) => SqlDialect.Column(p, prop.ColumnName);
            var versionColumn = version != null ? Q(version) : null;

            switch (p)
            {
                case DbProviderType.SqlServer:
                {
                    var sb = new StringBuilder();
                    sb.Append($"MERGE INTO {table} WITH (HOLDLOCK) AS target USING (VALUES {values}) AS source ({columns}) ON ");
                    sb.Append(string.Join(" AND ", keyProps.Select(k => $"target.{Q(k)} = source.{Q(k)}")));

                    var sets = updateProps.Select(u => $"target.{Q(u)} = source.{Q(u)}").ToList();
                    if (version != null)
                        sets.Add($"target.{versionColumn} = target.{versionColumn} + 1");
                    if (sets.Count > 0)
                        sb.Append($" WHEN MATCHED THEN UPDATE SET {string.Join(", ", sets)}");

                    sb.Append($" WHEN NOT MATCHED THEN INSERT ({columns}) VALUES ({string.Join(", ", insertProps.Select(c => "source." + Q(c)))});");
                    return sb.ToString();
                }

                case DbProviderType.MySQL:
                case DbProviderType.MariaDB:
                {
                    var sets = updateProps.Select(u => $"{Q(u)} = VALUES({Q(u)})").ToList();
                    if (version != null)
                        sets.Add($"{versionColumn} = {versionColumn} + 1");
                    if (sets.Count == 0)
                        sets.Add($"{Q(keyProps[0])} = {Q(keyProps[0])}"); // insert only: keep the existing row

                    return $"INSERT INTO {table} ({columns}) VALUES {values} ON DUPLICATE KEY UPDATE {string.Join(", ", sets)}";
                }

                default: // PostgreSQL, SQLite
                {
                    var sets = updateProps.Select(u => $"{Q(u)} = excluded.{Q(u)}").ToList();
                    if (version != null)
                        sets.Add($"{versionColumn} = {table}.{versionColumn} + 1");

                    var action = sets.Count > 0 ? $"DO UPDATE SET {string.Join(", ", sets)}" : "DO NOTHING";
                    return $"INSERT INTO {table} ({columns}) VALUES {values} ON CONFLICT ({string.Join(", ", keyProps.Select(Q))}) {action}";
                }
            }
        }

        #endregion

        #region Update / Delete

        /// <summary>
        /// Toplu güncelleme (tek transaction). [RowVersion] modellerde sürüm kontrol edilir.
        /// </summary>
        public static int BulkUpdate<T>(IEnumerable<T> models) where T : Model<T>, new()
        {
            return DbRun.Sync(BulkUpdateCore(models, false, default));
        }

        /// <summary>
        /// Toplu güncelleme (async)
        /// </summary>
        public static Task<int> BulkUpdateAsync<T>(IEnumerable<T> models, CancellationToken cancellationToken = default) where T : Model<T>, new()
        {
            return BulkUpdateCore(models, true, cancellationToken);
        }

        private static async Task<int> BulkUpdateCore<T>(IEnumerable<T> models, bool useAsync, CancellationToken ct) where T : Model<T>, new()
        {
            var modelList = models?.ToList() ?? new List<T>();
            if (modelList.Count == 0) return 0;

            var metadata = Model<T>.GetMetadata<T>();
            if (metadata.KeyProperties.Count == 0)
                throw new InvalidOperationException($"Model {typeof(T).Name} has no primary key.");

            var version = metadata.VersionProperty;
            int totalUpdated = 0;

            await MersoTransaction.RunCore(metadata.ConnectionName, async () =>
            {
                foreach (var model in modelList)
                {
                    using (var db = ModelBase.CreateCommand(model.LoadedConnection ?? metadata.ConnectionName))
                    {
                        var p = db.ProviderType;

                        // Set updated timestamp
                        if (metadata.UpdatedAtProperty != null)
                            metadata.UpdatedAtProperty.SetValue(model, DateTime.UtcNow);

                        var setClauses = new List<string>();
                        int paramIndex = 0;

                        foreach (var prop in metadata.Properties)
                        {
                            if (prop.IsPrimaryKey || prop == version)
                                continue;

                            var paramName = $"@p{paramIndex++}";
                            setClauses.Add($"{SqlDialect.Column(p, prop.ColumnName)} = {paramName}");
                            db.ParametersAdd(paramName, prop.GetDbValue(model));
                        }

                        string? versionColumn = null;
                        object? expected = null;
                        if (version != null)
                        {
                            versionColumn = SqlDialect.Column(p, version.ColumnName);
                            expected = model.OriginalValue(version);
                            setClauses.Add($"{versionColumn} = {versionColumn} + 1");
                        }

                        if (setClauses.Count == 0)
                            continue;

                        // Key values as loaded: a changed key still finds its row
                        var keyParts = new List<string>();
                        for (int k = 0; k < metadata.KeyProperties.Count; k++)
                        {
                            var key = metadata.KeyProperties[k];
                            var name = "@__k" + k.ToString(CultureInfo.InvariantCulture);
                            keyParts.Add($"{SqlDialect.Column(p, key.ColumnName)} = {name}");
                            db.ParametersAdd(name, model.OriginalValue(key));
                        }

                        var sql = $"UPDATE {SqlDialect.Table(p, metadata.TableName)} SET {string.Join(", ", setClauses)} WHERE {string.Join(" AND ", keyParts)}";
                        if (version != null)
                        {
                            sql += $" AND {versionColumn} = @__ver";
                            db.ParametersAdd("@__ver", expected);
                        }

                        var affected = await DbRun.NonQuery(db, sql, useAsync, ct).ConfigureAwait(false);
                        if (version != null)
                        {
                            if (affected == 0)
                                throw new DbConcurrencyException($"{typeof(T).Name} ({string.Join(", ", metadata.KeyProperties.Select(k => k.PropertyInfo.GetValue(model)))}) was changed or deleted by another user (expected version {expected}).");
                            ModelMapper.TrySetProperty(version.PropertyInfo, model, Convert.ToInt64(expected ?? 0, CultureInfo.InvariantCulture) + 1);
                        }
                        totalUpdated += affected;
                    }
                }
                return totalUpdated;
            }).ConfigureAwait(false);

            foreach (var model in modelList)
                model.SyncOriginal();

            return totalUpdated;
        }

        /// <summary>
        /// Toplu silme (ID listesi ile, soft delete destekli) — etkilenen satır sayısını döndürür
        /// </summary>
        public static int BulkDelete<T>(IEnumerable<object> ids) where T : Model<T>, new()
        {
            var metadata = Model<T>.GetMetadata<T>();
            return DbRun.Sync(DeleteByIds(metadata, ids, metadata.HasSoftDelete, false, default));
        }

        /// <summary>
        /// Toplu silme (async)
        /// </summary>
        public static Task<int> BulkDeleteAsync<T>(IEnumerable<object> ids, CancellationToken cancellationToken = default) where T : Model<T>, new()
        {
            var metadata = Model<T>.GetMetadata<T>();
            return DeleteByIds(metadata, ids, metadata.HasSoftDelete, true, cancellationToken);
        }

        /// <summary>
        /// Toplu kalıcı silme (soft delete bypass) — etkilenen satır sayısını döndürür
        /// </summary>
        public static int BulkForceDelete<T>(IEnumerable<object> ids) where T : Model<T>, new()
        {
            var metadata = Model<T>.GetMetadata<T>();
            return DbRun.Sync(DeleteByIds(metadata, ids, false, false, default));
        }

        /// <summary>
        /// Toplu kalıcı silme (async)
        /// </summary>
        public static Task<int> BulkForceDeleteAsync<T>(IEnumerable<object> ids, CancellationToken cancellationToken = default) where T : Model<T>, new()
        {
            var metadata = Model<T>.GetMetadata<T>();
            return DeleteByIds(metadata, ids, false, true, cancellationToken);
        }

        private static async Task<int> DeleteByIds(ModelMetadata metadata, IEnumerable<object> ids, bool soft, bool useAsync, CancellationToken ct)
        {
            var idList = ids?.ToList() ?? new List<object>();
            if (idList.Count == 0) return 0;

            var keys = metadata.KeyProperties;
            if (keys.Count == 0)
                throw new InvalidOperationException($"Model {metadata.ModelType.Name} has no primary key.");

            int perCommand = Math.Max(1, MaxParametersPerCommand / keys.Count);
            int affected = 0;
            await MersoTransaction.RunCore(metadata.ConnectionName, async () =>
            {
                for (int start = 0; start < idList.Count; start += perCommand)
                {
                    var chunk = idList.Skip(start).Take(perCommand).ToList();
                    using (var db = ModelBase.CreateCommand(metadata.ConnectionName))
                    {
                        var p = db.ProviderType;
                        var table = SqlDialect.Table(p, metadata.TableName);
                        string match;

                        if (keys.Count == 1)
                        {
                            var paramNames = new List<string>();
                            for (int i = 0; i < chunk.Count; i++)
                            {
                                var paramName = $"@id{i}";
                                paramNames.Add(paramName);
                                db.ParametersAdd(paramName, chunk[i]);
                            }
                            match = $"{SqlDialect.Column(p, keys[0].ColumnName)} IN ({string.Join(", ", paramNames)})";
                        }
                        else
                        {
                            // Composite key: each id is an object[] in key order → (k1 = .. AND k2 = ..) OR ...
                            var rows = new List<string>();
                            for (int i = 0; i < chunk.Count; i++)
                            {
                                var values = metadata.GetKeyValues(chunk[i]);
                                var parts = new List<string>();
                                for (int k = 0; k < keys.Count; k++)
                                {
                                    var paramName = $"@id{i}_{k}";
                                    parts.Add($"{SqlDialect.Column(p, keys[k].ColumnName)} = {paramName}");
                                    db.ParametersAdd(paramName, values[k]);
                                }
                                rows.Add("(" + string.Join(" AND ", parts) + ")");
                            }
                            match = "(" + string.Join(" OR ", rows) + ")";
                        }

                        string sql;
                        if (soft)
                        {
                            var deleted = SqlDialect.Column(p, metadata.SoftDeleteColumn!);
                            db.ParametersAdd("@deletedAt", DateTime.UtcNow);
                            sql = $"UPDATE {table} SET {deleted} = @deletedAt WHERE {match} AND {deleted} IS NULL";
                        }
                        else
                        {
                            sql = $"DELETE FROM {table} WHERE {match}";
                        }

                        affected += await DbRun.NonQuery(db, sql, useAsync, ct).ConfigureAwait(false);
                    }
                }
                return affected;
            }).ConfigureAwait(false);

            return affected;
        }

        #endregion

        #region Helpers

        private static List<PropertyMetadata> InsertProperties(ModelMetadata metadata)
        {
            return metadata.Properties.Where(p => !p.IsPrimaryKey || !p.IsAutoIncrement).ToList();
        }

        private static int EffectiveBatch(int batchSize, int columnCount)
        {
            return Math.Max(1, Math.Min(Math.Min(batchSize, MaxRowsPerInsert), MaxParametersPerCommand / Math.Max(1, columnCount)));
        }

        // Timestamps, generated Guid keys and the first row version
        private static void PrepareForInsert<T>(List<T> batch, ModelMetadata metadata) where T : Model<T>, new()
        {
            var now = DateTime.UtcNow;
            var pkProp = metadata.PrimaryKeyProperty;
            var guidKey = pkProp != null && !metadata.PrimaryKeyAutoIncrement
                          && (Nullable.GetUnderlyingType(pkProp.PropertyType) ?? pkProp.PropertyType) == typeof(Guid);

            foreach (var model in batch)
            {
                if (metadata.CreatedAtProperty != null)
                    metadata.CreatedAtProperty.SetValue(model, now);
                if (metadata.UpdatedAtProperty != null)
                    metadata.UpdatedAtProperty.SetValue(model, now);

                if (guidKey && ModelMapper.IsDefaultKey(pkProp!.GetValue(model)))
                    pkProp.SetValue(model, Guid.NewGuid());

                if (metadata.VersionProperty != null && ModelMapper.IsDefaultKey(metadata.VersionProperty.PropertyInfo.GetValue(model)))
                    ModelMapper.TrySetProperty(metadata.VersionProperty.PropertyInfo, model, 1);
            }
        }

        private static string ColumnList(DbProviderType provider, List<PropertyMetadata> props)
        {
            return string.Join(", ", props.Select(p => SqlDialect.Column(provider, p.ColumnName)));
        }

        // "(@p0, @p1), (@p2, @p3)" with the parameters queued on db
        private static string ValuesList<T>(DbCommandBase db, List<T> batch, List<PropertyMetadata> props)
        {
            var rows = new List<string>(batch.Count);
            int paramIndex = 0;

            foreach (var model in batch)
            {
                var names = new List<string>(props.Count);
                foreach (var prop in props)
                {
                    var paramName = "@p" + (paramIndex++).ToString(CultureInfo.InvariantCulture);
                    names.Add(paramName);
                    db.ParametersAdd(paramName, prop.GetDbValue(model!));
                }
                rows.Add($"({string.Join(", ", names)})");
            }

            return string.Join(", ", rows);
        }

        #endregion
    }
}
