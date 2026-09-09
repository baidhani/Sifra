namespace Sifra.Vault.Sync;

/// <summary>
/// Thrown when a sync is attempted while the cloud sync provider is not
/// connected. The offline change queue is left untouched — nothing is
/// lost, and the caller can simply retry once reconnected.
/// </summary>
public sealed class SyncUnavailableException : Exception
{
    public SyncUnavailableException()
        : base("Cannot sync while offline. Changes remain queued locally.")
    {
    }
}
