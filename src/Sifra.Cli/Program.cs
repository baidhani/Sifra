using Sifra.Vault;
using Sifra.Vault.Audit;
using Sifra.Vault.Auth;
using Sifra.Vault.Credentials;
using Sifra.Vault.Crypto;
using Sifra.Vault.Devices;
using Sifra.Vault.Dropbox;
using Sifra.Vault.GoogleDrive;
using Sifra.Vault.Health;
using Sifra.Vault.Sharing;
using Sifra.Vault.OneDrive;
using Sifra.Vault.Passwords;
using Sifra.Vault.Session;
using Sifra.Vault.Sync;

// Optional first argument overrides where vault data lives — useful for
// demos so they never touch a real user's actual application-data folder.
string? dataDirectory = args.Length > 0 ? args[0] : null;
var resolvedDataDirectory = dataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sifra");

var operationsLogPath = Path.Combine(resolvedDataDirectory, "operations.log");
var adminAlerts = new LocalFakeAdminAlertSink();
var auditLogger = new AuditLogger(new FileAuditLogSink(operationsLogPath), adminAlerts);

var store = new VaultStore(dataDirectory);
var service = new VaultService(store, auditLogger);

var vaultLogPath = Path.Combine(resolvedDataDirectory, "vault-access.log");
var vaultAuth = new VaultAuthenticator(new VaultAccessCredentialStore(dataDirectory), new FileAccessAuditLog(vaultLogPath), auditLogger);
var vaultRecovery = new VaultRecoveryService(service, vaultAuth, new VaultEncryptionService(new VaultMasterKeyStore(dataDirectory)), auditLogger);

string? demoRecoveryKey = null;
try
{
    demoRecoveryKey = service.CreateVault();
    Console.WriteLine("Vault created.");
    Console.WriteLine();
    Console.WriteLine("Your recovery key (shown once — save it somewhere safe now):");
    Console.WriteLine(demoRecoveryKey);

    // Links the recovery key to the vault master key while it's still in
    // plaintext (STORY-006) and sets the initial master password.
    vaultRecovery.EstablishRecoverySlot(demoRecoveryKey, "demo-password");
}
catch (VaultAlreadyExistsException)
{
    Console.WriteLine("A vault already exists on this device. Use the existing vault instead of creating a new one.");
}
catch (VaultStorageException ex)
{
    Console.WriteLine("Could not create the vault because of a storage error:");
    Console.WriteLine(ex.Message);
}

Console.WriteLine();
Console.WriteLine("--- STORY-013: cloud auth and vault auth are separate ---");

vaultAuth.SetCredential("demo-password"); // idempotent — already set by EstablishRecoverySlot above

var cloud = new LocalFakeCloudAuthProvider();

cloud.SignIn("demo@example.com");
Console.WriteLine($"Cloud signed in. Vault authenticate still required and succeeds: {vaultAuth.Authenticate("demo-password")}");

cloud.SignOut();
Console.WriteLine($"Cloud signed out. Vault authenticate is unaffected and still succeeds: {vaultAuth.Authenticate("demo-password")}");

Console.WriteLine($"Wrong vault credential is rejected: {vaultAuth.Authenticate("not-the-password")}");

Console.WriteLine();
Console.WriteLine("--- STORY-014: local-first operation with offline usability ---");

var syncProvider = new LocalFakeCloudSyncProvider(); // starts disconnected — offline
var dataService = new VaultDataService(new VaultDataStore(dataDirectory), new OfflineChangeQueueStore(dataDirectory), syncProvider, auditLogger);

Console.WriteLine($"Offline (connected={syncProvider.IsConnected}). Editing a vault data item...");
dataService.Edit(new VaultDataItem("note-1", "buy milk", DateTimeOffset.UtcNow));
Console.WriteLine($"View works offline: {dataService.View().Count} item(s) — \"{dataService.View()[0].Content}\"");
Console.WriteLine($"Queued for sync: {dataService.PendingChanges().Count} pending change(s)");

try
{
    dataService.SyncNow();
}
catch (SyncUnavailableException ex)
{
    Console.WriteLine($"Sync attempt while offline correctly failed: {ex.Message}");
}

Console.WriteLine("Reconnecting...");
syncProvider.IsConnected = true;
dataService.SyncNow();
Console.WriteLine($"After sync: {dataService.PendingChanges().Count} pending change(s), {syncProvider.PushedChanges.Count} pushed to the cloud");

Console.WriteLine();
Console.WriteLine("--- STORY-002: add, view, edit, delete, search credentials + clipboard copy ---");

var credentials = new CredentialService(
    new CredentialStore(dataDirectory),
    new VaultEncryptionService(new VaultMasterKeyStore(dataDirectory)),
    new TextCopyCredentialClipboard(),
    auditLogger);

const string vaultCredential = "demo-password"; // same credential just proven via vaultAuth.Authenticate above

var credentialId = credentials.Add(vaultCredential, "GitHub", "firas", "hunter2", "https://github.com");
Console.WriteLine($"Added credential. List now shows {credentials.List(vaultCredential).Count} item(s).");

var rawCredentialsFile = File.ReadAllText(Path.Combine(resolvedDataDirectory, "credentials.json"));
Console.WriteLine($"On disk (ciphertext only, no plaintext secrets): {rawCredentialsFile}");

credentials.CopyUsername(vaultCredential, credentialId);
Console.WriteLine("Copied username to clipboard.");
credentials.CopyPassword(vaultCredential, credentialId);
Console.WriteLine("Copied password to clipboard.");

try
{
    credentials.CopyUsername(vaultCredential, "does-not-exist");
}
catch (CredentialNotFoundException ex)
{
    Console.WriteLine($"Copy of a non-existent credential correctly failed: {ex.Message}");
}

var searchResults = credentials.Search(vaultCredential, "git");
Console.WriteLine($"Search for \"git\" found {searchResults.Count} result(s): {searchResults[0].Label}");

credentials.Delete(credentialId);
Console.WriteLine($"Deleted credential. List now shows {credentials.List(vaultCredential).Count} item(s).");

Console.WriteLine();
Console.WriteLine("--- STORY-003: generate strong passwords ---");

var passwordGenerator = new PasswordGenerator(auditLogger: auditLogger);
var generatedPassword = passwordGenerator.Generate(16);
Console.WriteLine($"Generated a {generatedPassword.Length}-character password (value withheld from this log, never persisted to the audit trail).");

try
{
    passwordGenerator.Generate(3); // below PasswordGenerator.MinLength
}
catch (UnsupportedPasswordLengthException ex)
{
    Console.WriteLine($"Invalid length correctly rejected: {ex.Message}");
}

Console.WriteLine();
Console.WriteLine("--- STORY-004: lock and unlock vault ---");

var freshSession = new VaultSession(vaultAuth); // simulates a freshly (re)opened application
Console.WriteLine($"On (re)open: {freshSession.State} — must unlock before credentials are accessible.");

try
{
    freshSession.RequireUnlockedCredential();
}
catch (VaultLockedException ex)
{
    Console.WriteLine($"Access while locked correctly refused: {ex.Message}");
}

Console.WriteLine($"Unlock with wrong password: {freshSession.Unlock("not-the-password")} (state stays {freshSession.State})");

var secondCredentialId = credentials.Add("demo-password", "Gmail", "firas", "hunter3", "https://gmail.com");
Console.WriteLine($"Unlock with correct password: {freshSession.Unlock("demo-password")} (state now {freshSession.State})");
Console.WriteLine($"All credentials accessible after unlock: {credentials.List(freshSession.RequireUnlockedCredential()).Count} item(s).");

freshSession.Lock();
Console.WriteLine($"Locked again: {freshSession.State}.");
credentials.Delete(secondCredentialId); // tidy up so later demo sections still show one credential's worth of behavior consistently

Console.WriteLine();
Console.WriteLine("--- STORY-005: change master password (without re-encrypting credentials) ---");

var thirdCredentialId = credentials.Add("demo-password", "GitHub", "firas", "hunter2", "https://github.com");
var credentialsFileBeforeChange = File.ReadAllText(Path.Combine(resolvedDataDirectory, "credentials.json"));

var masterPasswordService = new MasterPasswordService(vaultAuth, new VaultEncryptionService(new VaultMasterKeyStore(dataDirectory)), auditLogger);

try
{
    masterPasswordService.ChangePassword("wrong-current-password", "new-demo-password-123");
}
catch (IncorrectMasterPasswordException ex)
{
    Console.WriteLine($"Change with wrong current password correctly refused: {ex.Message}");
}

try
{
    masterPasswordService.ChangePassword("demo-password", "short");
}
catch (WeakMasterPasswordException ex)
{
    Console.WriteLine($"Change to a too-short new password correctly refused: {ex.Message}");
}

masterPasswordService.ChangePassword("demo-password", "new-demo-password-123");
Console.WriteLine("Master password changed.");
Console.WriteLine($"Old password now rejected: {vaultAuth.Authenticate("demo-password")}");
Console.WriteLine($"New password accepted: {vaultAuth.Authenticate("new-demo-password-123")}");

var credentialsFileAfterChange = File.ReadAllText(Path.Combine(resolvedDataDirectory, "credentials.json"));
Console.WriteLine($"credentials.json unchanged by the password change (REQ-008 — no re-encryption): {credentialsFileBeforeChange == credentialsFileAfterChange}");

var viewWithNewPassword = credentials.GetById("new-demo-password-123", thirdCredentialId);
Console.WriteLine($"Existing credential still decrypts correctly under the new password: username=\"{viewWithNewPassword.Username}\"");

credentials.Delete(thirdCredentialId); // tidy up

Console.WriteLine();
Console.WriteLine("--- STORY-006: recover vault with recovery key ---");

var recoveryDemoCredentialId = credentials.Add("new-demo-password-123", "GitHub", "firas", "hunter2", "https://github.com");

if (demoRecoveryKey is not null)
{
    try
    {
        vaultRecovery.Recover("WRONG-RECOVERY-KEY-VALUE", "recovered-password-456");
    }
    catch (InvalidRecoveryKeyException ex)
    {
        Console.WriteLine($"Recovery with an invalid key correctly refused: {ex.Message}");
    }

    vaultRecovery.Recover(demoRecoveryKey, "recovered-password-456");
    Console.WriteLine("Vault recovered with a new master password.");
    Console.WriteLine($"Pre-recovery password now rejected: {vaultAuth.Authenticate("new-demo-password-123")}");
    Console.WriteLine($"New recovered password accepted: {vaultAuth.Authenticate("recovered-password-456")}");

    var recoveredView = credentials.GetById("recovered-password-456", recoveryDemoCredentialId);
    Console.WriteLine($"Existing credential still accessible after recovery: username=\"{recoveredView.Username}\"");

    try
    {
        vaultRecovery.Recover(demoRecoveryKey, "yet-another-password-789");
    }
    catch (RecoveryKeyExpiredException ex)
    {
        Console.WriteLine($"Reusing the same recovery key correctly refused: {ex.Message}");
    }
}

credentials.Delete(recoveryDemoCredentialId); // tidy up

Console.WriteLine();
Console.WriteLine("--- STORY-007: synchronize vault with Google Drive ---");

var secretsFile = FindGoogleOAuthSecretsFile();
if (secretsFile is null)
{
    Console.WriteLine("No .secrets/google-oauth.json found — skipping the live Google Drive demo.");
}
else
{
    using var secretsDoc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(secretsFile));
    var clientId = secretsDoc.RootElement.GetProperty("clientId").GetString()!;
    var clientSecret = secretsDoc.RootElement.GetProperty("clientSecret").GetString()!;

    var tokenStoreDir = Path.Combine(resolvedDataDirectory, "google-token-cache");
    var googleAuth = new GoogleDriveAuthProvider(clientId, clientSecret, tokenStoreDir);

    Console.WriteLine("Opening your browser to sign in to Google...");
    try
    {
        googleAuth.SignIn("firas.sql@gmail.com");
        Console.WriteLine($"Google sign-in succeeded. Connected: {googleAuth.IsAuthenticated}");

        using var driveApiClient = new RealGoogleDriveApiClient(googleAuth.Credential!);
        var googleSyncProvider = new GoogleDriveSyncProvider(
            driveApiClient,
            new VaultEncryptionService(new VaultMasterKeyStore(dataDirectory)),
            googleAuth,
            "recovered-password-456");

        var googleDataService = new VaultDataService(
            new VaultDataStore(dataDirectory),
            new OfflineChangeQueueStore(dataDirectory),
            googleSyncProvider,
            auditLogger);

        googleDataService.Edit(new VaultDataItem("google-sync-demo", "real Google Drive sync test", DateTimeOffset.UtcNow));
        Console.WriteLine($"Queued for sync: {googleDataService.PendingChanges().Count} pending change(s)");

        googleDataService.SyncNow();
        Console.WriteLine("Synced to REAL Google Drive successfully.");
        Console.WriteLine($"Pending changes remaining: {googleDataService.PendingChanges().Count}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Live Google Drive demo did not complete: {ex.GetType().Name}: {ex.Message}");
    }
}

Console.WriteLine();
Console.WriteLine("--- STORY-008: synchronize vault with OneDrive and Dropbox ---");
Console.WriteLine("OneDrive: OneDriveSyncProvider/OneDriveAuthProvider are built and unit-tested against a fake");
Console.WriteLine("(no Azure tenant was available for this account — flagged as unverified against the real service).");

var dropboxSecretsFile = FindDropboxSecretsFile();
if (dropboxSecretsFile is null)
{
    Console.WriteLine("No .secrets/dropbox.json found — skipping the live Dropbox demo.");
}
else
{
    using var dropboxSecretsDoc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(dropboxSecretsFile));
    var dropboxAppKey = dropboxSecretsDoc.RootElement.GetProperty("appKey").GetString()!;

    var dropboxAuth = new DropboxAuthProvider(dropboxAppKey);

    Console.WriteLine("Opening your browser to sign in to Dropbox...");
    try
    {
        dropboxAuth.SignIn("dropbox-test-user");
        Console.WriteLine($"Dropbox sign-in succeeded. Connected: {dropboxAuth.IsAuthenticated}");

        using var dropboxApiClient = new RealDropboxApiClient(dropboxAuth.AccessToken!);
        var dropboxSyncProvider = new DropboxSyncProvider(
            dropboxApiClient,
            new VaultEncryptionService(new VaultMasterKeyStore(dataDirectory)),
            dropboxAuth,
            "recovered-password-456");

        var dropboxDataService = new VaultDataService(
            new VaultDataStore(dataDirectory),
            new OfflineChangeQueueStore(dataDirectory),
            dropboxSyncProvider,
            auditLogger);

        dropboxDataService.Edit(new VaultDataItem("dropbox-sync-demo", "real Dropbox sync test", DateTimeOffset.UtcNow));
        Console.WriteLine($"Queued for sync: {dropboxDataService.PendingChanges().Count} pending change(s)");

        dropboxDataService.SyncNow();
        Console.WriteLine("Synced to REAL Dropbox successfully.");
        Console.WriteLine($"Pending changes remaining: {dropboxDataService.PendingChanges().Count}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Live Dropbox demo did not complete: {ex.GetType().Name}: {ex.Message}");
    }
}

Console.WriteLine();
Console.WriteLine("--- STORY-009: manage device identity and revocation ---");

var deviceIdentity = new DeviceIdentityService(new DeviceRegistryStore(dataDirectory), auditLogger);
var (demoDeviceId, demoDeviceSecret) = deviceIdentity.EnrollDevice("Firas's Laptop");
Console.WriteLine($"Enrolled device {demoDeviceId}.");

deviceIdentity.VerifyDeviceAccess(demoDeviceId, demoDeviceSecret);
Console.WriteLine("Device access verified before revocation.");

try
{
    deviceIdentity.VerifyDeviceAccess(demoDeviceId, "an-attacker-guessed-this-secret");
}
catch (InvalidDeviceSecretException ex)
{
    Console.WriteLine($"Spoofed device (wrong secret) correctly refused: {ex.Message}");
}

deviceIdentity.RevokeDevice(demoDeviceId);
Console.WriteLine("Device revoked.");

try
{
    deviceIdentity.VerifyDeviceAccess(demoDeviceId, demoDeviceSecret);
}
catch (DeviceRevokedException ex)
{
    Console.WriteLine($"Revoked device correctly denied access: {ex.Message}");
}

deviceIdentity.ReEnrollDevice(demoDeviceId, demoDeviceSecret);
Console.WriteLine("Device re-enrolled.");
deviceIdentity.VerifyDeviceAccess(demoDeviceId, demoDeviceSecret);
Console.WriteLine("Device access restored after re-enrollment.");

Console.WriteLine();
Console.WriteLine("--- STORY-011: analyze password health and breach awareness ---");

const string healthDemoVaultCredential = "recovered-password-456"; // the password active at this point in the demo (post STORY-006 recovery)
var weakDemoCredentialId = credentials.Add(healthDemoVaultCredential, "Weak Example", "demo", "password123", null);
var strongDemoCredentialId = credentials.Add(healthDemoVaultCredential, "Strong Example", "demo", "Xk7#mQ9!vL2$pR4wZ8@nB6", null);

var healthService = new PasswordHealthService(credentials, auditLogger);
var localReports = healthService.AnalyzeAll(healthDemoVaultCredential);
Console.WriteLine($"Local analysis (no network): {localReports.Count} credential(s) analyzed.");
foreach (var report in localReports)
{
    Console.WriteLine($"  {report.Label}: weak={report.IsWeak} reasons=[{string.Join(",", report.Reasons)}]");
}

Console.WriteLine("Enabling breach awareness (live check against api.pwnedpasswords.com, k-anonymity — plaintext never leaves this device)...");
var breachChecker = new HibpBreachChecker(new HttpClient());
var breachReports = await healthService.AnalyzeAllAsync(healthDemoVaultCredential, breachChecker, CancellationToken.None);
foreach (var report in breachReports)
{
    Console.WriteLine($"  {report.Label}: breachOutcome={report.BreachOutcome} breachCount={report.BreachCount}");
}

credentials.Delete(weakDemoCredentialId); // tidy up
credentials.Delete(strongDemoCredentialId);

Console.WriteLine();
Console.WriteLine("--- STORY-012: share a credential securely ---");

var sharingDemoCredentialId = credentials.Add(healthDemoVaultCredential, "Shared Wi-Fi", "guest", "guest-network-pass", null);
var sharedCredentialService = new SharedCredentialService(credentials, new ShareRegistryStore(dataDirectory), auditLogger);

const string recipientPassphrase = "tell-alice-this-out-of-band";
var shareId = sharedCredentialService.CreateShare(healthDemoVaultCredential, sharingDemoCredentialId, "alice", recipientPassphrase);
Console.WriteLine($"Shared credential. Share id: {shareId} (recipient: alice)");

var acceptedShare = sharedCredentialService.AcceptShare(shareId, recipientPassphrase);
Console.WriteLine($"Recipient accepted share: label=\"{acceptedShare.Label}\" username=\"{acceptedShare.Username}\"");

try
{
    sharedCredentialService.AcceptShare(shareId, "wrong-passphrase");
}
catch (VaultDecryptionFailedException ex)
{
    Console.WriteLine($"Accept with wrong passphrase correctly refused: {ex.Message}");
}

sharedCredentialService.RevokeShare(shareId);
Console.WriteLine("Share revoked.");

try
{
    sharedCredentialService.AcceptShare(shareId, recipientPassphrase);
}
catch (ShareRevokedException ex)
{
    Console.WriteLine($"Recipient access after revocation correctly refused: {ex.Message}");
}

credentials.Delete(sharingDemoCredentialId); // tidy up

Console.WriteLine();
Console.WriteLine("--- STORY-015: trust spine — every operation above was logged ---");
var operationsLog = File.ReadAllLines(operationsLogPath);
Console.WriteLine($"{operationsLog.Length} operation(s) recorded in {operationsLogPath}:");
foreach (var line in operationsLog)
{
    Console.WriteLine("  " + line);
}

Console.WriteLine();
Console.WriteLine("Simulating a logging service outage (fails every attempt)...");
var alwaysFailingLogger = new AuditLogger(new AlwaysFailingSink(), adminAlerts, maxAttempts: 3, retryDelay: TimeSpan.Zero);
try
{
    alwaysFailingLogger.Log("DemoFailingOperation", Environment.UserName);
}
catch (AuditLoggingFailedException ex)
{
    Console.WriteLine($"Logging failed after retries, as expected: {ex.Message}");
    Console.WriteLine($"Admin was alerted: \"{adminAlerts.Alerts[^1]}\"");
}

static string? FindGoogleOAuthSecretsFile() => FindSecretsFile("google-oauth.json");

static string? FindDropboxSecretsFile() => FindSecretsFile("dropbox.json");

static string? FindSecretsFile(string fileName)
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    for (int i = 0; i < 8 && dir is not null; i++)
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

sealed class AlwaysFailingSink : IAuditLogSink
{
    public void Write(OperationLogEntry entry) => throw new AuditSinkUnavailableException("Simulated logging service outage.");
}
