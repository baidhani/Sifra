using System.Text.Json;

namespace Sifra.Vault.Audit;

/// <summary>
/// Appends one JSON line per audit entry to a local file. Local storage is
/// the real, appropriate choice here — no external logging service is
/// needed for a local-first vault — but the write can still fail (disk
/// full, permissions, path unavailable), which is exactly the "logging
/// service is unavailable" failure path AuditLogger retries against.
/// </summary>
public sealed class FileAuditLogSink : IAuditLogSink
{
    private readonly string _filePath;
    private readonly object _writeLock = new();

    public FileAuditLogSink(string filePath)
    {
        _filePath = filePath;
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public void Write(OperationLogEntry entry)
    {
        var line = JsonSerializer.Serialize(entry) + Environment.NewLine;
        try
        {
            lock (_writeLock)
            {
                File.AppendAllText(_filePath, line);
            }
        }
        catch (IOException ex)
        {
            throw new AuditSinkUnavailableException($"Could not write audit log entry to '{_filePath}'.", ex);
        }
    }

    public IReadOnlyList<string> ReadAll() =>
        File.Exists(_filePath) ? File.ReadAllLines(_filePath) : Array.Empty<string>();
}
