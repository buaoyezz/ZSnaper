using System.Reflection;
using ZSnaper.Services;

namespace ZSnaper.Helpers;

public static class AppVersionInfo
{
    public const string Version = "0.0.2";

    // User preference: which update channel should be checked.
    public static string Channel => ConfigService.Current.UpdateChannel;

    // Actual channel encoded in this build. It must not depend on the update preference.
    public static string BuildChannel => ResolveBuildChannel();

    public static bool IsReleaseBuild =>
        string.Equals(BuildChannel, "Release", StringComparison.OrdinalIgnoreCase);

    public static string? WelcomeChannelLabel => BuildChannel switch
    {
        "Alpha" => "ALPHA",
        "Beta" => "BETA",
        _ => null
    };

    public const string BuildNumber = "20260828.1";
    public const string BuildDate = "2026-08-28";
    public const int BuildCount = 1;

    public static bool ShowChannel => !IsReleaseBuild;

    public static string DisplayVersion => Version;

    private static string ResolveBuildChannel()
    {
        Assembly assembly = typeof(AppVersionInfo).Assembly;
        string informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?? Version;

        string versionWithoutMetadata = informationalVersion.Split('+', 2)[0];
        int separatorIndex = versionWithoutMetadata.IndexOf('-');
        if (separatorIndex < 0)
        {
            return "Release";
        }

        string prereleaseName = versionWithoutMetadata[(separatorIndex + 1)..]
            .Split('.', 2)[0]
            .Trim();

        return prereleaseName.Equals("alpha", StringComparison.OrdinalIgnoreCase)
            ? "Alpha"
            : prereleaseName.Equals("beta", StringComparison.OrdinalIgnoreCase)
                ? "Beta"
                : "Release";
    }
}
