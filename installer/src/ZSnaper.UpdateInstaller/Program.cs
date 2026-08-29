using ZSnaper.Installer.Core;

namespace ZSnaper.UpdateInstaller;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        using UpdateForm form = new(GetValue(args, "--package"));
        Application.Run(form);
    }

    private static string? GetValue(IReadOnlyList<string> args, string flag)
    {
        for (int index = 0; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], flag, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return args.FirstOrDefault(value => value.EndsWith(".zup", StringComparison.OrdinalIgnoreCase));
    }
}
