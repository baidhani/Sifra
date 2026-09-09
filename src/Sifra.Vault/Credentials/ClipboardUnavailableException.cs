namespace Sifra.Vault.Credentials;

/// <summary>Thrown when the OS clipboard cannot be written to — the "clipboard access is denied" failure path.</summary>
public sealed class ClipboardUnavailableException : Exception
{
    public ClipboardUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
