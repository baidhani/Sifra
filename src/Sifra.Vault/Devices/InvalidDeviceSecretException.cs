namespace Sifra.Vault.Devices;

/// <summary>
/// Thrown when a claimed device id is presented with the wrong secret —
/// the "device identity is spoofed" failure path. A device id alone
/// proves nothing; only a matching secret does.
/// </summary>
public sealed class InvalidDeviceSecretException : Exception
{
    public InvalidDeviceSecretException(string deviceId)
        : base($"The secret presented for device '{deviceId}' is incorrect.")
    {
    }
}
