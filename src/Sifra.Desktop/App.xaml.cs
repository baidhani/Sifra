using System.Linq;
using System.Windows;
using Sifra.Vault.Settings;

namespace Sifra.Desktop;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // A standalone AppSettingsStore rather than a full AppServices
        // here — the theme has to be applied before any window is
        // constructed, and reading one setting doesn't need the rest of
        // the vault services spun up yet.
        var themePreference = new AppSettingsStore(null).Get().ThemePreference;
        ThemeManager.Apply(themePreference switch
        {
            ThemePreferenceKind.Light => AppTheme.Light,
            ThemePreferenceKind.Dark => AppTheme.Dark,
            _ => AppTheme.System,
        });

        // --minimized is what StartupRegistration puts on the Run-key
        // command line — a silent auto-launch at Windows sign-in never
        // pops a window, it just starts hidden with a tray icon, per
        // product decision. ShutdownMode="OnExplicitShutdown" (App.xaml)
        // means this hidden, unshown window alone won't cause WPF to
        // think there are "no windows" and shut down early.
        var startMinimized = e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);
        var mainWindow = new MainWindow();
        if (startMinimized)
        {
            mainWindow.StartHiddenToTray();
        }
        else
        {
            mainWindow.Show();
        }
    }
}
