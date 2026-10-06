using System;
using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace mersolutionCore.ORM
{
    /// <summary>
    /// Connection Pool - Veritabanı bağlantı havuzu yönetimi
    /// </summary>
    /// <remarks>
    /// Every connection string (and connection type) gets its own pool, so two databases never
    /// hand out each other's connections. ADO.NET providers already pool physical connections;
    /// this helper only keeps opened <see cref="DbConnection"/> objects ready for reuse.
    /// </remarks>
    public static class ConnectionPool
    {
        private sealed class Bucket
        {
            public readonly ConcurrentQueue<DbConnection> Idle = new ConcurrentQueue<DbConnection>();
            public int Total;          // created and not yet destroyed (idle + rented)
            public int Generation;     // bumped by Clear(); older rented connections are destroyed on return
            public bool Initialized;
        }

        private sealed class Entry
        {
            public Bucket? Bucket;
            public int Generation;
            public int InPool;         // 1 while the connection sits in the idle queue
        }

        private static readonly ConcurrentDictionary<string, Bucket> _buckets = new ConcurrentDictionary<string, Bucket>(StringComparer.Ordinal);
        private static readonly ConditionalWeakTable<DbConnection, Entry> _entries = new ConditionalWeakTable<DbConnection, Entry>();
        private static readonly object _lock = new object();

        private static volatile int _minPoolSize = 5;
        private static volatile int _maxPoolSize = 100;
        private static TimeSpan _connectionTimeout = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Pool ayarlarını yapılandır
        /// </summary>
        public static void Configure(int minSize = 5, int maxSize = 100, int timeoutSeconds = 30)
        {
            if (minSize < 0) throw new ArgumentOutOfRangeException(nameof(minSize));
            if (maxSize < 1 || maxSize < minSize) throw new ArgumentOutOfRangeException(nameof(maxSize));
            if (timeoutSeconds < 0) throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));

            lock (_lock)
            {
                _minPoolSize = minSize;
                _maxPoolSize = maxSize;
                _connectionTimeout = TimeSpan.FromSeconds(timeoutSeconds);
            }
        }

        /// <summary>
        /// Pool'u başlat (MinPoolSize kadar bağlantı hazırlar; isteğe bağlı, ilk kullanımda havuz zaten oluşur)
        /// </summary>
        public static void Initialize(Func<DbConnection> connectionFactory)
        {
            if (connectionFactory == null) throw new ArgumentNullException(nameof(connectionFactory));

            var first = connectionFactory();
            var bucket = GetBucket(first);

            lock (_lock)
            {
                if (bucket.Initialized)
                {
                    first.Dispose();
                    return;
                }

                var next = first;
                while (bucket.Total < _minPoolSize)
                {
                    var conn = next ?? connectionFactory();
                    next = null;
                    Interlocked.Increment(ref bucket.Total);
                    Track(conn, bucket);
                    Enqueue(conn, bucket);
                }
                next?.Dispose();

                bucket.Initialized = true;
            }
        }

        /// <summary>
        /// Pool'dan bağlantı al (açık döner; işin bitince <see cref="ReturnConnection"/> ile geri ver)
        /// </summary>
        public static DbConnection GetConnection(Func<DbConnection> connectionFactory)
        {
            if (connectionFactory == null) throw new ArgumentNullException(nameof(connectionFactory));

            // A new (not yet opened) object is cheap; it identifies the pool and becomes the
            // new connection when no idle one is available.
            var fresh = connectionFactory();
            var bucket = GetBucket(fresh);
            var timer = Stopwatch.StartNew();

            while (true)
            {
                var idle = TakeIdle(bucket);
                if (idle != null)
                {
                    fresh?.Dispose();
                    return idle;
                }

                if (TryReserve(bucket))
                {
                    var conn = fresh ?? connectionFactory();
                    fresh = null;
                    try
                    {
                        if (conn.State != ConnectionState.Open)
                            conn.Open();
                    }
                    catch
                    {
                        conn.Dispose();
                        Interlocked.Decrement(ref bucket.Total);
                        throw;
                    }

                    Track(conn, bucket);
                    return conn;
                }

                if (timer.Elapsed >= _connectionTimeout)
                {
                    fresh?.Dispose();
                    throw new TimeoutException("Connection pool timeout - tüm bağlantılar kullanımda.");
                }

                Thread.Sleep(10);
            }
        }

        /// <summary>
        /// Bağlantıyı pool'a geri ver
        /// </summary>
        public static void ReturnConnection(DbConnection connection)
        {
            if (connection == null) return;

            if (!_entries.TryGetValue(connection, out var entry))
            {
                // Not created by the pool: nothing to reuse, just release it
                connection.Dispose();
                return;
            }

            if (Interlocked.Exchange(ref entry.InPool, 1) == 1)
                return; // returned twice - it is already idle

            var bucket = entry.Bucket;
            if (entry.Generation != Volatile.Read(ref bucket!.Generation) || connection.State != ConnectionState.Open)
            {
                Destroy(connection, bucket);
                return;
            }

            bucket.Idle.Enqueue(connection);
        }

        /// <summary>
        /// Pool'u temizle (boştaki bağlantılar kapanır, kullanımdakiler geri verildiğinde kapanır)
        /// </summary>
        public static void Clear()
        {
            lock (_lock)
            {
                foreach (var bucket in _buckets.Values)
                {
                    Interlocked.Increment(ref bucket.Generation);
                    bucket.Initialized = false;

                    while (bucket.Idle.TryDequeue(out var conn))
                        Destroy(conn, bucket);
                }
            }
        }

        /// <summary>
        /// Pool durumu (tüm bağlantı dizelerinin toplamı)
        /// </summary>
        public static PoolStatus GetStatus()
        {
            var buckets = _buckets.Values.ToList();

            return new PoolStatus
            {
                AvailableConnections = buckets.Sum(b => b.Idle.Count),
                TotalConnections = buckets.Sum(b => Volatile.Read(ref b.Total)),
                MinPoolSize = _minPoolSize,
                MaxPoolSize = _maxPoolSize,
                IsInitialized = buckets.Any(b => b.Initialized)
            };
        }

        private static Bucket GetBucket(DbConnection probe)
        {
            if (probe == null)
                throw new InvalidOperationException("Connection factory returned null.");

            var key = probe.GetType().FullName + "\n" + probe.ConnectionString;
            return _buckets.GetOrAdd(key, _ => new Bucket());
        }

        private static DbConnection? TakeIdle(Bucket bucket)
        {
            while (bucket.Idle.TryDequeue(out var conn))
            {
                if (_entries.TryGetValue(conn, out var entry))
                    Interlocked.Exchange(ref entry.InPool, 0);

                if (conn.State == ConnectionState.Open)
                    return conn;

                if (conn.State == ConnectionState.Closed)
                {
                    try
                    {
                        conn.Open();
                        return conn;
                    }
                    catch
                    {
                        // Broken or unreachable: drop it and try the next one
                    }
                }

                Destroy(conn, bucket);
            }

            return null;
        }

        private static bool TryReserve(Bucket bucket)
        {
            while (true)
            {
                var current = Volatile.Read(ref bucket.Total);
                if (current >= _maxPoolSize)
                    return false;

                if (Interlocked.CompareExchange(ref bucket.Total, current + 1, current) == current)
                    return true;
            }
        }

        private static void Track(DbConnection conn, Bucket bucket)
        {
            _entries.Remove(conn);
            _entries.Add(conn, new Entry { Bucket = bucket, Generation = Volatile.Read(ref bucket.Generation) });
        }

        private static void Enqueue(DbConnection conn, Bucket bucket)
        {
            if (_entries.TryGetValue(conn, out var entry))
                Interlocked.Exchange(ref entry.InPool, 1);
            bucket.Idle.Enqueue(conn);
        }

        private static void Destroy(DbConnection conn, Bucket bucket)
        {
            try
            {
                conn.Close();
                conn.Dispose();
            }
            catch { }

            _entries.Remove(conn);
            Interlocked.Decrement(ref bucket.Total);
        }
    }

    /// <summary>
    /// Pool durumu
    /// </summary>
    public class PoolStatus
    {
        public int AvailableConnections { get; set; }
        public int TotalConnections { get; set; }
        public int MinPoolSize { get; set; }
        public int MaxPoolSize { get; set; }
        public bool IsInitialized { get; set; }

        public override string ToString()
        {
            return $"Pool: {AvailableConnections}/{TotalConnections} (Min: {MinPoolSize}, Max: {MaxPoolSize})";
        }
    }

    /// <summary>
    /// Pooled connection wrapper - using ile otomatik geri verme
    /// </summary>
    public class PooledConnection : IDisposable
    {
        public DbConnection Connection { get; }
        private bool _disposed;

        public PooledConnection(DbConnection connection)
        {
            Connection = connection;
        }

        public void Dispose()
        {
            if (_disposed) return;
            ConnectionPool.ReturnConnection(Connection);
            _disposed = true;
        }
    }
}
