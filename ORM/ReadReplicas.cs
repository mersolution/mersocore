using System;
using System.Collections.Concurrent;
using System.Threading;
using mersolutionCore.Command.Abstractions;

namespace mersolutionCore.ORM
{
    /// <summary>
    /// Read replicas: SELECTs of models, query builders, relations and eager loading go to the replicas of their
    /// connection (round robin). Writes, everything inside a MersoTransaction, locking reads (LockForUpdate),
    /// Refresh / Fresh, FirstOrCreate / UpdateOrCreate and queries with <c>OnPrimary()</c> use the primary.
    /// Inside <see cref="BeginScope"/> (e.g. one web request) reads after a write also use the primary, so a
    /// just-saved row is read back even when replication lags. RawQuery always uses the primary.
    /// </summary>
    public static class ReadReplicas
    {
        private static readonly ConcurrentDictionary<string, Func<DbCommandBase>[]> _replicas =
            new ConcurrentDictionary<string, Func<DbCommandBase>[]>(StringComparer.OrdinalIgnoreCase);

        private static readonly AsyncLocal<ScopeState?> _scope = new AsyncLocal<ScopeState?>();
        private static int _next;

        /// <summary>
        /// Add a replica of a connection (null = the default connection)
        /// </summary>
        public static void Add(Func<DbCommandBase> replica, string? connectionName = null)
        {
            if (replica == null) throw new ArgumentNullException(nameof(replica));

            _replicas.AddOrUpdate(Key(connectionName), new[] { replica }, (_, existing) =>
            {
                var list = new Func<DbCommandBase>[existing.Length + 1];
                existing.CopyTo(list, 0);
                list[existing.Length] = replica;
                return list;
            });
        }

        /// <summary>
        /// Remove the replicas of a connection (null = the default connection)
        /// </summary>
        public static void Clear(string? connectionName = null)
        {
            _replicas.TryRemove(Key(connectionName), out _);
        }

        /// <summary>
        /// True when the connection has replicas
        /// </summary>
        public static bool Has(string? connectionName = null) => _replicas.ContainsKey(Key(connectionName));

        /// <summary>
        /// A unit of work (one web request): after its first write, its reads use the primary.
        /// <c>app.Use(async (ctx, next) =&gt; { using (ReadReplicas.BeginScope()) await next(); });</c>
        /// </summary>
        public static IDisposable BeginScope()
        {
            var previous = _scope.Value;
            _scope.Value = new ScopeState();
            return new Scope(previous);
        }

        /// <summary>
        /// Called for every write: the current scope (if any) reads from the primary from now on
        /// </summary>
        internal static void MarkWrite()
        {
            var scope = _scope.Value;
            if (scope != null)
                scope.Wrote = true;
        }

        /// <summary>
        /// Factory of the next replica for a read on <paramref name="connectionName"/>, or null for the primary
        /// </summary>
        internal static Func<DbCommandBase>? ForRead(string? connectionName)
        {
            if (_replicas.IsEmpty || AmbientTransaction.Current != null)
                return null;

            // A scoped connection (tenant) is used as it is
            if (string.IsNullOrEmpty(connectionName) && MersoConnection.CurrentFactory != null)
                return null;

            if (_scope.Value?.Wrote == true)
                return null;

            if (!_replicas.TryGetValue(Key(connectionName), out var list) || list.Length == 0)
                return null;

            var index = (int)((uint)Interlocked.Increment(ref _next) % (uint)list.Length);
            return list[index];
        }

        private static string Key(string? connectionName) => connectionName ?? string.Empty;

        private sealed class ScopeState
        {
            public bool Wrote;
        }

        private sealed class Scope : IDisposable
        {
            private readonly ScopeState? _previous;
            private bool _disposed;

            public Scope(ScopeState? previous)
            {
                _previous = previous;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _scope.Value = _previous;
                _disposed = true;
            }
        }
    }
}
