using System.Net;
using Google;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;

namespace Sifra.Vault.GoogleDrive;

/// <summary>Real Drive API calls via Google's official client library.</summary>
public sealed class RealGoogleDriveApiClient : IGoogleDriveApiClient, IDisposable
{
    private readonly DriveService _driveService;

    public RealGoogleDriveApiClient(Google.Apis.Http.IConfigurableHttpClientInitializer credential, TimeSpan? timeout = null)
    {
        _driveService = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Sifra",
        });
        _driveService.HttpClient.Timeout = timeout ?? TimeSpan.FromSeconds(30);
    }

    public async Task<string?> FindFileIdAsync(string fileName, CancellationToken cancellationToken)
    {
        try
        {
            var request = _driveService.Files.List();
            request.Q = $"name = '{fileName}' and trashed = false";
            request.Spaces = "drive";
            request.Fields = "files(id, name)";
            var result = await request.ExecuteAsync(cancellationToken);
            return result.Files is { Count: > 0 } ? result.Files[0].Id : null;
        }
        catch (GoogleApiException ex) when (IsUnauthorized(ex))
        {
            throw new GoogleDriveUnauthorizedException("Google Drive rejected the request as unauthorized.", ex);
        }
        catch (Exception ex) when (ex is not GoogleDriveUnauthorizedException)
        {
            throw new GoogleDriveNetworkException("Could not reach Google Drive.", ex);
        }
    }

    public async Task<string> UploadOrUpdateFileAsync(string fileName, string? existingFileId, byte[] content, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = new MemoryStream(content);
            IUploadProgress progress;
            string fileId;

            if (existingFileId is null)
            {
                var metadata = new Google.Apis.Drive.v3.Data.File { Name = fileName };
                var request = _driveService.Files.Create(metadata, stream, "application/octet-stream");
                progress = await request.UploadAsync(cancellationToken);
                fileId = request.ResponseBody?.Id ?? throw new GoogleDriveNetworkException("Upload completed but returned no file id.", new InvalidOperationException());
            }
            else
            {
                var request = _driveService.Files.Update(new Google.Apis.Drive.v3.Data.File(), existingFileId, stream, "application/octet-stream");
                progress = await request.UploadAsync(cancellationToken);
                fileId = existingFileId;
            }

            if (progress.Status != UploadStatus.Completed)
            {
                throw new GoogleDriveNetworkException("Upload to Google Drive did not complete.", progress.Exception ?? new InvalidOperationException(progress.Status.ToString()));
            }

            return fileId;
        }
        catch (GoogleApiException ex) when (IsUnauthorized(ex))
        {
            throw new GoogleDriveUnauthorizedException("Google Drive rejected the request as unauthorized.", ex);
        }
        catch (Exception ex) when (ex is not GoogleDriveUnauthorizedException and not GoogleDriveNetworkException)
        {
            throw new GoogleDriveNetworkException("Could not upload to Google Drive.", ex);
        }
    }

    private static bool IsUnauthorized(GoogleApiException ex) =>
        ex.HttpStatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;

    public void Dispose() => _driveService.Dispose();
}
