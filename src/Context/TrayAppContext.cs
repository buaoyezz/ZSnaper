using ZSnaper.Forms;
using ZSnaper.Helpers;
using ZSnaper.Models;
using ZSnaper.Services;
using ZSnaper.Controls;

namespace ZSnaper.Context;

public class TrayAppContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly Icon _lightAppIcon;
    private readonly Icon _darkAppIcon;
    private readonly OverlayForm _overlay;
    private readonly HotkeyService _hotkeyService;
    private readonly MainForm _mainForm;
    private readonly ModernTrayMenu _trayMenu;
    private readonly ToolStripMenuItem _themeMenuItem;
    private ResultForm? _result;
    private bool _ocrMode;
    private int _captureCount;
    private int _ocrCount;

    public TrayAppContext()
    {
        _lightAppIcon = AppIconProvider.CreateIcon(ThemeMode.Light);
        _darkAppIcon = AppIconProvider.CreateIcon(ThemeMode.Dark);
        Icon currentIcon = CurrentAppIcon;

        _overlay = new OverlayForm { Icon = currentIcon };
        _overlay.Captured += OnCaptured;

        _hotkeyService = new HotkeyService();
        _hotkeyService.CaptureTriggered += () => StartCapture(ocr: false);
        _hotkeyService.OcrTriggered += () => StartCapture(ocr: true);

        _mainForm = new MainForm { Icon = currentIcon };
        _mainForm.RequestCapture += StartCapture;
        _mainForm.RequestHotkeyChange += _hotkeyService.TryUpdateHotkey;

        _tray = new NotifyIcon
        {
            Icon = currentIcon,
            Text = "ZSnaper · 极简截图 & 本地 OCR",
            Visible = true
        };
        ThemeManager.ThemeChanged += ApplyThemeIcon;

        _trayMenu = new ModernTrayMenu();
        _trayMenu.AddBrandAction("打开 ZSnaper", (_, _) => ShowMainForm());
        _trayMenu.AddSectionSeparator();
        _trayMenu.AddAction(
            "截图",
            LucideIcon.Camera,
            (_, _) => StartCapture(ocr: false),
            _hotkeyService.CaptureGesture.DisplayText);
        _trayMenu.AddAction(
            "截图并 OCR",
            LucideIcon.FileText,
            (_, _) => StartCapture(ocr: true),
            _hotkeyService.OcrGesture.DisplayText);
        _themeMenuItem = _trayMenu.AddAction(
            "深色模式",
            LucideIcon.Moon,
            (_, _) => ThemeManager.ToggleTheme(),
            kind: TrayMenuItemKind.ThemeToggle);
        _trayMenu.AddSectionSeparator();
        _trayMenu.AddAction(
            "退出 ZSnaper",
            LucideIcon.Power,
            (_, _) => ExitApp(),
            kind: TrayMenuItemKind.Destructive);
        _trayMenu.Opening += (_, _) => ApplyTrayMenuTheme();
        _tray.ContextMenuStrip = _trayMenu;
        _tray.DoubleClick += (_, _) => ShowMainForm();

        _hotkeyService.RegisterConfiguredHotkeys(out bool captureOk, out bool ocrOk);
        if (!captureOk)
        {
            _tray.ShowBalloonTip(
                2000,
                "ZSnaper",
                $"{_hotkeyService.CaptureGesture.DisplayText} 截图快捷键注册失败，可能已被占用",
                ToolTipIcon.Warning);
        }
        if (!ocrOk)
        {
            _tray.ShowBalloonTip(
                2000,
                "ZSnaper",
                $"{_hotkeyService.OcrGesture.DisplayText} OCR 快捷键注册失败，可能已被占用",
                ToolTipIcon.Warning);
        }

        ShowMainForm();
    }

    private void ShowMainForm()
    {
        if (_mainForm.IsDisposed) return;
        _mainForm.Show();
        _mainForm.WindowState = FormWindowState.Normal;
        _mainForm.Activate();
    }

    private void StartCapture(bool ocr)
    {
        _ocrMode = ocr;
        _overlay.BeginCapture();
    }

    private async void OnCaptured(
        Bitmap bitmap,
        Point screenPoint,
        CaptureCompletionAction action)
    {
        using (bitmap)
        {
            bool performOcr = action == CaptureCompletionAction.Ocr ||
                              action == CaptureCompletionAction.Default && _ocrMode;
            (bool copyImage, bool saveImage) = ResolveCaptureDestinations(action, _ocrMode);

            bool copied = copyImage && CaptureService.TryCopyToClipboard(bitmap);
            string? savedFilePath = saveImage ? CaptureService.SaveToPictures(bitmap) : null;
            _captureCount++;

            if (!performOcr)
            {
                _mainForm.UpdateHomeOverview(_captureCount, _ocrCount, savedFilePath, wasOcr: false);
                ShowCaptureNotification(action, copied, savedFilePath);
                return;
            }

            string text;
            try
            {
                text = await OcrService.RecognizeAsync(bitmap);
            }
            catch (Exception ex)
            {
                text = "(OCR 失败: " + ex.Message + ")";
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                text = "(未识别到文字)";
            }
            else if (ConfigService.Current.AutoCleanOcrParagraphs)
            {
                text = OcrTextFormatter.Clean(text);
            }

            _ocrCount++;
            _mainForm.UpdateHomeOverview(_captureCount, _ocrCount, savedFilePath, wasOcr: true);
            CaptureService.TryCopyTextToClipboard(text);

            if (ConfigService.Current.ShowNotification)
            {
                _tray.ShowBalloonTip(800, "ZSnaper", "OCR 识别完成，文字已复制到剪贴板", ToolTipIcon.Info);
            }

            _mainForm.UpdateLatestOcrText(text);
            _result ??= new ResultForm { Icon = CurrentAppIcon };
            _result.ShowResult(text, screenPoint);
        }
    }

    private static (bool Copy, bool Save) ResolveCaptureDestinations(
        CaptureCompletionAction action,
        bool ocrMode)
    {
        if (action == CaptureCompletionAction.Copy) return (true, false);
        if (action == CaptureCompletionAction.Save) return (false, true);

        if (action == CaptureCompletionAction.Default && !ocrMode)
        {
            return ConfigService.Current.ConfirmButtonBehavior switch
            {
                ConfirmButtonBehavior.Copy => (true, false),
                ConfirmButtonBehavior.Save => (false, true),
                ConfirmButtonBehavior.CopyAndSave => (true, true),
                ConfirmButtonBehavior.FinishOnly => (false, false),
                _ => (ConfigService.Current.AutoCopyClipboard, ConfigService.Current.AutoSavePictures)
            };
        }

        return action is CaptureCompletionAction.Default or CaptureCompletionAction.Ocr
            ? (ConfigService.Current.AutoCopyClipboard, ConfigService.Current.AutoSavePictures)
            : (false, false);
    }

    private void ShowCaptureNotification(
        CaptureCompletionAction action,
        bool copied,
        string? savedFilePath)
    {
        if (!ConfigService.Current.ShowNotification) return;

        string message = action switch
        {
            CaptureCompletionAction.Copy when copied => "截图已复制到剪贴板",
            CaptureCompletionAction.Copy => "无法复制截图，请稍后重试",
            CaptureCompletionAction.Save when savedFilePath is not null => $"截图已保存到 {savedFilePath}",
            CaptureCompletionAction.Default when copied && savedFilePath is not null => "截图已复制并保存",
            CaptureCompletionAction.Default when copied => "截图已复制到剪贴板",
            CaptureCompletionAction.Default when savedFilePath is not null => $"截图已保存到 {savedFilePath}",
            _ => "截图已完成"
        };
        _tray.ShowBalloonTip(800, "ZSnaper", message, ToolTipIcon.Info);
    }

    private void ExitApp()
    {
        _tray.Visible = false;
        _tray.Dispose();
        _overlay.Dispose();
        _result?.Dispose();
        _mainForm.Dispose();
        _hotkeyService.Dispose();
        ExitThread();
    }

    private Icon CurrentAppIcon => ThemeManager.CurrentMode == ThemeMode.Dark
        ? _darkAppIcon
        : _lightAppIcon;

    private void ApplyThemeIcon()
    {
        Icon icon = CurrentAppIcon;
        _mainForm.Icon = icon;
        _overlay.Icon = icon;
        if (_result is not null)
        {
            _result.Icon = icon;
        }
        _tray.Icon = icon;
        ApplyTrayMenuTheme();
    }

    private void ApplyTrayMenuTheme()
    {
        _trayMenu.ApplyTheme();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ThemeManager.ThemeChanged -= ApplyThemeIcon;
            _trayMenu.Dispose();
            _tray.Dispose();
            _overlay.Dispose();
            _result?.Dispose();
            _mainForm.Dispose();
            _hotkeyService.Dispose();
            _lightAppIcon.Dispose();
            _darkAppIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
