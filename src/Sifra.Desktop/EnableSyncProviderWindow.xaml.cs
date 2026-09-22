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
/// The post-Setup entry point for turning on cloud sync for a vault that
/// was created without it — Setup only offers the choice once, and
/// MigrateSyncProviderWindow requires an already-active provider to
/// migrate FROM, so neither covers "I skipped it and want it now."
/// </summary>
public partial class EnableSyncProviderWindow : FluentWindow
{
    private readonly AppServices _services;
    private readonly List<RadioButton> _providerRadios = [];

    public EnableSyncProviderWindow(AppServices services)
    {
        InitializeComponent();
        _services = services;

        foreach (var candidate in Enum.GetValues<CloudSyncProviderKind>())
        {
            var radio = new RadioButton { GroupName = "Provider", Content = candidate.ToString(), Margin = new Thickness(0, 0, 16, 0), Tag = candidate };
            if (!_services.IsProviderConfigured(candidate))
            {
                radio.IsEnabled = false;
                radio.ToolTip = $"{candidate} is not configured on this machine.";
            }
            _providerRadios.Add(radio);
            ProviderPanel.Children.Add(radio);
        }

        var firstEnabled = _providerRadios.FirstOrDefault(r => r.IsEnabled);
        if (firstEnabled is not null)
        {
            firstEnabled.IsChecked = true;
        }
        else
        {
            EnableButton.IsEnabled = false;
            ErrorText.Text = "No cloud provider is configured on this machine. See the app's setup docs for adding one.";
        }
    }

    private async void OnEnableClick(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        var selectedRadio = _providerRadios.FirstOrDefault(r => r.IsChecked == true);
        if (selectedRadio is null)
        {
            ErrorText.Text = "Choose a provider.";
            return;
        }
        var provider = (CloudSyncProviderKind)selectedRadio.Tag;

        EnableButton.IsEnabled = false;
        ProgressRing.Visibility = Visibility.Visible;
        try
        {
            await Task.Run(() => _services.EnableSyncProvider(provider));
            DialogResult = true;
        }
        catch (CloudAuthenticationException ex)
        {
            ErrorText.Text = $"Sign-in failed: {ex.Message}";
        }
        catch (Exception ex) when (ex is SyncUnavailableException or DropboxNetworkException or DropboxUnauthorizedException or DropboxSyncFailedException
            or GoogleDriveNetworkException or GoogleDriveUnauthorizedException or GoogleDriveSyncFailedException
            or PCloudNetworkException or PCloudUnauthorizedException or PCloudSyncFailedException)
        {
            ErrorText.Text = $"Could not enable sync: {ex.Message}";
        }
        finally
        {
            EnableButton.IsEnabled = true;
            ProgressRing.Visibility = Visibility.Collapsed;
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
