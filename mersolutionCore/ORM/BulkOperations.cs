using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace mersolutionCore.ORM
{
    /// <summary>
    /// Bulk Operations - Toplu veritabanı işlemleri
    /// </summary>
    public static class BulkOperations
    {
        /// <summary>
        /// Toplu kayıt ekleme
        /// </summary>
        public static int BulkInsert<T>(IEnumerable<T> models, int batchSize = 100) where T : Model<T>, new()
        {
            var modelList = models.ToList();
            if (modelList.Count == 0) return 0;

            var metadata = Model<T>.GetMetadata<T>();
            int totalInserted = 0;

            // Batch'lere böl
            for (int i = 0; i < modelList.Count; i += batchSize)
            {
                var batch = modelList.Skip(i).Take(batchSize).ToList();
                totalInserted += InsertBatch(batch, metadata);
            }

            return totalInserted;
        }

        private static int InsertBatch<T>(List<T> batch, ModelMetadata metadata) where T : Model<T>, new()
        {
            using (var db = ModelBase.ConnectionFactory())
            {
                var columns = metadata.Properties
                    .Where(p => !p.IsPrimaryKey || !p.IsAutoIncrement)
                    .Select(p => p.ColumnName)
                    .ToList();

                var sb = new StringBuilder();
                sb.Append($"INSERT INTO {metadata.TableName} ({string.Join(", ", columns)}) VALUES ");

                var valuesList = new List<string>();
                int paramIndex = 0;

                foreach (var model in batch)
                {
                    // Set timestamps
                    if (metadata.CreatedAtProperty != null)
                        metadata.CreatedAtProperty.SetValue(model, DateTime.UtcNow);
                    if (metadata.UpdatedAtProperty != null)
                        metadata.UpdatedAtProperty.SetValue(model, DateTime.UtcNow);

                    var values = new List<string>();
                    foreach (var prop in metadata.Properties)
                    {
                        if (prop.IsPrimaryKey && prop.IsAutoIncrement)
                            continue;

                        var paramName = $"@p{paramIndex++}";
                        values.Add(paramName);
                        db.ParametersAdd(paramName, prop.PropertyInfo.GetValue(model));
                    }
                    valuesList.Add($"({string.Join(", ", values)})");
                }

                sb.Append(string.Join(", ", valuesList));
                db.RunExecute(sb.ToString());

                return batch.Count;
            }
        }

        /// <summary>
        /// Toplu güncelleme
        /// </summary>
        public static int BulkUpdate<T>(IEnumerable<T> models) where T : Model<T>, new()
        {
            var modelList = models.ToList();
            if (modelList.Count == 0) return 0;

            var metadata = Model<T>.GetMetadata<T>();
            int totalUpdated = 0;

            using (var db = ModelBase.ConnectionFactory())
            {
                foreach (var model in modelList)
                {
                    // Set updated timestamp
                    if (metadata.UpdatedAtProperty != null)
                        metadata.UpdatedAtProperty.SetValue(model, DateTime.UtcNow);

                    var setClauses = new List<string>();
                    int paramIndex = 0;

                    foreach (var prop in metadata.Properties)
                    {
                        if (prop.IsPrimaryKey)
                            continue;

                        var paramName = $"@p{paramIndex++}";
                        setClauses.Add($"{prop.ColumnName} = {paramName}");
                        db.ParametersAdd(paramName, prop.PropertyInfo.GetValue(model));
                    }

                    var pkValue = metadata.PrimaryKeyProperty.GetValue(model);
                    db.ParametersAdd("@pk", pkValue);

                    var sql = $"UPDATE {metadata.TableName} SET {string.Join(", ", setClauses)} WHERE {metadata.PrimaryKeyColumn} = @pk";
                    db.RunExecute(sql);
                    db.ParametersClear();

                    totalUpdated++;
                }
            }

            return totalUpdated;
        }

        /// <summary>
        /// Toplu silme (ID listesi ile)
        /// </summary>
        public static int BulkDelete<T>(IEnumerable<object> ids) where T : Model<T>, new()
        {
            var idList = ids.ToList();
            if (idList.Count == 0) return 0;

            var metadata = Model<T>.GetMetadata<T>();

            using (var db = ModelBase.ConnectionFactory())
            {
                var paramNames = new List<string>();
                for (int i = 0; i < idList.Count; i++)
                {
                    var paramName = $"@id{i}";
                    paramNames.Add(paramName);
                    db.ParametersAdd(paramName, idList[i]);
                }

                string sql;
                if (metadata.HasSoftDelete)
                {
                    db.ParametersAdd("@deletedAt", DateTime.UtcNow);
                    sql = $"UPDATE {metadata.TableName} SET {metadata.SoftDeleteColumn} = @deletedAt WHERE {metadata.PrimaryKeyColumn} IN ({string.Join(", ", paramNames)})";
                }
                else
                {
                    sql = $"DELETE FROM {metadata.TableName} WHERE {metadata.PrimaryKeyColumn} IN ({string.Join(", ", paramNames)})";
                }

                db.RunExecute(sql);
                return idList.Count;
            }
        }

        /// <summary>
        /// Toplu kalıcı silme (soft delete bypass)
        /// </summary>
        public static int BulkForceDelete<T>(IEnumerable<object> ids) where T : Model<T>, new()
        {
            var idList = ids.ToList();
            if (idList.Count == 0) return 0;

            var metadata = Model<T>.GetMetadata<T>();

            using (var db = ModelBase.ConnectionFactory())
            {
                var paramNames = new List<string>();
                for (int i = 0; i < idList.Count; i++)
                {
                    var paramName = $"@id{i}";
                    paramNames.Add(paramName);
                    db.ParametersAdd(paramName, idList[i]);
                }

                var sql = $"DELETE FROM {metadata.TableName} WHERE {metadata.PrimaryKeyColumn} IN ({string.Join(", ", paramNames)})";
                db.RunExecute(sql);
                return idList.Count;
            }
        }
    }
}
