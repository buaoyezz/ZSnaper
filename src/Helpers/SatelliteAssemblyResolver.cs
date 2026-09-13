using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace ZSnaper.Helpers;

internal static class SatelliteAssemblyResolver
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        AssemblyLoadContext.Default.Resolving += ResolveSatelliteAssembly;
    }

    private static Assembly? ResolveSatelliteAssembly(AssemblyLoadContext context, AssemblyName assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName.Name) ||
            string.IsNullOrWhiteSpace(assemblyName.CultureName))
        {
            return null;
        }

        string applicationDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        DirectoryInfo installDirectory = new(applicationDirectory);
        if (!Directory.Exists(Path.Combine(installDirectory.FullName, "langs")) &&
            (string.Equals(installDirectory.Name, "runtime", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(installDirectory.Name, "app", StringComparison.OrdinalIgnoreCase)))
        {
            installDirectory = installDirectory.Parent ?? installDirectory;
        }

        string candidate = Path.GetFullPath(Path.Combine(
            installDirectory.FullName,
            "langs",
            assemblyName.CultureName,
            assemblyName.Name + ".dll"));
        string languageRoot = Path.GetFullPath(Path.Combine(installDirectory.FullName, "langs"))
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(languageRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(candidate))
        {
            return null;
        }

        return context.LoadFromAssemblyPath(candidate);
    }
}
