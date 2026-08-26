using System.Diagnostics;

namespace ZSnaper.Helpers;

internal sealed class UiAnimationTimer : IDisposable
{
    private const int FrameIntervalMs = 8;

    private readonly Control _owner;
    private readonly Action<double> _tick;
    private readonly System.Threading.Timer _timer;
    private long _lastTimestamp;
    private int _framePending;
    private int _running;
    private int _disposed;

    public bool Enabled => Volatile.Read(ref _running) != 0;

    public UiAnimationTimer(Control owner, Action<double> tick)
    {
        _owner = owner;
        _tick = tick;
        _timer = new System.Threading.Timer(QueueFrame, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        if (Volatile.Read(ref _disposed) != 0 || Interlocked.Exchange(ref _running, 1) != 0)
        {
            return;
        }

        _lastTimestamp = Stopwatch.GetTimestamp();
        _timer.Change(0, FrameIntervalMs);
    }

    public void Stop()
    {
        Interlocked.Exchange(ref _running, 0);
        if (Volatile.Read(ref _disposed) == 0)
        {
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    private void QueueFrame(object? state)
    {
        if (Volatile.Read(ref _running) == 0 ||
            Volatile.Read(ref _disposed) != 0 ||
            !_owner.IsHandleCreated ||
            _owner.IsDisposed ||
            Interlocked.Exchange(ref _framePending, 1) != 0)
        {
            return;
        }

        try
        {
            _owner.BeginInvoke((MethodInvoker)DispatchFrame);
        }
        catch (ObjectDisposedException)
        {
            Interlocked.Exchange(ref _framePending, 0);
        }
        catch (InvalidOperationException)
        {
            Interlocked.Exchange(ref _framePending, 0);
        }
    }

    private void DispatchFrame()
    {
        Interlocked.Exchange(ref _framePending, 0);
        if (Volatile.Read(ref _running) == 0 || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        long now = Stopwatch.GetTimestamp();
        double elapsed = (now - _lastTimestamp) / (double)Stopwatch.Frequency;
        _lastTimestamp = now;

        // A stalled UI thread must not turn into one huge physics step.
        _tick(Math.Clamp(elapsed, 0.001, 0.032));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Interlocked.Exchange(ref _running, 0);
        _timer.Dispose();
    }
}
