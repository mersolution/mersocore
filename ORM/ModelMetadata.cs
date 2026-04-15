using System;
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
        private static readonly Dictionary<Type, ModelMetadata> _cache = new Dictionary<Type, ModelMetadata>();

        public Type ModelType { get; }
        public string TableName { get; }
        public string Schema { get; }
        public string PrimaryKeyColumn { get; }
        public PropertyInfo PrimaryKeyProperty { get; }
        public bool PrimaryKeyAutoIncrement { get; }
        public List<PropertyMetadata> Properties { get; }
        public bool HasSoftDelete { get; }
        public string SoftDeleteColumn { get; }
        public PropertyInfo SoftDeleteProperty { get; private set; }
        public PropertyInfo CreatedAtProperty { get; }
        public PropertyInfo UpdatedAtProperty { get; }

        /// <summary>
        /// Get metadata for a type (cached)
        /// </summary>
        public static ModelMetadata GetMetadata(Type type)
        {
            if (!_cache.ContainsKey(type))
            {
                _cache[type] = new ModelMetadata(type);
            }
            return _cache[type];
        }

        public ModelMetadata(Type modelType)
        {
            ModelType = modelType;
            Properties = new List<PropertyMetadata>();

            // Get table name
            var tableAttr = modelType.GetCustomAttribute<TableAttribute>();
            if (tableAttr != null)
            {
                TableName = tableAttr.Name;
                Schema = tableAttr.Schema;
            }
            else
            {
                // Convention: pluralize class name
                TableName = modelType.Name + "s";
            }

            // Get properties
            var props = modelType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var prop in props)
            {
                // Skip ignored properties
                if (prop.GetCustomAttribute<IgnoreAttribute>() != null)
                    continue;

                // Skip navigation properties (complex types)
                if (!IsSimpleType(prop.PropertyType))
                    continue;

                var propMeta = new PropertyMetadata(prop);
                Properties.Add(propMeta);

                // Check for primary key
                if (propMeta.IsPrimaryKey)
                {
                    PrimaryKeyColumn = propMeta.ColumnName;
                    PrimaryKeyProperty = prop;
                    PrimaryKeyAutoIncrement = propMeta.IsAutoIncrement;
                }

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
            }

            // Default primary key if not specified
            if (PrimaryKeyProperty == null)
            {
                var idProp = Properties.FirstOrDefault(p => 
                    p.ColumnName.Equals("Id", StringComparison.OrdinalIgnoreCase) ||
                    p.ColumnName.Equals(modelType.Name + "Id", StringComparison.OrdinalIgnoreCase));

                if (idProp != null)
                {
                    PrimaryKeyColumn = idProp.ColumnName;
                    PrimaryKeyProperty = idProp.PropertyInfo;
                    PrimaryKeyAutoIncrement = true;
                    idProp.IsPrimaryKey = true;
                    idProp.IsAutoIncrement = true;
                }
            }
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
                   underlyingType == typeof(byte[]);
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
        public string DefaultValue { get; }

        public PropertyMetadata(PropertyInfo prop)
        {
            PropertyInfo = prop;
            PropertyName = prop.Name;

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

            // Check for primary key
            var pkAttr = prop.GetCustomAttribute<PrimaryKeyAttribute>();
            if (pkAttr != null)
            {
                IsPrimaryKey = true;
                IsAutoIncrement = pkAttr.AutoIncrement;
            }
        }
    }
}
