using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace mersolutionCore.ORM.Validation
{
    /// <summary>
    /// MersoValidator - Model doğrulama sistemi
    /// </summary>
    public static class MersoValidator
    {
        /// <summary>
        /// Model'i doğrula
        /// </summary>
        public static ValidationResult Validate<T>(T model) where T : class
        {
            var result = new ValidationResult();
            var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var prop in properties)
            {
                var value = prop.GetValue(model);
                var attributes = prop.GetCustomAttributes(typeof(ValidationAttribute), true);

                foreach (ValidationAttribute attr in attributes)
                {
                    if (!attr.IsValid(value))
                    {
                        var message = string.Format(attr.ErrorMessage, prop.Name);
                        result.AddError(prop.Name, message);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Model'i doğrula, hata varsa exception fırlat
        /// </summary>
        public static void ValidateOrFail<T>(T model) where T : class
        {
            var result = Validate(model);
            if (!result.IsValid)
            {
                throw new ValidationException(result);
            }
        }
    }

    /// <summary>
    /// Doğrulama sonucu
    /// </summary>
    public class ValidationResult
    {
        private readonly Dictionary<string, List<string>> _errors = new Dictionary<string, List<string>>();

        public bool IsValid => _errors.Count == 0;

        public Dictionary<string, List<string>> Errors => _errors;

        public void AddError(string property, string message)
        {
            if (!_errors.ContainsKey(property))
                _errors[property] = new List<string>();

            _errors[property].Add(message);
        }

        public List<string> GetErrors(string property)
        {
            return _errors.ContainsKey(property) ? _errors[property] : new List<string>();
        }

        public List<string> AllErrors()
        {
            return _errors.SelectMany(e => e.Value).ToList();
        }

        public string FirstError()
        {
            return AllErrors().FirstOrDefault();
        }
    }

    /// <summary>
    /// Doğrulama hatası exception
    /// </summary>
    public class ValidationException : Exception
    {
        public ValidationResult ValidationResult { get; }

        public ValidationException(ValidationResult result)
            : base(result.FirstError() ?? "Doğrulama hatası")
        {
            ValidationResult = result;
        }
    }

    /// <summary>
    /// Model extension methods for validation
    /// </summary>
    public static class ValidationExtensions
    {
        /// <summary>
        /// Model'i doğrula
        /// </summary>
        public static ValidationResult Validate<T>(this T model) where T : class
        {
            return MersoValidator.Validate(model);
        }

        /// <summary>
        /// Model geçerli mi?
        /// </summary>
        public static bool IsValid<T>(this T model) where T : class
        {
            return MersoValidator.Validate(model).IsValid;
        }

        /// <summary>
        /// Model'i doğrula, hata varsa exception fırlat
        /// </summary>
        public static void ValidateOrFail<T>(this T model) where T : class
        {
            MersoValidator.ValidateOrFail(model);
        }
    }
}
