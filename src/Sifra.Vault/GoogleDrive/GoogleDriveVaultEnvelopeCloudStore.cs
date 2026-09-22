using System.Text.Json;
using Sifra.Vault.Auth;
using Sifra.Vault.Sync;

namespace Sifra.Vault.GoogleDrive;

/// <summary>Real IVaultEnvelopeCloudStore implementation for Google Drive — see IGoogleDriveEnvelopeApiClient's remarks, including its documented conditional-write limitation vs Dropbox.</summary>
public sealed class GoogleDriveVaultEnvelopeCloudStore : IVaultEnvelopeCloudStore
{
    private const string EnvelopeFileName = "sifra-vault-envelope.json";

    private readonly IGoogleDriveEnvelopeApiClient _apiClient;
    private readonly ICloudAuthProvider _authProvider;
    private readonly TimeSpan _callTimeout;

    public GoogleDriveVaultEnvelopeCloudStore(IGoogleDriveEnvelopeApiClient apiClient, ICloudAuthProvider authProvider, TimeSpan? callTimeout = null)
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
        var downloaded = _apiClient.DownloadAsync(EnvelopeFileName, cts.Token).GetAwaiter().GetResult();
        if (downloaded is null)
        {
            return null;
        }

        var envelope = JsonSerializer.Deserialize<VaultSyncEnvelope>(downloaded.Content)
            ?? throw new GoogleDriveSyncFailedException("The vault envelope on Google Drive was empty or malformed.", new JsonException());
        return (envelope, downloaded.Revision);
    }

    public bool TryPush(VaultSyncEnvelope envelope, string? expectedRevision, out string newRevision)
    {
        EnsureConnected();

        var content = JsonSerializer.SerializeToUtf8Bytes(envelope);
        using var cts = new CancellationTokenSource(_callTimeout);
        var newRev = _apiClient.UploadAsync(EnvelopeFileName, content, expectedRevision, cts.Token).GetAwaiter().GetResult();

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
