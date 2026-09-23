namespace Sifra.Vault.Settings;

/// <summary>
/// Local, plaintext app preferences — never secret data, so no encryption
/// (unlike Credential/CustomField). AutoLockMinutes null means "never".
///
/// CloudSyncProvider is which real
/// API-based provider (see CloudSyncProviderKind) this device's actual
/// credential/attachment sync (VaultSyncService/AttachmentBlobSyncService)
/// talks to. By product decision, a vault has exactly one active sync
/// provider at a time, chosen once at Setup — changing it later requires
/// an explicit, app-assisted migration (VaultProviderMigrationService),
/// not just editing this setting, since other devices sharing the vault
/// won't otherwise know the provider changed. Null means sync is not
/// configured (local-only vault).
/// </summary>
/// <param name="ShowTrayIcon">
/// True (the default) means the system tray icon appears the moment the
/// app starts and stays visible for the app's whole lifetime, regardless
/// of whether the main window is open, minimized, or hidden — it only
/// disappears when this setting is off, or the app actually exits. False
/// means no tray icon ever, at any point. Turning this off forces
/// CloseToTray off too (see its own remarks) — there'd be no icon left to
/// restore the window from otherwise.
/// </param>
/// <param name="CloseToTray">
/// True (the default) means the window's own close (X) button hides to
/// the tray instead of exiting, matching the previous, non-optional
/// behavior. False means the X button really exits the app, same as
/// choosing Exit from the tray menu. Turning ShowTrayIcon off forces this
/// off too, and turning this on forces ShowTrayIcon on — closing to tray
/// with no tray icon to click back from would strand the user with a
/// hidden, unreachable window.
/// </param>
public sealed record AppSettings(
    int? AutoLockMinutes = 5,
    CloudSyncProviderKind? CloudSyncProvider = null,
    bool ShowTrayIcon = true,
    bool CloseToTray = true,
    ThemePreferenceKind ThemePreference = ThemePreferenceKind.System);

public enum CloudSyncProviderKind
{
    Dropbox,
    GoogleDrive,
    PCloud,
}

/// <summary>
/// The user's explicit theme override. System (the default) means "follow
/// Windows' own light/dark setting" — Sifra.Desktop's ThemeManager already
/// does this automatically (including live-tracking a system theme change
/// while running), so Light/Dark here just pin the app to one regardless
/// of what Windows is set to.
/// </summary>
public enum ThemePreferenceKind
{
    System,
    Light,
    Dark,
}
