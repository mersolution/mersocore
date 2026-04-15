using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace mersolutionCore.Cache
{
    /// <summary>
    /// Query-specific cache for database operations
    /// </summary>
    public class QueryCache
    {
        private static readonly Lazy<QueryCache> _instance = new Lazy<QueryCache>(() => new QueryCache());
        private bool _enabled = true;
        private TimeSpan _defaultTtl = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Singleton instance
        /// </summary>
        public static QueryCache Instance => _instance.Value;

        private QueryCache() { }

        #region Configuration

        /// <summary>
        /// Enable query cache
        /// </summary>
        public void Enable() => _enabled = true;

        /// <summary>
        /// Disable query cache
        /// </summary>
        public void Disable() => _enabled = false;

        /// <summary>
        /// Check if query cache is enabled
        /// </summary>
        public bool IsEnabled => _enabled;

        /// <summary>
        /// Set default TTL for queries
        /// </summary>
        public void SetDefaultTtl(TimeSpan ttl) => _defaultTtl = ttl;

        #endregion

        #region Core Methods

        /// <summary>
        /// Get cached query result
        /// </summary>
        public T Get<T>(string sql, object parameters = null)
        {
            if (!_enabled) return default;

            var key = GenerateKey(sql, parameters);
            return Cache.Get<T>(key);
        }

        /// <summary>
        /// Set query result in cache
        /// </summary>
        public void Set<T>(string sql, T result, object parameters = null, TimeSpan? ttl = null, string tableName = null)
        {
            if (!_enabled) return;

            var key = GenerateKey(sql, parameters);
            var tags = tableName != null ? new[] { $"table:{tableName}" } : null;
            Cache.Set(key, result, ttl ?? _defaultTtl, tags);
        }

        /// <summary>
        /// Remember query result (get or execute)
        /// </summary>
        public T Remember<T>(string sql, Func<T> query, object parameters = null, TimeSpan? ttl = null, string tableName = null)
        {
            if (!_enabled)
                return query();

            var key = GenerateKey(sql, parameters);

            if (Cache.Has(key))
                return Cache.Get<T>(key);

            var result = query();
            var tags = tableName != null ? new[] { $"table:{tableName}" } : null;
            Cache.Set(key, result, ttl ?? _defaultTtl, tags);
            return result;
        }

        /// <summary>
        /// Invalidate cache for a specific table
        /// </summary>
        public void ForgetTable(string tableName)
        {
            Cache.ForgetByTag($"table:{tableName}");
        }

        /// <summary>
        /// Invalidate cache for multiple tables
        /// </summary>
        public void ForgetTables(params string[] tableNames)
        {
            foreach (var table in tableNames)
            {
                ForgetTable(table);
            }
        }

        /// <summary>
        /// Invalidate specific query
        /// </summary>
        public void Forget(string sql, object parameters = null)
        {
            var key = GenerateKey(sql, parameters);
            Cache.Forget(key);
        }

        /// <summary>
        /// Clear all query cache
        /// </summary>
        public void Flush()
        {
            Cache.ForgetByPrefix("query:");
        }

        #endregion

        #region Key Generation

        /// <summary>
        /// Generate cache key from SQL and parameters
        /// </summary>
        public string GenerateKey(string sql, object parameters = null)
        {
            var sb = new StringBuilder(sql.Trim().ToLowerInvariant());

            if (parameters != null)
            {
                if (parameters is Dictionary<string, object> dict)
                {
                    foreach (var kvp in dict)
                    {
                        sb.Append($"|{kvp.Key}={kvp.Value}");
                    }
                }
                else
                {
                    foreach (var prop in parameters.GetType().GetProperties())
                    {
                        var value = prop.GetValue(parameters);
                        sb.Append($"|{prop.Name}={value}");
                    }
                }
            }

            return "query:" + ComputeHash(sb.ToString());
        }

        private string ComputeHash(string input)
        {
            using (var md5 = MD5.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(input);
                var hash = md5.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        #endregion
    }

    /// <summary>
    /// Static query cache helper
    /// </summary>
    public static class QCache
    {
        /// <summary>
        /// Get cached query result
        /// </summary>
        public static T Get<T>(string sql, object parameters = null)
            => QueryCache.Instance.Get<T>(sql, parameters);

        /// <summary>
        /// Set query result in cache
        /// </summary>
        public static void Set<T>(string sql, T result, object parameters = null, TimeSpan? ttl = null, string tableName = null)
            => QueryCache.Instance.Set(sql, result, parameters, ttl, tableName);

        /// <summary>
        /// Remember query result
        /// </summary>
        public static T Remember<T>(string sql, Func<T> query, object parameters = null, TimeSpan? ttl = null, string tableName = null)
            => QueryCache.Instance.Remember(sql, query, parameters, ttl, tableName);

        /// <summary>
        /// Invalidate table cache
        /// </summary>
        public static void ForgetTable(string tableName)
            => QueryCache.Instance.ForgetTable(tableName);

        /// <summary>
        /// Invalidate multiple tables
        /// </summary>
        public static void ForgetTables(params string[] tableNames)
            => QueryCache.Instance.ForgetTables(tableNames);

        /// <summary>
        /// Invalidate specific query
        /// </summary>
        public static void Forget(string sql, object parameters = null)
            => QueryCache.Instance.Forget(sql, parameters);

        /// <summary>
        /// Clear all query cache
        /// </summary>
        public static void Flush()
            => QueryCache.Instance.Flush();

        /// <summary>
        /// Enable query cache
        /// </summary>
        public static void Enable() => QueryCache.Instance.Enable();

        /// <summary>
        /// Disable query cache
        /// </summary>
        public static void Disable() => QueryCache.Instance.Disable();

        /// <summary>
        /// Set default TTL
        /// </summary>
        public static void SetDefaultTtl(TimeSpan ttl) => QueryCache.Instance.SetDefaultTtl(ttl);
    }
}
