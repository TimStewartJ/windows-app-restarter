using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace WindowsAppRestarter;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly RestartService restartService = new();
    private readonly StartupManager startupManager = new(Application.ExecutablePath);
    private readonly NotifyIcon trayIcon;
    private readonly ToolStripMenuItem restartMenuItem;
    private readonly ToolStripMenuItem startupMenuItem;
    private readonly ToolStripMenuItem lastResultMenuItem;
    private bool isRestarting;

    public TrayApplicationContext()
    {
        restartMenuItem = new ToolStripMenuItem("Restart Windows App + Explorer", null, (_, _) => _ = RestartAsync());
        if (SystemFonts.MenuFont is { } menuFont)
        {
            restartMenuItem.Font = new Font(menuFont, FontStyle.Bold);
        }

        startupMenuItem = new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleStartup())
        {
            CheckOnClick = false
        };

        lastResultMenuItem = new ToolStripMenuItem("Last result: Nothing has run yet")
        {
            Enabled = false
        };

        var openLogsMenuItem = new ToolStripMenuItem("Open logs", null, (_, _) => OpenLogs());
        var exitMenuItem = new ToolStripMenuItem("Exit", null, (_, _) => ExitApplication());

        var menu = new ContextMenuStrip();
        menu.Opening += (_, _) => RefreshStartupMenuState();
        menu.Items.AddRange(
        [
            restartMenuItem,
            new ToolStripSeparator(),
            startupMenuItem,
            openLogsMenuItem,
            lastResultMenuItem,
            new ToolStripSeparator(),
            exitMenuItem
        ]);

        trayIcon = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = SystemIcons.Application,
            Text = "Windows App Restarter",
            Visible = true
        };
        trayIcon.DoubleClick += (_, _) => _ = RestartAsync();

        RefreshStartupMenuState();
        ShowBalloon("Windows App Restarter", "Ready. Double-click the tray icon to restart.", ToolTipIcon.Info, 1500);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            trayIcon.Visible = false;
            trayIcon.Dispose();
        }

        base.Dispose(disposing);
    }

    private async Task RestartAsync()
    {
        if (isRestarting)
        {
            ShowBalloon("Windows App Restarter", "A restart is already running.", ToolTipIcon.Info, 1500);
            return;
        }

        isRestarting = true;
        restartMenuItem.Enabled = false;
        lastResultMenuItem.Text = "Last result: Restart running...";

        try
        {
            AppLogger.Info("Restart requested.");
            ShowBalloon("Windows App Restarter", "Restarting Windows App and Explorer...", ToolTipIcon.Info, 1000);

            var result = await restartService.RestartAsync();
            AppLogger.Info(result.ToLogMessage());

            var summary = result.ToSummary();
            lastResultMenuItem.Text = $"Last result: {summary}";

            var icon = result.Failures.Count == 0 ? ToolTipIcon.Info : ToolTipIcon.Warning;
            ShowBalloon("Windows App Restarter", summary, icon, 3000);
        }
        catch (Exception exception)
        {
            AppLogger.Error("Restart failed.", exception);
            var message = $"Failed: {exception.Message}";
            lastResultMenuItem.Text = $"Last result: {message}";
            ShowBalloon("Windows App Restarter failed", exception.Message, ToolTipIcon.Error, 5000);
        }
        finally
        {
            isRestarting = false;
            restartMenuItem.Enabled = true;
            RestoreTrayIcon();
        }
    }

    private void ToggleStartup()
    {
        try
        {
            var enable = !startupManager.IsEnabled();
            startupManager.SetEnabled(enable);
            RefreshStartupMenuState();

            var message = enable
                ? "Windows App Restarter will start when you sign in."
                : "Windows App Restarter will no longer start when you sign in.";

            AppLogger.Info(message);
            ShowBalloon("Start with Windows", message, ToolTipIcon.Info, 2500);
        }
        catch (Exception exception)
        {
            AppLogger.Error("Could not update startup setting.", exception);
            ShowBalloon("Startup setting failed", exception.Message, ToolTipIcon.Error, 5000);
        }
    }

    private void RefreshStartupMenuState()
    {
        try
        {
            startupMenuItem.Checked = startupManager.IsEnabled();
        }
        catch (Exception exception)
        {
            AppLogger.Error("Could not read startup setting.", exception);
            startupMenuItem.Checked = false;
        }
    }

    private static void OpenLogs()
    {
        Directory.CreateDirectory(AppLogger.LogDirectory);

        if (!File.Exists(AppLogger.LogPath))
        {
            File.WriteAllText(AppLogger.LogPath, string.Empty);
        }

        Process.Start(new ProcessStartInfo(AppLogger.LogPath) { UseShellExecute = true });
    }

    private void RestoreTrayIcon()
    {
        trayIcon.Visible = false;
        trayIcon.Icon = SystemIcons.Application;
        trayIcon.Visible = true;
    }

    private void ShowBalloon(string title, string text, ToolTipIcon icon, int timeoutMilliseconds)
    {
        trayIcon.ShowBalloonTip(timeoutMilliseconds, title, text, icon);
    }

    private void ExitApplication()
    {
        AppLogger.Info("Exit requested.");
        trayIcon.Visible = false;
        ExitThread();
    }
}
