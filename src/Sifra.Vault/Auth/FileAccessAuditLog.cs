namespace Sifra.Vault.Auth;

/// <summary>
/// Appends one line per access event to its own file. Vault access and
/// cloud access each get a separate instance pointed at a separate file —
/// that separation is what makes "logged independently" structurally true
/// rather than just a label on shared rows.
/// </summary>
public sealed class FileAccessAuditLog : IAccessAuditLog
{
    private readonly string _logFilePath;
    private readonly object _writeLock = new();

    public FileAccessAuditLog(string logFilePath)
    {
        _logFilePath = logFilePath;
        var directory = Path.GetDirectoryName(logFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public void Record(string eventName, bool success)
    {
        var line = $"{DateTimeOffset.UtcNow:O}\t{eventName}\t{(success ? "success" : "failure")}{Environment.NewLine}";
        lock (_writeLock)
        {
            File.AppendAllText(_logFilePath, line);
        }
    }

    public IReadOnlyList<string> ReadAll() =>
        File.Exists(_logFilePath) ? File.ReadAllLines(_logFilePath) : Array.Empty<string>();
}
