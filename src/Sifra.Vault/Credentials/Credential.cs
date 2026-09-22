namespace Sifra.Vault.Credentials;

/// <summary>
/// What is persisted for a credential. Fully dynamic field model: every
/// piece of secret data (login, password, website, phone, notes, PIN,
/// anything else) is just an entry in Fields, typed via CustomFieldType,
/// and every field value is stored only as AES-GCM ciphertext (see
/// VaultEncryptionService) — never in plaintext. There is no fixed
/// "Username"/"Password"/"Url" shape any more; a credential can have any
/// number of fields of any type, added and removed freely.
///
/// Label, IsFavorite, and Tags remain fixed, plaintext metadata — not
/// fields — since they identify and organize the credential itself rather
/// than describing an account, and the list/filter views need them without
/// a full decrypt.
///
/// IsArchived and IsDeleted/DeletedAtUtc are the same kind of plaintext
/// metadata, backing the "special folders" (Archived, Trash) concept.
/// Deleting a credential through the normal UI only sets IsDeleted/
/// DeletedAtUtc — the record and all its fields stay fully intact and
/// keep syncing normally like any other edit, which is what makes Restore
/// possible. CredentialService.Delete (the pre-existing hard delete +
/// sync tombstone) is reserved for the one place data should actually be
/// destroyed: emptying the Trash.
///
/// IsLocked is a cross-cutting flag like IsFavorite (an item stays wherever
/// it normally lives — it isn't hidden into a bin the way Archived/Deleted
/// are), guarding against accidental edits/deletes rather than adding any
/// real cryptographic protection: by the time a locked item is visible in
/// an unlocked vault, its data is exactly as decrypted as any other item's.
/// Enforcement (blocking Edit/Delete/Archive/Tags on a locked item, and the
/// re-authenticate-to-unlock flow) is deliberately a Desktop UI concern,
/// not this service's — see CredentialService's own remarks on having no
/// persistent session state by design.
/// </summary>
public sealed record Credential(
    string Id,
    string Label,
    IReadOnlyList<CustomField> Fields,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset CreatedAtUtc,
    bool IsFavorite = false,
    IReadOnlyList<string>? Tags = null,
    CredentialIcon? Icon = null,
    bool IsArchived = false,
    bool IsDeleted = false,
    DateTimeOffset? DeletedAtUtc = null,
    bool IsLocked = false);
