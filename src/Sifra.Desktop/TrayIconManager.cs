using System.Windows.Forms;

namespace Sifra.Desktop;

/// <summary>
/// Wraps System.Windows.Forms.NotifyIcon — WPF has no native tray-icon
/// control (confirmed against WPF-UI 4.3.0 via reflection: no NotifyIcon
/// or Tray type exists in that package), so WinForms interop
/// (UseWindowsForms in the csproj) is the standard, well-trodden way to
/// add one to a WPF app. This class owns nothing about lock state or
/// window visibility itself — MainWindow decides when to Show()/Hide()
/// this and what OpenRequested/LockRequested/ExitRequested should do.
/// </summary>
public sealed class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;

    public event EventHandler? OpenRequested;
    public event EventHandler? LockRequested;
    public event EventHandler? ExitRequested;

    public TrayIconManager()
    {
        // A dedicated tray-specific icon file (SifraTrayIcon.ico), separate
        // from Sifra.ico (the exe/window icon) — the brand package supplies
        // both because a tray icon is rendered much smaller (16-32px) and
        // benefits from its own simplified artwork rather than a downscaled
        // copy of the full app icon.
        var iconUri = new Uri("pack://application:,,,/Assets/SifraTrayIcon.ico");
        var iconStream = System.Windows.Application.GetResourceStream(iconUri)?.Stream
            ?? throw new InvalidOperationException("Could not load Assets/SifraTrayIcon.ico as a resource stream.");
        using (iconStream)
        {
            _notifyIcon = new NotifyIcon
            {
                Icon = new System.Drawing.Icon(iconStream),
                Text = "Sifra",
                Visible = false,
            };
        }

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Sifra", null, (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("Lock", null, (_, _) => LockRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));
        _notifyIcon.ContextMenuStrip = menu;

        // MouseClick (not Click) so this only fires for a real mouse click,
        // and filtered to Left button so a right-click just opens the
        // context menu above without also triggering Open underneath it.
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                OpenRequested?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    public void Show() => _notifyIcon.Visible = true;

    public void Hide() => _notifyIcon.Visible = false;

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
