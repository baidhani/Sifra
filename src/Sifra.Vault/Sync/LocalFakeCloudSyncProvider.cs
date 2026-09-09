namespace Sifra.Vault.Sync;

/// <summary>
/// Stand-in cloud sync backend with no network calls — this repo has no
/// real cloud provider integration yet (that is STORY-007). Lets tests
/// control connectivity and force a Conflict outcome for specific items.
/// </summary>
public sealed class LocalFakeCloudSyncProvider : ICloudSyncProvider
{
    private readonly HashSet<string> _itemIdsToConflict = new();

    public bool IsConnected { get; set; }

    public List<QueuedChange> PushedChanges { get; } = new();

    public void ForceConflictFor(string itemId) => _itemIdsToConflict.Add(itemId);

    public IReadOnlyDictionary<string, SyncOutcome> Push(IReadOnlyList<QueuedChange> changes)
    {
        var outcomes = new Dictionary<string, SyncOutcome>();
        foreach (var change in changes)
        {
            PushedChanges.Add(change);
            outcomes[change.ChangeId] = _itemIdsToConflict.Contains(change.ItemId)
                ? SyncOutcome.Conflict
                : SyncOutcome.Accepted;
        }
        return outcomes;
    }
}
