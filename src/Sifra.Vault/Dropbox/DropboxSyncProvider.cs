using System.Text.Json;
using Sifra.Vault.Auth;
using Sifra.Vault.Crypto;
using Sifra.Vault.Sync;

namespace Sifra.Vault.Dropbox;

/// <summary>
/// Real ICloudSyncProvider implementation (STORY-014's seam) for Dropbox.
/// Same encrypt-before-upload and capped-retry design as
/// GoogleDriveSyncProvider/OneDriveSyncProvider — simpler here because
/// Dropbox's own upload API is idempotent by path (overwrite mode), so
/// there is no separate find-existing-file step.
/// </summary>
public sealed class DropboxSyncProvider : ICloudSyncProvider
{
    private const string SyncFilePath = "/sifra-vault-sync.json";

    private readonly IDropboxApiClient _apiClient;
    private readonly VaultEncryptionService _encryption;
    private readonly ICloudAuthProvider _authProvider;
    private readonly string _vaultCredential;
    private readonly int _maxAttempts;
    private readonly TimeSpan _retryDelay;
    private readonly TimeSpan _callTimeout;

    public DropboxSyncProvider(
        IDropboxApiClient apiClient,
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

    /// <exception cref="DropboxUnauthorizedException">Dropbox rejected the request as unauthorized. Not retried.</exception>
    /// <exception cref="DropboxSyncFailedException">The retry budget was exhausted.</exception>
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
                _apiClient.UploadAsync(SyncFilePath, content, cts.Token).GetAwaiter().GetResult();

                return changes.ToDictionary(c => c.ChangeId, _ => SyncOutcome.Accepted);
            }
            catch (DropboxUnauthorizedException)
            {
                throw;
            }
            catch (DropboxNetworkException ex)
            {
                lastNetworkError = ex;
                if (attempt < _maxAttempts)
                {
                    Thread.Sleep(_retryDelay);
                }
            }
        }

        throw new DropboxSyncFailedException(
            $"Could not sync to Dropbox after {_maxAttempts} attempts.",
            lastNetworkError!);
    }

    private sealed record SyncEnvelopeEntry(string ChangeId, string ItemId, string CiphertextBase64);
}
