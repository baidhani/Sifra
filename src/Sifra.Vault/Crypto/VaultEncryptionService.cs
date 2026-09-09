using System.Security.Cryptography;
using System.Text;

namespace Sifra.Vault.Crypto;

/// <summary>
/// Derives the vault's encryption key from the same vault credential used
/// for authentication (STORY-013), but via a separate persisted salt so
/// the two derived outputs are cryptographically independent. Encrypts
/// with AES-GCM (established, authenticated encryption from the BCL — no
/// new crypto dependency). Reusable by any future story that needs to
/// encrypt vault content before it leaves the device (e.g. sync).
/// </summary>
public sealed class VaultEncryptionService
{
    private const int KeyLengthBytes = 32; // AES-256
    private const int NonceLengthBytes = 12;
    private const int TagLengthBytes = 16;
    private const int Pbkdf2Iterations = 210_000; // OWASP current minimum for PBKDF2-SHA256

    private readonly VaultEncryptionKeyStore _keyStore;

    public VaultEncryptionService(VaultEncryptionKeyStore keyStore)
    {
        _keyStore = keyStore;
    }

    /// <summary>
    /// Derives the encryption key for this vault credential. Stable across
    /// calls and process restarts as long as the persisted salt is
    /// unchanged — the same credential always yields the same key.
    /// </summary>
    public byte[] DeriveKey(string vaultCredential)
    {
        var salt = _keyStore.EnsureSalt();
        return Rfc2898DeriveBytes.Pbkdf2(vaultCredential, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, KeyLengthBytes);
    }

    /// <returns>Base64 of nonce || tag || ciphertext.</returns>
    public string Encrypt(string plaintext, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceLengthBytes);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagLengthBytes];

        using var aesGcm = new AesGcm(key, TagLengthBytes);
        aesGcm.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        var combined = new byte[NonceLengthBytes + TagLengthBytes + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, combined, 0, NonceLengthBytes);
        Buffer.BlockCopy(tag, 0, combined, NonceLengthBytes, TagLengthBytes);
        Buffer.BlockCopy(ciphertext, 0, combined, NonceLengthBytes + TagLengthBytes, ciphertext.Length);

        return Convert.ToBase64String(combined);
    }

    /// <exception cref="VaultDecryptionFailedException">
    /// The ciphertext could not be authenticated — usually the wrong key (wrong vault credential).
    /// </exception>
    public string Decrypt(string base64CipherBlob, byte[] key)
    {
        var combined = Convert.FromBase64String(base64CipherBlob);
        var nonce = combined[..NonceLengthBytes];
        var tag = combined[NonceLengthBytes..(NonceLengthBytes + TagLengthBytes)];
        var ciphertext = combined[(NonceLengthBytes + TagLengthBytes)..];
        var plaintextBytes = new byte[ciphertext.Length];

        try
        {
            using var aesGcm = new AesGcm(key, TagLengthBytes);
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintextBytes);
        }
        catch (CryptographicException ex)
        {
            throw new VaultDecryptionFailedException("Could not decrypt — check the vault credential is correct.", ex);
        }

        return Encoding.UTF8.GetString(plaintextBytes);
    }
}
