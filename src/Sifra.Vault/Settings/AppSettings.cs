namespace Sifra.Vault.Settings;

/// <summary>
/// Local, plaintext app preferences — never secret data, so no encryption
/// (unlike Credential/CustomField). AutoLockMinutes null means "never".
/// </summary>
public sealed record AppSettings(int? AutoLockMinutes = 5);
