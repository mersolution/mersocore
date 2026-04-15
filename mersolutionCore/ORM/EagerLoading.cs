using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using mersolutionCore.ORM.Entity;

namespace mersolutionCore.ORM
{
    /// <summary>
    /// Eager Loading - İlişkileri önceden yükle
    /// </summary>
    public class EagerLoader<T> where T : Model<T>, new()
    {
        private readonly List<string> _includes = new List<string>();
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
            _includes.Add(relation);
            return this;
        }

        /// <summary>
        /// Sorguyu çalıştır ve ilişkileri yükle
        /// </summary>
        public List<T> Get()
        {
            var results = _query.Get();
            
            foreach (var relation in _includes)
            {
                LoadRelation(results, relation);
            }

            return results;
        }

        /// <summary>
        /// İlk kaydı getir
        /// </summary>
        public T First()
        {
            var results = Get();
            return results.FirstOrDefault();
        }

        private void LoadRelation(List<T> models, string relationName)
        {
            if (models.Count == 0) return;

            // Nested relation desteği (örn: "Orders.Items")
            var parts = relationName.Split('.');
            var currentRelation = parts[0];

            var prop = typeof(T).GetProperty(currentRelation);
            if (prop == null) return;

            // HasMany attribute kontrolü
            var hasManyAttr = prop.GetCustomAttribute<HasManyAttribute>();
            if (hasManyAttr != null)
            {
                LoadHasMany(models, prop, hasManyAttr);
                return;
            }

            // BelongsTo attribute kontrolü
            var belongsToAttr = prop.GetCustomAttribute<BelongsToAttribute>();
            if (belongsToAttr != null)
            {
                LoadBelongsTo(models, prop, belongsToAttr);
                return;
            }

            // HasOne attribute kontrolü
            var hasOneAttr = prop.GetCustomAttribute<HasOneAttribute>();
            if (hasOneAttr != null)
            {
                LoadHasOne(models, prop, hasOneAttr);
            }
        }

        private void LoadHasMany(List<T> models, PropertyInfo prop, HasManyAttribute attr)
        {
            var metadata = Model<T>.GetMetadata<T>();
            var pkValues = models
                .Select(m => metadata.PrimaryKeyProperty?.GetValue(m))
                .Where(v => v != null)
                .Distinct()
                .ToList();

            if (pkValues.Count == 0) return;

            // İlişkili kayıtları tek sorguda çek
            var relatedType = attr.RelatedType;
            var relatedMetadata = ModelMetadata.GetMetadata(relatedType);

            using (var db = ModelBase.ConnectionFactory())
            {
                var paramNames = new List<string>();
                for (int i = 0; i < pkValues.Count; i++)
                {
                    paramNames.Add($"@p{i}");
                    db.ParametersAdd($"@p{i}", pkValues[i]);
                }

                var sql = $"SELECT * FROM {relatedMetadata.TableName} WHERE {attr.ForeignKey} IN ({string.Join(", ", paramNames)})";
                var dt = db.RunDataTable(sql);

                // Sonuçları grupla ve modellere ata
                var relatedItems = new Dictionary<object, List<object>>();
                foreach (System.Data.DataRow row in dt.Rows)
                {
                    var fkValue = row[attr.ForeignKey];
                    if (!relatedItems.ContainsKey(fkValue))
                        relatedItems[fkValue] = new List<object>();

                    var relatedModel = Activator.CreateInstance(relatedType);
                    MapRowToModel(row, relatedModel, relatedMetadata);
                    relatedItems[fkValue].Add(relatedModel);
                }

                // Modellere ata
                foreach (var model in models)
                {
                    var pkValue = metadata.PrimaryKeyProperty?.GetValue(model);
                    if (pkValue != null && relatedItems.TryGetValue(pkValue, out var items))
                    {
                        var listType = typeof(List<>).MakeGenericType(relatedType);
                        var list = Activator.CreateInstance(listType);
                        var addMethod = listType.GetMethod("Add");
                        foreach (var item in items)
                        {
                            addMethod.Invoke(list, new[] { item });
                        }
                        prop.SetValue(model, list);
                    }
                }
            }
        }

        private void LoadBelongsTo(List<T> models, PropertyInfo prop, BelongsToAttribute attr)
        {
            var fkProp = typeof(T).GetProperty(attr.ForeignKey);
            if (fkProp == null) return;

            var fkValues = models
                .Select(m => fkProp.GetValue(m))
                .Where(v => v != null)
                .Distinct()
                .ToList();

            if (fkValues.Count == 0) return;

            var relatedType = attr.RelatedType;
            var relatedMetadata = ModelMetadata.GetMetadata(relatedType);

            using (var db = ModelBase.ConnectionFactory())
            {
                var paramNames = new List<string>();
                for (int i = 0; i < fkValues.Count; i++)
                {
                    paramNames.Add($"@p{i}");
                    db.ParametersAdd($"@p{i}", fkValues[i]);
                }

                var sql = $"SELECT * FROM {relatedMetadata.TableName} WHERE {relatedMetadata.PrimaryKeyColumn} IN ({string.Join(", ", paramNames)})";
                var dt = db.RunDataTable(sql);

                var relatedItems = new Dictionary<object, object>();
                foreach (System.Data.DataRow row in dt.Rows)
                {
                    var pkValue = row[relatedMetadata.PrimaryKeyColumn];
                    var relatedModel = Activator.CreateInstance(relatedType);
                    MapRowToModel(row, relatedModel, relatedMetadata);
                    relatedItems[pkValue] = relatedModel;
                }

                foreach (var model in models)
                {
                    var fkValue = fkProp.GetValue(model);
                    if (fkValue != null && relatedItems.TryGetValue(fkValue, out var related))
                    {
                        prop.SetValue(model, related);
                    }
                }
            }
        }

        private void LoadHasOne(List<T> models, PropertyInfo prop, HasOneAttribute attr)
        {
            var metadata = Model<T>.GetMetadata<T>();
            var pkValues = models
                .Select(m => metadata.PrimaryKeyProperty?.GetValue(m))
                .Where(v => v != null)
                .Distinct()
                .ToList();

            if (pkValues.Count == 0) return;

            var relatedType = attr.RelatedType;
            var relatedMetadata = ModelMetadata.GetMetadata(relatedType);

            using (var db = ModelBase.ConnectionFactory())
            {
                var paramNames = new List<string>();
                for (int i = 0; i < pkValues.Count; i++)
                {
                    paramNames.Add($"@p{i}");
                    db.ParametersAdd($"@p{i}", pkValues[i]);
                }

                var sql = $"SELECT * FROM {relatedMetadata.TableName} WHERE {attr.ForeignKey} IN ({string.Join(", ", paramNames)})";
                var dt = db.RunDataTable(sql);

                var relatedItems = new Dictionary<object, object>();
                foreach (System.Data.DataRow row in dt.Rows)
                {
                    var fkValue = row[attr.ForeignKey];
                    var relatedModel = Activator.CreateInstance(relatedType);
                    MapRowToModel(row, relatedModel, relatedMetadata);
                    relatedItems[fkValue] = relatedModel;
                }

                foreach (var model in models)
                {
                    var pkValue = metadata.PrimaryKeyProperty?.GetValue(model);
                    if (pkValue != null && relatedItems.TryGetValue(pkValue, out var related))
                    {
                        prop.SetValue(model, related);
                    }
                }
            }
        }

        private void MapRowToModel(System.Data.DataRow row, object model, ModelMetadata metadata)
        {
            foreach (var prop in metadata.Properties)
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
