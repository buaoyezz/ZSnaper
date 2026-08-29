using Microsoft.Win32;

namespace ZSnaper.Installer.Core;

public sealed class InstallerService
{
    public void UpdateInstalledVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException("Install version cannot be empty.", nameof(version));
        }

        using RegistryKey key = InstallerPaths.OpenInstallerKey(writable: true)
            ?? throw new InvalidOperationException("ZSnaper installation metadata was not found.");
        key.SetValue("Version", version, RegistryValueKind.String);

        using RegistryKey? uninstall = Registry.CurrentUser.OpenSubKey(
            InstallerPaths.UninstallRegistryPath,
            writable: true);
        uninstall?.SetValue("DisplayVersion", version, RegistryValueKind.String);
    }

    public InstallationInfo? GetInstalled()
    {
        using RegistryKey? key = InstallerPaths.OpenInstallerKey(writable: false);
        string? installDirectory = key?.GetValue("InstallPath") as string;
        if (string.IsNullOrWhiteSpace(installDirectory) ||
            !InstallerPaths.IsUsableInstallDirectory(installDirectory, out _))
        {
            return null;
        }

        string normalizedDirectory = InstallerPaths.Normalize(installDirectory);
        if (!File.Exists(Path.Combine(normalizedDirectory, InstallerPaths.ProductExecutableName)))
        {
            return null;
        }

        return new InstallationInfo(
            normalizedDirectory,
            key?.GetValue("Version") as string ?? string.Empty,
            Path.Combine(normalizedDirectory, InstallerPaths.ProductExecutableName),
            Path.Combine(normalizedDirectory, InstallerPaths.SetupExecutableName));
    }

    public void ApplyOptionalSettings(
        string installDirectory,
        bool createDesktopShortcut,
        bool createStartMenuShortcut,
        bool enableAutoStart)
    {
        string normalizedDirectory = InstallerPaths.Normalize(installDirectory);
        if (!IsRegisteredInstall(normalizedDirectory) ||
            !File.Exists(Path.Combine(normalizedDirectory, InstallerPaths.ProductExecutableName)))
        {
            throw new InvalidOperationException("The target directory is not a registered ZSnaper installation.");
        }

        ConfigureAutoStart(normalizedDirectory, enableAutoStart);
        ConfigureShortcuts(
            normalizedDirectory,
            new InstallOptions(
                normalizedDirectory,
                GetInstalled()?.Version ?? string.Empty,
                createDesktopShortcut,
                createStartMenuShortcut,
                enableAutoStart));
    }

    public void Install(
        string payloadDirectory,
        string installerExecutable,
        InstallOptions options,
        IProgress<InstallProgress>? progress = null)
    {
        if (!Directory.Exists(payloadDirectory) ||
            !File.Exists(Path.Combine(payloadDirectory, InstallerPaths.ProductExecutableName)))
        {
            throw new DirectoryNotFoundException("The payload does not contain ZSnaper.exe.");
        }

        if (!InstallerPaths.IsUsableInstallDirectory(options.InstallDirectory, out string error))
        {
            throw new ArgumentException(error, nameof(options));
        }

        string installDirectory = InstallerPaths.Normalize(options.InstallDirectory);
        ProcessGuard.EnsureClosed(installDirectory);

        string stagingDirectory = installDirectory + ".staging-" + Guid.NewGuid().ToString("N");
        string backupDirectory = installDirectory + ".backup-" + Guid.NewGuid().ToString("N");
        Dictionary<string, string?> backups = new(StringComparer.OrdinalIgnoreCase);
        bool createdInstallDirectory = false;
        try
        {
            Directory.CreateDirectory(stagingDirectory);
            CopyDirectory(payloadDirectory, stagingDirectory, progress);

            string stagedSetup = Path.Combine(stagingDirectory, InstallerPaths.SetupExecutableName);
            File.Copy(installerExecutable, stagedSetup, overwrite: true);

            if (!Directory.Exists(installDirectory))
            {
                Directory.Move(stagingDirectory, installDirectory);
                createdInstallDirectory = true;
            }
            else
            {
                Directory.CreateDirectory(backupDirectory);
                string? runningInstaller = InstallerPaths.Normalize(installerExecutable);
                CopyDirectory(stagingDirectory, installDirectory, progress, runningInstaller, backupDirectory, backups);
                Directory.Delete(stagingDirectory, recursive: true);
            }

            WriteInstallMetadata(installDirectory, options.Version);
            if (options.ApplyOptionalSettings)
            {
                ConfigureAutoStart(installDirectory, options.EnableAutoStart);
                ConfigureShortcuts(installDirectory, options);
            }
            PayloadArchive.TryDeleteDirectory(backupDirectory);
        }
        catch
        {
            if (createdInstallDirectory)
            {
                PayloadArchive.TryDeleteDirectory(installDirectory);
            }
            else
            {
                RollbackFiles(backups);
            }

            PayloadArchive.TryDeleteDirectory(stagingDirectory);
            PayloadArchive.TryDeleteDirectory(backupDirectory);
            throw;
        }
    }

    public void Uninstall(string installDirectory)
    {
        string normalizedDirectory = InstallerPaths.Normalize(installDirectory);
        if (!string.Equals(
                normalizedDirectory,
                InstallerPaths.Normalize(InstallerPaths.DefaultInstallDirectory),
                StringComparison.OrdinalIgnoreCase) &&
            !IsRegisteredInstall(normalizedDirectory))
        {
            throw new InvalidOperationException("The target directory is not a registered ZSnaper installation.");
        }

        ProcessGuard.EnsureClosed(normalizedDirectory);
        RemoveShortcuts(normalizedDirectory);
        RemoveAutoStart(normalizedDirectory);

        Directory.Delete(normalizedDirectory, recursive: true);
        Registry.CurrentUser.DeleteSubKeyTree(InstallerPaths.InstallerRegistryPath, throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(InstallerPaths.UninstallRegistryPath, throwOnMissingSubKey: false);
    }

    private static void WriteInstallMetadata(string installDirectory, string version)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(InstallerPaths.InstallerRegistryPath, writable: true)
            ?? throw new InvalidOperationException("Unable to write installation metadata.");
        key.SetValue("InstallPath", installDirectory, RegistryValueKind.String);
        key.SetValue("Version", version, RegistryValueKind.String);

        using RegistryKey uninstall = Registry.CurrentUser.CreateSubKey(InstallerPaths.UninstallRegistryPath, writable: true)
            ?? throw new InvalidOperationException("Unable to write uninstall metadata.");
        string setupPath = Path.Combine(installDirectory, InstallerPaths.SetupExecutableName);
        uninstall.SetValue("DisplayName", InstallerPaths.ProductName, RegistryValueKind.String);
        uninstall.SetValue("DisplayVersion", version, RegistryValueKind.String);
        uninstall.SetValue("Publisher", "ZZBuAoYe", RegistryValueKind.String);
        uninstall.SetValue("InstallLocation", installDirectory, RegistryValueKind.String);
        uninstall.SetValue("DisplayIcon", Path.Combine(installDirectory, InstallerPaths.ProductExecutableName), RegistryValueKind.String);
        uninstall.SetValue("UninstallString", $"\"{setupPath}\" --uninstall", RegistryValueKind.String);
        uninstall.SetValue("NoModify", 1, RegistryValueKind.DWord);
        uninstall.SetValue("NoRepair", 0, RegistryValueKind.DWord);
    }

    private static void ConfigureAutoStart(string installDirectory, bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(InstallerPaths.StartupRegistryPath, writable: true)
            ?? throw new InvalidOperationException("Unable to write startup settings.");
        string executablePath = Path.Combine(installDirectory, InstallerPaths.ProductExecutableName);
        if (enabled)
        {
            key.SetValue(InstallerPaths.StartupValueName, $"\"{executablePath}\" --startup", RegistryValueKind.String);
        }
        else
        {
            RemoveAutoStart(installDirectory);
        }
    }

    private static void RemoveAutoStart(string installDirectory)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(InstallerPaths.StartupRegistryPath, writable: true);
        string? value = key?.GetValue(InstallerPaths.StartupValueName) as string;
        string expectedExecutable = Path.Combine(InstallerPaths.Normalize(installDirectory), InstallerPaths.ProductExecutableName);
        if (value is not null && value.Contains(expectedExecutable, StringComparison.OrdinalIgnoreCase))
        {
            key?.DeleteValue(InstallerPaths.StartupValueName, throwOnMissingValue: false);
        }
    }

    private static void ConfigureShortcuts(string installDirectory, InstallOptions options)
    {
        string executablePath = Path.Combine(installDirectory, InstallerPaths.ProductExecutableName);
        string startMenuShortcut = Path.Combine(InstallerPaths.StartMenuDirectory, InstallerPaths.ProductName + ".lnk");
        string desktopShortcut = Path.Combine(InstallerPaths.DesktopDirectory, InstallerPaths.ProductName + ".lnk");

        if (options.CreateStartMenuShortcut)
        {
            ShortcutService.Create(startMenuShortcut, executablePath, description: InstallerPaths.ProductName);
        }
        else
        {
            ShortcutService.DeleteIfOwned(startMenuShortcut, executablePath);
        }

        if (options.CreateDesktopShortcut)
        {
            ShortcutService.Create(desktopShortcut, executablePath, description: InstallerPaths.ProductName);
        }
        else
        {
            ShortcutService.DeleteIfOwned(desktopShortcut, executablePath);
        }
    }

    private static void RemoveShortcuts(string installDirectory)
    {
        string executablePath = Path.Combine(InstallerPaths.Normalize(installDirectory), InstallerPaths.ProductExecutableName);
        string startMenuShortcut = Path.Combine(InstallerPaths.StartMenuDirectory, InstallerPaths.ProductName + ".lnk");
        string desktopShortcut = Path.Combine(InstallerPaths.DesktopDirectory, InstallerPaths.ProductName + ".lnk");
        ShortcutService.DeleteIfOwned(startMenuShortcut, executablePath);
        ShortcutService.DeleteIfOwned(desktopShortcut, executablePath);
    }

    private static bool IsRegisteredInstall(string directory)
    {
        using RegistryKey? key = InstallerPaths.OpenInstallerKey(writable: false);
        string? registered = key?.GetValue("InstallPath") as string;
        return registered is not null &&
               string.Equals(InstallerPaths.Normalize(registered), InstallerPaths.Normalize(directory), StringComparison.OrdinalIgnoreCase);
    }

    private static void CopyDirectory(
        string sourceDirectory,
        string destinationDirectory,
        IProgress<InstallProgress>? progress,
        string? skipDestinationPath = null,
        string? backupDirectory = null,
        IDictionary<string, string?>? backups = null)
    {
        string[] files = Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories);
        int completed = 0;
        foreach (string sourcePath in files)
        {
            string relativePath = Path.GetRelativePath(sourceDirectory, sourcePath);
            string destinationPath = Path.Combine(destinationDirectory, relativePath);
            if (skipDestinationPath is not null &&
                string.Equals(
                    InstallerPaths.Normalize(destinationPath),
                    InstallerPaths.Normalize(skipDestinationPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (backupDirectory is not null && backups is not null)
            {
                BackupFile(destinationPath, destinationDirectory, backupDirectory, backups);
            }

            string? directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temporaryPath = destinationPath + ".installing-" + Guid.NewGuid().ToString("N");
            File.Copy(sourcePath, temporaryPath, overwrite: true);
            File.Move(temporaryPath, destinationPath, overwrite: true);
            completed++;
            progress?.Report(new InstallProgress("Copying application files", completed, files.Length));
        }
    }

    private static void BackupFile(
        string destinationPath,
        string destinationRoot,
        string backupDirectory,
        IDictionary<string, string?> backups)
    {
        if (backups.ContainsKey(destinationPath))
        {
            return;
        }

        if (!File.Exists(destinationPath))
        {
            backups[destinationPath] = null;
            return;
        }

        string backupPath = Path.Combine(backupDirectory, Path.GetRelativePath(destinationRoot, destinationPath));
        string? directory = Path.GetDirectoryName(backupPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.Copy(destinationPath, backupPath, overwrite: true);
        backups[destinationPath] = backupPath;
    }

    private static void RollbackFiles(IReadOnlyDictionary<string, string?> backups)
    {
        foreach ((string destinationPath, string? backupPath) in backups)
        {
            try
            {
                if (backupPath is null)
                {
                    if (File.Exists(destinationPath))
                    {
                        File.Delete(destinationPath);
                    }
                }
                else if (File.Exists(backupPath))
                {
                    File.Copy(backupPath, destinationPath, overwrite: true);
                }
            }
            catch
            {
                // Preserve the original installation error.
            }
        }
    }
}
