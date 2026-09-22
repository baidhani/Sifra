using global::Dropbox.Api;
using global::Dropbox.Api.Files;

namespace Sifra.Vault.Dropbox;

/// <summary>Real Dropbox API calls via the official Dropbox.Api SDK — see IDropboxEnvelopeApiClient's remarks.</summary>
public sealed class RealDropboxEnvelopeApiClient : IDropboxEnvelopeApiClient, IDisposable
{
    private readonly DropboxClient _client;

    public RealDropboxEnvelopeApiClient(string accessToken, TimeSpan? timeout = null)
    {
        _client = new DropboxClient(accessToken, new DropboxClientConfig("Sifra")
        {
            HttpClient = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(30) }
        });
    }

    /// <summary>Uses an already-configured client — e.g. DropboxAuthProvider.CreateClient(), which auto-refreshes an expired access token via its stored refresh token.</summary>
    public RealDropboxEnvelopeApiClient(DropboxClient client)
    {
        _client = client;
    }

    public async Task<DropboxDownloadResult?> DownloadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _client.Files.DownloadAsync(path);
            var content = await response.GetContentAsByteArrayAsync();
            return new DropboxDownloadResult(content, response.Response.Rev);
        }
        catch (ApiException<DownloadError> ex) when (ex.ErrorResponse.IsPath && ex.ErrorResponse.AsPath.Value.IsNotFound)
        {
            return null;
        }
        catch (AuthException ex)
        {
            throw new DropboxUnauthorizedException("Dropbox rejected the request as unauthorized.", ex);
        }
        catch (Exception ex)
        {
            throw new DropboxNetworkException("Could not download from Dropbox.", ex);
        }
    }

    public async Task<string?> UploadAsync(string path, byte[] content, string? expectedRev, CancellationToken cancellationToken)
    {
        try
        {
            WriteMode writeMode = expectedRev is null ? WriteMode.Add.Instance : new WriteMode.Update(expectedRev);
            using var stream = new MemoryStream(content);
            var metadata = await _client.Files.UploadAsync(path, writeMode, body: stream);
            return metadata.Rev;
        }
        catch (ApiException<UploadError> ex) when (IsConflict(ex))
        {
            return null;
        }
        catch (AuthException ex)
        {
            throw new DropboxUnauthorizedException("Dropbox rejected the request as unauthorized.", ex);
        }
        catch (Exception ex)
        {
            throw new DropboxNetworkException("Could not upload to Dropbox.", ex);
        }
    }

    private static bool IsConflict(ApiException<UploadError> ex) =>
        ex.ErrorResponse.IsPath && ex.ErrorResponse.AsPath.Value.Reason.IsConflict;

    public void Dispose() => _client.Dispose();
}
