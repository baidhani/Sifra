using System.Text;
using System.Text.Json;
using Sifra.Vault.Audit;
using Sifra.Vault.Autofill;
using Sifra.Vault.Credentials;
using Sifra.Vault.Crypto;

// Chrome's native messaging host: reads/writes messages on stdin/stdout,
// each framed as a 4-byte little-endian length prefix followed by that
// many bytes of UTF-8 JSON. Runs pointed at the same vault data as the
// CLI (default %AppData%\Sifra). Chrome itself launches this process with
// its own argv (the calling extension's origin, not a data directory), so
// the override must come from an environment variable — args are only
// used by the manual protocol probe, never by the real browser launch.
string? dataDirectory = Environment.GetEnvironmentVariable("SIFRA_DATA_DIR");
var resolvedDataDirectory = dataDirectory ?? Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sifra");

var auditLogger = new AuditLogger(
    new FileAuditLogSink(Path.Combine(resolvedDataDirectory, "operations.log")),
    new LocalFakeAdminAlertSink());

var autofillService = new CredentialAutofillService(
    new CredentialService(
        new CredentialStore(dataDirectory),
        new VaultEncryptionService(new VaultMasterKeyStore(dataDirectory)),
        new TextCopyCredentialClipboard()),
    auditLogger);

using var stdin = Console.OpenStandardInput();
using var stdout = Console.OpenStandardOutput();

while (true)
{
    var lengthBytes = ReadExactly(stdin, 4);
    if (lengthBytes is null)
    {
        break; // stdin closed — the browser disconnected
    }

    var length = BitConverter.ToInt32(lengthBytes, 0);
    var messageBytes = ReadExactly(stdin, length);
    if (messageBytes is null)
    {
        break;
    }

    var responseJson = HandleRequest(Encoding.UTF8.GetString(messageBytes), autofillService);
    var responseBytes = Encoding.UTF8.GetBytes(responseJson);
    stdout.Write(BitConverter.GetBytes(responseBytes.Length), 0, 4);
    stdout.Write(responseBytes, 0, responseBytes.Length);
    stdout.Flush();
}

static byte[]? ReadExactly(Stream stream, int count)
{
    var buffer = new byte[count];
    var offset = 0;
    while (offset < count)
    {
        var read = stream.Read(buffer, offset, count - offset);
        if (read == 0)
        {
            return null; // end of stream
        }
        offset += read;
    }
    return buffer;
}

static string HandleRequest(string requestJson, CredentialAutofillService autofillService)
{
    try
    {
        using var doc = JsonDocument.Parse(requestJson);
        var root = doc.RootElement;
        var action = root.GetProperty("action").GetString();
        var vaultCredential = root.GetProperty("vaultCredential").GetString()!;
        var url = root.GetProperty("url").GetString()!;

        switch (action)
        {
            case "discover":
                var offered = autofillService.OfferCredentialsForUrl(vaultCredential, url);
                var payload = offered.Select(c => new { id = c.Id, label = c.Label, username = c.Username });
                return JsonSerializer.Serialize(new { ok = true, credentials = payload });

            case "fill":
                var credentialId = root.GetProperty("credentialId").GetString()!;
                var consent = root.GetProperty("consent").GetBoolean();
                var view = autofillService.FillCredential(vaultCredential, credentialId, url, consent);
                return JsonSerializer.Serialize(new { ok = true, username = view.Username, password = view.Password });

            default:
                return JsonSerializer.Serialize(new { ok = false, error = "UnknownAction", message = $"Unknown action '{action}'." });
        }
    }
    catch (Exception ex)
    {
        // Never crash the host on a bad/failed request — report the error
        // back to the extension instead. The exception type name lets the
        // extension distinguish "wrong consent" from "wrong domain" from
        // "wrong vault password" without parsing message text.
        return JsonSerializer.Serialize(new { ok = false, error = ex.GetType().Name, message = ex.Message });
    }
}
