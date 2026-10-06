using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace mersolutionCore.ORM
{
    /// <summary>
    /// GlobalScope attribute - Otomatik filtre tanımla
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class GlobalScopeAttribute : Attribute
    {
        public string Column { get; }
        public object Value { get; }
        public string Operator { get; }

        public GlobalScopeAttribute(string column, object value, string op = "=")
        {
            Column = column;
            Value = value;
            Operator = op;
        }
    }

    /// <summary>
    /// GlobalScope yöneticisi. [GlobalScope] filtreleri modelin tüm sorgularına (All, Find, Count, Query()...) eklenir;
    /// kaydetme / silme gibi tek kayıt işlemlerine eklenmez.
    /// </summary>
    public static class GlobalScopeManager
    {
        private static readonly ConcurrentDictionary<Type, List<GlobalScopeInfo>> _scopes = new ConcurrentDictionary<Type, List<GlobalScopeInfo>>();
        private static readonly ConcurrentDictionary<Type, byte> _disabledScopes = new ConcurrentDictionary<Type, byte>();

        // WithoutGlobalScopes<T>() only affects the current flow (request / async chain), never other threads
        private static readonly System.Threading.AsyncLocal<Type[]?> _flowDisabled = new System.Threading.AsyncLocal<Type[]?>();

        /// <summary>
        /// Model için global scope'ları al (kopya liste)
        /// </summary>
        public static List<GlobalScopeInfo> GetScopes<T>()
        {
            var type = typeof(T);

            if (_disabledScopes.ContainsKey(type) || (_flowDisabled.Value?.Contains(type) ?? false))
                return new List<GlobalScopeInfo>();

            var scopes = _scopes.GetOrAdd(type, t => t.GetCustomAttributes(typeof(GlobalScopeAttribute), true)
                .Cast<GlobalScopeAttribute>()
                .Select(attr => new GlobalScopeInfo { Column = attr.Column, Value = attr.Value, Operator = attr.Operator })
                .ToList());

            return new List<GlobalScopeInfo>(scopes);
        }

        /// <summary>
        /// Global scope'ları bu kod bloğu için devre dışı bırak (yalnızca mevcut akış — diğer istekler etkilenmez)
        /// </summary>
        public static IDisposable WithoutGlobalScopes<T>()
        {
            var previous = _flowDisabled.Value;
            var list = previous == null ? new List<Type>() : new List<Type>(previous);
            list.Add(typeof(T));
            _flowDisabled.Value = list.ToArray();
            return new FlowRestore(previous);
        }

        private sealed class FlowRestore : IDisposable
        {
            private readonly Type[]? _previous;
            private bool _disposed;

            public FlowRestore(Type[]? previous)
            {
                _previous = previous;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _flowDisabled.Value = _previous;
                _disposed = true;
            }
        }

        /// <summary>
        /// Belirli bir model için scope'ları devre dışı bırak
        /// </summary>
        public static void DisableScopes<T>()
        {
            _disabledScopes[typeof(T)] = 0;
        }

        /// <summary>
        /// Belirli bir model için scope'ları etkinleştir
        /// </summary>
        public static void EnableScopes<T>()
        {
            _disabledScopes.TryRemove(typeof(T), out _);
        }
    }

    public class GlobalScopeInfo
    {
        public string Column { get; set; } = string.Empty;
        public object? Value { get; set; }
        public string Operator { get; set; } = "=";
    }
}
