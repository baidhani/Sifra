using Sifra.Vault.Sync;

namespace Sifra.Vault.Tests;

public sealed class AttachmentTombstoneStoreTests : IDisposable
{
    private readonly string _dataDirectory;

    public AttachmentTombstoneStoreTests()
    {
        _dataDirectory = Path.Combine(Path.GetTempPath(), "sifra-attachment-tombstone-tests-" + Guid.NewGuid());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Add_ThenLoad_ReturnsTheTombstone()
    {
        var store = new AttachmentTombstoneStore(_dataDirectory);
        var deletedAt = DateTimeOffset.UtcNow;

        store.Add("att-1", deletedAt);

        Assert.Equal(deletedAt, store.Load("att-1")!.DeletedAtUtc);
    }

    [Fact]
    public void Load_ForAnIdNeverDeleted_ReturnsNull()
    {
        Assert.Null(new AttachmentTombstoneStore(_dataDirectory).Load("never-existed"));
    }

    [Fact]
    public void PurgeOlderThan_RemovesOnlyTombstonesOlderThanTheThreshold()
    {
        var store = new AttachmentTombstoneStore(_dataDirectory);
        store.Add("old-att", DateTimeOffset.UtcNow.AddDays(-100));
        store.Add("recent-att", DateTimeOffset.UtcNow.AddDays(-1));

        store.PurgeOlderThan(DateTimeOffset.UtcNow.AddDays(-90));

        Assert.Null(store.Load("old-att"));
        Assert.NotNull(store.Load("recent-att"));
    }
}
