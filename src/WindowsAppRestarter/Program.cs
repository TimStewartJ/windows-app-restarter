using System.Windows.Forms;

namespace WindowsAppRestarter;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var startInBackground = args.Any(argument =>
            string.Equals(argument, "--background", StringComparison.OrdinalIgnoreCase));

        using var mutex = SingleInstanceActivation.CreateMutex(out var createdNew);
        if (!createdNew)
        {
            if (!SingleInstanceActivation.NotifyRunningInstance())
            {
                MessageBox.Show(
                    "Windows App Restarter is already running, but the tray menu could not be opened automatically. Look for the Windows App Restarter icon in the notification area.",
                    "Windows App Restarter",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }

            return;
        }

        AppLogger.Info("Application started.");
        Application.Run(new TrayApplicationContext(showMenuOnStartup: !startInBackground));
        AppLogger.Info("Application exited.");
        GC.KeepAlive(mutex);
    }
}
