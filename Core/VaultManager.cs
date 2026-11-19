using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace USBAuthGuard.Core;

/// <summary>
/// Encrypts and decrypts files in a user-selected vault folder using AES-256-CBC.
/// The encryption key is derived from the trusted USB device's serial number
/// via PBKDF2 (SHA-256, 100,000 iterations).
///
/// Encrypted files have a ".vlt" extension appended.
/// The original file is overwritten — this operation is reversible as long as
/// you have the trusted USB present to decrypt.
///
/// NOTE: This encrypts user-owned files on user's own machine only.
/// </summary>
public sealed class VaultManager
{
    private const string EncryptedExtension = ".vlt";
    private const int    SaltSize           = 16;
    private const int    IvSize             = 16;
    private const int    KeySize            = 32; // 256-bit
    private const int    Pbkdf2Iterations   = 100_000;

    private readonly ILogger<VaultManager> _logger;

    public VaultManager(ILogger<VaultManager> logger)
    {
        _logger = logger;
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Encrypts all non-vault files in <paramref name="folderPath"/>.
    /// Each file becomes <c>filename.ext.vlt</c> and the original is removed.
    /// </summary>
    public VaultOperationResult LockVault(string folderPath, string deviceKey)
    {
        return ProcessFolder(folderPath, deviceKey, encrypt: true);
    }

    /// <summary>
    /// Decrypts all .vlt files in <paramref name="folderPath"/>.
    /// Each .vlt file is restored to its original name.
    /// </summary>
    public VaultOperationResult UnlockVault(string folderPath, string deviceKey)
    {
        return ProcessFolder(folderPath, deviceKey, encrypt: false);
    }

    // ── Private ────────────────────────────────────────────────────────────

    private VaultOperationResult ProcessFolder(string folderPath, string deviceKey, bool encrypt)
    {
        var result = new VaultOperationResult();

        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            _logger.LogWarning("VaultManager: folder not found — {Path}", folderPath);
            result.Errors.Add($"Folder not found: {folderPath}");
            return result;
        }

        var pattern = encrypt ? "*.*" : $"*{EncryptedExtension}";
        var files   = Directory.GetFiles(folderPath, pattern, SearchOption.TopDirectoryOnly);

        foreach (var file in files)
        {
            // Skip already-encrypted files during lock, skip non-vault files during unlock
            if (encrypt && file.EndsWith(EncryptedExtension, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                if (encrypt)
                    EncryptFile(file, deviceKey);
                else
                    DecryptFile(file, deviceKey);

                result.ProcessedFiles++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "VaultManager: failed to process file {File}", file);
                result.Errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }

        _logger.LogInformation(
            "VaultManager: {Op} complete — {Count} file(s), {Err} error(s).",
            encrypt ? "Lock" : "Unlock", result.ProcessedFiles, result.Errors.Count);

        return result;
    }

    private void EncryptFile(string inputPath, string deviceKey)
    {
        var outputPath = inputPath + EncryptedExtension;

        // Generate random salt + IV
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var iv   = RandomNumberGenerator.GetBytes(IvSize);
        var key  = DeriveKey(deviceKey, salt);

        var plainBytes = File.ReadAllBytes(inputPath);

        using var aes = Aes.Create();
        aes.Key  = key;
        aes.IV   = iv;
        aes.Mode = CipherMode.CBC;

        using var ms        = new MemoryStream();
        ms.Write(salt);                          // bytes  0–15
        ms.Write(iv);                            // bytes 16–31

        using var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write);
        cs.Write(plainBytes);
        cs.FlushFinalBlock();

        File.WriteAllBytes(outputPath, ms.ToArray());
        File.Delete(inputPath);
    }

    private void DecryptFile(string inputPath, string deviceKey)
    {
        var outputPath = inputPath[..^EncryptedExtension.Length]; // strip .vlt

        var fileBytes = File.ReadAllBytes(inputPath);

        if (fileBytes.Length < SaltSize + IvSize)
            throw new InvalidDataException("File is too small to be a valid vault file.");

        var salt      = fileBytes[..SaltSize];
        var iv        = fileBytes[SaltSize..(SaltSize + IvSize)];
        var encrypted = fileBytes[(SaltSize + IvSize)..];
        var key       = DeriveKey(deviceKey, salt);

        using var aes = Aes.Create();
        aes.Key  = key;
        aes.IV   = iv;
        aes.Mode = CipherMode.CBC;

        using var ms   = new MemoryStream(encrypted);
        using var cs   = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
        using var plain = new MemoryStream();
        cs.CopyTo(plain);

        File.WriteAllBytes(outputPath, plain.ToArray());
        File.Delete(inputPath);
    }

    private static byte[] DeriveKey(string password, byte[] salt)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(
            Encoding.UTF8.GetBytes(password),
            salt,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(KeySize);
    }
}

/// <summary>Result of a vault lock/unlock operation.</summary>
public sealed class VaultOperationResult
{
    public int ProcessedFiles { get; set; }
    public List<string> Errors { get; } = [];
    public bool Success => Errors.Count == 0;
}
