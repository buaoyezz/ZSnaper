using ZSnaper.Installer.Core;

namespace ZSnaper.Installer.Smoke;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: ZSnaper.Installer.Smoke <setup.exe> <update.zup>");
            return 2;
        }

        string setupPath = Path.GetFullPath(args[0]);
        string updatePath = Path.GetFullPath(args[1]);
        if (!PayloadArchive.TryReadPayloadRange(setupPath, out long offset, out long length) || length <= 0)
        {
            throw new InvalidDataException("The setup payload footer could not be read.");
        }

        string extracted = PayloadArchive.ExtractEmbeddedPayload(setupPath);
        try
        {
            if (!File.Exists(Path.Combine(extracted, InstallerPaths.ProductExecutableName)))
            {
                throw new InvalidDataException("The embedded payload does not contain ZSnaper.exe.");
            }
        }
        finally
        {
            PayloadArchive.TryDeleteDirectory(extracted);
        }

        UpdateManifest manifest = new UpdatePackageService().ReadManifest(updatePath);
        if (!string.Equals(manifest.Format, "zsnaper-update-1", StringComparison.Ordinal) ||
            !string.Equals(manifest.To, "0.0.3-alpha", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The update manifest did not pass the smoke test.");
        }

        Console.WriteLine($"Smoke test passed. Payload offset={offset}, length={length}, changed={manifest.Files.Count}, deleted={manifest.Delete.Count}.");
        return 0;
    }
}
