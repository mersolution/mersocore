using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using mersolutionCore.Command.Abstractions;

namespace mersolutionCore.ORM.Relationships
{
    internal static class PivotNames
    {
        /// <summary>Alias prefix of pivot columns in BelongsToMany results (read back with model.GetPivot())</summary>
        internal const string Prefix = "pivot_";
    }

    /// <summary>
    /// Key columns of relations. "OrderId, LineNo" is a two-column key. When a relation keeps the default "Id"
    /// and the model has no Id property, the model's own primary key is used (several columns for a composite
    /// key); the foreign key then defaults to the same column names.
    /// </summary>
    internal static class RelationKeys
    {
        internal static string[] Split(string? columns)
        {
            return (columns ?? string.Empty).Split(',').Select(c => c.Trim()).Where(c => c.Length > 0).ToArray();
        }

        // Key columns on the model that holds the referenced key (parent of HasOne / HasMany, owner of BelongsTo)
        internal static string[] KeyColumns(ModelMetadata model, string? key)
        {
            var columns = Split(key);
            var defaultKey = columns.Length == 0 || (columns.Length == 1 && columns[0] == "Id" && ModelBase.FindProperty(model, "Id") == null);
            if (defaultKey && model.KeyProperties.Count > 0)
                return model.KeyProperties.Select(k => k.ColumnName).ToArray();
            return ColumnNames(model, columns.Length == 0 ? new[] { "Id" } : columns);
        }

        // Foreign key columns: the given ones (as many as the key has), else {Name}Id for a one-column key or
        // the key's own column names for a composite key
        internal static string[] ForeignColumns(ModelMetadata model, string? foreignKey, string[] keyColumns, string singleDefault, string relation)
        {
            if (string.IsNullOrWhiteSpace(foreignKey))
                return ColumnNames(model, keyColumns.Length > 1 ? keyColumns : new[] { singleDefault });

            var columns = Split(foreignKey);
            if (columns.Length != keyColumns.Length)
                throw new ArgumentException(
                    $"{relation}: the foreign key ({string.Join(", ", columns)}) and the key ({string.Join(", ", keyColumns)}) need the same number of columns.");
            return ColumnNames(model, columns);
        }

        // Property names → column names ([Column("...")]); unknown names stay as given
        private static string[] ColumnNames(ModelMetadata model, string[] names)
        {
            return names.Select(n => ModelBase.FindProperty(model, n)?.ColumnName ?? n).ToArray();
        }

        // Values of the columns on a model (null for a missing / empty one)
        internal static object?[] Values(object model, ModelMetadata metadata, string[] columns)
        {
            return columns.Select(c => ModelBase.FindProperty(metadata, c)?.GetValue(model)).ToArray();
        }

        internal static bool Complete(object?[] values) => values.All(v => v != null);

        // Dictionary key of a key value set (null when one of the values is empty)
        internal static string? KeyOf(object?[] values)
        {
            if (values.Length == 1)
                return ModelMapper.KeyOf(values[0]);

            var parts = new string[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                var part = ModelMapper.KeyOf(values[i]);
                if (part == null) return null;
                parts[i] = part;
            }
            return string.Join("\u001f", parts);
        }

        // "a = @fk" for one column, "a = @fk0 AND b = @fk1" for several (parameters added to db)
        internal static string Match(DbCommandBase db, string[] columns, object?[] values, string param, string? alias = null)
        {
            var prefix = string.IsNullOrEmpty(alias) ? string.Empty : alias + ".";
            if (columns.Length == 1)
            {
                db.ParametersAdd(param, values[0]);
                return $"{prefix}{SqlDialect.Column(db.ProviderType, columns[0])} = {param}";
            }

            var parts = new List<string>(columns.Length);
            for (int i = 0; i < columns.Length; i++)
            {
                var name = param + i.ToString(CultureInfo.InvariantCulture);
                db.ParametersAdd(name, values[i]);
                parts.Add($"{prefix}{SqlDialect.Column(db.ProviderType, columns[i])} = {name}");
            }
            return string.Join(" AND ", parts);
        }

        // Set the columns on a model; false when one of them is not a property of the model
        internal static bool Assign(object model, ModelMetadata metadata, string[] columns, object?[] values)
        {
            var props = columns.Select(c => ModelBase.FindProperty(metadata, c)).ToList();
            if (props.Any(p => p == null))
                return false;
            for (int i = 0; i < props.Count; i++)
            {
                if (values[i] == null)
                    props[i]!.PropertyInfo.SetValue(model, null);
                else
                    props[i]!.Assign(model, values[i]);
            }
            return true;
        }

        internal static Task Save(ModelBase model, bool useAsync, CancellationToken ct)
        {
            return model.SaveModel(useAsync, ct);
        }
    }

    /// <summary>
    /// Base class for all relationships. Every database method has an async counterpart (…Async).
    /// </summary>
    public abstract class Relationship<TParent, TRelated>
        where TParent : Model<TParent>, new()
        where TRelated : Model<TRelated>, new()
    {
        protected readonly TParent _parent;
        protected readonly string _foreignKey;
        protected readonly string _localKey;
        protected readonly Func<DbCommandBase> _connectionFactory;

        /// <summary>Connection for reads (a read replica when one is registered, see ReadReplicas)</summary>
        protected readonly Func<DbCommandBase> _readFactory;
        protected readonly ModelMetadata _relatedMetadata;
        protected readonly ModelMetadata _parentMetadata;

        /// <summary>Foreign key columns (several for a composite key)</summary>
        protected string[] _foreignKeys;

        /// <summary>Key columns the foreign key points to (several for a composite key)</summary>
        protected string[] _localKeys;

        /// <param name="connectionFactory">null = the related model's connection ([Connection] attribute, scoped or default)</param>
        /// <param name="parent">Model the relation starts from</param>
        /// <param name="foreignKey">Foreign key column (null for BelongsToMany)</param>
        /// <param name="localKey">Key column of the parent</param>
        protected Relationship(TParent parent, string? foreignKey, string localKey, Func<DbCommandBase>? connectionFactory)
        {
            _parent = parent;
            _foreignKey = foreignKey ?? string.Empty;
            _localKey = localKey;
            _relatedMetadata = ModelBase.GetMetadata<TRelated>();
            _parentMetadata = ModelBase.GetMetadata<TParent>();
            _connectionFactory = connectionFactory ?? (() => ModelBase.CreateCommand(_relatedMetadata.ConnectionName));
            _readFactory = connectionFactory ?? (() => ModelBase.CreateReadCommand(_relatedMetadata.ConnectionName));
            _foreignKeys = new[] { _foreignKey };
            _localKeys = new[] { localKey };
        }

        /// <summary>
        /// Get the related model(s)
        /// </summary>
        public abstract object? Get();

        /// <summary>
        /// Get the related model(s) (async)
        /// </summary>
        public virtual Task<object?> GetAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Get());
        }

        /// <summary>
        /// HasOne / HasMany: the key is on the parent (its primary key by default, composite too), the foreign
        /// key on the related model
        /// </summary>
        protected void UseParentKey(string? foreignKey, string defaultForeignKey)
        {
            _localKeys = RelationKeys.KeyColumns(_parentMetadata, _localKey);
            _foreignKeys = RelationKeys.ForeignColumns(_relatedMetadata, foreignKey, _localKeys, defaultForeignKey, GetType().Name.Split('`')[0]);
        }

        /// <summary>
        /// Values of <see cref="_localKeys"/> on the parent
        /// </summary>
        protected object?[] GetLocalKeyValues()
        {
            return RelationKeys.Values(_parent, _parentMetadata, _localKeys);
        }

        /// <summary>
        /// "fk = @p" (or "fk1 = @p0 AND fk2 = @p1") for the related table, with the parameters added
        /// </summary>
        protected static string Match(DbCommandBase db, string[] columns, object?[] values, string param, string? alias = null)
        {
            return RelationKeys.Match(db, columns, values, param, alias);
        }

        /// <summary>
        /// Get the local key value from parent (relations that need a single-column key)
        /// </summary>
        protected object? GetLocalKeyValue()
        {
            var columns = RelationKeys.KeyColumns(_parentMetadata, _localKey);
            if (columns.Length != 1)
                throw new NotSupportedException(
                    $"{GetType().Name.Split('`')[0]} needs a single-column key on {typeof(TParent).Name}; pass localKey (a unique column). " +
                    "Composite keys work with HasOne, HasMany and BelongsTo.");

            return ModelBase.FindProperty(_parentMetadata, columns[0])?.GetValue(_parent);
        }

        /// <summary>
        /// Quoted identifier for the provider of <paramref name="db"/>
        /// </summary>
        protected static string Q(DbCommandBase db, string name) => SqlDialect.Column(db.ProviderType, name);

        /// <summary>
        /// Quoted table name for the provider of <paramref name="db"/>
        /// </summary>
        protected static string QTable(DbCommandBase db, string name) => SqlDialect.Table(db.ProviderType, name);

        /// <summary>
        /// " AND alias.DeletedAt IS NULL" (column not quoted) when the related model uses soft delete
        /// </summary>
        protected string SoftDeleteFilter(string? alias = null)
        {
            if (!_relatedMetadata.HasSoftDelete)
                return string.Empty;

            var prefix = string.IsNullOrEmpty(alias) ? string.Empty : alias + ".";
            return $" AND {prefix}{_relatedMetadata.SoftDeleteColumn} IS NULL";
        }

        /// <summary>
        /// " AND alias.DeletedAt IS NULL" with the column quoted for the provider of <paramref name="db"/>
        /// </summary>
        protected string SoftDeleteFilter(DbCommandBase db, string? alias = null)
        {
            if (!_relatedMetadata.HasSoftDelete)
                return string.Empty;

            var prefix = string.IsNullOrEmpty(alias) ? string.Empty : alias + ".";
            return $" AND {prefix}{Q(db, _relatedMetadata.SoftDeleteColumn)} IS NULL";
        }

        /// <summary>
        /// New related model: Fill(data) (mass assignment rules apply), then the given key columns, then Save()
        /// </summary>
        protected static TRelated CreateRelated(Dictionary<string, object?>? data, params object?[] columnValuePairs)
        {
            return DbRun.Sync(CreateRelatedCore(data, columnValuePairs, false, default));
        }

        /// <summary>
        /// <see cref="CreateRelated"/> for sync and async callers
        /// </summary>
        protected static async Task<TRelated> CreateRelatedCore(Dictionary<string, object?>? data, object?[] columnValuePairs, bool useAsync, CancellationToken ct)
        {
            var model = new TRelated();
            model.Fill(data);
            for (int i = 0; i + 1 < columnValuePairs.Length; i += 2)
                model.SetProperty((string)columnValuePairs[i]!, columnValuePairs[i + 1]);
            await RelationKeys.Save(model, useAsync, ct).ConfigureAwait(false);
            return model;
        }

        /// <summary>
        /// column1, value1, column2, value2 ... for <see cref="CreateRelated"/>
        /// </summary>
        protected static object?[] ForeignKeyPairs(string[] columns, object?[] values)
        {
            var pairs = new object?[columns.Length * 2];
            for (int i = 0; i < columns.Length; i++)
            {
                pairs[i * 2] = columns[i];
                pairs[i * 2 + 1] = values[i];
            }
            return pairs;
        }

        /// <summary>
        /// Related models of a query, filled straight from the data reader
        /// </summary>
        protected Task<List<TRelated>> LoadCore(DbCommandBase db, string sql, bool useAsync, CancellationToken ct)
        {
            return DbRun.Models(db, sql, _relatedMetadata, () => new TRelated(), useAsync, ct);
        }

        /// <summary>
        /// Map DataRow to model
        /// </summary>
        protected TRelated MapToModel(DataRow row)
        {
            var model = new TRelated();
            ModelMapper.MapRow(row, model, _relatedMetadata);
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
        /// <param name="parent">Parent model</param>
        /// <param name="foreignKey">Foreign key column(s) on the related table, "A, B" for a composite key
        /// (default {Parent}Id, or the parent's key column names when its key is composite)</param>
        /// <param name="localKey">Parent key column(s) (default: the parent's primary key)</param>
        /// <param name="connectionFactory">null = the related model's connection</param>
        public HasOne(TParent parent, string? foreignKey = null, string localKey = "Id", Func<DbCommandBase>? connectionFactory = null)
            : base(parent, foreignKey ?? typeof(TParent).Name + "Id", localKey, connectionFactory)
        {
            UseParentKey(foreignKey, typeof(TParent).Name + "Id");
        }

        /// <summary>
        /// Get the related model
        /// </summary>
        public override object? Get() => GetRelated();

        /// <inheritdoc />
        public override async Task<object?> GetAsync(CancellationToken cancellationToken = default)
            => await GetRelatedAsync(cancellationToken).ConfigureAwait(false);

        /// <summary>
        /// Get the related model (typed)
        /// </summary>
        public TRelated? GetRelated() => DbRun.Sync(GetRelatedCore(false, default));

        /// <summary>
        /// Get the related model (typed, async)
        /// </summary>
        public Task<TRelated?> GetRelatedAsync(CancellationToken cancellationToken = default) => GetRelatedCore(true, cancellationToken);

        private async Task<TRelated?> GetRelatedCore(bool useAsync, CancellationToken ct)
        {
            var keys = GetLocalKeyValues();
            if (!RelationKeys.Complete(keys)) return null;

            using (var db = _readFactory())
            {
                var sql = $"SELECT * FROM {QTable(db, _relatedMetadata.TableName)} WHERE {Match(db, _foreignKeys, keys, "@fk")}{SoftDeleteFilter(db)}";
                return (await LoadCore(db, sql, useAsync, ct).ConfigureAwait(false)).FirstOrDefault();
            }
        }

        /// <summary>
        /// Create related model (the foreign key is always set, even when it is [Guarded])
        /// </summary>
        public TRelated Create(Dictionary<string, object?> data)
        {
            return CreateRelated(data, ForeignKeyPairs(_foreignKeys, GetLocalKeyValues()));
        }

        /// <summary>
        /// Create related model (async)
        /// </summary>
        public Task<TRelated> CreateAsync(Dictionary<string, object?> data, CancellationToken cancellationToken = default)
        {
            return CreateRelatedCore(data, ForeignKeyPairs(_foreignKeys, GetLocalKeyValues()), true, cancellationToken);
        }

        /// <summary>
        /// Associate an existing model
        /// </summary>
        public void Associate(TRelated related) => DbRun.Sync(AssociateCore(related, false, default));

        /// <summary>
        /// Associate an existing model (async)
        /// </summary>
        public Task AssociateAsync(TRelated related, CancellationToken cancellationToken = default) => AssociateCore(related, true, cancellationToken);

        private async Task<bool> AssociateCore(TRelated related, bool useAsync, CancellationToken ct)
        {
            if (RelationKeys.Assign(related, _relatedMetadata, _foreignKeys, GetLocalKeyValues()))
                await RelationKeys.Save(related, useAsync, ct).ConfigureAwait(false);
            return true;
        }

        /// <summary>
        /// Dissociate the related model
        /// </summary>
        public void Dissociate() => DbRun.Sync(DissociateCore(false, default));

        /// <summary>
        /// Dissociate the related model (async)
        /// </summary>
        public Task DissociateAsync(CancellationToken cancellationToken = default) => DissociateCore(true, cancellationToken);

        private async Task<bool> DissociateCore(bool useAsync, CancellationToken ct)
        {
            var related = await GetRelatedCore(useAsync, ct).ConfigureAwait(false);
            if (related != null && RelationKeys.Assign(related, _relatedMetadata, _foreignKeys, new object?[_foreignKeys.Length]))
                await RelationKeys.Save(related, useAsync, ct).ConfigureAwait(false);
            return true;
        }
    }

    /// <summary>
    /// HasMany relationship (1:N)
    /// </summary>
    public class HasMany<TParent, TRelated> : Relationship<TParent, TRelated>
        where TParent : Model<TParent>, new()
        where TRelated : Model<TRelated>, new()
    {
        /// <param name="parent">Parent model</param>
        /// <param name="foreignKey">Foreign key column(s) on the related table, "A, B" for a composite key
        /// (default {Parent}Id, or the parent's key column names when its key is composite)</param>
        /// <param name="localKey">Parent key column(s) (default: the parent's primary key)</param>
        /// <param name="connectionFactory">null = the related model's connection</param>
        public HasMany(TParent parent, string? foreignKey = null, string localKey = "Id", Func<DbCommandBase>? connectionFactory = null)
            : base(parent, foreignKey ?? typeof(TParent).Name + "Id", localKey, connectionFactory)
        {
            UseParentKey(foreignKey, typeof(TParent).Name + "Id");
        }

        /// <summary>
        /// Get all related models
        /// </summary>
        public override object? Get() => GetRelated();

        /// <inheritdoc />
        public override async Task<object?> GetAsync(CancellationToken cancellationToken = default)
            => await GetRelatedAsync(cancellationToken).ConfigureAwait(false);

        /// <summary>
        /// Get all related models (typed)
        /// </summary>
        public List<TRelated> GetRelated() => DbRun.Sync(GetRelatedCore(false, default));

        /// <summary>
        /// Get all related models (typed, async)
        /// </summary>
        public Task<List<TRelated>> GetRelatedAsync(CancellationToken cancellationToken = default) => GetRelatedCore(true, cancellationToken);

        private async Task<List<TRelated>> GetRelatedCore(bool useAsync, CancellationToken ct)
        {
            var keys = GetLocalKeyValues();
            if (!RelationKeys.Complete(keys)) return new List<TRelated>();

            using (var db = _readFactory())
            {
                var sql = $"SELECT * FROM {QTable(db, _relatedMetadata.TableName)} WHERE {Match(db, _foreignKeys, keys, "@fk")}{SoftDeleteFilter(db)}";
                return await LoadCore(db, sql, useAsync, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Get related models with query builder
        /// </summary>
        public QueryBuilder<TRelated> Query()
        {
            var keys = GetLocalKeyValues();
            var query = Model<TRelated>.Where(_foreignKeys[0], keys[0]);
            for (int i = 1; i < _foreignKeys.Length; i++)
                query.Where(_foreignKeys[i], keys[i]);
            return query;
        }

        /// <summary>
        /// Count related models
        /// </summary>
        public int Count() => DbRun.Sync(CountCore(false, default));

        /// <summary>
        /// Count related models (async)
        /// </summary>
        public Task<int> CountAsync(CancellationToken cancellationToken = default) => CountCore(true, cancellationToken);

        private async Task<int> CountCore(bool useAsync, CancellationToken ct)
        {
            var keys = GetLocalKeyValues();
            if (!RelationKeys.Complete(keys)) return 0;

            using (var db = _readFactory())
            {
                var sql = $"SELECT COUNT(*) FROM {QTable(db, _relatedMetadata.TableName)} WHERE {Match(db, _foreignKeys, keys, "@fk")}{SoftDeleteFilter(db)}";
                return await DbRun.Int32(db, sql, useAsync, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Create a related model (the foreign key is always set, even when it is [Guarded])
        /// </summary>
        public TRelated Create(Dictionary<string, object?> data)
        {
            return CreateRelated(data, ForeignKeyPairs(_foreignKeys, GetLocalKeyValues()));
        }

        /// <summary>
        /// Create a related model (async)
        /// </summary>
        public Task<TRelated> CreateAsync(Dictionary<string, object?> data, CancellationToken cancellationToken = default)
        {
            return CreateRelatedCore(data, ForeignKeyPairs(_foreignKeys, GetLocalKeyValues()), true, cancellationToken);
        }

        /// <summary>
        /// Create multiple related models
        /// </summary>
        public List<TRelated> CreateMany(IEnumerable<Dictionary<string, object?>> dataList)
        {
            var results = new List<TRelated>();
            foreach (var data in dataList)
            {
                results.Add(Create(data));
            }
            return results;
        }

        /// <summary>
        /// Create multiple related models (async)
        /// </summary>
        public async Task<List<TRelated>> CreateManyAsync(IEnumerable<Dictionary<string, object?>> dataList, CancellationToken cancellationToken = default)
        {
            var results = new List<TRelated>();
            foreach (var data in dataList)
            {
                results.Add(await CreateAsync(data, cancellationToken).ConfigureAwait(false));
            }
            return results;
        }

        /// <summary>
        /// Delete all related models
        /// </summary>
        public int DeleteAll() => DbRun.Sync(DeleteAllCore(false, default));

        /// <summary>
        /// Delete all related models (async)
        /// </summary>
        public Task<int> DeleteAllAsync(CancellationToken cancellationToken = default) => DeleteAllCore(true, cancellationToken);

        private async Task<int> DeleteAllCore(bool useAsync, CancellationToken ct)
        {
            var keys = GetLocalKeyValues();
            if (!RelationKeys.Complete(keys)) return 0;

            using (var db = _connectionFactory())
            {
                var table = QTable(db, _relatedMetadata.TableName);
                string sql;
                if (_relatedMetadata.HasSoftDelete)
                {
                    sql = $"UPDATE {table} SET {Q(db, _relatedMetadata.SoftDeleteColumn)} = @now WHERE {Match(db, _foreignKeys, keys, "@fk")}{SoftDeleteFilter(db)}";
                    db.ParametersAdd("@now", DateTime.UtcNow);
                }
                else
                {
                    sql = $"DELETE FROM {table} WHERE {Match(db, _foreignKeys, keys, "@fk")}";
                }

                return await DbRun.NonQuery(db, sql, useAsync, ct).ConfigureAwait(false);
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
        /// <param name="parent">Child model (holds the foreign key)</param>
        /// <param name="foreignKey">Foreign key column(s) on this model, "A, B" for a composite key
        /// (default {Owner}Id, or the owner's key column names when its key is composite)</param>
        /// <param name="ownerKey">Owner key column(s) (default: the owner's primary key)</param>
        /// <param name="connectionFactory">null = the owner model's connection</param>
        public BelongsTo(TParent parent, string? foreignKey = null, string ownerKey = "Id", Func<DbCommandBase>? connectionFactory = null)
            : base(parent, foreignKey ?? typeof(TRelated).Name + "Id", ownerKey, connectionFactory)
        {
            // Here the key is on the related (owner) model and the foreign key on this model
            _localKeys = RelationKeys.KeyColumns(_relatedMetadata, ownerKey);
            _foreignKeys = RelationKeys.ForeignColumns(_parentMetadata, foreignKey, _localKeys, typeof(TRelated).Name + "Id", "BelongsTo");
        }

        /// <summary>
        /// Get the foreign key value from parent (child model); the first column of a composite foreign key
        /// </summary>
        protected object? GetForeignKeyValue()
        {
            return GetForeignKeyValues()[0];
        }

        /// <summary>
        /// Foreign key values of the child model (one per column)
        /// </summary>
        protected object?[] GetForeignKeyValues()
        {
            return RelationKeys.Values(_parent, _parentMetadata, _foreignKeys);
        }

        /// <summary>
        /// Get the related (owner) model
        /// </summary>
        public override object? Get() => GetRelated();

        /// <inheritdoc />
        public override async Task<object?> GetAsync(CancellationToken cancellationToken = default)
            => await GetRelatedAsync(cancellationToken).ConfigureAwait(false);

        /// <summary>
        /// Get the related (owner) model (typed)
        /// </summary>
        public TRelated? GetRelated() => DbRun.Sync(GetRelatedCore(false, default));

        /// <summary>
        /// Get the related (owner) model (typed, async)
        /// </summary>
        public Task<TRelated?> GetRelatedAsync(CancellationToken cancellationToken = default) => GetRelatedCore(true, cancellationToken);

        private async Task<TRelated?> GetRelatedCore(bool useAsync, CancellationToken ct)
        {
            var keys = GetForeignKeyValues();
            if (!RelationKeys.Complete(keys)) return null;

            using (var db = _readFactory())
            {
                var sql = $"SELECT * FROM {QTable(db, _relatedMetadata.TableName)} WHERE {Match(db, _localKeys, keys, "@pk")}{SoftDeleteFilter(db)}";
                return (await LoadCore(db, sql, useAsync, ct).ConfigureAwait(false)).FirstOrDefault();
            }
        }

        /// <summary>
        /// Associate with an owner model
        /// </summary>
        public void Associate(TRelated owner) => DbRun.Sync(AssociateCore(owner, false, default));

        /// <summary>
        /// Associate with an owner model (async)
        /// </summary>
        public Task AssociateAsync(TRelated owner, CancellationToken cancellationToken = default) => AssociateCore(owner, true, cancellationToken);

        private async Task<bool> AssociateCore(TRelated owner, bool useAsync, CancellationToken ct)
        {
            if (_localKeys.Any(k => ModelBase.FindProperty(_relatedMetadata, k) == null))
                return false;

            if (RelationKeys.Assign(_parent, _parentMetadata, _foreignKeys, RelationKeys.Values(owner, _relatedMetadata, _localKeys)))
                await RelationKeys.Save(_parent, useAsync, ct).ConfigureAwait(false);
            return true;
        }

        /// <summary>
        /// Dissociate from owner
        /// </summary>
        public void Dissociate() => DbRun.Sync(DissociateCore(false, default));

        /// <summary>
        /// Dissociate from owner (async)
        /// </summary>
        public Task DissociateAsync(CancellationToken cancellationToken = default) => DissociateCore(true, cancellationToken);

        private async Task<bool> DissociateCore(bool useAsync, CancellationToken ct)
        {
            if (RelationKeys.Assign(_parent, _parentMetadata, _foreignKeys, new object?[_foreignKeys.Length]))
                await RelationKeys.Save(_parent, useAsync, ct).ConfigureAwait(false);
            return true;
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
        private readonly List<string> _pivotColumns = new List<string>();
        private readonly List<KeyValuePair<string, object>> _pivotFilters = new List<KeyValuePair<string, object>>();

        public BelongsToMany(
            TParent parent,
            string? pivotTable = null,
            string? parentKey = null,
            string? relatedKey = null,
            string localKey = "Id",
            Func<DbCommandBase>? connectionFactory = null)
            : base(parent, null, localKey, connectionFactory)
        {
            var parentName = typeof(TParent).Name;
            var relatedName = typeof(TRelated).Name;

            // Default pivot table name: alphabetically ordered (e.g., "role_user")
            if (string.IsNullOrEmpty(pivotTable))
            {
                var names = new[] { parentName.ToLowerInvariant(), relatedName.ToLowerInvariant() };
                Array.Sort(names, StringComparer.Ordinal);
                _pivotTable = string.Join("_", names);
            }
            else
            {
                _pivotTable = pivotTable!;
            }

            _parentKey = parentKey ?? parentName + "Id";
            _relatedKey = relatedKey ?? relatedName + "Id";
        }

        /// <summary>
        /// Get all related models
        /// </summary>
        public override object? Get() => GetRelated();

        /// <inheritdoc />
        public override async Task<object?> GetAsync(CancellationToken cancellationToken = default)
            => await GetRelatedAsync(cancellationToken).ConfigureAwait(false);

        /// <summary>
        /// Also read these pivot table columns; each related model returns them with <c>GetPivot()</c>
        /// (e.g. user.Roles().WithPivot("ExpiresAt").GetRelated()[0].GetPivot()["ExpiresAt"])
        /// </summary>
        public BelongsToMany<TParent, TRelated> WithPivot(params string[] columns)
        {
            foreach (var column in columns ?? new string[0])
            {
                if (!_pivotColumns.Contains(column, StringComparer.OrdinalIgnoreCase))
                    _pivotColumns.Add(column);
            }
            return this;
        }

        /// <summary>
        /// Only related models whose pivot row has this value (pivot.column = value)
        /// </summary>
        public BelongsToMany<TParent, TRelated> WherePivot(string column, object value)
        {
            _pivotFilters.Add(new KeyValuePair<string, object>(column, value));
            return this;
        }

        /// <summary>
        /// Get all related models (typed); pivot columns from <see cref="WithPivot"/> come back via GetPivot()
        /// </summary>
        public List<TRelated> GetRelated() => DbRun.Sync(GetRelatedCore(false, default));

        /// <summary>
        /// Get all related models (typed, async)
        /// </summary>
        public Task<List<TRelated>> GetRelatedAsync(CancellationToken cancellationToken = default) => GetRelatedCore(true, cancellationToken);

        private async Task<List<TRelated>> GetRelatedCore(bool useAsync, CancellationToken ct)
        {
            var localValue = GetLocalKeyValue();
            if (localValue == null) return new List<TRelated>();

            using (var db = _readFactory())
            {
                // Join on the related model's own primary key (localKey belongs to the parent)
                if (_relatedMetadata.HasCompositeKey)
                    throw new NotSupportedException(
                        $"BelongsToMany needs a single-column key on {typeof(TRelated).Name}; use a pivot model with HasMany / BelongsTo for composite keys.");
                var relatedPk = _relatedMetadata.PrimaryKeyColumn ?? _localKey;
                var pivotSelect = string.Concat(_pivotColumns.Select(c => $", p.{Q(db, c)} AS {Q(db, PivotNames.Prefix + c)}"));
                var sql = $@"
                    SELECT r.*{pivotSelect} FROM {QTable(db, _relatedMetadata.TableName)} r
                    INNER JOIN {QTable(db, _pivotTable)} p ON r.{Q(db, relatedPk)} = p.{Q(db, _relatedKey)}
                    WHERE p.{Q(db, _parentKey)} = @pk{PivotFilterSql(db)}{SoftDeleteFilter(db, "r")}";

                db.ParametersAdd("@pk", localValue);
                return await LoadCore(db, sql, useAsync, ct).ConfigureAwait(false);
            }
        }

        private string PivotFilterSql(DbCommandBase db)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _pivotFilters.Count; i++)
            {
                var filter = _pivotFilters[i];
                if (filter.Value == null)
                {
                    sb.Append($" AND p.{Q(db, filter.Key)} IS NULL");
                    continue;
                }

                var name = "@wp" + i.ToString(CultureInfo.InvariantCulture);
                sb.Append($" AND p.{Q(db, filter.Key)} = {name}");
                db.ParametersAdd(name, filter.Value);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Change pivot columns of an attached model; returns the affected rows
        /// </summary>
        public int UpdateExistingPivot(object relatedId, Dictionary<string, object?> pivotData)
            => DbRun.Sync(UpdateExistingPivotCore(relatedId, pivotData, false, default));

        /// <summary>
        /// Change pivot columns of an attached model (async)
        /// </summary>
        public Task<int> UpdateExistingPivotAsync(object relatedId, Dictionary<string, object?> pivotData, CancellationToken cancellationToken = default)
            => UpdateExistingPivotCore(relatedId, pivotData, true, cancellationToken);

        private async Task<int> UpdateExistingPivotCore(object relatedId, Dictionary<string, object?> pivotData, bool useAsync, CancellationToken ct)
        {
            var localValue = GetLocalKeyValue();
            if (localValue == null || pivotData == null || pivotData.Count == 0) return 0;

            using (var db = _connectionFactory())
            {
                var sets = new List<string>();
                int i = 0;
                foreach (var kvp in pivotData)
                {
                    var name = "@v" + (i++).ToString(CultureInfo.InvariantCulture);
                    sets.Add($"{Q(db, kvp.Key)} = {name}");
                    db.ParametersAdd(name, kvp.Value);
                }

                db.ParametersAdd("@parent", localValue);
                db.ParametersAdd("@related", relatedId);
                var sql = $"UPDATE {QTable(db, _pivotTable)} SET {string.Join(", ", sets)} WHERE {Q(db, _parentKey)} = @parent AND {Q(db, _relatedKey)} = @related";
                return await DbRun.NonQuery(db, sql, useAsync, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Attach a related model (add to pivot table) with optional extra pivot columns
        /// </summary>
        public void Attach(object relatedId, Dictionary<string, object?>? pivotData = null)
            => DbRun.Sync(AttachCore(relatedId, pivotData, false, default));

        /// <summary>
        /// Attach a related model with optional extra pivot columns (async)
        /// </summary>
        public Task AttachAsync(object relatedId, Dictionary<string, object?>? pivotData = null, CancellationToken cancellationToken = default)
            => AttachCore(relatedId, pivotData, true, cancellationToken);

        private async Task<bool> AttachCore(object relatedId, Dictionary<string, object?>? pivotData, bool useAsync, CancellationToken ct)
        {
            var localValue = GetLocalKeyValue();
            if (localValue == null) return false;

            using (var db = _connectionFactory())
            {
                var columns = new List<string> { Q(db, _parentKey), Q(db, _relatedKey) };
                var values = new List<string> { "@parent", "@related" };

                db.ParametersAdd("@parent", localValue);
                db.ParametersAdd("@related", relatedId);

                if (pivotData != null)
                {
                    int i = 0;
                    foreach (var kvp in pivotData)
                    {
                        columns.Add(Q(db, kvp.Key));
                        values.Add($"@p{i}");
                        db.ParametersAdd($"@p{i}", kvp.Value);
                        i++;
                    }
                }

                var sql = $"INSERT INTO {QTable(db, _pivotTable)} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", values)})";
                await DbRun.NonQuery(db, sql, useAsync, ct).ConfigureAwait(false);
                return true;
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
        /// Attach multiple related models (async)
        /// </summary>
        public async Task AttachAsync(IEnumerable<object> relatedIds, CancellationToken cancellationToken = default)
        {
            foreach (var id in relatedIds)
            {
                await AttachAsync(id, null, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Attach multiple related models with the same extra pivot columns
        /// </summary>
        public void Attach(IEnumerable<object> relatedIds, Dictionary<string, object?> pivotData)
        {
            foreach (var id in relatedIds)
            {
                Attach(id, pivotData);
            }
        }

        /// <summary>
        /// Attach multiple related models with the same extra pivot columns (async)
        /// </summary>
        public async Task AttachAsync(IEnumerable<object> relatedIds, Dictionary<string, object?> pivotData, CancellationToken cancellationToken = default)
        {
            foreach (var id in relatedIds)
            {
                await AttachAsync(id, pivotData, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Detach a related model (remove from pivot table)
        /// </summary>
        public void Detach(object relatedId) => DbRun.Sync(DetachCore(relatedId, false, default));

        /// <summary>
        /// Detach a related model (async)
        /// </summary>
        public Task DetachAsync(object relatedId, CancellationToken cancellationToken = default) => DetachCore(relatedId, true, cancellationToken);

        private async Task<bool> DetachCore(object relatedId, bool useAsync, CancellationToken ct)
        {
            var localValue = GetLocalKeyValue();
            if (localValue == null) return false;

            using (var db = _connectionFactory())
            {
                var sql = $"DELETE FROM {QTable(db, _pivotTable)} WHERE {Q(db, _parentKey)} = @parent AND {Q(db, _relatedKey)} = @related";
                db.ParametersAdd("@parent", localValue);
                db.ParametersAdd("@related", relatedId);
                await DbRun.NonQuery(db, sql, useAsync, ct).ConfigureAwait(false);
                return true;
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
        /// Detach multiple related models (async)
        /// </summary>
        public async Task DetachAsync(IEnumerable<object> relatedIds, CancellationToken cancellationToken = default)
        {
            foreach (var id in relatedIds)
            {
                await DetachAsync(id, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Detach all related models
        /// </summary>
        public void DetachAll() => DbRun.Sync(DetachAllCore(false, default));

        /// <summary>
        /// Detach all related models (async)
        /// </summary>
        public Task DetachAllAsync(CancellationToken cancellationToken = default) => DetachAllCore(true, cancellationToken);

        private async Task<bool> DetachAllCore(bool useAsync, CancellationToken ct)
        {
            var localValue = GetLocalKeyValue();
            if (localValue == null) return false;

            using (var db = _connectionFactory())
            {
                var sql = $"DELETE FROM {QTable(db, _pivotTable)} WHERE {Q(db, _parentKey)} = @parent";
                db.ParametersAdd("@parent", localValue);
                await DbRun.NonQuery(db, sql, useAsync, ct).ConfigureAwait(false);
                return true;
            }
        }

        /// <summary>
        /// Sync related models (detach all, then attach given ids) in one transaction
        /// </summary>
        public void Sync(IEnumerable<object> relatedIds)
        {
            var ids = relatedIds?.ToList() ?? new List<object>();
            DbRun.Sync(SyncCore(ids.Select(id => new KeyValuePair<object, Dictionary<string, object?>?>(id, null)).ToList(), false, default));
        }

        /// <summary>
        /// Sync related models (async, one transaction)
        /// </summary>
        public Task SyncAsync(IEnumerable<object> relatedIds, CancellationToken cancellationToken = default)
        {
            var ids = relatedIds?.ToList() ?? new List<object>();
            return SyncCore(ids.Select(id => new KeyValuePair<object, Dictionary<string, object?>?>(id, null)).ToList(), true, cancellationToken);
        }

        /// <summary>
        /// Sync related models with their pivot columns (id → extra columns) in one transaction
        /// </summary>
        public void Sync(IDictionary<object, Dictionary<string, object?>> relatedIdsWithPivotData)
        {
            DbRun.Sync(SyncCore(PivotItems(relatedIdsWithPivotData), false, default));
        }

        /// <summary>
        /// Sync related models with their pivot columns (async, one transaction)
        /// </summary>
        public Task SyncAsync(IDictionary<object, Dictionary<string, object?>> relatedIdsWithPivotData, CancellationToken cancellationToken = default)
        {
            return SyncCore(PivotItems(relatedIdsWithPivotData), true, cancellationToken);
        }

        private static List<KeyValuePair<object, Dictionary<string, object?>?>> PivotItems(IDictionary<object, Dictionary<string, object?>>? items)
        {
            return items?.Select(i => new KeyValuePair<object, Dictionary<string, object?>?>(i.Key, i.Value)).ToList()
                   ?? new List<KeyValuePair<object, Dictionary<string, object?>?>>();
        }

        private Task<object?> SyncCore(List<KeyValuePair<object, Dictionary<string, object?>?>> items, bool useAsync, CancellationToken ct)
        {
            return MersoTransaction.RunCore<object?>(_relatedMetadata.ConnectionName, async () =>
            {
                await DetachAllCore(useAsync, ct).ConfigureAwait(false);
                foreach (var item in items)
                    await AttachCore(item.Key, item.Value, useAsync, ct).ConfigureAwait(false);
                return null;
            }, 1, useAsync, ct);
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
        /// Toggle attachment (async)
        /// </summary>
        public async Task ToggleAsync(object relatedId, CancellationToken cancellationToken = default)
        {
            if (await ContainsAsync(relatedId, cancellationToken).ConfigureAwait(false))
                await DetachAsync(relatedId, cancellationToken).ConfigureAwait(false);
            else
                await AttachAsync(relatedId, null, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Check if related model is attached
        /// </summary>
        public bool Contains(object relatedId) => DbRun.Sync(ContainsCore(relatedId, false, default));

        /// <summary>
        /// Check if related model is attached (async)
        /// </summary>
        public Task<bool> ContainsAsync(object relatedId, CancellationToken cancellationToken = default) => ContainsCore(relatedId, true, cancellationToken);

        private async Task<bool> ContainsCore(object relatedId, bool useAsync, CancellationToken ct)
        {
            var localValue = GetLocalKeyValue();
            if (localValue == null) return false;

            using (var db = _readFactory())
            {
                var sql = $"SELECT COUNT(*) FROM {QTable(db, _pivotTable)} WHERE {Q(db, _parentKey)} = @parent AND {Q(db, _relatedKey)} = @related";
                db.ParametersAdd("@parent", localValue);
                db.ParametersAdd("@related", relatedId);
                return await DbRun.Int32(db, sql, useAsync, ct).ConfigureAwait(false) > 0;
            }
        }

        /// <summary>
        /// Count related models
        /// </summary>
        public int Count() => DbRun.Sync(CountCore(false, default));

        /// <summary>
        /// Count related models (async)
        /// </summary>
        public Task<int> CountAsync(CancellationToken cancellationToken = default) => CountCore(true, cancellationToken);

        private async Task<int> CountCore(bool useAsync, CancellationToken ct)
        {
            var localValue = GetLocalKeyValue();
            if (localValue == null) return 0;

            using (var db = _readFactory())
            {
                var sql = $"SELECT COUNT(*) FROM {QTable(db, _pivotTable)} WHERE {Q(db, _parentKey)} = @parent";
                db.ParametersAdd("@parent", localValue);
                return await DbRun.Int32(db, sql, useAsync, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Shared FROM / JOIN / WHERE of HasManyThrough and HasOneThrough
    /// </summary>
    internal static class ThroughSql
    {
        // FROM related r JOIN through t ON t.secondLocalKey = r.secondKey WHERE t.firstKey = @pk (+ soft delete of both)
        internal static string From(DbCommandBase db, ModelMetadata related, ModelMetadata through,
            string firstKey, string secondKey, string secondLocalKey)
        {
            var p = db.ProviderType;
            string Q(string name) => SqlDialect.Column(p, name);

            var sql = $"FROM {SqlDialect.Table(p, related.TableName)} r INNER JOIN {SqlDialect.Table(p, through.TableName)} t " +
                      $"ON t.{Q(secondLocalKey)} = r.{Q(secondKey)} WHERE t.{Q(firstKey)} = @pk";
            if (through.HasSoftDelete)
                sql += $" AND t.{Q(through.SoftDeleteColumn)} IS NULL";
            if (related.HasSoftDelete)
                sql += $" AND r.{Q(related.SoftDeleteColumn)} IS NULL";
            return sql;
        }
    }

    /// <summary>
    /// HasManyThrough: related rows reached through an intermediate table
    /// (Country → Users → Posts: users.CountryId = country.Id, posts.UserId = user.Id)
    /// </summary>
    public class HasManyThrough<TParent, TThrough, TRelated> : Relationship<TParent, TRelated>
        where TParent : Model<TParent>, new()
        where TThrough : Model<TThrough>, new()
        where TRelated : Model<TRelated>, new()
    {
        private readonly string _secondKey;
        private readonly string _secondLocalKey;
        private readonly ModelMetadata _throughMetadata;

        /// <param name="parent">Parent model (Country)</param>
        /// <param name="firstKey">Foreign key on the through table → parent (default {Parent}Id)</param>
        /// <param name="secondKey">Foreign key on the related table → through (default {Through}Id)</param>
        /// <param name="localKey">Parent key (default Id)</param>
        /// <param name="secondLocalKey">Through key (default Id)</param>
        /// <param name="connectionFactory">null = the related model's connection</param>
        public HasManyThrough(TParent parent, string? firstKey = null, string? secondKey = null, string localKey = "Id",
            string secondLocalKey = "Id", Func<DbCommandBase>? connectionFactory = null)
            : base(parent, firstKey ?? typeof(TParent).Name + "Id", localKey, connectionFactory)
        {
            _secondKey = secondKey ?? typeof(TThrough).Name + "Id";
            _secondLocalKey = secondLocalKey ?? "Id";
            _throughMetadata = ModelBase.GetMetadata<TThrough>();
        }

        /// <summary>
        /// Get all related models
        /// </summary>
        public override object? Get() => GetRelated();

        /// <inheritdoc />
        public override async Task<object?> GetAsync(CancellationToken cancellationToken = default)
            => await GetRelatedAsync(cancellationToken).ConfigureAwait(false);

        /// <summary>
        /// Get all related models (typed)
        /// </summary>
        public List<TRelated> GetRelated() => DbRun.Sync(GetRelatedCore(false, default));

        /// <summary>
        /// Get all related models (typed, async)
        /// </summary>
        public Task<List<TRelated>> GetRelatedAsync(CancellationToken cancellationToken = default) => GetRelatedCore(true, cancellationToken);

        private async Task<List<TRelated>> GetRelatedCore(bool useAsync, CancellationToken ct)
        {
            var localValue = GetLocalKeyValue();
            if (localValue == null) return new List<TRelated>();

            using (var db = _readFactory())
            {
                db.ParametersAdd("@pk", localValue);
                var sql = "SELECT r.* " + ThroughSql.From(db, _relatedMetadata, _throughMetadata, _foreignKey, _secondKey, _secondLocalKey);
                return await LoadCore(db, sql, useAsync, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Count related models
        /// </summary>
        public int Count() => DbRun.Sync(CountCore(false, default));

        /// <summary>
        /// Count related models (async)
        /// </summary>
        public Task<int> CountAsync(CancellationToken cancellationToken = default) => CountCore(true, cancellationToken);

        private async Task<int> CountCore(bool useAsync, CancellationToken ct)
        {
            var localValue = GetLocalKeyValue();
            if (localValue == null) return 0;

            using (var db = _readFactory())
            {
                db.ParametersAdd("@pk", localValue);
                var sql = "SELECT COUNT(*) " + ThroughSql.From(db, _relatedMetadata, _throughMetadata, _foreignKey, _secondKey, _secondLocalKey);
                return await DbRun.Int32(db, sql, useAsync, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// HasOneThrough: the first related row reached through an intermediate table
    /// </summary>
    public class HasOneThrough<TParent, TThrough, TRelated> : Relationship<TParent, TRelated>
        where TParent : Model<TParent>, new()
        where TThrough : Model<TThrough>, new()
        where TRelated : Model<TRelated>, new()
    {
        private readonly string _secondKey;
        private readonly string _secondLocalKey;
        private readonly ModelMetadata _throughMetadata;

        public HasOneThrough(TParent parent, string? firstKey = null, string? secondKey = null, string localKey = "Id",
            string secondLocalKey = "Id", Func<DbCommandBase>? connectionFactory = null)
            : base(parent, firstKey ?? typeof(TParent).Name + "Id", localKey, connectionFactory)
        {
            _secondKey = secondKey ?? typeof(TThrough).Name + "Id";
            _secondLocalKey = secondLocalKey ?? "Id";
            _throughMetadata = ModelBase.GetMetadata<TThrough>();
        }

        /// <summary>
        /// Get the related model
        /// </summary>
        public override object? Get() => GetRelated();

        /// <inheritdoc />
        public override async Task<object?> GetAsync(CancellationToken cancellationToken = default)
            => await GetRelatedAsync(cancellationToken).ConfigureAwait(false);

        /// <summary>
        /// Get the related model (typed), null when there is none
        /// </summary>
        public TRelated? GetRelated() => DbRun.Sync(GetRelatedCore(false, default));

        /// <summary>
        /// Get the related model (typed, async), null when there is none
        /// </summary>
        public Task<TRelated?> GetRelatedAsync(CancellationToken cancellationToken = default) => GetRelatedCore(true, cancellationToken);

        private async Task<TRelated?> GetRelatedCore(bool useAsync, CancellationToken ct)
        {
            var localValue = GetLocalKeyValue();
            if (localValue == null) return null;

            using (var db = _readFactory())
            {
                db.ParametersAdd("@pk", localValue);
                var sql = "SELECT r.* " + ThroughSql.From(db, _relatedMetadata, _throughMetadata, _foreignKey, _secondKey, _secondLocalKey);
                return (await LoadCore(db, sql, useAsync, ct).ConfigureAwait(false)).FirstOrDefault();
            }
        }
    }

    /// <summary>
    /// Base of MorphMany / MorphOne: related rows with {Name}Type = parent's morph name and {Name}Id = parent key
    /// </summary>
    public abstract class MorphRelation<TParent, TRelated> : Relationship<TParent, TRelated>
        where TParent : Model<TParent>, new()
        where TRelated : Model<TRelated>, new()
    {
        protected readonly string _typeColumn;
        protected readonly string _morphType;

        protected MorphRelation(TParent parent, string name, string localKey, Func<DbCommandBase>? connectionFactory)
            : base(parent, (name ?? throw new ArgumentNullException(nameof(name))) + "Id", localKey, connectionFactory)
        {
            _typeColumn = name + "Type";
            _morphType = MorphMap.NameOf(typeof(TParent));
        }

        /// <summary>
        /// Query of the related rows (more conditions, ordering, paging)
        /// </summary>
        public QueryBuilder<TRelated> Query()
        {
            return Model<TRelated>.Where(_typeColumn, _morphType).Where(_foreignKey, GetLocalKeyValue());
        }

        /// <summary>
        /// Create a related model with the type / id columns set
        /// </summary>
        public TRelated Create(Dictionary<string, object?> data)
        {
            return CreateRelated(data, _typeColumn, _morphType, _foreignKey, GetLocalKeyValue());
        }

        /// <summary>
        /// Create a related model with the type / id columns set (async)
        /// </summary>
        public Task<TRelated> CreateAsync(Dictionary<string, object?> data, CancellationToken cancellationToken = default)
        {
            return CreateRelatedCore(data, new object?[] { _typeColumn, _morphType, _foreignKey, GetLocalKeyValue() }, true, cancellationToken);
        }

        /// <summary>
        /// Count related models
        /// </summary>
        public int Count() => DbRun.Sync(CountCore(false, default));

        /// <summary>
        /// Count related models (async)
        /// </summary>
        public Task<int> CountAsync(CancellationToken cancellationToken = default) => CountCore(true, cancellationToken);

        private async Task<int> CountCore(bool useAsync, CancellationToken ct)
        {
            var localValue = GetLocalKeyValue();
            if (localValue == null) return 0;

            using (var db = _readFactory())
            {
                return await DbRun.Int32(db, "SELECT COUNT(*) " + FromSql(db, localValue), useAsync, ct).ConfigureAwait(false);
            }
        }

        protected List<TRelated> Fetch() => DbRun.Sync(FetchCore(false, default));

        protected async Task<List<TRelated>> FetchCore(bool useAsync, CancellationToken ct)
        {
            var localValue = GetLocalKeyValue();
            if (localValue == null) return new List<TRelated>();

            using (var db = _readFactory())
            {
                return await LoadCore(db, "SELECT * " + FromSql(db, localValue), useAsync, ct).ConfigureAwait(false);
            }
        }

        private string FromSql(DbCommandBase db, object localValue)
        {
            db.ParametersAdd("@type", _morphType);
            db.ParametersAdd("@id", localValue);
            return $"FROM {QTable(db, _relatedMetadata.TableName)} WHERE {Q(db, _typeColumn)} = @type AND {Q(db, _foreignKey)} = @id{SoftDeleteFilter(db)}";
        }
    }

    /// <summary>
    /// Polymorphic one-to-many (a post's / a video's comments in one Comments table)
    /// </summary>
    public class MorphMany<TParent, TRelated> : MorphRelation<TParent, TRelated>
        where TParent : Model<TParent>, new()
        where TRelated : Model<TRelated>, new()
    {
        /// <param name="parent">Parent model</param>
        /// <param name="name">Column prefix: "Commentable" → CommentableType / CommentableId</param>
        /// <param name="localKey">Parent key (default Id)</param>
        /// <param name="connectionFactory">null = the related model's connection</param>
        public MorphMany(TParent parent, string name, string localKey = "Id", Func<DbCommandBase>? connectionFactory = null)
            : base(parent, name, localKey, connectionFactory)
        {
        }

        /// <summary>
        /// Get all related models
        /// </summary>
        public override object? Get() => GetRelated();

        /// <inheritdoc />
        public override async Task<object?> GetAsync(CancellationToken cancellationToken = default)
            => await GetRelatedAsync(cancellationToken).ConfigureAwait(false);

        /// <summary>
        /// Get all related models (typed)
        /// </summary>
        public List<TRelated> GetRelated() => Fetch();

        /// <summary>
        /// Get all related models (typed, async)
        /// </summary>
        public Task<List<TRelated>> GetRelatedAsync(CancellationToken cancellationToken = default) => FetchCore(true, cancellationToken);
    }

    /// <summary>
    /// Polymorphic one-to-one (a user's / a company's image)
    /// </summary>
    public class MorphOne<TParent, TRelated> : MorphRelation<TParent, TRelated>
        where TParent : Model<TParent>, new()
        where TRelated : Model<TRelated>, new()
    {
        public MorphOne(TParent parent, string name, string localKey = "Id", Func<DbCommandBase>? connectionFactory = null)
            : base(parent, name, localKey, connectionFactory)
        {
        }

        /// <summary>
        /// Get the related model
        /// </summary>
        public override object? Get() => GetRelated();

        /// <inheritdoc />
        public override async Task<object?> GetAsync(CancellationToken cancellationToken = default)
            => await GetRelatedAsync(cancellationToken).ConfigureAwait(false);

        /// <summary>
        /// Get the related model (typed), null when there is none
        /// </summary>
        public TRelated? GetRelated() => Fetch().FirstOrDefault();

        /// <summary>
        /// Get the related model (typed, async), null when there is none
        /// </summary>
        public async Task<TRelated?> GetRelatedAsync(CancellationToken cancellationToken = default)
            => (await FetchCore(true, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
    }

    /// <summary>
    /// Inverse of MorphMany / MorphOne: the owner named by {Name}Type / {Name}Id of the child
    /// </summary>
    public class MorphTo<TChild> where TChild : Model<TChild>, new()
    {
        private readonly TChild _child;
        private readonly PropertyMetadata _typeProperty;
        private readonly PropertyMetadata _idProperty;

        /// <param name="child">Child model (the comment)</param>
        /// <param name="name">Column prefix: "Commentable" → CommentableType / CommentableId</param>
        public MorphTo(TChild child, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));

            _child = child ?? throw new ArgumentNullException(nameof(child));
            var metadata = ModelBase.GetMetadata<TChild>();
            _typeProperty = ModelBase.FindProperty(metadata, name + "Type")
                            ?? throw new InvalidOperationException($"{typeof(TChild).Name} has no {name}Type property.");
            _idProperty = ModelBase.FindProperty(metadata, name + "Id")
                          ?? throw new InvalidOperationException($"{typeof(TChild).Name} has no {name}Id property.");
        }

        /// <summary>
        /// Get the owner (any model type)
        /// </summary>
        public ModelBase? Get() => GetRelated();

        /// <summary>
        /// Get the owner (any model type, async)
        /// </summary>
        public Task<ModelBase?> GetAsync(CancellationToken cancellationToken = default) => GetRelatedAsync(cancellationToken);

        /// <summary>
        /// Get the owner, or null when the type / id columns are empty or the row is gone
        /// </summary>
        public ModelBase? GetRelated() => DbRun.Sync(GetRelatedCore(false, default));

        /// <summary>
        /// Get the owner (async), or null when the type / id columns are empty or the row is gone
        /// </summary>
        public Task<ModelBase?> GetRelatedAsync(CancellationToken cancellationToken = default) => GetRelatedCore(true, cancellationToken);

        private async Task<ModelBase?> GetRelatedCore(bool useAsync, CancellationToken ct)
        {
            var typeName = _typeProperty.GetValue(_child) as string;
            var id = _idProperty.GetValue(_child);
            if (string.IsNullOrEmpty(typeName) || ModelMapper.IsDefaultKey(id))
                return null;

            var ownerType = MorphMap.TypeOf(typeName!);
            var owner = ModelMetadata.GetMetadata(ownerType);
            if (owner.PrimaryKeyColumn == null)
                throw new InvalidOperationException($"MorphTo: {ownerType.Name} needs a single-column primary key.");

            using (var db = ModelBase.CreateReadCommand(owner.ConnectionName ?? _child.LoadedConnection))
            {
                var p = db.ProviderType;
                db.ParametersAdd("@id", id);
                var sql = $"SELECT * FROM {SqlDialect.Table(p, owner.TableName)} WHERE {SqlDialect.Column(p, owner.PrimaryKeyColumn)} = @id";
                if (owner.HasSoftDelete)
                    sql += $" AND {SqlDialect.Column(p, owner.SoftDeleteColumn)} IS NULL";

                var rows = await DbRun.Models(db, sql, owner, () => (ModelBase)owner.NewInstance(), useAsync, ct).ConfigureAwait(false);
                return rows.FirstOrDefault();
            }
        }

        /// <summary>
        /// Get the owner when it is a <typeparamref name="TOwner"/> (null otherwise)
        /// </summary>
        public TOwner? GetRelated<TOwner>() where TOwner : ModelBase
        {
            return GetRelated() as TOwner;
        }

        /// <summary>
        /// Get the owner when it is a <typeparamref name="TOwner"/> (async, null otherwise)
        /// </summary>
        public async Task<TOwner?> GetRelatedAsync<TOwner>(CancellationToken cancellationToken = default) where TOwner : ModelBase
        {
            return await GetRelatedAsync(cancellationToken).ConfigureAwait(false) as TOwner;
        }

        /// <summary>
        /// Point the child at an owner and save the child
        /// </summary>
        public void Associate(ModelBase owner) => DbRun.Sync(AssociateCore(owner, false, default));

        /// <summary>
        /// Point the child at an owner and save the child (async)
        /// </summary>
        public Task AssociateAsync(ModelBase owner, CancellationToken cancellationToken = default) => AssociateCore(owner, true, cancellationToken);

        private async Task<bool> AssociateCore(ModelBase owner, bool useAsync, CancellationToken ct)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));

            var ownerMetadata = ModelMetadata.GetMetadata(owner.GetType());
            if (ownerMetadata.PrimaryKeyProperty == null)
                throw new InvalidOperationException($"MorphTo: {owner.GetType().Name} needs a single-column primary key.");

            _child.SetProperty(_typeProperty.PropertyName, MorphMap.NameOf(owner.GetType()));
            _child.SetProperty(_idProperty.PropertyName, ownerMetadata.PrimaryKeyProperty.GetValue(owner));
            await RelationKeys.Save(_child, useAsync, ct).ConfigureAwait(false);
            return true;
        }

        /// <summary>
        /// Clear the type / id columns and save the child
        /// </summary>
        public void Dissociate() => DbRun.Sync(DissociateCore(false, default));

        /// <summary>
        /// Clear the type / id columns and save the child (async)
        /// </summary>
        public Task DissociateAsync(CancellationToken cancellationToken = default) => DissociateCore(true, cancellationToken);

        private async Task<bool> DissociateCore(bool useAsync, CancellationToken ct)
        {
            _child.SetProperty(_typeProperty.PropertyName, null);
            _child.SetProperty(_idProperty.PropertyName, null);
            await RelationKeys.Save(_child, useAsync, ct).ConfigureAwait(false);
            return true;
        }
    }
}
