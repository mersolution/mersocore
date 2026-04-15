using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace mersolutionCore.Validation
{
    /// <summary>
    /// Input validation system
    /// </summary>
    public class Validator
    {
        private readonly Dictionary<string, object> _data;
        private readonly Dictionary<string, List<string>> _errors = new Dictionary<string, List<string>>();
        private readonly Dictionary<string, string> _customMessages = new Dictionary<string, string>();

        /// <summary>
        /// Create validator with data to validate
        /// </summary>
        public Validator(Dictionary<string, object> data)
        {
            _data = data ?? new Dictionary<string, object>();
        }

        /// <summary>
        /// Create validator from object properties
        /// </summary>
        public static Validator Make(object data)
        {
            var dict = new Dictionary<string, object>();
            if (data != null)
            {
                foreach (var prop in data.GetType().GetProperties())
                {
                    dict[prop.Name] = prop.GetValue(data);
                }
            }
            return new Validator(dict);
        }

        /// <summary>
        /// Validate data against rules
        /// </summary>
        public Validator Validate(Dictionary<string, string> rules)
        {
            foreach (var rule in rules)
            {
                var field = rule.Key;
                var ruleString = rule.Value;
                var ruleList = ruleString.Split('|');

                foreach (var r in ruleList)
                {
                    ApplyRule(field, r.Trim());
                }
            }

            return this;
        }

        /// <summary>
        /// Set custom error messages
        /// </summary>
        public Validator WithMessages(Dictionary<string, string> messages)
        {
            foreach (var msg in messages)
            {
                _customMessages[msg.Key] = msg.Value;
            }
            return this;
        }

        /// <summary>
        /// Check if validation failed
        /// </summary>
        public bool Fails()
        {
            return _errors.Count > 0;
        }

        /// <summary>
        /// Check if validation passed
        /// </summary>
        public bool Passes()
        {
            return _errors.Count == 0;
        }

        /// <summary>
        /// Get all errors
        /// </summary>
        public Dictionary<string, List<string>> Errors()
        {
            return _errors;
        }

        /// <summary>
        /// Get first error for a field
        /// </summary>
        public string FirstError(string field)
        {
            return _errors.ContainsKey(field) && _errors[field].Count > 0
                ? _errors[field][0]
                : null;
        }

        /// <summary>
        /// Get all errors for a field
        /// </summary>
        public List<string> GetErrors(string field)
        {
            return _errors.ContainsKey(field) ? _errors[field] : new List<string>();
        }

        /// <summary>
        /// Check if field has error
        /// </summary>
        public bool HasError(string field)
        {
            return _errors.ContainsKey(field) && _errors[field].Count > 0;
        }

        /// <summary>
        /// Get all error messages as flat list
        /// </summary>
        public List<string> AllErrors()
        {
            return _errors.SelectMany(e => e.Value).ToList();
        }

        /// <summary>
        /// Get first error message
        /// </summary>
        public string FirstErrorMessage()
        {
            return AllErrors().FirstOrDefault();
        }

        /// <summary>
        /// Get validated data (only fields that passed validation)
        /// </summary>
        public Dictionary<string, object> Validated()
        {
            var validated = new Dictionary<string, object>();
            foreach (var kvp in _data)
            {
                if (!_errors.ContainsKey(kvp.Key))
                {
                    validated[kvp.Key] = kvp.Value;
                }
            }
            return validated;
        }

        #region Rule Application

        private void ApplyRule(string field, string rule)
        {
            var value = _data.ContainsKey(field) ? _data[field] : null;
            var stringValue = value?.ToString() ?? "";

            // Parse rule with parameters
            var ruleParts = rule.Split(':');
            var ruleName = ruleParts[0].ToLower();
            var ruleParams = ruleParts.Length > 1 ? ruleParts[1].Split(',') : new string[0];

            switch (ruleName)
            {
                case "required":
                    if (value == null || string.IsNullOrWhiteSpace(stringValue))
                        AddError(field, rule, $"{field} is required");
                    break;

                case "nullable":
                    // Nullable allows null/empty values, skip other validations if empty
                    break;

                case "email":
                    if (!string.IsNullOrEmpty(stringValue) && !IsValidEmail(stringValue))
                        AddError(field, rule, $"{field} must be a valid email address");
                    break;

                case "url":
                    if (!string.IsNullOrEmpty(stringValue) && !IsValidUrl(stringValue))
                        AddError(field, rule, $"{field} must be a valid URL");
                    break;

                case "numeric":
                    if (!string.IsNullOrEmpty(stringValue) && !IsNumeric(stringValue))
                        AddError(field, rule, $"{field} must be numeric");
                    break;

                case "integer":
                    if (!string.IsNullOrEmpty(stringValue) && !int.TryParse(stringValue, out _))
                        AddError(field, rule, $"{field} must be an integer");
                    break;

                case "decimal":
                    if (!string.IsNullOrEmpty(stringValue) && !decimal.TryParse(stringValue, out _))
                        AddError(field, rule, $"{field} must be a decimal number");
                    break;

                case "boolean":
                    if (!string.IsNullOrEmpty(stringValue) && !IsBoolean(stringValue))
                        AddError(field, rule, $"{field} must be a boolean");
                    break;

                case "date":
                    if (!string.IsNullOrEmpty(stringValue) && !DateTime.TryParse(stringValue, out _))
                        AddError(field, rule, $"{field} must be a valid date");
                    break;

                case "min":
                    if (ruleParams.Length > 0 && int.TryParse(ruleParams[0], out int minVal))
                    {
                        if (IsNumeric(stringValue))
                        {
                            if (decimal.Parse(stringValue) < minVal)
                                AddError(field, rule, $"{field} must be at least {minVal}");
                        }
                        else if (stringValue.Length < minVal)
                        {
                            AddError(field, rule, $"{field} must be at least {minVal} characters");
                        }
                    }
                    break;

                case "max":
                    if (ruleParams.Length > 0 && int.TryParse(ruleParams[0], out int maxVal))
                    {
                        if (IsNumeric(stringValue))
                        {
                            if (decimal.Parse(stringValue) > maxVal)
                                AddError(field, rule, $"{field} must not exceed {maxVal}");
                        }
                        else if (stringValue.Length > maxVal)
                        {
                            AddError(field, rule, $"{field} must not exceed {maxVal} characters");
                        }
                    }
                    break;

                case "between":
                    if (ruleParams.Length >= 2 && 
                        int.TryParse(ruleParams[0], out int betweenMin) && 
                        int.TryParse(ruleParams[1], out int betweenMax))
                    {
                        if (IsNumeric(stringValue))
                        {
                            var numVal = decimal.Parse(stringValue);
                            if (numVal < betweenMin || numVal > betweenMax)
                                AddError(field, rule, $"{field} must be between {betweenMin} and {betweenMax}");
                        }
                        else
                        {
                            if (stringValue.Length < betweenMin || stringValue.Length > betweenMax)
                                AddError(field, rule, $"{field} must be between {betweenMin} and {betweenMax} characters");
                        }
                    }
                    break;

                case "length":
                    if (ruleParams.Length > 0 && int.TryParse(ruleParams[0], out int exactLen))
                    {
                        if (stringValue.Length != exactLen)
                            AddError(field, rule, $"{field} must be exactly {exactLen} characters");
                    }
                    break;

                case "alpha":
                    if (!string.IsNullOrEmpty(stringValue) && !Regex.IsMatch(stringValue, @"^[a-zA-ZğüşıöçĞÜŞİÖÇ]+$"))
                        AddError(field, rule, $"{field} must contain only letters");
                    break;

                case "alpha_num":
                    if (!string.IsNullOrEmpty(stringValue) && !Regex.IsMatch(stringValue, @"^[a-zA-Z0-9ğüşıöçĞÜŞİÖÇ]+$"))
                        AddError(field, rule, $"{field} must contain only letters and numbers");
                    break;

                case "alpha_dash":
                    if (!string.IsNullOrEmpty(stringValue) && !Regex.IsMatch(stringValue, @"^[a-zA-Z0-9_\-ğüşıöçĞÜŞİÖÇ]+$"))
                        AddError(field, rule, $"{field} must contain only letters, numbers, dashes and underscores");
                    break;

                case "regex":
                    if (ruleParams.Length > 0 && !string.IsNullOrEmpty(stringValue))
                    {
                        var pattern = string.Join(":", ruleParams); // Rejoin in case pattern contains :
                        if (!Regex.IsMatch(stringValue, pattern))
                            AddError(field, rule, $"{field} format is invalid");
                    }
                    break;

                case "in":
                    if (!string.IsNullOrEmpty(stringValue) && !ruleParams.Contains(stringValue))
                        AddError(field, rule, $"{field} must be one of: {string.Join(", ", ruleParams)}");
                    break;

                case "not_in":
                    if (!string.IsNullOrEmpty(stringValue) && ruleParams.Contains(stringValue))
                        AddError(field, rule, $"{field} must not be one of: {string.Join(", ", ruleParams)}");
                    break;

                case "confirmed":
                    var confirmField = ruleParams.Length > 0 ? ruleParams[0] : field + "_confirmation";
                    var confirmValue = _data.ContainsKey(confirmField) ? _data[confirmField]?.ToString() : null;
                    if (stringValue != confirmValue)
                        AddError(field, rule, $"{field} confirmation does not match");
                    break;

                case "same":
                    if (ruleParams.Length > 0)
                    {
                        var sameField = ruleParams[0];
                        var sameValue = _data.ContainsKey(sameField) ? _data[sameField]?.ToString() : null;
                        if (stringValue != sameValue)
                            AddError(field, rule, $"{field} must match {sameField}");
                    }
                    break;

                case "different":
                    if (ruleParams.Length > 0)
                    {
                        var diffField = ruleParams[0];
                        var diffValue = _data.ContainsKey(diffField) ? _data[diffField]?.ToString() : null;
                        if (stringValue == diffValue)
                            AddError(field, rule, $"{field} must be different from {diffField}");
                    }
                    break;

                case "starts_with":
                    if (ruleParams.Length > 0 && !string.IsNullOrEmpty(stringValue))
                    {
                        if (!ruleParams.Any(p => stringValue.StartsWith(p)))
                            AddError(field, rule, $"{field} must start with one of: {string.Join(", ", ruleParams)}");
                    }
                    break;

                case "ends_with":
                    if (ruleParams.Length > 0 && !string.IsNullOrEmpty(stringValue))
                    {
                        if (!ruleParams.Any(p => stringValue.EndsWith(p)))
                            AddError(field, rule, $"{field} must end with one of: {string.Join(", ", ruleParams)}");
                    }
                    break;

                case "uuid":
                    if (!string.IsNullOrEmpty(stringValue) && !Guid.TryParse(stringValue, out _))
                        AddError(field, rule, $"{field} must be a valid UUID");
                    break;

                case "ip":
                    if (!string.IsNullOrEmpty(stringValue) && !IsValidIp(stringValue))
                        AddError(field, rule, $"{field} must be a valid IP address");
                    break;

                case "json":
                    if (!string.IsNullOrEmpty(stringValue) && !IsValidJson(stringValue))
                        AddError(field, rule, $"{field} must be valid JSON");
                    break;

                case "phone":
                    if (!string.IsNullOrEmpty(stringValue) && !IsValidPhone(stringValue))
                        AddError(field, rule, $"{field} must be a valid phone number");
                    break;

                case "credit_card":
                    if (!string.IsNullOrEmpty(stringValue) && !IsValidCreditCard(stringValue))
                        AddError(field, rule, $"{field} must be a valid credit card number");
                    break;

                case "tc_kimlik":
                    if (!string.IsNullOrEmpty(stringValue) && !IsValidTcKimlik(stringValue))
                        AddError(field, rule, $"{field} must be a valid TC Kimlik number");
                    break;

                case "iban":
                    if (!string.IsNullOrEmpty(stringValue) && !IsValidIban(stringValue))
                        AddError(field, rule, $"{field} must be a valid IBAN");
                    break;

                case "after":
                    if (ruleParams.Length > 0 && DateTime.TryParse(stringValue, out DateTime afterDate))
                    {
                        if (DateTime.TryParse(ruleParams[0], out DateTime afterCompare))
                        {
                            if (afterDate <= afterCompare)
                                AddError(field, rule, $"{field} must be after {ruleParams[0]}");
                        }
                    }
                    break;

                case "before":
                    if (ruleParams.Length > 0 && DateTime.TryParse(stringValue, out DateTime beforeDate))
                    {
                        if (DateTime.TryParse(ruleParams[0], out DateTime beforeCompare))
                        {
                            if (beforeDate >= beforeCompare)
                                AddError(field, rule, $"{field} must be before {ruleParams[0]}");
                        }
                    }
                    break;

                case "password":
                    if (!string.IsNullOrEmpty(stringValue))
                    {
                        if (stringValue.Length < 8)
                            AddError(field, rule, $"{field} must be at least 8 characters");
                        else if (!Regex.IsMatch(stringValue, @"[A-Z]"))
                            AddError(field, rule, $"{field} must contain at least one uppercase letter");
                        else if (!Regex.IsMatch(stringValue, @"[a-z]"))
                            AddError(field, rule, $"{field} must contain at least one lowercase letter");
                        else if (!Regex.IsMatch(stringValue, @"[0-9]"))
                            AddError(field, rule, $"{field} must contain at least one number");
                    }
                    break;
            }
        }

        private void AddError(string field, string rule, string defaultMessage)
        {
            var messageKey = $"{field}.{rule.Split(':')[0]}";
            var message = _customMessages.ContainsKey(messageKey) 
                ? _customMessages[messageKey] 
                : defaultMessage;

            if (!_errors.ContainsKey(field))
                _errors[field] = new List<string>();

            _errors[field].Add(message);
        }

        #endregion

        #region Validation Helpers

        private bool IsValidEmail(string email)
        {
            return Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");
        }

        private bool IsValidUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var result) &&
                   (result.Scheme == Uri.UriSchemeHttp || result.Scheme == Uri.UriSchemeHttps);
        }

        private bool IsNumeric(string value)
        {
            return decimal.TryParse(value, out _);
        }

        private bool IsBoolean(string value)
        {
            var lower = value.ToLower();
            return lower == "true" || lower == "false" || lower == "1" || lower == "0" ||
                   lower == "yes" || lower == "no";
        }

        private bool IsValidIp(string ip)
        {
            return System.Net.IPAddress.TryParse(ip, out _);
        }

        private bool IsValidJson(string json)
        {
            try
            {
                json = json.Trim();
                return (json.StartsWith("{") && json.EndsWith("}")) ||
                       (json.StartsWith("[") && json.EndsWith("]"));
            }
            catch
            {
                return false;
            }
        }

        private bool IsValidPhone(string phone)
        {
            var cleaned = Regex.Replace(phone, @"[\s\-\(\)\+]", "");
            return Regex.IsMatch(cleaned, @"^\d{10,15}$");
        }

        private bool IsValidCreditCard(string card)
        {
            var cleaned = Regex.Replace(card, @"[\s\-]", "");
            if (!Regex.IsMatch(cleaned, @"^\d{13,19}$"))
                return false;

            // Luhn algorithm
            int sum = 0;
            bool alternate = false;
            for (int i = cleaned.Length - 1; i >= 0; i--)
            {
                int n = int.Parse(cleaned[i].ToString());
                if (alternate)
                {
                    n *= 2;
                    if (n > 9) n -= 9;
                }
                sum += n;
                alternate = !alternate;
            }
            return sum % 10 == 0;
        }

        private bool IsValidTcKimlik(string tc)
        {
            if (tc.Length != 11 || !tc.All(char.IsDigit) || tc[0] == '0')
                return false;

            var digits = tc.Select(c => int.Parse(c.ToString())).ToArray();

            // TC Kimlik algorithm
            int oddSum = digits[0] + digits[2] + digits[4] + digits[6] + digits[8];
            int evenSum = digits[1] + digits[3] + digits[5] + digits[7];
            int check1 = ((oddSum * 7) - evenSum) % 10;
            int check2 = (digits.Take(10).Sum()) % 10;

            return digits[9] == check1 && digits[10] == check2;
        }

        private bool IsValidIban(string iban)
        {
            var cleaned = Regex.Replace(iban.ToUpper(), @"\s", "");
            if (cleaned.Length < 15 || cleaned.Length > 34)
                return false;

            // Move first 4 chars to end
            var rearranged = cleaned.Substring(4) + cleaned.Substring(0, 4);

            // Convert letters to numbers (A=10, B=11, etc.)
            var numeric = "";
            foreach (var c in rearranged)
            {
                if (char.IsLetter(c))
                    numeric += (c - 'A' + 10).ToString();
                else
                    numeric += c;
            }

            // Mod 97 check
            int remainder = 0;
            foreach (var c in numeric)
            {
                remainder = (remainder * 10 + int.Parse(c.ToString())) % 97;
            }

            return remainder == 1;
        }

        #endregion
    }

    /// <summary>
    /// Validation result wrapper
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public Dictionary<string, List<string>> Errors { get; set; } = new Dictionary<string, List<string>>();

        public static ValidationResult Success() => new ValidationResult { IsValid = true };

        public static ValidationResult Failure(Dictionary<string, List<string>> errors) =>
            new ValidationResult { IsValid = false, Errors = errors };
    }
}
