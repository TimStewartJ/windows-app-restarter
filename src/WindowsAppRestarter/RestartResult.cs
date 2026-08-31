namespace WindowsAppRestarter;

internal sealed record StoppedProcess(string Name, int Id)
{
    public override string ToString() => $"{Name} (PID {Id})";
}

internal sealed record RestartResult(
    IReadOnlyList<StoppedProcess> WindowsAppProcesses,
    IReadOnlyList<StoppedProcess> SignInBrokerProcesses,
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

        var brokerSummary = SignInBrokerProcesses.Count switch
        {
            0 => string.Empty,
            1 => " Cleared 1 sign-in prompt.",
            var count => $" Cleared {count} sign-in prompts."
        };

        var explorerSummary = ExplorerStarted
            ? "Explorer relaunched."
            : "Explorer came back on its own.";

        if (Failures.Count == 0)
        {
            return $"{windowsAppSummary}{brokerSummary} {explorerSummary}";
        }

        var issues = Failures.Count == 1 ? "1 issue" : $"{Failures.Count} issues";
        return $"{windowsAppSummary}{brokerSummary} {explorerSummary} {issues} — open the log file for details.";
    }

    public string ToLogMessage()
    {
        var windowsAppProcesses = WindowsAppProcesses.Count == 0
            ? "none"
            : string.Join(", ", WindowsAppProcesses);

        var signInBrokerProcesses = SignInBrokerProcesses.Count == 0
            ? "none"
            : string.Join(", ", SignInBrokerProcesses);

        var explorerProcesses = ExplorerProcesses.Count == 0
            ? "none"
            : string.Join(", ", ExplorerProcesses);

        var failures = Failures.Count == 0
            ? "none"
            : string.Join(Environment.NewLine, Failures.Select(failure => $"- {failure}"));

        return $"""
            Restart completed.
            Windows App processes stopped: {windowsAppProcesses}
            Sign-in broker processes stopped: {signInBrokerProcesses}
            Explorer processes stopped: {explorerProcesses}
            Explorer launched manually: {ExplorerStarted}
            Failures: {failures}
            """;
    }
}
