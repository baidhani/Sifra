using Sifra.Vault.Auth;
using Sifra.Vault.Crypto;
using Sifra.Vault.Dropbox;
using Sifra.Vault.Sync;

namespace Sifra.Vault.Tests;

public sealed class DropboxSyncProviderTests : IDisposable
{
    private const string VaultCredential = "correct-horse-battery-staple";

    private readonly string _dataDirectory;

    public DropboxSyncProviderTests()
    {
        _dataDirectory = Path.Combine(Path.GetTempPath(), "sifra-dropbox-tests-" + Guid.NewGuid());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private VaultEncryptionService CreateEncryption() => new(new VaultMasterKeyStore(_dataDirectory));

    private static LocalFakeCloudAuthProvider SignedInAuth()
    {
        var provider = new LocalFakeCloudAuthProvider();
        provider.SignIn("test@example.com");
        return provider;
    }

    private static QueuedChange MakeChange(string itemId, string content) => new(
        ChangeId: Guid.NewGuid().ToString("N"),
        ItemId: itemId,
        PayloadJson: $"{{\"Content\":\"{content}\"}}",
        QueuedAtUtc: DateTimeOffset.UtcNow,
        Conflicted: false);

    [Fact]
    public void Push_WhenConnected_UploadsEncryptedContentAndReturnsAcceptedForEveryChange()
    {
        // Acceptance: when synchronized with Dropbox, data is encrypted and uploaded.
        var fakeApi = new FakeDropboxApiClient();
        var provider = new DropboxSyncProvider(fakeApi, CreateEncryption(), SignedInAuth(), VaultCredential);
        var change = MakeChange("item-1", "secret content");

        var outcomes = provider.Push(new[] { change });

        Assert.Equal(SyncOutcome.Accepted, outcomes[change.ChangeId]);
        Assert.Single(fakeApi.UploadedFiles);
        var uploadedText = System.Text.Encoding.UTF8.GetString(fakeApi.UploadedFiles.Values.Single());
        Assert.DoesNotContain("secret content", uploadedText); // ciphertext only, never plaintext
    }

    [Fact]
    public void Push_CalledTwice_OverwritesTheSamePathRatherThanCreatingASecondFile()
    {
        // Idempotency: Dropbox upserts by path natively — proven by upload count.
        var fakeApi = new FakeDropboxApiClient();
        var provider = new DropboxSyncProvider(fakeApi, CreateEncryption(), SignedInAuth(), VaultCredential);

        provider.Push(new[] { MakeChange("item-1", "first") });
        provider.Push(new[] { MakeChange("item-2", "second") });

        Assert.Single(fakeApi.UploadedFiles); // one path, overwritten twice
        Assert.Equal(2, fakeApi.UploadCallCount);
    }

    [Fact]
    public void Push_WithEmptyChangeList_DoesNothingAndCallsTheApiZeroTimes()
    {
        var fakeApi = new FakeDropboxApiClient();
        var provider = new DropboxSyncProvider(fakeApi, CreateEncryption(), SignedInAuth(), VaultCredential);

        var outcomes = provider.Push(Array.Empty<QueuedChange>());

        Assert.Empty(outcomes);
        Assert.Equal(0, fakeApi.UploadCallCount);
    }

    [Fact]
    public void Push_WhenTheNetworkFailsThenRecovers_SucceedsWithinTheRetryBudget()
    {
        // Failure path: "sync fails with Dropbox" — but recovers.
        var fakeApi = new FakeDropboxApiClient { FailNetworkTimesBeforeSuccess = 2 };
        var provider = new DropboxSyncProvider(fakeApi, CreateEncryption(), SignedInAuth(), VaultCredential, maxAttempts: 3, retryDelay: TimeSpan.Zero);

        var outcomes = provider.Push(new[] { MakeChange("item-1", "content") });

        Assert.All(outcomes.Values, o => Assert.Equal(SyncOutcome.Accepted, o));
    }

    [Fact]
    public void Push_WhenTheNetworkNeverRecovers_ThrowsAfterExhaustingRetries()
    {
        var fakeApi = new FakeDropboxApiClient { FailNetworkTimesBeforeSuccess = int.MaxValue };
        var provider = new DropboxSyncProvider(fakeApi, CreateEncryption(), SignedInAuth(), VaultCredential, maxAttempts: 3, retryDelay: TimeSpan.Zero);

        var ex = Assert.Throws<DropboxSyncFailedException>(() => provider.Push(new[] { MakeChange("item-1", "content") }));
        Assert.NotNull(ex.InnerException);
        Assert.Equal(3, fakeApi.UploadCallCount);
    }

    [Fact]
    public void Push_WhenUnauthorized_ThrowsImmediatelyWithoutRetrying()
    {
        var fakeApi = new FakeDropboxApiClient { AlwaysUnauthorized = true };
        var provider = new DropboxSyncProvider(fakeApi, CreateEncryption(), SignedInAuth(), VaultCredential, maxAttempts: 3, retryDelay: TimeSpan.Zero);

        Assert.Throws<DropboxUnauthorizedException>(() => provider.Push(new[] { MakeChange("item-1", "content") }));
        Assert.Equal(1, fakeApi.UploadCallCount);
    }

    [Fact]
    public void Push_WhenNotConnected_IsConnectedIsFalse()
    {
        var provider = new DropboxSyncProvider(new FakeDropboxApiClient(), CreateEncryption(), new LocalFakeCloudAuthProvider(), VaultCredential);

        Assert.False(provider.IsConnected);
    }

    [Fact]
    public void SyncNow_WhenDropboxSyncFails_LeavesTheOfflineQueueIntact()
    {
        var fakeApi = new FakeDropboxApiClient { FailNetworkTimesBeforeSuccess = int.MaxValue };
        var provider = new DropboxSyncProvider(fakeApi, CreateEncryption(), SignedInAuth(), VaultCredential, maxAttempts: 2, retryDelay: TimeSpan.Zero);
        var dataService = new VaultDataService(
            new VaultDataStore(_dataDirectory),
            new OfflineChangeQueueStore(_dataDirectory),
            provider);

        dataService.Edit(new VaultDataItem("item-1", "buy milk", DateTimeOffset.UtcNow));

        Assert.Throws<DropboxSyncFailedException>(() => dataService.SyncNow());

        var pending = dataService.PendingChanges();
        Assert.Single(pending);
        Assert.False(pending[0].Conflicted);
    }

    /// <summary>In-memory fake — never touches the network or a real Dropbox account.</summary>
    private sealed class FakeDropboxApiClient : IDropboxApiClient
    {
        public Dictionary<string, byte[]> UploadedFiles { get; } = new();
        public int UploadCallCount { get; private set; }
        public int FailNetworkTimesBeforeSuccess { get; set; }
        public bool AlwaysUnauthorized { get; set; }

        private int _networkFailureCount;

        public Task UploadAsync(string path, byte[] content, CancellationToken cancellationToken)
        {
            UploadCallCount++;
            if (AlwaysUnauthorized)
            {
                throw new DropboxUnauthorizedException("Simulated unauthorized response.");
            }
            if (_networkFailureCount < FailNetworkTimesBeforeSuccess)
            {
                _networkFailureCount++;
                throw new DropboxNetworkException("Simulated network outage.", new Exception());
            }

            UploadedFiles[path] = content;
            return Task.CompletedTask;
        }
    }
}
