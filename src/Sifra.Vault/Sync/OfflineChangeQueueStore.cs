using System.Text.Json;

namespace Sifra.Vault.Sync;

/// <summary>
/// Local, file-backed outbox of changes waiting to sync. At most one
/// pending change per item id is kept — enqueueing a second edit to the
/// same item replaces the first rather than piling up redundant entries,
/// so syncing twice never double-pushes the same item.
/// </summary>
public sealed class OfflineChangeQueueStore
{
    private const string FileName = "vault-change-queue.json";

    private readonly string _filePath;

    public OfflineChangeQueueStore(string? dataDirectory = null)
    {
        var directory = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Sifra");

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (IOException ex)
        {
            throw new VaultStorageException($"Could not create vault storage directory '{directory}'.", ex);
        }

        _filePath = Path.Combine(directory, FileName);
    }

    public IReadOnlyList<QueuedChange> LoadAll()
    {
        if (!File.Exists(_filePath))
        {
            return Array.Empty<QueuedChange>();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<List<QueuedChange>>(json) ?? new List<QueuedChange>();
        }
        catch (IOException ex)
        {
            throw new VaultStorageException($"Could not read change queue file '{_filePath}'.", ex);
        }
    }

    public void Enqueue(QueuedChange change)
    {
        var changes = LoadAll().Where(existing => existing.ItemId != change.ItemId).Append(change).ToList();
        Save(changes);
    }

    public void Remove(string changeId)
    {
        var changes = LoadAll().Where(existing => existing.ChangeId != changeId).ToList();
        Save(changes);
    }

    public void MarkConflicted(string changeId)
    {
        var changes = LoadAll()
            .Select(existing => existing.ChangeId == changeId ? existing with { Conflicted = true } : existing)
            .ToList();
        Save(changes);
    }

    private void Save(List<QueuedChange> changes)
    {
        var json = JsonSerializer.Serialize(changes);
        var tempFilePath = _filePath + ".tmp";

        try
        {
            File.WriteAllText(tempFilePath, json);
            File.Move(tempFilePath, _filePath, overwrite: true);
        }
        catch (IOException ex)
        {
            throw new VaultStorageException($"Could not write change queue file '{_filePath}'.", ex);
        }
    }
}
