using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ZSnaper.Installer.Core;

namespace ZSnaper.FullInstaller;

internal sealed partial class InstallerWindow : Window
{
    private const int DwmWindowCornerPreference = 33;
    private const int DwmRound = 2;

    private readonly InstallerService _installerService = new();
    private readonly string? _payloadDirectory;
    private readonly string _installerExecutable;
    private readonly string _version;

    private readonly Grid _readyPanel;
    private readonly Grid _customizePanel;
    private readonly Grid _progressPanel;
    private readonly Grid _successPanel;

    private readonly TextBox _pathBox;
    private readonly Button _browseButton;
    private readonly CheckBox _desktopCheck;
    private readonly CheckBox _startMenuCheck;
    private readonly CheckBox _autoStartCheck;
    private readonly Button _nextButton;
    private readonly Button _backFromCustomizeButton;
    private readonly Button _startCustomizeInstallButton;
    private readonly TextBlock _errorText;

    private readonly ProgressBar _progress;
    private readonly TextBlock _progressStatus;
    private readonly TextBlock _progressPercent;

    private readonly Button _finishButton;
    private readonly Button _exitButton;

    private string? _installedDirectory;
    private bool _busy;

    public InstallerWindow(string? payloadDirectory, string installerExecutable, string version)
    {
        _payloadDirectory = payloadDirectory;
        _installerExecutable = installerExecutable;
        _version = version;

        AvaloniaXamlLoader.Load(this);

        _readyPanel = this.FindControl<Grid>("ReadyPanel")!;
        _customizePanel = this.FindControl<Grid>("CustomizePanel")!;
        _progressPanel = this.FindControl<Grid>("ProgressPanel")!;
        _successPanel = this.FindControl<Grid>("SuccessPanel")!;

        _pathBox = this.FindControl<TextBox>("InstallPathBox")!;
        _browseButton = this.FindControl<Button>("BrowseButton")!;
        _desktopCheck = this.FindControl<CheckBox>("DesktopShortcutCheck")!;
        _startMenuCheck = this.FindControl<CheckBox>("StartMenuShortcutCheck")!;
        _autoStartCheck = this.FindControl<CheckBox>("AutoStartCheck")!;

        _nextButton = this.FindControl<Button>("NextButton")!;
        _backFromCustomizeButton = this.FindControl<Button>("BackFromCustomizeButton")!;
        _startCustomizeInstallButton = this.FindControl<Button>("StartCustomizeInstallButton")!;
        _errorText = this.FindControl<TextBlock>("ErrorText")!;

        _progress = this.FindControl<ProgressBar>("InstallProgress")!;
        _progressStatus = this.FindControl<TextBlock>("ProgressStatus")!;
        _progressPercent = this.FindControl<TextBlock>("ProgressPercent")!;

        _finishButton = this.FindControl<Button>("FinishButton")!;
        _exitButton = this.FindControl<Button>("ExitButton")!;

        InstallationInfo? installed = _installerService.GetInstalled();
        _installedDirectory = installed?.InstallDirectory;
        _pathBox.Text = _installedDirectory ?? InstallerPaths.DefaultInstallDirectory;

        // Step navigation
        _nextButton.Click += (_, _) => ShowPanel(_customizePanel);
        _backFromCustomizeButton.Click += (_, _) => ShowPanel(_readyPanel);

        _browseButton.Click += BrowseFolderAsync;
        _startCustomizeInstallButton.Click += (_, _) => StartInstall();
        _finishButton.Click += (_, _) => LaunchAndExit();
        _exitButton.Click += (_, _) => Close();

        this.FindControl<Button>("CloseButton")!.Click += (_, _) => Close();

        var minBtn = this.FindControl<Button>("MinimizeButton");
        if (minBtn != null)
        {
            minBtn.Click += (_, _) => WindowState = WindowState.Minimized;
        }

        Closing += OnClosing;
        Opened += OnOpened;
    }

    public int ExitCode { get; private set; }

    private void ShowPanel(Grid targetPanel)
    {
        _readyPanel.IsVisible = targetPanel == _readyPanel;
        _customizePanel.IsVisible = targetPanel == _customizePanel;
        _progressPanel.IsVisible = targetPanel == _progressPanel;
        _successPanel.IsVisible = targetPanel == _successPanel;
    }

    internal void ShowPreviewPage(int pageIndex)
    {
        Grid targetPanel = pageIndex switch
        {
            0 => _readyPanel,
            1 => _customizePanel,
            2 => _progressPanel,
            3 => _successPanel,
            _ => throw new ArgumentOutOfRangeException(nameof(pageIndex))
        };
        ShowPanel(targetPanel);
    }

    internal RenderTargetBitmap? CaptureRenderedFrame()
    {
        PixelSize pixelSize = PixelSize.FromSize(Bounds.Size, RenderScaling);
        if (pixelSize.Width <= 0 || pixelSize.Height <= 0)
        {
            return null;
        }

        var bitmap = new RenderTargetBitmap(
            pixelSize,
            new Vector(96 * RenderScaling, 96 * RenderScaling));
        bitmap.Render(this);
        return bitmap;
    }

    private void OnTitlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return;
        }

        nint windowHandle = TryGetPlatformHandle()?.Handle ?? nint.Zero;
        if (windowHandle == nint.Zero)
        {
            return;
        }

        int preference = DwmRound;
        _ = DwmSetWindowAttribute(
            windowHandle,
            DwmWindowCornerPreference,
            ref preference,
            sizeof(int));
    }

    private async void BrowseFolderAsync(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            IStorageProvider? storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storageProvider is null) return;

            IReadOnlyList<IStorageFolder> folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "选择 ZSnaper 安装路径",
                AllowMultiple = false
            });
            if (folders.Count > 0)
            {
                string? path = folders[0].TryGetLocalPath();
                if (string.IsNullOrWhiteSpace(path) && folders[0].Path.IsAbsoluteUri)
                {
                    path = folders[0].Path.LocalPath;
                }
                if (!string.IsNullOrWhiteSpace(path))
                {
                    _pathBox.Text = NormalizeSelectedInstallPath(path);
                }
            }
        }
        catch (Exception ex)
        {
            ShowError($"选择路径失败: {ex.Message}");
        }
    }

    private static string NormalizeSelectedInstallPath(string path)
    {
        string trimmed = path.Trim();
        string root = Path.GetPathRoot(trimmed) ?? string.Empty;

        // If user picked a drive root like "D:\", "D:", "C:\"
        if (string.Equals(trimmed.TrimEnd('\\', '/'), root.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(root.EndsWith('\\') ? root : root + "\\", "ZSnaper");
        }

        // If user picked a directory whose name is not already "ZSnaper" (e.g. "D:\Software" -> "D:\Software\ZSnaper")
        if (!string.Equals(Path.GetFileName(trimmed.TrimEnd('\\', '/')), "ZSnaper", StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(trimmed, "ZSnaper");
        }

        return trimmed;
    }

    private async void StartInstall()
    {
        if (_busy) return;

        string targetPath = _pathBox.Text ?? string.Empty;
        if (_payloadDirectory is null)
        {
            ShowError("当前为调试模式（无 payload），请运行 Build-Installers.ps1 生成完整安装包");
            return;
        }

        if (!InstallerPaths.IsUsableInstallDirectory(targetPath, out string error))
        {
            ShowError(error);
            ShowPanel(_customizePanel);
            return;
        }

        _installedDirectory = InstallerPaths.Normalize(targetPath);
        _busy = true;
        _errorText.IsVisible = false;

        ShowPanel(_progressPanel);

        try
        {
            InstallOptions options = new(_installedDirectory, _version, false, false, false, false);
            Progress<InstallProgress> progress = new(value =>
                Dispatcher.UIThread.Post(() =>
                {
                    int pct = value.Total <= 0 ? 0 : Math.Clamp(value.Completed * 100 / value.Total, 0, 100);
                    _progress.Value = pct;
                    _progressPercent.Text = $"{pct}%";

                    if (value.Stage == "Copying application files")
                    {
                        _progressStatus.Text = "正在复制文件...";
                    }
                    else if (value.Stage == "Writing uninstaller registry keys")
                    {
                        _progressStatus.Text = "正在注册组件...";
                    }
                    else
                    {
                        _progressStatus.Text = value.Stage;
                    }
                }));

            await Task.Run(() => _installerService.Install(
                _payloadDirectory,
                _installerExecutable,
                options,
                progress));

            bool createDesktop = _desktopCheck.IsChecked == true;
            bool createStartMenu = _startMenuCheck.IsChecked == true;
            bool autoStart = _autoStartCheck.IsChecked == true;

            // Apply shortcuts and autostart after the application files are committed.
            await Task.Run(() => _installerService.ApplyOptionalSettings(
                _installedDirectory,
                createDesktop,
                createStartMenu,
                autoStart));

            _busy = false;
            ShowPanel(_successPanel);
        }
        catch (Exception exception)
        {
            _busy = false;
            ShowPanel(_customizePanel);
            ShowError(exception.Message);
        }
    }

    private void LaunchAndExit()
    {
        if (string.IsNullOrWhiteSpace(_installedDirectory))
        {
            Close();
            return;
        }

        try
        {
            string appExe = Path.Combine(_installedDirectory, InstallerPaths.ProductExecutableName);
            if (File.Exists(appExe))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = appExe,
                    WorkingDirectory = _installedDirectory,
                    UseShellExecute = true
                });
            }
            ExitCode = 0;
            Close();
        }
        catch (Exception ex)
        {
            Program.ShowNativeError(ex.Message, "启动失败");
            Close();
        }
    }

    private void ShowError(string message)
    {
        _errorText.Text = message;
        _errorText.IsVisible = true;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_busy) e.Cancel = true;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint windowHandle,
        int attribute,
        ref int value,
        int valueSize);
}
