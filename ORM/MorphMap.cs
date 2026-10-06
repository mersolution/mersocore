using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using mersolutionCore.ORM.Entity;

namespace mersolutionCore.ORM
{
    /// <summary>
    /// Names written to the {Name}Type column of polymorphic relations (MorphMany / MorphOne / MorphTo).
    /// Default: the class name or <see cref="MorphNameAttribute"/>. Register short, stable names when class
    /// names may change or two classes share a name: <c>MorphMap.Register&lt;Post&gt;("post")</c>.
    /// </summary>
    public static class MorphMap
    {
        private static readonly ConcurrentDictionary<string, Type> _types = new ConcurrentDictionary<string, Type>(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<Type, string> _names = new ConcurrentDictionary<Type, string>();

        /// <summary>
        /// Use <paramref name="name"/> for <typeparamref name="TModel"/> in polymorphic type columns
        /// </summary>
        public static void Register<TModel>(string name) where TModel : ModelBase
        {
            Register(typeof(TModel), name);
        }

        /// <summary>
        /// Use <paramref name="name"/> for <paramref name="modelType"/> in polymorphic type columns
        /// </summary>
        public static void Register(Type modelType, string name)
        {
            if (modelType == null) throw new ArgumentNullException(nameof(modelType));
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
            if (!typeof(ModelBase).IsAssignableFrom(modelType))
                throw new ArgumentException($"{modelType.Name} is not a model.", nameof(modelType));

            if (_types.TryGetValue(name, out var existing) && existing != modelType)
                throw new InvalidOperationException($"Morph name '{name}' is already used by {existing.FullName}.");

            if (_names.TryGetValue(modelType, out var previous))
                _types.TryRemove(previous, out _);

            _names[modelType] = name;
            _types[name] = modelType;
        }

        /// <summary>
        /// Name stored for a model type
        /// </summary>
        public static string NameOf(Type modelType)
        {
            if (modelType == null) throw new ArgumentNullException(nameof(modelType));
            return _names.TryGetValue(modelType, out var name) ? name : ModelMetadata.GetMetadata(modelType).MorphName;
        }

        /// <summary>
        /// Model type for a stored name (registered names first, then models found in the loaded assemblies)
        /// </summary>
        public static Type TypeOf(string name)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (_types.TryGetValue(name, out var type))
                return type;

            var matches = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic)
                .SelectMany(LoadableTypes)
                .Where(t => typeof(ModelBase).IsAssignableFrom(t) && !t.IsAbstract && !t.ContainsGenericParameters
                            && t.GetConstructor(Type.EmptyTypes) != null && NameOf(t) == name)
                .Distinct()
                .ToList();

            if (matches.Count == 0)
                throw new InvalidOperationException($"No model is named '{name}' for polymorphic relations. Use MorphMap.Register<TModel>(\"{name}\").");
            if (matches.Count > 1)
                throw new InvalidOperationException($"Several models are named '{name}' ({string.Join(", ", matches.Select(m => m.FullName))}). Use MorphMap.Register.");

            _types.TryAdd(name, matches[0]);
            return matches[0];
        }

        private static Type[] LoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(t => t != null).Select(t => t!).ToArray();
            }
            catch
            {
                return new Type[0];
            }
        }
    }
}
