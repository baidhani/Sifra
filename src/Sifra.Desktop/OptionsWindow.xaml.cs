using System.Windows;
using System.Windows.Controls;
using Sifra.Vault.Settings;
using Wpf.Ui.Controls;

namespace Sifra.Desktop;

/// <summary>App-wide preferences: auto-lock timeout, and an entry point to the Manage Extensions window.</summary>
public partial class OptionsWindow : FluentWindow
{
    // (minutes|null for "Never", display label)
    private static readonly (int? Minutes, string Label)[] AutoLockOptions =
    {
        (1, "1 minute"),
        (5, "5 minutes"),
        (15, "15 minutes"),
        (30, "30 minutes"),
        (null, "Never"),
    };

    private readonly AppServices _services;

    public event EventHandler? SettingsChanged;

    public OptionsWindow(AppServices services)
    {
        InitializeComponent();
        _services = services;

        var current = _services.Settings.Get();
        foreach (var (minutes, label) in AutoLockOptions)
        {
            AutoLockCombo.Items.Add(new ComboBoxItem { Content = label, Tag = minutes });
        }

        var selectedIndex = Array.FindIndex(AutoLockOptions, o => o.Minutes == current.AutoLockMinutes);
        AutoLockCombo.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 1; // default to 5 minutes if unrecognized

        // The registry Run key IS the source of truth here — see
        // StartupRegistration's remarks — so this reads it directly rather
        // than trusting any cached/remembered state.
        StartWithWindowsCheckBox.IsChecked = StartupRegistration.IsEnabled();
        KeepTrayIconVisibleCheckBox.IsChecked = current.KeepTrayIconVisibleWhenOpen;

        (current.ThemePreference switch
        {
            ThemePreferenceKind.Light => ThemeLightRadio,
            ThemePreferenceKind.Dark => ThemeDarkRadio,
            _ => ThemeSystemRadio,
        }).IsChecked = true;

        RefreshSyncProviderStatus();
    }

    private void RefreshSyncProviderStatus()
    {
        if (_services.ActiveSyncProvider is { } provider)
        {
            SyncProviderStatusText.Text = $"This vault syncs via {provider}.";
            EnableSyncProviderButton.Visibility = Visibility.Collapsed;
            MigrateSyncProviderButton.Visibility = Visibility.Visible;
        }
        else
        {
            SyncProviderStatusText.Text = "This vault is not syncing to any cloud provider.";
            EnableSyncProviderButton.Visibility = Visibility.Visible;
            MigrateSyncProviderButton.Visibility = Visibility.Collapsed;
        }
    }

    private void OnEnableSyncProviderClick(object sender, RoutedEventArgs e)
    {
        var window = new EnableSyncProviderWindow(_services) { Owner = this };
        if (window.ShowDialog() == true)
        {
            RefreshSyncProviderStatus();
            ThemedMessageBox.Show(this, "Cloud sync is enabled for this vault.", "Sifra");
        }
    }

    private void OnManageExtensionsClick(object sender, RoutedEventArgs e)
    {
        var window = new ManageExtensionsWindow(_services) { Owner = this };
        window.ShowDialog();
    }

    private void OnMigrateSyncProviderClick(object sender, RoutedEventArgs e)
    {
        var window = new MigrateSyncProviderWindow(_services) { Owner = this };
        if (window.ShowDialog() == true)
        {
            RefreshSyncProviderStatus();
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var selected = (ComboBoxItem)AutoLockCombo.SelectedItem;
        var minutes = (int?)selected.Tag;
        var themePreference = ThemeLightRadio.IsChecked == true ? ThemePreferenceKind.Light
            : ThemeDarkRadio.IsChecked == true ? ThemePreferenceKind.Dark
            : ThemePreferenceKind.System;

        // `with`, not `new AppSettings(...)` — the latter would silently
        // reset every other field (CloudSyncProvider, ...) back to its
        // default on every single Options save.
        _services.Settings.Save(_services.Settings.Get() with
        {
            AutoLockMinutes = minutes,
            KeepTrayIconVisibleWhenOpen = KeepTrayIconVisibleCheckBox.IsChecked == true,
            ThemePreference = themePreference,
        });

        // Applied immediately, not just on next launch.
        ThemeManager.Apply(themePreference switch
        {
            ThemePreferenceKind.Light => AppTheme.Light,
            ThemePreferenceKind.Dark => AppTheme.Dark,
            _ => AppTheme.System,
        });

        if (StartWithWindowsCheckBox.IsChecked == true)
        {
            StartupRegistration.Enable();
        }
        else
        {
            StartupRegistration.Disable();
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
