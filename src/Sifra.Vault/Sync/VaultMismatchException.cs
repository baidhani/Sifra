namespace Sifra.Vault.Sync;

/// <summary>
/// Thrown when a pulled envelope's VaultId doesn't match this device's
/// own — the cloud file/folder this device is pointed at belongs to a
/// genuinely different vault (a misconfiguration, or leftover data from
/// an unrelated test/account), not one this device shares a master key
/// with. Refusing to merge here is what closes the gap that let foreign,
/// undecryptable ciphertext get merged into local storage before this
/// check existed — see VaultIdentityStore's remarks. Local data is left
/// completely untouched; nothing was pulled in before this check ran.
/// </summary>
public sealed class VaultMismatchException : Exception
{
    public VaultMismatchException()
        : base("The cloud vault at this location belongs to a different vault than this device's. Sync was refused to avoid merging in unrelated data.")
    {
    }
}
