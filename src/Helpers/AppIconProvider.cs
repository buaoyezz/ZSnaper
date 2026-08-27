using System.Reflection;
using ZSnaper.Models;
using ZSnaper.Services;

namespace ZSnaper.Helpers;

public static class AppIconProvider
{
    private const string LightIconResourceName = "ZSnaper.Assets.Logo.ZSnaper.ico";
    private const string DarkIconResourceName = "ZSnaper.Assets.Logo.ZSnaper-dark.ico";
    private const string LightSvgResourceName = "ZSnaper.Assets.Logo.icon-light.svg";
    private const string DarkSvgResourceName = "ZSnaper.Assets.Logo.icon-dark.svg";
    private const int MaxCustomSvgBytes = 2 * 1024 * 1024;

    /// <summary>
    /// Returns the legacy application icon used by windows and taskbar entries.
    /// Tray customization must not change these icons.
    /// </summary>
    public static Icon CreateApplicationIcon(ThemeMode themeMode)
    {
        return LoadEmbeddedIcon(themeMode == ThemeMode.Dark
            ? DarkIconResourceName
            : LightIconResourceName);
    }

    /// <summary>
    /// Returns the independently configurable notification-area icon.
    /// </summary>
    public static Icon CreateTrayIcon(ThemeMode themeMode)
    {
        if (ConfigService.Current.TrayIconStyle == TrayIconStyle.LegacyBlack)
        {
            // Keep the previous black-background tray icon and its original
            // ICO sizing. The SVG scale setting intentionally does not apply.
            return LoadEmbeddedIcon(DarkIconResourceName);
        }

        ThemeMode effectiveMode = ResolveThemeMode(themeMode);
        string? svgMarkup = TryLoadSelectedSvg(effectiveMode);
        if (svgMarkup is not null)
        {
            try
            {
                Color color = ResolveIconColor(effectiveMode);
                float scale = Math.Clamp(
                    ConfigService.Current.TrayIconScalePercent,
                    80,
                    160) / 100f;
                using Bitmap bitmap = SvgIconRenderer.Render(svgMarkup, 256, color, scale);
                return CreateIconFromBitmap(bitmap);
            }
            catch
            {
                // A malformed or inaccessible custom SVG falls back to the
                // packaged icon so changing settings never removes the tray icon.
            }
        }

        return CreateApplicationIcon(effectiveMode);
    }

    public static string GetTrayIconSettingsKey()
    {
        AppConfig config = ConfigService.Current;
        string customFileKey = string.Empty;
        if (config.TrayIconStyle == TrayIconStyle.CustomSvg &&
            !string.IsNullOrWhiteSpace(config.TrayIconSvgPath))
        {
            try
            {
                FileInfo fileInfo = new(config.TrayIconSvgPath);
                customFileKey = fileInfo.Exists
                    ? $"{fileInfo.Length}:{fileInfo.LastWriteTimeUtc.Ticks}"
                    : "missing";
            }
            catch
            {
                customFileKey = "unavailable";
            }
        }

        int scale = config.TrayIconStyle == TrayIconStyle.LegacyBlack
            ? 0
            : config.TrayIconScalePercent;
        return string.Join(
            "\u001F",
            config.TrayIconStyle,
            config.TrayIconSvgPath,
            config.TrayIconLightColorHex,
            config.TrayIconDarkColorHex,
            scale,
            customFileKey);
    }

    private static ThemeMode ResolveThemeMode(ThemeMode themeMode)
    {
        return ConfigService.Current.TrayIconStyle switch
        {
            TrayIconStyle.Light => ThemeMode.Light,
            TrayIconStyle.Dark => ThemeMode.Dark,
            _ => themeMode
        };
    }

    private static string? TryLoadSelectedSvg(ThemeMode mode)
    {
        string? markup = null;
        if (ConfigService.Current.TrayIconStyle == TrayIconStyle.CustomSvg)
        {
            string path = ConfigService.Current.TrayIconSvgPath;
            if (File.Exists(path))
            {
                FileInfo fileInfo = new(path);
                if (fileInfo.Length <= MaxCustomSvgBytes)
                {
                    markup = File.ReadAllText(path);
                }
            }
        }

        if (markup is not null)
        {
            return markup;
        }

        return LoadEmbeddedText(mode == ThemeMode.Dark
            ? DarkSvgResourceName
            : LightSvgResourceName);
    }

    private static Color ResolveIconColor(ThemeMode mode)
    {
        string value = mode == ThemeMode.Dark
            ? ConfigService.Current.TrayIconDarkColorHex
            : ConfigService.Current.TrayIconLightColorHex;
        try
        {
            return ColorTranslator.FromHtml(value);
        }
        catch
        {
            return mode == ThemeMode.Dark
                ? Color.White
                : Color.FromArgb(56, 60, 64);
        }
    }

    private static string? LoadEmbeddedText(string resourceName)
    {
        Assembly assembly = typeof(AppIconProvider).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static Icon LoadEmbeddedIcon(string resourceName)
    {
        Assembly assembly = typeof(AppIconProvider).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded app icon not found: {resourceName}");
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }

    private static Icon CreateIconFromBitmap(Bitmap bitmap)
    {
        nint handle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(nint handle);
}
