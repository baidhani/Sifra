namespace Sifra.Vault.Devices;

/// <summary>Thrown when an operation targets a device id that has never been enrolled.</summary>
public sealed class DeviceNotEnrolledException : Exception
{
    public DeviceNotEnrolledException(string deviceId)
        : base($"No device is enrolled with id '{deviceId}'.")
    {
    }
}
