using Sifra.Vault.Auth;
using Sifra.Vault.Crypto;
using Sifra.Vault.OneDrive;
using Sifra.Vault.Sync;

namespace Sifra.Vault.Tests;

public sealed class OneDriveSyncProviderTests : IDisposable
{
    private const string VaultCredential = "correct-horse-battery-staple";

    private readonly string _dataDirectory;

    public OneDriveSyncProviderTests()
    {
        _dataDirectory = Path.Combine(Path.GetTempPath(), "sifra-onedrive-tests-" + Guid.NewGuid());
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
        // Acceptance: when synchronized with OneDrive, data is encrypted and uploaded.
        var fakeApi = new FakeOneDriveApiClient();
        var provider = new OneDriveSyncProvider(fakeApi, CreateEncryption(), SignedInAuth(), VaultCredential);
        var change = MakeChange("item-1", "secret content");

        var outcomes = provider.Push(new[] { change });

        Assert.Equal(SyncOutcome.Accepted, outcomes[change.ChangeId]);
        Assert.Single(fakeApi.UploadedFiles);
        var uploadedText = System.Text.Encoding.UTF8.GetString(fakeApi.UploadedFiles.Values.Single());
        Assert.DoesNotContain("secret content", uploadedText); // ciphertext only, never plaintext
    }

    [Fact]
    public void Push_CalledTwice_UpdatesTheSameFileRatherThanCreatingASecondOne()
    {
        var fakeApi = new FakeOneDriveApiClient();
        var provider = new OneDriveSyncProvider(fakeApi, CreateEncryption(), SignedInAuth(), VaultCredential);

        provider.Push(new[] { MakeChange("item-1", "first") });
        provider.Push(new[] { MakeChange("item-2", "second") });

        Assert.Single(fakeApi.UploadedFiles);
        Assert.Equal(1, fakeApi.CreateCallCount);
        Assert.Equal(1, fakeApi.UpdateCallCount);
    }

    [Fact]
    public void Push_WithEmptyChangeList_DoesNothingAndCallsTheApiZeroTimes()
    {
        var fakeApi = new FakeOneDriveApiClient();
        var provider = new OneDriveSyncProvider(fakeApi, CreateEncryption(), SignedInAuth(), VaultCredential);

        var outcomes = provider.Push(Array.Empty<QueuedChange>());

        Assert.Empty(outcomes);
        Assert.Equal(0, fakeApi.CreateCallCount + fakeApi.UpdateCallCount);
    }

    [Fact]
    public void Push_WhenTheNetworkFailsThenRecovers_SucceedsWithinTheRetryBudget()
    {
        // Failure path: "sync fails with OneDrive" — but recovers.
        var fakeApi = new FakeOneDriveApiClient { FailNetworkTimesBeforeSuccess = 2 };
        var provider = new OneDriveSyncProvider(fakeApi, CreateEncryption(), SignedInAuth(), VaultCredential, maxAttempts: 3, retryDelay: TimeSpan.Zero);

        var outcomes = provider.Push(new[] { MakeChange("item-1", "content") });

        Assert.All(outcomes.Values, o => Assert.Equal(SyncOutcome.Accepted, o));
    }

    [Fact]
    public void Push_WhenTheNetworkNeverRecovers_ThrowsAfterExhaustingRetries()
    {
        var fakeApi = new FakeOneDriveApiClient { FailNetworkTimesBeforeSuccess = int.MaxValue };
        var provider = new OneDriveSyncProvider(fakeApi, CreateEncryption(), SignedInAuth(), VaultCredential, maxAttempts: 3, retryDelay: TimeSpan.Zero);

        var ex = Assert.Throws<OneDriveSyncFailedException>(() => provider.Push(new[] { MakeChange("item-1", "content") }));
        Assert.NotNull(ex.InnerException);
        Assert.Equal(3, fakeApi.FindAttemptCount);
    }

    [Fact]
    public void Push_WhenUnauthorized_ThrowsImmediatelyWithoutRetrying()
    {
        var fakeApi = new FakeOneDriveApiClient { AlwaysUnauthorized = true };
        var provider = new OneDriveSyncProvider(fakeApi, CreateEncryption(), SignedInAuth(), VaultCredential, maxAttempts: 3, retryDelay: TimeSpan.Zero);

        Assert.Throws<OneDriveUnauthorizedException>(() => provider.Push(new[] { MakeChange("item-1", "content") }));
        Assert.Equal(1, fakeApi.FindAttemptCount);
    }

    [Fact]
    public void Push_WhenNotConnected_IsConnectedIsFalse()
    {
        var provider = new OneDriveSyncProvider(new FakeOneDriveApiClient(), CreateEncryption(), new LocalFakeCloudAuthProvider(), VaultCredential);

        Assert.False(provider.IsConnected);
    }

    [Fact]
    public void SyncNow_WhenOneDriveSyncFails_LeavesTheOfflineQueueIntact()
    {
        // Acceptance-adjacent (mirrors STORY-007's proof): local data
        // remains intact through the actual VaultDataService integration point.
        var fakeApi = new FakeOneDriveApiClient { FailNetworkTimesBeforeSuccess = int.MaxValue };
        var provider = new OneDriveSyncProvider(fakeApi, CreateEncryption(), SignedInAuth(), VaultCredential, maxAttempts: 2, retryDelay: TimeSpan.Zero);
        var dataService = new VaultDataService(
            new VaultDataStore(_dataDirectory),
            new OfflineChangeQueueStore(_dataDirectory),
            provider);

        dataService.Edit(new VaultDataItem("item-1", "buy milk", DateTimeOffset.UtcNow));

        Assert.Throws<OneDriveSyncFailedException>(() => dataService.SyncNow());

        var pending = dataService.PendingChanges();
        Assert.Single(pending);
        Assert.False(pending[0].Conflicted);
    }

    /// <summary>In-memory fake — never touches the network or a real Microsoft account.</summary>
    private sealed class FakeOneDriveApiClient : IOneDriveApiClient
    {
        public Dictionary<string, byte[]> UploadedFiles { get; } = new();
        public int CreateCallCount { get; private set; }
        public int UpdateCallCount { get; private set; }
        public int FindAttemptCount { get; private set; }
        public int FailNetworkTimesBeforeSuccess { get; set; }
        public bool AlwaysUnauthorized { get; set; }

        private string? _itemId;
        private int _networkFailureCount;

        public Task<string?> FindFileIdAsync(string fileName, CancellationToken cancellationToken)
        {
            FindAttemptCount++;
            if (AlwaysUnauthorized)
            {
                throw new OneDriveUnauthorizedException("Simulated unauthorized response.");
            }
            if (_networkFailureCount < FailNetworkTimesBeforeSuccess)
            {
                _networkFailureCount++;
                throw new OneDriveNetworkException("Simulated network outage.", new Exception());
            }
            return Task.FromResult(_itemId);
        }

        public Task<string> UploadOrUpdateFileAsync(string fileName, string? existingItemId, byte[] content, CancellationToken cancellationToken)
        {
            if (existingItemId is null)
            {
                _itemId = Guid.NewGuid().ToString("N");
                CreateCallCount++;
            }
            else
            {
                UpdateCallCount++;
            }

            UploadedFiles[fileName] = content;
            return Task.FromResult(_itemId!);
        }
    }
}
