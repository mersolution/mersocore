using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
        public T? Get<T>(string sql, object? parameters = null)
        {
            if (!_enabled) return default;

            var key = GenerateKey(sql, parameters);
            return Cache.Get<T>(key);
        }

        /// <summary>
        /// Set query result in cache
        /// </summary>
        public void Set<T>(string sql, T result, object? parameters = null, TimeSpan? ttl = null, string? tableName = null)
        {
            if (!_enabled) return;

            var key = GenerateKey(sql, parameters);
            Cache.Set(key, result, ttl ?? _defaultTtl, Tags(tableName));
        }

        /// <summary>
        /// Remember query result (get or execute)
        /// </summary>
        public T Remember<T>(string sql, Func<T> query, object? parameters = null, TimeSpan? ttl = null, string? tableName = null)
        {
            if (!_enabled)
                return query();

            var key = GenerateKey(sql, parameters);

            if (Cache.TryGet<T>(key, out var cached))
                return cached;

            var result = query();
            Cache.Set(key, result, ttl ?? _defaultTtl, Tags(tableName));
            return result;
        }

        /// <summary>
        /// Tag of every query cache entry (Flush) and of one table (ForgetTable)
        /// </summary>
        internal const string AllQueriesTag = "query:all";

        internal static string[] Tags(string? tableName)
        {
            return tableName != null ? new[] { AllQueriesTag, $"table:{tableName}" } : new[] { AllQueriesTag };
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
        public void Forget(string sql, object? parameters = null)
        {
            var key = GenerateKey(sql, parameters);
            Cache.Forget(key);
        }

        /// <summary>
        /// Clear all query cache
        /// </summary>
        public void Flush()
        {
            Cache.ForgetByTag(AllQueriesTag);
            if (!Cache.IsDistributed)
                Cache.ForgetByPrefix("query:");
        }

        #endregion

        #region Key Generation

        /// <summary>
        /// Generate cache key from SQL and parameters
        /// </summary>
        public string GenerateKey(string sql, object? parameters = null)
        {
            // Not lower-cased: string literals in the SQL ('Ali' vs 'ali') are different queries
            var sb = new StringBuilder(sql.Trim());

            if (parameters != null)
            {
                if (parameters is Dictionary<string, object?> dict)
                {
                    foreach (var kvp in dict.OrderBy(k => k.Key, StringComparer.Ordinal))
                    {
                        sb.Append('|').Append(kvp.Key).Append('=').Append(FormatValue(kvp.Value));
                    }
                }
                else
                {
                    foreach (var prop in parameters.GetType().GetProperties().OrderBy(p => p.Name, StringComparer.Ordinal))
                    {
                        sb.Append('|').Append(prop.Name).Append('=').Append(FormatValue(prop.GetValue(parameters)));
                    }
                }
            }

            return "query:" + ComputeHash(sb.ToString());
        }

        private static string FormatValue(object? value)
        {
            if (value == null)
                return "<null>";
            var text = value is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : value.ToString();
            return value.GetType().Name + ":" + text;
        }

        private string ComputeHash(string input)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(input);
                var hash = sha.ComputeHash(bytes);
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
        public static T? Get<T>(string sql, object? parameters = null)
            => QueryCache.Instance.Get<T>(sql, parameters);

        /// <summary>
        /// Set query result in cache
        /// </summary>
        public static void Set<T>(string sql, T result, object? parameters = null, TimeSpan? ttl = null, string? tableName = null)
            => QueryCache.Instance.Set(sql, result, parameters, ttl, tableName);

        /// <summary>
        /// Remember query result
        /// </summary>
        public static T Remember<T>(string sql, Func<T> query, object? parameters = null, TimeSpan? ttl = null, string? tableName = null)
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
        public static void Forget(string sql, object? parameters = null)
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
