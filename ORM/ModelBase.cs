using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using mersolutionCore.Command.Abstractions;
using mersolutionCore.ORM.Entity;
using mersolutionCore.ORM.Relationships;

namespace mersolutionCore.ORM
{
    /// <summary>
    /// Base class for all ORM models
    /// </summary>
    public abstract class ModelBase
    {
        private static readonly Dictionary<Type, ModelMetadata> _metadataCache = new Dictionary<Type, ModelMetadata>();

        /// <summary>
        /// Database connection provider
        /// </summary>
        internal static Func<DbCommandBase> ConnectionFactory { get; private set; }

        /// <summary>
        /// Configure the database connection factory
        /// </summary>
        public static void Configure(Func<DbCommandBase> connectionFactory)
        {
            ConnectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        /// <summary>
        /// Get model metadata (cached)
        /// </summary>
        internal static ModelMetadata GetMetadata<T>() where T : ModelBase
        {
            var type = typeof(T);
            if (!_metadataCache.ContainsKey(type))
            {
                _metadataCache[type] = new ModelMetadata(type);
            }
            return _metadataCache[type];
        }

        /// <summary>
        /// Get metadata for this instance
        /// </summary>
        protected ModelMetadata GetMetadata()
        {
            var type = GetType();
            if (!_metadataCache.ContainsKey(type))
            {
                _metadataCache[type] = new ModelMetadata(type);
            }
            return _metadataCache[type];
        }
    }

    /// <summary>
    /// Generic model base with CRUD operations
    /// </summary>
    /// <typeparam name="T">Model type</typeparam>
    public abstract class Model<T> : ModelBase where T : Model<T>, new()
    {
        #region Static CRUD Methods

        /// <summary>
        /// Find record by primary key
        /// </summary>
        public static T Find(object id)
        {
            var metadata = GetMetadata<T>();
            using (var db = ConnectionFactory())
            {
                var sql = $"SELECT * FROM {metadata.TableName} WHERE {metadata.PrimaryKeyColumn} = @id";
                db.ParametersAdd("@id", id);
                var dt = db.RunDataTable(sql);

                if (dt.Rows.Count == 0)
                    return null;

                return MapToModel(dt.Rows[0], metadata);
            }
        }

        /// <summary>
        /// Find record by primary key or throw exception
        /// </summary>
        public static T FindOrFail(object id)
        {
            var result = Find(id);
            if (result == null)
                throw new ModelNotFoundException($"Record with id {id} not found in {GetMetadata<T>().TableName}");
            return result;
        }

        /// <summary>
        /// Find multiple records by primary keys
        /// </summary>
        public static List<T> FindMany(params object[] ids)
        {
            if (ids == null || ids.Length == 0)
                return new List<T>();

            var metadata = GetMetadata<T>();
            using (var db = ConnectionFactory())
            {
                var placeholders = string.Join(", ", ids.Select((_, i) => $"@id{i}"));
                var sql = $"SELECT * FROM {metadata.TableName} WHERE {metadata.PrimaryKeyColumn} IN ({placeholders})";
                
                for (int i = 0; i < ids.Length; i++)
                {
                    db.ParametersAdd($"@id{i}", ids[i]);
                }

                if (metadata.HasSoftDelete)
                    sql += $" AND {metadata.SoftDeleteColumn} IS NULL";

                var dt = db.RunDataTable(sql);
                return MapToModels(dt, metadata);
            }
        }

        /// <summary>
        /// Find multiple records by primary keys (list)
        /// </summary>
        public static List<T> FindMany(IEnumerable<object> ids)
        {
            return FindMany(ids.ToArray());
        }

        /// <summary>
        /// Get all records
        /// </summary>
        public static List<T> All()
        {
            var metadata = GetMetadata<T>();
            using (var db = ConnectionFactory())
            {
                var sql = $"SELECT * FROM {metadata.TableName}";
                
                if (metadata.HasSoftDelete)
                    sql += $" WHERE {metadata.SoftDeleteColumn} IS NULL";

                var dt = db.RunDataTable(sql);
                return MapToModels(dt, metadata);
            }
        }

        /// <summary>
        /// Create new record
        /// </summary>
        public static T Create(Dictionary<string, object> data)
        {
            var model = new T();
            foreach (var kvp in data)
            {
                model.SetProperty(kvp.Key, kvp.Value);
            }
            model.Save();
            return model;
        }

        /// <summary>
        /// Create new record from model instance
        /// </summary>
        public static T Create(T model)
        {
            model.Save();
            return model;
        }

        /// <summary>
        /// Start a query builder
        /// </summary>
        public static QueryBuilder<T> Query()
        {
            return new QueryBuilder<T>();
        }

        /// <summary>
        /// Where clause shortcut
        /// </summary>
        public static QueryBuilder<T> Where(string column, string op, object value)
        {
            return Query().Where(column, op, value);
        }

        /// <summary>
        /// Where clause shortcut (equals)
        /// </summary>
        public static QueryBuilder<T> Where(string column, object value)
        {
            return Query().Where(column, "=", value);
        }

        /// <summary>
        /// Get first record or null
        /// </summary>
        public static T First()
        {
            return Query().First();
        }

        /// <summary>
        /// Get first record or throw exception
        /// </summary>
        public static T FirstOrFail()
        {
            var result = First();
            if (result == null)
                throw new ModelNotFoundException($"No records found in {GetMetadata<T>().TableName}");
            return result;
        }

        /// <summary>
        /// Find first matching record or create new one
        /// </summary>
        public static T FirstOrCreate(Dictionary<string, object> search, Dictionary<string, object> create = null)
        {
            var query = Query();
            foreach (var kvp in search)
            {
                query.Where(kvp.Key, kvp.Value);
            }
            
            var existing = query.First();
            if (existing != null)
                return existing;

            var data = new Dictionary<string, object>(search);
            if (create != null)
            {
                foreach (var kvp in create)
                {
                    data[kvp.Key] = kvp.Value;
                }
            }
            return Create(data);
        }

        /// <summary>
        /// Find first matching record or create new instance (not saved)
        /// </summary>
        public static T FirstOrNew(Dictionary<string, object> search, Dictionary<string, object> values = null)
        {
            var query = Query();
            foreach (var kvp in search)
            {
                query.Where(kvp.Key, kvp.Value);
            }
            
            var existing = query.First();
            if (existing != null)
                return existing;

            var model = new T();
            foreach (var kvp in search)
            {
                model.SetProperty(kvp.Key, kvp.Value);
            }
            if (values != null)
            {
                foreach (var kvp in values)
                {
                    model.SetProperty(kvp.Key, kvp.Value);
                }
            }
            return model;
        }

        /// <summary>
        /// Update existing record or create new one
        /// </summary>
        public static T UpdateOrCreate(Dictionary<string, object> search, Dictionary<string, object> update)
        {
            var query = Query();
            foreach (var kvp in search)
            {
                query.Where(kvp.Key, kvp.Value);
            }
            
            var existing = query.First();
            if (existing != null)
            {
                foreach (var kvp in update)
                {
                    existing.SetProperty(kvp.Key, kvp.Value);
                }
                existing.Save();
                return existing;
            }

            var data = new Dictionary<string, object>(search);
            foreach (var kvp in update)
            {
                data[kvp.Key] = kvp.Value;
            }
            return Create(data);
        }

        /// <summary>
        /// Count all records
        /// </summary>
        public static int Count()
        {
            var metadata = GetMetadata<T>();
            using (var db = ConnectionFactory())
            {
                var sql = $"SELECT COUNT(*) FROM {metadata.TableName}";
                
                if (metadata.HasSoftDelete)
                    sql += $" WHERE {metadata.SoftDeleteColumn} IS NULL";

                return db.RunToInt32Scaler(sql);
            }
        }

        /// <summary>
        /// Delete record by primary key
        /// </summary>
        public static bool Destroy(object id)
        {
            var metadata = GetMetadata<T>();
            using (var db = ConnectionFactory())
            {
                string sql;
                if (metadata.HasSoftDelete)
                {
                    sql = $"UPDATE {metadata.TableName} SET {metadata.SoftDeleteColumn} = @now WHERE {metadata.PrimaryKeyColumn} = @id";
                    db.ParametersAdd("@now", DateTime.UtcNow);
                }
                else
                {
                    sql = $"DELETE FROM {metadata.TableName} WHERE {metadata.PrimaryKeyColumn} = @id";
                }
                db.ParametersAdd("@id", id);
                db.RunExecute(sql);
                return true;
            }
        }

        /// <summary>
        /// Delete multiple records by primary keys
        /// </summary>
        public static int Destroy(params object[] ids)
        {
            int count = 0;
            foreach (var id in ids)
            {
                if (Destroy(id))
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Delete multiple records by primary keys (list)
        /// </summary>
        public static int Destroy(IEnumerable<object> ids)
        {
            return Destroy(ids.ToArray());
        }

        /// <summary>
        /// Get all records including soft deleted
        /// </summary>
        public static List<T> WithTrashed()
        {
            var metadata = GetMetadata<T>();
            using (var db = ConnectionFactory())
            {
                var sql = $"SELECT * FROM {metadata.TableName}";
                var dt = db.RunDataTable(sql);
                return MapToModels(dt, metadata);
            }
        }

        /// <summary>
        /// Get only soft deleted records
        /// </summary>
        public static List<T> OnlyTrashed()
        {
            var metadata = GetMetadata<T>();
            if (!metadata.HasSoftDelete)
                return new List<T>();

            using (var db = ConnectionFactory())
            {
                var sql = $"SELECT * FROM {metadata.TableName} WHERE {metadata.SoftDeleteColumn} IS NOT NULL";
                var dt = db.RunDataTable(sql);
                return MapToModels(dt, metadata);
            }
        }

        /// <summary>
        /// Check if any records exist
        /// </summary>
        public static bool Exists()
        {
            return Count() > 0;
        }

        /// <summary>
        /// Check if record with id exists
        /// </summary>
        public static bool Exists(object id)
        {
            return Find(id) != null;
        }

        /// <summary>
        /// Get sum of a column
        /// </summary>
        public static decimal Sum(string column)
        {
            var metadata = GetMetadata<T>();
            using (var db = ConnectionFactory())
            {
                var sql = $"SELECT COALESCE(SUM({column}), 0) FROM {metadata.TableName}";
                if (metadata.HasSoftDelete)
                    sql += $" WHERE {metadata.SoftDeleteColumn} IS NULL";
                return db.RunToDecimalScaler(sql);
            }
        }

        /// <summary>
        /// Get average of a column
        /// </summary>
        public static decimal Avg(string column)
        {
            var metadata = GetMetadata<T>();
            using (var db = ConnectionFactory())
            {
                var sql = $"SELECT COALESCE(AVG({column}), 0) FROM {metadata.TableName}";
                if (metadata.HasSoftDelete)
                    sql += $" WHERE {metadata.SoftDeleteColumn} IS NULL";
                return db.RunToDecimalScaler(sql);
            }
        }

        /// <summary>
        /// Get min value of a column
        /// </summary>
        public static object Min(string column)
        {
            var metadata = GetMetadata<T>();
            using (var db = ConnectionFactory())
            {
                var sql = $"SELECT MIN({column}) FROM {metadata.TableName}";
                if (metadata.HasSoftDelete)
                    sql += $" WHERE {metadata.SoftDeleteColumn} IS NULL";
                return db.RunToObjectScaler(sql);
            }
        }

        /// <summary>
        /// Get max value of a column
        /// </summary>
        public static object Max(string column)
        {
            var metadata = GetMetadata<T>();
            using (var db = ConnectionFactory())
            {
                var sql = $"SELECT MAX({column}) FROM {metadata.TableName}";
                if (metadata.HasSoftDelete)
                    sql += $" WHERE {metadata.SoftDeleteColumn} IS NULL";
                return db.RunToObjectScaler(sql);
            }
        }

        /// <summary>
        /// Get values of a single column
        /// </summary>
        public static List<object> Pluck(string column)
        {
            var metadata = GetMetadata<T>();
            using (var db = ConnectionFactory())
            {
                var sql = $"SELECT {column} FROM {metadata.TableName}";
                if (metadata.HasSoftDelete)
                    sql += $" WHERE {metadata.SoftDeleteColumn} IS NULL";
                var dt = db.RunDataTable(sql);
                return dt.AsEnumerable().Select(r => r[0]).ToList();
            }
        }

        /// <summary>
        /// Truncate table (delete all records)
        /// </summary>
        public static void Truncate()
        {
            var metadata = GetMetadata<T>();
            using (var db = ConnectionFactory())
            {
                db.RunExecute($"DELETE FROM {metadata.TableName}");
            }
        }

        #endregion

        #region Instance Methods

        /// <summary>
        /// Save the model (insert or update)
        /// </summary>
        public void Save()
        {
            // Fire OnMersoSaving event
            if (!FireMersoEvent(MersoEventType.Saving))
                return;

            var metadata = GetMetadata();
            var pkValue = GetPrimaryKeyValue(metadata);

            if (pkValue == null || (pkValue is int intVal && intVal == 0))
            {
                // Fire OnMersoCreating event
                if (!FireMersoEvent(MersoEventType.Creating))
                    return;

                Insert(metadata);

                // Fire OnMersoCreated event
                FireMersoEvent(MersoEventType.Created);
            }
            else
            {
                // Fire OnMersoUpdating event
                if (!FireMersoEvent(MersoEventType.Updating))
                    return;

                Update(metadata);

                // Fire OnMersoUpdated event
                FireMersoEvent(MersoEventType.Updated);
            }

            // Fire OnMersoSaved event
            FireMersoEvent(MersoEventType.Saved);
        }

        /// <summary>
        /// Delete this record
        /// </summary>
        public void Delete()
        {
            // Fire OnMersoDeleting event
            if (!FireMersoEvent(MersoEventType.Deleting))
                return;

            var metadata = GetMetadata();
            var pkValue = GetPrimaryKeyValue(metadata);
            Destroy(pkValue);

            // Fire OnMersoDeleted event
            FireMersoEvent(MersoEventType.Deleted);
        }

        /// <summary>
        /// Refresh model from database
        /// </summary>
        public void Refresh()
        {
            var metadata = GetMetadata();
            var pkValue = GetPrimaryKeyValue(metadata);
            var fresh = Find(pkValue);

            if (fresh != null)
            {
                foreach (var prop in metadata.Properties)
                {
                    var value = prop.PropertyInfo.GetValue(fresh);
                    prop.PropertyInfo.SetValue(this, value);
                }
            }
        }

        /// <summary>
        /// Convert model to dictionary
        /// </summary>
        public Dictionary<string, object> ToDict()
        {
            var metadata = GetMetadata();
            var dict = new Dictionary<string, object>();

            foreach (var prop in metadata.Properties)
            {
                dict[prop.ColumnName] = prop.PropertyInfo.GetValue(this);
            }

            return dict;
        }

        /// <summary>
        /// Convert model to array (alias for ToDict)
        /// </summary>
        public Dictionary<string, object> ToArray()
        {
            return ToDict();
        }

        /// <summary>
        /// Convert model to JSON string
        /// </summary>
        public string ToJson()
        {
            var dict = ToDict();
            return System.Text.Json.JsonSerializer.Serialize(dict);
        }

        /// <summary>
        /// Check if this record is soft deleted
        /// </summary>
        public bool Trashed()
        {
            var metadata = GetMetadata();
            if (!metadata.HasSoftDelete)
                return false;

            var deletedAt = metadata.SoftDeleteProperty?.GetValue(this);
            return deletedAt != null;
        }

        /// <summary>
        /// Restore soft deleted record
        /// </summary>
        public void Restore()
        {
            var metadata = GetMetadata();
            if (!metadata.HasSoftDelete)
                return;

            // Fire OnMersoRestoring event
            if (!FireMersoEvent(MersoEventType.Restoring))
                return;

            metadata.SoftDeleteProperty?.SetValue(this, null);
            
            using (var db = ConnectionFactory())
            {
                var pkValue = GetPrimaryKeyValue(metadata);
                db.ParametersAdd("@pk", pkValue);
                var sql = $"UPDATE {metadata.TableName} SET {metadata.SoftDeleteColumn} = NULL WHERE {metadata.PrimaryKeyColumn} = @pk";
                db.RunExecute(sql);
            }

            // Fire OnMersoRestored event
            FireMersoEvent(MersoEventType.Restored);
        }

        /// <summary>
        /// Permanently delete record (bypass soft delete)
        /// </summary>
        public void ForceDelete()
        {
            var metadata = GetMetadata();
            var pkValue = GetPrimaryKeyValue(metadata);

            using (var db = ConnectionFactory())
            {
                db.ParametersAdd("@pk", pkValue);
                var sql = $"DELETE FROM {metadata.TableName} WHERE {metadata.PrimaryKeyColumn} = @pk";
                db.RunExecute(sql);
            }
        }

        /// <summary>
        /// Clone this model (without primary key)
        /// </summary>
        public T Replicate()
        {
            var metadata = GetMetadata();
            var clone = new T();

            foreach (var prop in metadata.Properties)
            {
                if (prop.IsPrimaryKey)
                    continue;

                var value = prop.PropertyInfo.GetValue(this);
                prop.PropertyInfo.SetValue(clone, value);
            }

            return clone;
        }

        /// <summary>
        /// Increment a column value
        /// </summary>
        public void Increment(string column, int amount = 1)
        {
            var metadata = GetMetadata();
            var pkValue = GetPrimaryKeyValue(metadata);

            using (var db = ConnectionFactory())
            {
                db.ParametersAdd("@pk", pkValue);
                db.ParametersAdd("@amount", amount);
                var sql = $"UPDATE {metadata.TableName} SET {column} = {column} + @amount WHERE {metadata.PrimaryKeyColumn} = @pk";
                db.RunExecute(sql);
            }

            Refresh();
        }

        /// <summary>
        /// Decrement a column value
        /// </summary>
        public void Decrement(string column, int amount = 1)
        {
            Increment(column, -amount);
        }

        /// <summary>
        /// Fill model with data
        /// </summary>
        public T Fill(Dictionary<string, object> data)
        {
            foreach (var kvp in data)
            {
                SetProperty(kvp.Key, kvp.Value);
            }
            return (T)(object)this;
        }

        /// <summary>
        /// Get fresh instance from database
        /// </summary>
        public T Fresh()
        {
            var metadata = GetMetadata();
            var pkValue = GetPrimaryKeyValue(metadata);
            return Find(pkValue);
        }

        /// <summary>
        /// Check if model exists in database
        /// </summary>
        public bool ExistsInDb()
        {
            var metadata = GetMetadata();
            var pkValue = GetPrimaryKeyValue(metadata);
            return pkValue != null && Find(pkValue) != null;
        }

        /// <summary>
        /// Get only specified attributes
        /// </summary>
        public Dictionary<string, object> Only(params string[] keys)
        {
            var dict = ToDict();
            return dict.Where(kvp => keys.Contains(kvp.Key, StringComparer.OrdinalIgnoreCase))
                       .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        /// <summary>
        /// Get all attributes except specified ones
        /// </summary>
        public Dictionary<string, object> Except(params string[] keys)
        {
            var dict = ToDict();
            return dict.Where(kvp => !keys.Contains(kvp.Key, StringComparer.OrdinalIgnoreCase))
                       .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        #endregion

        #region Relationship Methods

        /// <summary>
        /// Define a HasOne relationship (1:1)
        /// </summary>
        protected HasOne<T, TRelated> HasOne<TRelated>(string foreignKey = null, string localKey = "Id")
            where TRelated : Model<TRelated>, new()
        {
            return new HasOne<T, TRelated>((T)(object)this, foreignKey, localKey);
        }

        /// <summary>
        /// Define a HasMany relationship (1:N)
        /// </summary>
        protected HasMany<T, TRelated> HasMany<TRelated>(string foreignKey = null, string localKey = "Id")
            where TRelated : Model<TRelated>, new()
        {
            return new HasMany<T, TRelated>((T)(object)this, foreignKey, localKey);
        }

        /// <summary>
        /// Define a BelongsTo relationship (N:1)
        /// </summary>
        protected BelongsTo<T, TRelated> BelongsTo<TRelated>(string foreignKey = null, string ownerKey = "Id")
            where TRelated : Model<TRelated>, new()
        {
            return new BelongsTo<T, TRelated>((T)(object)this, foreignKey, ownerKey);
        }

        /// <summary>
        /// Define a BelongsToMany relationship (N:M)
        /// </summary>
        protected BelongsToMany<T, TRelated> BelongsToMany<TRelated>(
            string pivotTable = null,
            string parentKey = null,
            string relatedKey = null,
            string localKey = "Id")
            where TRelated : Model<TRelated>, new()
        {
            return new BelongsToMany<T, TRelated>((T)(object)this, pivotTable, parentKey, relatedKey, localKey);
        }

        #endregion

        #region Private Methods

        private void Insert(ModelMetadata metadata)
        {
            using (var db = ConnectionFactory())
            {
                var columns = new List<string>();
                var parameters = new List<string>();

                // Set timestamps
                if (metadata.CreatedAtProperty != null)
                {
                    metadata.CreatedAtProperty.SetValue(this, DateTime.UtcNow);
                }
                if (metadata.UpdatedAtProperty != null)
                {
                    metadata.UpdatedAtProperty.SetValue(this, DateTime.UtcNow);
                }

                foreach (var prop in metadata.Properties)
                {
                    if (prop.IsPrimaryKey && prop.IsAutoIncrement)
                        continue;

                    var value = prop.PropertyInfo.GetValue(this);
                    if (value != null || !prop.Nullable)
                    {
                        columns.Add(prop.ColumnName);
                        parameters.Add($"@{prop.ColumnName}");
                        db.ParametersAdd($"@{prop.ColumnName}", value);
                    }
                }

                var sql = $"INSERT INTO {metadata.TableName} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", parameters)})";
                db.RunExecute(sql);

                // Get inserted ID
                if (metadata.PrimaryKeyProperty != null && metadata.PrimaryKeyAutoIncrement)
                {
                    var lastId = db.PKLastKeyOrDefault(metadata.TableName, metadata.PrimaryKeyColumn);
                    metadata.PrimaryKeyProperty.SetValue(this, lastId);
                }
            }
        }

        private void Update(ModelMetadata metadata)
        {
            using (var db = ConnectionFactory())
            {
                var setClauses = new List<string>();

                // Set updated timestamp
                if (metadata.UpdatedAtProperty != null)
                {
                    metadata.UpdatedAtProperty.SetValue(this, DateTime.UtcNow);
                }

                foreach (var prop in metadata.Properties)
                {
                    if (prop.IsPrimaryKey)
                        continue;

                    var value = prop.PropertyInfo.GetValue(this);
                    setClauses.Add($"{prop.ColumnName} = @{prop.ColumnName}");
                    db.ParametersAdd($"@{prop.ColumnName}", value);
                }

                var pkValue = GetPrimaryKeyValue(metadata);
                db.ParametersAdd("@pk", pkValue);

                var sql = $"UPDATE {metadata.TableName} SET {string.Join(", ", setClauses)} WHERE {metadata.PrimaryKeyColumn} = @pk";
                db.RunExecute(sql);
            }
        }

        private object GetPrimaryKeyValue(ModelMetadata metadata)
        {
            return metadata.PrimaryKeyProperty?.GetValue(this);
        }

        /// <summary>
        /// Fire MersoEvent - returns false if event handler cancels the operation
        /// </summary>
        private bool FireMersoEvent(MersoEventType eventType)
        {
            // Check if model implements IMersoEvents
            if (this is IMersoEvents events)
            {
                switch (eventType)
                {
                    case MersoEventType.Creating:
                        return events.OnMersoCreating();
                    case MersoEventType.Created:
                        events.OnMersoCreated();
                        return true;
                    case MersoEventType.Updating:
                        return events.OnMersoUpdating();
                    case MersoEventType.Updated:
                        events.OnMersoUpdated();
                        return true;
                    case MersoEventType.Deleting:
                        return events.OnMersoDeleting();
                    case MersoEventType.Deleted:
                        events.OnMersoDeleted();
                        return true;
                    case MersoEventType.Saving:
                        return events.OnMersoSaving();
                    case MersoEventType.Saved:
                        events.OnMersoSaved();
                        return true;
                    case MersoEventType.Restoring:
                        return events.OnMersoRestoring();
                    case MersoEventType.Restored:
                        events.OnMersoRestored();
                        return true;
                    default:
                        return true;
                }
            }
            return true;
        }

        internal void SetProperty(string name, object value)
        {
            var prop = GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop != null && prop.CanWrite)
            {
                try
                {
                    var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                    if (value == null)
                    {
                        prop.SetValue(this, null);
                    }
                    else
                    {
                        prop.SetValue(this, Convert.ChangeType(value, targetType));
                    }
                }
                catch
                {
                    // Skip if conversion fails
                }
            }
        }

        private static T MapToModel(DataRow row, ModelMetadata metadata)
        {
            var model = new T();

            foreach (var prop in metadata.Properties)
            {
                if (row.Table.Columns.Contains(prop.ColumnName))
                {
                    var value = row[prop.ColumnName];
                    if (value != DBNull.Value)
                    {
                        try
                        {
                            var targetType = Nullable.GetUnderlyingType(prop.PropertyInfo.PropertyType) ?? prop.PropertyInfo.PropertyType;
                            var convertedValue = Convert.ChangeType(value, targetType);
                            prop.PropertyInfo.SetValue(model, convertedValue);
                        }
                        catch
                        {
                            // Skip if conversion fails
                        }
                    }
                }
            }

            return model;
        }

        private static List<T> MapToModels(DataTable dt, ModelMetadata metadata)
        {
            var list = new List<T>();
            foreach (DataRow row in dt.Rows)
            {
                list.Add(MapToModel(row, metadata));
            }
            return list;
        }

        #endregion
    }
}
