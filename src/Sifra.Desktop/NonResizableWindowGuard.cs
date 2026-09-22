using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Sifra.Desktop;

/// <summary>
/// Forces a FluentWindow to actually ignore edge-drag resize attempts.
///
/// ResizeMode="NoResize" alone does not work on a FluentWindow with
/// ExtendsContentIntoTitleBar="True": its custom chrome answers
/// WM_NCHITTEST with resize hit-test codes near the window edges
/// regardless of ResizeMode, and — confirmed empirically while chasing
/// this same bug for MainWindow's Setup/Unlock sizing —
/// HwndSource.AddHook never even sees WM_NCHITTEST for such a window, so
/// the usual WPF hook mechanism can't intercept it either. Replacing the
/// window procedure via SetWindowLongPtr is the one mechanism that
/// cannot be bypassed: every message reaches this hook first, before
/// FluentWindow's own procedure (still invoked via CallWindowProc for
/// everything else).
/// </summary>
internal static class NonResizableWindowGuard
{
    private const int WM_NCHITTEST = 0x0084;
    private const int HTCLIENT = 1;
    private const int GWLP_WNDPROC = -4;
    private const int ResizeEdgeThresholdPx = 8;

    /// <summary>Call once, any time before or after the window is shown — installs the hook on SourceInitialized (or immediately if the handle already exists).</summary>
    public static void Apply(Window window)
    {
        window.SourceInitialized += (_, _) => Install(window);
    }

    private static void Install(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        NativeMethods.WndProc wndProcDelegate = (h, msg, wParam, lParam) => WndProc(h, msg, wParam, lParam, window);
        // Keep the delegate alive for the window's lifetime — otherwise the GC
        // can collect it while native code still holds the function pointer.
        window.Resources["NonResizableWindowGuard.WndProcDelegate"] = wndProcDelegate;
        var originalWndProc = NativeMethods.SetWindowLongPtr(hwnd, GWLP_WNDPROC,
            Marshal.GetFunctionPointerForDelegate(wndProcDelegate));
        window.Resources["NonResizableWindowGuard.OriginalWndProc"] = originalWndProc;
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, Window window)
    {
        var originalWndProc = (IntPtr)window.Resources["NonResizableWindowGuard.OriginalWndProc"];

        if (msg == WM_NCHITTEST)
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

        return NativeMethods.CallWindowProc(originalWndProc, hwnd, msg, wParam, lParam);
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
}
