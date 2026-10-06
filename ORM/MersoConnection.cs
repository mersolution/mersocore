using System;
using System.Threading;
using mersolutionCore.Command.Abstractions;

namespace mersolutionCore.ORM
{
    /// <summary>
    /// Switch the default connection for the current flow (thread / async call chain), e.g. per tenant:
    /// <code>
    /// using (MersoConnection.Use("tenant_42"))
    /// {
    ///     var orders = Order.All();   // runs on tenant_42
    /// }
    /// </code>
    /// Models with <c>[Connection("name")]</c> and queries with <c>On("name")</c> keep their own connection.
    /// </summary>
    public static class MersoConnection
    {
        private static readonly AsyncLocal<Func<DbCommandBase>?> _current = new AsyncLocal<Func<DbCommandBase>?>();

        internal static Func<DbCommandBase>? CurrentFactory => _current.Value;

        /// <summary>
        /// Use a named connection (registered with <c>ModelBase.Configure(name, ...)</c> or <c>DbConfig.AddConnection</c>)
        /// until the returned scope is disposed
        /// </summary>
        public static IDisposable Use(string connectionName)
        {
            if (string.IsNullOrEmpty(connectionName))
                throw new ArgumentNullException(nameof(connectionName));

            return Use(ModelBase.ResolveFactory(connectionName));
        }

        /// <summary>
        /// Use a connection factory (e.g. a tenant database built at runtime) until the returned scope is disposed
        /// </summary>
        public static IDisposable Use(Func<DbCommandBase> connectionFactory)
        {
            if (connectionFactory == null)
                throw new ArgumentNullException(nameof(connectionFactory));

            var previous = _current.Value;
            _current.Value = connectionFactory;
            return new Scope(previous);
        }

        private sealed class Scope : IDisposable
        {
            private readonly Func<DbCommandBase>? _previous;
            private bool _disposed;

            public Scope(Func<DbCommandBase>? previous)
            {
                _previous = previous;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _current.Value = _previous;
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Thrown when a model with a <c>[RowVersion]</c> column was changed or deleted by someone else
    /// between loading and saving (optimistic concurrency)
    /// </summary>
    public class DbConcurrencyException : Exception
    {
        public DbConcurrencyException(string message) : base(message)
        {
        }
    }
}
