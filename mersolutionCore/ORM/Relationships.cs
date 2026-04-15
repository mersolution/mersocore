using System;
using System.Collections.Generic;
using System.Linq;
using mersolutionCore.ORM.Entity;

namespace mersolutionCore.ORM
{
    /// <summary>
    /// Relationship helper methods for Model
    /// </summary>
    public static class RelationshipExtensions
    {
        /// <summary>
        /// HasMany - Bir model birden fazla ilişkili kayda sahip
        /// </summary>
        public static List<TRelated> HasMany<T, TRelated>(this T model, string foreignKey) 
            where T : Model<T>, new() 
            where TRelated : Model<TRelated>, new()
        {
            var metadata = Model<T>.GetMetadata<T>();
            var pkValue = metadata.PrimaryKeyProperty?.GetValue(model);
            
            if (pkValue == null) return new List<TRelated>();

            return Model<TRelated>.Query().Where(foreignKey, pkValue).Get();
        }

        /// <summary>
        /// BelongsTo - Bir model başka bir modele ait
        /// </summary>
        public static TRelated BelongsTo<T, TRelated>(this T model, string foreignKey) 
            where T : Model<T>, new() 
            where TRelated : Model<TRelated>, new()
        {
            var prop = typeof(T).GetProperty(foreignKey);
            if (prop == null) return default;

            var fkValue = prop.GetValue(model);
            if (fkValue == null) return default;

            return Model<TRelated>.Find(fkValue);
        }

        /// <summary>
        /// HasOne - Bir model tek bir ilişkili kayda sahip
        /// </summary>
        public static TRelated HasOne<T, TRelated>(this T model, string foreignKey) 
            where T : Model<T>, new() 
            where TRelated : Model<TRelated>, new()
        {
            var metadata = Model<T>.GetMetadata<T>();
            var pkValue = metadata.PrimaryKeyProperty?.GetValue(model);
            
            if (pkValue == null) return default;

            return Model<TRelated>.Query().Where(foreignKey, pkValue).First();
        }
    }

    /// <summary>
    /// Eager Loading için ilişki bilgisi
    /// </summary>
    public class RelationshipInfo
    {
        public string Name { get; set; }
        public Type RelatedType { get; set; }
        public string ForeignKey { get; set; }
        public RelationType Type { get; set; }
    }

    public enum RelationType
    {
        HasMany,
        BelongsTo,
        HasOne
    }
}
