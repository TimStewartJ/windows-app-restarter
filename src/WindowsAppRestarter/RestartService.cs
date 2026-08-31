using System.ComponentModel;
using System.Diagnostics;

namespace WindowsAppRestarter;

internal sealed class RestartService
{
    private static readonly string[] WindowsAppProcessNames = ["Windows365", "msrdcw", "msrdc"];
    private static readonly TimeSpan ProcessExitTimeout = TimeSpan.FromSeconds(5);

    public async Task<RestartResult> RestartAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var stoppedWindowsAppProcesses = new List<StoppedProcess>();
        var stoppedExplorerProcesses = new List<StoppedProcess>();
        var failures = new List<string>();

        progress?.Report("Stopping Windows App…");
        foreach (var processName in WindowsAppProcessNames)
        {
            await StopProcessesByNameAsync(
                processName,
                stoppedWindowsAppProcesses,
                failures,
                entireProcessTree: true,
                cancellationToken);
        }

        progress?.Report("Restarting Explorer…");
        await StopProcessesByNameAsync(
            "explorer",
            stoppedExplorerProcesses,
            failures,
            entireProcessTree: false,
            cancellationToken);

        progress?.Report("Waiting for Explorer to come back…");
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

        var explorerStarted = false;
        if (!IsExplorerRunning())
        {
            Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
            explorerStarted = true;
        }

        return new RestartResult(stoppedWindowsAppProcesses, stoppedExplorerProcesses, explorerStarted, failures);
    }

    private static async Task StopProcessesByNameAsync(
        string processName,
        ICollection<StoppedProcess> stoppedProcesses,
        ICollection<string> failures,
        bool entireProcessTree,
        CancellationToken cancellationToken)
    {
        foreach (var process in Process.GetProcessesByName(processName))
        {
            await StopProcessAsync(process, stoppedProcesses, failures, entireProcessTree, cancellationToken);
        }
    }

    private static async Task StopProcessAsync(
        Process process,
        ICollection<StoppedProcess> stoppedProcesses,
        ICollection<string> failures,
        bool entireProcessTree,
        CancellationToken cancellationToken)
    {
        using (process)
        {
            var stoppedProcess = new StoppedProcess(process.ProcessName, process.Id);

            try
            {
                if (process.HasExited)
                {
                    return;
                }

                process.Kill(entireProcessTree);
                await process.WaitForExitAsync(cancellationToken).WaitAsync(ProcessExitTimeout, cancellationToken);
                stoppedProcesses.Add(stoppedProcess);
            }
            catch (Exception exception) when (exception is InvalidOperationException
                or Win32Exception
                or NotSupportedException
                or TimeoutException)
            {
                failures.Add($"Could not stop {stoppedProcess}: {exception.Message}");
            }
        }
    }

    private static bool IsExplorerRunning()
    {
        var explorerProcesses = Process.GetProcessesByName("explorer");

        try
        {
            return explorerProcesses.Length > 0;
        }
        finally
        {
            foreach (var process in explorerProcesses)
            {
                process.Dispose();
            }
        }
    }
}
