using System;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Threading;

namespace mersolutionCore.ORM
{
    /// <summary>
    /// Connection Pool - Veritabanı bağlantı havuzu yönetimi
    /// </summary>
    public static class ConnectionPool
    {
        private static readonly ConcurrentQueue<DbConnection> _pool = new ConcurrentQueue<DbConnection>();
        private static readonly object _lock = new object();
        
        private static int _minPoolSize = 5;
        private static int _maxPoolSize = 100;
        private static int _currentSize = 0;
        private static TimeSpan _connectionTimeout = TimeSpan.FromSeconds(30);
        private static bool _initialized = false;

        /// <summary>
        /// Pool ayarlarını yapılandır
        /// </summary>
        public static void Configure(int minSize = 5, int maxSize = 100, int timeoutSeconds = 30)
        {
            _minPoolSize = minSize;
            _maxPoolSize = maxSize;
            _connectionTimeout = TimeSpan.FromSeconds(timeoutSeconds);
        }

        /// <summary>
        /// Pool'u başlat
        /// </summary>
        public static void Initialize(Func<DbConnection> connectionFactory)
        {
            if (_initialized) return;

            lock (_lock)
            {
                if (_initialized) return;

                for (int i = 0; i < _minPoolSize; i++)
                {
                    var conn = connectionFactory();
                    _pool.Enqueue(conn);
                    Interlocked.Increment(ref _currentSize);
                }

                _initialized = true;
            }
        }

        /// <summary>
        /// Pool'dan bağlantı al
        /// </summary>
        public static DbConnection GetConnection(Func<DbConnection> connectionFactory)
        {
            DbConnection connection;

            if (_pool.TryDequeue(out connection))
            {
                if (connection.State == System.Data.ConnectionState.Closed)
                {
                    try
                    {
                        connection.Open();
                    }
                    catch
                    {
                        // Bağlantı bozuksa yeni oluştur
                        connection.Dispose();
                        Interlocked.Decrement(ref _currentSize);
                        return CreateNewConnection(connectionFactory);
                    }
                }
                return connection;
            }

            // Pool boşsa yeni bağlantı oluştur
            return CreateNewConnection(connectionFactory);
        }

        /// <summary>
        /// Bağlantıyı pool'a geri ver
        /// </summary>
        public static void ReturnConnection(DbConnection connection)
        {
            if (connection == null) return;

            if (_currentSize < _maxPoolSize && connection.State == System.Data.ConnectionState.Open)
            {
                _pool.Enqueue(connection);
            }
            else
            {
                connection.Close();
                connection.Dispose();
                Interlocked.Decrement(ref _currentSize);
            }
        }

        /// <summary>
        /// Pool'u temizle
        /// </summary>
        public static void Clear()
        {
            lock (_lock)
            {
                while (_pool.TryDequeue(out var conn))
                {
                    try
                    {
                        conn.Close();
                        conn.Dispose();
                    }
                    catch { }
                }
                _currentSize = 0;
                _initialized = false;
            }
        }

        /// <summary>
        /// Pool durumu
        /// </summary>
        public static PoolStatus GetStatus()
        {
            return new PoolStatus
            {
                AvailableConnections = _pool.Count,
                TotalConnections = _currentSize,
                MinPoolSize = _minPoolSize,
                MaxPoolSize = _maxPoolSize,
                IsInitialized = _initialized
            };
        }

        private static DbConnection CreateNewConnection(Func<DbConnection> connectionFactory)
        {
            if (_currentSize >= _maxPoolSize)
            {
                // Max'a ulaşıldı, bekle
                var startTime = DateTime.UtcNow;
                while (DateTime.UtcNow - startTime < _connectionTimeout)
                {
                    if (_pool.TryDequeue(out var conn))
                    {
                        if (conn.State == System.Data.ConnectionState.Closed)
                            conn.Open();
                        return conn;
                    }
                    Thread.Sleep(10);
                }
                throw new TimeoutException("Connection pool timeout - tüm bağlantılar kullanımda.");
            }

            var connection = connectionFactory();
            connection.Open();
            Interlocked.Increment(ref _currentSize);
            return connection;
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
