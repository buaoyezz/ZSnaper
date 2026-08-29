using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ZSnaper.FullInstaller.Views;

public sealed partial class AgreementPage : UserControl
{
    private readonly CheckBox _agreement;
    private readonly Button _next;

    public AgreementPage()
    {
        AvaloniaXamlLoader.Load(this);
        _agreement = this.FindControl<CheckBox>("AgreementCheck")!;
        _next = this.FindControl<Button>("NextButton")!;
        _agreement.IsCheckedChanged += (_, _) => _next.IsEnabled = _agreement.IsChecked == true;
        this.FindControl<Button>("BackButton")!.Click += (_, _) => BackRequested?.Invoke(this, EventArgs.Empty);
        _next.Click += (_, _) => NextRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? BackRequested;
    public event EventHandler? NextRequested;
}
