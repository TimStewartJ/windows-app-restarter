using System.Windows.Forms;

namespace WindowsAppRestarter;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
#pragma warning disable WFO5001 // Dark mode is still flagged experimental; it themes the tray menu and message boxes.
        Application.SetColorMode(SystemColorMode.System);
#pragma warning restore WFO5001

        var startInBackground = args.Any(argument =>
            string.Equals(argument, "--background", StringComparison.OrdinalIgnoreCase));
        var relaunchedAfterUpdate = args.Any(argument =>
            string.Equals(argument, "--updated", StringComparison.OrdinalIgnoreCase));

        using var mutex = SingleInstanceActivation.CreateMutex(out var createdNew);
        if (!createdNew)
        {
            if (!SingleInstanceActivation.NotifyRunningInstance())
            {
                MessageBox.Show(
                    "Windows App Restarter is already running, but its flyout could not be opened automatically. Look for the Windows App Restarter icon in the notification area.",
                    "Windows App Restarter",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }

            return;
        }

        AppLogger.Info(relaunchedAfterUpdate
            ? $"Application started after automatic update to version {Application.ProductVersion}."
            : "Application started.");
        Application.Run(new TrayApplicationContext(showFlyoutOnStartup: !startInBackground));
        AppLogger.Info("Application exited.");
        GC.KeepAlive(mutex);
    }
}
