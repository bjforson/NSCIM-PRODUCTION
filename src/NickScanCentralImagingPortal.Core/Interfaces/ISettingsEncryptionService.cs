namespace NickScanCentralImagingPortal.Core.Interfaces
{
    /// <summary>
    /// Service for encrypting/decrypting sensitive settings values
    /// </summary>
    public interface ISettingsEncryptionService
    {
        /// <summary>
        /// Encrypt a sensitive value
        /// </summary>
        Task<string> EncryptAsync(string plainText);

        /// <summary>
        /// Decrypt an encrypted value
        /// </summary>
        Task<string> DecryptAsync(string encryptedText);

        /// <summary>
        /// Check if a value is encrypted
        /// </summary>
        bool IsEncrypted(string value);
    }
}

