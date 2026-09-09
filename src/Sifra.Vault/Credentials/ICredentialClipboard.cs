namespace Sifra.Vault.Credentials;

/// <summary>
/// Abstraction over OS clipboard access. TextCopyCredentialClipboard is
/// the real implementation (cross-platform via the TextCopy package);
/// FakeCredentialClipboard is used in tests so they never depend on a
/// real display/clipboard being available.
/// </summary>
public interface ICredentialClipboard
{
    /// <exception cref="ClipboardUnavailableException">The system denied clipboard access.</exception>
    void SetText(string value);
}
