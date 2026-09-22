using System.IO;
using System.Net.Http;
using Sifra.Vault;
using Sifra.Vault.Attachments;
using Sifra.Vault.Audit;
using Sifra.Vault.Auth;
using Sifra.Vault.Credentials;
using Sifra.Vault.Crypto;
using Sifra.Vault.Devices;
using Sifra.Vault.Dropbox;
using Sifra.Vault.GoogleDrive;
using Sifra.Vault.PCloud;
using Sifra.Vault.Health;
using Sifra.Vault.Session;
using Sifra.Vault.Settings;
using Sifra.Vault.Sync;
using Sifra.Vault.Tags;

namespace Sifra.Desktop;

/// <summary>
/// Wires up the same Sifra.Vault services the CLI uses, pointed at the
/// real default data directory (%AppData%\Sifra) — this app is a UI on
/// top of the existing vault, not a second implementation of it.
/// </summary>
public sealed class AppServices
{
    public VaultStore VaultStore { get; }
    public VaultService VaultService { get; }
    public VaultRecoveryService VaultRecovery { get; }
    public VaultAuthenticator VaultAuth { get; }
    public VaultMasterKeyStore MasterKeyStore { get; }
    public VaultEncryptionService Encryption { get; }
    public CredentialService Credentials { get; }
    public CredentialAttachmentService Attachments { get; }
    public PasswordHistoryService PasswordHistory { get; }
    public CredentialIconService CredentialIcons { get; }
    public TagService Tags { get; }
    public PasswordHealthService PasswordHealth { get; }
    public IBreachChecker BreachChecker { get; } = new HibpBreachChecker(new HttpClient());
    public AppSettingsStore Settings { get; } = new(null);
    public DeviceIdentityService Devices { get; }
    public VaultIdentityStore IdentityStore { get; } = new(null);

    /// <summary>Non-null only when .secrets/dropbox.json was found — see FindDropboxAppKey. Null means Dropbox is simply not configured on this machine.</summary>
    public DropboxAuthProvider? DropboxAuth { get; }

    /// <summary>Non-null only when .secrets/google-oauth.json was found — see FindGoogleOAuthSecrets. Null means Google Drive is simply not configured on this machine.</summary>
    public GoogleDriveAuthProvider? GoogleDriveAuth { get; }

    /// <summary>Non-null only when .secrets/pcloud.json was found — see FindPCloudSecrets. Null means pCloud is simply not configured on this machine.</summary>
    public PCloudAuthProvider? PCloudAuth { get; }

    /// <summary>Which real sync provider this vault is configured to use, if any — see AppSettings.CloudSyncProvider's remarks on why this is a one-time-at-Setup choice, not a runtime toggle.</summary>
    public CloudSyncProviderKind? ActiveSyncProvider => Settings.Get().CloudSyncProvider;

    private readonly AuditLogger _auditLogger;
    private readonly CredentialStore _credentialStore;
    private readonly CredentialTombstoneStore _credentialTombstoneStore;
    private readonly TagStore _tagStore;
    private readonly AttachmentStore _attachmentStore;
    private readonly AttachmentTombstoneStore _attachmentTombstoneStore;

    public AppServices()
    {
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sifra");

        var auditLogger = new AuditLogger(
            new FileAuditLogSink(Path.Combine(dataDirectory, "operations.log")),
            new LocalFakeAdminAlertSink());
        _auditLogger = auditLogger;

        VaultStore = new VaultStore(null);
        VaultService = new VaultService(VaultStore, auditLogger);
        VaultAuth = new VaultAuthenticator(
            new VaultAccessCredentialStore(null),
            new FileAccessAuditLog(Path.Combine(dataDirectory, "vault-access.log")),
            auditLogger);
        MasterKeyStore = new VaultMasterKeyStore(null);
        Encryption = new VaultEncryptionService(MasterKeyStore);
        VaultRecovery = new VaultRecoveryService(VaultService, VaultAuth, Encryption, auditLogger, IdentityStore);
        PasswordHistory = new PasswordHistoryService(
            new PasswordHistoryStore(null), new VaultEncryptionService(new VaultMasterKeyStore(null)));
        _credentialStore = new CredentialStore(null);
        _credentialTombstoneStore = new CredentialTombstoneStore(null);
        Credentials = new CredentialService(
            _credentialStore, Encryption,
            new TextCopyCredentialClipboard(), auditLogger, PasswordHistory, _credentialTombstoneStore);
        _attachmentStore = new AttachmentStore(null);
        _attachmentTombstoneStore = new AttachmentTombstoneStore(null);
        Attachments = new CredentialAttachmentService(
            _attachmentStore, new VaultEncryptionService(new VaultMasterKeyStore(null)), auditLogger, _attachmentTombstoneStore);
        CredentialIcons = new CredentialIconService(
            Credentials, new CredentialIconImageStore(null), new GoogleFaviconFetcher(new HttpClient()));
        _tagStore = new TagStore(null);
        Tags = new TagService(_tagStore);
        PasswordHealth = new PasswordHealthService(Credentials, auditLogger);
        Devices = new DeviceIdentityService(new DeviceRegistryStore(null), auditLogger);

        var dropboxAppKey = FindDropboxAppKey();
        DropboxAuth = dropboxAppKey is null ? null : new DropboxAuthProvider(dropboxAppKey);

        var googleSecrets = FindGoogleOAuthSecrets();
        GoogleDriveAuth = googleSecrets is null
            ? null
            : new GoogleDriveAuthProvider(googleSecrets.Value.ClientId, googleSecrets.Value.ClientSecret, Path.Combine(dataDirectory, "google-token-cache"));

        var pcloudSecrets = FindPCloudSecrets();
        PCloudAuth = pcloudSecrets is null ? null : new PCloudAuthProvider(pcloudSecrets.Value.ClientId, pcloudSecrets.Value.ClientSecret);
    }

    /// <summary>
    /// Builds MasterPasswordService wired to re-publish the wrapped VMK to
    /// this vault's active cloud sync provider whenever the password
    /// changes, if one is configured and currently reachable — otherwise
    /// behaves exactly like a local-only vault. Uses a SILENT sign-in
    /// attempt only (never opens a browser) since a password change is not
    /// the kind of user gesture that should trigger an unprompted OAuth
    /// popup; a cloud that's merely unreachable right now just means the
    /// republish is skipped, same tolerance MasterPasswordService already
    /// has for a genuinely unreachable cloud (see its remarks).
    /// </summary>
    public MasterPasswordService CreateMasterPasswordService()
    {
        var cloudKeySlotStore = ActiveSyncProvider is { } provider && TrySilentSignIn(provider)
            ? CreateCloudKeySlotStore(provider)
            : null;
        return new MasterPasswordService(VaultAuth, Encryption, _auditLogger, cloudKeySlotStore);
    }

    // ----- Per-provider auth -----

    public ICloudAuthProvider? GetAuthProvider(CloudSyncProviderKind kind) => kind switch
    {
        CloudSyncProviderKind.Dropbox => DropboxAuth,
        CloudSyncProviderKind.GoogleDrive => GoogleDriveAuth,
        CloudSyncProviderKind.PCloud => PCloudAuth,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public bool IsProviderConfigured(CloudSyncProviderKind kind) => GetAuthProvider(kind) is not null;

    /// <summary>
    /// Ensures the given provider is signed in — tries a silent reconnect
    /// first, and only falls back to the interactive browser flow if that
    /// fails or nothing was ever saved. Call only from a user-gesture-driven
    /// path (e.g. a Sync button click) — SyncScheduler's background timer
    /// deliberately calls TrySilentSignIn directly instead, so an idle timer
    /// can never pop a browser unprompted.
    /// </summary>
    /// <exception cref="InvalidOperationException">This provider is not configured on this machine.</exception>
    /// <exception cref="Sifra.Vault.Auth.CloudAuthenticationException">Interactive sign-in failed.</exception>
    public void EnsureSignedIn(CloudSyncProviderKind kind)
    {
        switch (kind)
        {
            case CloudSyncProviderKind.Dropbox:
                var dropbox = RequireDropboxAuth();
                if (!dropbox.IsAuthenticated && !dropbox.TrySilentSignIn())
                {
                    dropbox.SignIn("dropbox-account");
                }
                break;
            case CloudSyncProviderKind.GoogleDrive:
                var drive = RequireGoogleDriveAuth();
                if (!drive.IsAuthenticated && !drive.TrySilentSignIn())
                {
                    drive.SignIn("google-drive-account");
                }
                break;
            case CloudSyncProviderKind.PCloud:
                var pcloud = RequirePCloudAuth();
                if (!pcloud.IsAuthenticated && !pcloud.TrySilentSignIn())
                {
                    pcloud.SignIn("pcloud-account");
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    /// <summary>Silent-only reconnect — never opens a browser. Returns false (rather than throwing) if the provider isn't configured.</summary>
    public bool TrySilentSignIn(CloudSyncProviderKind kind) => kind switch
    {
        CloudSyncProviderKind.Dropbox => DropboxAuth is { } d && (d.IsAuthenticated || d.TrySilentSignIn()),
        CloudSyncProviderKind.GoogleDrive => GoogleDriveAuth is { } g && (g.IsAuthenticated || g.TrySilentSignIn()),
        CloudSyncProviderKind.PCloud => PCloudAuth is { } p && (p.IsAuthenticated || p.TrySilentSignIn()),
        _ => false,
    };

    // ----- Per-provider sync services -----

    /// <exception cref="InvalidOperationException">This provider is not configured or not signed in yet.</exception>
    public VaultSyncService CreateSyncService(CloudSyncProviderKind kind) =>
        new(_credentialStore, _credentialTombstoneStore, _tagStore, _attachmentStore, _attachmentTombstoneStore, CreateEnvelopeCloudStore(kind), _auditLogger, IdentityStore);

    /// <summary>Run this AFTER CreateSyncService(kind).SyncNow() so any newly-pulled-in attachment metadata already exists locally before this looks for its content.</summary>
    /// <exception cref="InvalidOperationException">This provider is not configured or not signed in yet.</exception>
    public AttachmentBlobSyncService CreateAttachmentBlobSyncService(CloudSyncProviderKind kind) =>
        new(_attachmentStore, _attachmentTombstoneStore, CreateBlobCloudStore(kind), _auditLogger);

    /// <exception cref="InvalidOperationException">This provider is not configured or not signed in yet.</exception>
    public IVaultEnvelopeCloudStore CreateEnvelopeCloudStore(CloudSyncProviderKind kind) => kind switch
    {
        CloudSyncProviderKind.Dropbox => new DropboxVaultEnvelopeCloudStore(new RealDropboxEnvelopeApiClient(RequireSignedInDropboxAuth().CreateClient()), DropboxAuth!),
        CloudSyncProviderKind.GoogleDrive => new GoogleDriveVaultEnvelopeCloudStore(new RealGoogleDriveEnvelopeApiClient(RequireSignedInGoogleDriveAuth().Credential!), GoogleDriveAuth!),
        CloudSyncProviderKind.PCloud => new PCloudVaultEnvelopeCloudStore(CreateRealPCloudApiClient(), PCloudAuth!),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <exception cref="InvalidOperationException">This provider is not configured or not signed in yet.</exception>
    public ICloudBlobStore CreateBlobCloudStore(CloudSyncProviderKind kind) => kind switch
    {
        CloudSyncProviderKind.Dropbox => new DropboxCloudBlobStore(new RealDropboxBlobApiClient(RequireSignedInDropboxAuth().CreateClient()), DropboxAuth!),
        CloudSyncProviderKind.GoogleDrive => new GoogleDriveCloudBlobStore(new RealGoogleDriveBlobApiClient(RequireSignedInGoogleDriveAuth().Credential!), GoogleDriveAuth!),
        CloudSyncProviderKind.PCloud => new PCloudCloudBlobStore(CreateRealPCloudApiClient(), PCloudAuth!),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>
    /// The OAuth-API-based replacement for the old filesystem-folder join
    /// mechanism (FileCloudKeySlotStore) — publishes/fetches the wrapped
    /// master-password slot through the SAME provider account already used
    /// for credential sync, so a brand-new device can recover this vault
    /// via provider sign-in alone. See VaultDeviceEnrollmentService, which
    /// depends only on the ICloudKeySlotStore interface and is unaffected
    /// by which concrete store this returns.
    /// </summary>
    /// <exception cref="InvalidOperationException">This provider is not configured or not signed in yet.</exception>
    public ICloudKeySlotStore CreateCloudKeySlotStore(CloudSyncProviderKind kind) => kind switch
    {
        CloudSyncProviderKind.Dropbox => new DropboxCloudKeySlotStore(new RealDropboxBlobApiClient(RequireSignedInDropboxAuth().CreateClient()), DropboxAuth!),
        CloudSyncProviderKind.GoogleDrive => new GoogleDriveCloudKeySlotStore(new RealGoogleDriveBlobApiClient(RequireSignedInGoogleDriveAuth().Credential!), GoogleDriveAuth!),
        CloudSyncProviderKind.PCloud => new PCloudCloudKeySlotStore(CreateRealPCloudApiClient(), PCloudAuth!),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <exception cref="InvalidOperationException">This provider is not configured or not signed in yet.</exception>
    public VaultDeviceEnrollmentService CreateDeviceEnrollmentService(CloudSyncProviderKind kind) =>
        new(MasterKeyStore, Encryption, VaultAuth, CreateCloudKeySlotStore(kind), _auditLogger, IdentityStore);

    // ----- Active-provider convenience (what the Sync button/SyncScheduler actually use) -----

    /// <exception cref="InvalidOperationException">No cloud sync provider is configured for this vault.</exception>
    public void EnsureActiveProviderSignedIn() => EnsureSignedIn(RequireActiveSyncProvider());

    /// <summary>Silent-only — never opens a browser. Returns false if no provider is configured.</summary>
    public bool TrySilentSignInActiveProvider() => ActiveSyncProvider is { } provider && TrySilentSignIn(provider);

    /// <exception cref="InvalidOperationException">No cloud sync provider is configured, or it's not signed in yet.</exception>
    public VaultSyncService CreateActiveSyncService() => CreateSyncService(RequireActiveSyncProvider());

    /// <exception cref="InvalidOperationException">No cloud sync provider is configured, or it's not signed in yet.</exception>
    public AttachmentBlobSyncService CreateActiveAttachmentBlobSyncService() => CreateAttachmentBlobSyncService(RequireActiveSyncProvider());

    /// <summary>
    /// Migrates this vault's cloud data from its currently active provider
    /// to a different one — see VaultProviderMigrationService's remarks,
    /// especially the per-device caveat. Both providers must already be
    /// signed in (call EnsureSignedIn for each first). On success, updates
    /// Settings.CloudSyncProvider to the new provider — the caller is
    /// responsible for telling the user to switch every OTHER device too.
    /// </summary>
    /// <exception cref="InvalidOperationException">No active provider is configured, or either provider is not signed in yet.</exception>
    public VaultMigrationResult MigrateActiveSyncProviderTo(CloudSyncProviderKind destinationKind)
    {
        var sourceKind = RequireActiveSyncProvider();
        var result = new VaultProviderMigrationService().Migrate(
            CreateEnvelopeCloudStore(sourceKind), CreateBlobCloudStore(sourceKind),
            CreateEnvelopeCloudStore(destinationKind), CreateBlobCloudStore(destinationKind));

        if (result.Outcome == VaultMigrationOutcome.Migrated)
        {
            // Carries the key slot over too — this device already holds the
            // local master-password slot (it's the one migrating), so this
            // is a fresh publish to the destination, not a transfer of
            // anything from the source's key slot store.
            CreateDeviceEnrollmentService(destinationKind).PublishMasterPasswordSlot();
            Settings.Save(Settings.Get() with { CloudSyncProvider = destinationKind });
        }

        return result;
    }

    /// <summary>
    /// Turns on cloud sync for a vault that currently has none configured —
    /// the counterpart to MigrateActiveSyncProviderTo, which instead moves
    /// an already-syncing vault between providers. Publishes this device's
    /// current vault data to the newly-chosen provider as the initial sync.
    /// </summary>
    /// <exception cref="InvalidOperationException">A sync provider is already configured for this vault (use MigrateActiveSyncProviderTo), or the chosen provider is not configured on this machine.</exception>
    /// <exception cref="Sifra.Vault.Auth.CloudAuthenticationException">Interactive sign-in failed.</exception>
    public void EnableSyncProvider(CloudSyncProviderKind kind)
    {
        if (ActiveSyncProvider is not null)
        {
            throw new InvalidOperationException("This vault already has a cloud sync provider configured. Use Migrate instead.");
        }

        EnsureSignedIn(kind); // interactive — called from a Setup/Options user gesture
        Settings.Save(Settings.Get() with { CloudSyncProvider = kind });
        // Publishes the wrapped master-password slot alongside the data —
        // this is what makes a future fresh install able to recover this
        // vault via provider sign-in alone (see CreateDeviceEnrollmentService's
        // remarks). An unlocked vault always has a local slot to publish.
        CreateDeviceEnrollmentService(kind).PublishMasterPasswordSlot();
        CreateSyncService(kind).SyncNow();
        CreateAttachmentBlobSyncService(kind).SyncBlobs();
    }

    private CloudSyncProviderKind RequireActiveSyncProvider() =>
        ActiveSyncProvider ?? throw new InvalidOperationException("No cloud sync provider is configured for this vault.");

    private DropboxAuthProvider RequireDropboxAuth() =>
        DropboxAuth ?? throw new InvalidOperationException("Dropbox is not configured on this machine (.secrets/dropbox.json was not found).");

    private DropboxAuthProvider RequireSignedInDropboxAuth()
    {
        var auth = RequireDropboxAuth();
        if (!auth.IsAuthenticated)
        {
            throw new InvalidOperationException("Not signed in to Dropbox yet.");
        }
        return auth;
    }

    private GoogleDriveAuthProvider RequireGoogleDriveAuth() =>
        GoogleDriveAuth ?? throw new InvalidOperationException("Google Drive is not configured on this machine (.secrets/google-oauth.json was not found).");

    private GoogleDriveAuthProvider RequireSignedInGoogleDriveAuth()
    {
        var auth = RequireGoogleDriveAuth();
        if (!auth.IsAuthenticated)
        {
            throw new InvalidOperationException("Not signed in to Google Drive yet.");
        }
        return auth;
    }

    private PCloudAuthProvider RequirePCloudAuth() =>
        PCloudAuth ?? throw new InvalidOperationException("pCloud is not configured on this machine (.secrets/pcloud.json was not found).");

    private PCloudAuthProvider RequireSignedInPCloudAuth()
    {
        var auth = RequirePCloudAuth();
        if (!auth.IsAuthenticated)
        {
            throw new InvalidOperationException("Not signed in to pCloud yet.");
        }
        return auth;
    }

    private RealPCloudApiClient CreateRealPCloudApiClient()
    {
        var auth = RequireSignedInPCloudAuth();
        return new RealPCloudApiClient(auth.AccessToken!, auth.ApiHost!);
    }

    /// <summary>
    /// Walks up from the running app's directory looking for
    /// .secrets/dropbox.json (holding an "appKey" property) — same
    /// convention Sifra.Cli already uses for its own live-Dropbox demo, so
    /// one secrets file under the repo checkout works for both.
    /// </summary>
    private static string? FindDropboxAppKey()
    {
        var candidate = FindSecretsFile("dropbox.json");
        if (candidate is null)
        {
            return null;
        }
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(candidate));
        return doc.RootElement.GetProperty("appKey").GetString();
    }

    /// <summary>Same convention as FindDropboxAppKey, for .secrets/google-oauth.json (holding "clientId" and "clientSecret").</summary>
    private static (string ClientId, string ClientSecret)? FindGoogleOAuthSecrets()
    {
        var candidate = FindSecretsFile("google-oauth.json");
        if (candidate is null)
        {
            return null;
        }
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(candidate));
        return (doc.RootElement.GetProperty("clientId").GetString()!, doc.RootElement.GetProperty("clientSecret").GetString()!);
    }

    /// <summary>Same convention as FindDropboxAppKey, for .secrets/pcloud.json (holding "clientId" and "clientSecret").</summary>
    private static (string ClientId, string ClientSecret)? FindPCloudSecrets()
    {
        var candidate = FindSecretsFile("pcloud.json");
        if (candidate is null)
        {
            return null;
        }
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(candidate));
        return (doc.RootElement.GetProperty("clientId").GetString()!, doc.RootElement.GetProperty("clientSecret").GetString()!);
    }

    private static string? FindSecretsFile(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir.FullName, ".secrets", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        return null;
    }
}
