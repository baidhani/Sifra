using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Sifra.Vault.OneDrive;

/// <summary>Real Microsoft Graph calls, scoped to the app's dedicated OneDrive folder (Files.ReadWrite.AppFolder).</summary>
public sealed class RealOneDriveApiClient : IOneDriveApiClient, IDisposable
{
    private const string GraphBaseUrl = "https://graph.microsoft.com/v1.0";

    private readonly HttpClient _httpClient;

    public RealOneDriveApiClient(string accessToken, TimeSpan? timeout = null)
    {
        _httpClient = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(30) };
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    public async Task<string?> FindFileIdAsync(string fileName, CancellationToken cancellationToken)
    {
        try
        {
            var url = $"{GraphBaseUrl}/me/drive/special/approot/children?$filter=name eq '{Uri.EscapeDataString(fileName)}'&$select=id,name";
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            await ThrowIfUnauthorizedAsync(response);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var values = doc.RootElement.GetProperty("value");
            return values.GetArrayLength() > 0 ? values[0].GetProperty("id").GetString() : null;
        }
        catch (OneDriveUnauthorizedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new OneDriveNetworkException("Could not reach Microsoft Graph.", ex);
        }
    }

    public async Task<string> UploadOrUpdateFileAsync(string fileName, string? existingItemId, byte[] content, CancellationToken cancellationToken)
    {
        try
        {
            // Graph upserts by path within the app folder — create-or-replace
            // in one call, regardless of existingItemId.
            var url = $"{GraphBaseUrl}/me/drive/special/approot:/{Uri.EscapeDataString(fileName)}:/content";
            using var requestContent = new ByteArrayContent(content);
            requestContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            using var response = await _httpClient.PutAsync(url, requestContent, cancellationToken);
            await ThrowIfUnauthorizedAsync(response);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("id").GetString()
                ?? throw new OneDriveNetworkException("Upload succeeded but returned no item id.", new InvalidOperationException());
        }
        catch (OneDriveUnauthorizedException)
        {
            throw;
        }
        catch (OneDriveNetworkException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new OneDriveNetworkException("Could not upload to OneDrive.", ex);
        }
    }

    private static async Task ThrowIfUnauthorizedAsync(HttpResponseMessage response)
    {
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new OneDriveUnauthorizedException($"Microsoft Graph rejected the request ({(int)response.StatusCode}).");
        }
    }

    public void Dispose() => _httpClient.Dispose();
}
