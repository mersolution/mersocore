using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using mersolutionCore.Command.Abstractions;
using mersolutionCore.ORM.Entity;
using mersolutionCore.ORM.Relationships;

namespace mersolutionCore.ORM
{
    internal enum RelationKind { HasMany, HasOne, BelongsTo, MorphMany, MorphOne, MorphTo }

    /// <summary>
    /// A relation property of a model ([HasMany], [HasOne], [BelongsTo], [MorphMany], [MorphOne], [MorphTo]) with
    /// the columns that connect both tables. Shared by eager loading, WhereHas and WithCount.
    /// </summary>
    internal sealed class RelationInfo
    {
        private static readonly ConcurrentDictionary<string, RelationInfo?> _cache = new ConcurrentDictionary<string, RelationInfo?>(StringComparer.Ordinal);

        private RelationInfo(RelationKind kind, PropertyInfo property, Type modelType, ModelMetadata model)
        {
            Kind = kind;
            Property = property;
            ModelType = modelType;
            Model = model;
        }

        public RelationKind Kind { get; }
        public PropertyInfo Property { get; }
        public Type ModelType { get; }
        public ModelMetadata Model { get; }

        /// <summary>Related model (null for MorphTo: the owner type is per row)</summary>
        public Type? RelatedType { get; private set; }
        public ModelMetadata? Related { get; private set; }

        /// <summary>Columns on this model ...</summary>
        public string[] OuterColumns { get; private set; } = new string[0];

        /// <summary>... equal to these columns on the related table</summary>
        public string[] InnerColumns { get; private set; } = new string[0];

        /// <summary>Polymorphic {Name} prefix (MorphMany / MorphOne / MorphTo)</summary>
        public string? MorphName { get; private set; }

        /// <summary>{Name}Type column on the related table (MorphMany / MorphOne)</summary>
        public string? TypeColumn { get; private set; }

        /// <summary>Value of <see cref="TypeColumn"/> for this model (MorphMap may change at run time)</summary>
        public string TypeValue => MorphMap.NameOf(ModelType);

        public bool IsMany => Kind == RelationKind.HasMany || Kind == RelationKind.MorphMany;

        /// <summary>
        /// The relation, or an exception that names the attributes a relation property needs
        /// </summary>
        public static RelationInfo Get(Type modelType, string name)
        {
            return Find(modelType, name) ?? throw new ArgumentException(
                $"{modelType.Name} has no relation property '{name}' ([HasMany], [HasOne], [BelongsTo], [MorphMany], [MorphOne] or [MorphTo]).", nameof(name));
        }

        /// <summary>
        /// The relation, or null when <paramref name="name"/> is not an attributed relation property
        /// </summary>
        public static RelationInfo? Find(Type modelType, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            return _cache.GetOrAdd(modelType.FullName + "|" + name.ToUpperInvariant(), _ => Resolve(modelType, name));
        }

        private static RelationInfo? Resolve(Type modelType, string name)
        {
            var prop = modelType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop == null)
                return null;

            var model = ModelMetadata.GetMetadata(modelType);

            var hasMany = prop.GetCustomAttribute<HasManyAttribute>();
            if (hasMany != null)
                return ParentKey(RelationKind.HasMany, prop, modelType, model, hasMany.RelatedType, hasMany.ForeignKey, hasMany.LocalKey);

            var hasOne = prop.GetCustomAttribute<HasOneAttribute>();
            if (hasOne != null)
                return ParentKey(RelationKind.HasOne, prop, modelType, model, hasOne.RelatedType, hasOne.ForeignKey, hasOne.LocalKey);

            var belongsTo = prop.GetCustomAttribute<BelongsToAttribute>();
            if (belongsTo != null)
            {
                var relatedType = ElementType(prop, belongsTo.RelatedType);
                var related = ModelMetadata.GetMetadata(relatedType);
                var inner = KeyColumns(related, belongsTo.OwnerKey);
                return new RelationInfo(RelationKind.BelongsTo, prop, modelType, model)
                {
                    RelatedType = relatedType,
                    Related = related,
                    InnerColumns = inner,
                    OuterColumns = RelationKeys.ForeignColumns(model, belongsTo.ForeignKey, inner, relatedType.Name + "Id", "[BelongsTo] " + prop.Name)
                };
            }

            var morphMany = prop.GetCustomAttribute<MorphManyAttribute>();
            if (morphMany != null)
                return Morph(RelationKind.MorphMany, prop, modelType, model, morphMany.RelatedType, morphMany.Name, morphMany.LocalKey);

            var morphOne = prop.GetCustomAttribute<MorphOneAttribute>();
            if (morphOne != null)
                return Morph(RelationKind.MorphOne, prop, modelType, model, morphOne.RelatedType, morphOne.Name, morphOne.LocalKey);

            var morphTo = prop.GetCustomAttribute<MorphToAttribute>();
            if (morphTo != null)
                return new RelationInfo(RelationKind.MorphTo, prop, modelType, model) { MorphName = morphTo.Name };

            return null;
        }

        // HasMany / HasOne: key on this model, foreign key on the related table
        private static RelationInfo ParentKey(RelationKind kind, PropertyInfo prop, Type modelType, ModelMetadata model,
            Type? declaredType, string? foreignKey, string localKey)
        {
            var relatedType = ElementType(prop, declaredType);
            var related = ModelMetadata.GetMetadata(relatedType);
            var outer = KeyColumns(model, localKey);
            return new RelationInfo(kind, prop, modelType, model)
            {
                RelatedType = relatedType,
                Related = related,
                OuterColumns = outer,
                InnerColumns = RelationKeys.ForeignColumns(related, foreignKey, outer, modelType.Name + "Id", $"[{kind}] {prop.Name}")
            };
        }

        private static RelationInfo Morph(RelationKind kind, PropertyInfo prop, Type modelType, ModelMetadata model,
            Type declaredType, string name, string localKey)
        {
            var outer = KeyColumns(model, localKey);
            if (outer.Length != 1)
                throw new NotSupportedException($"{prop.Name}: polymorphic relations need a single-column key on {modelType.Name} (the {name}Id column holds one value).");

            var relatedType = ElementType(prop, declaredType);
            return new RelationInfo(kind, prop, modelType, model)
            {
                RelatedType = relatedType,
                Related = ModelMetadata.GetMetadata(relatedType),
                OuterColumns = outer,
                InnerColumns = new[] { name + "Id" },
                MorphName = name,
                TypeColumn = name + "Type"
            };
        }

        internal static Type ElementType(PropertyInfo prop, Type? declared)
        {
            if (declared != null)
                return declared;

            var type = prop.PropertyType;
            if (type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type))
                return type.GetGenericArguments()[0];

            return type;
        }

        // Key column(s) of a model: the given ones, its primary key for the default "Id" or a column it does not have
        internal static string[] KeyColumns(ModelMetadata metadata, string? key)
        {
            var columns = RelationKeys.KeyColumns(metadata, key);
            if (columns.Length == 1 && ModelBase.FindProperty(metadata, columns[0]) == null && metadata.KeyProperties.Count > 0)
                return metadata.KeyProperties.Select(k => k.ColumnName).ToArray();
            return columns;
        }

        /// <summary>
        /// A new QueryBuilder&lt;related&gt; (the place for the caller's conditions)
        /// </summary>
        internal ISubQuery NewRelatedQuery()
        {
            if (RelatedType == null)
                throw new NotSupportedException($"{Property.Name}: MorphTo has no single related model; query the owner types directly.");
            return (ISubQuery)Activator.CreateInstance(typeof(QueryBuilder<>).MakeGenericType(RelatedType))!;
        }
    }

    /// <summary>
    /// Correlated subquery over a relation: SELECT {select} FROM related r WHERE r.fk = outer.key [AND r.type = ..]
    /// AND (conditions of the related query, soft delete, global scopes). Used by WhereHas / Has / WithCount.
    /// </summary>
    internal sealed class RelationSubQuery : ISubQuery
    {
        private readonly RelationInfo _relation;
        private readonly ISubQuery _related;
        private readonly ISubQuery _owner;

        /// <param name="relation">Relation of the outer model</param>
        /// <param name="related">Query of the related model holding the extra conditions</param>
        /// <param name="owner">The outer query (its table name or alias is referenced)</param>
        internal RelationSubQuery(RelationInfo relation, ISubQuery related, ISubQuery owner)
        {
            if (relation.Related == null || relation.Kind == RelationKind.MorphTo)
                throw new NotSupportedException($"{relation.Property.Name}: WhereHas / WithCount do not support MorphTo; query the owner types directly.");

            _relation = relation;
            _related = related;
            _owner = owner;
        }

        internal string Build(DbProviderType provider, string select, ref int paramIndex, Dictionary<string, object?> parameters)
        {
            // Aliased inner table: self relations (categories → categories) and nested WhereHas stay unambiguous
            var alias = _owner.Alias == null ? "mc_r1" : _owner.Alias + "_1";
            var outer = _owner.Alias ?? SqlDialect.Table(provider, _owner.TableName);
            string Col(string name) => SqlDialect.Column(provider, name);

            var sb = new StringBuilder();
            sb.Append("SELECT ").Append(select.Replace("{alias}", alias))
              .Append(" FROM ").Append(SqlDialect.Table(provider, _relation.Related!.TableName)).Append(' ').Append(alias)
              .Append(" WHERE ");

            for (int i = 0; i < _relation.InnerColumns.Length; i++)
            {
                if (i > 0) sb.Append(" AND ");
                sb.Append($"{alias}.{Col(_relation.InnerColumns[i])} = {outer}.{Col(_relation.OuterColumns[i])}");
            }

            if (_relation.TypeColumn != null)
            {
                var name = "@p" + (paramIndex++).ToString(CultureInfo.InvariantCulture);
                parameters[name] = _relation.TypeValue;
                sb.Append($" AND {alias}.{Col(_relation.TypeColumn)} = {name}");
            }

            _related.Alias = alias;
            try
            {
                var conditions = _related.BuildConditions(provider, true, ref paramIndex, parameters);
                if (conditions.Length > 0)
                    sb.Append(" AND (").Append(conditions).Append(')');
            }
            finally
            {
                _related.Alias = null;
            }

            return sb.ToString();
        }

        public string BuildSubquery(DbProviderType provider, bool forIn, ref int paramIndex, Dictionary<string, object?> parameters)
        {
            return Build(provider, "1", ref paramIndex, parameters);
        }

        public string BuildUnionPart(DbProviderType provider, ref int paramIndex, Dictionary<string, object?> parameters)
        {
            throw new NotSupportedException("A relation subquery cannot be a UNION part.");
        }

        public string? Alias { get; set; }

        public string TableName => _relation.Related!.TableName;

        public string BuildConditions(DbProviderType provider, bool withGlobalScopes, ref int paramIndex, Dictionary<string, object?> parameters)
        {
            throw new NotSupportedException();
        }

        public string BuildOrderBy(DbProviderType provider, ref int paramIndex, Dictionary<string, object?> parameters)
        {
            return string.Empty;
        }

        public void AddComparison(string column, string op, object? value)
        {
            throw new NotSupportedException();
        }
    }
}
