using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using mersolutionCore.ORM.Entity;

namespace mersolutionCore.ORM
{
    /// <summary>
    /// Cached metadata for a model type
    /// </summary>
    public class ModelMetadata
    {
        private static readonly ConcurrentDictionary<Type, ModelMetadata> _cache = new ConcurrentDictionary<Type, ModelMetadata>();

        public Type ModelType { get; }

        /// <summary>
        /// Table name used in SQL (schema-qualified when <see cref="TableAttribute.Schema"/> is set, e.g. "dbo.Users")
        /// </summary>
        public string TableName { get; }

        /// <summary>
        /// Table name without schema
        /// </summary>
        public string BaseTableName { get; }

        public string? Schema { get; }

        /// <summary>
        /// Single-column primary key (null for a composite key — use <see cref="KeyProperties"/>)
        /// </summary>
        public string? PrimaryKeyColumn { get; }

        /// <summary>
        /// Single-column primary key property (null for a composite key — use <see cref="KeyProperties"/>)
        /// </summary>
        public PropertyInfo? PrimaryKeyProperty { get; }

        public bool PrimaryKeyAutoIncrement { get; }

        /// <summary>
        /// Primary key columns in key order: one for a normal key, several for a composite key, none without a key
        /// </summary>
        public IReadOnlyList<PropertyMetadata> KeyProperties { get; }

        /// <summary>
        /// True when the primary key has more than one column
        /// </summary>
        public bool HasCompositeKey => KeyProperties.Count > 1;

        /// <summary>
        /// True when any property has [Fillable] (then only those can be mass assigned)
        /// </summary>
        public bool HasFillableList { get; }

        /// <summary>
        /// [Touches] parents updated when this model changes
        /// </summary>
        public IReadOnlyList<TouchesAttribute> Touches { get; }

        /// <summary>
        /// Value written to the *Type column of polymorphic relations
        /// </summary>
        public string MorphName { get; }
        public List<PropertyMetadata> Properties { get; }

        /// <summary>
        /// [Computed] properties: filled from query results, never written
        /// </summary>
        public List<PropertyMetadata> ComputedProperties { get; } = new List<PropertyMetadata>();
        [System.Diagnostics.CodeAnalysis.MemberNotNullWhen(true, nameof(SoftDeleteColumn), nameof(SoftDeleteProperty))]
        public bool HasSoftDelete { get; }
        public string? SoftDeleteColumn { get; }
        public PropertyInfo? SoftDeleteProperty { get; private set; }
        public PropertyInfo? CreatedAtProperty { get; }
        public PropertyInfo? UpdatedAtProperty { get; }

        /// <summary>
        /// Named connection from <see cref="ConnectionAttribute"/> (null = default / scoped connection)
        /// </summary>
        public string? ConnectionName { get; }

        /// <summary>
        /// Optimistic concurrency column from <see cref="RowVersionAttribute"/> (null when the model has none)
        /// </summary>
        public PropertyMetadata? VersionProperty { get; }

        /// <summary>
        /// Get metadata for a type (cached, thread-safe)
        /// </summary>
        public static ModelMetadata GetMetadata(Type type)
        {
            return _cache.GetOrAdd(type, t => new ModelMetadata(t));
        }

        public ModelMetadata(Type modelType)
        {
            ModelType = modelType;
            Properties = new List<PropertyMetadata>();

            // Get table name
            var tableAttr = modelType.GetCustomAttribute<TableAttribute>();
            if (tableAttr != null)
            {
                BaseTableName = tableAttr.Name;
                Schema = tableAttr.Schema;
            }
            else
            {
                // Convention: pluralize class name
                BaseTableName = modelType.Name + "s";
            }
            TableName = string.IsNullOrEmpty(Schema) ? BaseTableName : $"{Schema}.{BaseTableName}";
            ConnectionName = modelType.GetCustomAttribute<ConnectionAttribute>(true)?.Name;
            Touches = modelType.GetCustomAttributes<TouchesAttribute>(true).ToList();
            MorphName = modelType.GetCustomAttribute<MorphNameAttribute>(false)?.Name ?? modelType.Name;

            // Get properties
            var props = modelType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var keys = new List<PropertyMetadata>();

            foreach (var prop in props)
            {
                // Skip ignored properties
                if (prop.GetCustomAttribute<IgnoreAttribute>() != null)
                    continue;

                // Skip navigation properties (complex types) and indexers; a [Converter] makes any type a column
                if ((!IsSimpleType(prop.PropertyType) && prop.GetCustomAttribute<ConverterAttribute>(true) == null) || prop.GetIndexParameters().Length > 0)
                    continue;

                // [Computed]: read from results only, never a table column
                if (prop.GetCustomAttribute<ComputedAttribute>(true) != null)
                {
                    ComputedProperties.Add(new PropertyMetadata(prop));
                    continue;
                }

                var propMeta = new PropertyMetadata(prop);
                Properties.Add(propMeta);

                if (propMeta.IsPrimaryKey)
                    keys.Add(propMeta);

                if (propMeta.IsFillable)
                    HasFillableList = true;

                // Check for soft delete
                if (prop.GetCustomAttribute<SoftDeleteAttribute>() != null)
                {
                    HasSoftDelete = true;
                    SoftDeleteColumn = propMeta.ColumnName;
                    SoftDeleteProperty = prop;
                }

                // Check for timestamps
                if (prop.GetCustomAttribute<CreatedAtAttribute>() != null)
                {
                    CreatedAtProperty = prop;
                }
                if (prop.GetCustomAttribute<UpdatedAtAttribute>() != null)
                {
                    UpdatedAtProperty = prop;
                }

                if (prop.GetCustomAttribute<RowVersionAttribute>() != null)
                {
                    if (!ModelMapper.IsIntegerType(prop.PropertyType))
                        throw new InvalidOperationException($"[RowVersion] on {modelType.Name}.{prop.Name} needs an int or long property.");
                    VersionProperty = propMeta;
                }
            }

            if (keys.Count > 1)
            {
                // Composite key: values come from the application, never auto-increment
                keys = keys.Select((k, i) => new { k, i })
                    .OrderBy(x => x.k.KeyOrder).ThenBy(x => x.i)
                    .Select(x => x.k).ToList();
                foreach (var key in keys)
                    key.IsAutoIncrement = false;
            }
            else if (keys.Count == 1)
            {
                PrimaryKeyColumn = keys[0].ColumnName;
                PrimaryKeyProperty = keys[0].PropertyInfo;
                PrimaryKeyAutoIncrement = keys[0].IsAutoIncrement;
            }
            else
            {
                // Default primary key if not specified
                var idProp = Properties.FirstOrDefault(p =>
                    p.ColumnName.Equals("Id", StringComparison.OrdinalIgnoreCase) ||
                    p.ColumnName.Equals(modelType.Name + "Id", StringComparison.OrdinalIgnoreCase));

                if (idProp != null)
                {
                    var autoIncrement = ModelMapper.IsIntegerType(idProp.PropertyInfo.PropertyType);
                    PrimaryKeyColumn = idProp.ColumnName;
                    PrimaryKeyProperty = idProp.PropertyInfo;
                    PrimaryKeyAutoIncrement = autoIncrement;
                    idProp.IsPrimaryKey = true;
                    idProp.IsAutoIncrement = autoIncrement;
                    keys.Add(idProp);
                }
            }

            KeyProperties = keys;
        }

        private Func<object>? _factory;

        /// <summary>
        /// New instance of the model type (compiled constructor call, no Activator per row)
        /// </summary>
        internal object NewInstance()
        {
            if (_factory == null)
            {
                try
                {
                    _factory = System.Linq.Expressions.Expression.Lambda<Func<object>>(
                        System.Linq.Expressions.Expression.Convert(System.Linq.Expressions.Expression.New(ModelType), typeof(object))).Compile();
                }
                catch
                {
                    _factory = () => Activator.CreateInstance(ModelType)!;
                }
            }
            return _factory();
        }

        /// <summary>
        /// Key values in key order from a single value, an object[] or a column → value dictionary
        /// </summary>
        public object?[] GetKeyValues(object? id)
        {
            var keys = KeyProperties;
            object?[] values;

            if (id is object?[] array)
            {
                values = array;
            }
            else if (id is IDictionary<string, object?> named && keys.Count > 1)
            {
                values = keys.Select(k => named.TryGetValue(k.PropertyName, out var v) || named.TryGetValue(k.ColumnName, out v)
                    ? v
                    : throw new ArgumentException($"Key value for '{k.PropertyName}' is missing.", nameof(id))).ToArray();
            }
            else
            {
                values = new[] { id };
            }

            if (values.Length != keys.Count)
            {
                throw new ArgumentException(keys.Count > 1
                    ? $"{ModelType.Name} has a composite key ({string.Join(", ", keys.Select(k => k.PropertyName))}): pass {keys.Count} values in that order."
                    : $"{ModelType.Name} has a single-column key: pass one value.", nameof(id));
            }
            return values;
        }

        /// <summary>
        /// True when Fill() / Create(dictionary) may set this property
        /// </summary>
        public bool IsMassAssignable(PropertyMetadata prop)
        {
            if (prop == null || prop.IsGuarded)
                return false;
            return !HasFillableList || prop.IsFillable;
        }

        private bool IsSimpleType(Type type)
        {
            var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

            return underlyingType.IsPrimitive ||
                   underlyingType == typeof(string) ||
                   underlyingType == typeof(decimal) ||
                   underlyingType == typeof(DateTime) ||
                   underlyingType == typeof(DateTimeOffset) ||
                   underlyingType == typeof(TimeSpan) ||
                   underlyingType == typeof(Guid) ||
                   underlyingType.IsEnum ||
                   underlyingType == typeof(byte[]) ||
                   DateTypes.IsDateOrTimeOnly(underlyingType);
        }
    }

    /// <summary>
    /// Metadata for a single property
    /// </summary>
    public class PropertyMetadata
    {
        public PropertyInfo PropertyInfo { get; }
        public string PropertyName { get; }
        public string ColumnName { get; }
        public bool IsPrimaryKey { get; set; }
        public bool IsAutoIncrement { get; set; }
        public bool Nullable { get; }
        public int Length { get; }
        public string? DefaultValue { get; }

        /// <summary>[Hidden]: left out of ToDict / ToJson</summary>
        public bool IsHidden { get; }

        /// <summary>[Fillable]: on the mass assignment allow-list</summary>
        public bool IsFillable { get; }

        /// <summary>[Guarded]: never mass assigned</summary>
        public bool IsGuarded { get; }

        /// <summary>Position in a composite primary key ([PrimaryKey(Order = n)])</summary>
        public int KeyOrder { get; }

        public PropertyMetadata(PropertyInfo prop)
        {
            PropertyInfo = prop;
            PropertyName = prop.Name;
            IsHidden = prop.GetCustomAttribute<HiddenAttribute>(true) != null;
            IsFillable = prop.GetCustomAttribute<FillableAttribute>(true) != null;
            IsGuarded = prop.GetCustomAttribute<GuardedAttribute>(true) != null;

            // Get column attribute
            var colAttr = prop.GetCustomAttribute<ColumnAttribute>();
            if (colAttr != null)
            {
                ColumnName = colAttr.Name ?? prop.Name;
                Nullable = colAttr.Nullable;
                Length = colAttr.Length;
                DefaultValue = colAttr.DefaultValue;
            }
            else
            {
                ColumnName = prop.Name;
                Nullable = true;
            }

            // Check for primary key — only integer keys can be database generated
            var pkAttr = prop.GetCustomAttribute<PrimaryKeyAttribute>();
            if (pkAttr != null)
            {
                IsPrimaryKey = true;
                IsAutoIncrement = pkAttr.AutoIncrement && ModelMapper.IsIntegerType(prop.PropertyType);
                KeyOrder = pkAttr.Order;
            }

            var converter = prop.GetCustomAttribute<ConverterAttribute>(true);
            if (converter != null)
                Converter = (IValueConverter)Activator.CreateInstance(converter.ConverterType)!;

            _setter = new Lazy<Action<object, object?>?>(() => CompileSetter(prop));
            _getter = new Lazy<Func<object, object?>>(() => CompileGetter(prop));
        }

        private readonly Lazy<Action<object, object?>?> _setter;
        private readonly Lazy<Func<object, object?>> _getter;

        /// <summary>
        /// [Converter] / [EnumAsString] / [JsonColumn] / [Encrypted] of the property (null: stored as it is)
        /// </summary>
        public IValueConverter? Converter { get; }

        /// <summary>
        /// Property value of <paramref name="model"/> (compiled getter, no reflection per call)
        /// </summary>
        public object? GetValue(object model) => _getter.Value(model);

        /// <summary>
        /// Value written to the column (after the converter)
        /// </summary>
        public object? GetDbValue(object model) => ToDatabase(GetValue(model));

        /// <summary>
        /// A property value as the column stores it (after the converter)
        /// </summary>
        public object? ToDatabase(object? value) => Converter == null ? value : Converter.ToDatabase(value);

        /// <summary>
        /// Set the property from a database value (converted to the property type); false when it cannot be
        /// converted, a non-nullable value type gets null, or the property has no setter
        /// </summary>
        internal bool Assign(object model, object? databaseValue)
        {
            var setter = _setter.Value;
            if (setter == null)
                return false;

            try
            {
                var converted = Converter != null
                    ? ModelMapper.ConvertTo(Converter.FromDatabase(databaseValue == DBNull.Value ? null : databaseValue, PropertyInfo.PropertyType), PropertyInfo.PropertyType)
                    : ModelMapper.ConvertTo(databaseValue, PropertyInfo.PropertyType);
                if (converted == null && PropertyInfo.PropertyType.IsValueType && System.Nullable.GetUnderlyingType(PropertyInfo.PropertyType) == null)
                    return false; // keep default(T) for non-nullable value types

                setter(model, converted);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // (model, value) => ((TModel)model).Property = (TProperty)value — falls back to reflection where
        // expressions cannot be compiled
        private static Action<object, object?>? CompileSetter(PropertyInfo prop)
        {
            if (!prop.CanWrite || prop.SetMethod == null || prop.DeclaringType == null)
                return null;

            try
            {
                var model = System.Linq.Expressions.Expression.Parameter(typeof(object), "model");
                var value = System.Linq.Expressions.Expression.Parameter(typeof(object), "value");
                var body = System.Linq.Expressions.Expression.Assign(
                    System.Linq.Expressions.Expression.Property(System.Linq.Expressions.Expression.Convert(model, prop.DeclaringType), prop),
                    System.Linq.Expressions.Expression.Convert(value, prop.PropertyType));
                return System.Linq.Expressions.Expression.Lambda<Action<object, object?>>(body, model, value).Compile();
            }
            catch
            {
                return (target, v) => prop.SetValue(target, v);
            }
        }

        private static Func<object, object?> CompileGetter(PropertyInfo prop)
        {
            if (prop.GetMethod == null || prop.DeclaringType == null)
                return target => prop.GetValue(target);

            try
            {
                var model = System.Linq.Expressions.Expression.Parameter(typeof(object), "model");
                var body = System.Linq.Expressions.Expression.Convert(
                    System.Linq.Expressions.Expression.Property(System.Linq.Expressions.Expression.Convert(model, prop.DeclaringType), prop),
                    typeof(object));
                return System.Linq.Expressions.Expression.Lambda<Func<object, object?>>(body, model).Compile();
            }
            catch
            {
                return target => prop.GetValue(target);
            }
        }
    }
}
