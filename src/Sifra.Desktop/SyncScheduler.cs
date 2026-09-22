using System.Windows.Threading;
using Sifra.Vault.Dropbox;
using Sifra.Vault.GoogleDrive;
using Sifra.Vault.PCloud;
using Sifra.Vault.Sync;

namespace Sifra.Desktop;

/// <summary>
/// Drives background sync against this vault's ActiveSyncProvider: once on
/// Start() (called right after unlock) and then on a repeating timer while
/// the vault stays unlocked. Deliberately never triggers the interactive
/// sign-in flow itself (which opens a browser) — only a silent reconnect
/// via a previously-saved token (AppServices.TrySilentSignInActiveProvider).
/// An unprompted browser popup from a background timer would be a
/// confusing, unexpected security-relevant UI event; the manual Sync
/// button (VaultView.OnSyncClick) is the one place a user gesture
/// justifies opening a browser for the first-ever connect or after a
/// revoked token.
///
/// Failures are never shown as a popup here — StatusChanged carries a
/// short status string for a quiet UI indicator instead, so a flaky
/// network doesn't interrupt the user; the audit log (via each service's
/// own AuditLogger) is the durable record of what happened.
/// </summary>
public sealed class SyncScheduler
{
    private readonly AppServices _services;
    private readonly DispatcherTimer _timer;
    private bool _isRunning;

    public event EventHandler<string>? StatusChanged;

    public SyncScheduler(AppServices services, TimeSpan? interval = null)
    {
        _services = services;
        _timer = new DispatcherTimer { Interval = interval ?? TimeSpan.FromMinutes(5) };
        _timer.Tick += async (_, _) => await RunAsync();
    }

    public void Start()
    {
        _timer.Start();
        _ = RunAsync();
    }

    public void Stop() => _timer.Stop();

    public async Task RunAsync()
    {
        if (_isRunning)
        {
            return; // a manual Sync click and the timer landed at the same moment — let the one already running finish
        }

        if (_services.ActiveSyncProvider is null)
        {
            return; // no provider configured for this vault — nothing to do, no status noise
        }

        _isRunning = true;
        try
        {
            var connected = await Task.Run(() => _services.TrySilentSignInActiveProvider());
            if (!connected)
            {
                StatusChanged?.Invoke(this, "Not connected — click Sync to connect.");
                return;
            }

            await Task.Run(() =>
            {
                _services.CreateActiveSyncService().SyncNow();
                _services.CreateActiveAttachmentBlobSyncService().SyncBlobs();
            });

            StatusChanged?.Invoke(this, $"Last synced at {DateTime.Now:t}");
        }
        catch (VaultMismatchException)
        {
            // Not a transient failure — the cloud file/folder genuinely
            // belongs to a different vault. Retrying won't help; surfacing
            // it as a distinct message (not "click Sync to retry") avoids
            // suggesting the user should just try again.
            StatusChanged?.Invoke(this, "Sync stopped — this cloud account has a different vault's data on it. See Options.");
        }
        catch (Exception ex) when (ex is SyncUnavailableException or SyncConflictExhaustedException
            or DropboxNetworkException or DropboxUnauthorizedException or DropboxSyncFailedException
            or GoogleDriveNetworkException or GoogleDriveUnauthorizedException or GoogleDriveSyncFailedException
            or PCloudNetworkException or PCloudUnauthorizedException or PCloudSyncFailedException)
        {
            StatusChanged?.Invoke(this, "Sync failed — click Sync to retry.");
        }
        finally
        {
            _isRunning = false;
        }
    }
}
