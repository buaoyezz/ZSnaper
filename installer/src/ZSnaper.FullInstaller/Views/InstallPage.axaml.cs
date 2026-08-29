using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using ZSnaper.Installer.Core;

namespace ZSnaper.FullInstaller.Views;

public sealed partial class InstallPage : UserControl
{
    private readonly TextBox _pathBox;
    private readonly Button _browseButton;
    private readonly Button _backButton;
    private readonly Button _installButton;
    private readonly StackPanel _progressArea;
    private readonly ProgressBar _progress;
    private readonly TextBlock _status;
    private readonly TextBlock _percent;
    private readonly Border _errorBanner;
    private readonly TextBlock _errorText;

    public InstallPage() : this(InstallerPaths.DefaultInstallDirectory)
    {
    }

    public InstallPage(string installDirectory)
    {
        AvaloniaXamlLoader.Load(this);
        _pathBox = this.FindControl<TextBox>("InstallPathBox")!;
        _browseButton = this.FindControl<Button>("BrowseButton")!;
        _backButton = this.FindControl<Button>("BackButton")!;
        _installButton = this.FindControl<Button>("InstallButton")!;
        _progressArea = this.FindControl<StackPanel>("ProgressArea")!;
        _progress = this.FindControl<ProgressBar>("InstallProgress")!;
        _status = this.FindControl<TextBlock>("ProgressStatus")!;
        _percent = this.FindControl<TextBlock>("ProgressPercent")!;
        _errorBanner = this.FindControl<Border>("ErrorBanner")!;
        _errorText = this.FindControl<TextBlock>("ErrorText")!;

        _pathBox.Text = installDirectory;
        _browseButton.Click += BrowseAsync;
        _backButton.Click += (_, _) => BackRequested?.Invoke(this, EventArgs.Empty);
        _installButton.Click += (_, _) => InstallRequested?.Invoke(this, _pathBox.Text ?? string.Empty);
    }

    public event EventHandler? BackRequested;
    public event EventHandler<string>? InstallRequested;

    public void SetBusy(bool busy)
    {
        _pathBox.IsEnabled = !busy;
        _browseButton.IsEnabled = !busy;
        _backButton.IsEnabled = !busy;
        _installButton.IsEnabled = !busy;
        _progressArea.IsVisible = busy;
        if (busy)
        {
            _errorBanner.IsVisible = false;
            ReportProgress(new InstallProgress("正在准备安装…", 0, 1));
        }
    }

    public void ReportProgress(InstallProgress progress)
    {
        int value = progress.Total <= 0 ? 0 : Math.Clamp(progress.Completed * 100 / progress.Total, 0, 100);
        _progress.Value = value;
        _percent.Text = $"{value}%";
        _status.Text = progress.Stage switch
        {
            "Copying application files" => "正在写入程序文件…",
            _ => progress.Stage
        };
    }

    public void ShowError(string message)
    {
        _errorText.Text = message;
        _errorBanner.IsVisible = true;
    }

    private async void BrowseAsync(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            IStorageProvider? storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storageProvider is null)
            {
                return;
            }

            IReadOnlyList<IStorageFolder> folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "选择 ZSnaper 安装目录",
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
                    _pathBox.Text = path;
                }
            }
        }
        catch
        {
            // Ignore folder picker error
        }
    }
}
