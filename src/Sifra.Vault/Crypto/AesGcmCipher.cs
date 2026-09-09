using System.Security.Cryptography;
using System.Text;

namespace Sifra.Vault.Crypto;

/// <summary>
/// Shared AES-GCM primitive (nonce || tag || ciphertext, base64 for the
/// string overloads). Used both for encrypting credential fields
/// (VaultEncryptionService) and for wrapping the vault master key
/// (VaultMasterKeyStore) — one implementation, not two copies.
/// </summary>
internal static class AesGcmCipher
{
    private const int NonceLengthBytes = 12;
    private const int TagLengthBytes = 16;

    public static string Encrypt(string plaintext, byte[] key) =>
        Convert.ToBase64String(EncryptBytes(Encoding.UTF8.GetBytes(plaintext), key));

    /// <exception cref="VaultDecryptionFailedException">The ciphertext could not be authenticated.</exception>
    public static string Decrypt(string base64CipherBlob, byte[] key) =>
        Encoding.UTF8.GetString(DecryptBytes(Convert.FromBase64String(base64CipherBlob), key));

    public static byte[] EncryptBytes(byte[] plaintext, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceLengthBytes);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagLengthBytes];

        using var aesGcm = new AesGcm(key, TagLengthBytes);
        aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);

        var combined = new byte[NonceLengthBytes + TagLengthBytes + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, combined, 0, NonceLengthBytes);
        Buffer.BlockCopy(tag, 0, combined, NonceLengthBytes, TagLengthBytes);
        Buffer.BlockCopy(ciphertext, 0, combined, NonceLengthBytes + TagLengthBytes, ciphertext.Length);
        return combined;
    }

    /// <exception cref="VaultDecryptionFailedException">The ciphertext could not be authenticated — usually the wrong key.</exception>
    public static byte[] DecryptBytes(byte[] combined, byte[] key)
    {
        var nonce = combined[..NonceLengthBytes];
        var tag = combined[NonceLengthBytes..(NonceLengthBytes + TagLengthBytes)];
        var ciphertext = combined[(NonceLengthBytes + TagLengthBytes)..];
        var plaintext = new byte[ciphertext.Length];

        try
        {
            using var aesGcm = new AesGcm(key, TagLengthBytes);
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);
        }
        catch (CryptographicException ex)
        {
            throw new VaultDecryptionFailedException("Could not decrypt — check the vault credential is correct.", ex);
        }

        return plaintext;
    }
}
