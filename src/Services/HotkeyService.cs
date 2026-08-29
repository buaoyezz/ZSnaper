using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SharpHook;
using SharpHook.Data;
using ZSnaper.Interop;
using ZSnaper.Models;

namespace ZSnaper.Services;

public class HotkeyService : NativeWindow, IDisposable
{
    public const int HOTKEY_CAPTURE = 1;
    public const int HOTKEY_OCR = 2;
    private const int WM_APP_RECORD_GESTURE = 0x8001;
    private const int WM_APP_RECORD_CANCELLED = 0x8002;
    private const int WM_APP_FORCE_TRIGGER = 0x8003;
    private const int WM_APP_RECORD_FEEDBACK = 0x8004;

    public event Action? CaptureTriggered;
    public event Action? OcrTriggered;
    public event Action<HotkeyCommand, HotkeyGesture>? RecordingGestureCaptured;
    public event Action? RecordingCancelled;
    public event Action<string>? RecordingFeedback;

    private bool _captureRegistered;
    private bool _ocrRegistered;
    private int _captureHotkeyId = HOTKEY_CAPTURE;
    private int _ocrHotkeyId = HOTKEY_OCR;
    private HotkeyGesture _captureGesture = new(Keys.Q, Keys.Alt);
    private HotkeyGesture _ocrGesture = new(Keys.X, Keys.Alt);
    private bool _captureForceBinding;
    private bool _ocrForceBinding;
    private long _captureSuppressUntil;
    private long _ocrSuppressUntil;
    private int _nextRegistrationId = 100;

    private SimpleGlobalHook? _keyboardHook;
    private readonly HashSet<KeyCode> _pressedKeys = [];
    private readonly HashSet<KeyCode> _suppressedKeys = [];
    private readonly ConcurrentQueue<(HotkeyCommand Command, HotkeyGesture Gesture)> _pendingGestures = [];
    private readonly ConcurrentQueue<string> _pendingRecordingFeedback = [];
    private HotkeyCommand? _recordingCommand;
    private bool _recordingForceBinding;
    private bool _captureWasRegisteredBeforeRecording;
    private bool _ocrWasRegisteredBeforeRecording;
    private int _captureIdBeforeRecording;
    private int _ocrIdBeforeRecording;

    public bool IsCaptureRegistered => _captureRegistered;
    public bool IsOcrRegistered => _ocrRegistered;
    public bool IsCaptureForceBinding => _captureForceBinding;
    public bool IsOcrForceBinding => _ocrForceBinding;

    public HotkeyService()
    {
        CreateHandle(new CreateParams());
    }

    public HotkeyGesture CaptureGesture => _captureGesture;
    public HotkeyGesture OcrGesture => _ocrGesture;

    public void RegisterConfiguredHotkeys(out bool captureOk, out bool ocrOk)
    {
        _captureForceBinding = ConfigService.Current.CaptureHotkeyForceBinding;
        _ocrForceBinding = ConfigService.Current.OcrHotkeyForceBinding;
        _captureGesture = ParseOrDefault(
            ConfigService.Current.CaptureHotkey,
            new HotkeyGesture(Keys.Q, Keys.Alt),
            _captureForceBinding);
        _ocrGesture = ParseOrDefault(
            ConfigService.Current.OcrHotkey,
            new HotkeyGesture(Keys.X, Keys.Alt),
            _ocrForceBinding);

        if (_ocrGesture == _captureGesture)
        {
            _ocrGesture = new HotkeyGesture(Keys.X, Keys.Alt);
            _ocrForceBinding = false;
        }

        captureOk = ActivateConfiguredHotkey(HotkeyCommand.Capture);
        ocrOk = ActivateConfiguredHotkey(HotkeyCommand.Ocr);
    }

    public HotkeyChangeResult TryUpdateHotkey(
        HotkeyCommand command,
        HotkeyGesture gesture,
        bool forceBinding = false)
    {
        bool gestureIsValid = forceBinding
            ? gesture.IsValidForForceBinding
            : gesture.IsValid;
        if (!gestureIsValid)
        {
            return CreateInvalidGestureResult(gesture, forceBinding);
        }

        HotkeyGesture otherGesture = command == HotkeyCommand.Capture ? _ocrGesture : _captureGesture;
        if (gesture == otherGesture)
        {
            return new HotkeyChangeResult(false, "该组合键已用于另一项功能");
        }

        int id = command == HotkeyCommand.Capture ? _captureHotkeyId : _ocrHotkeyId;
        bool wasRegistered = command == HotkeyCommand.Capture ? _captureRegistered : _ocrRegistered;
        bool previousForceBinding = command == HotkeyCommand.Capture ? _captureForceBinding : _ocrForceBinding;
        HotkeyGesture previous = command == HotkeyCommand.Capture ? _captureGesture : _ocrGesture;

        if (gesture == previous && forceBinding == previousForceBinding)
        {
            return new HotkeyChangeResult(true, $"已更新为 {gesture.DisplayText}");
        }

        if (forceBinding)
        {
            if (!EnsureKeyboardHook(out int hookError))
            {
                return hookError == NativeMethods.ERROR_ACCESS_DENIED
                    ? RequestElevatedRestart(command, gesture)
                    : new HotkeyChangeResult(false, "强力绑定启动失败，请稍后重试");
            }

            if (wasRegistered && !NativeMethods.UnregisterHotKey(Handle, id))
            {
                ReleaseKeyboardHookIfUnused();
                return new HotkeyChangeResult(false, "无法释放原快捷键，旧快捷键仍保持不变");
            }

            SetHotkeyState(command, gesture, registered: false, forceBinding: true, id: id);
            ConfigService.Save();
            return new HotkeyChangeResult(true, $"已启用强力绑定：{gesture.DisplayText}");
        }

        // 先用临时 ID 预留新组合键。新键注册失败时，旧注册完全不动。
        int candidateId = AllocateRegistrationId();
        if (!Register(candidateId, gesture))
        {
            return CreateRegistrationFailureResult(Marshal.GetLastPInvokeError());
        }

        if (wasRegistered && !NativeMethods.UnregisterHotKey(Handle, id))
        {
            NativeMethods.UnregisterHotKey(Handle, candidateId);
            return new HotkeyChangeResult(false, "无法释放原快捷键，旧快捷键仍保持不变");
        }

        SetHotkeyState(command, gesture, registered: true, forceBinding: false, id: candidateId);
        ConfigService.Save();
        return new HotkeyChangeResult(true, $"已更新为 {gesture.DisplayText}");
    }

    public HotkeyChangeResult BeginRecording(HotkeyCommand command, bool forceBinding)
    {
        if (_recordingCommand is not null)
        {
            return new HotkeyChangeResult(false, "已有另一个快捷键正在录制");
        }

        if (!EnsureKeyboardHook(out int hookError))
        {
            return hookError == NativeMethods.ERROR_ACCESS_DENIED
                ? RequestElevatedRestartForRecording(forceBinding)
                : new HotkeyChangeResult(false, "快捷键录制启动失败，请稍后重试");
        }

        _captureWasRegisteredBeforeRecording = _captureRegistered;
        _ocrWasRegisteredBeforeRecording = _ocrRegistered;
        _captureIdBeforeRecording = _captureHotkeyId;
        _ocrIdBeforeRecording = _ocrHotkeyId;
        _pressedKeys.Clear();
        _suppressedKeys.Clear();

        if (!forceBinding && !SuspendRegisteredHotkeys())
        {
            ReleaseKeyboardHookIfUnused();
            return new HotkeyChangeResult(false, "无法暂时保护当前快捷键，请稍后重试");
        }

        _recordingCommand = command;
        _recordingForceBinding = forceBinding;
        return new HotkeyChangeResult(true, string.Empty);
    }

    public HotkeyChangeResult EndRecording()
    {
        if (_recordingCommand is null)
        {
            return new HotkeyChangeResult(true, string.Empty);
        }

        _recordingCommand = null;
        _recordingForceBinding = false;
        List<string> errors = [];

        RestoreSuspendedHotkey(
            HotkeyCommand.Capture,
            _captureWasRegisteredBeforeRecording,
            _captureIdBeforeRecording,
            _captureGesture,
            errors);
        RestoreSuspendedHotkey(
            HotkeyCommand.Ocr,
            _ocrWasRegisteredBeforeRecording,
            _ocrIdBeforeRecording,
            _ocrGesture,
            errors);

        ReleaseKeyboardHookIfUnused();
        return errors.Count == 0
            ? new HotkeyChangeResult(true, string.Empty)
            : new HotkeyChangeResult(false, "新快捷键已保存，但另一个快捷键恢复失败，请重新启动应用");
    }

    private bool Register(int id, HotkeyGesture gesture) => NativeMethods.RegisterHotKey(
        Handle,
        id,
        ToNativeModifiers(gesture.Modifiers) | NativeMethods.MOD_NOREPEAT,
        (uint)(gesture.KeyCode & Keys.KeyCode));

    private static uint ToNativeModifiers(Keys modifiers)
    {
        uint nativeModifiers = NativeMethods.MOD_NONE;
        if (modifiers.HasFlag(Keys.Alt)) nativeModifiers |= NativeMethods.MOD_ALT;
        if (modifiers.HasFlag(Keys.Control)) nativeModifiers |= NativeMethods.MOD_CONTROL;
        if (modifiers.HasFlag(Keys.Shift)) nativeModifiers |= NativeMethods.MOD_SHIFT;
        return nativeModifiers;
    }

    private static HotkeyGesture ParseOrDefault(
        string? value,
        HotkeyGesture fallback,
        bool forceBinding) =>
        HotkeyGesture.TryParse(value, out HotkeyGesture gesture, forceBinding) ? gesture : fallback;

    private bool ActivateConfiguredHotkey(HotkeyCommand command)
    {
        bool forceBinding = command == HotkeyCommand.Capture ? _captureForceBinding : _ocrForceBinding;
        if (forceBinding)
        {
            return EnsureKeyboardHook(out _);
        }

        int id = command == HotkeyCommand.Capture ? HOTKEY_CAPTURE : HOTKEY_OCR;
        HotkeyGesture gesture = command == HotkeyCommand.Capture ? _captureGesture : _ocrGesture;
        bool registered = Register(id, gesture);
        SetHotkeyState(command, gesture, registered, forceBinding: false, id: id);
        return registered;
    }

    private bool SuspendRegisteredHotkeys()
    {
        if (_captureRegistered && !NativeMethods.UnregisterHotKey(Handle, _captureHotkeyId))
        {
            return false;
        }

        if (_captureRegistered)
        {
            _captureRegistered = false;
        }

        if (_ocrRegistered && !NativeMethods.UnregisterHotKey(Handle, _ocrHotkeyId))
        {
            if (_captureWasRegisteredBeforeRecording)
            {
                _captureRegistered = Register(_captureIdBeforeRecording, _captureGesture);
            }

            return false;
        }

        if (_ocrRegistered)
        {
            _ocrRegistered = false;
        }

        return true;
    }

    private void RestoreSuspendedHotkey(
        HotkeyCommand command,
        bool wasRegistered,
        int id,
        HotkeyGesture gesture,
        List<string> errors)
    {
        bool forceBinding = command == HotkeyCommand.Capture ? _captureForceBinding : _ocrForceBinding;
        bool registered = command == HotkeyCommand.Capture ? _captureRegistered : _ocrRegistered;
        if (!wasRegistered || forceBinding || registered)
        {
            return;
        }

        if (Register(id, gesture))
        {
            if (command == HotkeyCommand.Capture)
            {
                _captureHotkeyId = id;
                _captureRegistered = true;
            }
            else
            {
                _ocrHotkeyId = id;
                _ocrRegistered = true;
            }
        }
        else
        {
            errors.Add(command.ToString());
        }
    }

    private void SetHotkeyState(
        HotkeyCommand command,
        HotkeyGesture gesture,
        bool registered,
        bool forceBinding,
        int id)
    {
        if (command == HotkeyCommand.Capture)
        {
            _captureGesture = gesture;
            _captureRegistered = registered;
            _captureForceBinding = forceBinding;
            _captureHotkeyId = id;
            _captureSuppressUntil = Environment.TickCount64 + 750;
            ConfigService.Current.CaptureHotkey = gesture.ConfigText;
            ConfigService.Current.CaptureHotkeyForceBinding = forceBinding;
        }
        else
        {
            _ocrGesture = gesture;
            _ocrRegistered = registered;
            _ocrForceBinding = forceBinding;
            _ocrHotkeyId = id;
            _ocrSuppressUntil = Environment.TickCount64 + 750;
            ConfigService.Current.OcrHotkey = gesture.ConfigText;
            ConfigService.Current.OcrHotkeyForceBinding = forceBinding;
        }

        ReleaseKeyboardHookIfUnused();
    }

    private int AllocateRegistrationId() => _nextRegistrationId++;

    private HotkeyChangeResult CreateRegistrationFailureResult(int errorCode)
    {
        string suffix = errorCode == NativeMethods.ERROR_HOTKEY_ALREADY_REGISTERED
            ? "，该按键或组合键已被其他程序占用；如需继续，请点击“强力绑定”"
            : $"（错误码 {errorCode}）";
        return new HotkeyChangeResult(false, "快捷键注册失败" + suffix);
    }

    private void ReleaseKeyboardHookIfUnused()
    {
        if (_keyboardHook is null || _recordingCommand is not null || _captureForceBinding || _ocrForceBinding)
        {
            return;
        }

        DisposeKeyboardHook();
    }

    private bool EnsureKeyboardHook(out int errorCode)
    {
        if (_keyboardHook is { IsRunning: true })
        {
            errorCode = 0;
            return true;
        }

        DisposeKeyboardHook();

        SimpleGlobalHook hook = new(
            GlobalHookType.Keyboard,
            runAsyncOnBackgroundThread: true);
        hook.KeyPressed += OnKeyboardKeyPressed;
        hook.KeyReleased += OnKeyboardKeyReleased;

        using ManualResetEventSlim hookStarted = new(false);
        EventHandler<HookEventArgs> onHookEnabled = (_, _) => hookStarted.Set();
        hook.HookEnabled += onHookEnabled;

        try
        {
            Task hookTask = hook.RunAsync();
            if (!hookStarted.Wait(TimeSpan.FromSeconds(2)) || hookTask.IsFaulted)
            {
                Exception failure = hookTask.Exception?.GetBaseException()
                    ?? new InvalidOperationException("SharpHook 未能启动全局键盘 Hook");
                errorCode = GetKeyboardHookErrorCode(failure);
                DisposeHook(hook);
                return false;
            }

            _keyboardHook = hook;
            errorCode = 0;
            return true;
        }
        catch (Exception exception)
        {
            errorCode = GetKeyboardHookErrorCode(exception);
            DisposeHook(hook);
            return false;
        }
        finally
        {
            hook.HookEnabled -= onHookEnabled;
        }
    }

    private void DisposeKeyboardHook()
    {
        SimpleGlobalHook? hook = _keyboardHook;
        _keyboardHook = null;
        _pressedKeys.Clear();
        _suppressedKeys.Clear();

        if (hook is not null)
        {
            DisposeHook(hook);
        }
    }

    private static void DisposeHook(SimpleGlobalHook hook)
    {
        try
        {
            if (hook.IsRunning)
            {
                hook.Stop();
            }
        }
        catch
        {
            // 关闭阶段不应影响应用退出。
        }

        try
        {
            hook.Dispose();
        }
        catch
        {
            // 关闭阶段不应影响应用退出。
        }
    }

    private static int GetKeyboardHookErrorCode(Exception exception)
    {
        if (exception is UnauthorizedAccessException ||
            exception is Win32Exception { NativeErrorCode: NativeMethods.ERROR_ACCESS_DENIED } ||
            exception.Message.Contains("access denied", StringComparison.OrdinalIgnoreCase))
        {
            return NativeMethods.ERROR_ACCESS_DENIED;
        }

        return 0;
    }

    private void OnKeyboardKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        if (e.IsEventSimulated || e.Data.KeyCode == KeyCode.VcUndefined)
        {
            return;
        }

        KeyCode keyCode = e.Data.KeyCode;
        bool wasSuppressed = _suppressedKeys.Contains(keyCode);
        _pressedKeys.Add(keyCode);
        if (wasSuppressed)
        {
            e.SuppressEvent = true;
            return;
        }

        if (_recordingCommand is not null)
        {
            _suppressedKeys.Add(keyCode);
            e.SuppressEvent = true;
            if (keyCode == KeyCode.VcEscape)
            {
                NativeMethods.PostMessage(Handle, WM_APP_RECORD_CANCELLED, 0, 0);
                return;
            }

            if (IsModifierKey(keyCode))
            {
                return;
            }

            HotkeyGesture gesture = CreateGestureFromPressedKeys(keyCode);
            bool gestureIsValid = _recordingForceBinding
                ? gesture.IsValidForForceBinding
                : gesture.IsValid;
            if (gestureIsValid)
            {
                _pendingGestures.Enqueue((_recordingCommand.Value, gesture));
                NativeMethods.PostMessage(Handle, WM_APP_RECORD_GESTURE, 0, 0);
            }
            else
            {
                HotkeyChangeResult rejection = CreateInvalidGestureResult(gesture, _recordingForceBinding);
                _pendingRecordingFeedback.Enqueue(rejection.Message);
                NativeMethods.PostMessage(Handle, WM_APP_RECORD_FEEDBACK, 0, 0);
            }

            return;
        }

        if (TryGetForceCommand(keyCode, out HotkeyCommand command))
        {
            // Modifier key-down events have already reached the foreground app by the
            // time the complete gesture is known. Suppressing their key-up events would
            // leave Ctrl/Alt/Shift logically stuck and break unrelated keys such as Delete.
            // Only suppress the trigger key so every propagated key-down keeps its matching
            // key-up event.
            _suppressedKeys.Add(keyCode);
            e.SuppressEvent = true;
            NativeMethods.PostMessage(Handle, WM_APP_FORCE_TRIGGER, (nint)command, 0);
        }
    }

    private void OnKeyboardKeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        if (e.IsEventSimulated || e.Data.KeyCode == KeyCode.VcUndefined)
        {
            return;
        }

        KeyCode keyCode = e.Data.KeyCode;
        bool suppress = _suppressedKeys.Remove(keyCode);
        _pressedKeys.Remove(keyCode);
        if (suppress)
        {
            e.SuppressEvent = true;
        }
    }

    private HotkeyGesture CreateGestureFromPressedKeys(KeyCode keyCode)
    {
        Keys modifiers = Keys.None;
        if (_pressedKeys.Any(IsControlKey)) modifiers |= Keys.Control;
        if (_pressedKeys.Any(IsAltKey)) modifiers |= Keys.Alt;
        if (_pressedKeys.Any(IsShiftKey)) modifiers |= Keys.Shift;
        return new HotkeyGesture(ToWinFormsKey(keyCode), modifiers);
    }

    private bool TryGetForceCommand(KeyCode keyCode, out HotkeyCommand command)
    {
        HotkeyGesture pressed = CreateGestureFromPressedKeys(keyCode);
        if (_captureForceBinding && pressed == _captureGesture)
        {
            command = HotkeyCommand.Capture;
            return true;
        }

        if (_ocrForceBinding && pressed == _ocrGesture)
        {
            command = HotkeyCommand.Ocr;
            return true;
        }

        command = default;
        return false;
    }

    private static bool IsModifierKey(KeyCode keyCode) =>
        HotkeyGesture.IsModifierKey(ToWinFormsKey(keyCode));

    private static bool IsControlKey(KeyCode keyCode) => keyCode is
        KeyCode.VcLeftControl or KeyCode.VcRightControl;

    private static bool IsAltKey(KeyCode keyCode) => keyCode is
        KeyCode.VcLeftAlt or KeyCode.VcRightAlt;

    private static bool IsShiftKey(KeyCode keyCode) => keyCode is
        KeyCode.VcLeftShift or KeyCode.VcRightShift;

    private static Keys ToWinFormsKey(KeyCode keyCode)
    {
        int value = (int)keyCode;
        if (value >= (int)KeyCode.VcF1 && value <= (int)KeyCode.VcF12)
        {
            return (Keys)((int)Keys.F1 + (value - (int)KeyCode.VcF1));
        }

        if (value >= (int)KeyCode.VcF13 && value <= (int)KeyCode.VcF24)
        {
            return (Keys)((int)Keys.F13 + (value - (int)KeyCode.VcF13));
        }

        if (value >= (int)KeyCode.Vc0 && value <= (int)KeyCode.Vc9)
        {
            return (Keys)((int)Keys.D0 + (value - (int)KeyCode.Vc0));
        }

        if (value >= (int)KeyCode.VcA && value <= (int)KeyCode.VcZ)
        {
            return (Keys)((int)Keys.A + (value - (int)KeyCode.VcA));
        }

        if (value >= (int)KeyCode.VcNumPad0 && value <= (int)KeyCode.VcNumPad9)
        {
            return (Keys)((int)Keys.NumPad0 + (value - (int)KeyCode.VcNumPad0));
        }

        return keyCode switch
        {
            KeyCode.VcEscape => Keys.Escape,
            KeyCode.VcBackQuote => Keys.Oemtilde,
            KeyCode.VcMinus => Keys.OemMinus,
            KeyCode.VcEquals => Keys.Oemplus,
            KeyCode.VcBackspace => Keys.Back,
            KeyCode.VcTab => Keys.Tab,
            KeyCode.VcCapsLock => Keys.CapsLock,
            KeyCode.VcOpenBracket => Keys.OemOpenBrackets,
            KeyCode.VcCloseBracket => Keys.OemCloseBrackets,
            KeyCode.VcBackslash => Keys.OemPipe,
            KeyCode.VcSemicolon => Keys.OemSemicolon,
            KeyCode.VcQuote => Keys.OemQuotes,
            KeyCode.VcEnter or KeyCode.VcNumPadEnter => Keys.Enter,
            KeyCode.VcComma => Keys.Oemcomma,
            KeyCode.VcPeriod => Keys.OemPeriod,
            KeyCode.VcSlash => Keys.OemQuestion,
            KeyCode.VcSpace => Keys.Space,
            KeyCode.Vc102 => Keys.Oem102,
            KeyCode.VcPrintScreen => Keys.PrintScreen,
            KeyCode.VcScrollLock => Keys.Scroll,
            KeyCode.VcPause => Keys.Pause,
            KeyCode.VcCancel => Keys.Cancel,
            KeyCode.VcHelp => Keys.Help,
            KeyCode.VcInsert => Keys.Insert,
            KeyCode.VcDelete => Keys.Delete,
            KeyCode.VcHome => Keys.Home,
            KeyCode.VcEnd => Keys.End,
            KeyCode.VcPageUp => Keys.PageUp,
            KeyCode.VcPageDown => Keys.PageDown,
            KeyCode.VcUp => Keys.Up,
            KeyCode.VcLeft => Keys.Left,
            KeyCode.VcRight => Keys.Right,
            KeyCode.VcDown => Keys.Down,
            KeyCode.VcNumLock => Keys.NumLock,
            KeyCode.VcNumPadClear => Keys.Clear,
            KeyCode.VcNumPadDivide => Keys.Divide,
            KeyCode.VcNumPadMultiply => Keys.Multiply,
            KeyCode.VcNumPadSubtract => Keys.Subtract,
            KeyCode.VcNumPadEquals => Keys.Oemplus,
            KeyCode.VcNumPadAdd => Keys.Add,
            KeyCode.VcNumPadDecimal => Keys.Decimal,
            KeyCode.VcNumPadSeparator => Keys.Separator,
            KeyCode.VcLeftShift => Keys.LShiftKey,
            KeyCode.VcRightShift => Keys.RShiftKey,
            KeyCode.VcLeftControl => Keys.LControlKey,
            KeyCode.VcRightControl => Keys.RControlKey,
            KeyCode.VcLeftAlt => Keys.LMenu,
            KeyCode.VcRightAlt => Keys.RMenu,
            KeyCode.VcLeftMeta => Keys.LWin,
            KeyCode.VcRightMeta => Keys.RWin,
            KeyCode.VcContextMenu => Keys.Apps,
            KeyCode.VcVolumeMute => Keys.VolumeMute,
            KeyCode.VcVolumeDown => Keys.VolumeDown,
            KeyCode.VcVolumeUp => Keys.VolumeUp,
            KeyCode.VcMediaPlay => Keys.MediaPlayPause,
            KeyCode.VcMediaStop => Keys.MediaStop,
            KeyCode.VcMediaPrevious => Keys.MediaPreviousTrack,
            KeyCode.VcMediaNext => Keys.MediaNextTrack,
            KeyCode.VcBrowserSearch => Keys.BrowserSearch,
            KeyCode.VcBrowserHome => Keys.BrowserHome,
            KeyCode.VcBrowserBack => Keys.BrowserBack,
            KeyCode.VcBrowserForward => Keys.BrowserForward,
            KeyCode.VcBrowserStop => Keys.BrowserStop,
            KeyCode.VcBrowserRefresh => Keys.BrowserRefresh,
            KeyCode.VcBrowserFavorites => Keys.BrowserFavorites,
            _ => Keys.None
        };
    }

    private void TriggerForceCommand(HotkeyCommand command)
    {
        if (command == HotkeyCommand.Capture)
        {
            if (Environment.TickCount64 >= _captureSuppressUntil)
            {
                CaptureTriggered?.Invoke();
            }
        }
        else if (Environment.TickCount64 >= _ocrSuppressUntil)
        {
            OcrTriggered?.Invoke();
        }
    }

    private HotkeyChangeResult RequestElevatedRestart(HotkeyCommand command, HotkeyGesture gesture)
    {
        string previousHotkey = command == HotkeyCommand.Capture
            ? ConfigService.Current.CaptureHotkey
            : ConfigService.Current.OcrHotkey;
        bool previousForce = command == HotkeyCommand.Capture
            ? ConfigService.Current.CaptureHotkeyForceBinding
            : ConfigService.Current.OcrHotkeyForceBinding;

        if (command == HotkeyCommand.Capture)
        {
            ConfigService.Current.CaptureHotkey = gesture.ConfigText;
            ConfigService.Current.CaptureHotkeyForceBinding = true;
        }
        else
        {
            ConfigService.Current.OcrHotkey = gesture.ConfigText;
            ConfigService.Current.OcrHotkeyForceBinding = true;
        }

        try
        {
            ConfigService.Save();
            Process.Start(new ProcessStartInfo
            {
                FileName = Application.ExecutablePath,
                Arguments = $"--elevated-relaunch --wait-for-pid {Environment.ProcessId}",
                UseShellExecute = true,
                Verb = "runas"
            });
            Application.ExitThread();
            return new HotkeyChangeResult(false, "强力绑定需要管理员权限，正在请求 UAC；允许后应用会自动重启");
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            RestorePendingHotkey(command, previousHotkey, previousForce);
            return new HotkeyChangeResult(false, "已取消管理员权限请求，原快捷键保持不变");
        }
        catch
        {
            RestorePendingHotkey(command, previousHotkey, previousForce);
            return new HotkeyChangeResult(false, "无法请求管理员权限，原快捷键保持不变");
        }
    }

    private static HotkeyChangeResult CreateInvalidGestureResult(HotkeyGesture gesture, bool forceBinding)
    {
        if (gesture.KeyCode == Keys.None)
        {
            return new HotkeyChangeResult(false, "无法识别这个按键，请换一个按键重试");
        }

        return new HotkeyChangeResult(
            false,
            forceBinding
                ? $"强力绑定不支持 {gesture.DisplayText}；请按一个非修饰键，Esc 取消"
                : $"普通绑定不能单独使用 {gesture.DisplayText}；请加 Ctrl、Alt 或 Shift，或点击“强力绑定”后再按该键");
    }

    private HotkeyChangeResult RequestElevatedRestartForRecording(bool forceBinding)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Application.ExecutablePath,
                Arguments = $"--elevated-relaunch --wait-for-pid {Environment.ProcessId}",
                UseShellExecute = true,
                Verb = "runas"
            });
            Application.ExitThread();
            return new HotkeyChangeResult(
                false,
                forceBinding
                    ? "强力绑定需要管理员权限，正在请求 UAC；允许后请重新点击“强力绑定”"
                    : "快捷键录制需要管理员权限，正在请求 UAC；允许后请重新点击快捷键框");
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            return new HotkeyChangeResult(false, "已取消管理员权限请求，原快捷键保持不变");
        }
        catch
        {
            return new HotkeyChangeResult(false, "无法请求管理员权限，原快捷键保持不变");
        }
    }

    private static void RestorePendingHotkey(HotkeyCommand command, string hotkey, bool forceBinding)
    {
        if (command == HotkeyCommand.Capture)
        {
            ConfigService.Current.CaptureHotkey = hotkey;
            ConfigService.Current.CaptureHotkeyForceBinding = forceBinding;
        }
        else
        {
            ConfigService.Current.OcrHotkey = hotkey;
            ConfigService.Current.OcrHotkeyForceBinding = forceBinding;
        }

        ConfigService.Save();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_APP_RECORD_GESTURE)
        {
            if (_pendingGestures.TryDequeue(out (HotkeyCommand Command, HotkeyGesture Gesture) pendingGesture))
            {
                RecordingGestureCaptured?.Invoke(pendingGesture.Command, pendingGesture.Gesture);
            }
        }
        else if (m.Msg == WM_APP_RECORD_CANCELLED)
        {
            RecordingCancelled?.Invoke();
        }
        else if (m.Msg == WM_APP_FORCE_TRIGGER)
        {
            TriggerForceCommand((HotkeyCommand)m.WParam.ToInt32());
        }
        else if (m.Msg == WM_APP_RECORD_FEEDBACK)
        {
            if (_pendingRecordingFeedback.TryDequeue(out string? feedback))
            {
                RecordingFeedback?.Invoke(feedback);
            }
        }
        else if (m.Msg == NativeMethods.WM_HOTKEY)
        {
            int hotkeyId = m.WParam.ToInt32();
            if (_captureRegistered && hotkeyId == _captureHotkeyId)
            {
                if (Environment.TickCount64 >= _captureSuppressUntil)
                {
                    CaptureTriggered?.Invoke();
                }
            }
            else if (_ocrRegistered && hotkeyId == _ocrHotkeyId)
            {
                if (Environment.TickCount64 >= _ocrSuppressUntil)
                {
                    OcrTriggered?.Invoke();
                }
            }
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (_captureRegistered)
        {
            NativeMethods.UnregisterHotKey(Handle, _captureHotkeyId);
            _captureRegistered = false;
        }

        if (_ocrRegistered)
        {
            NativeMethods.UnregisterHotKey(Handle, _ocrHotkeyId);
            _ocrRegistered = false;
        }

        DisposeKeyboardHook();

        DestroyHandle();
        GC.SuppressFinalize(this);
    }
}
