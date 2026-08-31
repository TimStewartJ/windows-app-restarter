using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using WindowsAppRestarter.UI;

namespace WindowsAppRestarter;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private const string AppName = "Windows App Restarter";
    private static readonly TimeSpan InitialUpdateCheckDelay = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan UpdateRetryDelay = TimeSpan.FromMinutes(1);

    private readonly RestartService restartService = new();
    private readonly StartupManager startupManager = new(Application.ExecutablePath);
    private readonly AppSettings settings = new();
    private readonly UpdateService updateService;
    private readonly CancellationTokenSource activationCancellation = new();
    private readonly System.Windows.Forms.Timer activationTimer;
    private readonly System.Windows.Forms.Timer updateTimer;
    private readonly Icon trayAppIcon;
    private readonly NotifyIcon trayIcon;
    private readonly ContextMenuStrip trayMenu;
    private readonly ToolStripMenuItem restartMenuItem;
    private readonly ToolStripMenuItem startupMenuItem;
    private readonly ToolStripMenuItem autoUpdateMenuItem;
    private readonly ToolStripMenuItem statusMenuItem;
    private readonly FlyoutForm flyout;
    private readonly Task activationListenerTask;
    private int pendingActivations;
    private RestartStatus status = RestartStatus.Idle;
    private bool updateCycleRunning;
    private bool exitingForUpdate;
    private string? pendingInstallerPath;
    private Version? pendingUpdateVersion;

    public TrayApplicationContext(bool showFlyoutOnStartup)
    {
        trayAppIcon = LoadTrayIcon();
        updateService = new UpdateService(GetVersion());

        flyout = new FlyoutForm(trayAppIcon, $"Version {GetDisplayVersion()}");
        flyout.RestartRequested += () => _ = RestartAsync();
        flyout.StartupToggled += SetStartup;
        flyout.AutoUpdateToggled += SetAutoUpdate;
        flyout.OpenLogsRequested += OpenLogs;
        flyout.ExitRequested += ExitApplication;
        flyout.SetStatus(status);
        flyout.SetStartupEnabled(ReadStartupEnabled(), animate: false);
        flyout.SetAutoUpdateEnabled(settings.AutoUpdateEnabled, animate: false);

        restartMenuItem = new ToolStripMenuItem("Restart Windows App + Explorer", null, (_, _) => _ = RestartAsync());
        if (SystemFonts.MenuFont is { } menuFont)
        {
            restartMenuItem.Font = new Font(menuFont, FontStyle.Bold);
        }

        statusMenuItem = new ToolStripMenuItem(status.ToMenuText()) { Enabled = false };
        startupMenuItem = new ToolStripMenuItem("Start with Windows", null, (_, _) => SetStartup(!ReadStartupEnabled()));
        autoUpdateMenuItem = new ToolStripMenuItem("Automatic updates", null, (_, _) => SetAutoUpdate(!settings.AutoUpdateEnabled));

        var openMenuItem = new ToolStripMenuItem("Open", null, (_, _) => flyout.ShowFlyout(Cursor.Position, takeFocus: true));
        var openLogsMenuItem = new ToolStripMenuItem("Open log file", null, (_, _) => OpenLogs());
        var exitMenuItem = new ToolStripMenuItem("Exit", null, (_, _) => ExitApplication());

        trayMenu = new ContextMenuStrip { ShowImageMargin = false };
        trayMenu.Opening += (_, _) =>
        {
            flyout.HideFlyout();
            RefreshStartupMenuState();
            autoUpdateMenuItem.Checked = settings.AutoUpdateEnabled;
        };
        trayMenu.Items.AddRange(
        [
            openMenuItem,
            restartMenuItem,
            new ToolStripSeparator(),
            statusMenuItem,
            startupMenuItem,
            autoUpdateMenuItem,
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
        autoUpdateMenuItem.Checked = settings.AutoUpdateEnabled;
        activationTimer = new System.Windows.Forms.Timer { Interval = 100 };
        activationTimer.Tick += (_, _) => ShowPendingActivation();
        activationTimer.Start();
        activationListenerTask = SingleInstanceActivation.ListenAsync(QueueActivation, activationCancellation.Token);

        updateTimer = new System.Windows.Forms.Timer { Interval = (int)InitialUpdateCheckDelay.TotalMilliseconds };
        updateTimer.Tick += (_, _) => _ = RunUpdateCycleAsync();
        updateTimer.Start();

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
            updateTimer.Dispose();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            trayMenu.Dispose();
            flyout.Dispose();
            trayAppIcon.Dispose();
            updateService.Dispose();
            activationCancellation.Dispose();
        }

        base.Dispose(disposing);
    }

    private async Task RunUpdateCycleAsync()
    {
        updateTimer.Stop();
        if (updateCycleRunning || exitingForUpdate)
        {
            return;
        }

        updateCycleRunning = true;
        var nextCheck = UpdateCheckInterval;
        try
        {
            if (!settings.AutoUpdateEnabled)
            {
                pendingInstallerPath = null;
                pendingUpdateVersion = null;
                return;
            }

            if (!UpdateService.IsInstallerManagedInstall())
            {
                AppLogger.Info("Automatic updates skipped: this copy was not set up by the installer.");
                return;
            }

            if (pendingInstallerPath is null)
            {
                UpdateService.CleanUpDownloads();

                var update = await updateService.CheckForUpdateAsync(activationCancellation.Token);
                if (update is null)
                {
                    AppLogger.Info($"Update check: version {updateService.CurrentVersion} is current.");
                    return;
                }

                AppLogger.Info($"Update check: version {update.Version} is available; downloading.");
                pendingInstallerPath = await updateService.DownloadAndVerifyAsync(update, activationCancellation.Token);
                pendingUpdateVersion = update.Version;
                AppLogger.Info($"Update {update.Version} downloaded and verified.");
            }

            if (!TryInstallPendingUpdate())
            {
                nextCheck = UpdateRetryDelay;
            }
        }
        catch (OperationCanceledException) when (activationCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            AppLogger.Error("Automatic update failed; will try again later.", exception);
            pendingInstallerPath = null;
            pendingUpdateVersion = null;
        }
        finally
        {
            updateCycleRunning = false;
            if (!exitingForUpdate && !activationCancellation.IsCancellationRequested)
            {
                updateTimer.Interval = (int)nextCheck.TotalMilliseconds;
                updateTimer.Start();
            }
        }
    }

    /// <summary>Installs a downloaded update unless the user is mid-restart or has the flyout open.</summary>
    private bool TryInstallPendingUpdate()
    {
        if (pendingInstallerPath is null || pendingUpdateVersion is null)
        {
            return true;
        }

        if (status.IsRunning || flyout.Visible)
        {
            return false;
        }

        if (!File.Exists(pendingInstallerPath))
        {
            pendingInstallerPath = null;
            pendingUpdateVersion = null;
            return true;
        }

        AppLogger.Info($"Installing update {pendingUpdateVersion} silently and restarting.");
        exitingForUpdate = true;
        UpdateService.LaunchInstaller(pendingInstallerPath, ReadStartupEnabled());
        ExitApplication();
        return true;
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

    private void SetAutoUpdate(bool enable)
    {
        try
        {
            settings.AutoUpdateEnabled = enable;
            AppLogger.Info(enable ? "Automatic updates enabled." : "Automatic updates disabled.");

            if (enable && !updateCycleRunning && !exitingForUpdate)
            {
                updateTimer.Stop();
                updateTimer.Interval = (int)TimeSpan.FromSeconds(5).TotalMilliseconds;
                updateTimer.Start();
            }
        }
        catch (Exception exception)
        {
            AppLogger.Error("Could not update the automatic update setting.", exception);
        }

        var current = settings.AutoUpdateEnabled;
        autoUpdateMenuItem.Checked = current;
        flyout.SetAutoUpdateEnabled(current, animate: true);
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

    private static Version GetVersion() =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

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
