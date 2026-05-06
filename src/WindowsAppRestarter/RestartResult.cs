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
        var windowsAppSummary = WindowsAppProcesses.Count == 0
            ? "No Windows App processes were running."
            : $"Stopped {WindowsAppProcesses.Count} Windows App process(es).";

        var explorerSummary = ExplorerStarted
            ? "Explorer relaunched."
            : "Explorer was already running after restart.";

        if (Failures.Count == 0)
        {
            return $"{windowsAppSummary} {explorerSummary}";
        }

        return $"{windowsAppSummary} {explorerSummary} {Failures.Count} issue(s); open logs for details.";
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
