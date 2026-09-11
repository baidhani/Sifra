using System.Windows;
using System.Windows.Controls;
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

    private void OnContinueClick(object sender, RoutedEventArgs e)
    {
        SetupComplete?.Invoke(this, EventArgs.Empty);
    }
}
