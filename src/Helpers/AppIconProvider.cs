using System.Reflection;
using ZSnaper.Models;

namespace ZSnaper.Helpers;

public static class AppIconProvider
{
    private const string LightIconResourceName = "ZSnaper.Assets.Logo.ZSnaper.ico";
    private const string DarkIconResourceName = "ZSnaper.Assets.Logo.ZSnaper-dark.ico";

    public static Icon CreateIcon(ThemeMode mode)
    {
        string resourceName = mode == ThemeMode.Dark
            ? DarkIconResourceName
            : LightIconResourceName;
        Assembly assembly = typeof(AppIconProvider).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded app icon not found: {resourceName}");
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }
}
