using System;
using System.Collections.Generic;

namespace mersolutionCore.ORM
{
    /// <summary>
    /// MersoCache - Basit in-memory cache sistemi
    /// </summary>
    public static class MersoCache
    {
        private static readonly Dictionary<string, CacheItem> _cache = new Dictionary<string, CacheItem>();
        private static readonly object _lock = new object();

        /// <summary>
        /// Cache'e ekle
        /// </summary>
        public static void Set(string key, object value, TimeSpan? expiration = null)
        {
            lock (_lock)
            {
                _cache[key] = new CacheItem
                {
                    Value = value,
                    ExpiresAt = expiration.HasValue ? DateTime.UtcNow.Add(expiration.Value) : (DateTime?)null
                };
            }
        }

        /// <summary>
        /// Cache'den al
        /// </summary>
        public static T Get<T>(string key)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var item))
                {
                    if (item.ExpiresAt.HasValue && item.ExpiresAt.Value < DateTime.UtcNow)
                    {
                        _cache.Remove(key);
                        return default;
                    }
                    return (T)item.Value;
                }
                return default;
            }
        }

        /// <summary>
        /// Cache'de var mı?
        /// </summary>
        public static bool Has(string key)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var item))
                {
                    if (item.ExpiresAt.HasValue && item.ExpiresAt.Value < DateTime.UtcNow)
                    {
                        _cache.Remove(key);
                        return false;
                    }
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Cache'den sil
        /// </summary>
        public static void Forget(string key)
        {
            lock (_lock)
            {
                _cache.Remove(key);
            }
        }

        /// <summary>
        /// Tüm cache'i temizle
        /// </summary>
        public static void Flush()
        {
            lock (_lock)
            {
                _cache.Clear();
            }
        }

        /// <summary>
        /// Belirli prefix ile başlayan cache'leri temizle
        /// </summary>
        public static void FlushByPrefix(string prefix)
        {
            lock (_lock)
            {
                var keysToRemove = new List<string>();
                foreach (var key in _cache.Keys)
                {
                    if (key.StartsWith(prefix))
                        keysToRemove.Add(key);
                }
                foreach (var key in keysToRemove)
                {
                    _cache.Remove(key);
                }
            }
        }

        /// <summary>
        /// Cache'den al veya oluştur
        /// </summary>
        public static T Remember<T>(string key, TimeSpan expiration, Func<T> factory)
        {
            if (Has(key))
                return Get<T>(key);

            var value = factory();
            Set(key, value, expiration);
            return value;
        }

        /// <summary>
        /// Cache'den al veya oluştur (süresiz)
        /// </summary>
        public static T RememberForever<T>(string key, Func<T> factory)
        {
            if (Has(key))
                return Get<T>(key);

            var value = factory();
            Set(key, value);
            return value;
        }

        /// <summary>
        /// Süresi dolmuş cache'leri temizle
        /// </summary>
        public static void CleanExpired()
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                var keysToRemove = new List<string>();

                foreach (var kvp in _cache)
                {
                    if (kvp.Value.ExpiresAt.HasValue && kvp.Value.ExpiresAt.Value < now)
                        keysToRemove.Add(kvp.Key);
                }

                foreach (var key in keysToRemove)
                {
                    _cache.Remove(key);
                }
            }
        }

        private class CacheItem
        {
            public object Value { get; set; }
            public DateTime? ExpiresAt { get; set; }
        }
    }

    /// <summary>
    /// QueryBuilder için cache extension
    /// </summary>
    public static class QueryCacheExtensions
    {
        /// <summary>
        /// Sorgu sonucunu cache'le
        /// </summary>
        public static List<T> Remember<T>(this QueryBuilder<T> query, TimeSpan expiration) where T : Model<T>, new()
        {
            var cacheKey = $"query_{typeof(T).Name}_{query.ToSql().GetHashCode()}";
            return MersoCache.Remember(cacheKey, expiration, () => query.Get());
        }

        /// <summary>
        /// Sorgu sonucunu süresiz cache'le
        /// </summary>
        public static List<T> RememberForever<T>(this QueryBuilder<T> query) where T : Model<T>, new()
        {
            var cacheKey = $"query_{typeof(T).Name}_{query.ToSql().GetHashCode()}";
            return MersoCache.RememberForever(cacheKey, () => query.Get());
        }
    }
}
