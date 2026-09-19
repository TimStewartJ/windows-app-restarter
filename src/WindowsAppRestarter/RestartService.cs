using System.ComponentModel;
using System.Diagnostics;

namespace WindowsAppRestarter;

internal sealed class RestartService
{
    private static readonly string[] WindowsAppProcessNames = ["Windows365", "msrdcw", "msrdc"];

    // On-demand sign-in brokers that Windows respawns automatically. A stale instance holding an orphaned
    // "Windows Security" passkey prompt or "Work or school account" window makes every new passkey request
    // fail with RPC_S_CALL_IN_PROGRESS until it is cleared.
    private static readonly string[] SignInBrokerProcessNames = ["CredentialUIBroker", "Microsoft.AAD.BrokerPlugin"];

    private static readonly TimeSpan ProcessExitTimeout = TimeSpan.FromSeconds(5);

    // Windows answers "Access is denied" when asked to terminate a process that is already shutting down.
    // Stopping Windows App makes its helpers and sign-in brokers exit on their own, so a process that refuses
    // to be stopped gets this long to leave before it is reported as a failure.
    private static readonly TimeSpan SelfExitGracePeriod = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan ExitPollInterval = TimeSpan.FromMilliseconds(50);

    private const int ErrorAccessDenied = 5;

    private static readonly int CurrentSessionId = GetCurrentSessionId();

    public async Task<RestartResult> RestartAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var stoppedWindowsAppProcesses = new List<StoppedProcess>();
        var stoppedSignInBrokerProcesses = new List<StoppedProcess>();
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

        progress?.Report("Clearing stuck sign-in prompts…");
        foreach (var processName in SignInBrokerProcessNames)
        {
            await StopProcessesByNameAsync(
                processName,
                stoppedSignInBrokerProcesses,
                failures,
                entireProcessTree: false,
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

        return new RestartResult(stoppedWindowsAppProcesses, stoppedSignInBrokerProcesses, stoppedExplorerProcesses, explorerStarted, failures);
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
            using (process)
            {
                // Other sessions belong to other signed-in users; their apps and shell are not ours to restart.
                if (IsInCurrentSession(process))
                {
                    await StopProcessAsync(process, stoppedProcesses, failures, entireProcessTree, cancellationToken);
                }
            }
        }
    }

    private static async Task StopProcessAsync(
        Process process,
        ICollection<StoppedProcess> stoppedProcesses,
        ICollection<string> failures,
        bool entireProcessTree,
        CancellationToken cancellationToken)
    {
        var stoppedProcess = new StoppedProcess(process.ProcessName, process.Id);
        Exception? killError = null;

        try
        {
            if (process.HasExited)
            {
                return;
            }

            process.Kill(entireProcessTree);
        }
        catch (Exception exception) when (IsStopFailure(exception))
        {
            killError = exception;
        }

        try
        {
            var timeout = killError is null ? ProcessExitTimeout : SelfExitGracePeriod;
            if (await WaitForExitAsync(process, timeout, cancellationToken))
            {
                stoppedProcesses.Add(stoppedProcess);
            }
            else
            {
                failures.Add(DescribeFailure(stoppedProcess, killError));
            }
        }
        catch (Exception exception) when (IsStopFailure(exception))
        {
            failures.Add(DescribeFailure(stoppedProcess, killError ?? exception));
        }
    }

    // Process.WaitForExitAsync opens the process with PROCESS_ALL_ACCESS, which Windows refuses for exactly the
    // processes that refuse to be killed. HasExited only needs PROCESS_QUERY_LIMITED_INFORMATION, which is
    // granted even for elevated and protected processes.
    private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var elapsed = Stopwatch.StartNew();
        while (!process.HasExited)
        {
            if (elapsed.Elapsed >= timeout)
            {
                return false;
            }

            await Task.Delay(ExitPollInterval, cancellationToken);
        }

        return true;
    }

    // Kill(entireProcessTree: true) reports descendants it could not stop as an AggregateException.
    private static bool IsStopFailure(Exception exception) =>
        exception is InvalidOperationException or Win32Exception or NotSupportedException or AggregateException;

    /// <param name="killError">Why the process could not be killed, or null when it was killed but never exited.</param>
    private static string DescribeFailure(StoppedProcess process, Exception? killError)
    {
        if (killError is null)
        {
            return $"Could not stop {process}: it did not exit within {ProcessExitTimeout.TotalSeconds:0} seconds.";
        }

        if (IsAccessDenied(killError))
        {
            return $"Could not stop {process}: Windows denied access because it is running elevated, protected, or under another account. "
                + "End it from an elevated Task Manager, or sign out and back in.";
        }

        return $"Could not stop {process}: {killError.Message}";
    }

    private static bool IsAccessDenied(Exception exception) => exception switch
    {
        Win32Exception { NativeErrorCode: ErrorAccessDenied } => true,
        AggregateException aggregate => aggregate.Flatten().InnerExceptions.Any(IsAccessDenied),
        _ => false
    };

    private static bool IsInCurrentSession(Process process)
    {
        try
        {
            return process.SessionId == CurrentSessionId;
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // The process is already gone, so there is nothing to stop.
            return false;
        }
    }

    private static int GetCurrentSessionId()
    {
        using var currentProcess = Process.GetCurrentProcess();
        return currentProcess.SessionId;
    }

    private static bool IsExplorerRunning()
    {
        var explorerProcesses = Process.GetProcessesByName("explorer");

        try
        {
            return explorerProcesses.Any(IsInCurrentSession);
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
