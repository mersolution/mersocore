using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using mersolutionCore.Command.Abstractions;
using mersolutionCore.ORM.Relationships;

namespace mersolutionCore.ORM
{
    /// <summary>
    /// Base class for all ORM models
    /// </summary>
    public abstract class ModelBase
    {
        /// <summary>
        /// Save() / SaveAsync() of the concrete model (Model&lt;T&gt;) for code that only knows ModelBase
        /// </summary>
        internal abstract Task<bool> SaveModel(bool useAsync, CancellationToken ct);

        private static Func<DbCommandBase>? _defaultFactory;
        private static readonly ConcurrentDictionary<string, Func<DbCommandBase>> _namedFactories =
            new ConcurrentDictionary<string, Func<DbCommandBase>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Default database connection factory
        /// </summary>
        internal static Func<DbCommandBase>? ConnectionFactory => _defaultFactory;

        /// <summary>
        /// Configure the default database connection factory
        /// </summary>
        public static void Configure(Func<DbCommandBase> connectionFactory)
        {
            _defaultFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        /// <summary>
        /// Register a named connection (used by <c>[Connection("name")]</c>, <c>Model.On("name")</c>, <c>MersoConnection.Use("name")</c>)
        /// </summary>
        public static void Configure(string connectionName, Func<DbCommandBase> connectionFactory)
        {
            if (string.IsNullOrWhiteSpace(connectionName))
                throw new ArgumentNullException(nameof(connectionName));

            _namedFactories[connectionName] = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        /// <summary>
        /// Quote table / column names in generated SQL ([Order], `Order`, "order"). Default true; set false
        /// only for databases whose names were created case-sensitively in a way the quoting would not match.
        /// </summary>
        public static bool QuoteIdentifiers { get; set; } = true;

        /// <summary>
        /// Throw <see cref="MassAssignmentException"/> when Fill() / Create(dictionary) gets a [Guarded] or
        /// not-[Fillable] key. Default false: such keys are skipped silently.
        /// </summary>
        public static bool StrictMassAssignment { get; set; }

        /// <summary>
        /// Factory for a named connection, or (name null) the scoped <see cref="MersoConnection"/> / default one
        /// </summary>
        internal static Func<DbCommandBase> ResolveFactory(string? connectionName)
        {
            if (!string.IsNullOrEmpty(connectionName))
            {
                if (_namedFactories.TryGetValue(connectionName!, out var named))
                    return named;

                throw new InvalidOperationException(
                    $"Connection '{connectionName}' is not configured. Call ModelBase.Configure(\"{connectionName}\", ...) or DbConfig.AddConnection(\"{connectionName}\", ...) first.");
            }

            return MersoConnection.CurrentFactory
                ?? _defaultFactory
                ?? throw new InvalidOperationException("Database not configured. Create a DbContext or call ModelBase.Configure(...) first.");
        }

        /// <summary>
        /// Create a command for a named connection (null = scoped / default connection)
        /// </summary>
        internal static DbCommandBase CreateCommand(string? connectionName = null)
        {
            return ResolveFactory(connectionName)();
        }

        /// <summary>
        /// Command for a read: a read replica of the connection when one is registered and allowed
        /// (see <see cref="ReadReplicas"/>), else the primary
        /// </summary>
        internal static DbCommandBase CreateReadCommand(string? connectionName = null)
        {
            var replica = ReadReplicas.ForRead(connectionName);
            return replica != null ? replica() : CreateCommand(connectionName);
        }

        /// <summary>
        /// Provider of a connection (decides SQL dialect)
        /// </summary>
        internal static DbProviderType CurrentProvider(string? connectionName = null)
        {
            using (var db = CreateCommand(connectionName))
            {
                return db.ProviderType;
            }
        }

        /// <summary>
        /// Get model metadata (cached)
        /// </summary>
        internal static ModelMetadata GetMetadata<T>() where T : ModelBase
        {
            return ModelMetadata.GetMetadata(typeof(T));
        }

        /// <summary>
        /// Get metadata for this instance
        /// </summary>
        protected ModelMetadata GetMetadata()
        {
            return ModelMetadata.GetMetadata(GetType());
        }

        #region Change tracking

        // Property name → value when the model was loaded / last saved (null: never loaded)
        private Dictionary<string, object?>? _original;

        /// <summary>
        /// Connection the model was loaded from with <c>On(name)</c>; Save / Delete write back to it
        /// </summary>
        internal string? LoadedConnection { get; set; }

        /// <summary>
        /// True when a property differs from the loaded / saved value (always true for a model that was never loaded)
        /// </summary>
        public bool IsDirty()
        {
            var dirty = GetDirtyProperties(GetMetadata());
            return dirty == null || dirty.Count > 0;
        }

        /// <summary>
        /// True when the given property (or column) differs from the loaded / saved value
        /// </summary>
        public bool IsDirty(string propertyOrColumn)
        {
            var prop = FindProperty(GetMetadata(), propertyOrColumn);
            if (prop == null)
                return false;
            if (_original == null)
                return true;

            return !ValuesEqual(_original.TryGetValue(prop.PropertyName, out var old) ? old : null, TrackedValue(prop));
        }

        // Value compared for change tracking: mutable converted values (JSON lists ...) are compared as stored, so
        // changing a list in place is seen as a change. Strings and value types are compared as they are: an
        // [Encrypted] value gets a new IV on every conversion, so its stored form never compares equal.
        private object? TrackedValue(PropertyMetadata prop)
        {
            return TrackedAsIs(prop) ? prop.GetValue(this) : prop.GetDbValue(this);
        }

        private static bool TrackedAsIs(PropertyMetadata prop)
        {
            var type = prop.PropertyInfo.PropertyType;
            return prop.Converter == null || type == typeof(string) || type.IsValueType;
        }

        /// <summary>
        /// True when nothing changed since the model was loaded / saved
        /// </summary>
        public bool IsClean() => !IsDirty();

        /// <summary>
        /// Changed properties with their current values (all mapped properties for a model that was never loaded)
        /// </summary>
        public Dictionary<string, object?> GetDirty()
        {
            var metadata = GetMetadata();
            var dirty = GetDirtyProperties(metadata) ?? metadata.Properties;
            return dirty.ToDictionary(p => p.PropertyName, p => (object?)p.PropertyInfo.GetValue(this));
        }

        /// <summary>
        /// Value of a property when the model was loaded / last saved (null when never loaded)
        /// </summary>
        public object? GetOriginal(string propertyOrColumn)
        {
            var prop = FindProperty(GetMetadata(), propertyOrColumn);
            return prop != null && _original != null && _original.TryGetValue(prop.PropertyName, out var value) ? value : null;
        }

        /// <summary>
        /// Mark the current values as saved (clean)
        /// </summary>
        public void SyncOriginal()
        {
            var metadata = GetMetadata();
            var snapshot = new Dictionary<string, object?>(metadata.Properties.Count);
            foreach (var prop in metadata.Properties)
                snapshot[prop.PropertyName] = Snapshot(TrackedValue(prop));
            _original = snapshot;
        }

        internal bool HasOriginal => _original != null;

        internal void SyncOriginal(PropertyMetadata? prop)
        {
            if (_original != null && prop != null)
                _original[prop.PropertyName] = Snapshot(TrackedValue(prop));
        }

        internal void ForgetOriginal()
        {
            _original = null;
        }

        /// <summary>
        /// Value at load time as the column stores it (current value when the model was never loaded)
        /// </summary>
        internal object? OriginalValue(PropertyMetadata prop)
        {
            var value = _original != null && _original.TryGetValue(prop.PropertyName, out var old)
                ? old
                : TrackedValue(prop);
            return prop.Converter != null && TrackedAsIs(prop) ? prop.ToDatabase(value) : value;
        }

        /// <summary>
        /// Changed properties, or null when there is no loaded state to compare with
        /// </summary>
        internal List<PropertyMetadata>? GetDirtyProperties(ModelMetadata metadata)
        {
            if (_original == null)
                return null;

            return metadata.Properties
                .Where(p => !ValuesEqual(_original.TryGetValue(p.PropertyName, out var old) ? old : null, TrackedValue(p)))
                .ToList();
        }

        internal static PropertyMetadata? FindProperty(ModelMetadata metadata, string? name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            return metadata.Properties.FirstOrDefault(p => p.PropertyName.Equals(name, StringComparison.OrdinalIgnoreCase))
                   ?? metadata.Properties.FirstOrDefault(p => p.ColumnName.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        private static object? Snapshot(object? value)
        {
            return value is byte[] bytes ? (byte[])bytes.Clone() : value;
        }

        private static bool ValuesEqual(object? a, object? b)
        {
            if (a == null || b == null)
                return a == null && b == null;
            if (a is byte[] x && b is byte[] y)
                return x.SequenceEqual(y);
            return a.Equals(b);
        }

        #endregion

        #region Extra attributes / hidden properties

        // Result columns without a property (SelectRaw aliases, pivot_* columns)
        private Dictionary<string, object?>? _extra;
        private HashSet<string>? _madeVisible;
        private HashSet<string>? _madeHidden;

        internal void SetExtra(string name, object? value)
        {
            if (_extra == null)
                _extra = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            _extra[name] = value;
        }

        /// <summary>
        /// Value of a property / column, or of a result column without a property
        /// (<c>SelectRaw("COUNT(*) AS Total")</c> → <c>GetAttribute("Total")</c>); null when unknown
        /// </summary>
        public object? GetAttribute(string name)
        {
            var metadata = GetMetadata();
            var prop = FindProperty(metadata, name)
                       ?? metadata.ComputedProperties.FirstOrDefault(p => p.PropertyName.Equals(name, StringComparison.OrdinalIgnoreCase)
                                                                          || p.ColumnName.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (prop != null)
                return prop.GetValue(this);

            return _extra != null && _extra.TryGetValue(name, out var value) ? value : null;
        }

        /// <summary>
        /// <see cref="GetAttribute(string)"/> converted to <typeparamref name="TValue"/> (providers return
        /// COUNT as int, long or decimal)
        /// </summary>
        public TValue? GetAttribute<TValue>(string name)
        {
            var value = ModelMapper.ConvertTo(GetAttribute(name), typeof(TValue));
            return value == null ? default : (TValue)value;
        }

        /// <summary>
        /// Result columns that have no property on the model (read-only copy)
        /// </summary>
        public IReadOnlyDictionary<string, object?> GetExtraAttributes()
        {
            return _extra != null
                ? new Dictionary<string, object?>(_extra, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, object?>();
        }

        /// <summary>
        /// Pivot columns loaded by <c>BelongsToMany(...).WithPivot("Role")</c> (empty otherwise)
        /// </summary>
        public IReadOnlyDictionary<string, object?> GetPivot()
        {
            var pivot = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (_extra != null)
            {
                foreach (var kvp in _extra)
                {
                    if (kvp.Key.StartsWith(Relationships.PivotNames.Prefix, StringComparison.OrdinalIgnoreCase))
                        pivot[kvp.Key.Substring(Relationships.PivotNames.Prefix.Length)] = kvp.Value;
                }
            }
            return pivot;
        }

        /// <summary>
        /// True when ToDict / ToJson / MersoJson leave this property out ([Hidden] or MakeHidden, not MakeVisible)
        /// </summary>
        internal bool IsHiddenProperty(PropertyMetadata prop)
        {
            if (_madeHidden != null && _madeHidden.Contains(prop.PropertyName))
                return true;
            return prop.IsHidden && (_madeVisible == null || !_madeVisible.Contains(prop.PropertyName));
        }

        internal bool IsHiddenProperty(string propertyName)
        {
            var prop = FindProperty(GetMetadata(), propertyName);
            return prop != null && IsHiddenProperty(prop);
        }

        internal void SetVisibility(string[] names, bool visible)
        {
            var metadata = GetMetadata();
            foreach (var name in names ?? new string[0])
            {
                var prop = FindProperty(metadata, name)
                           ?? throw new ArgumentException($"{metadata.ModelType.Name} has no property '{name}'.", nameof(names));

                if (_madeVisible == null) _madeVisible = new HashSet<string>(StringComparer.Ordinal);
                if (_madeHidden == null) _madeHidden = new HashSet<string>(StringComparer.Ordinal);

                if (visible)
                {
                    _madeVisible.Add(prop.PropertyName);
                    _madeHidden.Remove(prop.PropertyName);
                }
                else
                {
                    _madeHidden.Add(prop.PropertyName);
                    _madeVisible.Remove(prop.PropertyName);
                }
            }
        }

        #endregion
    }

    /// <summary>
    /// Generic model base with CRUD operations
    /// </summary>
    /// <typeparam name="T">Model type</typeparam>
    public abstract class Model<T> : ModelBase where T : Model<T>, new()
    {
        #region Static CRUD Methods

        /// <summary>
        /// Start a query builder
        /// </summary>
        public static QueryBuilder<T> Query()
        {
            return new QueryBuilder<T>();
        }

        /// <summary>
        /// Start a query on a named connection; models loaded by it are saved back to that connection
        /// </summary>
        public static QueryBuilder<T> On(string connectionName)
        {
            return new QueryBuilder<T>().On(connectionName);
        }

        /// <summary>
        /// Find record by primary key (soft-deleted records are excluded).
        /// Composite key: pass the values in key order, <c>Find(orderId, productId)</c>.
        /// </summary>
        public static T? Find(object id)
        {
            return Query().WhereKey(id).First();
        }

        /// <summary>
        /// Find record by composite primary key (values in key order)
        /// </summary>
        public static T? Find(params object[] keyValues)
        {
            return Find((object)keyValues);
        }

        /// <summary>
        /// Find record by primary key (async). Composite key: pass an object[] in key order.
        /// </summary>
        public static Task<T?> FindAsync(object id, CancellationToken cancellationToken = default)
        {
            return Query().WhereKey(id).FirstAsync(cancellationToken);
        }

        /// <summary>
        /// Find record by primary key including soft-deleted records
        /// </summary>
        public static T? FindWithTrashed(object id)
        {
            return Query().WithTrashed().WhereKey(id).First();
        }

        /// <summary>
        /// Find record by primary key including soft-deleted records (async)
        /// </summary>
        public static Task<T?> FindWithTrashedAsync(object id, CancellationToken cancellationToken = default)
        {
            return Query().WithTrashed().WhereKey(id).FirstAsync(cancellationToken);
        }

        /// <summary>
        /// Find record by primary key or throw exception
        /// </summary>
        public static T FindOrFail(object id)
        {
            return Find(id) ?? throw NotFound(id);
        }

        /// <summary>
        /// Find record by composite primary key or throw exception
        /// </summary>
        public static T FindOrFail(params object[] keyValues)
        {
            return FindOrFail((object)keyValues);
        }

        /// <summary>
        /// Find record by primary key or throw exception (async)
        /// </summary>
        public static async Task<T> FindOrFailAsync(object id, CancellationToken cancellationToken = default)
        {
            return await FindAsync(id, cancellationToken).ConfigureAwait(false) ?? throw NotFound(id);
        }

        /// <summary>
        /// Find multiple records by primary keys (composite key: each id is an object[] in key order)
        /// </summary>
        public static List<T> FindMany(params object[]? ids)
        {
            return DbRun.Sync(FindManyCore(ids, false, default));
        }

        /// <summary>
        /// Find multiple records by primary keys (list)
        /// </summary>
        public static List<T> FindMany(IEnumerable<object>? ids)
        {
            return FindMany(ids?.ToArray());
        }

        /// <summary>
        /// Find multiple records by primary keys (async)
        /// </summary>
        public static Task<List<T>> FindManyAsync(IEnumerable<object>? ids, CancellationToken cancellationToken = default)
        {
            return FindManyCore(ids?.ToArray(), true, cancellationToken);
        }

        private static async Task<List<T>> FindManyCore(object[]? ids, bool useAsync, CancellationToken ct)
        {
            var results = new List<T>();
            if (ids == null || ids.Length == 0)
                return results;

            var metadata = GetMetadata<T>();
            RequirePrimaryKey(metadata);

            // Stay below provider parameter limits (SQL Server: 2100)
            foreach (var chunk in Chunk(ids, 1000 / metadata.KeyProperties.Count))
            {
                results.AddRange(await Query().WhereKeyIn(chunk).GetCoreAsync(useAsync, ct).ConfigureAwait(false));
            }
            return results;
        }

        /// <summary>
        /// Get all records
        /// </summary>
        public static List<T> All()
        {
            return Query().Get();
        }

        /// <summary>
        /// Get all records (async)
        /// </summary>
        public static Task<List<T>> AllAsync(CancellationToken cancellationToken = default)
        {
            return Query().GetAsync(cancellationToken);
        }

        /// <summary>
        /// Create new record from a dictionary ([Guarded] / not-[Fillable] keys are skipped)
        /// </summary>
        public static T Create(Dictionary<string, object?> data)
        {
            var model = new T();
            model.Fill(data);
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
        /// Create new record (async)
        /// </summary>
        public static Task<T> CreateAsync(Dictionary<string, object?> data, CancellationToken cancellationToken = default)
        {
            var model = new T();
            model.Fill(data);
            return CreateAsync(model, cancellationToken);
        }

        /// <summary>
        /// Create new record from model instance (async)
        /// </summary>
        public static async Task<T> CreateAsync(T model, CancellationToken cancellationToken = default)
        {
            await model.SaveAsync(cancellationToken).ConfigureAwait(false);
            return model;
        }

        /// <summary>
        /// Insert or update by a unique column set in one statement
        /// (MERGE / ON CONFLICT / ON DUPLICATE KEY). See <see cref="BulkOperations.Upsert{T}"/>.
        /// </summary>
        public static int Upsert(T model, params string[] uniqueBy)
        {
            return BulkOperations.Upsert(new[] { model }, uniqueBy);
        }

        /// <summary>
        /// Insert or update by a unique column set (async)
        /// </summary>
        public static Task<int> UpsertAsync(T model, params string[] uniqueBy)
        {
            return BulkOperations.UpsertAsync(new[] { model }, uniqueBy);
        }

        /// <summary>
        /// Insert or update by a unique column set (async, cancellable)
        /// </summary>
        public static Task<int> UpsertAsync(T model, string[] uniqueBy, CancellationToken cancellationToken)
        {
            return BulkOperations.UpsertAsync(new[] { model }, uniqueBy, cancellationToken: cancellationToken);
        }

        /// <summary>
        /// Models one by one while you iterate (see <see cref="QueryBuilder{T}.Cursor"/>)
        /// </summary>
        public static IEnumerable<T> Cursor()
        {
            return Query().Cursor();
        }

        /// <summary>
        /// Models one by one while you iterate (async, see <see cref="QueryBuilder{T}.CursorAsync"/>)
        /// </summary>
        public static IAsyncEnumerable<T> CursorAsync(CancellationToken cancellationToken = default)
        {
            return Query().CursorAsync(cancellationToken);
        }

        /// <summary>
        /// Where clause shortcut
        /// </summary>
        public static QueryBuilder<T> Where(string column, string op, object? value)
        {
            return Query().Where(column, op, value);
        }

        /// <summary>
        /// Where clause shortcut (equals)
        /// </summary>
        public static QueryBuilder<T> Where(string column, object? value)
        {
            return Query().Where(column, "=", value);
        }

        /// <summary>
        /// Parenthesised where group shortcut
        /// </summary>
        public static QueryBuilder<T> Where(Action<QueryBuilder<T>> group)
        {
            return Query().Where(group);
        }

        /// <summary>
        /// Type-safe where shortcut: <c>User.Where(u =&gt; u.Age &gt;= 18)</c>
        /// </summary>
        public static QueryBuilder<T> Where(System.Linq.Expressions.Expression<Func<T, bool>> predicate)
        {
            return Query().Where(predicate);
        }

        /// <summary>
        /// Get first record or null
        /// </summary>
        public static T? First()
        {
            return Query().First();
        }

        /// <summary>
        /// Get first record or null (async)
        /// </summary>
        public static Task<T?> FirstAsync(CancellationToken cancellationToken = default)
        {
            return Query().FirstAsync(cancellationToken);
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
        /// Find first matching record or create new one. The search values are always set;
        /// <paramref name="create"/> values follow [Fillable] / [Guarded].
        /// </summary>
        public static T FirstOrCreate(Dictionary<string, object?> search, Dictionary<string, object?>? create = null)
        {
            return DbRun.Sync(FirstOrCreateCore(search, create, false, default));
        }

        /// <summary>
        /// Find first matching record or create new one (async)
        /// </summary>
        public static Task<T> FirstOrCreateAsync(Dictionary<string, object?> search, Dictionary<string, object?>? create = null, CancellationToken cancellationToken = default)
        {
            return FirstOrCreateCore(search, create, true, cancellationToken);
        }

        private static async Task<T> FirstOrCreateCore(Dictionary<string, object?> search, Dictionary<string, object?>? create, bool useAsync, CancellationToken ct)
        {
            var existing = await SearchQuery(search).GetFirstCoreAsync(useAsync, ct).ConfigureAwait(false);
            if (existing != null)
                return existing;

            var model = new T();
            model.ForceFill(search);
            model.Fill(create);
            await model.SaveCore(useAsync, ct).ConfigureAwait(false);
            return model;
        }

        /// <summary>
        /// Find first matching record or create new instance (not saved)
        /// </summary>
        public static T FirstOrNew(Dictionary<string, object?> search, Dictionary<string, object?>? values = null)
        {
            return DbRun.Sync(FirstOrNewCore(search, values, false, default));
        }

        /// <summary>
        /// Find first matching record or create new instance (not saved, async)
        /// </summary>
        public static Task<T> FirstOrNewAsync(Dictionary<string, object?> search, Dictionary<string, object?>? values = null, CancellationToken cancellationToken = default)
        {
            return FirstOrNewCore(search, values, true, cancellationToken);
        }

        private static async Task<T> FirstOrNewCore(Dictionary<string, object?> search, Dictionary<string, object?>? values, bool useAsync, CancellationToken ct)
        {
            var existing = await SearchQuery(search).GetFirstCoreAsync(useAsync, ct).ConfigureAwait(false);
            if (existing != null)
                return existing;

            var model = new T();
            model.ForceFill(search);
            model.Fill(values);
            return model;
        }

        /// <summary>
        /// Update existing record or create new one
        /// </summary>
        public static T UpdateOrCreate(Dictionary<string, object?> search, Dictionary<string, object?> update)
        {
            return DbRun.Sync(UpdateOrCreateCore(search, update, false, default));
        }

        /// <summary>
        /// Update existing record or create new one (async)
        /// </summary>
        public static Task<T> UpdateOrCreateAsync(Dictionary<string, object?> search, Dictionary<string, object?> update, CancellationToken cancellationToken = default)
        {
            return UpdateOrCreateCore(search, update, true, cancellationToken);
        }

        private static async Task<T> UpdateOrCreateCore(Dictionary<string, object?> search, Dictionary<string, object?> update, bool useAsync, CancellationToken ct)
        {
            var existing = await SearchQuery(search).GetFirstCoreAsync(useAsync, ct).ConfigureAwait(false);
            if (existing != null)
            {
                existing.Fill(update);
                await existing.SaveCore(useAsync, ct).ConfigureAwait(false);
                return existing;
            }

            var model = new T();
            model.ForceFill(search);
            model.Fill(update);
            await model.SaveCore(useAsync, ct).ConfigureAwait(false);
            return model;
        }

        private static QueryBuilder<T> SearchQuery(Dictionary<string, object?> search)
        {
            // FirstOrCreate / UpdateOrCreate decide about a write: read the primary
            var query = Query().OnPrimary();
            foreach (var kvp in search ?? new Dictionary<string, object?>())
            {
                query.Where(kvp.Key, kvp.Value);
            }
            return query;
        }

        /// <summary>
        /// Count all records
        /// </summary>
        public static int Count()
        {
            return Query().Count();
        }

        /// <summary>
        /// Count all records (async)
        /// </summary>
        public static Task<int> CountAsync(CancellationToken cancellationToken = default)
        {
            return Query().CountAsync(cancellationToken: cancellationToken);
        }

        /// <summary>
        /// Delete record by primary key (soft delete when configured). Returns true when a row was affected.
        /// Composite key: pass an object[] in key order.
        /// </summary>
        public static bool Destroy(object id)
        {
            RequirePrimaryKey(GetMetadata<T>());
            return Query().WhereKey(id).Delete() > 0;
        }

        /// <summary>
        /// Delete record by primary key (async)
        /// </summary>
        public static async Task<bool> DestroyAsync(object id, CancellationToken cancellationToken = default)
        {
            RequirePrimaryKey(GetMetadata<T>());
            return await Query().WhereKey(id).DeleteAsync(cancellationToken).ConfigureAwait(false) > 0;
        }

        /// <summary>
        /// Delete multiple records by primary keys. Composite key: either one key (values in key order)
        /// or several keys, each an object[].
        /// </summary>
        public static int Destroy(params object[]? ids)
        {
            if (ids == null || ids.Length == 0)
                return 0;

            var metadata = GetMetadata<T>();
            RequirePrimaryKey(metadata);

            // Composite key given as plain values: Destroy(orderId, productId)
            if (metadata.HasCompositeKey && !ids.All(id => id is object[]))
                ids = new object[] { ids };

            int count = 0;
            foreach (var chunk in Chunk(ids, 1000 / metadata.KeyProperties.Count))
            {
                count += Query().WhereKeyIn(chunk).Delete();
            }
            return count;
        }

        /// <summary>
        /// Delete multiple records by primary keys (list)
        /// </summary>
        public static int Destroy(IEnumerable<object>? ids)
        {
            return Destroy(ids?.ToArray());
        }

        /// <summary>
        /// Get all records including soft deleted
        /// </summary>
        public static List<T> WithTrashed()
        {
            return Query().WithTrashed().Get();
        }

        /// <summary>
        /// Get only soft deleted records
        /// </summary>
        public static List<T> OnlyTrashed()
        {
            if (!GetMetadata<T>().HasSoftDelete)
                return new List<T>();

            return Query().OnlyTrashed().Get();
        }

        /// <summary>
        /// Check if any records exist
        /// </summary>
        public static bool Exists()
        {
            return Query().Exists();
        }

        /// <summary>
        /// Check if record with id exists (soft-deleted records do not count)
        /// </summary>
        public static bool Exists(object id)
        {
            return Query().WhereKey(id).Exists();
        }

        /// <summary>
        /// Check if record with id exists (async)
        /// </summary>
        public static Task<bool> ExistsAsync(object id, CancellationToken cancellationToken = default)
        {
            return Query().WhereKey(id).ExistsAsync(cancellationToken);
        }

        /// <summary>
        /// Get sum of a column
        /// </summary>
        public static decimal Sum(string column)
        {
            return Query().Sum(column);
        }

        /// <summary>
        /// Get average of a column
        /// </summary>
        public static decimal Avg(string column)
        {
            return Query().Avg(column);
        }

        /// <summary>
        /// Get min value of a column
        /// </summary>
        public static object? Min(string column)
        {
            return DbRun.Sync(Query().AggregateValueCore("MIN", column, false, default));
        }

        /// <summary>
        /// Get max value of a column
        /// </summary>
        public static object? Max(string column)
        {
            return DbRun.Sync(Query().AggregateValueCore("MAX", column, false, default));
        }

        /// <summary>
        /// Get sum of a column (async)
        /// </summary>
        public static Task<decimal> SumAsync(string column, CancellationToken cancellationToken = default)
        {
            return Query().SumAsync(column, cancellationToken);
        }

        /// <summary>
        /// Get average of a column (async)
        /// </summary>
        public static Task<decimal> AvgAsync(string column, CancellationToken cancellationToken = default)
        {
            return Query().AvgAsync(column, cancellationToken);
        }

        /// <summary>
        /// Get min value of a column (async)
        /// </summary>
        public static Task<object?> MinAsync(string column, CancellationToken cancellationToken = default)
        {
            return Query().AggregateValueCore("MIN", column, true, cancellationToken);
        }

        /// <summary>
        /// Get max value of a column (async)
        /// </summary>
        public static Task<object?> MaxAsync(string column, CancellationToken cancellationToken = default)
        {
            return Query().AggregateValueCore("MAX", column, true, cancellationToken);
        }

        /// <summary>
        /// Get values of a single column
        /// </summary>
        public static List<object?> Pluck(string column)
        {
            return Query().Pluck(column);
        }

        /// <summary>
        /// Get values of a single column (async)
        /// </summary>
        public static Task<List<object?>> PluckAsync(string column, CancellationToken cancellationToken = default)
        {
            return Query().PluckAsync(column, cancellationToken);
        }

        /// <summary>
        /// Truncate table (delete all records)
        /// </summary>
        public static void Truncate()
        {
            DbRun.Sync(TruncateCore(false, default));
        }

        /// <summary>
        /// Truncate table (delete all records, async)
        /// </summary>
        public static Task TruncateAsync(CancellationToken cancellationToken = default)
        {
            return TruncateCore(true, cancellationToken);
        }

        private static async Task<int> TruncateCore(bool useAsync, CancellationToken ct)
        {
            var metadata = GetMetadata<T>();
            using (var db = CreateCommand(metadata.ConnectionName))
            {
                return await DbRun.NonQuery(db, $"DELETE FROM {SqlDialect.Table(db.ProviderType, metadata.TableName)}", useAsync, ct).ConfigureAwait(false);
            }
        }

        #endregion

        #region Instance Methods

        /// <summary>
        /// Save the model (insert or update). Only changed columns are written for a loaded model.
        /// Returns false when an event handler / observer cancelled it.
        /// Throws <see cref="DbConcurrencyException"/> when a <c>[RowVersion]</c> model was changed by someone else.
        /// </summary>
        public bool Save()
        {
            return DbRun.Sync(SaveCore(false, default));
        }

        /// <summary>
        /// Save the model (async)
        /// </summary>
        public Task<bool> SaveAsync(CancellationToken cancellationToken = default)
        {
            return SaveCore(true, cancellationToken);
        }

        internal override Task<bool> SaveModel(bool useAsync, CancellationToken ct) => SaveCore(useAsync, ct);

        private async Task<bool> SaveCore(bool useAsync, CancellationToken ct)
        {
            // Fire OnMersoSaving event
            if (!FireMersoEvent(MersoEventType.Saving))
                return false;

            var metadata = GetMetadata();
            bool written = false;

            if (await IsNewRecordCore(metadata, useAsync, ct).ConfigureAwait(false))
            {
                // Fire OnMersoCreating event
                if (!FireMersoEvent(MersoEventType.Creating))
                    return false;

                await InsertCore(metadata, useAsync, ct).ConfigureAwait(false);
                written = true;

                // Fire OnMersoCreated event
                FireMersoEvent(MersoEventType.Created);
            }
            else
            {
                var dirty = GetDirtyProperties(metadata);
                if (dirty == null || dirty.Count > 0)
                {
                    // Fire OnMersoUpdating event
                    if (!FireMersoEvent(MersoEventType.Updating))
                        return false;

                    await UpdateCore(metadata, useAsync, ct).ConfigureAwait(false);
                    written = true;

                    // Fire OnMersoUpdated event
                    FireMersoEvent(MersoEventType.Updated);
                }
            }

            // Parents ([Touches]) — before SyncOriginal so a changed foreign key touches the old parent too
            if (written)
                await TouchOwnersCore(metadata, useAsync, ct).ConfigureAwait(false);

            // Fire OnMersoSaved event
            FireMersoEvent(MersoEventType.Saved);
            SyncOriginal();
            return true;
        }

        /// <summary>
        /// Delete this record (soft delete when configured). Returns false when cancelled.
        /// </summary>
        public bool Delete()
        {
            return DbRun.Sync(DeleteCore(false, default));
        }

        /// <summary>
        /// Delete this record (async)
        /// </summary>
        public Task<bool> DeleteAsync(CancellationToken cancellationToken = default)
        {
            return DeleteCore(true, cancellationToken);
        }

        private async Task<bool> DeleteCore(bool useAsync, CancellationToken ct)
        {
            // Fire OnMersoDeleting event
            if (!FireMersoEvent(MersoEventType.Deleting))
                return false;

            var metadata = GetMetadata();
            RequirePrimaryKey(metadata);
            var version = metadata.VersionProperty;
            var now = DateTime.UtcNow;

            using (var db = NewCommand(metadata))
            {
                var p = db.ProviderType;
                var table = SqlDialect.Table(p, metadata.TableName);
                var versionColumn = version != null ? SqlDialect.Column(p, version.ColumnName) : null;

                string sql;
                if (metadata.HasSoftDelete)
                {
                    var deleted = SqlDialect.Column(p, metadata.SoftDeleteColumn);
                    var bump = version != null ? $", {versionColumn} = {versionColumn} + 1" : string.Empty;
                    sql = $"UPDATE {table} SET {deleted} = @now{bump} WHERE {KeyWhere(db, metadata)} AND {deleted} IS NULL";
                    db.ParametersAdd("@now", now);
                }
                else
                {
                    sql = $"DELETE FROM {table} WHERE {KeyWhere(db, metadata)}";
                }

                object? expected = null;
                if (version != null)
                {
                    expected = OriginalValue(version);
                    sql += $" AND {versionColumn} = @__ver";
                    db.ParametersAdd("@__ver", expected);
                }

                var affected = await DbRun.NonQuery(db, sql, useAsync, ct).ConfigureAwait(false);
                if (version != null)
                {
                    if (affected == 0)
                        throw ConcurrencyConflict(metadata, expected);
                    if (metadata.HasSoftDelete)
                    {
                        SetVersion(version, expected);
                        SyncOriginal(version);
                    }
                }
            }

            await TouchOwnersCore(metadata, useAsync, ct).ConfigureAwait(false);

            if (metadata.HasSoftDelete)
            {
                var softProp = metadata.Properties.FirstOrDefault(x => x.PropertyInfo == metadata.SoftDeleteProperty);
                ModelMapper.TrySetProperty(metadata.SoftDeleteProperty, this, now);
                SyncOriginal(softProp);
            }
            else
            {
                ForgetOriginal();
            }

            // Fire OnMersoDeleted event
            FireMersoEvent(MersoEventType.Deleted);
            return true;
        }

        /// <summary>
        /// Refresh model from database
        /// </summary>
        public void Refresh()
        {
            DbRun.Sync(RefreshCore(false, default));
        }

        /// <summary>
        /// Refresh model from database (async)
        /// </summary>
        public Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            return RefreshCore(true, cancellationToken);
        }

        private async Task<bool> RefreshCore(bool useAsync, CancellationToken ct)
        {
            var metadata = GetMetadata();
            var fresh = await InstanceQuery(metadata).GetFirstCoreAsync(useAsync, ct).ConfigureAwait(false);
            if (fresh == null)
                return false;

            foreach (var prop in metadata.Properties)
            {
                var value = prop.PropertyInfo.GetValue(fresh);
                prop.PropertyInfo.SetValue(this, value);
            }
            SyncOriginal();
            return true;
        }

        /// <summary>
        /// Set UpdatedAt to now (and touch [Touches] parents). Returns false when the model has no [UpdatedAt].
        /// </summary>
        public bool Touch()
        {
            return DbRun.Sync(TouchCore(false, default));
        }

        /// <summary>
        /// Set UpdatedAt to now (async)
        /// </summary>
        public Task<bool> TouchAsync(CancellationToken cancellationToken = default)
        {
            return TouchCore(true, cancellationToken);
        }

        private async Task<bool> TouchCore(bool useAsync, CancellationToken ct)
        {
            var metadata = GetMetadata();
            var updated = metadata.Properties.FirstOrDefault(x => x.PropertyInfo == metadata.UpdatedAtProperty);
            if (updated == null)
                return false;

            RequirePrimaryKey(metadata);
            var now = DateTime.UtcNow;

            using (var db = NewCommand(metadata))
            {
                var p = db.ProviderType;
                db.ParametersAdd("@now", now);
                var sql = $"UPDATE {SqlDialect.Table(p, metadata.TableName)} SET {SqlDialect.Column(p, updated.ColumnName)} = @now WHERE {KeyWhere(db, metadata)}";
                await DbRun.NonQuery(db, sql, useAsync, ct).ConfigureAwait(false);
            }

            ModelMapper.TrySetProperty(updated.PropertyInfo, this, now);
            SyncOriginal(updated);
            await TouchOwnersCore(metadata, useAsync, ct).ConfigureAwait(false);
            return true;
        }

        /// <summary>
        /// Convert model to dictionary (column name → value; [Hidden] properties left out)
        /// </summary>
        public Dictionary<string, object?> ToDict()
        {
            var metadata = GetMetadata();
            var dict = new Dictionary<string, object?>();

            foreach (var prop in metadata.Properties.Concat(metadata.ComputedProperties))
            {
                if (IsHiddenProperty(prop))
                    continue;
                dict[prop.ColumnName] = prop.GetValue(this);
            }

            return dict;
        }

        /// <summary>
        /// Convert model to array (alias for ToDict)
        /// </summary>
        public Dictionary<string, object?> ToArray()
        {
            return ToDict();
        }

        /// <summary>
        /// Convert model to JSON string ([Hidden] properties left out)
        /// </summary>
        public string ToJson()
        {
            var dict = ToDict();
            return System.Text.Json.JsonSerializer.Serialize(dict);
        }

        /// <summary>
        /// Show [Hidden] properties of this instance in ToDict / ToJson
        /// </summary>
        public T MakeVisible(params string[] properties)
        {
            SetVisibility(properties, true);
            return (T)(object)this;
        }

        /// <summary>
        /// Hide properties of this instance in ToDict / ToJson
        /// </summary>
        public T MakeHidden(params string[] properties)
        {
            SetVisibility(properties, false);
            return (T)(object)this;
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
        /// Restore soft deleted record. Returns false when cancelled or the model has no soft delete.
        /// </summary>
        public bool Restore()
        {
            return DbRun.Sync(RestoreCore(false, default));
        }

        /// <summary>
        /// Restore soft deleted record (async)
        /// </summary>
        public Task<bool> RestoreAsync(CancellationToken cancellationToken = default)
        {
            return RestoreCore(true, cancellationToken);
        }

        private async Task<bool> RestoreCore(bool useAsync, CancellationToken ct)
        {
            var metadata = GetMetadata();
            if (!metadata.HasSoftDelete)
                return false;

            // Fire OnMersoRestoring event
            if (!FireMersoEvent(MersoEventType.Restoring))
                return false;

            metadata.SoftDeleteProperty?.SetValue(this, null);

            using (var db = NewCommand(metadata))
            {
                var p = db.ProviderType;
                var sql = $"UPDATE {SqlDialect.Table(p, metadata.TableName)} SET {SqlDialect.Column(p, metadata.SoftDeleteColumn)} = NULL WHERE {KeyWhere(db, metadata)}";
                await DbRun.NonQuery(db, sql, useAsync, ct).ConfigureAwait(false);
            }
            SyncOriginal(metadata.Properties.FirstOrDefault(x => x.PropertyInfo == metadata.SoftDeleteProperty));
            await TouchOwnersCore(metadata, useAsync, ct).ConfigureAwait(false);

            // Fire OnMersoRestored event
            FireMersoEvent(MersoEventType.Restored);
            return true;
        }

        /// <summary>
        /// Permanently delete record (bypass soft delete). Returns true when a row was removed.
        /// </summary>
        public bool ForceDelete()
        {
            return DbRun.Sync(ForceDeleteCore(false, default));
        }

        /// <summary>
        /// Permanently delete record (async)
        /// </summary>
        public Task<bool> ForceDeleteAsync(CancellationToken cancellationToken = default)
        {
            return ForceDeleteCore(true, cancellationToken);
        }

        private async Task<bool> ForceDeleteCore(bool useAsync, CancellationToken ct)
        {
            var metadata = GetMetadata();
            RequirePrimaryKey(metadata);

            bool removed;
            using (var db = NewCommand(metadata))
            {
                var p = db.ProviderType;
                var sql = $"DELETE FROM {SqlDialect.Table(p, metadata.TableName)} WHERE {KeyWhere(db, metadata)}";
                removed = await DbRun.NonQuery(db, sql, useAsync, ct).ConfigureAwait(false) > 0;
            }

            if (removed)
                await TouchOwnersCore(metadata, useAsync, ct).ConfigureAwait(false);
            ForgetOriginal();
            return removed;
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
        /// Increment a column value in the database and reload the model
        /// </summary>
        public void Increment(string column, int amount = 1)
        {
            DbRun.Sync(IncrementCore(column, amount, false, default));
        }

        /// <summary>
        /// Decrement a column value in the database and reload the model
        /// </summary>
        public void Decrement(string column, int amount = 1)
        {
            Increment(column, -amount);
        }

        /// <summary>
        /// Increment a column value (async)
        /// </summary>
        public Task IncrementAsync(string column, int amount = 1, CancellationToken cancellationToken = default)
        {
            return IncrementCore(column, amount, true, cancellationToken);
        }

        /// <summary>
        /// Decrement a column value (async)
        /// </summary>
        public Task DecrementAsync(string column, int amount = 1, CancellationToken cancellationToken = default)
        {
            return IncrementCore(column, -amount, true, cancellationToken);
        }

        private async Task<bool> IncrementCore(string column, int amount, bool useAsync, CancellationToken ct)
        {
            var metadata = GetMetadata();
            RequirePrimaryKey(metadata);

            using (var db = NewCommand(metadata))
            {
                var p = db.ProviderType;
                var col = SqlDialect.Column(p, ColumnNameOf(metadata, column));
                db.ParametersAdd("@amount", amount);
                var sql = $"UPDATE {SqlDialect.Table(p, metadata.TableName)} SET {col} = {col} + @amount WHERE {KeyWhere(db, metadata)}";
                await DbRun.NonQuery(db, sql, useAsync, ct).ConfigureAwait(false);
            }

            return await RefreshCore(useAsync, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Fill model with data (mass assignment): keys of [Guarded] properties — and, when the model has
        /// [Fillable] properties, every key that is not [Fillable] — are skipped
        /// (or throw <see cref="MassAssignmentException"/> with <see cref="ModelBase.StrictMassAssignment"/>)
        /// </summary>
        public T Fill(Dictionary<string, object?>? data)
        {
            if (data == null)
                return (T)(object)this;

            var metadata = GetMetadata();
            foreach (var kvp in data)
            {
                var prop = FindProperty(metadata, kvp.Key);
                var allowed = prop != null ? metadata.IsMassAssignable(prop) : !metadata.HasFillableList;
                if (!allowed)
                {
                    if (StrictMassAssignment)
                        throw new MassAssignmentException($"'{kvp.Key}' cannot be mass assigned on {metadata.ModelType.Name}. Add [Fillable] or set it in code / with ForceFill().");
                    continue;
                }

                SetProperty(kvp.Key, kvp.Value);
            }
            return (T)(object)this;
        }

        /// <summary>
        /// Fill model with data ignoring [Fillable] / [Guarded] (trusted input only)
        /// </summary>
        public T ForceFill(Dictionary<string, object?>? data)
        {
            if (data == null)
                return (T)(object)this;

            foreach (var kvp in data)
            {
                SetProperty(kvp.Key, kvp.Value);
            }
            return (T)(object)this;
        }

        /// <summary>
        /// Get fresh instance from database
        /// </summary>
        public T? Fresh()
        {
            return DbRun.Sync(InstanceQuery(GetMetadata()).GetFirstCoreAsync(false, default));
        }

        /// <summary>
        /// Get fresh instance from database (async)
        /// </summary>
        public Task<T?> FreshAsync(CancellationToken cancellationToken = default)
        {
            return InstanceQuery(GetMetadata()).GetFirstCoreAsync(true, cancellationToken);
        }

        /// <summary>
        /// Check if model exists in database (soft-deleted rows count as existing)
        /// </summary>
        public bool ExistsInDb()
        {
            var metadata = GetMetadata();
            return HasKeyValues(metadata) && InstanceQuery(metadata).Exists();
        }

        /// <summary>
        /// Get only specified attributes
        /// </summary>
        public Dictionary<string, object?> Only(params string[] keys)
        {
            var dict = ToDict();
            return dict.Where(kvp => keys.Contains(kvp.Key, StringComparer.OrdinalIgnoreCase))
                       .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        /// <summary>
        /// Get all attributes except specified ones
        /// </summary>
        public Dictionary<string, object?> Except(params string[] keys)
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
        protected HasOne<T, TRelated> HasOne<TRelated>(string? foreignKey = null, string localKey = "Id")
            where TRelated : Model<TRelated>, new()
        {
            return new HasOne<T, TRelated>((T)(object)this, foreignKey, localKey);
        }

        /// <summary>
        /// Define a HasMany relationship (1:N)
        /// </summary>
        protected HasMany<T, TRelated> HasMany<TRelated>(string? foreignKey = null, string localKey = "Id")
            where TRelated : Model<TRelated>, new()
        {
            return new HasMany<T, TRelated>((T)(object)this, foreignKey, localKey);
        }

        /// <summary>
        /// Define a BelongsTo relationship (N:1)
        /// </summary>
        protected BelongsTo<T, TRelated> BelongsTo<TRelated>(string? foreignKey = null, string ownerKey = "Id")
            where TRelated : Model<TRelated>, new()
        {
            return new BelongsTo<T, TRelated>((T)(object)this, foreignKey, ownerKey);
        }

        /// <summary>
        /// Define a BelongsToMany relationship (N:M)
        /// </summary>
        protected BelongsToMany<T, TRelated> BelongsToMany<TRelated>(
            string? pivotTable = null,
            string? parentKey = null,
            string? relatedKey = null,
            string localKey = "Id")
            where TRelated : Model<TRelated>, new()
        {
            return new BelongsToMany<T, TRelated>((T)(object)this, pivotTable, parentKey, relatedKey, localKey);
        }

        /// <summary>
        /// Define a HasManyThrough relationship: Country → Users → Posts is
        /// <c>HasManyThrough&lt;Post, User&gt;()</c> (users.CountryId, posts.UserId)
        /// </summary>
        /// <param name="firstKey">Foreign key on the through table pointing to this model (default: {ThisModel}Id)</param>
        /// <param name="secondKey">Foreign key on the related table pointing to the through table (default: {Through}Id)</param>
        /// <param name="localKey">Key of this model (default Id)</param>
        /// <param name="secondLocalKey">Key of the through model (default Id)</param>
        protected HasManyThrough<T, TThrough, TRelated> HasManyThrough<TRelated, TThrough>(
            string? firstKey = null, string? secondKey = null, string localKey = "Id", string secondLocalKey = "Id")
            where TRelated : Model<TRelated>, new()
            where TThrough : Model<TThrough>, new()
        {
            return new HasManyThrough<T, TThrough, TRelated>((T)(object)this, firstKey, secondKey, localKey, secondLocalKey);
        }

        /// <summary>
        /// Define a HasOneThrough relationship (first row of <see cref="HasManyThrough{TRelated, TThrough}"/>)
        /// </summary>
        protected HasOneThrough<T, TThrough, TRelated> HasOneThrough<TRelated, TThrough>(
            string? firstKey = null, string? secondKey = null, string localKey = "Id", string secondLocalKey = "Id")
            where TRelated : Model<TRelated>, new()
            where TThrough : Model<TThrough>, new()
        {
            return new HasOneThrough<T, TThrough, TRelated>((T)(object)this, firstKey, secondKey, localKey, secondLocalKey);
        }

        /// <summary>
        /// Polymorphic one-to-many: <c>MorphMany&lt;Comment&gt;("Commentable")</c> reads comments whose
        /// CommentableType is this model's morph name and CommentableId is this model's key
        /// </summary>
        protected MorphMany<T, TRelated> MorphMany<TRelated>(string name, string localKey = "Id")
            where TRelated : Model<TRelated>, new()
        {
            return new MorphMany<T, TRelated>((T)(object)this, name, localKey);
        }

        /// <summary>
        /// Polymorphic one-to-one (see <see cref="MorphMany{TRelated}"/>)
        /// </summary>
        protected MorphOne<T, TRelated> MorphOne<TRelated>(string name, string localKey = "Id")
            where TRelated : Model<TRelated>, new()
        {
            return new MorphOne<T, TRelated>((T)(object)this, name, localKey);
        }

        /// <summary>
        /// Inverse of a polymorphic relation: <c>MorphTo("Commentable")</c> on a comment returns its post or video
        /// </summary>
        protected MorphTo<T> MorphTo(string name)
        {
            return new MorphTo<T>((T)(object)this, name);
        }

        #endregion

        #region Private Methods

        // Instance operations go to the connection the model came from (On(name)), else its [Connection], else scoped / default
        private DbCommandBase NewCommand(ModelMetadata metadata)
        {
            return CreateCommand(LoadedConnection ?? metadata.ConnectionName);
        }

        // "k1 = @__k0 AND k2 = @__k1" with the key values as loaded (a changed key still finds its row)
        private string KeyWhere(DbCommandBase db, ModelMetadata metadata)
        {
            var p = db.ProviderType;
            var parts = new List<string>(metadata.KeyProperties.Count);
            for (int i = 0; i < metadata.KeyProperties.Count; i++)
            {
                var key = metadata.KeyProperties[i];
                var name = "@__k" + i.ToString(CultureInfo.InvariantCulture);
                parts.Add($"{SqlDialect.Column(p, key.ColumnName)} = {name}");
                db.ParametersAdd(name, OriginalValue(key));
            }
            return string.Join(" AND ", parts);
        }

        private object?[] KeyValuesOf(ModelMetadata metadata)
        {
            return metadata.KeyProperties.Select(OriginalValue).ToArray();
        }

        private bool HasKeyValues(ModelMetadata metadata)
        {
            if (metadata.KeyProperties.Count == 0)
                return false;
            if (metadata.HasCompositeKey)
                return metadata.KeyProperties.All(k => k.PropertyInfo.GetValue(this) != null);
            return !ModelMapper.IsDefaultKey(metadata.KeyProperties[0].PropertyInfo.GetValue(this));
        }

        // This row by primary key, ignoring soft delete and global scopes
        private QueryBuilder<T> InstanceQuery(ModelMetadata metadata)
        {
            RequirePrimaryKey(metadata);
            // Instance reads (Refresh, Fresh, the insert-or-update decision of Save) never use a read replica
            return new QueryBuilder<T>()
                .UseConnection(LoadedConnection)
                .OnPrimary()
                .WithoutGlobalScopes()
                .WithTrashed()
                .WhereKey((object)KeyValuesOf(metadata));
        }

        private async Task<bool> IsNewRecordCore(ModelMetadata metadata, bool useAsync, CancellationToken ct)
        {
            if (metadata.KeyProperties.Count == 0)
                return true;

            // Loaded (or already saved) models exist
            if (HasOriginal && HasKeyValues(metadata))
                return false;

            if (!metadata.HasCompositeKey && ModelMapper.IsDefaultKey(GetPrimaryKeyValue(metadata)))
                return true;
            if (metadata.HasCompositeKey && !HasKeyValues(metadata))
                return true;

            // Application-assigned keys (Guid, codes, composite): insert unless the row is already there
            if (!metadata.PrimaryKeyAutoIncrement)
                return !await InstanceQuery(metadata).ExistsCoreAsync(useAsync, ct).ConfigureAwait(false);

            return false;
        }

        private async Task InsertCore(ModelMetadata metadata, bool useAsync, CancellationToken ct)
        {
            // Set timestamps
            var now = DateTime.UtcNow;
            if (metadata.CreatedAtProperty != null)
            {
                metadata.CreatedAtProperty.SetValue(this, now);
            }
            if (metadata.UpdatedAtProperty != null)
            {
                metadata.UpdatedAtProperty.SetValue(this, now);
            }

            // Application generated Guid keys
            var pkProp = metadata.PrimaryKeyProperty;
            if (pkProp != null && !metadata.PrimaryKeyAutoIncrement
                && (Nullable.GetUnderlyingType(pkProp.PropertyType) ?? pkProp.PropertyType) == typeof(Guid)
                && ModelMapper.IsDefaultKey(pkProp.GetValue(this)))
            {
                pkProp.SetValue(this, Guid.NewGuid());
            }

            // Row version starts at 1
            if (metadata.VersionProperty != null && ModelMapper.IsDefaultKey(metadata.VersionProperty.PropertyInfo.GetValue(this)))
            {
                ModelMapper.TrySetProperty(metadata.VersionProperty.PropertyInfo, this, 1);
            }

            using (var db = NewCommand(metadata))
            {
                var p = db.ProviderType;
                var columns = new List<string>();
                var parameters = new List<string>();

                foreach (var prop in metadata.Properties)
                {
                    if (prop.IsPrimaryKey && prop.IsAutoIncrement)
                        continue;

                    var value = prop.GetDbValue(this);
                    if (value != null || !prop.Nullable)
                    {
                        var name = "@c" + parameters.Count.ToString(CultureInfo.InvariantCulture);
                        columns.Add(SqlDialect.Column(p, prop.ColumnName));
                        parameters.Add(name);
                        db.ParametersAdd(name, value);
                    }
                }

                var table = SqlDialect.Table(p, metadata.TableName);
                var sql = columns.Count > 0
                    ? $"INSERT INTO {table} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", parameters)})"
                    : DefaultValuesInsert(p, table);

                if (pkProp != null && metadata.PrimaryKeyAutoIncrement)
                {
                    // Same connection as the INSERT: concurrent inserts can't hand back each other's id
                    var newId = await DbRun.InsertGetId(db, sql, SqlDialect.Column(p, metadata.PrimaryKeyColumn!), useAsync, ct).ConfigureAwait(false);
                    ModelMapper.TrySetProperty(pkProp, this, newId);
                }
                else
                {
                    await DbRun.NonQuery(db, sql, useAsync, ct).ConfigureAwait(false);
                }
            }
        }

        private static string DefaultValuesInsert(DbProviderType provider, string quotedTable)
        {
            switch (provider)
            {
                case DbProviderType.MySQL:
                case DbProviderType.MariaDB:
                    return $"INSERT INTO {quotedTable} () VALUES ()";
                default:
                    return $"INSERT INTO {quotedTable} DEFAULT VALUES";
            }
        }

        private async Task UpdateCore(ModelMetadata metadata, bool useAsync, CancellationToken ct)
        {
            RequirePrimaryKey(metadata);

            // Set updated timestamp
            if (metadata.UpdatedAtProperty != null)
            {
                metadata.UpdatedAtProperty.SetValue(this, DateTime.UtcNow);
            }

            // Loaded models write only changed columns (an untouched column keeps what others saved)
            var dirty = GetDirtyProperties(metadata);
            var version = metadata.VersionProperty;

            using (var db = NewCommand(metadata))
            {
                var p = db.ProviderType;
                var setClauses = new List<string>();

                foreach (var prop in metadata.Properties)
                {
                    if (prop.IsPrimaryKey || prop == version)
                        continue;
                    if (dirty != null && !dirty.Contains(prop))
                        continue;

                    var name = "@c" + setClauses.Count.ToString(CultureInfo.InvariantCulture);
                    setClauses.Add($"{SqlDialect.Column(p, prop.ColumnName)} = {name}");
                    db.ParametersAdd(name, prop.GetDbValue(this));
                }

                string? versionColumn = null;
                object? expected = null;
                if (version != null)
                {
                    versionColumn = SqlDialect.Column(p, version.ColumnName);
                    expected = OriginalValue(version);
                    setClauses.Add($"{versionColumn} = {versionColumn} + 1");
                }

                if (setClauses.Count == 0)
                {
                    db.ParametersClear();
                    return;
                }

                var sql = $"UPDATE {SqlDialect.Table(p, metadata.TableName)} SET {string.Join(", ", setClauses)} WHERE {KeyWhere(db, metadata)}";

                if (version != null)
                {
                    sql += $" AND {versionColumn} = @__ver";
                    db.ParametersAdd("@__ver", expected);
                }

                var affected = await DbRun.NonQuery(db, sql, useAsync, ct).ConfigureAwait(false);

                if (version != null)
                {
                    if (affected == 0)
                        throw ConcurrencyConflict(metadata, expected);
                    SetVersion(version, expected);
                }
            }
        }

        // [Touches] parents: UPDATE parent SET UpdatedAt = now WHERE key IN (current fk, previous fk)
        // (composite parent key: the foreign key lists its columns in key order, "OrderId, LineNo")
        private async Task TouchOwnersCore(ModelMetadata metadata, bool useAsync, CancellationToken ct)
        {
            foreach (var touch in metadata.Touches)
            {
                var parent = ModelMetadata.GetMetadata(touch.ParentType);
                var updated = parent.Properties.FirstOrDefault(x => x.PropertyInfo == parent.UpdatedAtProperty);
                if (updated == null || parent.KeyProperties.Count == 0)
                    continue;

                var fkNames = RelationKeys.Split(touch.ForeignKey);
                if (fkNames.Length != parent.KeyProperties.Count)
                    throw new InvalidOperationException(
                        $"[Touches] on {metadata.ModelType.Name}: {parent.ModelType.Name} has {parent.KeyProperties.Count} key column(s), the foreign key '{touch.ForeignKey}' {fkNames.Length}.");
                var fks = fkNames.Select(name => FindProperty(metadata, name)
                          ?? throw new InvalidOperationException($"[Touches] on {metadata.ModelType.Name}: no property '{name}'.")).ToList();

                var keys = new List<object?[]>();
                var current = fks.Select(fk => fk.PropertyInfo.GetValue(this)).ToArray();
                if (RelationKeys.Complete(current))
                    keys.Add(current);
                var previous = fks.Select(fk => OriginalValue(fk)).ToArray();
                if (RelationKeys.Complete(previous) && RelationKeys.KeyOf(previous) != RelationKeys.KeyOf(current))
                    keys.Add(previous);
                if (keys.Count == 0)
                    continue;

                using (var db = CreateCommand(parent.ConnectionName ?? LoadedConnection))
                {
                    var p = db.ProviderType;
                    int n = 0;
                    string Param(object? value)
                    {
                        var name = "@t" + (n++).ToString(CultureInfo.InvariantCulture);
                        db.ParametersAdd(name, value);
                        return name;
                    }

                    var where = parent.KeyProperties.Count == 1
                        ? $"{SqlDialect.Column(p, parent.KeyProperties[0].ColumnName)} IN ({string.Join(", ", keys.Select(k => Param(k[0])))})"
                        : string.Join(" OR ", keys.Select(k => "(" + string.Join(" AND ",
                            parent.KeyProperties.Select((kp, i) => $"{SqlDialect.Column(p, kp.ColumnName)} = {Param(k[i])}")) + ")"));

                    db.ParametersAdd("@now", DateTime.UtcNow);
                    var sql = $"UPDATE {SqlDialect.Table(p, parent.TableName)} SET {SqlDialect.Column(p, updated.ColumnName)} = @now WHERE {where}";
                    await DbRun.NonQuery(db, sql, useAsync, ct).ConfigureAwait(false);
                }
            }
        }

        private void SetVersion(PropertyMetadata version, object? expected)
        {
            var next = Convert.ToInt64(expected ?? 0, CultureInfo.InvariantCulture) + 1;
            ModelMapper.TrySetProperty(version.PropertyInfo, this, next);
        }

        private DbConcurrencyException ConcurrencyConflict(ModelMetadata metadata, object? expectedVersion)
        {
            return new DbConcurrencyException(
                $"{metadata.ModelType.Name} {KeyText(KeyValuesOf(metadata))} was changed or deleted by another user (expected version {expectedVersion}). Reload it and try again.");
        }

        private static string KeyText(object? id)
        {
            return id is object?[] values
                ? (values.Length == 1 ? Convert.ToString(values[0], CultureInfo.InvariantCulture) ?? string.Empty : "(" + string.Join(", ", values) + ")")
                : Convert.ToString(id, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static string ColumnNameOf(ModelMetadata metadata, string name)
        {
            return FindProperty(metadata, name)?.ColumnName ?? name;
        }

        private object? GetPrimaryKeyValue(ModelMetadata metadata)
        {
            return metadata.PrimaryKeyProperty?.GetValue(this);
        }

        private static void RequirePrimaryKey(ModelMetadata metadata)
        {
            if (metadata.KeyProperties.Count == 0)
                throw new InvalidOperationException($"Model {metadata.ModelType.Name} has no primary key. Add [PrimaryKey] or an Id property.");
        }

        private static ModelNotFoundException NotFound(object? id)
        {
            return new ModelNotFoundException($"Record with id {KeyText(id)} not found in {GetMetadata<T>().TableName}");
        }

        private static IEnumerable<List<object>> Chunk(object[] source, int size)
        {
            size = Math.Max(1, size);
            for (int i = 0; i < source.Length; i += size)
            {
                yield return source.Skip(i).Take(size).ToList();
            }
        }

        /// <summary>
        /// Fire model events (IMersoEvents) and registered observers - returns false if any of them cancels
        /// </summary>
        private bool FireMersoEvent(MersoEventType eventType)
        {
            // Check if model implements IMersoEvents
            if (this is IMersoEvents events)
            {
                switch (eventType)
                {
                    case MersoEventType.Creating:
                        if (!events.OnMersoCreating()) return false;
                        break;
                    case MersoEventType.Created:
                        events.OnMersoCreated();
                        break;
                    case MersoEventType.Updating:
                        if (!events.OnMersoUpdating()) return false;
                        break;
                    case MersoEventType.Updated:
                        events.OnMersoUpdated();
                        break;
                    case MersoEventType.Deleting:
                        if (!events.OnMersoDeleting()) return false;
                        break;
                    case MersoEventType.Deleted:
                        events.OnMersoDeleted();
                        break;
                    case MersoEventType.Saving:
                        if (!events.OnMersoSaving()) return false;
                        break;
                    case MersoEventType.Saved:
                        events.OnMersoSaved();
                        break;
                    case MersoEventType.Restoring:
                        if (!events.OnMersoRestoring()) return false;
                        break;
                    case MersoEventType.Restored:
                        events.OnMersoRestored();
                        break;
                }
            }

            // Observers registered with ObserverManager.Register(...)
            var self = (T)(object)this;
            switch (eventType)
            {
                case MersoEventType.Creating: return ObserverManager.FireCreating(self);
                case MersoEventType.Created: ObserverManager.FireCreated(self); return true;
                case MersoEventType.Updating: return ObserverManager.FireUpdating(self);
                case MersoEventType.Updated: ObserverManager.FireUpdated(self); return true;
                case MersoEventType.Deleting: return ObserverManager.FireDeleting(self);
                case MersoEventType.Deleted: ObserverManager.FireDeleted(self); return true;
                case MersoEventType.Saving: return ObserverManager.FireSaving(self);
                case MersoEventType.Saved: ObserverManager.FireSaved(self); return true;
                case MersoEventType.Restoring: return ObserverManager.FireRestoring(self);
                case MersoEventType.Restored: ObserverManager.FireRestored(self); return true;
                default: return true;
            }
        }

        /// <summary>
        /// Set a property by property name or column name (values are converted to the property type)
        /// </summary>
        internal void SetProperty(string name, object? value)
        {
            var meta = FindProperty(GetMetadata(), name);

            var prop = meta?.PropertyInfo
                       ?? GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (prop == null || !prop.CanWrite)
                return;

            if (value == null)
            {
                if (!prop.PropertyType.IsValueType || Nullable.GetUnderlyingType(prop.PropertyType) != null)
                    prop.SetValue(this, null);
                return;
            }

            ModelMapper.TrySetProperty(prop, this, value);
        }

        #endregion
    }
}
