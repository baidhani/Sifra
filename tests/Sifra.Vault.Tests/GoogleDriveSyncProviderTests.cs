using Sifra.Vault.Auth;
using Sifra.Vault.Crypto;
using Sifra.Vault.GoogleDrive;
using Sifra.Vault.Sync;

namespace Sifra.Vault.Tests;

public sealed class GoogleDriveSyncProviderTests : IDisposable
{
    private const string VaultCredential = "correct-horse-battery-staple";

    private readonly string _dataDirectory;

    public GoogleDriveSyncProviderTests()
    {
        _dataDirectory = Path.Combine(Path.GetTempPath(), "sifra-gdrive-tests-" + Guid.NewGuid());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private VaultEncryptionService CreateEncryption() => new(new VaultMasterKeyStore(_dataDirectory));

    private static QueuedChange MakeChange(string itemId, string content) => new(
        ChangeId: Guid.NewGuid().ToString("N"),
        ItemId: itemId,
        PayloadJson: $"{{\"Content\":\"{content}\"}}",
        QueuedAtUtc: DateTimeOffset.UtcNow,
        Conflicted: false);

    [Fact]
    public void Push_WhenConnected_UploadsEncryptedContentAndReturnsAcceptedForEveryChange()
    {
        // Acceptance: when sync is enabled, data is encrypted and uploaded.
        var fakeApi = new FakeGoogleDriveApiClient();
        var provider = new GoogleDriveSyncProvider(fakeApi, CreateEncryption(), new LocalFakeCloudAuthProvider { SimulateFailure = false }.AlsoSignedIn(), VaultCredential);
        var change = MakeChange("item-1", "secret content");

        var outcomes = provider.Push(new[] { change });

        Assert.Equal(SyncOutcome.Accepted, outcomes[change.ChangeId]);
        Assert.Single(fakeApi.UploadedFiles);
        var uploadedBytes = fakeApi.UploadedFiles.Values.Single();
        var uploadedText = System.Text.Encoding.UTF8.GetString(uploadedBytes);
        Assert.DoesNotContain("secret content", uploadedText); // ciphertext only, never plaintext
    }

    [Fact]
    public void Push_CalledTwice_UpdatesTheSameFileRatherThanCreatingASecondOne()
    {
        // Idempotency: syncing twice must not double-create remote state.
        var fakeApi = new FakeGoogleDriveApiClient();
        var provider = new GoogleDriveSyncProvider(fakeApi, CreateEncryption(), new LocalFakeCloudAuthProvider().AlsoSignedIn(), VaultCredential);

        provider.Push(new[] { MakeChange("item-1", "first") });
        provider.Push(new[] { MakeChange("item-2", "second") });

        Assert.Single(fakeApi.UploadedFiles); // one file, updated twice — not two files
        Assert.Equal(2, fakeApi.UpdateCallCount + fakeApi.CreateCallCount);
        Assert.Equal(1, fakeApi.CreateCallCount);
        Assert.Equal(1, fakeApi.UpdateCallCount);
    }

    [Fact]
    public void Push_WithEmptyChangeList_DoesNothingAndCallsTheApiZeroTimes()
    {
        var fakeApi = new FakeGoogleDriveApiClient();
        var provider = new GoogleDriveSyncProvider(fakeApi, CreateEncryption(), new LocalFakeCloudAuthProvider().AlsoSignedIn(), VaultCredential);

        var outcomes = provider.Push(Array.Empty<QueuedChange>());

        Assert.Empty(outcomes);
        Assert.Equal(0, fakeApi.CreateCallCount + fakeApi.UpdateCallCount);
    }

    [Fact]
    public void Push_WhenTheNetworkFailsThenRecovers_SucceedsWithinTheRetryBudget()
    {
        // Failure path: "sync fails due to network issues" — but recovers.
        var fakeApi = new FakeGoogleDriveApiClient { FailNetworkTimesBeforeSuccess = 2 };
        var provider = new GoogleDriveSyncProvider(fakeApi, CreateEncryption(), new LocalFakeCloudAuthProvider().AlsoSignedIn(), VaultCredential, maxAttempts: 3, retryDelay: TimeSpan.Zero);

        var outcomes = provider.Push(new[] { MakeChange("item-1", "content") });

        Assert.All(outcomes.Values, o => Assert.Equal(SyncOutcome.Accepted, o));
    }

    [Fact]
    public void Push_WhenTheNetworkNeverRecovers_ThrowsAfterExhaustingRetries()
    {
        var fakeApi = new FakeGoogleDriveApiClient { FailNetworkTimesBeforeSuccess = int.MaxValue };
        var provider = new GoogleDriveSyncProvider(fakeApi, CreateEncryption(), new LocalFakeCloudAuthProvider().AlsoSignedIn(), VaultCredential, maxAttempts: 3, retryDelay: TimeSpan.Zero);

        var ex = Assert.Throws<GoogleDriveSyncFailedException>(() => provider.Push(new[] { MakeChange("item-1", "content") }));
        Assert.NotNull(ex.InnerException);
        Assert.Equal(3, fakeApi.FindAttemptCount);
    }

    [Fact]
    public void Push_WhenUnauthorized_ThrowsImmediatelyWithoutRetrying()
    {
        // Failure path: "unauthorized access to Google Drive" — not a transient error, so no retry.
        var fakeApi = new FakeGoogleDriveApiClient { AlwaysUnauthorized = true };
        var provider = new GoogleDriveSyncProvider(fakeApi, CreateEncryption(), new LocalFakeCloudAuthProvider().AlsoSignedIn(), VaultCredential, maxAttempts: 3, retryDelay: TimeSpan.Zero);

        Assert.Throws<GoogleDriveUnauthorizedException>(() => provider.Push(new[] { MakeChange("item-1", "content") }));
        Assert.Equal(1, fakeApi.FindAttemptCount); // exactly one attempt, no retry
    }

    [Fact]
    public void Push_WhenNotConnected_IsConnectedIsFalse()
    {
        // "Local data remains intact" starts with the caller (VaultDataService,
        // STORY-014) checking IsConnected before ever calling Push — proven
        // here at the provider level.
        var provider = new GoogleDriveSyncProvider(new FakeGoogleDriveApiClient(), CreateEncryption(), new LocalFakeCloudAuthProvider(), VaultCredential);

        Assert.False(provider.IsConnected);
    }

    [Fact]
    public void SyncNow_WhenGoogleDriveSyncFails_LeavesTheOfflineQueueIntact()
    {
        // Acceptance: "when synchronization fails, then local data remains
        // intact" — proven end-to-end through the actual VaultDataService
        // (STORY-014) integration point, not just asserted about Push() in
        // isolation.
        var fakeApi = new FakeGoogleDriveApiClient { FailNetworkTimesBeforeSuccess = int.MaxValue };
        var provider = new GoogleDriveSyncProvider(fakeApi, CreateEncryption(), new LocalFakeCloudAuthProvider().AlsoSignedIn(), VaultCredential, maxAttempts: 2, retryDelay: TimeSpan.Zero);
        var dataService = new VaultDataService(
            new VaultDataStore(_dataDirectory),
            new OfflineChangeQueueStore(_dataDirectory),
            provider);

        dataService.Edit(new VaultDataItem("item-1", "buy milk", DateTimeOffset.UtcNow));
        Assert.Single(dataService.PendingChanges());

        Assert.Throws<GoogleDriveSyncFailedException>(() => dataService.SyncNow());

        // Nothing was lost or marked conflicted — the queue is exactly as it was.
        var pending = dataService.PendingChanges();
        Assert.Single(pending);
        Assert.False(pending[0].Conflicted);
    }

    /// <summary>In-memory fake — never touches the network or a real Google account.</summary>
    private sealed class FakeGoogleDriveApiClient : IGoogleDriveApiClient
    {
        public Dictionary<string, byte[]> UploadedFiles { get; } = new();
        public int CreateCallCount { get; private set; }
        public int UpdateCallCount { get; private set; }
        public int FindAttemptCount { get; private set; }
        public int FailNetworkTimesBeforeSuccess { get; set; }
        public bool AlwaysUnauthorized { get; set; }

        private string? _fileId;
        private int _networkFailureCount;

        public Task<string?> FindFileIdAsync(string fileName, CancellationToken cancellationToken)
        {
            FindAttemptCount++;
            if (AlwaysUnauthorized)
            {
                throw new GoogleDriveUnauthorizedException("Simulated unauthorized response.");
            }
            if (_networkFailureCount < FailNetworkTimesBeforeSuccess)
            {
                _networkFailureCount++;
                throw new GoogleDriveNetworkException("Simulated network outage.", new Exception());
            }
            return Task.FromResult(_fileId);
        }

        public Task<string> UploadOrUpdateFileAsync(string fileName, string? existingFileId, byte[] content, CancellationToken cancellationToken)
        {
            if (existingFileId is null)
            {
                _fileId = Guid.NewGuid().ToString("N");
                CreateCallCount++;
            }
            else
            {
                UpdateCallCount++;
            }

            UploadedFiles[fileName] = content;
            return Task.FromResult(_fileId!);
        }
    }
}

internal static class LocalFakeCloudAuthProviderTestExtensions
{
    /// <summary>Convenience for tests: returns the provider already signed in.</summary>
    public static LocalFakeCloudAuthProvider AlsoSignedIn(this LocalFakeCloudAuthProvider provider)
    {
        provider.SignIn("test@example.com");
        return provider;
    }
}
