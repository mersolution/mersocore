using System;
using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;

namespace mersolutionCore.ORM.Validation
{
    internal static class AttributeValues
    {
        internal static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

        // "12.5" (invariant) and "12,5" (current culture, e.g. tr-TR) both mean 12.5;
        // Convert.ToDouble("12.5") under tr-TR would give 125
        internal static bool TryGetNumber(object value, out double number)
        {
            number = 0;

            if (value is string s)
            {
                return !string.IsNullOrWhiteSpace(s)
                    && (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
                        || double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out number));
            }

            try
            {
                number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // string length, or element count for arrays / collections
        internal static bool TryGetLength(object value, out int length)
        {
            switch (value)
            {
                case string str:
                    length = str.Length;
                    return true;
                case ICollection collection:
                    length = collection.Count;
                    return true;
                default:
                    length = 0;
                    return false;
            }
        }

        internal static bool IsMatch(Regex regex, string input)
        {
            try
            {
                return regex.IsMatch(input);
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Zorunlu alan
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class RequiredAttribute : ValidationAttribute
    {
        public override bool IsValid(object? value)
        {
            if (value == null) return false;
            if (value is string str && string.IsNullOrWhiteSpace(str)) return false;
            return true;
        }

        public override string ErrorMessage => "{0} alanı zorunludur.";
    }

    /// <summary>
    /// Maksimum uzunluk
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class MaxLengthAttribute : ValidationAttribute
    {
        public int Length { get; }

        public MaxLengthAttribute(int length)
        {
            Length = length;
        }

        public override bool IsValid(object? value)
        {
            if (value == null) return true;
            if (AttributeValues.TryGetLength(value, out var length)) return length <= Length;
            return true;
        }

        public override string ErrorMessage => $"{{0}} alanı en fazla {Length} karakter olabilir.";
    }

    /// <summary>
    /// Minimum uzunluk
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class MinLengthAttribute : ValidationAttribute
    {
        public int Length { get; }

        public MinLengthAttribute(int length)
        {
            Length = length;
        }

        public override bool IsValid(object? value)
        {
            if (value == null) return true;
            if (AttributeValues.TryGetLength(value, out var length)) return length >= Length;
            return true;
        }

        public override string ErrorMessage => $"{{0}} alanı en az {Length} karakter olmalıdır.";
    }

    /// <summary>
    /// Email formatı
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class EmailAttribute : ValidationAttribute
    {
        private static readonly Regex EmailRegex = new Regex(
            @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase, AttributeValues.RegexTimeout);

        public override bool IsValid(object? value)
        {
            if (value == null) return true;
            if (value is string str) return AttributeValues.IsMatch(EmailRegex, str);
            return false;
        }

        public override string ErrorMessage => "{0} geçerli bir email adresi olmalıdır.";
    }

    /// <summary>
    /// Sayı aralığı
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class RangeAttribute : ValidationAttribute
    {
        public double Min { get; }
        public double Max { get; }

        public RangeAttribute(double min, double max)
        {
            Min = min;
            Max = max;
        }

        public override bool IsValid(object? value)
        {
            if (value == null) return true;
            return AttributeValues.TryGetNumber(value, out var num) && num >= Min && num <= Max;
        }

        public override string ErrorMessage => $"{{0}} alanı {Min} ile {Max} arasında olmalıdır.";
    }

    /// <summary>
    /// Regex pattern
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class PatternAttribute : ValidationAttribute
    {
        public string Pattern { get; }
        private readonly Regex _regex;

        public PatternAttribute(string pattern)
        {
            Pattern = pattern;
            _regex = new Regex(pattern, RegexOptions.Compiled, AttributeValues.RegexTimeout);
        }

        public override bool IsValid(object? value)
        {
            if (value == null) return true;
            if (value is string str) return AttributeValues.IsMatch(_regex, str);
            return false;
        }

        public override string ErrorMessage => "{0} geçerli formatta değil.";
    }

    /// <summary>
    /// Telefon numarası
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class PhoneAttribute : ValidationAttribute
    {
        private static readonly Regex PhoneRegex = new Regex(
            @"^[\d\s\-\+\(\)]+$",
            RegexOptions.Compiled);

        // Same rule as Validator "phone": 10-15 digits; spaces, '-', '+', '(' and ')' are separators
        // (length alone let "----------" pass)
        public override bool IsValid(object? value)
        {
            if (value == null) return true;
            if (!(value is string str) || !PhoneRegex.IsMatch(str)) return false;

            var digits = 0;
            foreach (var c in str)
            {
                if (c >= '0' && c <= '9') digits++;
            }
            return digits >= 10 && digits <= 15;
        }

        public override string ErrorMessage => "{0} geçerli bir telefon numarası olmalıdır.";
    }

    /// <summary>
    /// URL formatı
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class UrlAttribute : ValidationAttribute
    {
        public override bool IsValid(object? value)
        {
            if (value == null) return true;
            if (value is string str)
            {
                return Uri.TryCreate(str, UriKind.Absolute, out var uri) &&
                       (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
            }
            return false;
        }

        public override string ErrorMessage => "{0} geçerli bir URL olmalıdır.";
    }

    /// <summary>
    /// Pozitif sayı
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class PositiveAttribute : ValidationAttribute
    {
        public override bool IsValid(object? value)
        {
            if (value == null) return true;
            return AttributeValues.TryGetNumber(value, out var num) && num > 0;
        }

        public override string ErrorMessage => "{0} pozitif bir sayı olmalıdır.";
    }

    /// <summary>
    /// Negatif olmayan sayı
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class NonNegativeAttribute : ValidationAttribute
    {
        public override bool IsValid(object? value)
        {
            if (value == null) return true;
            return AttributeValues.TryGetNumber(value, out var num) && num >= 0;
        }

        public override string ErrorMessage => "{0} negatif olamaz.";
    }

    /// <summary>
    /// Base validation attribute
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public abstract class ValidationAttribute : Attribute
    {
        public abstract bool IsValid(object? value);
        public abstract string ErrorMessage { get; }
    }
}
