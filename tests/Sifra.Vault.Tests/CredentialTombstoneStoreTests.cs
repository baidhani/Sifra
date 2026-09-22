using Sifra.Vault.Sync;

namespace Sifra.Vault.Tests;

public sealed class CredentialTombstoneStoreTests : IDisposable
{
    private readonly string _dataDirectory;

    public CredentialTombstoneStoreTests()
    {
        _dataDirectory = Path.Combine(Path.GetTempPath(), "sifra-tombstone-tests-" + Guid.NewGuid());
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
        var store = new CredentialTombstoneStore(_dataDirectory);
        var deletedAt = DateTimeOffset.UtcNow;

        store.Add("cred-1", deletedAt);

        var loaded = store.Load("cred-1");
        Assert.NotNull(loaded);
        Assert.Equal(deletedAt, loaded!.DeletedAtUtc);
    }

    [Fact]
    public void Load_ForAnIdNeverDeleted_ReturnsNull()
    {
        var store = new CredentialTombstoneStore(_dataDirectory);

        Assert.Null(store.Load("never-existed"));
    }

    [Fact]
    public void Add_CalledTwiceForTheSameId_KeepsTheLatestTimestamp()
    {
        var store = new CredentialTombstoneStore(_dataDirectory);
        var first = DateTimeOffset.UtcNow.AddDays(-1);
        var second = DateTimeOffset.UtcNow;

        store.Add("cred-1", first);
        store.Add("cred-1", second);

        Assert.Equal(second, store.Load("cred-1")!.DeletedAtUtc);
    }

    [Fact]
    public void LoadAll_ReturnsEveryTombstone()
    {
        var store = new CredentialTombstoneStore(_dataDirectory);
        store.Add("cred-1", DateTimeOffset.UtcNow);
        store.Add("cred-2", DateTimeOffset.UtcNow);

        var all = store.LoadAll();

        Assert.Equal(2, all.Count);
        Assert.Contains(all, t => t.Id == "cred-1");
        Assert.Contains(all, t => t.Id == "cred-2");
    }

    [Fact]
    public void PurgeOlderThan_RemovesOnlyTombstonesOlderThanTheThreshold()
    {
        var store = new CredentialTombstoneStore(_dataDirectory);
        var old = DateTimeOffset.UtcNow.AddDays(-100);
        var recent = DateTimeOffset.UtcNow.AddDays(-1);
        store.Add("old-cred", old);
        store.Add("recent-cred", recent);

        store.PurgeOlderThan(DateTimeOffset.UtcNow.AddDays(-90));

        Assert.Null(store.Load("old-cred"));
        Assert.NotNull(store.Load("recent-cred"));
    }

    [Fact]
    public void ReopeningTheStore_PersistedTombstonesSurvive()
    {
        var deletedAt = DateTimeOffset.UtcNow;
        new CredentialTombstoneStore(_dataDirectory).Add("cred-1", deletedAt);

        var reopened = new CredentialTombstoneStore(_dataDirectory);

        Assert.Equal(deletedAt, reopened.Load("cred-1")!.DeletedAtUtc);
    }
}
