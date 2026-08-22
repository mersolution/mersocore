using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using mersolutionCore.Command.Abstractions;

namespace mersolutionCore.ORM.Relationships
{
    /// <summary>
    /// Base class for all relationships
    /// </summary>
    public abstract class Relationship<TParent, TRelated>
        where TParent : Model<TParent>, new()
        where TRelated : Model<TRelated>, new()
    {
        protected readonly TParent _parent;
        protected readonly string _foreignKey;
        protected readonly string _localKey;
        protected readonly Func<DbCommandBase> _connectionFactory;
        protected readonly ModelMetadata _relatedMetadata;

        protected Relationship(TParent parent, string foreignKey, string localKey, Func<DbCommandBase> connectionFactory)
        {
            _parent = parent;
            _foreignKey = foreignKey;
            _localKey = localKey;
            _connectionFactory = connectionFactory;
            _relatedMetadata = ModelBase.GetMetadata<TRelated>();
        }

        /// <summary>
        /// Get the related model(s)
        /// </summary>
        public abstract object Get();

        /// <summary>
        /// Get the local key value from parent
        /// </summary>
        protected object GetLocalKeyValue()
        {
            var parentMetadata = ModelBase.GetMetadata<TParent>();
            var prop = parentMetadata.Properties.FirstOrDefault(p => 
                p.ColumnName.Equals(_localKey, StringComparison.OrdinalIgnoreCase) ||
                p.PropertyName.Equals(_localKey, StringComparison.OrdinalIgnoreCase));

            return prop?.PropertyInfo.GetValue(_parent);
        }

        /// <summary>
        /// Map DataRow to model
        /// </summary>
        protected TRelated MapToModel(DataRow row)
        {
            var model = new TRelated();
            foreach (var prop in _relatedMetadata.Properties)
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
                        catch { }
                    }
                }
            }
            return model;
        }

        /// <summary>
        /// Map DataTable to list of models
        /// </summary>
        protected List<TRelated> MapToModels(DataTable dt)
        {
            var list = new List<TRelated>();
            foreach (DataRow row in dt.Rows)
            {
                list.Add(MapToModel(row));
            }
            return list;
        }
    }

    /// <summary>
    /// HasOne relationship (1:1)
    /// </summary>
    public class HasOne<TParent, TRelated> : Relationship<TParent, TRelated>
        where TParent : Model<TParent>, new()
        where TRelated : Model<TRelated>, new()
    {
        public HasOne(TParent parent, string foreignKey = null, string localKey = "Id", Func<DbCommandBase> connectionFactory = null)
            : base(parent, foreignKey ?? typeof(TParent).Name + "Id", localKey, connectionFactory ?? ModelBase.ConnectionFactory)
        {
        }

        /// <summary>
        /// Get the related model
        /// </summary>
        public override object Get()
        {
            return GetRelated();
        }

        /// <summary>
        /// Get the related model (typed)
        /// </summary>
        public TRelated GetRelated()
        {
            var localValue = GetLocalKeyValue();
            if (localValue == null) return null;

            using (var db = _connectionFactory())
            {
                var sql = $"SELECT * FROM {_relatedMetadata.TableName} WHERE {_foreignKey} = @fk";
                db.ParametersAdd("@fk", localValue);

                var dt = db.RunDataTable(sql);
                if (dt.Rows.Count == 0) return null;

                return MapToModel(dt.Rows[0]);
            }
        }

        /// <summary>
        /// Create related model
        /// </summary>
        public TRelated Create(Dictionary<string, object> data)
        {
            var localValue = GetLocalKeyValue();
            data[_foreignKey] = localValue;

            return Model<TRelated>.Create(data);
        }

        /// <summary>
        /// Associate an existing model
        /// </summary>
        public void Associate(TRelated related)
        {
            var localValue = GetLocalKeyValue();
            var relatedProp = _relatedMetadata.Properties.FirstOrDefault(p =>
                p.ColumnName.Equals(_foreignKey, StringComparison.OrdinalIgnoreCase));

            if (relatedProp != null)
            {
                relatedProp.PropertyInfo.SetValue(related, localValue);
                related.Save();
            }
        }

        /// <summary>
        /// Dissociate the related model
        /// </summary>
        public void Dissociate()
        {
            var related = GetRelated();
            if (related != null)
            {
                var relatedProp = _relatedMetadata.Properties.FirstOrDefault(p =>
                    p.ColumnName.Equals(_foreignKey, StringComparison.OrdinalIgnoreCase));

                if (relatedProp != null)
                {
                    relatedProp.PropertyInfo.SetValue(related, null);
                    related.Save();
                }
            }
        }
    }

    /// <summary>
    /// HasMany relationship (1:N)
    /// </summary>
    public class HasMany<TParent, TRelated> : Relationship<TParent, TRelated>
        where TParent : Model<TParent>, new()
        where TRelated : Model<TRelated>, new()
    {
        public HasMany(TParent parent, string foreignKey = null, string localKey = "Id", Func<DbCommandBase> connectionFactory = null)
            : base(parent, foreignKey ?? typeof(TParent).Name + "Id", localKey, connectionFactory ?? ModelBase.ConnectionFactory)
        {
        }

        /// <summary>
        /// Get all related models
        /// </summary>
        public override object Get()
        {
            return GetRelated();
        }

        /// <summary>
        /// Get all related models (typed)
        /// </summary>
        public List<TRelated> GetRelated()
        {
            var localValue = GetLocalKeyValue();
            if (localValue == null) return new List<TRelated>();

            using (var db = _connectionFactory())
            {
                var sql = $"SELECT * FROM {_relatedMetadata.TableName} WHERE {_foreignKey} = @fk";

                // Add soft delete filter if applicable
                if (_relatedMetadata.HasSoftDelete)
                    sql += $" AND {_relatedMetadata.SoftDeleteColumn} IS NULL";

                db.ParametersAdd("@fk", localValue);

                var dt = db.RunDataTable(sql);
                return MapToModels(dt);
            }
        }

        /// <summary>
        /// Get related models with query builder
        /// </summary>
        public QueryBuilder<TRelated> Query()
        {
            var localValue = GetLocalKeyValue();
            return Model<TRelated>.Where(_foreignKey, localValue);
        }

        /// <summary>
        /// Count related models
        /// </summary>
        public int Count()
        {
            var localValue = GetLocalKeyValue();
            if (localValue == null) return 0;

            using (var db = _connectionFactory())
            {
                var sql = $"SELECT COUNT(*) FROM {_relatedMetadata.TableName} WHERE {_foreignKey} = @fk";

                if (_relatedMetadata.HasSoftDelete)
                    sql += $" AND {_relatedMetadata.SoftDeleteColumn} IS NULL";

                db.ParametersAdd("@fk", localValue);
                return db.RunToInt32Scaler(sql);
            }
        }

        /// <summary>
        /// Create a related model
        /// </summary>
        public TRelated Create(Dictionary<string, object> data)
        {
            var localValue = GetLocalKeyValue();
            data[_foreignKey] = localValue;

            return Model<TRelated>.Create(data);
        }

        /// <summary>
        /// Create multiple related models
        /// </summary>
        public List<TRelated> CreateMany(IEnumerable<Dictionary<string, object>> dataList)
        {
            var results = new List<TRelated>();
            foreach (var data in dataList)
            {
                results.Add(Create(data));
            }
            return results;
        }

        /// <summary>
        /// Delete all related models
        /// </summary>
        public int DeleteAll()
        {
            var localValue = GetLocalKeyValue();
            if (localValue == null) return 0;

            using (var db = _connectionFactory())
            {
                string sql;
                if (_relatedMetadata.HasSoftDelete)
                {
                    sql = $"UPDATE {_relatedMetadata.TableName} SET {_relatedMetadata.SoftDeleteColumn} = @now WHERE {_foreignKey} = @fk";
                    db.ParametersAdd("@now", DateTime.UtcNow);
                }
                else
                {
                    sql = $"DELETE FROM {_relatedMetadata.TableName} WHERE {_foreignKey} = @fk";
                }

                db.ParametersAdd("@fk", localValue);
                db.RunExecute(sql);
                return 1;
            }
        }
    }

    /// <summary>
    /// BelongsTo relationship (N:1)
    /// </summary>
    public class BelongsTo<TParent, TRelated> : Relationship<TParent, TRelated>
        where TParent : Model<TParent>, new()
        where TRelated : Model<TRelated>, new()
    {
        public BelongsTo(TParent parent, string foreignKey = null, string ownerKey = "Id", Func<DbCommandBase> connectionFactory = null)
            : base(parent, foreignKey ?? typeof(TRelated).Name + "Id", ownerKey, connectionFactory ?? ModelBase.ConnectionFactory)
        {
        }

        /// <summary>
        /// Get the foreign key value from parent (child model)
        /// </summary>
        protected object GetForeignKeyValue()
        {
            var parentMetadata = ModelBase.GetMetadata<TParent>();
            var prop = parentMetadata.Properties.FirstOrDefault(p =>
                p.ColumnName.Equals(_foreignKey, StringComparison.OrdinalIgnoreCase) ||
                p.PropertyName.Equals(_foreignKey, StringComparison.OrdinalIgnoreCase));

            return prop?.PropertyInfo.GetValue(_parent);
        }

        /// <summary>
        /// Get the related (owner) model
        /// </summary>
        public override object Get()
        {
            return GetRelated();
        }

        /// <summary>
        /// Get the related (owner) model (typed)
        /// </summary>
        public TRelated GetRelated()
        {
            var foreignValue = GetForeignKeyValue();
            if (foreignValue == null) return null;

            using (var db = _connectionFactory())
            {
                var sql = $"SELECT * FROM {_relatedMetadata.TableName} WHERE {_localKey} = @pk";
                db.ParametersAdd("@pk", foreignValue);

                var dt = db.RunDataTable(sql);
                if (dt.Rows.Count == 0) return null;

                return MapToModel(dt.Rows[0]);
            }
        }

        /// <summary>
        /// Associate with an owner model
        /// </summary>
        public void Associate(TRelated owner)
        {
            var parentMetadata = ModelBase.GetMetadata<TParent>();
            var fkProp = parentMetadata.Properties.FirstOrDefault(p =>
                p.ColumnName.Equals(_foreignKey, StringComparison.OrdinalIgnoreCase));

            if (fkProp != null)
            {
                var ownerPkProp = _relatedMetadata.Properties.FirstOrDefault(p =>
                    p.ColumnName.Equals(_localKey, StringComparison.OrdinalIgnoreCase));

                if (ownerPkProp != null)
                {
                    var ownerPkValue = ownerPkProp.PropertyInfo.GetValue(owner);
                    fkProp.PropertyInfo.SetValue(_parent, ownerPkValue);
                    _parent.Save();
                }
            }
        }

        /// <summary>
        /// Dissociate from owner
        /// </summary>
        public void Dissociate()
        {
            var parentMetadata = ModelBase.GetMetadata<TParent>();
            var fkProp = parentMetadata.Properties.FirstOrDefault(p =>
                p.ColumnName.Equals(_foreignKey, StringComparison.OrdinalIgnoreCase));

            if (fkProp != null)
            {
                fkProp.PropertyInfo.SetValue(_parent, null);
                _parent.Save();
            }
        }
    }

    /// <summary>
    /// BelongsToMany relationship (N:M) - Many to Many
    /// </summary>
    public class BelongsToMany<TParent, TRelated> : Relationship<TParent, TRelated>
        where TParent : Model<TParent>, new()
        where TRelated : Model<TRelated>, new()
    {
        private readonly string _pivotTable;
        private readonly string _parentKey;
        private readonly string _relatedKey;

        public BelongsToMany(
            TParent parent,
            string pivotTable = null,
            string parentKey = null,
            string relatedKey = null,
            string localKey = "Id",
            Func<DbCommandBase> connectionFactory = null)
            : base(parent, null, localKey, connectionFactory ?? ModelBase.ConnectionFactory)
        {
            var parentName = typeof(TParent).Name;
            var relatedName = typeof(TRelated).Name;

            // Default pivot table name: alphabetically ordered (e.g., "role_user")
            if (string.IsNullOrEmpty(pivotTable))
            {
                var names = new[] { parentName.ToLower(), relatedName.ToLower() };
                Array.Sort(names);
                _pivotTable = string.Join("_", names);
            }
            else
            {
                _pivotTable = pivotTable;
            }

            _parentKey = parentKey ?? parentName + "Id";
            _relatedKey = relatedKey ?? relatedName + "Id";
        }

        /// <summary>
        /// Get all related models
        /// </summary>
        public override object Get()
        {
            return GetRelated();
        }

        /// <summary>
        /// Get all related models (typed)
        /// </summary>
        public List<TRelated> GetRelated()
        {
            var localValue = GetLocalKeyValue();
            if (localValue == null) return new List<TRelated>();

            using (var db = _connectionFactory())
            {
                var sql = $@"
                    SELECT r.* FROM {_relatedMetadata.TableName} r
                    INNER JOIN {_pivotTable} p ON r.{_localKey} = p.{_relatedKey}
                    WHERE p.{_parentKey} = @pk";

                if (_relatedMetadata.HasSoftDelete)
                    sql += $" AND r.{_relatedMetadata.SoftDeleteColumn} IS NULL";

                db.ParametersAdd("@pk", localValue);

                var dt = db.RunDataTable(sql);
                return MapToModels(dt);
            }
        }

        /// <summary>
        /// Attach a related model (add to pivot table)
        /// </summary>
        public void Attach(object relatedId, Dictionary<string, object> pivotData = null)
        {
            var localValue = GetLocalKeyValue();
            if (localValue == null) return;

            using (var db = _connectionFactory())
            {
                var columns = new List<string> { _parentKey, _relatedKey };
                var values = new List<string> { "@parent", "@related" };

                db.ParametersAdd("@parent", localValue);
                db.ParametersAdd("@related", relatedId);

                if (pivotData != null)
                {
                    int i = 0;
                    foreach (var kvp in pivotData)
                    {
                        columns.Add(kvp.Key);
                        values.Add($"@p{i}");
                        db.ParametersAdd($"@p{i}", kvp.Value);
                        i++;
                    }
                }

                var sql = $"INSERT INTO {_pivotTable} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", values)})";
                db.RunExecute(sql);
            }
        }

        /// <summary>
        /// Attach multiple related models
        /// </summary>
        public void Attach(IEnumerable<object> relatedIds)
        {
            foreach (var id in relatedIds)
            {
                Attach(id);
            }
        }

        /// <summary>
        /// Detach a related model (remove from pivot table)
        /// </summary>
        public void Detach(object relatedId)
        {
            var localValue = GetLocalKeyValue();
            if (localValue == null) return;

            using (var db = _connectionFactory())
            {
                var sql = $"DELETE FROM {_pivotTable} WHERE {_parentKey} = @parent AND {_relatedKey} = @related";
                db.ParametersAdd("@parent", localValue);
                db.ParametersAdd("@related", relatedId);
                db.RunExecute(sql);
            }
        }

        /// <summary>
        /// Detach multiple related models
        /// </summary>
        public void Detach(IEnumerable<object> relatedIds)
        {
            foreach (var id in relatedIds)
            {
                Detach(id);
            }
        }

        /// <summary>
        /// Detach all related models
        /// </summary>
        public void DetachAll()
        {
            var localValue = GetLocalKeyValue();
            if (localValue == null) return;

            using (var db = _connectionFactory())
            {
                var sql = $"DELETE FROM {_pivotTable} WHERE {_parentKey} = @parent";
                db.ParametersAdd("@parent", localValue);
                db.RunExecute(sql);
            }
        }

        /// <summary>
        /// Sync related models (detach all, then attach given ids)
        /// </summary>
        public void Sync(IEnumerable<object> relatedIds)
        {
            DetachAll();
            Attach(relatedIds);
        }

        /// <summary>
        /// Toggle attachment (attach if not attached, detach if attached)
        /// </summary>
        public void Toggle(object relatedId)
        {
            if (Contains(relatedId))
                Detach(relatedId);
            else
                Attach(relatedId);
        }

        /// <summary>
        /// Check if related model is attached
        /// </summary>
        public bool Contains(object relatedId)
        {
            var localValue = GetLocalKeyValue();
            if (localValue == null) return false;

            using (var db = _connectionFactory())
            {
                var sql = $"SELECT COUNT(*) FROM {_pivotTable} WHERE {_parentKey} = @parent AND {_relatedKey} = @related";
                db.ParametersAdd("@parent", localValue);
                db.ParametersAdd("@related", relatedId);
                return db.RunToInt32Scaler(sql) > 0;
            }
        }

        /// <summary>
        /// Count related models
        /// </summary>
        public int Count()
        {
            var localValue = GetLocalKeyValue();
            if (localValue == null) return 0;

            using (var db = _connectionFactory())
            {
                var sql = $"SELECT COUNT(*) FROM {_pivotTable} WHERE {_parentKey} = @parent";
                db.ParametersAdd("@parent", localValue);
                return db.RunToInt32Scaler(sql);
            }
        }
    }
}
