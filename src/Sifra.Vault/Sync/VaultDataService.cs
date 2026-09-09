using System.Text.Json;
using Sifra.Vault.Audit;

namespace Sifra.Vault.Sync;

/// <summary>
/// Local-first vault data operations. Viewing and editing never touch the
/// network — every edit is persisted locally first, then queued for sync.
/// Syncing is a separate, explicit step that can fail without losing the
/// edit itself, because the edit was already durable before sync was ever
/// attempted.
/// </summary>
public sealed class VaultDataService
{
    private readonly VaultDataStore _dataStore;
    private readonly OfflineChangeQueueStore _queueStore;
    private readonly ICloudSyncProvider _syncProvider;
    private readonly AuditLogger? _auditLogger;

    /// <param name="auditLogger">
    /// Optional (STORY-015). When provided, every completed Edit and
    /// SyncNow call is logged to the trust-spine audit trail. Null means
    /// no audit logging — existing callers and tests are unaffected.
    /// </param>
    public VaultDataService(VaultDataStore dataStore, OfflineChangeQueueStore queueStore, ICloudSyncProvider syncProvider, AuditLogger? auditLogger = null)
    {
        _dataStore = dataStore;
        _queueStore = queueStore;
        _syncProvider = syncProvider;
        _auditLogger = auditLogger;
    }

    /// <summary>Works identically online or offline — pure local read.</summary>
    public IReadOnlyList<VaultDataItem> View() => _dataStore.GetAll();

    /// <summary>
    /// Persists the edit locally (always succeeds regardless of
    /// connectivity) and queues it for sync. A second edit to the same
    /// item before syncing replaces the queued change rather than adding
    /// a duplicate.
    /// </summary>
    public void Edit(VaultDataItem item)
    {
        _dataStore.Upsert(item);

        var change = new QueuedChange(
            ChangeId: Guid.NewGuid().ToString("N"),
            ItemId: item.Id,
            PayloadJson: JsonSerializer.Serialize(item),
            QueuedAtUtc: DateTimeOffset.UtcNow,
            Conflicted: false);

        _queueStore.Enqueue(change);

        _auditLogger?.Log(nameof(Edit), Environment.UserName, details: $"itemId={item.Id}");
    }

    public IReadOnlyList<QueuedChange> PendingChanges() => _queueStore.LoadAll();

    /// <summary>
    /// Drains the offline change queue to the cloud. Accepted changes are
    /// removed from the queue; conflicted changes stay queued (flagged)
    /// rather than being lost or silently overwritten.
    /// </summary>
    /// <exception cref="SyncUnavailableException">The sync provider is not connected.</exception>
    public void SyncNow()
    {
        if (!_syncProvider.IsConnected)
        {
            throw new SyncUnavailableException();
        }

        var pending = _queueStore.LoadAll().Where(c => !c.Conflicted).ToList();
        if (pending.Count == 0)
        {
            _auditLogger?.Log(nameof(SyncNow), Environment.UserName, details: "0 changes pushed");
            return;
        }

        var outcomes = _syncProvider.Push(pending);

        var accepted = 0;
        foreach (var change in pending)
        {
            if (outcomes.TryGetValue(change.ChangeId, out var outcome) && outcome == SyncOutcome.Accepted)
            {
                _queueStore.Remove(change.ChangeId);
                accepted++;
            }
            else
            {
                _queueStore.MarkConflicted(change.ChangeId);
            }
        }

        _auditLogger?.Log(nameof(SyncNow), Environment.UserName, details: $"{accepted} accepted, {pending.Count - accepted} conflicted");
    }
}
