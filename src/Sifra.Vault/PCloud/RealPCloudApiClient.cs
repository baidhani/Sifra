using System.Net.Http.Headers;
using System.Text.Json;

namespace Sifra.Vault.PCloud;

/// <summary>
/// Real pCloud API calls via raw REST (docs.pcloud.com/methods/) — pCloud
/// has no official .NET SDK, so this talks HTTP directly, the same way
/// Sifra already does for its handful of raw calls elsewhere. Uses
/// "stat" for existence/hash checks, "uploadfile" (multipart) for writes,
/// "getfilelink" + a follow-up GET to the returned CDN host for downloads
/// (pCloud doesn't return file content directly from a single call the
/// way Dropbox does), and "deletefile" for deletes.
///
/// IMPORTANT: exact pCloud response shapes here are built from published
/// documentation, not yet confirmed against a live account (no test
/// account was available at the time this was written) — the error-code
/// handling in particular (2009 = not found) should be re-verified on
/// first live run and adjusted if pCloud's actual responses differ.
/// </summary>
public sealed class RealPCloudApiClient : IPCloudEnvelopeApiClient, IPCloudBlobApiClient, IDisposable
{
    private const int FileNotFoundErrorCode = 2009;

    private readonly HttpClient _client;
    private readonly string _apiHost;

    public RealPCloudApiClient(string accessToken, string apiHost, TimeSpan? timeout = null)
    {
        _apiHost = apiHost;
        _client = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(30) };
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    // ----- Envelope (hash-conditional) -----

    public async Task<PCloudDownloadResult?> DownloadAsync(string path, CancellationToken cancellationToken)
    {
        var stat = await StatAsync(path, cancellationToken);
        if (stat is null)
        {
            return null;
        }

        var content = await DownloadContentAsync(path, cancellationToken);
        return content is null ? null : new PCloudDownloadResult(content, stat.Value.Hash);
    }

    public async Task<string?> UploadAsync(string path, byte[] content, string? expectedHash, CancellationToken cancellationToken)
    {
        // Best-effort client-side check — see IPCloudEnvelopeApiClient's
        // remarks: pCloud has no documented atomic conditional-write, so
        // this cannot fully close the race the way Dropbox's server-side
        // WriteMode.Update(rev) does.
        var current = await StatAsync(path, cancellationToken);
        var currentHash = current?.Hash;
        if (currentHash != expectedHash)
        {
            return null;
        }

        return await UploadInternalAsync(path, content, cancellationToken);
    }

    // ----- Blob (unconditional) -----

    public async Task<bool> ExistsAsync(string path, CancellationToken cancellationToken) =>
        await StatAsync(path, cancellationToken) is not null;

    async Task<byte[]?> IPCloudBlobApiClient.DownloadAsync(string path, CancellationToken cancellationToken) =>
        await DownloadContentAsync(path, cancellationToken);

    public async Task UploadAsync(string path, byte[] content, CancellationToken cancellationToken) =>
        await UploadInternalAsync(path, content, cancellationToken);

    public async Task DeleteAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _client.GetAsync(
                $"https://{_apiHost}/deletefile?path={Uri.EscapeDataString(path)}", cancellationToken);
            var json = await ParseResponseAsync(response);
            var errorCode = ErrorCodeOf(json);
            if (errorCode is not (null or FileNotFoundErrorCode) )
            {
                throw new PCloudNetworkException($"pCloud deletefile failed: {ErrorTextOf(json)}", new InvalidOperationException());
            }
        }
        catch (PCloudNetworkException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new PCloudNetworkException("Could not delete a file on pCloud.", ex);
        }
    }

    // ----- Shared helpers -----

    private async Task<(string Hash, long FileId)?> StatAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _client.GetAsync(
                $"https://{_apiHost}/stat?path={Uri.EscapeDataString(path)}", cancellationToken);
            var json = await ParseResponseAsync(response);
            var errorCode = ErrorCodeOf(json);
            if (errorCode == FileNotFoundErrorCode)
            {
                return null;
            }
            if (errorCode is not null)
            {
                throw new PCloudNetworkException($"pCloud stat failed: {ErrorTextOf(json)}", new InvalidOperationException());
            }

            var metadata = json.GetProperty("metadata");
            var hash = metadata.GetProperty("hash").GetRawText();
            var fileId = metadata.TryGetProperty("fileid", out var fid) ? fid.GetInt64() : 0;
            return (hash, fileId);
        }
        catch (PCloudNetworkException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new PCloudNetworkException("Could not check file status on pCloud.", ex);
        }
    }

    private async Task<byte[]?> DownloadContentAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var linkResponse = await _client.GetAsync(
                $"https://{_apiHost}/getfilelink?path={Uri.EscapeDataString(path)}", cancellationToken);
            var linkJson = await ParseResponseAsync(linkResponse);
            var errorCode = ErrorCodeOf(linkJson);
            if (errorCode == FileNotFoundErrorCode)
            {
                return null;
            }
            if (errorCode is not null)
            {
                throw new PCloudNetworkException($"pCloud getfilelink failed: {ErrorTextOf(linkJson)}", new InvalidOperationException());
            }

            var host = linkJson.GetProperty("hosts")[0].GetString();
            var filePath = linkJson.GetProperty("path").GetString();
            using var contentResponse = await _client.GetAsync($"https://{host}{filePath}", cancellationToken);
            contentResponse.EnsureSuccessStatusCode();
            return await contentResponse.Content.ReadAsByteArrayAsync(cancellationToken);
        }
        catch (PCloudNetworkException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new PCloudNetworkException("Could not download a file from pCloud.", ex);
        }
    }

    private async Task<string> UploadInternalAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        try
        {
            var folder = "/" + string.Join('/', path.TrimStart('/').Split('/')[..^1]);
            var fileName = path[(path.LastIndexOf('/') + 1)..];

            using var form = new MultipartFormDataContent();
            using var fileContent = new ByteArrayContent(content);
            form.Add(fileContent, "file", fileName);

            using var response = await _client.PostAsync(
                $"https://{_apiHost}/uploadfile?path={Uri.EscapeDataString(folder)}&filename={Uri.EscapeDataString(fileName)}",
                form, cancellationToken);
            var json = await ParseResponseAsync(response);
            var errorCode = ErrorCodeOf(json);
            if (errorCode is not null)
            {
                throw new PCloudNetworkException($"pCloud uploadfile failed: {ErrorTextOf(json)}", new InvalidOperationException());
            }

            return json.GetProperty("metadata")[0].GetProperty("hash").GetRawText();
        }
        catch (PCloudNetworkException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new PCloudNetworkException("Could not upload a file to pCloud.", ex);
        }
    }

    private static async Task<JsonElement> ParseResponseAsync(HttpResponseMessage response)
    {
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            throw new PCloudUnauthorizedException("pCloud rejected the request as unauthorized.");
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static int? ErrorCodeOf(JsonElement json) =>
        json.TryGetProperty("result", out var result) && result.GetInt32() != 0 ? result.GetInt32() : null;

    private static string ErrorTextOf(JsonElement json) =>
        json.TryGetProperty("error", out var error) ? error.GetString() ?? "unknown error" : "unknown error";

    public void Dispose() => _client.Dispose();
}
