using System.Windows.Controls;

namespace Sifra.Desktop;

public partial class ShellView : UserControl
{
    private readonly VaultView _vaultView;

    public event EventHandler? LockRequested;
    public event EventHandler? SettingsChanged;

    public ShellView(AppServices services, string vaultCredential, SyncScheduler syncScheduler)
    {
        InitializeComponent();

        _vaultView = new VaultView(services, vaultCredential, syncScheduler);
        _vaultView.LockRequested += (_, _) => LockRequested?.Invoke(this, EventArgs.Empty);
        _vaultView.SettingsChanged += (_, _) => SettingsChanged?.Invoke(this, EventArgs.Empty);
        ContentHost.Content = _vaultView;
    }

    /// <summary>See VaultView.DetachFromSyncScheduler's remarks.</summary>
    public void DetachFromSyncScheduler() => _vaultView.DetachFromSyncScheduler();
}
