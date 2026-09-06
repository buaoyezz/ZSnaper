using System.Diagnostics;
using System.IO.Pipes;
using ZSnaper.Interop;

namespace ZSnaper.Services;

internal sealed class SingleInstanceCoordinator : NativeWindow, IDisposable
{
    private const int WM_APP_ACTIVATE = 0x8010;
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _listenerTask;
    private bool _ownsMutex;
    private bool _disposed;

    public bool IsPrimary => _ownsMutex;

    public event Action? ActivationRequested;

    public SingleInstanceCoordinator(string? instanceScope = null)
    {
        int sessionId = Process.GetCurrentProcess().SessionId;
        string instanceName = instanceScope ?? $"ZSnaper.SingleInstance.{sessionId}";
        _pipeName = instanceName + ".Activation";
        _mutex = new Mutex(initiallyOwned: true, $"Local\\{instanceName}", out bool createdNew);
        _ownsMutex = createdNew;
        if (_ownsMutex)
        {
            CreateHandle(new CreateParams { Caption = "ZSnaper.SingleInstance" });
        }
    }

    public void StartListening()
    {
        if (!_ownsMutex || _listenerTask is not null)
        {
            return;
        }

        _listenerTask = Task.Run(ListenAsync);
    }

    public bool NotifyPrimaryInstance()
    {
        if (_ownsMutex)
        {
            return false;
        }

        for (int attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                using var client = new NamedPipeClientStream(
                    ".",
                    _pipeName,
                    PipeDirection.Out,
                    PipeOptions.None);
                client.Connect(300);
                client.WriteByte(1);
                client.Flush();
                return true;
            }
            catch (TimeoutException)
            {
                Thread.Sleep(80);
            }
            catch (IOException)
            {
                Thread.Sleep(80);
            }
        }

        return false;
    }

    public bool TryBecomePrimary()
    {
        if (_ownsMutex)
        {
            return true;
        }

        try
        {
            if (!_mutex.WaitOne(0))
            {
                return false;
            }
        }
        catch (AbandonedMutexException)
        {
            // The previous instance crashed after owning the mutex; this process now owns it.
        }

        _ownsMutex = true;
        CreateHandle(new CreateParams { Caption = "ZSnaper.SingleInstance" });
        return true;
    }

    private async Task ListenAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(_cancellation.Token).ConfigureAwait(false);

                byte[] signal = new byte[1];
                int read = await server.ReadAsync(signal, _cancellation.Token).ConfigureAwait(false);
                if (read > 0 && Handle != nint.Zero)
                {
                    NativeMethods.PostMessage(Handle, WM_APP_ACTIVATE, 0, 0);
                }
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                AppDiagnostics.LogException("SingleInstanceCoordinator.Listen", exception);
                if (!_cancellation.IsCancellationRequested)
                {
                    await Task.Delay(150, _cancellation.Token).ConfigureAwait(false);
                }
            }
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_APP_ACTIVATE)
        {
            ActivationRequested?.Invoke();
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancellation.Cancel();
        try
        {
            _listenerTask?.Wait(1_000);
        }
        catch (AggregateException exception) when (
            exception.InnerExceptions.All(inner => inner is OperationCanceledException))
        {
            // Expected when the pipe listener is cancelled during shutdown.
        }
        if (Handle != nint.Zero)
        {
            DestroyHandle();
        }

        if (_ownsMutex)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The mutex was already released during process shutdown.
            }
        }

        _ownsMutex = false;
        _mutex.Dispose();
        _cancellation.Dispose();
        GC.SuppressFinalize(this);
    }
}
