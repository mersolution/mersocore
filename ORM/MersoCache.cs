using System;
using System.Collections.Generic;
using CacheApi = mersolutionCore.Cache.Cache;

namespace mersolutionCore.ORM
{
    /// <summary>
    /// ORM-facing cache API. Storage is the shared <c>mersolutionCore.Cache.Cache</c> / MemoryCache instance.
    /// </summary>
    public static class MersoCache
    {
        private static readonly TimeSpan Forever = TimeSpan.FromDays(36500);

        public static void Set(string key, object value, TimeSpan? expiration = null)
        {
            CacheApi.Set(key, value, expiration ?? Forever);
        }

        public static T Get<T>(string key)
        {
            return CacheApi.Get<T>(key);
        }

        public static bool Has(string key)
        {
            return CacheApi.Has(key);
        }

        public static void Forget(string key)
        {
            CacheApi.Forget(key);
        }

        public static void Flush()
        {
            CacheApi.Flush();
        }

        public static void FlushByPrefix(string prefix)
        {
            CacheApi.ForgetByPrefix(prefix);
        }

        public static T Remember<T>(string key, TimeSpan expiration, Func<T> factory)
        {
            return CacheApi.Remember(key, expiration, factory);
        }

        public static T RememberForever<T>(string key, Func<T> factory)
        {
            return CacheApi.Remember(key, Forever, factory);
        }

        public static void CleanExpired()
        {
            // Expired entries are removed by MemoryCache's cleanup timer and on Get/Has.
        }
    }

    /// <summary>
    /// QueryBuilder cache extensions
    /// </summary>
    public static class QueryCacheExtensions
    {
        public static List<T> Remember<T>(this QueryBuilder<T> query, TimeSpan expiration) where T : Model<T>, new()
        {
            var cacheKey = $"query_{typeof(T).Name}_{query.ToSql().GetHashCode()}";
            return MersoCache.Remember(cacheKey, expiration, () => query.Get());
        }

        public static List<T> RememberForever<T>(this QueryBuilder<T> query) where T : Model<T>, new()
        {
            var cacheKey = $"query_{typeof(T).Name}_{query.ToSql().GetHashCode()}";
            return MersoCache.RememberForever(cacheKey, () => query.Get());
        }
    }
}
