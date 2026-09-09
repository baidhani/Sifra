namespace Sifra.Vault.Credentials;

/// <summary>Real clipboard implementation via the TextCopy package (cross-platform: Windows, macOS, Linux).</summary>
public sealed class TextCopyCredentialClipboard : ICredentialClipboard
{
    public void SetText(string value)
    {
        try
        {
            TextCopy.ClipboardService.SetText(value);
        }
        catch (Exception ex)
        {
            throw new ClipboardUnavailableException("Could not write to the system clipboard.", ex);
        }
    }
}
