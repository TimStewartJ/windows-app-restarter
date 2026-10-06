using System.ComponentModel;
using System.Diagnostics;

namespace WindowsAppRestarter;

/// <summary>
/// Restarts the Windows Audio service (<c>Audiosrv</c>) and nothing else. Only administrators may stop a
/// service, so the command runs elevated and Windows asks for approval each time.
/// </summary>
internal static class AudioServiceRestarter
{
    private const int ErrorCancelled = 1223;

    public static async Task<RestartStatus> RestartAsync()
    {
        var system = Environment.SystemDirectory;
        var net = Path.Combine(system, "net.exe");
        var startInfo = new ProcessStartInfo(
            Path.Combine(system, "cmd.exe"),
            $"/d /c \"\"{net}\" stop Audiosrv /y & \"{net}\" start Audiosrv\"")
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = system,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Windows did not start the elevated command.");
            // Process.Start already holds a waitable handle here, so this does not reopen the elevated process.
            await process.WaitForExitAsync();
            return process.ExitCode == 0
                ? new RestartStatus(RestartState.Succeeded, "Audio service restarted", "Windows Audio is running again.", DateTimeOffset.Now)
                : Failed($"Windows Audio did not start again (net start exit code {process.ExitCode}).");
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == ErrorCancelled)
        {
            return new RestartStatus(
                RestartState.CompletedWithIssues,
                "Audio restart cancelled",
                "The administrator prompt was declined, so nothing was changed.",
                DateTimeOffset.Now);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return Failed(exception.Message);
        }
    }

    private static RestartStatus Failed(string detail) =>
        new(RestartState.Failed, "Audio restart failed", detail, DateTimeOffset.Now);
}
