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
        public string? Schema { get; set; }

        public TableAttribute(string name)
        {
            Name = name;
        }
    }

    /// <summary>
    /// Run this model on a named connection (registered with <c>ModelBase.Configure(name, ...)</c>
    /// or <c>DbConfig.AddConnection(name, ...)</c>) instead of the default one
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public class ConnectionAttribute : Attribute
    {
        public string Name { get; }

        public ConnectionAttribute(string name)
        {
            Name = name;
        }
    }

    /// <summary>
    /// Optimistic concurrency column (int / long). Save() / Delete() only touch the row when it still has the
    /// version that was loaded and increase it by one; otherwise <c>DbConcurrencyException</c> is thrown.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class RowVersionAttribute : Attribute
    {
    }

    /// <summary>
    /// Specifies the primary key column. Put it on several properties for a composite key
    /// (never auto-increment; <see cref="Order"/> sets the column order, default = declaration order).
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class PrimaryKeyAttribute : Attribute
    {
        public bool AutoIncrement { get; set; } = true;

        /// <summary>
        /// Position of this column in a composite key (Find(a, b) takes the values in this order)
        /// </summary>
        public int Order { get; set; }
    }

    /// <summary>
    /// Leave this property out of ToDict() / ToJson() / ToArray() and of System.Text.Json output that uses
    /// <c>MersoJson</c> (password hashes, tokens). <c>model.MakeVisible("Name")</c> shows it for one instance.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public class HiddenAttribute : Attribute
    {
    }

    /// <summary>
    /// Mass assignment allow-list: once any property has [Fillable], Fill() / Create(dictionary) only set
    /// [Fillable] properties. Without [Fillable] every property except [Guarded] ones can be filled.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public class FillableAttribute : Attribute
    {
    }

    /// <summary>
    /// Never set by Fill() / Create(dictionary) (IsAdmin, Role, Balance ...). Assign it in code or use ForceFill().
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public class GuardedAttribute : Attribute
    {
    }

    /// <summary>
    /// When this model is saved, deleted or restored, set the UpdatedAt column of its parent
    /// (e.g. a comment touches its post): <c>[Touches(typeof(Post), "PostId")]</c>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public class TouchesAttribute : Attribute
    {
        /// <summary>Parent model type</summary>
        public Type ParentType { get; }

        /// <summary>Foreign key property / column on this model that holds the parent's primary key
        /// ("OrderId, LineNo" in key order for a composite parent key)</summary>
        public string ForeignKey { get; }

        public TouchesAttribute(Type parentType, string foreignKey)
        {
            ParentType = parentType ?? throw new ArgumentNullException(nameof(parentType));
            ForeignKey = foreignKey ?? throw new ArgumentNullException(nameof(foreignKey));
        }
    }

    /// <summary>
    /// Name stored in the *Type column of polymorphic relations (default: the class name).
    /// Same effect as <c>MorphMap.Register&lt;T&gt;(name)</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class MorphNameAttribute : Attribute
    {
        public string Name { get; }

        public MorphNameAttribute(string name)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }
    }

    /// <summary>
    /// Specifies the database column name for a property
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class ColumnAttribute : Attribute
    {
        public string? Name { get; }
        public bool Nullable { get; set; } = true;
        public int Length { get; set; } = 0;
        public string? DefaultValue { get; set; }

        public ColumnAttribute(string? name = null)
        {
            Name = name;
        }
    }

    /// <summary>
    /// Filled from query results but never written, never part of the table (EnsureCreated) and not change-tracked:
    /// WithCount / WithSum results, SelectRaw aliases, view columns. <c>[Computed] public int OrdersCount { get; set; }</c>
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public class ComputedAttribute : Attribute
    {
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
        public Type? RelatedType { get; set; }

        /// <summary>Foreign key column(s) on the related table ("A, B" for a composite key)</summary>
        public string? ForeignKey { get; set; }

        /// <summary>Key column(s) of this model (default: its primary key)</summary>
        public string LocalKey { get; set; } = "Id";

        public HasOneAttribute(Type relatedType, string? foreignKey = null)
        {
            RelatedType = relatedType;
            ForeignKey = foreignKey;
        }

        public HasOneAttribute(string? foreignKey = null)
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
        public Type? RelatedType { get; set; }

        /// <summary>Foreign key column(s) on the related table ("A, B" for a composite key)</summary>
        public string? ForeignKey { get; set; }

        /// <summary>Key column(s) of this model (default: its primary key)</summary>
        public string LocalKey { get; set; } = "Id";

        public HasManyAttribute(Type relatedType, string? foreignKey = null)
        {
            RelatedType = relatedType;
            ForeignKey = foreignKey;
        }

        public HasManyAttribute(string? foreignKey = null)
        {
            ForeignKey = foreignKey;
        }
    }

    /// <summary>
    /// Polymorphic one-to-many for eager loading: comments of a post where Comments.CommentableType = "Post"
    /// and Comments.CommentableId = post.Id → <c>[MorphMany(typeof(Comment), "Commentable")]</c>
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class MorphManyAttribute : Attribute
    {
        public Type RelatedType { get; set; }

        /// <summary>Prefix of the {Name}Type / {Name}Id columns on the related table</summary>
        public string Name { get; set; }

        public string LocalKey { get; set; } = "Id";

        public MorphManyAttribute(Type relatedType, string name)
        {
            RelatedType = relatedType;
            Name = name;
        }
    }

    /// <summary>
    /// Polymorphic one-to-one for eager loading (see <see cref="MorphManyAttribute"/>)
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class MorphOneAttribute : Attribute
    {
        public Type RelatedType { get; set; }
        public string Name { get; set; }
        public string LocalKey { get; set; } = "Id";

        public MorphOneAttribute(Type relatedType, string name)
        {
            RelatedType = relatedType;
            Name = name;
        }
    }

    /// <summary>
    /// Inverse of MorphMany / MorphOne for eager loading: the owner named by {Name}Type / {Name}Id of this model,
    /// e.g. <c>[MorphTo("Commentable")] public ModelBase? Commentable { get; set; }</c> on a comment
    /// (Comment.Query().With("Commentable") loads the posts and videos with one query per type)
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class MorphToAttribute : Attribute
    {
        /// <summary>Prefix of the {Name}Type / {Name}Id columns on this model</summary>
        public string Name { get; set; }

        public MorphToAttribute(string name)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }
    }

    /// <summary>
    /// Specifies a BelongsTo relationship
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class BelongsToAttribute : Attribute
    {
        public Type? RelatedType { get; set; }

        /// <summary>Foreign key column(s) on this model ("A, B" for a composite key)</summary>
        public string? ForeignKey { get; set; }

        /// <summary>Key column(s) of the owner (default: its primary key)</summary>
        public string OwnerKey { get; set; } = "Id";

        public BelongsToAttribute(Type relatedType, string? foreignKey = null)
        {
            RelatedType = relatedType;
            ForeignKey = foreignKey;
        }

        public BelongsToAttribute(string? foreignKey = null)
        {
            ForeignKey = foreignKey;
        }
    }
}
