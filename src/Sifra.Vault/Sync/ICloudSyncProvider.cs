namespace Sifra.Vault.Sync;

public enum SyncOutcome
{
    Accepted,
    Conflict
}

/// <summary>
/// Generic shape any cloud-storage sync backend must expose. STORY-007
/// (Google Drive) implements this with a real API call; nothing in the
/// offline queue or the coordinator depends on which implementation is
/// wired up, or on this provider ever being connected at all.
/// </summary>
public interface ICloudSyncProvider
{
    bool IsConnected { get; }

    /// <summary>
    /// Pushes changes to the remote side. Only called while
    /// <see cref="IsConnected"/> is true — the caller is responsible for
    /// that check. Returns one outcome per change, keyed by ChangeId.
    /// </summary>
    IReadOnlyDictionary<string, SyncOutcome> Push(IReadOnlyList<QueuedChange> changes);
}
