using System.Windows;
using System.Windows.Controls;
using Sifra.Vault.Devices;
using Wpf.Ui.Controls;

namespace Sifra.Desktop;

/// <summary>
/// Browser-extension device management (Phase 2's pairing feature) —
/// split into Active (revocable) and Revoked (history, no action) so the
/// list stays readable as revoked devices accumulate over time, rather
/// than one flat undifferentiated list. Reuses DeviceIdentityService
/// exactly as it was before this was pulled out of OptionsWindow.
/// </summary>
public partial class ManageExtensionsWindow : FluentWindow
{
    private readonly AppServices _services;

    public ManageExtensionsWindow(AppServices services)
    {
        InitializeComponent();
        _services = services;
        RefreshDevices();
    }

    private void RefreshDevices()
    {
        var devices = _services.Devices.ListDevices();
        var active = devices.Where(d => d.RevokedAtUtc is null).ToList();
        var revoked = devices.Where(d => d.RevokedAtUtc is not null).ToList();

        NoActiveText.Visibility = active.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ActiveDevicesList.ItemsSource = active.Select(BuildActiveRow).ToList();

        NoRevokedText.Visibility = revoked.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RevokedDevicesList.ItemsSource = revoked.Select(BuildRevokedRow).ToList();
    }

    private FrameworkElement BuildActiveRow(DeviceRecord device)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var info = new StackPanel();
        var nameText = new Wpf.Ui.Controls.TextBlock { Text = device.DeviceName, FontTypography = FontTypography.BodyStrong };
        nameText.SetResourceReference(Control.ForegroundProperty, "Sifra.TextPrimaryBrush");
        info.Children.Add(nameText);

        var statusText = new Wpf.Ui.Controls.TextBlock { Text = $"Paired {device.EnrolledAtUtc:g}", FontTypography = FontTypography.Caption };
        statusText.SetResourceReference(Control.ForegroundProperty, "Sifra.TextSecondaryBrush");
        info.Children.Add(statusText);

        Grid.SetColumn(info, 0);
        row.Children.Add(info);

        var revokeButton = new Wpf.Ui.Controls.Button { Content = "Revoke" };
        revokeButton.Click += (_, _) => OnRevokeDeviceClick(device);
        Grid.SetColumn(revokeButton, 1);
        row.Children.Add(revokeButton);

        return row;
    }

    // Revoked devices are history, not actionable — no Revoke button (it's
    // already revoked) and dimmed via the secondary text color throughout,
    // so the eye is drawn to Active where action is actually possible.
    private FrameworkElement BuildRevokedRow(DeviceRecord device)
    {
        var info = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

        var nameText = new Wpf.Ui.Controls.TextBlock { Text = device.DeviceName, FontTypography = FontTypography.Body };
        nameText.SetResourceReference(Control.ForegroundProperty, "Sifra.TextSecondaryBrush");
        info.Children.Add(nameText);

        var statusText = new Wpf.Ui.Controls.TextBlock { Text = $"Revoked {device.RevokedAtUtc:g}", FontTypography = FontTypography.Caption };
        statusText.SetResourceReference(Control.ForegroundProperty, "Sifra.TextSecondaryBrush");
        info.Children.Add(statusText);

        return info;
    }

    private void OnRevokeDeviceClick(DeviceRecord device)
    {
        var confirmed = ThemedMessageBox.ShowConfirm(
            this,
            $"Revoke \"{device.DeviceName}\"? It will immediately lose access to this vault.",
            "Sifra");

        if (!confirmed)
        {
            return;
        }

        _services.Devices.RevokeDevice(device.DeviceId);
        RefreshDevices();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
