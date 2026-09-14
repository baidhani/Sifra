using System.Security.Cryptography;
using Sifra.Vault.Audit;

namespace Sifra.Vault.Devices;

/// <summary>
/// Device enrollment, access verification, revocation, and re-enrollment.
/// A device id alone is not proof of identity — a device also holds a
/// secret (shown once at enrollment, only its hash persisted, same
/// pattern as the recovery key) that it must present to prove it is the
/// device it claims to be. This is what defends against a spoofed device
/// id: knowing the id is not enough.
/// </summary>
public sealed class DeviceIdentityService
{
    private const int SaltLengthBytes = 16;
    private const int HashLengthBytes = 32;
    private const int Pbkdf2Iterations = 210_000; // OWASP current minimum for PBKDF2-SHA256

    // A device's secret proves it's the browser that was paired, but says
    // nothing about whether whoever is sitting at that browser actually
    // knows the vault's master password. Without a limit, a stolen/leaked
    // device secret would let an attacker brute-force the master password
    // offline through this device indefinitely. Auto-revoking (rather than
    // a timed cooldown) matches the explicit choice made when this was
    // discussed: too many wrong passwords should force re-pairing, not
    // just a wait.
    private const int MaxFailedPasswordAttempts = 5;

    private readonly DeviceRegistryStore _store;
    private readonly AuditLogger? _auditLogger;

    public DeviceIdentityService(DeviceRegistryStore store, AuditLogger? auditLogger = null)
    {
        _store = store;
        _auditLogger = auditLogger;
    }

    /// <summary>
    /// Records a wrong vault-password attempt made through this device
    /// (distinct from VerifyDeviceAccess, which checks the device's own
    /// secret — this tracks whether whoever holds that secret actually
    /// knows the master password). Auto-revokes the device once
    /// MaxFailedPasswordAttempts is reached.
    /// </summary>
    /// <returns>True if this call caused (or the device already had) an auto-revocation.</returns>
    /// <exception cref="DeviceNotEnrolledException">This device id has never been enrolled.</exception>
    public bool RecordFailedPasswordAttempt(string deviceId)
    {
        var record = _store.Load(deviceId) ?? throw new DeviceNotEnrolledException(deviceId);
        if (record.RevokedAtUtc is not null)
        {
            return true;
        }

        var attempts = record.FailedPasswordAttempts + 1;
        var revokedNow = attempts >= MaxFailedPasswordAttempts;

        _store.Save(record with
        {
            FailedPasswordAttempts = attempts,
            RevokedAtUtc = revokedNow ? DateTimeOffset.UtcNow : null,
        });

        _auditLogger?.Log(nameof(RecordFailedPasswordAttempt), Environment.UserName,
            details: $"deviceId={deviceId} attempts={attempts} autoRevoked={revokedNow}");

        return revokedNow;
    }

    /// <summary>Clears a device's failed-password counter after a correct password. No-op for an unenrolled device id.</summary>
    public void ResetFailedPasswordAttempts(string deviceId)
    {
        var record = _store.Load(deviceId);
        if (record is not null && record.FailedPasswordAttempts != 0)
        {
            _store.Save(record with { FailedPasswordAttempts = 0 });
        }
    }

    /// <summary>All enrolled devices (active and revoked), newest first — for a device-management UI. Never includes a secret.</summary>
    public IReadOnlyList<DeviceRecord> ListDevices() =>
        _store.LoadAll().Values.OrderByDescending(d => d.EnrolledAtUtc).ToList();

    /// <returns>(deviceId, deviceSecret) — the secret is returned only this once and never persisted in plaintext.</returns>
    public (string DeviceId, string DeviceSecret) EnrollDevice(string deviceName)
    {
        var deviceId = RecoveryKeyGenerator.Generate();
        var deviceSecret = RecoveryKeyGenerator.Generate();

        var salt = RandomNumberGenerator.GetBytes(SaltLengthBytes);
        var hash = HashSecret(deviceSecret, salt);

        _store.Save(new DeviceRecord(
            deviceId,
            deviceName,
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash),
            EnrolledAtUtc: DateTimeOffset.UtcNow,
            RevokedAtUtc: null));

        _auditLogger?.Log(nameof(EnrollDevice), Environment.UserName, details: $"deviceId={deviceId}");

        return (deviceId, deviceSecret);
    }

    /// <summary>
    /// The access-control check: does this device id, with this secret,
    /// currently have access to the vault?
    /// </summary>
    /// <exception cref="DeviceNotEnrolledException">This device id has never been enrolled.</exception>
    /// <exception cref="InvalidDeviceSecretException">The secret does not match this device id.</exception>
    /// <exception cref="DeviceRevokedException">The device is enrolled but has been revoked.</exception>
    public void VerifyDeviceAccess(string deviceId, string deviceSecret)
    {
        var record = _store.Load(deviceId) ?? throw new DeviceNotEnrolledException(deviceId);

        if (!SecretMatches(deviceSecret, record))
        {
            _auditLogger?.Log(nameof(VerifyDeviceAccess), Environment.UserName, details: $"deviceId={deviceId} outcome=invalid_secret");
            throw new InvalidDeviceSecretException(deviceId);
        }

        if (record.RevokedAtUtc is not null)
        {
            _auditLogger?.Log(nameof(VerifyDeviceAccess), Environment.UserName, details: $"deviceId={deviceId} outcome=revoked");
            throw new DeviceRevokedException(deviceId);
        }

        _auditLogger?.Log(nameof(VerifyDeviceAccess), Environment.UserName, details: $"deviceId={deviceId} outcome=granted");
    }

    /// <summary>Idempotent — revoking an already-revoked device is a no-op success.</summary>
    /// <exception cref="DeviceNotEnrolledException">This device id has never been enrolled.</exception>
    public void RevokeDevice(string deviceId)
    {
        var record = _store.Load(deviceId) ?? throw new DeviceNotEnrolledException(deviceId);

        if (record.RevokedAtUtc is null)
        {
            _store.Save(record with { RevokedAtUtc = DateTimeOffset.UtcNow });
        }

        _auditLogger?.Log(nameof(RevokeDevice), Environment.UserName, details: $"deviceId={deviceId}");
    }

    /// <summary>
    /// Restores access to a revoked device. Requires the device's original
    /// secret — knowing a revoked device's id is not enough to reinstate
    /// it, only the device that was actually enrolled can. Idempotent —
    /// re-enrolling an already-active device is a no-op success.
    /// </summary>
    /// <exception cref="DeviceNotEnrolledException">This device id has never been enrolled.</exception>
    /// <exception cref="InvalidDeviceSecretException">The secret does not match this device id.</exception>
    public void ReEnrollDevice(string deviceId, string deviceSecret)
    {
        var record = _store.Load(deviceId) ?? throw new DeviceNotEnrolledException(deviceId);

        if (!SecretMatches(deviceSecret, record))
        {
            _auditLogger?.Log(nameof(ReEnrollDevice), Environment.UserName, details: $"deviceId={deviceId} outcome=invalid_secret");
            throw new InvalidDeviceSecretException(deviceId);
        }

        if (record.RevokedAtUtc is not null)
        {
            _store.Save(record with { RevokedAtUtc = null });
        }

        _auditLogger?.Log(nameof(ReEnrollDevice), Environment.UserName, details: $"deviceId={deviceId}");
    }

    private static bool SecretMatches(string candidateSecret, DeviceRecord record)
    {
        var salt = Convert.FromBase64String(record.SecretSaltBase64);
        var expectedHash = Convert.FromBase64String(record.SecretHashBase64);
        var actualHash = HashSecret(candidateSecret, salt);
        return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
    }

    private static byte[] HashSecret(string secret, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(secret, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, HashLengthBytes);
}
