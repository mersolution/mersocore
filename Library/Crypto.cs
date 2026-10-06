using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace mersolutionCore.Library
{
    /// <summary>
    /// Cryptography service for encryption and decryption
    /// </summary>
    public class Crypto : IDisposable
    {
        private SymmetricAlgorithm _cryptoService;
        private readonly CryptoProvider _provider;
        private bool _disposed = false;

        /// <summary>
        /// Supported encryption providers
        /// </summary>
        public enum CryptoProvider
        {
            Aes,
            TripleDES
        }

        /// <summary>
        /// Create crypto service with default provider (AES)
        /// </summary>
        public Crypto()
        {
            _cryptoService = Aes.Create();
            _provider = CryptoProvider.Aes;
        }

        /// <summary>
        /// Create crypto service with specified provider
        /// </summary>
        /// <param name="provider">Encryption provider</param>
        public Crypto(CryptoProvider provider)
        {
            _provider = provider;
            switch (provider)
            {
                case CryptoProvider.Aes:
                    _cryptoService = Aes.Create();
                    break;
                case CryptoProvider.TripleDES:
                    _cryptoService = TripleDES.Create();
                    break;
                default:
                    _cryptoService = Aes.Create();
                    break;
            }
        }

        private void SetLegalIV()
        {
            switch (_provider)
            {
                case CryptoProvider.Aes:
                    _cryptoService.IV = new byte[] { 0xb, 0x6e, 0x13, 0x2e, 0x31, 0xd2, 0xcd, 0xf7, 0x5, 0x36, 0x9c, 0xea, 0xa8, 0x4c, 0x63, 0xcc };
                    break;
                default:
                    _cryptoService.IV = new byte[] { 0xb, 0x6e, 0x13, 0x2e, 0x31, 0xd2, 0xcd, 0xf7 };
                    break;
            }
        }

        /// <summary>
        /// Get legal key bytes from string key
        /// </summary>
        /// <param name="key">String key</param>
        /// <returns>Byte array key</returns>
        public virtual byte[] GetLegalKey(string key)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            // Works on UTF-8 bytes (the old char-based sizing produced illegal key lengths for
            // non-ASCII keys such as "ğüşİ..."); for ASCII keys the result is byte-for-byte unchanged.
            var bytes = Encoding.UTF8.GetBytes(key);
            if (_cryptoService.LegalKeySizes.Length == 0)
                return bytes;

            int keySize = bytes.Length * 8;
            int minSize = _cryptoService.LegalKeySizes[0].MinSize;
            int maxSize = _cryptoService.LegalKeySizes[0].MaxSize;
            int skipSize = _cryptoService.LegalKeySizes[0].SkipSize;

            int targetSize = keySize;
            if (keySize > maxSize)
            {
                targetSize = maxSize;
            }
            else if (keySize < maxSize)
            {
                int validSize = (keySize <= minSize) ? minSize : (keySize - keySize % skipSize) + skipSize;
                if (keySize < validSize)
                    targetSize = validSize;
            }

            var legal = new byte[targetSize / 8];
            var copy = Math.Min(bytes.Length, legal.Length);
            Array.Copy(bytes, legal, copy);
            for (int i = copy; i < legal.Length; i++)
                legal[i] = (byte)'*';

            return legal;
        }

        /// <summary>
        /// Encrypt string value
        /// </summary>
        /// <param name="plainText">Text to encrypt</param>
        /// <param name="key">Encryption key</param>
        /// <returns>Encrypted Base64 string</returns>
        public virtual string Encrypt(string plainText, string key)
        {
            try
            {
                byte[] plainByte = Encoding.UTF8.GetBytes(plainText);
                byte[] keyByte = GetLegalKey(key);

                _cryptoService.Key = keyByte;
                SetLegalIV();

                ICryptoTransform cryptoTransform = _cryptoService.CreateEncryptor();

                using (MemoryStream ms = new MemoryStream())
                using (CryptoStream cs = new CryptoStream(ms, cryptoTransform, CryptoStreamMode.Write))
                {
                    cs.Write(plainByte, 0, plainByte.Length);
                    cs.FlushFinalBlock();

                    byte[] cryptoByte = ms.ToArray();
                    return Convert.ToBase64String(cryptoByte, 0, cryptoByte.GetLength(0));
                }
            }
            catch (Exception ex)
            {
                throw new CryptographicException("Encryption failed", ex);
            }
        }

        /// <summary>
        /// Encrypt byte array
        /// </summary>
        /// <param name="plainByte">Bytes to encrypt</param>
        /// <param name="key">Encryption key</param>
        /// <returns>Encrypted byte array</returns>
        public virtual byte[] Encrypt(byte[] plainByte, string key)
        {
            byte[] keyByte = GetLegalKey(key);

            _cryptoService.Key = keyByte;
            SetLegalIV();

            ICryptoTransform cryptoTransform = _cryptoService.CreateEncryptor();

            using (MemoryStream ms = new MemoryStream())
            using (CryptoStream cs = new CryptoStream(ms, cryptoTransform, CryptoStreamMode.Write))
            {
                cs.Write(plainByte, 0, plainByte.Length);
                cs.FlushFinalBlock();

                return ms.ToArray();
            }
        }

        /// <summary>
        /// Decrypt encrypted string
        /// </summary>
        /// <param name="cryptoText">Encrypted Base64 string</param>
        /// <param name="key">Decryption key</param>
        /// <returns>Decrypted string</returns>
        public virtual string? Decrypt(string cryptoText, string key)
        {
            byte[] cryptoByte = Convert.FromBase64String(cryptoText);
            byte[] keyByte = GetLegalKey(key);

            _cryptoService.Key = keyByte;
            SetLegalIV();

            ICryptoTransform cryptoTransform = _cryptoService.CreateDecryptor();
            try
            {
                using (MemoryStream ms = new MemoryStream(cryptoByte, 0, cryptoByte.Length))
                using (CryptoStream cs = new CryptoStream(ms, cryptoTransform, CryptoStreamMode.Read))
                using (StreamReader sr = new StreamReader(cs))
                {
                    return sr.ReadToEnd();
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Decrypt encrypted byte array
        /// </summary>
        /// <param name="cryptoByte">Encrypted bytes</param>
        /// <param name="key">Decryption key</param>
        /// <returns>Decrypted byte array</returns>
        public virtual byte[]? Decrypt(byte[] cryptoByte, string key)
        {
            byte[] keyByte = GetLegalKey(key);

            _cryptoService.Key = keyByte;
            SetLegalIV();

            ICryptoTransform cryptoTransform = _cryptoService.CreateDecryptor();
            try
            {
                using (MemoryStream ms = new MemoryStream(cryptoByte, 0, cryptoByte.Length))
                using (CryptoStream cs = new CryptoStream(ms, cryptoTransform, CryptoStreamMode.Read))
                using (MemoryStream output = new MemoryStream())
                {
                    // Read to the end: a single Read() may return less and the old buffer kept
                    // trailing zero bytes (ciphertext length > plaintext length because of padding)
                    cs.CopyTo(output);
                    return output.ToArray();
                }
            }
            catch
            {
                return null;
            }
        }

        #region Authenticated encryption (recommended for new data)

        private const byte SecureFormatVersion = 2;
        private const int SaltSize = 16;
        private const int IvSize = 16;
        private const int MacSize = 32;
        private const int Pbkdf2Iterations = 100000;

        /// <summary>
        /// Encrypt with AES-256-CBC + HMAC-SHA256, random salt and IV, PBKDF2 key derivation.
        /// The same input gives a different output every time; tampering is detected on decrypt.
        /// (<see cref="Encrypt(string, string)"/> keeps the old fixed-IV format for existing data.)
        /// </summary>
        public static string EncryptSecure(string plainText, string password)
        {
            if (plainText == null) throw new ArgumentNullException(nameof(plainText));
            if (string.IsNullOrEmpty(password)) throw new ArgumentNullException(nameof(password));

            var salt = new byte[SaltSize];
            var iv = new byte[IvSize];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
                rng.GetBytes(iv);
            }

            DeriveKeys(password, salt, out var encKey, out var macKey);

            byte[] cipher;
            using (var aes = Aes.Create())
            {
                aes.Key = encKey;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                using (var encryptor = aes.CreateEncryptor())
                {
                    var plain = Encoding.UTF8.GetBytes(plainText);
                    cipher = encryptor.TransformFinalBlock(plain, 0, plain.Length);
                }
            }

            var payload = new byte[1 + SaltSize + IvSize + cipher.Length + MacSize];
            payload[0] = SecureFormatVersion;
            Buffer.BlockCopy(salt, 0, payload, 1, SaltSize);
            Buffer.BlockCopy(iv, 0, payload, 1 + SaltSize, IvSize);
            Buffer.BlockCopy(cipher, 0, payload, 1 + SaltSize + IvSize, cipher.Length);

            using (var hmac = new HMACSHA256(macKey))
            {
                var mac = hmac.ComputeHash(payload, 0, payload.Length - MacSize);
                Buffer.BlockCopy(mac, 0, payload, payload.Length - MacSize, MacSize);
            }

            return Convert.ToBase64String(payload);
        }

        /// <summary>
        /// Decrypt a value produced by <see cref="EncryptSecure"/>. Returns null when the password is wrong
        /// or the data was modified.
        /// </summary>
        public static string? DecryptSecure(string cipherText, string password)
        {
            if (string.IsNullOrEmpty(cipherText) || string.IsNullOrEmpty(password))
                return null;

            byte[] payload;
            try
            {
                payload = Convert.FromBase64String(cipherText);
            }
            catch (FormatException)
            {
                return null;
            }

            int cipherLength = payload.Length - 1 - SaltSize - IvSize - MacSize;
            if (cipherLength <= 0 || cipherLength % 16 != 0 || payload[0] != SecureFormatVersion)
                return null;

            var salt = new byte[SaltSize];
            var iv = new byte[IvSize];
            Buffer.BlockCopy(payload, 1, salt, 0, SaltSize);
            Buffer.BlockCopy(payload, 1 + SaltSize, iv, 0, IvSize);

            DeriveKeys(password, salt, out var encKey, out var macKey);

            using (var hmac = new HMACSHA256(macKey))
            {
                var expected = hmac.ComputeHash(payload, 0, payload.Length - MacSize);
                if (!FixedTimeEquals(expected, payload, payload.Length - MacSize))
                    return null;
            }

            try
            {
                using (var aes = Aes.Create())
                {
                    aes.Key = encKey;
                    aes.IV = iv;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    using (var decryptor = aes.CreateDecryptor())
                    {
                        var plain = decryptor.TransformFinalBlock(payload, 1 + SaltSize + IvSize, cipherLength);
                        return Encoding.UTF8.GetString(plain);
                    }
                }
            }
            catch (CryptographicException)
            {
                return null;
            }
        }

        private static void DeriveKeys(string password, byte[] salt, out byte[] encKey, out byte[] macKey)
        {
            // PBKDF2-HMAC-SHA1, 100k iterations, on every target: data encrypted by the netstandard2.0
            // build must decrypt under net8.0 and back (netstandard2.0 has no hash-algorithm overload)
#pragma warning disable SYSLIB0041
            using (var kdf = new Rfc2898DeriveBytes(password, salt, Pbkdf2Iterations))
#pragma warning restore SYSLIB0041
            {
                var material = kdf.GetBytes(64);
                encKey = new byte[32];
                macKey = new byte[32];
                Buffer.BlockCopy(material, 0, encKey, 0, 32);
                Buffer.BlockCopy(material, 32, macKey, 0, 32);
            }
        }

        private static bool FixedTimeEquals(byte[] expected, byte[] buffer, int offset)
        {
            int diff = 0;
            for (int i = 0; i < expected.Length; i++)
                diff |= expected[i] ^ buffer[offset + i];
            return diff == 0;
        }

        #endregion

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _cryptoService?.Dispose();
                }
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Hash service for one-way encryption
    /// </summary>
    public class Hash : IDisposable
    {
        private HashAlgorithm _hashService;
        private bool _disposed = false;

        /// <summary>
        /// Supported hash providers
        /// </summary>
        public enum HashProvider
        {
            SHA1,
            SHA256,
            SHA384,
            SHA512,
            MD5
        }

        /// <summary>
        /// Create hash service with default provider (SHA256)
        /// </summary>
        public Hash()
        {
            _hashService = SHA256.Create();
        }

        /// <summary>
        /// Create hash service with specified provider
        /// </summary>
        /// <param name="provider">Hash provider</param>
        public Hash(HashProvider provider)
        {
            switch (provider)
            {
                case HashProvider.MD5:
                    _hashService = MD5.Create();
                    break;
                case HashProvider.SHA1:
                    _hashService = SHA1.Create();
                    break;
                case HashProvider.SHA256:
                    _hashService = SHA256.Create();
                    break;
                case HashProvider.SHA384:
                    _hashService = SHA384.Create();
                    break;
                case HashProvider.SHA512:
                    _hashService = SHA512.Create();
                    break;
                default:
                    _hashService = SHA256.Create();
                    break;
            }
        }

        /// <summary>
        /// Compute hash of string
        /// </summary>
        /// <param name="plainText">Text to hash</param>
        /// <returns>Base64 encoded hash</returns>
        public virtual string ComputeHash(string plainText)
        {
            byte[] cryptoByte = _hashService.ComputeHash(Encoding.UTF8.GetBytes(plainText));
            return Convert.ToBase64String(cryptoByte, 0, cryptoByte.Length);
        }

        /// <summary>
        /// Compute hash of string and return hex string
        /// </summary>
        /// <param name="plainText">Text to hash</param>
        /// <returns>Hex encoded hash</returns>
        public virtual string ComputeHashHex(string plainText)
        {
            byte[] cryptoByte = _hashService.ComputeHash(Encoding.UTF8.GetBytes(plainText));
            StringBuilder sb = new StringBuilder();
            foreach (byte b in cryptoByte)
            {
                sb.Append(b.ToString("x2"));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Compute hash of byte array
        /// </summary>
        /// <param name="data">Data to hash</param>
        /// <returns>Hash bytes</returns>
        public virtual byte[] ComputeHash(byte[] data)
        {
            return _hashService.ComputeHash(data);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _hashService?.Dispose();
                }
                _disposed = true;
            }
        }
    }
}
