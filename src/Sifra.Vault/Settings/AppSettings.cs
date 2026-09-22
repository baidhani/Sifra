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
/// <param name="KeepTrayIconVisibleWhenOpen">
/// True (the default) means the system tray icon stays visible even after
/// the window is restored from it — it only disappears when the user
/// explicitly chooses Exit from the tray menu. False restores the
/// simpler "tray icon only while hidden" behavior. Product decision:
/// default to staying visible, since a running Sifra instance (background
/// sync, tray Lock/Exit access) is meaningful state a user may want a
/// visible reminder of even while the window itself is open.
/// </param>
public sealed record AppSettings(
    int? AutoLockMinutes = 5,
    CloudSyncProviderKind? CloudSyncProvider = null,
    bool KeepTrayIconVisibleWhenOpen = true,
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
