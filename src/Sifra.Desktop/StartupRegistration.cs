using Microsoft.Win32;

namespace Sifra.Desktop;

/// <summary>
/// Registers/unregisters Sifra to launch automatically at Windows sign-in,
/// via the per-user Run key (HKCU) — no elevation needed, and it's removed
/// automatically if the user uninstalls by just deleting the app folder
/// (unlike a scheduled task, which would be orphaned). The registered
/// command line always includes the "--minimized" flag (see App.OnStartup)
/// so an auto-launch at login never pops a window — it starts silently,
/// tray icon only, per product decision.
///
/// The registry key IS the source of truth for "is this on" — there is no
/// separate AppSettings flag to keep in sync, so Options' checkbox and
/// SetupView's one-time default-enable can't ever drift from what Windows
/// will actually do at next sign-in.
/// </summary>
public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Sifra";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is not null;
    }

    /// <summary>
    /// No-op if the current process has no resolvable executable path
    /// (e.g. `dotnet run` during development — Environment.ProcessPath
    /// then points at dotnet.exe itself, which would be wrong to register;
    /// this only does something meaningful for a real published .exe).
    /// </summary>
    public static void Enable()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath) || !exePath.EndsWith("Sifra.Desktop.exe", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        key.SetValue(ValueName, $"\"{exePath}\" --minimized");
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
