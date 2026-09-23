using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Wpf.Ui.Controls;

namespace Sifra.Desktop;

public partial class MainWindow : FluentWindow
{
    private const int WM_NCHITTEST = 0x0084;
    private const int HTCLIENT = 1;
    private const int GWLP_WNDPROC = -4;
    private const int ResizeEdgeThresholdPx = 8;

    private static readonly TimeSpan VisibleSyncInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MinimizedSyncInterval = TimeSpan.FromHours(1);

    private readonly AppServices _services = new();
    private readonly IdleLockMonitor _idleLockMonitor = new();
    private readonly ExtensionPairingServer _pairingServer;
    private readonly TrayIconManager _trayIcon = new();
    // Owned here, not by any per-session screen, and started once for the
    // app's whole lifetime — see SyncScheduler's own remarks on why it
    // deliberately keeps running independent of vault lock state.
    private readonly SyncScheduler _syncScheduler;
    private bool _isUnlocked;
    private bool _resizeLocked;
    private bool _allowRealClose;

    // Raised each time the vault transitions from locked to unlocked —
    // ExtensionPairingServer waits on this (via WaitForUnlockAsync) before
    // ever showing a pairing-approval prompt, so approving a new device
    // always requires proving the master password first, not just
    // physical access to a locked, unattended window.
    public event EventHandler? Unlocked;

    // Tracks whether at least one pairing request is currently waiting on
    // the vault being unlocked, so the Unlock screen can show a notice
    // instead of leaving the user with no idea anything is pending. A
    // count, not a bool, in case more than one request is ever in flight
    // at once — the notice stays up until every one of them has resolved.
    private int _pendingPairingRequests;
    private EventHandler<bool>? _currentUnlockViewPairingHandler;
    public event EventHandler<bool>? PairingRequestStateChanged;
    public bool HasPendingPairingRequest => _pendingPairingRequests > 0;

    // Called by ExtensionPairingServer (from a background thread) right
    // before and after it awaits WaitForUnlockAsync for one request.
    public void NotifyPairingRequestStarted()
    {
        Dispatcher.Invoke(() =>
        {
            _pendingPairingRequests++;
            if (_pendingPairingRequests == 1)
            {
                PairingRequestStateChanged?.Invoke(this, true);
            }
        });
    }

    public void NotifyPairingRequestEnded()
    {
        Dispatcher.Invoke(() =>
        {
            _pendingPairingRequests = Math.Max(0, _pendingPairingRequests - 1);
            if (_pendingPairingRequests == 0)
            {
                PairingRequestStateChanged?.Invoke(this, false);
            }
        });
    }

    // Raised when the user clicks Deny on the Unlock screen's pairing
    // notice (see UnlockView.DenyPairingRequested, wired in ShowUnlock).
    // Deny needs no proof of the master password — refusing access is the
    // safe default, unlike Approve, which is gated behind WaitForUnlockAsync.
    private event EventHandler? PairingDeniedFromLockScreen;

    /// <summary>
    /// Resolves the moment the user clicks Deny on the lock screen's
    /// pairing notice, or is abandoned (its subscription cleaned up) once
    /// cancellationToken fires — letting ExtensionPairingServer race this
    /// against WaitForUnlockAsync without ever needing the vault unlocked
    /// just to say no.
    /// </summary>
    public Task WaitForLockScreenDenyAsync(CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource();
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            PairingDeniedFromLockScreen -= handler;
            tcs.TrySetResult();
        };
        PairingDeniedFromLockScreen += handler;
        cancellationToken.Register(() =>
        {
            PairingDeniedFromLockScreen -= handler;
            tcs.TrySetCanceled(cancellationToken);
        });
        return tcs.Task;
    }
    private IntPtr _originalWndProc;
    // Kept as a field, not a local — native code holds a raw pointer to this
    // delegate, so it must not become eligible for GC while installed.
    private NativeMethods.WndProc? _wndProcDelegate;

    public MainWindow()
    {
        InitializeComponent();

        // Started immediately, before any vault is ever unlocked — a
        // no-op tick (ActiveSyncProvider null) is harmless, and starting
        // this early means a vault configured for sync on a previous run
        // starts syncing again right away rather than waiting for the
        // user to unlock first.
        _syncScheduler = new SyncScheduler(_services, VisibleSyncInterval);
        _syncScheduler.Start();

        // No blanket sizing call here — ShowInitialScreen() immediately calls
        // whichever of SetSetupWindowSize()/SetUnlockWindowSize() applies
        // (via ShowUnlock()), so a call here would just be instantly
        // overridden.
        ShowInitialScreen();

        // Any user input anywhere in the window counts as activity — Preview
        // events tunnel from the root down, so this sees mouse/keyboard
        // activity from every child control without each of them wiring it
        // individually.
        PreviewMouseMove += (_, _) => _idleLockMonitor.NotifyActivity();
        PreviewMouseDown += (_, _) => _idleLockMonitor.NotifyActivity();
        PreviewKeyDown += (_, _) => _idleLockMonitor.NotifyActivity();
        _idleLockMonitor.TimedOut += (_, _) => ShowUnlock();

        // Runs for the app's whole lifetime — enrolling a device itself
        // only touches device-registry metadata, not encrypted data, but
        // ExtensionPairingServer still waits for WaitForUnlockAsync before
        // showing the approval prompt (see that class and Unlocked above)
        // so approving requires proving the master password.
        _pairingServer = new ExtensionPairingServer(_services, this);
        Closed += (_, _) =>
        {
            _pairingServer.Dispose();
            _trayIcon.Dispose(); // idempotent — also disposed explicitly by ExitApplication, covers the SessionEnding real-shutdown path too
            // ShutdownMode="OnExplicitShutdown" (App.xaml) means this is the
            // one place that actually ends the process — Closed only ever
            // fires on a genuine exit (tray Exit, CloseToTray-off X button,
            // or a real OS shutdown/sign-off), never on a hide-to-tray,
            // which cancels the Closing event instead of letting it reach here.
            Application.Current.Shutdown();
        };

        _trayIcon.OpenRequested += (_, _) => RestoreFromTray();
        _trayIcon.LockRequested += (_, _) =>
        {
            if (_isUnlocked)
            {
                ShowUnlock();
            }
        };
        _trayIcon.ExitRequested += (_, _) => ExitApplication();

        // A real OS sign-off/shutdown must never be blocked by the
        // close-to-tray override below — Windows expects apps to actually
        // exit when it says so.
        Application.Current.SessionEnding += (_, _) => _allowRealClose = true;

        Closing += OnWindowClosing;

        // Shown immediately at launch (not just reactively on the first
        // minimize) and kept in sync any time Options changes the setting —
        // see SyncTrayIconVisibility's remarks.
        SyncTrayIconVisibility();
    }

    /// <summary>
    /// Called by App.OnStartup for a silent auto-launch (see
    /// StartupRegistration) — the window is never shown at all. If the
    /// tray icon is disabled, hiding with no tray icon would strand the
    /// app with no way to reach it at all, so this falls back to showing
    /// the window normally instead of respecting --minimized.
    /// </summary>
    public void StartHiddenToTray()
    {
        if (!_services.Settings.Get().ShowTrayIcon)
        {
            Show();
        }
    }

    /// <summary>
    /// Applies AppSettings.ShowTrayIcon's current value to the actual tray
    /// icon — called once at startup and again any time Options saves a
    /// change, so toggling the setting takes effect immediately without
    /// requiring a restart. Tray icon visibility is now independent of
    /// whether the main window is open, minimized, or hidden (see
    /// ShowTrayIcon's own remarks) — RestoreFromTray/MinimizeToTray no
    /// longer touch it at all.
    /// </summary>
    private void SyncTrayIconVisibility()
    {
        if (_services.Settings.Get().ShowTrayIcon)
        {
            _trayIcon.Show();
        }
        else
        {
            _trayIcon.Hide();
        }
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_allowRealClose)
        {
            return; // let it actually close — tray Exit, CloseToTray-off, or a real OS shutdown/sign-off
        }

        if (!_services.Settings.Get().CloseToTray)
        {
            _allowRealClose = true;
            return; // let it actually close this time — no tray to hide to (or the user just doesn't want that behavior)
        }

        e.Cancel = true;
        MinimizeToTray();
    }

    /// <summary>
    /// The X button's actual behavior: hide instead of exit, and lock
    /// immediately (per product decision — an unlocked vault sitting
    /// hidden in the background for hours is a real exposure window if
    /// the machine is left unattended). Harmless to call when already
    /// locked or still on Setup — ShowUnlock() is only invoked when there
    /// is something unlocked to actually lock.
    /// </summary>
    private void MinimizeToTray()
    {
        if (_isUnlocked)
        {
            ShowUnlock();
        }

        Hide();
        // Tray icon visibility no longer changes here — see
        // SyncTrayIconVisibility's remarks; CloseToTray being enabled
        // already implies ShowTrayIcon is too, so it's always showing by
        // the time this runs.
        //
        // Background sync keeps running while minimized (it never touches
        // decrypted vault data) but backs off to a much slower cadence —
        // no one's actively watching the sync status while the window is
        // hidden, so there's no reason to keep hitting the cloud provider
        // every 5 minutes.
        _syncScheduler.SetInterval(MinimizedSyncInterval);
    }

    /// <summary>
    /// Always lands on the Unlock screen — either because MinimizeToTray
    /// already forced it there, or (belt-and-suspenders, per explicit
    /// product decision) because this locks again regardless of whatever
    /// state the window is actually in when restored. Tray icon visibility
    /// is independent of this now (see SyncTrayIconVisibility's remarks) —
    /// restoring the window never hides it.
    /// </summary>
    private void RestoreFromTray()
    {
        if (_isUnlocked)
        {
            ShowUnlock();
        }

        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        Activate();
        _syncScheduler.SetInterval(VisibleSyncInterval);
    }

    private void ExitApplication()
    {
        _allowRealClose = true;
        Close(); // triggers the Closed handler above, which disposes _trayIcon and shuts down
    }

    /// <summary>
    /// Resolves once the vault is next unlocked — immediately if it
    /// already is. Brings the window to the foreground so a locked,
    /// unattended app doesn't leave a pairing request silently waiting
    /// behind other windows.
    /// </summary>
    public Task WaitForUnlockAsync()
    {
        // Called from ExtensionPairingServer's background pipe-listener
        // thread — every operation here (reading _isUnlocked, touching the
        // Unlocked event, and especially Activate(), which is a real WPF
        // Window method with UI-thread affinity) must run on the
        // dispatcher thread instead, or WPF throws "the calling thread
        // cannot access this object because a different thread owns it."
        return Dispatcher.Invoke(() =>
        {
            if (_isUnlocked)
            {
                BringToForeground();
                return Task.CompletedTask;
            }

            var tcs = new TaskCompletionSource();
            EventHandler? handler = null;
            handler = (_, _) =>
            {
                Unlocked -= handler;
                tcs.SetResult();
            };
            Unlocked += handler;

            BringToForeground();
            return tcs.Task;
        });
    }

    // Activate() alone does not restore a minimized window in WPF — it can
    // activate/focus the window while it stays minimized, so a pairing
    // request arriving while the app is minimized would silently fail to
    // ever become visible. WindowState must be reset first.
    private void BringToForeground()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // HwndSource.AddHook never actually saw WM_NCHITTEST for this window
        // (confirmed empirically: an unconditional log write in that hook
        // never fired even once) — whatever FluentWindow does internally for
        // its custom title bar bypasses that hook chain. Replacing the raw
        // window procedure via SetWindowLongPtr is the one mechanism that
        // cannot be bypassed: every message reaches us first, unconditionally,
        // before it reaches FluentWindow's own procedure (which we still call
        // via CallWindowProc for everything we don't explicitly override).
        var hwnd = new WindowInteropHelper(this).Handle;
        _wndProcDelegate = WndProc;
        _originalWndProc = NativeMethods.SetWindowLongPtr(hwnd, GWLP_WNDPROC,
            Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        if (_resizeLocked && msg == WM_NCHITTEST)
        {
            var x = unchecked((short)(lParam.ToInt64() & 0xFFFF));
            var y = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));
            NativeMethods.GetWindowRect(hwnd, out var rect);

            var nearAnyEdge = x - rect.Left <= ResizeEdgeThresholdPx
                || rect.Right - x <= ResizeEdgeThresholdPx
                || y - rect.Top <= ResizeEdgeThresholdPx
                || rect.Bottom - y <= ResizeEdgeThresholdPx;

            if (nearAnyEdge)
            {
                return new IntPtr(HTCLIENT);
            }
        }

        return NativeMethods.CallWindowProc(_originalWndProc, hwnd, msg, wParam, lParam);
    }

    private static class NativeMethods
    {
        public delegate IntPtr WndProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", CharSet = CharSet.Auto)]
        public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr newProc);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }

    private void ShowInitialScreen()
    {
        if (_services.VaultStore.Exists())
        {
            ShowUnlock();
            return;
        }

        SetSetupWindowSize();
        var setupView = new SetupView(_services);
        setupView.SetupComplete += (_, _) => ShowUnlock();
        RootGrid.Children.Add(setupView);
    }

    private void ShowUnlock()
    {
        // Stops the idle timer for both paths that reach here: an explicit
        // Lock click and an idle timeout that already fired once — either
        // way, no vault is unlocked any more, so nothing should keep ticking
        // toward a second auto-lock.
        _idleLockMonitor.Stop();
        _isUnlocked = false;
        SetUnlockWindowSize();

        // Every path that reaches ShowUnlock (explicit Lock, idle timeout,
        // LockRequested) must detach VaultView's sync-status subscription —
        // otherwise it keeps trying to update a UI that's about to be torn
        // down. The shared SyncScheduler itself is never stopped here: it's
        // owned by this window and keeps running independent of lock state
        // (see SyncScheduler's own remarks).
        if (RootGrid.Children.Count > 0 && RootGrid.Children[0] is ShellView activeShell)
        {
            activeShell.DetachFromSyncScheduler();
        }

        RootGrid.Children.Clear();
        var unlockView = new UnlockView(_services);

        // Exactly one of these is ever wired at a time — unsubscribing the
        // previous one here (rather than only where the view gets replaced)
        // means every path that can call ShowUnlock again (idle timeout,
        // explicit Lock, LockRequested) can't accumulate stale handlers
        // pointing at a discarded UnlockView instance.
        if (_currentUnlockViewPairingHandler is not null)
        {
            PairingRequestStateChanged -= _currentUnlockViewPairingHandler;
        }
        unlockView.SetPairingNoticeVisible(HasPendingPairingRequest);
        _currentUnlockViewPairingHandler = (_, hasPending) => unlockView.SetPairingNoticeVisible(hasPending);
        PairingRequestStateChanged += _currentUnlockViewPairingHandler;
        unlockView.DenyPairingRequested += (_, _) => PairingDeniedFromLockScreen?.Invoke(this, EventArgs.Empty);

        unlockView.Unlocked += (_, vaultCredential) =>
        {
            _isUnlocked = true;
            PairingRequestStateChanged -= _currentUnlockViewPairingHandler;
            _currentUnlockViewPairingHandler = null;
            RootGrid.Children.Clear();
            SetShellWindowSize();
            var shellView = new ShellView(_services, vaultCredential, _syncScheduler);
            shellView.LockRequested += (_, _) => ShowUnlock();
            shellView.SettingsChanged += (_, _) =>
            {
                StartIdleLockMonitor();
                SyncTrayIconVisibility();
            };
            RootGrid.Children.Add(shellView);
            StartIdleLockMonitor();
            Unlocked?.Invoke(this, EventArgs.Empty);
        };
        RootGrid.Children.Add(unlockView);
    }

    private void StartIdleLockMonitor()
    {
        var minutes = _services.Settings.Get().AutoLockMinutes;
        _idleLockMonitor.Start(minutes is > 0 ? TimeSpan.FromMinutes(minutes.Value) : null);
    }

    // Setup and Unlock each get their own sizing method (below) so one can be
    // tuned (e.g. a different height) without silently resizing the other —
    // they used to share a single SetCompactWindowSize(), which meant any
    // height change to one screen was a height change to both, since it's
    // the same live window just swapping which UserControl sits in
    // RootGrid. Both still share this helper for the chrome settings that
    // really are identical between them.
    //
    // Setup/Unlock's card fills the window edge-to-edge (Stretch alignment).
    // SizeToContent cannot size a Stretch element correctly — it measures with
    // an infinite available size, so Stretch falls back to the content's
    // natural size for that measurement, and then MinWidth/MinHeight can clamp
    // the window larger than what the child actually arranges to, leaving a
    // gap. A fixed size gives Stretch a real, finite target to fill from the
    // first layout pass, so the card always matches the window exactly.
    private void ApplyCompactWindowChrome(double width, double height)
    {
        MinWidth = width;
        MinHeight = height;
        Width = width;
        Height = height;
        SizeToContent = SizeToContent.Manual;
        // Setup/Unlock's card fills the window edge-to-edge, so blend the title
        // bar into it by matching its background instead of leaving the
        // default (gray) title bar color.
        AppTitleBar.Background = (System.Windows.Media.Brush)FindResource("Sifra.CardBrush");
        // A resizable window reserves a few pixels of invisible resize-border
        // on its non-anchored edges, which showed up as a visible gap on the
        // right/bottom of the edge-to-edge card. Setup/Unlock isn't meant to
        // be resized anyway (SizeToContent already fits it exactly).
        ResizeMode = ResizeMode.NoResize;
        AppTitleBar.ShowMaximize = false;
        _resizeLocked = true;
        // Mica's DWM compositing reserves a subtle inset margin around the
        // window edges, which showed up as the same right/bottom gap even
        // after fixing ResizeMode — None renders flush, matching the
        // Add/Edit dialog which never had this backdrop in the first place.
        WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.None;
        CenterOnScreen();
    }

    private void SetSetupWindowSize()
    {
        // 480, not 420: the Setup recovery-key panel needs ~396px of inner
        // content width to fit its 39-character key on one line without
        // wrapping (39 monospace chars + textbox padding), plus 64px of card
        // padding (32 each side) — and a further ~20px of slack beyond that
        // exact sum, or rounded-corner/glyph anti-aliasing bleeds past the
        // content's own boundary and gets clipped (confirmed: happened at
        // exactly-396-fits-396 with zero slack; the narrower intro panel,
        // with 40px of slack per side, never showed it).
        ApplyCompactWindowChrome(480, 560);
    }

    private void SetUnlockWindowSize()
    {
        // 100px narrower and 100px shorter than Setup's 480x560 (four rounds
        // of reduction, per explicit request) — re-verify UnlockView's
        // centering Margin (tuned via UI Automation measurement, see
        // UnlockView.xaml) any time the height changes again, rather than
        // assuming it still holds. Confirmed still exact (equal top/bottom
        // gaps) at 540, 500, and 460. Width has 380 - 64 (card padding) =
        // 316px available; UnlockView's content StackPanel is 276px wide,
        // comfortably inside that, so no clipping risk there.
        ApplyCompactWindowChrome(380, 460);
    }

    private void SetShellWindowSize()
    {
        SizeToContent = SizeToContent.Manual;
        MinWidth = 820;
        MinHeight = 520;
        Width = 1120;
        Height = 680;
        CenterOnScreen();
        // Shell's panes sit on the window canvas with visible margins, not an
        // edge-to-edge card, so the title bar goes back to its own default color
        // instead of blending into a card that no longer spans the whole window.
        AppTitleBar.ClearValue(BackgroundProperty);
        ResizeMode = ResizeMode.CanResize;
        AppTitleBar.ShowMaximize = true;
        _resizeLocked = false;
        WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica;
    }

    private void CenterOnScreen()
    {
        // Width/Height (not ActualWidth/ActualHeight) — this can run before the
        // first layout pass (e.g. from the constructor, before Show()), when
        // ActualWidth/ActualHeight are still 0.
        Left = (SystemParameters.WorkArea.Width - Width) / 2;
        Top = (SystemParameters.WorkArea.Height - Height) / 2;
    }
}
