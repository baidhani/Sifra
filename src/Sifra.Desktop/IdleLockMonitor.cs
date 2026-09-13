using System.Windows.Threading;

namespace Sifra.Desktop;

/// <summary>
/// Ticks once a second and fires TimedOut once no activity has been
/// reported for the configured timeout. A null timeout means "never" —
/// the timer stays stopped and no lock happens automatically. Owned by
/// MainWindow, started only while the vault is unlocked (Shell is shown)
/// and stopped on manual lock or when the vault re-locks for any reason,
/// so it never fires against the Setup/Unlock screens.
/// </summary>
public sealed class IdleLockMonitor
{
    private readonly DispatcherTimer _timer;
    private DateTime _lastActivityUtc;
    private TimeSpan? _timeout;

    public event EventHandler? TimedOut;

    public IdleLockMonitor()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => CheckIdle();
    }

    public void Start(TimeSpan? timeout)
    {
        _timeout = timeout;
        _lastActivityUtc = DateTime.UtcNow;

        if (timeout is not null)
        {
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }

    public void Stop() => _timer.Stop();

    public void NotifyActivity() => _lastActivityUtc = DateTime.UtcNow;

    private void CheckIdle()
    {
        if (_timeout is null)
        {
            return;
        }

        if (DateTime.UtcNow - _lastActivityUtc >= _timeout.Value)
        {
            _timer.Stop();
            TimedOut?.Invoke(this, EventArgs.Empty);
        }
    }
}
