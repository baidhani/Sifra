using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Sifra.Vault;
using Sifra.Vault.Session;

namespace Sifra.Desktop;

public partial class SetupView : UserControl
{
    private readonly AppServices _services;
    public event EventHandler? SetupComplete;

    public SetupView(AppServices services)
    {
        InitializeComponent();
        _services = services;
    }

    private void OnPasswordBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnCreateVaultClick(sender, e);
        }
    }

    private void OnCreateVaultClick(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        var masterPassword = MasterPasswordBox.Password;

        if (masterPassword.Length < MasterPasswordService.MinLength)
        {
            ErrorText.Text = $"Master password must be at least {MasterPasswordService.MinLength} characters.";
            return;
        }

        try
        {
            var recoveryKey = _services.VaultService.CreateVault();
            _services.VaultRecovery.EstablishRecoverySlot(recoveryKey, masterPassword);

            RecoveryKeyText.Text = recoveryKey;
            IntroPanel.Visibility = Visibility.Collapsed;
            RecoveryPanel.Visibility = Visibility.Visible;
            // Focusing this button (rather than leaving focus wherever it was
            // on the now-collapsed intro panel) means Enter activates it via
            // WPF's normal focused-button behavior — landing on Copy, not
            // Continue, so pressing Enter here can't skip past the key before
            // it's saved somewhere. Deferred to Loaded priority: calling
            // Focus() synchronously right after Visibility = Visible silently
            // fails (confirmed — HasKeyboardFocus stayed false) because the
            // button hasn't been through a layout pass yet and isn't
            // considered focusable until then.
            Dispatcher.BeginInvoke(() => CopyToClipboardButton.Focus(), DispatcherPriority.Loaded);
        }
        catch (VaultAlreadyExistsException)
        {
            ErrorText.Text = "A vault already exists on this device.";
        }
        catch (VaultStorageException ex)
        {
            ErrorText.Text = $"Could not create the vault: {ex.Message}";
        }
    }

    private void OnCopyToClipboardClick(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(RecoveryKeyText.Text);
        CopyConfirmationText.Visibility = Visibility.Visible;
    }

    private void OnContinueClick(object sender, RoutedEventArgs e)
    {
        SetupComplete?.Invoke(this, EventArgs.Empty);
    }
}
