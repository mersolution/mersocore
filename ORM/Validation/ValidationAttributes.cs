using System;
using System.Text.RegularExpressions;

namespace mersolutionCore.ORM.Validation
{
    /// <summary>
    /// Zorunlu alan
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class RequiredAttribute : ValidationAttribute
    {
        public override bool IsValid(object value)
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

        public override bool IsValid(object value)
        {
            if (value == null) return true;
            if (value is string str) return str.Length <= Length;
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

        public override bool IsValid(object value)
        {
            if (value == null) return true;
            if (value is string str) return str.Length >= Length;
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
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public override bool IsValid(object value)
        {
            if (value == null) return true;
            if (value is string str) return EmailRegex.IsMatch(str);
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

        public override bool IsValid(object value)
        {
            if (value == null) return true;
            
            try
            {
                var num = Convert.ToDouble(value);
                return num >= Min && num <= Max;
            }
            catch
            {
                return false;
            }
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
            _regex = new Regex(pattern, RegexOptions.Compiled);
        }

        public override bool IsValid(object value)
        {
            if (value == null) return true;
            if (value is string str) return _regex.IsMatch(str);
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

        public override bool IsValid(object value)
        {
            if (value == null) return true;
            if (value is string str) return PhoneRegex.IsMatch(str) && str.Length >= 10;
            return false;
        }

        public override string ErrorMessage => "{0} geçerli bir telefon numarası olmalıdır.";
    }

    /// <summary>
    /// URL formatı
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class UrlAttribute : ValidationAttribute
    {
        public override bool IsValid(object value)
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
        public override bool IsValid(object value)
        {
            if (value == null) return true;
            
            try
            {
                var num = Convert.ToDouble(value);
                return num > 0;
            }
            catch
            {
                return false;
            }
        }

        public override string ErrorMessage => "{0} pozitif bir sayı olmalıdır.";
    }

    /// <summary>
    /// Negatif olmayan sayı
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class NonNegativeAttribute : ValidationAttribute
    {
        public override bool IsValid(object value)
        {
            if (value == null) return true;
            
            try
            {
                var num = Convert.ToDouble(value);
                return num >= 0;
            }
            catch
            {
                return false;
            }
        }

        public override string ErrorMessage => "{0} negatif olamaz.";
    }

    /// <summary>
    /// Base validation attribute
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public abstract class ValidationAttribute : Attribute
    {
        public abstract bool IsValid(object value);
        public abstract string ErrorMessage { get; }
    }
}
