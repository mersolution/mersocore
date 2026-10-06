using System;
using System.Data;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace mersolutionCore.ORM
{
    /// <summary>
    /// Converts database values to model property types and maps rows to models.
    /// Handles what Convert.ChangeType cannot: enums, Guid (text / 16-byte blob), DateTimeOffset,
    /// TimeSpan, SQLite text dates and invariant-culture numbers.
    /// </summary>
    internal static class ModelMapper
    {
        /// <summary>
        /// Convert a database value to <paramref name="targetType"/> (null / DBNull → null)
        /// </summary>
        internal static object? ConvertTo(object? value, Type targetType)
        {
            if (value == null || value == DBNull.Value)
                return null;

            var type = Nullable.GetUnderlyingType(targetType) ?? targetType;

            if (type.IsInstanceOfType(value))
                return value;

            if (type.IsEnum)
            {
                if (value is string s)
                {
                    return long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                        ? Enum.ToObject(type, n)
                        : Enum.Parse(type, s, true);
                }
                return Enum.ToObject(type, Convert.ChangeType(value, Enum.GetUnderlyingType(type), CultureInfo.InvariantCulture));
            }

            if (type == typeof(Guid))
            {
                if (value is byte[] bytes && bytes.Length == 16)
                    return new Guid(bytes);
                return Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
            }

            if (type == typeof(DateTimeOffset))
            {
                if (value is DateTime dt)
                    return new DateTimeOffset(dt.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : dt);
                return DateTimeOffset.Parse(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, CultureInfo.InvariantCulture);
            }

            if (type == typeof(DateTime))
            {
                if (value is DateTimeOffset dto)
                    return dto.UtcDateTime;
                if (value is string ds)
                    return DateTime.Parse(ds, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces);
            }

            if (type == typeof(TimeSpan))
            {
                if (value is DateTime t)
                    return t.TimeOfDay;
                if (value is string ts)
                    return TimeSpan.Parse(ts, CultureInfo.InvariantCulture);
                return TimeSpan.FromTicks(Convert.ToInt64(value, CultureInfo.InvariantCulture));
            }

            if (DateTypes.IsDateOnly(type))
                return DateTypes.ToDateOnly(type, value);

            if (DateTypes.IsTimeOnly(type))
                return DateTypes.ToTimeOnly(type, value);

            if (type == typeof(bool) && value is string bs)
            {
                bs = bs.Trim();
                return bs == "1" || bs.Equals("true", StringComparison.OrdinalIgnoreCase) || bs.Equals("yes", StringComparison.OrdinalIgnoreCase);
            }

            if (type == typeof(byte[]))
            {
                if (value is Guid g)
                    return g.ToByteArray();
                return value as byte[];
            }

            if (type == typeof(string))
            {
                if (value is byte[] raw && raw.Length == 16)
                    return new Guid(raw).ToString();
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            }

            return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Set a property from a database value; returns false when the value cannot be converted
        /// </summary>
        internal static bool TrySetProperty(PropertyInfo? property, object target, object? value)
        {
            if (property == null || !property.CanWrite)
                return false;

            try
            {
                var converted = ConvertTo(value, property.PropertyType);
                if (converted == null && property.PropertyType.IsValueType && Nullable.GetUnderlyingType(property.PropertyType) == null)
                    return false; // keep default(T) for non-nullable value types

                property.SetValue(target, converted);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Column → property positions of one result set, built once per query and used for every row.
        /// Models are filled straight from the data reader (no DataTable copy).
        /// </summary>
        internal sealed class ReaderMap
        {
            private readonly ModelMetadata _metadata;
            private readonly string[] _names;
            private readonly int[] _propertyOrdinals;
            private readonly int[] _computedOrdinals;
            private readonly int[] _extraOrdinals;

            internal ReaderMap(IDataRecord record, ModelMetadata metadata)
            {
                _metadata = metadata;

                // Duplicate names (joins without aliases) get 1, 2 ... as the DataTable path did
                _names = new string[record.FieldCount];
                var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < _names.Length; i++)
                {
                    var name = record.GetName(i);
                    var unique = name;
                    for (int n = 1; !seen.Add(unique); n++)
                        unique = name + n.ToString(CultureInfo.InvariantCulture);
                    _names[i] = unique;
                }

                _propertyOrdinals = new int[metadata.Properties.Count];
                var used = new bool[_names.Length];
                for (int i = 0; i < _propertyOrdinals.Length; i++)
                {
                    var ordinal = Ordinal(metadata.Properties[i].ColumnName);
                    _propertyOrdinals[i] = ordinal;
                    if (ordinal >= 0)
                        used[ordinal] = true;
                }

                _computedOrdinals = new int[metadata.ComputedProperties.Count];
                for (int i = 0; i < _computedOrdinals.Length; i++)
                {
                    var ordinal = Ordinal(metadata.ComputedProperties[i].ColumnName);
                    _computedOrdinals[i] = ordinal;
                    if (ordinal >= 0)
                        used[ordinal] = true;
                }

                var extra = new System.Collections.Generic.List<int>();
                for (int i = 0; i < _names.Length; i++)
                {
                    if (!used[i])
                        extra.Add(i);
                }
                _extraOrdinals = extra.ToArray();
            }

            /// <summary>
            /// Column position by name (exact first, then ignoring case without the culture), -1 when missing
            /// </summary>
            internal int Ordinal(string name)
            {
                int fallback = -1;
                for (int i = 0; i < _names.Length; i++)
                {
                    if (string.Equals(_names[i], name, StringComparison.Ordinal))
                        return i;
                    if (fallback < 0 && string.Equals(_names[i], name, StringComparison.OrdinalIgnoreCase))
                        fallback = i;
                }
                return fallback;
            }

            /// <summary>
            /// Value of a column of the current row (DBNull when the column is missing)
            /// </summary>
            internal object Value(IDataRecord record, string column)
            {
                var ordinal = Ordinal(column);
                return ordinal < 0 ? DBNull.Value : record.GetValue(ordinal);
            }

            /// <summary>
            /// Copy the current row into <paramref name="model"/>; columns without a property are kept for
            /// GetAttribute(name); the model is clean afterwards
            /// </summary>
            internal void Fill(IDataRecord record, object model)
            {
                var properties = _metadata.Properties;
                for (int i = 0; i < _propertyOrdinals.Length; i++)
                {
                    var ordinal = _propertyOrdinals[i];
                    if (ordinal < 0)
                        continue;

                    var value = record.GetValue(ordinal);
                    if (value != DBNull.Value)
                        properties[i].Assign(model, value);
                }

                var computed = _metadata.ComputedProperties;
                for (int i = 0; i < _computedOrdinals.Length; i++)
                {
                    var ordinal = _computedOrdinals[i];
                    if (ordinal < 0)
                        continue;

                    var value = record.GetValue(ordinal);
                    if (value != DBNull.Value)
                        computed[i].Assign(model, value);
                }

                if (model is ModelBase loaded)
                {
                    foreach (var ordinal in _extraOrdinals)
                    {
                        var value = record.GetValue(ordinal);
                        loaded.SetExtra(_names[ordinal], value == DBNull.Value ? null : value);
                    }

                    // Loaded state = clean: Save() writes only what changes from here on
                    loaded.SyncOriginal();
                }
            }
        }

        /// <summary>
        /// Copy matching columns of <paramref name="row"/> into <paramref name="model"/>. Columns without a
        /// property (SelectRaw aliases, pivot columns) are kept on the model: <c>GetAttribute(name)</c>.
        /// </summary>
        internal static void MapRow(DataRow row, object model, ModelMetadata metadata)
        {
            var map = GetMap(row.Table, metadata);
            for (int i = 0; i < map.PropertyOrdinals.Length; i++)
            {
                var ordinal = map.PropertyOrdinals[i];
                if (ordinal < 0)
                    continue;

                var value = row[ordinal];
                if (value != DBNull.Value)
                    metadata.Properties[i].Assign(model, value);
            }

            var loaded = model as ModelBase;
            if (loaded != null)
            {
                foreach (var ordinal in map.ExtraOrdinals)
                {
                    var value = row[ordinal];
                    loaded.SetExtra(row.Table.Columns[ordinal].ColumnName, value == DBNull.Value ? null : value);
                }

                // Loaded state = clean: Save() writes only what changes from here on
                loaded.SyncOriginal();
            }
        }

        /// <summary>
        /// Column ordinal by name, ignoring case without the current culture (-1 when missing).
        /// DataTable's own lookup uses the culture: under tr-TR "id" (PostgreSQL folds names to
        /// lower case) does not match "Id", so properties with an upper-case I were never filled.
        /// </summary>
        internal static int ColumnOrdinal(DataTable table, string name)
        {
            if (table == null || string.IsNullOrEmpty(name))
                return -1;

            var columns = table.Columns;
            int fallback = -1;
            for (int i = 0; i < columns.Count; i++)
            {
                var columnName = columns[i].ColumnName;
                if (string.Equals(columnName, name, StringComparison.Ordinal))
                    return i;
                if (fallback < 0 && string.Equals(columnName, name, StringComparison.OrdinalIgnoreCase))
                    fallback = i;
            }
            return fallback;
        }

        /// <summary>
        /// Value of a column by name (see <see cref="ColumnOrdinal"/>); DBNull when the column is missing
        /// </summary>
        internal static object ColumnValue(DataRow row, string name)
        {
            var ordinal = ColumnOrdinal(row.Table, name);
            return ordinal < 0 ? DBNull.Value : row[ordinal];
        }

        // Column ordinals per DataTable + model type, computed once per result set
        private static readonly ConditionalWeakTable<DataTable, RowMap> _maps = new ConditionalWeakTable<DataTable, RowMap>();

        private static RowMap GetMap(DataTable table, ModelMetadata metadata)
        {
            lock (_maps)
            {
                if (_maps.TryGetValue(table, out var map))
                {
                    if (map.Metadata == metadata && map.ColumnCount == table.Columns.Count)
                        return map;
                    _maps.Remove(table);
                }

                map = new RowMap(table, metadata);
                _maps.Add(table, map);
                return map;
            }
        }

        private sealed class RowMap
        {
            public RowMap(DataTable table, ModelMetadata metadata)
            {
                Metadata = metadata;
                ColumnCount = table.Columns.Count;
                PropertyOrdinals = new int[metadata.Properties.Count];

                var used = new bool[ColumnCount];
                for (int i = 0; i < PropertyOrdinals.Length; i++)
                {
                    var ordinal = ColumnOrdinal(table, metadata.Properties[i].ColumnName);
                    PropertyOrdinals[i] = ordinal;
                    if (ordinal >= 0)
                        used[ordinal] = true;
                }

                var extra = new System.Collections.Generic.List<int>();
                for (int i = 0; i < ColumnCount; i++)
                {
                    if (!used[i])
                        extra.Add(i);
                }
                ExtraOrdinals = extra.ToArray();
            }

            public ModelMetadata Metadata { get; }
            public int ColumnCount { get; }
            public int[] PropertyOrdinals { get; }
            public int[] ExtraOrdinals { get; }
        }

        /// <summary>
        /// Key used to match related rows: 5 (int) and 5L (long, SQLite) must be equal
        /// </summary>
        internal static string? KeyOf(object? value)
        {
            switch (value)
            {
                case null:
                    return null;
                case DBNull _:
                    return null;
                case byte[] b when b.Length == 16:
                    return new Guid(b).ToString("N");
                case Guid g:
                    return g.ToString("N");
                case string s:
                    return Guid.TryParse(s, out var parsed) ? parsed.ToString("N") : s;
                case sbyte _:
                case byte _:
                case short _:
                case ushort _:
                case int _:
                case uint _:
                case long _:
                case ulong _:
                case decimal _:
                case Enum _:
                    return Convert.ToDecimal(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
                default:
                    return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// True when a primary key value means "not saved yet" (null, 0, Guid.Empty, "")
        /// </summary>
        internal static bool IsDefaultKey(object? value)
        {
            switch (value)
            {
                case null:
                    return true;
                case string s:
                    return s.Length == 0;
                case Guid g:
                    return g == Guid.Empty;
                case sbyte _:
                case byte _:
                case short _:
                case ushort _:
                case int _:
                case uint _:
                case long _:
                case ulong _:
                case decimal _:
                    return Convert.ToDecimal(value, CultureInfo.InvariantCulture) == 0;
                default:
                    return false;
            }
        }

        internal static bool IsIntegerType(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            return type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte)
                || type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort) || type == typeof(sbyte);
        }
    }
}
