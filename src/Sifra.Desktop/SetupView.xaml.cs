using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using Sifra.Vault;
using Sifra.Vault.Auth;
using Sifra.Vault.Crypto;
using Sifra.Vault.Dropbox;
using Sifra.Vault.GoogleDrive;
using Sifra.Vault.PCloud;
using Sifra.Vault.Session;
using Sifra.Vault.Settings;
using Sifra.Vault.Sync;

namespace Sifra.Desktop;

/// <summary>
/// A small wizard rather than one dense screen: Choice -> (Create | Cloud
/// provider -> Cloud restore | local-file restore). Each path only shows
/// the fields it actually needs, which also fixed a real clipping bug the
/// old single-screen layout had once cloud/provider fields were added
/// alongside master-password fields.
/// </summary>
public partial class SetupView : UserControl
{
    private readonly AppServices _services;
    private CloudSyncProviderKind? _restoreProvider;

    public event EventHandler? SetupComplete;

    public SetupView(AppServices services)
    {
        InitializeComponent();
        _services = services;
    }

    private void OnChoiceNextClick(object sender, RoutedEventArgs e)
    {
        ChoiceErrorText.Text = string.Empty;

        if (RestoreCloudOption.IsChecked == true)
        {
            ShowPanel(CloudProviderPanel);
            return;
        }

        if (RestoreFileOption.IsChecked == true)
        {
            var dialog = new OpenFileDialog { Title = "Choose a Sifra backup file", Filter = "Sifra backup (*.sifra-backup)|*.sifra-backup|All files (*.*)|*.*" };
            if (dialog.ShowDialog() == true)
            {
                ThemedMessageBox.Show(
                    Window.GetWindow(this),
                    "Restoring from a local backup file is not built yet — this path needs further design and discussion.",
                    "Sifra");
            }
            return;
        }

        // CreateNewOption (the default).
        ShowPanel(CreatePanel);
    }

    private void OnBackToChoiceClick(object sender, RoutedEventArgs e)
    {
        CreateErrorText.Text = string.Empty;
        CloudProviderErrorText.Text = string.Empty;
        ShowPanel(ChoicePanel);
    }

    private void OnCreatePasswordBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnCreateVaultClick(sender, e);
        }
    }

    private void OnCreateVaultClick(object sender, RoutedEventArgs e)
    {
        CreateErrorText.Text = string.Empty;
        var masterPassword = MasterPasswordBox.Password;

        if (masterPassword.Length < MasterPasswordService.MinLength)
        {
            CreateErrorText.Text = $"Master password must be at least {MasterPasswordService.MinLength} characters.";
            return;
        }

        try
        {
            var recoveryKey = _services.VaultService.CreateVault();
            _services.VaultRecovery.EstablishRecoverySlot(recoveryKey, masterPassword);

            // On by default for a new install — see StartupRegistration's
            // remarks. This only ever runs once (Setup only ever runs once
            // per device); any later Options toggle is what's authoritative
            // after this point. Cloud sync itself is set up later, from
            // Options ("Set up cloud sync...") — not part of this wizard.
            StartupRegistration.Enable();

            ShowRecoveryPanel("Vault created", recoveryKey);
        }
        catch (VaultAlreadyExistsException)
        {
            CreateErrorText.Text = "A vault already exists on this device.";
        }
        catch (VaultStorageException ex)
        {
            CreateErrorText.Text = $"Could not create the vault: {ex.Message}";
        }
    }

    private void OnCloudProviderNextClick(object sender, RoutedEventArgs e)
    {
        CloudProviderErrorText.Text = string.Empty;
        var provider = RestoreDropboxRadio.IsChecked == true ? CloudSyncProviderKind.Dropbox
            : RestoreGoogleDriveRadio.IsChecked == true ? CloudSyncProviderKind.GoogleDrive
            : CloudSyncProviderKind.PCloud;

        if (!_services.IsProviderConfigured(provider))
        {
            CloudProviderErrorText.Text = $"{provider} is not configured on this machine.";
            return;
        }

        _restoreProvider = provider;
        CloudRestoreDescriptionText.Text = $"Sign in to {provider} and enter this vault's master password. You'll be signed in when you click Restore.";
        CloudRestoreErrorText.Text = string.Empty;
        RestoreStatusText.Text = string.Empty;
        ShowPanel(CloudRestorePanel);
    }

    private void OnBackToCloudProviderClick(object sender, RoutedEventArgs e)
    {
        CloudRestoreErrorText.Text = string.Empty;
        ShowPanel(CloudProviderPanel);
    }

    private void OnRestorePasswordBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnRestoreClick(sender, e);
        }
    }

    private enum RestoreOutcome
    {
        Restored,
        NoVaultFound,
    }

    private async void OnRestoreClick(object sender, RoutedEventArgs e)
    {
        CloudRestoreErrorText.Text = string.Empty;
        var provider = _restoreProvider!.Value;
        var masterPassword = RestoreMasterPasswordBox.Password;

        if (masterPassword.Length == 0)
        {
            CloudRestoreErrorText.Text = "Enter the vault's master password.";
            return;
        }

        RestoreButton.IsEnabled = false;
        CloudRestoreBackButton.IsEnabled = false;
        RestoreProgressRing.Visibility = Visibility.Visible;
        RestoreStatusText.Text = $"Signing in to {provider}...";
        try
        {
            var (outcome, recoveryKey) = await Task.Run(() =>
            {
                _services.EnsureSignedIn(provider); // interactive — a real browser sign-in, triggered by this explicit user gesture
                var enrollment = _services.CreateDeviceEnrollmentService(provider);

                if (!enrollment.CloudVaultExists())
                {
                    return (RestoreOutcome.NoVaultFound, (string?)null);
                }

                // Join flow: verify the password against the cloud-published
                // slot BEFORE touching this device's own vault record, so a
                // wrong password leaves nothing behind to roll back.
                enrollment.JoinExistingVault(masterPassword);

                // This device still gets its own local vault record and its
                // own recovery key — recovery is per-device by design, and
                // EstablishRecoverySlot simply wraps the ALREADY-SHARED VMK
                // (just unwrapped above) under that new recovery key too.
                var newRecoveryKey = _services.VaultService.CreateVault();
                _services.VaultRecovery.EstablishRecoverySlot(newRecoveryKey, masterPassword);
                _services.Settings.Save(_services.Settings.Get() with { CloudSyncProvider = provider });

                // The key slot only carried the wrapped VMK — now that this
                // device shares it, pull down the actual credential/tag/
                // attachment data to finish the restore.
                _services.CreateSyncService(provider).SyncNow();
                _services.CreateAttachmentBlobSyncService(provider).SyncBlobs();

                // Icon IMAGE bytes (favicons/custom images) are local-only
                // and never traveled through sync — only the credential's
                // Icon.Kind metadata did. Re-fetch what can be re-fetched
                // (anything with a Website field) and fall back to the
                // default look for anything that can't be.
                _services.CredentialIcons.RepairMissingImagesAsync(masterPassword).GetAwaiter().GetResult();

                return (RestoreOutcome.Restored, (string?)newRecoveryKey);
            });

            if (outcome == RestoreOutcome.NoVaultFound)
            {
                CloudRestoreErrorText.Text = $"No vault was found in this {provider} account.";
                return;
            }

            StartupRegistration.Enable();
            ShowRecoveryPanel("Vault restored", recoveryKey!);
        }
        catch (CloudAuthenticationException ex)
        {
            CloudRestoreErrorText.Text = $"Sign-in to {provider} failed: {ex.Message}";
        }
        catch (VaultDecryptionFailedException)
        {
            CloudRestoreErrorText.Text = "Incorrect master password for this vault.";
        }
        catch (VaultAlreadyExistsException)
        {
            CloudRestoreErrorText.Text = "A vault already exists on this device.";
        }
        catch (VaultStorageException ex)
        {
            CloudRestoreErrorText.Text = $"Could not restore the vault: {ex.Message}";
        }
        catch (Exception ex) when (ex is SyncUnavailableException or DropboxNetworkException or DropboxUnauthorizedException or DropboxSyncFailedException
            or GoogleDriveNetworkException or GoogleDriveUnauthorizedException or GoogleDriveSyncFailedException
            or PCloudNetworkException or PCloudUnauthorizedException or PCloudSyncFailedException)
        {
            CloudRestoreErrorText.Text = $"Restore failed: {ex.Message}";
        }
        finally
        {
            RestoreButton.IsEnabled = true;
            CloudRestoreBackButton.IsEnabled = true;
            RestoreProgressRing.Visibility = Visibility.Collapsed;
            RestoreStatusText.Text = string.Empty;
        }
    }

    private void ShowRecoveryPanel(string title, string recoveryKey)
    {
        RecoveryTitleText.Text = title;
        RecoveryKeyText.Text = recoveryKey;
        ShowPanel(RecoveryPanel);
        // Focusing this button (rather than leaving focus wherever it was on
        // the now-hidden panel) means Enter activates it via WPF's normal
        // focused-button behavior — landing on Copy, not Continue, so
        // pressing Enter here can't skip past the key before it's saved
        // somewhere. Deferred to Loaded priority: calling Focus()
        // synchronously right after Visibility = Visible silently fails
        // (confirmed — HasKeyboardFocus stayed false) because the button
        // hasn't been through a layout pass yet and isn't considered
        // focusable until then.
        Dispatcher.BeginInvoke(() => CopyToClipboardButton.Focus(), DispatcherPriority.Loaded);
    }

    private void ShowPanel(UIElement panel)
    {
        foreach (var candidate in new UIElement[] { ChoicePanel, CreatePanel, CloudProviderPanel, CloudRestorePanel, RecoveryPanel })
        {
            candidate.Visibility = candidate == panel ? Visibility.Visible : Visibility.Collapsed;
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
