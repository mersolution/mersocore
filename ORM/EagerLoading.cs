using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using mersolutionCore.Command.Abstractions;
using mersolutionCore.ORM.Relationships;

namespace mersolutionCore.ORM
{
    /// <summary>
    /// Eager Loading - İlişkileri önceden yükle ([HasMany], [HasOne], [BelongsTo], [MorphMany], [MorphOne], [MorphTo]
    /// özellikleri). İç içe yükleme desteklenir: With("Orders.Items"); koşullu: With&lt;Order&gt;("Orders", q =&gt; q.Where(...)).
    /// </summary>
    public class EagerLoader<T> where T : Model<T>, new()
    {
        private readonly List<KeyValuePair<string, ISubQuery?>> _includes = new List<KeyValuePair<string, ISubQuery?>>();
        private readonly QueryBuilder<T> _query;

        public EagerLoader(QueryBuilder<T> query)
        {
            _query = query;
        }

        /// <summary>
        /// İlişki ekle
        /// </summary>
        public EagerLoader<T> With(string relation)
        {
            _includes.Add(new KeyValuePair<string, ISubQuery?>(relation, null));
            return this;
        }

        /// <summary>
        /// İlişkiyi koşulla / sıralamayla yükle (son ilişkiye uygulanır):
        /// <c>With&lt;Order&gt;("Orders", q =&gt; q.Where("Status", "paid").OrderByDesc("Id"))</c>.
        /// Take / Skip ilişki başına uygulanmaz.
        /// </summary>
        public EagerLoader<T> With<TRelated>(string relation, Action<QueryBuilder<TRelated>> constraint) where TRelated : Model<TRelated>, new()
        {
            if (constraint == null) throw new ArgumentNullException(nameof(constraint));

            EagerLoadEngine.CheckRelatedType(typeof(T), relation, typeof(TRelated));
            var query = new QueryBuilder<TRelated>();
            constraint(query);
            _includes.Add(new KeyValuePair<string, ISubQuery?>(relation, query));
            return this;
        }

        /// <summary>
        /// Sorguyu çalıştır ve ilişkileri yükle
        /// </summary>
        public List<T> Get()
        {
            return DbRun.Sync(GetCore(false, default));
        }

        /// <summary>
        /// Sorguyu çalıştır ve ilişkileri yükle (async)
        /// </summary>
        public Task<List<T>> GetAsync(CancellationToken cancellationToken = default)
        {
            return GetCore(true, cancellationToken);
        }

        /// <summary>
        /// İlk kaydı getir
        /// </summary>
        public T? First()
        {
            _query.Take(1);
            return Get().FirstOrDefault();
        }

        /// <summary>
        /// İlk kaydı getir (async)
        /// </summary>
        public async Task<T?> FirstAsync(CancellationToken cancellationToken = default)
        {
            _query.Take(1);
            return (await GetAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault();
        }

        private async Task<List<T>> GetCore(bool useAsync, CancellationToken ct)
        {
            var results = await _query.GetCoreAsync(useAsync, ct).ConfigureAwait(false);

            foreach (var include in _includes)
            {
                await EagerLoadEngine.Load(typeof(T), results.Cast<object>().ToList(), include.Key, _query.ExplicitConnection, useAsync, ct, include.Value).ConfigureAwait(false);
            }

            return results;
        }
    }

    /// <summary>
    /// Non-generic loader so nested relations can continue on the related type
    /// </summary>
    internal static class EagerLoadEngine
    {
        // Stay below provider parameter limits (SQL Server: 2100)
        private const int MaxInValues = 1000;

        /// <param name="connectionName">On(name) of the parent query; a related model with [Connection] keeps its own</param>
        /// <param name="modelType">Type of the loaded models</param>
        /// <param name="models">Loaded models</param>
        /// <param name="relationPath">Relation property, nested with dots: "Orders.Items"</param>
        /// <param name="useAsync">Run async ADO.NET calls</param>
        /// <param name="ct">Cancellation</param>
        /// <param name="constraint">Conditions / order for the last relation of the path</param>
        internal static async Task Load(Type modelType, List<object> models, string relationPath, string? connectionName, bool useAsync,
            CancellationToken ct, ISubQuery? constraint = null)
        {
            if (models.Count == 0 || string.IsNullOrWhiteSpace(relationPath))
                return;

            var parts = relationPath.Split(new[] { '.' }, 2);
            var relation = RelationInfo.Find(modelType, parts[0]);
            if (relation == null)
                return;

            var own = parts.Length == 1 ? constraint : null;
            List<object> loaded;
            switch (relation.Kind)
            {
                case RelationKind.BelongsTo:
                    loaded = await LoadOwners(relation, models, connectionName, useAsync, ct, own).ConfigureAwait(false);
                    break;
                case RelationKind.MorphTo:
                    if (own != null)
                        throw new NotSupportedException($"{relation.Property.Name}: MorphTo cannot be loaded with conditions (the owners have different types).");
                    loaded = await LoadMorphTo(relation, models, connectionName, useAsync, ct).ConfigureAwait(false);
                    break;
                default:
                    loaded = await LoadChildren(relation, models, connectionName, useAsync, ct, own).ConfigureAwait(false);
                    break;
            }

            // Nested: "Orders.Items" → load Items for every loaded order (MorphTo: per owner type)
            if (parts.Length > 1 && loaded.Count > 0)
            {
                foreach (var group in loaded.GroupBy(m => m.GetType()))
                    await Load(group.Key, group.ToList(), parts[1], connectionName, useAsync, ct, constraint).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// The last relation of <paramref name="relationPath"/> must load <paramref name="relatedType"/>
        /// </summary>
        internal static void CheckRelatedType(Type modelType, string relationPath, Type relatedType)
        {
            var type = modelType;
            var parts = (relationPath ?? string.Empty).Split('.');
            for (int i = 0; i < parts.Length; i++)
            {
                var relation = RelationInfo.Get(type, parts[i]);
                if (relation.RelatedType == null)
                    throw new NotSupportedException($"{relation.Property.Name}: MorphTo cannot be loaded with conditions.");
                if (i == parts.Length - 1 && relation.RelatedType != relatedType)
                    throw new ArgumentException($"{type.Name}.{relation.Property.Name} loads {relation.RelatedType.Name}, not {relatedType.Name}.", nameof(relationPath));
                type = relation.RelatedType;
            }
        }

        // Distinct, complete key value sets of the models
        private static List<object?[]> DistinctKeys(List<object> models, ModelMetadata metadata, string[] columns)
        {
            return models.Select(m => RelationKeys.Values(m, metadata, columns))
                .Where(RelationKeys.Complete)
                .GroupBy(RelationKeys.KeyOf)
                .Select(g => g.First())
                .ToList();
        }

        private static string? KeyOf(object model, ModelMetadata metadata, string[] columns)
        {
            return RelationKeys.KeyOf(RelationKeys.Values(model, metadata, columns));
        }

        // "c IN (@k0, @k1 ...)" for one column, "((a = @k0 AND b = @k1) OR (a = @k2 AND b = @k3) ...)" for several
        private static string KeyFilter(DbCommandBase db, string[] columns, List<object?[]> keys)
        {
            var p = db.ProviderType;
            int n = 0;
            string Next(object? value)
            {
                var name = "@k" + (n++).ToString(CultureInfo.InvariantCulture);
                db.ParametersAdd(name, value);
                return name;
            }

            if (columns.Length == 1)
                return $"{SqlDialect.Column(p, columns[0])} IN ({string.Join(", ", keys.Select(k => Next(k[0])))})";

            var groups = keys.Select(k => "(" + string.Join(" AND ", columns.Select((c, i) => $"{SqlDialect.Column(p, c)} = {Next(k[i])}")) + ")");
            return "(" + string.Join(" OR ", groups) + ")";
        }

        /// <summary>
        /// SELECT * FROM related WHERE key column(s) match [AND typeColumn = typeValue] AND (conditions or not
        /// soft-deleted) [ORDER BY of the conditions], in chunks
        /// </summary>
        private static async Task<List<(string Key, object Model)>> FetchRelated(ModelMetadata relatedMetadata, string[] columns,
            List<object?[]> keys, string? connectionName, bool useAsync, CancellationToken ct, string? typeColumn, string? typeValue, ISubQuery? constraint)
        {
            var result = new List<(string, object)>();
            var connection = relatedMetadata.ConnectionName ?? connectionName;
            var perChunk = Math.Max(1, MaxInValues / columns.Length);

            for (int start = 0; start < keys.Count; start += perChunk)
            {
                var chunk = keys.Skip(start).Take(perChunk).ToList();
                using (var db = ModelBase.CreateReadCommand(connection))
                {
                    var p = db.ProviderType;
                    var sql = $"SELECT * FROM {SqlDialect.Table(p, relatedMetadata.TableName)} WHERE {KeyFilter(db, columns, chunk)}";
                    if (typeColumn != null)
                    {
                        sql += $" AND {SqlDialect.Column(p, typeColumn)} = @morphType";
                        db.ParametersAdd("@morphType", typeValue);
                    }

                    if (constraint != null)
                    {
                        // The conditions bring their own soft delete filter (WithTrashed() inside them works)
                        int index = 0;
                        var parameters = new Dictionary<string, object?>();
                        var conditions = constraint.BuildConditions(p, false, ref index, parameters);
                        if (conditions.Length > 0)
                            sql += $" AND ({conditions})";
                        sql += constraint.BuildOrderBy(p, ref index, parameters);
                        DbRun.Bind(db, parameters);
                    }
                    else if (relatedMetadata.HasSoftDelete)
                    {
                        sql += $" AND {SqlDialect.Column(p, relatedMetadata.SoftDeleteColumn)} IS NULL";
                    }

                    await DbRun.Models(db, sql, relatedMetadata, () =>
                    {
                        var relatedModel = relatedMetadata.NewInstance();
                        if (relatedModel is ModelBase mb && relatedMetadata.ConnectionName == null)
                            mb.LoadedConnection = connectionName;
                        return relatedModel;
                    }, useAsync, ct, (record, map, relatedModel) =>
                    {
                        var key = RelationKeys.KeyOf(columns.Select(c => map.Value(record, c)).ToArray());
                        if (key != null)
                            result.Add((key, relatedModel));
                    }).ConfigureAwait(false);
                }
            }

            return result;
        }

        // HasMany / HasOne / MorphMany / MorphOne: related.fk IN (keys of the models)
        private static async Task<List<object>> LoadChildren(RelationInfo relation, List<object> models, string? connectionName,
            bool useAsync, CancellationToken ct, ISubQuery? constraint)
        {
            var keyValues = DistinctKeys(models, relation.Model, relation.OuterColumns);
            if (keyValues.Count == 0) return new List<object>();

            var rows = await FetchRelated(relation.Related!, relation.InnerColumns, keyValues, connectionName, useAsync, ct,
                relation.TypeColumn, relation.TypeColumn != null ? relation.TypeValue : null, constraint).ConfigureAwait(false);
            var grouped = rows.GroupBy(r => r.Key).ToDictionary(g => g.Key, g => g.Select(r => r.Model).ToList());

            var prop = relation.Property;
            var listType = typeof(List<>).MakeGenericType(relation.RelatedType!);
            foreach (var model in models)
            {
                var key = KeyOf(model, relation.Model, relation.OuterColumns);
                List<object>? items = null;
                if (key != null)
                    grouped.TryGetValue(key, out items);

                if (relation.IsMany)
                {
                    var list = (IList)Activator.CreateInstance(listType)!;
                    foreach (var item in items ?? new List<object>())
                        list.Add(item);
                    if (prop.CanWrite && prop.PropertyType.IsAssignableFrom(listType))
                        prop.SetValue(model, list);
                }
                else if (items != null && items.Count > 0 && prop.CanWrite)
                {
                    prop.SetValue(model, items[0]);
                }
            }

            return rows.Select(r => r.Model).ToList();
        }

        // BelongsTo: owner.key IN (foreign keys of the models)
        private static async Task<List<object>> LoadOwners(RelationInfo relation, List<object> models, string? connectionName,
            bool useAsync, CancellationToken ct, ISubQuery? constraint)
        {
            var fkValues = DistinctKeys(models, relation.Model, relation.OuterColumns);
            if (fkValues.Count == 0) return new List<object>();

            var rows = await FetchRelated(relation.Related!, relation.InnerColumns, fkValues, connectionName, useAsync, ct, null, null, constraint).ConfigureAwait(false);
            var byKey = new Dictionary<string, object>();
            foreach (var row in rows)
            {
                if (!byKey.ContainsKey(row.Key))
                    byKey[row.Key] = row.Model;
            }

            var prop = relation.Property;
            foreach (var model in models)
            {
                var key = KeyOf(model, relation.Model, relation.OuterColumns);
                if (key != null && byKey.TryGetValue(key, out var owner) && prop.CanWrite)
                    prop.SetValue(model, owner);
            }

            return rows.Select(r => r.Model).ToList();
        }

        // [MorphTo]: owners named by {Name}Type / {Name}Id of the children, one query per owner type
        private static async Task<List<object>> LoadMorphTo(RelationInfo relation, List<object> models, string? connectionName,
            bool useAsync, CancellationToken ct)
        {
            var metadata = relation.Model;
            var name = relation.MorphName!;
            var typeProp = ModelBase.FindProperty(metadata, name + "Type")
                           ?? throw new InvalidOperationException($"{relation.ModelType.Name} has no {name}Type property.");
            var idProp = ModelBase.FindProperty(metadata, name + "Id")
                         ?? throw new InvalidOperationException($"{relation.ModelType.Name} has no {name}Id property.");

            var prop = relation.Property;
            var loaded = new List<object>();
            var byType = models.GroupBy(m => typeProp.GetValue(m) as string)
                               .Where(g => !string.IsNullOrEmpty(g.Key));

            foreach (var group in byType)
            {
                var ownerType = MorphMap.TypeOf(group.Key!);
                var owner = ModelMetadata.GetMetadata(ownerType);
                if (owner.PrimaryKeyColumn == null)
                    throw new InvalidOperationException($"MorphTo: {ownerType.Name} needs a single-column primary key.");

                var ids = group.Select(m => idProp.GetValue(m))
                               .Where(v => !ModelMapper.IsDefaultKey(v))
                               .GroupBy(ModelMapper.KeyOf)
                               .Select(g => new object?[] { g.First() })
                               .ToList();
                if (ids.Count == 0)
                    continue;

                var rows = await FetchRelated(owner, new[] { owner.PrimaryKeyColumn }, ids, connectionName, useAsync, ct, null, null, null).ConfigureAwait(false);
                var byKey = new Dictionary<string, object>();
                foreach (var row in rows)
                    byKey[row.Key] = row.Model;

                foreach (var model in group)
                {
                    var key = ModelMapper.KeyOf(idProp.GetValue(model));
                    if (key != null && byKey.TryGetValue(key, out var related) && prop.CanWrite && prop.PropertyType.IsInstanceOfType(related))
                        prop.SetValue(model, related);
                }

                loaded.AddRange(rows.Select(r => r.Model));
            }

            return loaded;
        }
    }

    /// <summary>
    /// QueryBuilder extension for eager loading
    /// </summary>
    public static class EagerLoadingExtensions
    {
        /// <summary>
        /// İlişki yükle
        /// </summary>
        public static EagerLoader<T> With<T>(this QueryBuilder<T> query, string relation) where T : Model<T>, new()
        {
            return new EagerLoader<T>(query).With(relation);
        }
    }
}
