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

        public static T? Get<T>(string key)
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
    /// QueryBuilder cache extensions. Entries carry the table tag: <c>QCache.ForgetTable("Users")</c> drops them
    /// (also in a distributed cache).
    /// </summary>
    public static class QueryCacheExtensions
    {
        private static readonly TimeSpan Forever = TimeSpan.FromDays(36500);

        // Key = SQL + parameter values: Where("Id", 1) and Where("Id", 2) must not share a cache entry
        public static List<T> Remember<T>(this QueryBuilder<T> query, TimeSpan expiration) where T : Model<T>, new()
        {
            var cacheKey = $"query_{typeof(T).Name}_{query.ToCacheKey()}";
            if (CacheApi.TryGet<List<T>>(cacheKey, out var cached) && cached != null)
            {
                // From a distributed cache the models are new objects: mark them loaded (Save writes changes only)
                foreach (var model in cached)
                {
                    if (model != null && !model.HasOriginal)
                        model.SyncOriginal();
                }
                return cached;
            }

            var result = query.Get();
            CacheApi.Set(cacheKey, result, expiration, mersolutionCore.Cache.QueryCache.Tags(ModelBase.GetMetadata<T>().TableName));
            return result;
        }

        public static List<T> RememberForever<T>(this QueryBuilder<T> query) where T : Model<T>, new()
        {
            return Remember(query, Forever);
        }
    }
}
