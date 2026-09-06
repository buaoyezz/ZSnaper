using ZSnaper.Services;

namespace ZSnaper.Models;

public sealed record HotkeyCommandDefinition(
    HotkeyCommand Command,
    string Name,
    string Description,
    string Keywords);

public static class HotkeyCommandCatalog
{
    public static IReadOnlyList<HotkeyCommandDefinition> Definitions { get; } =
    [
        new(HotkeyCommand.Capture, "区域截图", "截取选区并按当前设置保存或复制", "截图 截屏 选区 capture snip"),
        new(HotkeyCommand.Ocr, "截图并 OCR", "截取选区并调用本地文字识别", "读取文字 识别文字 ocr text"),
        new(HotkeyCommand.CaptureAndPin, "截图并贴图", "截取选区后立即作为置顶贴图", "贴图 钉住 pin screenshot"),
        new(HotkeyCommand.CaptureCurrentScreen, "当前屏幕截图", "立即截取鼠标所在的整块屏幕", "全屏 显示器 monitor screen"),
        new(HotkeyCommand.PinClipboardImage, "贴剪贴板图片", "把剪贴板中的图片直接置顶显示", "剪贴板 粘贴图片 clipboard paste pin"),
        new(HotkeyCommand.OpenMainWindow, "打开 ZSnaper", "显示并聚焦应用主界面", "主界面 工作台 app window show"),
        new(HotkeyCommand.OpenSaveFolder, "打开截图目录", "在资源管理器中打开截图保存位置", "文件夹 保存目录 folder explorer"),
        new(HotkeyCommand.ToggleTheme, "切换明暗主题", "在浅色与深色主题之间快速切换", "主题 夜间 深色 浅色 theme dark light")
    ];

    public static HotkeyCommandDefinition GetDefinition(HotkeyCommand command) =>
        Definitions.First(definition => definition.Command == command);

    public static string GetConfigText(AppConfig config, HotkeyCommand command) => command switch
    {
        HotkeyCommand.Capture => config.CaptureHotkey,
        HotkeyCommand.Ocr => config.OcrHotkey,
        HotkeyCommand.CaptureAndPin => config.CaptureAndPinHotkey,
        HotkeyCommand.CaptureCurrentScreen => config.CaptureCurrentScreenHotkey,
        HotkeyCommand.PinClipboardImage => config.PinClipboardImageHotkey,
        HotkeyCommand.OpenMainWindow => config.OpenMainWindowHotkey,
        HotkeyCommand.OpenSaveFolder => config.OpenSaveFolderHotkey,
        HotkeyCommand.ToggleTheme => config.ToggleThemeHotkey,
        _ => string.Empty
    };

    public static bool GetForceBinding(AppConfig config, HotkeyCommand command) => command switch
    {
        HotkeyCommand.Capture => config.CaptureHotkeyForceBinding,
        HotkeyCommand.Ocr => config.OcrHotkeyForceBinding,
        HotkeyCommand.CaptureAndPin => config.CaptureAndPinHotkeyForceBinding,
        HotkeyCommand.CaptureCurrentScreen => config.CaptureCurrentScreenHotkeyForceBinding,
        HotkeyCommand.PinClipboardImage => config.PinClipboardImageHotkeyForceBinding,
        HotkeyCommand.OpenMainWindow => config.OpenMainWindowHotkeyForceBinding,
        HotkeyCommand.OpenSaveFolder => config.OpenSaveFolderHotkeyForceBinding,
        HotkeyCommand.ToggleTheme => config.ToggleThemeHotkeyForceBinding,
        _ => false
    };

    public static void SetConfig(AppConfig config, HotkeyCommand command, string value, bool forceBinding)
    {
        value ??= string.Empty;
        switch (command)
        {
            case HotkeyCommand.Capture:
                config.CaptureHotkey = value;
                config.CaptureHotkeyForceBinding = forceBinding;
                break;
            case HotkeyCommand.Ocr:
                config.OcrHotkey = value;
                config.OcrHotkeyForceBinding = forceBinding;
                break;
            case HotkeyCommand.CaptureAndPin:
                config.CaptureAndPinHotkey = value;
                config.CaptureAndPinHotkeyForceBinding = forceBinding;
                break;
            case HotkeyCommand.CaptureCurrentScreen:
                config.CaptureCurrentScreenHotkey = value;
                config.CaptureCurrentScreenHotkeyForceBinding = forceBinding;
                break;
            case HotkeyCommand.PinClipboardImage:
                config.PinClipboardImageHotkey = value;
                config.PinClipboardImageHotkeyForceBinding = forceBinding;
                break;
            case HotkeyCommand.OpenMainWindow:
                config.OpenMainWindowHotkey = value;
                config.OpenMainWindowHotkeyForceBinding = forceBinding;
                break;
            case HotkeyCommand.OpenSaveFolder:
                config.OpenSaveFolderHotkey = value;
                config.OpenSaveFolderHotkeyForceBinding = forceBinding;
                break;
            case HotkeyCommand.ToggleTheme:
                config.ToggleThemeHotkey = value;
                config.ToggleThemeHotkeyForceBinding = forceBinding;
                break;
        }
    }

    public static bool Matches(HotkeyCommandDefinition definition, string? query, string? shortcutText = null)
    {
        string normalized = query?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            return true;
        }

        if (definition.Name.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
            definition.Description.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
            definition.Keywords.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
            (shortcutText?.Contains(normalized, StringComparison.OrdinalIgnoreCase) ?? false))
        {
            return true;
        }

        string[] tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length <= 1)
        {
            return false;
        }

        return tokens.All(token =>
            definition.Name.Contains(token, StringComparison.OrdinalIgnoreCase) ||
            definition.Description.Contains(token, StringComparison.OrdinalIgnoreCase) ||
            definition.Keywords.Contains(token, StringComparison.OrdinalIgnoreCase) ||
            (shortcutText?.Contains(token, StringComparison.OrdinalIgnoreCase) ?? false));
    }
}
