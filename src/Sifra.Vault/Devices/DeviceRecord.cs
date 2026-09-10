namespace Sifra.Vault.Devices;

/// <summary>
/// What is persisted for an enrolled device. The device secret is never
/// stored — only a salted hash, the same pattern as the recovery key
/// (STORY-001) and vault access credential (STORY-013).
/// </summary>
public sealed record DeviceRecord(
    string DeviceId,
    string DeviceName,
    string SecretSaltBase64,
    string SecretHashBase64,
    DateTimeOffset EnrolledAtUtc,
    DateTimeOffset? RevokedAtUtc);
