using System.Windows.Controls;

namespace Sifra.Desktop;

public partial class ShellView : UserControl
{
    public event EventHandler? LockRequested;

    public ShellView(AppServices services, string vaultCredential)
    {
        InitializeComponent();

        var vaultView = new VaultView(services, vaultCredential);
        vaultView.LockRequested += (_, _) => LockRequested?.Invoke(this, EventArgs.Empty);
        ContentHost.Content = vaultView;
    }
}
