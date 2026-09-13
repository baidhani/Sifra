using System.Windows;
using System.Windows.Controls;
using Sifra.Vault.Settings;
using Wpf.Ui.Controls;

namespace Sifra.Desktop;

/// <summary>App-wide preferences. Currently just auto-lock timeout; more settings can join this same dialog later.</summary>
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
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var selected = (ComboBoxItem)AutoLockCombo.SelectedItem;
        var minutes = (int?)selected.Tag;

        _services.Settings.Save(new AppSettings(AutoLockMinutes: minutes));
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
