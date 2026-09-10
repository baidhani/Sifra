using global::Dropbox.Api;
using global::Dropbox.Api.Files;

namespace Sifra.Vault.Dropbox;

/// <summary>Real Dropbox API calls via the official Dropbox.Api SDK.</summary>
public sealed class RealDropboxApiClient : IDropboxApiClient, IDisposable
{
    private readonly DropboxClient _client;

    public RealDropboxApiClient(string accessToken, TimeSpan? timeout = null)
    {
        _client = new DropboxClient(accessToken, new DropboxClientConfig("Sifra")
        {
            HttpClient = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(30) }
        });
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
            throw new DropboxNetworkException("Could not upload to Dropbox.", ex);
        }
    }

    public void Dispose() => _client.Dispose();
}
