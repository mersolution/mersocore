using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Distributed;

namespace mersolutionCore.Cache
{
    /// <summary>
    /// Byte storage shared by every server (Redis, SQL Server, NCache ...). Implement it directly, or wrap an
    /// ASP.NET Core <see cref="IDistributedCache"/> with <see cref="DistributedCacheStore"/>.
    /// </summary>
    public interface ICacheStore
    {
        /// <summary>Stored bytes, or null when the key is missing / expired</summary>
        byte[]? Get(string key);

        /// <summary>Store bytes for <paramref name="ttl"/></summary>
        void Set(string key, byte[] value, TimeSpan ttl);

        /// <summary>Remove a key (no error when it is missing)</summary>
        void Remove(string key);
    }

    /// <summary>
    /// Optional: atomic counters (Redis INCRBY). Without it Cache.Increment is a read + write.
    /// </summary>
    public interface ICacheCounterStore : ICacheStore
    {
        long Increment(string key, long amount);
    }

    /// <summary>
    /// <see cref="ICacheStore"/> over <see cref="IDistributedCache"/>
    /// (AddStackExchangeRedisCache, AddDistributedSqlServerCache, ...)
    /// </summary>
    public sealed class DistributedCacheStore : ICacheStore
    {
        private readonly IDistributedCache _cache;

        public DistributedCacheStore(IDistributedCache cache)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        public byte[]? Get(string key) => _cache.Get(key);

        public void Set(string key, byte[] value, TimeSpan ttl)
        {
            _cache.Set(key, value, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl });
        }

        public void Remove(string key) => _cache.Remove(key);
    }

    /// <summary>
    /// Cache on an <see cref="ICacheStore"/>: values are stored as JSON, so every server sees the same entries.
    /// Tags, prefixes and Flush work with version keys (ForgetByTag / ForgetByPrefix / Flush change one small key;
    /// old entries become misses and expire on their own). The first ForgetByPrefix of a new prefix registers it
    /// and clears the whole cache once; later calls drop only that prefix. Use fixed prefixes ("user:"), tags for
    /// per-record groups.
    /// Turn it on with <c>Cache.UseStore(store)</c> or <c>services.AddMersoCore(o =&gt; o.UseDistributedCache())</c>.
    /// </summary>
    public sealed class DistributedCache : ICache
    {
        private static readonly TimeSpan VersionTtl = TimeSpan.FromDays(365);

        // More registered prefixes than this: start a new list (and a new generation, so nothing goes stale)
        private const int MaxPrefixes = 100;

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            ReferenceHandler = ReferenceHandler.IgnoreCycles
        };

        private readonly ICacheStore _store;
        private readonly string _prefix;
        private bool _enabled = true;
        private TimeSpan _defaultTtl = TimeSpan.FromMinutes(60);

        /// <param name="store">Shared storage</param>
        /// <param name="keyPrefix">Prefix of every key (separates applications sharing one Redis)</param>
        public DistributedCache(ICacheStore store, string keyPrefix = "mc:")
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _prefix = keyPrefix ?? string.Empty;
        }

        /// <summary>
        /// Hit / miss / write counters of this server
        /// </summary>
        public CacheStats Stats { get; } = new CacheStats();

        public bool IsEnabled => _enabled;
        public void Enable() => _enabled = true;
        public void Disable() => _enabled = false;
        public void SetDefaultTtl(TimeSpan ttl) => _defaultTtl = ttl;

        public T? Get<T>(string key)
        {
            return TryGet<T>(key, out var value) ? value : default;
        }

        public T Get<T>(string key, T defaultValue)
        {
            return TryGet<T>(key, out var value) && value != null ? value : defaultValue;
        }

        public bool TryGet<T>(string key, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out T value)
        {
            value = default;
            if (!_enabled)
                return false;

            var bytes = _store.Get(_prefix + key);
            Entry? entry = null;
            if (bytes != null)
            {
                try
                {
                    entry = JsonSerializer.Deserialize<Entry>(bytes, Json);
                }
                catch (JsonException)
                {
                    entry = null;
                }
            }

            var generation = ReadGeneration();
            if (entry == null || generation == null || entry.G != generation.Token || !TagsCurrent(entry.T)
                || !PrefixesCurrent(key, generation.Prefixes, entry.P))
            {
                Stats.RecordMiss();
                return false;
            }

            try
            {
                value = entry.V.ValueKind == JsonValueKind.Undefined ? default! : entry.V.Deserialize<T>(Json)!;
            }
            catch (JsonException)
            {
                Stats.RecordMiss();
                return false;
            }

            Stats.RecordHit();
            return true;
        }

        public void Set<T>(string key, T value, TimeSpan? ttl = null, string[]? tags = null)
        {
            if (!_enabled)
                return;

            var generation = ReadGeneration() ?? WriteGeneration(new List<string>());
            var prefixes = generation.Prefixes.Where(p => key.StartsWith(p, StringComparison.Ordinal)).ToList();
            var entry = new Entry
            {
                G = generation.Token,
                T = tags == null || tags.Length == 0 ? null : tags.Distinct().ToDictionary(t => t, t => EnsureVersion("__tag:" + t)),
                P = prefixes.Count == 0 ? null : prefixes.ToDictionary(p => p, p => EnsureVersion("__pfx:" + p)),
                V = JsonSerializer.SerializeToElement(value, Json)
            };

            _store.Set(_prefix + key, JsonSerializer.SerializeToUtf8Bytes(entry, Json), ttl ?? _defaultTtl);
            Stats.RecordWrite();
        }

        public bool Has(string key)
        {
            return TryGet<JsonElement>(key, out _);
        }

        public bool Remove(string key)
        {
            _store.Remove(_prefix + key);
            return true;
        }

        public bool Forget(string key) => Remove(key);

        /// <summary>
        /// Invalidate every entry of this cache on every server
        /// </summary>
        public void Flush()
        {
            WriteGeneration(ReadGeneration()?.Prefixes ?? new List<string>());
            Stats.Reset();
        }

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

        public T Remember<T>(string key, Func<T> factory) => Remember(key, _defaultTtl, factory);

        public T? Pull<T>(string key)
        {
            var value = Get<T>(key);
            Remove(key);
            return value;
        }

        /// <summary>
        /// Increment a counter (atomic only when the store implements <see cref="ICacheCounterStore"/>)
        /// </summary>
        public long Increment(string key, long amount = 1)
        {
            if (_store is ICacheCounterStore counters)
                return counters.Increment(_prefix + key, amount);

            var next = (TryGet<long>(key, out var current) ? current : 0) + amount;
            Set(key, next);
            return next;
        }

        public long Decrement(string key, long amount = 1) => Increment(key, -amount);

        /// <summary>
        /// Invalidate every entry stored with this tag, on every server
        /// </summary>
        public void ForgetByTag(string tag)
        {
            WriteVersion("__tag:" + tag);
        }

        /// <summary>
        /// Invalidate every entry whose key starts with <paramref name="prefix"/>, on every server. A key-value
        /// store cannot list keys, so the prefix is registered: its first call clears the whole cache once, later
        /// calls only that prefix. Returns -1 (the number of entries is not known).
        /// </summary>
        public int ForgetByPrefix(string prefix)
        {
            if (string.IsNullOrEmpty(prefix))
            {
                Flush();
                return -1;
            }

            // Registered: bump its version
            var generation = ReadGeneration();
            if (generation != null && generation.Prefixes.Contains(prefix))
            {
                WriteVersion("__pfx:" + prefix);
                return -1;
            }

            // New prefix: a new generation with the prefix in its list. Entries written before (which could not
            // record this prefix) become misses. Two servers adding prefixes at once may drop one from the list,
            // but the generation still changed, and the next call for that prefix adds it again.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                var prefixes = generation?.Prefixes ?? new List<string>();
                if (prefixes.Count >= MaxPrefixes)
                    prefixes = new List<string>();
                if (!prefixes.Contains(prefix))
                    prefixes.Add(prefix);

                WriteGeneration(prefixes);
                generation = ReadGeneration();
                if (generation != null && generation.Prefixes.Contains(prefix))
                    break;
            }
            return -1;
        }

        private bool PrefixesCurrent(string key, List<string> registered, Dictionary<string, string>? recorded)
        {
            foreach (var prefix in registered)
            {
                if (!key.StartsWith(prefix, StringComparison.Ordinal))
                    continue;
                if (recorded == null || !recorded.TryGetValue(prefix, out var version) || ReadVersion("__pfx:" + prefix) != version)
                    return false;
            }
            return true;
        }

        // "__gen" = {"t": token, "p": [registered prefixes]} (1.x / early 2.0 stored the bare token)
        private Generation? ReadGeneration()
        {
            var bytes = _store.Get(_prefix + "__gen");
            if (bytes == null)
                return null;

            if (bytes.Length > 0 && bytes[0] == (byte)'{')
            {
                try
                {
                    var stored = JsonSerializer.Deserialize<Generation>(bytes, Json);
                    if (stored?.Token != null)
                    {
                        stored.Prefixes = stored.Prefixes ?? new List<string>();
                        return stored;
                    }
                }
                catch (JsonException)
                {
                }
                return null;
            }

            return new Generation { Token = Encoding.UTF8.GetString(bytes), Prefixes = new List<string>() };
        }

        private Generation WriteGeneration(List<string> prefixes)
        {
            var generation = new Generation { Token = Guid.NewGuid().ToString("N"), Prefixes = prefixes };
            _store.Set(_prefix + "__gen", JsonSerializer.SerializeToUtf8Bytes(generation, Json), VersionTtl);
            return generation;
        }

        private bool TagsCurrent(Dictionary<string, string>? tags)
        {
            if (tags == null)
                return true;

            foreach (var tag in tags)
            {
                if (ReadVersion("__tag:" + tag.Key) != tag.Value)
                    return false;
            }
            return true;
        }

        private string? ReadVersion(string name)
        {
            var bytes = _store.Get(_prefix + name);
            return bytes == null ? null : Encoding.UTF8.GetString(bytes);
        }

        private string EnsureVersion(string name)
        {
            return ReadVersion(name) ?? WriteVersion(name);
        }

        private string WriteVersion(string name)
        {
            var token = Guid.NewGuid().ToString("N");
            _store.Set(_prefix + name, Encoding.UTF8.GetBytes(token), VersionTtl);
            return token;
        }

        private sealed class Entry
        {
            public string? G { get; set; }
            public Dictionary<string, string>? T { get; set; }
            public Dictionary<string, string>? P { get; set; }
            public JsonElement V { get; set; }
        }

        private sealed class Generation
        {
            [JsonPropertyName("t")]
            public string? Token { get; set; }

            [JsonPropertyName("p")]
            public List<string> Prefixes { get; set; } = new List<string>();
        }
    }
}
