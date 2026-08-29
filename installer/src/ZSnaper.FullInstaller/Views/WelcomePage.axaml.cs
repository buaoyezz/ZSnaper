using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ZSnaper.FullInstaller.Views;

public sealed partial class WelcomePage : UserControl
{
    public WelcomePage()
    {
        AvaloniaXamlLoader.Load(this);
        this.FindControl<Button>("NextButton")!.Click += (_, _) => NextRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? NextRequested;
}
