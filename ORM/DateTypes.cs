using System;
using System.Globalization;
using System.Reflection;

namespace mersolutionCore.ORM
{
    /// <summary>
    /// DateOnly / TimeOnly (.NET 6+). The net8.0 build uses them directly; the netstandard2.0 build finds them by
    /// name at run time, so a .NET 6 / 7 application on that build gets the same mapping.
    /// </summary>
    internal static class DateTypes
    {
        internal static bool IsDateOnly(Type type) => type.FullName == "System.DateOnly";

        internal static bool IsTimeOnly(Type type) => type.FullName == "System.TimeOnly";

        internal static bool IsDateOrTimeOnly(Type type) => IsDateOnly(type) || IsTimeOnly(type);

        /// <summary>
        /// Database value (DateTime, DateTimeOffset, text) → DateOnly
        /// </summary>
        internal static object ToDateOnly(Type dateOnlyType, object value)
        {
            DateTime date;
            switch (value)
            {
                case DateTime dt:
                    date = dt;
                    break;
                case DateTimeOffset dto:
                    date = dto.DateTime;
                    break;
                case string s:
                    date = DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces);
                    break;
                default:
                    date = Convert.ToDateTime(value, CultureInfo.InvariantCulture);
                    break;
            }
#if NET6_0_OR_GREATER
            return DateOnly.FromDateTime(date);
#else
            return Invoke(dateOnlyType, "FromDateTime", typeof(DateTime), date);
#endif
        }

        /// <summary>
        /// Database value (TimeSpan, DateTime, text) → TimeOnly
        /// </summary>
        internal static object ToTimeOnly(Type timeOnlyType, object value)
        {
            TimeSpan time;
            switch (value)
            {
                case TimeSpan ts:
                    time = ts;
                    break;
                case DateTime dt:
                    time = dt.TimeOfDay;
                    break;
                case string s:
                    time = s.IndexOf('-') >= 0 || s.IndexOf(' ') > 0
                        ? DateTime.Parse(s, CultureInfo.InvariantCulture).TimeOfDay
                        : TimeSpan.Parse(s, CultureInfo.InvariantCulture);
                    break;
                default:
                    time = TimeSpan.FromTicks(Convert.ToInt64(value, CultureInfo.InvariantCulture));
                    break;
            }
#if NET6_0_OR_GREATER
            return TimeOnly.FromTimeSpan(time);
#else
            return Invoke(timeOnlyType, "FromTimeSpan", typeof(TimeSpan), time);
#endif
        }

        /// <summary>
        /// Parameter value for the provider. The net8.0 drivers bind DateOnly / TimeOnly themselves; the
        /// netstandard2.0 drivers get DateTime (midnight) / TimeSpan.
        /// </summary>
        internal static object ToParameterValue(object value)
        {
#if NET6_0_OR_GREATER
            return value;
#else
            var type = value.GetType();
            if (IsDateOnly(type))
            {
                var dayNumber = (int)type.GetProperty("DayNumber")!.GetValue(value)!;
                return DateTime.MinValue.AddDays(dayNumber);
            }
            if (IsTimeOnly(type))
                return new TimeSpan((long)type.GetProperty("Ticks")!.GetValue(value)!);
            return value;
#endif
        }

#if !NET6_0_OR_GREATER
        private static object Invoke(Type type, string method, Type argumentType, object argument)
        {
            var factory = type.GetMethod(method, BindingFlags.Public | BindingFlags.Static, null, new[] { argumentType }, null)
                          ?? throw new MissingMethodException(type.FullName, method);
            return factory.Invoke(null, new[] { argument })!;
        }
#endif
    }
}
