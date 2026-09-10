namespace Sifra.Vault.Sharing;

/// <summary>Decrypted result of accepting a share — the recipient-side view.</summary>
public sealed record SharedCredentialView(string Label, string Username, string Password, string? Url);
