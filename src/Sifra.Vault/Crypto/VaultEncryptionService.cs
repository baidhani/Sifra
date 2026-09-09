using System.Security.Cryptography;

namespace Sifra.Vault.Crypto;

/// <summary>
/// Encrypts vault content with a persistent, random vault master key
/// (VMK) — not with a key derived directly from the password. The VMK is
/// stored wrapped under a key-encryption-key (KEK) derived from the
/// current vault credential (STORY-013's auth credential). This envelope
/// design is what makes changing the master password (STORY-005,
/// REQ-008) a matter of re-wrapping one small key rather than
/// re-encrypting every credential: the VMK itself never changes, only
/// how it is wrapped does.
/// </summary>
public sealed class VaultEncryptionService
{
    private const int KeyLengthBytes = 32; // AES-256
    private const int KekSaltLengthBytes = 16;
    private const int Pbkdf2Iterations = 210_000; // OWASP current minimum for PBKDF2-SHA256

    private readonly VaultMasterKeyStore _masterKeyStore;

    public VaultEncryptionService(VaultMasterKeyStore masterKeyStore)
    {
        _masterKeyStore = masterKeyStore;
    }

    /// <summary>
    /// Returns the vault master key, unwrapped using a KEK derived from
    /// this vault credential. On first use (no wrapped key persisted yet),
    /// generates a fresh random VMK and persists it wrapped under this
    /// credential.
    /// </summary>
    /// <exception cref="VaultDecryptionFailedException">
    /// A wrapped key already exists and this credential is wrong — the KEK
    /// derived from it cannot unwrap the VMK.
    /// </exception>
    public byte[] DeriveKey(string vaultCredential)
    {
        var record = _masterKeyStore.Load();
        if (record is null)
        {
            var vmk = RandomNumberGenerator.GetBytes(KeyLengthBytes);
            PersistWrapped(vmk, vaultCredential);
            return vmk;
        }

        var kek = DeriveKek(vaultCredential, Convert.FromBase64String(record.KekSaltBase64));
        return AesGcmCipher.DecryptBytes(Convert.FromBase64String(record.WrappedKeyBase64), kek);
    }

    /// <summary>
    /// Re-wraps the existing vault master key under a new credential's KEK.
    /// The VMK itself is unchanged, so every credential ciphertext already
    /// on disk remains decryptable — nothing is re-encrypted (REQ-008).
    /// </summary>
    /// <exception cref="VaultDecryptionFailedException">The old credential is wrong.</exception>
    public void RewrapMasterKey(string oldVaultCredential, string newVaultCredential)
    {
        var vmk = DeriveKey(oldVaultCredential); // unwraps with the old credential; also handles the no-key-yet case
        PersistWrapped(vmk, newVaultCredential);
    }

    public string Encrypt(string plaintext, byte[] key) => AesGcmCipher.Encrypt(plaintext, key);

    /// <exception cref="VaultDecryptionFailedException">
    /// The ciphertext could not be authenticated — usually the wrong key (wrong vault credential).
    /// </exception>
    public string Decrypt(string base64CipherBlob, byte[] key) => AesGcmCipher.Decrypt(base64CipherBlob, key);

    private void PersistWrapped(byte[] vmk, string vaultCredential)
    {
        var salt = RandomNumberGenerator.GetBytes(KekSaltLengthBytes);
        var kek = DeriveKek(vaultCredential, salt);
        var wrapped = AesGcmCipher.EncryptBytes(vmk, kek);
        _masterKeyStore.Save(new VaultMasterKeyRecord(Convert.ToBase64String(salt), Convert.ToBase64String(wrapped)));
    }

    private static byte[] DeriveKek(string vaultCredential, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(vaultCredential, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, KeyLengthBytes);
}
