using System;
using System.Collections.Generic;
using System.Text.Json;
using mersolutionCore.ORM.Entity;

namespace mersolutionCore.ORM
{
    /// <summary>
    /// JSON Column attribute - Property'yi JSON olarak sakla
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class JsonColumnAttribute : Attribute
    {
    }

    /// <summary>
    /// JSON Column helper methods
    /// </summary>
    public static class JsonColumnHelper
    {
        private static readonly JsonSerializerOptions _options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        /// <summary>
        /// Object'i JSON string'e çevir
        /// </summary>
        public static string Serialize<T>(T obj)
        {
            if (obj == null) return null;
            return JsonSerializer.Serialize(obj, _options);
        }

        /// <summary>
        /// JSON string'i object'e çevir
        /// </summary>
        public static T Deserialize<T>(string json)
        {
            if (string.IsNullOrEmpty(json)) return default;
            return JsonSerializer.Deserialize<T>(json, _options);
        }

        /// <summary>
        /// JSON string'i dynamic object'e çevir
        /// </summary>
        public static object Deserialize(string json, Type type)
        {
            if (string.IsNullOrEmpty(json)) return null;
            return JsonSerializer.Deserialize(json, type, _options);
        }
    }

    /// <summary>
    /// JSON wrapper for complex types
    /// </summary>
    public class JsonValue<T>
    {
        private T _value;
        private string _json;

        public T Value
        {
            get => _value;
            set
            {
                _value = value;
                _json = JsonColumnHelper.Serialize(value);
            }
        }

        public string Json
        {
            get => _json;
            set
            {
                _json = value;
                _value = JsonColumnHelper.Deserialize<T>(value);
            }
        }

        public JsonValue() { }

        public JsonValue(T value)
        {
            Value = value;
        }

        public static implicit operator T(JsonValue<T> jsonValue) => jsonValue.Value;
        public static implicit operator JsonValue<T>(T value) => new JsonValue<T>(value);

        public override string ToString() => _json;
    }

    /// <summary>
    /// JSON Dictionary wrapper
    /// </summary>
    public class JsonDictionary : JsonValue<Dictionary<string, object>>
    {
        public JsonDictionary() : base(new Dictionary<string, object>()) { }
        public JsonDictionary(Dictionary<string, object> value) : base(value) { }

        public object this[string key]
        {
            get => Value.ContainsKey(key) ? Value[key] : null;
            set
            {
                Value[key] = value;
                Json = JsonColumnHelper.Serialize(Value);
            }
        }

        public bool ContainsKey(string key) => Value.ContainsKey(key);
        public void Remove(string key)
        {
            Value.Remove(key);
            Json = JsonColumnHelper.Serialize(Value);
        }
    }

    /// <summary>
    /// JSON List wrapper
    /// </summary>
    public class JsonList<T> : JsonValue<List<T>>
    {
        public JsonList() : base(new List<T>()) { }
        public JsonList(List<T> value) : base(value) { }

        public void Add(T item)
        {
            Value.Add(item);
            Json = JsonColumnHelper.Serialize(Value);
        }

        public void Remove(T item)
        {
            Value.Remove(item);
            Json = JsonColumnHelper.Serialize(Value);
        }

        public T this[int index]
        {
            get => Value[index];
            set
            {
                Value[index] = value;
                Json = JsonColumnHelper.Serialize(Value);
            }
        }

        public int Count => Value.Count;
    }

    /// <summary>
    /// JSON Array wrapper
    /// </summary>
    public class JsonArray : JsonList<object>
    {
        public JsonArray() : base() { }
        public JsonArray(List<object> value) : base(value) { }
    }
}
