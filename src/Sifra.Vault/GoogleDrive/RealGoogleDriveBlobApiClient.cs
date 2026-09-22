using System.Net;
using Google;
using Google.Apis.Download;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;

namespace Sifra.Vault.GoogleDrive;

/// <summary>Real Drive API calls via Google's official client library — see IGoogleDriveBlobApiClient's remarks.</summary>
public sealed class RealGoogleDriveBlobApiClient : IGoogleDriveBlobApiClient, IDisposable
{
    private readonly DriveService _driveService;

    public RealGoogleDriveBlobApiClient(Google.Apis.Http.IConfigurableHttpClientInitializer credential, TimeSpan? timeout = null)
    {
        _driveService = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Sifra",
        });
        _driveService.HttpClient.Timeout = timeout ?? TimeSpan.FromSeconds(30);
    }

    public async Task<bool> ExistsAsync(string fileName, CancellationToken cancellationToken)
    {
        try
        {
            return await FindFileIdAsync(fileName, cancellationToken) is not null;
        }
        catch (GoogleApiException ex) when (IsUnauthorized(ex))
        {
            throw new GoogleDriveUnauthorizedException("Google Drive rejected the request as unauthorized.", ex);
        }
        catch (Exception ex) when (ex is not GoogleDriveUnauthorizedException)
        {
            throw new GoogleDriveNetworkException("Could not check attachment existence on Google Drive.", ex);
        }
    }

    public async Task<byte[]?> DownloadAsync(string fileName, CancellationToken cancellationToken)
    {
        try
        {
            var fileId = await FindFileIdAsync(fileName, cancellationToken);
            if (fileId is null)
            {
                return null;
            }

            using var stream = new MemoryStream();
            var request = _driveService.Files.Get(fileId);
            var progress = await request.DownloadAsync(stream, cancellationToken);
            if (progress.Status != DownloadStatus.Completed)
            {
                throw new GoogleDriveNetworkException("Download from Google Drive did not complete.", progress.Exception ?? new InvalidOperationException(progress.Status.ToString()));
            }

            return stream.ToArray();
        }
        catch (GoogleApiException ex) when (IsUnauthorized(ex))
        {
            throw new GoogleDriveUnauthorizedException("Google Drive rejected the request as unauthorized.", ex);
        }
        catch (Exception ex) when (ex is not GoogleDriveUnauthorizedException and not GoogleDriveNetworkException)
        {
            throw new GoogleDriveNetworkException("Could not download an attachment from Google Drive.", ex);
        }
    }

    public async Task UploadAsync(string fileName, byte[] content, CancellationToken cancellationToken)
    {
        try
        {
            var fileId = await FindFileIdAsync(fileName, cancellationToken);
            using var stream = new MemoryStream(content);
            IUploadProgress progress;

            if (fileId is null)
            {
                var metadata = new Google.Apis.Drive.v3.Data.File { Name = fileName };
                var request = _driveService.Files.Create(metadata, stream, "application/octet-stream");
                progress = await request.UploadAsync(cancellationToken);
            }
            else
            {
                var request = _driveService.Files.Update(new Google.Apis.Drive.v3.Data.File(), fileId, stream, "application/octet-stream");
                progress = await request.UploadAsync(cancellationToken);
            }

            if (progress.Status != UploadStatus.Completed)
            {
                throw new GoogleDriveNetworkException("Upload to Google Drive did not complete.", progress.Exception ?? new InvalidOperationException(progress.Status.ToString()));
            }
        }
        catch (GoogleApiException ex) when (IsUnauthorized(ex))
        {
            throw new GoogleDriveUnauthorizedException("Google Drive rejected the request as unauthorized.", ex);
        }
        catch (Exception ex) when (ex is not GoogleDriveUnauthorizedException and not GoogleDriveNetworkException)
        {
            throw new GoogleDriveNetworkException("Could not upload an attachment to Google Drive.", ex);
        }
    }

    public async Task DeleteAsync(string fileName, CancellationToken cancellationToken)
    {
        try
        {
            var fileId = await FindFileIdAsync(fileName, cancellationToken);
            if (fileId is null)
            {
                return; // already gone — best-effort delete succeeds either way
            }
            await _driveService.Files.Delete(fileId).ExecuteAsync(cancellationToken);
        }
        catch (GoogleApiException ex) when (IsUnauthorized(ex))
        {
            throw new GoogleDriveUnauthorizedException("Google Drive rejected the request as unauthorized.", ex);
        }
        catch (Exception ex) when (ex is not GoogleDriveUnauthorizedException)
        {
            throw new GoogleDriveNetworkException("Could not delete an attachment from Google Drive.", ex);
        }
    }

    private async Task<string?> FindFileIdAsync(string fileName, CancellationToken cancellationToken)
    {
        var request = _driveService.Files.List();
        request.Q = $"name = '{fileName}' and trashed = false";
        request.Spaces = "drive";
        request.Fields = "files(id, name)";
        var result = await request.ExecuteAsync(cancellationToken);
        return result.Files is { Count: > 0 } ? result.Files[0].Id : null;
    }

    private static bool IsUnauthorized(GoogleApiException ex) =>
        ex.HttpStatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;

    public void Dispose() => _driveService.Dispose();
}
