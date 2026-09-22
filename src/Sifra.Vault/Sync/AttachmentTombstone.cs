namespace Sifra.Vault.Sync;

/// <summary>
/// Marks an attachment as deleted, with a timestamp — same purpose as
/// CredentialTombstone, for the same reason: without it, a device that was
/// offline when an attachment was deleted elsewhere would push its
/// still-locally-present metadata back up and resurrect a metadata row
/// pointing at a blob file that no longer exists anywhere.
/// </summary>
public sealed record AttachmentTombstone(string Id, DateTimeOffset DeletedAtUtc);
