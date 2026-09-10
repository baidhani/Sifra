namespace Sifra.Vault.Devices;

/// <summary>Thrown when a revoked device attempts to access the vault.</summary>
public sealed class DeviceRevokedException : Exception
{
    public DeviceRevokedException(string deviceId)
        : base($"Device '{deviceId}' has been revoked and cannot access the vault.")
    {
    }
}
