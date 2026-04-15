using System;

namespace mersolutionCore.ORM.Entity
{
    /// <summary>
    /// Specifies the database table name for a model
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class TableAttribute : Attribute
    {
        public string Name { get; }
        public string Schema { get; set; }

        public TableAttribute(string name)
        {
            Name = name;
        }
    }

    /// <summary>
    /// Specifies the primary key column
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class PrimaryKeyAttribute : Attribute
    {
        public bool AutoIncrement { get; set; } = true;
    }

    /// <summary>
    /// Specifies the database column name for a property
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class ColumnAttribute : Attribute
    {
        public string Name { get; }
        public bool Nullable { get; set; } = true;
        public int Length { get; set; } = 0;
        public string DefaultValue { get; set; }

        public ColumnAttribute(string name = null)
        {
            Name = name;
        }
    }

    /// <summary>
    /// Marks a property to be ignored by ORM
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class IgnoreAttribute : Attribute
    {
    }

    /// <summary>
    /// Specifies created_at timestamp column
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class CreatedAtAttribute : Attribute
    {
    }

    /// <summary>
    /// Specifies updated_at timestamp column
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class UpdatedAtAttribute : Attribute
    {
    }

    /// <summary>
    /// Specifies soft delete column (deleted_at)
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class SoftDeleteAttribute : Attribute
    {
    }

    /// <summary>
    /// Specifies a HasOne relationship
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class HasOneAttribute : Attribute
    {
        public Type RelatedType { get; set; }
        public string ForeignKey { get; set; }
        public string LocalKey { get; set; } = "Id";

        public HasOneAttribute(Type relatedType, string foreignKey = null)
        {
            RelatedType = relatedType;
            ForeignKey = foreignKey;
        }

        public HasOneAttribute(string foreignKey = null)
        {
            ForeignKey = foreignKey;
        }
    }

    /// <summary>
    /// Specifies a HasMany relationship
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class HasManyAttribute : Attribute
    {
        public Type RelatedType { get; set; }
        public string ForeignKey { get; set; }
        public string LocalKey { get; set; } = "Id";

        public HasManyAttribute(Type relatedType, string foreignKey = null)
        {
            RelatedType = relatedType;
            ForeignKey = foreignKey;
        }

        public HasManyAttribute(string foreignKey = null)
        {
            ForeignKey = foreignKey;
        }
    }

    /// <summary>
    /// Specifies a BelongsTo relationship
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class BelongsToAttribute : Attribute
    {
        public Type RelatedType { get; set; }
        public string ForeignKey { get; set; }
        public string OwnerKey { get; set; } = "Id";

        public BelongsToAttribute(Type relatedType, string foreignKey = null)
        {
            RelatedType = relatedType;
            ForeignKey = foreignKey;
        }

        public BelongsToAttribute(string foreignKey = null)
        {
            ForeignKey = foreignKey;
        }
    }
}
