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
            try
            {
                if (_cryptoService.LegalKeySizes.Length > 0)
                {
                    int keySize = key.Length * 8;
                    int minSize = _cryptoService.LegalKeySizes[0].MinSize;
                    int maxSize = _cryptoService.LegalKeySizes[0].MaxSize;
                    int skipSize = _cryptoService.LegalKeySizes[0].SkipSize;

                    if (keySize > maxSize)
                    {
                        key = key.Substring(0, maxSize / 8);
                    }
                    else if (keySize < maxSize)
                    {
                        int validSize = (keySize <= minSize) ? minSize : (keySize - keySize % skipSize) + skipSize;
                        if (keySize < validSize)
                        {
                            key = key.PadRight((validSize / 8) - (Encoding.UTF8.GetByteCount(key) - key.Length), '*');
                        }
                    }
                }

                return Encoding.UTF8.GetBytes(key);
            }
            catch
            {
                return null;
            }
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
        public virtual string Decrypt(string cryptoText, string key)
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
        public virtual byte[] Decrypt(byte[] cryptoByte, string key)
        {
            byte[] keyByte = GetLegalKey(key);

            _cryptoService.Key = keyByte;
            SetLegalIV();

            ICryptoTransform cryptoTransform = _cryptoService.CreateDecryptor();
            try
            {
                using (MemoryStream ms = new MemoryStream(cryptoByte, 0, cryptoByte.Length))
                using (CryptoStream cs = new CryptoStream(ms, cryptoTransform, CryptoStreamMode.Read))
                {
                    byte[] value = new byte[ms.Length];
                    cs.Read(value, 0, value.Length);
                    return value;
                }
            }
            catch
            {
                return null;
            }
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
