using System;
using System.Globalization;

namespace mersolutionCore.ORM
{
    /// <summary>
    /// Converts a property value to the value stored in its column and back. Put it on a property with
    /// <c>[Converter(typeof(MyConverter))]</c>; the converter needs a public parameterless constructor.
    /// </summary>
    public interface IValueConverter
    {
        /// <summary>Property value → column value (parameters of INSERT / UPDATE / WHERE)</summary>
        object? ToDatabase(object? value);

        /// <summary>Column value (never DBNull) → property value of <paramref name="propertyType"/></summary>
        object? FromDatabase(object? value, Type propertyType);
    }

    /// <summary>
    /// Store this property through an <see cref="IValueConverter"/>. The property is a column even when its type
    /// is complex (a list or class for JSON). Where("Column", value) converts the value the same way.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public class ConverterAttribute : Attribute
    {
        public Type ConverterType { get; }

        public ConverterAttribute(Type converterType)
        {
            if (converterType == null) throw new ArgumentNullException(nameof(converterType));
            if (!typeof(IValueConverter).IsAssignableFrom(converterType))
                throw new ArgumentException($"{converterType.Name} does not implement IValueConverter.", nameof(converterType));
            ConverterType = converterType;
        }
    }

    /// <summary>
    /// Store an enum by its name ("Active") instead of its number
    /// </summary>
    public class EnumAsStringAttribute : ConverterAttribute
    {
        public EnumAsStringAttribute() : base(typeof(EnumStringConverter)) { }
    }

    /// <summary>
    /// Store a string encrypted (AES-256 + HMAC-SHA256, random IV per value) with
    /// <see cref="EncryptedStringConverter.Key"/>. Encrypted columns cannot be searched with Where.
    /// </summary>
    public class EncryptedAttribute : ConverterAttribute
    {
        public EncryptedAttribute() : base(typeof(EncryptedStringConverter)) { }
    }

    /// <summary>
    /// Enum ↔ its name (numbers stored earlier are still read)
    /// </summary>
    public sealed class EnumStringConverter : IValueConverter
    {
        public object? ToDatabase(object? value)
        {
            return value is Enum e ? e.ToString() : value;
        }

        public object? FromDatabase(object? value, Type propertyType)
        {
            if (value == null)
                return null;

            var type = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
            if (!type.IsEnum)
                return value;

            if (value is string text)
            {
                return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                    ? Enum.ToObject(type, number)
                    : Enum.Parse(type, text.Trim(), true);
            }
            return Enum.ToObject(type, Convert.ChangeType(value, Enum.GetUnderlyingType(type), CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// Any value ↔ JSON text (System.Text.Json, camelCase); a string property keeps its text as it is
    /// </summary>
    public sealed class JsonValueConverter : IValueConverter
    {
        public object? ToDatabase(object? value)
        {
            if (value == null || value is string)
                return value;
            return JsonColumnHelper.Serialize(value);
        }

        public object? FromDatabase(object? value, Type propertyType)
        {
            if (value == null)
                return null;

            var text = value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture);
            if (propertyType == typeof(string) || string.IsNullOrEmpty(text))
                return text;
            return JsonColumnHelper.Deserialize(text, propertyType);
        }
    }

    /// <summary>
    /// String ↔ encrypted text: AES-256-CBC with a random IV per value + HMAC-SHA256 (tampering is detected).
    /// The keys are derived from <see cref="Key"/> once (PBKDF2), so reading many rows stays fast.
    /// Set <see cref="Key"/> once at startup and keep it out of source control.
    /// </summary>
    public sealed class EncryptedStringConverter : IValueConverter
    {
        private const string Prefix = "mc1:";
        private static readonly byte[] Salt = System.Text.Encoding.UTF8.GetBytes("mersolutionCore.Encrypted.v1");
        private static readonly object _lock = new object();
        private static string? _derivedFor;
        private static byte[]? _encryptionKey;
        private static byte[]? _macKey;
        private static string? _key;

        /// <summary>
        /// Password of [Encrypted] columns (required)
        /// </summary>
        public static string? Key
        {
            get => _key;
            set => _key = value;
        }

        // (encryption key, mac key) for the current Key, derived once
        private static void Keys(out byte[] encryption, out byte[] mac)
        {
            var key = _key;
            if (string.IsNullOrEmpty(key))
                throw new InvalidOperationException("Set EncryptedStringConverter.Key before using [Encrypted] properties.");

            lock (_lock)
            {
                if (_derivedFor != key)
                {
#pragma warning disable SYSLIB0041 // SHA1 PBKDF2: the netstandard2.0 API; runs once per key
                    using (var kdf = new System.Security.Cryptography.Rfc2898DeriveBytes(key!, Salt, 100000))
#pragma warning restore SYSLIB0041
                    {
                        _encryptionKey = kdf.GetBytes(32);
                        _macKey = kdf.GetBytes(32);
                    }
                    _derivedFor = key;
                }
                encryption = _encryptionKey!;
                mac = _macKey!;
            }
        }

        public object? ToDatabase(object? value)
        {
            if (value == null)
                return null;

            Keys(out var encryptionKey, out var macKey);
            var plain = System.Text.Encoding.UTF8.GetBytes(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);

            using (var aes = System.Security.Cryptography.Aes.Create())
            {
                aes.Key = encryptionKey;
                aes.GenerateIV();
                byte[] cipher;
                using (var encryptor = aes.CreateEncryptor())
                    cipher = encryptor.TransformFinalBlock(plain, 0, plain.Length);

                var payload = new byte[16 + cipher.Length + 32];
                Buffer.BlockCopy(aes.IV, 0, payload, 0, 16);
                Buffer.BlockCopy(cipher, 0, payload, 16, cipher.Length);
                using (var hmac = new System.Security.Cryptography.HMACSHA256(macKey))
                {
                    var tag = hmac.ComputeHash(payload, 0, 16 + cipher.Length);
                    Buffer.BlockCopy(tag, 0, payload, 16 + cipher.Length, 32);
                }
                return Prefix + Convert.ToBase64String(payload);
            }
        }

        public object? FromDatabase(object? value, Type propertyType)
        {
            if (value == null)
                return null;

            var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            if (!text.StartsWith(Prefix, StringComparison.Ordinal))
                throw new InvalidOperationException("An [Encrypted] column holds a value that was not written by EncryptedStringConverter.");

            Keys(out var encryptionKey, out var macKey);
            var payload = Convert.FromBase64String(text.Substring(Prefix.Length));
            if (payload.Length < 16 + 16 + 32)
                throw new InvalidOperationException("An [Encrypted] column could not be decrypted (changed data).");

            var cipherLength = payload.Length - 16 - 32;
            using (var hmac = new System.Security.Cryptography.HMACSHA256(macKey))
            {
                var expected = hmac.ComputeHash(payload, 0, 16 + cipherLength);
                var diff = 0;
                for (int i = 0; i < 32; i++)
                    diff |= expected[i] ^ payload[16 + cipherLength + i];
                if (diff != 0)
                    throw new InvalidOperationException("An [Encrypted] column could not be decrypted (wrong key or changed data).");
            }

            using (var aes = System.Security.Cryptography.Aes.Create())
            {
                aes.Key = encryptionKey;
                var iv = new byte[16];
                Buffer.BlockCopy(payload, 0, iv, 0, 16);
                aes.IV = iv;
                using (var decryptor = aes.CreateDecryptor())
                {
                    var plain = decryptor.TransformFinalBlock(payload, 16, cipherLength);
                    return System.Text.Encoding.UTF8.GetString(plain);
                }
            }
        }
    }
}
