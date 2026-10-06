using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace mersolutionCore.Http
{
    /// <summary>
    /// JWT (JSON Web Token) helper for token generation and validation (HS256)
    /// </summary>
    public class JwtHelper
    {
        private readonly string _secretKey;
        private readonly string? _issuer;
        private readonly string? _audience;
        private readonly int _expirationMinutes;

        /// <summary>
        /// Create JWT helper with secret key
        /// </summary>
        /// <param name="secretKey">Secret key for signing (min 32 characters recommended)</param>
        /// <param name="issuer">Token issuer (optional; when set, tokens without this issuer are rejected)</param>
        /// <param name="audience">Token audience (optional; when set, tokens without this audience are rejected)</param>
        /// <param name="expirationMinutes">Token expiration in minutes (default: 60)</param>
        public JwtHelper(string secretKey, string? issuer = null, string? audience = null, int expirationMinutes = 60)
        {
            if (string.IsNullOrEmpty(secretKey) || secretKey.Length < 16)
                throw new ArgumentException("Secret key must be at least 16 characters", nameof(secretKey));

            _secretKey = secretKey;
            _issuer = issuer;
            _audience = audience;
            _expirationMinutes = expirationMinutes;
        }

        /// <summary>
        /// Generate JWT token
        /// </summary>
        /// <param name="claims">Claims to include in token</param>
        /// <returns>JWT token string</returns>
        public string GenerateToken(Dictionary<string, object?> claims)
        {
            var header = new Dictionary<string, object?>
            {
                { "alg", "HS256" },
                { "typ", "JWT" }
            };

            var payload = new Dictionary<string, object?>(claims ?? new Dictionary<string, object?>());

            // Add standard claims
            var now = DateTimeOffset.UtcNow;
            payload["iat"] = now.ToUnixTimeSeconds();
            payload["exp"] = now.AddMinutes(_expirationMinutes).ToUnixTimeSeconds();
            payload["nbf"] = now.ToUnixTimeSeconds();

            if (!string.IsNullOrEmpty(_issuer))
                payload["iss"] = _issuer;

            if (!string.IsNullOrEmpty(_audience))
                payload["aud"] = _audience;

            // Encode header and payload (System.Text.Json: culture independent numbers, proper escaping)
            var headerBase64 = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(header));
            var payloadBase64 = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload));

            // Create signature
            var signatureInput = $"{headerBase64}.{payloadBase64}";
            var signature = ComputeHmacSha256(signatureInput, _secretKey);
            var signatureBase64 = Base64UrlEncode(signature);

            return $"{headerBase64}.{payloadBase64}.{signatureBase64}";
        }

        /// <summary>
        /// Generate JWT token with user ID and role
        /// </summary>
        public string GenerateToken(string userId, string username, string? role = null)
        {
            var claims = new Dictionary<string, object?>
            {
                { "sub", userId },
                { "name", username },
                { "jti", Guid.NewGuid().ToString() }
            };

            if (!string.IsNullOrEmpty(role))
                claims["role"] = role;

            return GenerateToken(claims);
        }

        /// <summary>
        /// Generate JWT token with user ID, username, and multiple roles
        /// </summary>
        public string GenerateToken(string userId, string username, string[] roles)
        {
            var claims = new Dictionary<string, object?>
            {
                { "sub", userId },
                { "name", username },
                { "jti", Guid.NewGuid().ToString() },
                { "roles", roles }
            };

            return GenerateToken(claims);
        }

        /// <summary>
        /// Validate JWT token and return claims
        /// </summary>
        /// <param name="token">JWT token to validate</param>
        /// <returns>Validation result with claims when valid</returns>
        public JwtValidationResult ValidateToken(string token)
        {
            try
            {
                if (string.IsNullOrEmpty(token))
                    return JwtValidationResult.Invalid("Token is empty");

                var parts = token.Split('.');
                if (parts.Length != 3)
                    return JwtValidationResult.Invalid("Invalid token format");

                var headerBase64 = parts[0];
                var payloadBase64 = parts[1];
                var signatureBase64 = parts[2];

                // Only HS256 tokens are accepted (no "none", no algorithm switching)
                var header = ParseObject(Encoding.UTF8.GetString(Base64UrlDecode(headerBase64)));
                if (!header.TryGetValue("alg", out var alg) || !"HS256".Equals(alg as string, StringComparison.Ordinal))
                    return JwtValidationResult.Invalid("Unsupported algorithm");

                // Verify signature (constant time)
                var expected = ComputeHmacSha256($"{headerBase64}.{payloadBase64}", _secretKey);
                byte[] actual;
                try
                {
                    actual = Base64UrlDecode(signatureBase64);
                }
                catch (FormatException)
                {
                    return JwtValidationResult.Invalid("Invalid signature");
                }

                if (!FixedTimeEquals(expected, actual))
                    return JwtValidationResult.Invalid("Invalid signature");

                // Decode payload
                var claims = ParseObject(Encoding.UTF8.GetString(Base64UrlDecode(payloadBase64)));
                var now = DateTimeOffset.UtcNow;

                // Check expiration
                if (claims.TryGetValue("exp", out var expValue))
                {
                    var expDate = DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(expValue, System.Globalization.CultureInfo.InvariantCulture));
                    if (expDate < now)
                        return JwtValidationResult.Invalid("Token has expired");
                }

                // Check not before
                if (claims.TryGetValue("nbf", out var nbfValue))
                {
                    var nbfDate = DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(nbfValue, System.Globalization.CultureInfo.InvariantCulture));
                    if (nbfDate > now)
                        return JwtValidationResult.Invalid("Token is not yet valid");
                }

                // Check issuer (required when configured)
                if (!string.IsNullOrEmpty(_issuer))
                {
                    if (!claims.TryGetValue("iss", out var iss) || !_issuer!.Equals(iss?.ToString(), StringComparison.Ordinal))
                        return JwtValidationResult.Invalid("Invalid issuer");
                }

                // Check audience (required when configured; may be a string or an array)
                if (!string.IsNullOrEmpty(_audience))
                {
                    claims.TryGetValue("aud", out var aud);
                    bool audienceOk = aud is string s
                        ? s.Equals(_audience, StringComparison.Ordinal)
                        : aud is string[] list && list.Contains(_audience, StringComparer.Ordinal);
                    if (!audienceOk)
                        return JwtValidationResult.Invalid("Invalid audience");
                }

                return JwtValidationResult.Valid(claims);
            }
            catch (Exception ex)
            {
                return JwtValidationResult.Invalid($"Token validation failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if token is valid (quick check)
        /// </summary>
        public bool IsTokenValid(string token)
        {
            return ValidateToken(token).IsValid;
        }

        /// <summary>
        /// Get claim value from token WITHOUT verifying the signature — never use it for authorization
        /// </summary>
        public string? GetClaimValue(string token, string claimName)
        {
            try
            {
                var parts = token.Split('.');
                if (parts.Length != 3) return null;

                var claims = ParseObject(Encoding.UTF8.GetString(Base64UrlDecode(parts[1])));
                return claims.TryGetValue(claimName, out var value) ? JwtValidationResult.ClaimToString(value) : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Get token expiration date
        /// </summary>
        public DateTimeOffset? GetTokenExpiration(string token)
        {
            try
            {
                var expStr = GetClaimValue(token, "exp");
                if (string.IsNullOrEmpty(expStr)) return null;

                var exp = Convert.ToInt64(expStr, System.Globalization.CultureInfo.InvariantCulture);
                return DateTimeOffset.FromUnixTimeSeconds(exp);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Check if token is expired
        /// </summary>
        public bool IsTokenExpired(string token)
        {
            var exp = GetTokenExpiration(token);
            return exp.HasValue && exp.Value < DateTimeOffset.UtcNow;
        }

        /// <summary>
        /// Refresh token (generate new token with same claims but new expiration)
        /// </summary>
        public string RefreshToken(string token)
        {
            var result = ValidateToken(token);
            if (!result.IsValid)
                throw new InvalidOperationException($"Cannot refresh invalid token: {result.ErrorMessage}");

            // Remove time-related claims
            var claims = new Dictionary<string, object?>(result.Claims ?? new Dictionary<string, object?>());
            claims.Remove("iat");
            claims.Remove("exp");
            claims.Remove("nbf");
            claims.Remove("jti");

            // Generate new jti
            claims["jti"] = Guid.NewGuid().ToString();

            return GenerateToken(claims);
        }

        #region Helper Methods

        private static byte[] ComputeHmacSha256(string data, string key)
        {
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key)))
            {
                return hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
            }
        }

        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
                return false;

            int diff = 0;
            for (int i = 0; i < a.Length; i++)
                diff |= a[i] ^ b[i];
            return diff == 0;
        }

        private static string Base64UrlEncode(byte[] data)
        {
            return Convert.ToBase64String(data)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static byte[] Base64UrlDecode(string base64Url)
        {
            var base64 = base64Url
                .Replace('-', '+')
                .Replace('_', '/');

            switch (base64.Length % 4)
            {
                case 2: base64 += "=="; break;
                case 3: base64 += "="; break;
                case 1: throw new FormatException("Invalid base64url length");
            }

            return Convert.FromBase64String(base64);
        }

        /// <summary>
        /// JSON object → claims: strings, long/double, bool, null, string[] (or object[]) and nested dictionaries
        /// </summary>
        private static Dictionary<string, object?> ParseObject(string json)
        {
            using (var doc = JsonDocument.Parse(json))
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    throw new FormatException("Invalid JSON format");

                return (Dictionary<string, object?>)ToClrValue(doc.RootElement)!;
            }
        }

        private static object? ToClrValue(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    return element.GetString();
                case JsonValueKind.Number:
                    return element.TryGetInt64(out var l) ? (object)l : element.GetDouble();
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                case JsonValueKind.Array:
                    var items = element.EnumerateArray().Select(ToClrValue).ToList();
                    return items.All(i => i is string) ? (object)items.Cast<string>().ToArray() : items.ToArray();
                case JsonValueKind.Object:
                    var dict = new Dictionary<string, object?>();
                    foreach (var property in element.EnumerateObject())
                        dict[property.Name] = ToClrValue(property.Value);
                    return dict;
                default:
                    return null;
            }
        }

        #endregion
    }

    /// <summary>
    /// JWT validation result
    /// </summary>
    public class JwtValidationResult
    {
        /// <summary>
        /// Whether the token is valid
        /// </summary>
        public bool IsValid { get; private set; }

        /// <summary>
        /// Error message if invalid
        /// </summary>
        public string? ErrorMessage { get; private set; }

        /// <summary>
        /// Token claims if valid (string, long, double, bool, string[] ...)
        /// </summary>
        public Dictionary<string, object?>? Claims { get; private set; }

        /// <summary>
        /// Get claim value as string (arrays are joined with ",")
        /// </summary>
        public string? GetClaim(string name)
        {
            return Claims != null && Claims.TryGetValue(name, out var value) ? ClaimToString(value) : null;
        }

        /// <summary>
        /// Get an array claim (e.g. "roles"); a single string claim becomes a one-item array
        /// </summary>
        public string[] GetClaimArray(string name)
        {
            if (Claims == null || !Claims.TryGetValue(name, out var value) || value == null)
                return new string[0];

            switch (value)
            {
                case string[] strings:
                    return strings;
                case object[] objects:
                    return objects.Select(o => ClaimToString(o) ?? string.Empty).ToArray();
                default:
                    return new[] { ClaimToString(value) ?? string.Empty };
            }
        }

        /// <summary>
        /// Get user ID (sub claim)
        /// </summary>
        public string? UserId => GetClaim("sub");

        /// <summary>
        /// Get username (name claim)
        /// </summary>
        public string? Username => GetClaim("name");

        /// <summary>
        /// Get role (role claim)
        /// </summary>
        public string? Role => GetClaim("role");

        /// <summary>
        /// Get roles (roles claim)
        /// </summary>
        public string[] Roles => GetClaimArray("roles");

        internal static string? ClaimToString(object? value)
        {
            switch (value)
            {
                case null:
                    return null;
                case string s:
                    return s;
                case bool b:
                    return b ? "true" : "false";
                case string[] strings:
                    return string.Join(",", strings);
                case object[] objects:
                    return string.Join(",", objects.Select(ClaimToString));
                case IFormattable f:
                    return f.ToString(null, System.Globalization.CultureInfo.InvariantCulture);
                default:
                    return value.ToString();
            }
        }

        public static JwtValidationResult Valid(Dictionary<string, object?> claims)
        {
            return new JwtValidationResult { IsValid = true, Claims = claims };
        }

        public static JwtValidationResult Invalid(string errorMessage)
        {
            return new JwtValidationResult { IsValid = false, ErrorMessage = errorMessage };
        }
    }
}
