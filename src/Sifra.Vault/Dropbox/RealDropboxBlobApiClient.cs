using global::Dropbox.Api;
using global::Dropbox.Api.Files;

namespace Sifra.Vault.Dropbox;

/// <summary>Real Dropbox API calls via the official Dropbox.Api SDK — see IDropboxBlobApiClient's remarks.</summary>
public sealed class RealDropboxBlobApiClient : IDropboxBlobApiClient, IDisposable
{
    private readonly DropboxClient _client;

    public RealDropboxBlobApiClient(string accessToken, TimeSpan? timeout = null)
    {
        _client = new DropboxClient(accessToken, new DropboxClientConfig("Sifra")
        {
            HttpClient = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(30) }
        });
    }

    /// <summary>Uses an already-configured client — e.g. DropboxAuthProvider.CreateClient(), which auto-refreshes an expired access token via its stored refresh token.</summary>
    public RealDropboxBlobApiClient(DropboxClient client)
    {
        _client = client;
    }

    public async Task<bool> ExistsAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await _client.Files.GetMetadataAsync(path);
            return true;
        }
        catch (ApiException<GetMetadataError> ex) when (ex.ErrorResponse.IsPath && ex.ErrorResponse.AsPath.Value.IsNotFound)
        {
            return false;
        }
        catch (AuthException ex)
        {
            throw new DropboxUnauthorizedException("Dropbox rejected the request as unauthorized.", ex);
        }
        catch (Exception ex)
        {
            throw new DropboxNetworkException("Could not check attachment existence on Dropbox.", ex);
        }
    }

    public async Task<byte[]?> DownloadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _client.Files.DownloadAsync(path);
            return await response.GetContentAsByteArrayAsync();
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
            throw new DropboxNetworkException("Could not download an attachment from Dropbox.", ex);
        }
    }

    public async Task UploadAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = new MemoryStream(content);
            await _client.Files.UploadAsync(path, WriteMode.Overwrite.Instance, body: stream);
        }
        catch (AuthException ex)
        {
            throw new DropboxUnauthorizedException("Dropbox rejected the request as unauthorized.", ex);
        }
        catch (Exception ex)
        {
            throw new DropboxNetworkException("Could not upload an attachment to Dropbox.", ex);
        }
    }

    public async Task DeleteAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await _client.Files.DeleteV2Async(path);
        }
        catch (ApiException<DeleteError> ex) when (ex.ErrorResponse.IsPathLookup && ex.ErrorResponse.AsPathLookup.Value.IsNotFound)
        {
            // Already gone — best-effort delete succeeds either way.
        }
        catch (AuthException ex)
        {
            throw new DropboxUnauthorizedException("Dropbox rejected the request as unauthorized.", ex);
        }
        catch (Exception ex)
        {
            throw new DropboxNetworkException("Could not delete an attachment from Dropbox.", ex);
        }
    }

    public void Dispose() => _client.Dispose();
}
