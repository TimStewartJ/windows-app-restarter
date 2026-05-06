using System.Windows.Forms;

namespace WindowsAppRestarter;

internal static class Program
{
    private const string MutexName = @"Local\WindowsAppRestarter";

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "Windows App Restarter is already running.",
                "Windows App Restarter",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        AppLogger.Info("Application started.");
        Application.Run(new TrayApplicationContext());
        AppLogger.Info("Application exited.");
        GC.KeepAlive(mutex);
    }
}
