using System.Text;
using System.Text.Json;

namespace ZSnaper.Services;

internal static class ConfigFileStore
{
    public static bool TryRead(
        string path,
        JsonSerializerOptions options,
        out AppConfig config)
    {
        try
        {
            using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 16 * 1024,
                FileOptions.SequentialScan);
            config = JsonSerializer.Deserialize<AppConfig>(stream, options) ?? new AppConfig();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            config = new AppConfig();
            return false;
        }
    }

    public static void WriteAtomic(
        string path,
        string contents,
        string? backupPath,
        bool backupExisting)
    {
        string? directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("The configuration path has no parent directory.", nameof(path));
        }

        Directory.CreateDirectory(directory);
        string temporaryPath = $"{path}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (FileStream stream = new(
                       temporaryPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 16 * 1024,
                       FileOptions.WriteThrough))
            using (StreamWriter writer = new(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(contents);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            if (backupExisting && File.Exists(path) && !string.IsNullOrWhiteSpace(backupPath))
            {
                try
                {
                    File.Replace(temporaryPath, path, backupPath, ignoreMetadataErrors: true);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Copy(path, backupPath, overwrite: true);
                    File.Move(temporaryPath, path, overwrite: true);
                }
                catch (IOException)
                {
                    File.Copy(path, backupPath, overwrite: true);
                    File.Move(temporaryPath, path, overwrite: true);
                }
            }
            else
            {
                File.Move(temporaryPath, path, overwrite: true);
            }
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            catch
            {
                // A stale temp file is harmless and will be replaced by the next save.
            }
        }
    }
}
