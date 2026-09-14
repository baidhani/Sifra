namespace Sifra.Vault.Credentials;

/// <summary>
/// A user-defined name/value/type field for anything the built-in fields
/// don't cover — matches the type picker users choose from when adding a
/// field (Text, Number, Login, Password, One-Time Password, Expiry,
/// Website, Email, Phone, Date, PIN, Secret). The value is always treated
/// as a secret (encrypted like Password) regardless of type, since even a
/// "Text" custom field could hold something sensitive — type only drives
/// how the UI displays/masks/formats it, and for OneTimePassword, what the
/// stored value actually means (a TOTP secret, not a code).
/// </summary>
/// <param name="UpdatedAtUtc">
/// When this specific field's value last changed — not the credential's
/// overall UpdatedAtUtc. Groundwork for per-field last-write-wins cloud
/// sync (Phase 3): defaults to epoch-like "unset" only for records built
/// before this existed; real writers always supply the actual edit time.
/// </param>
public sealed record CustomField(string Name, string EncryptedValueBase64, CustomFieldType Type = CustomFieldType.Text, DateTimeOffset UpdatedAtUtc = default);

/// <summary>Decrypted view of a CustomField.</summary>
public sealed record CustomFieldView(string Name, string Value, CustomFieldType Type = CustomFieldType.Text, DateTimeOffset UpdatedAtUtc = default);
