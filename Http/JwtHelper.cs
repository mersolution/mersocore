using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace mersolutionCore.Http
{
    /// <summary>
    /// JWT (JSON Web Token) helper for token generation and validation
    /// </summary>
    public class JwtHelper
    {
        private readonly string _secretKey;
        private readonly string _issuer;
        private readonly string _audience;
        private readonly int _expirationMinutes;

        /// <summary>
        /// Create JWT helper with secret key
        /// </summary>
        /// <param name="secretKey">Secret key for signing (min 32 characters recommended)</param>
        /// <param name="issuer">Token issuer (optional)</param>
        /// <param name="audience">Token audience (optional)</param>
        /// <param name="expirationMinutes">Token expiration in minutes (default: 60)</param>
        public JwtHelper(string secretKey, string issuer = null, string audience = null, int expirationMinutes = 60)
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
        public string GenerateToken(Dictionary<string, object> claims)
        {
            var header = new Dictionary<string, object>
            {
                { "alg", "HS256" },
                { "typ", "JWT" }
            };

            var payload = new Dictionary<string, object>(claims);

            // Add standard claims
            var now = DateTimeOffset.UtcNow;
            payload["iat"] = now.ToUnixTimeSeconds();
            payload["exp"] = now.AddMinutes(_expirationMinutes).ToUnixTimeSeconds();
            payload["nbf"] = now.ToUnixTimeSeconds();

            if (!string.IsNullOrEmpty(_issuer))
                payload["iss"] = _issuer;

            if (!string.IsNullOrEmpty(_audience))
                payload["aud"] = _audience;

            // Encode header and payload
            var headerJson = SimpleJsonSerialize(header);
            var payloadJson = SimpleJsonSerialize(payload);

            var headerBase64 = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
            var payloadBase64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));

            // Create signature
            var signatureInput = $"{headerBase64}.{payloadBase64}";
            var signature = ComputeHmacSha256(signatureInput, _secretKey);
            var signatureBase64 = Base64UrlEncode(signature);

            return $"{headerBase64}.{payloadBase64}.{signatureBase64}";
        }

        /// <summary>
        /// Generate JWT token with user ID and role
        /// </summary>
        public string GenerateToken(string userId, string username, string role = null)
        {
            var claims = new Dictionary<string, object>
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
            var claims = new Dictionary<string, object>
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
        /// <returns>Token claims if valid, null if invalid</returns>
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

                // Verify signature
                var signatureInput = $"{headerBase64}.{payloadBase64}";
                var expectedSignature = Base64UrlEncode(ComputeHmacSha256(signatureInput, _secretKey));

                if (signatureBase64 != expectedSignature)
                    return JwtValidationResult.Invalid("Invalid signature");

                // Decode payload
                var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(payloadBase64));
                var claims = SimpleJsonDeserialize(payloadJson);

                // Check expiration
                if (claims.ContainsKey("exp"))
                {
                    var exp = Convert.ToInt64(claims["exp"]);
                    var expDate = DateTimeOffset.FromUnixTimeSeconds(exp);
                    if (expDate < DateTimeOffset.UtcNow)
                        return JwtValidationResult.Invalid("Token has expired");
                }

                // Check not before
                if (claims.ContainsKey("nbf"))
                {
                    var nbf = Convert.ToInt64(claims["nbf"]);
                    var nbfDate = DateTimeOffset.FromUnixTimeSeconds(nbf);
                    if (nbfDate > DateTimeOffset.UtcNow)
                        return JwtValidationResult.Invalid("Token is not yet valid");
                }

                // Check issuer
                if (!string.IsNullOrEmpty(_issuer) && claims.ContainsKey("iss"))
                {
                    if (claims["iss"].ToString() != _issuer)
                        return JwtValidationResult.Invalid("Invalid issuer");
                }

                // Check audience
                if (!string.IsNullOrEmpty(_audience) && claims.ContainsKey("aud"))
                {
                    if (claims["aud"].ToString() != _audience)
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
        /// Get claim value from token without full validation
        /// </summary>
        public string GetClaimValue(string token, string claimName)
        {
            try
            {
                var parts = token.Split('.');
                if (parts.Length != 3) return null;

                var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
                var claims = SimpleJsonDeserialize(payloadJson);

                return claims.ContainsKey(claimName) ? claims[claimName]?.ToString() : null;
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

                var exp = Convert.ToInt64(expStr);
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
            var claims = new Dictionary<string, object>(result.Claims);
            claims.Remove("iat");
            claims.Remove("exp");
            claims.Remove("nbf");
            claims.Remove("jti");

            // Generate new jti
            claims["jti"] = Guid.NewGuid().ToString();

            return GenerateToken(claims);
        }

        #region Helper Methods

        private byte[] ComputeHmacSha256(string data, string key)
        {
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key)))
            {
                return hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
            }
        }

        private string Base64UrlEncode(byte[] data)
        {
            return Convert.ToBase64String(data)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private byte[] Base64UrlDecode(string base64Url)
        {
            var base64 = base64Url
                .Replace('-', '+')
                .Replace('_', '/');

            switch (base64.Length % 4)
            {
                case 2: base64 += "=="; break;
                case 3: base64 += "="; break;
            }

            return Convert.FromBase64String(base64);
        }

        // Simple JSON serializer (no external dependencies)
        private string SimpleJsonSerialize(Dictionary<string, object> dict)
        {
            var sb = new StringBuilder("{");
            bool first = true;

            foreach (var kvp in dict)
            {
                if (!first) sb.Append(",");
                first = false;

                sb.Append($"\"{kvp.Key}\":");

                if (kvp.Value == null)
                {
                    sb.Append("null");
                }
                else if (kvp.Value is string str)
                {
                    sb.Append($"\"{EscapeJsonString(str)}\"");
                }
                else if (kvp.Value is bool b)
                {
                    sb.Append(b ? "true" : "false");
                }
                else if (kvp.Value is int || kvp.Value is long || kvp.Value is double || kvp.Value is decimal)
                {
                    sb.Append(kvp.Value);
                }
                else if (kvp.Value is string[] arr)
                {
                    sb.Append("[");
                    for (int i = 0; i < arr.Length; i++)
                    {
                        if (i > 0) sb.Append(",");
                        sb.Append($"\"{EscapeJsonString(arr[i])}\"");
                    }
                    sb.Append("]");
                }
                else
                {
                    sb.Append($"\"{EscapeJsonString(kvp.Value.ToString())}\"");
                }
            }

            sb.Append("}");
            return sb.ToString();
        }

        private string EscapeJsonString(string str)
        {
            return str
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }

        // Simple JSON deserializer
        private Dictionary<string, object> SimpleJsonDeserialize(string json)
        {
            var result = new Dictionary<string, object>();
            json = json.Trim();

            if (!json.StartsWith("{") || !json.EndsWith("}"))
                throw new FormatException("Invalid JSON format");

            json = json.Substring(1, json.Length - 2).Trim();

            if (string.IsNullOrEmpty(json))
                return result;

            int i = 0;
            while (i < json.Length)
            {
                // Skip whitespace
                while (i < json.Length && char.IsWhiteSpace(json[i])) i++;

                // Parse key
                if (json[i] != '"') break;
                i++;
                int keyStart = i;
                while (i < json.Length && json[i] != '"') i++;
                string key = json.Substring(keyStart, i - keyStart);
                i++;

                // Skip colon
                while (i < json.Length && (char.IsWhiteSpace(json[i]) || json[i] == ':')) i++;

                // Parse value
                object value = null;
                if (json[i] == '"')
                {
                    i++;
                    int valueStart = i;
                    while (i < json.Length && json[i] != '"')
                    {
                        if (json[i] == '\\') i++;
                        i++;
                    }
                    value = json.Substring(valueStart, i - valueStart);
                    i++;
                }
                else if (json[i] == '[')
                {
                    // Skip arrays for simplicity
                    int depth = 1;
                    i++;
                    int arrStart = i;
                    while (i < json.Length && depth > 0)
                    {
                        if (json[i] == '[') depth++;
                        else if (json[i] == ']') depth--;
                        i++;
                    }
                    value = json.Substring(arrStart, i - arrStart - 1);
                }
                else if (json.Substring(i).StartsWith("null"))
                {
                    value = null;
                    i += 4;
                }
                else if (json.Substring(i).StartsWith("true"))
                {
                    value = true;
                    i += 4;
                }
                else if (json.Substring(i).StartsWith("false"))
                {
                    value = false;
                    i += 5;
                }
                else
                {
                    // Number
                    int numStart = i;
                    while (i < json.Length && (char.IsDigit(json[i]) || json[i] == '.' || json[i] == '-'))
                        i++;
                    value = json.Substring(numStart, i - numStart);
                }

                result[key] = value;

                // Skip comma
                while (i < json.Length && (char.IsWhiteSpace(json[i]) || json[i] == ',')) i++;
            }

            return result;
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
        public string ErrorMessage { get; private set; }

        /// <summary>
        /// Token claims if valid
        /// </summary>
        public Dictionary<string, object> Claims { get; private set; }

        /// <summary>
        /// Get claim value
        /// </summary>
        public string GetClaim(string name)
        {
            return Claims?.ContainsKey(name) == true ? Claims[name]?.ToString() : null;
        }

        /// <summary>
        /// Get user ID (sub claim)
        /// </summary>
        public string UserId => GetClaim("sub");

        /// <summary>
        /// Get username (name claim)
        /// </summary>
        public string Username => GetClaim("name");

        /// <summary>
        /// Get role (role claim)
        /// </summary>
        public string Role => GetClaim("role");

        public static JwtValidationResult Valid(Dictionary<string, object> claims)
        {
            return new JwtValidationResult { IsValid = true, Claims = claims };
        }

        public static JwtValidationResult Invalid(string errorMessage)
        {
            return new JwtValidationResult { IsValid = false, ErrorMessage = errorMessage };
        }
    }
}
