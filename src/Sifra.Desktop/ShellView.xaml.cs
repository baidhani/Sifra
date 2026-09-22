using System.Windows.Controls;

namespace Sifra.Desktop;

public partial class ShellView : UserControl
{
    private readonly VaultView _vaultView;

    public event EventHandler? LockRequested;
    public event EventHandler? SettingsChanged;

    public ShellView(AppServices services, string vaultCredential)
    {
        InitializeComponent();

        _vaultView = new VaultView(services, vaultCredential);
        _vaultView.LockRequested += (_, _) => LockRequested?.Invoke(this, EventArgs.Empty);
        _vaultView.SettingsChanged += (_, _) => SettingsChanged?.Invoke(this, EventArgs.Empty);
        ContentHost.Content = _vaultView;
    }

    /// <summary>See VaultView.StopBackgroundSync's remarks.</summary>
    public void StopBackgroundSync() => _vaultView.StopBackgroundSync();
}
