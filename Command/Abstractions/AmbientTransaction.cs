using System.Data.Common;
using System.Threading;

namespace mersolutionCore.Command.Abstractions
{
    /// <summary>
    /// Connection + transaction shared by every command created inside <c>MersoTransaction.Run</c>.
    /// Flows with the current (async) execution context. Transactions on different databases nest:
    /// each command finds the one for its own database through <see cref="Outer"/>.
    /// </summary>
    internal sealed class AmbientTransaction
    {
        private static readonly AsyncLocal<AmbientTransaction?> _current = new AsyncLocal<AmbientTransaction?>();

        internal static AmbientTransaction? Current
        {
            get => _current.Value;
            set => _current.Value = value;
        }

        internal DbCommandBase Owner { get; }
        internal DbConnection Connection { get; }
        internal DbTransaction Transaction { get; }
        internal DbProviderType ProviderType { get; }
        internal string? ConnectionString { get; }

        /// <summary>
        /// Transaction that was current when this one started (another database, or null)
        /// </summary>
        internal AmbientTransaction? Outer { get; }

        internal AmbientTransaction(DbCommandBase owner, DbConnection connection, DbTransaction transaction, string? connectionString, AmbientTransaction? outer)
        {
            Owner = owner;
            Connection = connection;
            Transaction = transaction;
            ProviderType = owner.ProviderType;
            ConnectionString = connectionString;
            Outer = outer;
        }

        /// <summary>
        /// The active transaction of the current flow that <paramref name="command"/> can join, or null
        /// </summary>
        internal static AmbientTransaction? Find(DbCommandBase command)
        {
            for (var t = Current; t != null; t = t.Outer)
            {
                if (t.Matches(command))
                    return t;
            }
            return null;
        }

        /// <summary>
        /// A command joins the ambient transaction when it talks to the same database.
        /// </summary>
        internal bool Matches(DbCommandBase command)
        {
            if (command == null || ReferenceEquals(command, Owner) || command.ProviderType != ProviderType)
                return false;

            var other = command.ConnectionStringForMatching;
            return ConnectionString == null || other == null || ConnectionString == other;
        }
    }
}
