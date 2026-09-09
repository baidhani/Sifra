using Sifra.Vault;

var store = new VaultStore();
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
