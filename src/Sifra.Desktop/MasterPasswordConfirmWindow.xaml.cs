using System.Windows;
using System.Windows.Input;
using Sifra.Vault.Auth;
using Wpf.Ui.Controls;

namespace Sifra.Desktop;

/// <summary>
/// Generic "type your master password to proceed" gate — used by the
/// Lock feature (unlock-for-this-session, and permanently removing a
/// lock), reusing the existing VaultAuthenticator check rather than any
/// new secret storage. Purely a re-authentication step: it does not touch
/// vault data itself, that's the caller's job after DialogResult is true.
/// </summary>
public partial class MasterPasswordConfirmWindow : FluentWindow
{
    private readonly VaultAuthenticator _vaultAuth;

    public MasterPasswordConfirmWindow(VaultAuthenticator vaultAuth, string message)
    {
        InitializeComponent();
        _vaultAuth = vaultAuth;
        MessageText.Text = message;
    }

    private void OnPasswordBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnConfirmClick(sender, e);
        }
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        if (_vaultAuth.Authenticate(PasswordBox.Password))
        {
            DialogResult = true;
        }
        else
        {
            ErrorText.Text = "Incorrect master password.";
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
