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

    private readonly AppServices _services = new();
    private bool _resizeLocked;
    private IntPtr _originalWndProc;
    // Kept as a field, not a local — native code holds a raw pointer to this
    // delegate, so it must not become eligible for GC while installed.
    private NativeMethods.WndProc? _wndProcDelegate;

    public MainWindow()
    {
        InitializeComponent();
        SetCompactWindowSize();
        ShowInitialScreen();
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

        var setupView = new SetupView(_services);
        setupView.SetupComplete += (_, _) => ShowUnlock();
        RootGrid.Children.Add(setupView);
    }

    private void ShowUnlock()
    {
        SetCompactWindowSize();
        RootGrid.Children.Clear();
        var unlockView = new UnlockView(_services);
        unlockView.Unlocked += (_, vaultCredential) =>
        {
            RootGrid.Children.Clear();
            SetShellWindowSize();
            var shellView = new ShellView(_services, vaultCredential);
            shellView.LockRequested += (_, _) => ShowUnlock();
            RootGrid.Children.Add(shellView);
        };
        RootGrid.Children.Add(unlockView);
    }

    // Setup/Unlock's card fills the window edge-to-edge (Stretch alignment).
    // SizeToContent cannot size a Stretch element correctly — it measures with
    // an infinite available size, so Stretch falls back to the content's
    // natural size for that measurement, and then MinWidth/MinHeight can clamp
    // the window larger than what the child actually arranges to, leaving a
    // gap. A fixed size gives Stretch a real, finite target to fill from the
    // first layout pass, so the card always matches the window exactly.
    private void SetCompactWindowSize()
    {
        MinWidth = 420;
        MinHeight = 560;
        Width = 420;
        Height = 560;
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
