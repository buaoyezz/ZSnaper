using System.Diagnostics;
using ZSnaper.Context;

namespace ZSnaper;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        WaitForPreviousInstance(args);
        bool startMinimizedToTray = args.Any(argument =>
            string.Equals(argument, "--startup", StringComparison.OrdinalIgnoreCase));
        using var context = new TrayAppContext(startMinimizedToTray);
        Application.Run(context);
    }

    private static void WaitForPreviousInstance(string[] args)
    {
        int waitArgumentIndex = Array.FindIndex(
            args,
            argument => string.Equals(argument, "--wait-for-pid", StringComparison.OrdinalIgnoreCase));
        if (waitArgumentIndex < 0 || waitArgumentIndex + 1 >= args.Length ||
            !int.TryParse(args[waitArgumentIndex + 1], out int processId) ||
            processId == Environment.ProcessId)
        {
            return;
        }

        try
        {
            using Process previous = Process.GetProcessById(processId);
            previous.WaitForExit(10_000);
        }
        catch
        {
            // The previous instance may already have exited.
        }
    }
}
