using System;
using System.Collections.Generic;

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
    /// GlobalScope yöneticisi
    /// </summary>
    public static class GlobalScopeManager
    {
        private static readonly Dictionary<Type, List<GlobalScopeInfo>> _scopes = new Dictionary<Type, List<GlobalScopeInfo>>();
        private static readonly HashSet<Type> _disabledScopes = new HashSet<Type>();

        /// <summary>
        /// Model için global scope'ları al
        /// </summary>
        public static List<GlobalScopeInfo> GetScopes<T>()
        {
            var type = typeof(T);

            if (_disabledScopes.Contains(type))
                return new List<GlobalScopeInfo>();

            if (!_scopes.ContainsKey(type))
            {
                var scopes = new List<GlobalScopeInfo>();
                var attributes = type.GetCustomAttributes(typeof(GlobalScopeAttribute), true);

                foreach (GlobalScopeAttribute attr in attributes)
                {
                    scopes.Add(new GlobalScopeInfo
                    {
                        Column = attr.Column,
                        Value = attr.Value,
                        Operator = attr.Operator
                    });
                }

                _scopes[type] = scopes;
            }

            return _scopes[type];
        }

        /// <summary>
        /// Global scope'ları geçici olarak devre dışı bırak
        /// </summary>
        public static IDisposable WithoutGlobalScopes<T>()
        {
            return new ScopeDisabler<T>();
        }

        /// <summary>
        /// Belirli bir model için scope'ları devre dışı bırak
        /// </summary>
        public static void DisableScopes<T>()
        {
            _disabledScopes.Add(typeof(T));
        }

        /// <summary>
        /// Belirli bir model için scope'ları etkinleştir
        /// </summary>
        public static void EnableScopes<T>()
        {
            _disabledScopes.Remove(typeof(T));
        }

        private class ScopeDisabler<T> : IDisposable
        {
            public ScopeDisabler()
            {
                DisableScopes<T>();
            }

            public void Dispose()
            {
                EnableScopes<T>();
            }
        }
    }

    public class GlobalScopeInfo
    {
        public string Column { get; set; }
        public object Value { get; set; }
        public string Operator { get; set; }
    }
}
