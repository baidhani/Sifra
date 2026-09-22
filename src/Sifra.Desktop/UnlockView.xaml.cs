using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Sifra.Desktop;

public partial class UnlockView : UserControl
{
    private readonly AppServices _services;

    /// <summary>Raised with the now-proven-correct vault credential once unlock succeeds.</summary>
    public event EventHandler<string>? Unlocked;

    /// <summary>Raised when the user clicks Deny on the pending-pairing-request notice, without ever unlocking.</summary>
    public event EventHandler? DenyPairingRequested;

    public UnlockView(AppServices services)
    {
        InitializeComponent();
        _services = services;

        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version is null ? string.Empty : $"v{version.Major}.{version.Minor}.{version.Build}";
    }

    public void SetPairingNoticeVisible(bool visible)
    {
        PairingNoticeBorder.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnDenyPairingClick(object sender, RoutedEventArgs e)
    {
        DenyPairingRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnUnlockClick(object sender, RoutedEventArgs e) => TryUnlock();

    private void OnPasswordBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            TryUnlock();
        }
    }

    private void TryUnlock()
    {
        ErrorText.Text = string.Empty;
        var password = MasterPasswordBox.Password;

        if (_services.VaultAuth.Authenticate(password))
        {
            Unlocked?.Invoke(this, password);
        }
        else
        {
            ErrorText.Text = "Incorrect master password.";
            MasterPasswordBox.Clear();
        }
    }
}
