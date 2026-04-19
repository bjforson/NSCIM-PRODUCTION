using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Services.Settings
{
    /// <summary>
    /// Service for encrypting/decrypting sensitive settings using AES encryption
    /// </summary>
    public class SettingsEncryptionService : ISettingsEncryptionService
    {
        private readonly ILogger<SettingsEncryptionService> _logger;
        private readonly byte[] _encryptionKey;
        private readonly byte[] _iv;
        private const string ENCRYPTED_PREFIX = "ENC:";

        public SettingsEncryptionService(IConfiguration configuration, ILogger<SettingsEncryptionService> logger)
        {
            _logger = logger;

            // Get encryption key from configuration or environment variable
            var keyString = configuration["Settings:EncryptionKey"]
                          ?? Environment.GetEnvironmentVariable("NICKSCAN_SETTINGS_ENCRYPTION_KEY")
                          ?? "NickScanCentralImagingPortal2025DefaultKey!!"; // Fallback (should be replaced in production)

            // Derive 256-bit key from the key string
            using var sha256 = SHA256.Create();
            _encryptionKey = sha256.ComputeHash(Encoding.UTF8.GetBytes(keyString));

            // Use first 16 bytes of key as IV (in production, use separate IV)
            _iv = _encryptionKey.Take(16).ToArray();
        }

        public async Task<string> EncryptAsync(string plainText)
        {
            try
            {
                if (string.IsNullOrEmpty(plainText))
                    return plainText;

                using var aes = Aes.Create();
                aes.Key = _encryptionKey;
                aes.IV = _iv;

                using var encryptor = aes.CreateEncryptor();
                using var ms = new MemoryStream();
                using var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write);
                using var sw = new StreamWriter(cs);

                await sw.WriteAsync(plainText);
                await sw.FlushAsync();
                cs.FlushFinalBlock();

                var encrypted = Convert.ToBase64String(ms.ToArray());
                return ENCRYPTED_PREFIX + encrypted;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error encrypting value");
                throw;
            }
        }

        public async Task<string> DecryptAsync(string encryptedText)
        {
            try
            {
                if (string.IsNullOrEmpty(encryptedText))
                    return encryptedText;

                if (!IsEncrypted(encryptedText))
                    return encryptedText; // Not encrypted, return as-is

                // Remove prefix
                var base64 = encryptedText.Substring(ENCRYPTED_PREFIX.Length);
                var buffer = Convert.FromBase64String(base64);

                using var aes = Aes.Create();
                aes.Key = _encryptionKey;
                aes.IV = _iv;

                using var decryptor = aes.CreateDecryptor();
                using var ms = new MemoryStream(buffer);
                using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
                using var sr = new StreamReader(cs);

                return await sr.ReadToEndAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error decrypting value");
                throw;
            }
        }

        public bool IsEncrypted(string value)
        {
            return value?.StartsWith(ENCRYPTED_PREFIX) == true;
        }
    }
}

