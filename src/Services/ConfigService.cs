using Microsoft.Win32;
using System.Text.Json;
using ZSnaper.Models;

namespace ZSnaper.Services;

public enum AnimationLevel
{
    Fast,      // 精简：快速利落 (~100ms)
    Balanced,  // 默认：优雅均衡 (~200ms)
    Elegant    // 极度优雅：丝滑流体 (~320ms)
}

public class AppConfig
{
    public ThemeMode Theme { get; set; } = ThemeMode.Light;
    public AnimationLevel AnimationMode { get; set; } = AnimationLevel.Balanced;
    public bool EnableGlowEffect { get; set; } = true;
    public bool EnableBackgroundGlow { get => EnableGlowEffect; set => EnableGlowEffect = value; }
    public string AccentColorHex { get; set; } = "#10B981"; // 翡翠绿
    public bool AutoCopyClipboard { get; set; } = true;
    public TrayIconStyle TrayIconStyle { get; set; } = TrayIconStyle.FollowTheme;
    public string TrayIconSvgPath { get; set; } = string.Empty;
    public string TrayIconLightColorHex { get; set; } = "#383C40";
    public string TrayIconDarkColorHex { get; set; } = "#FFFFFF";
    public int TrayIconScalePercent { get; set; } = 128;
    public List<string> TrayIconCustomPalette { get; set; } =
        ["#383C40", "#FFFFFF", "#10B981", "#0EA5E9", "#8B5CF6", "#F97316", "#EF4444", "#F59E0B"];
    public bool AutoSavePictures { get; set; } = true;
    public bool AutoCleanOcrParagraphs { get; set; } = true;
    public bool ShowNotification { get; set; } = true;
    public ToolbarPlacementMode ToolbarPlacement { get; set; } = ToolbarPlacementMode.Auto;
    public double ToolbarAutoHorizontalBias { get; set; } = 0.78d;
    public int ToolbarAutoSampleCount { get; set; }
    public List<CaptureToolbarItem> CaptureToolbarItems { get; set; } = CaptureToolbarDefaults.CreateItems();
    public List<CaptureToolbarItem> CaptureToolbarOrder { get; set; } = CaptureToolbarDefaults.CreateItems();
    public CaptureToolbarLayout CaptureToolbarLayout { get; set; } = CaptureToolbarLayout.Full;
    public ConfirmButtonBehavior ConfirmButtonBehavior { get; set; } = ConfirmButtonBehavior.Copy;
    public AnnotationToolBehavior AnnotationToolBehavior { get; set; } = AnnotationToolBehavior.Sticky;
    public string AnnotationColorHex { get; set; } = "#FF3B30";
    public string AnnotationFontFamily { get; set; } = "Microsoft YaHei UI";
    public float AnnotationFontSize { get; set; } = 18f;
    public int AnnotationFontStyle { get; set; } = (int)FontStyle.Regular;
    public float AnnotationPenWidth { get; set; } = 4f;
    public float AnnotationMosaicSize { get; set; } = 24f;
    public int AnnotationMosaicPixelSize { get; set; } = 10;
    public AnnotationArrowStyle AnnotationArrowStyle { get; set; } = AnnotationArrowStyle.Open;
    public string CustomSavePath { get; set; } = string.Empty;
    public bool AutoStartOnBoot { get; set; } = false;
    public string CaptureHotkey { get; set; } = "Alt+Q";
    public string OcrHotkey { get; set; } = "Alt+X";
    public bool CaptureHotkeyForceBinding { get; set; }
    public bool OcrHotkeyForceBinding { get; set; }
    public string UpdateChannel { get; set; } = "Alpha";
    public bool AutoCheckUpdates { get; set; } = true;
    public int UpdateCheckIntervalHours { get; set; } = 24;
    public DateTimeOffset? LastUpdateCheckAt { get; set; }
}

public static class ConfigService
{
    private const string StartupRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupValueName = "ZSnaper";
    private const string StartupArgument = "--startup";
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ZSnaper",
        "config.json");

    public static AppConfig Current { get; private set; } = new();

    public static event Action? ConfigChanged;

    static ConfigService()
    {
        Load();
    }

    public static void Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                Current = JsonSerializer.Deserialize<AppConfig>(json) ?? new();
                NormalizeCaptureToolbar();
                NormalizeUpdateSettings();
                NormalizeTrayIconSettings();
            }
        }
        catch
        {
            Current = new();
        }

        // 注册表是开机启动的真实来源，配置文件只负责保存 UI 状态。
        Current.AutoStartOnBoot = IsAutoStartEnabled();
    }

    private static void NormalizeCaptureToolbar()
    {
        Current.CaptureToolbarItems ??= [];
        Current.CaptureToolbarOrder ??= [];

        if (Current.CaptureToolbarLayout != CaptureToolbarLayout.Custom)
        {
            Current.CaptureToolbarItems = CaptureToolbarDefaults.CreateLayout(Current.CaptureToolbarLayout);
            Current.CaptureToolbarOrder = CaptureToolbarDefaults.CreateItems();
            return;
        }

        if (!Current.CaptureToolbarOrder.Contains(CaptureToolbarItem.ScrollCapture))
        {
            int insertAt = Current.CaptureToolbarOrder.IndexOf(CaptureToolbarItem.Ocr);
            if (insertAt < 0) insertAt = Current.CaptureToolbarOrder.Count;
            Current.CaptureToolbarOrder.Insert(insertAt, CaptureToolbarItem.ScrollCapture);
        }
    }

    private static void NormalizeUpdateSettings()
    {
        if (Current.UpdateCheckIntervalHours is not (6 or 12 or 24 or 168))
        {
            Current.UpdateCheckIntervalHours = 24;
        }
    }

    private static void NormalizeTrayIconSettings()
    {
        if (!Enum.IsDefined(Current.TrayIconStyle))
        {
            Current.TrayIconStyle = TrayIconStyle.FollowTheme;
        }

        Current.TrayIconCustomPalette ??= [];
        Current.TrayIconCustomPalette = Current.TrayIconCustomPalette
            .Where(IsValidHexColor)
            .Take(16)
            .ToList();

        if (Current.TrayIconCustomPalette.Count == 0)
        {
            Current.TrayIconCustomPalette = new AppConfig().TrayIconCustomPalette;
        }

        if (!IsValidHexColor(Current.TrayIconLightColorHex))
        {
            Current.TrayIconLightColorHex = "#383C40";
        }

        if (!IsValidHexColor(Current.TrayIconDarkColorHex))
        {
            Current.TrayIconDarkColorHex = "#FFFFFF";
        }

        Current.TrayIconScalePercent = Math.Clamp(Current.TrayIconScalePercent, 80, 160);
    }

    private static bool IsValidHexColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        try
        {
            _ = ColorTranslator.FromHtml(value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
            ConfigChanged?.Invoke();
        }
        catch
        {
            // Ignore write errors
        }
    }

    public static string GetEffectiveSavePath()
    {
        if (!string.IsNullOrWhiteSpace(Current.CustomSavePath) && Directory.Exists(Current.CustomSavePath))
        {
            return Current.CustomSavePath;
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "ZSnaper");
    }

    public static bool IsAutoStartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryPath, false);
            return key?.GetValue(StartupValueName) is string command &&
                   !string.IsNullOrWhiteSpace(command);
        }
        catch
        {
            return false;
        }
    }

    public static bool SetAutoStart(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(StartupRegistryPath, writable: true);
            if (key is null)
            {
                return false;
            }

            if (enable)
            {
                string exePath = Application.ExecutablePath;
                key.SetValue(StartupValueName, $"\"{exePath}\" {StartupArgument}");
            }
            else
            {
                key.DeleteValue(StartupValueName, false);
            }

            Current.AutoStartOnBoot = enable;
            Save();
            return IsAutoStartEnabled() == enable;
        }
        catch
        {
            return false;
        }
    }

    public static void ResetToDefaults()
    {
        Current = new AppConfig();
        Save();
    }

    public static int GetAnimationDuration(int baseDurationMs = 200)
    {
        return Current.AnimationMode switch
        {
            AnimationLevel.Fast => (int)(baseDurationMs * 0.55),
            AnimationLevel.Balanced => baseDurationMs,
            AnimationLevel.Elegant => (int)(baseDurationMs * 1.6),
            _ => baseDurationMs
        };
    }
}
