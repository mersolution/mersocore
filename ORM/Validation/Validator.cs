using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace mersolutionCore.ORM.Validation
{
    /// <summary>
    /// Input validation system
    /// </summary>
    public class Validator
    {
        private readonly Dictionary<string, object?> _data;
        private readonly Dictionary<string, List<string>> _errors = new Dictionary<string, List<string>>();
        private readonly Dictionary<string, string> _customMessages = new Dictionary<string, string>();

        /// <summary>
        /// Create validator with data to validate
        /// </summary>
        public Validator(Dictionary<string, object?> data)
        {
            _data = data ?? new Dictionary<string, object?>();
        }

        /// <summary>
        /// Create validator from object properties
        /// </summary>
        public static Validator Make(object data)
        {
            var dict = new Dictionary<string, object?>();
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
                var ruleList = SplitRules(rule.Value ?? string.Empty);
                var ruleNames = ruleList.Select(r => RuleName(r)).ToList();

                // "nullable": an empty value skips every other rule
                var value = _data.ContainsKey(field) ? _data[field] : null;
                if (ruleNames.Contains("nullable") && (value == null || string.IsNullOrEmpty(value.ToString())))
                    continue;

                bool numericContext = IsNumericType(value) || ruleNames.Any(n => n == "numeric" || n == "integer" || n == "decimal");

                foreach (var r in ruleList)
                {
                    ApplyRule(field, r, numericContext);
                }
            }

            return this;
        }

        // Split on '|' but keep a regex rule (which may contain '|') as the last rule
        private static List<string> SplitRules(string ruleString)
        {
            var result = new List<string>();
            var regexIndex = ruleString.IndexOf("regex:", StringComparison.OrdinalIgnoreCase);
            var head = regexIndex >= 0 ? ruleString.Substring(0, regexIndex) : ruleString;

            result.AddRange(head.Split('|').Select(r => r.Trim()).Where(r => r.Length > 0));
            if (regexIndex >= 0)
                result.Add(ruleString.Substring(regexIndex).Trim());

            return result;
        }

        private static string RuleName(string rule)
        {
            var colon = rule.IndexOf(':');
            return (colon >= 0 ? rule.Substring(0, colon) : rule).Trim().ToLowerInvariant();
        }

        private static bool IsNumericType(object? value)
        {
            return value is sbyte || value is byte || value is short || value is ushort || value is int || value is uint ||
                   value is long || value is ulong || value is float || value is double || value is decimal;
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
        public string? FirstError(string field)
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
        public string? FirstErrorMessage()
        {
            return AllErrors().FirstOrDefault();
        }

        /// <summary>
        /// Get validated data (only fields that passed validation)
        /// </summary>
        public Dictionary<string, object?> Validated()
        {
            var validated = new Dictionary<string, object?>();
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

        private void ApplyRule(string field, string rule, bool numericContext)
        {
            var value = _data.ContainsKey(field) ? _data[field] : null;
            // Numbers as invariant text ("1.5"), everything else as before (dates keep the current culture)
            var stringValue = IsNumericType(value)
                ? ((IFormattable)value!).ToString(null, CultureInfo.InvariantCulture)
                : value?.ToString() ?? "";

            // Parse rule with parameters: everything after the first ':' (dates may contain ':')
            var colon = rule.IndexOf(':');
            var ruleName = RuleName(rule);
            var paramString = colon >= 0 ? rule.Substring(colon + 1) : null;
            var ruleParams = paramString != null ? paramString.Split(',') : new string[0];

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
                    if (!string.IsNullOrEmpty(stringValue) && !long.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                        AddError(field, rule, $"{field} must be an integer");
                    break;

                case "decimal":
                    if (!string.IsNullOrEmpty(stringValue) && !TryParseNumber(stringValue, out _))
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

                // min / max / between compare numbers when the value is a number type or the field has a
                // numeric|integer|decimal rule; otherwise they compare the text length (Laravel semantics)
                case "min":
                    if (ruleParams.Length > 0 && TryParseNumber(ruleParams[0], out decimal minVal))
                    {
                        if (numericContext)
                        {
                            if (TryParseNumber(stringValue, out var num) && num < minVal)
                                AddError(field, rule, $"{field} must be at least {ruleParams[0]}");
                        }
                        else if (stringValue.Length < minVal)
                        {
                            AddError(field, rule, $"{field} must be at least {ruleParams[0]} characters");
                        }
                    }
                    break;

                case "max":
                    if (ruleParams.Length > 0 && TryParseNumber(ruleParams[0], out decimal maxVal))
                    {
                        if (numericContext)
                        {
                            if (TryParseNumber(stringValue, out var num) && num > maxVal)
                                AddError(field, rule, $"{field} must not exceed {ruleParams[0]}");
                        }
                        else if (stringValue.Length > maxVal)
                        {
                            AddError(field, rule, $"{field} must not exceed {ruleParams[0]} characters");
                        }
                    }
                    break;

                case "between":
                    if (ruleParams.Length >= 2 &&
                        TryParseNumber(ruleParams[0], out decimal betweenMin) &&
                        TryParseNumber(ruleParams[1], out decimal betweenMax))
                    {
                        if (numericContext)
                        {
                            if (TryParseNumber(stringValue, out var numVal) && (numVal < betweenMin || numVal > betweenMax))
                                AddError(field, rule, $"{field} must be between {ruleParams[0]} and {ruleParams[1]}");
                        }
                        else
                        {
                            if (stringValue.Length < betweenMin || stringValue.Length > betweenMax)
                                AddError(field, rule, $"{field} must be between {ruleParams[0]} and {ruleParams[1]} characters");
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
                    // The whole text after "regex:" is the pattern (it may contain ',', ':' and '|')
                    if (!string.IsNullOrEmpty(paramString) && !string.IsNullOrEmpty(stringValue))
                    {
                        if (!Regex.IsMatch(stringValue, paramString, RegexOptions.None, TimeSpan.FromSeconds(1)))
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
            var messageKey = $"{field}.{RuleName(rule)}";
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
            return TryParseNumber(value, out _);
        }

        // "1.5" (invariant) and "1,5" (current culture, e.g. tr-TR) are both accepted; thousands separators are not
        private static bool TryParseNumber(string value, out decimal result)
        {
            result = 0;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
                || decimal.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out result);
        }

        private bool IsBoolean(string value)
        {
            var lower = value.ToLowerInvariant();
            return lower == "true" || lower == "false" || lower == "1" || lower == "0" ||
                   lower == "yes" || lower == "no";
        }

        private bool IsValidIp(string ip)
        {
            // IPAddress.TryParse alone accepts "1" or "1.2" as IPv4
            if (!System.Net.IPAddress.TryParse(ip, out var address))
                return false;

            return address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 || ip.Count(c => c == '.') == 3;
        }

        private bool IsValidJson(string json)
        {
            try
            {
                using (System.Text.Json.JsonDocument.Parse(json))
                {
                    return true;
                }
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

            // TC Kimlik algorithm (C# % can be negative: normalise to 0..9)
            int oddSum = digits[0] + digits[2] + digits[4] + digits[6] + digits[8];
            int evenSum = digits[1] + digits[3] + digits[5] + digits[7];
            int check1 = (((oddSum * 7) - evenSum) % 10 + 10) % 10;
            int check2 = (digits.Take(10).Sum()) % 10;

            return digits[9] == check1 && digits[10] == check2;
        }

        private bool IsValidIban(string iban)
        {
            var cleaned = Regex.Replace(iban.ToUpperInvariant(), @"[\s\-]", "");
            if (cleaned.Length < 15 || cleaned.Length > 34)
                return false;

            // Country code + check digits + A-Z/0-9 only (other characters made int.Parse throw)
            if (!Regex.IsMatch(cleaned, @"^[A-Z]{2}[0-9]{2}[A-Z0-9]+$"))
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
}
