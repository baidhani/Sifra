using System.Text.Json;
using Sifra.Vault.Auth;
using Sifra.Vault.Crypto;
using Sifra.Vault.Sync;

namespace Sifra.Vault.OneDrive;

/// <summary>
/// Real ICloudSyncProvider implementation (STORY-014's seam) for OneDrive.
/// Same shape as GoogleDriveSyncProvider (STORY-007): encrypts every
/// queued change before it ever leaves the device, upserts one file
/// rather than ever creating a second one, retries network failures a
/// capped number of times, and never retries an unauthorized response.
/// </summary>
public sealed class OneDriveSyncProvider : ICloudSyncProvider
{
    private const string SyncFileName = "sifra-vault-sync.json";

    private readonly IOneDriveApiClient _apiClient;
    private readonly VaultEncryptionService _encryption;
    private readonly ICloudAuthProvider _authProvider;
    private readonly string _vaultCredential;
    private readonly int _maxAttempts;
    private readonly TimeSpan _retryDelay;
    private readonly TimeSpan _callTimeout;

    public OneDriveSyncProvider(
        IOneDriveApiClient apiClient,
        VaultEncryptionService encryption,
        ICloudAuthProvider authProvider,
        string vaultCredential,
        int maxAttempts = 3,
        TimeSpan? retryDelay = null,
        TimeSpan? callTimeout = null)
    {
        _apiClient = apiClient;
        _encryption = encryption;
        _authProvider = authProvider;
        _vaultCredential = vaultCredential;
        _maxAttempts = maxAttempts;
        _retryDelay = retryDelay ?? TimeSpan.FromSeconds(1);
        _callTimeout = callTimeout ?? TimeSpan.FromSeconds(30);
    }

    public bool IsConnected => _authProvider.IsAuthenticated;

    /// <exception cref="OneDriveUnauthorizedException">OneDrive rejected the request as unauthorized. Not retried.</exception>
    /// <exception cref="OneDriveSyncFailedException">The retry budget was exhausted.</exception>
    public IReadOnlyDictionary<string, SyncOutcome> Push(IReadOnlyList<QueuedChange> changes)
    {
        if (changes.Count == 0)
        {
            return new Dictionary<string, SyncOutcome>();
        }

        var vmk = _encryption.DeriveKey(_vaultCredential);
        var envelope = changes
            .Select(c => new SyncEnvelopeEntry(c.ChangeId, c.ItemId, _encryption.Encrypt(c.PayloadJson, vmk)))
            .ToList();
        var content = JsonSerializer.SerializeToUtf8Bytes(envelope);

        Exception? lastNetworkError = null;
        for (int attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            try
            {
                using var cts = new CancellationTokenSource(_callTimeout);
                var existingItemId = _apiClient.FindFileIdAsync(SyncFileName, cts.Token).GetAwaiter().GetResult();
                _apiClient.UploadOrUpdateFileAsync(SyncFileName, existingItemId, content, cts.Token).GetAwaiter().GetResult();

                return changes.ToDictionary(c => c.ChangeId, _ => SyncOutcome.Accepted);
            }
            catch (OneDriveUnauthorizedException)
            {
                throw;
            }
            catch (OneDriveNetworkException ex)
            {
                lastNetworkError = ex;
                if (attempt < _maxAttempts)
                {
                    Thread.Sleep(_retryDelay);
                }
            }
        }

        throw new OneDriveSyncFailedException(
            $"Could not sync to OneDrive after {_maxAttempts} attempts.",
            lastNetworkError!);
    }

    private sealed record SyncEnvelopeEntry(string ChangeId, string ItemId, string CiphertextBase64);
}
