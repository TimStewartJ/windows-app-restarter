namespace WindowsAppRestarter;

internal sealed record StoppedProcess(string Name, int Id)
{
    public override string ToString() => $"{Name} (PID {Id})";
}

internal sealed record RestartResult(
    IReadOnlyList<StoppedProcess> WindowsAppProcesses,
    IReadOnlyList<StoppedProcess> ExplorerProcesses,
    bool ExplorerStarted,
    IReadOnlyList<string> Failures)
{
    public string ToSummary()
    {
        var windowsAppSummary = WindowsAppProcesses.Count switch
        {
            0 => "No Windows App processes were running.",
            1 => "Stopped 1 Windows App process.",
            var count => $"Stopped {count} Windows App processes."
        };

        var explorerSummary = ExplorerStarted
            ? "Explorer relaunched."
            : "Explorer came back on its own.";

        if (Failures.Count == 0)
        {
            return $"{windowsAppSummary} {explorerSummary}";
        }

        var issues = Failures.Count == 1 ? "1 issue" : $"{Failures.Count} issues";
        return $"{windowsAppSummary} {explorerSummary} {issues} — open the log file for details.";
    }

    public string ToLogMessage()
    {
        var windowsAppProcesses = WindowsAppProcesses.Count == 0
            ? "none"
            : string.Join(", ", WindowsAppProcesses);

        var explorerProcesses = ExplorerProcesses.Count == 0
            ? "none"
            : string.Join(", ", ExplorerProcesses);

        var failures = Failures.Count == 0
            ? "none"
            : string.Join(Environment.NewLine, Failures.Select(failure => $"- {failure}"));

        return $"""
            Restart completed.
            Windows App processes stopped: {windowsAppProcesses}
            Explorer processes stopped: {explorerProcesses}
            Explorer launched manually: {ExplorerStarted}
            Failures: {failures}
            """;
    }
}
