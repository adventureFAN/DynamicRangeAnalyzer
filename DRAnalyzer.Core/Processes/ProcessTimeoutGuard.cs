using System.ComponentModel;
using System.Diagnostics;
using System.Threading;

namespace DRAnalyzer.Core.Processes;

internal sealed class ProcessTimeoutGuard : IDisposable
{
    private readonly Process _process;
    private readonly Timer _timer;
    private int _timedOut;

    public ProcessTimeoutGuard(
        Process process,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout));
        }

        _process = process;

        _timer =
            new Timer(
                OnTimeout,
                state: null,
                dueTime: timeout,
                period: Timeout.InfiniteTimeSpan);
    }

    public bool TimedOut =>
        Volatile.Read(ref _timedOut) != 0;

    private void OnTimeout(object? state)
    {
        try
        {
            if (_process.HasExited)
                return;

            // Vor Kill markieren, damit der wartende Thread den Timeout
            // auch dann sicher erkennt, wenn der Prozess sehr schnell endet.
            Interlocked.Exchange(
                ref _timedOut,
                1);

            try
            {
                _process.Kill(
                    entireProcessTree: true);
            }
            catch (NotSupportedException)
            {
                KillProcessOnlyFallback();
            }
            catch (Win32Exception)
            {
                KillProcessOnlyFallback();
            }
        }
        catch (ObjectDisposedException)
        {
            // Der Guard/Prozess wurde gleichzeitig beendet.
            Interlocked.Exchange(
                ref _timedOut,
                0);
        }
        catch (InvalidOperationException)
        {
            // Der Prozess ist zwischen HasExited und Kill regulär beendet.
            Interlocked.Exchange(
                ref _timedOut,
                0);
        }
        catch
        {
            // Ein Watchdog darf selbst niemals einen unbehandelten
            // ThreadPool-Fehler verursachen. Falls Kill unerwartet scheitert,
            // bleibt der normale Prozesspfad zuständig.
            Interlocked.Exchange(
                ref _timedOut,
                0);
        }
    }

    private void KillProcessOnlyFallback()
    {
        try
        {
            if (_process.HasExited)
            {
                Interlocked.Exchange(
                    ref _timedOut,
                    0);
                return;
            }

            _process.Kill();
        }
        catch (ObjectDisposedException)
        {
            Interlocked.Exchange(
                ref _timedOut,
                0);
        }
        catch (InvalidOperationException)
        {
            Interlocked.Exchange(
                ref _timedOut,
                0);
        }
        catch (Win32Exception)
        {
            Interlocked.Exchange(
                ref _timedOut,
                0);
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
    }
}
