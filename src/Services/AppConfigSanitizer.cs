using ZSnaper.Models;

namespace ZSnaper.Services;

internal static class AppConfigSanitizer
{
    private static readonly HotkeyGesture DefaultCaptureHotkey = new(Keys.Q, Keys.Alt);
    private static readonly HotkeyGesture DefaultOcrHotkey = new(Keys.X, Keys.Alt);

    public static void Normalize(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (!Enum.IsDefined(config.Theme)) config.Theme = ThemeMode.Light;
        if (!Enum.IsDefined(config.AnimationMode)) config.AnimationMode = AnimationLevel.Balanced;
        if (!Enum.IsDefined(config.ToolbarPlacement)) config.ToolbarPlacement = ToolbarPlacementMode.Auto;
        if (!Enum.IsDefined(config.CaptureToolbarLayout)) config.CaptureToolbarLayout = CaptureToolbarLayout.Full;
        if (!Enum.IsDefined(config.ConfirmButtonBehavior)) config.ConfirmButtonBehavior = ConfirmButtonBehavior.Copy;
        if (!Enum.IsDefined(config.AnnotationToolBehavior)) config.AnnotationToolBehavior = AnnotationToolBehavior.Sticky;
        if (!Enum.IsDefined(config.AnnotationArrowStyle)) config.AnnotationArrowStyle = AnnotationArrowStyle.Open;
        if (!Enum.IsDefined(config.TrayIconStyle)) config.TrayIconStyle = TrayIconStyle.FollowTheme;

        config.AccentColorHex = NormalizeColor(config.AccentColorHex, "#10B981");
        config.AnnotationColorHex = NormalizeColor(config.AnnotationColorHex, "#FF3B30");
        config.TrayIconLightColorHex = NormalizeColor(config.TrayIconLightColorHex, "#383C40");
        config.TrayIconDarkColorHex = NormalizeColor(config.TrayIconDarkColorHex, "#FFFFFF");

        config.ToolbarAutoHorizontalBias = double.IsFinite(config.ToolbarAutoHorizontalBias)
            ? Math.Clamp(config.ToolbarAutoHorizontalBias, 0d, 1d)
            : 0.78d;
        config.ToolbarAutoSampleCount = Math.Clamp(config.ToolbarAutoSampleCount, 0, 10_000);
        config.AnnotationFontSize = float.IsFinite(config.AnnotationFontSize)
            ? Math.Clamp(config.AnnotationFontSize, 8f, 72f)
            : 18f;
        config.AnnotationPenWidth = float.IsFinite(config.AnnotationPenWidth)
            ? Math.Clamp(config.AnnotationPenWidth, 1f, 48f)
            : 4f;
        config.AnnotationMosaicSize = float.IsFinite(config.AnnotationMosaicSize)
            ? Math.Clamp(config.AnnotationMosaicSize, 8f, 80f)
            : 24f;
        config.AnnotationMosaicPixelSize = Math.Clamp(config.AnnotationMosaicPixelSize, 4, 32);
        config.TrayIconScalePercent = Math.Clamp(config.TrayIconScalePercent, 80, 160);

        const FontStyle allowedFontStyles = FontStyle.Bold | FontStyle.Italic | FontStyle.Underline | FontStyle.Strikeout;
        if ((config.AnnotationFontStyle & ~(int)allowedFontStyles) != 0)
        {
            config.AnnotationFontStyle = (int)FontStyle.Regular;
        }

        config.AnnotationFontFamily = NormalizeText(config.AnnotationFontFamily, "Microsoft YaHei UI", 128);
        config.CustomSavePath = NormalizeText(config.CustomSavePath, string.Empty, 32_767);
        config.TrayIconSvgPath = NormalizeText(config.TrayIconSvgPath, string.Empty, 32_767);
        config.UpdateChannel = NormalizeUpdateChannel(config.UpdateChannel);
        config.UpdateCheckIntervalHours = config.UpdateCheckIntervalHours is 6 or 12 or 24 or 168
            ? config.UpdateCheckIntervalHours
            : 24;
        if (config.LastUpdateCheckAt is { } lastCheck && lastCheck > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            config.LastUpdateCheckAt = null;
        }

        NormalizeHotkeys(config);
        NormalizeToolbar(config);
        NormalizeTrayPalette(config);
    }

    private static void NormalizeHotkeys(AppConfig config)
    {
        bool captureValid = HotkeyGesture.TryParse(
            config.CaptureHotkey,
            out HotkeyGesture capture,
            config.CaptureHotkeyForceBinding);
        bool ocrValid = HotkeyGesture.TryParse(
            config.OcrHotkey,
            out HotkeyGesture ocr,
            config.OcrHotkeyForceBinding);
        if (!captureValid)
        {
            capture = DefaultCaptureHotkey;
            config.CaptureHotkeyForceBinding = false;
        }

        if (!ocrValid || ocr == capture)
        {
            ocr = capture == DefaultOcrHotkey ? DefaultCaptureHotkey : DefaultOcrHotkey;
            config.OcrHotkeyForceBinding = false;
        }

        config.CaptureHotkey = capture.ConfigText;
        config.OcrHotkey = ocr.ConfigText;
    }

    private static void NormalizeToolbar(AppConfig config)
    {
        List<CaptureToolbarItem> defaults = CaptureToolbarDefaults.CreateItems();
        config.CaptureToolbarOrder = (config.CaptureToolbarOrder ?? [])
            .Where(Enum.IsDefined)
            .Distinct()
            .ToList();
        foreach (CaptureToolbarItem item in defaults)
        {
            if (!config.CaptureToolbarOrder.Contains(item)) config.CaptureToolbarOrder.Add(item);
        }

        if (config.CaptureToolbarLayout != CaptureToolbarLayout.Custom)
        {
            config.CaptureToolbarItems = CaptureToolbarDefaults.CreateLayout(config.CaptureToolbarLayout);
            return;
        }

        HashSet<CaptureToolbarItem> selected = (config.CaptureToolbarItems ?? [])
            .Where(Enum.IsDefined)
            .ToHashSet();
        config.CaptureToolbarItems = config.CaptureToolbarOrder
            .Where(selected.Contains)
            .ToList();
        if (config.CaptureToolbarItems.Count == 0)
        {
            config.CaptureToolbarItems.Add(CaptureToolbarItem.Confirm);
        }
    }

    private static void NormalizeTrayPalette(AppConfig config)
    {
        config.TrayIconCustomPalette = (config.TrayIconCustomPalette ?? [])
            .Select(value => NormalizeColor(value, string.Empty))
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(16)
            .ToList();
        if (config.TrayIconCustomPalette.Count == 0)
        {
            config.TrayIconCustomPalette = new AppConfig().TrayIconCustomPalette;
        }
    }

    private static string NormalizeColor(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        string candidate = value.Trim();
        if (candidate.Length != 7 || candidate[0] != '#' ||
            !candidate.AsSpan(1).ToString().All(Uri.IsHexDigit))
        {
            return fallback;
        }

        return candidate.ToUpperInvariant();
    }

    private static string NormalizeText(string? value, string fallback, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        string normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : fallback;
    }

    private static string NormalizeUpdateChannel(string? value)
    {
        if (string.Equals(value, "Release", StringComparison.OrdinalIgnoreCase)) return "Release";
        if (string.Equals(value, "Beta", StringComparison.OrdinalIgnoreCase)) return "Beta";
        return "Alpha";
    }
}
