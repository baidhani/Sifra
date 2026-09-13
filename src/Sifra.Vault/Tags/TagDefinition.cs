namespace Sifra.Vault.Tags;

/// <summary>
/// A shared, vault-wide tag definition (name + display color + pin
/// state) that credentials reference by name via Credential.Tags.
/// Plaintext metadata, same as Credential.Tags itself — not secret.
/// </summary>
public sealed record TagDefinition(string Id, string Name, string Color, bool PinnedToTop);
