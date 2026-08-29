namespace ZSnaper.Plugins;

/// <summary>
/// Plugin storage locations. Directory creation is intentionally explicit so
/// the disabled plugin feature never changes the user's filesystem by itself.
/// </summary>
public static class PluginStorage
{
    public static string RootDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ZSnaper",
        "Plugins");

    public static string InstalledDirectory => Path.Combine(RootDirectory, "installed");

    public static string StagingDirectory => Path.Combine(RootDirectory, "staging");

    public static string QuarantineDirectory => Path.Combine(RootDirectory, "quarantine");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(InstalledDirectory);
        Directory.CreateDirectory(StagingDirectory);
        Directory.CreateDirectory(QuarantineDirectory);
    }
}
