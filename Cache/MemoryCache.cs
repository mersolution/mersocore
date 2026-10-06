using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace mersolutionCore.Cache
{
    /// <summary>
    /// Thread-safe in-memory cache implementation
    /// </summary>
    public class MemoryCache : ICache
    {
        private static readonly Lazy<MemoryCache> _instance = new Lazy<MemoryCache>(() => new MemoryCache());
        private readonly ConcurrentDictionary<string, CacheItem> _cache = new ConcurrentDictionary<string, CacheItem>();
        private readonly ConcurrentDictionary<string, HashSet<string>> _tags = new ConcurrentDictionary<string, HashSet<string>>();
        private readonly Timer _cleanupTimer;
        private bool _enabled = true;
        private TimeSpan _defaultTtl = TimeSpan.FromMinutes(60);

        /// <summary>
        /// Singleton instance
        /// </summary>
        public static MemoryCache Instance => _instance.Value;

        /// <summary>
        /// Cache statistics
        /// </summary>
        public CacheStats Stats { get; } = new CacheStats();

        private MemoryCache()
        {
            // Cleanup expired items every minute
            _cleanupTimer = new Timer(CleanupExpired, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        #region Configuration

        /// <summary>
        /// Enable cache
        /// </summary>
        public void Enable() => _enabled = true;

        /// <summary>
        /// Disable cache
        /// </summary>
        public void Disable() => _enabled = false;

        /// <summary>
        /// Check if cache is enabled
        /// </summary>
        public bool IsEnabled => _enabled;

        /// <summary>
        /// Set default TTL
        /// </summary>
        public void SetDefaultTtl(TimeSpan ttl) => _defaultTtl = ttl;

        #endregion

        #region Core Methods

        /// <summary>
        /// Get value from cache
        /// </summary>
        public T? Get<T>(string key)
        {
            return TryGet<T>(key, out var value) ? value : default;
        }

        /// <summary>
        /// Get value in one step (no race between "exists" and "read")
        /// </summary>
        public bool TryGet<T>(string key, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out T value)
        {
            value = default;
            if (!_enabled) return false;

            if (_cache.TryGetValue(key, out var item))
            {
                if (!item.IsExpired)
                {
                    Stats.RecordHit();
                    value = (T)item.Value!;
                    return true;
                }

                Remove(key);
            }

            Stats.RecordMiss();
            return false;
        }

        /// <summary>
        /// Get value from cache with default
        /// </summary>
        public T Get<T>(string key, T defaultValue)
        {
            var value = Get<T>(key);
            return value != null ? value : defaultValue;
        }

        /// <summary>
        /// Set value in cache
        /// </summary>
        public void Set<T>(string key, T value, TimeSpan? ttl = null, string[]? tags = null)
        {
            if (!_enabled) return;

            var expiration = ttl ?? _defaultTtl;
            var item = new CacheItem(value, DateTime.UtcNow.Add(expiration));

            _cache[key] = item;
            Stats.RecordWrite();

            // Handle tags (HashSet is not thread-safe: lock the set itself)
            if (tags != null)
            {
                foreach (var tag in tags)
                {
                    var set = _tags.GetOrAdd(tag, _ => new HashSet<string>());
                    lock (set)
                    {
                        set.Add(key);
                    }
                }
            }
        }

        /// <summary>
        /// Check if key exists and is not expired
        /// </summary>
        public bool Has(string key)
        {
            if (!_enabled) return false;

            if (_cache.TryGetValue(key, out var item))
            {
                if (!item.IsExpired)
                    return true;
                Remove(key);
            }
            return false;
        }

        /// <summary>
        /// Remove item from cache
        /// </summary>
        public bool Remove(string key)
        {
            return _cache.TryRemove(key, out _);
        }

        /// <summary>
        /// Remove item (alias for Remove)
        /// </summary>
        public bool Forget(string key) => Remove(key);

        /// <summary>
        /// Clear all cache
        /// </summary>
        public void Flush()
        {
            _cache.Clear();
            _tags.Clear();
            Stats.Reset();
        }

        /// <summary>
        /// Clear all cache (alias for Flush)
        /// </summary>
        public void Clear() => Flush();

        #endregion

        #region Advanced Methods

        /// <summary>
        /// Get or set value (remember pattern)
        /// </summary>
        public T Remember<T>(string key, TimeSpan ttl, Func<T> factory)
        {
            if (!_enabled)
                return factory();

            if (TryGet<T>(key, out var cached))
                return cached;

            var value = factory();
            Set(key, value, ttl);
            return value;
        }

        /// <summary>
        /// Get or set value with default TTL
        /// </summary>
        public T Remember<T>(string key, Func<T> factory)
        {
            return Remember(key, _defaultTtl, factory);
        }

        /// <summary>
        /// Get or set value forever (no expiration)
        /// </summary>
        public T RememberForever<T>(string key, Func<T> factory)
        {
            return Remember(key, TimeSpan.FromDays(365 * 10), factory);
        }

        /// <summary>
        /// Get and delete
        /// </summary>
        public T? Pull<T>(string key)
        {
            var value = Get<T>(key);
            Remove(key);
            return value;
        }

        /// <summary>
        /// Add only if not exists
        /// </summary>
        public bool Add<T>(string key, T value, TimeSpan? ttl = null)
        {
            if (Has(key))
                return false;

            Set(key, value, ttl);
            return true;
        }

        /// <summary>
        /// Increment numeric value
        /// </summary>
        public long Increment(string key, long amount = 1)
        {
            // Read-modify-write must be atomic: two threads incrementing at once lost an update before
            lock (_incrementLock)
            {
                if (_cache.TryGetValue(key, out var item) && !item.IsExpired)
                {
                    var newValue = Convert.ToInt64(item.Value) + amount;
                    item.Value = newValue;
                    return newValue;
                }

                Set(key, amount);
                return amount;
            }
        }

        private readonly object _incrementLock = new object();

        /// <summary>
        /// Decrement numeric value
        /// </summary>
        public long Decrement(string key, long amount = 1)
        {
            return Increment(key, -amount);
        }

        #endregion

        #region Tag Operations

        /// <summary>
        /// Remove all items with tag
        /// </summary>
        public void ForgetByTag(string tag)
        {
            if (_tags.TryRemove(tag, out var keys))
            {
                List<string> snapshot;
                lock (keys)
                {
                    snapshot = keys.ToList();
                }

                foreach (var key in snapshot)
                {
                    Remove(key);
                }
            }
        }

        /// <summary>
        /// Remove all items with prefix
        /// </summary>
        public int ForgetByPrefix(string prefix)
        {
            var keysToRemove = _cache.Keys.Where(k => k.StartsWith(prefix)).ToList();
            foreach (var key in keysToRemove)
            {
                Remove(key);
            }
            return keysToRemove.Count;
        }

        #endregion

        #region Utility

        /// <summary>
        /// Get all cache keys
        /// </summary>
        public IEnumerable<string> Keys()
        {
            // Enumerate pairs: indexing by key throws when the item expired/was removed meanwhile
            return _cache.Where(x => !x.Value.IsExpired).Select(x => x.Key).ToList();
        }

        /// <summary>
        /// Get cache count
        /// </summary>
        public int Count => _cache.Count(x => !x.Value.IsExpired);

        /// <summary>
        /// Get cache statistics
        /// </summary>
        public CacheStats GetStats() => Stats;

        private void CleanupExpired(object? state)
        {
            var expiredKeys = _cache.Where(x => x.Value.IsExpired).Select(x => x.Key).ToList();
            foreach (var key in expiredKeys)
            {
                Remove(key);
            }
        }

        #endregion
    }

    /// <summary>
    /// Cache item wrapper
    /// </summary>
    internal class CacheItem
    {
        public object? Value { get; set; }
        public DateTime ExpiresAt { get; }
        public bool IsExpired => DateTime.UtcNow > ExpiresAt;

        public CacheItem(object? value, DateTime expiresAt)
        {
            Value = value;
            ExpiresAt = expiresAt;
        }
    }

    /// <summary>
    /// Cache statistics
    /// </summary>
    public class CacheStats
    {
        private long _hits;
        private long _misses;
        private long _writes;

        public long Hits { get => Interlocked.Read(ref _hits); set => Interlocked.Exchange(ref _hits, value); }
        public long Misses { get => Interlocked.Read(ref _misses); set => Interlocked.Exchange(ref _misses, value); }
        public long Writes { get => Interlocked.Read(ref _writes); set => Interlocked.Exchange(ref _writes, value); }

        public double HitRatio
        {
            get
            {
                var hits = Hits;
                var total = hits + Misses;
                return total > 0 ? (double)hits / total : 0;
            }
        }

        internal void RecordHit() => Interlocked.Increment(ref _hits);
        internal void RecordMiss() => Interlocked.Increment(ref _misses);
        internal void RecordWrite() => Interlocked.Increment(ref _writes);

        public void Reset()
        {
            Hits = 0;
            Misses = 0;
            Writes = 0;
        }
    }

    /// <summary>
    /// Cache interface
    /// </summary>
    public interface ICache
    {
        T? Get<T>(string key);
        void Set<T>(string key, T value, TimeSpan? ttl = null, string[]? tags = null);
        bool Has(string key);
        bool Remove(string key);
        void Flush();
        T Remember<T>(string key, TimeSpan ttl, Func<T> factory);
    }

    /// <summary>
    /// Static cache helper. In-process memory by default; <see cref="UseStore"/> moves every Cache / QCache /
    /// MersoCache call (and the query cache) to a store shared by all servers, e.g. Redis.
    /// </summary>
    public static class Cache
    {
        private static DistributedCache? _distributed;

        /// <summary>
        /// Use a shared store (Redis, SQL ...) instead of process memory, so several servers see the same entries
        /// </summary>
        /// <param name="store">Shared storage (<see cref="DistributedCacheStore"/> wraps IDistributedCache)</param>
        /// <param name="keyPrefix">Prefix of every key</param>
        public static void UseStore(ICacheStore store, string keyPrefix = "mc:")
        {
            _distributed = new DistributedCache(store, keyPrefix);
        }

        /// <summary>
        /// Go back to the in-process memory cache
        /// </summary>
        public static void UseMemory()
        {
            _distributed = null;
        }

        /// <summary>
        /// True when a shared store is in use (<see cref="UseStore"/>)
        /// </summary>
        public static bool IsDistributed => _distributed != null;

        /// <summary>
        /// Get value from cache
        /// </summary>
        public static T? Get<T>(string key) => _distributed != null ? _distributed.Get<T>(key) : MemoryCache.Instance.Get<T>(key);

        /// <summary>
        /// Get value with default
        /// </summary>
        public static T Get<T>(string key, T defaultValue)
            => _distributed != null ? _distributed.Get(key, defaultValue) : MemoryCache.Instance.Get(key, defaultValue);

        /// <summary>
        /// Get value in one step (false when missing / expired)
        /// </summary>
        public static bool TryGet<T>(string key, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out T value)
            => _distributed != null ? _distributed.TryGet(key, out value) : MemoryCache.Instance.TryGet(key, out value);

        /// <summary>
        /// Set value in cache
        /// </summary>
        public static void Set<T>(string key, T value, TimeSpan? ttl = null, string[]? tags = null)
        {
            if (_distributed != null)
                _distributed.Set(key, value, ttl, tags);
            else
                MemoryCache.Instance.Set(key, value, ttl, tags);
        }

        /// <summary>
        /// Check if key exists
        /// </summary>
        public static bool Has(string key) => _distributed != null ? _distributed.Has(key) : MemoryCache.Instance.Has(key);

        /// <summary>
        /// Remove item
        /// </summary>
        public static bool Forget(string key) => _distributed != null ? _distributed.Forget(key) : MemoryCache.Instance.Forget(key);

        /// <summary>
        /// Clear all cache
        /// </summary>
        public static void Flush()
        {
            if (_distributed != null)
                _distributed.Flush();
            else
                MemoryCache.Instance.Flush();
        }

        /// <summary>
        /// Get or set value
        /// </summary>
        public static T Remember<T>(string key, TimeSpan ttl, Func<T> factory)
            => _distributed != null ? _distributed.Remember(key, ttl, factory) : MemoryCache.Instance.Remember(key, ttl, factory);

        /// <summary>
        /// Get or set value with default TTL
        /// </summary>
        public static T Remember<T>(string key, Func<T> factory)
            => _distributed != null ? _distributed.Remember(key, factory) : MemoryCache.Instance.Remember(key, factory);

        /// <summary>
        /// Get and delete
        /// </summary>
        public static T? Pull<T>(string key) => _distributed != null ? _distributed.Pull<T>(key) : MemoryCache.Instance.Pull<T>(key);

        /// <summary>
        /// Increment value
        /// </summary>
        public static long Increment(string key, long amount = 1)
            => _distributed != null ? _distributed.Increment(key, amount) : MemoryCache.Instance.Increment(key, amount);

        /// <summary>
        /// Decrement value
        /// </summary>
        public static long Decrement(string key, long amount = 1)
            => _distributed != null ? _distributed.Decrement(key, amount) : MemoryCache.Instance.Decrement(key, amount);

        /// <summary>
        /// Remove by tag
        /// </summary>
        public static void ForgetByTag(string tag)
        {
            if (_distributed != null)
                _distributed.ForgetByTag(tag);
            else
                MemoryCache.Instance.ForgetByTag(tag);
        }

        /// <summary>
        /// Remove by prefix (distributed store: the first call for a new prefix clears the whole cache once and returns -1)
        /// </summary>
        public static int ForgetByPrefix(string prefix)
            => _distributed != null ? _distributed.ForgetByPrefix(prefix) : MemoryCache.Instance.ForgetByPrefix(prefix);

        /// <summary>
        /// Get statistics (of this server)
        /// </summary>
        public static CacheStats Stats => _distributed != null ? _distributed.Stats : MemoryCache.Instance.Stats;

        /// <summary>
        /// Enable cache
        /// </summary>
        public static void Enable()
        {
            MemoryCache.Instance.Enable();
            _distributed?.Enable();
        }

        /// <summary>
        /// Disable cache
        /// </summary>
        public static void Disable()
        {
            MemoryCache.Instance.Disable();
            _distributed?.Disable();
        }

        /// <summary>
        /// Set default TTL
        /// </summary>
        public static void SetDefaultTtl(TimeSpan ttl)
        {
            MemoryCache.Instance.SetDefaultTtl(ttl);
            _distributed?.SetDefaultTtl(ttl);
        }
    }
}
