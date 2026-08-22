using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace mersolutionCore.ORM
{
    /// <summary>
    /// Raw SQL Query desteği
    /// </summary>
    public static class RawQuery
    {
        /// <summary>
        /// Raw SQL çalıştır ve model listesi döndür
        /// </summary>
        public static List<T> Query<T>(string sql, Dictionary<string, object> parameters = null) where T : Model<T>, new()
        {
            var metadata = Model<T>.GetMetadata<T>();

            using (var db = ModelBase.ConnectionFactory())
            {
                if (parameters != null)
                {
                    foreach (var param in parameters)
                    {
                        db.ParametersAdd(param.Key.StartsWith("@") ? param.Key : $"@{param.Key}", param.Value);
                    }
                }

                var dt = db.RunDataTable(sql);
                return MapToModels<T>(dt, metadata);
            }
        }

        /// <summary>
        /// Raw SQL çalıştır ve tek model döndür
        /// </summary>
        public static T QueryFirst<T>(string sql, Dictionary<string, object> parameters = null) where T : Model<T>, new()
        {
            return Query<T>(sql, parameters).FirstOrDefault();
        }

        /// <summary>
        /// Raw SQL çalıştır (INSERT, UPDATE, DELETE)
        /// </summary>
        public static int Execute(string sql, Dictionary<string, object> parameters = null)
        {
            using (var db = ModelBase.ConnectionFactory())
            {
                if (parameters != null)
                {
                    foreach (var param in parameters)
                    {
                        db.ParametersAdd(param.Key.StartsWith("@") ? param.Key : $"@{param.Key}", param.Value);
                    }
                }

                db.RunExecute(sql);
                return 1; // Affected rows (basit implementasyon)
            }
        }

        /// <summary>
        /// Raw SQL çalıştır ve scalar değer döndür
        /// </summary>
        public static T Scalar<T>(string sql, Dictionary<string, object> parameters = null)
        {
            using (var db = ModelBase.ConnectionFactory())
            {
                if (parameters != null)
                {
                    foreach (var param in parameters)
                    {
                        db.ParametersAdd(param.Key.StartsWith("@") ? param.Key : $"@{param.Key}", param.Value);
                    }
                }

                var result = db.RunToObjectScaler(sql);
                if (result == null || result == DBNull.Value)
                    return default;

                return (T)Convert.ChangeType(result, typeof(T));
            }
        }

        /// <summary>
        /// Raw SQL çalıştır ve DataTable döndür
        /// </summary>
        public static DataTable QueryTable(string sql, Dictionary<string, object> parameters = null)
        {
            using (var db = ModelBase.ConnectionFactory())
            {
                if (parameters != null)
                {
                    foreach (var param in parameters)
                    {
                        db.ParametersAdd(param.Key.StartsWith("@") ? param.Key : $"@{param.Key}", param.Value);
                    }
                }

                return db.RunDataTable(sql);
            }
        }

        private static List<T> MapToModels<T>(DataTable dt, ModelMetadata metadata) where T : new()
        {
            var list = new List<T>();
            foreach (DataRow row in dt.Rows)
            {
                var model = new T();
                foreach (var prop in metadata.Properties)
                {
                    if (dt.Columns.Contains(prop.ColumnName))
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
                list.Add(model);
            }
            return list;
        }
    }

    /// <summary>
    /// Model extension for raw queries
    /// </summary>
    public static class RawQueryExtensions
    {
        /// <summary>
        /// Raw SQL sorgusu çalıştır
        /// </summary>
        public static List<T> Raw<T>(string sql, Dictionary<string, object> parameters = null) where T : Model<T>, new()
        {
            return RawQuery.Query<T>(sql, parameters);
        }
    }
}
