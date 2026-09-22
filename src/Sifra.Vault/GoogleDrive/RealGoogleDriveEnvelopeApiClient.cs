using System.Net;
using Google;
using Google.Apis.Download;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;

namespace Sifra.Vault.GoogleDrive;

/// <summary>Real Drive API calls via Google's official client library — see IGoogleDriveEnvelopeApiClient's remarks (including its documented conditional-write limitation).</summary>
public sealed class RealGoogleDriveEnvelopeApiClient : IGoogleDriveEnvelopeApiClient, IDisposable
{
    private readonly DriveService _driveService;

    public RealGoogleDriveEnvelopeApiClient(Google.Apis.Http.IConfigurableHttpClientInitializer credential, TimeSpan? timeout = null)
    {
        _driveService = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Sifra",
        });
        _driveService.HttpClient.Timeout = timeout ?? TimeSpan.FromSeconds(30);
    }

    public async Task<GoogleDriveDownloadResult?> DownloadAsync(string fileName, CancellationToken cancellationToken)
    {
        try
        {
            var fileId = await FindFileIdAsync(fileName, cancellationToken);
            if (fileId is null)
            {
                return null;
            }

            var metadataRequest = _driveService.Files.Get(fileId);
            metadataRequest.Fields = "id, headRevisionId";
            var metadata = await metadataRequest.ExecuteAsync(cancellationToken);

            using var stream = new MemoryStream();
            var downloadRequest = _driveService.Files.Get(fileId);
            var progress = await downloadRequest.DownloadAsync(stream, cancellationToken);
            if (progress.Status != DownloadStatus.Completed)
            {
                throw new GoogleDriveNetworkException("Download from Google Drive did not complete.", progress.Exception ?? new InvalidOperationException(progress.Status.ToString()));
            }

            return new GoogleDriveDownloadResult(stream.ToArray(), RevisionOf(metadata));
        }
        catch (GoogleApiException ex) when (IsUnauthorized(ex))
        {
            throw new GoogleDriveUnauthorizedException("Google Drive rejected the request as unauthorized.", ex);
        }
        catch (Exception ex) when (ex is not GoogleDriveUnauthorizedException and not GoogleDriveNetworkException)
        {
            throw new GoogleDriveNetworkException("Could not download from Google Drive.", ex);
        }
    }

    public async Task<string?> UploadAsync(string fileName, byte[] content, string? expectedRevision, CancellationToken cancellationToken)
    {
        try
        {
            var fileId = await FindFileIdAsync(fileName, cancellationToken);

            // Best-effort conditional check — see IGoogleDriveEnvelopeApiClient's
            // remarks: not atomic, unlike Dropbox's server-enforced rev check.
            string? currentRevision = null;
            if (fileId is not null)
            {
                var metadataRequest = _driveService.Files.Get(fileId);
                metadataRequest.Fields = "id, headRevisionId";
                currentRevision = RevisionOf(await metadataRequest.ExecuteAsync(cancellationToken));
            }

            if (currentRevision != expectedRevision)
            {
                return null;
            }

            using var stream = new MemoryStream(content);
            IUploadProgress progress;
            Google.Apis.Drive.v3.Data.File? resultFile;

            if (fileId is null)
            {
                var metadata = new Google.Apis.Drive.v3.Data.File { Name = fileName };
                var request = _driveService.Files.Create(metadata, stream, "application/octet-stream");
                request.Fields = "id, headRevisionId";
                progress = await request.UploadAsync(cancellationToken);
                resultFile = request.ResponseBody;
            }
            else
            {
                var request = _driveService.Files.Update(new Google.Apis.Drive.v3.Data.File(), fileId, stream, "application/octet-stream");
                request.Fields = "id, headRevisionId";
                progress = await request.UploadAsync(cancellationToken);
                resultFile = request.ResponseBody;
            }

            if (progress.Status != UploadStatus.Completed)
            {
                throw new GoogleDriveNetworkException("Upload to Google Drive did not complete.", progress.Exception ?? new InvalidOperationException(progress.Status.ToString()));
            }

            return resultFile is null
                ? throw new GoogleDriveNetworkException("Upload completed but returned no file.", new InvalidOperationException())
                : RevisionOf(resultFile);
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

    private async Task<string?> FindFileIdAsync(string fileName, CancellationToken cancellationToken)
    {
        var request = _driveService.Files.List();
        request.Q = $"name = '{fileName}' and trashed = false";
        request.Spaces = "drive";
        request.Fields = "files(id, name)";
        var result = await request.ExecuteAsync(cancellationToken);
        return result.Files is { Count: > 0 } ? result.Files[0].Id : null;
    }

    // HeadRevisionId changes with every content write, so it's a closer analog to
    // Dropbox's per-content rev than Version (which can also bump on metadata-only
    // changes) — falls back to Id only in case a file genuinely lacks one.
    private static string RevisionOf(Google.Apis.Drive.v3.Data.File file) => file.HeadRevisionId ?? file.Id;

    private static bool IsUnauthorized(GoogleApiException ex) =>
        ex.HttpStatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;

    public void Dispose() => _driveService.Dispose();
}
