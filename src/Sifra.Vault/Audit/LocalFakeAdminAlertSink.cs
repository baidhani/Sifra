namespace Sifra.Vault.Audit;

/// <summary>
/// In-memory stand-in for a real admin-notification channel. Records
/// alerts so tests and the CLI demo can observe that an alert was raised.
/// </summary>
public sealed class LocalFakeAdminAlertSink : IAdminAlertSink
{
    public List<string> Alerts { get; } = new();

    public void Alert(string message) => Alerts.Add(message);
}
