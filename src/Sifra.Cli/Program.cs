using Sifra.Vault;
using Sifra.Vault.Audit;
using Sifra.Vault.Auth;
using Sifra.Vault.Credentials;
using Sifra.Vault.Crypto;
using Sifra.Vault.Passwords;
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

try
{
    var recoveryKey = service.CreateVault();
    Console.WriteLine("Vault created.");
    Console.WriteLine();
    Console.WriteLine("Your recovery key (shown once — save it somewhere safe now):");
    Console.WriteLine(recoveryKey);
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

var vaultLogPath = Path.Combine(resolvedDataDirectory, "vault-access.log");

var vaultAuth = new VaultAuthenticator(new VaultAccessCredentialStore(dataDirectory), new FileAccessAuditLog(vaultLogPath), auditLogger);
vaultAuth.SetCredential("demo-password");

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
    new VaultEncryptionService(new VaultEncryptionKeyStore(dataDirectory)),
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

sealed class AlwaysFailingSink : IAuditLogSink
{
    public void Write(OperationLogEntry entry) => throw new AuditSinkUnavailableException("Simulated logging service outage.");
}
