namespace Sifra.Vault.Auth;

/// <summary>
/// What is persisted for vault authentication. Deliberately its own record
/// type, its own file, and its own hash — sharing nothing with any
/// cloud-provider credential or token. The plaintext credential is never
/// stored, only a salted hash.
/// </summary>
public sealed record VaultAccessCredentialRecord(
    string SaltBase64,
    string HashBase64);
