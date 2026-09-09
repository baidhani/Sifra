using Sifra.Vault.Sync;

namespace Sifra.Vault.Tests;

public sealed class VaultDataServiceTests : IDisposable
{
    private readonly string _dataDirectory;

    public VaultDataServiceTests()
    {
        _dataDirectory = Path.Combine(Path.GetTempPath(), "sifra-sync-tests-" + Guid.NewGuid());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private (VaultDataService service, LocalFakeCloudSyncProvider provider) CreateService()
    {
        var provider = new LocalFakeCloudSyncProvider(); // starts disconnected, i.e. offline
        var service = new VaultDataService(
            new VaultDataStore(_dataDirectory),
            new OfflineChangeQueueStore(_dataDirectory),
            provider);
        return (service, provider);
    }

    [Fact]
    public void Edit_WhileOffline_PersistsLocallyAndIsImmediatelyViewable()
    {
        // Acceptance: offline access allows viewing and editing.
        var (service, provider) = CreateService();
        Assert.False(provider.IsConnected); // offline

        service.Edit(new VaultDataItem("item-1", "hello", DateTimeOffset.UtcNow));

        var items = service.View();
        Assert.Single(items);
        Assert.Equal("hello", items[0].Content);
    }

    [Fact]
    public void Edit_WhileOffline_QueuesTheChangeForLaterSync()
    {
        // Trust: offline changes are logged and queued for sync.
        var (service, _) = CreateService();

        service.Edit(new VaultDataItem("item-1", "hello", DateTimeOffset.UtcNow));

        var pending = service.PendingChanges();
        Assert.Single(pending);
        Assert.Equal("item-1", pending[0].ItemId);
        Assert.False(pending[0].Conflicted);
    }

    [Fact]
    public void SyncNow_WhenReconnected_DrainsTheQueueAndPushesEverything()
    {
        // Acceptance: changes made offline sync once reconnected.
        var (service, provider) = CreateService();
        service.Edit(new VaultDataItem("item-1", "hello", DateTimeOffset.UtcNow));
        service.Edit(new VaultDataItem("item-2", "world", DateTimeOffset.UtcNow));

        provider.IsConnected = true; // reconnect
        service.SyncNow();

        Assert.Empty(service.PendingChanges());
        Assert.Equal(2, provider.PushedChanges.Count);
    }

    [Fact]
    public void Edit_CalledTwiceForSameItemBeforeSync_QueuesOnlyOnePendingChange()
    {
        // Idempotency: re-editing the same item does not pile up duplicate sync work.
        var (service, _) = CreateService();

        service.Edit(new VaultDataItem("item-1", "first", DateTimeOffset.UtcNow));
        service.Edit(new VaultDataItem("item-1", "second", DateTimeOffset.UtcNow));

        var pending = service.PendingChanges();
        Assert.Single(pending);
        Assert.Contains("second", pending[0].PayloadJson);
    }

    [Fact]
    public void SyncNow_WhileStillOffline_ThrowsAndLeavesTheQueueIntact()
    {
        // Failure path: "user tries to sync while offline."
        var (service, provider) = CreateService();
        service.Edit(new VaultDataItem("item-1", "hello", DateTimeOffset.UtcNow));

        Assert.False(provider.IsConnected);
        Assert.Throws<SyncUnavailableException>(() => service.SyncNow());

        // Nothing was lost — the change is still queued, ready to retry.
        Assert.Single(service.PendingChanges());
    }

    [Fact]
    public void SyncNow_WhenRemoteReportsConflict_KeepsTheChangeQueuedInsteadOfLosingIt()
    {
        // Failure path: "data conflicts occur during offline edits."
        var (service, provider) = CreateService();
        service.Edit(new VaultDataItem("item-1", "hello", DateTimeOffset.UtcNow));
        provider.ForceConflictFor("item-1");
        provider.IsConnected = true;

        service.SyncNow();

        var pending = service.PendingChanges();
        Assert.Single(pending); // not dropped, not lost
        Assert.True(pending[0].Conflicted);
    }

    [Fact]
    public void Edit_WhenStorageDirectoryCannotBeCreated_ThrowsInsteadOfSilentlyLosingTheEdit()
    {
        // Failure path: "user loses data due to failed offline operations" —
        // a storage error must be surfaced loudly, never swallowed.
        var blockingFilePath = Path.Combine(Path.GetTempPath(), "sifra-sync-tests-blocked-" + Guid.NewGuid());
        File.WriteAllText(blockingFilePath, "not a directory");

        try
        {
            Assert.Throws<VaultStorageException>(() => new VaultDataStore(blockingFilePath));
        }
        finally
        {
            File.Delete(blockingFilePath);
        }
    }
}
