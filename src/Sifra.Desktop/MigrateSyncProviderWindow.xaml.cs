using System.Windows;
using System.Windows.Controls;
using Sifra.Vault.Auth;
using Sifra.Vault.Dropbox;
using Sifra.Vault.GoogleDrive;
using Sifra.Vault.PCloud;
using Sifra.Vault.Settings;
using Sifra.Vault.Sync;
using Wpf.Ui.Controls;

namespace Sifra.Desktop;

/// <summary>
/// App-assisted migration between cloud sync providers — see
/// VaultProviderMigrationService's remarks, especially the per-device
/// caveat surfaced explicitly in this window's own warning text.
/// </summary>
public partial class MigrateSyncProviderWindow : FluentWindow
{
    private readonly AppServices _services;
    private readonly CloudSyncProviderKind _sourceProvider;
    private readonly List<RadioButton> _destinationRadios = [];

    public MigrateSyncProviderWindow(AppServices services)
    {
        InitializeComponent();
        _services = services;
        _sourceProvider = _services.ActiveSyncProvider
            ?? throw new InvalidOperationException("No active sync provider to migrate from.");

        CurrentProviderText.Text = $"This vault currently syncs via {_sourceProvider}.";

        foreach (var candidate in Enum.GetValues<CloudSyncProviderKind>())
        {
            if (candidate == _sourceProvider)
            {
                continue;
            }

            var radio = new RadioButton { GroupName = "DestinationProvider", Content = candidate.ToString(), Margin = new Thickness(0, 0, 16, 0), Tag = candidate };
            if (!_services.IsProviderConfigured(candidate))
            {
                radio.IsEnabled = false;
                radio.ToolTip = $"{candidate} is not configured on this machine.";
            }
            _destinationRadios.Add(radio);
            DestinationProviderPanel.Children.Add(radio);
        }

        var firstEnabled = _destinationRadios.FirstOrDefault(r => r.IsEnabled);
        if (firstEnabled is not null)
        {
            firstEnabled.IsChecked = true;
        }
        else
        {
            MigrateButton.IsEnabled = false;
            ErrorText.Text = "No other cloud provider is configured on this machine to migrate to.";
        }
    }

    private async void OnMigrateClick(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        var selectedRadio = _destinationRadios.FirstOrDefault(r => r.IsChecked == true);
        if (selectedRadio is null)
        {
            ErrorText.Text = "Choose a provider to migrate to.";
            return;
        }
        var destinationProvider = (CloudSyncProviderKind)selectedRadio.Tag;

        MigrateButton.IsEnabled = false;
        ProgressRing.Visibility = Visibility.Visible;
        try
        {
            var result = await Task.Run(() =>
            {
                _services.EnsureSignedIn(_sourceProvider); // should already be signed in; harmless if so
                _services.EnsureSignedIn(destinationProvider); // interactive if needed — the user just clicked Migrate, a legitimate gesture
                return _services.MigrateActiveSyncProviderTo(destinationProvider);
            });

            switch (result.Outcome)
            {
                case VaultMigrationOutcome.Migrated:
                    ThemedMessageBox.Show(
                        this, $"Migrated to {destinationProvider} ({result.AttachmentsTransferred} attachment(s) transferred).\n\nRemember: every other device sharing this vault must also be switched to {destinationProvider}.",
                        "Sifra");
                    DialogResult = true;
                    break;
                case VaultMigrationOutcome.NothingToMigrate:
                    ErrorText.Text = $"Nothing to migrate — {_sourceProvider} has no published vault yet.";
                    break;
                case VaultMigrationOutcome.DestinationAlreadyHasData:
                    ErrorText.Text = $"{destinationProvider} already has vault data on it — migration was refused to avoid overwriting it.";
                    break;
            }
        }
        catch (CloudAuthenticationException ex)
        {
            ErrorText.Text = $"Sign-in failed: {ex.Message}";
        }
        catch (Exception ex) when (ex is SyncUnavailableException or DropboxNetworkException or DropboxUnauthorizedException or DropboxSyncFailedException
            or GoogleDriveNetworkException or GoogleDriveUnauthorizedException or GoogleDriveSyncFailedException
            or PCloudNetworkException or PCloudUnauthorizedException or PCloudSyncFailedException)
        {
            ErrorText.Text = $"Migration failed: {ex.Message}";
        }
        finally
        {
            MigrateButton.IsEnabled = true;
            ProgressRing.Visibility = Visibility.Collapsed;
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
