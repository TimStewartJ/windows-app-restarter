using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using WindowsAppRestarter.UI;

namespace WindowsAppRestarter;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private const string AppName = "Windows App Restarter";

    private readonly RestartService restartService = new();
    private readonly StartupManager startupManager = new(Application.ExecutablePath);
    private readonly CancellationTokenSource activationCancellation = new();
    private readonly System.Windows.Forms.Timer activationTimer;
    private readonly Icon trayAppIcon;
    private readonly NotifyIcon trayIcon;
    private readonly ContextMenuStrip trayMenu;
    private readonly ToolStripMenuItem restartMenuItem;
    private readonly ToolStripMenuItem startupMenuItem;
    private readonly ToolStripMenuItem statusMenuItem;
    private readonly FlyoutForm flyout;
    private readonly Task activationListenerTask;
    private int pendingActivations;
    private RestartStatus status = RestartStatus.Idle;

    public TrayApplicationContext(bool showFlyoutOnStartup)
    {
        trayAppIcon = LoadTrayIcon();

        flyout = new FlyoutForm(trayAppIcon, $"Version {GetDisplayVersion()}");
        flyout.RestartRequested += () => _ = RestartAsync();
        flyout.StartupToggled += SetStartup;
        flyout.OpenLogsRequested += OpenLogs;
        flyout.ExitRequested += ExitApplication;
        flyout.SetStatus(status);
        flyout.SetStartupEnabled(ReadStartupEnabled(), animate: false);

        restartMenuItem = new ToolStripMenuItem("Restart Windows App + Explorer", null, (_, _) => _ = RestartAsync());
        if (SystemFonts.MenuFont is { } menuFont)
        {
            restartMenuItem.Font = new Font(menuFont, FontStyle.Bold);
        }

        statusMenuItem = new ToolStripMenuItem(status.ToMenuText()) { Enabled = false };
        startupMenuItem = new ToolStripMenuItem("Start with Windows", null, (_, _) => SetStartup(!ReadStartupEnabled()));

        var openMenuItem = new ToolStripMenuItem("Open", null, (_, _) => flyout.ShowFlyout(Cursor.Position, takeFocus: true));
        var openLogsMenuItem = new ToolStripMenuItem("Open log file", null, (_, _) => OpenLogs());
        var exitMenuItem = new ToolStripMenuItem("Exit", null, (_, _) => ExitApplication());

        trayMenu = new ContextMenuStrip { ShowImageMargin = false };
        trayMenu.Opening += (_, _) =>
        {
            flyout.HideFlyout();
            RefreshStartupMenuState();
        };
        trayMenu.Items.AddRange(
        [
            openMenuItem,
            restartMenuItem,
            new ToolStripSeparator(),
            statusMenuItem,
            startupMenuItem,
            openLogsMenuItem,
            new ToolStripSeparator(),
            exitMenuItem
        ]);

        trayIcon = new NotifyIcon
        {
            ContextMenuStrip = trayMenu,
            Icon = trayAppIcon,
            Text = AppName,
            Visible = true
        };
        trayIcon.MouseClick += OnTrayMouseClick;
        trayIcon.MouseDoubleClick += OnTrayMouseDoubleClick;

        RefreshStartupMenuState();
        activationTimer = new System.Windows.Forms.Timer { Interval = 100 };
        activationTimer.Tick += (_, _) => ShowPendingActivation();
        activationTimer.Start();
        activationListenerTask = SingleInstanceActivation.ListenAsync(QueueActivation, activationCancellation.Token);

        if (showFlyoutOnStartup)
        {
            QueueActivation();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            activationCancellation.Cancel();
            activationTimer.Dispose();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            trayMenu.Dispose();
            flyout.Dispose();
            trayAppIcon.Dispose();
            activationCancellation.Dispose();
        }

        base.Dispose(disposing);
    }

    private void OnTrayMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            flyout.ToggleFlyout(Cursor.Position);
        }
    }

    private void OnTrayMouseDoubleClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        if (!flyout.Visible)
        {
            flyout.ShowFlyout(Cursor.Position, takeFocus: true);
        }

        _ = RestartAsync();
    }


    private async Task RestartAsync()
    {
        if (status.IsRunning)
        {
            return;
        }

        AppLogger.Info("Restart requested.");
        UpdateStatus(RestartStatus.Running("Preparing…"));

        try
        {
            var progress = new Progress<string>(step => UpdateStatus(RestartStatus.Running(step)));
            var result = await restartService.RestartAsync(progress);
            AppLogger.Info(result.ToLogMessage());

            UpdateStatus(RestartStatus.FromResult(result));
            if (!flyout.Visible)
            {
                var icon = result.Failures.Count == 0 ? ToolTipIcon.Info : ToolTipIcon.Warning;
                ShowBalloon(status.Title, status.Detail, icon, 3000);
            }
        }
        catch (Exception exception)
        {
            AppLogger.Error("Restart failed.", exception);
            UpdateStatus(RestartStatus.Failure(exception));
            if (!flyout.Visible)
            {
                ShowBalloon(status.Title, exception.Message, ToolTipIcon.Error, 5000);
            }
        }
        finally
        {
            RestoreTrayIcon();
        }
    }

    private void UpdateStatus(RestartStatus value)
    {
        status = value;
        restartMenuItem.Enabled = !value.IsRunning;
        statusMenuItem.Text = value.ToMenuText();
        flyout.SetStatus(value);
    }

    private void SetStartup(bool enable)
    {
        try
        {
            startupManager.SetEnabled(enable);
            RefreshStartupMenuState();

            var message = enable
                ? "Windows App Restarter will start when you sign in."
                : "Windows App Restarter will no longer start when you sign in.";
            AppLogger.Info(message);
        }
        catch (Exception exception)
        {
            AppLogger.Error("Could not update startup setting.", exception);
            RefreshStartupMenuState();
            ShowBalloon("Startup setting failed", exception.Message, ToolTipIcon.Error, 5000);
        }
    }

    private bool ReadStartupEnabled()
    {
        try
        {
            return startupManager.IsEnabled();
        }
        catch (Exception exception)
        {
            AppLogger.Error("Could not read startup setting.", exception);
            return false;
        }
    }

    private void RefreshStartupMenuState()
    {
        var enabled = ReadStartupEnabled();
        startupMenuItem.Checked = enabled;
        flyout.SetStartupEnabled(enabled, animate: true);
    }

    private static void OpenLogs()
    {
        try
        {
            Directory.CreateDirectory(AppLogger.LogDirectory);
            if (!File.Exists(AppLogger.LogPath))
            {
                File.WriteAllText(AppLogger.LogPath, string.Empty);
            }

            Process.Start(new ProcessStartInfo(AppLogger.LogPath) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            AppLogger.Error("Could not open the log file.", exception);
        }
    }

    private void RestoreTrayIcon()
    {
        trayIcon.Visible = false;
        trayIcon.Icon = trayAppIcon;
        trayIcon.Visible = true;
    }

    private void QueueActivation()
    {
        Interlocked.Exchange(ref pendingActivations, 1);
    }

    private void ShowPendingActivation()
    {
        if (Interlocked.Exchange(ref pendingActivations, 0) == 0)
        {
            return;
        }

        AppLogger.Info("Launch activated the flyout.");
        RestoreTrayIcon();
        RefreshStartupMenuState();
        // Launches (Start menu, scripts, a second instance) must never steal keyboard focus.
        flyout.ShowFlyout(Cursor.Position, takeFocus: false);
    }

    private static Icon LoadTrayIcon()
    {
        try
        {
            var size = SystemInformation.SmallIconSize.Width;
            if (Icon.ExtractIcon(Application.ExecutablePath, 0, size) is { } sized)
            {
                return sized;
            }
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
        }

        return Icon.ExtractAssociatedIcon(Application.ExecutablePath)
            ?? new Icon(SystemIcons.Application, SystemInformation.SmallIconSize);
    }

    private static string GetDisplayVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? "dev" : version.ToString(3);
    }

    private void ShowBalloon(string title, string text, ToolTipIcon icon, int timeoutMilliseconds)
    {
        trayIcon.ShowBalloonTip(timeoutMilliseconds, title, text, icon);
    }

    private void ExitApplication()
    {
        AppLogger.Info("Exit requested.");
        activationCancellation.Cancel();
        flyout.HideFlyout();
        trayIcon.Visible = false;
        ExitThread();
    }
}
