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
}

public static class ConfigService
{
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
            }
        }
        catch
        {
            Current = new();
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
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
            return key?.GetValue("ZSnaper") != null;
        }
        catch
        {
            return false;
        }
    }

    public static void SetAutoStart(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key != null)
            {
                if (enable)
                {
                    var exePath = Environment.ProcessPath ?? Application.ExecutablePath;
                    key.SetValue("ZSnaper", $"\"{exePath}\"");
                }
                else
                {
                    key.DeleteValue("ZSnaper", false);
                }
            }
            Current.AutoStartOnBoot = enable;
            Save();
        }
        catch
        {
            // Ignore registry write error
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
