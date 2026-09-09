namespace Sifra.Vault.Audit;

/// <summary>
/// Generic shape any admin-notification channel must expose. No real
/// channel (email, Slack, etc.) is integrated yet — that needs credentials
/// this repo does not have — so this is a seam for a future story, the
/// same way ICloudAuthProvider and ICloudSyncProvider were.
/// </summary>
public interface IAdminAlertSink
{
    void Alert(string message);
}
