using System.Security.Cryptography;
using Sifra.Vault.Audit;
using Sifra.Vault.Credentials;
using Sifra.Vault.Crypto;

namespace Sifra.Vault.Sharing;

/// <summary>
/// Shares a single credential with a recipient by re-encrypting it under a
/// key derived from a passphrase the recipient chooses out of band (told
/// to them directly, not stored or transmitted by this service). Reuses
/// CredentialService for the vault-side decrypt — no change to it or to
/// vault encryption. The recipient never needs the vault credential
/// itself, only the share id and the passphrase.
///
/// Revocation is registry-checked, the same idiom as device revocation
/// (STORY-009): AcceptShare consults ShareRegistryStore before decrypting,
/// so a revoked share stops working through this app going forward. It
/// cannot un-share plaintext the recipient may have already copied
/// elsewhere — no offline scheme can — and this class does not claim to.
/// </summary>
public sealed class SharedCredentialService
{
    private const int KeyLengthBytes = 32; // AES-256
    private const int KekSaltLengthBytes = 16;
    private const int Pbkdf2Iterations = 210_000; // matches VaultEncryptionService's KEK derivation

    private readonly CredentialService _credentials;
    private readonly ShareRegistryStore _shares;
    private readonly AuditLogger? _auditLogger;

    public SharedCredentialService(CredentialService credentials, ShareRegistryStore shares, AuditLogger? auditLogger = null)
    {
        _credentials = credentials;
        _shares = shares;
        _auditLogger = auditLogger;
    }

    /// <exception cref="CredentialNotFoundException">No credential exists with this id.</exception>
    public string CreateShare(string vaultCredential, string credentialId, string recipientLabel, string recipientPassphrase)
    {
        var credential = _credentials.GetById(vaultCredential, credentialId);

        var salt = RandomNumberGenerator.GetBytes(KekSaltLengthBytes);
        var key = DeriveKey(recipientPassphrase, salt);

        var shareId = Guid.NewGuid().ToString("N");
        _shares.Save(new ShareRecord(
            shareId,
            recipientLabel,
            credential.Label,
            credential.Url,
            Convert.ToBase64String(salt),
            AesGcmCipher.Encrypt(credential.Username, key),
            AesGcmCipher.Encrypt(credential.Password!, key),
            DateTimeOffset.UtcNow,
            Revoked: false,
            RevokedAtUtc: null));

        _auditLogger?.Log(nameof(CreateShare), Environment.UserName, details: $"shareId={shareId} recipient={recipientLabel}");
        return shareId;
    }

    /// <exception cref="ShareNotFoundException">No share exists with this id.</exception>
    /// <exception cref="ShareRevokedException">The share has been revoked.</exception>
    /// <exception cref="VaultDecryptionFailedException">The passphrase is wrong for this share.</exception>
    public SharedCredentialView AcceptShare(string shareId, string recipientPassphrase)
    {
        var record = _shares.Load(shareId) ?? throw new ShareNotFoundException(shareId);

        if (record.Revoked)
        {
            _auditLogger?.Log(nameof(AcceptShare), Environment.UserName, details: $"shareId={shareId} outcome=revoked");
            throw new ShareRevokedException(shareId);
        }

        var key = DeriveKey(recipientPassphrase, Convert.FromBase64String(record.KekSaltBase64));
        var username = AesGcmCipher.Decrypt(record.EncryptedUsernameBase64, key);
        var password = AesGcmCipher.Decrypt(record.EncryptedPasswordBase64, key);

        _auditLogger?.Log(nameof(AcceptShare), Environment.UserName, details: $"shareId={shareId} outcome=accepted");
        return new SharedCredentialView(record.Label, username, password, record.Url);
    }

    /// <summary>Idempotent — revoking an already-revoked share succeeds without error.</summary>
    /// <exception cref="ShareNotFoundException">No share exists with this id.</exception>
    public void RevokeShare(string shareId)
    {
        var record = _shares.Load(shareId) ?? throw new ShareNotFoundException(shareId);

        if (!record.Revoked)
        {
            _shares.Save(record with { Revoked = true, RevokedAtUtc = DateTimeOffset.UtcNow });
        }

        _auditLogger?.Log(nameof(RevokeShare), Environment.UserName, details: $"shareId={shareId}");
    }

    private static byte[] DeriveKey(string passphrase, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, KeyLengthBytes);
}
