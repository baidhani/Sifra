using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using Sifra.Vault.Pairing;

namespace Sifra.Desktop;

/// <summary>
/// Hosts the named pipe a browser extension's pairing request arrives on
/// (see Sifra.NativeHost's "pair" action, which connects to this as a
/// client). Runs for the app's whole lifetime. Enrolling a device itself
/// only writes device-registry metadata (DeviceIdentityService.EnrollDevice)
/// and never touches an encrypted credential, but the approval prompt still
/// waits for the vault to be unlocked (via MainWindow.WaitForUnlockAsync)
/// before showing — otherwise anyone with brief physical access to a
/// locked, unattended window could approve a rogue device without ever
/// proving they know the master password.
/// </summary>
public sealed class ExtensionPairingServer : IDisposable
{
    private const string PipeName = "SifraExtensionPairing";

    private readonly AppServices _services;
    private readonly MainWindow _mainWindow;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _listenLoop;

    public ExtensionPairingServer(AppServices services, MainWindow mainWindow)
    {
        _services = services;
        _mainWindow = mainWindow;
        _listenLoop = Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    // A pending pairing request can legitimately take a long time to
    // resolve — WaitForUnlockAsync blocks until a human unlocks the vault,
    // which might never happen (they ignore it, close the extension, walk
    // away). This bounds how long any single request is allowed to sit
    // half-open, matching the native host's own ~90s client-side wait so
    // the two sides give up around the same time rather than the server
    // holding a request the client already abandoned.
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(90);

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                // MaxAllowedServerInstances (not 1) — accepting a connection
                // and immediately listening for the next one, below, only
                // actually helps if a second client CAN connect while the
                // first is still being handled. A single stuck/abandoned
                // request (e.g. nobody ever unlocks the vault to approve
                // it) must never be able to block every later pairing
                // attempt until the app restarts, which is exactly what
                // maxNumberOfServerInstances=1 allowed to happen.
                pipe = new NamedPipeServerStream(
                    PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await pipe.WaitForConnectionAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break; // shutting down — expected
            }

            // Handled independently (fire-and-forget) so this loop can go
            // straight back to accepting the next connection rather than
            // waiting on this one's full request/response/approval cycle.
            _ = HandleConnectionAsync(pipe, cancellationToken);
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        using (pipe)
        using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeoutCts.CancelAfter(RequestTimeout);
            try
            {
                var requestBytes = await PipeFraming.ReadFrameAsync(pipe, timeoutCts.Token);
                if (requestBytes is null)
                {
                    return; // client disconnected before sending a full request
                }

                var browserName = ParseBrowserName(requestBytes);
                var responseBytes = await HandlePairingRequestOnUiThreadAsync(browserName, timeoutCts.Token);
                await PipeFraming.WriteFrameAsync(pipe, responseBytes, timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                // Either the app is shutting down, or this specific request
                // timed out waiting for a human — either way, just drop it.
            }
            catch (IOException)
            {
                // The client (native host) disconnected mid-handshake — not fatal.
            }
        }
    }

    private static string ParseBrowserName(byte[] requestBytes)
    {
        try
        {
            using var doc = JsonDocument.Parse(requestBytes);
            return doc.RootElement.TryGetProperty("browserName", out var name) ? name.GetString() ?? "Browser extension" : "Browser extension";
        }
        catch (JsonException)
        {
            return "Browser extension";
        }
    }

    // The pipe read happens on a background thread, but showing a window
    // and calling EnrollDevice (which touches the same on-disk device
    // registry the Desktop UI's own device list will read) needs to happen
    // on the UI thread — Dispatcher.Invoke bridges the two synchronously.
    private async Task<byte[]> HandlePairingRequestOnUiThreadAsync(string browserName, CancellationToken cancellationToken)
    {
        // Brings the app to the foreground and — while still locked — shows
        // a notice on the Unlock screen itself (see MainWindow's
        // PairingRequestStateChanged / UnlockView's PairingNoticeBorder) so
        // the user isn't left wondering why nothing happened. Cleared in
        // the finally below whether this ends in approval, denial, or a
        // timeout with nobody ever unlocking.
        _mainWindow.NotifyPairingRequestStarted();
        try
        {
            // Races unlocking (needed to Approve — see this class's own
            // remarks on why) against Deny from the lock screen, which
            // needs no proof of the master password at all since refusing
            // access is the safe default. Whichever happens first decides
            // the outcome; the loser's subscription is cleaned up by
            // cancellationToken firing at the latest (see
            // WaitForLockScreenDenyAsync's Register callback).
            var unlockTask = _mainWindow.WaitForUnlockAsync();
            var denyTask = _mainWindow.WaitForLockScreenDenyAsync(cancellationToken);
            var completed = await Task.WhenAny(unlockTask, denyTask).WaitAsync(cancellationToken);

            if (completed == denyTask)
            {
                return JsonSerializer.SerializeToUtf8Bytes(new { approved = false });
            }

            return await ShowApprovalDialogAsync(browserName);
        }
        finally
        {
            _mainWindow.NotifyPairingRequestEnded();
        }
    }

    private Task<byte[]> ShowApprovalDialogAsync(string browserName)
    {
        var tcs = new TaskCompletionSource<byte[]>();

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var window = new PairingRequestWindow(browserName);
            var approved = window.ShowDialog() == true;

            byte[] response;
            if (approved)
            {
                var (deviceId, deviceSecret) = _services.Devices.EnrollDevice(browserName);
                response = JsonSerializer.SerializeToUtf8Bytes(new { approved = true, deviceId, deviceSecret });
            }
            else
            {
                response = JsonSerializer.SerializeToUtf8Bytes(new { approved = false });
            }

            tcs.SetResult(response);
        });

        return tcs.Task;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
