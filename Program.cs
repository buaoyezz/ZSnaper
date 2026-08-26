using ZSnaper.Context;

namespace ZSnaper;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var context = new TrayAppContext();
        Application.Run(context);
    }
}
