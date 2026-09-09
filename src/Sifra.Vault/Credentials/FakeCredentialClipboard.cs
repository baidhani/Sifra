namespace Sifra.Vault.Credentials;

/// <summary>
/// In-memory clipboard for tests — never touches the real OS clipboard, so
/// tests are deterministic and safe to run in a headless environment.
/// </summary>
public sealed class FakeCredentialClipboard : ICredentialClipboard
{
    public string? LastCopiedValue { get; private set; }
    public bool SimulateFailure { get; set; }

    public void SetText(string value)
    {
        if (SimulateFailure)
        {
            throw new ClipboardUnavailableException("Simulated clipboard access denial.");
        }

        LastCopiedValue = value;
    }
}
