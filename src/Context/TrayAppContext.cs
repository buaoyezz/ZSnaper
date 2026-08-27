using ZSnaper.Forms;
using ZSnaper.Helpers;
using ZSnaper.Models;
using ZSnaper.Services;
using ZSnaper.Controls;
using ZSnaper.Update;

namespace ZSnaper.Context;

public class TrayAppContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly Icon _lightAppIcon;
    private readonly Icon _darkAppIcon;
    private Icon _lightTrayIcon;
    private Icon _darkTrayIcon;
    private string _appliedTrayIconSettingsKey;
    private readonly OverlayForm _overlay;
    private readonly HotkeyService _hotkeyService;
    private readonly MainForm _mainForm;
    private readonly ModernTrayMenu _trayMenu;
    private readonly ToolStripMenuItem _captureMenuItem;
    private readonly ToolStripMenuItem _ocrMenuItem;
    private readonly ToolStripMenuItem _themeMenuItem;
    private ResultForm? _result;
    private bool _ocrMode;
    private int _captureCount;
    private int _ocrCount;
    private readonly CancellationTokenSource _updateCancellation = new();
    private readonly System.Windows.Forms.Timer _updateTimer;
    private bool _updateCheckInProgress;

    public TrayAppContext(bool startMinimizedToTray = false)
    {
        _lightAppIcon = AppIconProvider.CreateApplicationIcon(ThemeMode.Light);
        _darkAppIcon = AppIconProvider.CreateApplicationIcon(ThemeMode.Dark);
        _lightTrayIcon = AppIconProvider.CreateTrayIcon(ThemeMode.Light);
        _darkTrayIcon = AppIconProvider.CreateTrayIcon(ThemeMode.Dark);
        _appliedTrayIconSettingsKey = AppIconProvider.GetTrayIconSettingsKey();
        Icon currentWindowIcon = CurrentWindowIcon;
        Icon currentTrayIcon = CurrentTrayIcon;

        _overlay = new OverlayForm { Icon = currentWindowIcon };
        _overlay.Captured += OnCaptured;

        _hotkeyService = new HotkeyService();
        _hotkeyService.CaptureTriggered += () => StartCapture(ocr: false);
        _hotkeyService.OcrTriggered += () => StartCapture(ocr: true);

        _mainForm = new MainForm
        {
            Icon = currentWindowIcon,
            ShowInTaskbar = !startMinimizedToTray
        };
        _mainForm.RequestCapture += StartCapture;
        _mainForm.RequestHotkeyChange += (command, gesture, forceBinding) =>
            _hotkeyService.TryUpdateHotkey(command, gesture, forceBinding);
        _mainForm.RequestHotkeyRecordingStart += _hotkeyService.BeginRecording;
        _mainForm.RequestHotkeyRecordingStop += _ => _hotkeyService.EndRecording();
        _hotkeyService.RecordingGestureCaptured += _mainForm.ApplyRecordedHotkey;
        _hotkeyService.RecordingCancelled += _mainForm.CancelRecordedHotkey;
        _mainForm.RequestUpdateCheck += () => _ = CheckForUpdatesAsync(manual: true);
        _mainForm.RequestOpenUpdate += OpenUpdatePage;
        _mainForm.Shown += (_, _) => CheckForUpdatesIfDue();

        _updateTimer = new System.Windows.Forms.Timer { Interval = 60_000 };
        _updateTimer.Tick += (_, _) => CheckForUpdatesIfDue();
        ConfigService.ConfigChanged += OnUpdateConfigChanged;
        ConfigService.ConfigChanged += ApplyConfiguredTrayIcon;
        ConfigService.ConfigChanged += ApplyHotkeyMenuShortcuts;
        ConfigureUpdateTimer();

        _tray = new NotifyIcon
        {
            Icon = currentTrayIcon,
            Text = "ZSnaper · 极简截图 & 本地 OCR",
            Visible = true
        };
        _overlay.CaptureFailed += message =>
            _tray.ShowBalloonTip(1800, "ZSnaper", message, ToolTipIcon.Warning);
        ThemeManager.ThemeChanged += ApplyThemeIcon;

        _trayMenu = new ModernTrayMenu();
        _trayMenu.AddBrandAction("打开 ZSnaper", (_, _) => ShowMainForm());
        _trayMenu.AddSectionSeparator();
        _captureMenuItem = _trayMenu.AddAction(
            "截图",
            LucideIcon.Camera,
            (_, _) => StartCapture(ocr: false),
            _hotkeyService.CaptureGesture.DisplayText);
        _ocrMenuItem = _trayMenu.AddAction(
            "截图并 OCR",
            LucideIcon.FileText,
            (_, _) => StartCapture(ocr: true),
            _hotkeyService.OcrGesture.DisplayText);
        _trayMenu.AddAction(
            "检查更新",
            LucideIcon.RotateCcw,
            (_, _) => _ = CheckForUpdatesAsync(manual: true));
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
                $"{_hotkeyService.CaptureGesture.DisplayText} 截图快捷键注册失败，{(_hotkeyService.IsCaptureForceBinding ? "强力绑定需要管理员权限" : "可能已被占用")}",
                ToolTipIcon.Warning);
        }
        if (!ocrOk)
        {
            _tray.ShowBalloonTip(
                2000,
                "ZSnaper",
                $"{_hotkeyService.OcrGesture.DisplayText} OCR 快捷键注册失败，{(_hotkeyService.IsOcrForceBinding ? "强力绑定需要管理员权限" : "可能已被占用")}",
                ToolTipIcon.Warning);
        }

        if (!startMinimizedToTray)
        {
            ShowMainForm();
        }
    }

    private void ShowMainForm()
    {
        if (_mainForm.IsDisposed) return;
        _mainForm.ShowInTaskbar = true;
        _mainForm.Show();
        _mainForm.WindowState = FormWindowState.Normal;
        _mainForm.Activate();
    }

    private void OnUpdateConfigChanged()
    {
        ConfigureUpdateTimer();
        CheckForUpdatesIfDue();
    }

    private void ApplyHotkeyMenuShortcuts()
    {
        _captureMenuItem.ShortcutKeyDisplayString = _hotkeyService.CaptureGesture.DisplayText;
        _ocrMenuItem.ShortcutKeyDisplayString = _hotkeyService.OcrGesture.DisplayText;
        _trayMenu.PerformLayout();
    }

    private void ConfigureUpdateTimer()
    {
        if (ConfigService.Current.AutoCheckUpdates)
        {
            _updateTimer.Start();
        }
        else
        {
            _updateTimer.Stop();
        }
    }

    private void CheckForUpdatesIfDue()
    {
        if (!ConfigService.Current.AutoCheckUpdates ||
            _updateCheckInProgress ||
            _updateCancellation.IsCancellationRequested ||
            !IsUpdateCheckDue())
        {
            return;
        }

        _ = CheckForUpdatesAsync(manual: false);
    }

    private static bool IsUpdateCheckDue()
    {
        DateTimeOffset? lastCheck = ConfigService.Current.LastUpdateCheckAt;
        if (lastCheck is null) return true;

        int intervalHours = Math.Clamp(ConfigService.Current.UpdateCheckIntervalHours, 1, 24 * 365);
        return DateTimeOffset.UtcNow - lastCheck.Value >= TimeSpan.FromHours(intervalHours);
    }

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (_updateCheckInProgress || _updateCancellation.IsCancellationRequested) return;

        _updateCheckInProgress = true;
        ConfigService.Current.LastUpdateCheckAt = DateTimeOffset.UtcNow;
        ConfigService.Save();
        _mainForm.RefreshUpdateCheckInfo();
        _mainForm.SetUpdateStatus("检查中…", isBusy: true);

        try
        {
            UpdateCheckResult result = await VersionGet.CheckForUpdateAsync(_updateCancellation.Token);
            if (_updateCancellation.IsCancellationRequested || _mainForm.IsDisposed) return;

            if (result.IsSuccess && result.HasUpdate && result.LatestRelease is { } release)
            {
                string version = release.CleanVersion;
                _mainForm.SetUpdateStatus(
                    "打开下载页",
                    isBusy: false,
                    release.HtmlUrl);

                if (manual || ConfigService.Current.ShowNotification)
                {
                    _tray.ShowBalloonTip(
                        4000,
                        "ZSnaper 有新版本",
                        $"发现 v{version}，可在设置页打开下载页",
                        ToolTipIcon.Info);
                }
            }
            else if (result.IsSuccess)
            {
                _mainForm.SetUpdateStatus("已是最新", isBusy: false);
                if (manual)
                {
                    _tray.ShowBalloonTip(2200, "ZSnaper", "当前已是最新版本", ToolTipIcon.Info);
                }
            }
            else
            {
                _mainForm.SetUpdateStatus("检查失败", isBusy: false);
                if (manual)
                {
                    _tray.ShowBalloonTip(
                        3000,
                        "ZSnaper",
                        result.ErrorMessage ?? "检查更新失败，请稍后重试",
                        ToolTipIcon.Warning);
                }
            }
        }
        finally
        {
            _updateCheckInProgress = false;
        }
    }

    private void OpenUpdatePage(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            _tray.ShowBalloonTip(2500, "ZSnaper", "更新链接无效", ToolTipIcon.Warning);
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _tray.ShowBalloonTip(3000, "ZSnaper", "无法打开更新页面：" + ex.Message, ToolTipIcon.Warning);
        }
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
            _result ??= new ResultForm { Icon = CurrentWindowIcon };
            _result.ShowResult(text, screenPoint);
        }
    }

    private static (bool Copy, bool Save) ResolveCaptureDestinations(
        CaptureCompletionAction action,
        bool ocrMode)
    {
        if (action == CaptureCompletionAction.Copy) return (true, false);
        if (action == CaptureCompletionAction.Save) return (false, true);

        if (action == CaptureCompletionAction.ScrollCapture)
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
            CaptureCompletionAction.ScrollCapture when copied && savedFilePath is not null => "长截图已复制并保存",
            CaptureCompletionAction.ScrollCapture when copied => "长截图已复制到剪贴板",
            CaptureCompletionAction.ScrollCapture when savedFilePath is not null => $"长截图已保存到 {savedFilePath}",
            CaptureCompletionAction.ScrollCapture => "长截图已完成",
            CaptureCompletionAction.Default when copied && savedFilePath is not null => "截图已复制并保存",
            CaptureCompletionAction.Default when copied => "截图已复制到剪贴板",
            CaptureCompletionAction.Default when savedFilePath is not null => $"截图已保存到 {savedFilePath}",
            _ => "截图已完成"
        };
        _tray.ShowBalloonTip(800, "ZSnaper", message, ToolTipIcon.Info);
    }

    private void ExitApp()
    {
        _updateCancellation.Cancel();
        _updateTimer.Stop();
        _tray.Visible = false;
        _tray.Dispose();
        _overlay.Dispose();
        _result?.Dispose();
        _mainForm.Dispose();
        _hotkeyService.Dispose();
        ExitThread();
    }

    private Icon CurrentWindowIcon => ThemeManager.CurrentMode == ThemeMode.Dark
        ? _darkAppIcon
        : _lightAppIcon;

    private Icon CurrentTrayIcon => ThemeManager.CurrentMode == ThemeMode.Dark
        ? _darkTrayIcon
        : _lightTrayIcon;

    private void ApplyThemeIcon()
    {
        Icon windowIcon = CurrentWindowIcon;
        _mainForm.Icon = windowIcon;
        _overlay.Icon = windowIcon;
        if (_result is not null)
        {
            _result.Icon = windowIcon;
        }
        _tray.Icon = CurrentTrayIcon;
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
            ConfigService.ConfigChanged -= OnUpdateConfigChanged;
            ConfigService.ConfigChanged -= ApplyConfiguredTrayIcon;
            _updateTimer.Stop();
            _updateTimer.Dispose();
            _trayMenu.Dispose();
            _tray.Dispose();
            _overlay.Dispose();
            _result?.Dispose();
            _mainForm.Dispose();
            _hotkeyService.Dispose();
            _updateCancellation.Dispose();
            _lightTrayIcon.Dispose();
            _darkTrayIcon.Dispose();
            _lightAppIcon.Dispose();
            _darkAppIcon.Dispose();
        }
        base.Dispose(disposing);
    }

    private void ApplyConfiguredTrayIcon()
    {
        string settingsKey = AppIconProvider.GetTrayIconSettingsKey();
        if (string.Equals(settingsKey, _appliedTrayIconSettingsKey, StringComparison.Ordinal))
        {
            return;
        }

        Icon? nextLight = null;
        Icon? nextDark = null;
        try
        {
            nextLight = AppIconProvider.CreateTrayIcon(ThemeMode.Light);
            nextDark = AppIconProvider.CreateTrayIcon(ThemeMode.Dark);
        }
        catch
        {
            nextLight?.Dispose();
            nextDark?.Dispose();
            return;
        }

        Icon previousLight = _lightTrayIcon;
        Icon previousDark = _darkTrayIcon;
        _lightTrayIcon = nextLight;
        _darkTrayIcon = nextDark;
        _appliedTrayIconSettingsKey = settingsKey;
        _tray.Icon = CurrentTrayIcon;
        previousLight.Dispose();
        previousDark.Dispose();
    }
}
