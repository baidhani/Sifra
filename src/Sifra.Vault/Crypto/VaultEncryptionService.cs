using System.Security.Cryptography;

namespace Sifra.Vault.Crypto;

/// <summary>
/// Encrypts vault content with a persistent, random vault master key
/// (VMK) — not with a key derived directly from any single credential.
/// The VMK can be wrapped under more than one credential at once, each in
/// its own named slot: STORY-005 uses the "master-password" slot so
/// changing the password only re-wraps one small key rather than
/// re-encrypting every credential (REQ-008); STORY-006 adds a
/// "recovery-key" slot so recovery can unwrap the SAME VMK — and
/// therefore the SAME existing credentials — using the recovery key
/// instead of the forgotten password (REQ-009).
/// </summary>
public sealed class VaultEncryptionService
{
    public const string MasterPasswordSlot = "master-password";
    public const string RecoveryKeySlot = "recovery-key";

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
    /// this credential via the named slot. On first ever use (no slot
    /// exists anywhere yet), generates a fresh random VMK and persists it
    /// wrapped in this slot.
    /// </summary>
    /// <exception cref="VaultDecryptionFailedException">
    /// The named slot does not exist (but others do), or this credential
    /// is wrong for it.
    /// </exception>
    public byte[] DeriveKey(string vaultCredential, string slotId = MasterPasswordSlot)
    {
        var slot = _masterKeyStore.LoadSlot(slotId);
        if (slot is null)
        {
            if (_masterKeyStore.HasAnySlot())
            {
                throw new VaultDecryptionFailedException(
                    $"No key slot '{slotId}' exists for this vault.",
                    new InvalidOperationException($"Slot '{slotId}' not found."));
            }

            var vmk = RandomNumberGenerator.GetBytes(KeyLengthBytes);
            PersistWrapped(slotId, vmk, vaultCredential);
            return vmk;
        }

        var kek = DeriveKek(vaultCredential, Convert.FromBase64String(slot.KekSaltBase64));
        return AesGcmCipher.DecryptBytes(Convert.FromBase64String(slot.WrappedKeyBase64), kek);
    }

    /// <summary>
    /// Wraps an already-known vault master key under an additional
    /// credential's slot — used to let a second credential (e.g. a
    /// recovery key) unlock the same VMK, without needing to know any
    /// other slot's credential.
    /// </summary>
    public void AddSlot(string slotId, string credentialForSlot, byte[] vaultMasterKey) =>
        PersistWrapped(slotId, vaultMasterKey, credentialForSlot);

    /// <summary>
    /// Re-wraps a slot's key under a new credential — the VMK itself is
    /// unchanged, so content encrypted under it needs no re-encryption.
    /// </summary>
    /// <exception cref="VaultDecryptionFailedException">The old credential is wrong for this slot.</exception>
    public void RewrapMasterKey(string oldVaultCredential, string newVaultCredential, string slotId = MasterPasswordSlot)
    {
        var vmk = DeriveKey(oldVaultCredential, slotId);
        AddSlot(slotId, newVaultCredential, vmk);
    }

    public string Encrypt(string plaintext, byte[] key) => AesGcmCipher.Encrypt(plaintext, key);

    /// <exception cref="VaultDecryptionFailedException">
    /// The ciphertext could not be authenticated — usually the wrong key (wrong vault credential).
    /// </exception>
    public string Decrypt(string base64CipherBlob, byte[] key) => AesGcmCipher.Decrypt(base64CipherBlob, key);

    private void PersistWrapped(string slotId, byte[] vmk, string vaultCredential)
    {
        var salt = RandomNumberGenerator.GetBytes(KekSaltLengthBytes);
        var kek = DeriveKek(vaultCredential, salt);
        var wrapped = AesGcmCipher.EncryptBytes(vmk, kek);
        _masterKeyStore.SaveSlot(slotId, new VaultMasterKeySlot(Convert.ToBase64String(salt), Convert.ToBase64String(wrapped)));
    }

    private static byte[] DeriveKek(string vaultCredential, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(vaultCredential, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, KeyLengthBytes);
}
