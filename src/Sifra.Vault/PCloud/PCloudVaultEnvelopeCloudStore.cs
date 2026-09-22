using System.Text.Json;
using Sifra.Vault.Auth;
using Sifra.Vault.Sync;

namespace Sifra.Vault.PCloud;

/// <summary>Real IVaultEnvelopeCloudStore implementation for pCloud — see IPCloudEnvelopeApiClient's remarks, including its documented conditional-write limitation vs Dropbox.</summary>
public sealed class PCloudVaultEnvelopeCloudStore : IVaultEnvelopeCloudStore
{
    private const string EnvelopePath = "/sifra-vault-envelope.json";

    private readonly IPCloudEnvelopeApiClient _apiClient;
    private readonly ICloudAuthProvider _authProvider;
    private readonly TimeSpan _callTimeout;

    public PCloudVaultEnvelopeCloudStore(IPCloudEnvelopeApiClient apiClient, ICloudAuthProvider authProvider, TimeSpan? callTimeout = null)
    {
        _apiClient = apiClient;
        _authProvider = authProvider;
        _callTimeout = callTimeout ?? TimeSpan.FromSeconds(30);
    }

    public bool IsConnected => _authProvider.IsAuthenticated;

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
            ?? throw new PCloudSyncFailedException("The vault envelope on pCloud was empty or malformed.", new JsonException());
        return (envelope, downloaded.Hash);
    }

    public bool TryPush(VaultSyncEnvelope envelope, string? expectedRevision, out string newRevision)
    {
        EnsureConnected();

        var content = JsonSerializer.SerializeToUtf8Bytes(envelope);
        using var cts = new CancellationTokenSource(_callTimeout);
        var newHash = _apiClient.UploadAsync(EnvelopePath, content, expectedRevision, cts.Token).GetAwaiter().GetResult();

        if (newHash is null)
        {
            newRevision = string.Empty;
            return false;
        }

        newRevision = newHash;
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
