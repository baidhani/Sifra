using Sifra.Vault.Audit;
using Sifra.Vault.Devices;

namespace Sifra.Vault.Tests;

public sealed class DeviceIdentityServiceTests : IDisposable
{
    private readonly string _dataDirectory;

    public DeviceIdentityServiceTests()
    {
        _dataDirectory = Path.Combine(Path.GetTempPath(), "sifra-device-tests-" + Guid.NewGuid());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private DeviceIdentityService CreateService(AuditLogger? auditLogger = null) =>
        new(new DeviceRegistryStore(_dataDirectory), auditLogger);

    [Fact]
    public void EnrollDevice_ThenVerifyAccess_Succeeds()
    {
        var service = CreateService();
        var (deviceId, deviceSecret) = service.EnrollDevice("Firas's Laptop");

        // Does not throw.
        service.VerifyDeviceAccess(deviceId, deviceSecret);
    }

    [Fact]
    public void RevokeDevice_ThenVerifyAccess_ThrowsAndBlocksAccess()
    {
        // Acceptance: a revoked device cannot access the vault.
        var service = CreateService();
        var (deviceId, deviceSecret) = service.EnrollDevice("Firas's Laptop");

        service.RevokeDevice(deviceId);

        Assert.Throws<DeviceRevokedException>(() => service.VerifyDeviceAccess(deviceId, deviceSecret));
    }

    [Fact]
    public void RevokeDevice_IsIdempotent()
    {
        var service = CreateService();
        var (deviceId, deviceSecret) = service.EnrollDevice("Firas's Laptop");

        service.RevokeDevice(deviceId);
        service.RevokeDevice(deviceId); // second call must not throw

        Assert.Throws<DeviceRevokedException>(() => service.VerifyDeviceAccess(deviceId, deviceSecret));
    }

    [Fact]
    public void ReEnrollDevice_WithCorrectSecret_RestoresAccess()
    {
        // Acceptance: a re-enrolled device regains access.
        var service = CreateService();
        var (deviceId, deviceSecret) = service.EnrollDevice("Firas's Laptop");
        service.RevokeDevice(deviceId);

        service.ReEnrollDevice(deviceId, deviceSecret);

        service.VerifyDeviceAccess(deviceId, deviceSecret); // does not throw
    }

    [Fact]
    public void ReEnrollDevice_IsIdempotentWhenAlreadyActive()
    {
        var service = CreateService();
        var (deviceId, deviceSecret) = service.EnrollDevice("Firas's Laptop");

        service.ReEnrollDevice(deviceId, deviceSecret); // never was revoked — must not throw

        service.VerifyDeviceAccess(deviceId, deviceSecret);
    }

    [Fact]
    public void VerifyDeviceAccess_WithUnknownDeviceId_ThrowsDeviceNotEnrolled()
    {
        var service = CreateService();

        Assert.Throws<DeviceNotEnrolledException>(() => service.VerifyDeviceAccess("no-such-device", "any-secret"));
    }

    [Fact]
    public void VerifyDeviceAccess_WithWrongSecret_ThrowsInvalidDeviceSecret()
    {
        // Failure path: "device identity is spoofed" — knowing the device
        // id is not enough; the secret must also match.
        var service = CreateService();
        var (deviceId, _) = service.EnrollDevice("Firas's Laptop");

        Assert.Throws<InvalidDeviceSecretException>(() => service.VerifyDeviceAccess(deviceId, "attacker-guessed-this"));
    }

    [Fact]
    public void ReEnrollDevice_WithWrongSecret_ThrowsAndLeavesTheDeviceRevoked()
    {
        // Failure path (spoofing, at re-enrollment): merely knowing a
        // revoked device's id must not be enough to reinstate it.
        var service = CreateService();
        var (deviceId, deviceSecret) = service.EnrollDevice("Firas's Laptop");
        service.RevokeDevice(deviceId);

        Assert.Throws<InvalidDeviceSecretException>(() => service.ReEnrollDevice(deviceId, "attacker-guessed-this"));

        // Still revoked — the failed attempt did not accidentally restore access.
        Assert.Throws<DeviceRevokedException>(() => service.VerifyDeviceAccess(deviceId, deviceSecret));
    }

    [Fact]
    public void RevokeDevice_WithUnknownDeviceId_ThrowsDeviceNotEnrolled()
    {
        var service = CreateService();

        Assert.Throws<DeviceNotEnrolledException>(() => service.RevokeDevice("no-such-device"));
    }

    [Fact]
    public void RevokeDevice_WhenTheUnderlyingStoreFails_PropagatesInsteadOfSilentlySucceeding()
    {
        // Failure path: "revocation fails to block access" — a storage
        // error must be surfaced loudly, never silently swallowed while
        // pretending the device is now revoked. Simulated by holding an
        // exclusive lock on devices.json while Save() tries to move a new
        // version over it (throws UnauthorizedAccessException on Windows).
        var service = CreateService();
        var (deviceId, deviceSecret) = service.EnrollDevice("Firas's Laptop");

        var devicesFilePath = Path.Combine(_dataDirectory, "devices.json");
        using (new FileStream(devicesFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Throws<VaultStorageException>(() => service.RevokeDevice(deviceId));
        }

        // Confirm the device was never actually revoked by the failed attempt.
        service.VerifyDeviceAccess(deviceId, deviceSecret); // does not throw — original enrollment is untouched
    }

    [Fact]
    public void EnrollDevice_WithAuditLoggerProvided_LogsTheEventWithoutLoggingTheSecret()
    {
        // Trust: device management actions are logged.
        var sink = new FileAuditLogSink(Path.Combine(_dataDirectory, "operations.log"));
        var logger = new AuditLogger(sink, new LocalFakeAdminAlertSink());
        var service = CreateService(logger);

        var (deviceId, deviceSecret) = service.EnrollDevice("Firas's Laptop");

        var lines = sink.ReadAll();
        Assert.Single(lines);
        Assert.Contains("EnrollDevice", lines[0]);
        Assert.Contains(deviceId, lines[0]);
        Assert.DoesNotContain(deviceSecret, lines[0]);
    }

    [Fact]
    public void RevokeDevice_ThenReEnroll_BothLogSeparately()
    {
        var sink = new FileAuditLogSink(Path.Combine(_dataDirectory, "operations.log"));
        var logger = new AuditLogger(sink, new LocalFakeAdminAlertSink());
        var service = CreateService(logger);
        var (deviceId, deviceSecret) = service.EnrollDevice("Firas's Laptop");

        service.RevokeDevice(deviceId);
        service.ReEnrollDevice(deviceId, deviceSecret);

        var lines = sink.ReadAll();
        Assert.Contains(lines, l => l.Contains("RevokeDevice"));
        Assert.Contains(lines, l => l.Contains("ReEnrollDevice"));
    }
}
