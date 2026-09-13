namespace Sifra.Vault.Attachments;

public enum AttachmentKind
{
    Image,
    File,
}

/// <summary>
/// Metadata for one attachment (an image or arbitrary file attached to a
/// credential). FileName is plaintext, same convention as CustomField.Name
/// — names/labels aren't treated as secrets in this data model, only
/// values/content are. The actual file bytes are never stored here; they
/// live encrypted on disk under AttachmentStore's blob directory, keyed by
/// this record's Id.
/// </summary>
public sealed record CredentialAttachment(
    string Id,
    string CredentialId,
    string FileName,
    AttachmentKind Kind,
    long SizeBytes,
    DateTimeOffset CreatedAtUtc);
