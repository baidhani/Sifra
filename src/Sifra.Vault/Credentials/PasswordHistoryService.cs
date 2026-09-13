using Sifra.Vault.Crypto;

namespace Sifra.Vault.Credentials;

/// <summary>
/// Tracks previous values of Password-type fields, capped at the last
/// MaxEntries per (credential, field) so history can't grow unbounded.
/// Only Password-type fields are tracked — PIN/Secret/etc. are not, per
/// the "History" feature's scope.
/// </summary>
public sealed class PasswordHistoryService
{
    private const int MaxEntriesPerField = 20;

    private readonly PasswordHistoryStore _store;
    private readonly VaultEncryptionService _encryption;

    public PasswordHistoryService(PasswordHistoryStore store, VaultEncryptionService encryption)
    {
        _store = store;
        _encryption = encryption;
    }

    /// <summary>Records the value being replaced — call with the OLD ciphertext, before the field is overwritten.</summary>
    public void Add(string credentialId, string fieldName, string encryptedValueBase64)
    {
        var all = _store.GetAll().ToList();
        all.Add(new PasswordHistoryRecord(credentialId, fieldName, encryptedValueBase64, DateTimeOffset.UtcNow));

        // Trim this (credential, field) group to the newest MaxEntriesPerField
        // — other groups are untouched.
        var groupOrdered = all
            .Where(r => r.CredentialId == credentialId && r.FieldName == fieldName)
            .OrderByDescending(r => r.ChangedAtUtc)
            .ToList();
        if (groupOrdered.Count > MaxEntriesPerField)
        {
            var toDrop = groupOrdered.Skip(MaxEntriesPerField).ToHashSet();
            all.RemoveAll(r => toDrop.Contains(r));
        }

        _store.Save(all);
    }

    /// <summary>Decrypted history for one field, newest first.</summary>
    public IReadOnlyList<(string Value, DateTimeOffset ChangedAtUtc)> List(string vaultCredential, string credentialId, string fieldName)
    {
        var key = _encryption.DeriveKey(vaultCredential);
        return _store.GetAll()
            .Where(r => r.CredentialId == credentialId && r.FieldName == fieldName)
            .OrderByDescending(r => r.ChangedAtUtc)
            .Select(r => (_encryption.Decrypt(r.EncryptedValueBase64, key), r.ChangedAtUtc))
            .ToList();
    }

    public void Clear(string credentialId, string fieldName)
    {
        var remaining = _store.GetAll()
            .Where(r => !(r.CredentialId == credentialId && r.FieldName == fieldName))
            .ToList();
        _store.Save(remaining);
    }

    /// <summary>Called when a credential is deleted, so its history doesn't become orphaned.</summary>
    public void DeleteAllForCredential(string credentialId)
    {
        var remaining = _store.GetAll().Where(r => r.CredentialId != credentialId).ToList();
        _store.Save(remaining);
    }
}
