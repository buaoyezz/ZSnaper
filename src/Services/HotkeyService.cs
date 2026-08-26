using System.Runtime.InteropServices;
using ZSnaper.Interop;
using ZSnaper.Models;

namespace ZSnaper.Services;

public class HotkeyService : NativeWindow, IDisposable
{
    public const int HOTKEY_CAPTURE = 1;
    public const int HOTKEY_OCR = 2;

    public event Action? CaptureTriggered;
    public event Action? OcrTriggered;

    private bool _captureRegistered;
    private bool _ocrRegistered;
    private HotkeyGesture _captureGesture = new(Keys.Q, Keys.Alt);
    private HotkeyGesture _ocrGesture = new(Keys.X, Keys.Alt);
    private long _captureSuppressUntil;
    private long _ocrSuppressUntil;

    public bool IsCaptureRegistered => _captureRegistered;
    public bool IsOcrRegistered => _ocrRegistered;

    public HotkeyService()
    {
        CreateHandle(new CreateParams());
    }

    public HotkeyGesture CaptureGesture => _captureGesture;
    public HotkeyGesture OcrGesture => _ocrGesture;

    public void RegisterConfiguredHotkeys(out bool captureOk, out bool ocrOk)
    {
        _captureGesture = ParseOrDefault(ConfigService.Current.CaptureHotkey, new HotkeyGesture(Keys.Q, Keys.Alt));
        _ocrGesture = ParseOrDefault(ConfigService.Current.OcrHotkey, new HotkeyGesture(Keys.X, Keys.Alt));
        if (_ocrGesture == _captureGesture)
        {
            _ocrGesture = new HotkeyGesture(Keys.X, Keys.Alt);
        }

        captureOk = Register(HOTKEY_CAPTURE, _captureGesture);
        ocrOk = Register(HOTKEY_OCR, _ocrGesture);
        _captureRegistered = captureOk;
        _ocrRegistered = ocrOk;
    }

    public HotkeyChangeResult TryUpdateHotkey(HotkeyCommand command, HotkeyGesture gesture)
    {
        if (!gesture.IsValid)
        {
            return new HotkeyChangeResult(false, "请至少按下一个修饰键和一个普通按键");
        }

        HotkeyGesture otherGesture = command == HotkeyCommand.Capture ? _ocrGesture : _captureGesture;
        if (gesture == otherGesture)
        {
            return new HotkeyChangeResult(false, "该组合键已用于另一项功能");
        }

        int id = command == HotkeyCommand.Capture ? HOTKEY_CAPTURE : HOTKEY_OCR;
        bool wasRegistered = command == HotkeyCommand.Capture ? _captureRegistered : _ocrRegistered;
        HotkeyGesture previous = command == HotkeyCommand.Capture ? _captureGesture : _ocrGesture;

        if (wasRegistered && !NativeMethods.UnregisterHotKey(Handle, id))
        {
            return new HotkeyChangeResult(false, "无法释放原快捷键，请稍后重试");
        }

        if (Register(id, gesture))
        {
            if (command == HotkeyCommand.Capture)
            {
                _captureGesture = gesture;
                _captureRegistered = true;
                _captureSuppressUntil = Environment.TickCount64 + 750;
                ConfigService.Current.CaptureHotkey = gesture.ConfigText;
            }
            else
            {
                _ocrGesture = gesture;
                _ocrRegistered = true;
                _ocrSuppressUntil = Environment.TickCount64 + 750;
                ConfigService.Current.OcrHotkey = gesture.ConfigText;
            }

            ConfigService.Save();
            return new HotkeyChangeResult(true, $"已更新为 {gesture.DisplayText}");
        }

        int errorCode = Marshal.GetLastPInvokeError();
        bool restored = wasRegistered && Register(id, previous);
        if (command == HotkeyCommand.Capture)
        {
            _captureRegistered = restored;
        }
        else
        {
            _ocrRegistered = restored;
        }

        string suffix = errorCode == 1409 ? "，该组合键已被其他程序占用" : string.Empty;
        return new HotkeyChangeResult(false, "快捷键注册失败" + suffix);
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

    private static HotkeyGesture ParseOrDefault(string? value, HotkeyGesture fallback) =>
        HotkeyGesture.TryParse(value, out HotkeyGesture gesture) ? gesture : fallback;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_HOTKEY)
        {
            switch (m.WParam.ToInt32())
            {
                case HOTKEY_CAPTURE:
                    if (Environment.TickCount64 >= _captureSuppressUntil)
                    {
                        CaptureTriggered?.Invoke();
                    }
                    break;
                case HOTKEY_OCR:
                    if (Environment.TickCount64 >= _ocrSuppressUntil)
                    {
                        OcrTriggered?.Invoke();
                    }
                    break;
            }
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (_captureRegistered)
        {
            NativeMethods.UnregisterHotKey(Handle, HOTKEY_CAPTURE);
            _captureRegistered = false;
        }

        if (_ocrRegistered)
        {
            NativeMethods.UnregisterHotKey(Handle, HOTKEY_OCR);
            _ocrRegistered = false;
        }

        DestroyHandle();
        GC.SuppressFinalize(this);
    }
}
