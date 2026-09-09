using System.Text.Json;
using Sifra.Vault.Auth;
using Sifra.Vault.Crypto;
using Sifra.Vault.Sync;

namespace Sifra.Vault.GoogleDrive;

/// <summary>
/// Real ICloudSyncProvider implementation (STORY-014's seam). Encrypts
/// every queued change before it ever leaves the device (REQ-010: "the
/// system must use Google Drive for ENCRYPTED cross-device
/// synchronization"), then upserts one file in Drive — never creates a
/// second file for the same vault, so syncing twice cannot double-create
/// remote state. Retries transient (network) failures a capped number of
/// times; an unauthorized response is never retried, since the problem
/// there is the credential, not a transient blip.
/// </summary>
public sealed class GoogleDriveSyncProvider : ICloudSyncProvider
{
    private const string SyncFileName = "sifra-vault-sync.json";

    private readonly IGoogleDriveApiClient _apiClient;
    private readonly VaultEncryptionService _encryption;
    private readonly ICloudAuthProvider _authProvider;
    private readonly string _vaultCredential;
    private readonly int _maxAttempts;
    private readonly TimeSpan _retryDelay;
    private readonly TimeSpan _callTimeout;

    /// <param name="vaultCredential">
    /// The current vault credential, used to derive the encryption key.
    /// Known limitation: held for this provider's lifetime rather than
    /// refreshed per call — same class of trade-off as VaultSession
    /// (STORY-004). A caller must construct a fresh provider (or a future
    /// setter) after a master password change.
    /// </param>
    public GoogleDriveSyncProvider(
        IGoogleDriveApiClient apiClient,
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

    /// <exception cref="GoogleDriveUnauthorizedException">Drive rejected the request as unauthorized. Not retried.</exception>
    /// <exception cref="GoogleDriveSyncFailedException">The retry budget was exhausted.</exception>
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
                var existingFileId = _apiClient.FindFileIdAsync(SyncFileName, cts.Token).GetAwaiter().GetResult();
                _apiClient.UploadOrUpdateFileAsync(SyncFileName, existingFileId, content, cts.Token).GetAwaiter().GetResult();

                return changes.ToDictionary(c => c.ChangeId, _ => SyncOutcome.Accepted);
            }
            catch (GoogleDriveUnauthorizedException)
            {
                throw; // the credential is the problem — retrying will not help
            }
            catch (GoogleDriveNetworkException ex)
            {
                lastNetworkError = ex;
                if (attempt < _maxAttempts)
                {
                    Thread.Sleep(_retryDelay);
                }
            }
        }

        throw new GoogleDriveSyncFailedException(
            $"Could not sync to Google Drive after {_maxAttempts} attempts.",
            lastNetworkError!);
    }

    private sealed record SyncEnvelopeEntry(string ChangeId, string ItemId, string CiphertextBase64);
}
