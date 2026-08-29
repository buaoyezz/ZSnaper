using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ZSnaper.FullInstaller.Views;

public sealed record CompletionOptions(bool DesktopShortcut, bool StartMenuShortcut, bool AutoStart);

public sealed partial class CompletionPage : UserControl
{
    private readonly CheckBox _desktopShortcut;
    private readonly CheckBox _startMenuShortcut;
    private readonly CheckBox _autoStart;
    private readonly Button _finishButton;

    public CompletionPage()
    {
        AvaloniaXamlLoader.Load(this);
        _desktopShortcut = this.FindControl<CheckBox>("DesktopShortcutCheck")!;
        _startMenuShortcut = this.FindControl<CheckBox>("StartMenuShortcutCheck")!;
        _autoStart = this.FindControl<CheckBox>("AutoStartCheck")!;
        _finishButton = this.FindControl<Button>("FinishButton")!;
        _finishButton.Click += (_, _) => FinishRequested?.Invoke(
            this,
            new CompletionOptions(
                _desktopShortcut.IsChecked == true,
                _startMenuShortcut.IsChecked == true,
                _autoStart.IsChecked == true));
    }

    public event EventHandler<CompletionOptions>? FinishRequested;

    public void SetBusy(bool busy) => _finishButton.IsEnabled = !busy;
}
