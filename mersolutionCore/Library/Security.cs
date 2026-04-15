using System;
using System.Security.Cryptography;
using System.Text;

namespace mersolutionCore.Library
{
    /// <summary>
    /// Security utilities for encryption and code generation
    /// </summary>
    public class Security
    {
        /// <summary>
        /// Convert byte array to hex string
        /// </summary>
        private static string ByteArrayToHexString(byte[] bytes)
        {
            StringBuilder sb = new StringBuilder();
            foreach (byte item in bytes)
                sb.AppendFormat("{0:x2}", item);
            return sb.ToString();
        }

        /// <summary>
        /// ROT13 encryption/decryption
        /// </summary>
        /// <param name="value">Value to encode/decode</param>
        /// <returns>ROT13 encoded/decoded string</returns>
        public static string ROT13(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            char[] array = value.ToCharArray();

            for (int i = 0; i < array.Length; i++)
            {
                int number = (int)array[i];

                if (number >= 'a' && number <= 'z')
                {
                    if (number > 'm')
                        number -= 13;
                    else
                        number += 13;
                }
                else if (number >= 'A' && number <= 'Z')
                {
                    if (number > 'M')
                        number -= 13;
                    else
                        number += 13;
                }

                array[i] = (char)number;
            }

            return new string(array);
        }

        /// <summary>
        /// Compute HMAC-SHA1 hash
        /// </summary>
        /// <param name="value">Value to hash</param>
        /// <param name="key">Secret key</param>
        /// <returns>Hex encoded hash</returns>
        public static string ComputeHmacSha1(string value, string key)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));

            byte[] keyBytes = Encoding.ASCII.GetBytes(key);
            byte[] dataBytes = Encoding.UTF8.GetBytes(value);

            using (HMACSHA1 algorithm = new HMACSHA1(keyBytes))
            {
                byte[] hash = algorithm.ComputeHash(dataBytes);
                return ByteArrayToHexString(hash);
            }
        }

        /// <summary>
        /// Compute HMAC-SHA256 hash
        /// </summary>
        /// <param name="value">Value to hash</param>
        /// <param name="key">Secret key</param>
        /// <returns>Hex encoded hash</returns>
        public static string ComputeHmacSha256(string value, string key)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));

            byte[] keyBytes = Encoding.UTF8.GetBytes(key);
            byte[] dataBytes = Encoding.UTF8.GetBytes(value);

            using (HMACSHA256 algorithm = new HMACSHA256(keyBytes))
            {
                byte[] hash = algorithm.ComputeHash(dataBytes);
                return ByteArrayToHexString(hash);
            }
        }

        /// <summary>
        /// Create MD5 hash
        /// </summary>
        /// <param name="value">Value to hash</param>
        /// <returns>Hex encoded MD5 hash</returns>
        public static string MD5Hash(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            using (MD5 md5 = MD5.Create())
            {
                byte[] data = md5.ComputeHash(Encoding.UTF8.GetBytes(value));
                StringBuilder builder = new StringBuilder();

                for (int i = 0; i < data.Length; i++)
                    builder.Append(data[i].ToString("x2"));

                return builder.ToString();
            }
        }

        /// <summary>
        /// Create SHA256 hash
        /// </summary>
        /// <param name="value">Value to hash</param>
        /// <returns>Hex encoded SHA256 hash</returns>
        public static string SHA256Hash(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] data = sha256.ComputeHash(Encoding.UTF8.GetBytes(value));
                StringBuilder builder = new StringBuilder();

                for (int i = 0; i < data.Length; i++)
                    builder.Append(data[i].ToString("x2"));

                return builder.ToString();
            }
        }

        /// <summary>
        /// Generate random alphanumeric code
        /// </summary>
        /// <param name="length">Code length</param>
        /// <returns>Random code</returns>
        public static string GenerateRandomCode(int length)
        {
            const string characters = "ABCDEFGHJKLMNPQRSTUVXWYZ123456789";
            StringBuilder code = new StringBuilder();

            using (var rng = RandomNumberGenerator.Create())
            {
                byte[] data = new byte[length];
                rng.GetBytes(data);

                for (int i = 0; i < length; i++)
                {
                    code.Append(characters[data[i] % characters.Length]);
                }
            }

            return code.ToString();
        }

        /// <summary>
        /// Generate random alphanumeric code with custom characters
        /// </summary>
        /// <param name="length">Code length</param>
        /// <param name="characters">Character set to use</param>
        /// <returns>Random code</returns>
        public static string GenerateRandomCode(int length, string characters)
        {
            if (string.IsNullOrEmpty(characters))
                throw new ArgumentNullException(nameof(characters));

            StringBuilder code = new StringBuilder();

            using (var rng = RandomNumberGenerator.Create())
            {
                byte[] data = new byte[length];
                rng.GetBytes(data);

                for (int i = 0; i < length; i++)
                {
                    code.Append(characters[data[i] % characters.Length]);
                }
            }

            return code.ToString();
        }

        /// <summary>
        /// Generate cryptographically secure random bytes
        /// </summary>
        /// <param name="length">Number of bytes</param>
        /// <returns>Random bytes</returns>
        public static byte[] GenerateRandomBytes(int length)
        {
            byte[] data = new byte[length];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(data);
            }
            return data;
        }

        /// <summary>
        /// Generate GUID string
        /// </summary>
        /// <returns>New GUID string</returns>
        public static string GenerateGuid()
        {
            return Guid.NewGuid().ToString();
        }

        /// <summary>
        /// Generate GUID string without dashes
        /// </summary>
        /// <returns>New GUID string without dashes</returns>
        public static string GenerateGuidNoDashes()
        {
            return Guid.NewGuid().ToString("N");
        }
    }
}
