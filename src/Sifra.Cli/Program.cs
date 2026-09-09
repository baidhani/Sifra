using Sifra.Vault;
using Sifra.Vault.Auth;

// Optional first argument overrides where vault data lives — useful for
// demos so they never touch a real user's actual application-data folder.
string? dataDirectory = args.Length > 0 ? args[0] : null;

var store = new VaultStore(dataDirectory);
var service = new VaultService(store);

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

var vaultLogPath = Path.Combine(
    dataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sifra"),
    "vault-access.log");

var vaultAuth = new VaultAuthenticator(new VaultAccessCredentialStore(dataDirectory), new FileAccessAuditLog(vaultLogPath));
vaultAuth.SetCredential("demo-password");

var cloud = new LocalFakeCloudAuthProvider();

cloud.SignIn("demo@example.com");
Console.WriteLine($"Cloud signed in. Vault authenticate still required and succeeds: {vaultAuth.Authenticate("demo-password")}");

cloud.SignOut();
Console.WriteLine($"Cloud signed out. Vault authenticate is unaffected and still succeeds: {vaultAuth.Authenticate("demo-password")}");

Console.WriteLine($"Wrong vault credential is rejected: {vaultAuth.Authenticate("not-the-password")}");
