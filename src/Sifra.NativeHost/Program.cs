using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Sifra.Vault.Audit;
using Sifra.Vault.Autofill;
using Sifra.Vault.Credentials;
using Sifra.Vault.Crypto;
using Sifra.Vault.Devices;
using Sifra.Vault.Pairing;

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

var encryptionService = new VaultEncryptionService(new VaultMasterKeyStore(dataDirectory));
var autofillService = new CredentialAutofillService(
    new CredentialService(
        new CredentialStore(dataDirectory),
        encryptionService,
        new TextCopyCredentialClipboard()),
    auditLogger);

var deviceIdentityService = new DeviceIdentityService(new DeviceRegistryStore(dataDirectory), auditLogger);

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

    var responseJson = HandleRequest(Encoding.UTF8.GetString(messageBytes), autofillService, deviceIdentityService, encryptionService);
    var responseBytes = Encoding.UTF8.GetBytes(responseJson);
    stdout.Write(BitConverter.GetBytes(responseBytes.Length), 0, 4);
    stdout.Write(responseBytes, 0, responseBytes.Length);
    stdout.Flush();
}

static string LoginValue(CredentialView view) =>
    view.Fields.FirstOrDefault(f => f.Type == CustomFieldType.Login)?.Value ?? string.Empty;

static string PasswordValue(CredentialView view) =>
    view.Fields.FirstOrDefault(f => f.Type == CustomFieldType.Password)?.Value ?? string.Empty;

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

static string HandleRequest(
    string requestJson, CredentialAutofillService autofillService, DeviceIdentityService deviceIdentityService, VaultEncryptionService encryptionService)
{
    try
    {
        using var doc = JsonDocument.Parse(requestJson);
        var root = doc.RootElement;
        var action = root.GetProperty("action").GetString();

        // "pair" has no device identity yet by definition — it's the
        // action that creates one — so it's handled before the
        // device-verification gate below, not after it.
        if (action == "pair")
        {
            var browserName = root.TryGetProperty("browserName", out var bn) ? bn.GetString() ?? "Browser extension" : "Browser extension";
            return PerformPairingAsync(browserName).GetAwaiter().GetResult();
        }

        // Every other action requires a device already paired via the
        // named-pipe approval flow above — see Sifra.Desktop's
        // ExtensionPairingServer. This is what makes the extension a
        // real, individually-revocable device (Phase 2) rather than any
        // copy of it having implicit access purely because Chrome's
        // native-messaging registry entry exists.
        var deviceId = root.GetProperty("deviceId").GetString()!;
        var deviceSecret = root.GetProperty("deviceSecret").GetString()!;
        deviceIdentityService.VerifyDeviceAccess(deviceId, deviceSecret);

        var vaultCredential = root.GetProperty("vaultCredential").GetString()!;

        // "verify" only checks the password is correct — it never returns
        // any credential data. DeriveKey throws VaultDecryptionFailedException
        // if the password can't unwrap the vault's master key, which is
        // exactly what "wrong password" means here. Added because the
        // extension's own UNLOCK previously cached whatever was typed
        // without ever checking it against the real vault.
        if (action == "verify")
        {
            try
            {
                encryptionService.DeriveKey(vaultCredential);
            }
            catch (Sifra.Vault.Crypto.VaultDecryptionFailedException)
            {
                // 5 wrong passwords through this device auto-revokes it — a
                // leaked device secret alone must not be enough for
                // unlimited offline password guesses. Report it as the same
                // DeviceRevokedException the extension already treats as
                // "needs pairing again" (see DEVICE_INVALID_ERRORS in
                // background.js) rather than a plain wrong-password error.
                var wasRevoked = deviceIdentityService.RecordFailedPasswordAttempt(deviceId);
                if (wasRevoked)
                {
                    return JsonSerializer.Serialize(new
                    {
                        ok = false,
                        error = "DeviceRevokedException",
                        message = "Too many failed password attempts — this browser has been unpaired. Pair again.",
                    });
                }
                throw;
            }

            deviceIdentityService.ResetFailedPasswordAttempts(deviceId);
            return JsonSerializer.Serialize(new { ok = true });
        }

        // "url" is only fetched inside the actions that actually need one —
        // "update" targets an existing credential by id and never sends a
        // url at all (see background.js's UPDATE_CAPTURED_LOGIN), so
        // fetching it unconditionally here used to throw before "update"
        // ever got a chance to run.
        switch (action)
        {
            case "discover":
                var discoverUrl = root.GetProperty("url").GetString()!;
                var offered = autofillService.OfferCredentialsForUrl(vaultCredential, discoverUrl);
                var payload = offered.Select(c => new { id = c.Id, label = c.Label, username = LoginValue(c) });
                return JsonSerializer.Serialize(new { ok = true, credentials = payload });

            case "fill":
                var fillUrl = root.GetProperty("url").GetString()!;
                var credentialId = root.GetProperty("credentialId").GetString()!;
                var consent = root.GetProperty("consent").GetBoolean();
                var view = autofillService.FillCredential(vaultCredential, credentialId, fillUrl, consent);
                return JsonSerializer.Serialize(new { ok = true, username = LoginValue(view), password = PasswordValue(view) });

            // Save-password-prompt flow: the extension captures a submitted
            // login form, then (after the resulting page loads) asks here
            // whether it's new, matches what's already saved, or updates an
            // existing entry's password — see CredentialAutofillService's
            // own remarks on why this comparison happens server-side rather
            // than handing the stored password to the extension.
            case "check":
                var checkUrl = root.GetProperty("url").GetString()!;
                var capturedUsername = root.GetProperty("username").GetString()!;
                var capturedPassword = root.GetProperty("password").GetString()!;
                var check = autofillService.CheckCapturedLogin(vaultCredential, checkUrl, capturedUsername, capturedPassword);
                return JsonSerializer.Serialize(new
                {
                    ok = true,
                    status = check.Status.ToString(),
                    credentialId = check.CredentialId,
                    existingLabel = check.ExistingLabel,
                });

            case "save":
                var saveUrl = root.GetProperty("url").GetString()!;
                var saveLabel = root.GetProperty("label").GetString()!;
                var saveUsername = root.GetProperty("username").GetString()!;
                var savePassword = root.GetProperty("password").GetString()!;
                var savedId = autofillService.SaveCapturedLogin(vaultCredential, saveLabel, saveUrl, saveUsername, savePassword);
                return JsonSerializer.Serialize(new { ok = true, credentialId = savedId });

            case "update":
                var updateCredentialId = root.GetProperty("credentialId").GetString()!;
                var updatePassword = root.GetProperty("password").GetString()!;
                autofillService.UpdateCapturedLoginPassword(vaultCredential, updateCredentialId, updatePassword);
                return JsonSerializer.Serialize(new { ok = true });

            default:
                return JsonSerializer.Serialize(new { ok = false, error = "UnknownAction", message = $"Unknown action '{action}'." });
        }
    }
    catch (Exception ex)
    {
        // Never crash the host on a bad/failed request — report the error
        // back to the extension instead. The exception type name lets the
        // extension distinguish "wrong consent" from "wrong domain" from
        // "wrong vault password" from "device not paired/revoked" without
        // parsing message text.
        return JsonSerializer.Serialize(new { ok = false, error = ex.GetType().Name, message = ex.Message });
    }
}

// Connects to Sifra.Desktop's pairing pipe (see ExtensionPairingServer) and
// relays the human's approve/deny decision back to the extension. A short
// connect timeout means "Desktop isn't running" is reported almost
// instantly rather than making the extension wait out the full pairing
// timeout to find out nobody could possibly answer.
static async Task<string> PerformPairingAsync(string browserName)
{
    try
    {
        using var client = new NamedPipeClientStream(".", "SifraExtensionPairing", PipeDirection.InOut, PipeOptions.Asynchronous);
        using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await client.ConnectAsync(connectCts.Token);
        }
        catch (OperationCanceledException)
        {
            return JsonSerializer.Serialize(new { ok = false, error = "DesktopNotRunning", message = "Open Sifra Desktop to approve pairing." });
        }

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var request = JsonSerializer.SerializeToUtf8Bytes(new { browserName });
        await PipeFraming.WriteFrameAsync(client, request, timeoutCts.Token);

        var responseBytes = await PipeFraming.ReadFrameAsync(client, timeoutCts.Token);
        if (responseBytes is null)
        {
            return JsonSerializer.Serialize(new { ok = false, error = "PairingFailed", message = "Sifra Desktop closed the connection." });
        }

        using var responseDoc = JsonDocument.Parse(responseBytes);
        var approved = responseDoc.RootElement.GetProperty("approved").GetBoolean();
        if (!approved)
        {
            return JsonSerializer.Serialize(new { ok = false, error = "PairingDenied", message = "Pairing was declined in Sifra Desktop." });
        }

        var deviceId = responseDoc.RootElement.GetProperty("deviceId").GetString();
        var deviceSecret = responseDoc.RootElement.GetProperty("deviceSecret").GetString();
        return JsonSerializer.Serialize(new { ok = true, deviceId, deviceSecret });
    }
    catch (OperationCanceledException)
    {
        return JsonSerializer.Serialize(new { ok = false, error = "PairingTimedOut", message = "No response from Sifra Desktop." });
    }
}
