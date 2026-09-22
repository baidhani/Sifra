using System.Text.Json;
using Sifra.Vault.Auth;
using Sifra.Vault.Sync;

namespace Sifra.Vault.Dropbox;

/// <summary>
/// Real IVaultEnvelopeCloudStore implementation for Dropbox. The envelope
/// is plaintext JSON at rest on Dropbox — same trust model as every other
/// synced field in this design (credential VALUES are already ciphertext
/// inside the envelope; only the envelope's outer JSON shape is visible to
/// Dropbox, same as it already is to this app's own SQLite file locally).
/// Dropbox's own file revision (WriteMode.Update's rev / the returned
/// FileMetadata.Rev) is exactly the "opaque revision" IVaultEnvelopeCloudStore
/// asks for — no separate versioning scheme needed.
/// </summary>
public sealed class DropboxVaultEnvelopeCloudStore : IVaultEnvelopeCloudStore
{
    private const string EnvelopePath = "/sifra-vault-envelope.json";

    private readonly IDropboxEnvelopeApiClient _apiClient;
    private readonly ICloudAuthProvider _authProvider;
    private readonly TimeSpan _callTimeout;

    public DropboxVaultEnvelopeCloudStore(IDropboxEnvelopeApiClient apiClient, ICloudAuthProvider authProvider, TimeSpan? callTimeout = null)
    {
        _apiClient = apiClient;
        _authProvider = authProvider;
        _callTimeout = callTimeout ?? TimeSpan.FromSeconds(30);
    }

    public bool IsConnected => _authProvider.IsAuthenticated;

    /// <exception cref="SyncUnavailableException">Not connected.</exception>
    /// <exception cref="DropboxNetworkException">The request failed for network reasons.</exception>
    /// <exception cref="DropboxUnauthorizedException">The request was rejected as unauthorized.</exception>
    public (VaultSyncEnvelope Envelope, string Revision)? Pull()
    {
        EnsureConnected();

        using var cts = new CancellationTokenSource(_callTimeout);
        var downloaded = _apiClient.DownloadAsync(EnvelopePath, cts.Token).GetAwaiter().GetResult();
        if (downloaded is null)
        {
            return null;
        }

        var envelope = JsonSerializer.Deserialize<VaultSyncEnvelope>(downloaded.Content)
            ?? throw new DropboxSyncFailedException("The vault envelope on Dropbox was empty or malformed.", new JsonException());
        return (envelope, downloaded.Rev);
    }

    /// <exception cref="SyncUnavailableException">Not connected.</exception>
    /// <exception cref="DropboxNetworkException">The request failed for network reasons.</exception>
    /// <exception cref="DropboxUnauthorizedException">The request was rejected as unauthorized.</exception>
    public bool TryPush(VaultSyncEnvelope envelope, string? expectedRevision, out string newRevision)
    {
        EnsureConnected();

        var content = JsonSerializer.SerializeToUtf8Bytes(envelope);
        using var cts = new CancellationTokenSource(_callTimeout);
        var newRev = _apiClient.UploadAsync(EnvelopePath, content, expectedRevision, cts.Token).GetAwaiter().GetResult();

        if (newRev is null)
        {
            newRevision = string.Empty;
            return false;
        }

        newRevision = newRev;
        return true;
    }

    private void EnsureConnected()
    {
        if (!IsConnected)
        {
            throw new SyncUnavailableException();
        }
    }
}
